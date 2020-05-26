using System;
using Machine;
using Vector;

namespace CNCState
{
    public class AxisState : IState
    {
        public enum Units
        {
            Millimeters,
            Inches,
        }

        public enum Plane
        {
            XY,
            YZ,
            ZX
        }

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

            private decimal ToGlobalX(decimal x)
            {
                return x * Sign.x + Offset.x;
            }

            private decimal ToGlobalY(decimal y)
            {
                return y * Sign.y + Offset.y;
            }

            private decimal ToGlobalZ(decimal z)
            {
                return z * Sign.z + Offset.z;
            }

            private decimal ToLocalX(decimal x)
            {
                return (x - Offset.x) * Sign.x;
            }

            private decimal ToLocalY(decimal y)
            {
                return (y - Offset.y) * Sign.y;
            }

            private decimal ToLocalZ(decimal z)
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

            public decimal Feed { get; set; }

            public Plane CurrentPlane { get; set; }

            public Units SizeUnits { get; set; }

            public Parameters BuildCopy()
            {
                Parameters copy = new Parameters
                {
                    Absolute = Absolute,
                    CurrentPlane = CurrentPlane,
                    Feed = Feed,
                    CurrentCoordinateSystemIndex = CurrentCoordinateSystemIndex,
                    CoordinateSystems = new CoordinateSystem[CoordinateSystems.Length],
                    SizeUnits = SizeUnits,
                };
                for (int i = 0; i < copy.CoordinateSystems.Length; ++i)
                {
                    copy.CoordinateSystems[i] = CoordinateSystems[i].BuildCopy();
                }
                return copy;
            }
        }

        public decimal Feed
        {
            get => Params.Feed;
            set { Params.Feed = value; }
        }

        public bool Absolute
        {
            get => Params.Absolute;
            set { Params.Absolute = value; }
        }

        public Plane Axis
        {
            get => Params.CurrentPlane;
            set { Params.CurrentPlane = value; }
        }

        public Vector3 Position { get; set; }
        public Parameters Params { get; set; }

        public AxisState()
        {
            Params = new Parameters
            {
                Absolute = true,
                CurrentPlane = Plane.XY,
                Feed = 100,
                CoordinateSystems = new CoordinateSystem[8],
                CurrentCoordinateSystemIndex = 0,
                SizeUnits = Units.Millimeters,
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

        public void Reset()
        {
            Position.x = Position.y = Position.z = 0;
            foreach (var cs in Params.CoordinateSystems)
            {
                cs.Offset.x = cs.Offset.y = cs.Offset.z = 0;
            }
        }
    }

}
