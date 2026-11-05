using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Game;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Content;
using Djancoeg.Net;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Practice / host setup: arena, mode, bots and limits.</summary>
public sealed class SetupScreen(App app, bool host) : Screen(app)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private string _serverName = app.Settings.ServerName;
    private string _port = app.Settings.HostPort.ToString(CultureInfo.InvariantCulture);
    private bool _public;
    private string _password = app.Settings.HostPassword;

    public override void Update(float dt)
    {
        if (Gui.Back && Gui.TextFocus < 0) App.Go(new MainMenuScreen(App));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth();
        var st = App.Settings;
        App.DrawHeader(S.T(host ? "setup.titleHost" : "setup.titlePractice"));

        // Arena cards on the right.
        var arenas = App.Content.Arenas;
        int arenaIndex = Math.Max(0, App.Content.ArenaIndexOf(st.ArenaId));
        float cx = 880 * u, cy = 210 * u, cw = (sw - cx - 70 * u), ch = 104 * u;
        Theme.Text(Theme.Mono, S.T("setup.arena").ToUpperInvariant(), new Vector2(cx, cy - 30 * u), 16 * u, Theme.Kapur);
        for (int i = 0; i < arenas.Count; i++)
        {
            var r = new Rectangle(cx, cy + i * (ch + 14 * u), cw, ch);
            if (Gui.Row(r, i == arenaIndex, out _)) st.ArenaId = arenas[i].Id;
            var a = arenas[i];
            var thumb = App.ArenaThumbnail(a);
            float tw = (ch - 20 * u) * 16f / 9f;
            var dst = new Rectangle(r.X + 10 * u, r.Y + 10 * u, tw, ch - 20 * u);
            DrawTexturePro(thumb, new Rectangle(0, 0, thumb.Width, -thumb.Height), dst, Vector2.Zero, 0, Color.White);
            DrawRectangleLinesEx(dst, 2, Theme.Aspal);
            bool sel = i == arenaIndex;
            float lx = dst.X + dst.Width + 16 * u;
            Theme.Text(Theme.Display, S.T(a.Name).ToUpperInvariant(), new Vector2(lx, r.Y + 14 * u), 24 * u, sel ? Theme.Aspal : Theme.Kuning);
            Theme.Paragraph(Theme.Body, S.T(a.Description), new Vector2(lx, r.Y + 48 * u), 17 * u, r.X + cw - lx - 16 * u, sel ? Theme.Aspal : Theme.Kapur, 1.25f);
        }

        // Options column.
        float x = 84 * u, y = 210 * u, w = 720 * u, rh = 54 * u;
        var modes = new[] { ModeNames.Of(GameModeKind.FreeForAll), ModeNames.Of(GameModeKind.TeamDeathmatch), ModeNames.Of(GameModeKind.KingOfTheHill), ModeNames.Of(GameModeKind.CaptureTheFlag), ModeNames.Of(GameModeKind.Race) }
            .Select(m => S.Lang == Core.Localization.Language.Indonesian ? m.Id : m.En).ToArray();
        int mode = st.Mode;
        Gui.Choice(new Rectangle(x, y, w, rh), S.T("setup.mode"), ref mode, modes);
        st.Mode = mode;
        Theme.Text(Theme.Body, S.T($"setup.modeDesc{mode}"), new Vector2(x, y + rh), 17 * u, Theme.Fade(Theme.Kapur, 0.85f));
        y += rh + 36 * u;

        float bots = st.BotCount;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("setup.bots"), ref bots, 0, 15, ((int)MathF.Round(bots)).ToString(Inv))) st.BotCount = (int)MathF.Round(bots);
        y += rh;
        float skill = st.BotSkill;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("setup.skill"), ref skill, 0, 1, (skill * 100).ToString("0", Inv) + "%")) st.BotSkill = skill;
        y += rh;
        float score = st.ScoreLimit;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("setup.score"), ref score, 5, 100, ((int)score).ToString(Inv))) st.ScoreLimit = (int)score;
        y += rh;
        float time = st.TimeLimitMinutes;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("setup.time"), ref time, 0, 30, time < 0.5f ? "-" : ((int)time).ToString(Inv))) st.TimeLimitMinutes = (int)time;
        y += rh;

        bool creeps = st.Creeps;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("setup.creeps"), ref creeps)) st.Creeps = creeps;
        y += rh;
        if (!host)
        {
            bool spectate = st.SpectatePractice;
            if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("setup.spectate"), ref spectate)) st.SpectatePractice = spectate;
            y += rh;
        }
        if (host)
        {
            if (Gui.TextField(new Rectangle(x, y, w, rh), S.T("setup.serverName"), ref _serverName, 32)) st.ServerName = _serverName;
            y += rh;
            if (Gui.TextField(new Rectangle(x, y, w, rh), S.T("setup.port"), ref _port, 5) && int.TryParse(_port, Inv, out int p) && p is > 1024 and < 65536) st.HostPort = p;
            y += rh;
            Gui.Toggle(new Rectangle(x, y, w, rh), S.T("setup.public"), ref _public);
            y += rh;
            if (Gui.TextField(new Rectangle(x, y, w, rh), S.T("setup.password"), ref _password, 32)) st.HostPassword = _password;
            y += rh;
        }

        y += 20 * u;
        if (Gui.Button(new Rectangle(x, y, 300 * u, 62 * u), S.T("setup.start"), primary: true, 26 * u)) Start();
        if (Gui.Button(new Rectangle(x + 320 * u, y, 200 * u, 62 * u), S.T("common.back"), false, 22 * u)) App.Go(new MainMenuScreen(App));
        App.DrawFooter();
    }

    private void Start()
    {
        var st = App.Settings;
        st.Save();
        var match = new MatchSettings
        {
            ArenaId = st.ArenaId, Mode = (GameModeKind)st.Mode, BotCount = st.BotCount, BotSkill = st.BotSkill,
            ScoreLimit = st.ScoreLimit, TimeLimitSeconds = st.TimeLimitMinutes * 60, ServerName = host ? st.ServerName : "Latihan", Creeps = st.Creeps,
        };
        try
        {
            var session = Session.StartLocal(App.Content, st, match, host, host && _public ? st.MasterServer : null, spectator: !host && st.SpectatePractice);
            App.Go(new PlayScreen(App, session));
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            App.Go(new MessageScreen(App, ex.Message, new SetupScreen(App, host)));
        }
    }
}

/// <summary>Simple message with a back button (errors, disconnects).</summary>
public sealed class MessageScreen(App app, string message, Screen next) : Screen(app)
{
    public override void Update(float dt)
    {
        if (Gui.Back) App.Go(next);
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        var r = new Rectangle(sw / 2f - 380 * u, sh / 2f - 140 * u, 760 * u, 280 * u);
        Gui.Panel(r, Theme.Kapur);
        Theme.SignText("WADUH!", new Vector2(r.X + 30 * u, r.Y + 24 * u), 48 * u, Theme.Cabe);
        Theme.Paragraph(Theme.BodyBold, message, new Vector2(r.X + 32 * u, r.Y + 100 * u), 22 * u, r.Width - 64 * u, Theme.Aspal);
        if (Gui.Button(new Rectangle(r.X + r.Width - 230 * u, r.Y + r.Height - 80 * u, 200 * u, 56 * u), S.T("common.back"), true)) App.Go(next);
    }
}
