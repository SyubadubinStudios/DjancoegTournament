using System.Text.Json;
using System.Text.Json.Serialization;
using Djancoeg.Core.Content;

namespace Djancoeg.Client;

/// <summary>Tur Nusantara progress (best stars and times per stage), saved next to the settings in AppData.</summary>
public sealed class CampaignProgress
{
    public Dictionary<string, int> Stars { get; set; } = [];
    public Dictionary<string, float> BestSeconds { get; set; } = [];
    public string LastIsland { get; set; } = "jawa";

    [JsonIgnore] public static string FilePath => Path.Combine(ClientSettings.Folder, "campaign.json");

    public int StarsOf(string stageId) => Stars.TryGetValue(stageId, out int s) ? s : 0;
    [JsonIgnore] public int TotalStars => Stars.Values.Sum();

    /// <summary>Records a result; keeps the best stars and time. Returns true if it improved anything.</summary>
    public bool Record(CampaignStage stage, int stars, float seconds)
    {
        bool improved = false;
        if (stars > StarsOf(stage.Id)) { Stars[stage.Id] = stars; improved = true; }
        if (stars > 0 && (!BestSeconds.TryGetValue(stage.Id, out float best) || seconds < best)) { BestSeconds[stage.Id] = seconds; improved = true; }
        return improved;
    }

    public static CampaignProgress Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize(File.ReadAllText(path), CampaignJsonContext.Default.CampaignProgress) ?? new CampaignProgress();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new CampaignProgress();
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, CampaignJsonContext.Default.CampaignProgress));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CampaignProgress))]
internal sealed partial class CampaignJsonContext : JsonSerializerContext;
