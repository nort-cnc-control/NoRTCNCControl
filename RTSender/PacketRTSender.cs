using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;


namespace RTSender
{
    public class PacketRTSender : IRTSender
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
        private readonly object lockObj = new object();
        private Thread receiveThread;
        private bool running;

        private void ReceiveThreadProc()
        {
            while (running)
            {
                var line = input.ReadLine();
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
                    Console.WriteLine("Can not parse response");
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
                var cmd = String.Format("RT:N{0} {1}", index, command);
                output.WriteLine(cmd);
                output.Flush();
                Indexed?.Invoke(index);
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
