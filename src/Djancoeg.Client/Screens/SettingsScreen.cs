using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Localization;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Settings in three tabs: General, Graphics and Accessibility. Controls open their own screen.</summary>
public sealed class SettingsScreen(App app, Screen? returnTo = null, int tab = 0) : Screen(app)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Languages = ["English", "Bahasa Indonesia"];
    private string _master = app.Settings.MasterServer;
    private string _workshop = app.Settings.WorkshopUrl;
    private string _workshopToken = app.Settings.WorkshopToken;
    private int _tab = tab;

    public override void Update(float dt)
    {
        returnTo?.BackgroundTick(dt);
        if (Gui.Back && Gui.TextFocus < 0) Close();
    }

    private void Close()
    {
        App.Settings.Save();
        App.Go(returnTo ?? new MainMenuScreen(App));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        App.DrawHeader(S.T("settings.title"));

        // Tabs.
        string[] tabs = [S.T("settings.tabGeneral"), S.T("settings.tabGraphics"), S.T("settings.tabAccess"), S.T("settings.tabOnline"), S.T("settings.tabControls")];
        float tx = 84 * u;
        for (int i = 0; i < tabs.Length; i++)
        {
            float w = Theme.Measure(Theme.BodyBold, tabs[i], 20 * u).X + 40 * u;
            if (Gui.Button(new Rectangle(tx, 186 * u, w, 46 * u), tabs[i], i == _tab, 20 * u))
            {
                if (i == 4) { App.Settings.Save(); App.Go(new ControlsScreen(App, returnTo)); return; }
                _tab = i;
            }
            tx += w + 14 * u;
        }

        float x = 84 * u, y = 262 * u, rw = 820 * u, rh = 54 * u;
        switch (_tab)
        {
            case 0: General(ref y, x, rw, rh); break;
            case 1: Graphics(ref y, x, rw, rh, u); break;
            case 3: Online(ref y, x, rw, rh); break;
            default: Accessibility(ref y, x, rw, rh); break;
        }

        // Controls summary card.
        float cx = x + rw + 50 * u;
        var card = new Rectangle(cx, 262 * u, sw - cx - 70 * u, 300 * u);
        Gui.Panel(card, Theme.Kapur);
        Theme.Text(Theme.Display, S.T("settings.controls").ToUpperInvariant(), new Vector2(card.X + 22 * u, card.Y + 20 * u), 26 * u, Theme.Aspal);
        float ty = card.Y + 64 * u;
        foreach (var a in (ReadOnlySpan<GameAction>)[GameAction.Fire, GameAction.Jump, GameAction.Crouch, GameAction.Ability, GameAction.Camera, GameAction.Scores, GameAction.Chat, GameAction.Taunt])
        {
            string key = Controls.Name(Controls.Binding(a));
            var km = Theme.Measure(Theme.Mono, key, 15 * u);
            DrawRectangleRec(new Rectangle(card.X + 22 * u, ty - 2 * u, km.X + 14 * u, 22 * u), Theme.Aspal);
            Theme.Text(Theme.Mono, key, new Vector2(card.X + 29 * u, ty), 15 * u, Theme.Kuning);
            Theme.Text(Theme.BodyBold, S.T("action." + a), new Vector2(card.X + 44 * u + km.X, ty), 16 * u, Theme.Aspal);
            ty += 26 * u;
        }
        if (Controls.PadAvailable)
            Theme.Text(Theme.BodyBold, S.T("settings.padConnected"), new Vector2(card.X + 22 * u, card.Y + card.Height - 34 * u), 16 * u, Theme.Daun);

        if (Gui.Button(new Rectangle(x, sh - 110 * u, 220 * u, 56 * u), S.T("common.back"), true, 22 * u)) Close();
        App.DrawFooter();
    }

    private void General(ref float y, float x, float w, float rh)
    {
        var st = App.Settings;
        int lang = (int)st.Language;
        if (Gui.Choice(new Rectangle(x, y, w, rh), S.T("settings.language"), ref lang, Languages)) { st.Language = (Language)lang; S.Lang = st.Language; }
        y += rh;
        float sens = st.MouseSensitivity;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("settings.sensitivity"), ref sens, 0.1f, 4f, sens.ToString("0.00", Inv))) st.MouseSensitivity = sens;
        y += rh;
        float pad = st.GamepadLookSpeed;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("settings.padLook"), ref pad, 0.3f, 3f, pad.ToString("0.00", Inv))) st.GamepadLookSpeed = pad;
        y += rh;
        bool inv = st.InvertY;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.invert"), ref inv)) st.InvertY = inv;
        y += rh;
        float vol = st.MasterVolume;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("settings.volume"), ref vol, 0, 1, (vol * 100).ToString("0", Inv) + "%")) { st.MasterVolume = vol; App.ApplyVolume(); }
        y += rh;
        float sfx = st.SfxVolume;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("settings.sfx"), ref sfx, 0, 1, (sfx * 100).ToString("0", Inv) + "%")) { st.SfxVolume = sfx; App.ApplyVolume(); App.Sounds.Play(Audio.Sfx.Pickup, 0.6f); }
        y += rh;
        bool tp = st.ThirdPerson;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.thirdPerson"), ref tp)) st.ThirdPerson = tp;
        y += rh;
        bool creeps = st.Creeps;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.creeps"), ref creeps)) st.Creeps = creeps;
        y += rh;
        bool voice = st.StoryVoice;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.storyVoice"), ref voice)) st.StoryVoice = voice;
        y += rh;
    }

    /// <summary>Master server, Workshop address and upload token.</summary>
    private void Online(ref float y, float x, float w, float rh)
    {
        var st = App.Settings;
        if (Gui.TextField(new Rectangle(x, y, w, rh), S.T("settings.master"), ref _master, 64)) st.MasterServer = _master;
        y += rh;
        if (Gui.TextField(new Rectangle(x, y, w, rh), S.T("workshop.url"), ref _workshop, 96)) st.WorkshopUrl = _workshop;
        y += rh;
        if (Gui.TextField(new Rectangle(x, y, w, rh), S.T("workshop.token"), ref _workshopToken, 96)) st.WorkshopToken = _workshopToken.Trim();
        y += rh + 10 * U;
        Theme.Paragraph(Theme.Body, S.T("settings.onlineHint"), new System.Numerics.Vector2(x, y), 17 * U, w, Theme.Kapur);
    }

    private void Graphics(ref float y, float x, float w, float rh, float u)
    {
        var st = App.Settings;
        string[] quality = [S.T("settings.qLow"), S.T("settings.qMedium"), S.T("settings.qHigh")];
        int q = st.Quality;
        if (Gui.Choice(new Rectangle(x, y, w, rh), S.T("settings.quality"), ref q, quality)) st.Quality = q;
        y += rh;
        float scale = st.RenderScale;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("settings.renderScale"), ref scale, 0.5f, 1f, (scale * 100).ToString("0", Inv) + "%")) st.RenderScale = MathF.Round(scale * 20) / 20;
        y += rh;
        float fov = st.FieldOfView;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("settings.fov"), ref fov, 70, 120, fov.ToString("0", Inv) + "°")) st.FieldOfView = MathF.Round(fov);
        y += rh;
        bool shadows = st.BlobShadows;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.shadows"), ref shadows)) st.BlobShadows = shadows;
        y += rh;
        bool msaa = st.Msaa;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.msaa"), ref msaa)) st.Msaa = msaa;
        y += rh;
        bool fs = st.Fullscreen;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.fullscreen"), ref fs)) { st.Fullscreen = fs; ToggleFullscreen(); }
        y += rh;
        bool fps = st.ShowFps;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.fps"), ref fps)) st.ShowFps = fps;
        y += rh + 8 * u;
        Theme.Text(Theme.Body, S.T("settings.restartNote"), new Vector2(x, y), 16 * u, Theme.Fade(Theme.Kapur, 0.8f));
    }

    private void Accessibility(ref float y, float x, float w, float rh)
    {
        var st = App.Settings;
        bool cb = st.ColorBlindTeams;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.colorBlind"), ref cb)) st.ColorBlindTeams = cb;
        y += rh;
        bool subs = st.Subtitles;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.subtitles"), ref subs)) st.Subtitles = subs;
        y += rh;
        bool shake = st.ReduceShake;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.reduceShake"), ref shake)) st.ReduceShake = shake;
        y += rh;
        bool flashes = st.ReduceFlashes;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.reduceFlashes"), ref flashes)) st.ReduceFlashes = flashes;
        y += rh;
        bool blood = st.Blood;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.blood"), ref blood)) st.Blood = blood;
        y += rh;

        // Preview swatches for the team palette currently selected.
        float u = U;
        var t0 = st.ColorBlindTeams ? Theme.Hex(0xE69F00) : Theme.Cabe;
        var t1 = st.ColorBlindTeams ? Theme.Hex(0x56B4E9) : Theme.TarpLight;
        y += 16 * u;
        DrawRectangleRec(new Rectangle(x, y, 220 * u, 40 * u), t0);
        Theme.TextCentered(Theme.BodyBold, S.T("hud.team0"), new Vector2(x + 110 * u, y + 20 * u), 18 * u, Theme.Kapur);
        DrawRectangleRec(new Rectangle(x + 236 * u, y, 220 * u, 40 * u), t1);
        Theme.TextCentered(Theme.BodyBold, S.T("hud.team1"), new Vector2(x + 346 * u, y + 20 * u), 18 * u, Theme.Kapur);
    }
}

/// <summary>Rebind every action: click a row (or press Enter), then press the new key or mouse button.</summary>
public sealed class ControlsScreen(App app, Screen? returnTo = null) : Screen(app)
{
    private int _waiting = -1;
    private float _waitStarted;

    public override void Update(float dt)
    {
        returnTo?.BackgroundTick(dt);
        if (_waiting >= 0)
        {
            if (IsKeyPressed(KeyboardKey.Escape)) { _waiting = -1; return; }
            // Ignore the click that started the capture.
            if (App.Time - _waitStarted < 0.15f) { _ = GetKeyPressed(); return; }
            int code = Controls.CaptureNext();
            if (code != 0) { Controls.Bind(Controls.All[_waiting], code); _waiting = -1; App.Sounds.Play(Audio.Sfx.UiClick, 0.5f); }
            return;
        }
        if (Gui.Back) Close();
    }

    private void Close()
    {
        App.Settings.Save();
        App.Go(new SettingsScreen(App, returnTo));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        App.DrawHeader(S.T("settings.tabControls"), S.T("controls.hint"));

        var actions = Controls.All;
        int half = (actions.Length + 1) / 2;
        float colW = (sw - 200 * u) / 2, rh = 50 * u;
        for (int i = 0; i < actions.Length; i++)
        {
            float x = 84 * u + (i >= half ? colW + 30 * u : 0), y = 214 * u + (i % half) * (rh + 6 * u);
            var row = new Rectangle(x, y, colW, rh);
            bool waiting = _waiting == i;
            if (Gui.Row(row, waiting, out bool focused) && _waiting < 0) { _waiting = i; _waitStarted = App.Time; }
            var col = waiting ? Theme.Aspal : focused ? Theme.Kuning : Theme.Kapur;
            Theme.Text(Theme.BodyBold, S.T("action." + actions[i]), new Vector2(row.X + 18 * u, row.Y + 13 * u), 22 * u, col);
            string key = waiting ? S.T("controls.press") : Controls.Name(Controls.Binding(actions[i]));
            var km = Theme.Measure(Theme.Mono, key, 18 * u);
            var plate = new Rectangle(row.X + row.Width - km.X - 40 * u, row.Y + 8 * u, km.X + 24 * u, rh - 16 * u);
            DrawRectangleRec(plate, waiting ? Theme.Cabe : Theme.Aspal);
            Theme.Text(Theme.Mono, key, new Vector2(plate.X + 12 * u, plate.Y + 7 * u), 18 * u, waiting ? Theme.Kapur : Theme.Kuning);
        }

        float by = sh - 200 * u;
        var pad = new Rectangle(84 * u, by - 10 * u, sw - 168 * u, 70 * u);
        DrawRectangleRec(pad, Theme.Fade(Theme.Aspal, 0.45f));
        Theme.Paragraph(Theme.BodyBold, S.T("controls.gamepad"), new Vector2(pad.X + 18 * u, pad.Y + 10 * u), 17 * u, pad.Width - 36 * u, Theme.Kapur);
        if (Gui.Button(new Rectangle(84 * u, sh - 110 * u, 220 * u, 56 * u), S.T("common.back"), true, 22 * u)) Close();
        if (Gui.Button(new Rectangle(324 * u, sh - 110 * u, 300 * u, 56 * u), S.T("controls.reset"), false, 20 * u)) Controls.ResetDefaults();
        App.DrawFooter();
    }
}
