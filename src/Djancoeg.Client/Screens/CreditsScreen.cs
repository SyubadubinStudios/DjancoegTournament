using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Localization;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

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
