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
                                    AxisState axisState,
                                    SpindleState spindleState)
        {
            bool spindleChange = false;
            if (args.Feed != null)
            {
                axisState.Feed = args.Feed.value;
            }
            if (args.Speed != null)
            {
                if (Math.Abs(spindleState.SpindleSpeed - args.Speed.value) > 1e-2)
                {
                    spindleState.SpindleSpeed = args.Speed.value;
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
                                spindleState.RotationState = SpindleRotationState.Clockwise;
                                break;
                            case 4:
                                spindleChange = true;
                                spindleState.RotationState = SpindleRotationState.CounterClockwise;
                                break;
                            case 120:
                                PushState(axisState);
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
                                axisState.Params.CurrentCoordinateSystemIndex = 0;
                                break;
                            case 54:
                                axisState.Params.CurrentCoordinateSystemIndex = 1;
                                break;
                            case 55:
                                axisState.Params.CurrentCoordinateSystemIndex = 2;
                                break;
                            case 56:
                                axisState.Params.CurrentCoordinateSystemIndex = 3;
                                break;
                            case 57:
                                axisState.Params.CurrentCoordinateSystemIndex = 4;
                                break;
                            case 58:
                                axisState.Params.CurrentCoordinateSystemIndex = 5;
                                break;
                            case 59:
                                axisState.Params.CurrentCoordinateSystemIndex = 6;
                                break;
                            case 90:
                                axisState.Absolute = true;
                                break;
                            case 91:
                                axisState.Absolute = false;
                                break;
                        }
                    }
                }
            }

            if (spindleChange)
            {
                var command = spindleToolFactory.CreateSpindleToolCommand(spindleState.RotationState, spindleState.SpindleSpeed);
                program.AddModbusToolCommand(command);
            }
        }

        private void ProcessMove(Arguments args,
                                 ActionProgram.ActionProgram program,
                                 AxisState axisState)
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
            if (axisState.Absolute)
            {
                if (X != null)
                {
                    has_move = true;
                    dx = axisState.Params.CurrentCoordinateSystem.ToGlobalX(X.value) - axisState.Position.x;
                }
                if (Y != null)
                {
                    has_move = true;
                    dy = axisState.Params.CurrentCoordinateSystem.ToGlobalY(Y.value) - axisState.Position.y;
                }
                if (Z != null)
                {
                    has_move = true;
                    dz = axisState.Params.CurrentCoordinateSystem.ToGlobalZ(Z.value) - axisState.Position.z;
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
                            axisState.MoveType = AxisState.MType.FastLine;
                            break;
                        case 1:
                            axisState.MoveType = AxisState.MType.Line;
                            break;
                        case 2:
                            axisState.MoveType = AxisState.MType.ArcCW;
                            break;
                        case 3:
                            axisState.MoveType = AxisState.MType.ArcCCW;
                            break;
                        case 28:
                            program.AddHoming();
                            break;
                        case 30:
                            program.AddZProbe();
                            break;
                        case 92:
                            has_move = false;
                            if (X != null)
                            {
                                axisState.Params.CurrentCoordinateSystem.Offset.x = axisState.Position.x - X.value;
                            }
                            if (Y != null)
                            {
                                axisState.Params.CurrentCoordinateSystem.Offset.y = axisState.Position.y - Y.value;
                            }
                            if (Z != null)
                            {
                                axisState.Params.CurrentCoordinateSystem.Offset.z = axisState.Position.z - Z.value;
                            }
                            program.AddRTForgetResidual();
                            break;
                    }
                }

            }

            if (has_move)
            {
                switch (axisState.MoveType)
                {
                    case AxisState.MType.FastLine:
                        program.AddFastLineMovement(delta);
                        break;
                    case AxisState.MType.Line:
                        program.AddLineMovement(delta, axisState.Feed);
                        break;
                    case AxisState.MType.ArcCW:
                    case AxisState.MType.ArcCCW:
                        {
                            bool ccw = (axisState.MoveType == AxisState.MType.ArcCCW);
                            if (R != null)
                            {
                                program.AddArcMovement(delta, R.value, ccw, axisState.ArcAxis, axisState.Feed);
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
                                program.AddArcMovement(delta, new Vector3(i, j, k), ccw, axisState.ArcAxis, axisState.Feed);
                            }
                        }
                        break;
                    default:
                        break;
                }
                axisState.Position.x += dx;
                axisState.Position.y += dy;
                axisState.Position.z += dz;
            }
        }

        private void ProcessPostMove(Arguments args,
                                     ActionProgram.ActionProgram program,
                                     AxisState axisState,
                                     SpindleState spindleState)
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
                                spindleState.RotationState = SpindleRotationState.Off;
                                break;
                            case 121:
                                PopState(axisState);
                                break;
                        }
                    }
                }
            }
            if (spindleChange)
            {
                ModbusToolCommand command = spindleToolFactory.CreateSpindleToolCommand(spindleState.RotationState, spindleState.SpindleSpeed);
                program.AddModbusToolCommand(command);
            }
        }

        private int Process(String frame,
                            ActionProgram.ActionProgram program,
                            AxisState axisState,
                            SpindleState spindleState,
                            int index)
        {
            Arguments args = new Arguments(frame);
            var line_number = args.LineNumber;

            ProcessPreMove(args, program, axisState, spindleState);
            ProcessMove(args, program, axisState);
            ProcessPostMove(args, program, axisState, spindleState);

            return index + 1;
        }

        public ActionProgram.ActionProgram BuildProgram(String[] frames,
                                                        AxisState axisState,
                                                        SpindleState spindleState)
        {
            var program = new ActionProgram.ActionProgram(rtSender, modbusSender, config, machine);
            int index = 0;
            int len = frames.Length;
            program.AddRTUnlock();
            while (index < len)
            {
                var frame = frames[index];
                int next = Process(frame, program, axisState, spindleState, index);
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
                                                        AxisState axisState,
                                                        SpindleState spindleState)
        {
            return BuildProgram(frame.Split('\n'), axisState, spindleState);
        }
    }
}
