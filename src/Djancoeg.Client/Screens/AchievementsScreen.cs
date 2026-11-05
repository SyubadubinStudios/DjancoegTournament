using System.Globalization;
using System.Numerics;
using Djancoeg.Client.Ui;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>Grid of achievement cards with progress.</summary>
public sealed class AchievementsScreen(App app, Screen back) : Screen(app)
{
    public override void Update(float dt)
    {
        if (Gui.Back) App.Go(back);
    }

    public override void Draw()
    {
        App.DrawBackdrop();
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        float u = U;
        var ach = App.Achievements;
        Theme.SignText(S.T("ach.title").ToUpperInvariant(), new Vector2(60 * u, 40 * u), 64 * u, Theme.Kuning, doubleShadow: true);
        Theme.TextRight(Theme.Display, $"{ach.UnlockedCount} / {Achievements.All.Count}", new Vector2(sw - 60 * u, 52 * u), 40 * u, Theme.Kapur);

        int cols = sw > 1500 * u ? 3 : 2;
        float gap = 20 * u, x0 = 60 * u, y0 = 150 * u;
        float cw = (sw - 2 * x0 - gap * (cols - 1)) / cols, ch = 96 * u;
        for (int i = 0; i < Achievements.All.Count; i++)
        {
            var a = Achievements.All[i];
            bool done = ach.IsUnlocked(a.Id);
            var r = new Rectangle(x0 + (i % cols) * (cw + gap), y0 + (i / cols) * (ch + gap), cw, ch);
            Gui.Panel(r, done ? Theme.Kuning : Theme.Fade(Theme.Kapur, 0.55f), 6 * u);
            CampaignScreen.DrawStar(new Vector2(r.X + 46 * u, r.Y + ch / 2), 28 * u, done ? Theme.Cabe : Theme.Fade(Theme.Aspal, 0.25f));
            Theme.Text(Theme.Display, S.T(a.Name).ToUpperInvariant(), new Vector2(r.X + 90 * u, r.Y + 12 * u), 26 * u, Theme.Aspal);
            Theme.Text(Theme.Body, S.T(a.Description), new Vector2(r.X + 90 * u, r.Y + 46 * u), 16 * u, Theme.Aspal);
            if (a.Goal > 1 && !done)
            {
                float p = Math.Clamp(ach.ProgressOf(a.Id) / (float)a.Goal, 0, 1);
                var bar = new Rectangle(r.X + 90 * u, r.Y + 72 * u, r.Width - 190 * u, 10 * u);
                DrawRectangleRec(bar, Theme.Fade(Theme.Aspal, 0.25f));
                DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * p, bar.Height), Theme.Cabe);
                Theme.TextRight(Theme.Mono, ach.ProgressOf(a.Id).ToString(CultureInfo.InvariantCulture) + "/" + a.Goal.ToString(CultureInfo.InvariantCulture),
                    new Vector2(r.X + r.Width - 14 * u, r.Y + 66 * u), 15 * u, Theme.Aspal);
            }
        }
        if (Gui.Button(new Rectangle(sw - 260 * u, sh - 112 * u, 200 * u, 52 * u), S.T("common.back"), false, 22 * u)) App.Go(back);
        App.DrawFooter();
    }
}
