using System;
using Machine;
using Vector;

namespace CNCState
{


    public class CNCState
    {
        public CNCState(AxisState axisState, SpindleState spindleState, DrillingState drillingState, SyncToolState syncToolState)
        {
            AxisState = axisState;
            SpindleState = spindleState;
            DrillingState = drillingState;
            SyncToolState = syncToolState;
        }

        public CNCState()
        {
            AxisState = new AxisState();
            SpindleState = new SpindleState();
            DrillingState = new DrillingState();
            SyncToolState = new SyncToolState();
        }

        public CNCState BuildCopy()
        {
            return new CNCState(AxisState.BuildCopy(), SpindleState.BuildCopy(), DrillingState.BuildCopy(), SyncToolState.BuildCopy());
        }

        public AxisState AxisState { get; private set; }
        public SpindleState SpindleState { get; private set; }
        public DrillingState DrillingState { get; private set; }
        public SyncToolState SyncToolState { get; private set; }
    }
}
