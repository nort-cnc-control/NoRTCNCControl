using System;
using System.Collections.Generic;
using System.Linq;
using Config;
using Machine;
using Vector;

namespace CNCState
{
    public class CNCState
    {
        private MachineParameters config;

        private CNCState(MachineParameters config,
                         AxisState axisState,
                         DrillingState drillingState,
                         SyncToolState syncToolState,
                         IReadOnlyDictionary<int, IToolState> ts,
                         VarsState vs)
        {
            AxisState = axisState;
            toolStates = ts.ToDictionary(entry => entry.Key, entry => entry.Value);
            DrillingState = drillingState;
            SyncToolState = syncToolState;
            VarsState = vs;
            this.config = config;
        }

        public CNCState(MachineParameters config)
        {
            AxisState = new AxisState
			{
				Feed = config.fastfeed/60m,
			};
            DrillingState = new DrillingState();
            SyncToolState = new SyncToolState();
            toolStates = new Dictionary<int, IToolState>();
            VarsState = new VarsState();
            foreach (var item in config.tools)
            {
                int id = item.Key;
                var driver = item.Value;
                if (driver is N700E_Tool)
                    toolStates[id] = new SpindleState();
                else if (driver is GPIO_Tool)
                    toolStates[id] = new BinaryState();
                else if (driver is RawModbus_Tool)
                    toolStates[id] = new BinaryState();
                else if (driver is Dummy_Tool)
                    toolStates[id] = null;
                else
                    throw new ArgumentOutOfRangeException();
            }
        }

        public CNCState BuildCopy()
        {
            Dictionary<int, IToolState> newToolStates = new Dictionary<int, IToolState>();
            foreach (var item in toolStates)
            {
                if (item.Value is SpindleState ss)
                    newToolStates.Add(item.Key, ss.BuildCopy());
                else if (item.Value is BinaryState bs)
                    newToolStates.Add(item.Key, bs.BuildCopy());
                else if (item.Value == null)
                    newToolStates.Add(item.Key, null);
                else
                    throw new ArgumentOutOfRangeException("Invalid state");
            }
            return new CNCState(config,
                                AxisState.BuildCopy(),
                                DrillingState.BuildCopy(),
                                SyncToolState.BuildCopy(),
                                newToolStates,
                                VarsState);
        }

        public AxisState AxisState { get; private set; }

        private readonly Dictionary<int, IToolState> toolStates;
        public IReadOnlyDictionary<int, IToolState> ToolStates => toolStates;

        public DrillingState DrillingState { get; private set; }
        public SyncToolState SyncToolState { get; private set; }
        public VarsState VarsState { get; private set; }
    }
}
