using System;

namespace Vector
{
    public class Vector2
    {
        public decimal x, y;
        public Vector2()
        {
            x = 0;
            y = 0;
        }
        public Vector2(decimal x, decimal y)
        {
            this.x = x;
            this.y = y;
        }
        public decimal Length()
        {
            return (decimal)Math.Sqrt((double)(x * x + y * y));
        }

        public static decimal operator *(Vector2 v, Vector2 w)
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

        public static Vector2 operator *(Vector2 v, decimal x)
        {
            return new Vector2(v.x * x, v.y * x);
        }

        public static Vector2 operator /(Vector2 v, decimal x)
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
            decimal l = v.Length();
            Vector2 n = new Vector2(v.x / l, v.y / l);
            return n;
        }
    }

    public class Vector3
    {
        public decimal x, y, z;
        public Vector3()
        {
            x = 0;
            y = 0;
            z = 0;
        }
        public Vector3(decimal x, decimal y, decimal z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3(Vector3 v)
        {
            this.x = v.x;
            this.y = v.y;
            this.z = v.z;
        }

        public static decimal operator *(Vector3 v, Vector3 w)
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

        public decimal Length()
        {
            return (decimal)(Math.Sqrt((double)(x * x + y * y + z * z)));
        }
        public static Vector3 Normalize(Vector3 v)
        {
            decimal l = v.Length();
            Vector3 n = new Vector3(v.x / l, v.y / l, v.z / l);
            return n;
        }

        public static bool operator == (Vector3 a, Vector3 b)
        {
            return (a.x == b.x) && (a.y == b.y) && (a.z == b.z);
        }

        public static bool operator != (Vector3 a, Vector3 b)
        {
            return !(a == b);
        }
    }

}
