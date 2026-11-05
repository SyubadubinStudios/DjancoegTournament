#if STEAM
using Steamworks;
using Steamworks.Data;

namespace Djancoeg.Client.Platform;

/// <summary>
/// Steamworks integration (build with <c>dotnet publish src/Djancoeg.Client -r win-x64 -p:Steam=true</c>).
/// The app id comes from <c>steam_appid.txt</c> next to the game (Steam writes it for dev builds) or the
/// DJANCOEG_STEAM_APPID environment variable. Without Steam running, the game falls back to <see cref="NullPlatform"/>.
/// </summary>
public sealed class SteamPlatform : IPlatformServices
{
    public string Name => "steam";

    private SteamPlatform() { }

    public static bool TryCreate(out IPlatformServices platform)
    {
        platform = new NullPlatform();
        uint appId = ReadAppId();
        if (appId == 0) return false;
        try
        {
            SteamClient.Init(appId, asyncCallbacks: false);
            if (!SteamClient.IsValid) return false;
            platform = new SteamPlatform();
            Console.WriteLine($"[steam] signed in as {SteamClient.Name} (app {appId})");
            return true;
        }
        catch (Exception ex) // Steam not running, wrong app id, missing steam_api64.dll...
        {
            Console.WriteLine("[steam] not available: " + ex.Message);
            return false;
        }
    }

    private static uint ReadAppId()
    {
        if (uint.TryParse(Environment.GetEnvironmentVariable("DJANCOEG_STEAM_APPID"), out uint env)) return env;
        var file = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
        return File.Exists(file) && uint.TryParse(File.ReadAllText(file).Trim(), out uint id) ? id : 0;
    }

    public void UnlockAchievement(string localId)
    {
        try { new Achievement(PlatformServices.SteamName(localId)).Trigger(); }
        catch (Exception ex) { Console.WriteLine("[steam] achievement failed: " + ex.Message); }
    }

    public void SetPresence(string status)
    {
        try { SteamFriends.SetRichPresence("status", status); }
        catch (Exception ex) { Console.WriteLine("[steam] presence failed: " + ex.Message); }
    }

    public void Update() => SteamClient.RunCallbacks();

    public void Dispose()
    {
        if (SteamClient.IsValid) SteamClient.Shutdown();
    }
}
#endif
