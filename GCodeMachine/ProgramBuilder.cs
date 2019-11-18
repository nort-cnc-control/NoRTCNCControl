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

using System.Collections.Generic;

namespace GCodeMachine
{
    public class ProgramBuilder
    {
        private readonly IRTSender rtSender;
        private readonly IModbusSender modbusSender;
        private readonly ISpindleToolFactory spindleToolFactory;
        private readonly MachineParameters config;
        private readonly GCodeMachine machine;
        private ArcMoveFeedLimiter arcMoveFeedLimiter;
        private MoveOptimizer optimizer;
        private Stack<AxisState.Parameters> axisStateStack;

        public ProgramBuilder(GCodeMachine machine,
                              IRTSender rtSender,
                              IModbusSender modbusSender,
                              ISpindleToolFactory spindleToolFactory,
                              MachineParameters config)
        {
            this.machine = machine;
            this.rtSender = rtSender;
            this.modbusSender = modbusSender;
            this.spindleToolFactory = spindleToolFactory;
            this.config = config;
            arcMoveFeedLimiter = new ArcMoveFeedLimiter(this.config);
            optimizer = new MoveOptimizer(this.config);
            axisStateStack = new Stack<AxisState.Parameters>();
        }

        private void PushState(AxisState axisState)
        {
            axisStateStack.Push(axisState.Params.BuildCopy());
        }

        private void PopState(AxisState axisState)
        {
            axisState.Params = axisStateStack.Pop();
        }

        private void ProcessPreMove(Arguments args,
                                    ActionProgram.ActionProgram program,
                                    CNCState.CNCState state)
        {
            bool spindleChange = false;
            if (args.Feed != null)
            {
                state.AxisState.Feed = args.Feed.value;
            }
            if (args.Speed != null)
            {
                if (Math.Abs(state.SpindleState.SpindleSpeed - args.Speed.value) > 1e-2)
                {
                    state.SpindleState.SpindleSpeed = args.Speed.value;
                    spindleChange = true;
                }
            }
            foreach (var cmd in args.Options)
            {
                if (cmd.letter == 'M')
                {
                    if (cmd.dot == false)
                    {
                        // We don't check current state == new state,
                        // because of possible inconsistency of spindle driver and state
                        switch (cmd.ivalue1)
                        {
                            case 3:
                                spindleChange = true;
                                state.SpindleState.RotationState = SpindleRotationState.Clockwise;
                                break;
                            case 4:
                                spindleChange = true;
                                state.SpindleState.RotationState = SpindleRotationState.CounterClockwise;
                                break;
                            case 120:
                                PushState(state.AxisState);
                                break;
                        }
                    }
                }
                else if (cmd.letter == 'G')
                {
                    if (cmd.dot == false)
                    {
                        switch (cmd.ivalue1)
                        {
                            case 53:
                                state.AxisState.Params.CurrentCoordinateSystemIndex = 0;
                                break;
                            case 54:
                                state.AxisState.Params.CurrentCoordinateSystemIndex = 1;
                                break;
                            case 55:
                                state.AxisState.Params.CurrentCoordinateSystemIndex = 2;
                                break;
                            case 56:
                                state.AxisState.Params.CurrentCoordinateSystemIndex = 3;
                                break;
                            case 57:
                                state.AxisState.Params.CurrentCoordinateSystemIndex = 4;
                                break;
                            case 58:
                                state.AxisState.Params.CurrentCoordinateSystemIndex = 5;
                                break;
                            case 59:
                                state.AxisState.Params.CurrentCoordinateSystemIndex = 6;
                                break;
                            case 90:
                                state.AxisState.Absolute = true;
                                break;
                            case 91:
                                state.AxisState.Absolute = false;
                                break;
                        }
                    }
                }
            }

            if (spindleChange)
            {
                var command = spindleToolFactory.CreateSpindleToolCommand(state.SpindleState.RotationState, state.SpindleState.SpindleSpeed);
                program.AddModbusToolCommand(command, state);
            }
        }

        private void ProcessMove(Arguments args,
                                 ActionProgram.ActionProgram program,
                                 CNCState.CNCState state)
        {
            var X = args.X;
            var Y = args.Y;
            var Z = args.Z;
            var I = args.I;
            var J = args.J;
            var K = args.K;
            var R = args.R;

            double dx = 0;
            double dy = 0;
            double dz = 0;
            bool has_move = false;
            if (state.AxisState.Absolute)
            {
                if (X != null)
                {
                    has_move = true;
                    dx = state.AxisState.Params.CurrentCoordinateSystem.ToGlobalX(X.value) -
                         state.AxisState.Position.x;
                }
                if (Y != null)
                {
                    has_move = true;
                    dy = state.AxisState.Params.CurrentCoordinateSystem.ToGlobalY(Y.value) -
                         state.AxisState.Position.y;
                }
                if (Z != null)
                {
                    has_move = true;
                    dz = state.AxisState.Params.CurrentCoordinateSystem.ToGlobalZ(Z.value) -
                         state.AxisState.Position.z;
                }
            }
            else
            {
                if (X != null)
                {
                    has_move = true;
                    dx = X.value;
                }
                if (Y != null)
                {
                    has_move = true;
                    dy = Y.value;
                }
                if (Z != null)
                {
                    has_move = true;
                    dz = Z.value;
                }
            }

            var delta = new Vector3(dx, dy, dz);
            foreach (var cmd in args.GCommands)
            {
                if (cmd.dot == false)
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
                        case 28:
                            program.AddHoming(state);
                            break;
                        case 30:
                            program.AddZProbe(state);
                            break;
                        case 92:
                            has_move = false;
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
                            break;
                    }
                }

            }

            if (has_move)
            {
                switch (state.AxisState.MoveType)
                {
                    case AxisState.MType.FastLine:
                        program.AddFastLineMovement(delta, state);
                        break;
                    case AxisState.MType.Line:
                        program.AddLineMovement(delta, state.AxisState.Feed, state);
                        break;
                    case AxisState.MType.ArcCW:
                    case AxisState.MType.ArcCCW:
                        {
                            bool ccw = (state.AxisState.MoveType == AxisState.MType.ArcCCW);
                            if (R != null)
                            {
                                program.AddArcMovement(delta, R.value, ccw, state.AxisState.ArcAxis, state.AxisState.Feed, state);
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
                                program.AddArcMovement(delta, new Vector3(i, j, k), ccw, state.AxisState.ArcAxis, state.AxisState.Feed, state);
                            }
                        }
                        break;
                    default:
                        break;
                }
                state.AxisState.Position.x += dx;
                state.AxisState.Position.y += dy;
                state.AxisState.Position.z += dz;
            }
        }

        private void ProcessPostMove(Arguments args,
                                     ActionProgram.ActionProgram program,
                                     CNCState.CNCState state)
        {
            bool spindleChange = false;
            foreach (var cmd in args.Options)
            {
                if (cmd.letter == 'M')
                {
                    if (cmd.dot == false)
                    {
                        // We don't check current state == new state,
                        // because of possible inconsistency of spindle driver and state
                        switch (cmd.ivalue1)
                        {
                            case 5:
                                spindleChange = true;
                                state.SpindleState.RotationState = SpindleRotationState.Off;
                                break;
                            case 121:
                                PopState(state.AxisState);
                                break;
                        }
                    }
                }
            }
            if (spindleChange)
            {
                ModbusToolCommand command = spindleToolFactory.CreateSpindleToolCommand(state.SpindleState.RotationState, state.SpindleState.SpindleSpeed);
                program.AddModbusToolCommand(command, state);
            }
        }

        private int Process(String frame,
                            ActionProgram.ActionProgram program,
                            CNCState.CNCState state,
                            int index)
        {
            Arguments args = new Arguments(frame);
            var line_number = args.LineNumber;

            ProcessPreMove(args, program, state);
            ProcessMove(args, program, state);
            ProcessPostMove(args, program, state);

            return index + 1;
        }

        public ActionProgram.ActionProgram BuildProgram(String[] frames,
                                                        CNCState.CNCState state)
        {
            var program = new ActionProgram.ActionProgram(rtSender, modbusSender, config, machine);
            int index = 0;
            int len = frames.Length;
            program.AddRTUnlock(state);
            while (index < len)
            {
                var frame = frames[index];
                int next = Process(frame, program, state, index);
                if (next < 0)
                    break;
                else
                    index = next;
            }

            arcMoveFeedLimiter.ProcessProgram(program);
            optimizer.ProcessProgram(program);

            return program;
        }

        public ActionProgram.ActionProgram BuildProgram(String frame,
                                                        CNCState.CNCState state)
        {
            return BuildProgram(frame.Split('\n'), state);
        }
    }
}
