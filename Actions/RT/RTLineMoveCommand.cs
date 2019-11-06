using System;
using Config;

namespace Actions
{
    public class RTLineMoveCommand : IRTMoveCommand
    {
        public bool CommandIsCached { get { return true; } }
        private MachineParameters config;
        public Vector3 DirStart { get; private set; }
        public Vector3 DirEnd { get; private set; }
        public Vector3 Delta { get; private set; }
        public double Length { get; private set; }
        public RTMovementOptions Options { get; private set; }

        public RTLineMoveCommand(Vector3 delta, RTMovementOptions opts, MachineParameters config)
        {
            this.config = config;
            this.Delta = delta;
            this.DirStart = this.DirEnd = Vector3.Normalize(this.Delta);
            this.Options = opts;
            Length = Delta.Length();
        }

        public RTLineMoveCommand(double dx, double dy, double dz, RTMovementOptions opts, MachineParameters config)
        {
            this.config = config;
            this.Delta = new Vector3(dx, dy, dz);
            this.DirStart = this.DirEnd = Vector3.Normalize(this.Delta);
            this.Options = opts;
            Length = Delta.Length();
        }

        private string FormatD(double x)
        {
            return x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        public String Command
        {
            get
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

                return $"G1 {Options.Command} X{FormatD(hwdelta.x)} Y{FormatD(hwdelta.y)} Z{FormatD(hwdelta.z)}";
            }
        }
    }
}
