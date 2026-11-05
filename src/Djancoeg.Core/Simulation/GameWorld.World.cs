using System.Numerics;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

public sealed partial class GameWorld
{
    // ------------------------------------------------------------------ chaos physics props

    private void UpdateProps(float dt)
    {
        for (int i = 0; i < Props.Length; i++)
        {
            ref var pr = ref Props[i];
            if (!pr.Active) continue;
            if (pr.HitCooldown > 0) pr.HitCooldown -= dt;

            pr.Velocity.Y -= PlayerMovement.Gravity * dt;
            var box = Aabb.FromCenter(pr.Position, pr.HalfExtents);
            bool grounded = false;

            float mx = Collision.SweepAxis(box, 0, pr.Velocity.X * dt);
            if (MathF.Abs(mx - pr.Velocity.X * dt) > 1e-5f) pr.Velocity.X *= -0.35f;
            pr.Position.X += mx; box = Aabb.FromCenter(pr.Position, pr.HalfExtents);

            float mz = Collision.SweepAxis(box, 2, pr.Velocity.Z * dt);
            if (MathF.Abs(mz - pr.Velocity.Z * dt) > 1e-5f) pr.Velocity.Z *= -0.35f;
            pr.Position.Z += mz; box = Aabb.FromCenter(pr.Position, pr.HalfExtents);

            float my = Collision.SweepAxis(box, 1, pr.Velocity.Y * dt);
            if (MathF.Abs(my - pr.Velocity.Y * dt) > 1e-5f)
            {
                if (pr.Velocity.Y < 0) grounded = true;
                pr.Velocity.Y = pr.Velocity.Y < -3f ? -pr.Velocity.Y * 0.3f : 0f;
            }
            pr.Position.Y += my;

            if (grounded)
            {
                float k = MathF.Max(0, 1f - 5f * dt);
                pr.Velocity.X *= k; pr.Velocity.Z *= k;
                pr.AngularVelocity *= MathF.Max(0, 1f - 4f * dt);
                // Settle upright-ish so crates come to rest on a face.
                pr.Rotation.X *= MathF.Max(0, 1f - 3f * dt);
                pr.Rotation.Z *= MathF.Max(0, 1f - 3f * dt);
            }
            pr.Rotation += pr.AngularVelocity * dt;

            if (pr.Position.Y < Collision.KillY)
            {
                // Respawn the prop at its original place.
                var d = Arena.Props[i];
                pr.Position = d.PositionV; pr.Velocity = Vector3.Zero; pr.AngularVelocity = Vector3.Zero; pr.Rotation = Vector3.Zero;
            }

            InteractWithPlayers(i, ref pr);
        }
    }

    private void InteractWithPlayers(int propIndex, ref Prop pr)
    {
        var box = Aabb.FromCenter(pr.Position, pr.HalfExtents);
        float speed = pr.Velocity.Length();
        for (int k = 0; k < Players.Length; k++)
        {
            ref var p = ref Players[k];
            if (!p.Active || !p.Alive) continue;
            var body = BodyBox(p);
            if (!body.Intersects(box.Expand(0.05f))) continue;

            // Fast props hurt ("ketiban gerobak").
            if (speed > 9f && pr.HitCooldown <= 0 && pr.LastToucher != k)
            {
                pr.HitCooldown = 0.5f;
                int attacker = pr.LastToucher >= 0 && MatchTime - pr.LastTouchTime < 6f ? pr.LastToucher : k;
                float dmg = MathF.Min(60f, speed * pr.Mass * 0.05f);
                Push(new GameEvent { Type = GameEventType.PropHit, A = (byte)k, Value = (short)propIndex, Position = pr.Position });
                ApplyDamage(k, attacker, 255, dmg, Vector3.Normalize(pr.Velocity), 4f, KillFlags.PropKill);
                pr.Velocity *= 0.4f;
                continue;
            }

            // Players kick props around.
            var away = VectorMath.Horizontal(pr.Position - body.Center);
            float len = away.Length();
            away = len < 1e-3f ? VectorMath.FlatForward(p.Yaw) : away / len;
            var pv = VectorMath.Horizontal(p.Move.Velocity);
            float push = (pv.Length() * 1.1f + 1.5f) * MathF.Min(1.5f, 25f / pr.Mass);
            pr.Velocity.X = away.X * push + pv.X * 0.3f;
            pr.Velocity.Z = away.Z * push + pv.Z * 0.3f;
            if (pv.LengthSquared() > 30f) pr.Velocity.Y += 1.5f;
            pr.AngularVelocity += new Vector3(away.Z, 0, -away.X) * push * 1.5f;
            pr.LastToucher = k; pr.LastTouchTime = MatchTime;

            // Stand on top of heavy props.
            if (p.Move.Position.Y >= box.Max.Y - 0.25f && p.Move.Velocity.Y <= 0)
            {
                p.Move.Position.Y = box.Max.Y;
                p.Move.Velocity.Y = 0;
                p.Move.OnGround = true;
            }
        }
    }

    // ------------------------------------------------------------------ pickups

    private void UpdatePickups(float dt)
    {
        for (int i = 0; i < Pickups.Length; i++)
        {
            ref var st = ref Pickups[i];
            if (!st.Available)
            {
                st.Timer -= dt;
                if (st.Timer <= 0) st.Available = true;
                continue;
            }
            var def = Arena.Pickups[i];
            var pos = def.PositionV + new Vector3(0, 0.6f, 0);
            for (int k = 0; k < Players.Length; k++)
            {
                ref var p = ref Players[k];
                if (!p.Active || !p.Alive) continue;
                if (!BodyBox(p).Expand(0.45f).Contains(pos)) continue;
                if (!TryTake(ref p, def)) continue;
                st.Available = false;
                st.Timer = def.RespawnSeconds;
                Push(new GameEvent
                {
                    Type = GameEventType.Pickup, A = (byte)k, Value = (short)def.Kind,
                    Weapon = (byte)Math.Max(0, def.Weapon is null ? 0 : Content.WeaponIndexOf(def.Weapon)), Position = pos,
                });
                break;
            }
        }
    }

    private bool TryTake(ref Player p, ArenaPickup def)
    {
        float max = p.MaxHealthOf(this);
        switch (def.Kind)
        {
            case PickupKind.HealthSmall:
                if (p.Health >= max) return false;
                p.Health = MathF.Min(max, p.Health + 25);
                return true;
            case PickupKind.HealthMega:
                if (p.Health >= SimConstants.OverHealthCap) return false;
                p.Health = MathF.Min(SimConstants.OverHealthCap, p.Health + 100);
                return true;
            case PickupKind.Armor:
                if (p.Armor >= SimConstants.MaxArmor) return false;
                p.Armor = MathF.Min(SimConstants.MaxArmor, p.Armor + 50);
                return true;
            case PickupKind.ArmorSmall:
                if (p.Armor >= SimConstants.MaxArmor) return false;
                p.Armor = MathF.Min(SimConstants.MaxArmor, p.Armor + 15);
                return true;
            case PickupKind.Quad:
                p.QuadTimer = 15f;
                return true;
            case PickupKind.Weapon:
            {
                int w = def.Weapon is null ? -1 : Content.WeaponIndexOf(def.Weapon);
                if (w < 0) return false;
                var wd = Content.Weapons[w];
                bool had = p.Owns(w);
                if (had && p.Ammo[w] >= wd.MaxAmmo) return false;
                p.OwnedWeapons |= (ushort)(1 << w);
                p.Ammo[w] = Math.Min(wd.MaxAmmo, p.Ammo[w] + (had ? wd.PickupAmmo : Math.Max(wd.StartAmmo, wd.PickupAmmo)));
                if (!had && (p.IsBot || Content.Weapons[p.Weapon].Slot < wd.Slot)) p.Weapon = w;
                return true;
            }
            case PickupKind.Ammo:
            {
                bool any = false;
                for (int w = 0; w < Content.Weapons.Count; w++)
                {
                    var wd = Content.Weapons[w];
                    if (!p.Owns(w) || wd.InfiniteAmmo || p.Ammo[w] >= wd.MaxAmmo) continue;
                    p.Ammo[w] = Math.Min(wd.MaxAmmo, p.Ammo[w] + Math.Max(1, wd.PickupAmmo / 2));
                    any = true;
                }
                return any;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ match rules

    private void UpdateRules(float dt)
    {
        if (Settings.Mode == GameModeKind.KingOfTheHill) UpdateZone(dt);
        else if (Settings.Mode == GameModeKind.CaptureTheFlag) UpdateFlags(dt);
        else if (Settings.Mode == GameModeKind.Race) UpdateRace();

        int limit = Settings.ScoreLimit;
        int winner = -1;
        if (IsTeamMode)
        {
            if (TeamScores[0] >= limit) winner = 0;
            else if (TeamScores[1] >= limit) winner = 1;
        }
        else
        {
            for (int i = 0; i < Players.Length; i++)
                if (Players[i].Active && Players[i].Score >= limit) { winner = i; break; }
        }

        if (winner < 0 && HasBoss && BossDefeated)
        {
            winner = IsTeamMode ? (TeamScores[0] >= TeamScores[1] ? 0 : 1) : Leader();
            if (winner < 0) winner = 0;
        }

        if (winner < 0 && Settings.TimeLimitSeconds > 0 && MatchTime >= Settings.TimeLimitSeconds)
            winner = IsTeamMode ? (TeamScores[0] >= TeamScores[1] ? 0 : 1) : Leader();

        if (winner >= 0 || (Settings.TimeLimitSeconds > 0 && MatchTime >= Settings.TimeLimitSeconds))
        {
            Phase = MatchPhase.Ended;
            PhaseTimer = 10f;
            Winner = winner;
            Push(new GameEvent { Type = GameEventType.MatchEnd, A = (byte)Math.Max(0, winner), Flags = (byte)(IsTeamMode ? 1 : 0) });
        }
    }

    public int Leader()
    {
        int best = -1;
        for (int i = 0; i < Players.Length; i++)
        {
            if (!Players[i].Active) continue;
            if (best < 0 || Players[i].Score > Players[best].Score ||
                (Players[i].Score == Players[best].Score && Players[i].Deaths < Players[best].Deaths)) best = i;
        }
        return best;
    }

    // ------------------------------------------------------------------ Rebutan Gorengan (capture the flag)

    private const float FlagTouch = 1.4f;
    private const float FlagReturnSeconds = 20f;

    private void UpdateFlags(float dt)
    {
        for (int t = 0; t < 2; t++)
        {
            ref var f = ref Flags[t];
            if (f.Carrier >= 0)
            {
                ref readonly var c = ref Players[f.Carrier];
                if (!c.Active || !c.Alive)
                {
                    Push(new GameEvent { Type = GameEventType.FlagDropped, A = (byte)f.Carrier, Value = (short)t, Position = f.Position });
                    f.Carrier = -1;
                    f.ReturnTimer = FlagReturnSeconds;
                    if (f.Position.Y < Collision.KillY + 1) ReturnFlag(t, -1);
                }
                else f.Position = c.Move.Position + new Vector3(0, 0.2f, 0);
            }
            else if (!f.AtHome)
            {
                f.ReturnTimer -= dt;
                if (f.ReturnTimer <= 0) ReturnFlag(t, -1);
            }

            for (int i = 0; i < Players.Length && f.Carrier < 0; i++)
            {
                ref var p = ref Players[i];
                if (!p.Active || !p.Alive) continue;
                if (Vector3.DistanceSquared(p.Move.Position, f.Position) > FlagTouch * FlagTouch) continue;
                if (p.Team == t)
                {
                    if (!f.AtHome) { p.Score += 1; ReturnFlag(t, i); }
                }
                else
                {
                    f.Carrier = i;
                    f.AtHome = false;
                    Push(new GameEvent { Type = GameEventType.FlagTaken, A = (byte)i, Value = (short)t, Position = f.Position });
                }
            }
        }

        // Capture: bring the enemy gorengan to your own base while yours is at home.
        for (int t = 0; t < 2; t++)
        {
            int enemy = 1 - t;
            ref var ef = ref Flags[enemy];
            if (ef.Carrier < 0 || !Flags[t].AtHome) continue;
            ref var carrier = ref Players[ef.Carrier];
            if (carrier.Team != t) continue;
            if (Vector3.DistanceSquared(carrier.Move.Position, Flags[t].Home) > FlagTouch * FlagTouch * 1.5f) continue;
            TeamScores[t]++;
            carrier.Score += 5;
            Push(new GameEvent { Type = GameEventType.FlagCaptured, A = (byte)ef.Carrier, Value = (short)enemy, Position = Flags[t].Home });
            ef.Carrier = -1;
            ef.AtHome = true;
            ef.Position = ef.Home;
        }
    }

    private void ReturnFlag(int team, int by)
    {
        ref var f = ref Flags[team];
        f.Carrier = -1; f.AtHome = true; f.Position = f.Home; f.ReturnTimer = 0;
        Push(new GameEvent { Type = GameEventType.FlagReturned, A = (byte)Math.Max(0, by), B = (byte)(by < 0 ? 1 : 0), Value = (short)team, Position = f.Home });
    }

    // ------------------------------------------------------------------ Panjat Pinang (race to the top)

    private void UpdateRace()
    {
        var goal = RaceGoal;
        float r = MathF.Max(1.2f, Arena.ZoneRadius);
        for (int i = 0; i < Players.Length; i++)
        {
            ref var p = ref Players[i];
            if (!p.Active || !p.Alive) continue;
            var d = p.Move.Position - goal;
            if (d.X * d.X + d.Z * d.Z > r * r || d.Y < -0.6f || d.Y > 2.5f) continue;
            p.Score++;
            Push(new GameEvent { Type = GameEventType.RaceGoal, A = (byte)i, Value = (short)p.Score, Position = goal });
            Respawn(i);
        }
    }

    private void UpdateZone(float dt)
    {
        var c = Arena.ZoneCenterV;
        float r2 = Arena.ZoneRadius * Arena.ZoneRadius;
        int inside = -1, count = 0;
        for (int i = 0; i < Players.Length; i++)
        {
            ref readonly var p = ref Players[i];
            if (!p.Active || !p.Alive) continue;
            var d = p.Move.Position - c;
            if (d.X * d.X + d.Z * d.Z <= r2 && MathF.Abs(d.Y) < 2.5f) { inside = i; count++; }
        }
        ZoneHolder = count == 1 ? inside : -1;
        if (ZoneHolder < 0) return;
        ref var h = ref Players[ZoneHolder];
        h.ZoneAccumulator += dt;
        if (h.ZoneAccumulator >= 1f)
        {
            h.ZoneAccumulator -= 1f;
            h.Score++;
            Push(new GameEvent { Type = GameEventType.ZoneScore, A = (byte)ZoneHolder, Value = (short)h.Score, Position = c });
        }
    }
}
