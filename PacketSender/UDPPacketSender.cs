using System;
using System.Net.Sockets;

namespace PacketSender
{
    public class UDPPacketSender : IPacketSender
    {
        private UdpClient client;
        public UDPPacketSender(UdpClient client)
        {
            this.client = client;
        }

        public bool SendPacket(string data)
        {
            byte[] msg = System.Text.Encoding.ASCII.GetBytes(data);
            client.Send(msg, msg.Length);
            return true;
        }
    }
}
