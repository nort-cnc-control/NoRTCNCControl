using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ControlConnection;
using System.Net;
using System.Net.Sockets;

namespace NoRTServer.Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            var program = "G0 X10 F100\\nG0 X20";
            var loadCommand = "{\"command\":\"load\", \"args\":{\"program\":\"" + program + "\" } }";
            string[] commands = {
                loadCommand,
                "{\"command\":\"start\", \"args\":{} }",
                "{\"command\":\"exit\", \"args\":{} }",
            };

            var addr = IPAddress.Parse("127.0.0.1");
            int port = 8888;
            TcpClient tcpClient = new TcpClient();
            tcpClient.Connect(addr, port);

            var stream = tcpClient.GetStream();
            var builder = new MessageSender(stream, (s) => true);
            foreach (var cmd in commands)
                builder.MessageSend(cmd);

            tcpClient.Close();
        }
    }
}
