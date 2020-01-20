using System;
using Machine;
using ActionExecutor;
using ActionProgram;
using Actions;
using System.Threading;
using RTSender;
using System.Collections.Generic;
using CNCState;
using Log;

namespace GCodeMachine
{
    public class GCodeMachine : IMachine, IActionExecutor, ILoggerSource
    {
        public event Action<IAction> ActionStarted;

        public enum MachineState
        {
            Idle,
            Ready,
            WaitActionReady,
            ActionRun,
            WaitActionContiniousBlockCompleted,
            WaitPreviousActionFinished,
            Pause,
            WaitEnd,
            End,
            Error,
            Aborted,
        }

        public MachineState StateMachine { get; private set; }
        private ActionProgram.ActionProgram program;
        private int index;
        private IAction currentAction;
        private IAction previousAction;
        private CNCState.CNCState lastState;
        public CNCState.CNCState LastState => lastState.BuildCopy();
        private readonly Config.MachineParameters config;
        public IMessageRouter messageRouter;

        private Dictionary<IAction, (CNCState.CNCState before, CNCState.CNCState after)> states;

        private bool machineIsRunning;

        public AxisState.CoordinateSystem CurrentCoordinateSystem =>
                LastState.AxisState.Params.CurrentCoordinateSystem;
        public int CurrentCoordinateSystemIndex =>
                LastState.AxisState.Params.CurrentCoordinateSystemIndex;
        public AxisState.CoordinateSystem HwCoordinateSystem { get; private set; }

        private EventWaitHandle currentWait;
        public State RunState { get; private set; }

        public string Name => "gcode machine";

        private IRTSender rtSender;

        private EventWaitHandle reseted;

        public GCodeMachine(IRTSender sender,
                            IMessageRouter messageRouter,
                            CNCState.CNCState state,
                            Config.MachineParameters config)
        {
            reseted = new EventWaitHandle(false, EventResetMode.AutoReset);
            this.config = config;
            this.messageRouter = messageRouter;
            lastState = state.BuildCopy();
            HwCoordinateSystem = null;
            this.rtSender = sender;
            this.rtSender.Reseted += OnReseted;
            machineIsRunning = false;
            states = new Dictionary<IAction, (CNCState.CNCState before, CNCState.CNCState after)>();
        }

        private void SwitchToState(MachineState state, bool force=false)
        {
            if (StateMachine != MachineState.Aborted || force)
                StateMachine = state;
        }

        private void OnReseted()
        {
            bool run = machineIsRunning;

            if (previousAction != null)
            {
                previousAction.Abort();
            }

            if (currentAction != null)
            {
                currentAction.Abort();
            }

            if (currentWait != null)
            {
                currentWait.Set();
            }
            SendState("init");
            SwitchToState(MachineState.Aborted);
            if (!run)
                reseted.Set();
        }

        public (Vector3 loc, string cs)
            ConvertCoordinates(Vector3 glob)
        {
            Vector3 loc;
            if (CurrentCoordinateSystem == null)
                loc = new Vector3();
            else
                loc = CurrentCoordinateSystem.ToLocal(glob);
            var id = CurrentCoordinateSystemIndex;
            return (loc, String.Format("G5{0}", 3 + id));
        }


        private void Wait(EventWaitHandle waitHandle)
        {
            currentWait = waitHandle;
            waitHandle.WaitOne();
        }

        public void Activate()
        {
            var disableBreakOnProbe = new RTAction(rtSender, new RTBreakOnProbeCommand(false));
            Wait(disableBreakOnProbe.ReadyToRun);
            disableBreakOnProbe.Run();
            Wait(disableBreakOnProbe.Finished);
        }

        private void SendState(string state)
        {
            Dictionary<string, string> message = new Dictionary<string, string>
            {
                ["type"] = "state",
                ["state"] = state,
                ["message"] = "",
            };
            messageRouter.Message(message);
        }

        public void Start()
        {
            if (program == null)
            {
                RunState = State.Stopped;
                return;
            }
            index = 0;
            SwitchToState(MachineState.Ready, true);
            Continue();
        }

        public void Continue()
        {
            var runThread = new Thread(new ThreadStart(Process));
            runThread.Start();
        }

        public void Process()
        {
            CNCState.CNCState sa, sb;
            MachineState change = MachineState.Error;
            machineIsRunning = true;
            List<IAction> started = new List<IAction>();
            RunState = State.Running;
            previousAction = null;
            SendState("running");
            SwitchToState(MachineState.Ready);
            while (StateMachine != MachineState.End &&
                   StateMachine != MachineState.Error &&
                   StateMachine != MachineState.Pause)
            {
                switch (StateMachine)
                {
                    case MachineState.Idle:
                        SwitchToState(MachineState.Ready);
                        break;
                    case MachineState.Ready:
                        previousAction = currentAction;
                        (currentAction, sb, sa) = PopAction();
                        if (currentAction == null)
                        {
                            SwitchToState(MachineState.WaitEnd);
                        }
                        else
                        {
                            states.Add(currentAction, (sb, sa));
                            SwitchToState(MachineState.WaitPreviousActionFinished);
                        }
                        break;
                    case MachineState.WaitEnd:
                        if (previousAction != null)
                            Wait(previousAction.Finished);
                        SwitchToState(MachineState.End);
                        break;
                    case MachineState.WaitPreviousActionFinished:
                        if (currentAction.RequireFinish)
                        {
                            if (previousAction != null)
                                Wait(previousAction.Finished);
                        }
                        SwitchToState(MachineState.WaitActionReady);
                        break;
                    case MachineState.WaitActionReady:
                        Wait(currentAction.ReadyToRun);
                        SwitchToState(MachineState.ActionRun);
                        break;
                    case MachineState.ActionRun:
                        currentAction.EventStarted += Action_OnStarted;
                        currentAction.EventFinished += Action_OnFinished;
                        started.Add(currentAction);
                        currentAction.Run();
                        change = StateMachine;
                        SwitchToState(MachineState.WaitActionContiniousBlockCompleted);
                        break;
                    case MachineState.WaitActionContiniousBlockCompleted:
                        if (currentAction is RTAction)
                        {
                            Logger.Instance.Debug(this, "wait", String.Format("Waiting for command {0}", (currentAction as RTAction).CommandId));
                        }
                        Wait(currentAction.ContiniousBlockCompleted);
                        if (change == MachineState.ActionRun)
                            SwitchToState(MachineState.Ready);
                        else
                            SwitchToState(change);
                        break;
                    case MachineState.Aborted:
                        Stop();
                        reseted.Set();
                        break;
                    case MachineState.End:
                        break;
                    case MachineState.Error:
                        break;
                }
            }

            foreach (var act in started)
            {
                act.EventStarted -= Action_OnStarted;
                act.EventFinished -= Action_OnFinished;
            }

            switch (StateMachine)
            {
                case MachineState.Pause:
                    SendState("paused");
                    break;
                case MachineState.End:
                case MachineState.Idle:
                    SendState("init");
                    break;
                case MachineState.Error:
                    SendState("error");
                    break;
            }
            machineIsRunning = false;
        }

        private void Action_OnStarted(IAction action)
        {
            if (action is RTAction)
                Logger.Instance.Debug(this, "started", (action as RTAction).CommandId.ToString());
            else
                Logger.Instance.Debug(this, "started", "");
            if (states[action].before != null)
                lastState = states[action].before;
            ActionStarted?.Invoke(action);
        }

        private void Action_OnFinished(IAction action)
        {
            if (states[action].after != null)
                lastState = states[action].after;
        }

        public void Stop()
        {
            SendState("init");
            SwitchToState(MachineState.End, true);
            RunState = State.Stopped;
        }

        public void Pause()
        {
            SendState("paused");
            SwitchToState(MachineState.Pause);
            RunState = State.Paused;
        }

        public void Reboot()
        {
            reseted.Reset();
            rtSender.SendCommand("M999");
            reseted.WaitOne();
        }

        public void Abort()
        {
            reseted.Reset();
            rtSender.SendCommand("M999");
            reseted.WaitOne();
        }

        private (IAction, CNCState.CNCState, CNCState.CNCState) PopAction()
        {
            if (program == null)
                return (null, null, null);
            if (index >= program.Actions.Count)
                return (null, null, null);
            return program.Actions[index++];
        }

        public void Dispose()
        {
            rtSender.Reseted -= OnReseted;
            program = null; // remove link usage
            messageRouter = null;
            states.Clear();
        }

        public void LoadProgram(ActionProgram.ActionProgram program)
        {
            this.program = program;
        }
    }
}
