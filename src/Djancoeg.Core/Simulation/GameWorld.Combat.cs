using System.Numerics;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

public sealed partial class GameWorld
{
    private const float HeadshotMultiplier = 1.5f;
    private const float QuadMultiplier = 3f;
    private readonly (float t, int id)[] _pierceHits = new (float, int)[SimConstants.MaxPlayers];

    private void TryFire(int id)
    {
        ref var p = ref Players[id];
        if (p.FireCooldown > 0) return;
        var w = Content.Weapons[p.Weapon];
        if (!w.InfiniteAmmo && p.Ammo[p.Weapon] <= 0)
        {
            // Auto-switch to the best weapon that still has ammo.
            for (int k = Content.Weapons.Count - 1; k >= 0; k--)
                if (p.Owns(k) && (Content.Weapons[k].InfiniteAmmo || p.Ammo[k] > 0)) { p.Weapon = k; break; }
            p.FireCooldown = 0.25f;
            return;
        }

        float interval = w.FireInterval;
        if (IsHasted(p)) interval *= 0.75f;
        p.FireCooldown += interval;
        if (p.FireCooldown < 0) p.FireCooldown = 0;
        if (!w.InfiniteAmmo) p.Ammo[p.Weapon]--;
        if (IsInvisible(p)) p.AbilityTimer = 0; // shooting reveals you
        p.SpawnProtect = 0;

        var eye = EyePosition(p);
        var fwd = VectorMath.Forward(p.Yaw, p.Pitch);
        switch (w.Kind)
        {
            case WeaponKind.Melee: FireMelee(id, w, eye, fwd); break;
            case WeaponKind.Hitscan:
                for (int i = 0; i < w.Pellets; i++) FireHitscan(id, w, eye, Spread(fwd, w.SpreadDegrees));
                break;
            case WeaponKind.Projectile:
                for (int i = 0; i < w.Pellets; i++) SpawnProjectile(id, w, eye + fwd * 0.4f, Spread(fwd, w.SpreadDegrees) * w.ProjectileSpeed);
                break;
            case WeaponKind.Blast: FireBlast(id, w, eye, fwd); break;
        }
    }

    private Vector3 Spread(Vector3 dir, float degrees)
    {
        if (degrees <= 0) return dir;
        float r = degrees * MathF.PI / 180f;
        var jitter = _rng.InsideUnitSphere() * MathF.Tan(r);
        return Vector3.Normalize(dir + jitter);
    }

    private void FireMelee(int id, WeaponDef w, Vector3 eye, Vector3 fwd)
    {
        int best = -1; float bestDist = w.Range + 0.6f;
        for (int i = 0; i < Players.Length; i++)
        {
            if (i == id || !Players[i].Active || !Players[i].Alive) continue;
            var box = BodyBox(Players[i]);
            var closest = box.ClosestPoint(eye);
            var to = closest - eye;
            float d = to.Length();
            if (d > bestDist) continue;
            if (d > 0.3f && Vector3.Dot(to / d, fwd) < 0.55f) continue;
            if (!Collision.LineOfSight(eye, closest)) continue;
            best = i; bestDist = d;
        }
        var end = eye + fwd * w.Range;
        Push(new GameEvent { Type = GameEventType.Shot, A = (byte)id, Weapon = (byte)Players[id].Weapon, Position = eye, Vector = end });
        int creep = RaycastCreeps(eye, fwd, w.Range + 0.8f, out float ct);
        if (creep >= 0 && (best < 0 || ct < bestDist)) DamageCreep(creep, id, w.Damage, fwd, w.Knockback);
        else if (best >= 0)
            ApplyDamage(best, id, Players[id].Weapon, w.Damage, fwd, w.Knockback, KillFlags.None);
        else
        {
            HitPropsAlongRay(eye, fwd, w.Range, w.Knockback * 3f, id);
            if (Collision.Raycast(eye, fwd, w.Range, out _, out _, out int box)) DamageBox(box, w.Damage, fwd);
        }
    }

    private void FireHitscan(int id, WeaponDef w, Vector3 eye, Vector3 dir)
    {
        float maxDist = w.Range;
        int wallBox = -1;
        if (Collision.Raycast(eye, dir, maxDist, out float wallDist, out _, out wallBox)) maxDist = wallDist;
        float wallAt = maxDist;

        int lag = Players[id].IsBot ? 0 : Players[id].LagCompensationTicks;
        var inv = VectorMath.SafeInverse(dir);
        int hitCount = 0;
        for (int i = 0; i < Players.Length; i++)
        {
            if (i == id || !Players[i].Active || !Players[i].Alive) continue;
            var mp = ParamsFor(Players[i]);
            var feet = lag > 0 ? HistoricalPosition(i, lag) : Players[i].Move.Position;
            var box = Aabb.FromFeet(feet, mp.HalfWidth, mp.Height(Players[i].Move.Crouched));
            if (box.Raycast(eye, inv, maxDist, out float t, out _)) _pierceHits[hitCount++] = (t, i);
        }

        // Arena creeps in the line of fire.
        int creep = RaycastCreeps(eye, dir, maxDist, out float creepT);
        if (creep >= 0 && (w.Piercing || hitCount == 0 || creepT < MinHit(hitCount)))
        {
            bool head = Creeps[creep].Kind < Content.Creeps.Count && eye.Y + dir.Y * creepT > CreepBox(Creeps[creep]).Max.Y - 0.35f * Content.Creeps[Creeps[creep].Kind].Scale;
            DamageCreep(creep, id, w.Damage * (head ? HeadshotMultiplier : 1f), dir, w.Knockback);
            if (!w.Piercing) { hitCount = 0; maxDist = creepT; }
        }

        float endDist = maxDist;
        if (hitCount > 0)
        {
            var span = _pierceHits.AsSpan(0, hitCount);
            span.Sort(static (a, b) => a.t.CompareTo(b.t));
            int count = w.Piercing ? hitCount : 1;
            for (int k = 0; k < count; k++)
            {
                var (t, victim) = span[k];
                var hitPoint = eye + dir * t;
                var mp = ParamsFor(Players[victim]);
                var feet = lag > 0 ? HistoricalPosition(victim, lag) : Players[victim].Move.Position;
                bool head = hitPoint.Y > feet.Y + mp.Height(Players[victim].Move.Crouched) - 0.32f * mp.Scale;
                float dmg = w.Damage * (head ? HeadshotMultiplier : 1f);
                ApplyDamage(victim, id, Players[id].Weapon, dmg, dir, w.Knockback, head ? KillFlags.Headshot : KillFlags.None);
            }
            if (!w.Piercing) endDist = span[0].t;
        }
        else
        {
            HitPropsAlongRay(eye, dir, maxDist, w.Knockback * 2f, id);
        }
        if (wallBox >= 0 && endDist >= wallAt - 0.01f) DamageBox(wallBox, w.Damage, dir);
        Push(new GameEvent { Type = GameEventType.Shot, A = (byte)id, Weapon = (byte)Players[id].Weapon, Position = eye, Vector = eye + dir * endDist });
    }

    /// <summary>Kipas Sate: a cone of wind that shoves players and props and turns enemy projectiles around.</summary>
    private void FireBlast(int id, WeaponDef w, Vector3 eye, Vector3 fwd)
    {
        float cos = MathF.Cos(w.BlastConeDegrees * MathF.PI / 180f);
        Push(new GameEvent { Type = GameEventType.Shot, A = (byte)id, Weapon = (byte)Players[id].Weapon, Position = eye, Vector = eye + fwd * w.Range, Flags = 2 });
        for (int i = 0; i < Players.Length; i++)
        {
            if (i == id || !Players[i].Active || !Players[i].Alive) continue;
            var center = BodyBox(Players[i]).Center;
            var to = center - eye;
            float d = to.Length();
            if (d > w.Range || d < 0.01f || Vector3.Dot(to / d, fwd) < cos) continue;
            if (!Collision.LineOfSight(eye, center)) continue;
            float falloff = 1f - d / w.Range * 0.6f;
            var dir = Vector3.Normalize(fwd + new Vector3(0, 0.35f, 0));
            ApplyDamage(i, id, Players[id].Weapon, w.Damage * falloff, dir, w.Knockback * falloff, KillFlags.None);
        }
        for (int i = 0; i < Props.Length; i++)
        {
            ref var pr = ref Props[i];
            var to = pr.Position - eye;
            float d = to.Length();
            if (!pr.Active || d > w.Range * 1.2f || d < 0.01f || Vector3.Dot(to / d, fwd) < cos) continue;
            pr.Velocity += (fwd + new Vector3(0, 0.4f, 0)) * w.Knockback * 1.2f * (20f / pr.Mass);
            pr.AngularVelocity += _rng.InsideUnitSphere() * 8f;
            pr.LastToucher = id; pr.LastTouchTime = MatchTime;
        }
        for (int i = 0; i < Projectiles.Length; i++)
        {
            ref var pr = ref Projectiles[i];
            if (!pr.Active || pr.Owner == id) continue;
            var to = pr.Position - eye;
            float d = to.Length();
            if (d > w.Range * 1.3f || d < 0.01f || Vector3.Dot(to / d, fwd) < cos * 0.9f) continue;
            float speed = MathF.Max(12f, pr.Velocity.Length());
            pr.Velocity = fwd * speed;
            pr.Owner = (byte)id;
            pr.Age = 0;
            pr.Life = MathF.Max(pr.Life, 1.5f);
            Push(new GameEvent { Type = GameEventType.Deflect, A = (byte)id, Weapon = pr.Weapon, Position = pr.Position });
        }
    }

    private float MinHit(int count)
    {
        float m = float.MaxValue;
        for (int k = 0; k < count; k++) m = MathF.Min(m, _pierceHits[k].t);
        return m;
    }

    private void HitPropsAlongRay(Vector3 origin, Vector3 dir, float maxDist, float impulse, int attacker)
    {
        var inv = VectorMath.SafeInverse(dir);
        for (int i = 0; i < Props.Length; i++)
        {
            ref var pr = ref Props[i];
            if (!pr.Active) continue;
            if (Aabb.FromCenter(pr.Position, pr.HalfExtents).Raycast(origin, inv, maxDist, out _, out _))
            {
                pr.Velocity += dir * impulse * (20f / pr.Mass) + new Vector3(0, 1.5f, 0);
                pr.AngularVelocity += _rng.InsideUnitSphere() * 6f;
                pr.LastToucher = attacker; pr.LastTouchTime = MatchTime;
                return;
            }
        }
    }

    private void SpawnProjectile(int owner, WeaponDef w, Vector3 pos, Vector3 vel)
    {
        for (int i = 0; i < Projectiles.Length; i++)
        {
            if (Projectiles[i].Active) continue;
            Projectiles[i] = new Projectile
            {
                Active = true, Owner = (byte)owner, Weapon = (byte)Players[owner].Weapon, Id = _nextProjectileId++,
                Position = pos, Velocity = vel, Life = w.ProjectileLifetime, Quad = Players[owner].QuadTimer > 0,
            };
            if (_nextProjectileId == 0) _nextProjectileId = 1;
            Push(new GameEvent { Type = GameEventType.Shot, A = (byte)owner, Weapon = (byte)Players[owner].Weapon, Position = pos, Vector = pos, Flags = 1 });
            return;
        }
    }

    private void UpdateProjectiles(float dt)
    {
        for (int i = 0; i < Projectiles.Length; i++)
        {
            ref var pr = ref Projectiles[i];
            if (!pr.Active) continue;
            var w = Content.Weapons[pr.Weapon];
            pr.Age += dt;
            pr.Life -= dt;
            pr.Velocity.Y -= w.ProjectileGravity * dt;

            var move = pr.Velocity * dt;
            float len = move.Length();
            if (len > 1e-5f)
            {
                var dir = move / len;
                bool hitWorld = Collision.Raycast(pr.Position, dir, len, out float tw, out var normal, out int hitBox);
                float tHit = hitWorld ? tw : len;

                // Players (sphere approximated by inflating their boxes).
                int victim = -1;
                var inv = VectorMath.SafeInverse(dir);
                for (int k = 0; k < Players.Length; k++)
                {
                    if (!Players[k].Active || !Players[k].Alive) continue;
                    if (k == pr.Owner && pr.Age < 0.25f) continue;
                    if (BodyBox(Players[k]).Expand(0.15f).Raycast(pr.Position, inv, tHit, out float tp, out _) && tp < tHit)
                    {
                        tHit = tp; victim = k;
                    }
                }
                // Arena creeps.
                int creepHit = -1;
                for (int k = 0; k < Creeps.Length; k++)
                {
                    if (!CreepHittable(Creeps[k])) continue;
                    if (CreepBox(Creeps[k]).Expand(0.1f).Raycast(pr.Position, inv, tHit, out float tc, out _) && tc < tHit) { tHit = tc; creepHit = k; victim = -1; }
                }
                if (creepHit >= 0)
                {
                    pr.Position += dir * tHit;
                    DamageCreep(creepHit, pr.Owner, w.Damage, dir, w.Knockback);
                    Detonate(ref pr, w, -1);
                    continue;
                }
                // Props.
                int propHit = -1;
                for (int k = 0; k < Props.Length; k++)
                {
                    if (!Props[k].Active) continue;
                    if (Aabb.FromCenter(Props[k].Position, Props[k].HalfExtents).Expand(0.1f).Raycast(pr.Position, inv, tHit, out float tp, out _) && tp < tHit)
                    {
                        tHit = tp; propHit = k; victim = -1;
                    }
                }

                if (victim >= 0)
                {
                    pr.Position += dir * tHit;
                    float dmg = w.Damage;
                    ApplyDamage(victim, pr.Owner, pr.Weapon, dmg, dir, w.Knockback * (w.SplashRadius > 0 ? 0.5f : 1f), KillFlags.None);
                    Detonate(ref pr, w, victim);
                    continue;
                }
                if (propHit >= 0)
                {
                    pr.Position += dir * tHit;
                    ref var prop = ref Props[propHit];
                    prop.Velocity += dir * (w.Knockback + 4f) * (20f / prop.Mass);
                    prop.AngularVelocity += _rng.InsideUnitSphere() * 8f;
                    prop.LastToucher = pr.Owner; prop.LastTouchTime = MatchTime;
                    if (w.ProjectileBounces) { pr.Velocity = Vector3.Reflect(pr.Velocity, -dir) * 0.5f; }
                    else { Detonate(ref pr, w, -1); continue; }
                }
                else if (hitWorld)
                {
                    pr.Position += dir * MathF.Max(0, tHit - 0.02f);
                    if (w.ProjectileBounces && normal != Vector3.Zero)
                    {
                        pr.Velocity = Vector3.Reflect(pr.Velocity, normal) * 0.55f;
                        if (pr.Velocity.LengthSquared() > 4f)
                            Push(new GameEvent { Type = GameEventType.ProjectileBounce, A = pr.Owner, Weapon = pr.Weapon, Position = pr.Position });
                        else pr.Velocity = Vector3.Zero;
                    }
                    else { DamageBox(hitBox, w.Damage, dir); Detonate(ref pr, w, -1); continue; }
                }
                else pr.Position += move;
            }

            if (pr.Life <= 0) Detonate(ref pr, w, -1);
        }
    }

    private void Detonate(ref Projectile pr, WeaponDef w, int directVictim)
    {
        pr.Active = false;
        if (w.SplashRadius <= 0)
        {
            Push(new GameEvent { Type = GameEventType.Explosion, A = pr.Owner, Weapon = pr.Weapon, Position = pr.Position, Value = 0 });
            return;
        }
        Explode(pr.Position, w.SplashRadius, w.SplashDamage, w.Knockback, pr.Owner, pr.Weapon, directVictim);
    }

    /// <summary>Radius damage with linear falloff and line-of-sight, plus knockback (rocket jumps!).</summary>
    public void Explode(Vector3 center, float radius, float damage, float knockback, int owner, int weapon, int skipVictim)
    {
        Push(new GameEvent { Type = GameEventType.Explosion, A = (byte)Math.Max(0, owner), Weapon = (byte)weapon, Position = center, Value = (short)(radius * 10) });
        ExplodeOnCreeps(center, radius, damage, knockback, owner);
        ExplodeOnBoxes(center, radius, damage);
        for (int i = 0; i < Players.Length; i++)
        {
            if (!Players[i].Active || !Players[i].Alive || i == skipVictim) continue;
            var box = BodyBox(Players[i]);
            var closest = box.ClosestPoint(center);
            float d = Vector3.Distance(closest, center);
            if (d > radius) continue;
            if (!Collision.LineOfSight(center + new Vector3(0, 0.1f, 0), box.Center)) continue;
            float falloff = 1f - d / radius;
            var dir = box.Center - center;
            dir = dir.LengthSquared() < 1e-4f ? Vector3.UnitY : Vector3.Normalize(dir);
            dir = Vector3.Normalize(dir + new Vector3(0, 0.35f, 0));
            ApplyDamage(i, owner, weapon, damage * falloff, dir, knockback * (0.4f + 0.6f * falloff), KillFlags.None);
        }
        for (int i = 0; i < Props.Length; i++)
        {
            ref var pr = ref Props[i];
            if (!pr.Active) continue;
            var to = pr.Position - center;
            float d = to.Length();
            if (d > radius * 1.5f) continue;
            float f = 1f - d / (radius * 1.5f);
            var dir = d < 1e-3f ? Vector3.UnitY : to / d;
            pr.Velocity += (dir + new Vector3(0, 0.6f, 0)) * knockback * 1.4f * f * (20f / pr.Mass);
            pr.AngularVelocity += _rng.InsideUnitSphere() * 12f * f;
            pr.LastToucher = owner; pr.LastTouchTime = MatchTime;
        }
    }

    /// <summary>Single entry point for all damage: teams, quad, stone skin, armor, knockback, kills.</summary>
    public void ApplyDamage(int victim, int attacker, int weapon, float amount, Vector3 dir, float knockback, KillFlags flags)
    {
        ref var v = ref Players[victim];
        if (!v.Active || !v.Alive) return;
        bool self = attacker == victim;
        bool hasAttacker = attacker >= 0 && attacker < Players.Length && Players[attacker].Active;

        if (hasAttacker && !self && IsTeamMode && !Settings.FriendlyFire && Players[attacker].Team == v.Team) return;

        float kbScale = 1f;
        if (hasAttacker && Players[attacker].QuadTimer > 0 && weapon != 255) { amount *= QuadMultiplier; kbScale = 1.5f; }
        if (self && weapon < Content.Weapons.Count) amount *= Content.Weapons[weapon].SelfDamageScale;
        if (IsStone(v)) { amount *= Content.Characters[v.CharacterIndex].Ability.Power; kbScale *= 0.3f; }

        // Knockback always applies (even during spawn protection) - it is half the fun.
        float massScale = 1f / MathF.Max(0.6f, Content.Characters[v.CharacterIndex].Scale);
        v.Move.Velocity += dir * knockback * kbScale * massScale * (self ? 1.15f : 1f);
        if (dir.Y > 0.2f && knockback > 3f) v.Move.OnGround = false;

        if (v.SpawnProtect > 0 && !self) return;

        float save = MathF.Min(v.Armor, amount * 0.66f);
        v.Armor -= save;
        v.Health -= amount - save;
        if (hasAttacker && !self) { v.LastAttacker = attacker; v.LastAttackerTime = MatchTime; }

        Push(new GameEvent { Type = GameEventType.Hit, A = (byte)victim, B = (byte)Math.Max(0, attacker), Weapon = (byte)weapon, Value = (short)MathF.Round(amount), Flags = (byte)flags, Position = v.Move.Position });

        if (v.Health <= 0)
        {
            if (v.Health < -40) flags |= KillFlags.Gibbed;
            Kill(victim, attacker, weapon, flags, dir * knockback);
        }
    }

    private void Kill(int victim, int attacker, int weapon, KillFlags flags, Vector3 impulse)
    {
        ref var v = ref Players[victim];
        if (!v.Alive) return;
        v.Alive = false;
        v.Deaths++;
        v.RespawnTimer = SimConstants.RespawnDelay;
        v.Streak = 0;
        bool byCreep = (flags & KillFlags.Creep) != 0;
        bool suicide = !byCreep && (attacker < 0 || attacker == victim);
        if (byCreep) attacker = victim;
        else if (suicide)
        {
            flags |= KillFlags.Suicide;
            if (Settings.Mode is GameModeKind.FreeForAll or GameModeKind.TeamDeathmatch) v.Score--;
            attacker = victim;
        }
        else if (!byCreep)
        {
            ref var a = ref Players[attacker];
            a.Kills++;
            a.Streak++;
            if (Settings.Mode == GameModeKind.TeamDeathmatch) TeamScores[a.Team]++;
            else if (Settings.Mode is GameModeKind.FreeForAll or GameModeKind.CaptureTheFlag) a.Score++;
            if (a.Streak is 3 or 5 or 8 or 12)
                Push(new GameEvent { Type = GameEventType.Streak, A = (byte)attacker, Value = (short)a.Streak });
        }
        Push(new GameEvent
        {
            Type = GameEventType.Kill, A = (byte)victim, B = (byte)attacker, Weapon = (byte)Math.Clamp(weapon, 0, 255),
            Flags = (byte)flags, Position = v.Move.Position, Vector = impulse, Value = (short)(byCreep ? _lastCreepKind : 0),
        });
        if (ZoneHolder == victim) ZoneHolder = -1;
    }

    // ------------------------------------------------------------------ abilities

    private void TryAbility(int id)
    {
        ref var p = ref Players[id];
        if (p.AbilityCooldown > 0) return;
        var ab = Content.Characters[p.CharacterIndex].Ability;
        if (ab.Kind == AbilityKind.None) return;
        p.AbilityCooldown = ab.Cooldown;
        var fwd = VectorMath.FlatForward(p.Yaw);
        switch (ab.Kind)
        {
            case AbilityKind.SuperJump:
                p.Move.Velocity.Y = PlayerMovement.JumpVelocity * ab.Power;
                p.Move.Velocity += fwd * 3f;
                p.Move.OnGround = false;
                break;
            case AbilityKind.Dash:
                var dash = fwd * 21f * ab.Power;
                p.Move.Velocity = new Vector3(dash.X, MathF.Max(p.Move.Velocity.Y, 3.5f), dash.Z);
                p.Move.OnGround = false;
                p.DashTimer = 0.4f;
                p.DashHitMask = 0;
                break;
            case AbilityKind.Shockwave:
                Shockwave(id, 7f * MathF.Sqrt(ab.Power), 12f * ab.Power, 13f * ab.Power);
                break;
            case AbilityKind.Heal:
                p.Health = MathF.Min(p.MaxHealthOf(this) + 25, p.Health + ab.Power);
                break;
            case AbilityKind.Invisibility:
            case AbilityKind.Haste:
            case AbilityKind.StoneSkin:
            case AbilityKind.Levitate:
                p.AbilityTimer = ab.Duration;
                if (ab.Kind == AbilityKind.Levitate) { p.Move.Velocity.Y = MathF.Max(p.Move.Velocity.Y, 4f); p.Move.OnGround = false; }
                break;
            case AbilityKind.Teleport:
            {
                var from = p.Move.Position;
                if (!Teleport(id, ab.Power)) { p.AbilityCooldown = 0.5f; return; }
                Push(new GameEvent { Type = GameEventType.Ability, A = (byte)id, Value = (short)ab.Kind, Position = from, Vector = Players[id].Move.Position });
                return;
            }
            case AbilityKind.Pull:
                Shockwave(id, 9f * MathF.Sqrt(ab.Power), 6f * ab.Power, -11f * ab.Power);
                break;
            case AbilityKind.Rally:
                Rally(id, ab.Power);
                break;
        }
        Push(new GameEvent { Type = GameEventType.Ability, A = (byte)id, Value = (short)ab.Kind, Position = p.Move.Position });
    }

    private void Shockwave(int id, float radius, float damage, float force)
    {
        var center = Players[id].Move.Position + new Vector3(0, 0.9f, 0);
        for (int i = 0; i < Players.Length; i++)
        {
            if (i == id || !Players[i].Active || !Players[i].Alive) continue;
            var to = BodyBox(Players[i]).Center - center;
            float d = to.Length();
            if (d > radius) continue;
            var dir = Vector3.Normalize(VectorMath.Horizontal(to) + new Vector3(0, 0.01f, 0));
            dir = force >= 0 ? Vector3.Normalize(dir + new Vector3(0, 0.55f, 0)) : Vector3.Normalize(-dir + new Vector3(0, 0.3f, 0));
            ApplyDamage(i, id, 255, damage * (1f - d / radius * 0.5f), dir, MathF.Abs(force) * (force < 0 ? MathF.Min(1, d / radius + 0.3f) : 1f), KillFlags.Ability);
        }
        for (int i = 0; i < Props.Length; i++)
        {
            ref var pr = ref Props[i];
            var to = pr.Position - center;
            float d = to.Length();
            if (!pr.Active || d > radius) continue;
            pr.Velocity += (Vector3.Normalize(to + new Vector3(0, 0.01f, 0)) * MathF.Sign(force) + new Vector3(0, 0.7f, 0)) * MathF.Abs(force) * (20f / pr.Mass);
            pr.AngularVelocity += _rng.InsideUnitSphere() * 10f;
            pr.LastToucher = id; pr.LastTouchTime = MatchTime;
        }
        Push(new GameEvent { Type = GameEventType.Explosion, A = (byte)id, Weapon = 255, Position = center, Value = (short)(radius * 10), Flags = 1 });
    }

    /// <summary>Si Kancil: blink forward up to 9 m x power, stopping before walls; fails if there is no room.</summary>
    private bool Teleport(int id, float power)
    {
        ref var p = ref Players[id];
        var mp = ParamsFor(p);
        var dir = VectorMath.Forward(p.Yaw, MathF.Max(-0.2f, MathF.Min(0.5f, p.Pitch)));
        var eye = EyePosition(p);
        float max = 9f * power;
        if (Collision.Raycast(eye, dir, max, out float hit, out _)) max = hit - 0.6f;
        for (float d = max; d >= 2f; d -= 0.5f)
        {
            var feet = p.Move.Position + dir * d;
            if (Collision.Overlaps(Aabb.FromFeet(feet, mp.HalfWidth, mp.Height(false)))) continue;
            p.Move.Position = feet;
            p.Move.Velocity = dir * 6f;
            p.Move.OnGround = false;
            return true;
        }
        return false;
    }

    /// <summary>Bapak-bapak Ronda: heal and speed up yourself and nearby teammates.</summary>
    private void Rally(int id, float power)
    {
        ref readonly var caster = ref Players[id];
        for (int i = 0; i < Players.Length; i++)
        {
            ref var p = ref Players[i];
            if (!p.Active || !p.Alive) continue;
            bool friend = i == id || (IsTeamMode && p.Team == caster.Team);
            if (!friend || Vector3.Distance(p.Move.Position, caster.Move.Position) > 9f) continue;
            p.Health = MathF.Min(p.MaxHealthOf(this) + 15, p.Health + power);
            p.BoostTimer = 4f;
        }
    }

    private void DashContact(int id)
    {
        ref var p = ref Players[id];
        var box = BodyBox(p).Expand(0.35f);
        for (int i = 0; i < Players.Length; i++)
        {
            if (i == id || !Players[i].Active || !Players[i].Alive || (p.DashHitMask & (1u << i)) != 0) continue;
            if (!box.Intersects(BodyBox(Players[i]))) continue;
            p.DashHitMask |= 1u << i;
            var dir = Vector3.Normalize(VectorMath.FlatForward(p.Yaw) + new Vector3(0, 0.5f, 0));
            ApplyDamage(i, id, 255, 35f * Content.Characters[p.CharacterIndex].Ability.Power, dir, 12f, KillFlags.Ability);
        }
    }
}
