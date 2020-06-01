using System;
using GCodeMachine;
using RTSender;
using ModbusSender;
using Config;
using CNCState;
using System.IO;
using Actions;
using Actions.Tools;
using Machine;
using System.Collections.Generic;

namespace GCodeMachine.Tests
{
    public class DummyToolManager : IToolManager
    {
        public void SelectTool(int toolId)
        {
            Console.WriteLine("Select tool {0}", toolId);
        }
    }

    public class DummyMessageRouter : IMessageRouter
    {
        public void Message(IReadOnlyDictionary<string, string> message)
        {

        }
    }

    public class Test
    {
        private static void PrintStream(MemoryStream output)
        {
            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        public static void TestCase1()
        {
            var programGcode = "G0 X10 F100\nG0 X20\n";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();
            var spindleState = new SpindleState();

            var sender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(sender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, sender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            Console.WriteLine("BUILDING");
            var (program,_,_, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            { }

            machine.Dispose();
            sender.Dispose();
            PrintStream(output);
        }

        public static void TestCase2()
        {
            var programGcode = "G0 X10 F100\nG0 Y10\n";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();

            var sender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(sender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, sender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            Console.WriteLine("BUILDING");
            var (program, _, _, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            { }

            machine.Dispose();
            sender.Dispose();
        }

        public static void TestCase3()
        {
            var programGcode = "G0 X10 F100\nG0 X20Y10\n";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800,
                max_movement_leap = 40
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();

            var sender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(sender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, sender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            var (program, _, _, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            { }

            machine.Dispose();
            sender.Dispose();

            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        public static void TestCase4()
        {
            var programGcode = "G1 X10 F100\nG1 X20Y10\n";

            var config = new MachineParameters
            {
                max_acceleration        = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800,
                max_movement_leap = 40
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();

            var sender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(sender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, sender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            var (program, _, _, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            { }

            machine.Dispose();
            sender.Dispose();

            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        public static void TestCase5()
        {
            var programGcode = "G2 X10 R5\n";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800,
                max_movement_leap = 40,
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();

            var sender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(sender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, sender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            var (program, _, _, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            { }

            machine.Dispose();
            sender.Dispose();

            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        public static void TestCase6()
        {
            var programGcode = "G2 Y10X10 R10\n";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800,
                max_movement_leap = 40
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();

            var sender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(sender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, sender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            var (program, _, _, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            { }

            machine.Dispose();
            sender.Dispose();

            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        public static void TestCase7()
        {
            var programGcode = "G2 X10Y10 I10J0\n";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800,
                max_movement_leap = 40
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();

            var rtSender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(rtSender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, rtSender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            var (program,_, _, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            { }

            machine.Dispose();
            rtSender.Dispose();

            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        public static void TestCase8()
        {
            var programGcode = "G2 Y10X10 R10 F100\nG2 Y0X20 R10\n";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800,
                max_movement_leap = 40
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();

            var rtSender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(rtSender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, rtSender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            var (program,_, _, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            { }

            machine.Dispose();
            rtSender.Dispose();

            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        public static void TestCase9()
        {
            var programGcode = "M3 S12000\nG2 Y10X10 R10 F100\nG2 Y0X20 R10\nM5\n";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800,
                max_movement_leap = 40
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();

            var rtSender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(rtSender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, rtSender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            var (program,_, _, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            { }

            machine.Dispose();
            rtSender.Dispose();

            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }

        public static void TestCase10()
        {
            var programGcode = "G92 Z1.5\nZ10\nG90\nG98\nG81 X10 Y10 Z0 R3\nM2\n";

            var config = new MachineParameters
            {
                max_acceleration = 40 * 60 * 60,
                fastfeed = 600,
                slowfeed = 100,
                maxfeed = 800,
                max_movement_leap = 40
            };

            var output = new MemoryStream();
            var state = new CNCState.CNCState();

            var rtSender = new EmulationRTSender(Console.Out);
            var modbusSender = new EmulationModbusSender(Console.Out);
            var spindleCmdFactory = new Actions.Tools.SpindleTool.N700ESpindleToolFactory();

            var machine = new GCodeMachine(rtSender, new DummyMessageRouter(), state, config);
            var programBuilder = new ProgramBuilder(machine, null, rtSender, modbusSender, spindleCmdFactory, new DummyToolManager(), config);

            var (program, _, _, _) = programBuilder.BuildProgram(programGcode, state);
            machine.LoadProgram(program);
            machine.Start();

            while (machine.IsRunning())
            {}

            machine.Dispose();
            rtSender.Dispose();

            output.Seek(0, SeekOrigin.Begin);
            var resultb = output.ToArray();
            var result = System.Text.Encoding.UTF8.GetString(resultb, 0, resultb.Length);
            Console.WriteLine("RESULT:\n{0}", result);
        }


        static void Main(string[] args)
        {
            TestCase10();
        }
    }
}
