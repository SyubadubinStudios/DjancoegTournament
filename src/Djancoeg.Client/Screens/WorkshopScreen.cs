using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Modding;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Local Workshop: list, enable/disable and install mods (.djmod packages from mods/_inbox).</summary>
public sealed class WorkshopScreen(App app, bool online = false) : Screen(app)
{
    private string _status = "";
    private int _selected;
    private bool _online = online;
    private string _url = app.Settings.WorkshopUrl;
    private Task<WorkshopIndex>? _fetch;
    private WorkshopIndex? _index;
    private Task<ModInfo>? _install;
    private string _installing = "";
    private string _onlineStatus = "";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private string Inbox => Path.Combine(App.ModsDir, "_inbox");

    public override void Enter()
    {
        base.Enter();
        if (_online) Fetch();
    }

    public override bool ReadyForScreenshot => !_online || _index is not null || _onlineStatus.StartsWith("Workshop", StringComparison.Ordinal);

    public override void Update(float dt)
    {
        if (_fetch is { IsCompleted: true } f)
        {
            if (f.IsCompletedSuccessfully) { _index = f.Result; _onlineStatus = ""; }
            else _onlineStatus = string.Format(CultureInfo.InvariantCulture, S.T("workshop.failed"), f.Exception?.GetBaseException().Message);
            _fetch = null;
        }
        if (_install is { IsCompleted: true } t)
        {
            if (t.IsCompletedSuccessfully) { App.ReloadContent(); _onlineStatus = S.T("workshop.done").Replace("{0}", _installing); }
            else _onlineStatus = string.Format(CultureInfo.InvariantCulture, S.T("workshop.failed"), t.Exception?.GetBaseException().Message);
            _install = null;
        }
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

    private void Fetch()
    {
        App.Settings.WorkshopUrl = _url;
        _onlineStatus = S.T("workshop.loadingList");
        _fetch = new WorkshopClient(Http).FetchIndexAsync(_url);
    }

    private void DrawOnline(float u, int sw, int sh)
    {
        float x = 84 * u, y = 210 * u;
        Gui.TextField(new Rectangle(x, y, 900 * u, 52 * u), S.T("workshop.url"), ref _url, 96);
        if (Gui.Button(new Rectangle(x + 920 * u, y, 220 * u, 52 * u), S.T("workshop.fetch"), true, 20 * u) && _fetch is null) Fetch();
        y += 70 * u;
        if (_onlineStatus.Length > 0) { Theme.Paragraph(Theme.BodyBold, _onlineStatus, new Vector2(x, y), 18 * u, sw - 2 * x, Theme.Kuning); y += 34 * u; }
        var packages = _index?.Packages ?? [];
        if (_index is not null && packages.Count == 0)
            Theme.Paragraph(Theme.BodyBold, S.T("workshop.onlineEmpty"), new Vector2(x, y), 20 * u, sw - 2 * x, Theme.Kapur);
        foreach (var e in packages)
        {
            if (y > sh - 220 * u) break;
            var row = new Rectangle(x, y, sw - 2 * x, 92 * u);
            Gui.Panel(row, Theme.Kapur, 5 * u);
            Theme.Text(Theme.Display, S.T(e.Name).ToUpperInvariant(), new Vector2(row.X + 18 * u, row.Y + 10 * u), 22 * u, Theme.Aspal);
            Theme.Text(Theme.Mono, $"{e.Id}  v{e.Version}  ·  {e.Author}  ·  {e.Size / 1024} KB  ·  " +
                string.Format(CultureInfo.InvariantCulture, S.T("workshop.contents"), e.Weapons, e.Characters, e.Arenas), new Vector2(row.X + 18 * u, row.Y + 40 * u), 14 * u, Theme.Fade(Theme.Aspal, 0.75f));
            Theme.Text(Theme.Body, S.T(e.Description), new Vector2(row.X + 18 * u, row.Y + 62 * u), 16 * u, Theme.Aspal);
            var installed = App.Mods.FirstOrDefault(m => m.Id == e.Id);
            bool same = installed?.Manifest?.Version == e.Version;
            string label = same ? S.T("workshop.upToDate") : installed is null ? S.T("workshop.download") : S.T("workshop.update");
            if (Gui.Button(new Rectangle(row.X + row.Width - 280 * u, row.Y + 20 * u, 260 * u, 52 * u), label, !same, 18 * u, enabled: !same && _install is null))
            {
                _installing = S.T(e.Name);
                _onlineStatus = S.T("workshop.downloading").Replace("{0}", _installing);
                _install = new WorkshopClient(Http).InstallAsync(_url, e, App.ModsDir);
            }
            y += 106 * u;
        }
        if (Gui.Button(new Rectangle(x, sh - 110 * u, 200 * u, 54 * u), S.T("common.back"), false, 20 * u)) App.Go(new MainMenuScreen(App));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        App.DrawHeader(S.T("workshop.title"), S.T("workshop.desc"));
        if (Gui.Button(new Rectangle(sw - 500 * u, 92 * u, 200 * u, 48 * u), S.T("workshop.tabInstalled"), !_online, 20 * u)) _online = false;
        if (Gui.Button(new Rectangle(sw - 284 * u, 92 * u, 200 * u, 48 * u), S.T("workshop.tabOnline"), _online, 20 * u))
        {
            _online = true;
            if (_index is null && _fetch is null) Fetch();
        }
        if (_online) { DrawOnline(u, sw, sh); App.DrawFooter(); return; }

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
