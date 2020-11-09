using System;
using System.Threading;
using Machine;

namespace Actions.Mills
{
    public class SelectToolCommand : IMachineControlCommand
    {
        public int Mill { get; private set; }
        private IMachine machine;
        private IMillManager manager;

        public SelectToolCommand(int toolId, IMachine machine, IMillManager manager)
        {
            Mill = toolId;
            this.machine = machine;
            this.manager = manager;
        }

        public void Run()
        {
            manager.SelectMill(Mill);
        }
    }
}
