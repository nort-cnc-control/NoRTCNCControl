using System;
using System.Collections.Generic;
using System.Json;

namespace Config
{
    public interface IToolDriver
    { 
        string driver { get; }
        string name { get; }
    }

    public class RawModbus_Tool : IToolDriver
    {
        public enum RegisterType
        { 
            Boolean,
        }

        public string driver => "modbus";
        public int address { get; set; }
        public UInt16 register { get; set; }
        public RegisterType type { get; set; }
        public string name { get; set; }

        static public RawModbus_Tool ParseConfig(JsonValue config)
        {
            RawModbus_Tool tool = new RawModbus_Tool
            {
                address = config["address"],
                register = (UInt16)((int)config["register"]),
                name = config["name"],
            };
            string type = config["type"];
            switch (type)
            {
                case "boolean":
                    {
                        tool.type = RegisterType.Boolean;
                        break;
                    }
                default:
                    throw new NotSupportedException("Unknown type: " + type);
            }
            return tool;
        }
    }

    public class N700E_Tool : IToolDriver
    {
        public string driver => "n700e";
        public int address { get; set; }
        public int maxspeed { get; set; }
        public int basespeed { get; set; }
        public string name { get; set; }

        static public N700E_Tool ParseConfig(JsonValue config)
        {
            N700E_Tool tool = new N700E_Tool
            {
                address = config["address"],
                maxspeed = config["maxspeed"],
                basespeed = config["basespeed"],
                name = config["name"],
            };
            return tool;
        }
    }

    public class GPIO_Tool : IToolDriver
    {
        public string driver => "gpio";
        public int gpio { get; set; }
        public string name { get; set; }

        static public GPIO_Tool ParseConfig(JsonValue config)
        {
            GPIO_Tool tool = new GPIO_Tool
            {
                gpio = config["gpio"],
                name = config["name"],
            };
            return tool;
        }
    }

    public class Dummy_Tool : IToolDriver
    {
        public string driver => "dummy";
        public string name { get; set; }

        static public Dummy_Tool ParseConfig(JsonValue config)
        {
            Dummy_Tool tool = new Dummy_Tool
            {
                name = config["name"],
            };
            return tool;
        }
    }

    public class Axis
    {
        public decimal maxfeed { get; set; }
        public decimal size { get; set; }
        public decimal step_back { get; set; }
        public bool invert { get; set; }
        public decimal steps_per_mm { get; set; }
        public decimal max_acceleration { get; set; }
        public int sign => invert ? -1 : 1;

        static public Axis ParseConfig(JsonValue config)
        {
            Axis axis = new Axis
            {
                maxfeed = config["maxfeed"],
                size = config["size"],
                step_back = config["step_back"],
                invert = config["invert"],
                steps_per_mm = config["steps_per_mm"],
                max_acceleration = config["max_acceleration"],
            };
            return axis;
        }
    }

    public class MachineParameters
    {
        public MachineParameters()
        {
            X_axis = new Axis();
            Y_axis = new Axis();
            Z_axis = new Axis();
        }

        public Axis X_axis { get; private set; }
        public Axis Y_axis { get; private set; }
        public Axis Z_axis { get; private set; }

        public Dictionary<int, IToolDriver> tools;
        public int deftool_id;
        public IToolDriver deftool;

        public decimal fastfeed { get; set; }
        public decimal slowfeed { get; set; }

        public decimal max_movement_leap { get; set; }
        public decimal max_acceleration => Math.Min(Math.Min(X_axis.max_acceleration, Y_axis.max_acceleration), Z_axis.max_acceleration);

        public int state_refresh_update { get; set; }
        public int state_refresh_timeout { get; set; }
        public int state_refresh_maxretry { get; set; }

        public static MachineParameters ParseConfig(JsonValue config)
        {
            MachineParameters machineConfig = new MachineParameters
            {
                X_axis = Axis.ParseConfig(config["axises"]["x"]),
                Y_axis = Axis.ParseConfig(config["axises"]["y"]),
                Z_axis = Axis.ParseConfig(config["axises"]["z"]),
                fastfeed = config["movement"]["fastfeed"],
                slowfeed = config["movement"]["slowfeed"],
                max_movement_leap = config["movement"]["max_movement_leap"],
                tools = new Dictionary<int, IToolDriver>(),
            };
            foreach (KeyValuePair<string, JsonValue> tool in config["tools"])
            {
                string ids = tool.Key;
                JsonValue val = tool.Value;
                if (ids == "default")
                {
                    machineConfig.deftool_id = val;
                }
                else
                {
                    int id = int.Parse(ids);
                    string driver = val["driver"];
                    switch (driver)
                    {
                        case "n700e":
                            {
                                machineConfig.tools.Add(id, N700E_Tool.ParseConfig(val));
                                break;
                            }
                        case "modbus":
                            {
                                machineConfig.tools.Add(id, RawModbus_Tool.ParseConfig(val));
                                break;
                            }
                        case "gpio":
                            {
                                machineConfig.tools.Add(id, GPIO_Tool.ParseConfig(val));
                                break;
                            }
                        case "dummy":
                            {
                                machineConfig.tools.Add(id, Dummy_Tool.ParseConfig(val));
                                break;
                            }
                        default:
                            {
                                throw new NotSupportedException("Unknown driver: " + driver);
                            }
                    }
                }
            }
            if (machineConfig.deftool_id == -1)
                machineConfig.deftool = null;
            else
                machineConfig.deftool = machineConfig.tools[machineConfig.deftool_id];

            machineConfig.state_refresh_timeout = config["control"]["state_refresh_timeout"];
            machineConfig.state_refresh_update = config["control"]["state_refresh_update"];
            machineConfig.state_refresh_maxretry = config["control"]["state_refresh_maxretry"];

            return machineConfig;
        }
    }
}
