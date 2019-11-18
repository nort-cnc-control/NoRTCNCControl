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
                Sign = new Vector3(1, 1, 1);
            }

            public Vector3 Offset { get; set; }
            public Vector3 Sign { get; set; }

            public Vector3 ToLocal(Vector3 P)
            {
                return new Vector3(ToLocalX(P.x), ToLocalY(P.y), ToLocalZ(P.z));
            }

            public Vector3 ToGlobal(Vector3 P)
            {
                return new Vector3(ToGlobalX(P.x), ToGlobalY(P.y), ToGlobalZ(P.z));
            }

            public double ToGlobalX(double x)
            {
                return x * Sign.x + Offset.x;
            }

            public double ToGlobalY(double y)
            {
                return y * Sign.y + Offset.y;
            }

            public double ToGlobalZ(double z)
            {
                return z * Sign.z + Offset.z;
            }

            public double ToLocalX(double x)
            {
                return (x - Offset.x) * Sign.x;
            }

            public double ToLocalY(double y)
            {
                return (y - Offset.y) * Sign.y;
            }

            public double ToLocalZ(double z)
            {
                return (z - Offset.z) * Sign.z;
            }

            public CoordinateSystem BuildCopy()
            {
                CoordinateSystem cs = new CoordinateSystem
                {
                    Offset = new Vector3(Offset.x, Offset.y, Offset.z),
                };
                return cs;
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
                    CoordinateSystems = new CoordinateSystem[CoordinateSystems.Length],
                };
                for (int i = 0; i < copy.CoordinateSystems.Length; ++i)
                {
                    copy.CoordinateSystems[i] = CoordinateSystems[i].BuildCopy();
                }
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

        public AxisState BuildCopy()
        {
            var copy = new AxisState
            {
                MoveType = MoveType,
                Params = Params.BuildCopy(),
                Position = new Vector3(Position.x, Position.y, Position.z),
            };
            return copy;
        }
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

        public SpindleState BuildCopy()
        {
            return new SpindleState
            {
                RotationState = RotationState,
                SpindleSpeed = SpindleSpeed,
            };
        }
    }

    public class CNCState
    {
        public CNCState(AxisState axisState, SpindleState spindleState)
        {
            AxisState = axisState;
            SpindleState = spindleState;
        }

        public CNCState BuildCopy()
        {
            return new CNCState(AxisState.BuildCopy(), SpindleState.BuildCopy());
        }

        public AxisState AxisState { get; private set; }
        public SpindleState SpindleState { get; private set; }
    }
}
