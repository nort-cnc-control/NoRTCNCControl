using System;
using System.IO;

namespace ModbusSender
{
    public class PacketModbusSender : IModbusSender
    {
        private StreamWriter output;
        private readonly object lockObj = new object();

        public PacketModbusSender(StreamWriter output, StreamReader input)
        {
            this.output = output;
        }

        public void WriteRegister(ushort devid, ushort index, ushort value)
        {
            lock (lockObj)
            {
                var cmd = string.Format("MB:{0:X4}:{1:X4}:{2:X4}", devid, index, value);
                output.WriteLine(cmd);
            }
        }
    }
}
