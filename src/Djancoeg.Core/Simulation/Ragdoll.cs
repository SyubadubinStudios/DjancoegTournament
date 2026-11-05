using System.Numerics;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

/// <summary>
/// Per-limb ragdoll for the shared 12-bone fighter rig (see tools/blender/build_characters.py).
/// Verlet particles at the joints, distance constraints (rigid torso cluster, floppy head and limbs), and
/// collision against the arena. <see cref="Pose"/> turns the particles into a bone pose for skinning.
/// Positions are in world space; the rig's bind pose faces +Z with feet at the origin (glTF axes).
/// </summary>
public sealed class Ragdoll
{
    /// <summary>Bone order used by <see cref="Pose"/>; matches the bone names in the fighter models.</summary>
    public static readonly string[] BoneNames =
        ["hips", "spine", "chest", "head", "upperarm.R", "forearm.R", "upperarm.L", "forearm.L", "thigh.R", "shin.R", "thigh.L", "shin.L"];

    public const int Pelvis = 0, Neck = 1, ShoulderR = 2, ShoulderL = 3, HipR = 4, HipL = 5, HeadTop = 6,
        ElbowR = 7, HandR = 8, ElbowL = 9, HandL = 10, KneeR = 11, FootR = 12, KneeL = 13, FootL = 14, Count = 15;

    /// <summary>Bind-pose joint positions (metres, glTF axes, character scale 1).</summary>
    public static readonly Vector3[] Bind =
    [
        new(0, 0.92f, 0), new(0, 1.50f, 0), new(-0.23f, 1.46f, 0), new(0.23f, 1.46f, 0), new(-0.1f, 0.92f, 0), new(0.1f, 0.92f, 0),
        new(0, 1.85f, 0), new(-0.25f, 1.19f, 0), new(-0.26f, 0.95f, 0), new(0.25f, 1.19f, 0), new(0.26f, 0.95f, 0),
        new(-0.1f, 0.5f, 0), new(-0.1f, 0.06f, 0), new(0.1f, 0.5f, 0), new(0.1f, 0.06f, 0),
    ];

    /// <summary>Bind-pose bone heads (in <see cref="BoneNames"/> order).</summary>
    public static readonly Vector3[] BoneHeads =
    [
        new(0, 0.92f, 0), new(0, 1.08f, 0), new(0, 1.32f, 0), new(0, 1.50f, 0),
        Bind[ShoulderR], Bind[ElbowR], Bind[ShoulderL], Bind[ElbowL], Bind[HipR], Bind[KneeR], Bind[HipL], Bind[KneeL],
    ];

    private static readonly int[] TorsoCluster = [Pelvis, Neck, ShoulderR, ShoulderL, HipR, HipL];
    private static readonly (int A, int B)[] Limbs =
    [
        (Neck, HeadTop), (ShoulderR, ElbowR), (ElbowR, HandR), (ShoulderL, ElbowL), (ElbowL, HandL),
        (HipR, KneeR), (KneeR, FootR), (HipL, KneeL), (KneeL, FootL),
    ];
    // Keeps joints from folding flat: minimum distance between the ends of each two-bone chain (and head vs chest).
    private static readonly (int A, int B, float MinFraction)[] Hinges =
    [
        (ShoulderR, HandR, 0.45f), (ShoulderL, HandL, 0.45f), (HipR, FootR, 0.55f), (HipL, FootL, 0.55f),
        (ShoulderR, HeadTop, 0.8f), (ShoulderL, HeadTop, 0.8f),
    ];

    private readonly (int A, int B, float Len)[] _constraints;
    private readonly (int A, int B, float Min)[] _hinges;
    public readonly Vector3[] Position = new Vector3[Count];
    private readonly Vector3[] _prev = new Vector3[Count];
    private readonly Vector3[] _beforeSolve = new Vector3[Count];
    private readonly bool[] _grounded = new bool[Count];
    public float Scale { get; private set; } = 1f;
    public bool Sleeping { get; private set; }
    private float _still;

    public Ragdoll()
    {
        var list = new List<(int, int, float)>();
        for (int i = 0; i < TorsoCluster.Length; i++)
            for (int j = i + 1; j < TorsoCluster.Length; j++) list.Add((TorsoCluster[i], TorsoCluster[j], 0));
        foreach (var (a, b) in Limbs) list.Add((a, b, 0));
        _constraints = [.. list];
        _hinges = new (int, int, float)[Hinges.Length];
    }

    /// <summary>Places the rig standing at <paramref name="feet"/> facing <paramref name="yaw"/> and gives it a push.</summary>
    public void Reset(Vector3 feet, float yaw, float scale, Vector3 velocity, Vector3 kick, float spinSeed = 0)
    {
        Scale = scale;
        Sleeping = false;
        _still = 0;
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        const float dt = 1f / 60f;
        for (int i = 0; i < Count; i++)
        {
            Position[i] = feet + Vector3.Transform(Bind[i] * scale, q);
            // Upper body takes more of the hit so the body topples instead of sliding.
            float height = Bind[i].Y / 1.85f;
            var v = velocity + kick * (0.35f + height) + new Vector3(MathF.Sin(spinSeed + i) * 0.4f, 0, MathF.Cos(spinSeed * 1.7f + i) * 0.4f);
            _prev[i] = Position[i] - v * dt;
            _grounded[i] = false;
        }
        for (int c = 0; c < _constraints.Length; c++)
        {
            var (a, b, _) = _constraints[c];
            _constraints[c].Len = Vector3.Distance(Bind[a], Bind[b]) * scale;
        }
        for (int h = 0; h < Hinges.Length; h++)
        {
            var (a, b, f) = Hinges[h];
            _hinges[h] = (a, b, Vector3.Distance(Bind[a], Bind[b]) * scale * f);
        }
    }

    /// <summary>Advances the simulation. <paramref name="world"/> may be null (no collision).</summary>
    public void Step(float dt, CollisionWorld? world, float gravity = 26f)
    {
        if (Sleeping) return;
        float r = 0.07f * Scale;
        var half = new Vector3(r);
        float moved = 0;
        for (int i = 0; i < Count; i++)
        {
            var p = Position[i];
            var vel = (p - _prev[i]) * 0.995f;
            if (_grounded[i]) { vel.X *= 0.82f; vel.Z *= 0.82f; }
            var target = vel + new Vector3(0, -gravity * dt * dt, 0);
            _prev[i] = p;
            _grounded[i] = false;
            Position[i] = world is null ? p + target : Sweep(world, p, target, half, i);
            moved += (Position[i] - p).LengthSquared();
        }

        for (int i = 0; i < Count; i++) _beforeSolve[i] = Position[i];
        for (int iter = 0; iter < 8; iter++)
        {
            foreach (var (a, b, rest) in _constraints) Satisfy(a, b, rest, exact: true);
            foreach (var (a, b, min) in _hinges)
            {
                float d = Vector3.Distance(Position[a], Position[b]);
                if (d < min) Satisfy(a, b, min, exact: true);
            }
        }
        // Constraint corrections must not push joints through walls or floors.
        if (world is not null)
            for (int i = 0; i < Count; i++)
                if (world.Overlaps(Aabb.FromCenter(Position[i], half)))
                    Position[i] = Sweep(world, _beforeSolve[i], Position[i] - _beforeSolve[i], half, i);

        _still = moved < 1e-6f * Count ? _still + dt : 0;
        if (_still > 0.6f) Sleeping = true;
    }

    private void Satisfy(int a, int b, float rest, bool exact)
    {
        var d = Position[b] - Position[a];
        float len = d.Length();
        if (len < 1e-6f) return;
        var corr = d * ((len - rest) / len * 0.5f);
        Position[a] += corr;
        Position[b] -= corr;
    }

    private Vector3 Sweep(CollisionWorld world, Vector3 from, Vector3 delta, Vector3 half, int i)
    {
        var p = from;
        var box = Aabb.FromCenter(p, half);
        float my = world.SweepAxis(box, 1, delta.Y);
        if (MathF.Abs(my - delta.Y) > 1e-6f && delta.Y < 0) _grounded[i] = true;
        p.Y += my;
        box = Aabb.FromCenter(p, half);
        p.X += world.SweepAxis(box, 0, delta.X);
        box = Aabb.FromCenter(p, half);
        p.Z += world.SweepAxis(box, 2, delta.Z);
        return p;
    }

    public Vector3 Center => (Position[Pelvis] + Position[Neck]) * 0.5f;
    public float LowestY { get { float y = float.MaxValue; foreach (var p in Position) y = MathF.Min(y, p.Y); return y; } }

    /// <summary>
    /// Bone pose in <see cref="BoneNames"/> order: head position (world space) and the rotation from the bind pose.
    /// Skinning uses <c>rotation * bindRotation</c> at <c>head</c>.
    /// </summary>
    public void Pose(Span<Vector3> heads, Span<Quaternion> rotations)
    {
        var pelvis = Position[Pelvis];
        var up = Vector3.Normalize(Position[Neck] - pelvis);
        var right = Position[ShoulderL] - Position[ShoulderR] + (Position[HipL] - Position[HipR]);
        right = Vector3.Normalize(right - up * Vector3.Dot(right, up));
        var fwd = Vector3.Cross(right, up);
        var torso = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            fwd.X, fwd.Y, fwd.Z, 0,
            0, 0, 0, 1)));

        // Torso bones ride the rigid torso frame.
        for (int b = 0; b < 3; b++)
        {
            heads[b] = pelvis + Vector3.Transform((BoneHeads[b] - Bind[Pelvis]) * Scale, torso);
            rotations[b] = torso;
        }
        heads[3] = Position[Neck];
        rotations[3] = Limb(torso, Bind[HeadTop] - Bind[Neck], Position[HeadTop] - Position[Neck]);
        Chain(4, ShoulderR, ElbowR, HandR, torso, heads, rotations);
        Chain(6, ShoulderL, ElbowL, HandL, torso, heads, rotations);
        Chain(8, HipR, KneeR, FootR, torso, heads, rotations);
        Chain(10, HipL, KneeL, FootL, torso, heads, rotations);
    }

    private void Chain(int bone, int a, int b, int c, Quaternion torso, Span<Vector3> heads, Span<Quaternion> rotations)
    {
        heads[bone] = Position[a];
        rotations[bone] = Limb(torso, Bind[b] - Bind[a], Position[b] - Position[a]);
        heads[bone + 1] = Position[b];
        rotations[bone + 1] = Limb(torso, Bind[c] - Bind[b], Position[c] - Position[b]);
    }

    /// <summary>Rotation that turns a bind-pose bone direction into its current direction, with twist following the torso.</summary>
    private static Quaternion Limb(Quaternion torso, Vector3 bindDir, Vector3 current)
    {
        var from = Vector3.Normalize(Vector3.Transform(bindDir, torso));
        if (current.LengthSquared() < 1e-10f) return torso;
        return Quaternion.Normalize(FromTo(from, Vector3.Normalize(current)) * torso);
    }

    public static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b);
        if (d > 0.99999f) return Quaternion.Identity;
        if (d < -0.99999f)
        {
            var axis = Vector3.Cross(Vector3.UnitX, a);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, a);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c, 1 + d));
    }
}
