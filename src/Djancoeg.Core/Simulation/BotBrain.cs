using System.Numerics;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

/// <summary>
/// Lightweight bot AI that produces <see cref="PlayerInput"/> just like a human client.
/// Behaviour: pick a visible target, strafe-fight at a weapon-appropriate range, otherwise roam
/// between pickups and spawn points with feeler-based obstacle avoidance. Skill (0..1) scales
/// reaction time, aim error and turn speed.
/// </summary>
public sealed class BotBrain(ulong seed)
{
    private FastRandom _rng = new(seed);
    private float _skill = 0.5f;
    private int _target = -1;
    private float _thinkTimer, _strafeTimer, _stuckTimer, _goalTimer, _reaction;
    private int _strafeDir = 1;
    private Vector3 _goal;
    private bool _hasGoal;
    private Vector3 _lastPos;
    private float _aimYaw, _aimPitch;
    private Vector3 _aimNoise;

    public void Reset(float skill)
    {
        _skill = Math.Clamp(skill, 0f, 1f);
        _target = -1; _hasGoal = false; _thinkTimer = 0; _reaction = 0;
    }

    public void Think(GameWorld world, int id, ref PlayerInput input, float dt)
    {
        ref var me = ref world.Players[id];
        input.Sequence++;
        input.Buttons = InputButtons.None;
        input.WeaponSlot = 0;
        input.Forward = 0; input.Right = 0;
        if (!me.Alive) { _target = -1; _hasGoal = false; return; }

        var eye = world.EyePosition(me);
        _thinkTimer -= dt;
        if (_thinkTimer <= 0)
        {
            _thinkTimer = 0.2f;
            int prev = _target;
            _target = FindTarget(world, id, eye);
            if (_target != prev) _reaction = MathF.Max(0.08f, 0.55f - _skill * 0.45f);
            _aimNoise = _rng.InsideUnitSphere() * (1.1f - _skill) * 0.9f;
        }
        if (_target >= 0 && (!world.Players[_target].Active || !world.Players[_target].Alive)) _target = -1;

        float desiredYaw = me.Yaw, desiredPitch = 0;
        Vector3 moveDir;

        if (_target >= 0)
        {
            ref readonly var t = ref world.Players[_target];
            var tBox = world.BodyBox(t);
            var aimPoint = tBox.Center + new Vector3(0, tBox.HalfExtents.Y * 0.35f, 0) + _aimNoise;
            float dist = Vector3.Distance(eye, aimPoint);
            SelectWeapon(world, ref me, ref input, dist);

            var w = world.Content.Weapons[me.Weapon];
            if (w.Kind == WeaponKind.Projectile && w.ProjectileSpeed > 0)
            {
                float time = dist / w.ProjectileSpeed;
                aimPoint += t.Move.Velocity * time * (0.4f + 0.6f * _skill);
                if (w.SplashRadius > 0) aimPoint.Y = t.Move.Position.Y + 0.2f; // aim at feet with splash
                aimPoint.Y += 0.5f * w.ProjectileGravity * time * time;
            }

            var to = aimPoint - eye;
            desiredYaw = MathF.Atan2(to.X, to.Z);
            desiredPitch = MathF.Atan2(to.Y, MathF.Sqrt(to.X * to.X + to.Z * to.Z));

            float preferred = w.Kind == WeaponKind.Melee ? 1.5f : w.Pellets > 1 ? 6f : w.Piercing ? 25f : 12f;
            var flat = VectorMath.Horizontal(t.Move.Position - me.Move.Position);
            float flatLen = flat.Length();
            var toward = flatLen > 1e-3f ? flat / flatLen : VectorMath.FlatForward(me.Yaw);
            var side = new Vector3(toward.Z, 0, -toward.X) * _strafeDir;
            float approach = flatLen > preferred + 2 ? 1f : flatLen < preferred - 2 ? -0.7f : 0f;
            moveDir = toward * approach + side;

            _strafeTimer -= dt;
            if (_strafeTimer <= 0) { _strafeDir = -_strafeDir; _strafeTimer = _rng.Range(0.5f, 1.6f); }

            _reaction -= dt;
            float yawErr = MathF.Abs(VectorMath.WrapAngle(desiredYaw - _aimYaw));
            float pitchErr = MathF.Abs(desiredPitch - _aimPitch);
            if (_reaction <= 0 && yawErr < 0.12f + (1 - _skill) * 0.1f && pitchErr < 0.15f && dist <= w.Range + 1f)
                input.Buttons |= InputButtons.Fire;
            if (_rng.Chance(dt * 0.6f)) input.Buttons |= InputButtons.Jump;
            if (me.AbilityCooldown <= 0 && _rng.Chance(dt * 0.5f)) input.Buttons |= InputButtons.Ability;
            if (_rng.Chance(dt * 0.03f)) input.Buttons |= InputButtons.Taunt;
        }
        else
        {
            _goalTimer -= dt;
            if (!_hasGoal || _goalTimer <= 0 || VectorMath.Horizontal(_goal - me.Move.Position).LengthSquared() < 2f) PickGoal(world, ref me);
            var flat = VectorMath.Horizontal(_goal - me.Move.Position);
            moveDir = flat.LengthSquared() > 1e-4f ? Vector3.Normalize(flat) : Vector3.Zero;
            desiredYaw = moveDir == Vector3.Zero ? me.Yaw : MathF.Atan2(moveDir.X, moveDir.Z);
            desiredPitch = 0;
        }

        // Obstacle avoidance and ledge safety using feelers.
        moveDir = Avoid(world, ref me, moveDir, ref input);

        // Smooth aim towards the desired direction (turn speed scales with skill).
        float turn = (2.5f + _skill * 9f) * dt;
        _aimYaw += Math.Clamp(VectorMath.WrapAngle(desiredYaw - _aimYaw), -turn, turn);
        _aimPitch += Math.Clamp(desiredPitch - _aimPitch, -turn, turn);
        _aimYaw = VectorMath.WrapAngle(_aimYaw);
        input.Yaw = _aimYaw;
        input.Pitch = Math.Clamp(_aimPitch, -1.4f, 1.4f);

        // Convert world move direction into local forward/right axes.
        if (moveDir != Vector3.Zero)
        {
            var f = VectorMath.FlatForward(input.Yaw);
            var r = VectorMath.FlatRight(input.Yaw);
            input.Forward = (sbyte)Math.Clamp(Vector3.Dot(moveDir, f) * 127f, -127, 127);
            input.Right = (sbyte)Math.Clamp(Vector3.Dot(moveDir, r) * 127f, -127, 127);
        }

        // Stuck detection.
        _stuckTimer += dt;
        if (_stuckTimer > 1f)
        {
            if (Vector3.DistanceSquared(_lastPos, me.Move.Position) < 0.5f && moveDir != Vector3.Zero)
            {
                input.Buttons |= InputButtons.Jump;
                _hasGoal = false;
                _strafeDir = -_strafeDir;
            }
            _lastPos = me.Move.Position;
            _stuckTimer = 0;
        }
    }

    private Vector3 Avoid(GameWorld world, ref Player me, Vector3 dir, ref PlayerInput input)
    {
        if (dir == Vector3.Zero) return dir;
        var c = world.Collision;
        var knee = me.Move.Position + new Vector3(0, 0.35f, 0);
        if (c.Raycast(knee, dir, 1.2f, out _, out _))
        {
            var chest = me.Move.Position + new Vector3(0, 1.4f, 0);
            if (!c.Raycast(chest, dir, 1.4f, out _, out _) && me.Move.OnGround) input.Buttons |= InputButtons.Jump;
            else
            {
                var left = new Vector3(-dir.Z, 0, dir.X);
                dir = c.Raycast(knee, left, 1.5f, out _, out _) ? -left : left;
            }
        }
        // Don't walk off into the kill plane.
        var ahead = me.Move.Position + dir * 1.6f + new Vector3(0, 0.5f, 0);
        if (!c.Raycast(ahead, -Vector3.UnitY, 8f, out _, out _) && ahead.Y - 8f < c.KillY + 2f) dir = -dir;
        return dir;
    }

    private int FindTarget(GameWorld world, int id, Vector3 eye)
    {
        int best = -1; float bestScore = float.MaxValue;
        ref readonly var me = ref world.Players[id];
        for (int i = 0; i < world.Players.Length; i++)
        {
            if (i == id) continue;
            ref readonly var p = ref world.Players[i];
            if (!p.Active || !p.Alive) continue;
            if (world.IsTeamMode && p.Team == me.Team) continue;
            var center = world.BodyBox(p).Center;
            float d = Vector3.Distance(eye, center);
            if (d > 70f) continue;
            if (world.IsInvisible(p) && d > 5f) continue;
            if (!world.Collision.LineOfSight(eye, center)) continue;
            float score = d - (i == _target ? 6f : 0f);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    private void SelectWeapon(GameWorld world, ref Player me, ref PlayerInput input, float dist)
    {
        int best = me.Weapon; float bestScore = float.MinValue;
        var weapons = world.Content.Weapons;
        for (int w = 0; w < weapons.Count; w++)
        {
            if (!me.Owns(w) || (!weapons[w].InfiniteAmmo && me.Ammo[w] <= 0)) continue;
            var wd = weapons[w];
            float score = wd.Kind switch
            {
                WeaponKind.Melee => dist < 3f ? 50f : -10f,
                _ when wd.Pellets > 1 => dist < 10f ? 40f : 5f,
                _ when wd.Piercing => dist > 15f ? 45f : 15f,
                _ when wd.SplashRadius > 0 => dist is > 4f and < 25f ? 38f : 2f,
                _ => 20f,
            };
            score += wd.Damage * wd.Pellets / MathF.Max(0.1f, wd.FireInterval) * 0.05f;
            if (score > bestScore) { bestScore = score; best = w; }
        }
        if (best != me.Weapon) input.WeaponSlot = (byte)weapons[best].Slot;
    }

    private void PickGoal(GameWorld world, ref Player me)
    {
        _goalTimer = _rng.Range(6f, 12f);
        var arena = world.Arena;
        bool hurt = me.Health < me.MaxHealthOf(world) * 0.6f;
        float bestScore = float.MaxValue; int bestPickup = -1;
        for (int i = 0; i < world.Pickups.Length; i++)
        {
            if (!world.Pickups[i].Available) continue;
            var def = arena.Pickups[i];
            float d = Vector3.Distance(def.PositionV, me.Move.Position);
            float want = def.Kind switch
            {
                PickupKind.HealthSmall or PickupKind.HealthMega => hurt ? 0.3f : 2.5f,
                PickupKind.Weapon => def.Weapon is not null && world.Content.WeaponIndexOf(def.Weapon) is int w and >= 0 && me.Owns(w) ? 3f : 0.6f,
                PickupKind.Quad => 0.4f,
                _ => 1.2f,
            };
            float score = d * want + _rng.Range(0, 10f);
            if (score < bestScore) { bestScore = score; bestPickup = i; }
        }
        if (bestPickup >= 0 && _rng.Chance(0.75f)) _goal = arena.Pickups[bestPickup].PositionV;
        else if (arena.Spawns.Count > 0) _goal = arena.Spawns[_rng.Next(arena.Spawns.Count)].PositionV;
        _hasGoal = true;
    }
}
