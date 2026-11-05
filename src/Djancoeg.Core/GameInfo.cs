namespace Djancoeg.Core;

/// <summary>Static product information shown in the app, server banner and documentation.</summary>
public static class GameInfo
{
    public const string Name = "Djancoeg Tournament";
    public const string Tagline = "Ultimate Indonesian Chaos Battle Arena";
    public const string Version = "2.3.0";
    public const string Studio = "Syubadubin Studios";
    public const string Creator = "Ariana Mischa Fadhila";

    /// <summary>Credit line required by the brief (Bahasa Indonesia).</summary>
    public const string CreditId = "Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios";

    /// <summary>Credit line (English).</summary>
    public const string CreditEn = "Made by Ariana Mischa Fadhila of Syubadubin Studios";
}

/// <summary>Simulation-wide constants. Changing these changes the network protocol.</summary>
public static class SimConstants
{
    public const int TickRate = 60;
    public const float TickDelta = 1f / TickRate;
    public const int SnapshotEveryTicks = 2; // 30 Hz snapshots
    public const int MaxPlayers = 32;
    public const int MaxProjectiles = 256;
    public const int MaxProps = 64;
    public const int MaxPickups = 64;
    public const int MaxWeapons = 16;
    public const float RespawnDelay = 3f;
    public const float SpawnProtection = 1.5f;
    public const float PlayerHalfWidth = 0.4f;
    public const float PlayerHeight = 1.8f;
    public const float CrouchHeight = 1.2f;
    public const float EyeHeight = 1.62f;
    public const float CrouchEyeHeight = 1.05f;
    public const int MaxHealth = 100;
    public const int OverHealthCap = 200;
    public const int MaxArmor = 150;
}
