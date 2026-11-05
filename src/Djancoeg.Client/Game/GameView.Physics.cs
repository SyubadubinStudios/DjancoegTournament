using System.Numerics;
using Djancoeg.Client.Audio;
using Djancoeg.Client.Render;
using Djancoeg.Client.Ui;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;
using Djancoeg.Core.Simulation;
using Djancoeg.Net;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Game;

/// <summary>v2.0: physics corpses ("ragdoll-lite") and destructible arena boxes.</summary>
public sealed partial class GameView
{
    private struct Corpse
    {
        public bool Active;
        public int PlayerId;          // -1 for creeps
        public CharacterModel? Model;
        public float Scale;
        public Color Fallback;
        public Vector3 Pos, Vel;
        public float Yaw, Pitch, Roll, PitchVel, RollVel, YawVel;
        public float Age;
        public bool Grounded;
        public int Bounces;
        public Color Blood;
    }

    private readonly Corpse[] _corpses = new Corpse[40];
    private int _corpseNext;

    private void SpawnCorpse(int playerId, CharacterModel? model, float scale, Vector3 pos, Vector3 impulse, float yaw, Color fallback, Color blood)
    {
        ref var c = ref _corpses[_corpseNext];
        _corpseNext = (_corpseNext + 1) % _corpses.Length;
        var rng = new FastRandom((ulong)(_time * 1000) + (ulong)_corpseNext);
        var kick = impulse * 0.55f;
        if (kick.LengthSquared() > 400) kick = Vector3.Normalize(kick) * 20;
        c = new Corpse
        {
            Active = true, PlayerId = playerId, Model = model, Scale = scale, Fallback = fallback, Blood = blood,
            Pos = pos, Vel = kick + new Vector3(0, 2.5f + kick.Length() * 0.25f, 0), Yaw = yaw,
            PitchVel = -(2.5f + kick.Length() * 0.4f) * (rng.Chance(0.5f) ? 1 : -1), RollVel = rng.Range(-3, 3), YawVel = rng.Range(-2, 2),
        };
    }

    private void RemovePlayerCorpse(int id)
    {
        for (int i = 0; i < _corpses.Length; i++) if (_corpses[i].Active && _corpses[i].PlayerId == id) _corpses[i].Active = false;
    }

    private bool HasCorpse(int id)
    {
        for (int i = 0; i < _corpses.Length; i++) if (_corpses[i].Active && _corpses[i].PlayerId == id) return true;
        return false;
    }

    private void UpdateCorpses(float dt)
    {
        var col = _session.Client.Collision;
        for (int i = 0; i < _corpses.Length; i++)
        {
            ref var c = ref _corpses[i];
            if (!c.Active) continue;
            c.Age += dt;
            if (c.Age > 14f) { c.Active = false; continue; }
            if (c.PlayerId >= 0 && TryView(c.PlayerId, out var p) && p.Has(NetPlayerFlags.Alive) && c.Age > 0.5f) { c.Active = false; continue; }

            c.Vel.Y -= PlayerMovement.Gravity * dt;
            if (col is not null)
            {
                var half = new Vector3(0.35f, 0.25f, 0.35f) * c.Scale;
                var box = Aabb.FromCenter(c.Pos, half);
                float mx = col.SweepAxis(box, 0, c.Vel.X * dt); if (MathF.Abs(mx - c.Vel.X * dt) > 1e-5f) c.Vel.X *= -0.4f; c.Pos.X += mx;
                box = Aabb.FromCenter(c.Pos, half);
                float mz = col.SweepAxis(box, 2, c.Vel.Z * dt); if (MathF.Abs(mz - c.Vel.Z * dt) > 1e-5f) c.Vel.Z *= -0.4f; c.Pos.Z += mz;
                box = Aabb.FromCenter(c.Pos, half);
                float my = col.SweepAxis(box, 1, c.Vel.Y * dt);
                bool hitFloor = MathF.Abs(my - c.Vel.Y * dt) > 1e-5f && c.Vel.Y < 0;
                c.Pos.Y += my;
                if (hitFloor)
                {
                    if (c.Vel.Y < -4f && c.Bounces < 3)
                    {
                        c.Bounces++;
                        c.Vel.Y = -c.Vel.Y * 0.35f;
                        _fx.Dust(c.Pos - new Vector3(0, half.Y, 0), 5, 1.5f);
                        if (_app.Settings.Blood) _fx.SplatBelow(c.Pos, 0.7f * c.Scale, new Color((byte)(c.Blood.R * 0.6f), (byte)(c.Blood.G * 0.6f), (byte)(c.Blood.B * 0.6f), (byte)220), 30f);
                        _app.Sounds.PlayAt(Sfx.Land, c.Pos, ListenerPos, _session.Yaw, 0.7f, 0.7f);
                    }
                    else { c.Vel.Y = 0; c.Grounded = true; }
                    c.Vel.X *= 0.6f; c.Vel.Z *= 0.6f;
                }
                if (c.Pos.Y < col.KillY) c.Active = false;
            }
            if (c.Grounded)
            {
                // Settle lying down: pitch towards +-90 degrees, roll to flat, stop spinning.
                float target = c.Pitch >= 0 ? MathF.PI / 2 : -MathF.PI / 2;
                c.Pitch += (target - c.Pitch) * MathF.Min(1, dt * 6f);
                c.Roll += (0 - c.Roll) * MathF.Min(1, dt * 6f);
                c.PitchVel = c.RollVel = c.YawVel = 0;
                c.Vel.X *= MathF.Max(0, 1 - dt * 6); c.Vel.Z *= MathF.Max(0, 1 - dt * 6);
            }
            else
            {
                c.Pitch += c.PitchVel * dt;
                c.Roll += c.RollVel * dt;
                c.Yaw += c.YawVel * dt;
            }
        }
    }

    private unsafe void DrawCorpses()
    {
        for (int i = 0; i < _corpses.Length; i++)
        {
            ref var c = ref _corpses[i];
            if (!c.Active) continue;
            Rlgl.PushMatrix();
            Rlgl.Translatef(c.Pos.X, c.Pos.Y, c.Pos.Z);
            Rlgl.Rotatef(c.Yaw * 57.2958f, 0, 1, 0);
            Rlgl.Rotatef(c.Pitch * 57.2958f, 1, 0, 0);
            Rlgl.Rotatef(c.Roll * 57.2958f, 0, 0, 1);
            var tint = Color.White;
            if (c.Age > 11f) tint.A = (byte)(255 * MathF.Max(0, (14f - c.Age) / 3f));
            if (c.Model is { } m)
            {
                if (m.HasAnimations && m.Idle >= 0) UpdateModelAnimation(m.Model, m.Animations[m.Idle], 0);
                DrawModelEx(m.Model, new Vector3(0, -0.9f * c.Scale, 0), Vector3.UnitY, 0, new Vector3(c.Scale), tint);
            }
            else DrawCapsule(new Vector3(0, -0.6f * c.Scale, 0), new Vector3(0, 0.6f * c.Scale, 0), 0.35f * c.Scale, 6, 6, c.Fallback);
            Rlgl.PopMatrix();
        }
    }

    /// <summary>Creates a corpse for a killed player or creep.</summary>
    private void CorpseForPlayer(in GameEvent e)
    {
        if (!TryView(e.A, out var p)) return;
        var chars = _session.Content.Characters;
        var cd = chars[Math.Min(p.Character, chars.Count - 1)];
        RemovePlayerCorpse(e.A);
        SpawnCorpse(e.A, _app.Models.Character(cd), cd.Scale, p.Position + new Vector3(0, 0.9f * cd.Scale, 0), e.Vector + p.Velocity * 0.5f, p.Yaw,
            Theme.Rgba(ColorUtil.ParseRgba(cd.PrimaryColor)), new Color(140, 8, 10, 255));
    }

    private void CorpseForCreep(in GameEvent e)
    {
        if ((uint)e.B >= (uint)_creeps.Length || CreepDef(e.Value) is not { } def || def.Shape == CreepShape.Worm) return;
        ref var v = ref _creeps[e.B];
        var model = _app.Models.Creep(def);
        float lift = def.Shape == CreepShape.FloatingHead ? 0 : 0.9f * def.Scale;
        SpawnCorpse(-1, model, def.Scale, v.Pos + new Vector3(0, lift, 0), e.Vector, v.Yaw, Col(def.Color), Col(def.BloodColor));
        v.Seen = false; // the corpse replaces the live creep visual
    }

    // ------------------------------------------------------------------ destructibles

    private void OnBoxEvent(in GameEvent e)
    {
        var arena = _session.Client.Arena;
        if (arena is null) return;
        int boxIndex = DestructibleBoxIndex(arena, e.Value);
        if (boxIndex < 0) return;
        var b = arena.Boxes[boxIndex];
        var color = Theme.Rgba(ColorUtil.ParseRgba(b.Color));
        var size = b.MaxV - b.MinV;
        if (e.Type == GameEventType.BoxDestroyed)
        {
            // Splinters in the box colour, dust, and a crunch.
            var rng = new FastRandom((ulong)e.Id * 31);
            int n = (int)Math.Clamp(size.X * size.Y * size.Z * 30, 12, 60);
            for (int i = 0; i < n; i++)
            {
                var p = b.MinV + new Vector3(rng.NextFloat() * size.X, rng.NextFloat() * size.Y, rng.NextFloat() * size.Z);
                var v = rng.InsideUnitSphere() * 5f + e.Vector * 3f + new Vector3(0, 4f, 0);
                _fx.Emit(p, v, color, rng.Range(1.0f, 2.2f), rng.Range(0.06f, 0.18f), 16f, 0.3f);
            }
            _fx.Dust(e.Position, 20, 3f);
            _app.Sounds.PlayAt(Sfx.PropHit, e.Position, ListenerPos, _session.Yaw, 1f, 0.7f);
            _app.Sounds.PlayAt(Sfx.Crunch, e.Position, ListenerPos, _session.Yaw, 1f, 0.6f);
        }
        else _fx.Dust(e.Position, 12, 2f);
    }

    private static int DestructibleBoxIndex(ArenaDef arena, int d)
    {
        int k = 0;
        for (int i = 0; i < arena.Boxes.Count && k < GameWorld.MaxDestructibles; i++)
        {
            if (!arena.Boxes[i].Solid || arena.Boxes[i].Health <= 0) continue;
            if (k == d) return i;
            k++;
        }
        return -1;
    }

    /// <summary>Destructible boxes aren't in the static arena mesh; draw the intact ones with the world shader.</summary>
    private void DrawDestructibles(ArenaDef arena) => _arena?.DrawDestructibles(_app.Shader, _latest!.DestroyedMask);
}
