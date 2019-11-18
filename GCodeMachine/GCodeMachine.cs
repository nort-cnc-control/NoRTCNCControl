using System;
using Machine;
using ActionProgram;
using Actions;
using System.Threading;
using RTSender;
using System.Collections.Generic;
using CNCState;

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
        public CNCState.CNCState LastState { get; set; }
        private readonly Config.MachineParameters config;

        private AxisState.CoordinateSystem CurrentCoordinateSystem =>
                LastState.AxisState.Params.CurrentCoordinateSystem;

        private AxisState.CoordinateSystem hwCoordinateSystem;

        private EventWaitHandle currentWait;
        public State RunState { get; private set; }

        private IRTSender rtSender;

        public GCodeMachine(IRTSender sender, CNCState.CNCState state, Config.MachineParameters config)
        {
            this.config = config;
            LastState = state;
            hwCoordinateSystem = null;
            this.rtSender = sender;
            this.rtSender.Reseted += OnReseted;

            var crds = ReadHardwareCoordinates();
            var pos = LastState.AxisState.Position;
            var sign = new Vector3(config.invert_x ? -1 : 1, config.invert_y ? -1 : 1, config.invert_z ? -1 : 1);
            hwCoordinateSystem = new AxisState.CoordinateSystem
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
                        CNCState.CNCState state;
                        (currentAction, state) = PopAction();
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

        private Vector3 ReadHardwareCoordinates()
        {
            RTAction action = new RTAction(rtSender, new RTGetPositionCommand());
            // action.ReadyToRun.WaitOne();
            action.Run();
            action.Finished.WaitOne(1000);
            return new Vector3(double.Parse(action.ActionResult["X"]),
                               double.Parse(action.ActionResult["Y"]),
                               double.Parse(action.ActionResult["Z"]));
        }

        public (Vector3 hw, Vector3 glob, Vector3 loc, String coordinate_system) ReadCurrentCoordinates()
        {
            Vector3 hw = ReadHardwareCoordinates();

            Vector3 glob;
            if (hwCoordinateSystem == null)
                glob = new Vector3();
            else
                glob = hwCoordinateSystem.ToLocal(hw);

            Vector3 loc;
            if (CurrentCoordinateSystem == null)
                loc = new Vector3();
            else
                loc = CurrentCoordinateSystem.ToLocal(glob);
            var id = LastState.AxisState.Params.CurrentCoordinateSystemIndex;
            return (hw, glob, loc, String.Format("G5{0}", 3 + id));
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
        }

        public void LoadProgram(ActionProgram.ActionProgram program)
        {
            this.program = program;
        }
    }
}
