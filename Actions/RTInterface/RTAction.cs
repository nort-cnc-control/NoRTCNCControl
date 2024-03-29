using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Log;

using RTSender;

namespace Actions
{
    public class RTActionLeakManager : ILoggerSource
    {
        public string Name => "RTAction leak";

        private int lastNid;

        private static RTActionLeakManager instance;
        public static RTActionLeakManager Instance
        {
            get
            {
                if (instance == null)
                    instance = new RTActionLeakManager();
                return instance;
            }
        }

        public Dictionary<int, int> Counter { get; private set; }

        public RTActionLeakManager()
        {
            Counter = new Dictionary<int, int>();
            lastNid = -1;
        }

        public void ActionExecuted(int nid)
        {
            if (!Counter.ContainsKey(nid))
                Counter.Add(nid, 0);
            Counter[nid]++;
            if (nid != lastNid)
            {
                foreach (var item in Counter)
                    Logger.Instance.Debug(this, "leak manager", String.Format("{0} : {1}", item.Key, item.Value));
            }
            lastNid = nid;
        }
    }

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

        private int resendMax = 2;
        private int resendCount;
        private string cmd;

        public RTAction(IRTSender sender, IRTCommand command)
        {
            this.sender = sender;
            this.sender.Queued += OnQueuedHdl;
            this.sender.Dropped += OnDroppedHdl;
            this.sender.Started += OnStartedHdl;
            this.sender.Completed += OnCompletedHdl;
            this.sender.Failed += OnFailedHdl;
            this.sender.EmptySlotAppeared += OnEmptySlotHdl;
            this.sender.EmptySlotsEnded += OnEmptySlotsEndedHdl;
            this.sender.SlotsNumberReceived += OnSpaceReceived;
            Command = command;

            ContiniousBlockCompleted = new EventWaitHandle(false, EventResetMode.ManualReset);
            ReadyToRun = new EventWaitHandle(sender.HasSlots, EventResetMode.ManualReset);
            Started = new EventWaitHandle(false, EventResetMode.ManualReset);
            Finished = new EventWaitHandle(false, EventResetMode.ManualReset);

            Failed = false;
            Aborted = false;
            CommandId = -1;
        }

        private void OnSpaceReceived(int nid)
        {
            if (nid != CommandId)
                return;
            ContiniousBlockCompleted.Set();
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

        private void OnFailedHdl(int nid, String line)
        {
            if (nid != CommandId)
                return;
            Logger.Instance.Debug(this, "failed", nid.ToString());
            var dict = new Dictionary<string, string>
            {
                ["error"] = line
            };
            ActionResult = dict;
            Failed = true;
            Finished.Set();
            EventFinished?.Invoke(this);
        }

        private void OnStartedHdl(int nid)
        {
            //RTActionLeakManager.Instance.ActionExecuted(nid);
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

        #region methods

        public void Run()
        {
            cmd = Command.Command;
            if (!sender.HasSlots && Command.CommandIsCached)
            {
                Logger.Instance.Error(this, "no slots", String.Format("Command = \"{0}\"", cmd));
                throw new OutOfMemoryException("MCU doesn't have empty slots");
            }

            resendCount = 0;
	
            CommandId = sender.GetNewIndex();
            sender.SendCommand(cmd, CommandId);
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
            this.sender.Failed -= OnFailedHdl;
            this.sender.EmptySlotAppeared -= OnEmptySlotHdl;
            this.sender.EmptySlotsEnded -= OnEmptySlotsEndedHdl;
            this.sender.SlotsNumberReceived -= OnSpaceReceived;
        }
        #endregion
    }
}
