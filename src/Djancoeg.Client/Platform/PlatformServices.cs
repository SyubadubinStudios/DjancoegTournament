namespace Djancoeg.Client.Platform;

/// <summary>
/// Storefront integration (achievements, rich presence). The default build has no storefront; building with
/// <c>-p:Steam=true</c> compiles <see cref="SteamPlatform"/> (Steamworks via Facepunch.Steamworks, Windows x64).
/// </summary>
public interface IPlatformServices : IDisposable
{
    string Name { get; }
    /// <summary>Unlocks a store achievement by local id (mapped to <c>ACH_&lt;id&gt;</c>, see distribution/steam/achievements.json).</summary>
    void UnlockAchievement(string localId);
    /// <summary>Short status shown to friends ("Tur Nusantara: Malam Pembukaan").</summary>
    void SetPresence(string status);
    /// <summary>Pumps callbacks; called once per frame.</summary>
    void Update();
}

public sealed class NullPlatform : IPlatformServices
{
    public string Name => "none";
    public void UnlockAchievement(string localId) { }
    public void SetPresence(string status) { }
    public void Update() { }
    public void Dispose() { }
}

public static class PlatformServices
{
    /// <summary>Steam API name for a local achievement id.</summary>
    public static string SteamName(string localId) => "ACH_" + localId;

    public static IPlatformServices Create(bool screenshotRun)
    {
#if STEAM
        if (!screenshotRun && SteamPlatform.TryCreate(out var steam)) return steam;
#endif
        return new NullPlatform();
    }
}
