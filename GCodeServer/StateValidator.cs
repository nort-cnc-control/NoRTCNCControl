using System;
namespace GCodeServer
{
    internal class StateValidator
    {
        public StateValidator()
        {
        }

        private Actions.RTAction action;
        private CNCState.CNCState stateAfter;

        public void ActionStarted(Actions.RTAction action, CNCState.CNCState stateAfter)
        {
            this.action = action;
            this.stateAfter = stateAfter;
        }

        public void ActionCompleted()
        {
            var result = action.ActionResult;
            var expectedPos = stateAfter.AxisState.Position;
            if (result.ContainsKey("X"))
            { 
                
            }
        }
    }
}
