using System.Net;
using System.Net.Sockets;
using Config;
using RTSender;
using ModbusSender;
using System.IO;
using System;
using Actions.Tools.SpindleTool;
using Newtonsoft.Json;
using Gnu.Getopt;
using System.Threading;

namespace NoRTServer
{
    class Program
    {
        private class RunConfig
        {
            public string rt_sender { get; set; }
            public string modbus_sender { get; set; }
            public string spindle_driver { get; set; }
        }

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
            Console.WriteLine("\t\t\"rt_sender\" : \"...\",");
            Console.WriteLine("\t\t\"modbus_sender\" : \"...\",");
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
            var opts = new Getopt("NoRTServer.exe", args, "m:p:hr:");

            string machineConfigName = "";
            string runConfigName = "";
            int controlPort = 8888;
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
            RunConfig runConfig;
            if (runConfigName != "")
            {
                try
                {
                    string cfg = File.ReadAllText(runConfigName);
                    runConfig = JsonConvert.DeserializeObject<RunConfig>(cfg);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Can not load config: {0}", e.ToString());
                    return;
                }
            }
            else
            {
                runConfig = new RunConfig
                {
                    rt_sender = "EmulationRTSender",
                    modbus_sender = "EmulationModbusSender",
                    spindle_driver = "N700E",
                };
            }

            var localAddr = IPAddress.Parse("0.0.0.0");
            TcpListener tcpServer = new TcpListener(localAddr, controlPort);
            tcpServer.Start();

            bool packetModbus = false;
            IModbusSender modbusSender = null;
            switch (runConfig.modbus_sender)
            {
                case "EmulationModbusSender":
                    modbusSender = new EmulationModbusSender(Console.Out);
                    break;
                case "PacketModbusSender":
                    packetModbus = true;
                    break;
                default:
                    Console.WriteLine("Invalid modbus sender: {0}", runConfig.modbus_sender);
                    return;
            }

            bool packetRT = false;
            IRTSender rtSender = null;
            switch (runConfig.rt_sender)
            {
                case "EmulationRTSender":
                    rtSender = new EmulationRTSender(Console.Out);
                    break;
                case "PacketRTSender":
                    packetRT = true;
                    break;
                default:
                    Console.WriteLine("Invalid RT sender: {0}", runConfig.rt_sender);
                    return;
            }

            if (packetModbus || packetRT)
            {
                var proxy = IPAddress.Parse("127.0.0.1");
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
            switch (runConfig.spindle_driver)
            {
                case "N700E":
                    spindleCommandFactory = new N700ESpindleToolFactory();
                    break;
                case "None":
                    spindleCommandFactory = new NoneSpindleToolFactory();
                    break;
                default:
                    Console.WriteLine("Invalid spindle driver: {0}", runConfig.spindle_driver);
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
