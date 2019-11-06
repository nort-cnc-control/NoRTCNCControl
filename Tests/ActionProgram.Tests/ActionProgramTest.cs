using System;
using Xunit;
using ActionProgram;
using RTSender;
using ModbusSender;
using Actions;
using Config;

namespace ActionProgram.Tests
{
    public class ProgramTest
    {
        [Fact]
        public void TestSingleLine()
        {
            MachineParameters config = new MachineParameters();
            var sender = new DummyRTSender();
            var modbusSender = new DummyModbusSender();
            var machine = new DummyMachine();

            ActionProgram prg = new ActionProgram(sender, modbusSender, config, machine);
            prg.AddRTUnlock();
            prg.AddLineMovement(new Vector3(10, 0, 0), 100);
        }

        [Fact]
        public void TestLinePair_1()
        {
            MachineParameters config = new MachineParameters();
            var sender = new DummyRTSender();
            var modbusSender = new DummyModbusSender();
            var machine = new DummyMachine();

            ActionProgram prg = new ActionProgram(sender, modbusSender, config, machine);
            prg.AddRTUnlock();
            prg.AddLineMovement(new Vector3(10, 0, 0), 100);
            prg.AddLineMovement(new Vector3(10, 0, 0), 100);
        }
        [Fact]
        public void TestLinePair_2()
        {
            MachineParameters config = new MachineParameters();
            var sender = new DummyRTSender();
            var modbusSender = new DummyModbusSender();
            var machine = new DummyMachine();

            ActionProgram prg = new ActionProgram(sender, modbusSender, config, machine);
            prg.AddRTUnlock();
            prg.AddLineMovement(new Vector3(10, 0, 0), 100);
            prg.AddLineMovement(new Vector3(0, 10, 0), 100);
        }
        [Fact]
        public void TestLinePair_3()
        {
            MachineParameters config = new MachineParameters();
            var sender = new DummyRTSender();
            var modbusSender = new DummyModbusSender();
            var machine = new DummyMachine();

            ActionProgram prg = new ActionProgram(sender, modbusSender, config, machine);
            prg.AddRTUnlock();
            prg.AddLineMovement(new Vector3(10, 0, 0), 100);
            prg.AddLineMovement(new Vector3(10, 1, 0), 100);
        }
        [Fact]
        public void TestLinePair_4()
        {
            MachineParameters config = new MachineParameters();
            var sender = new DummyRTSender();
            var modbusSender = new DummyModbusSender();
            var machine = new DummyMachine();

            ActionProgram prg = new ActionProgram(sender, modbusSender, config, machine);
            prg.AddRTUnlock();
            prg.AddLineMovement(new Vector3(10, 0, 0), 100);
            prg.AddLineMovement(new Vector3(10, 10, 0), 100);
        }
    }
}
