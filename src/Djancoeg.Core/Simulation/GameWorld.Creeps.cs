using System.Numerics;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

public enum CreepState : byte { Emerging, Chasing, Attacking, Burrowed, Dying }

/// <summary>A live Arena Creep. Slot index is the network id.</summary>
public struct Creep
{
    public bool Active;
    public byte Kind;
    public CreepState State;
    public MoveState Move;
    public float Yaw;
    public float Health;
    public int Target;
    public float StateTimer;
    public float AttackCooldown;
    public float Life;
    public float RetargetTimer;
    public int Eruptions;
}

public sealed partial class GameWorld
{
    public const int MaxCreeps = 16;
    public readonly Creep[] Creeps = new Creep[MaxCreeps];
    private float _creepTimer = 20f;
    private int _lastCreepKind;

    public CreepDef CreepDefOf(in Creep c) => Content.Creeps[c.Kind];

    /// <summary>Hitbox of a creep (feet at Move.Position; the floating head is centred higher).</summary>
    public Aabb CreepBox(in Creep c)
    {
        var d = Content.Creeps[c.Kind];
        float s = d.Scale;
        return d.Shape switch
        {
            CreepShape.FloatingHead => Aabb.FromCenter(c.Move.Position, new Vector3(0.45f * s)),
            CreepShape.Worm => Aabb.FromFeet(c.Move.Position + new Vector3(0, -0.5f, 0), 0.45f * s, 2.2f * s),
            CreepShape.LongLegs => Aabb.FromFeet(c.Move.Position, 0.25f * s, 1.8f * s),
            _ => Aabb.FromFeet(c.Move.Position, 0.38f * s, 1.8f * s),
        };
    }

    public bool CreepHittable(in Creep c) => c.Active && c.State != CreepState.Burrowed && c.State != CreepState.Dying;

    // ------------------------------------------------------------------ spawning

    private void UpdateCreepSpawner(float dt)
    {
        if (!Settings.Creeps || Content.Creeps.Count == 0 || Phase != MatchPhase.Playing) return;
        _creepTimer -= dt;
        if (_creepTimer > 0) return;
        _creepTimer = _rng.Range(Settings.CreepIntervalMin, Settings.CreepIntervalMax);
        int active = 0;
        foreach (ref readonly var c in Creeps.AsSpan()) if (c.Active) active++;
        if (active > 4) return;
        SpawnCreep(PickCreepKind());
    }

    private int PickCreepKind()
    {
        float total = 0;
        foreach (var d in Content.Creeps) total += d.Weight;
        float r = _rng.Range(0, total);
        for (int i = 0; i < Content.Creeps.Count; i++) { r -= Content.Creeps[i].Weight; if (r <= 0) return i; }
        return 0;
    }

    /// <summary>Spawns a creep (or horde) of <paramref name="kind"/> at a random place away from players. Returns the first slot or -1.</summary>
    public int SpawnCreep(int kind)
    {
        var def = Content.Creeps[kind];
        Vector3 origin;
        if (def.Shape == CreepShape.Worm)
        {
            int victim = RandomAlivePlayer();
            if (victim < 0) return -1;
            origin = Players[victim].Move.Position;
        }
        else origin = RandomSpawnPointAwayFromPlayers();

        int first = -1;
        for (int n = 0; n < Math.Max(1, def.Horde); n++)
        {
            int slot = Array.FindIndex(Creeps, c => !c.Active);
            if (slot < 0) break;
            var pos = origin + (n == 0 ? Vector3.Zero : new Vector3(_rng.Range(-2.5f, 2.5f), 0, _rng.Range(-2.5f, 2.5f)));
            if (def.Shape == CreepShape.FloatingHead) pos.Y += 4f;
            else if (def.Shape != CreepShape.Worm)
            {
                var mp = CreepParams(def);
                for (int k = 0; k < 40 && Collision.Overlaps(Aabb.FromFeet(pos, mp.HalfWidth, mp.Height(false))); k++) pos.Y += 0.25f;
            }
            Creeps[slot] = new Creep
            {
                Active = true, Kind = (byte)kind, Health = def.Health, Target = -1, Life = def.Lifetime,
                State = def.Shape == CreepShape.Worm ? CreepState.Burrowed : CreepState.Emerging,
                StateTimer = def.Shape == CreepShape.Worm ? 2.5f : 1.0f,
                Move = new MoveState { Position = pos }, Yaw = _rng.Range(-MathF.PI, MathF.PI),
            };
            if (first < 0) first = slot;
        }
        if (first >= 0) Push(new GameEvent { Type = GameEventType.CreepSpawn, A = (byte)first, Value = (short)kind, Position = Creeps[first].Move.Position });
        return first;
    }

    private int RandomAlivePlayer()
    {
        Span<int> alive = stackalloc int[SimConstants.MaxPlayers];
        int n = 0;
        for (int i = 0; i < Players.Length; i++) if (Players[i].Active && Players[i].Alive) alive[n++] = i;
        return n == 0 ? -1 : alive[_rng.Next(n)];
    }

    private Vector3 RandomSpawnPointAwayFromPlayers()
    {
        var best = Arena.Spawns.Count > 0 ? Arena.Spawns[0].PositionV : Arena.ZoneCenterV;
        float bestScore = float.MinValue;
        int candidates = Arena.Spawns.Count + Arena.Pickups.Count;
        for (int t = 0; t < 12 && candidates > 0; t++)
        {
            int k = _rng.Next(candidates);
            var p = k < Arena.Spawns.Count ? Arena.Spawns[k].PositionV : Arena.Pickups[k - Arena.Spawns.Count].PositionV;
            float nearest = 1000;
            foreach (ref readonly var pl in Players.AsSpan()) if (pl.Active && pl.Alive) nearest = MathF.Min(nearest, Vector3.Distance(p, pl.Move.Position));
            float score = MathF.Min(nearest, 25f) + _rng.Range(0, 6f);
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    private static MoveParams CreepParams(CreepDef d) => new(d.Speed, 1.25f, MathF.Min(d.Scale, 1.4f));

    // ------------------------------------------------------------------ AI

    private void UpdateCreeps(float dt)
    {
        UpdateCreepSpawner(dt);
        for (int i = 0; i < Creeps.Length; i++)
        {
            ref var c = ref Creeps[i];
            if (!c.Active) continue;
            var def = Content.Creeps[c.Kind];
            c.Life -= dt;
            c.StateTimer -= dt;
            if (c.AttackCooldown > 0) c.AttackCooldown -= dt;

            if (c.State == CreepState.Dying)
            {
                if (c.StateTimer <= 0) c.Active = false;
                continue;
            }
            if (c.Life <= 0 && c.State != CreepState.Attacking)
            {
                Push(new GameEvent { Type = GameEventType.CreepLeave, A = (byte)i, Value = c.Kind, Position = c.Move.Position });
                c.Active = false;
                continue;
            }
            c.RetargetTimer -= dt;
            if (c.RetargetTimer <= 0 || c.Target < 0 || !Players[c.Target].Active || !Players[c.Target].Alive)
            {
                c.RetargetTimer = _rng.Range(1.0f, 2.5f);
                c.Target = NearestPlayer(c.Move.Position, 70f, randomize: true);
            }
            if (_rng.Chance(dt * 0.12f)) Push(new GameEvent { Type = GameEventType.CreepRoar, A = (byte)i, Value = c.Kind, Position = c.Move.Position });

            switch (def.Shape)
            {
                case CreepShape.Worm: UpdateWorm(i, ref c, def, dt); break;
                case CreepShape.FloatingHead: UpdateFlyer(i, ref c, def, dt); break;
                default: UpdateWalker(i, ref c, def, dt); break;
            }
            if (c.Move.Position.Y < Collision.KillY) { c.Active = false; Push(new GameEvent { Type = GameEventType.CreepLeave, A = (byte)i, Value = c.Kind, Position = c.Move.Position }); }
        }
    }

    private int NearestPlayer(Vector3 from, float maxDist, bool randomize)
    {
        int best = -1, second = -1; float bd = maxDist, sd = maxDist;
        for (int p = 0; p < Players.Length; p++)
        {
            ref readonly var pl = ref Players[p];
            if (!pl.Active || !pl.Alive) continue;
            float d = Vector3.Distance(from, pl.Move.Position);
            if (d < bd) { second = best; sd = bd; best = p; bd = d; }
            else if (d < sd) { second = p; sd = d; }
        }
        return randomize && second >= 0 && _rng.Chance(0.3f) ? second : best;
    }

    private void UpdateWalker(int i, ref Creep c, CreepDef def, float dt)
    {
        var mp = CreepParams(def);
        var input = new PlayerInput { Yaw = c.Yaw };
        bool emerging = c.State == CreepState.Emerging && c.StateTimer > 0;
        if (!emerging && c.State == CreepState.Emerging) c.State = CreepState.Chasing;
        if (c.State == CreepState.Attacking && c.StateTimer <= 0) c.State = CreepState.Chasing;

        if (!emerging && c.Target >= 0)
        {
            var tp = Players[c.Target].Move.Position;
            var to = VectorMath.Horizontal(tp - c.Move.Position);
            float dist = to.Length();
            float want = MathF.Atan2(to.X, to.Z);
            c.Yaw += Math.Clamp(VectorMath.WrapAngle(want - c.Yaw), -4f * dt, 4f * dt);
            input.Yaw = c.Yaw;
            float reach = def.AttackRange + 0.4f * def.Scale;
            if (dist > reach * 0.8f) input.Forward = 127;
            // Hop over obstacles and chase players upwards; aliens hop around just because.
            var dir = dist > 0.01f ? to / dist : VectorMath.FlatForward(c.Yaw);
            if (Collision.Raycast(c.Move.Position + new Vector3(0, 0.4f, 0), dir, 1.0f + mp.HalfWidth, out _, out _) || (tp.Y - c.Move.Position.Y > 1.2f && dist < 6f)
                || (def.Shape == CreepShape.Alien && _rng.Chance(dt * 0.8f)))
                input.Buttons |= InputButtons.Jump;
            if (dist < reach && MathF.Abs(tp.Y - c.Move.Position.Y) < 2f * def.Scale && c.AttackCooldown <= 0) CreepAttack(i, ref c, def);
        }
        else if (!emerging)
        {
            c.Yaw += _rng.Range(-1f, 1f) * dt;
            input.Yaw = c.Yaw;
            input.Forward = 60;
        }
        PlayerMovement.Step(ref c.Move, input, mp, Collision, dt);
    }

    private void UpdateFlyer(int i, ref Creep c, CreepDef def, float dt)
    {
        if (c.State == CreepState.Emerging && c.StateTimer <= 0) c.State = CreepState.Chasing;
        if (c.State == CreepState.Attacking && c.StateTimer <= 0) c.State = CreepState.Chasing;
        Vector3 goal = c.Target >= 0 ? Players[c.Target].Move.Position + new Vector3(0, 1.6f + MathF.Sin(MatchTime * 2f + i) * 0.6f, 0)
                                     : c.Move.Position + VectorMath.FlatForward(c.Yaw) * 3f;
        var to = goal - c.Move.Position;
        float dist = to.Length();
        float speed = PlayerMovement.MaxSpeed * def.Speed;
        var desired = dist > 0.01f ? to / dist * MathF.Min(speed, dist * 3f) : Vector3.Zero;
        c.Move.Velocity = Vector3.Lerp(c.Move.Velocity, desired, MathF.Min(1, dt * 2.5f));
        c.Move.Position += c.Move.Velocity * dt;
        var b = Collision.Bounds;
        c.Move.Position = Vector3.Clamp(c.Move.Position, b.Min + new Vector3(1, Collision.KillY + 3, 1), b.Max + new Vector3(-1, 10, -1));
        if (dist > 0.5f) c.Yaw = MathF.Atan2(to.X, to.Z);
        if (c.Target >= 0 && dist < def.AttackRange + 0.5f * def.Scale && c.AttackCooldown <= 0) CreepAttack(i, ref c, def);
    }

    private void UpdateWorm(int i, ref Creep c, CreepDef def, float dt)
    {
        if (c.State == CreepState.Burrowed)
        {
            // Tunnel towards the target, rumbling so players can run.
            if (c.Target >= 0)
            {
                var tp = Players[c.Target].Move.Position;
                var to = VectorMath.Horizontal(tp - c.Move.Position);
                float d = to.Length();
                if (d > 0.1f) c.Move.Position += to / d * MathF.Min(d, PlayerMovement.MaxSpeed * def.Speed * dt);
                if (Collision.Raycast(c.Move.Position + new Vector3(0, 3, 0), -Vector3.UnitY, 12, out float h, out _)) c.Move.Position.Y = c.Move.Position.Y + 3 - h;
            }
            if (((int)(c.StateTimer * 4) & 1) == 0 && _rng.Chance(dt * 6)) Push(new GameEvent { Type = GameEventType.WormRumble, A = (byte)i, Position = c.Move.Position });
            if (c.StateTimer <= 0)
            {
                c.State = CreepState.Attacking;
                c.StateTimer = 3.5f;
                c.Eruptions++;
                Push(new GameEvent { Type = GameEventType.CreepAttack, A = (byte)i, B = 255, Value = c.Kind, Position = c.Move.Position, Flags = 1 });
                // Eruption: everyone on top gets launched.
                _lastCreepKind = c.Kind;
                float r = def.AttackRange;
                for (int p = 0; p < Players.Length; p++)
                {
                    ref readonly var pl = ref Players[p];
                    if (!pl.Active || !pl.Alive) continue;
                    var d = VectorMath.Horizontal(pl.Move.Position - c.Move.Position);
                    if (d.Length() > r || MathF.Abs(pl.Move.Position.Y - c.Move.Position.Y) > 3f) continue;
                    var dir = Vector3.Normalize(d + new Vector3(0, 2.5f, 0) + new Vector3(0.01f, 0, 0));
                    ApplyDamage(p, -1, 255, def.Damage * (1 - d.Length() / r * 0.5f), dir, def.Knockback, KillFlags.Creep);
                }
            }
        }
        else if (c.State == CreepState.Attacking && c.StateTimer <= 0)
        {
            if (c.Eruptions >= 3) { Push(new GameEvent { Type = GameEventType.CreepLeave, A = (byte)i, Value = c.Kind, Position = c.Move.Position }); c.Active = false; return; }
            c.State = CreepState.Burrowed;
            c.StateTimer = _rng.Range(2.5f, 4f);
            c.Target = RandomAlivePlayer();
        }
    }

    private void CreepAttack(int i, ref Creep c, CreepDef def)
    {
        c.AttackCooldown = def.AttackInterval;
        c.State = CreepState.Attacking;
        c.StateTimer = 0.6f;
        ref readonly var t = ref Players[c.Target];
        var dir = Vector3.Normalize(VectorMath.Horizontal(t.Move.Position - c.Move.Position) + new Vector3(0.01f, 0, 0)) + new Vector3(0, 0.45f, 0);
        Push(new GameEvent { Type = GameEventType.CreepAttack, A = (byte)i, B = (byte)c.Target, Value = c.Kind, Position = c.Move.Position });
        _lastCreepKind = c.Kind;
        ApplyDamage(c.Target, -1, 255, def.Damage, Vector3.Normalize(dir), def.Knockback, KillFlags.Creep);
    }

    // ------------------------------------------------------------------ taking damage

    /// <summary>Damages a creep; returns true if it died. Credits <paramref name="attacker"/> with the reward.</summary>
    public bool DamageCreep(int index, int attacker, float amount, Vector3 dir, float knockback)
    {
        ref var c = ref Creeps[index];
        if (!CreepHittable(c)) return false;
        var def = Content.Creeps[c.Kind];
        if (attacker >= 0 && Players[attacker].QuadTimer > 0) amount *= QuadMultiplier;
        c.Health -= amount;
        c.Move.Velocity += dir * knockback / MathF.Max(1f, def.Scale);
        if (c.Target < 0 && attacker >= 0) c.Target = attacker; // aggro on whoever shot it
        Push(new GameEvent { Type = GameEventType.CreepHurt, A = (byte)index, B = (byte)Math.Max(0, attacker), Value = (short)MathF.Round(amount), Position = CreepBox(c).Center });
        if (c.Health > 0) return false;
        c.State = CreepState.Dying;
        c.StateTimer = 1.2f;
        if (attacker >= 0 && Players[attacker].Active && Settings.Mode is GameModeKind.FreeForAll) Players[attacker].Score += def.ScoreReward;
        Push(new GameEvent { Type = GameEventType.CreepKilled, A = (byte)Math.Max(0, attacker), B = (byte)index, Value = c.Kind, Position = CreepBox(c).Center, Vector = dir * knockback });
        return true;
    }

    /// <summary>Nearest creep hit by a ray, or -1.</summary>
    private int RaycastCreeps(Vector3 origin, Vector3 dir, float maxDist, out float t)
    {
        var inv = VectorMath.SafeInverse(dir);
        int best = -1; t = maxDist;
        for (int i = 0; i < Creeps.Length; i++)
        {
            if (!CreepHittable(Creeps[i])) continue;
            if (CreepBox(Creeps[i]).Raycast(origin, inv, t, out float hit, out _) && hit < t) { t = hit; best = i; }
        }
        return best;
    }

    private void ExplodeOnCreeps(Vector3 center, float radius, float damage, float knockback, int owner)
    {
        for (int i = 0; i < Creeps.Length; i++)
        {
            if (!CreepHittable(Creeps[i])) continue;
            var box = CreepBox(Creeps[i]);
            float d = Vector3.Distance(box.ClosestPoint(center), center);
            if (d > radius) continue;
            var dir = Vector3.Normalize(box.Center - center + new Vector3(0, 0.5f, 0));
            DamageCreep(i, owner, damage * (1 - d / radius * 0.6f), dir, knockback);
        }
    }
}
