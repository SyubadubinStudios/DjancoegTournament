using Djancoeg.Client.Game;
using Djancoeg.Client.Ui;
using Djancoeg.Net;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Hosts a <see cref="GameView"/>; handles mouse capture, the pause menu and leaving.</summary>
public sealed class PlayScreen(App app, Session session) : Screen(app)
{
    private readonly GameView _view = new(app, session);
    private float _age;
    private bool _suspended; // true while Fighters/Settings are shown on top of the match
    private bool _showBoard;
    private bool _ragdollThrown;
    private float _ragdollAt;

    public bool ShowScoreboardForShot { get; init; }
    /// <summary>Set when this match is a Tur Nusantara stage.</summary>
    public CampaignRun? Campaign { get; init; }
    /// <summary>Where "leave match" goes (e.g. back to the level editor).</summary>
    public Func<Screen>? ReturnTo { get; init; }
    public override bool ReadyForScreenshot =>
        (session.Client.IsSpectator && _age > 8f) ||
        (App.Options.Shot == "creeps" && _age > 14f && session.Client.Latest is { CreepCount: > 2 }) ||
        (App.Options.Shot == "ragdoll" && _ragdollThrown && _age > _ragdollAt + 2.2f) ||
        (App.Options.Shot == "boss" && _age > 13f && session.Client.Latest is { CreepCount: > 0 }) ||
        App.Options.Shot is not ("boss" or "ragdoll") && _age > 7f && session.Client.State == ClientState.Connected &&
        session.Client.Latest is { } s && s.TryGetPlayer(session.Client.LocalId, out var me) && me.Has(NetPlayerFlags.Alive) && !me.Has(NetPlayerFlags.Protected) && (_age > 30f || (me.Health >= 60 && (!session.Autopilot || session.AutopilotEngaged)));

    public override void Enter()
    {
        _suspended = false;
        _view.ForceScoreboard = ShowScoreboardForShot;
        if (Campaign is { } run)
        {
            _view.BossKind = run.Stage.IsBoss ? session.Content.CreepIndexOf(run.Stage.Boss) : -1;
            _view.CreepKilledHook = kind => run.OnCreepKilled(kind, session.Content);
            _view.Banner = S.T(run.Stage.Name);
        }
        if (_view.Paused) EnableCursor(); else DisableCursor();
    }

    public override void Leave()
    {
        if (_suspended) return;
        _view.Dispose();
        session.Dispose();
        EnableCursor();
    }

    /// <summary>Keeps the connection alive while a menu is shown over the match.</summary>
    public override void BackgroundTick(float dt) => _view.Update(dt, inputAllowed: false);

    public override void Update(float dt)
    {
        _age += dt;
        if (session.Client.State is ClientState.Rejected or ClientState.TimedOut && _age > 0.5f)
        {
            string msg = session.Client.RejectCode switch
            {
                Net.RejectCode.Password => S.T("reject.password"),
                Net.RejectCode.Banned => S.T("reject.banned"),
                Net.RejectCode.Kicked => S.T("reject.kicked").Replace("{0}", session.Client.RejectReason),
                _ => session.Client.RejectReason,
            };
            App.Go(new MessageScreen(App, msg, session.Kind == SessionKind.Join ? new BrowserScreen(App) : new MainMenuScreen(App)));
            return;
        }
        if (!_view.Chatting && Controls.PausePressed)
        {
            _view.Paused = !_view.Paused;
            if (_view.Paused) EnableCursor(); else DisableCursor();
        }
        _view.Update(dt, inputAllowed: true);
        if (App.Options.Shot == "ragdoll" && !_ragdollThrown && _age > 6f) { _view.DebugThrowRagdolls(); _ragdollThrown = true; _ragdollAt = _age; }
        if (Campaign is { } run)
        {
            run.Update(dt, session);
            if (run.EndedFor > 4f)
            {
                var (won, deaths) = run.Result(session);
                App.Go(new CampaignResultScreen(App, run.Stage, won, deaths, run.Seconds, run.Coop));
            }
        }
    }

    public override void Draw()
    {
        _view.Draw();
        if (_view.Paused) DrawPause();
    }

    private void Suspend(Screen next)
    {
        _suspended = true;
        App.Go(next);
    }

    private void DrawPause()
    {
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        float u = U;
        DrawRectangle(0, 0, sw, sh, Theme.Fade(Theme.TarpDeep, 0.78f));
        Theme.SignText(S.Lang == Core.Localization.Language.Indonesian ? "ISTIRAHAT DULU" : "TAKE A BREATHER", new System.Numerics.Vector2(96 * u, 120 * u), 64 * u, Theme.Kuning, doubleShadow: true);
        string[] items = ["pause.resume", "pause.fighter", "pause.camera", "pause.leaderboard", "menu.settings", "pause.leave"];
        float[] tilts = [-2f, 2f, -1.5f, 1.5f, 2f, -2.5f];
        float y = 280 * u;
        for (int i = 0; i < items.Length; i++)
        {
            bool leave = i == items.Length - 1;
            if (Gui.Sticker(new Rectangle(110 * u, y, 440 * u, 56 * u), S.T(items[i]).ToUpperInvariant(), tilts[i], leave ? Theme.Cabe : Theme.Kapur, leave ? Theme.Kapur : Theme.Aspal, 26 * u))
            {
                switch (i)
                {
                    case 0: _view.Paused = false; DisableCursor(); break;
                    case 1: Suspend(new FightersScreen(App, this, id => session.Client.ChangeCharacter(id))); break;
                    case 2: session.ThirdPerson = !session.ThirdPerson; break;
                    case 3: _showBoard = !_showBoard; if (_showBoard) session.Client.RequestLeaderboard(); break;
                    case 4: Suspend(new SettingsScreen(App, this)); break;
                    case 5: App.Go(ReturnTo?.Invoke() ?? (Campaign is null ? new MainMenuScreen(App) : new CampaignScreen(App))); break;
                }
            }
            y += 70 * u;
        }
        if (_showBoard)
        {
            var board = new Rectangle(sw - 760 * u, 200 * u, 690 * u, sh - 300 * u);
            LeaderboardScreen.DrawTable(board, S.T("lb.server"), session.Client.Leaderboard.Select(e => (e.Rank, e.Name, e.Kills, e.Deaths, e.Wins, e.Matches, e.Captures, e.BestStreak, e.Minutes, e.IsYou)).ToList(),
                session.Client.LeaderboardVersion == 0 ? S.T("lb.loading") : S.T("lb.empty"), u);
            return;
        }
        var r = new Rectangle(sw - 640 * u, 280 * u, 560 * u, 230 * u);
        Gui.Panel(r, Theme.Kapur);
        string kind = session.Kind switch { SessionKind.Practice => "PRACTICE", SessionKind.Host => "HOST", _ => "ONLINE" };
        Theme.Text(Theme.Mono, kind + "  ·  " + session.Endpoint, new System.Numerics.Vector2(r.X + 20 * u, r.Y + 20 * u), 16 * u, Theme.Cabe);
        Theme.Paragraph(Theme.Body, S.T("settings.controlsText"), new System.Numerics.Vector2(r.X + 20 * u, r.Y + 54 * u), 17 * u, r.Width - 40 * u, Theme.Aspal);
    }
}
