using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Content;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Fighter select: portrait grid, big portrait, stats and ability. Also edits the player name.</summary>
public sealed class FightersScreen(App app, Screen? returnTo = null, Action<string>? onPick = null) : Screen(app)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private int _selected = Math.Max(0, app.Content.CharacterIndexOf(app.Settings.CharacterId));
    private string _name = app.Settings.PlayerName;

    public override void Update(float dt)
    {
        returnTo?.BackgroundTick(dt);
        int n = App.Content.Characters.Count;
        if (Gui.TextFocus < 0)
        {
            if (IsKeyPressed(KeyboardKey.Right)) _selected = (_selected + 1) % n;
            if (IsKeyPressed(KeyboardKey.Left)) _selected = (_selected - 1 + n) % n;
        }
        if (Gui.Back && Gui.TextFocus < 0) Leave(false);
    }

    private void Leave(bool pick)
    {
        if (pick)
        {
            App.Settings.CharacterId = App.Content.Characters[_selected].Id;
            App.Settings.PlayerName = string.IsNullOrWhiteSpace(_name) ? "Jagoan" : _name.Trim();
            App.Settings.Save();
            onPick?.Invoke(App.Settings.CharacterId);
        }
        App.Go(returnTo ?? new MainMenuScreen(App));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        App.DrawHeader(S.T("fighters.title"));
        var chars = App.Content.Characters;

        // Grid of portrait stickers.
        int cols = 5;
        float gx = 84 * u, gy = 200 * u, cell = 128 * u, gap = 14 * u;
        for (int i = 0; i < chars.Count; i++)
        {
            int c = i % cols, r = i / cols;
            var rect = new Rectangle(gx + c * (cell + gap), gy + r * (cell * 1.2f + gap), cell, cell * 1.2f);
            if (Gui.Row(rect, i == _selected, out bool focused)) _selected = i;
            var tex = App.Models.Portrait(chars[i]);
            var inner = new Rectangle(rect.X + 6 * u, rect.Y + 6 * u, rect.Width - 12 * u, rect.Height - 34 * u);
            if (tex is { } t) DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Width * inner.Height / inner.Width), inner, Vector2.Zero, 0, Color.White);
            else DrawRectangleRec(inner, Theme.Rgba(ColorUtil.ParseRgba(chars[i].PrimaryColor)));
            string label = S.T(chars[i].Name);
            if (Theme.Measure(Theme.BodyBold, label, 15 * u).X > rect.Width - 8 * u) label = label[..Math.Min(label.Length, 14)] + ".";
            Theme.TextCentered(Theme.BodyBold, label, new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height - 15 * u), 15 * u, i == _selected ? Theme.Aspal : Theme.Kapur);
            if (focused && Gui.Clicked) _selected = i;
        }

        // Detail card.
        var cd = chars[_selected];
        float dx = gx + cols * (cell + gap) + 30 * u;
        var card = new Rectangle(dx, 190 * u, sw - dx - 60 * u, sh - 290 * u);
        Gui.Panel(card, Theme.Kapur);
        var portrait = App.Models.Portrait(cd);
        float ph = card.Height - 40 * u, pw = ph * 0.75f;
        if (pw > card.Width * 0.45f) { pw = card.Width * 0.45f; ph = pw / 0.75f; }
        var prect = new Rectangle(card.X + 20 * u, card.Y + 20 * u, pw, ph);
        if (portrait is { } ptex) DrawTexturePro(ptex, new Rectangle(0, 0, ptex.Width, ptex.Height), prect, Vector2.Zero, 0, Color.White);
        else DrawRectangleRec(prect, Theme.Rgba(ColorUtil.ParseRgba(cd.PrimaryColor)));
        DrawRectangleLinesEx(prect, 4, Theme.Aspal);

        float tx = prect.X + prect.Width + 26 * u, tw = card.X + card.Width - tx - 24 * u;
        float ty = card.Y + 24 * u;
        var cat = S.T("cat." + cd.Category).ToUpperInvariant();
        var cm = Theme.Measure(Theme.Mono, cat, 15 * u);
        DrawRectangleRec(new Rectangle(tx, ty, cm.X + 18 * u, 26 * u), Theme.Cabe);
        Theme.Text(Theme.Mono, cat, new Vector2(tx + 9 * u, ty + 4 * u), 15 * u, Theme.Kapur);
        ty += 38 * u;
        Theme.Text(Theme.Display, S.T(cd.Name).ToUpperInvariant(), new Vector2(tx, ty), 38 * u, Theme.Aspal);
        ty += 52 * u;
        ty = Theme.Paragraph(Theme.Body, S.T(cd.Description), new Vector2(tx, ty), 19 * u, tw, Theme.Aspal) + 14 * u;

        Stat(ref ty, tx, tw, u, S.T("fighters.health"), cd.MaxHealth / 150f, cd.MaxHealth.ToString(Inv));
        Stat(ref ty, tx, tw, u, S.T("fighters.speed"), cd.SpeedMultiplier / 1.3f, (cd.SpeedMultiplier * 100).ToString("0", Inv) + "%");
        Stat(ref ty, tx, tw, u, S.T("fighters.jump"), cd.JumpMultiplier / 1.4f, (cd.JumpMultiplier * 100).ToString("0", Inv) + "%");
        Stat(ref ty, tx, tw, u, S.T("fighters.size"), cd.Scale / 1.3f, (cd.Scale * 100).ToString("0", Inv) + "%");
        ty += 10 * u;
        Theme.Text(Theme.Mono, S.T("fighters.ability").ToUpperInvariant(), new Vector2(tx, ty), 15 * u, Theme.Fade(Theme.Aspal, 0.7f));
        ty += 24 * u;
        Theme.Text(Theme.Display, S.T(cd.Ability.Name).ToUpperInvariant(), new Vector2(tx, ty), 24 * u, Theme.Cabe);
        ty += 34 * u;
        Theme.Paragraph(Theme.Body, S.T(cd.Ability.Description) + $"  ({cd.Ability.Cooldown:0} s {S.T("fighters.cooldown")})", new Vector2(tx, ty), 18 * u, tw, Theme.Aspal);

        // Name + confirm.
        float by = sh - 92 * u;
        Gui.TextField(new Rectangle(84 * u, by, 640 * u, 52 * u), S.T("fighters.name"), ref _name, 20);
        if (Gui.Button(new Rectangle(sw - 400 * u, by, 330 * u, 56 * u), S.T("fighters.confirm"), primary: true, 22 * u)) Leave(true);
        if (Gui.Button(new Rectangle(sw - 600 * u, by, 180 * u, 56 * u), S.T("common.back"), false, 20 * u)) Leave(false);
    }

    private static void Stat(ref float y, float x, float w, float u, string label, float frac, string value)
    {
        Theme.Text(Theme.BodyBold, label, new Vector2(x, y), 17 * u, Theme.Aspal);
        var track = new Rectangle(x + 150 * u, y + 4 * u, w - 230 * u, 14 * u);
        DrawRectangleRec(track, Theme.Fade(Theme.Aspal, 0.15f));
        DrawRectangleRec(new Rectangle(track.X, track.Y, track.Width * Math.Clamp(frac, 0, 1), track.Height), Theme.Aspal);
        Theme.TextRight(Theme.Mono, value, new Vector2(x + w, y), 16 * u, Theme.Aspal);
        y += 30 * u;
    }
}
