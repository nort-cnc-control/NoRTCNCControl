using System;
using System.IO.Ports;

namespace PacketSender
{
    public class SerialPacketSender : IPacketSender
    {
        private SerialPort port;
        public SerialPacketSender(SerialPort port)
        {
            this.port = port;
        }

        public bool SendPacket(string data)
        {
            port.WriteLine(data);
            return true;
        }
    }
}
