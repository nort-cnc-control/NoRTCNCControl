using System;
using System.IO;

namespace PacketSender
{
    public class StreamPacketSender : IPacketSender
    {
        private StreamWriter stream;

        public StreamPacketSender(StreamWriter stream)
        {
            this.stream = stream;
        }

        public bool SendPacket(string data)
        {
            stream.WriteLine(data);
            stream.Flush();
            return true;
        }
    }
}
