using System.Numerics;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Simulation;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Game;

/// <summary>Campaign extras: boss health bar and the stage title banner.</summary>
public sealed partial class GameView
{
    /// <summary>Creep kind index of the stage boss, or -1.</summary>
    public int BossKind { get; set; } = -1;
    /// <summary>Called with the creep kind whenever a creep dies.</summary>
    public Action<int>? CreepKilledHook { get; set; }
    /// <summary>Stage title shown for the first seconds of the match.</summary>
    public string Banner { get; set; } = "";

    private void DrawBossBar(int sw, float u)
    {
        if (BossKind < 0 || BossKind >= _session.Content.Creeps.Count) return;
        int idx = -1;
        for (int i = 0; i < _creeps.Length; i++)
            if (_creeps[i].Seen && _creeps[i].Kind == BossKind && _creeps[i].State != CreepState.Dying) { idx = i; break; }
        if (idx < 0) return;
        var def = _session.Content.Creeps[BossKind];
        float w = MathF.Min(760 * u, sw - 80 * u), h = 22 * u;
        var r = new Rectangle(sw / 2f - w / 2, 150 * u, w, h);
        Theme.TextCentered(Theme.Display, S.T(def.Name).ToUpperInvariant(), new Vector2(sw / 2f, r.Y - 18 * u), 26 * u, Theme.Kapur);
        DrawRectangleRec(new Rectangle(r.X + 5 * u, r.Y + 5 * u, w, h), Theme.Aspal);
        DrawRectangleRec(r, Theme.Fade(Theme.Aspal, 0.8f));
        DrawRectangleRec(new Rectangle(r.X, r.Y, w * _creeps[idx].HealthPct, h), Theme.Cabe);
        DrawRectangleLinesEx(r, 2 * u, Theme.Kuning);
    }

    private void DrawBanner(int sw, int sh, float u)
    {
        if (Banner.Length == 0 || _time > 5f) return;
        float a = MathF.Min(1, MathF.Min(_time * 3f, (5f - _time) * 2f));
        var m = Theme.Measure(Theme.Display, Banner.ToUpperInvariant(), 52 * u);
        var r = new Rectangle(sw / 2f - m.X / 2 - 30 * u, sh * 0.28f, m.X + 60 * u, 80 * u);
        DrawRectangleRec(new Rectangle(r.X + 8 * u, r.Y + 8 * u, r.Width, r.Height), Theme.Fade(Theme.Aspal, a));
        DrawRectangleRec(r, Theme.Fade(Theme.Kuning, a));
        Theme.TextCentered(Theme.Display, Banner.ToUpperInvariant(), new Vector2(sw / 2f, r.Y + r.Height / 2), 52 * u, Theme.Fade(Theme.Aspal, a));
    }
}
