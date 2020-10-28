using System;
using System.Threading;
using Machine;

namespace Actions.Tools
{
    public class SelectToolCommand : IMachineControlCommand
    {
        public int Tool { get; private set; }
        private IMachine machine;
        private IToolManager manager;

        public SelectToolCommand(int toolId, IMachine machine, IToolManager manager)
        {
            Tool = toolId;
            this.machine = machine;
            this.manager = manager;
        }

        public void Run()
        {
            manager.SelectTool(Tool);
        }
    }
}
