using System;
using Actions;
using Actions.ModbusTool;
using Actions.Tools.SpindleTool;
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
using Actions.Tools;
using Vector;

namespace GCodeMachine
{
    public class ProgramBuilder : ILoggerSource
    {
        private readonly IRTSender rtSender;
        private readonly IModbusSender modbusSender;
        private readonly ISpindleToolFactory spindleToolFactory;
        private readonly MachineParameters config;
        private readonly GCodeMachine machine;
        private readonly IToolManager toolManager;
        private ArcMoveFeedLimiter arcMoveFeedLimiter;
        private MoveOptimizer optimizer;
        private ExpectedTimeCalculator timeCalculator;
        private Stack<AxisState.Parameters> axisStateStack;
        private readonly IStateSyncManager stateSyncManager;

        private bool spindleCommandPending;

        public string Name => "gcode builder";

        public ProgramBuilder(GCodeMachine machine,
                              IStateSyncManager stateSyncManager,
                              IRTSender rtSender,
                              IModbusSender modbusSender,
                              ISpindleToolFactory spindleToolFactory,
                              IToolManager toolManager,
                              MachineParameters config)
        {
            this.stateSyncManager = stateSyncManager;
            this.machine = machine;
            this.rtSender = rtSender;
            this.modbusSender = modbusSender;
            this.spindleToolFactory = spindleToolFactory;
            this.toolManager = toolManager;
            this.config = config;
            arcMoveFeedLimiter = new ArcMoveFeedLimiter(this.config);
            optimizer = new MoveOptimizer(this.config);
            timeCalculator = new ExpectedTimeCalculator();
            axisStateStack = new Stack<AxisState.Parameters>();
            spindleCommandPending = false;
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
                state.AxisState.Feed = block.Feed.value;
            }
            if (block.Speed != null)
            {
                state.SpindleState.SpindleSpeed = block.Speed.value;
                spindleCommandPending = true;
            }
            return state;
        }

        private CNCState.CNCState ProcessDrillingMove(Arguments block,
                                              ActionProgram.ActionProgram program,
                                              CNCState.CNCState state)
        {
            state = state.BuildCopy();

            var X = block.X;
            var Y = block.Y;
            var Z = block.Z;
            var R = block.R;

            if (R != null)
            {
                if (state.AxisState.Absolute)
                    state.DrillingState.RHeight = R.value;
                else
                    state.DrillingState.RHeight = state.AxisState.Position.z + R.value;
            }

            if (Z != null)
            {
                if (state.AxisState.Absolute)
                    state.DrillingState.Depth = Z.value;
                else
                    state.DrillingState.Depth = state.AxisState.Position.z + Z.value;
            }

            double dx = 0;
            double dy = 0;

            if (state.AxisState.Absolute)
            {
                if (X != null)
                {
                    dx = state.AxisState.Params.CurrentCoordinateSystem.ToGlobalX(X.value) -
                         state.AxisState.Position.x;
                }
                if (Y != null)
                {
                    dy = state.AxisState.Params.CurrentCoordinateSystem.ToGlobalY(Y.value) -
                         state.AxisState.Position.y;
                }
            }
            else
            {
                if (X != null)
                {
                    dx = X.value;
                }
                if (Y != null)
                {
                    dy = Y.value;
                }
            }
            state = program.AddFastLineMovement(new Vector3(dx, dy, 0), state);

            double originalZ = state.AxisState.Position.z;

            double dz = state.DrillingState.RHeight - state.AxisState.Position.z;
            state = program.AddFastLineMovement(new Vector3(0, 0, dz), state);

            // TODO: add pecking, dwelling, etc
            dz = state.DrillingState.Depth - state.AxisState.Position.z;
            state = program.AddLineMovement(new Vector3(0, 0, dz), state.AxisState.Feed, state);

            switch (state.DrillingState.RetractDepth)
            {
                case DrillingState.RetractDepthType.InitialHeight:
                    dz = originalZ - state.AxisState.Position.z;
                    break;
                case DrillingState.RetractDepthType.RHeight:
                    dz = state.DrillingState.RHeight - state.AxisState.Position.z;
                    break;
            }
            state = program.AddFastLineMovement(new Vector3(0, 0, dz), state);

            return state;
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

            double dx = 0;
            double dy = 0;
            double dz = 0;

            if (state.AxisState.Absolute)
            {
                if (X != null)
                {
                    dx = state.AxisState.Params.CurrentCoordinateSystem.ToGlobalX(X.value) -
                         state.AxisState.Position.x;
                }
                if (Y != null)
                {
                    dy = state.AxisState.Params.CurrentCoordinateSystem.ToGlobalY(Y.value) -
                         state.AxisState.Position.y;
                }
                if (Z != null)
                {
                    dz = state.AxisState.Params.CurrentCoordinateSystem.ToGlobalZ(Z.value) -
                         state.AxisState.Position.z;
                }
            }
            else
            {
                if (X != null)
                {
                    dx = X.value;
                }
                if (Y != null)
                {
                    dy = Y.value;
                }
                if (Z != null)
                {
                    dz = Z.value;
                }
            }

            var delta = new Vector3(dx, dy, dz);

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
                        if (R != null)
                        {
                            state = program.AddArcMovement(delta, R.value, ccw, state.AxisState.Axis, state.AxisState.Feed, state);
                        }
                        else
                        {
                            double i = 0;
                            double j = 0;
                            double k = 0;
                            if (I != null)
                                i = I.value;
                            if (J != null)
                                j = J.value;
                            if (K != null)
                                k = K.value;
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
                    state = state.BuildCopy();
                    spindleCommandPending = true;
                    state.SpindleState.RotationState = SpindleState.SpindleRotationState.Clockwise;
                    break;
                case 4:
                    state = state.BuildCopy();
                    spindleCommandPending = true;
                    state.SpindleState.RotationState = SpindleState.SpindleRotationState.CounterClockwise;
                    break;
                case 5:
                    state = state.BuildCopy();
                    spindleCommandPending = true;
                    state.SpindleState.RotationState = SpindleState.SpindleRotationState.Off;
                    break;
            }
            return state;
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
                    state.AxisState.Position.x - X.value;
            }
            if (Y != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.y =
                    state.AxisState.Position.y - Y.value;
            }
            if (Z != null)
            {
                state.AxisState.Params.CurrentCoordinateSystem.Offset.z =
                    state.AxisState.Position.z - Z.value;
            }
            program.AddRTForgetResidual(state);
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

        private void CommitPendingCommands(ActionProgram.ActionProgram program,
                                           CNCState.CNCState state)
        {
            if (spindleCommandPending)
            {
                var command = spindleToolFactory.CreateSpindleToolCommand(state.SpindleState.RotationState, state.SpindleState.SpindleSpeed);
                program.AddModbusToolCommand(command, state, state);
                spindleCommandPending = false;
            }
        }
        /*
        private int CallSubprogram(Arguments args,
                                   int index,
                                   CNCState.CNCState state)
        {
            var prg = args.SingleOptions['P'];
            int prgindex = programs[prg];

            PushPosition(prgindex + 1);

            return prgindex;
        }

        private int ReturnFromSubprogram(Arguments args,
                                         ActionProgram.ActionProgram program,
                                         CNCState.CNCState state)
        {
            int pos = PopPosition();
            if (pos < 0)
                program.AddStop();
            return pos;
        }
        */
        private (int, CNCState.CNCState) ProcessBlock(Arguments block,
                                                      ActionProgram.ActionProgram program,
                                                      CNCState.CNCState state)
        {
            var cmd = block.Options.FirstOrDefault((arg) => (arg.letter == 'G' || arg.letter == 'M'));
            int next = -1;
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
                    case 17:
                        state.AxisState.Params.CurrentPlane = AxisState.Plane.XY;
                        break;
                    case 18:
                        state.AxisState.Params.CurrentPlane = AxisState.Plane.YZ;
                        break;
                    case 19:
                        state.AxisState.Params.CurrentPlane = AxisState.Plane.ZX;
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
                        state.DrillingState.RetractDepth = 0;
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
                        state.DrillingState.RetractDepth = 0;
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
                        state.DrillingState.RetractDepth = 0;
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
                        state.DrillingState.RetractDepth = 0;
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
                        state.DrillingState.RetractDepth = 0;
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
                        state.DrillingState.RetractDepth = 0;
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
                        state.DrillingState.RetractDepth = 0;
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
                        state.DrillingState.RetractDepth = 0;
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
                        state.DrillingState.RetractDepth = 0;
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
                        break;
                    case 3:
                    case 4:
                    case 5:
                        state = ProcessSpindleRunCommand(block, program, state);
                        break;
                    case 6:
                        if (block.SingleOptions.ContainsKey('T'))
                            program.AddToolChange(block.SingleOptions['T'].ivalue1);
                        break;
                    /*case 97:
                        next = CallSubprogram(block, index, state);
                        break;
                    case 99:
                        next = ReturnFromSubprogram(block, program, state);
                        break;*/
                    case 120:
                        PushState(state.AxisState);
                        break;
                    case 121:
                        PopState(state.AxisState);
                        break;
                }
            }

            CommitPendingCommands(program, state);

            return (next, state);
        }

        private (int, CNCState.CNCState) Process(String frame,
                                                 ActionProgram.ActionProgram program,
                                                 CNCState.CNCState state,
                                                 Dictionary<IAction, int> starts,
                                                 int index)
        {
            Arguments args = new Arguments(frame);
            var line_number = args.LineNumber;
            var len0 = program.Actions.Count;
            var next = index + 1;
            var sargs = SplitFrame(args);
            foreach (var block in sargs)
            {
                int si;
                (si, state) = ProcessBlock(block, program, state);
                if (si >= 0)
                {
                    next = si;
                    break;
                }
            }

            var len1 = program.Actions.Count;
            if (len1 > len0)
            {
                var (first, _, _) = program.Actions[len0];
                starts[first] = index;
            }
            return (next, state);
        }

        public (ActionProgram.ActionProgram program,
                CNCState.CNCState state,
                IReadOnlyDictionary<IAction, int> starts,
                double executionTime)
            BuildProgram(String[] frames, CNCState.CNCState state)
        {
            var program = new ActionProgram.ActionProgram(rtSender, modbusSender, config, machine, toolManager);
            var starts = new Dictionary<IAction, int>();
            int index = 0;
            int len = frames.Length;
            program.AddRTUnlock(state);
            starts[program.Actions[0].action] = index;
            while (index < len)
            {
                var frame = frames[index];
                int next;
                try
                {
                    (next, state) = Process(frame, program, state, starts, index);
                    if (next < 0)
                        break;
                    else
                        index = next;
                }
                catch (Exception e)
                {
                    Logger.Instance.Error(this, "compile error", String.Format("{0} : {1}", frame, e.ToString()));
                    throw e;
                }
            }

            program.AddPlaceholder(state);
            arcMoveFeedLimiter.ProcessProgram(program);
            optimizer.ProcessProgram(program);
            timeCalculator.ProcessProgram(program);

            return (program, state, starts, timeCalculator.ExecutionTime);
        }

        public (ActionProgram.ActionProgram program, CNCState.CNCState state, IReadOnlyDictionary<IAction, int> starts, double executionTime)
                BuildProgram(String frame, CNCState.CNCState state)
        {
            return BuildProgram(frame.Split('\n'), state);
        }
    }
}
