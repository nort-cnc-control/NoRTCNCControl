﻿using System.Net;
using System.Net.Sockets;
using Config;
using RTSender;
using ModbusSender;
using System.IO;
using System;
using Actions.Mills;
using System.Json;
using Newtonsoft.Json;
using PacketSender;
using System.IO.Ports;
using GCodeServer;
using System.Collections.Generic;

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
            int controlPort = 8888;
			
			foreach (var arg in args)
			{
				System.Console.WriteLine(arg);
			}

			var optsparser = new FormatParser("NoRTServer", "p:hl:");
			var opts = optsparser.ParseArgs(args);
			if (opts == null)
			{
				System.Console.WriteLine("Can not parse arguments");
				return;
			}
            foreach (var (key, val) in opts)
            {
                switch (key)
                {
                    case 'p':
                        {
                            controlPort = int.Parse(val);
                            break;
                        }
                    case 'l':
                        {
                            var file = File.Open(val, FileMode.Create);
                            Log.Logger.Instance.Writer = new StreamWriter(file);
                        }
                        break;
                    case 'h':
                        Usage();
                        return;
                }
            }

            var localAddr = IPAddress.Parse("0.0.0.0");
            TcpListener tcpServer = new TcpListener(localAddr, controlPort);
            tcpServer.Start();

            bool run = true;
            do
            {
                Socket tcpClient = tcpServer.AcceptSocket();
                Console.WriteLine("Connection from client");

                Stream stream = new SocketStream(tcpClient);

                var machineServer = new GCodeServer.GCodeServer(stream, stream);

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

            tcpServer.Stop();
        }
    }
}
