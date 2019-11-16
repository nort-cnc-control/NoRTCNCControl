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

        private readonly AxisState axisState;
        private readonly SpindleState spindleState;

        private MessageReceiver cmdReceiver;
        private MessageSender responseSender;

        private readonly Stream commandStream;
        private readonly Stream responseStream;

        public GCodeServer(IRTSender rtSender, IModbusSender modbusSender, ISpindleToolFactory spindleToolFactory,
                           MachineParameters config, Stream commandStream, Stream responseStream)
        {
            axisState = new AxisState();
            spindleState = new SpindleState();
            this.rtSender = rtSender;
            this.modbusSender = modbusSender;
            this.spindleToolFactory = spindleToolFactory;
            this.Config = config;
            this.commandStream = commandStream;
            this.responseStream = responseStream;
            this.Machine = new GCodeMachine.GCodeMachine(this.rtSender);
            this.programBuilder = new ProgramBuilder(this.Machine, this.rtSender, this.modbusSender, this.spindleToolFactory, this.Config);
            cmdReceiver = new MessageReceiver(commandStream);
            responseSender = new MessageSender(responseStream);
        }

        #region gcode machine methods
        private void RunGcode(String[] prg)
        {
            var program = this.programBuilder.BuildProgram(prg, this.axisState, this.spindleState);
            this.Machine.LoadProgram(program);
            this.Machine.Start();
        }

        private void RunGcode(String cmd)
        {
            ActionProgram.ActionProgram program;

            try
            {
                program = programBuilder.BuildProgram(cmd, this.axisState, this.spindleState);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: {0}", e.ToString());
                return;
            }
            this.Machine.LoadProgram(program);
            this.Machine.Start();
        }
        #endregion

        public bool Run()
        {
            cmdReceiver.Run();

            String[] gcodeprogram = { };

            do
            {
                var cmd = cmdReceiver.MessageReceive();
                if (cmd == null)
                {
                    // broken connection
                    return true;
                }
                JsonValue message;
                try
                {
                    message = JsonValue.Parse(cmd);
                }
                catch
                {
                    Console.WriteLine("Cannot parse command \"{0}\"", cmd);
                    return true;
                }
                var type = message["type"];
                if (type == "command")
                {
                    var command = message["command"];
                    if (command == "exit")
                    {
                        return false;
                    }
                    else if (command == "disconnect")
                    {
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
                        var response = new JsonObject( );
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
