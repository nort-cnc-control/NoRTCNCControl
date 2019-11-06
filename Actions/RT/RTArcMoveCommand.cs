using System;
using Config;

namespace Actions
{
    public class RTArcMoveCommand : IRTMoveCommand
    {
        public enum ArcAxis
        {
            XY,
            YZ,
            ZX
        }

        public bool CommandIsCached { get { return true; } }
        
        private MachineParameters config;
        private Vector2 deltaProj;
        private Vector2 startToCenterProj;
        private Vector2 endToCenterProj;

        public ArcAxis Plane { get; private set; }
        public RTMovementOptions Options { get; private set; }
        public Vector3 Delta { get; private set; }
        public bool CCW { get; private set; }
        public double R { get; private set; }

        public Double Hcl { get; private set; }
        public Vector3 DirStart { get; private set; }
        public Vector3 DirEnd { get; private set; }
        public double Length { get; private set; }
        public double Angle { get; private set; }
        private bool left_basis
        {
            get
            {
                bool left = false;
                switch (Plane)
                {
                    case ArcAxis.XY:
                        if (config.invert_x)
                            left = !left;
                        if (config.invert_y)
                            left = !left;
                        return left;
                    case ArcAxis.YZ:
                        if (config.invert_y)
                            left = !left;
                        if (config.invert_z)
                            left = !left;
                        return left;
                    case ArcAxis.ZX:
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
                    case ArcAxis.XY:
                        return "G17";
                    case ArcAxis.YZ:
                        return "G18";
                    case ArcAxis.ZX:
                        return "G19";
                    default:
                        throw new ArgumentOutOfRangeException("Invalid arc plane selection");
                }
            }
        }
    
        private String move_cmd
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
                    hwhcl = -Hcl;
                    hwccw = !CCW;
                }
                else
                {
                    hwhcl = Hcl;
                    hwccw = CCW;
                }
                return $"{move_cmd} {plane_cmd} {Options.Command} X{FormatD(hwdelta.x)} Y{FormatD(hwdelta.y)} Z{FormatD(hwdelta.z)} D{FormatD(hwhcl)}";
            }
        }

        private double FindHclR(Vector2 delta, bool ccw, double R)
        {
            double D = delta.Length();
            bool big_arc = (R < 0);
            R = Math.Abs(R);
            if (R < D/2)
                throw new ArgumentOutOfRangeException("Too small radius");
            double s = Math.Sqrt(R*R - D*D/4);
            int center_side;
            if (ccw == big_arc)
                center_side = 1;
            else
                center_side = -1;
            double hcl = s * center_side;
            return hcl;
        }

        private double FindHclIJK(Vector2 delta, bool ccw, Vector2 center)
        {
            double D = delta.Length();
            double R = center.Length();
            if (R < D/2)
                throw new ArgumentOutOfRangeException("Too small radius");
            double s = Math.Sqrt(R*R - D*D/4);
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

        private Vector2 VectorPlaneProj(Vector3 delta, ArcAxis plane)
        {
            switch (plane)
            {
                case ArcAxis.XY:
                    return new Vector2(delta.x, delta.y);
                case ArcAxis.YZ:
                    return new Vector2(delta.y, delta.z);
                case ArcAxis.ZX:
                    return new Vector2(delta.z, delta.x);
                default:
                    throw new ArgumentOutOfRangeException("Invalid axis");
            }
        }

        private double VectorPlaneNormalProj(Vector3 delta, ArcAxis plane)
        {
            switch (plane)
            {
                case ArcAxis.XY:
                    return delta.z;
                case ArcAxis.YZ:
                    return delta.x;
                case ArcAxis.ZX:
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
                case ArcAxis.XY:
                    DirStart = new Vector3(startTan.x, startTan.y, 0);
                    DirEnd = new Vector3(endTan.x, endTan.y, 0);
                    break;
                case ArcAxis.YZ:
                    DirStart = new Vector3(0, startTan.x, startTan.y);
                    DirEnd = new Vector3(0, endTan.x, endTan.y);
                    break;
                case ArcAxis.ZX:
                    DirStart = new Vector3(startTan.y, 0, startTan.x);
                    DirEnd = new Vector3(endTan.y, 0, endTan.x);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Invalid axis");
            }
        }

        
        public RTArcMoveCommand(Vector3 delta, double R, bool ccw, ArcAxis plane,
                                RTMovementOptions opts, MachineParameters config)
        {
            double h = VectorPlaneNormalProj(delta, plane);
            if (h != 0)
            {
                throw new ArgumentOutOfRangeException("Only arc supported, not helix");
            }

            this.config = config;
            Delta = delta;
            CCW = ccw;
            Plane = plane;
            Options = opts;
            deltaProj = VectorPlaneProj(Delta, Plane);
            Hcl = FindHclR(deltaProj, CCW, R);
            startToCenterProj = deltaProj / 2 + Vector2.Normalize(deltaProj.Right()) * Hcl;
            endToCenterProj = startToCenterProj - deltaProj;
            FillDirs();
            this.R = Math.Abs(R);
            Angle = 2*Math.Asin(deltaProj.Length()/2/R);
            if (Hcl > 0 && CCW || Hcl < 0 && !CCW)
            {
                Angle = Math.PI*2 - Angle;
            }
            Length = Angle * R;
        }

        public RTArcMoveCommand(Vector3 delta, Vector3 startToCenter, bool ccw, ArcAxis plane,
                                RTMovementOptions opts, MachineParameters config)
        {
            double h = VectorPlaneNormalProj(delta, plane);
            if (h != 0)
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
            Hcl = FindHclIJK(deltaProj, CCW, startToCenterProj);
            FillDirs();
            R = startToCenter.Length();
            Angle = 2*Math.Asin(deltaProj.Length()/2/R);
            if (Hcl > 0 && !CCW || Hcl < 0 && CCW)
            {
                Angle = Math.PI*2 - Angle;
            }
            Length = Angle * R;
        }
    }
}
