using System.Text.Json;
using System.Text.Json.Serialization;
using Djancoeg.Core.Localization;

namespace Djancoeg.Client;

/// <summary>Player preferences, saved as JSON in the user's application-data folder.</summary>
public sealed class ClientSettings
{
    public string PlayerName { get; set; } = "Jagoan";
    public string CharacterId { get; set; } = "tukang-ojol";
    public Language Language { get; set; } = Language.Indonesian;
    public float MouseSensitivity { get; set; } = 1.0f;
    public bool InvertY { get; set; }
    public float FieldOfView { get; set; } = 90f;
    public float MasterVolume { get; set; } = 0.8f;
    public float SfxVolume { get; set; } = 0.9f;
    public bool ThirdPerson { get; set; }
    public bool Fullscreen { get; set; }
    public bool ShowFps { get; set; } = true;
    public string MasterServer { get; set; } = "127.0.0.1:27950";
    public string LastDirectConnect { get; set; } = "127.0.0.1:27960";

    // Last practice / host setup.
    public string ArenaId { get; set; } = "pasar-malam";
    public int Mode { get; set; }
    public int BotCount { get; set; } = 6;
    public float BotSkill { get; set; } = 0.5f;
    public int ScoreLimit { get; set; } = 20;
    public int TimeLimitMinutes { get; set; } = 10;
    public string ServerName { get; set; } = "Warung Tawuran";
    public int HostPort { get; set; } = 27960;

    [JsonIgnore] public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DjancoegTournament");
    [JsonIgnore] public static string FilePath => Path.Combine(Folder, "settings.json");

    public static ClientSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), ClientJsonContext.Default.ClientSettings) ?? new ClientSettings();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new ClientSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, ClientJsonContext.Default.ClientSettings));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ClientSettings))]
internal sealed partial class ClientJsonContext : JsonSerializerContext;
