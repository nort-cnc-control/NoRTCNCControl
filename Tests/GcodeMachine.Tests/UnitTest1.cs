using System;
using Xunit;
using GcodeMachine;
using RTSender;
using Config;
using CNCState;

namespace GcodeMachine.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void TestMachine1()
        {
            var programGcode = "G0 X10 F100\nG0 X20\n";

            var config = new MachineParameters();

            config.ACC = 40;
            config.FASTFEED = 600;
            config.SLOWFEED = 100;
            config.MAXFEED = 800;

            var axisState = new AxisState();
            var spindleState = new SpindleState();
            
            var sender = new EmulationRTSender();
            
            var machine = new GcodeMachine(sender);
            var programBuilder = new ProgramBuilder(machine, sender, config);

            Console.WriteLine("BUILDING");
            var program = programBuilder.BuildProgram(programGcode, axisState, spindleState);
            machine.LoadProgram(program);
            machine.Start();
            machine.Dispose();
            sender.Dispose();
        }
    }
}
