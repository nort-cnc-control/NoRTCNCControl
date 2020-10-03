using System;
using GCodeServer;
using System.IO;
using RTSender;
using ModbusSender;
using ControlConnection;
using Config;
using Actions.Tools.SpindleTool;

namespace GCodeServer.Tests
{
    class MainClass
    {
        private static void PrintStream(MemoryStream output)
        {
            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        public static void TestExit()
        {
            var output = new MemoryStream();
            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed_x = 800,
                maxfeed_y = 800,
                maxfeed_z = 800,
            };

            var rtSender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new N700ESpindleToolFactory();
            Console.WriteLine("Begin server test 1");

            var stream = new MemoryStream();
            var builder = new MessageSender(stream);
            builder.MessageSend("{\"command\":\"exit\", \"args\":{} }");

            Console.WriteLine("Creating server");
            var server = new GCodeServer(rtSender, modbusSender, spindleCmdFactory, config, stream, stream);
            try
            {
                server.Run();
            }
            catch (System.Exception e)
            {
                Console.WriteLine("Exception: {0}", e.ToString());
                server.Dispose();
                rtSender.Dispose();
                return;
            }

            rtSender.Dispose();
            server.Dispose();
            PrintStream(output);
        }

        public static void TestG0()
        {
            var program = "G0 X10 F100";
            var loadCommand = "{\"command\":\"load\", \"args\":{\"program\":\"" + program + "\" } }";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed_x = 800,
                maxfeed_y = 800,
                maxfeed_z = 800,
            };
            var stream = new MemoryStream();
            var output = new MemoryStream();
            var sender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new N700ESpindleToolFactory();

            Console.WriteLine("Begin server test 2");

            var builder = new MessageSender(stream);
            builder.MessageSend(loadCommand);
            builder.MessageSend("{\"command\":\"start\", \"args\":{} }");
            builder.MessageSend("{\"command\":\"exit\", \"args\":{} }");
            stream.Seek(0, SeekOrigin.Begin);

            Console.WriteLine("Creating server");
            var server = new GCodeServer(sender, modbusSender, spindleCmdFactory, config, stream, stream);
            try
            {
                server.Run();
            }
            catch (System.Exception)
            {
                server.Dispose();
                stream.Dispose();
                sender.Dispose();
                return;
            }

            sender.Dispose();
            server.Dispose();
            stream.Dispose();
            PrintStream(output);
        }

        public static void TestG0G0()
        {
            var program = "G0 X10 F100\\nG0 X20";
            var loadCommand = "{\"command\":\"load\", \"args\":{\"program\":\"" + program + "\" } }";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed_x = 800,
                maxfeed_y = 800,
                maxfeed_z = 800,
            };
            var stream = new MemoryStream();
            var output = new MemoryStream();
            var sender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new N700ESpindleToolFactory();

            Console.WriteLine("Begin server test 3");

            var builder = new MessageSender(stream);
            builder.MessageSend(loadCommand);
            builder.MessageSend("{\"command\":\"start\", \"args\":{} }");
            builder.MessageSend("{\"command\":\"exit\", \"args\":{} }");
            stream.Seek(0, SeekOrigin.Begin);

            Console.WriteLine("Creating server");
            var server = new GCodeServer(sender, modbusSender, spindleCmdFactory, config, stream, stream);
            try
            {
                server.Run();
            }
            catch (System.Exception)
            {
                server.Dispose();
                stream.Dispose();
                sender.Dispose();
                return;
            }

            sender.Dispose();
            server.Dispose();
            stream.Dispose();
            PrintStream(output);
        }

        public static void Main(string[] args)
        {
            TestG0G0();
        }
    }
}
