using System;

namespace Vector
{
    public class Vector2
    {
        public double x, y;
        public Vector2()
        {
            x = 0;
            y = 0;
        }
        public Vector2(double x, double y)
        {
            this.x = x;
            this.y = y;
        }
        public double Length()
        {
            return Math.Sqrt(x * x + y * y);
        }

        public static double operator *(Vector2 v, Vector2 w)
        {
            return v.x * w.x + v.y * w.y;
        }

        public static Vector2 operator +(Vector2 v, Vector2 w)
        {
            return new Vector2(v.x + w.x, v.y + w.y);
        }

        public static Vector2 operator -(Vector2 v, Vector2 w)
        {
            return new Vector2(v.x - w.x, v.y - w.y);
        }

        public static Vector2 operator *(Vector2 v, double x)
        {
            return new Vector2(v.x * x, v.y * x);
        }

        public static Vector2 operator /(Vector2 v, double x)
        {
            return new Vector2(v.x / x, v.y / x);
        }


        public static Vector2 operator -(Vector2 v)
        {
            return new Vector2(-v.x, -v.y);
        }

        public Vector2 Right()
        {
            return new Vector2(y, -x);
        }
        public static Vector2 Normalize(Vector2 v)
        {
            double l = v.Length();
            Vector2 n = new Vector2(v.x / l, v.y / l);
            return n;
        }
    }

    public class Vector3
    {
        public double x, y, z;
        public Vector3()
        {
            x = 0;
            y = 0;
            z = 0;
        }
        public Vector3(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static double operator *(Vector3 v, Vector3 w)
        {
            return v.x * w.x + v.y * w.y + v.z * w.z;
        }

        public static Vector3 operator +(Vector3 v, Vector3 w)
        {
            return new Vector3(v.x + w.x, v.y + w.y, v.z + w.z);
        }

        public static Vector3 operator -(Vector3 v, Vector3 w)
        {
            return new Vector3(v.x - w.x, v.y - w.y, v.z - w.z);
        }

        public double Length()
        {
            return Math.Sqrt(x * x + y * y + z * z);
        }
        public static Vector3 Normalize(Vector3 v)
        {
            double l = v.Length();
            Vector3 n = new Vector3(v.x / l, v.y / l, v.z / l);
            return n;
        }
    }

}
