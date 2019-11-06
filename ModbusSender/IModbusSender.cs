using System;

namespace ModbusSender
{
    public interface IModbusSender
    {
        void WriteRegister(UInt16 index, UInt16 value);
    }
}
