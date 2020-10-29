using System;
using ModbusSender;

namespace Actions.Tools
{
    public class RawModbus_driver : IDriver
    {
        private readonly IModbusSender sender;
        private readonly int devid;
        private readonly UInt16 register;

        public RawModbus_driver(IModbusSender sender, int devid, UInt16 register)
        {
            this.sender = sender;
            this.devid = devid;
            this.register = register;
        }

        public IAction Configure()
        {
            return new PlaceholderAction();
        }

        public IAction CreateAction(bool enable)
        {
            var registers = new ModbusRegister[1];
            registers[0] = new ModbusRegister { DeviceId = devid, RegisterId = register, RegisterValue = (UInt16)(enable ? 1 : 0)};
            var command = new ModbusCommand { Registers = registers, Delay = 0 };
            return new ModbusAction(command, sender);
        }
    }
}
