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

/// <summary>v1.1 additions: spectator cameras, surface footsteps, blob shadows, subtitles.</summary>
public sealed partial class GameView
{
    // ------------------------------------------------------------------ spectator

    private void UpdateSpectator(float dt, bool inputEnabled)
    {
        if (_followId >= 0 && !TryView(_followId, out _)) _followId = -1;
        if (_followId < 0 && _viewCount > 0) _followId = _view[0].Id;
        if (!inputEnabled) return;
        if (!_freeFly)
        {
            if (Controls.Pressed(GameAction.Fire) || Controls.Pressed(GameAction.NextWeapon)) CycleFollow(1);
            if (Controls.Pressed(GameAction.Ability) || Controls.Pressed(GameAction.PrevWeapon)) CycleFollow(-1);
            return;
        }
        // Free-fly: noclip camera driven by the same movement bindings.
        var fwd = VectorMath.Forward(_session.Yaw, _session.Pitch);
        var right = VectorMath.FlatRight(_session.Yaw);
        var move = Vector3.Zero;
        if (Controls.Down(GameAction.Forward)) move += fwd;
        if (Controls.Down(GameAction.Back)) move -= fwd;
        if (Controls.Down(GameAction.Right)) move += right;
        if (Controls.Down(GameAction.Left)) move -= right;
        if (Controls.Down(GameAction.Jump)) move += Vector3.UnitY;
        if (Controls.Down(GameAction.Crouch)) move -= Vector3.UnitY;
        var stick = Controls.PadMove();
        move += fwd * stick.Y + right * stick.X;
        float speed = IsKeyDown(KeyboardKey.LeftShift) ? 26f : 12f;
        if (move.LengthSquared() > 1e-4f) _freePos += Vector3.Normalize(move) * speed * dt;
    }

    private void CycleFollow(int dir)
    {
        if (_viewCount == 0) return;
        int idx = 0;
        for (int i = 0; i < _viewCount; i++) if (_view[i].Id == _followId) idx = i;
        idx = ((idx + dir) % _viewCount + _viewCount) % _viewCount;
        _followId = _view[idx].Id;
        _app.Sounds.Play(Sfx.UiMove, 0.4f);
    }

    private void SpectatorCamera()
    {
        if (_freeFly || _followId < 0 || !TryView(_followId, out var p))
        {
            _camera.Position = _freePos;
            _camera.Target = _freePos + VectorMath.Forward(_session.Yaw, _session.Pitch);
            return;
        }
        var cd = _session.Content.Characters[Math.Min(p.Character, _session.Content.Characters.Count - 1)];
        var pivot = p.Position + new Vector3(0, SimConstants.EyeHeight * cd.Scale + 0.3f, 0);
        var fwd = VectorMath.Forward(p.Yaw, p.Pitch * 0.5f);
        var desired = pivot - fwd * 3.6f + new Vector3(0, 0.6f, 0);
        var pos = ClipCamera(pivot, desired);
        _camera.Position = Vector3.Lerp(_camera.Position == Vector3.Zero ? pos : _camera.Position, pos, MathF.Min(1, GetFrameTime() * 10f));
        _camera.Target = pivot + fwd * 8f;
        _freePos = _camera.Position;
    }

    private void DrawSpectatorHud(int sw, int sh, float u)
    {
        string title = S.Lang == Core.Localization.Language.Indonesian ? "MENONTON" : "SPECTATING";
        string who = _freeFly ? (S.Lang == Core.Localization.Language.Indonesian ? "Kamera bebas" : "Free camera") : NameOf(_followId);
        var r = new Rectangle(sw / 2f - 220 * u, sh - 120 * u, 440 * u, 70 * u);
        Gui.Plate(r, title, who.ToUpperInvariant(), Theme.Kuning, 32 * u);
        string hint = _freeFly
            ? $"{Controls.Name(Controls.Binding(GameAction.Forward))}/{Controls.Name(Controls.Binding(GameAction.Left))}/{Controls.Name(Controls.Binding(GameAction.Back))}/{Controls.Name(Controls.Binding(GameAction.Right))} · Shift · {Controls.Name(Controls.Binding(GameAction.Camera))}: " + (S.Lang == Core.Localization.Language.Indonesian ? "ikuti pemain" : "follow a player")
            : $"{Controls.Name(Controls.Binding(GameAction.Fire))}: " + (S.Lang == Core.Localization.Language.Indonesian ? "pemain berikutnya" : "next player") + $" · {Controls.Name(Controls.Binding(GameAction.Camera))}: " + (S.Lang == Core.Localization.Language.Indonesian ? "kamera bebas" : "free camera");
        Theme.TextCentered(Theme.BodyBold, hint, new Vector2(sw / 2f, sh - 30 * u), 17 * u, Theme.Kapur);
        if (!_freeFly && TryView(_followId, out var p))
        {
            var wr = new Rectangle(24 * u, sh - 108 * u, 210 * u, 84 * u);
            Gui.Plate(wr, S.T("hud.health"), Math.Max(0, (int)p.Health).ToString(System.Globalization.CultureInfo.InvariantCulture), p.Health <= 35 ? Theme.Cabe : Theme.Kapur, 50 * u);
        }
    }

    // ------------------------------------------------------------------ v1.2 objectives

    private void WindGust(Vector3 from, Vector3 to)
    {
        var dir = to - from;
        float len = dir.Length();
        if (len < 0.01f) return;
        dir /= len;
        var rng = new FastRandom((ulong)(_time * 1000));
        for (int i = 0; i < 26; i++)
        {
            var jitter = rng.InsideUnitSphere() * 0.35f;
            _fx.Emit(from + dir * 0.6f, (dir + jitter) * rng.Range(9f, 15f), new Color(245, 240, 225, 160), 0.45f, 0.06f, 0f, 3f);
        }
    }

    private string TeamName(int team) => S.T(team == 0 ? "hud.team0" : "hud.team1");

    private void OnFlagEvent(in GameEvent e, int me)
    {
        int team = e.Value;
        string key = e.Type switch
        {
            GameEventType.FlagTaken => "hud.flagTaken",
            GameEventType.FlagDropped => "hud.flagDropped",
            GameEventType.FlagReturned => "hud.flagReturned",
            _ => "hud.flagCaptured",
        };
        string line = S.T(key).Replace("{0}", NameOf(e.A)).Replace("{1}", TeamName(team));
        AddFeed(line, TeamColor(team));
        var sfx = e.Type switch { GameEventType.FlagCaptured => Sfx.Gong, GameEventType.FlagTaken => Sfx.Whistle, GameEventType.FlagReturned => Sfx.Drum, _ => Sfx.PropHit };
        _app.Sounds.Play(sfx, 0.7f);
        if (e.Type == GameEventType.FlagCaptured)
        {
            _fx.Burst(e.Position + new Vector3(0, 1.5f, 0), 80, 9f, null, 1.5f, 0.08f);
            Shout(line.ToUpperInvariant().Length > 26 ? (S.Lang == Core.Localization.Language.Indonesian ? "GORENGAN MASUK!" : "SNACK SCORED!") : line.ToUpperInvariant(), TeamColor(1 - team));
        }
        if (e.Type == GameEventType.FlagTaken && e.A == me) Shout(S.T("hud.youCarry"), Theme.Kuning);
    }

    private void DrawObjectives(ArenaDef arena)
    {
        var s = _latest!;
        var mode = _session.Client.Mode;
        if (mode == GameModeKind.CaptureTheFlag)
        {
            var world = new GameWorldProbe(arena, _session.Client.Collision);
            for (int t = 0; t < 2; t++)
            {
                Primitives.FlagBase(world.Home(t), TeamColor(t));
                var f = s.Flags[t];
                var pos = f.Position;
                if (f.State == 1 && TryView(f.Carrier, out var carrier))
                {
                    var cd = _session.Content.Characters[Math.Min(carrier.Character, _session.Content.Characters.Count - 1)];
                    pos = carrier.Position + new Vector3(0, SimConstants.PlayerHeight * cd.Scale - 0.6f, 0);
                    if (carrier.Id == _session.Client.LocalId && !_session.ThirdPerson) continue;
                }
                Primitives.Gorengan(pos, _time + t, TeamColor(t), f.State == 1);
            }
        }
        else if (mode == GameModeKind.Race)
        {
            var goal = arena.RaceGoal is { Length: 3 } g ? new Vector3(g[0], g[1], g[2]) : arena.ZoneCenterV;
            Primitives.RaceGoal(goal, MathF.Max(1.2f, arena.ZoneRadius), _time);
        }
    }

    private void DrawObjectiveHud(int sw, int sh, float u)
    {
        var s = _latest!;
        var mode = _session.Client.Mode;
        if (mode == GameModeKind.CaptureTheFlag)
        {
            for (int t = 0; t < 2; t++)
            {
                var f = s.Flags[t];
                string state = f.State switch { 0 => S.T("hud.flagHome"), 1 => S.T("hud.flagCarried") + ": " + NameOf(f.Carrier), _ => S.T("hud.flagDroppedShort") };
                var r = new Rectangle(sw / 2f + (t == 0 ? -320 * u : 20 * u), 150 * u, 300 * u, 34 * u);
                DrawRectangleRec(r, Theme.Fade(Theme.Aspal, 0.8f));
                DrawRectangleRec(new Rectangle(r.X, r.Y, 8 * u, r.Height), TeamColor(t));
                Theme.Text(Theme.BodyBold, "GORENGAN " + TeamName(t).ToUpperInvariant() + " · " + state, new Vector2(r.X + 16 * u, r.Y + 7 * u), 16 * u, f.State == 0 ? Theme.Kapur : Theme.Kuning);
            }
            if (s.Flags[0].Carrier == _session.Client.LocalId || s.Flags[1].Carrier == _session.Client.LocalId)
                Theme.TextCentered(Theme.Display, S.T("hud.youCarry"), new Vector2(sw / 2f, sh * 0.72f), 26 * u, Theme.Lerp(Theme.Kuning, Theme.Cabe, 0.5f + 0.5f * MathF.Sin(_time * 6)));
        }
        else if (mode == GameModeKind.Race && s.TryGetPlayer(_session.Client.LocalId, out var me) && me.Has(Net.NetPlayerFlags.Alive))
        {
            var arena = _session.Client.Arena!;
            var goal = arena.RaceGoal is { Length: 3 } g ? new Vector3(g[0], g[1], g[2]) : arena.ZoneCenterV;
            float d = Vector3.Distance(goal, me.Position);
            Theme.TextCentered(Theme.BodyBold, S.T("hud.raceHint").Replace("{0}", d.ToString("0", System.Globalization.CultureInfo.InvariantCulture)), new Vector2(sw / 2f, 150 * u), 20 * u, Theme.Kuning);
            // Edge arrow pointing at the goal when it is off-screen.
            var sp = GetWorldToScreen(goal + new Vector3(0, 1.5f, 0), _camera);
            var fwd = Vector3.Normalize(_camera.Target - _camera.Position);
            bool behind = Vector3.Dot(goal - _camera.Position, fwd) < 0;
            if (behind) sp = new Vector2(sw - sp.X, sh - sp.Y);
            var c = new Vector2(Math.Clamp(sp.X, 40 * u, sw - 40 * u), Math.Clamp(sp.Y, 60 * u, sh - 140 * u));
            DrawCircleV(c, 16 * u, Theme.Aspal);
            DrawCircleV(c, 12 * u, Theme.Kuning);
            Theme.TextCentered(Theme.Display, "!", c, 18 * u, Theme.Aspal);
        }
    }

    /// <summary>Client-side mirror of <see cref="GameWorld.FlagHome"/> so bases render where the server keeps them.</summary>
    private readonly struct GameWorldProbe(ArenaDef arena, CollisionWorld? col)
    {
        public Vector3 Home(int team)
        {
            if (arena.FlagBases.Count >= 2 && arena.FlagBases[team].Length == 3)
                return new Vector3(arena.FlagBases[team][0], arena.FlagBases[team][1], arena.FlagBases[team][2]);
            Vector3 sum = Vector3.Zero; int n = 0;
            foreach (var s in arena.Spawns) if (s.Team == team) { sum += s.PositionV; n++; }
            if (n == 0) return arena.ZoneCenterV + new Vector3(team == 0 ? -10 : 10, 0, 0);
            var c = sum / n;
            if (col is not null && col.Raycast(c + new Vector3(0, 3, 0), -Vector3.UnitY, 10, out float d, out _)) c.Y = c.Y + 3 - d;
            return c;
        }
    }

    // ------------------------------------------------------------------ footsteps

    private void UpdateFootsteps(float dt)
    {
        var col = _session.Client.Collision;
        if (col is null || _latest is null) return;
        int me = _session.Client.LocalId;

        // Local player (predicted): one step every ~2.1 m on the ground. Crouching is silent.
        if (!_session.Client.IsSpectator && _session.Client.TryGetPredicted(0, out var ms, out var visual) && _latest.TryGetPlayer(me, out var self) && self.Has(NetPlayerFlags.Alive))
        {
            float speed = VectorMath.Horizontal(ms.Velocity).Length();
            if (ms.OnGround && !ms.Crouched && speed > 1.5f)
            {
                _stepDist += speed * dt;
                if (_stepDist > 2.1f)
                {
                    _stepDist = 0;
                    if (col.SurfaceBelow(visual, 0.4f, out var surface)) _app.Sounds.Play(StepSound(surface), 0.28f, 0.92f + _time % 0.16f);
                }
            }
            else _stepDist = 1.2f; // first step comes quickly after starting to move
        }

        // Others nearby.
        for (int i = 0; i < _viewCount; i++)
        {
            var p = _view[i];
            if (p.Id == me || !p.Has(NetPlayerFlags.Alive) || !p.Has(NetPlayerFlags.OnGround) || p.Has(NetPlayerFlags.Crouched)) continue;
            float speed = VectorMath.Horizontal(p.Velocity).Length();
            if (speed < 1.5f || Vector3.DistanceSquared(p.Position, _camera.Position) > 25f * 25f) continue;
            _remoteStep[p.Id] += speed * dt;
            if (_remoteStep[p.Id] < 2.3f) continue;
            _remoteStep[p.Id] = 0;
            if (col.SurfaceBelow(p.Position, 0.4f, out var surface))
                _app.Sounds.PlayAt(StepSound(surface), p.Position, _camera.Position, _session.Yaw, 0.55f, 0.9f + p.Id * 0.02f);
        }
    }

    private static Sfx StepSound(SurfaceKind s) => s switch
    {
        SurfaceKind.Wood => Sfx.StepWood,
        SurfaceKind.Metal or SurfaceKind.Roof => Sfx.StepMetal,
        SurfaceKind.Grass => Sfx.StepGrass,
        SurfaceKind.Tile or SurfaceKind.Wall => Sfx.StepTile,
        SurfaceKind.Water => Sfx.StepWater,
        SurfaceKind.Fabric => Sfx.StepFabric,
        _ => Sfx.StepGround,
    };

    // ------------------------------------------------------------------ blob shadows

    private void DrawBlobShadows()
    {
        var col = _session.Client.Collision;
        if (col is null) return;
        int me = _session.Client.LocalId;
        for (int i = 0; i < _viewCount; i++)
        {
            var p = _view[i];
            if (!p.Has(NetPlayerFlags.Alive) || p.Has(NetPlayerFlags.Invisible)) continue;
            var pos = p.Position;
            if (p.Id == me && _session.Client.TryGetPredicted(0, out _, out var visual)) pos = visual;
            var cd = _session.Content.Characters[Math.Min(p.Character, _session.Content.Characters.Count - 1)];
            Blob(col, pos + new Vector3(0, 0.3f, 0), 0.45f * cd.Scale);
        }
        for (int i = 0; i < _propPos.Length; i++)
        {
            var half = _session.Client.Arena!.Props[i].HalfExtentsV;
            Blob(col, _propPos[i] - new Vector3(0, half.Y - 0.2f, 0), MathF.Max(half.X, half.Z) * 1.15f);
        }
    }

    private static void Blob(CollisionWorld col, Vector3 from, float radius)
    {
        if (!col.Raycast(from, -Vector3.UnitY, 6f, out float d, out _)) return;
        float k = 1f - d / 6f;
        var ground = from - new Vector3(0, d - 0.02f, 0);
        DrawCylinder(ground, radius * (0.6f + 0.4f * k), radius * (0.6f + 0.4f * k), 0.01f, 12, new Color((byte)0, (byte)0, (byte)0, (byte)(90 * k)));
    }

    // ------------------------------------------------------------------ voice + subtitles

    private static readonly Dictionary<string, (string En, string Id)> Captions = new()
    {
        ["tawuran-dimulai"] = ("Let the brawl begin! Go!", "Tawuran dimulai! Gas!"),
        ["mantap"] = ("Awesome!", "Mantap jiwa!"),
        ["ngamuk"] = ("Rampage! Rampage!", "Ngamuk! Ngamuk!"),
        ["sakti"] = ("Unstoppable...!", "Sakti... mandraguna!"),
    };

    private void Voice(string id)
    {
        if (!_app.Sounds.PlayVoice(id) || !_app.Settings.Subtitles || !Captions.TryGetValue(id, out var c)) return;
        _caption = S.Lang == Core.Localization.Language.Indonesian ? c.Id : c.Id + "  (" + c.En + ")";
        _captionUntil = _time + 2.6f;
    }

    private void DrawCaption(int sw, int sh, float u)
    {
        if (_time > _captionUntil || _caption.Length == 0) return;
        string text = (S.Lang == Core.Localization.Language.Indonesian ? "[Penyiar] " : "[Announcer] ") + _caption;
        var m = Theme.Measure(Theme.BodyBold, text, 22 * u);
        var r = new Rectangle(sw / 2f - m.X / 2 - 16 * u, sh - 220 * u, m.X + 32 * u, 40 * u);
        DrawRectangleRec(r, Theme.Fade(Theme.Aspal, 0.8f));
        Theme.TextCentered(Theme.BodyBold, text, new Vector2(sw / 2f, r.Y + r.Height / 2), 22 * u, Theme.Kapur);
    }

    /// <summary>Team colours; the colour-blind palette (Okabe-Ito orange/sky blue) avoids red/green.</summary>
    private Color TeamColor(int team) => _app.Settings.ColorBlindTeams
        ? (team == 0 ? Theme.Hex(0xE69F00) : Theme.Hex(0x56B4E9))
        : (team == 0 ? Theme.Cabe : Theme.TarpLight);

    private Color FriendColor => _app.Settings.ColorBlindTeams ? Theme.Hex(0x56B4E9) : Theme.Daun;
    private Color EnemyColor => _app.Settings.ColorBlindTeams ? Theme.Hex(0xE69F00) : Theme.Cabe;
}
