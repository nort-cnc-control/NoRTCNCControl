using System;
using System.Collections.Generic;
using Actions.ModbusTool;

namespace Actions.Tools.SpindleTool
{
    public class NoneSpindleToolFactory : ISpindleToolFactory
    {
        public ModbusToolCommand CreateSpindleToolCommand(SpindleRotationState rotation, double speed)
        {
            return new ModbusToolCommand { Registers = new ModbusRegister[0], Delay = 0 };
        }
    }
}
