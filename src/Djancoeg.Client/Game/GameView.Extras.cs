using System.Numerics;
using Djancoeg.Client.Audio;
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
