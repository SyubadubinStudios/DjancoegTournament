using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Modding;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Local Workshop: list, enable/disable and install mods (.djmod packages from mods/_inbox).</summary>
public sealed class WorkshopScreen(App app) : Screen(app)
{
    private string _status = "";
    private int _selected;

    private string Inbox => Path.Combine(App.ModsDir, "_inbox");

    public override void Update(float dt)
    {
        if (Gui.Back) App.Go(new MainMenuScreen(App));
    }

    private void InstallInbox()
    {
        Directory.CreateDirectory(Inbox);
        int n = 0;
        var errors = new List<string>();
        foreach (var pkg in Directory.EnumerateFiles(Inbox, "*" + ModLoader.PackageExtension))
        {
            try
            {
                var info = ModLoader.Install(pkg, App.ModsDir);
                if (!info.IsValid) errors.Add($"{info.Id}: {string.Join("; ", info.Errors)}");
                File.Delete(pkg);
                n++;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                errors.Add($"{Path.GetFileName(pkg)}: {ex.Message}");
            }
        }
        App.ReloadContent();
        _status = string.Format(CultureInfo.InvariantCulture, S.T("workshop.installed"), n) + (errors.Count > 0 ? "  " + string.Join("  ", errors) : "");
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        App.DrawHeader(S.T("workshop.title"), S.T("workshop.desc"));

        float x = 84 * u, y = 214 * u;
        var mods = App.Mods;
        if (mods.Count == 0)
            Theme.Paragraph(Theme.BodyBold, S.T("workshop.none"), new Vector2(x, y + 10 * u), 22 * u, sw * 0.5f, Theme.Kapur);
        for (int i = 0; i < mods.Count; i++)
        {
            var mod = mods[i];
            var row = new Rectangle(x, y, sw * 0.45f, 82 * u);
            if (Gui.Row(row, i == _selected, out _)) _selected = i;
            var col = i == _selected ? Theme.Aspal : Theme.Kapur;
            var name = mod.Manifest is { } m ? S.T(m.Name) : mod.Id;
            Theme.Text(Theme.Display, name.ToUpperInvariant(), new Vector2(row.X + 18 * u, row.Y + 12 * u), 24 * u, col);
            string state = !mod.IsValid ? S.T("workshop.invalid") : mod.Enabled ? S.T("workshop.enabled") : S.T("workshop.disabled");
            var stateColor = !mod.IsValid ? Theme.Cabe : mod.Enabled ? Theme.Daun : Theme.Fade(col, 0.6f);
            Theme.TextRight(Theme.BodyBold, state, new Vector2(row.X + row.Width - 18 * u, row.Y + 14 * u), 18 * u, stateColor);
            Theme.Text(Theme.Mono, $"{mod.Id}  v{mod.Manifest?.Version}  ·  {mod.Manifest?.Author}", new Vector2(row.X + 18 * u, row.Y + 50 * u), 15 * u, Theme.Fade(col, 0.8f));
            y += 94 * u;
        }

        // Detail panel.
        if (mods.Count > 0)
        {
            _selected = Math.Clamp(_selected, 0, mods.Count - 1);
            var mod = mods[_selected];
            var card = new Rectangle(sw * 0.45f + x + 30 * u, 214 * u, sw - (sw * 0.45f + x + 30 * u) - 70 * u, sh - 420 * u);
            Gui.Panel(card, Theme.Kapur);
            float ty = card.Y + 22 * u, tx = card.X + 24 * u, tw = card.Width - 48 * u;
            if (mod.Manifest is { } m)
            {
                Theme.Text(Theme.Display, S.T(m.Name).ToUpperInvariant(), new Vector2(tx, ty), 30 * u, Theme.Aspal);
                ty += 48 * u;
                ty = Theme.Paragraph(Theme.Body, S.T(m.Description), new Vector2(tx, ty), 19 * u, tw, Theme.Aspal) + 10 * u;
                Theme.Text(Theme.BodyBold, string.Format(CultureInfo.InvariantCulture, S.T("workshop.contents"), m.Weapons.Count, m.Characters.Count, m.Arenas.Count), new Vector2(tx, ty), 19 * u, Theme.Cabe);
                ty += 34 * u;
                foreach (var c in m.Characters) { Theme.Text(Theme.BodyBold, "+ " + S.T(c.Name), new Vector2(tx, ty), 18 * u, Theme.Aspal); ty += 26 * u; }
                foreach (var w in m.Weapons) { Theme.Text(Theme.BodyBold, "+ " + S.T(w.Name), new Vector2(tx, ty), 18 * u, Theme.Aspal); ty += 26 * u; }
                foreach (var a in m.Arenas) { Theme.Text(Theme.BodyBold, "+ " + S.T(a.Name), new Vector2(tx, ty), 18 * u, Theme.Aspal); ty += 26 * u; }
            }
            foreach (var e in mod.Errors.Take(5)) { ty = Theme.Paragraph(Theme.Body, "! " + e, new Vector2(tx, ty), 16 * u, tw, Theme.Cabe); }

            if (mod.IsValid && Gui.Button(new Rectangle(card.X + 24 * u, card.Y + card.Height - 74 * u, 260 * u, 52 * u),
                    mod.Enabled ? S.T("workshop.turnOff") : S.T("workshop.turnOn"), !mod.Enabled, 20 * u))
            {
                ModLoader.SetEnabled(App.ModsDir, mod.Id, !mod.Enabled);
                App.ReloadContent();
            }
        }

        float by = sh - 150 * u;
        if (Gui.Button(new Rectangle(x, by, 320 * u, 54 * u), S.T("workshop.install"), primary: true, 20 * u)) InstallInbox();
        if (Gui.Button(new Rectangle(x + 340 * u, by, 300 * u, 54 * u), S.T("workshop.open"), false, 20 * u))
        {
            Directory.CreateDirectory(Inbox);
            try { Process.Start(new ProcessStartInfo { FileName = App.ModsDir, UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { _status = ex.Message; }
        }
        if (Gui.Button(new Rectangle(x + 660 * u, by, 200 * u, 54 * u), S.T("common.back"), false, 20 * u)) App.Go(new MainMenuScreen(App));
        if (_status.Length > 0) Theme.Paragraph(Theme.BodyBold, _status, new Vector2(x, by + 70 * u), 18 * u, sw - 2 * x, Theme.Kuning);
        App.DrawFooter();
    }
}
