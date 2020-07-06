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
        public MachineParameters Config { get; private set; }
        public GCodeMachine.GCodeMachine Machine { get; private set; }
        public ReadStatusMachine.ReadStatusMachine StatusMachine { get; private set; }

        private StateValidator stateValidator;

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
            StatusMachine = new ReadStatusMachine.ReadStatusMachine(config, rtSender, Config.state_refresh_timeout);
            StatusMachine.CurrentStatusUpdate += OnStatusUpdate;
            commands = new BlockingCollection<JsonObject>();
            Init();

            cmdReceiver = new MessageReceiver(commandStream);
            responseSender = new MessageSender(responseStream);

            stateValidator = new StateValidator();
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
            var (loc_crds, crd_system) = Machine.ConvertCoordinates(gl_crds);
            var response = new JsonObject
            {
                ["type"] = "coordinates",
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
                int line = sequencer.SubprogramStart[currentProgram] + currentLine;
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
                                case "contuinue":
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
                            ActionProgram.ActionProgram program;
                            Sequence excommand = new Sequence();
                            excommand.AddLine(new Arguments(command["program"]));
                            (program, _, starts) = programBuilder.BuildProgram(excommand, sequencer, Machine.LastState);
                            Machine.LoadProgram(program);
                            Machine.Start();
                            Machine.Continue();
                            break;
                        }
                    case "start":
                        {
                            ActionProgram.ActionProgram program;
                            (program, _, starts) = programBuilder.BuildProgram(sequencer.MainProgram, sequencer, Machine.LastState);
                            Machine.LoadProgram(program);
                            Machine.Start();
                            Machine.Continue();
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
