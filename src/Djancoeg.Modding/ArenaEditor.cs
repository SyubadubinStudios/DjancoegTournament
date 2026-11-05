using System.Numerics;
using System.Text.Json;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Modding;

public enum EditorItem : byte { Box, Spawn, Pickup, JumpPad, Prop, Light }

/// <summary>A selected thing inside the arena being edited.</summary>
public readonly record struct EditorSelection(EditorItem Kind, int Index)
{
    public static readonly EditorSelection None = new(EditorItem.Box, -1);
    public bool IsNone => Index < 0;
}

/// <summary>
/// Window-free model behind the in-game level editor: placing, moving, resizing and deleting arena
/// items with grid snapping, undo/redo, ray picking, validation and saving into a local mod.
/// </summary>
public sealed class ArenaEditor
{
    public const string ModId = "arena-buatanku";
    public const int MaxUndo = 64;

    public ArenaDef Arena { get; private set; }
    public EditorSelection Selection { get; set; } = EditorSelection.None;
    public float Grid { get; set; } = 0.5f;
    /// <summary>Incremented on every change; the renderer rebuilds when it differs.</summary>
    public int Version { get; private set; }

    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public ArenaEditor(ArenaDef arena) => Arena = Clone(arena);

    /// <summary>Physics props the editor can place (model kind, half extents, mass).</summary>
    public static readonly (string Kind, Vector3 Half, float Mass)[] PropKinds =
    [
        ("crate", new(0.45f, 0.45f, 0.45f), 22f), ("galon", new(0.2f, 0.3f, 0.2f), 10f), ("jerigen", new(0.22f, 0.3f, 0.15f), 12f),
        ("kursi", new(0.25f, 0.42f, 0.25f), 6f), ("ban", new(0.45f, 0.45f, 0.45f), 15f), ("gerobak", new(0.9f, 0.6f, 0.5f), 60f),
    ];

    public static readonly string[] Palette = ["#8D6E63", "#ECEFF1", "#FFC21A", "#E3242B", "#1FA64A", "#1D3FBB", "#455A64", "#FF7043", "#7CB342", "#4FC3F7"];

    /// <summary>A starter arena: floor, four walls, four spawns and a few pickups.</summary>
    public static ArenaDef NewArena(string id) => new()
    {
        Id = id,
        Name = new("My Arena", "Arena Buatanku"),
        Description = new("Built with the in-game editor.", "Dibuat dengan editor dalam game."),
        SkyTop = "#2F72C9", SkyBottom = "#D2E9FA", FogColor = "#D6E9FF", FogDensity = 0.008f, AmbientColor = "#9AA9BF", SunColor = "#FFF8E1",
        SunDirection = [-0.3f, -0.9f, 0.2f], KillY = -20, ZoneCenter = [0, 0, 0], ZoneRadius = 3,
        Boxes =
        [
            new() { Min = [-20, -1, -20], Max = [20, 0, 20], Surface = SurfaceKind.Tile, Color = "#8F877C" },
            new() { Min = [-21, 0, -21], Max = [21, 3, -20], Surface = SurfaceKind.Wall, Color = "#ECEFF1" },
            new() { Min = [-21, 0, 20], Max = [21, 3, 21], Surface = SurfaceKind.Wall, Color = "#ECEFF1" },
            new() { Min = [-21, 0, -20], Max = [-20, 3, 20], Surface = SurfaceKind.Wall, Color = "#ECEFF1" },
            new() { Min = [20, 0, -20], Max = [21, 3, 20], Surface = SurfaceKind.Wall, Color = "#ECEFF1" },
        ],
        Spawns =
        [
            new() { Position = [-15, 0, -15], Yaw = 0.785f, Team = 0 }, new() { Position = [15, 0, 15], Yaw = -2.36f, Team = 1 },
            new() { Position = [15, 0, -15], Yaw = -0.785f, Team = 1 }, new() { Position = [-15, 0, 15], Yaw = 2.36f, Team = 0 },
        ],
        Pickups =
        [
            new() { Position = [0, 0.5f, 0], Kind = PickupKind.HealthMega, RespawnSeconds = 35 },
            new() { Position = [-10, 0.5f, 0], Kind = PickupKind.Ammo }, new() { Position = [10, 0.5f, 0], Kind = PickupKind.Armor },
        ],
        Lights = [new() { Position = [0, 4, 0], Radius = 12 }],
    };

    // ------------------------------------------------------------------ history

    private void Checkpoint()
    {
        _undo.Push(Serialize(Arena));
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            foreach (var k in keep) _undo.Push(k);
        }
        _redo.Clear();
        Version++;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Serialize(Arena));
        Arena = Deserialize(_undo.Pop());
        Selection = EditorSelection.None;
        Version++;
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Serialize(Arena));
        Arena = Deserialize(_redo.Pop());
        Selection = EditorSelection.None;
        Version++;
    }

    // ------------------------------------------------------------------ editing

    public float Snap(float v) => Grid <= 0 ? v : MathF.Round(v / Grid) * Grid;
    public Vector3 Snap(Vector3 v) => new(Snap(v.X), Snap(v.Y), Snap(v.Z));

    /// <summary>Places a new item standing on <paramref name="at"/> and selects it.</summary>
    public EditorSelection Place(EditorItem kind, Vector3 at, string color = "#8D6E63", SurfaceKind surface = SurfaceKind.Wood)
    {
        Checkpoint();
        var p = Snap(at);
        int index;
        switch (kind)
        {
            case EditorItem.Box:
                Arena.Boxes.Add(new ArenaBox { Min = [p.X - 1, p.Y, p.Z - 1], Max = [p.X + 1, p.Y + 1, p.Z + 1], Surface = surface, Color = color });
                index = Arena.Boxes.Count - 1; break;
            case EditorItem.Spawn:
                Arena.Spawns.Add(new ArenaSpawn { Position = [p.X, p.Y, p.Z], Team = Arena.Spawns.Count % 2 });
                index = Arena.Spawns.Count - 1; break;
            case EditorItem.Pickup:
                Arena.Pickups.Add(new ArenaPickup { Position = [p.X, p.Y + 0.5f, p.Z], Kind = PickupKind.HealthSmall });
                index = Arena.Pickups.Count - 1; break;
            case EditorItem.JumpPad:
                Arena.JumpPads.Add(new ArenaJumpPad { Min = [p.X - 0.75f, p.Y, p.Z - 0.75f], Max = [p.X + 0.75f, p.Y + 0.2f, p.Z + 0.75f], Launch = [0, 15, 0] });
                index = Arena.JumpPads.Count - 1; break;
            case EditorItem.Prop:
                Arena.Props.Add(new ArenaProp { Kind = "crate", Position = [p.X, p.Y + 0.45f, p.Z], HalfExtents = [0.45f, 0.45f, 0.45f], Mass = 22f });
                index = Arena.Props.Count - 1; break;
            default:
                Arena.Lights.Add(new ArenaLight { Position = [p.X, p.Y + 3, p.Z] });
                index = Arena.Lights.Count - 1; break;
        }
        return Selection = new EditorSelection(kind, index);
    }

    public bool Delete()
    {
        if (!IsValid(Selection)) return false;
        Checkpoint();
        int i = Selection.Index;
        switch (Selection.Kind)
        {
            case EditorItem.Box: Arena.Boxes.RemoveAt(i); break;
            case EditorItem.Spawn: Arena.Spawns.RemoveAt(i); break;
            case EditorItem.Pickup: Arena.Pickups.RemoveAt(i); break;
            case EditorItem.JumpPad: Arena.JumpPads.RemoveAt(i); break;
            case EditorItem.Prop: Arena.Props.RemoveAt(i); break;
            default: Arena.Lights.RemoveAt(i); break;
        }
        Selection = EditorSelection.None;
        return true;
    }

    /// <summary>Copies the selected item one grid step (or 1 unit) along +X and selects the copy.</summary>
    public bool Duplicate()
    {
        if (!IsValid(Selection)) return false;
        Checkpoint();
        var off = new Vector3(MathF.Max(1f, Grid * 2), 0, 0);
        int i = Selection.Index;
        switch (Selection.Kind)
        {
            case EditorItem.Box:
            {
                var b = Arena.Boxes[i];
                float w = b.Max[0] - b.Min[0];
                Arena.Boxes.Add(b with { Min = [b.Min[0] + w, b.Min[1], b.Min[2]], Max = [b.Max[0] + w, b.Max[1], b.Max[2]] });
                Selection = new(EditorItem.Box, Arena.Boxes.Count - 1);
                break;
            }
            case EditorItem.Spawn: Arena.Spawns.Add(Arena.Spawns[i] with { Position = Add(Arena.Spawns[i].Position, off) }); Selection = new(EditorItem.Spawn, Arena.Spawns.Count - 1); break;
            case EditorItem.Pickup: Arena.Pickups.Add(Arena.Pickups[i] with { Position = Add(Arena.Pickups[i].Position, off) }); Selection = new(EditorItem.Pickup, Arena.Pickups.Count - 1); break;
            case EditorItem.JumpPad: Arena.JumpPads.Add(Arena.JumpPads[i] with { Min = Add(Arena.JumpPads[i].Min, off), Max = Add(Arena.JumpPads[i].Max, off) }); Selection = new(EditorItem.JumpPad, Arena.JumpPads.Count - 1); break;
            case EditorItem.Prop: Arena.Props.Add(Arena.Props[i] with { Position = Add(Arena.Props[i].Position, off) }); Selection = new(EditorItem.Prop, Arena.Props.Count - 1); break;
            default: Arena.Lights.Add(Arena.Lights[i] with { Position = Add(Arena.Lights[i].Position, off) }); Selection = new(EditorItem.Light, Arena.Lights.Count - 1); break;
        }
        return true;
    }

    /// <summary>Moves the selection by <paramref name="delta"/>.</summary>
    public void Move(Vector3 delta)
    {
        if (!IsValid(Selection) || delta == Vector3.Zero) return;
        Checkpoint();
        int i = Selection.Index;
        switch (Selection.Kind)
        {
            case EditorItem.Box: Arena.Boxes[i] = Arena.Boxes[i] with { Min = Add(Arena.Boxes[i].Min, delta), Max = Add(Arena.Boxes[i].Max, delta) }; break;
            case EditorItem.Spawn: Arena.Spawns[i] = Arena.Spawns[i] with { Position = Add(Arena.Spawns[i].Position, delta) }; break;
            case EditorItem.Pickup: Arena.Pickups[i] = Arena.Pickups[i] with { Position = Add(Arena.Pickups[i].Position, delta) }; break;
            case EditorItem.JumpPad: Arena.JumpPads[i] = Arena.JumpPads[i] with { Min = Add(Arena.JumpPads[i].Min, delta), Max = Add(Arena.JumpPads[i].Max, delta) }; break;
            case EditorItem.Prop: Arena.Props[i] = Arena.Props[i] with { Position = Add(Arena.Props[i].Position, delta) }; break;
            default: Arena.Lights[i] = Arena.Lights[i] with { Position = Add(Arena.Lights[i].Position, delta) }; break;
        }
    }

    /// <summary>Grows (or shrinks) the selected box or jump pad on its max corner; sizes never drop below one grid step.</summary>
    public void Resize(Vector3 delta)
    {
        if (!IsValid(Selection) || delta == Vector3.Zero) return;
        float min = MathF.Max(0.1f, Grid);
        if (Selection.Kind == EditorItem.Box)
        {
            Checkpoint();
            var b = Arena.Boxes[Selection.Index];
            Arena.Boxes[Selection.Index] = b with { Max = Grow(b.Min, b.Max, delta, min) };
        }
        else if (Selection.Kind == EditorItem.JumpPad)
        {
            Checkpoint();
            var p = Arena.JumpPads[Selection.Index];
            Arena.JumpPads[Selection.Index] = p with { Max = Grow(p.Min, p.Max, delta with { Y = 0 }, min) };
        }
        else if (Selection.Kind == EditorItem.Light && delta.Y != 0)
        {
            Checkpoint();
            var l = Arena.Lights[Selection.Index];
            Arena.Lights[Selection.Index] = l with { Radius = Math.Clamp(l.Radius + delta.Y * 2, 2, 40) };
        }
    }

    /// <summary>Cycles the "style" of the selection: surface/colour for boxes, pickup kind, team for spawns, prop kind.</summary>
    public void CycleStyle(int dir, bool colorOnly = false)
    {
        if (!IsValid(Selection)) return;
        Checkpoint();
        int i = Selection.Index;
        switch (Selection.Kind)
        {
            case EditorItem.Box:
            {
                var b = Arena.Boxes[i];
                if (colorOnly)
                {
                    int ci = Array.IndexOf(Palette, b.Color);
                    Arena.Boxes[i] = b with { Color = Palette[Wrap(ci + dir, Palette.Length)] };
                }
                else Arena.Boxes[i] = b with { Surface = (SurfaceKind)Wrap((int)b.Surface + dir, Enum.GetValues<SurfaceKind>().Length) };
                break;
            }
            case EditorItem.Pickup:
            {
                var p = Arena.Pickups[i];
                var kinds = new[] { PickupKind.HealthSmall, PickupKind.HealthMega, PickupKind.Armor, PickupKind.ArmorSmall, PickupKind.Ammo, PickupKind.Quad };
                int k = Array.IndexOf(kinds, p.Kind);
                Arena.Pickups[i] = p with { Kind = kinds[Wrap(k + dir, kinds.Length)], Weapon = null };
                break;
            }
            case EditorItem.Spawn: Arena.Spawns[i] = Arena.Spawns[i] with { Team = Wrap(Arena.Spawns[i].Team + 1 + dir, 3) - 1 }; break;
            case EditorItem.Prop:
            {
                int k = Array.FindIndex(PropKinds, x => x.Kind == Arena.Props[i].Kind);
                var (kind, half, mass) = PropKinds[Wrap(k + dir, PropKinds.Length)];
                var pos = Arena.Props[i].Position;
                float floor = pos[1] - Arena.Props[i].HalfExtents[1];
                Arena.Props[i] = Arena.Props[i] with { Kind = kind, HalfExtents = [half.X, half.Y, half.Z], Mass = mass, Position = [pos[0], floor + half.Y, pos[2]] };
                break;
            }
            case EditorItem.JumpPad:
            {
                var l = Arena.JumpPads[i].Launch;
                Arena.JumpPads[i] = Arena.JumpPads[i] with { Launch = [l[0], Math.Clamp(l[1] + dir * 2, 6, 30), l[2]] };
                break;
            }
            default:
            {
                int ci = Array.IndexOf(Palette, Arena.Lights[i].Color);
                Arena.Lights[i] = Arena.Lights[i] with { Color = Palette[Wrap(ci + dir, Palette.Length)] };
                break;
            }
        }
    }

    /// <summary>Toggles a box between solid, destructible (hp 120) and decorative.</summary>
    public void CycleBoxKind()
    {
        if (Selection.Kind != EditorItem.Box || !IsValid(Selection)) return;
        Checkpoint();
        var b = Arena.Boxes[Selection.Index];
        Arena.Boxes[Selection.Index] = (b.Solid, b.Health > 0) switch
        {
            (true, false) => b with { Health = 120 },
            (true, true) => b with { Health = 0, Solid = false },
            _ => b with { Solid = true, Health = 0 },
        };
    }

    public void SetZoneHere(Vector3 at)
    {
        Checkpoint();
        var p = Snap(at);
        Arena.ZoneCenter = [p.X, p.Y, p.Z];
    }

    public void Rename(string id, string nameEn, string nameId)
    {
        Checkpoint();
        Arena.Id = id;
        Arena.Name = new(nameEn, nameId);
    }

    // ------------------------------------------------------------------ queries

    public bool IsValid(EditorSelection s) => s.Index >= 0 && s.Index < s.Kind switch
    {
        EditorItem.Box => Arena.Boxes.Count,
        EditorItem.Spawn => Arena.Spawns.Count,
        EditorItem.Pickup => Arena.Pickups.Count,
        EditorItem.JumpPad => Arena.JumpPads.Count,
        EditorItem.Prop => Arena.Props.Count,
        _ => Arena.Lights.Count,
    };

    /// <summary>World-space bounds of an item (markers for points).</summary>
    public Aabb Bounds(EditorSelection s)
    {
        int i = s.Index;
        return s.Kind switch
        {
            EditorItem.Box => new Aabb(Arena.Boxes[i].MinV, Arena.Boxes[i].MaxV),
            EditorItem.Spawn => Aabb.FromFeet(Arena.Spawns[i].PositionV, 0.4f, 1.8f),
            EditorItem.Pickup => Aabb.FromCenter(Arena.Pickups[i].PositionV, new Vector3(0.4f)),
            EditorItem.JumpPad => new Aabb(Arena.JumpPads[i].MinV, Arena.JumpPads[i].MaxV + new Vector3(0, 0.1f, 0)),
            EditorItem.Prop => Aabb.FromCenter(Arena.Props[i].PositionV, Arena.Props[i].HalfExtentsV),
            _ => Aabb.FromCenter(Arena.Lights[i].PositionV, new Vector3(0.3f)),
        };
    }

    /// <summary>Nearest item under the ray. Markers (spawns, pickups, lights…) win over boxes they sit on.</summary>
    public EditorSelection Pick(Vector3 origin, Vector3 dir, out float dist, out Vector3 normal)
    {
        var inv = VectorMath.SafeInverse(dir);
        dist = 1000f; normal = Vector3.UnitY;
        var best = EditorSelection.None;
        foreach (EditorItem kind in Enum.GetValues<EditorItem>())
        {
            int count = kind switch
            {
                EditorItem.Box => Arena.Boxes.Count, EditorItem.Spawn => Arena.Spawns.Count, EditorItem.Pickup => Arena.Pickups.Count,
                EditorItem.JumpPad => Arena.JumpPads.Count, EditorItem.Prop => Arena.Props.Count, _ => Arena.Lights.Count,
            };
            for (int i = 0; i < count; i++)
            {
                var s = new EditorSelection(kind, i);
                var box = Bounds(s);
                if (!box.Raycast(origin, inv, dist + (kind == EditorItem.Box ? 0 : 0.6f), out float t, out var n)) continue;
                // Give markers a small bias so they can be picked when resting on a floor.
                float score = kind == EditorItem.Box ? t : t - 0.6f;
                if (score < dist) { dist = MathF.Max(0, score); best = s; normal = n; if (kind != EditorItem.Box) dist = t; }
            }
        }
        return best;
    }

    /// <summary>Problems that would stop the arena from loading or playing well.</summary>
    public List<string> Problems()
    {
        var m = new ModManifest { Id = ModId, Name = new("My Arenas", "Arena Buatanku"), Arenas = [Arena] };
        var list = ModLoader.Validate(m).ToList();
        if (Arena.Boxes.Count == 0) list.Add("Arena has no boxes.");
        if (Arena.Boxes.Count(b => b.Solid && b.Health > 0) > 64) list.Add("At most 64 destructible boxes.");
        return list;
    }

    // ------------------------------------------------------------------ files

    /// <summary>Saves the arena into <c>mods/arena-buatanku/arenas/&lt;id&gt;.json</c>, creating/updating the mod manifest. Returns the file path.</summary>
    public string Save(string modsDir)
    {
        var dir = Path.Combine(modsDir, ModId);
        Directory.CreateDirectory(Path.Combine(dir, "arenas"));
        var manifestPath = Path.Combine(dir, ModLoader.ManifestFile);
        ModManifest manifest;
        try
        {
            manifest = File.Exists(manifestPath)
                ? JsonSerializer.Deserialize(File.ReadAllText(manifestPath), ModJsonContext.Default.ModManifest) ?? NewManifest()
                : NewManifest();
        }
        catch (JsonException) { manifest = NewManifest(); }
        string rel = "arenas/" + Arena.Id + ".json";
        if (!manifest.ArenaFiles.Contains(rel)) manifest.ArenaFiles.Add(rel);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ModJsonContext.Default.ModManifest));
        var path = Path.Combine(dir, "arenas", Arena.Id + ".json");
        File.WriteAllText(path, Serialize(Arena));
        return path;
    }

    /// <summary>Arenas previously saved by the editor.</summary>
    public static List<ArenaDef> LoadSaved(string modsDir)
    {
        var dir = Path.Combine(modsDir, ModId, "arenas");
        var list = new List<ArenaDef>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try { if (JsonSerializer.Deserialize(File.ReadAllText(f), ModJsonContext.Default.ArenaDef) is { } a) list.Add(a); }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return list;
    }

    private static ModManifest NewManifest() => new()
    {
        Id = ModId,
        Name = new("My Arenas", "Arena Buatanku"),
        Description = new("Arenas made with the in-game level editor.", "Arena yang dibuat dengan editor level dalam game."),
        Author = Environment.UserName,
        GameVersion = "2.0",
    };

    public static string Serialize(ArenaDef a) => JsonSerializer.Serialize(a, ModJsonContext.Default.ArenaDef);
    public static ArenaDef Deserialize(string json) => JsonSerializer.Deserialize(json, ModJsonContext.Default.ArenaDef)!;
    public static ArenaDef Clone(ArenaDef a) => Deserialize(Serialize(a));

    private static float[] Add(float[] v, Vector3 d) => [v[0] + d.X, v[1] + d.Y, v[2] + d.Z];

    private static float[] Grow(float[] min, float[] max, Vector3 d, float minSize) =>
    [
        MathF.Max(min[0] + minSize, max[0] + d.X),
        MathF.Max(min[1] + minSize, max[1] + d.Y),
        MathF.Max(min[2] + minSize, max[2] + d.Z),
    ];

    private static int Wrap(int v, int n) => ((v % n) + n) % n;
}
