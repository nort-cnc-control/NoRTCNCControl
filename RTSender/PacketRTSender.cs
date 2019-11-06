using System;
using System.Collections.Generic;

namespace RTSender
{
    public class PacketRTSender : IRTSender
    {
        public event Action EmptySlotAppeared;
        public event Action EmptySlotsEnded;
        
        public event Action<int> Indexed;
        public event Action<int> Queued;
        public event Action<int> Dropped;
        public event Action<int> Started;
        public event Action<int, IReadOnlyDictionary<String, String>> Completed;
        public event Action<int, String> Failed;

        private int Q;
        public bool HasSlots { get { return Q > 0; } }
        
        public void SendCommand(String command)
        {

        }

        public void Dispose()
        {
            
        }
    }
}
