using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Content;
using Djancoeg.Core.Localization;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client;

/// <summary>Definition of one achievement. <see cref="Id"/> is stable and mapped 1:1 to Steam API names in steam/achievements.json.</summary>
public sealed record AchievementDef(string Id, LocalizedText Name, LocalizedText Description, int Goal = 1);

/// <summary>
/// Local achievements with toasts. Progress lives in AppData/achievements.json; a storefront layer (Steam) can mirror
/// unlocks by id. Counters are only written when they change, and saving is batched to match/menu boundaries.
/// </summary>
public sealed class Achievements
{
    private static LocalizedText T(string en, string id) => new(en, id);

    public static readonly IReadOnlyList<AchievementDef> All =
    [
        new("FIRST_BLOOD", T("First Blood", "Darah Pertama"), T("Knock out your first opponent.", "KO lawan pertamamu.")),
        new("KO_100", T("Neighbourhood Legend", "Legenda Kampung"), T("Knock out 100 opponents.", "KO 100 lawan."), 100),
        new("STREAK_5", T("On a Roll", "Lagi Panas"), T("Five knockouts without dying.", "Lima KO tanpa mati."), 5),
        new("CREEP_FIRST", T("Pest Control", "Pembasmi Hama"), T("Kill an Arena Creep.", "Bunuh satu Makhluk Arena.")),
        new("CREEP_50", T("Cryptid Hunter", "Pemburu Makhluk"), T("Kill 50 Arena Creeps.", "Bunuh 50 Makhluk Arena."), 50),
        new("WRECKER", T("Property Damage", "Perusak Fasilitas"), T("Break 25 stalls, benches or counters.", "Hancurkan 25 lapak, bangku, atau loket."), 25),
        new("BOSS_FIRST", T("Giant Slayer", "Penakluk Raksasa"), T("Win a boss stage.", "Menangkan satu tahap bos.")),
        new("ISLAND_JAWA", T("Java Conquered", "Jawa Takluk"), T("Win every stage on Java.", "Menangkan semua tahap di Jawa.")),
        new("ISLAND_ALL", T("Nusantara Champion", "Juara Nusantara"), T("Win every stage of Tur Nusantara.", "Menangkan semua tahap Tur Nusantara.")),
        new("THREE_STARS", T("Perfectionist", "Perfeksionis"), T("Get three stars on any stage.", "Dapat tiga bintang di tahap mana pun.")),
        new("ALL_STARS", T("All the Stars", "Semua Bintang"), T("Collect every star in the campaign.", "Kumpulkan semua bintang kampanye.")),
        new("EDITOR", T("Architect", "Arsitek"), T("Save an arena in the level editor.", "Simpan arena di editor level.")),
    ];

    public sealed class Data
    {
        public Dictionary<string, int> Progress { get; set; } = [];
        public Dictionary<string, DateTime> Unlocked { get; set; } = [];
    }

    private readonly string _path;
    private readonly Data _data;
    private readonly Queue<AchievementDef> _toasts = new();
    private AchievementDef? _current;
    private float _toastTime;
    private bool _dirty;
    private int _streak;

    public Achievements(string? path = null)
    {
        _path = path ?? Path.Combine(ClientSettings.Folder, "achievements.json");
        _data = Load(_path);
    }

    public bool IsUnlocked(string id) => _data.Unlocked.ContainsKey(id);
    public int ProgressOf(string id) => _data.Progress.TryGetValue(id, out int v) ? v : 0;
    public int UnlockedCount => _data.Unlocked.Count;

    /// <summary>Adds to a counter and unlocks when the goal is reached.</summary>
    public void Add(string id, int amount = 1)
    {
        var def = All.FirstOrDefault(a => a.Id == id);
        if (def is null || IsUnlocked(id)) return;
        int v = ProgressOf(id) + amount;
        _data.Progress[id] = v;
        _dirty = true;
        if (v >= def.Goal) Unlock(id);
    }

    /// <summary>Sets a counter to at least <paramref name="value"/> (for "best" style goals like streaks).</summary>
    public void AtLeast(string id, int value)
    {
        if (value > ProgressOf(id)) Add(id, value - ProgressOf(id));
    }

    public void Unlock(string id)
    {
        var def = All.FirstOrDefault(a => a.Id == id);
        if (def is null || IsUnlocked(id)) return;
        _data.Unlocked[id] = DateTime.UtcNow;
        _data.Progress[id] = def.Goal;
        _toasts.Enqueue(def);
        _dirty = true;
        Save();
    }

    // ------------------------------------------------------------------ game hooks

    public void OnLocalKill() { Add("FIRST_BLOOD"); Add("KO_100"); _streak++; AtLeast("STREAK_5", _streak); }
    public void OnLocalDeath() => _streak = 0;
    public void OnLocalCreepKill() { Add("CREEP_FIRST"); Add("CREEP_50"); }
    public void OnBoxBroken() => Add("WRECKER");
    public void OnEditorSaved() => Unlock("EDITOR");

    public void OnCampaignResult(CampaignStage stage, int stars, CampaignProgress progress, int starsBefore)
    {
        if (stars <= 0) return;
        if (stage.IsBoss) Unlock("BOSS_FIRST");
        if (stars >= 3) Unlock("THREE_STARS");
        var jawa = Campaign.Islands[0];
        if (jawa.Stages.All(s => progress.StarsOf(s.Id) > 0)) Unlock("ISLAND_JAWA");
        if (Campaign.AllStages.All(s => progress.StarsOf(s.Id) > 0)) Unlock("ISLAND_ALL");
        if (progress.TotalStars >= Campaign.MaxStars) Unlock("ALL_STARS");
    }

    // ------------------------------------------------------------------ persistence

    private static Data Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize(File.ReadAllText(path), AchievementJsonContext.Default.Data) ?? new Data();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new Data();
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, AchievementJsonContext.Default.Data));
            _dirty = false;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------ toast

    /// <summary>Draws the current unlock toast (top-right, slides in, holds, slides out).</summary>
    public void DrawToast(float dt, App app)
    {
        if (_current is null)
        {
            if (!_toasts.TryDequeue(out _current)) return;
            _toastTime = 0;
            app.Sounds.Play(Audio.Sfx.Pickup, 1f, 1.3f);
        }
        _toastTime += dt;
        const float hold = 4.2f;
        if (_toastTime > hold) { _current = null; return; }
        int sw = GetScreenWidth();
        float u = GetScreenHeight() / 900f;
        float slide = MathF.Min(1, MathF.Min(_toastTime, hold - _toastTime) * 5f);
        float w = 440 * u, h = 92 * u;
        var r = new Rectangle(sw - (w + 30 * u) * slide, 30 * u, w, h);
        DrawRectangleRec(new Rectangle(r.X + 6 * u, r.Y + 6 * u, w, h), Theme.Aspal);
        DrawRectangleRec(r, Theme.Kuning);
        DrawRectangleRec(new Rectangle(r.X, r.Y, 12 * u, h), Theme.Cabe);
        Theme.Text(Theme.Mono, S.T("ach.unlocked").ToUpperInvariant(), new Vector2(r.X + 28 * u, r.Y + 12 * u), 15 * u, Theme.Aspal);
        Theme.Text(Theme.Display, S.T(_current.Name).ToUpperInvariant(), new Vector2(r.X + 28 * u, r.Y + 32 * u), 28 * u, Theme.Aspal);
        Theme.Text(Theme.Body, S.T(_current.Description), new Vector2(r.X + 28 * u, r.Y + 64 * u), 15 * u, Theme.Aspal);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Achievements.Data))]
internal sealed partial class AchievementJsonContext : JsonSerializerContext;
