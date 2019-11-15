using System;
using Machine;
using Actions;
using Actions.ModbusTool;
using Actions.Tools.SpindleTool;
using System.Collections.Generic;

namespace CNCState
{
    public class AxisState : IState
    {
        public class CoordinateSystem
        {
            public CoordinateSystem()
            {
                Offset = new Vector3();
            }

            public Vector3 Offset { get; set; }

            public Vector3 ToLocal(Vector3 P)
            {
                return P - Offset;
            }

            public Vector3 ToGlobal(Vector3 P)
            {
                return P + Offset;
            }

            public double ToGlobalX(double x)
            {
                return x + Offset.x;
            }

            public double ToGlobalY(double y)
            {
                return y + Offset.y;
            }

            public double ToGlobalZ(double z)
            {
                return z + Offset.z;
            }
        }

        public class Parameters
        {
            public CoordinateSystem CurrentCoordinateSystem => CoordinateSystems[CurrentCoordinateSystemIndex];

            public CoordinateSystem[] CoordinateSystems { get; set; }

            public int CurrentCoordinateSystemIndex { get; set; }

            public bool Absolute { get; set; }

            public double Feed { get; set; }

            public RTArcMoveCommand.ArcAxis ArcAxis { get; set; }

            public Parameters BuildCopy()
            {
                Parameters copy = new Parameters
                {
                    Absolute = Absolute,
                    ArcAxis = ArcAxis,
                    Feed = Feed,
                    CurrentCoordinateSystemIndex = CurrentCoordinateSystemIndex,
                    CoordinateSystems = CoordinateSystems, // Not a copy
                };
                return copy;
            }
        }

        public double Feed
        {
            get => Params.Feed;
            set { Params.Feed = value; }
        }

        public bool Absolute
        {
            get => Params.Absolute;
            set { Params.Absolute = value; }
        }

        public RTArcMoveCommand.ArcAxis ArcAxis
        {
            get => Params.ArcAxis;
            set { Params.ArcAxis = value; }
        }

        public Vector3 Position { get; set; }
        public Parameters Params { get; set; }

        public AxisState()
        {
            Params = new Parameters
            {
                Absolute = true,
                ArcAxis = RTArcMoveCommand.ArcAxis.XY,
                Feed = 0,
                CoordinateSystems = new CoordinateSystem[8],
                CurrentCoordinateSystemIndex = 0,
            };
            for (int i = 0; i < 8; i++)
            {
                Params.CoordinateSystems[i] = new CoordinateSystem();
            }
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
