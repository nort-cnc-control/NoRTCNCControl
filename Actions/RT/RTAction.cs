using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Log;

using RTSender;

namespace Actions
{
    public class RTAction : IAction, ILoggerSource
    {
        public bool RequireFinish { get { return !Command.CommandIsCached; } }

        #region status
        public EventWaitHandle ContiniousBlockCompleted { get; private set; }
        public EventWaitHandle Started { get; private set; }
        public EventWaitHandle Finished { get; private set; }
        public EventWaitHandle ReadyToRun { get; private set; }
        public bool Aborted { get; private set; }
        public bool Failed { get; private set; }
        #endregion

        #region Parameters
        public int CommandId { get; private set; }

        public IReadOnlyDictionary<String, String> ActionResult { get; private set; }
        #endregion

        public IRTCommand Command { get; private set; }

        public string Name => "RTAction";

        private IRTSender sender;

        public event Action<IAction> EventStarted;
        public event Action<IAction> EventFinished;

        public RTAction(IRTSender sender, IRTCommand command)
        {
            this.sender = sender;
            this.sender.Queued += OnQueuedHdl;
            this.sender.Dropped += OnDroppedHdl;
            this.sender.Started += OnStartedHdl;
            this.sender.Completed += OnCompletedHdl;
            this.sender.EmptySlotAppeared += OnEmptySlotHdl;
            this.sender.EmptySlotsEnded += OnEmptySlotsEndedHdl;

            Command = command;
            
            ContiniousBlockCompleted = new EventWaitHandle(false, EventResetMode.ManualReset);
            ReadyToRun = new EventWaitHandle(sender.HasSlots, EventResetMode.ManualReset);
            Started = new EventWaitHandle(false, EventResetMode.ManualReset);
            Finished = new EventWaitHandle(false, EventResetMode.ManualReset);

            Failed = false;
            Aborted = false;
            CommandId = -1;
        }

        private void OnEmptySlotHdl()
        {
            ReadyToRun.Set();
        }

        private void OnEmptySlotsEndedHdl()
        {
            ReadyToRun.Reset();
        }

        private void OnCompletedHdl(int nid, IReadOnlyDictionary<String, String> result)
        {
            if (nid != CommandId)
                return;
            Logger.Instance.Debug(this, "completed", nid.ToString());
            var dict = new Dictionary<string, string>();
            foreach (var pair in result)
                dict.Add(pair.Key, pair.Value);
            ActionResult = dict;
            Finished.Set();
            EventFinished?.Invoke(this);
        }

        private void OnStartedHdl(int nid)
        {
            if (nid != CommandId)
                return;
            Started.Set();
            Logger.Instance.Debug(this, "start", nid.ToString());
            EventStarted?.Invoke(this);
        }

        private void OnQueuedHdl(int nid)
        {
            //Logger.Instance.Debug(this, "queued", String.Format("{0} {1}", nid, CommandId));
            if (nid != CommandId)
                return;
            Logger.Instance.Debug(this, "queued", nid.ToString());
            ContiniousBlockCompleted.Set();
        }

        private void OnDroppedHdl(int nid)
        {
            if (nid != CommandId)
                return;
            Logger.Instance.Debug(this, "dropped", nid.ToString());
            ContiniousBlockCompleted.Set();
            Started.Set();
            Finished.Set();
            EventFinished?.Invoke(this);
        }

        private void OnIndexed(int nid)
        {
            CommandId = nid;
        }

        #region methods

        public void Run()
        {
            if (!sender.HasSlots)
                throw new OutOfMemoryException("MCU doesn't have empty slots");
            String cmd = Command.Command;
            lock (sender)
            {
                sender.Indexed += OnIndexed;
                sender.SendCommand(cmd);
                sender.Indexed -= OnIndexed;
            }
        }

        public void Abort()
        {
            Aborted = true;
        }

        public void Dispose()
        {
            this.sender.Queued -= OnQueuedHdl;
            this.sender.Dropped -= OnDroppedHdl;
            this.sender.Started -= OnStartedHdl;
            this.sender.Completed -= OnCompletedHdl;
        }
        #endregion
    }
}
