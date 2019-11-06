using System;
using Machine;
using ActionProgram;
using Actions;
using System.Threading.Tasks;
using RTSender;

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
        }

        public MachineState StateMachine { get; private set; }
        private ActionProgram.ActionProgram program;
        private int index;
        private IAction currentAction;
        public State RunState { get; private set; }

        private IRTSender sender;

        public GCodeMachine(IRTSender sender)
        {
            this.sender = sender;
        }

        public void Activate()
        {
            var disableBreakOnProbe = new RTAction(sender, new RTBreakOnProbeCommand(false));
            disableBreakOnProbe.ReadyToRun.WaitOne();
            disableBreakOnProbe.Run();
            disableBreakOnProbe.Finished.WaitOne();
        }

        public void Start()
        {
            if (program == null)
            {
                RunState = State.Stopped;
                return;
            }
            index = 0;
            StateMachine = MachineState.Ready;
            Continue();
        }

        public void Continue()
        {
            RunState = State.Running;
            IAction previousAction = null;
            while (StateMachine != MachineState.End &&
                   StateMachine != MachineState.Error &&
                   StateMachine != MachineState.Pause)
            {
                switch (StateMachine)
                {
                    case MachineState.Idle:
                        StateMachine = MachineState.Ready;
                        break;
                    case MachineState.Ready:
                        previousAction = currentAction;
                        currentAction = PopAction();
                        if (currentAction == null)
                            StateMachine = MachineState.End;
                        else
                            StateMachine = MachineState.WaitActionCanRun;
                        break;
                    case MachineState.WaitActionCanRun:
                        currentAction.ReadyToRun.WaitOne();
                        if (currentAction.RequireFinish)
                            StateMachine = MachineState.WaitPreviousActionFinished;
                        else
                            StateMachine = MachineState.ActionRun;
                        break;
                    case MachineState.WaitPreviousActionFinished:
                        if (previousAction != null)
                            previousAction.Finished.WaitOne();
                        StateMachine = MachineState.ActionRun;
                        break;
                    case MachineState.ActionRun:
                        StateMachine = MachineState.WaitActionContiniousBlockCompleted;
                        currentAction.Run();
                        break;
                    case MachineState.WaitActionContiniousBlockCompleted:
                        currentAction.ContiniousBlockCompleted.WaitOne();
                        StateMachine = MachineState.Ready;
                        break;
                    case MachineState.End:
                        break;
                    case MachineState.Error:
                        break;
                }
            }
        }

        public void Stop()
        {
            StateMachine = MachineState.End;
            RunState = State.Stopped;
        }

        public void Pause()
        {
            StateMachine = MachineState.Pause;
            RunState = State.Paused;
        }

        public void Reboot()
        {
            sender.SendCommand("M999");
        }

        public void Abort()
        {
            
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
