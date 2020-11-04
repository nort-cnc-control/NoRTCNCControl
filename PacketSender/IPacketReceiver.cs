using System;
namespace PacketSender
{
    public interface IPacketReceiver
    {
        string ReceivePacket(int timeoutMs);
    }
}
