using System;
using System.Collections.Generic;
using Actions;
using Actions.ModbusTool;
using RTSender;
using Config;
using Machine;
using ModbusSender;
using CNCState;
using Actions.Tools;

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
        private readonly IToolManager toolManager;

        public ActionProgram(IRTSender rtSender, IModbusSender modbusSender,
                             MachineParameters config, IMachine machine, IToolManager toolManager)
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

        public void AddRTForgetResidual(CNCState.CNCState currentState)
        {
            AddAction(new RTAction(rtSender, new RTForgetResidualCommand()), currentState, currentState);
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
        public void AddHoming(CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            var gz1 = -config.size_z;
            var gz2 = config.step_back_z;
            var gz3 = -config.step_back_z*1.2;
            if (config.invert_z)
            {
                gz1 *= -1;
                gz2 *= -1;
                gz3 *= -1;
            }
            AddRTForgetResidual(currentState);
            AddRTDisableFailOnEndstops(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz1, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)), null, null);
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz2, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz3, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);

            var gx1 = -config.size_x;
            var gx2 = config.step_back_x;
            var gx3 = -config.step_back_x*1.2;
            if (config.invert_x)
            {
                gx1 *= -1;
                gx2 *= -1;
                gx3 *= -1;
            }
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(gx1, 0, 0, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)), null, null);
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(gx2, 0, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(gx3, 0, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);

            var gy1 = -config.size_x;
            var gy2 = config.step_back_y;
            var gy3 = -config.step_back_y*1.2;
            if(config.invert_y)
            {
                gy1 *= -1;
                gy2 *= -1;
                gy3 *= -1;
            }
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, gy1, 0, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)), null, null);
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, gy2, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, gy3, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            AddRTSetZero(null, stateAfter);
            AddRTForgetResidual(null);
            AddRTEnableFailOnEndstops(null);
        }

        public void AddZProbe(CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            var gz1 = -config.size_z;
            var gz2 = config.step_back_z;
            var gz3 = -config.step_back_z*1.2;

            AddRTEnableBreakOnProbe(currentState);
            AddRTDisableFailOnEndstops(null);
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz1, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)), null, null);
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz2, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, null);
            AddRTForgetResidual(null);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz3, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)), null, stateAfter);
            AddRTForgetResidual(null);
            AddRTDisableBreakOnProbe(null);
            AddRTEnableFailOnEndstops(null);
        }

        private (double feed, double acc) MaxLineFeedAcc(Vector3 dir)
        {
            double maxacc = config.max_acceleration;
            double feed, acc;
            acc = Double.PositiveInfinity;
            if (Math.Abs(dir.x) > 1e-8)
                acc = Math.Min(acc, maxacc / Math.Abs(dir.x));
            if (Math.Abs(dir.y) > 1e-8)
                acc = Math.Min(acc, maxacc / Math.Abs(dir.y));
            if (Math.Abs(dir.z) > 1e-8)
                acc = Math.Min(acc, maxacc / Math.Abs(dir.z));

            double maxf = config.maxfeed;
            feed = Double.PositiveInfinity;
            if (Math.Abs(dir.x) > 1e-8)
                feed = Math.Min(feed, maxf / Math.Abs(dir.x));
            if (Math.Abs(dir.y) > 1e-8)
                feed = Math.Min(feed, maxf / Math.Abs(dir.y));
            if (Math.Abs(dir.z) > 1e-8)
                feed = Math.Min(feed, maxf / Math.Abs(dir.z));
            return (feed, acc);
        }

        public CNCState.CNCState AddLineMovement(Vector3 delta, double feed, CNCState.CNCState currentState)
        {
            if (Math.Abs(delta.x) < 1e-12 && Math.Abs(delta.y) < 1e-12 && Math.Abs(delta.z) < 1e-12)
                return currentState;
            var dir = Vector3.Normalize(delta);
            var fa = MaxLineFeedAcc(dir);
            var maxfeed = fa.feed;
            var acc = config.max_acceleration;
            feed = Math.Min(feed, maxfeed);
            var stateAfter = currentState.BuildCopy();
            stateAfter.AxisState.Position.x += delta.x;
            stateAfter.AxisState.Position.y += delta.y;
            stateAfter.AxisState.Position.z += delta.z;
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(delta, new RTMovementOptions(0, feed, 0, acc), config)), currentState, stateAfter);
            return stateAfter;
        }

        public CNCState.CNCState AddFastLineMovement(Vector3 delta, CNCState.CNCState currentState)
        {
            if (Math.Abs(delta.x) < 1e-12 && Math.Abs(delta.y) < 1e-12 && Math.Abs(delta.z) < 1e-12)
                return currentState;
            var dir = Vector3.Normalize(delta);
            var fa = MaxLineFeedAcc(dir);
            var maxfeed = fa.feed;
            var acc = fa.acc;
            var stateAfter = currentState.BuildCopy();
            stateAfter.AxisState.Position.x += delta.x;
            stateAfter.AxisState.Position.y += delta.y;
            stateAfter.AxisState.Position.z += delta.z;
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(delta, new RTMovementOptions(0, maxfeed, 0, acc), config)), currentState, stateAfter);
            return stateAfter;
        }

        private void MaxArcAcc(out double acc)
        {
            //TODO: calculate
            acc = config.max_acceleration;
        }

        public CNCState.CNCState AddArcMovement(Vector3 delta, double R, bool ccw, RTArcMoveCommand.ArcAxis axis, double feed, CNCState.CNCState currentState)
        {
            var stateAfter = currentState.BuildCopy();
            stateAfter.AxisState.Position.x += delta.x;
            stateAfter.AxisState.Position.y += delta.y;
            stateAfter.AxisState.Position.z += delta.z;
            MaxArcAcc(out double acc);

            AddAction(new RTAction(rtSender, new RTArcMoveCommand(delta, R, ccw, axis, new RTMovementOptions(0, feed, 0, acc), config)), currentState, stateAfter);
            return stateAfter;
        }

        public CNCState.CNCState AddArcMovement(Vector3 delta, Vector3 center, bool ccw, RTArcMoveCommand.ArcAxis axis, double feed, CNCState.CNCState currentState)
        {
            var stateAfter = currentState.BuildCopy();
            stateAfter.AxisState.Position.x += delta.x;
            stateAfter.AxisState.Position.y += delta.y;
            stateAfter.AxisState.Position.z += delta.z;

            MaxArcAcc(out double acc);
            AddAction(new RTAction(rtSender, new RTArcMoveCommand(delta, center, ccw, axis, new RTMovementOptions(0, feed, 0, acc), config)), currentState, stateAfter);
            return stateAfter;
        }
        #endregion

        #region Tool
        public void AddModbusToolCommand(ModbusToolCommand command, CNCState.CNCState currentState, CNCState.CNCState stateAfter)
        {
            AddAction(new ModbusToolAction(command, modbusSender), currentState, stateAfter);
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
        #endregion

        public void AddPlaceholder(CNCState.CNCState currentState)
        {
            AddAction(new PlaceholderAction(), currentState, currentState);
        }
    }
}
