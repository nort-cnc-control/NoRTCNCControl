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

        private class ProgramStack
        {
            private class ProgramState
            {
                public ActionProgram.ActionProgram Program { get; set; }
                public int Index { get; set; }
            }

            private List<ProgramState> programStack;

            public ProgramStack()
            {
                programStack = new List<ProgramState>();
            }

            public ActionProgram.ActionProgram Program
            {
                get
                {
                    if (programStack.Count == 0)
                        return null;
                    return programStack[programStack.Count - 1].Program;
                }
            }

            public int Index
            {
                get
                {
                    if (programStack.Count == 0)
                        return -1;
                    return programStack[programStack.Count - 1].Index;
                }
                set
                {
                    programStack[programStack.Count - 1].Index = value;
                }
            }

            public void Clear()
            {
                programStack.Clear();
            }

            public void Pop()
            {
                if (programStack.Count > 0)
                    programStack.RemoveAt(programStack.Count - 1);
            }

            public void Push(ActionProgram.ActionProgram program, int index = 0)
            {
                programStack.Add(new ProgramState { Program = program, Index = index });
            }
        }

        private ProgramStack programStack;

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
            programStack = new ProgramStack();
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
            SendState("init");
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

        private void SendState(string state)
        {
            Dictionary<string, string> message = new Dictionary<string, string>
            {
                ["type"] = "state",
                ["state"] = state,
                ["message"] = "",
            };
            Logger.Instance.Debug(this, "message state", state);
            messageRouter.Message(message);
        }

        public void Start()
        {
            if (messageRouter == null)
                throw new InvalidProgramException();
            currentAction = null;
            if (programStack.Program == null)
            {
                RunState = State.Stopped;
                return;
            }
            programStack.Index = 0;
            SwitchToState(MachineState.Ready, true);
        }

        public void Continue()
        {
            if (messageRouter == null)
                throw new InvalidProgramException();
            machineIsRunning = true;
            var runThread = new Thread(new ThreadStart(Process));
            runThread.Start();
        }

        public bool IsRunning()
        {
            return machineIsRunning;
        }

        private void Process()
        {
            bool wasReset = false;
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
                            programStack.Pop();
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
                            programStack.Clear();
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

            switch (StateMachine)
            {
                case MachineState.Pause:
                    SendState("paused");
                    break;
                case MachineState.End:
                case MachineState.Idle:
                    if (programStack.Program == null)
                        SendState("init");
                    else
                        SendState("paused");
                    break;
                case MachineState.Error:
                    SendState("error");
                    break;
            }
            machineIsRunning = false;
            if (wasReset)
                reseted.Set();
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
            if (messageRouter == null)
                throw new InvalidProgramException();
            SendState("init");
            SwitchToState(MachineState.End, true);
            RunState = State.Stopped;
            programStack.Clear();
        }

        public void Pause()
        {
            if (messageRouter == null)
                throw new InvalidProgramException();
            SendState("paused");
            SwitchToState(MachineState.Pause);
            RunState = State.Paused;
        }

        public void Reboot()
        {
            if (messageRouter == null)
                throw new InvalidProgramException();
            reseted.Reset();
            rtSender.SendCommand("M999");
            Thread.Sleep(3000);
            rtSender.Init();
            reseted.WaitOne();
        }

        public void Abort()
        {
            if (messageRouter == null)
                throw new InvalidProgramException();
            reseted.Reset();
            rtSender.SendCommand("M999");
            Thread.Sleep(3000);
            rtSender.Init();
            reseted.WaitOne();
        }

        private (IAction, CNCState.CNCState, CNCState.CNCState) PopAction()
        {
            if (programStack.Program == null)
                return (null, null, null);
            if (programStack.Index >= programStack.Program.Actions.Count)
                return (null, null, null);
            return programStack.Program.Actions[programStack.Index++];
        }

        public void Dispose()
        {
            rtSender.Reseted -= OnReseted;
            programStack.Clear();
            messageRouter = null;
            states.Clear();
        }

        public void LoadProgram(ActionProgram.ActionProgram program)
        {
            if (messageRouter == null)
                throw new InvalidProgramException();
            programStack.Push(program);
        }
    }
}
