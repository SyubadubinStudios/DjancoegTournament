using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Localization;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

public sealed class SettingsScreen(App app, Screen? returnTo = null) : Screen(app)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Languages = ["English", "Bahasa Indonesia"];
    private string _master = app.Settings.MasterServer;

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
        var st = App.Settings;
        App.DrawHeader(S.T("settings.title"));

        float x = 84 * u, y = 200 * u, w = 820 * u, rh = 56 * u;
        int lang = (int)st.Language;
        if (Gui.Choice(new Rectangle(x, y, w, rh), S.T("settings.language"), ref lang, Languages)) { st.Language = (Language)lang; S.Lang = st.Language; }
        y += rh;
        float sens = st.MouseSensitivity;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("settings.sensitivity"), ref sens, 0.1f, 4f, sens.ToString("0.00", Inv))) st.MouseSensitivity = sens;
        y += rh;
        bool inv = st.InvertY;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.invert"), ref inv)) st.InvertY = inv;
        y += rh;
        float fov = st.FieldOfView;
        if (Gui.Slider(new Rectangle(x, y, w, rh), S.T("settings.fov"), ref fov, 70, 120, fov.ToString("0", Inv) + "°")) st.FieldOfView = MathF.Round(fov);
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
        bool fs = st.Fullscreen;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.fullscreen"), ref fs)) { st.Fullscreen = fs; ToggleFullscreen(); }
        y += rh;
        bool fps = st.ShowFps;
        if (Gui.Toggle(new Rectangle(x, y, w, rh), S.T("settings.fps"), ref fps)) st.ShowFps = fps;
        y += rh;
        if (Gui.TextField(new Rectangle(x, y, w, rh), S.T("settings.master"), ref _master, 64)) st.MasterServer = _master;
        y += rh + 18 * u;

        // Controls card.
        float cx = x + w + 50 * u;
        var card = new Rectangle(cx, 200 * u, sw - cx - 70 * u, 330 * u);
        Gui.Panel(card, Theme.Kapur);
        Theme.Text(Theme.Display, S.T("settings.controls").ToUpperInvariant(), new Vector2(card.X + 22 * u, card.Y + 20 * u), 28 * u, Theme.Aspal);
        float ty = card.Y + 70 * u;
        foreach (var part in S.T("settings.controlsText").Split(" · "))
        {
            int sp = part.IndexOf(' ');
            string key = sp > 0 ? part[..sp] : part, desc = sp > 0 ? part[(sp + 1)..] : "";
            var km = Theme.Measure(Theme.Mono, key, 16 * u);
            DrawRectangleRec(new Rectangle(card.X + 22 * u, ty - 2 * u, km.X + 14 * u, 24 * u), Theme.Aspal);
            Theme.Text(Theme.Mono, key, new Vector2(card.X + 29 * u, ty), 16 * u, Theme.Kuning);
            Theme.Text(Theme.BodyBold, desc, new Vector2(card.X + 40 * u + km.X, ty), 17 * u, Theme.Aspal);
            ty += 23 * u;
        }

        if (Gui.Button(new Rectangle(x, sh - 130 * u, 220 * u, 56 * u), S.T("common.back"), true, 22 * u)) Close();
        App.DrawFooter();
    }
}

public sealed class CreditsScreen(App app) : Screen(app)
{
    public override void Update(float dt)
    {
        if (Gui.Back || Gui.Clicked && GetMouseY() < GetScreenHeight() - 160) App.Go(new MainMenuScreen(App));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        App.DrawHeader(S.T("credits.title"));
        var card = new Rectangle(84 * u, 200 * u, sw - 168 * u, sh - 380 * u);
        Gui.Panel(card, Theme.Kapur);
        float artW = 0;
        if (App.KeyArt is { } art)
        {
            float ah = card.Height - 48 * u;
            artW = ah * art.Width / art.Height;
            if (artW > card.Width * 0.45f) { artW = card.Width * 0.45f; ah = artW * art.Height / art.Width; }
            var dst = new Rectangle(card.X + card.Width - artW - 24 * u, card.Y + 24 * u, artW, ah);
            DrawTexturePro(art, new Rectangle(0, 0, art.Width, art.Height), dst, Vector2.Zero, 0, Color.White);
            DrawRectangleLinesEx(dst, 4, Theme.Aspal);
        }
        float x = card.X + 36 * u, y = card.Y + 34 * u, w = card.Width - 72 * u - artW - 24 * u;
        Theme.Text(Theme.Mono, "SYUBADUBIN STUDIOS", new Vector2(x, y), 18 * u, Theme.Cabe);
        y += 34 * u;
        Theme.Text(Theme.Display, "ARIANA MISCHA FADHILA", new Vector2(x, y), 48 * u, Theme.Aspal);
        y += 70 * u;
        y = Theme.Paragraph(Theme.BodyBold, S.T("credits.made"), new Vector2(x, y), 24 * u, w, Theme.Aspal) + 12 * u;
        y = Theme.Paragraph(Theme.Body, S.T("credits.tech"), new Vector2(x, y), 20 * u, w, Theme.Aspal) + 8 * u;
        y = Theme.Paragraph(Theme.Body, S.T("credits.fonts"), new Vector2(x, y), 18 * u, w, Theme.Fade(Theme.Aspal, 0.8f)) + 22 * u;
        Theme.SignText(S.T("credits.thanks"), new Vector2(x, y), 28 * u, Theme.Kuning);
        if (Gui.Button(new Rectangle(84 * u, sh - 140 * u, 220 * u, 56 * u), S.T("common.back"), true, 22 * u)) App.Go(new MainMenuScreen(App));
        App.DrawFooter();
    }
}
