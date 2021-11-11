using System.Net;
using System.Net.Sockets;
using GCodeMachine;
using Config;
using RTSender;
using ModbusSender;
using System.IO;
using System;
using Actions.ModbusTool.SpindleTool;

namespace GcodeNoRTServer
{
    class Program
    {
        
        static void Main(string[] args)
        {

            var localAddr = IPAddress.Parse("0.0.0.0");
            int port = 8888;
            TcpListener tcpServer = new TcpListener(localAddr, port);
            tcpServer.Start();

            var outputStream = new MemoryStream();

            var config = new MachineParameters();
            var rtSender = new EmulationRTSender(outputStream);
            var modbusSender = new EmulationModbusSender(outputStream);
            var spindleCommandFactory = new N700ESpindleToolFactory();

            bool run = true;
            do
            {
                TcpClient tcpClient = tcpServer.AcceptTcpClient();
                NetworkStream stream = tcpClient.GetStream();
                var machineServer = new GCodeServer.GCodeServer(rtSender, modbusSender, spindleCommandFactory, config, stream, stream);
                try
                {
                    run = machineServer.Run();
                }
                catch (System.Exception e)
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
