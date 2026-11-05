using System.Numerics;
using System.Text.Json.Serialization;
using Djancoeg.Core.Localization;

namespace Djancoeg.Core.Content;

[JsonConverter(typeof(JsonStringEnumConverter<WeaponKind>))]
public enum WeaponKind : byte { Melee, Hitscan, Projectile, Blast }

[JsonConverter(typeof(JsonStringEnumConverter<CharacterCategory>))]
public enum CharacterCategory : byte { Legend, Profession, Ghost, Animal, Meme }

/// <summary>Silhouette used by the procedural renderer when no 3D model is supplied.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BodyShape>))]
public enum BodyShape : byte { Humanoid, Shrouded, Ghostly, Tiny, Brute, Reptile, Cat }

[JsonConverter(typeof(JsonStringEnumConverter<AbilityKind>))]
public enum AbilityKind : byte { None, SuperJump, Dash, Shockwave, Heal, Invisibility, Haste, StoneSkin, Teleport, Levitate, Pull, Rally }

[JsonConverter(typeof(JsonStringEnumConverter<PickupKind>))]
public enum PickupKind : byte { HealthSmall, HealthMega, Armor, ArmorSmall, Weapon, Ammo, Quad }

[JsonConverter(typeof(JsonStringEnumConverter<SurfaceKind>))]
public enum SurfaceKind : byte { Ground, Wall, Wood, Metal, Fabric, Water, Grass, Tile, Neon, Roof }

/// <summary>A weapon. All behaviour is data driven so mods can add new weapons without code.</summary>
public sealed record WeaponDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    /// <summary>Kill-feed template. <c>{killer}</c> and <c>{victim}</c> are replaced.</summary>
    public LocalizedText KillVerb { get; set; } = new("{killer} fragged {victim}", "{killer} menghajar {victim}");
    public WeaponKind Kind { get; set; } = WeaponKind.Hitscan;
    public int Slot { get; set; } = 1;
    public float Damage { get; set; } = 10;
    public float FireInterval { get; set; } = 0.5f;
    public int Pellets { get; set; } = 1;
    public float SpreadDegrees { get; set; }
    public float Range { get; set; } = 200f;
    public float ProjectileSpeed { get; set; } = 30f;
    public float ProjectileGravity { get; set; }
    public float ProjectileLifetime { get; set; } = 5f;
    public bool ProjectileBounces { get; set; }
    public float SplashRadius { get; set; }
    public float SplashDamage { get; set; }
    public float Knockback { get; set; } = 2f;
    public float SelfDamageScale { get; set; } = 0.5f;
    public int MaxAmmo { get; set; } = 100;
    public int StartAmmo { get; set; } = 25;
    public int PickupAmmo { get; set; } = 10;
    public bool InfiniteAmmo { get; set; }
    public bool Piercing { get; set; }
    public bool StartsWith { get; set; }
    /// <summary>Half-angle of the cone for <see cref="WeaponKind.Blast"/> weapons.</summary>
    public float BlastConeDegrees { get; set; } = 35f;
    /// <summary>Hex color "#RRGGBB" used for tracers, projectiles and HUD accents.</summary>
    public string Color { get; set; } = "#FFFFFF";
    /// <summary>Procedural viewmodel key (racket, slingshot, pan, bamboo, bazooka, firecracker, sandal) or a .glb path.</summary>
    public string Model { get; set; } = "slingshot";
}

public sealed record AbilityDef
{
    public AbilityKind Kind { get; set; }
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public float Cooldown { get; set; } = 12f;
    public float Duration { get; set; }
    public float Power { get; set; } = 1f;
}

/// <summary>A playable fighter: Indonesian legends, professions, ghosts, animals and local memes.</summary>
public sealed record CharacterDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public CharacterCategory Category { get; set; }
    public LocalizedText Description { get; set; } = new();
    public int MaxHealth { get; set; } = 100;
    public float SpeedMultiplier { get; set; } = 1f;
    public float JumpMultiplier { get; set; } = 1f;
    public float Scale { get; set; } = 1f;
    public BodyShape Shape { get; set; } = BodyShape.Humanoid;
    public string PrimaryColor { get; set; } = "#C0392B";
    public string SecondaryColor { get; set; } = "#F1C40F";
    public string SkinColor { get; set; } = "#C68642";
    public AbilityDef Ability { get; set; } = new();
    public LocalizedText[] Taunts { get; set; } = [];
    /// <summary>Optional rigged glTF model path relative to the assets folder.</summary>
    public string? Model { get; set; }
    /// <summary>Optional portrait image path relative to the assets folder.</summary>
    public string? Portrait { get; set; }
}

public sealed record ArenaBox
{
    public float[] Min { get; set; } = [0, 0, 0];
    public float[] Max { get; set; } = [1, 1, 1];
    public SurfaceKind Surface { get; set; } = SurfaceKind.Wall;
    public string Color { get; set; } = "#888888";
    /// <summary>False for purely decorative geometry (e.g. lamp strings).</summary>
    public bool Solid { get; set; } = true;

    [JsonIgnore] public Vector3 MinV => new(Min[0], Min[1], Min[2]);
    [JsonIgnore] public Vector3 MaxV => new(Max[0], Max[1], Max[2]);
}

public sealed record ArenaSpawn
{
    public float[] Position { get; set; } = [0, 0, 0];
    public float Yaw { get; set; }
    public int Team { get; set; } = -1;
    [JsonIgnore] public Vector3 PositionV => new(Position[0], Position[1], Position[2]);
}

public sealed record ArenaPickup
{
    public float[] Position { get; set; } = [0, 0, 0];
    public PickupKind Kind { get; set; }
    public string? Weapon { get; set; }
    public float RespawnSeconds { get; set; } = 20f;
    [JsonIgnore] public Vector3 PositionV => new(Position[0], Position[1], Position[2]);
}

public sealed record ArenaJumpPad
{
    public float[] Min { get; set; } = [0, 0, 0];
    public float[] Max { get; set; } = [1, 0.2f, 1];
    public float[] Launch { get; set; } = [0, 15, 0];
    [JsonIgnore] public Vector3 MinV => new(Min[0], Min[1], Min[2]);
    [JsonIgnore] public Vector3 MaxV => new(Max[0], Max[1], Max[2]);
    [JsonIgnore] public Vector3 LaunchV => new(Launch[0], Launch[1], Launch[2]);
}

public sealed record ArenaProp
{
    public string Kind { get; set; } = "crate";
    public float[] Position { get; set; } = [0, 0, 0];
    public float[] HalfExtents { get; set; } = [0.4f, 0.4f, 0.4f];
    public float Mass { get; set; } = 20f;
    public string Color { get; set; } = "#A0522D";
    [JsonIgnore] public Vector3 PositionV => new(Position[0], Position[1], Position[2]);
    [JsonIgnore] public Vector3 HalfExtentsV => new(HalfExtents[0], HalfExtents[1], HalfExtents[2]);
}

public sealed record ArenaLight
{
    public float[] Position { get; set; } = [0, 3, 0];
    public string Color { get; set; } = "#FFD27F";
    public float Radius { get; set; } = 8f;
    [JsonIgnore] public Vector3 PositionV => new(Position[0], Position[1], Position[2]);
}

/// <summary>An arena map built from boxes. Built-in arenas are generated in code; mods ship JSON.</summary>
public sealed record ArenaDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public string SkyTop { get; set; } = "#1B1640";
    public string SkyBottom { get; set; } = "#E8743B";
    public string FogColor { get; set; } = "#2A1F4A";
    public float FogDensity { get; set; } = 0.012f;
    /// <summary>Cloud coverage 0 (clear) .. 1 (overcast).</summary>
    public float CloudCoverage { get; set; } = 0.4f;
    public string CloudColor { get; set; } = "#FFFFFF";
    /// <summary>Cloud drift speed (sky units per second).</summary>
    public float CloudSpeed { get; set; } = 0.006f;
    /// <summary>Night sky: stars and a moon disc instead of the sun.</summary>
    public bool Night { get; set; }
    /// <summary>Optional equirectangular panorama (path relative to assets/, e.g. a CC0 HDRI exported to JPG).</summary>
    public string? SkyTexture { get; set; }
    public string AmbientColor { get; set; } = "#5A5080";
    public string SunColor { get; set; } = "#FFE0B0";
    public float[] SunDirection { get; set; } = [-0.4f, -1f, -0.3f];
    public float KillY { get; set; } = -20f;
    public float[] ZoneCenter { get; set; } = [0, 0, 0];
    public float ZoneRadius { get; set; } = 4f;
    public List<ArenaBox> Boxes { get; set; } = [];
    public List<ArenaSpawn> Spawns { get; set; } = [];
    public List<ArenaPickup> Pickups { get; set; } = [];
    public List<ArenaJumpPad> JumpPads { get; set; } = [];
    public List<ArenaProp> Props { get; set; } = [];
    public List<ArenaLight> Lights { get; set; } = [];
    /// <summary>Optional [team0, team1] gorengan bases for Rebutan Gorengan; derived from team spawns when empty.</summary>
    public List<float[]> FlagBases { get; set; } = [];
    /// <summary>Optional goal for Panjat Pinang; the zone centre is used when empty.</summary>
    public float[]? RaceGoal { get; set; }

    [JsonIgnore] public Vector3 ZoneCenterV => new(ZoneCenter[0], ZoneCenter[1], ZoneCenter[2]);
}

[JsonConverter(typeof(JsonStringEnumConverter<GameModeKind>))]
public enum GameModeKind : byte { FreeForAll, TeamDeathmatch, KingOfTheHill, CaptureTheFlag, Race }

/// <summary>Rules for one match. Shared by the dedicated server, listen server and practice mode.</summary>
public sealed record MatchSettings
{
    public GameModeKind Mode { get; set; } = GameModeKind.FreeForAll;
    public string ArenaId { get; set; } = "pasar-malam";
    public int ScoreLimit { get; set; } = 25;
    public int TimeLimitSeconds { get; set; } = 600;
    public int BotCount { get; set; } = 5;
    public float BotSkill { get; set; } = 0.5f;
    public int MaxPlayers { get; set; } = SimConstants.MaxPlayers;
    public bool FriendlyFire { get; set; }
    public string ServerName { get; set; } = "Warung Tawuran";
}

public static class ColorUtil
{
    /// <summary>Parses "#RRGGBB" or "#RRGGBBAA" into 0xRRGGBBAA. Returns opaque magenta on bad input.</summary>
    public static uint ParseRgba(ReadOnlySpan<char> hex)
    {
        if (hex.Length > 0 && hex[0] == '#') hex = hex[1..];
        if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return (rgb << 8) | 0xFF;
        if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgba)) return rgba;
        return 0xFF00FFFF;
    }

    public static Vector3 ToVector3(uint rgba) => new(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f);
}
