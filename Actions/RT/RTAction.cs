using System;
using System.Threading;
using System.Collections.Generic;

using RTSender;

namespace Actions
{
    public class RTAction : IAction
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
        private IRTSender sender;
        
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
            ActionResult = result;
            Finished.Set();
        }

        private void OnStartedHdl(int nid)
        {
            if (nid != CommandId)
                return;
            
            Started.Set();
        }

        private void OnQueuedHdl(int nid)
        {
            if (nid != CommandId)
                return;
            
            ContiniousBlockCompleted.Set();
        }

        private void OnDroppedHdl(int nid)
        {
            if (nid != CommandId)
                return;
                
            ContiniousBlockCompleted.Set();
            Started.Set();
            Finished.Set();
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
            sender.Indexed += OnIndexed;
            sender.SendCommand(cmd);
            sender.Indexed -= OnIndexed;
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
