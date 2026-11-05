using System.Text.Json.Serialization;

namespace Djancoeg.Core.Localization;

public enum Language : byte { English = 0, Indonesian = 1 }

/// <summary>A pair of strings, one per supported language. Used by all content definitions.</summary>
public sealed record LocalizedText
{
    [JsonPropertyName("en")] public string En { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    public LocalizedText() { }
    public LocalizedText(string en, string id) { En = en; Id = id; }

    public string Get(Language lang) => lang == Language.Indonesian
        ? (string.IsNullOrEmpty(Id) ? En : Id)
        : (string.IsNullOrEmpty(En) ? Id : En);

    public static implicit operator LocalizedText(string both) => new(both, both);
    public override string ToString() => En;
}
