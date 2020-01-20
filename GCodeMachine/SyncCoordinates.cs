using System;
using System.Threading;
using Actions;

namespace GCodeMachine
{
    public class SyncCoordinates : IAction
    {
        private IStateSyncManager manager;
        private Vector3 globalPosition;

        public SyncCoordinates(IStateSyncManager manager, Vector3 globalPosition)
        {
            this.manager = manager;
            this.globalPosition = globalPosition;
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
            manager = null;
        }

        public void Run()
        {
            Started.Set();
            EventStarted?.Invoke(this);
            if (manager != null)
                manager.SyncCoordinates(globalPosition);
            Finished.Set();
            ContiniousBlockCompleted.Set();
            EventFinished?.Invoke(this);
        }
    }
}
