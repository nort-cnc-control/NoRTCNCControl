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
using Vector;
using System.Threading.Tasks;

namespace GCodeMachine
{
    public class GCodeMachine : IMachine, IActionExecutor, ILoggerSource
    {
        public event Action ProgramStarted;
        public event Action ProgramFinished;
        public event Action<IAction> ActionStarted;
        public event Action<IAction, CNCState.CNCState, CNCState.CNCState> ActionFinished;
        public event Action<IAction, CNCState.CNCState> ActionFailed;

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
            Failed,
            End,
            Error,
            Aborted,
        }


        public MachineState StateMachine { get; private set; }

        private ActionProgram.ActionProgram program;
        private int index;

        private IAction currentAction;
        private IAction previousAction;
        private IAction failedAction;
        private CNCState.CNCState lastState;
        public CNCState.CNCState LastState => lastState.BuildCopy();
        private readonly Config.MachineParameters config;

        private IMessageRouter messageRouter;

        private Dictionary<IAction, (CNCState.CNCState before, CNCState.CNCState after)> states;

        private bool machineIsRunning;

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
            this.rtSender = sender;
            this.rtSender.Reseted += OnReseted;
            machineIsRunning = false;
            states = new Dictionary<IAction, (CNCState.CNCState before, CNCState.CNCState after)>();
            program = null;
        }

        public void ConfigureState(CNCState.CNCState state)
        {
            lastState = state;
        }

        private bool IsErrorState(MachineState state)
        {
            return state == MachineState.Aborted || state == MachineState.Failed;
        }

        private void SwitchToState(MachineState state, bool force=false)
        {
            if (!IsErrorState(StateMachine) || force)
                StateMachine = state;
        }

        private void OnReseted()
        {
            bool run = machineIsRunning;
            SwitchToState(MachineState.Aborted);

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
            if (!run)
                reseted.Set();
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


        public void Start()
        {
            currentAction = null;
            if (program == null)
            {
                RunState = State.Stopped;
                return;
            }
            index = 0;
            SwitchToState(MachineState.Ready, true);
        }

        public void Continue()
        {
            machineIsRunning = true;
            var runThread = new Thread(new ThreadStart(Process));
            runThread.Start();
        }

        public bool IsRunning()
        {
            return machineIsRunning;
        }

        private (IAction action, CNCState.CNCState before, CNCState.CNCState after) PopAction()
        {
            if (program == null)
                return (null, null, null);
            if (index >= program.Actions.Count)
                return (null, null, null);
            return program.Actions[index++];
        }

        private void Process()
        {
            ProgramStarted?.Invoke();
            bool wasReset = false;
            CNCState.CNCState sa, sb;
            MachineState change = MachineState.Error;
            machineIsRunning = true;
            List<IAction> started = new List<IAction>();
            RunState = State.Running;
            previousAction = null;

            SwitchToState(MachineState.Ready);
            while (StateMachine != MachineState.End &&
                   StateMachine != MachineState.Error &&
                   StateMachine != MachineState.Pause)
            {
                switch (StateMachine)
                {
                    case MachineState.Idle:
                        Logger.Instance.Debug(this, "state", "Idle");
                        SwitchToState(MachineState.Ready);
                        break;
                    case MachineState.Ready:
                        Logger.Instance.Debug(this, "state", "Ready");
                        Logger.Instance.Debug(this, "action", "Pop action");
                        previousAction = currentAction;
                        (currentAction, sb, sa) = PopAction();
                        if (currentAction == null)
                        {
                            Logger.Instance.Debug(this, "action", "No more actions, end");
                            SwitchToState(MachineState.WaitEnd);
                        }
                        else
                        {
                            states.Add(currentAction, (sb, sa));
                            SwitchToState(MachineState.WaitPreviousActionFinished);
                        }
                        break;
                    case MachineState.WaitEnd:
                        Logger.Instance.Debug(this, "action", "Wait for last action end");
                        if (previousAction != null)
                            Wait(previousAction.Finished);
                        SwitchToState(MachineState.End);
                        break;
                    case MachineState.WaitPreviousActionFinished:
                        if (currentAction.RequireFinish)
                        {
                            if (previousAction != null)
                            {
                                Logger.Instance.Debug(this, "action", "Wait for previous end");
                                Wait(previousAction.Finished);
                            }
                        }
                        else
                        {
                            Logger.Instance.Debug(this, "action", "No need wait for previous end");
                        }
                        SwitchToState(MachineState.WaitActionReady);
                        break;
                    case MachineState.Failed:
                        {
                            String fail = "";
                            Logger.Instance.Warning(this, "failed", "Command has failed");
                            Stop();
                            if (failedAction is RTAction pa)
                            {
                                if (pa.ActionResult.ContainsKey("error"))
                                    fail = pa.ActionResult["error"];
                            }
                            Dictionary<string, string> message = new Dictionary<string, string>
                            {
                                ["type"] = "message",
                                ["message"] = String.Format("Command has failed : {0}", fail),
                                ["message_type"] = "command fail",
                            };

                            messageRouter.Message(message);
                            SwitchToState(MachineState.End, true);
                        }
                        break;
                    case MachineState.WaitActionReady:
                        Logger.Instance.Debug(this, "action", "Wait for action ready to run");
                        Wait(currentAction.ReadyToRun);
                        SwitchToState(MachineState.ActionRun);
                        break;
                    case MachineState.ActionRun:
                        Logger.Instance.Debug(this, "action", "Run action");
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
                        Logger.Instance.Debug(this, "action", "Machine aborted");
                        Stop();
                        wasReset = true;
                        break;
                    case MachineState.End:
                        failedAction = null;
                        currentAction = null;
                        previousAction = null;
                        Logger.Instance.Debug(this, "action", "End");
                        break;
                    case MachineState.Error:
                        Logger.Instance.Debug(this, "action", "Error");
                        break;
                }
            }

            foreach (var act in started)
            {
                act.EventStarted -= Action_OnStarted;
                act.EventFinished -= Action_OnFinished;
            }

            machineIsRunning = false;
            if (wasReset)
                reseted.Set();
            ProgramFinished?.Invoke();
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
            if (action.Failed)
            {
                failedAction = action;
                SwitchToState(MachineState.Failed);
                currentWait.Set();
                ActionFailed?.Invoke(action, states[action].before);
            }
            else
            {
                if (states[action].after != null)
                    lastState = states[action].after;
                ActionFinished?.Invoke(action, states[action].before, states[action].after);
            }
        }

        public void Stop()
        {
            SwitchToState(MachineState.End, true);
            RunState = State.Stopped;
            program = null;
        }

        public void Pause()
        {
            SwitchToState(MachineState.Pause);
            RunState = State.Paused;
        }

        public void Reboot()
        {
            reseted.Reset();
            rtSender.SendCommand("M999");
            Thread.Sleep(3000);
            rtSender.Init();
            reseted.WaitOne();
        }

        public void Abort()
        {
            reseted.Reset();
            rtSender.SendCommand("M999");
            Thread.Sleep(3000);
            rtSender.Init();
            reseted.WaitOne();
        }

        public void Dispose()
        {
            rtSender.Reseted -= OnReseted;
            program = null;
            messageRouter = null;
            states.Clear();
        }

        public void LoadProgram(ActionProgram.ActionProgram program)
        {
            this.program = program;
        }
    }
}
