using System;
using Machine;
using Actions;
using Actions.ModbusTool;
using Actions.ModbusTool.SpindleTool;

namespace CNCState
{
    public class AxisState : IState
    {
        public Vector3 Position { get; set; }

        public bool Absolute { get; set; }

        public double Feed { get; set; }

        public RTArcMoveCommand.ArcAxis ArcAxis { get; set; }

        public AxisState()
        {
            Absolute = true;
            ArcAxis = RTArcMoveCommand.ArcAxis.XY;
            Feed = 0;
            Position = new Vector3(0, 0, 0);            
        }

        public enum MType
        {
            FastLine,
            Line,
            ArcCW,
            ArcCCW,
        }

        public MType MoveType { get; set; }
    }

    public class SpindleState
    {
        public SpindleRotationState RotationState { get; set; }

        public double SpindleSpeed { get; set; }

        public SpindleState()
        {
            RotationState = SpindleRotationState.Off;
            SpindleSpeed = 0;
        }
    }
}
