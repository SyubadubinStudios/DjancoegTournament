using System.Numerics;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

[Flags]
public enum InputButtons : ushort
{
    None = 0,
    Jump = 1 << 0,
    Fire = 1 << 1,
    Crouch = 1 << 2,
    Ability = 1 << 3,
    Taunt = 1 << 4,
    NextWeapon = 1 << 5,
    PrevWeapon = 1 << 6,
}

/// <summary>One tick of player intent. 16 bytes; sent redundantly over the network.</summary>
public struct PlayerInput
{
    public uint Sequence;
    public float Yaw;
    public float Pitch;
    public sbyte Forward;   // -127..127
    public sbyte Right;     // -127..127
    public InputButtons Buttons;
    public byte WeaponSlot; // 0 = keep current, 1..9 = select slot

    public readonly bool Has(InputButtons b) => (Buttons & b) != 0;
}

/// <summary>The part of a player's state that movement prediction needs.</summary>
public struct MoveState
{
    public Vector3 Position; // feet
    public Vector3 Velocity;
    public bool OnGround;
    public bool Crouched;
    public float PadCooldown;
}

public readonly record struct MoveParams(float SpeedMultiplier, float JumpMultiplier, float Scale)
{
    public static readonly MoveParams Default = new(1f, 1f, 1f);
    public float HalfWidth => SimConstants.PlayerHalfWidth * MathF.Max(0.75f, Scale);
    public float Height(bool crouched) => (crouched ? SimConstants.CrouchHeight : SimConstants.PlayerHeight) * Scale;
    public float EyeHeight(bool crouched) => (crouched ? SimConstants.CrouchEyeHeight : SimConstants.EyeHeight) * Scale;
}

[Flags]
public enum MoveEvents : byte { None = 0, Jumped = 1, Landed = 2, JumpPad = 4 }

/// <summary>
/// Quake-style movement (ground friction, ground/air acceleration, bunny-hop friendly)
/// with swept AABB collision and stair stepping. Shared by server simulation and client prediction.
/// </summary>
public static class PlayerMovement
{
    public const float Gravity = 20f;
    public const float MaxSpeed = 8.5f;
    public const float CrouchSpeedScale = 0.5f;
    public const float GroundAccel = 10f;
    public const float AirAccel = 1.2f;
    public const float AirSpeedCap = 1.6f;
    public const float Friction = 6f;
    public const float StopSpeed = 2.5f;
    public const float JumpVelocity = 7f;
    public const float StepHeight = 0.5f;
    public const float MaxFallSpeed = 50f;

    public static MoveEvents Step(ref MoveState s, in PlayerInput input, in MoveParams p, CollisionWorld world, float dt)
    {
        var events = MoveEvents.None;
        float hw = p.HalfWidth;

        // Crouch (only stand up if there is headroom).
        bool wantCrouch = input.Has(InputButtons.Crouch);
        if (wantCrouch) s.Crouched = true;
        else if (s.Crouched && !world.Overlaps(Aabb.FromFeet(s.Position, hw, p.Height(false)))) s.Crouched = false;
        float height = p.Height(s.Crouched);

        Vector3 fwd = VectorMath.FlatForward(input.Yaw), right = VectorMath.FlatRight(input.Yaw);
        Vector3 wish = fwd * (input.Forward / 127f) + right * (input.Right / 127f);
        float wishLen = wish.Length();
        Vector3 wishDir = wishLen > 1e-4f ? wish / wishLen : Vector3.Zero;
        float wishSpeed = MathF.Min(wishLen, 1f) * MaxSpeed * p.SpeedMultiplier * (s.Crouched ? CrouchSpeedScale : 1f);

        if (s.OnGround)
        {
            if (input.Has(InputButtons.Jump) && !s.Crouched)
            {
                s.Velocity.Y = JumpVelocity * p.JumpMultiplier;
                s.OnGround = false;
                events |= MoveEvents.Jumped;
                Accelerate(ref s.Velocity, wishDir, wishSpeed, GroundAccel, dt); // keep bhop speed: no friction on jump tick
            }
            else
            {
                ApplyFriction(ref s.Velocity, dt);
                Accelerate(ref s.Velocity, wishDir, wishSpeed, GroundAccel, dt);
            }
        }
        else
        {
            AirAccelerate(ref s.Velocity, wishDir, wishSpeed, dt);
        }

        if (!s.OnGround) s.Velocity.Y = MathF.Max(s.Velocity.Y - Gravity * dt, -MaxFallSpeed);

        // Sub-step so fast bodies never tunnel through thin geometry.
        Vector3 total = s.Velocity * dt;
        int steps = Math.Clamp((int)MathF.Ceiling(total.Length() / 0.2f), 1, 10);
        Vector3 delta = total / steps;
        bool wasOnGround = s.OnGround;
        for (int i = 0; i < steps; i++) MoveOnce(ref s, delta, hw, height, world, wasOnGround);

        // Stick to stairs/slopes when walking down.
        if (wasOnGround && !s.OnGround && s.Velocity.Y <= 0 && (events & MoveEvents.Jumped) == 0)
        {
            var box = Aabb.FromFeet(s.Position, hw, height);
            float down = world.SweepAxis(box, 1, -StepHeight);
            if (down > -StepHeight + 0.0005f) { s.Position.Y += down; s.OnGround = true; s.Velocity.Y = 0; }
        }

        if (!wasOnGround && s.OnGround) events |= MoveEvents.Landed;

        // Jump pads ("kasur lompat").
        if (s.PadCooldown > 0) s.PadCooldown -= dt;
        else
        {
            int pad = world.TouchingPad(Aabb.FromFeet(s.Position, hw, 0.3f));
            if (pad >= 0)
            {
                s.Velocity = world.PadLaunch(pad);
                s.OnGround = false;
                s.PadCooldown = 0.4f;
                events |= MoveEvents.JumpPad;
            }
        }
        return events;
    }

    private static void MoveOnce(ref MoveState s, Vector3 delta, float hw, float height, CollisionWorld world, bool stepAllowed)
    {
        Vector3 start = s.Position;

        // Horizontal first, with a stair-step retry.
        Vector3 pos = start;
        bool hitX = MoveAxis(ref pos, 0, delta.X, hw, height, world);
        bool hitZ = MoveAxis(ref pos, 2, delta.Z, hw, height, world);

        if ((hitX || hitZ) && (stepAllowed || s.OnGround))
        {
            Vector3 alt = start;
            var box = Aabb.FromFeet(alt, hw, height);
            float up = world.SweepAxis(box, 1, StepHeight);
            alt.Y += up;
            bool ax = MoveAxis(ref alt, 0, delta.X, hw, height, world);
            bool az = MoveAxis(ref alt, 2, delta.Z, hw, height, world);
            float down = world.SweepAxis(Aabb.FromFeet(alt, hw, height), 1, -up);
            alt.Y += down;
            float gainedAlt = VectorMath.Horizontal(alt - start).LengthSquared();
            float gainedPos = VectorMath.Horizontal(pos - start).LengthSquared();
            if (gainedAlt > gainedPos + 1e-6f && alt.Y - start.Y < StepHeight + 0.01f)
            {
                pos = alt; hitX = ax; hitZ = az;
            }
        }

        if (hitX) s.Velocity.X = 0;
        if (hitZ) s.Velocity.Z = 0;

        // Vertical.
        float dy = delta.Y;
        bool probe = s.OnGround && dy <= 0;
        if (probe) dy = -0.02f; // ground probe
        var b = Aabb.FromFeet(pos, hw, height);
        float moved = world.SweepAxis(b, 1, dy);
        pos.Y += moved;
        if (MathF.Abs(moved - dy) > 1e-5f)
        {
            if (dy < 0) { s.OnGround = true; if (s.Velocity.Y < 0) s.Velocity.Y = 0; }
            else if (s.Velocity.Y > 0) s.Velocity.Y = 0; // head bump
        }
        else if (dy < 0)
        {
            s.OnGround = false;
            if (probe) pos.Y -= moved; // undo probe when we walked off an edge
        }
        s.Position = pos;
    }

    private static bool MoveAxis(ref Vector3 pos, int axis, float d, float hw, float height, CollisionWorld world)
    {
        if (d == 0) return false;
        float m = world.SweepAxis(Aabb.FromFeet(pos, hw, height), axis, d);
        if (axis == 0) pos.X += m; else pos.Z += m;
        return MathF.Abs(m - d) > 1e-5f;
    }

    private static void ApplyFriction(ref Vector3 v, float dt)
    {
        float speed = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
        if (speed < 0.01f) { v.X = 0; v.Z = 0; return; }
        float control = MathF.Max(speed, StopSpeed);
        float newSpeed = MathF.Max(0, speed - control * Friction * dt);
        float k = newSpeed / speed;
        v.X *= k; v.Z *= k;
    }

    private static void Accelerate(ref Vector3 v, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        float current = v.X * wishDir.X + v.Z * wishDir.Z;
        float add = wishSpeed - current;
        if (add <= 0) return;
        float a = MathF.Min(accel * wishSpeed * dt, add);
        v.X += a * wishDir.X; v.Z += a * wishDir.Z;
    }

    private static void AirAccelerate(ref Vector3 v, Vector3 wishDir, float wishSpeed, float dt)
    {
        float capped = MathF.Min(wishSpeed, AirSpeedCap);
        float current = v.X * wishDir.X + v.Z * wishDir.Z;
        float add = capped - current;
        if (add <= 0) return;
        float a = MathF.Min(AirAccel * wishSpeed * dt * 10f, add);
        v.X += a * wishDir.X; v.Z += a * wishDir.Z;
    }
}
