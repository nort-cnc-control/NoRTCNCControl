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
            Vector3 hwdelta = new Vector3();

            if (config.invert_x)
                hwdelta.x = -Delta.x;
            else
                hwdelta.x = Delta.x;

            if (config.invert_y)
                hwdelta.y = -Delta.y;
            else
                hwdelta.y = Delta.y;

            if (config.invert_z)
                hwdelta.z = -Delta.z;
            else
                hwdelta.z = Delta.z;

            dx = (int)(hwdelta.x * config.steps_per_x);
            dy = (int)(hwdelta.y * config.steps_per_y);
            dz = (int)(hwdelta.z * config.steps_per_z);

            PhysicalDelta = new Vector3();

            if (!config.invert_x)
                PhysicalDelta.x = dx / config.steps_per_x;
            else
                PhysicalDelta.x = -dx / config.steps_per_x;

            if (!config.invert_y)
                PhysicalDelta.y = dy / config.steps_per_y;
            else
                PhysicalDelta.y = -dy / config.steps_per_y;

            if (!config.invert_z)
                PhysicalDelta.z = dz / config.steps_per_z;
            else
                PhysicalDelta.z = -dz / config.steps_per_z;
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
