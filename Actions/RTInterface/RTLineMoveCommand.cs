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
		public Vector3 Compenstation { get; private set; }
        public Vector3 PhysicalDelta { get; private set; }
        public decimal Length { get; private set; }
        public RTMovementOptions Options { get; private set; }

        private int dx, dy, dz;

        private void FindPhysicalParameters()
        {
            Vector3 hwdelta = new Vector3
            {
                x = (Delta.x + Compenstation.x) * config.X_axis.sign,
                y = (Delta.y + Compenstation.y) * config.Y_axis.sign,
                z = (Delta.z + Compenstation.z) * config.Z_axis.sign
            };

            dx = (int)Math.Round(hwdelta.x * config.X_axis.steps_per_mm);
            dy = (int)Math.Round(hwdelta.y * config.Y_axis.steps_per_mm);
            dz = (int)Math.Round(hwdelta.z * config.Z_axis.steps_per_mm);

            PhysicalDelta = new Vector3
            {
                x = dx / config.X_axis.steps_per_mm * config.X_axis.sign,
                y = dy / config.Y_axis.steps_per_mm * config.Y_axis.sign,
                z = dz / config.Z_axis.steps_per_mm * config.Z_axis.sign
            };
        }

        public RTLineMoveCommand(Vector3 delta, Vector3 compensation, RTMovementOptions opts, MachineParameters config)
        {
            this.config = config;
            this.Delta = delta;
			this.Compenstation = compensation;
            this.Options = opts;
            this.DirStart = this.DirEnd = Vector3.Normalize(this.Delta);
			FindPhysicalParameters();
			Length = Delta.Length();
        }

        public RTLineMoveCommand(decimal dx, decimal dy, decimal dz, Vector3 compensation, RTMovementOptions opts, MachineParameters config)
        {
            this.config = config;
            this.Delta = new Vector3(dx, dy, dz);
			this.Compenstation = compensation;
            this.Options = opts;
            this.DirStart = this.DirEnd = Vector3.Normalize(this.Delta);
			FindPhysicalParameters();
			Length = PhysicalDelta.Length();
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
