using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Core;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Title screen: sticker menu on the tarp, framed key art, the LED running-text sign.</summary>
public sealed class MainMenuScreen(App app) : Screen(app)
{
    private int _tip;

    public override void Enter()
    {
        base.Enter();
        App.Marquee.SetText(S.Tip(_tip));
    }

    public override void Update(float dt)
    {
        if (Gui.Back) App.Quit();
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        float u = U;

        // Key art in a hand-painted frame, slightly crooked like a poster taped to the tarp.
        if (App.KeyArt is { } art)
        {
            float w = MathF.Min(sw - 640 * u, 900 * u), h = w * art.Height / art.Width;
            var center = new Vector2(sw - w / 2 - 70 * u, 150 * u + h / 2);
            float rot = 1.6f;
            DrawRectanglePro(new Rectangle(center.X + 14 * u, center.Y + 14 * u, w + 36 * u, h + 36 * u), new Vector2((w + 36 * u) / 2, (h + 36 * u) / 2), rot, Theme.Aspal);
            DrawRectanglePro(new Rectangle(center.X, center.Y, w + 36 * u, h + 36 * u), new Vector2((w + 36 * u) / 2, (h + 36 * u) / 2), rot, Theme.Kapur);
            DrawTexturePro(art, new Rectangle(0, 0, art.Width, art.Height), new Rectangle(center.X, center.Y, w, h), new Vector2(w / 2, h / 2), rot, Color.White);
            // Tape strips.
            DrawRectanglePro(new Rectangle(center.X - w / 2 + 40 * u, center.Y - h / 2 - 8 * u, 110 * u, 30 * u), new Vector2(55 * u, 15 * u), -18, Theme.Fade(Theme.Kuning, 0.8f));
            DrawRectanglePro(new Rectangle(center.X + w / 2 - 40 * u, center.Y - h / 2 - 2 * u, 110 * u, 30 * u), new Vector2(55 * u, 15 * u), 22, Theme.Fade(Theme.Kuning, 0.8f));
            DrawPlayingAs(new Vector2(center.X - w / 2 + 30 * u, center.Y + h / 2 + 40 * u), u);
        }

        // Title lettering.
        Theme.SignText("DJANCOEG", new Vector2(70 * u, 84 * u), 104 * u, Theme.Kuning, doubleShadow: true);
        Theme.SignText("TOURNAMENT", new Vector2(78 * u, 196 * u), 46 * u, Theme.Kapur);
        Theme.Text(Theme.BodyBold, S.Lang == Core.Localization.Language.Indonesian ? "Arena tawuran paling rusuh se-Nusantara" : GameInfo.Tagline,
            new Vector2(80 * u, 256 * u), 22 * u, Theme.Kapur);

        // Sticker menu.
        string[] keys = ["menu.campaign", "menu.practice", "menu.host", "menu.find", "menu.fighters", "menu.workshop", "menu.leaderboard", "menu.settings", "menu.credits", "menu.quit"];
        float[] tilts = [2f, -3f, 2f, -1.5f, 2.5f, -2f, 2f, 1.5f, -2.5f, 1.8f];
        float y = 300 * u;
        for (int i = 0; i < keys.Length; i++)
        {
            var r = new Rectangle(96 * u, y, 420 * u, 40 * u);
            var fill = i == 0 ? Theme.Kuning : Theme.Kapur;
            if (Gui.Sticker(r, S.T(keys[i]).ToUpperInvariant(), tilts[i], fill, Theme.Aspal, 23 * u)) Activate(i);
            y += 48 * u;
        }

        // LED running text.
        if (App.Marquee.Draw(new Rectangle(0, sh - 80 * u, sw, 34 * u), GetFrameTime(), 46 * u))
            App.Marquee.SetText(S.Tip(++_tip));
        App.DrawFooter();
    }

    private void DrawPlayingAs(Vector2 at, float u)
    {
        var cd = App.Content.Characters[Math.Max(0, App.Content.CharacterIndexOf(App.Settings.CharacterId))];
        var portrait = App.Models.Portrait(cd);
        var card = new Rectangle(at.X, at.Y, 390 * u, 118 * u);
        Gui.Panel(card, Theme.Kapur, 6 * u);
        if (portrait is { } tex)
            DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Width), new Rectangle(card.X + 10 * u, card.Y + 10 * u, 98 * u, 98 * u), Vector2.Zero, 0, Color.White);
        Theme.Text(Theme.Mono, S.T("menu.playingAs").ToUpperInvariant(), new Vector2(card.X + 124 * u, card.Y + 16 * u), 15 * u, Theme.Fade(Theme.Aspal, 0.7f));
        Theme.Text(Theme.Display, S.T(cd.Name).ToUpperInvariant(), new Vector2(card.X + 124 * u, card.Y + 38 * u), 26 * u, Theme.Aspal);
        Theme.Text(Theme.BodyBold, App.Settings.PlayerName + "  ·  " + S.T("cat." + cd.Category), new Vector2(card.X + 124 * u, card.Y + 78 * u), 18 * u, Theme.Cabe);
    }

    private void Activate(int i)
    {
        switch (i)
        {
            case 0: App.Go(new CampaignScreen(App)); break;
            case 1: App.Go(new SetupScreen(App, host: false)); break;
            case 2: App.Go(new SetupScreen(App, host: true)); break;
            case 3: App.Go(new BrowserScreen(App)); break;
            case 4: App.Go(new FightersScreen(App)); break;
            case 5: App.Go(new WorkshopScreen(App)); break;
            case 6: App.Go(new LeaderboardScreen(App)); break;
            case 7: App.Go(new SettingsScreen(App)); break;
            case 8: App.Go(new CreditsScreen(App)); break;
            case 9: App.Quit(); break;
        }
    }
}
