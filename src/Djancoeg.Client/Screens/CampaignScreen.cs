using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Game;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;
using Djancoeg.Net;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Tracks one campaign attempt while the match runs.</summary>
public sealed class CampaignRun(CampaignStage stage, bool coop = false)
{
    public CampaignStage Stage { get; } = stage;
    public bool Coop { get; } = coop;
    public float Seconds { get; private set; }
    public float EndedFor { get; private set; }
    public bool BossDown { get; private set; }

    public void Update(float dt, Session session)
    {
        if (session.Client.Latest is not { } s) return;
        if (s.Phase == MatchPhase.Playing) Seconds += dt;
        else if (s.Phase == MatchPhase.Ended) EndedFor += dt;
    }

    public void OnCreepKilled(int kind, ContentRegistry content)
    {
        if (Stage.IsBoss && kind == content.CreepIndexOf(Stage.Boss)) BossDown = true;
    }

    /// <summary>Win/loss and deaths from the final snapshot.</summary>
    public (bool Won, int Deaths) Result(Session session)
    {
        var s = session.Client.Latest;
        if (s is null || !s.TryGetPlayer(session.Client.LocalId, out var me)) return (false, 99);
        var mode = Stage.ToMatch(Coop).Mode;
        bool team = mode is GameModeKind.TeamDeathmatch or GameModeKind.CaptureTheFlag;
        bool won = team ? s.Winner == me.Team
            // Co-op in solo modes (King of the Warung, race): any human winning counts for the group.
            : Coop ? s.TryGetPlayer(s.Winner, out var w) && !w.Has(NetPlayerFlags.Bot) : s.Winner == me.Id;
        if (Stage.IsBoss && !BossDown) won = false;
        return (won, me.Deaths);
    }

    /// <summary>Starts a stage, showing the island's intro cutscene first the first time.</summary>
    public static void Play(App app, CampaignStage stage, bool coop = false)
    {
        var island = Campaign.Islands.First(i => i.Stages.Contains(stage));
        var progress = CampaignProgress.Load();
        if (island.Intro.Count > 0 && !progress.SeenCutscenes.Contains(island.Id))
        {
            progress.SeenCutscenes.Add(island.Id);
            progress.Save();
            app.Go(new CutsceneScreen(app, Campaign.IntroOf(island), () => new StageLauncher(app, stage, coop)));
            return;
        }
        Start(app, stage, coop);
    }

    public static void Start(App app, CampaignStage stage, bool coop = false)
    {
        var st = app.Settings;
        try
        {
            // Co-op hosts a LAN server (visible in Find a game) on the usual host port; friends simply join it.
            var session = Session.StartLocal(app.Content, st, stage.ToMatch(coop), host: coop, master: null, persistStats: false, rotate: false);
            var run = new CampaignRun(stage, coop);
            app.Go(new PlayScreen(app, session) { Campaign = run });
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            app.Go(new MessageScreen(app, ex.Message, new CampaignScreen(app)));
        }
    }
}

/// <summary>Island map for the single-player Tur Nusantara.</summary>
public sealed class CampaignScreen(App app) : Screen(app)
{
    private readonly CampaignProgress _progress = CampaignProgress.Load();
    private bool _coop;
    private int _island = -1;
    private float _time;

    public override void Enter()
    {
        base.Enter();
        if (_island < 0) _island = Math.Max(0, Campaign.Islands.ToList().FindIndex(i => i.Id == _progress.LastIsland));
    }

    public override void Update(float dt)
    {
        _time += dt;
        if (Gui.Back) App.Go(new MainMenuScreen(App));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        float u = U;
        int total = _progress.TotalStars;

        Theme.SignText("TUR NUSANTARA", new Vector2(60 * u, 40 * u), 64 * u, Theme.Kuning, doubleShadow: true);
        Theme.Text(Theme.BodyBold, S.T("camp.subtitle"), new Vector2(64 * u, 116 * u), 20 * u, Theme.Kapur);
        string starText = $"{total} / {Campaign.MaxStars}";
        Theme.TextRight(Theme.Display, starText, new Vector2(sw - 60 * u, 52 * u), 40 * u, Theme.Kuning);
        DrawStar(new Vector2(sw - 84 * u - Theme.Measure(Theme.Display, starText, 40 * u).X, 74 * u), 20 * u, Theme.Kuning);

        // Map.
        float panelW = 520 * u;
        var mapArea = new Rectangle(50 * u, 160 * u, sw - panelW - 130 * u, sh - 280 * u);
        var map = App.Models.Texture("ui/nusantara-map.jpg");
        Rectangle mr = mapArea;
        if (map is { } tex)
        {
            float aspect = (float)tex.Width / tex.Height;
            float w = MathF.Min(mapArea.Width, mapArea.Height * aspect), h = w / aspect;
            mr = new Rectangle(mapArea.X, mapArea.Y + (mapArea.Height - h) / 2, w, h);
            DrawRectangleRec(new Rectangle(mr.X + 12 * u, mr.Y + 12 * u, mr.Width, mr.Height), Theme.Aspal);
            DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), mr, Vector2.Zero, 0, Color.White);
        }
        else Gui.Panel(mr, Theme.TarpLight);

        var mouse = GetMousePosition();
        for (int i = 0; i < Campaign.Islands.Count; i++)
        {
            var isl = Campaign.Islands[i];
            bool open = Campaign.IsIslandUnlocked(isl, total);
            var p = new Vector2(mr.X + isl.MapX * mr.Width, mr.Y + isl.MapY * mr.Height);
            float r = (i == _island ? 30 : 24) * u * (1 + 0.06f * MathF.Sin(_time * 4 + i));
            var col = open ? Theme.Rgba(ColorUtil.ParseRgba(isl.Color)) : new Color(90, 90, 90, 255);
            DrawCircleV(p + new Vector2(4 * u, 4 * u), r, Theme.Aspal);
            DrawCircleV(p, r, col);
            DrawRing(p, r - 3 * u, r, 0, 360, 32, Theme.Aspal);
            int stars = isl.Stages.Sum(st => _progress.StarsOf(st.Id));
            Theme.TextCentered(Theme.Display, open ? (i + 1).ToString(CultureInfo.InvariantCulture) : "?", p, 26 * u, Theme.Aspal);
            string label = S.T(isl.Name).ToUpperInvariant() + (open ? $"   {stars}/{isl.Stages.Count * 3}" : $"   {isl.StarsToUnlock}");
            var lm = Theme.Measure(Theme.BodyBold, label, 18 * u);
            var lr = new Rectangle(p.X - lm.X / 2 - 10 * u, p.Y + r + 8 * u, lm.X + 40 * u, 28 * u);
            DrawRectangleRec(lr, i == _island ? Theme.Kuning : Theme.Kapur);
            Theme.Text(Theme.BodyBold, label, new Vector2(lr.X + 10 * u, lr.Y + 4 * u), 18 * u, Theme.Aspal);
            DrawStar(new Vector2(lr.X + lr.Width - 16 * u, lr.Y + lr.Height / 2), 9 * u, open ? Theme.Cabe : Theme.Aspal);
            if (Vector2.Distance(mouse, p) < r + 6 * u && IsMouseButtonPressed(MouseButton.Left)) { _island = i; _progress.LastIsland = isl.Id; }
        }

        // Island panel.
        var island = Campaign.Islands[_island];
        bool islandOpen = Campaign.IsIslandUnlocked(island, total);
        var pr = new Rectangle(sw - panelW - 60 * u, 160 * u, panelW, sh - 280 * u);
        Gui.Panel(pr, Theme.Kapur);
        float y = pr.Y + 20 * u;
        Theme.Text(Theme.Display, S.T(island.Name).ToUpperInvariant(), new Vector2(pr.X + 24 * u, y), 40 * u, Theme.Cabe);
        y += 52 * u;
        y = Theme.Paragraph(Theme.Body, S.T(island.Blurb), new Vector2(pr.X + 24 * u, y), 17 * u, pr.Width - 48 * u, Theme.Aspal) + 14 * u;
        if (!islandOpen)
        {
            Theme.Paragraph(Theme.BodyBold, S.T("camp.locked").Replace("{0}", island.StarsToUnlock.ToString(CultureInfo.InvariantCulture)),
                new Vector2(pr.X + 24 * u, y), 20 * u, pr.Width - 48 * u, Theme.Cabe);
        }
        for (int k = 0; k < island.Stages.Count && islandOpen; k++)
        {
            var st = island.Stages[k];
            bool open = Campaign.IsStageUnlocked(island, k, _progress.StarsOf, total);
            var row = new Rectangle(pr.X + 18 * u, y, pr.Width - 36 * u, 96 * u);
            bool clicked = Gui.Row(row, false, out bool focused) && open;
            if (st.IsBoss) DrawRectangleRec(new Rectangle(row.X, row.Y, 8 * u, row.Height), Theme.Cabe);
            var ink = open ? Theme.Aspal : Theme.Fade(Theme.Aspal, 0.35f);
            Theme.Text(Theme.BodyBold, $"{k + 1}. {S.T(st.Name)}", new Vector2(row.X + 18 * u, row.Y + 8 * u), 20 * u, ink);
            Theme.Paragraph(Theme.Body, open ? S.T(st.Briefing) : S.T("camp.stageLocked"), new Vector2(row.X + 18 * u, row.Y + 36 * u), 15 * u, row.Width - 150 * u, ink);
            int stars = _progress.StarsOf(st.Id);
            for (int sIdx = 0; sIdx < 3; sIdx++)
                DrawStar(new Vector2(row.X + row.Width - 104 * u + sIdx * 34 * u, row.Y + 30 * u), 14 * u, sIdx < stars ? Theme.Kuning : Theme.Fade(Theme.Aspal, 0.2f));
            if (_progress.BestSeconds.TryGetValue(st.Id, out float best))
                Theme.TextRight(Theme.Mono, FormatTime(best), new Vector2(row.X + row.Width - 14 * u, row.Y + 58 * u), 15 * u, Theme.Fade(Theme.Aspal, 0.7f));
            if (clicked)
            {
                _progress.Save();
                CampaignRun.Play(App, st, _coop);
                return;
            }
            y += 106 * u;
        }

        var coopRect = new Rectangle(mapArea.X, sh - 116 * u, 380 * u, 46 * u);
        Gui.Toggle(coopRect, S.T("camp.coop"), ref _coop);
        float textX = coopRect.X + coopRect.Width + 20 * u, textW = sw - 760 * u - textX;
        Theme.Paragraph(Theme.Body, S.T(_coop ? "camp.coopHint" : "camp.rules"), new Vector2(textX, sh - 116 * u), 15 * u, textW, _coop ? Theme.Kuning : Theme.Kapur);
        if (islandOpen && island.Intro.Count > 0 && Gui.Button(new Rectangle(sw - 740 * u, sh - 112 * u, 220 * u, 52 * u), S.T("camp.story"), false, 22 * u))
        {
            _progress.Save();
            App.Go(new CutsceneScreen(App, Campaign.IntroOf(island), () => new CampaignScreen(App)));
            return;
        }
        if (Gui.Button(new Rectangle(sw - 500 * u, sh - 112 * u, 220 * u, 52 * u), S.T("menu.achievements"), false, 22 * u))
        {
            _progress.Save();
            App.Go(new AchievementsScreen(App, this));
            return;
        }
        if (Gui.Button(new Rectangle(sw - 260 * u, sh - 112 * u, 200 * u, 52 * u), S.T("common.back"), false, 22 * u))
        {
            _progress.Save();
            App.Go(new MainMenuScreen(App));
        }
        App.DrawFooter();
    }

    public static string FormatTime(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    public static void DrawStar(Vector2 c, float r, Color color)
    {
        Span<Vector2> pts = stackalloc Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -MathF.PI / 2 + i * MathF.PI / 5;
            float rr = (i & 1) == 0 ? r : r * 0.45f;
            pts[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
        }
        for (int i = 0; i < 10; i++) DrawTriangle(c, pts[(i + 1) % 10], pts[i], color);
    }
}

/// <summary>Stars, time and what next after a campaign stage.</summary>
public sealed class CampaignResultScreen(App app, CampaignStage stage, bool won, int deaths, float seconds, bool coop = false) : Screen(app)
{
    private readonly int _stars = stage.Stars(won, deaths, seconds);
    private bool _recorded;
    private bool _improved;
    private float _t;

    public override void Enter()
    {
        base.Enter();
        if (_recorded) return;
        _recorded = true;
        var progress = CampaignProgress.Load();
        int before = progress.TotalStars;
        _improved = progress.Record(stage, _stars, seconds);
        progress.Save();
        App.Achievements.OnCampaignResult(stage, _stars, progress, before);
        if (_stars > 0 && stage.Id == Campaign.FinaleStage && !progress.SeenCutscenes.Contains(Campaign.Finale.Id))
        {
            progress.SeenCutscenes.Add(Campaign.Finale.Id);
            progress.Save();
            _showFinale = true;
        }
    }

    private bool _showFinale;

    public override void Update(float dt)
    {
        _t += dt;
        if (_showFinale && _t > 2.5f)
        {
            _showFinale = false;
            App.Go(new CutsceneScreen(App, Campaign.Finale, () => this));
            return;
        }
        if (Gui.Back) App.Go(new CampaignScreen(App));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        float u = U;
        string title = won ? S.T("camp.won") : S.T("camp.lost");
        Theme.SignText(title.ToUpperInvariant(), new Vector2(sw / 2f - Theme.Measure(Theme.Display, title.ToUpperInvariant(), 80 * u).X / 2, 120 * u), 80 * u, won ? Theme.Kuning : Theme.Cabe, doubleShadow: true);
        Theme.TextCentered(Theme.BodyBold, S.T(stage.Name), new Vector2(sw / 2f, 240 * u), 28 * u, Theme.Kapur);

        for (int i = 0; i < 3; i++)
        {
            float appear = Math.Clamp((_t - 0.4f - i * 0.35f) * 4f, 0, 1);
            bool lit = i < _stars && appear > 0;
            float r = 54 * u * (lit ? 1 + 0.25f * (1 - appear) : 1);
            DrawStar(new Vector2(sw / 2f + (i - 1) * 150 * u, 360 * u), r, lit ? Theme.Kuning : Theme.Fade(Theme.Kapur, 0.25f));
        }

        float y = 460 * u;
        string[] lines =
        [
            (won ? "✓ " : "✗ ") + S.T("camp.star1"),
            (won && deaths <= stage.MaxDeathsForTwoStars ? "✓ " : "✗ ") + S.T("camp.star2").Replace("{0}", stage.MaxDeathsForTwoStars.ToString(CultureInfo.InvariantCulture)) + $"  ({deaths})",
            (_stars >= 3 ? "✓ " : "✗ ") + S.T("camp.star3").Replace("{0}", CampaignScreen.FormatTime(stage.ParSeconds)) + $"  ({CampaignScreen.FormatTime(seconds)})",
        ];
        foreach (var l in lines) { Theme.TextCentered(Theme.BodyBold, l.Replace("✓", "+").Replace("✗", "-"), new Vector2(sw / 2f, y), 22 * u, Theme.Kapur); y += 36 * u; }
        if (_improved && _stars > 0) Theme.TextCentered(Theme.BodyBold, S.T("camp.newBest"), new Vector2(sw / 2f, y + 6 * u), 22 * u, Theme.Kuning);

        float bx = sw / 2f - 330 * u, by = sh - 210 * u;
        if (Gui.Button(new Rectangle(bx, by, 200 * u, 60 * u), S.T("camp.retry"), !won, 22 * u)) { CampaignRun.Play(App, stage, coop); return; }
        var next = NextStage();
        if (Gui.Button(new Rectangle(bx + 230 * u, by, 200 * u, 60 * u), S.T("camp.next"), won, 22 * u, enabled: next is not null && won)) { CampaignRun.Play(App, next!, coop); return; }
        if (Gui.Button(new Rectangle(bx + 460 * u, by, 200 * u, 60 * u), S.T("camp.map"), false, 22 * u)) App.Go(new CampaignScreen(App));
        App.DrawFooter();
    }

    private static void DrawStar(Vector2 c, float r, Color color) => CampaignScreen.DrawStar(c, r, color);

    private CampaignStage? NextStage()
    {
        var all = Campaign.AllStages.ToList();
        int i = all.FindIndex(s => s.Id == stage.Id);
        if (i < 0 || i + 1 >= all.Count) return null;
        var next = all[i + 1];
        var progress = CampaignProgress.Load();
        var island = Campaign.Islands.First(isl => isl.Stages.Contains(next));
        int total = progress.TotalStars;
        return Campaign.IsStageUnlocked(island, island.Stages.ToList().IndexOf(next), progress.StarsOf, total) ? next : null;
    }
}

/// <summary>Starts a stage after a cutscene (screen transitions must go through <see cref="App.Go"/>).</summary>
public sealed class StageLauncher(App app, CampaignStage stage, bool coop) : Screen(app)
{
    private bool _started;
    public override void Update(float dt)
    {
        if (_started) return;
        _started = true;
        CampaignRun.Start(App, stage, coop);
    }
    public override void Draw() => App.DrawBackdrop();
}
