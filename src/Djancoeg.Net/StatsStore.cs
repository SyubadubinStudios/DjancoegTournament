using System.Text.Json;
using System.Text.Json.Serialization;

namespace Djancoeg.Net;

/// <summary>Career statistics for one player, keyed by the client's random player key (no accounts needed).</summary>
public sealed class PlayerStats
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Wins { get; set; }
    public int Matches { get; set; }
    public int Captures { get; set; }
    public int RaceGoals { get; set; }
    public int BestStreak { get; set; }
    public double PlaySeconds { get; set; }
    public DateTime LastSeen { get; set; }

    [JsonIgnore] public float KillDeathRatio => Deaths == 0 ? Kills : (float)Kills / Deaths;
}

public sealed class StatsFile
{
    public int Version { get; set; } = 1;
    public List<PlayerStats> Players { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatsFile))]
internal sealed partial class StatsJsonContext : JsonSerializerContext;

/// <summary>
/// Persistent player statistics and leaderboard (JSON file). Thread-safe; writes are atomic (temp file + move).
/// </summary>
public sealed class StatsStore
{
    private readonly Dictionary<string, PlayerStats> _byKey = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private bool _dirty;

    public string? Path { get; }

    public StatsStore(string? path)
    {
        Path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            using var fs = File.OpenRead(path);
            var file = JsonSerializer.Deserialize(fs, StatsJsonContext.Default.StatsFile);
            if (file is not null) foreach (var p in file.Players) if (p.Key.Length > 0) _byKey[p.Key] = p;
        }
        catch (JsonException) { }
        catch (IOException) { }
    }

    public int Count { get { lock (_gate) return _byKey.Count; } }

    /// <summary>Gets (or creates) the record for a player and refreshes their display name.</summary>
    public void Touch(string key, string name)
    {
        if (key.Length == 0) return;
        lock (_gate)
        {
            if (!_byKey.TryGetValue(key, out var p)) _byKey[key] = p = new PlayerStats { Key = key };
            p.Name = name;
            p.LastSeen = DateTime.UtcNow;
            _dirty = true;
        }
    }

    public void Update(string key, Action<PlayerStats> change)
    {
        if (key.Length == 0) return;
        lock (_gate)
        {
            if (!_byKey.TryGetValue(key, out var p)) return;
            change(p);
            _dirty = true;
        }
    }

    public PlayerStats? Get(string key)
    {
        lock (_gate) return _byKey.TryGetValue(key, out var p) ? p : null;
    }

    /// <summary>Top players by kills, then K/D, then wins.</summary>
    public List<PlayerStats> Top(int count)
    {
        lock (_gate)
            return _byKey.Values.OrderByDescending(p => p.Kills).ThenByDescending(p => p.KillDeathRatio).ThenByDescending(p => p.Wins).Take(count).ToList();
    }

    public int RankOf(string key)
    {
        lock (_gate)
        {
            if (!_byKey.TryGetValue(key, out var me)) return 0;
            return 1 + _byKey.Values.Count(p => p.Kills > me.Kills || (p.Kills == me.Kills && p.KillDeathRatio > me.KillDeathRatio));
        }
    }

    public void Save()
    {
        if (Path is null) return;
        StatsFile file;
        lock (_gate)
        {
            if (!_dirty) return;
            file = new StatsFile { Players = [.. _byKey.Values] };
            _dirty = false;
        }
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            string tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, StatsJsonContext.Default.StatsFile));
            File.Move(tmp, Path, overwrite: true);
        }
        catch (IOException) { _dirty = true; }
        catch (UnauthorizedAccessException) { _dirty = true; }
    }
}
