using System.Text.Json.Serialization;
using Djancoeg.Core.Content;
using Djancoeg.Core.Localization;

namespace Djancoeg.Modding;

/// <summary>
/// The <c>mod.json</c> file at the root of every mod folder. A mod can add or override
/// weapons, characters and arenas. Arenas may be inline or in separate files listed in <see cref="ArenaFiles"/>.
/// </summary>
public sealed record ModManifest
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string GameVersion { get; set; } = "1.0";
    public List<WeaponDef> Weapons { get; set; } = [];
    public List<CharacterDef> Characters { get; set; } = [];
    public List<ArenaDef> Arenas { get; set; } = [];
    public List<string> ArenaFiles { get; set; } = [];
}

/// <summary>Which mods the player has switched off (stored in <c>mods/settings.json</c>).</summary>
public sealed record ModSettings
{
    public List<string> Disabled { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ModManifest))]
[JsonSerializable(typeof(ModSettings))]
[JsonSerializable(typeof(ArenaDef))]
[JsonSerializable(typeof(MatchSettings))]
public sealed partial class ModJsonContext : JsonSerializerContext;
