using System;
using System.Collections.Generic;
using Actions.ModbusTool;
using CNCState;

namespace Actions.Tools.SpindleTool
{
    public class NoneSpindleToolFactory : ISpindleToolFactory
    {
        public ModbusToolCommand CreateSpindleToolCommand(SpindleState.SpindleRotationState rotation, double speed)
        {
            return new ModbusToolCommand { Registers = new ModbusRegister[0], Delay = 0 };
        }
    }
}
