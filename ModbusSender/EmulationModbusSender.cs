using System;
using System.IO;

namespace ModbusSender
{
    public class EmulationModbusSender : IModbusSender
    {
        private TextWriter output;
        public EmulationModbusSender(TextWriter output)
        {
            this.output = output;
        }

        public void Init()
        {
        }

        public void WriteRegister(int devid, UInt16 index, UInt16 value)
        {
            var bts = String.Format("MODBUS: {0:X4}[{1:X4}] = {2:X4}", devid, index, value);
            output.WriteLine(bts);
        }
    }
}
