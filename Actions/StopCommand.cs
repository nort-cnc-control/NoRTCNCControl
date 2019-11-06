using System;
using Machine;

namespace Actions
{
    public class StopCommand : IMachineControlCommand
    {
        private IMachine machine;

        #region methods
        public void Run()
        {
            machine.Stop();
        }
        #endregion
        public StopCommand(IMachine machine)
        {
            this.machine = machine;
        }
    }
}
