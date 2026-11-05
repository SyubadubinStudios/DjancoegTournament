using System.Globalization;
using System.Net;
using System.Numerics;
using Djancoeg.Client.Game;
using Djancoeg.Client.Ui;
using Djancoeg.Net;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>LAN and online (master server) browser plus direct IP connect.</summary>
public sealed class BrowserScreen(App app) : Screen(app)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly ServerBrowser _browser = new();
    private bool _online;
    private string _direct = app.Settings.LastDirectConnect;
    private int _selected = -1;
    private float _refreshTimer;

    public override void Enter()
    {
        base.Enter();
        Refresh();
    }

    public override void Leave() => _browser.Dispose();

    private void Refresh()
    {
        _browser.Clear();
        _selected = -1;
        if (_online) _browser.RefreshOnline(App.Settings.MasterServer);
        else _browser.RefreshLan();
        _refreshTimer = 5f;
    }

    public override void Update(float dt)
    {
        _browser.Poll();
        _refreshTimer -= dt;
        if (_refreshTimer <= 0) Refresh();
        if (Gui.Back && Gui.TextFocus < 0) App.Go(new MainMenuScreen(App));
    }

    private void Join(IPEndPoint ep)
    {
        App.Settings.Save();
        App.Go(new PlayScreen(App, Session.Join(App.Content, App.Settings, ep)));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        App.DrawHeader(S.T("browser.title"));

        float x = 84 * u, y = 196 * u;
        if (Gui.Button(new Rectangle(x, y, 160 * u, 48 * u), S.T("browser.lan"), !_online, 20 * u)) { _online = false; Refresh(); }
        if (Gui.Button(new Rectangle(x + 176 * u, y, 160 * u, 48 * u), S.T("browser.online"), _online, 20 * u)) { _online = true; Refresh(); }
        if (Gui.Button(new Rectangle(x + 352 * u, y, 200 * u, 48 * u), S.T("browser.refresh"), false, 20 * u)) Refresh();
        if (_online) Theme.Text(Theme.Mono, S.T("browser.master") + ": " + App.Settings.MasterServer, new Vector2(x + 576 * u, y + 14 * u), 16 * u, Theme.Kapur);

        // List.
        var list = new Rectangle(x, y + 72 * u, sw - 2 * x, sh - 470 * u);
        DrawRectangleRec(list, Theme.Fade(Theme.Aspal, 0.45f));
        float cx0 = list.X + 20 * u, cx1 = list.X + list.Width * 0.42f, cx2 = list.X + list.Width * 0.62f, cx3 = list.X + list.Width * 0.82f, cx4 = list.X + list.Width - 20 * u;
        float hy = list.Y + 12 * u;
        Theme.Text(Theme.Mono, S.T("browser.colName"), new Vector2(cx0, hy), 15 * u, Theme.Kuning);
        Theme.Text(Theme.Mono, S.T("browser.colArena"), new Vector2(cx1, hy), 15 * u, Theme.Kuning);
        Theme.Text(Theme.Mono, S.T("browser.colMode"), new Vector2(cx2, hy), 15 * u, Theme.Kuning);
        Theme.Text(Theme.Mono, S.T("browser.colPlayers"), new Vector2(cx3, hy), 15 * u, Theme.Kuning);
        Theme.TextRight(Theme.Mono, S.T("browser.colPing"), new Vector2(cx4, hy), 15 * u, Theme.Kuning);
        float ry = hy + 32 * u;
        var servers = _browser.Servers.ToList();
        if (servers.Count == 0)
            Theme.Paragraph(Theme.BodyBold, S.T("browser.empty"), new Vector2(cx0, ry + 20 * u), 22 * u, list.Width - 40 * u, Theme.Kapur);
        for (int i = 0; i < servers.Count; i++)
        {
            var sv = servers[i];
            var row = new Rectangle(list.X + 8 * u, ry, list.Width - 16 * u, 44 * u);
            bool sel = i == _selected;
            if (Gui.Row(row, sel, out _))
            {
                if (sel) Join(sv.EndPoint);
                _selected = i;
            }
            var col = sel ? Theme.Aspal : Theme.Kapur;
            bool mismatch = sv.ContentSignature != App.Content.Signature;
            Theme.Text(Theme.BodyBold, sv.Name + (mismatch ? $"  ({S.T("browser.mismatch")})" : ""), new Vector2(cx0, ry + 10 * u), 20 * u, mismatch ? Theme.Cabe : col);
            int ai = App.Content.ArenaIndexOf(sv.ArenaId);
            Theme.Text(Theme.Body, ai >= 0 ? S.T(App.Content.Arenas[ai].Name) : sv.ArenaId, new Vector2(cx1, ry + 10 * u), 19 * u, col);
            var (en, id) = ModeNames.Of((Core.Content.GameModeKind)sv.Mode);
            Theme.Text(Theme.Body, S.Lang == Core.Localization.Language.Indonesian ? id : en, new Vector2(cx2, ry + 10 * u), 19 * u, col);
            Theme.Text(Theme.Mono, $"{sv.Players}/{sv.MaxPlayers}", new Vector2(cx3, ry + 11 * u), 18 * u, col);
            Theme.TextRight(Theme.Mono, sv.PingMs.ToString("0", Inv), new Vector2(cx4, ry + 11 * u), 18 * u, col);
            ry += 50 * u;
        }

        // Direct connect.
        float dy = list.Y + list.Height + 30 * u;
        Gui.TextField(new Rectangle(x, dy, 760 * u, 54 * u), S.T("browser.direct"), ref _direct, 64, "192.168.1.10:27960");
        if (Gui.Button(new Rectangle(x + 790 * u, dy, 180 * u, 54 * u), S.T("browser.join"), primary: true, 22 * u))
        {
            var text = _direct.Contains(':') ? _direct : _direct + ":" + Protocol.DefaultPort.ToString(Inv);
            if (IPEndPoint.TryParse(text, out var ep)) { App.Settings.LastDirectConnect = _direct; Join(ep); }
            else
            {
                try
                {
                    var parts = text.Split(':');
                    var addr = Dns.GetHostAddresses(parts[0]).First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    App.Settings.LastDirectConnect = _direct;
                    Join(new IPEndPoint(addr, int.Parse(parts[1], Inv)));
                }
                catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException or FormatException)
                {
                    App.Go(new MessageScreen(App, ex.Message, new BrowserScreen(App)));
                }
            }
        }
        if (_selected >= 0 && _selected < servers.Count && Gui.Button(new Rectangle(sw - x - 240 * u, dy, 240 * u, 54 * u), S.T("browser.join") + " >", true, 22 * u))
            Join(servers[_selected].EndPoint);
        if (Gui.Button(new Rectangle(x, dy + 80 * u, 200 * u, 52 * u), S.T("common.back"), false, 20 * u)) App.Go(new MainMenuScreen(App));
        App.DrawFooter();
    }
}
