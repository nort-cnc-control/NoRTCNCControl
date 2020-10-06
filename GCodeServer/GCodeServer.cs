using System;
using System.Collections.Generic;
using Machine;
using Config;
using ActionProgram;
using Actions;
using Actions.Tools.SpindleTool;
using CNCState;
using RTSender;
using ModbusSender;
using ControlConnection;
using System.IO;
using System.Linq;
using GCodeMachine;
using System.Json;
using System.Threading;
using Actions.Tools;
using Log;
using System.Collections.Concurrent;
using Vector;

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

        public string Name => "gcode server";

        private ProgramBuilder programBuilder;

        private bool runFlag;

        private readonly IRTSender rtSender;
        private readonly IModbusSender modbusSender;
        private readonly ISpindleToolFactory spindleToolFactory;

        private IReadOnlyDictionary<IAction, (int, int)> starts;

        private CNCState.CNCState State => Machine.LastState;
        private AxisState.CoordinateSystem hwCoordinateSystem;

        private MessageReceiver cmdReceiver;
        private MessageSender responseSender;

        private readonly Stream commandStream;
        private readonly Stream responseStream;

        private IToolManager toolManager;
        private bool serverRun;

        private int currentProgram, currentLine;

        private ProgramSequencer sequencer;

        private BlockingCollection<JsonObject> commands;

        public GCodeServer(IRTSender rtSender,
                           IModbusSender modbusSender,
                           ISpindleToolFactory spindleToolFactory,
                           MachineParameters config,
                           Stream commandStream,
                           Stream responseStream)
        {
            this.rtSender = rtSender;
            this.modbusSender = modbusSender;
            this.spindleToolFactory = spindleToolFactory;
            this.Config = config;
            this.commandStream = commandStream;
            this.responseStream = responseStream;
            StatusMachine = new ReadStatusMachine.ReadStatusMachine(config, rtSender, Config.state_refresh_update, Config.state_refresh_timeout, Config.state_refresh_maxretry);
            StatusMachine.CurrentStatusUpdate += OnStatusUpdate;
            commands = new BlockingCollection<JsonObject>();
            Init();

            cmdReceiver = new MessageReceiver(commandStream);
            responseSender = new MessageSender(responseStream);
        }

        private void Init()
        {
            sequencer = new ProgramSequencer();
            var newState = new CNCState.CNCState();
            Machine = new GCodeMachine.GCodeMachine(this.rtSender, this, newState, Config);

            var crds = StatusMachine.ReadHardwareCoordinates();
            var sign = new Vector3(Config.SignX, Config.SignY, Config.SignZ);
            hwCoordinateSystem = new AxisState.CoordinateSystem
            {
                Sign = sign,
                Offset = new Vector3(crds.x - sign.x * State.AxisState.Position.x,
                                     crds.y - sign.y * State.AxisState.Position.y,
                                     crds.z - sign.z * State.AxisState.Position.z)
            };

            Machine.ActionStarted += Machine_ActionStarted;
            Machine.ActionFinished += Machine_ActionCompleted;
            toolManager = new ManualToolManager(this, Machine);
            programBuilder = new ProgramBuilder(Machine,
                                                this,
                                                rtSender,
                                                modbusSender,
                                                spindleToolFactory,
                                                toolManager,
                                                Config);
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

        private void OnStatusUpdate(Vector3 hw_crds, bool ex, bool ey, bool ez, bool ep)
        {
            var gl_crds = hwCoordinateSystem.ToLocal(hw_crds);
            var state = Machine.LastState;

            var loc_crds = state.AxisState.Params.CurrentCoordinateSystem.ToLocal(gl_crds);
            var crd_system = String.Format("G5{0}", 3 + state.AxisState.Params.CurrentCoordinateSystemIndex);

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

            string spindlestatus = "";
            string spindledir = "";
            switch (state.SpindleState.RotationState)
            {
                case SpindleState.SpindleRotationState.Clockwise:
                    spindledir = "CW";
                    spindlestatus = "ON";
                    break;
                case SpindleState.SpindleRotationState.CounterClockwise:
                    spindledir = "CCW";
                    spindlestatus = "ON";
                    break;
                case SpindleState.SpindleRotationState.Off:
                    spindledir = "-";
                    spindlestatus = "OFF";
                    break;
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
                ["spindel"] = new JsonObject
                {
                    ["status"] = spindlestatus,
                    ["speed"] = state.SpindleState.SpindleSpeed,
                    ["direction"] = spindledir,
                },
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
                        x = x / Config.steps_per_x,
                        y = y / Config.steps_per_y,
                        z = z / Config.steps_per_z,
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

        #region gcode machine methods
        private void LoadGcode(String[] prg)
        {
            try
            {
                sequencer = new ProgramSequencer();
                sequencer.SequenceProgram(prg);
            }
            catch (Exception e)
            {
                Logger.Instance.Error(this, "sequence", String.Format("Exception: {0}", e));
                return;
            }
            //Logger.Instance.Info(this, "compile", String.Format("Expected execution time = {0}", time));
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
                    StatusMachine.Stop();
                    Machine.Stop();
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
                                case "reset":
                                case "stop":
                                    {
                                        StatusMachine.Stop();
                                        Machine.Abort();
                                        Machine.ActionFinished -= Machine_ActionCompleted;
                                        Machine.ActionStarted -= Machine_ActionStarted;
                                        Machine.Dispose();
                                        Init();
                                        StatusMachine.Start();
                                        StatusMachine.Continue();
                                        break;
                                    }
                                case "pause":
                                    {
                                        // TODO: implement
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
                }

            } while (serverRun);
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

        public bool Run()
        {
            StatusMachine.Start();
            StatusMachine.Continue();
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
                            LoadGcode(program.ToArray());
                            break;
                        }
                    case "execute":
                        {
                            string errorMsg;
                            ActionProgram.ActionProgram program;
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
                            (program, _, _, errorMsg) = programBuilder.BuildProgram(excommand, sequencer, Machine.LastState);
                            if (program != null)
                            {
                                Machine.LoadProgram(program);
                                Machine.Start();
                                Machine.Continue();
                            }
                            else
                            {
                                CompileErrorMessageSend(errorMsg);
                            }
                            break;
                        }
                    case "start":
                        {
                            string errorMsg;

                            ActionProgram.ActionProgram program;
                            (program, _, starts, errorMsg) = programBuilder.BuildProgram(sequencer.MainProgram, sequencer, Machine.LastState);
                            if (program != null)
                            {
                                Machine.LoadProgram(program);
                                Machine.Start();
                                Machine.Continue();
                            }
                            else
                            {
                                CompileErrorMessageSend(errorMsg);
                            }
                            break;
                        }
                    case "continue":
                        {
                            Machine.Continue();
                            break;
                        }
                    case "run_finish":
                        {
                            runFlag = false;
                            break;
                        }
                }

            } while (runFlag);
            return true;
        }

        public void Dispose()
        {
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
            Machine.Dispose();
            StatusMachine.Dispose();
        }

        public void SyncCoordinates(Vector3 stateCoordinates)
        {
            var crds = StatusMachine.ReadHardwareCoordinates();
            var sign = new Vector3(Config.SignX, Config.SignY, Config.SignZ);
            hwCoordinateSystem = new AxisState.CoordinateSystem
            {
                Sign = sign,
                Offset = new Vector3(crds.x - sign.x * State.AxisState.Position.x,
                                     crds.y - sign.y * State.AxisState.Position.y,
                                     crds.z - sign.z * State.AxisState.Position.z)
            };
        }
    }
}
