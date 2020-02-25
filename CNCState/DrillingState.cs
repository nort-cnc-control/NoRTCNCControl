using System;
using Machine;

namespace CNCState
{
    public class DrillingState : IState
    {
        public DrillingState()
        {
            Retract = RetractType.Rapid;
            RetractDepth = RetractDepthType.InitialHeight;
        }

        public enum RetractType
        { 
            Rapid,
            Feed,
            Manual,
        }

        public enum RetractDepthType
        { 
            InitialHeight,
            RHeight,
        }

        public bool Drilling { get; set; }
        public bool Peck { get; set; }
        public bool RetractReverse { get; set; }
        public bool Dwell { get; set; }
        public bool LeftHand { get; set; }
        public bool StopSpindle { get; set; }
        public bool Tapping { get; set; }

        public RetractType Retract { get; set; }
        public RetractDepthType RetractDepth { get; set; }

        public double PeckDepth { get; set; }
        public double RHeight { get; set; }
        public double Depth { get; set; }

        public DrillingState BuildCopy()
        {
            return new DrillingState
            {
                Drilling = Drilling,
                Peck = Peck,
                RetractReverse = RetractReverse,
                Dwell = Dwell,
                LeftHand = LeftHand,
                StopSpindle = StopSpindle,
                Tapping = Tapping,
                Retract = Retract,
                PeckDepth = PeckDepth,
                RetractDepth = RetractDepth,
                Depth = Depth,
                RHeight = RHeight,
            };
        }
    }
}
