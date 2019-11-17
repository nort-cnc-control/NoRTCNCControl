using System;
using Machine;
using ActionProgram;
using Actions;
using System.Threading;
using RTSender;
using System.Collections.Generic;

namespace GCodeMachine
{
    public class GCodeMachine : IMachine
    {
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

        private EventWaitHandle currentWait;
        public State RunState { get; private set; }

        private IRTSender rtSender;

        public GCodeMachine(IRTSender sender)
        {
            this.rtSender = sender;
            this.rtSender.Reseted += OnReseted;
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
            RunState = State.Running;
            previousAction = null;
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
                        currentAction = PopAction();
                        if (currentAction == null)
                            SwitchToState(MachineState.End);
                        else
                            SwitchToState(MachineState.WaitActionCanRun);
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
                        currentAction.Run();
                        break;
                    case MachineState.WaitActionContiniousBlockCompleted:
                        Wait(currentAction.ContiniousBlockCompleted);
                        SwitchToState(MachineState.Ready);
                        break;
                    case MachineState.Aborted:
                        Stop();
                        break;
                    case MachineState.End:
                        break;
                    case MachineState.Error:
                        break;
                }
            }
        }

        public Vector3 ReadCurrentCoordinates()
        {
            RTAction action = new RTAction(rtSender, new RTGetPositionCommand());
            // action.ReadyToRun.WaitOne();
            action.Run();
            action.Finished.WaitOne();
            return new Vector3(double.Parse(action.ActionResult["X"]),
                               double.Parse(action.ActionResult["Y"]),
                               double.Parse(action.ActionResult["Z"]));
        }

        public (bool ex, bool ey, bool ez, bool ep) ReadCurrentEndstops()
        {
            RTAction action = new RTAction(rtSender, new RTGetEndstopsCommand());
            // action.ReadyToRun.WaitOne();
            action.Run();
            action.Finished.WaitOne();
            return (action.ActionResult["EX"] == "1",
                    action.ActionResult["EY"] == "1",
                    action.ActionResult["EZ"] == "1",
                    action.ActionResult["EP"] == "1");
        }

        public void Stop()
        {
            SwitchToState(MachineState.End);
            RunState = State.Stopped;
        }

        public void Pause()
        {
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

        private IAction PopAction()
        {
            if (program == null)
                return null;
            if (index >= program.Actions.Count)
                return null;
            return program.Actions[index++];
        }

        public void Dispose()
        {
            program = null; // remove link usage
        }

        public void LoadProgram(ActionProgram.ActionProgram program)
        {
            this.program = program;
        }
    }
}
