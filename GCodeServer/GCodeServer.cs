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

namespace GCodeServer
{

    public class GCodeServer : IDisposable
    {
        public MachineParameters Config { get; private set; }
        public GCodeMachine.GCodeMachine Machine { get; private set; }

        private readonly ProgramBuilder programBuilder;

        private readonly IRTSender rtSender;
        private readonly IModbusSender modbusSender;
        private readonly ISpindleToolFactory spindleToolFactory;

        private readonly CNCState.CNCState state;

        private MessageReceiver cmdReceiver;
        private MessageSender responseSender;

        private readonly Stream commandStream;
        private readonly Stream responseStream;

        public GCodeServer(IRTSender rtSender, IModbusSender modbusSender, ISpindleToolFactory spindleToolFactory,
                           MachineParameters config, Stream commandStream, Stream responseStream)
        {
            state = new CNCState.CNCState(new AxisState(), new SpindleState());
            this.rtSender = rtSender;
            this.modbusSender = modbusSender;
            this.spindleToolFactory = spindleToolFactory;
            this.Config = config;
            this.commandStream = commandStream;
            this.responseStream = responseStream;
            this.Machine = new GCodeMachine.GCodeMachine(this.rtSender, state, Config);
            this.programBuilder = new ProgramBuilder(this.Machine, this.rtSender, this.modbusSender, this.spindleToolFactory, this.Config);
            cmdReceiver = new MessageReceiver(commandStream);
            responseSender = new MessageSender(responseStream);
        }

        #region gcode machine methods
        private void RunGcode(String[] prg)
        {
            var program = this.programBuilder.BuildProgram(prg, state);
            Machine.LoadProgram(program);
            Machine.Start();
            Machine.LastState = state.BuildCopy();
        }

        private void RunGcode(String cmd)
        {
            ActionProgram.ActionProgram program;

            try
            {
                program = programBuilder.BuildProgram(cmd, state);
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
            cmdReceiver.Run();
            bool run = true;
            String[] gcodeprogram = { };

            void AskPosition()
            {
                while (run)
                {
                    /*
                    {
                        "type":"coordinates",
                        "hardware" : [hw["x"], hw["y"], hw["z"]],
                        "global" : [glob["x"], glob["y"], glob["z"]],
                        "local" : [loc["x"], loc["y"], loc["z"]],
                        "cs" : cs,
                    }
                    */
                    Thread.Sleep(100);
                    try
                    {
                        var (hw_crds, gl_crds, loc_crds, crd_system) = Machine.ReadCurrentCoordinates();

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
                    catch
                    {
                        ;
                    }
                }
                Console.WriteLine("End ask coordinate");
            }

            Thread askPosThread = new Thread(new ThreadStart(AskPosition));
            askPosThread.Start();

            do
            {
                var cmd = cmdReceiver.MessageReceive();
                if (cmd == null)
                {
                    // broken connection
                    run = false;
                    askPosThread.Join();
                    return true;
                }
                JsonValue message;
                try
                {
                    message = JsonValue.Parse(cmd);
                }
                catch
                {
                    run = false;
                    askPosThread.Join();
                    Console.WriteLine("Cannot parse command \"{0}\"", cmd);
                    return true;
                }
                var type = message["type"];
                if (type == "command")
                {
                    var command = message["command"];
                    if (command == "exit")
                    {
                        run = false;
                        askPosThread.Join();
                        return false;
                    }
                    else if (command == "disconnect")
                    {
                        run = false;
                        askPosThread.Join();
                        return true;
                    }
                    else if (command == "reboot")
                    {
                        Machine.Reboot();
                    }
                    else if (command == "reset")
                    {
                        Machine.Abort();
                    }
                    else if (command == "start")
                    {
                        RunGcode(gcodeprogram);
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
                        var response = new JsonObject();
                        List<string> program = new List<string>();
                        foreach (JsonValue line in message["program"])
                        {
                            var str = line.ToString();
                            program.Add(str);
                        }
                        gcodeprogram = program.ToArray();
                        response["type"] = "loadlines";
                        response["lines"] = message["program"];
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

        public Vector3 ReadCurrentCoordinates()
        {
            RTAction action = new RTAction(rtSender, new RTGetPositionCommand());
            // action.ReadyToRun.WaitOne();
            action.Run();
            action.Finished.WaitOne(1000);
            return new Vector3(double.Parse(action.ActionResult["X"]),
                               double.Parse(action.ActionResult["Y"]),
                               double.Parse(action.ActionResult["Z"]));
        }

        public (bool ex, bool ey, bool ez, bool ep) ReadCurrentEndstops()
        {
            RTAction action = new RTAction(rtSender, new RTGetEndstopsCommand());
            // action.ReadyToRun.WaitOne();
            action.Run();
            action.Finished.WaitOne();
            return (action.ActionResult["EX"] == "1",
                    action.ActionResult["EY"] == "1",
                    action.ActionResult["EZ"] == "1",
                    action.ActionResult["EP"] == "1");
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
        }
    }
}
