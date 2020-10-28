using System;
using CNCState;
using ModbusSender;

namespace Actions.Tools
{
    public class N700E_driver
    {
        private readonly int devid;

        private readonly UInt16 runRegister = 0x0002;
        private readonly UInt16 speedRegister = 0x0004;

        private readonly UInt16 runForward = 0x0001;
        private readonly UInt16 runReverse = 0x0002;
        private readonly UInt16 runNone = 0x0000;

        private readonly IModbusSender sender;

        public N700E_driver(IModbusSender sender, int devid)
        {
            this.devid = devid;
            this.sender = sender;
        }

        public IAction CreateAction(SpindleState.SpindleRotationState rotation, decimal speed)
        {
            var registers = new ModbusRegister[2];
            int delay = 0;
            switch (rotation)
            {
                case SpindleState.SpindleRotationState.Off:
                    registers[0] = new ModbusRegister { DeviceId = devid, RegisterId = runRegister, RegisterValue = runNone };
                    delay = 0;
                    break;
                case SpindleState.SpindleRotationState.Clockwise:
                    registers[0] = new ModbusRegister { DeviceId = devid, RegisterId = runRegister, RegisterValue = runForward };
                    delay = 3000;
                    break;
                case SpindleState.SpindleRotationState.CounterClockwise:
                    registers[0] = new ModbusRegister { DeviceId = devid, RegisterId = runRegister, RegisterValue = runReverse };
                    delay = 3000;
                    break;
            }
            registers[1] = new ModbusRegister { DeviceId = devid, RegisterId = speedRegister, RegisterValue = (UInt16)(speed / 60.0m * 100) };
            var command = new ModbusCommand { Registers = registers, Delay = delay };
            return new ModbusAction(command, sender);
        }
    }
}
