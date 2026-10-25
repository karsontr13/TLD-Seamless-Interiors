using UnityEngine;

namespace SeamlessInteriors
{
    // ─── VECTOR AND MATRIX MATH THAT LEAVES NO GARBAGE IN THE GAME ───
    //
    // In this game every one of Unity's own math operations is a call into the game that comes back boxed:
    // Vector3 +, -, *, Dot, Cross, magnitude, normalized, Mathf.Max, Matrix4x4 * Vector4 or * Matrix4x4,
    // Quaternion * Vector3, even Vector3.zero and Matrix4x4.identity each leave one object for the game's
    // collector. The views out of the windows did about 1,900 of them a frame at the hangar, a collection
    // every few seconds and a dropped frame with each, seen as a jump (owner, 2026-10-24). Measured with the
    // runtime's own count of objects created: one per operation; none for `new Vector3(...)`, for reading
    // or writing a field (.x, .m03), or for System.Math (2026-10-24, LOG 1024-21 and 1024-22).
    //
    // So per-frame code does its arithmetic here, on the fields, and calls Unity only to read or set state.
    internal static class NoGarbageMath
    {
        internal static Vector3 Add(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        internal static Vector3 Sub(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        internal static Vector3 Scale(Vector3 a, float s) { return new Vector3(a.x * s, a.y * s, a.z * s); }
        internal static Vector3 Neg(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
        // a + b * s
        internal static Vector3 AddScaled(Vector3 a, Vector3 b, float s) { return new Vector3(a.x + b.x * s, a.y + b.y * s, a.z + b.z * s); }
        internal static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        internal static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        internal static float Length(Vector3 a) { return (float)System.Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z); }
        internal static float SqrLength(Vector3 a) { return a.x * a.x + a.y * a.y + a.z * a.z; }
        internal static float Distance(Vector3 a, Vector3 b) { return Length(Sub(a, b)); }
        // Unity's normalized: zero below 1e-5.
        internal static Vector3 Normalized(Vector3 a)
        {
            float m = Length(a);
            return m > 1e-5f ? new Vector3(a.x / m, a.y / m, a.z / m) : new Vector3(0f, 0f, 0f);
        }
        internal static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            t = Clamp01(t);
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        internal static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        internal static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        internal static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        internal static Vector4 Mul(Matrix4x4 m, Vector4 v)
        {
            return new Vector4(m.m00 * v.x + m.m01 * v.y + m.m02 * v.z + m.m03 * v.w,
                               m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13 * v.w,
                               m.m20 * v.x + m.m21 * v.y + m.m22 * v.z + m.m23 * v.w,
                               m.m30 * v.x + m.m31 * v.y + m.m32 * v.z + m.m33 * v.w);
        }

        // m * (p, 1)
        internal static Vector4 MulPoint4(Matrix4x4 m, Vector3 p)
        {
            return new Vector4(m.m00 * p.x + m.m01 * p.y + m.m02 * p.z + m.m03,
                               m.m10 * p.x + m.m11 * p.y + m.m12 * p.z + m.m13,
                               m.m20 * p.x + m.m21 * p.y + m.m22 * p.z + m.m23,
                               m.m30 * p.x + m.m31 * p.y + m.m32 * p.z + m.m33);
        }

        // Matrix4x4.MultiplyVector: the upper 3x3 only.
        internal static Vector3 MulDirection(Matrix4x4 m, Vector3 d)
        {
            return new Vector3(m.m00 * d.x + m.m01 * d.y + m.m02 * d.z,
                               m.m10 * d.x + m.m11 * d.y + m.m12 * d.z,
                               m.m20 * d.x + m.m21 * d.y + m.m22 * d.z);
        }

        internal static Matrix4x4 Mul(Matrix4x4 a, Matrix4x4 b)
        {
            Matrix4x4 r = new Matrix4x4();
            r.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20 + a.m03 * b.m30;
            r.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21 + a.m03 * b.m31;
            r.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22 + a.m03 * b.m32;
            r.m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03 * b.m33;
            r.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20 + a.m13 * b.m30;
            r.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31;
            r.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32;
            r.m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33;
            r.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20 + a.m23 * b.m30;
            r.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31;
            r.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32;
            r.m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33;
            r.m30 = a.m30 * b.m00 + a.m31 * b.m10 + a.m32 * b.m20 + a.m33 * b.m30;
            r.m31 = a.m30 * b.m01 + a.m31 * b.m11 + a.m32 * b.m21 + a.m33 * b.m31;
            r.m32 = a.m30 * b.m02 + a.m31 * b.m12 + a.m32 * b.m22 + a.m33 * b.m32;
            r.m33 = a.m30 * b.m03 + a.m31 * b.m13 + a.m32 * b.m23 + a.m33 * b.m33;
            return r;
        }

        internal static Matrix4x4 Identity()
        {
            Matrix4x4 r = new Matrix4x4();
            r.m00 = 1f; r.m11 = 1f; r.m22 = 1f; r.m33 = 1f;
            return r;
        }

        internal static Matrix4x4 Translation(Vector3 t)
        {
            Matrix4x4 r = Identity();
            r.m03 = t.x; r.m13 = t.y; r.m23 = t.z;
            return r;
        }

        // Matrix4x4.GetRow
        internal static Vector4 Row(Matrix4x4 m, int i)
        {
            switch (i)
            {
                case 0: return new Vector4(m.m00, m.m01, m.m02, m.m03);
                case 1: return new Vector4(m.m10, m.m11, m.m12, m.m13);
                case 2: return new Vector4(m.m20, m.m21, m.m22, m.m23);
                default: return new Vector4(m.m30, m.m31, m.m32, m.m33);
            }
        }

        internal static Vector4 Add4(Vector4 a, Vector4 b) { return new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w); }
        internal static Vector4 Sub4(Vector4 a, Vector4 b) { return new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w); }

        // Mathf.Lerp on floats: t clamped to [0, 1].
        internal static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }

        // Mathf.Approximately; Mathf.Epsilon taken as the smallest normal float, as in the game's build.
        internal static bool Approximately(float a, float b)
        {
            return System.Math.Abs(b - a) < System.Math.Max(1E-06f * System.Math.Max(System.Math.Abs(a), System.Math.Abs(b)), 1.17549435E-38f * 8f);
        }

        // Unity's `obj != null` (false for C# null and for a destroyed object) without the call into the game,
        // for classes outside SeamlessInteriorsMod; the same reading of m_CachedPtr as its IsAliveDirect.
        private static int s_CachedPtrOffset = -2;

        internal static bool IsAlive(Object obj)
        {
            if ((object)obj == null) return false;
            if (s_CachedPtrOffset == -2)
            {
                s_CachedPtrOffset = -1;
                try
                {
                    var field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(Il2CppInterop.Runtime.Il2CppClassPointerStore<Object>.NativeClassPtr, "m_CachedPtr");
                    if (field != System.IntPtr.Zero) s_CachedPtrOffset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
                }
                catch { s_CachedPtrOffset = -1; }
            }
            if (s_CachedPtrOffset <= 0) return obj != null;
            return System.Runtime.InteropServices.Marshal.ReadIntPtr(obj.Pointer, s_CachedPtrOffset) != System.IntPtr.Zero;
        }

        // Unity's `a == b` on two objects: both gone (C# null or destroyed) counts as equal, as there.
        internal static bool SameObject(Object a, Object b)
        {
            bool aAlive = IsAlive(a), bAlive = IsAlive(b);
            if (!aAlive || !bAlive) return aAlive == bAlive;
            return a.Pointer == b.Pointer;
        }
    }
}
