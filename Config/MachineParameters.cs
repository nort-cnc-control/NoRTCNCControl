using System;
using Newtonsoft.Json;

namespace Config
{
    public class MachineParameters
    {
        public decimal maxfeed_x { get; set; }
        public decimal maxfeed_y { get; set; }
        public decimal maxfeed_z { get; set; }

        public decimal fastfeed { get; set; }
        public decimal slowfeed { get; set; }

        public decimal size_x { get; set; }
        public decimal step_back_x { get; set; }
        public bool invert_x { get; set; }

        public decimal size_y { get; set; }
        public decimal step_back_y { get; set; }
        public bool invert_y { get; set; }

        public decimal size_z { get; set; }
        public decimal step_back_z { get; set; }
        public bool invert_z { get; set; }

        public decimal steps_per_x { get; set; }
        public decimal steps_per_y { get; set; }
        public decimal steps_per_z { get; set; }

        [JsonIgnore]
        public int SignX => invert_x ? -1 : 1;
        [JsonIgnore]
        public int SignY => invert_y ? -1 : 1;
        [JsonIgnore]
        public int SignZ => invert_z ? -1 : 1;

        public decimal max_movement_leap { get; set; }
        public decimal max_acceleration { get; set; }

        public int state_refresh_update { get; set; }
        public int state_refresh_timeout { get; set; }
        public int state_refresh_maxretry { get; set; }

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
