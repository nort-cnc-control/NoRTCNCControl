using System;
using Machine;

namespace CNCState
{
    public class BinaryState : IToolState
    {
        public bool Enabled { get; set; }

        public BinaryState()
        {
            Enabled = false;
        }

        public BinaryState BuildCopy()
        {
            return new BinaryState
            {
                Enabled = Enabled,
            };
        }
    }
}
