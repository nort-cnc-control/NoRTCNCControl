using System;
using Machine;

namespace Actions
{
    public class PauseCommand : IMachineControlCommand
    {
        private IMachine machine;

        #region methods
        public void Run()
        {
            machine.Pause();
        }
        #endregion
        public PauseCommand(IMachine machine)
        {
            this.machine = machine;
        }
    }
}
