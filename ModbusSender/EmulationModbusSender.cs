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

        public void WriteRegister(ushort index, ushort value)
        {
            var bts = String.Format("MODBUS: {0} = {1}", index, value);
            output.WriteLine(bts);
        }
    }
}
