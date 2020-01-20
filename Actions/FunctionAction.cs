using System;
using System.Threading;

namespace Actions
{
    public class FunctionAction : IAction
    {
        public delegate void Function();

        private Function f;
        public FunctionAction(Function function)
        {
            f = function;
            ReadyToRun = new EventWaitHandle(true, EventResetMode.ManualReset);
            ContiniousBlockCompleted = new EventWaitHandle(false, EventResetMode.ManualReset);
            Started = new EventWaitHandle(false, EventResetMode.ManualReset);
            Finished = new EventWaitHandle(false, EventResetMode.ManualReset);
        }

        public bool RequireFinish => true;

        public EventWaitHandle ReadyToRun { get; private set; }

        public EventWaitHandle ContiniousBlockCompleted { get; private set; }

        public EventWaitHandle Started { get; private set; }
       
        public EventWaitHandle Finished { get; private set; }

        public bool Aborted => false;

        public bool Failed => false;

        public event Action<IAction> EventStarted;
        public event Action<IAction> EventFinished;

        public void Abort()
        {
        }

        public void Dispose()
        {
            f = null;
        }

        public void Run()
        {
            Started.Set();
            EventStarted?.Invoke(this);
            f();
            Finished.Set();
            ContiniousBlockCompleted.Set();
            EventFinished?.Invoke(this);
        }
    }
}
