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

namespace GCodeMachine
{
    public class ProgramSource
    {
        public IReadOnlyDictionary<int, Sequence> Procedures;
        public int MainProcedureId;

        public ProgramSource(IReadOnlyDictionary<int, Sequence> procedures, int mainProcedureId)
        {
            Procedures = procedures;
            MainProcedureId = mainProcedureId;
        }
    }

    public class ProgramBuildingState
    {
        public ProgramSource Source;
        public Stack<(int fromProcedure, int fromLine, int toProcedure, int repeat)> Callstack;
        public int CurrentProcedure;
        public int CurrentLine;
        public bool Completed;

        public ProgramBuildingState(ProgramSource source)
        {
            Completed = true;
            Source = source;
            Callstack = new Stack<(int fromProcedure, int fromLine, int toProcedure, int repeat)>();
            CurrentLine = 0;
            CurrentProcedure = Source.MainProcedureId;
        }

        public void Init(int program, int line)
        {
            CurrentLine = line;
            CurrentProcedure = program;
            Completed = false;
            Callstack.Clear();
        }
    }



    public class ProgramBuilder : ILoggerSource
    {
        private readonly IRTSender rtSender;
        private readonly IModbusSender modbusSender;
        private readonly MachineParameters config;
        private readonly GCodeMachine machine;
        private readonly IMillManager toolManager;
        private MoveFeedLimiter moveFeedLimiter;
        private MoveOptimizer optimizer;
        private ExpectedTimeCalculator timeCalculator;

        private Stack<AxisState.Parameters> axisStateStack;
        private readonly IStateSyncManager stateSyncManager;

        private IReadOnlyDictionary<int, IDriver> tool_drivers;
        private List<int> toolsPending;

        public string Name => "gcode builder";

        private enum ProgramBuilderCommand
        {
            Continue,
            Call,
            Return,
            Pause,
            Finish,
        }

        public ProgramBuilder(GCodeMachine machine,
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

        private void PushState(AxisState axisState)
        {
            axisStateStack.Push(axisState.Params.BuildCopy());
        }

        private void PopState(AxisState axisState)
        {
            axisState.Params = axisStateStack.Pop();
        }

        private CNCState.CNCState ProcessParameters(Arguments block,
                                       ActionProgram.ActionProgram program,
                                       CNCState.CNCState state)
        {
            state = state.BuildCopy();
            if (block.Feed != null)
            {
                state.AxisState.Feed = ConvertSizes(block.Feed.optValue, state) / 60.0m; // convert from min to sec
            }
            return state;
        }

        private CNCState.CNCState ProcessDrillingMove(Arguments block,
                                                      ActionProgram.ActionProgram program,
                                                      CNCState.CNCState state)
        {
            state = state.BuildCopy();

            // TODO Handle G17, 18, 19

            var X = block.X;
            var Y = block.Y;
            var Z = block.Z; // Drill depth
            var R = block.R; // Retract
            var Q = block.Q; // Pecking

            Vector3 delta;

            var coordinateSystem = state.AxisState.Params.CurrentCoordinateSystem;

            if (R != null)
            {
                state.DrillingState.RetractHeightLocal = R.optValue;
            }
            if (Z != null)
            {
                state.DrillingState.DrillHeightLocal = Z.optValue;
            }
            if (Q != null)
            {
                state.DrillingState.PeckDepth = Math.Abs(Q.optValue);
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
            (delta, topPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, X, Y, null);
            state.AxisState.TargetPosition = topPosition;
            state = program.AddFastLineMovement(delta, state);
            #endregion Positioning

            var absmode = state.AxisState.Absolute;
            state.AxisState.Absolute = true;

            #region R height
            Vector3 rPosition;
            (delta, rPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, state.DrillingState.RetractHeightLocal);
            state.AxisState.TargetPosition = rPosition;
            state = program.AddFastLineMovement(delta, state);
            #endregion R height

            // TODO: dwelling, etc

            Vector3 currentDrillPosition = state.AxisState.TargetPosition;

            Vector3 bottomPosition;
            (_, bottomPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, state.DrillingState.DrillHeightLocal);

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
                    (delta, currentDrillPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, preparationHeight);
                    state.AxisState.TargetPosition = currentDrillPosition;
                    state = program.AddFastLineMovement(delta, state);
                }
                #endregion

                #region drilling
                (delta, currentDrillPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, targetHeight);
                state.AxisState.TargetPosition = currentDrillPosition;
                state = program.AddLineMovement(delta, state.AxisState.Feed, state);
                #endregion

                #region retracting
                (delta, currentDrillPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, null, null, startHeight);
                state.AxisState.TargetPosition = currentDrillPosition;
                state = program.AddFastLineMovement(delta, state);
                #endregion

                preparationHeight = currentHeight;
                currentHeight = targetHeight;
            }

            #region Retract
            switch (state.DrillingState.RetractDepth)
            {
                case DrillingState.RetractDepthType.InitialHeight:
                    state.AxisState.TargetPosition = topPosition;
                    delta = state.AxisState.TargetPosition - state.AxisState.Position;
                    state = program.AddFastLineMovement(delta, state);
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

        private (Vector3, Vector3) FindMovement(CNCState.CNCState state, Vector3 currentTargetPosition, Vector3 currentPhysicalPosition, decimal? X, decimal? Y, decimal? Z)
        {
            var coordinateSystem = state.AxisState.Params.CurrentCoordinateSystem;
            Vector3 currentTargetPositionLocal = coordinateSystem.ToLocal(currentTargetPosition);
            Vector3 nextTargetPositionLocal = MakeMove(state, currentTargetPositionLocal, X, Y, Z);
            Vector3 nextTargetPositionGlobal = coordinateSystem.ToGlobal(nextTargetPositionLocal);
            Vector3 delta = nextTargetPositionGlobal - currentPhysicalPosition;
            return (delta, nextTargetPositionGlobal);
        }

        private (Vector3, Vector3) FindMovement(CNCState.CNCState state, Vector3 currentTargetPosition, Vector3 currentPhysicalPosition, Arguments.Option X, Arguments.Option Y, Arguments.Option Z)
        {
            decimal? Xv = null, Yv = null, Zv = null;
            if (X != null)
                Xv = X.optValue;
            if (Y != null)
                Yv = Y.optValue;
            if (Z != null)
                Zv = Z.optValue;
            return FindMovement(state, currentTargetPosition, currentPhysicalPosition, Xv, Yv, Zv);
        }

        private CNCState.CNCState ProcessDirectMove(Arguments block,
                                                    ActionProgram.ActionProgram program,
                                                    CNCState.CNCState state)
        {
            var X = block.X;
            var Y = block.Y;
            var Z = block.Z;
            var I = block.I;
            var J = block.J;
            var K = block.K;
            var R = block.R;

            state = state.BuildCopy();

            var cmd = block.Options.FirstOrDefault((arg) => arg.letter == 'G');
            if (cmd != null)
            {
                switch (cmd.ivalue1)
                {
                    case 0:
                        state.AxisState.MoveType = AxisState.MType.FastLine;
                        break;
                    case 1:
                        state.AxisState.MoveType = AxisState.MType.Line;
                        break;
                    case 2:
                        state.AxisState.MoveType = AxisState.MType.ArcCW;
                        break;
                    case 3:
                        state.AxisState.MoveType = AxisState.MType.ArcCCW;
                        break;
                }
            }

            if (X == null && Y == null && Z == null)
                return state;

            Vector3 delta;
            Vector3 targetPosition;
            (delta, targetPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, X, Y, Z);
            state.AxisState.TargetPosition = targetPosition;

            switch (state.AxisState.MoveType)
            {
                case AxisState.MType.FastLine:
                    state = program.AddFastLineMovement(delta, state);
                    break;
                case AxisState.MType.Line:
                    state = program.AddLineMovement(delta, state.AxisState.Feed, state);
                    break;
                case AxisState.MType.ArcCW:
                case AxisState.MType.ArcCCW:
                    {
                        bool ccw = (state.AxisState.MoveType == AxisState.MType.ArcCCW);
                        if (R == null && I == null && J == null && K == null)
                        {
                            decimal r;
                            switch (state.AxisState.Axis)
                            {
                                case AxisState.Plane.XY:
                                    r = (new Vector2(delta.x, delta.y)).Length()/2;
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
                            state = program.AddArcMovement(delta, r, ccw, state.AxisState.Axis, state.AxisState.Feed, state);
                        }
                        else if (R != null)
                        {
                            var r = ConvertSizes(R.optValue, state);
                            state = program.AddArcMovement(delta, r, ccw, state.AxisState.Axis, state.AxisState.Feed, state);
                        }
                        else
                        {
                            decimal i = 0;
                            decimal j = 0;
                            decimal k = 0;
                            if (I != null)
                                i = ConvertSizes(I.optValue, state);
                            if (J != null)
                                j = ConvertSizes(J.optValue, state);
                            if (K != null)
                                k = ConvertSizes(K.optValue, state);
                            state = program.AddArcMovement(delta, new Vector3(i, j, k), ccw, state.AxisState.Axis, state.AxisState.Feed, state);
                        }
                    }
                    break;
                default:
                    break;
            }
            return state;
        }


        private CNCState.CNCState ProcessMove(Arguments block,
                                              ActionProgram.ActionProgram program,
                                              CNCState.CNCState state)
        {
            if (!state.DrillingState.Drilling)
                return ProcessDirectMove(block, program, state);
            else
                return ProcessDrillingMove(block, program, state);
        }

        private CNCState.CNCState ProcessSpindleRunCommand(Arguments block,
                                                           ActionProgram.ActionProgram program,
                                                           CNCState.CNCState state)
        {
            var cmd = block.Options.FirstOrDefault((arg) => (arg.letter == 'M'));
            if (cmd == null)
                return state;
            switch (cmd.ivalue1)
            {
                case 3:
                    {
                        state = state.BuildCopy();
                        int toolid;
                        if (cmd.dot)
                            toolid = cmd.ivalue2;
                        else
                            toolid = config.deftool_id;

                        toolsPending.Add(toolid);
                        IToolState toolState = state.ToolStates[toolid];
                        if (toolState is SpindleState ss)
                        {
                            if (block.Speed != null)
                                ss.SpindleSpeed = block.Speed.optValue;
                            ss.RotationState = SpindleState.SpindleRotationState.Clockwise;
                        }
                        else if (toolState is BinaryState bs)
                        {
                            bs.Enabled = true;
                        }
                        else
                            throw new ArgumentOutOfRangeException("Invalid type of state");
                    }
                    break;
                case 4:
                    {
                        state = state.BuildCopy();
                        int toolid;
                        if (cmd.dot)
                            toolid = cmd.ivalue2;
                        else
                            toolid = config.deftool_id;

                        toolsPending.Add(toolid);

                        state = state.BuildCopy();
                        toolsPending.Add(toolid);
                        IToolState toolState = state.ToolStates[toolid];
                        if (toolState is SpindleState ss)
                        {
                            if (block.Speed != null)
                                ss.SpindleSpeed = block.Speed.optValue;
                            ss.RotationState = SpindleState.SpindleRotationState.CounterClockwise;
                        }
                        else if (toolState is BinaryState bs)
                        {
                            bs.Enabled = true;
                        }
                        else
                            throw new ArgumentOutOfRangeException("Invalid type of state");
                    }
                    break;
                case 5:
                    {
                        state = state.BuildCopy();
                        int toolid;
                        if (cmd.dot)
                            toolid = cmd.ivalue2;
                        else
                            toolid = config.deftool_id;

                        toolsPending.Add(toolid);

                        state = state.BuildCopy();
                        toolsPending.Add(toolid);
                        IToolState toolState = state.ToolStates[toolid];
                        if (toolState is SpindleState ss)
                        {
                            if (block.Speed != null)
                                ss.SpindleSpeed = block.Speed.optValue;
                            ss.RotationState = SpindleState.SpindleRotationState.Off;
                        }
                        else if (toolState is BinaryState bs)
                        {
                            bs.Enabled = false;
                        }
                        else
                            throw new ArgumentOutOfRangeException("Invalid type of state");
                    }
                    break;
            }
            return state;
        }

        private CNCState.CNCState ProcessSyncToolCommand(Arguments block,
                                                         ActionProgram.ActionProgram program,
                                                         CNCState.CNCState state)
        {
            var cmd = block.Options.FirstOrDefault((arg) => (arg.letter == 'M'));
            if (cmd == null)
                return state;
            var newstate = state.BuildCopy();
            switch (cmd.ivalue1)
            {
                case 703:
                case 705:
                    {
                        int tool;
                        if (block.SingleOptions.ContainsKey('T'))
                        {
                            tool = block.SingleOptions['T'].ivalue1;
                            newstate.SyncToolState.Tool = tool;
                        }
                        else
                        {
                            tool = state.SyncToolState.Tool;
                        }
                        if (cmd.ivalue1 == 703)
                        {
                            newstate.SyncToolState.Enabled = true;
                            program.EnableRTTool(tool, state, newstate);
                        }
                        else
                        {
                            newstate.SyncToolState.Enabled = false;
                            program.DisableRTTool(tool, state, newstate);
                        }
                    }

                    break;
            }
            return newstate;
        }

        private CNCState.CNCState ProcessCoordinatesSet(Arguments args,
                                                        ActionProgram.ActionProgram program,
                                                        CNCState.CNCState state)
        {
            var X = args.X;
            var Y = args.Y;
            var Z = args.Z;
            state = state.BuildCopy();
            if (X != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.x =
                    state.AxisState.Position.x - ConvertSizes(X.optValue, state);
            }
            if (Y != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.y =
                    state.AxisState.Position.y - ConvertSizes(Y.optValue, state);
            }
            if (Z != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.z =
                    state.AxisState.Position.z - ConvertSizes(Z.optValue, state);
            }
            state.AxisState.TargetPosition = state.AxisState.Position;
            return state;
        }

        private CNCState.CNCState ProcessCoordinatesSystemSet(Arguments block,
                                                 ActionProgram.ActionProgram program,
                                                 CNCState.CNCState state)
        {
            var cmd = block.Options.FirstOrDefault((arg) => (arg.letter == 'G'));
            if (cmd == null)
                return state;
            if (cmd.ivalue1 < 53 || cmd.ivalue1 > 59)
                return state;
            state = state.BuildCopy();
            int crdsid = cmd.ivalue1 - 53;
            state.AxisState.Params.CurrentCoordinateSystemIndex = crdsid;
            return state;
        }

        private IReadOnlyList<Arguments> SplitFrame(Arguments args)
        {
            List<Arguments> arguments = new List<Arguments>();

            Arguments cur = new Arguments();

            foreach (var arg in args.Options)
            {
                if (arg.letter == 'M' || arg.letter == 'G')
                {
                    if (cur.Options.Count > 0)
                    {
                        arguments.Add(cur);
                        cur = new Arguments();
                    }
                }
                cur.AddOption(arg);
            }

            if (cur.Options.Count > 0)
            {
                arguments.Add(cur);
                cur = new Arguments();
            }

            return arguments;
        }

        private decimal ConvertSizes(decimal value, CNCState.CNCState state)
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

        private Vector3 ConvertSizes(Vector3 value, CNCState.CNCState state)
        {
            return new Vector3(ConvertSizes(value.x, state), ConvertSizes(value.y, state), ConvertSizes(value.z, state));
        }

        private void CommitPendingCommands(ActionProgram.ActionProgram program, CNCState.CNCState state)
        {
            foreach (int toolPending in toolsPending)
            {
                IAction action = null;
                try
                {
                    IDriver driver = tool_drivers[toolPending];
                    if (driver is N700E_driver n700e)
                    {
                        SpindleState ss = state.ToolStates[toolPending] as SpindleState;
                        action = n700e.CreateAction(ss.RotationState, ss.SpindleSpeed);
                    }
                    else if (driver is GPIO_driver gpio)
                    {
                        BinaryState bs = state.ToolStates[toolPending] as BinaryState;
                        action = gpio.CreateAction(bs.Enabled);
                    }
                    else if (driver is RawModbus_driver modbus)
                    {
                        BinaryState bs = state.ToolStates[toolPending] as BinaryState;
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
                        program.AddAction(action, state, state);
                }
                catch (Exception e)
                {
                    toolsPending.Clear();
                    throw e;
                }
            }
            toolsPending.Clear();
        }

        private Stack<int> PushPosition(int index, Stack<int> callStack)
        {
            callStack.Push(index);
            return callStack;
        }

        private (int, Stack<int>) PopPosition(Stack<int> callStack)
        {
            if (callStack.Count == 0)
                return (-1, callStack);
            int index = callStack.Pop();
            return (index, callStack);
        }

        private (int, int) CallSubprogram(Arguments args,
                                          CNCState.CNCState state)
        {
            if (!args.SingleOptions.ContainsKey('P'))
                throw new InvalidOperationException("Subprogram id not specified");

            int prgid = args.SingleOptions['P'].ivalue1;
            int amount = 1;
            if (args.SingleOptions.ContainsKey('L'))
                amount = args.SingleOptions['L'].ivalue1;

            return (prgid, amount);
        }

        private (CNCState.CNCState state,
                 ProgramBuilderCommand command,
                 int pid,
                 int amount)
        
            ProcessBlock(Arguments block,
                         ActionProgram.ActionProgram program,
                         CNCState.CNCState state)
        {
            var cmd = block.Options.FirstOrDefault((arg) => (arg.letter == 'G' || arg.letter == 'M'));
            ProgramBuilderCommand command = ProgramBuilderCommand.Continue;
            int pid = -1, amount = -1;
            state = state.BuildCopy();
            state = ProcessParameters(block, program, state);

            if (cmd == null)
            {
                state = ProcessMove(block, program, state);
            }
            else if (cmd.letter == 'G')
            {
                switch (cmd.ivalue1)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                        state = ProcessMove(block, program, state);
                        break;
                    case 4:
                        {
                            decimal dt = 0;
                            try
                            {
                                var P = block.SingleOptions['P'];
                                dt = P.optValue;
                                if (P.dot)
                                    dt *= 1000;
                            }
                            catch
                            {
                                ;
                            }
                            program.AddDelay((int)dt, state);
                        }
                        break;
                    case 17:
                        state.AxisState.Params.CurrentPlane = AxisState.Plane.XY;
                        break;
                    case 18:
                        state.AxisState.Params.CurrentPlane = AxisState.Plane.YZ;
                        break;
                    case 19:
                        state.AxisState.Params.CurrentPlane = AxisState.Plane.ZX;
                        break;
                    case 20:
                        state.AxisState.Params.SizeUnits = AxisState.Units.Inches;
                        break;
                    case 21:
                        state.AxisState.Params.SizeUnits = AxisState.Units.Millimeters;
                        break;
                    case 28:
                        {
                            var after = state.BuildCopy();
                            after.AxisState.Position.x = after.AxisState.Position.y = after.AxisState.Position.z = 0;
                            program.AddHoming(state, after);
                            state = after;
                            program.AddAction(new SyncCoordinates(stateSyncManager, state.AxisState.Position), state, null);
                            break;
                        }
                    case 30:
                        {
                            var after = state.BuildCopy();
                            after.AxisState.Position.z = 0;
                            program.AddZProbe(state, after);
                            state = after;
                            program.AddAction(new SyncCoordinates(stateSyncManager, state.AxisState.Position), state, null);
                            break;
                        }
                    case 53:
                    case 54:
                    case 55:
                    case 56:
                    case 57:
                    case 58:
                    case 59:
                        state = ProcessCoordinatesSystemSet(block, program, state);
                        break;
                    case 80:
                        state.DrillingState.Drilling = false;
                        state = ProcessMove(block, program, state);
                        break;
                    case 81:
                        state.DrillingState.Drilling = true;
                        state.DrillingState.Peck = false;
                        state.DrillingState.Retract = DrillingState.RetractType.Rapid;
                        state.DrillingState.RetractReverse = false;
                        state.DrillingState.Dwell = false;
                        state.DrillingState.LeftHand = false;
                        state.DrillingState.StopSpindle = false;
                        state.DrillingState.Tapping = false;
                        state = ProcessMove(block, program, state);
                        break;
                    case 82:
                        state.DrillingState.Drilling = true;
                        state.DrillingState.Peck = false;
                        state.DrillingState.Retract = DrillingState.RetractType.Rapid;
                        state.DrillingState.RetractReverse = false;
                        state.DrillingState.Dwell = true;
                        state.DrillingState.LeftHand = false;
                        state.DrillingState.StopSpindle = false;
                        state.DrillingState.Tapping = false;
                        state = ProcessMove(block, program, state);
                        break;
                    case 83:
                        state.DrillingState.Drilling = true;
                        state.DrillingState.Peck = true;
                        state.DrillingState.Retract = DrillingState.RetractType.Rapid;
                        state.DrillingState.RetractReverse = false;
                        state.DrillingState.Dwell = false;
                        state.DrillingState.LeftHand = false;
                        state.DrillingState.StopSpindle = false;
                        state.DrillingState.Tapping = false;
                        state = ProcessMove(block, program, state);
                        break;
                    case 84:
                        state.DrillingState.Drilling = true;
                        state.DrillingState.Peck = false;
                        state.DrillingState.Retract = DrillingState.RetractType.Feed;
                        state.DrillingState.RetractReverse = true;
                        state.DrillingState.Dwell = false;
                        state.DrillingState.LeftHand = false;
                        state.DrillingState.StopSpindle = false;
                        state.DrillingState.Tapping = true;
                        state = ProcessMove(block, program, state);
                        break;
                    case 85:
                        state.DrillingState.Drilling = true;
                        state.DrillingState.Peck = false;
                        state.DrillingState.Retract = DrillingState.RetractType.Feed;
                        state.DrillingState.RetractReverse = false;
                        state.DrillingState.Dwell = false;
                        state.DrillingState.LeftHand = false;
                        state.DrillingState.StopSpindle = false;
                        state.DrillingState.Tapping = false;
                        state = ProcessMove(block, program, state);
                        break;
                    case 86:
                        state.DrillingState.Drilling = true;
                        state.DrillingState.Peck = false;
                        state.DrillingState.Retract = DrillingState.RetractType.Feed;
                        state.DrillingState.RetractReverse = false;
                        state.DrillingState.Dwell = false;
                        state.DrillingState.LeftHand = false;
                        state.DrillingState.StopSpindle = true;
                        state.DrillingState.Tapping = false;
                        state = ProcessMove(block, program, state);
                        break;
                    case 87:
                        state.DrillingState.Drilling = true;
                        state.DrillingState.Peck = false;
                        state.DrillingState.Retract = DrillingState.RetractType.Manual;
                        state.DrillingState.RetractReverse = false;
                        state.DrillingState.Dwell = false;
                        state.DrillingState.LeftHand = false;
                        state.DrillingState.StopSpindle = true;
                        state.DrillingState.Tapping = false;
                        state = ProcessMove(block, program, state);
                        break;
                    case 88:
                        state.DrillingState.Drilling = true;
                        state.DrillingState.Peck = false;
                        state.DrillingState.Retract = DrillingState.RetractType.Manual;
                        state.DrillingState.RetractReverse = false;
                        state.DrillingState.Dwell = true;
                        state.DrillingState.LeftHand = false;
                        state.DrillingState.StopSpindle = true;
                        state.DrillingState.Tapping = false;
                        state = ProcessMove(block, program, state);
                        break;
                    case 89:
                        state.DrillingState.Drilling = true;
                        state.DrillingState.Peck = false;
                        state.DrillingState.Retract = DrillingState.RetractType.Feed;
                        state.DrillingState.RetractReverse = false;
                        state.DrillingState.Dwell = true;
                        state.DrillingState.LeftHand = false;
                        state.DrillingState.StopSpindle = false;
                        state.DrillingState.Tapping = false;
                        state = ProcessMove(block, program, state);
                        break;

                    case 90:
                        state.AxisState.Absolute = true;
                        break;
                    case 91:
                        state.AxisState.Absolute = false;
                        break;
                    case 92:
                        state = ProcessCoordinatesSet(block, program, state);
                        break;

                    case 98:
                        state.DrillingState.RetractDepth = DrillingState.RetractDepthType.InitialHeight;
                        break;
                    case 99:
                        state.DrillingState.RetractDepth = DrillingState.RetractDepthType.RHeight;
                        break;
                }
            }
            else if (cmd.letter == 'M')
            {
                switch (cmd.ivalue1)
                {
                    case 0:
                        program.AddBreak(state);
                        command = ProgramBuilderCommand.Pause;
                        break;
                    case 2:
                        program.AddStop(state);
                        command = ProgramBuilderCommand.Finish;
                        break;
                    case 3:
                    case 4:
                    case 5:
                        state = ProcessSpindleRunCommand(block, program, state);
                        break;
                    case 6:
                        if (block.SingleOptions.ContainsKey('T'))
                        {
                            // TODO: stop spindle
                            program.AddToolChange(block.SingleOptions['T'].ivalue1);    // change tool
                            if (toolManager.ToolChangeInterrupts)
                                command = ProgramBuilderCommand.Pause;
                        }
                        break;
                    case 97:
                        command = ProgramBuilderCommand.Call;
                        (pid, amount) = CallSubprogram(block, state);
                        break;
                    case 99:
                        command = ProgramBuilderCommand.Return;
                        break;
                    case 120:
                        PushState(state.AxisState);
                        break;
                    case 121:
                        PopState(state.AxisState);
                        break;
                    case 703:
                    case 705:
                        state = ProcessSyncToolCommand(block, program, state);
                        break;
                }
            }

            CommitPendingCommands(program, state);
            return (state, command, pid, amount);
        }

        private (CNCState.CNCState,
                 ProgramBuilderCommand command,
                 int programid,
                 int amount)

            Process(Arguments args,
                    ActionProgram.ActionProgram program,
                    CNCState.CNCState state,
                    int curprogram, int curline,
                    Dictionary<IAction, (int,int)> starts)
        {
            ProgramBuilderCommand command = ProgramBuilderCommand.Continue;
            int pid = -1, amount = -1;

            var line_number = args.LineNumber;
            var len0 = program.Actions.Count;
            var sargs = SplitFrame(args);
            foreach (var block in sargs)
            {
                (state, command, pid, amount) = ProcessBlock(block, program, state);
            }

            var len1 = program.Actions.Count;
            if (len1 > len0)
            {
                var (first, _, _) = program.Actions[len0];
                starts[first] = (curprogram, curline);
            }
            return (state, command, pid, amount);
        }

        public ProgramBuildingState InitNewProgram(ProgramSource source)
        {
            var builderState = new ProgramBuildingState(source)
            {
                CurrentProcedure = source.MainProcedureId,
                CurrentLine = 0
            };
            return builderState;
        }

        public (ActionProgram.ActionProgram actionProgram,
                ProgramBuildingState finalState,
                IReadOnlyDictionary<IAction, (int procedure, int line)> actionLines,
                string errorMessage)
                
            BuildProgram(CNCState.CNCState initialMachineState,
                         ProgramBuildingState builderState)
        {
            var program = new ActionProgram.ActionProgram(rtSender, modbusSender, config, machine, toolManager);
            var actionLines = new Dictionary<IAction, (int procedure, int line)>();
            Sequence sequence = builderState.Source.Procedures[builderState.CurrentProcedure];
            var state = initialMachineState.BuildCopy();

            program.AddRTUnlock(state);
            actionLines[program.Actions[0].action] = (-1, -1);
            bool finish = false;

            while (!finish)
            {
                if (builderState.CurrentLine >= sequence.Lines.Count)
                {
                    builderState.Completed = true;
                    break;
                }
                Arguments frame = sequence.Lines[builderState.CurrentLine];
                try
                {
                    int newpid, amount;
                    ProgramBuilderCommand command;
                    (state, command, newpid, amount) = Process(frame,
                                                               program,
                                                               state,
                                                               builderState.CurrentProcedure,
                                                               builderState.CurrentLine,
                                                               actionLines);
                    switch (command)
                    {
                        case ProgramBuilderCommand.Call:
                            {
                                if (amount > 0)
                                {
                                    builderState.Callstack.Push((builderState.CurrentProcedure, builderState.CurrentLine, newpid, amount));
                                    builderState.CurrentProcedure = newpid;
                                    builderState.CurrentLine = 0;
                                    sequence = builderState.Source.Procedures[builderState.CurrentProcedure];
                                }
                                else
                                {
                                    builderState.CurrentLine += 1;
                                }
                                break;
                            }
                        case ProgramBuilderCommand.Continue:
                            {
                                builderState.CurrentLine += 1;
                                break;
                            }
                        case ProgramBuilderCommand.Return:
                            {
                                if (builderState.Callstack.Count == 0)
                                {
                                    builderState.Completed = true;
                                    finish = true;
                                    break;
                                }
                                var top = builderState.Callstack.Pop();
                                if (top.repeat == 1)
                                {
                                    builderState.CurrentProcedure = top.fromProcedure;
                                    builderState.CurrentLine = top.fromLine + 1;
                                    sequence = builderState.Source.Procedures[builderState.CurrentProcedure];
                                }
                                else if (top.repeat > 1)
                                {
                                    builderState.CurrentLine = 0;
                                    builderState.Callstack.Push((top.fromProcedure, top.fromLine, top.toProcedure, top.repeat - 1));
                                }
                                break;
                            }
                        case ProgramBuilderCommand.Finish:
                            {
                                builderState.Completed = true;
                                finish = true;
                                break; // END
                            }
                        case ProgramBuilderCommand.Pause:
                            {
                                builderState.CurrentLine += 1;
                                finish = true;
                                break;
                            }
                    }
                }
                catch (Exception e)
                {
                    var msg = String.Format("{0} : {1}", frame, e.ToString());
                    Logger.Instance.Error(this, "compile error", msg);
                    return (null, null, new Dictionary<IAction, (int, int)>(), e.Message);
                }
            }

            program.AddPlaceholder(state);
            moveFeedLimiter.ProcessProgram(program);
            optimizer.ProcessProgram(program);
            timeCalculator.ProcessProgram(program);
            return (program, builderState, actionLines, "");
        }
    }
}
