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
/// <summary>English/Indonesian label pair for editor presets.</summary>
public readonly record struct LocalizedTextPair(string En, string Id);

public sealed class ArenaEditor
{
    public const string ModId = "arena-buatanku";
    public const int MaxUndo = 64;

    public ArenaDef Arena { get; private set; }
    private readonly List<EditorSelection> _selected = [];

    /// <summary>Primary selection (the last one clicked). Setting it replaces the whole selection.</summary>
    public EditorSelection Selection
    {
        get => _selected.Count == 0 ? EditorSelection.None : _selected[^1];
        set { _selected.Clear(); if (!value.IsNone) _selected.Add(value); }
    }

    /// <summary>Everything selected; the primary selection is last.</summary>
    public IReadOnlyList<EditorSelection> SelectedItems => _selected;

    /// <summary>Shift+click: adds the item to the selection, or removes it if it was already selected.</summary>
    public void ToggleSelect(EditorSelection s)
    {
        if (s.IsNone) return;
        if (!_selected.Remove(s)) _selected.Add(s);
    }

    private List<EditorSelection> ValidSelection() => _selected.Where(IsValid).Distinct().ToList();
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

    /// <summary>Copied items (deep copies) in an arena-shaped holder, shared by every editor so they paste into another arena.</summary>
    public static ArenaDef? Clipboard { get; private set; }
    private static List<EditorSelection> _clipboardItems = [];
    public static int ClipboardCount => _clipboardItems.Count;

    /// <summary>Copies every selected item to <see cref="Clipboard"/>.</summary>
    public bool Copy()
    {
        var sel = ValidSelection();
        if (sel.Count == 0) return false;
        var holder = new ArenaDef { Id = "clipboard" };
        var items = new List<EditorSelection>();
        foreach (var it in sel) items.Add(AddItem(holder, Arena, it));
        Clipboard = Clone(holder);
        _clipboardItems = items;
        return true;
    }

    /// <summary>
    /// Pastes the clipboard so the bottom centre of the copied group lands on <paramref name="at"/> (snapped),
    /// keeping the items' relative layout. Selects the pasted items and returns the primary one.
    /// </summary>
    public EditorSelection Paste(Vector3 at)
    {
        if (Clipboard is null || _clipboardItems.Count == 0) return EditorSelection.None;
        var holder = Clone(Clipboard);
        var bounds = BoundsOf(holder, _clipboardItems[0]);
        foreach (var it in _clipboardItems) bounds = Union(bounds, BoundsOf(holder, it));
        var anchor = new Vector3((bounds.Min.X + bounds.Max.X) / 2, bounds.Min.Y, (bounds.Min.Z + bounds.Max.Z) / 2);
        var offset = Snap(at) - anchor;
        Checkpoint();
        _selected.Clear();
        foreach (var it in _clipboardItems)
        {
            Translate(holder, it, offset);
            _selected.Add(AddItem(Arena, holder, it));
        }
        return Selection;
    }

    // ---- per-item helpers (work on any ArenaDef so the clipboard can use them too)

    private static EditorSelection AddItem(ArenaDef target, ArenaDef source, EditorSelection s)
    {
        int i = s.Index;
        switch (s.Kind)
        {
            case EditorItem.Box: target.Boxes.Add(source.Boxes[i] with { }); return new(s.Kind, target.Boxes.Count - 1);
            case EditorItem.Spawn: target.Spawns.Add(source.Spawns[i] with { }); return new(s.Kind, target.Spawns.Count - 1);
            case EditorItem.Pickup: target.Pickups.Add(source.Pickups[i] with { }); return new(s.Kind, target.Pickups.Count - 1);
            case EditorItem.JumpPad: target.JumpPads.Add(source.JumpPads[i] with { }); return new(s.Kind, target.JumpPads.Count - 1);
            case EditorItem.Prop: target.Props.Add(source.Props[i] with { }); return new(s.Kind, target.Props.Count - 1);
            default: target.Lights.Add(source.Lights[i] with { }); return new(s.Kind, target.Lights.Count - 1);
        }
    }

    private static void Translate(ArenaDef a, EditorSelection s, Vector3 d)
    {
        int i = s.Index;
        switch (s.Kind)
        {
            case EditorItem.Box: a.Boxes[i] = a.Boxes[i] with { Min = Add(a.Boxes[i].Min, d), Max = Add(a.Boxes[i].Max, d) }; break;
            case EditorItem.Spawn: a.Spawns[i] = a.Spawns[i] with { Position = Add(a.Spawns[i].Position, d) }; break;
            case EditorItem.Pickup: a.Pickups[i] = a.Pickups[i] with { Position = Add(a.Pickups[i].Position, d) }; break;
            case EditorItem.JumpPad: a.JumpPads[i] = a.JumpPads[i] with { Min = Add(a.JumpPads[i].Min, d), Max = Add(a.JumpPads[i].Max, d) }; break;
            case EditorItem.Prop: a.Props[i] = a.Props[i] with { Position = Add(a.Props[i].Position, d) }; break;
            default: a.Lights[i] = a.Lights[i] with { Position = Add(a.Lights[i].Position, d) }; break;
        }
    }

    private static void RemoveItem(ArenaDef a, EditorSelection s)
    {
        int i = s.Index;
        switch (s.Kind)
        {
            case EditorItem.Box: a.Boxes.RemoveAt(i); break;
            case EditorItem.Spawn: a.Spawns.RemoveAt(i); break;
            case EditorItem.Pickup: a.Pickups.RemoveAt(i); break;
            case EditorItem.JumpPad: a.JumpPads.RemoveAt(i); break;
            case EditorItem.Prop: a.Props.RemoveAt(i); break;
            default: a.Lights.RemoveAt(i); break;
        }
    }

    private static Aabb Union(Aabb a, Aabb b) => new(Vector3.Min(a.Min, b.Min), Vector3.Max(a.Max, b.Max));

    /// <summary>Bounds of the whole selection (for the gizmo and group paste).</summary>
    public Aabb SelectionBounds()
    {
        var sel = ValidSelection();
        if (sel.Count == 0) return default;
        var b = Bounds(sel[0]);
        foreach (var it in sel) b = Union(b, Bounds(it));
        return b;
    }

    /// <summary>An editable copy of any arena (for example a built-in one) under a new id.</summary>
    public static ArenaDef CopyOf(ArenaDef source, string newId, string nameEn, string nameId)
    {
        var a = Clone(source);
        a.Id = newId;
        a.Name = new(nameEn, nameId);
        return a;
    }

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

    private bool _gesture, _gestureSaved;

    /// <summary>Starts a continuous edit (a gizmo drag): all changes until <see cref="EndGesture"/> undo as one step.</summary>
    public void BeginGesture() { _gesture = true; _gestureSaved = false; }
    public void EndGesture() { _gesture = false; _gestureSaved = false; }

    private void Checkpoint()
    {
        if (_gesture && _gestureSaved) { Version++; return; }
        if (_gesture) _gestureSaved = true;
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

    /// <summary>Deletes every selected item.</summary>
    public bool Delete()
    {
        var sel = ValidSelection();
        if (sel.Count == 0) return false;
        Checkpoint();
        // Highest index first so earlier removals don't shift later ones.
        foreach (var it in sel.OrderByDescending(x => x.Index)) RemoveItem(Arena, it);
        _selected.Clear();
        return true;
    }

    /// <summary>Copies the selected items next to the originals (shifted along +X by the group's width) and selects the copies.</summary>
    public bool Duplicate()
    {
        var sel = ValidSelection();
        if (sel.Count == 0) return false;
        Checkpoint();
        var bounds = SelectionBounds();
        float w = sel.Count == 1 && sel[0].Kind == EditorItem.Box ? bounds.Max.X - bounds.Min.X : MathF.Max(1f, Grid * 2);
        if (sel.Count > 1) w = MathF.Max(w, bounds.Max.X - bounds.Min.X);
        var off = new Vector3(w, 0, 0);
        _selected.Clear();
        foreach (var it in sel)
        {
            var copy = AddItem(Arena, Arena, it);
            Translate(Arena, copy, off);
            _selected.Add(copy);
        }
        return true;
    }

    /// <summary>Moves every selected item by <paramref name="delta"/>.</summary>
    public void Move(Vector3 delta)
    {
        var sel = ValidSelection();
        if (sel.Count == 0 || delta == Vector3.Zero) return;
        Checkpoint();
        foreach (var it in sel) Translate(Arena, it, delta);
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

    /// <summary>Rotates the selection 90 degrees around the vertical axis (boxes and pads swap their X/Z size around the centre).</summary>
    public void RotateY()
    {
        var sel = ValidSelection();
        if (sel.Count == 0) return;
        Checkpoint();
        foreach (var it in sel) RotateOne(it);
    }

    private void RotateOne(EditorSelection it)
    {
        int i = it.Index;
        switch (it.Kind)
        {
            case EditorItem.Box:
            {
                var b = Arena.Boxes[i];
                var (mn, mx) = Swap(b.Min, b.Max);
                Arena.Boxes[i] = b with { Min = mn, Max = mx };
                break;
            }
            case EditorItem.JumpPad:
            {
                var p = Arena.JumpPads[i];
                var (mn, mx) = Swap(p.Min, p.Max);
                Arena.JumpPads[i] = p with { Min = mn, Max = mx, Launch = [-p.Launch[2], p.Launch[1], p.Launch[0]] };
                break;
            }
            case EditorItem.Spawn: Arena.Spawns[i] = Arena.Spawns[i] with { Yaw = (Arena.Spawns[i].Yaw + MathF.PI / 2) % (MathF.PI * 2) }; break;
            case EditorItem.Prop:
            {
                var h = Arena.Props[i].HalfExtents;
                Arena.Props[i] = Arena.Props[i] with { HalfExtents = [h[2], h[1], h[0]] };
                break;
            }
        }

        static (float[] Min, float[] Max) Swap(float[] min, float[] max)
        {
            float cx = (min[0] + max[0]) / 2, cz = (min[2] + max[2]) / 2;
            float hx = (max[0] - min[0]) / 2, hz = (max[2] - min[2]) / 2;
            return ([cx - hz, min[1], cz - hx], [cx + hz, max[1], cz + hx]);
        }
    }

    /// <summary>Sky/fog/light presets for the arena environment.</summary>
    public static readonly (LocalizedTextPair Name, string SkyTop, string SkyBottom, string Fog, float FogDensity, string Ambient, string Sun, float[] SunDir, bool Night, float Clouds)[] Environments =
    [
        (new("Midday", "Siang bolong"), "#2F72C9", "#D2E9FA", "#D6E9FF", 0.008f, "#9AA9BF", "#FFF8E1", [-0.3f, -0.9f, 0.2f], false, 0.35f),
        (new("Sunset", "Senja"), "#3B2A6B", "#F2994A", "#B5655A", 0.012f, "#7A5A80", "#FFC07A", [-0.8f, -0.35f, 0.3f], false, 0.5f),
        (new("Night", "Malam"), "#070B1F", "#1E2A55", "#141B38", 0.014f, "#3A4470", "#9FB3FF", [-0.2f, -1f, 0.4f], true, 0.25f),
        (new("Overcast", "Mendung"), "#6B7686", "#B8C0C8", "#A9B1BA", 0.016f, "#8B939E", "#E6E9EC", [-0.3f, -1f, 0.1f], false, 0.85f),
    ];

    /// <summary>Index of the environment preset that matches the arena, or -1.</summary>
    public int EnvironmentIndex => Array.FindIndex(Environments, e => e.SkyTop == Arena.SkyTop && e.SkyBottom == Arena.SkyBottom);

    public void CycleEnvironment(int dir = 1)
    {
        Checkpoint();
        int k = EnvironmentIndex;
        var e = Environments[Wrap(k + dir, Environments.Length)];
        Arena.SkyTop = e.SkyTop; Arena.SkyBottom = e.SkyBottom; Arena.FogColor = e.Fog; Arena.FogDensity = e.FogDensity;
        Arena.AmbientColor = e.Ambient; Arena.SunColor = e.Sun; Arena.SunDirection = [.. e.SunDir]; Arena.Night = e.Night; Arena.CloudCoverage = e.Clouds;
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
    public Aabb Bounds(EditorSelection s) => BoundsOf(Arena, s);

    private static Aabb BoundsOf(ArenaDef a, EditorSelection s)
    {
        int i = s.Index;
        return s.Kind switch
        {
            EditorItem.Box => new Aabb(a.Boxes[i].MinV, a.Boxes[i].MaxV),
            EditorItem.Spawn => Aabb.FromFeet(a.Spawns[i].PositionV, 0.4f, 1.8f),
            EditorItem.Pickup => Aabb.FromCenter(a.Pickups[i].PositionV, new Vector3(0.4f)),
            EditorItem.JumpPad => new Aabb(a.JumpPads[i].MinV, a.JumpPads[i].MaxV + new Vector3(0, 0.1f, 0)),
            EditorItem.Prop => Aabb.FromCenter(a.Props[i].PositionV, a.Props[i].HalfExtentsV),
            _ => Aabb.FromCenter(a.Lights[i].PositionV, new Vector3(0.3f)),
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
        if (ArenaPieces.CountPieces(Arena) > Djancoeg.Core.Simulation.GameWorld.MaxDestructibles) list.Add($"At most {Djancoeg.Core.Simulation.GameWorld.MaxDestructibles} breakable pieces (long breakable boxes count as several).");
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
