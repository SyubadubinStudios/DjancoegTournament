using System.Numerics;
using System.Runtime.CompilerServices;

namespace Djancoeg.Core.Geometry;

/// <summary>Axis-aligned bounding box. Immutable 24-byte value type used for all collision.</summary>
public readonly struct Aabb(Vector3 min, Vector3 max)
{
    public readonly Vector3 Min = min;
    public readonly Vector3 Max = max;

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
    public Vector3 HalfExtents => (Max - Min) * 0.5f;

    public static Aabb FromCenter(Vector3 center, Vector3 halfExtents) => new(center - halfExtents, center + halfExtents);

    /// <summary>Box for a body standing with feet at <paramref name="feet"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Aabb FromFeet(Vector3 feet, float halfWidth, float height) =>
        new(new Vector3(feet.X - halfWidth, feet.Y, feet.Z - halfWidth), new Vector3(feet.X + halfWidth, feet.Y + height, feet.Z + halfWidth));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(in Aabb o) =>
        Min.X < o.Max.X && Max.X > o.Min.X &&
        Min.Y < o.Max.Y && Max.Y > o.Min.Y &&
        Min.Z < o.Max.Z && Max.Z > o.Min.Z;

    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

    public Aabb Translate(Vector3 d) => new(Min + d, Max + d);
    public Aabb Expand(float r) => new(Min - new Vector3(r), Max + new Vector3(r));

    /// <summary>Closest point inside the box to <paramref name="p"/>.</summary>
    public Vector3 ClosestPoint(Vector3 p) => Vector3.Clamp(p, Min, Max);

    /// <summary>Slab ray test. <paramref name="invDir"/> is 1/dir per component (may be infinity).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Raycast(Vector3 origin, Vector3 invDir, float maxDist, out float tHit, out Vector3 normal)
    {
        float tx1 = (Min.X - origin.X) * invDir.X, tx2 = (Max.X - origin.X) * invDir.X;
        float ty1 = (Min.Y - origin.Y) * invDir.Y, ty2 = (Max.Y - origin.Y) * invDir.Y;
        float tz1 = (Min.Z - origin.Z) * invDir.Z, tz2 = (Max.Z - origin.Z) * invDir.Z;

        float txn = MathF.Min(tx1, tx2), txf = MathF.Max(tx1, tx2);
        float tyn = MathF.Min(ty1, ty2), tyf = MathF.Max(ty1, ty2);
        float tzn = MathF.Min(tz1, tz2), tzf = MathF.Max(tz1, tz2);

        // NaN appears when origin lies exactly on a slab plane with a zero direction component (0 * inf).
        if (float.IsNaN(txn)) { if (origin.X < Min.X || origin.X > Max.X) { tHit = 0; normal = default; return false; } txn = float.NegativeInfinity; txf = float.PositiveInfinity; }
        if (float.IsNaN(tyn)) { if (origin.Y < Min.Y || origin.Y > Max.Y) { tHit = 0; normal = default; return false; } tyn = float.NegativeInfinity; tyf = float.PositiveInfinity; }
        if (float.IsNaN(tzn)) { if (origin.Z < Min.Z || origin.Z > Max.Z) { tHit = 0; normal = default; return false; } tzn = float.NegativeInfinity; tzf = float.PositiveInfinity; }

        float tNear = MathF.Max(txn, MathF.Max(tyn, tzn));
        float tFar = MathF.Min(txf, MathF.Min(tyf, tzf));

        if (tNear > tFar || tFar < 0f || tNear > maxDist)
        {
            tHit = 0; normal = default;
            return false;
        }

        if (tNear < 0f)
        {
            // Origin inside the box: report an immediate hit.
            tHit = 0f; normal = Vector3.Zero;
            return true;
        }

        tHit = tNear;
        if (tNear == txn) normal = new Vector3(invDir.X > 0 ? -1 : 1, 0, 0);
        else if (tNear == tyn) normal = new Vector3(0, invDir.Y > 0 ? -1 : 1, 0);
        else normal = new Vector3(0, 0, invDir.Z > 0 ? -1 : 1);
        return true;
    }

    public override string ToString() => $"[{Min} .. {Max}]";
}

public static class VectorMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Forward(float yaw, float pitch)
    {
        var (sy, cy) = MathF.SinCos(yaw);
        var (sp, cp) = MathF.SinCos(pitch);
        return new Vector3(cp * sy, sp, cp * cy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 FlatForward(float yaw)
    {
        var (sy, cy) = MathF.SinCos(yaw);
        return new Vector3(sy, 0, cy);
    }

    /// <summary>Screen-right vector for a yaw (right-handed, Y up).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 FlatRight(float yaw)
    {
        var (sy, cy) = MathF.SinCos(yaw);
        return new Vector3(-cy, 0, sy);
    }

    public static Vector3 SafeInverse(Vector3 d) => new(
        d.X == 0 ? float.PositiveInfinity : 1f / d.X,
        d.Y == 0 ? float.PositiveInfinity : 1f / d.Y,
        d.Z == 0 ? float.PositiveInfinity : 1f / d.Z);

    public static Vector3 Horizontal(Vector3 v) => new(v.X, 0, v.Z);

    public static float WrapAngle(float a)
    {
        a %= MathF.Tau;
        if (a > MathF.PI) a -= MathF.Tau;
        else if (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    public static float YawTowards(Vector3 from, Vector3 to) => MathF.Atan2(to.X - from.X, to.Z - from.Z);
}
