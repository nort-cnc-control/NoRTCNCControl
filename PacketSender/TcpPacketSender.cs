using System;
using System.IO;
using System.Net.Sockets;

namespace PacketSender
{
    public class TcpPacketSender : IPacketSender
    {
        private StreamWriter stream;
        private TcpClient tcpClient;

        public TcpPacketSender(TcpClient tcpClient)
        {
            this.tcpClient = tcpClient;
            this.stream = new StreamWriter(tcpClient.GetStream());
        }

        public bool SendPacket(string data)
        {
            stream.WriteLine(data);
            stream.Flush();
            return true;
        }
    }
}
