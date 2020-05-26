using System;
using Config;
using Vector;
using CNCState;

namespace Actions
{
    public class RTArcMoveCommand : IRTMoveCommand
    {
        public bool CommandIsCached { get { return true; } }
        
        private MachineParameters Config;
        private Vector2 deltaProj;
        private Vector2 startToCenterProj;
        private Vector2 endToCenterProj;
        private bool bigArc;

        public AxisState.Plane Plane { get; private set; }
        public RTMovementOptions Options { get; private set; }
        public Vector3 Delta { get; private set; }
        public bool CCW { get; private set; }
        public decimal R { get; private set; }

        public Vector3 DirStart { get; private set; }
        public Vector3 DirEnd { get; private set; }
        public decimal Length { get; private set; }
        public decimal Angle { get; private set; }

        private readonly double eps = 1e-6;

        private bool left_basis
        {
            get
            {
                bool left = false;
                switch (Plane)
                {
                    case AxisState.Plane.XY:
                        if (Config.invert_x)
                            left = !left;
                        if (Config.invert_y)
                            left = !left;
                        return left;
                    case AxisState.Plane.YZ:
                        if (Config.invert_y)
                            left = !left;
                        if (Config.invert_z)
                            left = !left;
                        return left;
                    case AxisState.Plane.ZX:
                        if (Config.invert_z)
                            left = !left;
                        if (Config.invert_x)
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

        public String Command
        {
            get
            {   
                bool hwccw;

                Vector3 hwdelta = new Vector3();

                if (Config.invert_x)
                    hwdelta.x = -Delta.x;
                else
                    hwdelta.x = Delta.x;
                
                if (Config.invert_y)
                    hwdelta.y = -Delta.y;
                else
                    hwdelta.y = Delta.y;

                if (Config.invert_z)
                    hwdelta.z = -Delta.z;
                else
                    hwdelta.z = Delta.z;

                int dx = (int)(hwdelta.x * Config.steps_per_x);
                int dy = (int)(hwdelta.y * Config.steps_per_y);
                int dz = (int)(hwdelta.z * Config.steps_per_z);

                if (left_basis)
                {
                    hwccw = !CCW;
                }
                else
                {
                    hwccw = CCW;
                }

                double a;
                double b;
                switch (Plane)
                {
                    case AxisState.Plane.XY:
                        a = (int)(R * Config.steps_per_x);
                        b = (int)(R * Config.steps_per_y);
                        break;
                    case AxisState.Plane.YZ:
                        a = (int)(R * Config.steps_per_y);
                        b = (int)(R * Config.steps_per_z);
                        break;
                    case AxisState.Plane.ZX:
                        a = (int)(R * Config.steps_per_z);
                        b = (int)(R * Config.steps_per_x);
                        break;
                    default:
                        throw new InvalidOperationException("Invalid plane");
                }

                if (bigArc)
                {
                    a = -a;
                    b = -b;
                }

                return $"{MoveCmd} {plane_cmd} {Options.Command} X{dx}Y{dy}Z{dz} A{a}B{b}";
            }
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

        private decimal VectorPlaneNormalProj(Vector3 delta, AxisState.Plane plane)
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

        
        public RTArcMoveCommand(Vector3 delta, decimal r, bool ccw, AxisState.Plane plane,
                                RTMovementOptions opts, MachineParameters config)
        {
            decimal h = VectorPlaneNormalProj(delta, plane);
            if (Math.Abs((double)h) > eps)
            {
                throw new ArgumentOutOfRangeException("Only arc supported, not helix");
            }

            Config = config;
            if (r < 0)
                R = -r;
            else
                R = r;
            Delta = delta;
            CCW = ccw;
            Plane = plane;
            Options = opts;
            deltaProj = VectorPlaneProj(Delta, Plane);
            bigArc = (r < 0);

            decimal d = Delta.Length();
            decimal hcl = (decimal)Math.Sqrt((double)(R*R - d*d/4));
            if (ccw && !bigArc || !ccw && bigArc)
                hcl = -hcl;

            startToCenterProj = deltaProj / 2 + Vector2.Normalize(deltaProj.Right()) * hcl;
            endToCenterProj = startToCenterProj - deltaProj;
            FillDirs();

            Angle = (decimal)(2*Math.Asin((double)(deltaProj.Length()/2/R)));
            if (bigArc)
            {
                Angle = (decimal)(Math.PI*2) - Angle;
            }
            Length = Angle * R;
        }

        public RTArcMoveCommand(Vector3 delta, Vector3 startToCenter, bool ccw, AxisState.Plane plane,
                                RTMovementOptions opts, MachineParameters config)
        {
            decimal h = VectorPlaneNormalProj(delta, plane);
            if (Math.Abs((double)h) > eps)
            {
                throw new ArgumentOutOfRangeException("Only arc supported, not helix");
            }
            this.Config = config;
            Delta = delta;
            CCW = ccw;
            Plane = plane;
            Options = opts;
            deltaProj = VectorPlaneProj(Delta, Plane);
            startToCenterProj = VectorPlaneProj(startToCenter, Plane);
            endToCenterProj = startToCenterProj - deltaProj;

            decimal nd = deltaProj.x * startToCenterProj.y - deltaProj.y * startToCenterProj.x;
            if (nd > 0 && CCW || nd < 0 && !CCW)
            {
                bigArc = false;
            }
            else
            {
                bigArc = true;
            }

            FillDirs();
            R = startToCenter.Length();
            Angle = (decimal)(2*Math.Asin((double)(deltaProj.Length()/2/R)));
            if (bigArc)
            {
                Angle = (decimal)(Math.PI*2) - Angle;
            }
            Length = Angle * R;
        }
    }
}
