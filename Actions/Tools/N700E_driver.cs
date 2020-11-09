using System;
using CNCState;
using ModbusSender;

namespace Actions.Mills
{
    public class N700E_driver : IDriver
    {
        private readonly int devid;
        private readonly int maxspeed;
        private readonly int basespeed;

        private readonly UInt16 maxSpeedRegister = 0x0304;
        private readonly UInt16 baseSpeedRegister = 0x0303;
        private readonly UInt16 runRegister = 0x0002;
        private readonly UInt16 speedRegister = 0x0004;

        private readonly UInt16 runForward = 0x0001;
        private readonly UInt16 runReverse = 0x0002;
        private readonly UInt16 runNone = 0x0000;

        private readonly IModbusSender sender;

        private UInt16 ConvertSpeed(decimal speed)
        {
            return (UInt16)(speed / 60.0m * 100);
        }

        public N700E_driver(IModbusSender sender, int devid, int maxspeed, int basespeed)
        {
            this.devid = devid;
            this.maxspeed = maxspeed;
            this.basespeed = basespeed;
            this.sender = sender;
        }

        public IAction Configure()
        {
            var registers = new ModbusRegister[2];

            registers[0] = new ModbusRegister { DeviceId = devid, RegisterId = maxSpeedRegister, RegisterValue = ConvertSpeed(maxspeed) };
            registers[1] = new ModbusRegister { DeviceId = devid, RegisterId = baseSpeedRegister, RegisterValue = ConvertSpeed(basespeed) };

            var command = new ModbusCommand { Registers = registers, Delay = 0 };
            return new ModbusAction(command, sender);
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
            registers[1] = new ModbusRegister { DeviceId = devid, RegisterId = speedRegister, RegisterValue = ConvertSpeed(speed) };
            var command = new ModbusCommand { Registers = registers, Delay = delay };
            return new ModbusAction(command, sender);
        }
    }
}
