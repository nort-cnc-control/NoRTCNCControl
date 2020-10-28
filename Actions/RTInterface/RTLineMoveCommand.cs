using System;
using Config;
using Vector;

namespace Actions
{
    public class RTLineMoveCommand : IRTMoveCommand
    {
        public bool CommandIsCached { get { return true; } }
        private MachineParameters config;
        public Vector3 DirStart { get; private set; }
        public Vector3 DirEnd { get; private set; }
        public Vector3 Delta { get; private set; }
        public Vector3 PhysicalDelta { get; private set; }
        public decimal Length { get; private set; }
        public RTMovementOptions Options { get; private set; }

        private int dx, dy, dz;

        private void FindPhysicalParameters()
        {
            Vector3 hwdelta = new Vector3
            {
                x = Delta.x * config.X_axis.sign,
                y = Delta.y * config.Y_axis.sign,
                z = Delta.z * config.Z_axis.sign
            };

            dx = (int)(hwdelta.x * config.X_axis.steps_per_mm);
            dy = (int)(hwdelta.y * config.Y_axis.steps_per_mm);
            dz = (int)(hwdelta.z * config.Z_axis.steps_per_mm);

            PhysicalDelta = new Vector3
            {
                x = dx / config.X_axis.steps_per_mm * config.X_axis.sign,
                y = dy / config.Y_axis.steps_per_mm * config.Y_axis.sign,
                z = dz / config.Z_axis.steps_per_mm * config.Z_axis.sign
            };
        }

        public RTLineMoveCommand(Vector3 delta, RTMovementOptions opts, MachineParameters config)
        {
            this.config = config;
            this.Delta = delta;
            this.DirStart = this.DirEnd = Vector3.Normalize(this.Delta);
            this.Options = opts;
            Length = Delta.Length();
            FindPhysicalParameters();
        }

        public RTLineMoveCommand(decimal dx, decimal dy, decimal dz, RTMovementOptions opts, MachineParameters config)
        {
            this.config = config;
            this.Delta = new Vector3(dx, dy, dz);
            this.DirStart = this.DirEnd = Vector3.Normalize(this.Delta);
            this.Options = opts;
            Length = Delta.Length();
            FindPhysicalParameters();
        }

        private string FormatD(decimal x)
        {
            return x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        public String Command
        {
            get
            {
                return $"G1 {Options.Command} X{dx} Y{dy} Z{dz}";
            }
        }


    }
}
