using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Net;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Career leaderboard of this computer's practice and hosted matches (server-data/stats.json).</summary>
public sealed class LeaderboardScreen(App app) : Screen(app)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly List<(int Rank, string Name, int Kills, int Deaths, int Wins, int Matches, int Captures, int Streak, int Minutes, bool You)> _rows = Load(app.Settings.PlayerKey);

    private static List<(int, string, int, int, int, int, int, int, int, bool)> Load(string key)
    {
        var store = new StatsStore(Path.Combine(ClientSettings.ServerDataFolder, "stats.json"));
        return store.Top(15).Select((p, i) => (i + 1, p.Name, p.Kills, p.Deaths, p.Wins, p.Matches, p.Captures, p.BestStreak, (int)(p.PlaySeconds / 60), p.Key == key)).ToList();
    }

    public override void Update(float dt)
    {
        if (Gui.Back) App.Go(new MainMenuScreen(App));
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        float u = U;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        App.DrawHeader(S.T("lb.title"), S.T("lb.local"));
        DrawTable(new Rectangle(84 * u, 210 * u, sw - 168 * u, sh - 340 * u), null, _rows, S.T("lb.empty"), u);
        if (Gui.Button(new Rectangle(84 * u, sh - 110 * u, 220 * u, 56 * u), S.T("common.back"), true, 22 * u)) App.Go(new MainMenuScreen(App));
        App.DrawFooter();
    }

    /// <summary>Paper-sign table shared by the local board and the in-match server board.</summary>
    public static void DrawTable(Rectangle r, string? title, IReadOnlyList<(int Rank, string Name, int Kills, int Deaths, int Wins, int Matches, int Captures, int Streak, int Minutes, bool You)> rows, string emptyText, float u)
    {
        Gui.Panel(r, Theme.Kapur);
        float y = r.Y + 18 * u;
        if (title is not null) { Theme.SignText(title.ToUpperInvariant(), new Vector2(r.X + 20 * u, y), 28 * u, Theme.Kuning); y += 50 * u; }
        if (rows.Count == 0)
        {
            Theme.Paragraph(Theme.BodyBold, emptyText, new Vector2(r.X + 24 * u, y + 10 * u), 20 * u, r.Width - 48 * u, Theme.Aspal);
            return;
        }
        string[] heads = [S.T("lb.rank"), S.T("lb.name"), S.T("lb.kills"), S.T("lb.deaths"), S.T("lb.kd"), S.T("lb.wins"), S.T("lb.matches"), S.T("lb.captures"), S.T("lb.streak"), S.T("lb.time")];
        float[] cols = [0.03f, 0.08f, 0.40f, 0.48f, 0.56f, 0.64f, 0.72f, 0.80f, 0.90f, 0.985f];
        bool compact = r.Width < 900 * u;
        int shown = compact ? 7 : heads.Length;
        float Col(int i) => r.X + r.Width * (compact ? new[] { 0.04f, 0.11f, 0.56f, 0.68f, 0.80f, 0.90f, 0.985f }[i] : cols[i]);
        string[] shownHeads = compact ? [heads[0], heads[1], heads[2], heads[3], heads[4], heads[5], heads[9]] : heads;
        for (int i = 0; i < shown; i++)
        {
            var pos = new Vector2(Col(i), y);
            if (i <= 1) Theme.Text(Theme.Mono, shownHeads[i], pos, 14 * u, Theme.Fade(Theme.Aspal, 0.7f));
            else Theme.TextRight(Theme.Mono, shownHeads[i], pos, 14 * u, Theme.Fade(Theme.Aspal, 0.7f));
        }
        y += 26 * u;
        foreach (var row in rows)
        {
            if (y > r.Y + r.Height - 34 * u) break;
            var band = new Rectangle(r.X + 10 * u, y - 4 * u, r.Width - 20 * u, 32 * u);
            if (row.You) DrawRectangleRec(band, Theme.Kuning);
            float kd = row.Deaths == 0 ? row.Kills : (float)row.Kills / row.Deaths;
            string[] vals = compact
                ? [row.Rank.ToString(Inv), row.Name, row.Kills.ToString(Inv), row.Deaths.ToString(Inv), kd.ToString("0.00", Inv), row.Wins.ToString(Inv), row.Minutes.ToString(Inv)]
                : [row.Rank.ToString(Inv), row.Name, row.Kills.ToString(Inv), row.Deaths.ToString(Inv), kd.ToString("0.00", Inv), row.Wins.ToString(Inv), row.Matches.ToString(Inv), row.Captures.ToString(Inv), row.Streak.ToString(Inv), row.Minutes.ToString(Inv)];
            for (int i = 0; i < vals.Length; i++)
            {
                var pos = new Vector2(Col(i), y);
                if (i == 0) Theme.Text(Theme.Display, vals[i], pos, 20 * u, Theme.Cabe);
                else if (i == 1) Theme.Text(Theme.BodyBold, vals[i], pos, 20 * u, Theme.Aspal);
                else Theme.TextRight(Theme.Mono, vals[i], pos, 18 * u, Theme.Aspal);
            }
            y += 34 * u;
        }
    }
}
