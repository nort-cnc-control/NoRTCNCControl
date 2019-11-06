using System;
using ModbusSender;

namespace ActionProgram.Tests
{
    public class DummyModbusSender : IModbusSender
    {
        public DummyModbusSender()
        {
        }

        public void WriteRegister(ushort index, ushort value)
        {
            Console.WriteLine("{0} = {1}", index, value);
        }
    }
}
