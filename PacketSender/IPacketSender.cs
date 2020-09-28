using System;
namespace PacketSender
{
    public interface IPacketSender
    {
        bool SendPacket(string data);
    }
}
