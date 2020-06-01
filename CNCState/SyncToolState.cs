using System;
using Machine;

namespace CNCState
{
    public class SyncToolState : IState
    {
        public int Tool { get; set; }
        public bool Enabled { get; set; }

        public SyncToolState()
        {
            Tool = 0;
            Enabled = false;
        }

        public SyncToolState BuildCopy()
        {
            return new SyncToolState
            {
                Tool = Tool,
                Enabled = Enabled,
            };
        }
    }
}
