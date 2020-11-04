using System;
using System.IO;
using System.Net.Sockets;

namespace PacketSender
{
    public class TcpPacketReceiver : IPacketReceiver
    {
        private StreamReader stream;
        private TcpClient tcpClient;

        public TcpPacketReceiver(TcpClient tcpClient)
        {
            this.tcpClient = tcpClient;
            this.stream = new StreamReader(tcpClient.GetStream());
        }

        public string ReceivePacket(int timeoutMs)
        {
            tcpClient.ReceiveTimeout = timeoutMs;
            try
            {
                return stream.ReadLine();
            }
            catch
            {
                return null;
            }
        }
    }
}
