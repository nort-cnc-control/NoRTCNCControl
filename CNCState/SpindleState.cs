using System;
using Machine;

namespace CNCState
{
    public class SpindleState : IToolState
    {
        public enum SpindleRotationState
        {
            Off,
            Clockwise,
            CounterClockwise,
        }

        public SpindleRotationState RotationState { get; set; }

        public decimal SpindleSpeed { get; set; }

        public SpindleState()
        {
            RotationState = SpindleRotationState.Off;
            SpindleSpeed = 0;
        }

        public SpindleState BuildCopy()
        {
            return new SpindleState
            {
                RotationState = RotationState,
                SpindleSpeed = SpindleSpeed,
            };
        }
    }

}
