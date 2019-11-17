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
        public event Action EmptySlotAppeared
        { add { } remove { } }
  
        public event Action EmptySlotsEnded
        { add { } remove { } }

        public event Action<int> Indexed;

        public event Action<int> Queued;

        public event Action<int> Dropped
        { add { } remove { } }

        public event Action<int> Started;

        public event Action Reseted
        { add { } remove { } }

        public event Action<int, IReadOnlyDictionary<String, String>> Completed;

        public event Action<int, String> Failed
        { add { } remove { } }

        private int index;
        public bool HasSlots { get { return true; } }
        private IReadOnlyDictionary<String, String> opts = new Dictionary<String, String>();
        private TextWriter output;

        private object lockObj = new object();
        private Thread queueThread;
        private Thread runThread;

        private ConcurrentQueue<int> commandsQueue;
        private ConcurrentQueue<int> commandsRun;

        public void SendCommand(String command)
        {
            lock (lockObj)
            {
                var msg = "RT: " + String.Format("N{0} {1}", index, command);
                output.WriteLine(msg);
                Indexed?.Invoke(index);
                commandsQueue.Enqueue(index);
                index++;
            }
        }

        private bool running;
        private void QueueThreadProc()
        {
            while (running)
            {
                if (commandsQueue.TryDequeue(out int cmd))
                {
                    Queued?.Invoke(cmd);
                    commandsRun.Enqueue(cmd);
                }
                Thread.Sleep(10);
            }
        }

        private void RunThreadProc()
        {
            while (running)
            {
                if (commandsRun.TryDequeue(out int cmd))
                {
                    Started?.Invoke(cmd);
                    Completed?.Invoke(cmd, opts);
                }
                Thread.Sleep(10);
            }
        }

        public EmulationRTSender(TextWriter output)
        {
            this.output = output;
            running = true;
            queueThread = new Thread(new ThreadStart(QueueThreadProc));
            runThread = new Thread(new ThreadStart(RunThreadProc));
            commandsQueue = new ConcurrentQueue<int>();
            commandsRun = new ConcurrentQueue<int>();
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
