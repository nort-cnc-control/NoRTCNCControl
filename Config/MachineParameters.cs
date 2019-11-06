using System;
using Newtonsoft.Json;

namespace Config
{
    public class MachineParameters
    {
        public double maxfeed { get; set; }
        public double fastfeed { get; set; }
        public double slowfeed { get; set; }

        public double size_x { get; set; }
        public double step_back_x { get; set; }
        public bool invert_x { get; set; }

        public double size_y { get; set; }
        public double step_back_y { get; set; }
        public bool invert_y { get; set; }

        public double size_z { get; set; }
        public double step_back_z { get; set; }
        public bool invert_z { get; set; }

        public double max_movement_leap { get; set; }
        public double max_acceleration { get; set; }

        public static MachineParameters LoadConfig(string config)
        {
            try
            {
                var cfg = JsonConvert.DeserializeObject<MachineParameters>(config);
                cfg.max_acceleration *= 3600; // convert to mm / min^2
                return cfg;
            }
            catch
            {
                return null;
            }
        }
    }
}
