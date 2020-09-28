using System;
using System.IO;

namespace PacketSender
{
    public class StreamPacketReceiver : IPacketReceiver
    {
        private StreamReader stream;

        public StreamPacketReceiver(StreamReader stream)
        {
            this.stream = stream;
        }

        public string ReceivePacket()
        {
            return stream.ReadLine();
        }
    }
}
