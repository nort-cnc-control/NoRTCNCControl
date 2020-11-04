using System;

namespace ModbusSender
{
    public interface IModbusSender : IDisposable
    {
        void Init();
        void WriteRegister(int devid, UInt16 index, UInt16 value);
    }
}
