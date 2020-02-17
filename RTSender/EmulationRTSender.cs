using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace RTSender
{
    public class EmulationRTSender : IRTSender
    {
        private class Command
        {
            public int id;
            public string command;

            public Command(int id, string command)
            {
                this.id = id;
                this.command = command;
            }
        }

        public event Action EmptySlotAppeared;
        public event Action EmptySlotsEnded;

        public event Action<int> Indexed;
        public event Action<int> Queued;
        public event Action<int> Started;
        public event Action<int, IReadOnlyDictionary<String, String>> Completed;
        public event Action<int> SlotsNumberReceived;

        public event Action Reseted;

        public event Action<int> Dropped
        { add { } remove { } }

        public event Action<int, String> Failed
        { add { } remove { } }

        private int index;
        public bool HasSlots { get { return true; } }
        private readonly Dictionary<String, String> opts = new Dictionary<String, String>();
        private TextWriter output;

        private readonly object lockObj = new object();
        private Thread queueThread;
        private Thread runThread;

        private ConcurrentQueue<Command> commandsQueue;
        private ConcurrentQueue<Command> commandsRun;
        private readonly int maxLength;

        public void SendCommand(String command)
        {
            lock (lockObj)
            {
                var msg = "RT: " + String.Format("N{0} {1}", index, command);
                output.WriteLine(msg);
                Indexed?.Invoke(index);
                commandsQueue.Enqueue(new Command(index, command));
                index++;
            }
        }

        private bool running;
        private void QueueThreadProc()
        {
            while (running)
            {
                if (commandsQueue.TryDequeue(out Command cmd))
                {
                    Queued?.Invoke(cmd.id);
                    commandsRun.Enqueue(cmd);
                    if (commandsRun.Count >= maxLength)
                    {
                        EmptySlotsEnded?.Invoke();
                    }
                    SlotsNumberReceived?.Invoke(cmd.id);
                }
                Thread.Sleep(10);
            }
        }

        private void RunThreadProc()
        {
            while (running)
            {
                if (commandsRun.TryDequeue(out Command cmd))
                {
                    Started?.Invoke(cmd.id);
                    Dictionary<string, string> results;

                    if (cmd.command == "M114" || cmd.command == "M119")
                    {
                        results = new Dictionary<string, string>();
                        if (cmd.command == "M114")
                        {
                            results["X"] = "0.0";
                            results["Y"] = "0.0";
                            results["Z"] = "0.0";
                        }
                        if (cmd.command == "M119")
                        {
                            results["EX"] = "0";
                            results["EY"] = "0";
                            results["EZ"] = "0";
                            results["EP"] = "0";
                        }
                    }
                    else
                    {
                        if (cmd.command == "M999")
                        {
                            Reseted?.Invoke();
                            continue;
                        }
                        results = opts;
                    }

                    Completed?.Invoke(cmd.id, results);
                    if (commandsRun.Count == maxLength - 1)
                    {
                        EmptySlotAppeared?.Invoke();
                    }
                    SlotsNumberReceived?.Invoke(cmd.id);
                }
                Thread.Sleep(50);
            }
        }

        public EmulationRTSender(TextWriter output)
        {
            this.output = output;
            running = true;
            queueThread = new Thread(new ThreadStart(QueueThreadProc));
            runThread = new Thread(new ThreadStart(RunThreadProc));
            commandsQueue = new ConcurrentQueue<Command>();
            commandsRun = new ConcurrentQueue<Command>();
            maxLength = 8;
            queueThread.Start();
            runThread.Start();
        }

        public void Dispose()
        {
            running = false;
            queueThread.Join();
            runThread.Join();
        }
    }    
}
