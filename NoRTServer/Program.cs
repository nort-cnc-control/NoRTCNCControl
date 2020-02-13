using System.Net;
using System.Net.Sockets;
using Config;
using RTSender;
using ModbusSender;
using System.IO;
using System;
using Actions.Tools.SpindleTool;
using Gnu.Getopt;
using System.Json;
using Newtonsoft.Json;

namespace NoRTServer
{
    class Program
    {
        private static void PrintStream(MemoryStream output)
        {
            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        private static void Usage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("NoRTServer.exe [-h] -m machineConfig.json [-r runConfig.json] [-p port]");
            Console.WriteLine("");
            Console.WriteLine("Detailed options:");

            Console.WriteLine("");

            Console.WriteLine("-l file.log");
            Console.WriteLine("Log to file");

            Console.WriteLine("");

            Console.WriteLine("-m machineConfig.json");
            Console.WriteLine("Use config file for setting CNC size, max feedrate, etc");

            Console.WriteLine("");

            Console.WriteLine("-p port");
            Console.WriteLine("Control connection port (tcp). Default: 8888");

            Console.WriteLine("");

            Console.WriteLine("-r runConfig.json");
            Console.WriteLine("Use config file for selecting used interfaces");

            Console.WriteLine("\nFormat:\n");
            Console.WriteLine("\t{");
            Console.WriteLine("\t\t\"rt_sender\" : {\"sender\" : \"...\"},");
            Console.WriteLine("\t\t\"modbus_sender\" : {\"sender\" : \"...\"},");
            Console.WriteLine("\t\t\"spindle_driver\" : \"...\"");
            Console.WriteLine("\t}");

            Console.WriteLine("");

            Console.WriteLine("\trt_sender - realtime part connection driver");
            Console.WriteLine("\tAvailable values:");
            Console.WriteLine("\t\tEmulationRTSender");
            Console.WriteLine("\t\tPacketRTSender");

            Console.WriteLine("");

            Console.WriteLine("\tmodbus_sender - modbus driver");
            Console.WriteLine("\tAvailable values:");
            Console.WriteLine("\t\tEmulationModbusSender");
            Console.WriteLine("\t\tPacketModbusSender");

            Console.WriteLine("");

            Console.WriteLine("\tspindle_driver - variable-frequency drive (VFD)");
            Console.WriteLine("\tAvailable values:");
            Console.WriteLine("\t\tN700E");
            Console.WriteLine("\t\tNone");

            Console.WriteLine("");
            Console.WriteLine("-h");
            Console.WriteLine("Print help and exit");
        }



        class SocketStream : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotImplementedException();

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            private Socket socket;

            public SocketStream(Socket socket)
            {
                this.socket = socket;
                this.socket.Blocking = true;
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                // TODO: implement offset
                try
                {
                    return socket.Receive(buffer, count, 0);
                }
                catch
                {
                    return -1;
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new InvalidOperationException();
            }

            public override void SetLength(long value)
            {
                throw new InvalidOperationException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                try
                {
                    socket.Send(buffer, count, 0);
                }
                catch
                {
                    return;
                }
            }
        }


        static void Main(string[] args)
        {
            var opts = new Getopt("NoRTServer.exe", args, "m:p:hr:x:l:");

            string machineConfigName = "";
            string runConfigName = "";
            int controlPort = 8888;
            string proxyAddress = "127.0.0.1";
            int proxyPort = 8889;

            int arg;
            while ((arg = opts.getopt()) != -1)
            {
                switch (arg)
                {
                    case 'm':
                        {
                            machineConfigName = opts.Optarg;
                            break;
                        }
                    case 'p':
                        {
                            controlPort = int.Parse(opts.Optarg);
                            break;
                        }
                    case 'r':
                        {
                            runConfigName = opts.Optarg;
                            break;
                        }
                    case 'x':
                        {
                            proxyAddress = opts.Optarg;
                            break;
                        }
                    case 'l':
                        {
                            var file = File.Open(opts.Optarg, FileMode.Create);
                            Log.Logger.Instance.Writer = new StreamWriter(file);
                        }
                        break;
                    case 'h':
                    default:
                        Usage();
                        return;
                }
            }

            MachineParameters machineConfig;
            if (machineConfigName != "")
            {
                try
                {
                    string cfg = File.ReadAllText(machineConfigName);
                    machineConfig = JsonConvert.DeserializeObject<MachineParameters>(cfg);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Can not load config: {0}", e.ToString());
                    return;
                }
            }
            else
            {
                machineConfig = new MachineParameters
                {
                    max_acceleration = 40,
                    fastfeed = 600,
                    slowfeed = 100,
                    maxfeed = 800
                };
            }
            machineConfig.max_acceleration *= 3600; // to mm/min^2
            JsonValue runConfig;
            if (runConfigName != "")
            {
                try
                {
                    string cfg = File.ReadAllText(runConfigName);
                    runConfig = JsonValue.Parse(cfg);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Can not load config: {0}", e.ToString());
                    return;
                }
            }
            else
            {
                runConfig = new JsonObject
                {
                    ["rt_sender"] = new JsonObject {
                        ["sender"] = "EmulationRTSender",
                    },
                    ["modbus_sender"] = new JsonObject {
                        ["sender"] = "EmulationModbusSender",
                    },
                    ["spindle_driver"] = "N700E",
                };
            }

            var localAddr = IPAddress.Parse("0.0.0.0");
            TcpListener tcpServer = new TcpListener(localAddr, controlPort);
            tcpServer.Start();

            bool packetModbus = false;
            IModbusSender modbusSender = null;
            string modbus_sender = runConfig["modbus_sender"]["sender"];
            Console.WriteLine("Using {0} modbus sender", modbus_sender);
            switch (modbus_sender)
            {
                case "EmulationModbusSender":
                    {
                        modbusSender = new EmulationModbusSender(Console.Out);
                        break;
                    }
                case "PacketModbusSender":
                    {
                        string address = runConfig["modbus_sender"]["address"];
                        int port = runConfig["modbus_sender"]["port"];
                        TcpClient tcpClient = new TcpClient();
                        tcpClient.Connect(IPAddress.Parse(address), port);
                        var stream = tcpClient.GetStream();
                        var reader = new StreamReader(stream);
                        var writer = new StreamWriter(stream);
                        modbusSender = new PacketModbusSender(writer, reader);
                        break;
                    }
                default:
                    Console.WriteLine("Invalid modbus sender: {0}", modbus_sender);
                    return;
            }

            bool packetRT = false;
            IRTSender rtSender = null;
            string rt_sender = runConfig["rt_sender"]["sender"];
            Console.WriteLine("Using {0} rt sender", rt_sender);
            switch (rt_sender)
            {
                case "EmulationRTSender":
                    {
                        rtSender = new EmulationRTSender(Console.Out);
                        break;
                    }
                case "PacketRTSender":
                    {
                        string address = runConfig["rt_sender"]["address"];
                        int port = runConfig["rt_sender"]["port"];
                        TcpClient tcpClient = new TcpClient();
                        tcpClient.Connect(IPAddress.Parse(address), port);
                        var stream = tcpClient.GetStream();
                        var reader = new StreamReader(stream);
                        var writer = new StreamWriter(stream);
                        rtSender = new PacketRTSender(writer, reader);
                        break;
                    }
                default:
                    Console.WriteLine("Invalid RT sender: {0}", rt_sender);
                    return;
            }

            if (packetModbus || packetRT)
            {
                var proxy = IPAddress.Parse(proxyAddress);
                TcpClient tcpClient = new TcpClient();
                tcpClient.Connect(proxy, proxyPort);
                var stream = tcpClient.GetStream();
                var reader = new StreamReader(stream);
                var writer = new StreamWriter(stream);
                if (packetRT)
                    rtSender = new PacketRTSender(writer, reader);
                if (packetModbus)
                    modbusSender = new PacketModbusSender(writer, reader);
            }

            ISpindleToolFactory spindleCommandFactory;
            string spindle_driver = runConfig["spindle_driver"];
            switch (spindle_driver)
            {
                case "N700E":
                    spindleCommandFactory = new N700ESpindleToolFactory();
                    break;
                case "None":
                    spindleCommandFactory = new NoneSpindleToolFactory();
                    break;
                default:
                    Console.WriteLine("Invalid spindle driver: {0}", spindle_driver);
                    return;
            }
            

            bool run = true;
            do
            {

                Socket tcpClient = tcpServer.AcceptSocket();
                Stream stream = new SocketStream(tcpClient);

                var machineServer = new GCodeServer.GCodeServer(rtSender, modbusSender, spindleCommandFactory,
                                                                machineConfig, stream, stream);

                try
                {
                    run = machineServer.Run();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception: {0}", e);
                }

                machineServer.Dispose();
                stream.Close();
                tcpClient.Close();
            } while (run);
            rtSender.Dispose();
            tcpServer.Stop();
        }
    }
}
