using System.Numerics;
using System.Runtime.CompilerServices;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

/// <summary>Static collision for an arena: solid boxes, jump pads and the kill plane.</summary>
/// <remarks>
/// Arenas contain a few hundred boxes, so a flat array scan is cache friendly and faster than a
/// tree at this size. Overlap queries use a coarse XZ grid to skip far-away boxes.
/// </remarks>
public sealed class CollisionWorld
{
    private const float CellSize = 6f;
    private readonly Aabb[] _solids;
    private readonly SurfaceKind[] _surfaces;
    private readonly int[] _solidToBox;
    private readonly Dictionary<int, int> _boxToSolid = [];
    private readonly bool[] _disabled;
    private readonly Aabb[] _pads;
    private readonly Vector3[] _padLaunch;

    // Grid broadphase: cell -> [start, count) into _cellItems.
    private readonly int _gridW, _gridH;
    private readonly float _gridX0, _gridZ0;
    private readonly int[] _cellStart, _cellCount, _cellItems;

    public float KillY { get; }
    public Aabb Bounds { get; }
    public ReadOnlySpan<Aabb> Solids => _solids;
    public ReadOnlySpan<Aabb> JumpPads => _pads;
    public Vector3 PadLaunch(int i) => _padLaunch[i];

    public CollisionWorld(ArenaDef arena)
    {
        var solids = new List<Aabb>(arena.Boxes.Count);
        var surfaces = new List<SurfaceKind>(arena.Boxes.Count);
        var map = new List<int>(arena.Boxes.Count);
        for (int bi = 0; bi < arena.Boxes.Count; bi++)
        {
            var b = arena.Boxes[bi];
            if (!b.Solid) continue;
            _boxToSolid[bi] = solids.Count;
            solids.Add(new Aabb(b.MinV, b.MaxV));
            surfaces.Add(b.Surface);
            map.Add(bi);
        }
        _solids = [.. solids];
        _surfaces = [.. surfaces];
        _solidToBox = [.. map];
        _disabled = new bool[_solids.Length];
        _pads = new Aabb[arena.JumpPads.Count];
        _padLaunch = new Vector3[arena.JumpPads.Count];
        for (int i = 0; i < _pads.Length; i++)
        {
            _pads[i] = new Aabb(arena.JumpPads[i].MinV, arena.JumpPads[i].MaxV);
            _padLaunch[i] = arena.JumpPads[i].LaunchV;
        }
        KillY = arena.KillY;

        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var s in _solids) { min = Vector3.Min(min, s.Min); max = Vector3.Max(max, s.Max); }
        if (_solids.Length == 0) { min = new(-1); max = new(1); }
        Bounds = new Aabb(min, max);

        _gridX0 = min.X; _gridZ0 = min.Z;
        _gridW = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / CellSize));
        _gridH = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / CellSize));
        _cellStart = new int[_gridW * _gridH];
        _cellCount = new int[_gridW * _gridH];

        // Two passes: count, then fill (no per-cell lists, one flat array).
        for (int i = 0; i < _solids.Length; i++) ForCells(_solids[i], c => _cellCount[c]++);
        int total = 0;
        for (int c = 0; c < _cellCount.Length; c++) { _cellStart[c] = total; total += _cellCount[c]; _cellCount[c] = 0; }
        _cellItems = new int[total];
        for (int i = 0; i < _solids.Length; i++)
        {
            int idx = i;
            ForCells(_solids[i], c => _cellItems[_cellStart[c] + _cellCount[c]++] = idx);
        }
        _stamp = new int[_solids.Length];
    }

    private void ForCells(in Aabb b, Action<int> action)
    {
        CellRange(b, out int x0, out int z0, out int x1, out int z1);
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                action(z * _gridW + x);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CellRange(in Aabb b, out int x0, out int z0, out int x1, out int z1)
    {
        x0 = Math.Clamp((int)((b.Min.X - _gridX0) / CellSize), 0, _gridW - 1);
        x1 = Math.Clamp((int)((b.Max.X - _gridX0) / CellSize), 0, _gridW - 1);
        z0 = Math.Clamp((int)((b.Min.Z - _gridZ0) / CellSize), 0, _gridH - 1);
        z1 = Math.Clamp((int)((b.Max.Z - _gridZ0) / CellSize), 0, _gridH - 1);
    }

    // Query de-duplication without allocation: a box is visited once per query stamp.
    private readonly int[] _stamp;
    private int _query;

    /// <summary>True if <paramref name="box"/> overlaps any solid.</summary>
    public bool Overlaps(in Aabb box)
    {
        CellRange(box, out int x0, out int z0, out int x1, out int z1);
        int q = ++_query;
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int c = z * _gridW + x, s = _cellStart[c], n = _cellCount[c];
                for (int k = 0; k < n; k++)
                {
                    int i = _cellItems[s + k];
                    if (_stamp[i] == q) continue;
                    _stamp[i] = q;
                    if (!_disabled[i] && _solids[i].Intersects(box)) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Sweeps <paramref name="box"/> along one axis and returns how far it can travel before touching a solid.
    /// Boxes the body already overlaps are ignored so bodies can never get stuck permanently.
    /// </summary>
    public float SweepAxis(in Aabb box, int axis, float delta)
    {
        if (delta == 0f) return 0f;
        const float Skin = 0.001f;
        Vector3 d = axis switch { 0 => new(delta, 0, 0), 1 => new(0, delta, 0), _ => new(0, 0, delta) };
        var moved = box.Translate(d);
        var swept = new Aabb(Vector3.Min(box.Min, moved.Min), Vector3.Max(box.Max, moved.Max));

        float allowed = delta;
        CellRange(swept, out int x0, out int z0, out int x1, out int z1);
        int q = ++_query;
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int c = z * _gridW + x, s = _cellStart[c], n = _cellCount[c];
                for (int k = 0; k < n; k++)
                {
                    int i = _cellItems[s + k];
                    if (_stamp[i] == q) continue;
                    _stamp[i] = q;
                    if (_disabled[i]) continue;
                    ref readonly var solid = ref _solids[i];
                    if (!solid.Intersects(swept) || solid.Intersects(box)) continue;
                    if (delta > 0)
                    {
                        float gap = Component(solid.Min, axis) - Component(box.Max, axis) - Skin;
                        if (gap < allowed) allowed = MathF.Max(0, gap);
                    }
                    else
                    {
                        float gap = Component(solid.Max, axis) - Component(box.Min, axis) + Skin;
                        if (gap > allowed) allowed = MathF.Min(0, gap);
                    }
                }
            }
        }
        return allowed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>Ray against all solids. <paramref name="dir"/> must be normalized.</summary>
    public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out float hitDist, out Vector3 normal) =>
        Raycast(origin, dir, maxDist, out hitDist, out normal, out _);

    /// <summary>Ray against all enabled solids; <paramref name="boxIndex"/> is the arena box index that was hit.</summary>
    public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out float hitDist, out Vector3 normal, out int boxIndex)
    {
        var inv = VectorMath.SafeInverse(dir);
        hitDist = maxDist;
        normal = default;
        boxIndex = -1;
        var solids = _solids;
        for (int i = 0; i < solids.Length; i++)
        {
            if (_disabled[i]) continue;
            if (solids[i].Raycast(origin, inv, hitDist, out float t, out var n) && t < hitDist)
            {
                hitDist = t; normal = n; boxIndex = _solidToBox[i];
            }
        }
        return boxIndex >= 0;
    }

    /// <summary>Enables or disables an arena box (destructibles). Unknown or non-solid boxes are ignored.</summary>
    public void SetBoxEnabled(int boxIndex, bool enabled)
    {
        if (_boxToSolid.TryGetValue(boxIndex, out int s)) _disabled[s] = !enabled;
    }

    public bool IsBoxEnabled(int boxIndex) => !_boxToSolid.TryGetValue(boxIndex, out int s) || !_disabled[s];

    /// <summary>Surface directly below <paramref name="feet"/> (within <paramref name="maxDrop"/>), used for footstep sounds.</summary>
    public bool SurfaceBelow(Vector3 feet, float maxDrop, out SurfaceKind surface)
    {
        var origin = feet + new Vector3(0, 0.1f, 0);
        var inv = VectorMath.SafeInverse(-Vector3.UnitY);
        float best = maxDrop + 0.1f;
        int hit = -1;
        for (int i = 0; i < _solids.Length; i++)
            if (!_disabled[i] && _solids[i].Raycast(origin, inv, best, out float t, out _) && t < best) { best = t; hit = i; }
        surface = hit >= 0 ? _surfaces[hit] : SurfaceKind.Ground;
        return hit >= 0;
    }

    public bool LineOfSight(Vector3 a, Vector3 b)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return true;
        return !Raycast(a, d / len, len, out _, out _);
    }

    /// <summary>Index of a jump pad the box is touching, or -1.</summary>
    public int TouchingPad(in Aabb feet)
    {
        for (int i = 0; i < _pads.Length; i++)
            if (_pads[i].Intersects(feet)) return i;
        return -1;
    }
}
