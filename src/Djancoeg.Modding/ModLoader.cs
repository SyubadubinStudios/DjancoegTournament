using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Djancoeg.Core;
using Djancoeg.Core.Content;

namespace Djancoeg.Modding;

/// <summary>A mod found on disk, valid or not.</summary>
public sealed class ModInfo
{
    public required string Directory { get; init; }
    public ModManifest? Manifest { get; init; }
    public List<string> Errors { get; } = [];
    public bool Enabled { get; set; } = true;
    public bool IsValid => Manifest is not null && Errors.Count == 0;
    public string Id => Manifest?.Id ?? Path.GetFileName(Directory);
}

/// <summary>
/// Discovers, validates, installs and applies mods ("Workshop"). Mods are folders under <c>mods/</c>
/// containing a <c>mod.json</c>; packaged mods are zip files with the <c>.djmod</c> extension.
/// </summary>
public static partial class ModLoader
{
    public const string ManifestFile = "mod.json";
    public const string SettingsFile = "settings.json";
    public const string PackageExtension = ".djmod";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex IdPattern();

    public static string DefaultModsDirectory => Path.Combine(AppContext.BaseDirectory, "mods");

    /// <summary>
    /// Returns a mods folder the player can write to. Portable builds use the folder next to the game; installed builds
    /// (Program Files, AppImage, macOS .app, or any read-only location) use <paramref name="userDir"/>, seeded with the
    /// bundled mods that aren't there yet so the examples keep working.
    /// </summary>
    public static string ResolveWritableModsDirectory(string bundledDir, string userDir, bool forceUser = false)
    {
        if (!forceUser && IsWritable(bundledDir)) return bundledDir;
        System.IO.Directory.CreateDirectory(userDir);
        if (System.IO.Directory.Exists(bundledDir))
        {
            foreach (var src in System.IO.Directory.EnumerateDirectories(bundledDir))
            {
                var dest = Path.Combine(userDir, Path.GetFileName(src));
                if (System.IO.Directory.Exists(dest) || !File.Exists(Path.Combine(src, ManifestFile))) continue;
                CopyTree(src, dest);
            }
        }
        return userDir;
    }

    /// <summary>True when the folder exists (or can be created) and a file can be written into it.</summary>
    public static bool IsWritable(string dir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-test-" + Environment.ProcessId);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    private static void CopyTree(string src, string dest)
    {
        System.IO.Directory.CreateDirectory(dest);
        foreach (var f in System.IO.Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: false);
        foreach (var d in System.IO.Directory.EnumerateDirectories(src)) CopyTree(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    public static List<ModInfo> Discover(string modsDir)
    {
        var result = new List<ModInfo>();
        if (!System.IO.Directory.Exists(modsDir)) return result;
        var settings = LoadSettings(modsDir);
        foreach (var dir in System.IO.Directory.EnumerateDirectories(modsDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(dir, ManifestFile))) continue;
            var info = Load(dir);
            info.Enabled = !settings.Disabled.Contains(info.Id, StringComparer.OrdinalIgnoreCase);
            result.Add(info);
        }
        return result;
    }

    public static ModInfo Load(string dir)
    {
        ModManifest? manifest = null;
        var errors = new List<string>();
        try
        {
            using var fs = File.OpenRead(Path.Combine(dir, ManifestFile));
            manifest = JsonSerializer.Deserialize(fs, ModJsonContext.Default.ModManifest);
            if (manifest is not null && manifest.ArenaFiles.Count > 0)
            {
                var arenas = new List<ArenaDef>(manifest.Arenas);
                foreach (var file in manifest.ArenaFiles)
                {
                    var full = Path.GetFullPath(Path.Combine(dir, file));
                    if (!full.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Arena file '{file}' is outside the mod folder.");
                        continue;
                    }
                    using var afs = File.OpenRead(full);
                    var arena = JsonSerializer.Deserialize(afs, ModJsonContext.Default.ArenaDef);
                    if (arena is not null) arenas.Add(arena);
                }
                manifest = manifest with { Arenas = arenas };
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            errors.Add($"Cannot read {ManifestFile}: {ex.Message}");
        }

        var info = new ModInfo { Directory = dir, Manifest = manifest };
        info.Errors.AddRange(errors);
        if (manifest is null) { if (info.Errors.Count == 0) info.Errors.Add($"{ManifestFile} is empty."); }
        else info.Errors.AddRange(Validate(manifest));
        return info;
    }

    /// <summary>Checks a manifest for mistakes that would crash or unbalance the game.</summary>
    public static IEnumerable<string> Validate(ModManifest m)
    {
        if (!IdPattern().IsMatch(m.Id)) yield return $"Mod id '{m.Id}' must be 2-40 chars of a-z, 0-9 and '-'.";
        if (string.IsNullOrWhiteSpace(m.Name.En) && string.IsNullOrWhiteSpace(m.Name.Id)) yield return "Mod needs a name.";

        foreach (var w in m.Weapons)
        {
            string p = $"Weapon '{w.Id}':";
            if (!IdPattern().IsMatch(w.Id)) yield return $"{p} invalid id.";
            if (w.Slot is < 1 or > 9) yield return $"{p} slot must be 1-9.";
            if (w.Damage is < 0 or > 500) yield return $"{p} damage must be 0-500.";
            if (w.FireInterval < 0.05f) yield return $"{p} fireInterval must be at least 0.05 s.";
            if (w.Pellets is < 1 or > 24) yield return $"{p} pellets must be 1-24.";
            if (w.Kind == WeaponKind.Projectile && w.ProjectileSpeed is <= 0 or > 200) yield return $"{p} projectileSpeed must be 1-200.";
            if (w.SplashRadius is < 0 or > 12) yield return $"{p} splashRadius must be 0-12.";
            if (w.MaxAmmo < 1 && !w.InfiniteAmmo) yield return $"{p} maxAmmo must be positive.";
        }
        foreach (var c in m.Characters)
        {
            string p = $"Character '{c.Id}':";
            if (!IdPattern().IsMatch(c.Id)) yield return $"{p} invalid id.";
            if (c.MaxHealth is < 25 or > 300) yield return $"{p} maxHealth must be 25-300.";
            if (c.SpeedMultiplier is < 0.5f or > 1.6f) yield return $"{p} speedMultiplier must be 0.5-1.6.";
            if (c.JumpMultiplier is < 0.5f or > 2f) yield return $"{p} jumpMultiplier must be 0.5-2.";
            if (c.Scale is < 0.5f or > 1.6f) yield return $"{p} scale must be 0.5-1.6.";
            if (c.Ability.Cooldown < 1f && c.Ability.Kind != AbilityKind.None) yield return $"{p} ability cooldown must be at least 1 s.";
        }
        foreach (var c in m.Creeps)
        {
            string p = $"Creep '{c.Id}':";
            if (!IdPattern().IsMatch(c.Id)) yield return $"{p} invalid id.";
            if (c.Health is < 1 or > 5000) yield return $"{p} health must be 1-5000.";
            if (c.Scale is < 0.3f or > 5f) yield return $"{p} scale must be 0.3-5.";
            if (c.Horde is < 1 or > 8) yield return $"{p} horde must be 1-8.";
            if (c.Damage is < 0 or > 200) yield return $"{p} damage must be 0-200.";
        }
        foreach (var a in m.Arenas)
        {
            string p = $"Arena '{a.Id}':";
            if (!IdPattern().IsMatch(a.Id)) yield return $"{p} invalid id.";
            if (a.Spawns.Count < 2) yield return $"{p} needs at least 2 spawns.";
            if (a.Boxes.Count is 0 or > 4096) yield return $"{p} needs 1-4096 boxes.";
            if (a.Pickups.Count > SimConstants.MaxPickups) yield return $"{p} has more than {SimConstants.MaxPickups} pickups.";
            if (a.Props.Count > SimConstants.MaxProps) yield return $"{p} has more than {SimConstants.MaxProps} props.";
            foreach (var b in a.Boxes)
                if (b.Min.Length != 3 || b.Max.Length != 3) { yield return $"{p} every box needs 3-number min/max."; break; }
            foreach (var pk in a.Pickups)
                if (pk.Kind == PickupKind.Weapon && string.IsNullOrEmpty(pk.Weapon)) { yield return $"{p} weapon pickup without a weapon id."; break; }
        }
    }

    /// <summary>Adds all enabled, valid mods to the registry in a stable (folder name) order.</summary>
    public static List<ModInfo> ApplyEnabled(ContentRegistry registry, string modsDir, Action<string>? log = null)
    {
        var mods = Discover(modsDir);
        foreach (var mod in mods)
        {
            if (!mod.Enabled) { log?.Invoke($"[mods] {mod.Id}: disabled"); continue; }
            if (!mod.IsValid)
            {
                log?.Invoke($"[mods] {mod.Id}: skipped ({string.Join("; ", mod.Errors)})");
                continue;
            }
            Apply(registry, mod.Manifest!);
            log?.Invoke($"[mods] {mod.Id} v{mod.Manifest!.Version} loaded: {mod.Manifest.Weapons.Count} weapons, {mod.Manifest.Characters.Count} characters, {mod.Manifest.Arenas.Count} arenas");
        }
        return mods;
    }

    public static void Apply(ContentRegistry registry, ModManifest m)
    {
        foreach (var w in m.Weapons) registry.AddOrReplace(w);
        foreach (var c in m.Characters) registry.AddOrReplace(c);
        foreach (var a in m.Arenas) registry.AddOrReplace(a);
        foreach (var c in m.Creeps) registry.AddOrReplace(c);
        registry.RegisterMod(m.Id);
    }

    public static ModSettings LoadSettings(string modsDir)
    {
        var path = Path.Combine(modsDir, SettingsFile);
        if (!File.Exists(path)) return new ModSettings();
        try
        {
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize(fs, ModJsonContext.Default.ModSettings) ?? new ModSettings();
        }
        catch (JsonException) { return new ModSettings(); }
    }

    public static void SetEnabled(string modsDir, string modId, bool enabled)
    {
        var s = LoadSettings(modsDir);
        var disabled = new List<string>(s.Disabled.Where(d => !d.Equals(modId, StringComparison.OrdinalIgnoreCase)));
        if (!enabled) disabled.Add(modId);
        System.IO.Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, SettingsFile), JsonSerializer.Serialize(new ModSettings { Disabled = disabled }, ModJsonContext.Default.ModSettings));
    }

    /// <summary>Zips a mod folder into a shareable <c>.djmod</c> package after validating it.</summary>
    public static string Pack(string modDir, string outputDir)
    {
        var info = Load(modDir);
        if (!info.IsValid) throw new InvalidDataException("Mod is not valid: " + string.Join("; ", info.Errors));
        System.IO.Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, $"{info.Id}-{info.Manifest!.Version}{PackageExtension}");
        if (File.Exists(path)) File.Delete(path);
        ZipFile.CreateFromDirectory(modDir, path, CompressionLevel.Optimal, includeBaseDirectory: false);
        return path;
    }

    /// <summary>Installs a <c>.djmod</c> package into the mods folder. Rejects path traversal entries.</summary>
    public static ModInfo Install(string packagePath, string modsDir)
    {
        using var zip = ZipFile.OpenRead(packagePath);
        var manifestEntry = zip.GetEntry(ManifestFile) ?? throw new InvalidDataException($"Package has no {ManifestFile}.");
        ModManifest manifest;
        using (var s = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize(s, ModJsonContext.Default.ModManifest) ?? throw new InvalidDataException("Empty manifest.");
        if (!IdPattern().IsMatch(manifest.Id)) throw new InvalidDataException($"Invalid mod id '{manifest.Id}'.");

        var target = Path.GetFullPath(Path.Combine(modsDir, manifest.Id));
        if (System.IO.Directory.Exists(target)) System.IO.Directory.Delete(target, recursive: true);
        System.IO.Directory.CreateDirectory(target);
        foreach (var entry in zip.Entries)
        {
            var dest = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!dest.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe path in package: {entry.FullName}");
            if (entry.FullName.EndsWith('/')) { System.IO.Directory.CreateDirectory(dest); continue; }
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
        return Load(target);
    }

    /// <summary>Creates a ready-to-edit example mod folder.</summary>
    public static string Scaffold(string modsDir, string id)
    {
        if (!IdPattern().IsMatch(id)) throw new ArgumentException("Mod id must be 2-40 chars of a-z, 0-9 and '-'.", nameof(id));
        var dir = Path.Combine(modsDir, id);
        System.IO.Directory.CreateDirectory(dir);
        var manifest = new ModManifest
        {
            Id = id,
            Name = new("My Mod", "Mod Saya"),
            Description = new("Describe your mod here.", "Jelaskan mod kamu di sini."),
            Author = Environment.UserName,
            Weapons =
            [
                new WeaponDef
                {
                    Id = id + "-cobek", Slot = 8, Kind = WeaponKind.Projectile, Model = "firecracker", Color = "#8D6E63",
                    Name = new("Stone Mortar", "Cobek Batu"), Description = new("Throws a heavy stone mortar.", "Melempar cobek batu yang berat."),
                    KillVerb = new("{killer} ground {victim} into sambal", "{killer} mengulek {victim} jadi sambal"),
                    Damage = 60, FireInterval = 1.0f, ProjectileSpeed = 20, ProjectileGravity = 12, MaxAmmo = 15, StartAmmo = 5, PickupAmmo = 5,
                },
            ],
        };
        File.WriteAllText(Path.Combine(dir, ManifestFile), JsonSerializer.Serialize(manifest, ModJsonContext.Default.ModManifest));
        return dir;
    }
}
