using System;
using Machine;
using ActionExecutor;
using ActionProgram;
using Actions;
using System.Threading;
using RTSender;
using System.Collections.Generic;
using CNCState;

namespace GCodeMachine
{
    public class GCodeMachine : IMachine, IActionExecutor
    {
        public event Action<IAction> ActionStarted;

        public enum MachineState
        {
            Idle,
            Ready,
            WaitActionCanRun,
            ActionRun,
            WaitActionContiniousBlockCompleted,
            WaitPreviousActionFinished,
            Pause,
            End,
            Error,
            Aborted,
        }

        public MachineState StateMachine { get; private set; }
        private ActionProgram.ActionProgram program;
        private int index;
        private IAction currentAction;
        private IAction previousAction;
        public CNCState.CNCState LastState { get; set; }
        private readonly Config.MachineParameters config;
        public IMessageRouter messageRouter;

        public AxisState.CoordinateSystem CurrentCoordinateSystem =>
                LastState.AxisState.Params.CurrentCoordinateSystem;
        public int CurrentCoordinateSystemIndex =>
                LastState.AxisState.Params.CurrentCoordinateSystemIndex;
        public AxisState.CoordinateSystem HwCoordinateSystem { get; private set; }

        private EventWaitHandle currentWait;
        public State RunState { get; private set; }

        private IRTSender rtSender;

        public GCodeMachine(IRTSender sender,
                            IMessageRouter messageRouter,
                            CNCState.CNCState state,
                            Config.MachineParameters config,
                            Vector3 hwCoords)
        {
            this.config = config;
            this.messageRouter = messageRouter;
            LastState = state;
            HwCoordinateSystem = null;
            this.rtSender = sender;
            this.rtSender.Reseted += OnReseted;

            Vector3 crds = hwCoords;
            var pos = LastState.AxisState.Position;
            var sign = new Vector3(config.invert_x ? -1 : 1, config.invert_y ? -1 : 1, config.invert_z ? -1 : 1);
            HwCoordinateSystem = new AxisState.CoordinateSystem
            {
                Sign = sign,
                Offset = new Vector3(crds.x - sign.x * pos.x,
                                     crds.y - sign.y * pos.y,
                                     crds.z - sign.z * pos.z)
            };
        }

        private void SwitchToState(MachineState state, bool force=false)
        {
            if (StateMachine != MachineState.Aborted || force)
                StateMachine = state;
        }

        private void OnReseted()
        {
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
        }

        public (Vector3 glob, Vector3 loc, string cs)
            ConvertCoordinates(Vector3 hw)
        {
            Vector3 glob;
            if (HwCoordinateSystem == null)
                glob = new Vector3();
            else
                glob = HwCoordinateSystem.ToLocal(hw);

            Vector3 loc;
            if (CurrentCoordinateSystem == null)
                loc = new Vector3();
            else
                loc = CurrentCoordinateSystem.ToLocal(glob);
            var id = CurrentCoordinateSystemIndex;
            return (glob, loc, String.Format("G5{0}", 3 + id));
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
            bool fast_exit = false;
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
                        (currentAction, _) = PopAction();
                        if (currentAction == null)
                        {
                            SwitchToState(MachineState.End);
                        }
                        else
                        {
                            SwitchToState(MachineState.WaitActionCanRun);
                        }
                        break;
                    case MachineState.WaitActionCanRun:
                        Wait(currentAction.ReadyToRun);
                        if (currentAction.RequireFinish)
                            SwitchToState(MachineState.WaitPreviousActionFinished);
                        else
                            SwitchToState(MachineState.ActionRun);
                        break;
                    case MachineState.WaitPreviousActionFinished:
                        if (previousAction != null)
                            Wait(previousAction.Finished);
                        SwitchToState(MachineState.ActionRun);
                        break;
                    case MachineState.ActionRun:
                        SwitchToState(MachineState.WaitActionContiniousBlockCompleted);
                        currentAction.EventStarted += Action_OnStarted;
                        started.Add(currentAction);
                        currentAction.Run();
                        break;
                    case MachineState.WaitActionContiniousBlockCompleted:
                        Wait(currentAction.ContiniousBlockCompleted);
                        SwitchToState(MachineState.Ready);
                        break;
                    case MachineState.Aborted:
                        Stop();
                        fast_exit = true;
                        break;
                    case MachineState.End:
                        break;
                    case MachineState.Error:
                        fast_exit = true;
                        break;
                }
            }
            if (!fast_exit)
            {
                foreach (var act in started)
                {
                    act.Finished.WaitOne();
                }
            }
            foreach (var act in started)
            {
                act.EventStarted -= Action_OnStarted;
            }

            switch (StateMachine)
            {
                case MachineState.Pause:
                    SendState("pause");
                    break;
                case MachineState.End:
                case MachineState.Idle:
                    SendState("init");
                    break;
                case MachineState.Error:
                    SendState("error");
                    break;
            }
        }

        void Action_OnStarted(IAction obj)
        {
            Console.WriteLine("STARTED event");
            ActionStarted?.Invoke(obj);
        }

        public void Stop()
        {
            SendState("init");
            SwitchToState(MachineState.End);
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
            rtSender.SendCommand("M999");
        }

        public void Abort()
        {
            rtSender.SendCommand("M999");
        }

        private (IAction, CNCState.CNCState) PopAction()
        {
            if (program == null)
                return (null, null);
            if (index >= program.Actions.Count)
                return (null, null);
            return program.Actions[index++];
        }

        public void Dispose()
        {
            program = null; // remove link usage
            messageRouter = null;
        }

        public void LoadProgram(ActionProgram.ActionProgram program)
        {
            this.program = program;
        }
    }
}
