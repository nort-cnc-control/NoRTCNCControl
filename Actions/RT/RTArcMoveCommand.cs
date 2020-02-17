using System;
using Config;
using Vector;
using CNCState;

namespace Actions
{
    public class RTArcMoveCommand : IRTMoveCommand
    {
        public bool CommandIsCached { get { return true; } }
        
        private MachineParameters config;
        private Vector2 deltaProj;
        private Vector2 startToCenterProj;
        private Vector2 endToCenterProj;

        public AxisState.Plane Plane { get; private set; }
        public RTMovementOptions Options { get; private set; }
        public Vector3 Delta { get; private set; }
        public bool CCW { get; private set; }
        public double R { get; private set; }

        public double HordeCenterDistance { get; private set; }
        public Vector3 DirStart { get; private set; }
        public Vector3 DirEnd { get; private set; }
        public double Length { get; private set; }
        public double Angle { get; private set; }

        private readonly double eps = 1e-6;

        private bool left_basis
        {
            get
            {
                bool left = false;
                switch (Plane)
                {
                    case AxisState.Plane.XY:
                        if (config.invert_x)
                            left = !left;
                        if (config.invert_y)
                            left = !left;
                        return left;
                    case AxisState.Plane.YZ:
                        if (config.invert_y)
                            left = !left;
                        if (config.invert_z)
                            left = !left;
                        return left;
                    case AxisState.Plane.ZX:
                        if (config.invert_z)
                            left = !left;
                        if (config.invert_x)
                            left = !left;
                        return left;
                    default:
                        throw new ArgumentOutOfRangeException("Invalid arc plane selection");
                }   
            }
        }

        private String plane_cmd
        {
            get
            {
                switch (Plane)
                {
                    case AxisState.Plane.XY:
                        return "G17";
                    case AxisState.Plane.YZ:
                        return "G18";
                    case AxisState.Plane.ZX:
                        return "G19";
                    default:
                        throw new ArgumentOutOfRangeException("Invalid arc plane selection");
                }
            }
        }
    
        private String MoveCmd
        {
            get
            {
                bool hwccw;
                if (left_basis)
                    hwccw = !CCW;
                else
                    hwccw = CCW;

                if (hwccw)
                    return "G3";
                else
                    return "G2";            
            }
        }

        private string FormatD(double x)
        {
            return x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        public String Command
        {
            get
            {   
                bool hwccw;
                double hwhcl;
                Vector3 hwdelta = new Vector3();

                if (config.invert_x)
                    hwdelta.x = -Delta.x;
                else
                    hwdelta.x = Delta.x;
                
                if (config.invert_y)
                    hwdelta.y = -Delta.y;
                else
                    hwdelta.y = Delta.y;

                if (config.invert_z)
                    hwdelta.z = -Delta.z;
                else
                    hwdelta.z = Delta.z;

                if (left_basis)
                {
                    hwhcl = -HordeCenterDistance;
                    hwccw = !CCW;
                }
                else
                {
                    hwhcl = HordeCenterDistance;
                    hwccw = CCW;
                }
                return $"{MoveCmd} {plane_cmd} {Options.Command} X{FormatD(hwdelta.x)} Y{FormatD(hwdelta.y)} Z{FormatD(hwdelta.z)} D{FormatD(hwhcl)}";
            }
        }

        private double FindHordeCenterDistanceR(Vector2 delta, bool ccw, double R)
        {
            double D = delta.Length();
            bool big_arc = (R < 0);
            R = Math.Abs(R);
            double s;
            if (R > D / 2)
            {
                s = Math.Sqrt(R * R - D * D / 4);
            }
            else if (R > D / 2 - eps)
            {
                s = 0;
            }
            else
            {
                throw new ArgumentOutOfRangeException(String.Format("Too small radius {0}, minimal {1}", R, D / 2));
            }

            int center_side;
            if (ccw == big_arc)
                center_side = 1;
            else
                center_side = -1;
            double hcl = s * center_side;
            return hcl;
        }

        private double FindHordeCenterDistanceIJK(Vector2 delta, bool ccw, Vector2 center)
        {
            double D = delta.Length();
            double R = center.Length();
            double s;
            if (R > D / 2)
            {
                s = Math.Sqrt(R * R - D * D / 4);
            }
            else if (R > D / 2 - eps)
            {
                s = 0;
            }
            else
            {
                throw new ArgumentOutOfRangeException(String.Format("Too small radius {0}, minimal {1}", R, D / 2));
            }

            int center_side = Math.Sign(delta.Right() * center);
            double hcl = s * center_side;
            return hcl;
        }

        private (Vector2, Vector2) FindTangents(Vector2 startToCenter, Vector2 endToCenter, bool ccw)
        {
            var st = startToCenter.Right();
            var et = endToCenter.Right();
            if (!ccw)
            {
                st = -st;
                et = -et;
            }
            return (st / st.Length(), et / et.Length());
        }

        private Vector2 VectorPlaneProj(Vector3 delta, AxisState.Plane plane)
        {
            switch (plane)
            {
                case AxisState.Plane.XY:
                    return new Vector2(delta.x, delta.y);
                case AxisState.Plane.YZ:
                    return new Vector2(delta.y, delta.z);
                case AxisState.Plane.ZX:
                    return new Vector2(delta.z, delta.x);
                default:
                    throw new ArgumentOutOfRangeException("Invalid axis");
            }
        }

        private double VectorPlaneNormalProj(Vector3 delta, AxisState.Plane plane)
        {
            switch (plane)
            {
                case AxisState.Plane.XY:
                    return delta.z;
                case AxisState.Plane.YZ:
                    return delta.x;
                case AxisState.Plane.ZX:
                    return delta.y;
                default:
                    throw new ArgumentOutOfRangeException("Invalid axis");
            }
        }

        private void FillDirs()
        {
            var tans = FindTangents(startToCenterProj, endToCenterProj, CCW);
            Vector2 startTan = tans.Item1;
            Vector2 endTan = tans.Item2;

            switch (Plane)
            {
                case AxisState.Plane.XY:
                    DirStart = new Vector3(startTan.x, startTan.y, 0);
                    DirEnd = new Vector3(endTan.x, endTan.y, 0);
                    break;
                case AxisState.Plane.YZ:
                    DirStart = new Vector3(0, startTan.x, startTan.y);
                    DirEnd = new Vector3(0, endTan.x, endTan.y);
                    break;
                case AxisState.Plane.ZX:
                    DirStart = new Vector3(startTan.y, 0, startTan.x);
                    DirEnd = new Vector3(endTan.y, 0, endTan.x);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Invalid axis");
            }
        }

        
        public RTArcMoveCommand(Vector3 delta, double R, bool ccw, AxisState.Plane plane,
                                RTMovementOptions opts, MachineParameters config)
        {
            double h = VectorPlaneNormalProj(delta, plane);
            if (Math.Abs(h) > eps)
            {
                throw new ArgumentOutOfRangeException("Only arc supported, not helix");
            }

            this.config = config;
            Delta = delta;
            CCW = ccw;
            Plane = plane;
            Options = opts;
            deltaProj = VectorPlaneProj(Delta, Plane);
            HordeCenterDistance = FindHordeCenterDistanceR(deltaProj, CCW, R);
            startToCenterProj = deltaProj / 2 + Vector2.Normalize(deltaProj.Right()) * HordeCenterDistance;
            endToCenterProj = startToCenterProj - deltaProj;
            FillDirs();
            this.R = Math.Abs(R);
            Angle = 2*Math.Asin(deltaProj.Length()/2/R);
            if (HordeCenterDistance > 0 && CCW || HordeCenterDistance < 0 && !CCW)
            {
                Angle = Math.PI*2 - Angle;
            }
            Length = Angle * R;
        }

        public RTArcMoveCommand(Vector3 delta, Vector3 startToCenter, bool ccw, AxisState.Plane plane,
                                RTMovementOptions opts, MachineParameters config)
        {
            double h = VectorPlaneNormalProj(delta, plane);
            if (Math.Abs(h) > eps)
            {
                throw new ArgumentOutOfRangeException("Only arc supported, not helix");
            }
            this.config = config;
            Delta = delta;
            CCW = ccw;
            Plane = plane;
            Options = opts;
            deltaProj = VectorPlaneProj(Delta, Plane);
            startToCenterProj = VectorPlaneProj(startToCenter, Plane);
            endToCenterProj = startToCenterProj - deltaProj;
            HordeCenterDistance = FindHordeCenterDistanceIJK(deltaProj, CCW, startToCenterProj);
            FillDirs();
            R = startToCenter.Length();
            Angle = 2*Math.Asin(deltaProj.Length()/2/R);
            if (HordeCenterDistance > 0 && CCW || HordeCenterDistance < 0 && !CCW)
            {
                Angle = Math.PI*2 - Angle;
            }
            Length = Angle * R;
        }
    }
}
