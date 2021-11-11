using System;
using Actions;
using Actions.Mills;
using RTSender;
using ModbusSender;
using Config;
using Machine;
using CNCState;
using ActionProgram;
using System.Linq;
using Processor;
using Log;
using System.Collections.Generic;
using System.Threading;
using Vector;
using GCodeMachine;

namespace ProgramBuilder
{
    public class ProgramBuilder : ILoggerSource
    {
        private readonly IRTSender rtSender;
        private readonly IModbusSender modbusSender;
        private readonly MachineParameters config;
        private readonly GCodeMachine.GCodeMachine machine;
        private readonly IMillManager toolManager;
        private MoveFeedLimiter moveFeedLimiter;
        private MoveOptimizer optimizer;
        private ExpectedTimeCalculator timeCalculator;

        private Stack<AxisState.Parameters> axisStateStack;
        private readonly IStateSyncManager stateSyncManager;

        private IReadOnlyDictionary<int, IDriver> tool_drivers;
        private List<int> toolsPending;

        public string Name => "program builder";

        public ProgramBuilder(GCodeMachine.GCodeMachine machine,
                              IStateSyncManager stateSyncManager,
                              IRTSender rtSender,
                              IModbusSender modbusSender,
                              IMillManager toolManager,
                              MachineParameters config,
                              IReadOnlyDictionary<int, IDriver> tool_drivers)
        {
            this.stateSyncManager = stateSyncManager;
            this.machine = machine;
            this.rtSender = rtSender;
            this.modbusSender = modbusSender;
            this.toolManager = toolManager;
            this.config = config;

            moveFeedLimiter = new MoveFeedLimiter(this.config);
            optimizer = new MoveOptimizer(this.config);
            timeCalculator = new ExpectedTimeCalculator();
            axisStateStack = new Stack<AxisState.Parameters>();
            toolsPending = new List<int>();

            this.tool_drivers = tool_drivers;
        }

        public void PushState(AxisState axisState)
        {
            axisStateStack.Push(axisState.Params.BuildCopy());
        }

        public void PopState(AxisState axisState)
        {
            axisState.Params = axisStateStack.Pop();
        }

        public CNCState.CNCState ProcessDrillingMove(decimal? X, decimal? Y, decimal? Z, decimal? R, decimal? Q,
                                                      ActionProgram.ActionProgram program,
                                                      CNCState.CNCState state)
        {
            state = state.BuildCopy();

            // TODO Handle G17, 18, 19

            
            Vector3 delta, compensation;

            var coordinateSystem = state.AxisState.Params.CurrentCoordinateSystem;

            if (R != null)
            {
                state.DrillingState.RetractHeightLocal = R.Value;
            }
            if (Z != null)
            {
                state.DrillingState.DrillHeightLocal = Z.Value;
            }
            if (Q != null)
            {
                state.DrillingState.PeckDepth = Math.Abs(Q.Value);
            }

            if (state.DrillingState.Peck && state.DrillingState.PeckDepth == 0)
            {
                throw new InvalidOperationException("Pecking with depth = 0");
            }

            #region Positioning
            if (X == null && Y == null)
            {
                return state;
            }

            Vector3 topPosition;
            (delta, compensation, topPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, X, Y, null);
            state.AxisState.TargetPosition = topPosition;
            state = program.AddFastLineMovement(delta, compensation, state);
            #endregion Positioning

            var absmode = state.AxisState.Absolute;
            state.AxisState.Absolute = true;

            #region R height
            Vector3 rPosition;
            (delta, compensation, rPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, state.DrillingState.RetractHeightLocal);
            state.AxisState.TargetPosition = rPosition;
            state = program.AddFastLineMovement(delta, compensation, state);
            #endregion R height

            // TODO: dwelling, etc

            Vector3 currentDrillPosition = state.AxisState.TargetPosition;

            Vector3 bottomPosition;
            (_, _, bottomPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, state.DrillingState.DrillHeightLocal);

            decimal startHeight = coordinateSystem.ToLocal(rPosition).z;
            decimal preparationHeight = startHeight;
            decimal currentHeight = startHeight;
            decimal finishHeight = coordinateSystem.ToLocal(bottomPosition).z;
            decimal peckMax;

            if (state.DrillingState.Peck)
            {
                peckMax = state.DrillingState.PeckDepth;
            }
            else
            {
                peckMax = decimal.MaxValue;
            }

            while (currentHeight != finishHeight)
            {
                decimal targetHeight;
                decimal deltaHeight = finishHeight - currentHeight;

                if (deltaHeight > peckMax)
                    deltaHeight = peckMax;
                if (deltaHeight < -peckMax)
                    deltaHeight = -peckMax;
                targetHeight = currentHeight + deltaHeight;

                #region preparation
                if (preparationHeight != startHeight)
                {
                    (delta, compensation, currentDrillPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, preparationHeight);
                    state.AxisState.TargetPosition = currentDrillPosition;
                    state = program.AddFastLineMovement(delta, compensation, state);
                }
                #endregion

                #region drilling
                (delta, compensation, currentDrillPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, targetHeight);
                state.AxisState.TargetPosition = currentDrillPosition;
                state = program.AddLineMovement(delta, compensation, state.AxisState.Feed, state);
                #endregion

                #region retracting
                (delta, compensation, currentDrillPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, startHeight);
                state.AxisState.TargetPosition = currentDrillPosition;
                state = program.AddFastLineMovement(delta, compensation, state);
                #endregion

                preparationHeight = currentHeight;
                currentHeight = targetHeight;
            }

            #region Retract
            switch (state.DrillingState.RetractDepth)
            {
                case DrillingState.RetractDepthType.InitialHeight:
                    delta = topPosition - state.AxisState.TargetPosition;
					compensation = state.AxisState.TargetPosition - state.AxisState.Position;
					state.AxisState.TargetPosition = topPosition;
                    state = program.AddFastLineMovement(delta, compensation, state);
                    break;
                case DrillingState.RetractDepthType.RHeight:
                    break;
                default:
                    throw new InvalidOperationException("Unknown retract depth state");
            }

            #endregion

            // Restore mode
            state.AxisState.Absolute = absmode;

            return state;
        }

        private Vector3 MakeMove(CNCState.CNCState state, Vector3 pos, decimal? X, decimal? Y, decimal? Z)
        {
            pos = new Vector3(pos.x, pos.y, pos.z);
            if (state.AxisState.Absolute)
            {
                if (X != null)
                {
                    pos.x = ConvertSizes(X.Value, state);
                }
                if (Y != null)
                {
                    pos.y = ConvertSizes(Y.Value, state);
                }
                if (Z != null)
                {
                    pos.z = ConvertSizes(Z.Value, state);
                }
            }
            else
            {
                if (X != null)
                {
                    pos.x += ConvertSizes(X.Value, state);
                }
                if (Y != null)
                {
                    pos.y += ConvertSizes(Y.Value, state);
                }
                if (Z != null)
                {
                    pos.z += ConvertSizes(Z.Value, state);
                }
            }
            return pos;
        }

		public static decimal ConvertSizes(decimal value, CNCState.CNCState state)
		{
			switch (state.AxisState.Params.SizeUnits)
			{
				case AxisState.Units.Millimeters:
					return value;
				case AxisState.Units.Inches:
					return value * 25.4m;
				default:
					throw new ArgumentException("Invalid units");
			}
		}

		public static Vector3 ConvertSizes(Vector3 value, CNCState.CNCState state)
		{
			return new Vector3(ConvertSizes(value.x, state), ConvertSizes(value.y, state), ConvertSizes(value.z, state));
		}


        private (Vector3, Vector3, Vector3) FindMovement(CNCState.CNCState state, Vector3 currentTargetPosition, Vector3 currentPhysicalPosition, decimal? X, decimal? Y, decimal? Z)
        {
            var coordinateSystem = state.AxisState.Params.CurrentCoordinateSystem;
            Vector3 currentTargetPositionLocal = coordinateSystem.ToLocal(currentTargetPosition);
            Vector3 nextTargetPositionLocal = MakeMove(state, currentTargetPositionLocal, X, Y, Z);
            Vector3 nextTargetPositionGlobal = coordinateSystem.ToGlobal(nextTargetPositionLocal);
            Vector3 delta = nextTargetPositionGlobal - currentTargetPosition;
			Vector3 compensation = currentTargetPosition - currentPhysicalPosition;
            return (delta, compensation, nextTargetPositionGlobal);
        }

        public CNCState.CNCState ProcessLineMove(decimal? X, decimal? Y, decimal? Z,
													bool fast,
                                                    ActionProgram.ActionProgram program,
                                                    CNCState.CNCState state)
        {
            if (X == null && Y == null && Z == null)
                return state;

            state = state.BuildCopy();

            Vector3 delta;
			Vector3 compensation;
            Vector3 targetPosition;
            (delta, compensation, targetPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, X, Y, Z);
            state.AxisState.TargetPosition = targetPosition;

            if (fast)
            {
                return program.AddFastLineMovement(delta, compensation, state);
			}
			else
			{
                return program.AddLineMovement(delta, compensation, state.AxisState.Feed, state);
			}
		}

		public CNCState.CNCState ProcessArcMove(decimal? X, decimal? Y, decimal? Z,
													decimal? I, decimal? J, decimal? K, decimal? R,
													bool ccw,
                                                    ActionProgram.ActionProgram program,
                                                    CNCState.CNCState state)
		{
			if (X == null && Y == null && Z == null)
                return state;

			state = state.BuildCopy();

			Vector3 delta;
			Vector3 compensation;
            Vector3 targetPosition;
            (delta, compensation, targetPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, X, Y, Z);
            state.AxisState.TargetPosition = targetPosition;

            if (R == null && I == null && J == null && K == null)
            {
                decimal r;
                switch (state.AxisState.Axis)
                {
                    case AxisState.Plane.XY:
                        r = (new Vector2(delta.x, delta.y)).Length() / 2;
                        break;
                    case AxisState.Plane.YZ:
                        r = (new Vector2(delta.y, delta.z)).Length() / 2;
                        break;
                    case AxisState.Plane.ZX:
                        r = (new Vector2(delta.z, delta.x)).Length() / 2;
						break;
                    default:
                        throw new InvalidOperationException();
                }
                return program.AddArcMovement(delta, compensation, r, ccw, state.AxisState.Axis, state.AxisState.Feed, state);
            }
            else if (R != null)
            {
                var r = ConvertSizes(R.Value, state);
                return program.AddArcMovement(delta, compensation, r, ccw, state.AxisState.Axis, state.AxisState.Feed, state);
            }
            else
            {
                decimal i = 0;
                decimal j = 0;
                decimal k = 0;
                if (I != null)
                    i = ConvertSizes(I.Value, state);
                if (J != null)
                    j = ConvertSizes(J.Value, state);
                if (K != null)
                    k = ConvertSizes(K.Value, state);
                return program.AddArcMovement(delta, compensation, new Vector3(i, j, k), ccw, state.AxisState.Axis, state.AxisState.Feed, state);
    		}        
        }

		private CNCState.CNCState CommitTool(int tool_id,
											 ActionProgram.ActionProgram program,
                                             CNCState.CNCState state)
		{
			IAction action = null;
			IDriver driver = tool_drivers[tool_id];
            if (driver is N700E_driver n700e)
            {
                SpindleState ss = state.ToolStates[tool_id] as SpindleState;
                action = n700e.CreateAction(ss.RotationState, ss.SpindleSpeed);
            }
			else if (driver is GPIO_driver gpio)
            {
                BinaryState bs = state.ToolStates[tool_id] as BinaryState;
                action = gpio.CreateAction(bs.Enabled);
            }
            else if (driver is RawModbus_driver modbus)
            {
                BinaryState bs = state.ToolStates[tool_id] as BinaryState;
                action = modbus.CreateAction(bs.Enabled);
            }
            else if (driver is Dummy_driver dummy)
            {
                action = dummy.CreateAction();
            }
            else
            {
                throw new InvalidOperationException("invalid driver: " + driver);
            }

			if (action != null)
			{
				program.AddAction(action, state, state);
				return state;
			}
			else
				return state;
		}

        public CNCState.CNCState ProcessToolStart(int tool_id,
												   IReadOnlyDictionary<string, decimal> options,
                                                   ActionProgram.ActionProgram program,
                                                   CNCState.CNCState state)
        {
			state = state.BuildCopy();

            IToolState toolState = state.ToolStates[tool_id];

            if (toolState is SpindleState ss)
            {
                ss.SpindleSpeed = options["speed"];
				if (options["ccw"] != 0)
				{
                	ss.RotationState = SpindleState.SpindleRotationState.CounterClockwise;
				}
				else
				{
					ss.RotationState = SpindleState.SpindleRotationState.Clockwise;
				}
            }
            else if (toolState is BinaryState bs)
			{
				bs.Enabled = true;
			}
			
			return CommitTool(tool_id, program, state);
		}

		public CNCState.CNCState ProcessToolStop(int tool_id,
                                                   ActionProgram.ActionProgram program,
                                                   CNCState.CNCState state)
        {
			state = state.BuildCopy();

            IToolState toolState = state.ToolStates[tool_id];

            if (toolState is SpindleState ss)
            {
                ss.RotationState = SpindleState.SpindleRotationState.Off;
            }
            else if (toolState is BinaryState bs)
			{
				bs.Enabled = false;
			}
			
			return CommitTool(tool_id, program, state);
		}

        public CNCState.CNCState ProcessSyncToolStart(int tool_id,
                                                       ActionProgram.ActionProgram program,
                                                       CNCState.CNCState state)
        {
            var newstate = state.BuildCopy();
            newstate.SyncToolState.Tool = tool_id;
            newstate.SyncToolState.Enabled = true;
            program.EnableRTTool(tool_id, state, newstate);
            return newstate;
        }

		public CNCState.CNCState ProcessSyncToolStop(int tool_id,
                                                      ActionProgram.ActionProgram program,
                                                      CNCState.CNCState state)
        {
            var newstate = state.BuildCopy();
            newstate.SyncToolState.Tool = tool_id;
            newstate.SyncToolState.Enabled = false;
            program.EnableRTTool(tool_id, state, newstate);
            return newstate;
        }

        public CNCState.CNCState CoordinatesSet(decimal? X, decimal? Y, decimal? Z,
                                                 ActionProgram.ActionProgram program,
                                                 CNCState.CNCState state)
        {
            state = state.BuildCopy();
            if (X != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.x =
                    state.AxisState.Position.x - ConvertSizes(X.Value, state);
            }
            if (Y != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.y =
                    state.AxisState.Position.y - ConvertSizes(Y.Value, state);
            }
            if (Z != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.z =
                    state.AxisState.Position.z - ConvertSizes(Z.Value, state);
            }
            state.AxisState.TargetPosition = state.AxisState.Position;
            return state;
        }

        public CNCState.CNCState CoordinatesSystemSet(int cs,
                                                 			  ActionProgram.ActionProgram program,
                                                 			  CNCState.CNCState state)
        {
            state = state.BuildCopy();
            state.AxisState.Params.CurrentCoordinateSystemIndex = cs;
            return state;
        }

		public CNCState.CNCState AddPause(decimal dt, ActionProgram.ActionProgram program, CNCState.CNCState state)
		{
			program.AddDelay((int)dt, state);
			return state;
		}

		public CNCState.CNCState AddHoming(ActionProgram.ActionProgram program, CNCState.CNCState state)
		{
			var after = state.BuildCopy();
			after.AxisState.Position.x = after.AxisState.Position.y = after.AxisState.Position.z = 0;
			program.AddHoming(state, after);
			program.AddAction(new SyncCoordinates(stateSyncManager, state.AxisState.Position), state, null);
			return after;
		}

		public CNCState.CNCState AddZProbe(ActionProgram.ActionProgram program, CNCState.CNCState state)
		{
			var after = state.BuildCopy();
			after.AxisState.Position.z = 0;
			program.AddZProbe(state, after);
			program.AddAction(new SyncCoordinates(stateSyncManager, state.AxisState.Position), state, null);
			return state;
		}

        public CNCState.CNCState BeginProgram(ActionProgram.ActionProgram program, CNCState.CNCState state)
        {
            state = state.BuildCopy();

            var currentPos = state.AxisState.Params.CurrentCoordinateSystem.ToLocal(state.AxisState.TargetPosition);
            state.VarsState.Vars["x"] = currentPos.x;
            state.VarsState.Vars["y"] = currentPos.y;
            state.VarsState.Vars["z"] = currentPos.z;

            Logger.Instance.Debug(this, "build", "start build program");

            program.AddConfiguration(state);
            program.AddRTUnlock(state);

			return state;
		}

		public CNCState.CNCState FinishProgram(ActionProgram.ActionProgram program, CNCState.CNCState state)
		{
            program.AddPlaceholder(state);
            Logger.Instance.Debug(this, "build", "finish build program");
            Logger.Instance.Debug(this, "build", "move feed limit");
            moveFeedLimiter.ProcessProgram(program);
            Logger.Instance.Debug(this, "build", "optimize program");
            optimizer.ProcessProgram(program);
            Logger.Instance.Debug(this, "build", "calculate time of execution");
            timeCalculator.ProcessProgram(program);
            Logger.Instance.Debug(this, "build", string.Format("program ready. Time = {0}", timeCalculator.ExecutionTime));
            return state;
        }
    }
}
