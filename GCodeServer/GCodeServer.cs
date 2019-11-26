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

namespace GCodeServer
{

    public class GCodeServer : IDisposable, IMessageRouter
    {
        public MachineParameters Config { get; private set; }
        public GCodeMachine.GCodeMachine Machine { get; private set; }
        public ReadStatusMachine.ReadStatusMachine StatusMachine { get; private set; }
        private readonly ProgramBuilder programBuilder;

        private readonly IRTSender rtSender;
        private readonly IModbusSender modbusSender;
        private readonly ISpindleToolFactory spindleToolFactory;

        private IReadOnlyDictionary<IAction, int> starts;

        private readonly CNCState.CNCState state;

        private MessageReceiver cmdReceiver;
        private MessageSender responseSender;

        private readonly Stream commandStream;
        private readonly Stream responseStream;

        private readonly IToolManager toolManager;

        public GCodeServer(IRTSender rtSender,
                           IModbusSender modbusSender,
                           ISpindleToolFactory spindleToolFactory,
                           MachineParameters config,
                           Stream commandStream,
                           Stream responseStream)
        {
            state = new CNCState.CNCState(new AxisState(), new SpindleState());
            this.rtSender = rtSender;
            this.modbusSender = modbusSender;
            this.spindleToolFactory = spindleToolFactory;
            this.Config = config;
            this.commandStream = commandStream;
            this.responseStream = responseStream;
            StatusMachine = new ReadStatusMachine.ReadStatusMachine(rtSender);
            StatusMachine.CurrentStatusUpdate += OnStatusUpdate;
            Machine = new GCodeMachine.GCodeMachine(this.rtSender, this, state, Config, StatusMachine.ReadHardwareCoordinates());
            Machine.ActionStarted += Machine_ActionStarted;

            toolManager = new ManualToolManager(this, Machine);

            programBuilder = new ProgramBuilder(this.Machine,
                                                this.rtSender,
                                                this.modbusSender,
                                                this.spindleToolFactory,
                                                toolManager,
                                                this.Config);
            cmdReceiver = new MessageReceiver(commandStream);
            responseSender = new MessageSender(responseStream);
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
            var (gl_crds, loc_crds, crd_system) = Machine.ConvertCoordinates(hw_crds);
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
                int index = starts[action];

                var response = new JsonObject
                {
                    ["type"] = "line",
                    ["line"] = index
                };
                responseSender.MessageSend(response.ToString());
            }
            catch
            {
                ;
            }
        }


        #region gcode machine methods
        private void RunGcode(String[] prg)
        {
            var (program, starts) = this.programBuilder.BuildProgram(prg, state);
            this.starts = starts;
            Machine.LoadProgram(program);
            Machine.Start();
            Machine.LastState = state.BuildCopy();
        }

        private void RunGcode(String cmd)
        {
            ActionProgram.ActionProgram program;
            try
            {
                (program, starts) = programBuilder.BuildProgram(cmd, state);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: {0}", e);
                return;
            }
            this.Machine.LoadProgram(program);
            this.Machine.Start();
            Machine.LastState = state.BuildCopy();
        }
        #endregion

        public bool Run()
        {
            StatusMachine.Start();
            cmdReceiver.Run();
            String[] gcodeprogram = { };
            do
            {
                var cmd = cmdReceiver.MessageReceive();
                if (cmd == null)
                {
                    // broken connection
                    cmdReceiver.Stop();
                    StatusMachine.Stop();
                    Machine.Stop();
                    return true;
                }
                JsonValue message;
                try
                {
                    message = JsonValue.Parse(cmd);
                }
                catch
                {
                    cmdReceiver.Stop();
                    StatusMachine.Stop();
                    Machine.Stop();
                    Console.WriteLine("Cannot parse command \"{0}\"", cmd);
                    return true;
                }
                var type = message["type"];
                if (type == "command")
                {
                    var command = message["command"];
                    if (command == "exit")
                    {
                        cmdReceiver.Stop();
                        StatusMachine.Stop();
                        Machine.Stop();
                        return false;
                    }
                    else if (command == "disconnect")
                    {
                        cmdReceiver.Stop();
                        StatusMachine.Stop();
                        Machine.Stop();
                        return true;
                    }
                    else if (command == "reboot")
                    {
                        Machine.Reboot();
                    }
                    else if (command == "reset")
                    {
                        StatusMachine.Abort();
                        Machine.Abort();
                        StatusMachine.Start();
                    }
                    else if (command == "start")
                    {
                        RunGcode(gcodeprogram);
                    }
                    else if (command == "continue")
                    {
                        Machine.Continue();
                        Machine.LastState = state.BuildCopy();
                    }
                    else if (command == "stop")
                    {
                        Machine.Stop();
                    }
                    else if (command == "pause")
                    {
                        //TODO
                    }
                    else if (command == "load")
                    {

                        List<string> program = new List<string>();
                        foreach (JsonPrimitive line in message["program"])
                        {
                            string str = line;
                            program.Add(str);
                        }
                        gcodeprogram = program.ToArray();
                        var response = new JsonObject
                        {
                            ["type"] = "loadlines",
                            ["lines"] = message["program"]
                        };
                        responseSender.MessageSend(response.ToString());
                    }
                    else if (command == "execute")
                    {
                        string program = message["program"];
                        RunGcode(program);
                    }
                    else
                    {
                        throw new ArgumentException(String.Format("Invalid command \"{0}\"", message.ToString()));
                    }
                }

            } while (true);
        }

        public void Dispose()
        {
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
    }
}
