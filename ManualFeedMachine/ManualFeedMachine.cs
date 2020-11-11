using System;
using System.Threading;
using Actions;
using Machine;
using Vector;

namespace ManualFeedMachine
{
    public class ManualFeedMachine : IMachine
    {
        public State RunState { get; private set; }

        private readonly Config.MachineParameters config;
        private readonly RTSender.IRTSender rtSender;
        private readonly CancellationToken token;
        private readonly CancellationTokenSource tokenSource;
        private CancellationToken runStopToken;
        private CancellationTokenSource runStopTokenSource;
        private Thread moveThread;
        private EventWaitHandle waiter;

        private decimal cfx, cfy, cfz, cdt;
        private IAction first_action, second_action;

        public ManualFeedMachine(RTSender.IRTSender rtSender, Config.MachineParameters config)
        {
            RunState = State.Stopped;
            tokenSource = new CancellationTokenSource();
            token = tokenSource.Token;
            this.rtSender = rtSender;
            this.config = config;
            waiter = null;
        }

        public void Abort()
        {
        }

        public void Activate()
        {

        }

        public void Continue()
        {

        }

        public void Dispose()
        {
            Stop();
            tokenSource.Cancel();
        }

        public void Pause()
        {

        }

        public void Reboot()
        {

        }

        public void Start()
        {
            runStopTokenSource = new CancellationTokenSource();
            runStopToken = runStopTokenSource.Token;
            var action = new RTAction(rtSender, new RTLockCommand(false));
            action.Run();
            moveThread = new Thread(new ThreadStart(Loop));
            moveThread.Start();
            RunState = State.Running;
        }

        public void Stop()
        {
            if (waiter != null)
                waiter.Set();
            runStopTokenSource.Cancel();
            moveThread.Join();
            RunState = State.Stopped;
        }

        public void SetFeed(decimal fx, decimal fy, decimal fz, decimal dt)
        {
            cfx = fx/60;
            cfy = fy/60;
            cfz = fz/60;
            cdt = dt;
        }

        private void Wait(EventWaitHandle wait)
        {
            waiter = wait;
            waiter.WaitOne();
            waiter = null;
        }

        private void Loop()
        {
            while (!runStopToken.IsCancellationRequested)
            {
                second_action = BuildStepAction(cfx, cfy, cfz, cdt);
                if (second_action != null)
                {
                    Wait(second_action.ReadyToRun);
                    second_action.Run();
                }
                if (first_action != null)
                {
                    Wait(first_action.Finished);
                    if (first_action.Failed)
                    {
                        second_action = null;
                        runStopTokenSource.Cancel();
                    }
                }
                first_action = second_action;
                second_action = null;
            }
            RunState = State.Stopped;
        }

        private IAction BuildStepAction(decimal fx, decimal fy, decimal fz, decimal dt)
        {
            var feed = (decimal)(Math.Sqrt((double)(fx * fx + fy * fy + fz * fz)));
            var delta = new Vector3(fx * dt, fy * dt, fz * dt);
            if (delta.Length() == 0)
                return null;
            var opts = new RTMovementOptions
            {
                acceleration = config.max_acceleration,
                Feed = feed,
                FeedStart = feed,
                FeedEnd = feed
            };
            var command = new RTLineMoveCommand(delta, opts, config);
            var action = new RTAction(rtSender, command);
            return action;
        }
    }
}
