using System;
using System.Collections.Generic;
using Machine;
using Config;
using ActionProgram;
using Actions;
using Actions.ModbusTool.SpindleTool;
using CNCState;
using RTSender;
using ModbusSender;
using ControlConnection;
using System.IO;
using System.Linq;
using GCodeMachine;
using Newtonsoft.Json;

namespace GCodeServer
{
    internal struct ServerCommand
    {
        public String command {get;set;}
        public Dictionary<String, String> args {get;set;}
    }

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
            var program = this.programBuilder.BuildProgram(cmd, this.axisState, this.spindleState);
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
                ServerCommand command;
                try
                {

                    command = JsonConvert.DeserializeObject<ServerCommand>(cmd);
                }
                catch
                {
                    Console.WriteLine("Cannot parse command \"{0}\"", cmd);
                    return true;
                }

                Console.WriteLine("Command = {0}", command.command);
                switch (command.command)
                {
                    case "exit":
                    {
                        return false;
                    }
                    case "disconnect":
                    {
                        return true;
                    }
                    case "reboot":
                    {
                        Machine.Reboot();
                        break;
                    }
                    case "reset":
                    {
                        Machine.Abort();
                        break;
                    }
                    case "start":
                    {
                        RunGcode(gcodeprogram);
                        break;
                    }
                    case "stop":
                    {
                        Machine.Stop();
                        break;
                    }
                    case "pause":
                    {
                        // TODO: implement
                        break;
                    }
                    case "home":
                    {
                        RunGcode("G28");
                        break;
                    }
                    case "zprobe":
                    {
                        RunGcode("G30");
                        break;
                    }
                    case "load":
                    {
                        String prg = command.args["program"];
                        gcodeprogram = prg.Split('\n');
                        break;
                    }
                    case "execute":
                    {
                        RunGcode(command.args["command"]);
                        break;
                    }
                    default:
                    {
                        throw new ArgumentException(String.Format("Invalid command \"{0}\"", command.command));
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
