using System;
using System.Threading;
using Machine;

namespace Actions
{
    public class MachineControlAction : IAction
    {
        public bool RequireFinish { get { return true; } }

        #region status
        public EventWaitHandle ContiniousBlockCompleted { get; private set; }
        public EventWaitHandle Started { get; private set; }
        public EventWaitHandle Finished { get; private set; }
        public EventWaitHandle ReadyToRun { get; private set; }

        public bool Aborted { get; private set; }
        public bool Failed { get; private set; }
        #endregion

        private IMachineControlCommand command;

        #region methods
        public void Run()
        {
            Started.Set();
            EventStarted?.Invoke(this);
            command.Run();
            Finished.Set();
            ContiniousBlockCompleted.Set();
            EventFinished?.Invoke(this);
        }

        public void Abort()
        {
            Aborted = true;
        }

        public void Dispose()
        {
            machine = null; // remove link
        }
        #endregion

        private IMachine machine;

        public event Action<IAction> EventStarted;
        public event Action<IAction> EventFinished;

        public MachineControlAction(IMachineControlCommand command, IMachine machine)
        {
            ContiniousBlockCompleted = new EventWaitHandle(false, EventResetMode.ManualReset);
            ReadyToRun = new EventWaitHandle(true, EventResetMode.ManualReset);
            Started = new EventWaitHandle(false, EventResetMode.ManualReset);
            Finished = new EventWaitHandle(false, EventResetMode.ManualReset);

            this.command = command;
            this.machine = machine;
        }
    }
}
