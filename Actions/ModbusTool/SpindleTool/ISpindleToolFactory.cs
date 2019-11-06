using System;

namespace Actions.ModbusTool.SpindleTool
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
