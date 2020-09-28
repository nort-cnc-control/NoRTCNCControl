using System;
using System.Net;
using System.Net.Sockets;

namespace PacketSender
{
    public class UDPPacketReceiver : IPacketReceiver
    {
        private UdpClient client;
        private IPEndPoint ep;

        public UDPPacketReceiver(UdpClient client, string addr, int port)
        {
            this.client = client;
            ep = new IPEndPoint(IPAddress.Any, 0);
        }

        public string ReceivePacket()
        {
            var data = client.Receive(ref ep);
            return System.Text.Encoding.ASCII.GetString(data);
        }
    }
}
