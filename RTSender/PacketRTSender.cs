using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Log;

namespace RTSender
{
    public class PacketRTSender : IRTSender, ILoggerSource
    {
        public event Action EmptySlotAppeared;
        public event Action EmptySlotsEnded;

        public event Action Reseted;
        public event Action<int> Indexed;
        public event Action<int> Queued;
        public event Action<int> Dropped;
        public event Action<int> Started;
        public event Action<int, IReadOnlyDictionary<String, String>> Completed;
        public event Action<int, String> Failed;

        StreamReader input;
        StreamWriter output;
        private int index;
        private int q;
        private int Q {
            get => q;
            set {
                int oldq = q;
                q = value;
                if (oldq == 0 && value > 0)
                {
                    EmptySlotAppeared?.Invoke();
                }
                else if (oldq > 0 && value == 0)
                {
                    EmptySlotsEnded?.Invoke();
                }
            }
        }
        public bool HasSlots { get { return Q > 0; } }

        public string Name => "Packet sender";

        private readonly object lockObj = new object();
        private Thread receiveThread;
        private bool running;

        private void ReceiveThreadProc()
        {
            while (running)
            {
                var line = input.ReadLine();
                Logger.Instance.Debug(this, "receive", line);
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
                        default:
                            break;
                    }
                    if (args.Values.ContainsKey("Q"))
                    {
                        Q = int.Parse(args.Values["Q"]);
                    }
                }
                catch
                {
                    Logger.Instance.Error(this, "parse error", line);
                }
            }
        }

        public PacketRTSender(StreamWriter output, StreamReader input)
        {
            this.input = input;
            this.output = output;
            index = 0;
            q = 1; // placeholder value > 0
            receiveThread = new Thread(new ThreadStart(ReceiveThreadProc));
            running = true;
            receiveThread.Start();
        }

        public void SendCommand(String command)
        {
            lock (lockObj)
            {
                Indexed?.Invoke(index);
                var cmd = String.Format("RT:N{0} {1}", index, command);
                Logger.Instance.Debug(this, "send", cmd);
                output.WriteLine(cmd);
                output.Flush();
                index++;
            }
        }

        public void Dispose()
        {
            output.WriteLine("EXIT:");
            running = false;
            receiveThread.Join();
        }
    }
}
