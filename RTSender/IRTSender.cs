using System;
using System.Collections.Generic;

namespace RTSender
{
    public interface IRTSender : IDisposable
    {
        event Action Reseted;
        event Action<int> Indexed;
        event Action<int> Queued;
        event Action<int> Dropped;
        event Action<int> Started;
        event Action EmptySlotAppeared;
        event Action EmptySlotsEnded;
        event Action<int> SlotsNumberReceived;
        event Action<int, IReadOnlyDictionary<String, String>> Completed;
        event Action<int, String> Failed;

        bool HasSlots { get; }
        void SendCommand(String command);
    }
}
