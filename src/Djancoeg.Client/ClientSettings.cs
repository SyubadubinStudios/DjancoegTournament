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

    // Controls (action -> binding code, see Controls.cs).
    public Dictionary<string, int> Bindings { get; set; } = new(Controls.Defaults);
    public float GamepadLookSpeed { get; set; } = 1.0f;

    // Graphics.
    public bool Msaa { get; set; } = true;
    public float RenderScale { get; set; } = 1.0f;
    /// <summary>0 = low (4 lights, fewer particles), 1 = medium, 2 = high (16 lights).</summary>
    public int Quality { get; set; } = 2;
    public bool BlobShadows { get; set; } = true;

    // Accessibility.
    public bool ColorBlindTeams { get; set; }
    public bool Subtitles { get; set; } = true;
    public bool ReduceShake { get; set; }
    public bool ReduceFlashes { get; set; }

    public bool SpectatePractice { get; set; }

    /// <summary>Arena Creeps in practice and hosted games.</summary>
    public bool Creeps { get; set; } = true;
    /// <summary>Blood and gore effects (off = confetti).</summary>
    public bool Blood { get; set; } = true;

    // Online.
    /// <summary>Random key that lets servers keep your career stats without an account.</summary>
    public string PlayerKey { get; set; } = "";
    public string WorkshopUrl { get; set; } = "http://127.0.0.1:27951";
    /// <summary>Upload token for the Workshop server (from <c>djancoeg-server workshop token</c>).</summary>
    public string WorkshopToken { get; set; } = "";
    /// <summary>Narrated story stills (Indonesian voice).</summary>
    public bool StoryVoice { get; set; } = true;
    public string HostPassword { get; set; } = "";

    [JsonIgnore] public static string ServerDataFolder => Path.Combine(Folder, "server-data");

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
