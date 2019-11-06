using System;
using System.Collections.Generic;
using Actions;
using Actions.ModbusTool;
using RTSender;
using Config;
using Machine;
using ModbusSender;

namespace ActionProgram
{
    public class ActionProgram
    {
        private List<IAction> actions;
        public IReadOnlyList<IAction> Actions => actions;
        private int index;
        private IRTSender rtSender;
        private IModbusSender modbusSender;
        private MachineParameters config;
        private IMachine machine;

        public ActionProgram(IRTSender rtSender, IModbusSender modbusSender,
                             MachineParameters config, IMachine machine,
                             int index=0)
        {
            this.index = index;
            this.machine = machine;
            this.config = config;
            this.rtSender = rtSender;
            this.modbusSender = modbusSender;
            actions = new List<IAction>();
        }

        private void AddAction(IAction action)
        {
            actions.Add(action);
        }

        public void AddRTAction(String cmd)
        {
            AddAction(new RTAction(rtSender, new SimpleRTCommand(cmd)));
        }

        #region control
        public void AddRTUnlock()
        {
            AddAction(new RTAction(rtSender, new RTLockCommand(false)));
        }

        public void AddRTLock()
        {
            AddAction(new RTAction(rtSender, new RTLockCommand(true)));
        }

        public void AddRTForgetResidual()
        {
            AddAction(new RTAction(rtSender, new RTForgetResidualCommand()));
        }

        public void AddRTSetZero()
        {
            AddAction(new RTAction(rtSender, new RTSetZeroCommand()));
        }
        public void AddRTEnableBreakOnProbe()
        {
            AddAction(new RTAction(rtSender, new RTBreakOnProbeCommand(true)));
        }
        public void AddRTDisableBreakOnProbe()
        {
            AddAction(new RTAction(rtSender, new RTBreakOnProbeCommand(false)));
        }

        #endregion

        #region Movements
        public void AddHoming()
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
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz1, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)));
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz2, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)));
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz3, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)));

            var gx1 = -config.size_x;
            var gx2 = config.step_back_x;
            var gx3 = -config.step_back_x*1.2;
            if (config.invert_x)
            {
                gx1 *= -1;
                gx2 *= -1;
                gx3 *= -1;
            }
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(gx1, 0, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)));
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(gx2, 0, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)));
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(gx3, 0, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)));

            var gy1 = -config.size_x;
            var gy2 = config.step_back_y;
            var gy3 = -config.step_back_y*1.2;
            if(config.invert_y)
            {
                gy1 *= -1;
                gy2 *= -1;
                gy3 *= -1;
            }
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, gy1, 0, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)));
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, gy2, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)));
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, gy3, 0, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)));
            AddRTSetZero();
            AddRTForgetResidual();
        }

        public void AddZProbe()
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
            AddRTEnableBreakOnProbe();
            
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz1, new RTMovementOptions(0, config.fastfeed, 0, config.max_acceleration), config)));
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz2, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)));
            AddRTForgetResidual();
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(0, 0, gz3, new RTMovementOptions(0, config.slowfeed, 0, config.max_acceleration), config)));
            AddRTForgetResidual();
            AddRTDisableBreakOnProbe();
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

        public void AddLineMovement(Vector3 delta, double feed)
        {
            if (Math.Abs(delta.x) < 1e-12 && Math.Abs(delta.y) < 1e-12 && Math.Abs(delta.z) < 1e-12)
                return;
            var dir = Vector3.Normalize(delta);
            var fa = MaxLineFeedAcc(dir);
            var maxfeed = fa.feed;
            var acc = config.max_acceleration;
            feed = Math.Min(feed, maxfeed);
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(delta, new RTMovementOptions(0, feed, 0, acc), config)));
        }

        public void AddFastLineMovement(Vector3 delta)
        {
            if (Math.Abs(delta.x) < 1e-12 && Math.Abs(delta.y) < 1e-12 && Math.Abs(delta.z) < 1e-12)
                return;
            var dir = Vector3.Normalize(delta);
            var fa = MaxLineFeedAcc(dir);
            var maxfeed = fa.feed;
            var acc = fa.acc;
            AddAction(new RTAction(rtSender, new RTLineMoveCommand(delta, new RTMovementOptions(0, maxfeed, 0, acc), config)));
        }

        private void MaxArcAcc(out double acc)
        {
            //TODO: calculate
            acc = 40;
        }

        public void AddArcMovement(Vector3 delta, double R, bool ccw, RTArcMoveCommand.ArcAxis axis, double feed)
        {
            MaxArcAcc(out double acc);
            AddAction(new RTAction(rtSender, new RTArcMoveCommand(delta, R, ccw, axis, new RTMovementOptions(0, feed, 0, acc), config)));
        }

        public void AddArcMovement(Vector3 delta, Vector3 center, bool ccw, RTArcMoveCommand.ArcAxis axis, double feed)
        {
            double acc = 0;
            AddAction(new RTAction(rtSender, new RTArcMoveCommand(delta, center, ccw, axis, new RTMovementOptions(0, feed, 0, acc), config)));
        }
        #endregion

        #region Tool
        public void AddModbusToolCommand(ModbusToolCommand command)
        {
            AddAction(new ModbusToolAction(command, modbusSender));
        }
        #endregion

        #region stops
        public void AddBreak()
        {
            AddAction(new MachineControlAction(new PauseCommand(machine), machine));
        }

        public void AddStop()
        {
            AddAction(new MachineControlAction(new StopCommand(machine), machine));
        }
        #endregion
    }
}
