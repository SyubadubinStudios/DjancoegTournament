using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;
using Djancoeg.Core.Simulation;
using Djancoeg.Net;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Game;

public sealed partial class GameView
{
    private struct FeedLine { public string Text; public Color Color; public float Until; }
    private readonly List<FeedLine> _feed = [];
    private readonly List<FeedLine> _chat = [];
    private string _shout = "";
    private Color _shoutColor;
    private float _shoutTime = -10;
    private string _chatDraft = "";
    private readonly int[] _order = new int[SimConstants.MaxPlayers];

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public bool ScoreboardHeld => ForceScoreboard || Controls.Down(GameAction.Scores);

    private void AddFeed(string text, Color color)
    {
        _feed.Add(new FeedLine { Text = text, Color = color, Until = _time + 6f });
        if (_feed.Count > 6) _feed.RemoveAt(0);
    }

    private void AddChat(string from, string text, Color color)
    {
        _chat.Add(new FeedLine { Text = from.Length > 0 ? from + ": " + text : text, Color = color, Until = _time + 9f });
        if (_chat.Count > 7) _chat.RemoveAt(0);
    }

    private void Shout(string text, Color color)
    {
        _shout = text; _shoutColor = color; _shoutTime = _time;
    }

    private void UpdateHud(float dt)
    {
        _feed.RemoveAll(f => f.Until < _time);
        _chat.RemoveAll(f => f.Until < _time);
        if (Paused) return;
        if (!Chatting && Controls.Pressed(GameAction.Chat)) { Chatting = true; _chatDraft = ""; while (GetCharPressed() > 0) { } return; }
        if (Chatting)
        {
            int ch;
            while ((ch = GetCharPressed()) > 0) if (ch >= 32 && ch < 256 && _chatDraft.Length < 100) _chatDraft += (char)ch;
            if ((IsKeyPressed(KeyboardKey.Backspace) || IsKeyPressedRepeat(KeyboardKey.Backspace)) && _chatDraft.Length > 0) _chatDraft = _chatDraft[..^1];
            if (IsKeyPressed(KeyboardKey.Enter)) { _session.Client.SendChat(_chatDraft); Chatting = false; }
            if (IsKeyPressed(KeyboardKey.Escape)) Chatting = false;
        }
    }

    private float Ui => GetScreenHeight() / 900f;

    private void DrawHud()
    {
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        float u = Ui;
        var s = _latest!;
        bool haveMe = s.TryGetPlayer(_session.Client.LocalId, out var me);

        DrawNameTags();
        DrawFloatingTexts();

        // Damage vignette.
        float flashScale = _app.Settings.ReduceFlashes ? 0.4f : 1f;
        float hurt = flashScale * MathF.Max(_damageFlash, haveMe && me.Health < 35 && me.Has(NetPlayerFlags.Alive) ? 0.35f + 0.15f * MathF.Sin(_time * 6) : 0);
        if (hurt > 0.01f)
        {
            var c = Theme.Fade(Theme.Cabe, MathF.Min(0.6f, hurt));
            DrawRectangleGradientV(0, 0, sw, (int)(sh * 0.2f), c, Theme.Fade(Theme.Cabe, 0));
            DrawRectangleGradientV(0, (int)(sh * 0.8f), sw, (int)(sh * 0.2f), Theme.Fade(Theme.Cabe, 0), c);
            DrawRectangleGradientH(0, 0, (int)(sw * 0.12f), sh, c, Theme.Fade(Theme.Cabe, 0));
            DrawRectangleGradientH((int)(sw * 0.88f), 0, (int)(sw * 0.12f), sh, Theme.Fade(Theme.Cabe, 0), c);
        }

        if (haveMe && me.Has(NetPlayerFlags.Alive)) DrawCrosshair(sw / 2f, sh / 2f, u, me);
        DrawTopBar(sw, u, s, haveMe ? me : default);
        DrawFeed(sw, u);
        DrawChat(u, sh);
        if (haveMe) DrawVitals(sw, sh, u, me, s);
        else if (_session.Client.IsSpectator && s.Phase == MatchPhase.Playing) DrawSpectatorHud(sw, sh, u);
        DrawObjectiveHud(sw, sh, u);
        DrawShout(sw, sh, u);
        DrawCaption(sw, sh, u);

        if (haveMe && !me.Has(NetPlayerFlags.Alive) && s.Phase == MatchPhase.Playing)
        {
            float y = sh * 0.62f;
            if (_killerId >= 0 && TryView(_killerId, out var kp) && kp.Has(NetPlayerFlags.Alive))
                Theme.TextCentered(Theme.Mono, "KILL CAM  ·  " + NameOf(_killerId) + "  " + Math.Max(0, (int)kp.Health).ToString(Inv) + " HP", new Vector2(sw / 2f, y - 40 * u), 18 * u, Theme.Kuning);
            if (_killedBy.Length > 0) Theme.SignText(S.T("hud.killedBy").Replace("{0}", _killedBy), new Vector2(sw / 2f - Theme.Measure(Theme.Display, S.T("hud.killedBy").Replace("{0}", _killedBy), 40 * u).X / 2, y), 40 * u, Theme.Kapur);
            float since = _time - _deathTime[me.Id];
            string t = since < SimConstants.RespawnDelay ? S.T("hud.respawnIn").Replace("{0}", MathF.Ceiling(SimConstants.RespawnDelay - since).ToString(Inv)) : S.T("hud.respawn");
            Theme.TextCentered(Theme.BodyBold, t, new Vector2(sw / 2f, y + 70 * u), 26 * u, Theme.Kuning);
        }

        if (s.Phase == MatchPhase.Ended) DrawMatchEnd(sw, sh, u, s);
        else if (ScoreboardHeld && !Paused) DrawScoreboard(sw, sh, u, s);

        if (_app.Settings.ShowFps)
            Theme.Text(Theme.Mono, $"{GetFPS()} FPS  {_session.Client.RttMs:0} ms", new Vector2(12 * u, 10 * u), 16 * u, Theme.Fade(Theme.Kapur, 0.8f));
    }

    /// <summary>Crosshair shaped by the weapon: ring for spread, circle for melee, long ticks for piercing, drop marks for arcs.</summary>
    private void DrawCrosshair(float cx, float cy, float u, in NetPlayer me)
    {
        var weapons = _session.Content.Weapons;
        var w = me.Weapon < weapons.Count ? weapons[me.Weapon] : null;
        Color o = Theme.Aspal, c = Theme.Kuning;
        float t = 3 * u;
        var center = new Vector2(cx, cy);
        if (w is { Kind: WeaponKind.Melee })
        {
            DrawRing(center, 9 * u - 1, 9 * u + t + 1, 0, 360, 24, o);
            DrawRing(center, 9 * u, 9 * u + t, 0, 360, 24, c);
        }
        else if (w is { Pellets: > 1 })
        {
            // Ring sized to the real pellet cone at the current field of view.
            float halfFov = _camera.FovY * MathF.PI / 360f;
            float radius = MathF.Max(12 * u, MathF.Tan(w.SpreadDegrees * MathF.PI / 180f) / MathF.Tan(halfFov) * GetScreenHeight() / 2f);
            DrawRing(center, radius - 1, radius + t + 1, 0, 360, 40, o);
            DrawRing(center, radius, radius + t, 0, 360, 40, c);
            DrawCircleV(center, 2.5f * u, c);
        }
        else if (w is { Piercing: true })
        {
            float g = 10 * u, l = 26 * u;
            foreach (int dx in (ReadOnlySpan<int>)[1, -1])
            {
                DrawLineEx(new Vector2(cx + dx * g, cy), new Vector2(cx + dx * (g + l), cy), t + 3, o);
                DrawLineEx(new Vector2(cx + dx * g, cy), new Vector2(cx + dx * (g + l), cy), t, c);
            }
            DrawCircleV(center, 2.2f * u, Theme.Cabe);
        }
        else
        {
            float g = 6 * u, l = 10 * u;
            foreach (var (dx, dy) in (ReadOnlySpan<(int, int)>)[(1, 0), (-1, 0), (0, 1), (0, -1)])
            {
                var a = new Vector2(cx + dx * g, cy + dy * g);
                var b = new Vector2(cx + dx * (g + l), cy + dy * (g + l));
                DrawLineEx(a, b, t + 3, o);
                DrawLineEx(a, b, t, c);
            }
            DrawCircleV(center, 2.2f * u, c);
            if (w is { Kind: WeaponKind.Projectile, ProjectileGravity: > 4 })
                for (int k = 1; k <= 3; k++) DrawRectangleRec(new Rectangle(cx - (8 - k * 1.5f) * u, cy + (14 + k * 9) * u, (16 - k * 3) * u, 2.5f * u), Theme.Fade(c, 0.9f - k * 0.2f));
            if (w is { SplashRadius: > 0 }) DrawRing(center, 18 * u, 19.5f * u, 0, 360, 32, Theme.Fade(c, 0.5f));
        }
        if (_hitMarker > 0)
        {
            float k = 10 * u + (1 - _hitMarker) * 6 * u;
            var hc = Theme.Fade(Theme.Kapur, _hitMarker);
            DrawLineEx(new Vector2(cx - k, cy - k), new Vector2(cx - k * 0.4f, cy - k * 0.4f), 3 * u, hc);
            DrawLineEx(new Vector2(cx + k, cy - k), new Vector2(cx + k * 0.4f, cy - k * 0.4f), 3 * u, hc);
            DrawLineEx(new Vector2(cx - k, cy + k), new Vector2(cx - k * 0.4f, cy + k * 0.4f), 3 * u, hc);
            DrawLineEx(new Vector2(cx + k, cy + k), new Vector2(cx + k * 0.4f, cy + k * 0.4f), 3 * u, hc);
        }
    }

    private void DrawVitals(int sw, int sh, float u, in NetPlayer me, Snapshot s)
    {
        float y = sh - 108 * u;
        var hpColor = me.Health <= 35 ? Theme.Cabe : me.Health > 100 ? Theme.Kuning : Theme.Kapur;
        Gui.Plate(new Rectangle(24 * u, y, 210 * u, 84 * u), S.T("hud.health"), Math.Max(0, (int)me.Health).ToString(Inv), hpColor, 50 * u);
        Gui.Plate(new Rectangle(246 * u, y, 190 * u, 84 * u), S.T("hud.armor"), me.Armor.ToString(Inv), me.Armor > 0 ? Theme.Kuning : Theme.Fade(Theme.Kapur, 0.4f), 50 * u);

        var weapons = _session.Content.Weapons;
        if (me.Weapon < weapons.Count)
        {
            var w = weapons[me.Weapon];
            string ammo = w.InfiniteAmmo ? "~" : s.Ammo[me.Weapon].ToString(Inv);
            var r = new Rectangle(sw - 234 * u, y, 210 * u, 84 * u);
            Gui.Plate(r, S.T("hud.ammo"), ammo, !w.InfiniteAmmo && s.Ammo[me.Weapon] <= 3 ? Theme.Cabe : Theme.Kapur, 50 * u);
            string name = S.T(w.Name).ToUpperInvariant();
            var m = Theme.Measure(Theme.Display, name, 22 * u);
            var tag = new Rectangle(sw - 24 * u - m.X - 24 * u, y - 46 * u, m.X + 24 * u, 36 * u);
            Raylib.DrawRectangleRec(new Rectangle(tag.X + 4, tag.Y + 4, tag.Width, tag.Height), Theme.Aspal);
            Raylib.DrawRectangleRec(tag, Theme.Rgba(ColorUtil.ParseRgba(w.Color)));
            Theme.Text(Theme.Display, name, new Vector2(tag.X + 12 * u, tag.Y + 7 * u), 22 * u, Theme.Aspal);

            // Weapon slots strip.
            float sx = sw - 24 * u;
            for (int k = weapons.Count - 1; k >= 0; k--)
            {
                bool owned = (s.OwnedWeapons & (1 << k)) != 0;
                var box = new Rectangle(sx - 30 * u, y - 84 * u, 26 * u, 28 * u);
                sx -= 32 * u;
                DrawRectangleRec(box, owned ? (k == me.Weapon ? Theme.Kuning : Theme.Kapur) : Theme.Fade(Theme.Aspal, 0.5f));
                if (k == me.Weapon) DrawRectangleLinesEx(box, 2, Theme.Cabe);
                Theme.TextCentered(Theme.Mono, weapons[k].Slot.ToString(Inv), new Vector2(box.X + box.Width / 2, box.Y + box.Height / 2), 16 * u, owned ? Theme.Aspal : Theme.Fade(Theme.Kapur, 0.5f));
            }
        }

        // Ability ring (bottom centre).
        var cd = _session.Content.Characters[Math.Min(me.Character, _session.Content.Characters.Count - 1)];
        var center = new Vector2(sw / 2f, sh - 64 * u);
        float radius = 34 * u;
        float frac = cd.Ability.Cooldown <= 0 ? 1 : 1f - Math.Clamp(me.AbilityCooldown / cd.Ability.Cooldown, 0, 1);
        DrawCircleV(center + new Vector2(4, 4), radius + 6 * u, Theme.Aspal);
        DrawCircleV(center, radius + 4 * u, Theme.Aspal);
        DrawRing(center, radius - 6 * u, radius, -90, -90 + 360 * frac, 40, frac >= 1 ? Theme.Kuning : Theme.Kapur);
        Theme.TextCentered(Theme.Display, "Q", center, 30 * u, frac >= 1 ? Theme.Kuning : Theme.Fade(Theme.Kapur, 0.6f));
        string label = frac >= 1 ? S.T(cd.Ability.Name) + " · " + S.T("hud.ready") : S.T(cd.Ability.Name) + " · " + me.AbilityCooldown.ToString("0", Inv);
        Theme.TextCentered(Theme.BodyBold, label, center + new Vector2(0, -radius - 22 * u), 18 * u, frac >= 1 ? Theme.Kuning : Theme.Kapur);

        if (me.QuadTimer > 0)
            Theme.TextCentered(Theme.Display, S.T("hud.quad").ToUpperInvariant() + "  " + me.QuadTimer.ToString("0", Inv), new Vector2(sw / 2f, sh - 160 * u), 26 * u, Theme.Lerp(Theme.Cabe, Theme.Kuning, 0.5f + 0.5f * MathF.Sin(_time * 8)));
    }

    private void DrawTopBar(int sw, float u, Snapshot s, in NetPlayer me)
    {
        var client = _session.Client;
        int secs = (int)s.TimeLeft;
        string clock = client.TimeLimit > 0 ? $"{secs / 60:00}:{secs % 60:00}" : "--:--";
        var r = new Rectangle(sw / 2f - 90 * u, 14 * u, 180 * u, 64 * u);
        Gui.Plate(r, ModeShort(client.Mode), clock, secs <= 30 && client.TimeLimit > 0 ? Theme.Cabe : Theme.Kapur, 38 * u);

        string info;
        if (client.Mode == GameModeKind.TeamDeathmatch)
        {
            DrawTeamTag(new Vector2(r.X - 12 * u, r.Y + 8 * u), S.T("hud.team0") + "  " + s.TeamScore0.ToString(Inv), TeamColor(0), true, u);
            DrawTeamTag(new Vector2(r.X + r.Width + 12 * u, r.Y + 8 * u), s.TeamScore1.ToString(Inv) + "  " + S.T("hud.team1"), TeamColor(1), false, u);
            info = (S.Lang == Core.Localization.Language.Indonesian ? "Timmu: " : "Your team: ") + S.T(me.Team == 0 ? "hud.team0" : "hud.team1") + " · " + client.ScoreLimit.ToString(Inv);
        }
        else
        {
            int leader = -1, best = int.MinValue;
            for (int i = 0; i < s.PlayerCount; i++) if (s.Players[i].Score > best) { best = s.Players[i].Score; leader = s.Players[i].Id; }
            string you = S.Lang == Core.Localization.Language.Indonesian ? "Kamu" : "You";
            string lead = S.Lang == Core.Localization.Language.Indonesian ? "Teratas" : "Leader";
            info = $"{you} {me.Score}  ·  {lead} {NameOf(leader)} {best}  ·  / {client.ScoreLimit}";
            if (client.Mode == GameModeKind.KingOfTheHill)
            {
                string z = s.ZoneHolder == client.LocalId ? S.T("hud.zoneYou") : s.ZoneHolder >= 0 ? S.T("hud.zone") + ": " + NameOf(s.ZoneHolder) : S.T("hud.zoneContested");
                Theme.TextCentered(Theme.Display, z, new Vector2(sw / 2f, r.Y + r.Height + 52 * u), 22 * u, s.ZoneHolder == client.LocalId ? Theme.Kuning : Theme.Kapur);
            }
        }
        var m = Theme.Measure(Theme.BodyBold, info, 18 * u);
        var bar = new Rectangle(sw / 2f - m.X / 2 - 12 * u, r.Y + r.Height + 8 * u, m.X + 24 * u, 28 * u);
        DrawRectangleRec(bar, Theme.Fade(Theme.Aspal, 0.75f));
        Theme.TextCentered(Theme.BodyBold, info, new Vector2(sw / 2f, bar.Y + bar.Height / 2), 18 * u, Theme.Kapur);
    }

    private static void DrawTeamTag(Vector2 anchor, string text, Color color, bool rightAligned, float u)
    {
        var m = Theme.Measure(Theme.Display, text, 22 * u);
        var r = new Rectangle(rightAligned ? anchor.X - m.X - 24 * u : anchor.X, anchor.Y, m.X + 24 * u, 44 * u);
        DrawRectangleRec(new Rectangle(r.X + 4, r.Y + 4, r.Width, r.Height), Theme.Aspal);
        DrawRectangleRec(r, color);
        Theme.Text(Theme.Display, text, new Vector2(r.X + 12 * u, r.Y + 10 * u), 22 * u, Theme.Kapur);
    }

    private static string ModeShort(GameModeKind m)
    {
        var (en, id) = ModeNames.Of(m);
        return (S.Lang == Core.Localization.Language.Indonesian ? id : en).ToUpperInvariant();
    }

    private void DrawFeed(int sw, float u)
    {
        float y = 16 * u;
        foreach (var f in _feed)
        {
            var m = Theme.Measure(Theme.BodyBold, f.Text, 18 * u);
            var r = new Rectangle(sw - m.X - 40 * u, y, m.X + 24 * u, 30 * u);
            DrawRectangleRec(new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), Theme.Fade(Theme.Aspal, 0.7f));
            DrawRectangleRec(r, Theme.Fade(Theme.Aspal, 0.82f));
            DrawRectangleRec(new Rectangle(r.X, r.Y, 5 * u, r.Height), f.Color);
            Theme.Text(Theme.BodyBold, f.Text, new Vector2(r.X + 14 * u, r.Y + 5 * u), 18 * u, f.Color);
            y += 36 * u;
        }
    }

    private void DrawChat(float u, int sh)
    {
        float y = sh - 160 * u - _chat.Count * 26 * u;
        foreach (var c in _chat)
        {
            Theme.Text(Theme.BodyBold, c.Text, new Vector2(26 * u, y + 1), 18 * u, Theme.Aspal);
            Theme.Text(Theme.BodyBold, c.Text, new Vector2(24 * u, y), 18 * u, c.Color);
            y += 26 * u;
        }
        if (Chatting)
        {
            var r = new Rectangle(24 * u, sh - 150 * u, 560 * u, 34 * u);
            DrawRectangleRec(r, Theme.Kapur);
            DrawRectangleLinesEx(r, 3, Theme.Cabe);
            Theme.Text(Theme.BodyBold, S.T("hud.chat") + " " + _chatDraft + (((int)(_time * 2) & 1) == 0 ? "_" : ""), new Vector2(r.X + 10 * u, r.Y + 6 * u), 20 * u, Theme.Aspal);
        }
    }

    private void DrawShout(int sw, int sh, float u)
    {
        float age = _time - _shoutTime;
        if (age > 1.6f || _shout.Length == 0) return;
        float pop = age < 0.12f ? 0.6f + age / 0.12f * 0.55f : age < 0.25f ? 1.15f - (age - 0.12f) / 0.13f * 0.15f : 1f;
        float alpha = age > 1.2f ? 1 - (age - 1.2f) / 0.4f : 1;
        float size = 76 * u * pop;
        var m = Theme.Measure(Theme.Display, _shout, size);
        var pos = new Vector2(sw / 2f - m.X / 2, sh * 0.26f - m.Y / 2);
        float rot = -4f;
        float o = size * 0.06f;
        Raylib.DrawTextPro(Theme.Display, _shout, pos + new Vector2(o * 2, o * 2), Vector2.Zero, rot, size, size * 0.02f, Theme.Fade(_shoutColor.R == Theme.Cabe.R && _shoutColor.G == Theme.Cabe.G ? Theme.Kuning : Theme.Cabe, alpha));
        Raylib.DrawTextPro(Theme.Display, _shout, pos + new Vector2(o, o), Vector2.Zero, rot, size, size * 0.02f, Theme.Fade(Theme.Aspal, alpha));
        Raylib.DrawTextPro(Theme.Display, _shout, pos, Vector2.Zero, rot, size, size * 0.02f, Theme.Fade(_shoutColor, alpha));
    }

    private void DrawNameTags()
    {
        var col = _session.Client.Collision;
        int me = _session.Client.LocalId;
        var fwd = Vector3.Normalize(_camera.Target - _camera.Position);
        bool team = _session.Client.Mode == GameModeKind.TeamDeathmatch;
        _latest!.TryGetPlayer(me, out var self);
        float u = Ui;
        for (int i = 0; i < _viewCount; i++)
        {
            var p = _view[i];
            if (p.Id == me || !p.Has(NetPlayerFlags.Alive) || p.Has(NetPlayerFlags.Invisible)) continue;
            var cd = _session.Content.Characters[Math.Min(p.Character, _session.Content.Characters.Count - 1)];
            var head = p.Position + new Vector3(0, SimConstants.PlayerHeight * cd.Scale + 0.35f, 0);
            var to = head - _camera.Position;
            float dist = to.Length();
            if (dist > 40 || Vector3.Dot(to, fwd) <= 0) continue;
            if (col is not null && !col.LineOfSight(_camera.Position, head)) continue;
            var sp = GetWorldToScreen(head, _camera);
            float size = Math.Clamp(22 - dist * 0.25f, 13, 22) * u;
            var color = !team ? Theme.Kapur : _session.Client.IsSpectator ? TeamColor(p.Team) : p.Team == self.Team ? FriendColor : EnemyColor;
            string name = NameOf(p.Id);
            var m = Theme.Measure(Theme.BodyBold, name, size);
            DrawRectangleRec(new Rectangle(sp.X - m.X / 2 - 6, sp.Y - m.Y / 2 - 2, m.X + 12, m.Y + 4), Theme.Fade(Theme.Aspal, 0.6f));
            Theme.TextCentered(Theme.BodyBold, name, sp, size, color);
            if (_tauntUntil[p.Id] > _time && _tauntText[p.Id].Length > 0) SpeechBubble(sp - new Vector2(0, 34 * u), _tauntText[p.Id], u);
        }
        if (_session.ThirdPerson && self.Has(NetPlayerFlags.Alive) && _tauntUntil[me] > _time)
            SpeechBubble(GetWorldToScreen(self.Position + new Vector3(0, 2.3f, 0), _camera), _tauntText[me], u);
    }

    private static void SpeechBubble(Vector2 at, string text, float u)
    {
        var m = Theme.Measure(Theme.BodyBold, text, 18 * u);
        var r = new Rectangle(at.X - m.X / 2 - 12 * u, at.Y - m.Y - 14 * u, m.X + 24 * u, m.Y + 12 * u);
        DrawRectangleRec(new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), Theme.Aspal);
        DrawRectangleRec(r, Theme.Kapur);
        DrawRectangleLinesEx(r, 2, Theme.Aspal);
        DrawTriangle(new Vector2(at.X - 8 * u, r.Y + r.Height), new Vector2(at.X, r.Y + r.Height + 10 * u), new Vector2(at.X + 8 * u, r.Y + r.Height), Theme.Kapur);
        Theme.Text(Theme.BodyBold, text, new Vector2(r.X + 12 * u, r.Y + 6 * u), 18 * u, Theme.Aspal);
    }

    private void DrawFloatingTexts()
    {
        var fwd = Vector3.Normalize(_camera.Target - _camera.Position);
        foreach (ref readonly var t in _fx.Texts)
        {
            if (t.Life <= 0 || t.Text is null) continue;
            if (Vector3.Dot(t.Pos - _camera.Position, fwd) <= 0) continue;
            var sp = GetWorldToScreen(t.Pos, _camera);
            float a = MathF.Min(1, t.Life * 2);
            Theme.TextCentered(Theme.Display, t.Text, sp + new Vector2(2, 2), t.Size * Ui, Theme.Fade(Theme.Aspal, a));
            Theme.TextCentered(Theme.Display, t.Text, sp, t.Size * Ui, Theme.Fade(t.Color, a));
        }
    }

    private int SortedPlayers(Snapshot s)
    {
        int n = s.PlayerCount;
        for (int i = 0; i < n; i++) _order[i] = i;
        var players = s.Players;
        Array.Sort(_order, 0, n, Comparer<int>.Create((a, b) =>
        {
            int c = players[b].Score.CompareTo(players[a].Score);
            return c != 0 ? c : players[a].Deaths.CompareTo(players[b].Deaths);
        }));
        return n;
    }

    private void DrawScoreboard(int sw, int sh, float u, Snapshot s)
    {
        int n = SortedPlayers(s);
        float w = 760 * u, rowH = 34 * u;
        var r = new Rectangle(sw / 2f - w / 2, sh * 0.16f, w, 90 * u + n * rowH);
        Gui.Panel(r, Theme.Kapur);
        Theme.SignText(_session.Client.ServerName.Length > 0 ? _session.Client.ServerName.ToUpperInvariant() : GameInfo.Name.ToUpperInvariant(), new Vector2(r.X + 24 * u, r.Y + 14 * u), 30 * u, Theme.Kuning);
        float y = r.Y + 58 * u;
        float cScore = r.X + w - 260 * u, cK = r.X + w - 180 * u, cD = r.X + w - 120 * u, cP = r.X + w - 30 * u;
        Theme.Text(Theme.Mono, S.T("score.player"), new Vector2(r.X + 24 * u, y), 15 * u, Theme.Aspal);
        Theme.TextRight(Theme.Mono, S.T("score.score"), new Vector2(cScore, y), 15 * u, Theme.Aspal);
        Theme.TextRight(Theme.Mono, S.T("score.kills"), new Vector2(cK, y), 15 * u, Theme.Aspal);
        Theme.TextRight(Theme.Mono, S.T("score.deaths"), new Vector2(cD, y), 15 * u, Theme.Aspal);
        Theme.TextRight(Theme.Mono, S.T("score.ping"), new Vector2(cP, y), 15 * u, Theme.Aspal);
        y += 24 * u;
        bool team = _session.Client.Mode == GameModeKind.TeamDeathmatch;
        for (int k = 0; k < n; k++)
        {
            var p = s.Players[_order[k]];
            bool isMe = p.Id == _session.Client.LocalId;
            var row = new Rectangle(r.X + 12 * u, y, w - 24 * u, rowH - 4 * u);
            DrawRectangleRec(row, isMe ? Theme.Kuning : k % 2 == 0 ? Theme.Fade(Theme.Aspal, 0.06f) : Theme.Fade(Theme.Aspal, 0.0f));
            if (team) DrawRectangleRec(new Rectangle(row.X, row.Y, 6 * u, row.Height), TeamColor(p.Team));
            var cd = _session.Content.Characters[Math.Min(p.Character, _session.Content.Characters.Count - 1)];
            string name = NameOf(p.Id) + "  ·  " + S.T(cd.Name);
            Theme.Text(Theme.BodyBold, name, new Vector2(row.X + 14 * u, row.Y + 5 * u), 19 * u, Theme.Aspal);
            Theme.TextRight(Theme.Display, p.Score.ToString(Inv), new Vector2(cScore, row.Y + 3 * u), 22 * u, Theme.Aspal);
            Theme.TextRight(Theme.Mono, p.Kills.ToString(Inv), new Vector2(cK, row.Y + 6 * u), 18 * u, Theme.Aspal);
            Theme.TextRight(Theme.Mono, p.Deaths.ToString(Inv), new Vector2(cD, row.Y + 6 * u), 18 * u, Theme.Aspal);
            Theme.TextRight(Theme.Mono, p.Has(NetPlayerFlags.Bot) ? S.T("score.bot") : (p.PingQuarterMs * 4).ToString(Inv), new Vector2(cP, row.Y + 6 * u), 18 * u, Theme.Aspal);
            y += rowH;
        }
    }

    private void DrawMatchEnd(int sw, int sh, float u, Snapshot s)
    {
        DrawRectangle(0, 0, sw, sh, Theme.Fade(Theme.TarpDeep, 0.55f));
        string winner = _session.Client.Mode == GameModeKind.TeamDeathmatch
            ? S.T(s.Winner == 0 ? "hud.team0" : "hud.team1")
            : s.Winner >= 0 ? NameOf(s.Winner) : "-";
        string title = S.T("hud.winner").Replace("{0}", winner).ToUpperInvariant();
        var m = Theme.Measure(Theme.Display, title, 56 * u);
        Theme.SignText(title, new Vector2(sw / 2f - m.X / 2, sh * 0.06f), 56 * u, Theme.Kuning, doubleShadow: true);
        DrawScoreboard(sw, sh, u, s);
        Theme.TextCentered(Theme.BodyBold, S.T("hud.nextMatch").Replace("{0}", s.PhaseTimer.ToString("0", Inv)), new Vector2(sw / 2f, sh - 60 * u), 24 * u, Theme.Kapur);
    }
}
