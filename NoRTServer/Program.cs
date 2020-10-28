using System.Net;
using System.Net.Sockets;
using Config;
using RTSender;
using ModbusSender;
using System.IO;
using System;
using Actions.Tools;
using Gnu.Getopt;
using System.Json;
using Newtonsoft.Json;
using PacketSender;
using System.IO.Ports;

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

        static bool BuildSR(string proto, string addr, string port, out IPacketSender writer, out IPacketReceiver reader)
        {
            if (proto == "TCP")
            {
                TcpClient tcpClient = new TcpClient();
                tcpClient.Connect(IPAddress.Parse(addr), int.Parse(port));
                NetworkStream stream = tcpClient.GetStream();
                reader = new StreamPacketReceiver(new StreamReader(stream));
                writer = new StreamPacketSender(new StreamWriter(stream));
            }
            else if (proto == "UDP")
            {
                UdpClient udpClient = new UdpClient();
                udpClient.Connect(IPAddress.Parse(addr), int.Parse(port));
                reader = new UDPPacketReceiver(udpClient, addr, int.Parse(port));
                writer = new UDPPacketSender(udpClient);
            }
            else if (proto == "UART")
            {
                SerialPort sport = new SerialPort(port)
                {
                    StopBits = StopBits.One,
                    BaudRate = 38400,
                    Parity = Parity.None,
                    DataBits = 8
                };
                sport.Open();
                reader = new SerialPacketReceiver(sport);
                writer = new SerialPacketSender(sport);
            }
            else
            {
                writer = null;
                reader = null;
                Console.WriteLine("Unknown protocol {0}", proto);
                return false;
            }
            return true;
        }

        static void Main(string[] args)
        {
            var opts = new Getopt("NoRTServer.exe", args, "m:p:hr:x:l:");

            string machineConfigName = "";
            string runConfigName = "";
            int controlPort = 8888;

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
                    machineConfig = MachineParameters.ParseConfig(JsonValue.Parse(cfg));
                }
                catch (Exception e)
                {
                    Console.WriteLine("Can not load config: {0}", e.ToString());
                    return;
                }
            }
            else
            {
                throw new ArgumentNullException("None machine specification");
            }
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
                    }
                };
            }

            var localAddr = IPAddress.Parse("0.0.0.0");
            TcpListener tcpServer = new TcpListener(localAddr, controlPort);
            tcpServer.Start();

            bool packetRT = false;
            string addrRT = null;
            string portRT = null;
            string protoRT = null;
            IRTSender senderRT = null;

            bool packetMB = false;
            string addrMB = null;
            string portMB = null;
            string protoMB = null;
            IModbusSender senderMB = null;

            string modbus_sender = runConfig["modbus_sender"]["sender"];
            Console.WriteLine("Using {0} modbus sender", modbus_sender);
            switch (modbus_sender)
            {
                case "EmulationModbusSender":
                    {
                        senderMB = new EmulationModbusSender(Console.Out);
                        break;
                    }
                case "PacketModbusSender":
                    {
                        packetMB = true;
                        protoMB = runConfig["modbus_sender"]["protocol"];
                        if (protoMB == "TCP" || protoMB == "UDP")
                        {
                            addrMB = runConfig["modbus_sender"]["address"];
                            portMB = runConfig["modbus_sender"]["port"];
                        }
                        else if (protoMB == "UART")
                        {
                            portMB = runConfig["modbus_sender"]["port"];
                        }
                        break;
                    }
                default:
                    Console.WriteLine("Invalid modbus sender: {0}", modbus_sender);
                    return;
            }

            string rt_sender = runConfig["rt_sender"]["sender"];
            Console.WriteLine("Using {0} rt sender", rt_sender);
            switch (rt_sender)
            {
                case "EmulationRTSender":
                    {
                        senderRT = new EmulationRTSender(Console.Out);
                        break;
                    }
                case "PacketRTSender":
                    {
                        packetRT = true;
                        protoRT = runConfig["rt_sender"]["protocol"];
                        if (protoRT == "TCP" || protoRT == "UDP")
                        {
                            addrRT = runConfig["rt_sender"]["address"];
                            portRT = runConfig["rt_sender"]["port"];
                        }
                        else if (protoRT == "UART")
                        {
                            portRT = runConfig["rt_sender"]["port"];
                        }
                        break;
                    }
                default:
                    Console.WriteLine("Invalid RT sender: {0}", rt_sender);
                    return;
            }

            if (packetMB && packetRT && (protoMB == protoRT) && (portMB == portRT) && (addrMB == addrRT))
            {
                if (!BuildSR(protoMB, addrMB, portMB, out IPacketSender writer, out IPacketReceiver reader))
                    return;
                senderRT = new PacketRTSender(writer, reader);
                senderMB = new PacketModbusSender(writer, reader);

            }
            else
            {
                if (packetMB)
                {
                    if (!BuildSR(protoMB, addrMB, portMB, out IPacketSender writer, out IPacketReceiver reader))
                        return;
                    senderMB = new PacketModbusSender(writer, reader);
                }
                if (packetRT)
                {
                    if (!BuildSR(protoRT, addrRT, portRT, out IPacketSender writer, out IPacketReceiver reader))
                        return;
                    senderRT = new PacketRTSender(writer, reader);
                }
            }

            senderRT.Init();
            senderMB.Init();

            bool run = true;
            do
            {
                Socket tcpClient = tcpServer.AcceptSocket();
                Console.WriteLine("Connection from client");

                Stream stream = new SocketStream(tcpClient);

                var machineServer = new GCodeServer.GCodeServer(senderRT, senderMB, machineConfig, stream, stream);

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
                Console.WriteLine("Client disconnected");
            } while (run);
            senderRT.Dispose();
            tcpServer.Stop();
        }
    }
}
