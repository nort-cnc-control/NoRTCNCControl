using System;
using Actions.ModbusTool;

namespace Actions.Tools.SpindleTool
{
    public enum SpindleRotationState
    {
        Off,
        Clockwise,
        CounterClockwise,
    }

    public interface ISpindleToolFactory
    {
        ModbusToolCommand CreateSpindleToolCommand(SpindleRotationState rotation, double speed);
    }
}
