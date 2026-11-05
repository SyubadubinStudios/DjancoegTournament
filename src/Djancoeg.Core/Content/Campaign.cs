using Djancoeg.Core.Localization;

namespace Djancoeg.Core.Content;

/// <summary>One stage of the single-player "Tur Nusantara" campaign.</summary>
public sealed record CampaignStage
{
    public required string Id { get; init; }
    public required LocalizedText Name { get; init; }
    public required LocalizedText Briefing { get; init; }
    public required string ArenaId { get; init; }
    public GameModeKind Mode { get; init; } = GameModeKind.FreeForAll;
    public int Bots { get; init; } = 4;
    public float BotSkill { get; init; } = 0.4f;
    public int ScoreLimit { get; init; } = 10;
    public int TimeLimitSeconds { get; init; } = 300;
    public bool Creeps { get; init; } = true;
    /// <summary>Creep id of the stage boss (empty = no boss). Killing blow on the boss is worth <see cref="Campaign.BossReward"/> points.</summary>
    public string Boss { get; init; } = "";
    public float BossHealthScale { get; init; } = 6f;
    /// <summary>Two stars: win with at most this many deaths.</summary>
    public int MaxDeathsForTwoStars { get; init; } = 3;
    /// <summary>Three stars: also finish within this many seconds.</summary>
    public int ParSeconds { get; init; } = 180;

    public bool IsBoss => Boss.Length > 0;

    public MatchSettings ToMatch() => new()
    {
        ArenaId = ArenaId, Mode = Mode, BotCount = Bots, BotSkill = BotSkill,
        ScoreLimit = IsBoss ? 999 : ScoreLimit, TimeLimitSeconds = TimeLimitSeconds,
        ServerName = "Tur Nusantara", Creeps = Creeps && !IsBoss, CreepIntervalMin = 20, CreepIntervalMax = 40,
        BossCreepId = Boss, BossHealthScale = BossHealthScale,
    };

    /// <summary>0 = lost; 1 = won; 2 = won with few deaths; 3 = also under par time.</summary>
    public int Stars(bool won, int deaths, float seconds)
    {
        if (!won) return 0;
        if (deaths > MaxDeathsForTwoStars) return 1;
        return seconds <= ParSeconds ? 3 : 2;
    }
}

public sealed record CampaignIsland
{
    public required string Id { get; init; }
    public required LocalizedText Name { get; init; }
    public required LocalizedText Blurb { get; init; }
    /// <summary>Position on the map image, normalised 0..1.</summary>
    public required float MapX { get; init; }
    public required float MapY { get; init; }
    public required string Color { get; init; }
    /// <summary>Total stars needed to open this island.</summary>
    public int StarsToUnlock { get; init; }
    public required IReadOnlyList<CampaignStage> Stages { get; init; }
}

/// <summary>The built-in island tour: four islands, four stages each, a boss at the end of every island.</summary>
public static class Campaign
{
    public const int BossReward = 10;
    private static LocalizedText T(string en, string id) => new(en, id);

    public static readonly IReadOnlyList<CampaignIsland> Islands =
    [
        new()
        {
            Id = "jawa", Name = T("Java", "Jawa"), MapX = 0.45f, MapY = 0.76f, Color = "#FFC93C", StarsToUnlock = 0,
            Blurb = T("Where it all started: night markets, bus terminals and a gorilla nobody invited.", "Tempat semua bermula: pasar malam, terminal bus, dan gorila yang tak diundang."),
            Stages =
            [
                new() { Id = "jawa-1", ArenaId = "pasar-malam", Bots = 3, BotSkill = 0.25f, ScoreLimit = 8, ParSeconds = 150,
                    Name = T("Opening Night", "Malam Pembukaan"), Briefing = T("First to 8 knockouts. The satay seller is watching.", "Siapa duluan 8 KO. Abang sate menonton.") },
                new() { Id = "jawa-2", ArenaId = "terminal-bus", Mode = GameModeKind.TeamDeathmatch, Bots = 5, BotSkill = 0.3f, ScoreLimit = 20, ParSeconds = 200,
                    Name = T("Last Bus Home", "Bus Terakhir"), Briefing = T("Team up and win the terminal 20 knockouts to the other side.", "Kompak satu tim, menangkan terminal 20 KO.") },
                new() { Id = "jawa-3", ArenaId = "kantor-kelurahan", Mode = GameModeKind.KingOfTheHill, Bots = 4, BotSkill = 0.35f, ScoreLimit = 60, ParSeconds = 210,
                    Name = T("Queue Number 99", "Antrean Nomor 99"), Briefing = T("Hold the service counter. Nobody leaves without a stamp.", "Kuasai loket. Tak ada yang pulang tanpa cap.") },
                new() { Id = "jawa-boss", ArenaId = "pasar-malam", Bots = 2, BotSkill = 0.3f, Boss = "gorila-albino", BossHealthScale = 5f, TimeLimitSeconds = 360, ParSeconds = 150,
                    Name = T("BOSS: The Albino Gorilla", "BOS: Gorila Albino"), Briefing = T("It escaped the night-market prize booth. Land the killing blow for +10.", "Kabur dari stan hadiah pasar malam. Pukulan terakhir +10 poin.") },
            ],
        },
        new()
        {
            Id = "sumatera", Name = T("Sumatra", "Sumatera"), MapX = 0.23f, MapY = 0.45f, Color = "#FF7043", StarsToUnlock = 6,
            Blurb = T("Rice terraces, harbour cranes and a one-eyed strongman.", "Sawah berundak, derek pelabuhan, dan si mata satu yang kekar."),
            Stages =
            [
                new() { Id = "sumatera-1", ArenaId = "sawah-ronda", Bots = 4, BotSkill = 0.4f, ScoreLimit = 10, ParSeconds = 180,
                    Name = T("Night Watch", "Ronda Malam"), Briefing = T("10 knockouts before the kentongan sounds.", "10 KO sebelum kentongan berbunyi.") },
                new() { Id = "sumatera-2", ArenaId = "pelabuhan", Mode = GameModeKind.CaptureTheFlag, Bots = 5, BotSkill = 0.4f, ScoreLimit = 3, ParSeconds = 240,
                    Name = T("Gorengan Smugglers", "Penyelundup Gorengan"), Briefing = T("Steal their gorengan three times. Keep yours warm.", "Curi gorengan mereka tiga kali. Jaga punyamu tetap hangat.") },
                new() { Id = "sumatera-3", ArenaId = "atap-kampung", Mode = GameModeKind.Race, Bots = 4, BotSkill = 0.45f, ScoreLimit = 3, ParSeconds = 150,
                    Name = T("Rooftop Dash", "Lari Atap"), Briefing = T("Reach the flag on the water tower three times first.", "Sampai di bendera menara air tiga kali duluan.") },
                new() { Id = "sumatera-boss", ArenaId = "sawah-ronda", Bots = 3, BotSkill = 0.4f, Boss = "cyclops-kekar", BossHealthScale = 5f, TimeLimitSeconds = 360, ParSeconds = 160,
                    Name = T("BOSS: Cyclops of the Paddies", "BOS: Cyclops Sawah"), Briefing = T("He came for the harvest. Send him home.", "Dia datang mau panen. Pulangkan dia.") },
            ],
        },
        new()
        {
            Id = "kalimantan", Name = T("Borneo", "Kalimantan"), MapX = 0.54f, MapY = 0.34f, Color = "#66BB6A", StarsToUnlock = 14,
            Blurb = T("Old railways, long platforms and a head that floats where it likes.", "Rel tua, peron panjang, dan kepala yang melayang sesukanya."),
            Stages =
            [
                new() { Id = "kalimantan-1", ArenaId = "stasiun-kereta", Bots = 5, BotSkill = 0.5f, ScoreLimit = 12, ParSeconds = 200,
                    Name = T("Platform Two", "Peron Dua"), Briefing = T("12 knockouts. Mind the gap.", "12 KO. Hati-hati celah peron.") },
                new() { Id = "kalimantan-2", ArenaId = "terminal-bus", Mode = GameModeKind.KingOfTheHill, Bots = 6, BotSkill = 0.5f, ScoreLimit = 80, ParSeconds = 240,
                    Name = T("Ticket Counter War", "Perang Loket Tiket"), Briefing = T("Hold the ticket booth longer than anyone.", "Kuasai loket tiket paling lama.") },
                new() { Id = "kalimantan-3", ArenaId = "pelabuhan", Mode = GameModeKind.TeamDeathmatch, Bots = 7, BotSkill = 0.55f, ScoreLimit = 30, ParSeconds = 260,
                    Name = T("Dockside Riot", "Ribut di Dermaga"), Briefing = T("30 team knockouts on the slippery docks.", "30 KO tim di dermaga licin.") },
                new() { Id = "kalimantan-boss", ArenaId = "stasiun-kereta", Bots = 3, BotSkill = 0.5f, Boss = "kepala-melayang", BossHealthScale = 6f, TimeLimitSeconds = 360, ParSeconds = 170,
                    Name = T("BOSS: The Giant Floating Head", "BOS: Kepala Raksasa Melayang"), Briefing = T("It screams at the trains. Make it stop.", "Dia teriak ke kereta. Hentikan.") },
            ],
        },
        new()
        {
            Id = "sulawesi", Name = T("Sulawesi", "Sulawesi"), MapX = 0.73f, MapY = 0.47f, Color = "#4FC3F7", StarsToUnlock = 24,
            Blurb = T("The final leg. Everything at once, then the green giant himself.", "Etape terakhir. Semua sekaligus, lalu sang raksasa hijau."),
            Stages =
            [
                new() { Id = "sulawesi-1", ArenaId = "atap-kampung", Bots = 6, BotSkill = 0.6f, ScoreLimit = 15, ParSeconds = 220,
                    Name = T("Clothesline Chaos", "Kacau di Jemuran"), Briefing = T("15 knockouts across the rooftops.", "15 KO di atas genteng.") },
                new() { Id = "sulawesi-2", ArenaId = "kantor-kelurahan", Mode = GameModeKind.CaptureTheFlag, Bots = 7, BotSkill = 0.6f, ScoreLimit = 3, ParSeconds = 280,
                    Name = T("Stolen Stamp", "Stempel Dicuri"), Briefing = T("Three captures. The village head wants his gorengan back.", "Tiga kali rebut. Pak Lurah mau gorengannya kembali.") },
                new() { Id = "sulawesi-3", ArenaId = "pasar-malam", Mode = GameModeKind.Race, Bots = 6, BotSkill = 0.65f, ScoreLimit = 4, ParSeconds = 200,
                    Name = T("Ferris Wheel Sprint", "Sprint Bianglala"), Briefing = T("Four laps to the goal. Creeps everywhere.", "Empat kali ke garis. Makhluk di mana-mana.") },
                new() { Id = "sulawesi-boss", ArenaId = "pelabuhan", Bots = 4, BotSkill = 0.6f, Boss = "buto-ijo", BossHealthScale = 6f, TimeLimitSeconds = 420, ParSeconds = 200,
                    Name = T("BOSS: Buto Ijo", "BOS: Buto Ijo"), Briefing = T("He walked out of the sea. Send him back.", "Dia keluar dari laut. Kembalikan ke laut.") },
            ],
        },
        new()
        {
            Id = "nusa-tenggara", Name = T("Nusa Tenggara", "Nusa Tenggara"), MapX = 0.70f, MapY = 0.83f, Color = "#BA68C8", StarsToUnlock = 34,
            Blurb = T("The bonus tour east: dry savanna winds, a lanky stalker and something huge under the ground.", "Tur bonus ke timur: angin sabana, si jangkung penguntit, dan sesuatu yang besar di bawah tanah."),
            Stages =
            [
                new() { Id = "nusa-1", ArenaId = "sawah-ronda", Mode = GameModeKind.KingOfTheHill, Bots = 7, BotSkill = 0.65f, ScoreLimit = 90, ParSeconds = 260,
                    Name = T("Savanna Watch Post", "Pos Ronda Sabana"), Briefing = T("Hold the watch post against seven hungry rivals.", "Kuasai pos ronda melawan tujuh rival yang lapar.") },
                new() { Id = "nusa-2", ArenaId = "kantor-kelurahan", Bots = 7, BotSkill = 0.7f, ScoreLimit = 18, ParSeconds = 240,
                    Name = T("Office Hours Brawl", "Ribut Jam Kantor"), Briefing = T("18 knockouts. Smash the counters if they get in the way.", "18 KO. Hancurkan loket kalau menghalangi.") },
                new() { Id = "nusa-boss-1", ArenaId = "terminal-bus", Bots = 4, BotSkill = 0.65f, Boss = "si-jangkung", BossHealthScale = 8f, TimeLimitSeconds = 420, ParSeconds = 180,
                    Name = T("BOSS: The Lanky One", "BOS: Si Jangkung"), Briefing = T("It waits at the last bus stop. Too tall, too fast.", "Ia menunggu di halte terakhir. Terlalu tinggi, terlalu cepat.") },
                new() { Id = "nusa-boss-2", ArenaId = "pasar-malam", Bots = 5, BotSkill = 0.7f, Boss = "cacing-raksasa", BossHealthScale = 5f, TimeLimitSeconds = 480, ParSeconds = 240,
                    Name = T("FINAL BOSS: Giant Earthworm", "BOS TERAKHIR: Cacing Tanah Raksasa"), Briefing = T("Feel the rumble, jump away, shoot when it rises. Win the tournament.", "Rasakan getarannya, lompat menjauh, tembak saat ia muncul. Menangkan turnamen.") },
            ],
        },
    ];

    public static IEnumerable<CampaignStage> AllStages => Islands.SelectMany(i => i.Stages);

    public static CampaignStage? Stage(string id) => AllStages.FirstOrDefault(s => s.Id == id);

    public static int MaxStars => AllStages.Count() * 3;

    /// <summary>An island is open once enough stars are collected; a stage is open once the previous one has a star.</summary>
    public static bool IsIslandUnlocked(CampaignIsland island, int totalStars) => totalStars >= island.StarsToUnlock;

    public static bool IsStageUnlocked(CampaignIsland island, int stageIndex, Func<string, int> starsOf, int totalStars)
    {
        if (!IsIslandUnlocked(island, totalStars)) return false;
        return stageIndex == 0 || starsOf(island.Stages[stageIndex - 1].Id) > 0;
    }
}
