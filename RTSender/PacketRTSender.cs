using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Log;
using PacketSender;

namespace RTSender
{
    public class PacketRTSender : IRTSender, ILoggerSource
    {
        public event Action EmptySlotAppeared;
        public event Action EmptySlotsEnded;

        public event Action Reseted;
        public event Action<int> Queued;
        public event Action<int> Dropped;
        public event Action<int> Started;
        public event Action<int, IReadOnlyDictionary<String, String>> Completed;
        public event Action<int, String> Failed;
        public event Action<String> Error;
        public event Action<String> Debug;
        public event Action<int> SlotsNumberReceived;

        private IPacketReceiver input;
        private IPacketSender output;
        private int index;

        private int Q;

        private void SetQ(int value, int nid)
        {
            int oldQ = Q;
            Q = value;
            if (oldQ == 0 && Q > 0)
            {
                Logger.Instance.Debug(this, "empty slot", $"N{nid}: empty slot appeared");
                EmptySlotAppeared?.Invoke();
            }
            else if (oldQ > 0 && value == 0)
            {
                Logger.Instance.Debug(this, "empty slot", $"N{nid}: empty slots ended");
                EmptySlotsEnded?.Invoke();
            }
            SlotsNumberReceived?.Invoke(nid);
        }

        public bool HasSlots => (Q > 0);

        public string Name => "packet rt sender";

        private readonly object lockObj = new object();
        private Thread receiveThread;
        private bool running;

        private void ReceiveHandle(object arg)
        {
            var line = arg as String;
            try
            {
                var args = new Answer(line);
                switch (args.Message)
                {
                    case "Hello":
                        Reseted?.Invoke();
                        break;
                    case "dropped":
                        Dropped?.Invoke(int.Parse(args.Values["N"]));
                        break;
                    case "queued":
                        Queued?.Invoke(int.Parse(args.Values["N"]));
                        break;
                    case "started":
                        Started?.Invoke(int.Parse(args.Values["N"]));
                        break;
                    case "completed":
                        Completed?.Invoke(int.Parse(args.Values["N"]), args.Values);
                        break;
                    case "failed":
                        Failed?.Invoke(int.Parse(args.Values["N"]), line);
                        break;
                    case "debug":
                        Debug?.Invoke(line);
                        break;
                    case "error":
                        Error?.Invoke(line);
                        break;
                    default:
                        break;
                }
                if (args.Values.ContainsKey("Q"))
                {
                    SetQ(int.Parse(args.Values["Q"]), int.Parse(args.Values["N"]));
                }
            }
            catch
            {
                Logger.Instance.Error(this, "exception", line);
            }
        }

        private void ReceiveThreadProc()
        {
            while (running)
            {
                var line = input.ReceivePacket(1000);
                if (!string.IsNullOrEmpty(line))
                {
                    Logger.Instance.Debug(this, "receive", line);
                    ReceiveHandle(line);
                }
            }
        }

        public PacketRTSender(IPacketSender output, IPacketReceiver input)
        {
            this.input = input;
            this.output = output;
            index = 0;
            Q = 1; // placeholder value > 0
            receiveThread = new Thread(new ThreadStart(ReceiveThreadProc));
            running = true;
            receiveThread.Start();
        }

        public void Init()
        {
            var cmd = String.Format("START:");
            Logger.Instance.Debug(this, "send", cmd);
            lock (lockObj)
            {
                Logger.Instance.Debug(this, "lock", "success");
                try
                {
                    output.SendPacket(cmd);
                }
                catch (Exception e)
                {
                    Logger.Instance.Error(this, "send", e.ToString());
                }
            }
        }

        public int GetNewIndex()
	{
	    lock (lockObj)
	    {
	        return index++;
	    }
	}

        public void SendCommand(String command, int cmdIndex)
        {
            var cmd = String.Format("RT:N{0} {1}", cmdIndex, command);
            Logger.Instance.Debug(this, "send", cmd);
            lock (lockObj)
            {
                try
                {
                    output.SendPacket(cmd);
                }
                catch (Exception e)
                {
                    Logger.Instance.Error(this, "send", e.ToString());
                }
            }
        }

        public void Dispose()
        {
            Logger.Instance.Debug(this, "exit", "Disposing");
            running = false;
            receiveThread.Join();
        }
    }
}
