using System.Numerics;
using Djancoeg.Client.Audio;
using Djancoeg.Client.Render;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;
using Djancoeg.Core.Simulation;
using Djancoeg.Net;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Game;

/// <summary>Arena Creeps on the client: smoothed rendering, animations, health bars, sounds and gore.</summary>
public sealed partial class GameView
{
    private struct CreepView
    {
        public bool Seen;
        public byte Kind;
        public CreepState State;
        public Vector3 Pos;
        public float Yaw;
        public float HealthPct;
        public float SpawnTime, StateTime, Phase;
        public CreepState LastState;
        public bool Present;
    }

    private readonly CreepView[] _creeps = new CreepView[GameWorld.MaxCreeps];

    private void UpdateCreepViews(float dt)
    {
        var s = _latest!;
        for (int i = 0; i < _creeps.Length; i++) _creeps[i].Present = false;
        for (int k = 0; k < s.CreepCount; k++)
        {
            var n = s.Creeps[k];
            ref var v = ref _creeps[n.Index];
            if (!v.Seen || v.Kind != n.Kind) { v = new CreepView { Seen = true, Kind = n.Kind, Pos = n.Position, Yaw = n.Yaw, SpawnTime = _time, StateTime = _time }; }
            if (n.State != v.LastState) { v.LastState = n.State; v.StateTime = _time; }
            v.State = n.State;
            v.Present = true;
            if (Vector3.DistanceSquared(v.Pos, n.Position) > 64f) v.Pos = n.Position;
            v.Pos = Vector3.Lerp(v.Pos, n.Position, MathF.Min(1, dt * 12f));
            v.Yaw += VectorMath.WrapAngle(n.Yaw - v.Yaw) * MathF.Min(1, dt * 10f);
            v.HealthPct = n.HealthPct / 255f;
            v.Phase += dt;
        }
        for (int i = 0; i < _creeps.Length; i++) if (!_creeps[i].Present) _creeps[i].Seen = false;
    }

    private CreepDef? CreepDef(int kind) => (uint)kind < (uint)_session.Content.Creeps.Count ? _session.Content.Creeps[kind] : null;

    private static Color Col(string hex) => Theme.Rgba(ColorUtil.ParseRgba(hex));

    private static Sfx CreepSound(CreepShape s) => s switch
    {
        CreepShape.Zombie => Sfx.CreepMoan,
        CreepShape.FloatingHead => Sfx.CreepWail,
        CreepShape.LongLegs => Sfx.CreepClick,
        CreepShape.Alien => Sfx.CreepChirp,
        CreepShape.Gorilla => Sfx.CreepChest,
        CreepShape.Worm => Sfx.CreepRumble,
        _ => Sfx.CreepRoar,
    };

    private void HandleCreepEvent(in GameEvent e, int me)
    {
        var def = CreepDef(e.Value);
        var sounds = _app.Sounds;
        switch (e.Type)
        {
            case GameEventType.CreepSpawn when def is not null:
            {
                sounds.Play(Sfx.Siren, 0.6f);
                string voice = def.Shape switch { CreepShape.Zombie => "creep-zombie", CreepShape.Giant => "creep-buto", CreepShape.Alien => "creep-alien", _ => "awas-makhluk" };
                Voice(voice);
                string name = S.T(def.Name).ToUpperInvariant();
                Shout((S.Lang == Core.Localization.Language.Indonesian ? "AWAS! " : "LOOK OUT! ") + name, Theme.Cabe);
                AddFeed((S.Lang == Core.Localization.Language.Indonesian ? "Muncul: " : "Appeared: ") + S.T(def.Name), Theme.Cabe);
                if (def.Shape == CreepShape.Worm) _fx.Dust(e.Position, 20, 4f);
                else { _fx.Eruption(e.Position, 1.2f * def.Scale); for (int k = 0; k < 20; k++) _fx.Smoke(e.Position + new Vector3(0, k * 0.15f, 0), 2f); }
                break;
            }
            case GameEventType.CreepAttack when def is not null:
                sounds.PlayAt(def.Shape == CreepShape.Worm ? Sfx.Explosion : CreepSound(def.Shape), e.Position, ListenerPos, _session.Yaw, 1f, def.Shape == CreepShape.Worm ? 0.6f : 0.9f);
                if (def.Shape == CreepShape.Worm) _fx.Eruption(e.Position, def.AttackRange);
                else if (def.Scale > 1.8f) { _fx.Dust(e.Position, 18, 4f); _fx.AddRing(e.Position, def.AttackRange * 1.4f, new Color(200, 180, 140, 180), 0.4f); _fx.AddShake(0.25f); }
                break;
            case GameEventType.CreepHurt:
            {
                var d2 = (uint)e.A < (uint)_creeps.Length ? CreepDef(_creeps[e.A].Kind) : null;
                if (d2 is null) break;
                _fx.BloodHit(e.Position, Vector3.Normalize(e.Position - ListenerPos), Col(d2.BloodColor), MathF.Min(1.5f, e.Value / 25f));
                if (e.B == me) { _hitMarker = 1; _fx.AddText(e.Position + new Vector3(0, 0.6f * d2.Scale, 0), e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), Theme.Kuning); }
                break;
            }
            case GameEventType.CreepKilled when def is not null:
            {
                CorpseForCreep(e);
                bool big = def.Scale >= 2f || def.Shape == CreepShape.Worm;
                _fx.Gore(e.Position, Col(def.BloodColor), big);
                sounds.PlayAt(Sfx.Splat, e.Position, ListenerPos, _session.Yaw, 1f, big ? 0.7f : 1f);
                sounds.PlayAt(CreepSound(def.Shape), e.Position, ListenerPos, _session.Yaw, 0.9f, 0.6f);
                string killer = NameOf(e.A);
                AddFeed((S.Lang == Core.Localization.Language.Indonesian ? "{0} membasmi {1}!" : "{0} took down {1}!").Replace("{0}", killer).Replace("{1}", S.T(def.Name)), e.A == me ? Theme.Kuning : Theme.Daun);
                if (e.A == me) { Shout(S.Lang == Core.Localization.Language.Indonesian ? "MONSTER TUMBANG!" : "MONSTER DOWN!", Theme.Kuning); _app.Sounds.Play(Sfx.Kill, 0.9f, 0.8f); }
                break;
            }
            case GameEventType.CreepRoar when def is not null:
                sounds.PlayAt(CreepSound(def.Shape), e.Position, ListenerPos, _session.Yaw, 0.8f, 0.9f + (e.A % 4) * 0.06f);
                break;
            case GameEventType.CreepLeave:
                for (int k = 0; k < 24; k++) _fx.Smoke(e.Position + new Vector3(0, k * 0.15f, 0), 2.5f);
                break;
            case GameEventType.WormRumble:
                _fx.Dust(e.Position, 4, 2f);
                if (Vector3.Distance(e.Position, ListenerPos) < 20f) { _fx.AddShake(0.12f); sounds.PlayAt(Sfx.CreepRumble, e.Position, ListenerPos, _session.Yaw, 0.6f); }
                break;
        }
    }

    private unsafe void DrawCreeps()
    {
        for (int i = 0; i < _creeps.Length; i++)
        {
            ref var v = ref _creeps[i];
            if (!v.Seen) continue;
            var def = CreepDef(v.Kind);
            if (def is null) continue;
            if (def.Shape == CreepShape.Worm) { DrawWorm(ref v, def); continue; }

            var pos = v.Pos;
            float scale = def.Scale;
            // Rise out of the ground while emerging.
            if (v.State == CreepState.Emerging) pos.Y -= MathF.Max(0, 1f - (_time - v.StateTime) / 1.0f) * 1.8f * scale;
            if (def.Shape == CreepShape.FloatingHead) pos.Y -= 1.45f * scale * 0.8f;

            var cm = _app.Models.Creep(def);
            float yawDeg = v.Yaw * 180f / MathF.PI;
            if (cm is not null && cm.HasAnimations)
            {
                int clip; float frame;
                switch (v.State)
                {
                    case CreepState.Dying: clip = cm.Die; frame = MathF.Min(cm.FrameCount(clip) - 1, (_time - v.StateTime) * 30f); break;
                    case CreepState.Attacking: clip = cm.Shoot; frame = (_time - v.StateTime) * 20f; break;
                    case CreepState.Chasing: clip = cm.Run; frame = v.Phase * 30f * MathF.Max(0.5f, def.Speed); break;
                    default: clip = cm.Idle; frame = v.Phase * 24f; break;
                }
                if (clip < 0) clip = 0;
                int fc = Math.Max(1, cm.FrameCount(clip));
                int f = v.State == CreepState.Dying ? Math.Clamp((int)frame, 0, fc - 1) : ((int)frame % fc + fc) % fc;
                UpdateModelAnimation(cm.Model, cm.Animations[clip], f);
                var tint = v.State == CreepState.Attacking ? new Color(255, 200, 200, 255) : Color.White;
                DrawModelEx(cm.Model, pos, Vector3.UnitY, yawDeg, new Vector3(scale), tint);
            }
            else DrawCapsule(pos + new Vector3(0, 0.4f * scale, 0), pos + new Vector3(0, 1.4f * scale, 0), 0.4f * scale, 6, 6, Col(def.Color));
        }
    }

    /// <summary>The giant earthworm: a chain of segments arching out of the ground (only when surfaced).</summary>
    private void DrawWorm(ref CreepView v, CreepDef def)
    {
        float s = def.Scale;
        var body = Col(def.Color);
        var ring = new Color((byte)(body.R * 0.75f), (byte)(body.G * 0.7f), (byte)(body.B * 0.7f), (byte)255);
        if (v.State == CreepState.Burrowed)
        {
            // Moving mound of earth.
            DrawSphereEx(v.Pos + new Vector3(0, -0.2f, 0), 0.6f * s * (0.9f + 0.1f * MathF.Sin(_time * 12)), 6, 8, new Color(110, 84, 60, 255));
            return;
        }
        float t = Math.Clamp((_time - v.StateTime) / 0.6f, 0, 1);
        float sink = v.State == CreepState.Dying ? Math.Clamp((_time - v.StateTime) / 1.2f, 0, 1) : 0;
        float height = 2.6f * s * t * (1 - sink);
        int segs = 12;
        var fwd = VectorMath.FlatForward(v.Yaw);
        Vector3 prev = v.Pos;
        for (int k = 0; k <= segs; k++)
        {
            float u = k / (float)segs;
            var p = v.Pos + new Vector3(0, MathF.Sin(u * MathF.PI * 0.85f) * height, 0) + fwd * (u * 1.6f * s) + new Vector3(MathF.Sin(_time * 3 + u * 6) * 0.15f * s, 0, 0);
            float r = 0.55f * s * (1f - u * 0.25f);
            DrawSphereEx(p, r, 6, 10, k % 2 == 0 ? body : ring);
            prev = p;
        }
        // Mouth: dark disc with teeth at the head end.
        DrawSphereEx(prev + fwd * 0.3f * s, 0.32f * s, 6, 8, new Color(60, 20, 20, 255));
        for (int k = 0; k < 6; k++)
        {
            float a = k * MathF.Tau / 6 + _time;
            DrawCubeV(prev + fwd * 0.45f * s + new Vector3(MathF.Cos(a), MathF.Sin(a) * 0.5f + 0.2f, 0) * 0.3f * s, new Vector3(0.08f * s), new Color(250, 245, 230, 255));
        }
    }

    private void DrawCreepHealthBars(float u)
    {
        var fwd = Vector3.Normalize(_camera.Target - _camera.Position);
        for (int i = 0; i < _creeps.Length; i++)
        {
            ref var v = ref _creeps[i];
            if (!v.Seen || v.State is CreepState.Dying or CreepState.Burrowed || v.HealthPct >= 0.999f) continue;
            var def = CreepDef(v.Kind);
            if (def is null) continue;
            var top = v.Pos + new Vector3(0, (def.Shape == CreepShape.FloatingHead ? 0.6f : 2.0f) * def.Scale + 0.3f, 0);
            if (Vector3.Dot(top - _camera.Position, fwd) <= 0 || Vector3.Distance(top, _camera.Position) > 45f) continue;
            var sp = GetWorldToScreen(top, _camera);
            float w = 90 * u;
            DrawRectangleRec(new Rectangle(sp.X - w / 2 - 2, sp.Y - 2, w + 4, 12 * u + 4), Theme.Aspal);
            DrawRectangleRec(new Rectangle(sp.X - w / 2, sp.Y, w * v.HealthPct, 12 * u), Theme.Cabe);
            Theme.TextCentered(Theme.BodyBold, S.T(def.Name), sp - new Vector2(0, 14 * u), 14 * u, Theme.Kapur);
        }
    }
}
