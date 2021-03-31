using System;
using System.Collections.Generic;
using System.Threading;
using Log;

namespace RTSender
{
    public class SyncRTSender : IRTSender, ILoggerSource
    {
        private IRTSender rtSender;

        public string Name => "SyncPacketRTSender";
        public bool HasSlots => rtSender.HasSlots;

        public event Action EmptySlotAppeared;
        public event Action EmptySlotsEnded;

        public event Action Reseted;
        public event Action<int> Indexed;
        public event Action<int> Queued;
        public event Action<int> Dropped;
        public event Action<int> Started;
        public event Action<int, IReadOnlyDictionary<String, String>> Completed;
        public event Action<int, String> Failed;
        public event Action<String> Error;
        public event Action<String> Debug;
        public event Action<int> SlotsNumberReceived;

        private EventWaitHandle waitResponse;

        public SyncRTSender(IRTSender rtSender)
        {
            waitResponse = new EventWaitHandle(false, EventResetMode.ManualReset);
            this.rtSender = rtSender;
            this.rtSender.Debug               += RTSender_Debug;
            this.rtSender.Error               += RTSender_Error;
            this.rtSender.Reseted             += RTSender_Reseted;
            this.rtSender.Queued              += RTSender_Queued;
            this.rtSender.Dropped             += RTSender_Dropped;
            this.rtSender.Indexed             += RTSender_Indexed;
            this.rtSender.Started             += RTSender_Started;
            this.rtSender.Failed              += RTSender_Failed;
            this.rtSender.Completed           += RTSender_Completed;
            this.rtSender.SlotsNumberReceived += RTSender_SlotsNumberReceived;
            this.rtSender.EmptySlotAppeared   += RTSender_EmptySlotAppeared;
            this.rtSender.EmptySlotsEnded     += RTSender_EmptySlotsEnded;
        }

        #region events
        void RTSender_Started(int N)
        {
            Started?.Invoke(N);
        }

        void RTSender_SlotsNumberReceived(int Q)
        {
            SlotsNumberReceived?.Invoke(Q);
        }

        public void RTSender_Debug(string reason)
        {
            Debug?.Invoke(reason);
        }

        public void RTSender_Error(string reason)
        {
            Logger.Instance.Debug(this, "unlock", "error");
            waitResponse.Reset();
            Error?.Invoke(reason);
        }

        public void RTSender_Reseted()
        {
            Logger.Instance.Debug(this, "unlock", "reseted");
            waitResponse.Reset();
            Reseted?.Invoke();
        }

        private void RTSender_EmptySlotsEnded()
        {
            EmptySlotsEnded?.Invoke();
        }

        private void RTSender_EmptySlotAppeared()
        {
            EmptySlotAppeared?.Invoke();
        }

        private void RTSender_Completed(int N, IReadOnlyDictionary<string, string> args)
        {
            Completed?.Invoke(N, args);
        }

        private void RTSender_Failed(int N, string reason)
        {
            Failed?.Invoke(N, reason);
        }

        private void RTSender_Indexed(int N)
        {
            Indexed?.Invoke(N);
        }

        private void RTSender_Dropped(int N)
        {
            Logger.Instance.Debug(this, "unlock", "dropped");
            waitResponse.Reset();
            Dropped?.Invoke(N);
        }

        private void RTSender_Queued(int N)
        {
            Logger.Instance.Debug(this, "unlock", "queued");
            waitResponse.Set();
            Queued?.Invoke(N);
        }

        #endregion events

        public void SendCommand(string command)
        {
            Logger.Instance.Debug(this, "send", "sending command");
            waitResponse.Reset();
            rtSender.SendCommand(command);
            Logger.Instance.Debug(this, "send", "wait for response");
            waitResponse.WaitOne();
            Logger.Instance.Debug(this, "send", "wait: done");
        }

        public void Init()
        {
            rtSender.Init();
        }

        public void Dispose()
        {
            this.rtSender.Started             -= RTSender_Started;
            this.rtSender.Queued              -= RTSender_Queued;
            this.rtSender.Dropped             -= RTSender_Dropped;
            this.rtSender.Indexed             -= RTSender_Indexed;
            this.rtSender.Failed              -= RTSender_Failed;
            this.rtSender.Completed           -= RTSender_Completed;
            this.rtSender.EmptySlotAppeared   -= RTSender_EmptySlotAppeared;
            this.rtSender.EmptySlotsEnded     -= RTSender_EmptySlotsEnded;
            this.rtSender.SlotsNumberReceived -= RTSender_SlotsNumberReceived;
            rtSender.Dispose();
        }
    }
}
