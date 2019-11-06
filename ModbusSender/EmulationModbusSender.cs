using System;
using System.IO;

namespace ModbusSender
{
    public class EmulationModbusSender : IModbusSender
    {
        private Stream output;
        public EmulationModbusSender(Stream output)
        {
            this.output = output;
        }

        public void WriteRegister(ushort index, ushort value)
        {
            var bts = System.Text.Encoding.UTF8.GetBytes(String.Format("MODBUS: {0} = {1}\n", index, value));
            output.Write(bts, 0, bts.Length);
        }
    }
}
