using System;
using System.Collections.Generic;
using RTSender;

namespace ActionProgram.Tests
{
    public class DummyRTSender : IRTSender
    {
        public event Action EmptySlotAppeared;
        public event Action EmptySlotsEnded;
        
        public event Action<int> Indexed;
        public event Action<int> Queued;
        public event Action<int> Dropped;
        public event Action<int> Started;
        public event Action<int, IReadOnlyDictionary<String, String>> Completed;
        public event Action<int, String> Failed;

        public bool HasSlots { get { return true; } }
        
        private int index = 0;

        private IReadOnlyDictionary<String, String> opts = new Dictionary<String, String>();
        public void SendCommand(String command)
        {
            command = String.Format("N{0} {1}", index, command);
            Console.WriteLine("Command: " + command);
            Indexed?.Invoke(index);
            Queued?.Invoke(index);
            Started?.Invoke(index);
            Completed?.Invoke(index, opts);
            index++;
        }

        public void Dispose()
        {}
    }
}
