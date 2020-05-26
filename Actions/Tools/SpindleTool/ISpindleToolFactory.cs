using System;
using Actions.ModbusTool;
using CNCState;

namespace Actions.Tools.SpindleTool
{
    public interface ISpindleToolFactory
    {
        ModbusToolCommand CreateSpindleToolCommand(SpindleState.SpindleRotationState rotation, decimal speed);
    }
}
