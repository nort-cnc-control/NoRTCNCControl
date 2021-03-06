﻿using System;
using System.IO;
using Log;
using PacketSender;

namespace ModbusSender
{
    public class PacketModbusSender : IModbusSender, ILoggerSource
    {
        private IPacketSender output;
        private readonly object lockObj = new object();

        public string Name => "packet mb sender";

        public PacketModbusSender(IPacketSender output, IPacketReceiver input)
        {
            this.output = output;
        }

        public void Init()
        {
            var cmd = String.Format("START:");
            Logger.Instance.Debug(this, "send", cmd);
            lock (lockObj)
            {
                Logger.Instance.Debug(this, "lock", "success");
                try
                {
                    output.SendPacket(cmd);
                }
                catch (Exception e)
                {
                    Logger.Instance.Error(this, "send", e.ToString());
                }
            }
        }

        public void WriteRegister(int devid, UInt16 index, UInt16 value)
        {
            lock (lockObj)
            {
                var cmd = string.Format("MB:{0:X4}:{1:X4}:{2:X4}", devid, index, value);
                Logger.Instance.Debug(this, "send", cmd);
		        output.SendPacket(cmd);
            }
        }

        public void Dispose()
        {
        }
    }
}
