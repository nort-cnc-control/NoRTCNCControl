using System;
using System.Collections.Generic;
using Actions;
using RTSender;
using Config;
using Machine;
using ModbusSender;
using CNCState;
using Actions.Mills;
using Vector;
using System.Threading;

namespace ActionProgram
{
    public class ActionProgram
    {
        private List<(IAction action, CNCState.CNCState state, CNCState.CNCState stateAfter)> actions;
        public IReadOnlyList<(IAction action, CNCState.CNCState state, CNCState.CNCState stateAfter)> Actions => actions;
        private readonly IRTSender rtSender;
        private readonly IModbusSender modbusSender;
        private MachineParameters config;
        private readonly IMachine machine;
        private readonly IMillManager toolManager;

        public ActionProgram(IRTSender rtSender, IModbusSender modbusSender,
                             MachineParameters config, IMachine machine, IMillManager toolManager)
        {
            this.machine = machine;
            this.config = config;
            this.rtSender = rtSender;
            this.modbusSender = modbusSender;
            this.toolManager = toolManager;
            actions = new List<(IAction action, CNCState.CNCState state, CNCState.CNCState stateAfter)>();
        }

        public void AddAction(IAction action, CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            CNCState.CNCState before = null, after = null;
            if (currentState != null)
                before = currentState.BuildCopy();
            if (stateAfter != null)
                after = stateAfter.BuildCopy();

            actions.Add((action, before, after));
        }

        public void AddRTAction(String cmd, CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            AddAction(new RTAction(rtSender, new SimpleRTCommand(cmd)), currentState, stateAfter);
        }

        #region control
        public void AddRTUnlock(CNCState.CNCState currentState)
        {
            AddAction(new RTAction(rtSender, new RTLockCommand(false)), currentState, currentState);
        }

        public void AddRTLock(CNCState.CNCState currentState)
        {
            AddAction(new RTAction(rtSender, new RTLockCommand(true)), currentState, currentState);
        }


        public void AddRTSetZero(CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            AddAction(new RTAction(rtSender, new RTSetZeroCommand()), currentState, stateAfter);
        }

        public void AddRTEnableBreakOnProbe(CNCState.CNCState currentState)
        {
            AddAction(new RTAction(rtSender, new RTBreakOnProbeCommand(true)), currentState, currentState);
        }

        public void AddRTDisableBreakOnProbe(CNCState.CNCState currentState)
        {
            AddAction(new RTAction(rtSender, new RTBreakOnProbeCommand(false)), currentState, currentState);
        }

        public void AddRTEnableFailOnEndstops(CNCState.CNCState currentState)
        {
            AddAction(new RTAction(rtSender, new RTFailOnESCommand(true)), currentState, currentState);
        }

        public void AddRTDisableFailOnEndstops(CNCState.CNCState currentState)
        {
            AddAction(new RTAction(rtSender, new RTFailOnESCommand(false)), currentState, currentState);
        }
        #endregion

        #region Movements
        private void ForgetResidual(CNCState.CNCState currentState)
        {
            currentState.AxisState.TargetPosition = currentState.AxisState.Position;
        }

        public void AddHoming(CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            var gz1 = -config.Z_axis.size * config.Z_axis.sign;
            var gz2 = config.Z_axis.step_back * config.Z_axis.sign;
            var gz3 = -config.Z_axis.step_back * 1.2m * config.Z_axis.sign;

            AddRTDisableFailOnEndstops(null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz1, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)), null, null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz2, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz3, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);

            var gx1 = -config.X_axis.size * config.X_axis.sign;
            var gx2 = config.X_axis.step_back * config.X_axis.sign;
            var gx3 = -config.X_axis.step_back*1.2m * config.X_axis.sign;

            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(gx1, 0, 0, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)), null, null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(gx2, 0, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(gx3, 0, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);

            var gy1 = -config.Y_axis.size * config.Y_axis.sign;
            var gy2 = config.Y_axis.step_back * config.Y_axis.sign;
            var gy3 = -config.Y_axis.step_back* 1.2m * config.Y_axis.sign;

            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, gy1, 0, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)), null, null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, gy2, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, gy3, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            ForgetResidual(currentState);
            AddRTSetZero(null, stateAfter);
            AddRTEnableFailOnEndstops(null);
        }

        public void AddZProbe(CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            var gz1 = -config.Z_axis.size;
            var gz2 = config.Z_axis.step_back;
            var gz3 = -config.Z_axis.step_back*1.2m;

            AddRTEnableBreakOnProbe(currentState);
            AddRTDisableFailOnEndstops(null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz1, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)), null, null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz2, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            ForgetResidual(currentState);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz3, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, stateAfter);
            ForgetResidual(currentState);
            AddRTDisableBreakOnProbe(null);
            AddRTEnableFailOnEndstops(null);
        }

        public CNCState.CNCState AddLineMovement(Vector3 delta, decimal feed, CNCState.CNCState currentState)
        {
            if (Math.Abs(delta.x) < 1e-12m && Math.Abs(delta.y) < 1e-12m && Math.Abs(delta.z) < 1e-12m)
                return currentState;
            var stateAfter = currentState.BuildCopy();
            var command = new RTLineMoveCommand(delta, new RTMovementOptions(0, feed, 0, config.max_acceleration), config);
            stateAfter.AxisState.Position += command.PhysicalDelta;
            AddAction(new RTAction(rtSender, command), currentState, stateAfter);
            return stateAfter;
        }

        public CNCState.CNCState AddFastLineMovement(Vector3 delta, CNCState.CNCState currentState)
        {
            if (Math.Abs(delta.x) < 1e-12m && Math.Abs(delta.y) < 1e-12m && Math.Abs(delta.z) < 1e-12m)
                return currentState;
            var stateAfter = currentState.BuildCopy();
            var command = new RTLineMoveCommand(delta, new RTMovementOptions(0, Decimal.MaxValue, 0, config.max_acceleration), config);
            stateAfter.AxisState.Position += command.PhysicalDelta;
            AddAction(new RTAction(rtSender, command), currentState, stateAfter);
            return stateAfter;
        }

        public CNCState.CNCState AddArcMovement(Vector3 delta, decimal R, bool ccw, AxisState.Plane axis, decimal feed, CNCState.CNCState currentState)
        {
            var stateAfter = currentState.BuildCopy();

            var cmd = new RTArcMoveCommand(delta, R, ccw, axis, new RTMovementOptions(0, feed, 0, config.max_acceleration), config);
            stateAfter.AxisState.Position += cmd.PhysicalDelta;
            AddAction(new RTAction(rtSender, cmd), currentState, stateAfter);
            return stateAfter;
        }

        public CNCState.CNCState AddArcMovement(Vector3 delta, Vector3 center, bool ccw, AxisState.Plane axis, decimal feed, CNCState.CNCState currentState)
        {
            var stateAfter = currentState.BuildCopy();

            var cmd = new RTArcMoveCommand(delta, center, ccw, axis, new RTMovementOptions(0, feed, 0, config.max_acceleration), config);
            stateAfter.AxisState.Position += cmd.PhysicalDelta;
            AddAction(new RTAction(rtSender, cmd), currentState, stateAfter);
            return stateAfter;
        }

        #endregion

        #region stops
        public void AddBreak()
        {
            AddAction(new MachineControlAction(new PauseCommand(machine), machine), null, null);
        }

        public void AddStop()
        {
            AddAction(new MachineControlAction(new StopCommand(machine), machine), null, null);
        }
        #endregion

        #region tools
        public void AddToolChange(int toolId)
        {
            var cmd = new SelectToolCommand(toolId, machine, toolManager);
            AddAction(new MachineControlAction(cmd, machine), null, null);
        }

        public void EnableRTTool(int tool, CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            var action = new RTAction(rtSender, new RTGPIOCommand(tool, true));
            AddAction(action, currentState, stateAfter);
        }

        public void DisableRTTool(int tool, CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            var action = new RTAction(rtSender, new RTGPIOCommand(tool, false));
            AddAction(action, currentState, stateAfter);
        }

        #endregion

        public void AddPlaceholder(CNCState.CNCState currentState)
        {
            AddAction(new PlaceholderAction(), currentState, currentState);
        }

        public void AddDelay(int ms, CNCState.CNCState currentState)
        {
            void delayf()
            {
                Thread.Sleep(ms);
            }
            AddAction(new FunctionAction(delayf), currentState, currentState);
        }
    }
}
