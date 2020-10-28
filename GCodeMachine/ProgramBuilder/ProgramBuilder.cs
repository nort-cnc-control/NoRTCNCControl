using System;
using Actions;
using Actions.Tools;
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
    public class ProgramBuilder : ILoggerSource
    {
        private readonly IRTSender rtSender;
        private readonly IModbusSender modbusSender;
        private readonly MachineParameters config;
        private readonly GCodeMachine machine;
        private readonly IToolManager toolManager;
        private MoveFeedLimiter moveFeedLimiter;
        private MoveOptimizer optimizer;
        private ExpectedTimeCalculator timeCalculator;

        private Stack<AxisState.Parameters> axisStateStack;
        private readonly IStateSyncManager stateSyncManager;

        private Dictionary<int, object> tool_drivers;
        private List<int> toolsPending;

        public string Name => "gcode builder";

        public ProgramBuilder(GCodeMachine machine,
                              IStateSyncManager stateSyncManager,
                              IRTSender rtSender,
                              IModbusSender modbusSender,
                              IToolManager toolManager,
                              MachineParameters config)
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

            tool_drivers = new Dictionary<int, object>();

            foreach (KeyValuePair<int, IToolDriver> item in config.tools)
            {
                int id = item.Key;
                IToolDriver driverDesc = item.Value;
                object driver;
                Logger.Instance.Debug(this, "create tool", "Create tool " + driverDesc.name + " with driver " + driverDesc.driver);
                switch (driverDesc.driver)
                {
                    case "n700e":
                        {
                            driver = new N700E_driver(modbusSender, (driverDesc as N700E_Tool).address);
                            break;
                        }
                    case "dummy":
                        {
                            driver = new Dummy_driver();
                            break;
                        }
                    case "gpio":
                        {
                            driver = new GPIO_driver(rtSender, (driverDesc as GPIO_Tool).gpio);
                            break;
                        }
                    case "modbus":
                        {
                            int addr = (driverDesc as RawModbus_Tool).address;
                            UInt16 reg = (driverDesc as RawModbus_Tool).register;
                            driver = new RawModbus_driver(modbusSender, addr, reg);
                            break;
                        }
                    default:
                        {
                            throw new NotSupportedException("Unsupported driver: " + driverDesc.driver + " : " + driverDesc.name);
                        }
                }
                tool_drivers.Add(id, driver);
            }
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
                state.AxisState.Feed = ConvertSizes(block.Feed.value, state) / 60.0m; // convert from min to sec
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
            var Z = block.Z;
            var R = block.R;

            Vector3 delta;
            Vector3 topPosition;
            var coordinateSystem = state.AxisState.Params.CurrentCoordinateSystem;

            #region Positioning
            (delta, topPosition) = FindMovement(state, state.AxisState.TargetPosition, state.AxisState.Position, X, Y, null);
            state.AxisState.TargetPosition = topPosition;
            state = program.AddFastLineMovement(delta, state);
            #endregion

            #region R height
            Vector3 rPosition = new Vector3(topPosition);
            if (R != null)
            {
                Vector3 targetPositionLocal = coordinateSystem.ToLocal(topPosition);
                (delta, rPosition) = FindMovement(state, topPosition, state.AxisState.Position, null, null, R);
                state.DrillingState.RetractHeightGlobal = rPosition.z;
            }
            else
            {
                rPosition.z = state.DrillingState.RetractHeightGlobal;
                delta = rPosition - state.AxisState.Position;
            }
            state.AxisState.TargetPosition = rPosition;
            state = program.AddFastLineMovement(delta, state);
            #endregion

            // TODO: add pecking, dwelling, etc

            #region drilling
            Vector3 bottomPosition = new Vector3(rPosition);
            if (Z != null)
            {
                Vector3 targetPositionLocal = coordinateSystem.ToLocal(topPosition);
                (delta, bottomPosition) = FindMovement(state, rPosition, state.AxisState.Position, null, null, Z);
                state.DrillingState.DrillHeightGlobal = bottomPosition.z;
            }
            else
            {
                bottomPosition.z = state.DrillingState.DrillHeightGlobal;
                delta = bottomPosition - state.AxisState.Position;
            }
            state.AxisState.TargetPosition = bottomPosition;
            state = program.AddLineMovement(delta, state.AxisState.Feed, state);
            #endregion

            #region Retract
            Vector3 retractPosition;
            switch (state.DrillingState.RetractDepth)
            {
                case DrillingState.RetractDepthType.InitialHeight:
                    retractPosition = topPosition;
                    break;
                case DrillingState.RetractDepthType.RHeight:
                    retractPosition = rPosition;
                    break;
                default:
                    throw new InvalidOperationException("Unknown retract depth state");
            }
            state.AxisState.TargetPosition = retractPosition;
            delta = retractPosition - bottomPosition;
            state = program.AddFastLineMovement(delta, state);
            #endregion

            return state;
        }

        private Vector3 MakeMove(CNCState.CNCState state, Vector3 pos, Arguments.Option X, Arguments.Option Y, Arguments.Option Z)
        {
            pos = new Vector3(pos.x, pos.y, pos.z);
            if (state.AxisState.Absolute)
            {
                if (X != null)
                {
                    pos.x = ConvertSizes(X.value, state);
                }
                if (Y != null)
                {
                    pos.y = ConvertSizes(Y.value, state);
                }
                if (Z != null)
                {
                    pos.z = ConvertSizes(Z.value, state);
                }
            }
            else
            {
                if (X != null)
                {
                    pos.x += ConvertSizes(X.value, state);
                }
                if (Y != null)
                {
                    pos.y += ConvertSizes(Y.value, state);
                }
                if (Z != null)
                {
                    pos.z += ConvertSizes(Z.value, state);
                }
            }
            return pos;
        }

        private (Vector3, Vector3) FindMovement(CNCState.CNCState state, Vector3 currentTargetPosition, Vector3 currentPhysicalPosition, Arguments.Option X, Arguments.Option Y, Arguments.Option Z)
        {
            var coordinateSystem = state.AxisState.Params.CurrentCoordinateSystem;
            Vector3 currentTargetPositionLocal = coordinateSystem.ToLocal(currentTargetPosition);
            Vector3 nextTargetPositionLocal = MakeMove(state, currentTargetPositionLocal, X, Y, Z);
            Vector3 nextTargetPositionGlobal = coordinateSystem.ToGlobal(nextTargetPositionLocal);
            Vector3 delta = nextTargetPositionGlobal - currentPhysicalPosition;
            return (delta, nextTargetPositionGlobal);
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
                            var r = delta.Length() / 2;
                            state = program.AddArcMovement(delta, r, ccw, state.AxisState.Axis, state.AxisState.Feed, state);
                        }
                        else if (R != null)
                        {
                            var r = ConvertSizes(R.value, state);
                            state = program.AddArcMovement(delta, r, ccw, state.AxisState.Axis, state.AxisState.Feed, state);
                        }
                        else
                        {
                            decimal i = 0;
                            decimal j = 0;
                            decimal k = 0;
                            if (I != null)
                                i = ConvertSizes(I.value, state);
                            if (J != null)
                                j = ConvertSizes(J.value, state);
                            if (K != null)
                                k = ConvertSizes(K.value, state);
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
                                ss.SpindleSpeed = block.Speed.value;
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
                                ss.SpindleSpeed = block.Speed.value;
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
                                ss.SpindleSpeed = block.Speed.value;
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
                    state.AxisState.Position.x - ConvertSizes(X.value, state);
            }
            if (Y != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.y =
                    state.AxisState.Position.y - ConvertSizes(Y.value, state);
            }
            if (Z != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.z =
                    state.AxisState.Position.z - ConvertSizes(Z.value, state);
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
                    object driver = tool_drivers[toolPending];
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

        private (CNCState.CNCState state, int pid, int amount, bool end) ProcessBlock(Arguments block,
                                                                          ActionProgram.ActionProgram program,
                                                                          CNCState.CNCState state)
        {
            var cmd = block.Options.FirstOrDefault((arg) => (arg.letter == 'G' || arg.letter == 'M'));
            bool finish = false;
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
                                dt = P.value;
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
                        program.AddBreak();
                        program.AddPlaceholder(state);
                        break;
                    case 2:
                        program.AddStop();
                        program.AddPlaceholder(state);
                        finish = true;
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
                        }
                        break;
                    case 97:
                        (pid, amount) = CallSubprogram(block, state);
                        break;
                    case 99:
                        pid = -2;
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
            return (state, pid, amount, finish);
        }

        private (CNCState.CNCState, int programid, int amount) Process( Arguments args,
                                                                        ActionProgram.ActionProgram program,
                                                                        CNCState.CNCState state,
                                                                        int curprogram, int curline,
                                                                        Dictionary<IAction, (int,int)> starts)
        {
            bool finish = false;
            int pid = -1, amount = -1;

            var line_number = args.LineNumber;
            var len0 = program.Actions.Count;
            var sargs = SplitFrame(args);
            foreach (var block in sargs)
            {
                (state, pid, amount, finish) = ProcessBlock(block, program, state);
            }

            if (finish)
            {
                pid = -3;
            }

            var len1 = program.Actions.Count;
            if (len1 > len0)
            {
                var (first, _, _) = program.Actions[len0];
                starts[first] = (curprogram, curline);
            }
            return (state, pid, amount);
        }

        public (ActionProgram.ActionProgram program, CNCState.CNCState finalState, IReadOnlyDictionary<IAction, (int, int)> actionLines, string errorMessage)
                
            BuildProgram(Sequence mainSequence, ProgramSequencer programs, CNCState.CNCState initialState)
        {
            var program = new ActionProgram.ActionProgram(rtSender, modbusSender, config, machine, toolManager);
            var actionLines = new Dictionary<IAction, (int, int)>();
            int programid = 0, lineid = 0;
            Sequence sequence = mainSequence;
            var state = initialState.BuildCopy();
            Stack<(int, int)> callStack = new Stack<(int, int)>();

            program.AddRTUnlock(state);
            actionLines[program.Actions[0].action] = (-1, -1);
            while (true)
            {
                if (lineid >= sequence.Lines.Count)
                    break;

                Arguments frame = sequence.Lines[lineid];

                try
                {
                    int newpid, amount;
                    (state, newpid, amount) = Process(frame, program, state, programid, lineid, actionLines);
                    if (newpid >= 0)
                    {
                        callStack.Push((programid, lineid + 1));
                        while (amount > 1)
                        {
                            callStack.Push((newpid, 0));
                            amount--;
                        }
                        programid = newpid;
                        lineid = 0;
                    }
                    else if (newpid == -1)
                    {
                        lineid += 1;
                    }
                    else if (newpid == -2)
                    {
                        if (callStack.Count == 0)
                        {
                            break;
                        }
                        (programid, lineid) = callStack.Pop();
                    }
                    else if (newpid == -3)
                    {
                        break; // END
                    }

                    if (programid == 0)
                        sequence = mainSequence;
                    else
                        sequence = programs.Subprograms[programid];
                }
                catch (Exception e)
                {
                    var msg = String.Format("{0} : {1}", frame, e.ToString());
                    Logger.Instance.Error(this, "compile error", msg);
                    return (null, initialState, new Dictionary<IAction, (int, int)>(), e.Message);
                }
            }

            program.AddPlaceholder(state);
            moveFeedLimiter.ProcessProgram(program);
            optimizer.ProcessProgram(program);
            timeCalculator.ProcessProgram(program);
            return (program, state, actionLines, "");
        }

        public (ActionProgram.ActionProgram program, CNCState.CNCState finalState, IReadOnlyDictionary<IAction, (int, int)> actionLines, string errorMessage)

            BuildProgram(String code, CNCState.CNCState initialState)
        {
            var seq = new ProgramSequencer();
            seq.SequenceProgram(code.Split('\n'));
            return BuildProgram(seq.MainProgram, seq, initialState);
        }
    }
}
