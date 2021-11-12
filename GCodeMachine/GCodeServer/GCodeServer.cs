using System;
using System.Collections.Generic;
using Machine;
using Config;
using ActionProgram;
using Actions;
using CNCState;
using RTSender;
using ModbusSender;
using System.IO;
using System.Linq;
using GCodeMachine;
using System.Json;
using System.Threading;
using Actions.Mills;
using Log;
using System.Collections.Concurrent;
using Vector;
using ControlConnection;
using PacketSender;
using ManualFeedMachine;
using ProgramBuilder;
using ProgramBuilder.GCode;
using ProgramBuilder.Excellon;

namespace GCodeServer
{
    public class GCodeServer : IDisposable, IMessageRouter, IStateSyncManager, ILoggerSource
    {
        public class IncostistensStateException : Exception
        {
            public IncostistensStateException(string message) : base(message)
            {
            }
        }

        public MachineParameters Config { get; private set; }
        public GCodeMachine.GCodeMachine Machine { get; private set; }
        public ReadStatusMachine.ReadStatusMachine StatusMachine { get; private set; }
        public ManualFeedMachine.ManualFeedMachine ManualFeedMachine { get; private set; }

        public string Name => "gcode server";

        private ProgramBuilder.ProgramBuilder programBuilder;
		private ProgramBuilder.GCode.ProgramBuilder_GCode programBuilderGCode;
		private ProgramBuilder.Excellon.ProgramBuilder_Excellon programBuilderExcellon;

        private List<ProgramBuildingState> builderStates;
        private ProgramBuildingState mainBuilderState;

        private bool runFlag;

        private IRTSender rtSender;
        private IModbusSender modbusSender;

        private IReadOnlyDictionary<int, IDriver> tool_drivers;

        private IReadOnlyDictionary<IAction, (int, int)> starts;


        private CNCState.CNCState CurrentMachineState
        {
            get
            {
                return Machine.LastState;
            }
        }

        private AxisState.CoordinateSystem hwCoordinateSystem;

        private MessageReceiver cmdReceiver;
        private MessageSender responseSender;

        private readonly Stream commandStream;
        private readonly Stream responseStream;

        private IMillManager toolManager;
        private bool serverRun;

        private int currentProgram, currentLine;

        private ProgramSequencer sequencer;

        private BlockingCollection<JsonObject> commands;

        private ConnectionManager connectionManager;

		private ProgramSource program_source;

		string program_format;

        public GCodeServer(Stream commandStream,
                           Stream responseStream)
        {
            this.rtSender = null;
            this.modbusSender = null;
            this.Config = null;
            this.commandStream = commandStream;
            this.responseStream = responseStream;
            commands = new BlockingCollection<JsonObject>();

            cmdReceiver = new MessageReceiver(commandStream);
            responseSender = new MessageSender(responseStream);
            connectionManager = new ConnectionManager();
            builderStates = new List<ProgramBuildingState>();
        }

        private void Reset()
        {
            var newState = new CNCState.CNCState(Config);
            Machine.ConfigureState(newState);
        }

        private (bool, string) Init(JsonValue configuration)
        {
            JsonValue runConfig;
            JsonValue machineConfig;
            try
            {
                runConfig = configuration["run"];
                machineConfig = configuration["machine"];
                Config = MachineParameters.ParseConfig(machineConfig);
            }
            catch (Exception e)
            {
                var msg = String.Format("Can not parse config: {0}", e.Message);
                Logger.Instance.Error(this, "config error", msg);
                return (false, msg);
            }

            try
            {
                connectionManager.CreateConnections(runConfig["connections"]);
            }
            catch (Exception e)
            {
                var msg = String.Format("Can not connect: {0}", e.Message);
                Logger.Instance.Error(this, "connection", msg);
                return (false, msg);
            }

            rtSender = new SyncRTSender(new PacketRTSender(connectionManager.Connections["RT"].writer, connectionManager.Connections["RT"].reader));
            modbusSender = new PacketModbusSender(connectionManager.Connections["Modbus"].writer, connectionManager.Connections["Modbus"].reader);

            rtSender.Init();
            modbusSender.Init();

            StatusMachine = new ReadStatusMachine.ReadStatusMachine(Config, rtSender, Config.state_refresh_update, Config.state_refresh_timeout, Config.state_refresh_maxretry);
            StatusMachine.CurrentStatusUpdate += OnStatusUpdate;

            ManualFeedMachine = new ManualFeedMachine.ManualFeedMachine(rtSender, Config);

            sequencer = new ProgramSequencer();

            var newState = new CNCState.CNCState(Config);

            var crds = StatusMachine.ReadHardwareCoordinates();

            Machine = new GCodeMachine.GCodeMachine(rtSender, this, newState, Config);
            Machine.ActionStarted += Machine_ActionStarted;
            Machine.ActionFinished += Machine_ActionCompleted;
            Machine.ActionFailed += Machine_ActionFailed;
            Machine.ProgramStarted += Machine_ProgramStarted;
            Machine.ProgramFinished += Machine_ProgramFinished;

            SyncCoordinates(newState.AxisState.Position);

            toolManager = new ManualMillManager(this, Machine);
            tool_drivers = ConfigureToolDrivers(Config);
            programBuilder = new ProgramBuilder.ProgramBuilder(Machine,
                                                this,
                                                rtSender,
                                                modbusSender,
                                                toolManager,
                                                Config,
                                                tool_drivers);

			programBuilderGCode = new ProgramBuilder_GCode(programBuilder, Machine, toolManager, Config, rtSender, modbusSender);
			programBuilderExcellon = new ProgramBuilder_Excellon(programBuilder, Machine, toolManager, Config, rtSender, modbusSender);

            UploadConfiguration();
            StatusMachine.Start();
            StatusMachine.Continue();
            return (true, null);
        }

        private void Machine_ProgramStarted()
        {
            SendState("running");
        }

        private void Machine_ProgramFinished()
        {
            if (builderStates.Count > 0)
            {
                var index = builderStates.Count - 1;
                if (!builderStates[index].Completed)
                {
                    SendState("paused");
                }
                else
                {
                    builderStates.RemoveAt(index);
                    if (builderStates.Count == 0)
                    {
                        if (mainBuilderState.Completed)
                            SendState("init");
                        else
                            SendState("paused");
                    }
                    else
                    {
                        SendState("paused");
                    }
                }
            }
            else
            {
                if (mainBuilderState.Completed)
                    SendState("init");
                else
                    SendState("paused");
            }
        }

        public void Message(IReadOnlyDictionary<string, string> message)
        {
            var response = new JsonObject();
            foreach (var keyval in message)
            {
                response[keyval.Key] = keyval.Value;
            }
            responseSender.MessageSend(response.ToString());
        }

        private void UploadConfiguration()
        {
            var tools = new JsonObject();
            foreach (var item in Config.tools)
            {
                int id = item.Key;
                var tool = item.Value;
                string type;

                switch (tool.tooltype)
                {
                    case ToolDriverType.Spindle:
                        type = "spindle";
                        break;
                    case ToolDriverType.Binary:
                        type = "binary";
                        break;
                    case ToolDriverType.None:
                        type = "null";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                var desc = new JsonObject
                {
                    ["name"] = tool.name,
                    ["driver"] = tool.driver,
                    ["type"] = type
                };
                tools.Add(id.ToString(), desc);
            }

            var response = new JsonObject
            {
                ["type"] = "machine_config",
                ["tools"] = tools,
            };

            var resp = response.ToString();
            responseSender.MessageSend(resp);
        }

        private IReadOnlyDictionary<int, IDriver> ConfigureToolDrivers(MachineParameters config)
        {
            var drivers = new Dictionary<int, IDriver>();
            foreach (KeyValuePair<int, IToolDriver> item in config.tools)
            {
                int id = item.Key;
                IToolDriver driverDesc = item.Value;
                IDriver driver;
                Logger.Instance.Debug(this, "create tool", "Create tool " + driverDesc.name + " with driver " + driverDesc.driver);
                switch (driverDesc.driver)
                {
                    case "n700e":
                        {
                            int addr = (driverDesc as N700E_Tool).address;
                            int maxspeed = (driverDesc as N700E_Tool).maxspeed;
                            int basespeed = (driverDesc as N700E_Tool).basespeed;
                            driver = new N700E_driver(modbusSender, addr, maxspeed, basespeed);
                            break;
                        }
                    case "dummy":
                        {
                            driver = new Dummy_driver();
                            break;
                        }
                    case "gpio":
                        {
                            driver = new GPIO_driver(rtSender, (driverDesc as GPIO_Tool).gpio);
                            break;
                        }
                    case "modbus":
                        {
                            int addr = (driverDesc as RawModbus_Tool).address;
                            UInt16 reg = (driverDesc as RawModbus_Tool).register;
                            driver = new RawModbus_driver(modbusSender, addr, reg);
                            break;
                        }
                    default:
                        {
                            throw new NotSupportedException("Unsupported driver: " + driverDesc.driver + " : " + driverDesc.name);
                        }
                }
                drivers.Add(id, driver);
                Logger.Instance.Debug(this, "driver", "configuring: " + driverDesc.name + " " + driverDesc.driver);
                IAction configuration = driver.Configure();
                configuration.Run();
                configuration.Finished.WaitOne();
                Logger.Instance.Debug(this, "driver", "complete configuration: " + driverDesc.name + " " + driverDesc.driver);
            }
            return drivers;
        }

        private void OnStatusUpdate(Vector3 hw_crds, bool ex, bool ey, bool ez, bool ep)
        {
            var gl_crds = hwCoordinateSystem.ToLocal(hw_crds);
            var state = Machine.LastState;

            var loc_crds = state.AxisState.Params.CurrentCoordinateSystem.ToLocal(gl_crds);
            var crd_system = String.Format("G5{0}", 3 + state.AxisState.Params.CurrentCoordinateSystemIndex);
            var loc_state_crds = state.AxisState.Params.CurrentCoordinateSystem.ToLocal(state.AxisState.TargetPosition);

            string movecmd = "";
            switch (state.AxisState.MoveType)
            {
                case AxisState.MType.FastLine:
                    movecmd = "G0";
                    break;
                case AxisState.MType.Line:
                    movecmd = "G1";
                    break;
                case AxisState.MType.ArcCW:
                    movecmd = "G2";
                    break;
                case AxisState.MType.ArcCCW:
                    movecmd = "G3";
                    break;
            }

            var tools = new JsonObject();
            foreach (var item in state.ToolStates)
            {
                int id = item.Key;
                var toolState = item.Value;
                if (toolState is SpindleState ss)
                {
                    string dir;
                    if (ss.RotationState == SpindleState.SpindleRotationState.Clockwise)
                        dir = "CW";
                    else if (ss.RotationState == SpindleState.SpindleRotationState.CounterClockwise)
                        dir = "CCW";
                    else
                        dir = "-";
                    var msg = new JsonObject
                    {
                        ["enabled"] = ss.RotationState != SpindleState.SpindleRotationState.Off,
                        ["speed"] = ss.SpindleSpeed,
                        ["direction"] = dir,
                    };
                    tools.Add(id.ToString(), msg);
                }
                else if (toolState is BinaryState bs)
                {
                    var msg = new JsonObject
                    {
                        ["enabled"] = bs.Enabled,
                    };
                    tools.Add(id.ToString(), msg);
                }
                else if (toolState == null)
                {
                    var msg = new JsonObject
                    {
                    };
                    tools.Add(id.ToString(), msg);
                }
                else
                {
                    throw new ArgumentOutOfRangeException();
                }
            }

            var response = new JsonObject
            {
                ["type"] = "machine_state",
                ["coordinates"] = new JsonObject
                {
                    ["hardware"] = new JsonArray
                            {
                                hw_crds.x,
                                hw_crds.y,
                                hw_crds.z
                            },
                    ["global"] = new JsonArray
                            {
                                gl_crds.x,
                                gl_crds.y,
                                gl_crds.z
                            },
                    ["local"] = new JsonArray
                            {
                                loc_crds.x,
                                loc_crds.y,
                                loc_crds.z
                            },
                    ["cs"] = crd_system,
                },
                ["state"] = new JsonObject
                {
                    ["local"] = new JsonArray
                            {
                                loc_state_crds.x,
                                loc_state_crds.y,
                                loc_state_crds.z,
                            }
                },
                ["endstops"] = new JsonObject
                {
                    ["axes"] = new JsonArray
                            {
                                ex,
                                ey,
                                ez,
                            },
                    ["probe"] = ep,
                },
                ["movement"] = new JsonObject
                {
                    ["status"] = "",
                    ["feed"] = state.AxisState.Feed * 60m,
                    ["command"] = movecmd,
                },
                ["tools"] = tools,
            };
            var resp = response.ToString();
            responseSender.MessageSend(resp);
        }

        private void Machine_ActionStarted(IAction action)
        {
            if (starts == null)
                return;
            try
            {
                (currentProgram, currentLine) = starts[action];
                int line;
                if (currentLine >= 0)
                {
                    if (currentProgram > 0)
                    {
                        line = sequencer.SubprogramStart[currentProgram] + currentLine;
                    }
                    else
                    {
                        line = currentLine;
                    }
                }
                else
                {
                    line = -1;
                }
                Logger.Instance.Debug(this, "line selected", line.ToString());
                var response = new JsonObject
                {
                    ["type"] = "line",
                    ["line"] = line
                };
                responseSender.MessageSend(response.ToString());
            }
            catch
            {
                ;
            }
        }

        private void Machine_ActionCompleted(IAction action, CNCState.CNCState before, CNCState.CNCState after)
        {
            if (action is RTAction rtaction)
            {
                if (rtaction.ActionResult.ContainsKey("X") && rtaction.ActionResult.ContainsKey("Y") && rtaction.ActionResult.ContainsKey("Z"))
                {
                    int x = int.Parse(rtaction.ActionResult["X"]);
                    int y = int.Parse(rtaction.ActionResult["Y"]);
                    int z = int.Parse(rtaction.ActionResult["Z"]);

                    Vector3 hwpos = new Vector3
                    {
                        x = x / Config.X_axis.steps_per_mm,
                        y = y / Config.Y_axis.steps_per_mm,
                        z = z / Config.Z_axis.steps_per_mm,
                    };

                    Vector3 actualPos = hwCoordinateSystem.ToLocal(hwpos);
                    Vector3 expectedPos = after.AxisState.Position;

                    if (Math.Abs(actualPos.x - expectedPos.x) > (decimal)1e-6 ||
                        Math.Abs(actualPos.x - expectedPos.x) > (decimal)1e-6 ||
                        Math.Abs(actualPos.x - expectedPos.x) > (decimal)1e-6)
                    {
                        var err = String.Format("Expected position: {0}, {1}, {2}. Actual position: {3}, {4}, {5}",
                                                    expectedPos.x, expectedPos.y, expectedPos.z,
                                                    actualPos.x, actualPos.y, actualPos.z);

                        Logger.Instance.Error(this, "position error", err);
                        // TODO: stop execution
                    }
                }
            }
        }

        private void Machine_ActionFailed(IAction action, CNCState.CNCState before)
        {
            if (action is RTAction rtaction)
            {
                // Queue read position
                commands.Add(new JsonObject
                {
                    ["command"] = "read_position",
                });
            }
        }

        private void ReadActualPosition()
        {
            CNCState.CNCState before = Machine.LastState;
            var hw_crds = StatusMachine.ReadHardwareCoordinates();
            var global_crds = hwCoordinateSystem.ToLocal(hw_crds);
            CNCState.CNCState current = before.BuildCopy();
            current.AxisState.Position = global_crds;
            Machine.ConfigureState(current);
        }

        #region gcode machine methods
        private ProgramSequencer LoadGcode(String[] prg)
        {
            try
            {
                var sequencer = new ProgramSequencer();
                sequencer.SequenceProgram(prg);
				//Logger.Instance.Info(this, "compile", String.Format("Expected execution time = {0}", time));
				return sequencer;
            }
            catch (Exception e)
            {
                Logger.Instance.Error(this, "sequence", String.Format("Exception: {0}", e));
                return null;
            }	
        }
        #endregion

        private void ReceiveCmdCycle()
        {
            String[] gcodeprogram = { };
            cmdReceiver.Run();
            do
            {
                var cmd = cmdReceiver.MessageReceive();
                if (cmd == null)
                {
                    // broken connection
                    cmdReceiver.Stop();
                    if (StatusMachine != null)
                    {
                        StatusMachine.Stop();
                    }
                    if (Machine != null)
                    {
                        Machine.Stop();
                    }
                    commands.Add(new JsonObject
                    {
                        ["command"] = "run_finish",
                    });
                    break;
                }
                JsonObject message;
                try
                {
                    message = JsonValue.Parse(cmd) as JsonObject;
                }
                catch
                {
                    cmdReceiver.Stop();
                    StatusMachine.Stop();
                    Machine.Stop();
                    serverRun = false;
                    Logger.Instance.Error(this, "parse", cmd);
                    break;
                }

                string type = message["type"];
                switch (type)
                {
                    case "command":
                        {
                            string command = message["command"];
                            switch (command)
                            {
                                case "exit":
                                    {
                                        cmdReceiver.Stop();
                                        StatusMachine.Stop();
                                        Machine.Stop();
                                        serverRun = false;
                                        break;
                                    }
                                case "disconnect":
                                    {
                                        cmdReceiver.Stop();
                                        StatusMachine.Stop();
                                        Machine.Stop();
                                        serverRun = false;
                                        break;
                                    }
                                case "reboot":
                                    {
                                        Machine.Reboot();
                                        break;
                                    }
                                case "enable_steppers":
                                    {
                                        Machine.EnableSteppers(true);
                                        break;
                                    }
                                case "disable_steppers":
                                    {
                                        Machine.EnableSteppers(false);
                                        break;
                                    }
                                case "reset":
                                case "stop":
                                    {
                                        if (StatusMachine != null)
                                        {
                                            StatusMachine.Stop();
                                        }

                                        if (Machine != null)
                                        {
                                            Machine.Abort();
                                            Reset();
                                            StatusMachine.Start();
                                            StatusMachine.Continue();
                                        }
                                        break;
                                    }
                                case "pause":
                                    {
                                        // TODO: implement
                                        break;
                                    }
                                case "manual_feed":
                                    {
                                        decimal fx = message["feed"]["x"];
                                        decimal fy = message["feed"]["y"];
                                        decimal fz = message["feed"]["z"];
                                        ManualFeedMachine.SetFeed(fx, fy, fz, 0.1m);
                                        break;
                                    }
                                case "load":
                                case "start":
                                case "execute":
                                case "continue":
                                    {
                                        commands.Add(message);
                                        break;
                                    }
                                default:
                                    throw new ArgumentException(String.Format("Invalid command \"{0}\"", message.ToString()));
                            }
                            break;
                        }
                    case "configuration":
                        {
                            var (result, msg) = Init(message["configuration"]);
                            if (result == false)
                            {
                                var response = new JsonObject
                                {
                                    ["type"] = "message",
                                    ["message"] = msg,
                                    ["message_type"] = "init error",
                                };
                                responseSender.MessageSend(response.ToString());
                            }
                            break;
                        }
                    case "mode_selection":
                        {
                            string mode = message["mode"];
                            if (Machine.RunState != State.Stopped)
                            {
                                break;
                            }
                            switch (mode)
                            {
                                case "manual":
                                    {
                                        ManualFeedMachine.SetFeed(0, 0, 0, 0.1m);
                                        ManualFeedMachine.Start();
                                        break;
                                    }
                                case "gcode":
                                    {
                                        ManualFeedMachine.Stop();
                                        break;
                                    }
                            }
                            break;
                        }
                }

            } while (serverRun);
            if (cmdReceiver != null)
                cmdReceiver.Stop();
        }

        private void CompileErrorMessageSend(String msg)
        {
            Logger.Instance.Warning(this, "compile error message", msg);
            var response = new JsonObject
            {
                ["type"] = "message",
                ["message"] = msg,
                ["message_type"] = "compile error",
            };
            responseSender.MessageSend(response.ToString());
        }

        private void SendState(string state)
        {
            var message = new JsonObject
            {
                ["type"] = "state",
                ["state"] = state,
                ["message"] = "",
            };
            Logger.Instance.Debug(this, "message state", state);
            responseSender.MessageSend(message.ToString());
        }

        private ProgramBuildingState BuildAndRun_GCode(ProgramBuildingState builderState)
        {
            string errorMsg;
            // send status
            SendState("yes_sir");

            UpdateCoordinates();
            ActionProgram.ActionProgram program;
            ProgramBuildingState newBuilderState;
            (program, newBuilderState, starts, errorMsg) = programBuilderGCode.BuildProgram(Machine.LastState, builderState);
            if (program != null)
            {
                Machine.LoadProgram(program);
                Machine.Start();
                SyncCoordinates(Machine.LastState.AxisState.Position);
                Machine.Continue();
            }
            else
            {
                CompileErrorMessageSend(errorMsg);
            }
            return newBuilderState;
        }

		private void BuildAndRun_Excellon(ProgramSource source)
		{
			ActionProgram.ActionProgram program;
			IReadOnlyDictionary<IAction, int> actionLines;
			string errorMessage;
            (program, actionLines, errorMessage) = programBuilderExcellon.BuildProgram(Machine.LastState, source.Procedures[source.MainProcedureId]);

			var al = new Dictionary<IAction, (int, int)>();
			foreach (var action in actionLines.Keys)
			{
				al[action] = (0, actionLines[action]);
			}
			starts = al;

			if (program != null)
			{
				Machine.LoadProgram(program);
                Machine.Start();
                SyncCoordinates(Machine.LastState.AxisState.Position);
                Machine.Continue();
			}
		}

        public bool Run()
        {
            serverRun = true;
            var cmdThread = new Thread(new ThreadStart(ReceiveCmdCycle));
            cmdThread.Start();
            String[] gcodeprogram = { };
            runFlag = true;
            do
            {
                var command = commands.Take();
                string cmd = command["command"];
                switch (cmd)
                {
                    case "load":
                        {
                            List<string> program = new List<string>();
                            foreach (JsonPrimitive line in command["program"])
                            {
                                string str = line;
                                program.Add(str);
                            }

							if (command.ContainsKey("format"))
								program_format = command["format"];
							else
								program_format = "gcode";

							Dictionary<int, Sequence> programs;
							if (program_format == "gcode")
							{
                            	ProgramSequencer gcode_program = LoadGcode(program.ToArray());
                            	programs = gcode_program.Subprograms.ToDictionary(item => item.Key, item => item.Value);
                            	programs[0] = gcode_program.MainProgram;
                            	
							}
							else if (program_format == "excellon")
							{
								Sequence excellon_program = new Sequence();
								foreach (var line in program)
									excellon_program.AddLine(new Arguments(line));
								programs[0] = excellon_program;
							}
							program_source = new ProgramSource(programs, 0);
                            break;
                        }
                    case "execute":
                        {
                            Sequence excommand = new Sequence();
                            try
                            {
                                excommand.AddLine(new Arguments(command["program"]));
                            }
                            catch (Exception e)
                            {
                                CompileErrorMessageSend("Parse error: " + e.Message);
                                break;
                            }

                            Dictionary<int, Sequence> programs = sequencer.Subprograms.ToDictionary(item => item.Key, item => item.Value);
                            programs[0] = excommand;
                            ProgramSource source = new ProgramSource(programs, 0);

                            var executeBuilderState = programBuilderGCode.InitNewProgram(source);
                            executeBuilderState.Init(0, 0);
                            builderStates.Add(executeBuilderState);
                            BuildAndRun_GCode(executeBuilderState);
                            break;
                        }
                    case "start":
                        {
                            if (program_format == "gcode")
							{
								mainBuilderState = programBuilderGCode.InitNewProgram(program_source);

								// Remove all programs from stack
                            	builderStates.Clear();

                            	mainBuilderState.Init(0, 0);
                            	mainBuilderState = BuildAndRun_GCode(mainBuilderState);
							}
							else if (program_format == "excellon")
							{
								BuildAndRun_Excellon(program_source);
							}
                            break;
                        }
                    case "continue":
                        {
							if (program_format == "gcode")
							{
                            	if (builderStates.Count == 0)
                            	{
	                                mainBuilderState = BuildAndRun_GCode(mainBuilderState);
                            	}
                            	else
                            	{
	                                int index = builderStates.Count - 1;
                                	builderStates[index] = BuildAndRun_GCode(builderStates[index]);
                            	}
							}
							else if (program_format == "excellon")
							{
								Machine.Continue();
							}
                            break;
                        }
                    case "run_finish":
                        {
                            runFlag = false;
                            break;
                        }
                    case "read_position":
                        {
                            ReadActualPosition();
                            break;
                        }

                    default:
                        {
                            // ERROR
                            break;
                        }
                }

            } while (runFlag);
            return true;
        }

        public void Dispose()
        {
            serverRun = false;
            runFlag = false;
            if (cmdReceiver != null)
            {
                cmdReceiver.Dispose();
                cmdReceiver = null;
            }
            if (responseSender != null)
            {
                responseSender.Dispose();
                responseSender = null;
            }
            if (Machine != null)
            {
                Machine.ActionStarted -= Machine_ActionStarted;
                Machine.ActionFinished -= Machine_ActionCompleted;
                Machine.ActionFailed -= Machine_ActionFailed;
                Machine.ProgramStarted -= Machine_ProgramStarted;
                Machine.ProgramFinished -= Machine_ProgramFinished;

                Machine.Dispose();
                Machine = null;
            }
            if (StatusMachine != null)
            {
                StatusMachine.Dispose();
                StatusMachine = null;
            }

            if (rtSender != null)
            {
                rtSender.Dispose();
                rtSender = null;
            }

            if (modbusSender != null)
            {
                modbusSender.Dispose();
                modbusSender = null;
            }

            connectionManager.Disconnect();
        }

        public void SyncCoordinates(Vector3 stateCoordinates)
        {
            var crds = StatusMachine.ReadHardwareCoordinates();
            var sign = new Vector3(Config.X_axis.sign, Config.Y_axis.sign, Config.Z_axis.sign);
            hwCoordinateSystem = new AxisState.CoordinateSystem
            {
                Sign = sign,
                Offset = new Vector3(crds.x - sign.x * CurrentMachineState.AxisState.Position.x,
                                     crds.y - sign.y * CurrentMachineState.AxisState.Position.y,
                                     crds.z - sign.z * CurrentMachineState.AxisState.Position.z)
            };
        }

        private void UpdateCoordinates()
        {
            var hw_crds = StatusMachine.ReadHardwareCoordinates();
            var gl_crds = hwCoordinateSystem.ToLocal(hw_crds);
            var state = Machine.LastState;
            state.AxisState.Position = gl_crds;
            state.AxisState.TargetPosition = gl_crds;
            Machine.ConfigureState(state);
        }
    }
}
