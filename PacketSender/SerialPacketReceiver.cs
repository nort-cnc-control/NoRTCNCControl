using System;
using System.IO.Ports;

namespace PacketSender
{
    public class SerialPacketReceiver : IPacketReceiver
    {
        private SerialPort port;
        public SerialPacketReceiver(SerialPort port)
        {
            this.port = port;
        }

        public string ReceivePacket(int timeoutMs)
        {
            port.ReadTimeout = timeoutMs;
            try
            {
                string line = port.ReadLine();
                line = line.Replace("\r", "");
                line = line.Replace("\n", "");
                return line;
            }
            catch
            {
                return null;
            }
        }
    }
}
