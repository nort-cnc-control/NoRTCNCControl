using System;
using Machine;
using Vector;

namespace CNCState
{


    public class CNCState
    {
        public CNCState(AxisState axisState, SpindleState spindleState, DrillingState drillingState)
        {
            AxisState = axisState;
            SpindleState = spindleState;
            DrillingState = drillingState;
        }

        public CNCState BuildCopy()
        {
            return new CNCState(AxisState.BuildCopy(), SpindleState.BuildCopy(), DrillingState.BuildCopy());
        }

        public AxisState AxisState { get; private set; }
        public SpindleState SpindleState { get; private set; }
        public DrillingState DrillingState { get; private set; }
    }
}
