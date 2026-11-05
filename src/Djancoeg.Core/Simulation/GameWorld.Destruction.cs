using System.Numerics;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

/// <summary>Destructible arena boxes: stalls, counters, benches and water tanks break and come back later.</summary>
public sealed partial class GameWorld
{
    public const int MaxDestructibles = 128;
    private const float RestoreSeconds = 45f;

    /// <summary>Arena box index of each destructible (network order).</summary>
    public int[] Destructibles { get; private set; } = [];
    private float[] _boxHp = [];
    /// <summary>For each destructible, the destructibles it rests on (touching from below, overlapping in XZ).</summary>
    private int[][] _supports = [];
    private float[] _boxRestore = [];
    /// <summary>Bit i set = destructible i is currently destroyed.</summary>
    public UInt128 DestroyedMask { get; private set; }

    public static UInt128 Bit(int d) => UInt128.One << d;

    private void InitDestructibles()
    {
        var list = new List<int>();
        for (int i = 0; i < Arena.Boxes.Count && list.Count < MaxDestructibles; i++)
            if (Arena.Boxes[i].Solid && Arena.Boxes[i].Health > 0) list.Add(i);
        Destructibles = [.. list];
        _boxHp = new float[list.Count];
        _boxRestore = new float[list.Count];
        for (int d = 0; d < list.Count; d++)
        {
            _boxHp[d] = Arena.Boxes[list[d]].Health;
            Collision.SetBoxEnabled(list[d], true);
        }
        DestroyedMask = UInt128.Zero;
        _supports = new int[list.Count][];
        for (int d = 0; d < list.Count; d++)
        {
            var top = Arena.Boxes[list[d]];
            var below = new List<int>();
            for (int e = 0; e < list.Count; e++)
            {
                if (e == d) continue;
                var b = Arena.Boxes[list[e]];
                bool touching = MathF.Abs(b.Max[1] - top.Min[1]) < 0.02f;
                bool overlapXZ = b.Min[0] < top.Max[0] - 0.01f && b.Max[0] > top.Min[0] + 0.01f && b.Min[2] < top.Max[2] - 0.01f && b.Max[2] > top.Min[2] + 0.01f;
                if (touching && overlapXZ) below.Add(e);
            }
            _supports[d] = [.. below];
        }
    }

    private bool IsDestroyed(int d) => (DestroyedMask & Bit(d)) != UInt128.Zero;

    /// <summary>True when the piece rests only on destructible pieces and all of them are gone.</summary>
    private bool LostSupport(int d)
    {
        var s = _supports[d];
        if (s.Length == 0) return false;
        foreach (int e in s) if (!IsDestroyed(e)) return false;
        return true;
    }

    private int DestructibleOf(int boxIndex)
    {
        if (boxIndex < 0) return -1;
        for (int d = 0; d < Destructibles.Length; d++) if (Destructibles[d] == boxIndex) return d;
        return -1;
    }

    /// <summary>Damages the box a shot hit (if it is destructible).</summary>
    public void DamageBox(int boxIndex, float amount, Vector3 dir)
    {
        int d = DestructibleOf(boxIndex);
        if (d < 0 || (DestroyedMask & Bit(d)) != 0) return;
        _boxHp[d] -= amount;
        if (_boxHp[d] > 0) return;
        DestroyedMask |= Bit(d);
        _boxRestore[d] = RestoreSeconds;
        Collision.SetBoxEnabled(boxIndex, false);
        var b = Arena.Boxes[boxIndex];
        Push(new GameEvent { Type = GameEventType.BoxDestroyed, Value = (short)d, Position = (b.MinV + b.MaxV) * 0.5f, Vector = dir });
        // Whatever stood on this piece comes down with it.
        for (int up = 0; up < Destructibles.Length; up++)
            if (!IsDestroyed(up) && LostSupport(up)) DamageBox(Destructibles[up], float.MaxValue, new Vector3(0, -1, 0));
    }

    private void ExplodeOnBoxes(Vector3 center, float radius, float damage)
    {
        for (int d = 0; d < Destructibles.Length; d++)
        {
            if ((DestroyedMask & Bit(d)) != 0) continue;
            var b = Arena.Boxes[Destructibles[d]];
            var box = new Aabb(b.MinV, b.MaxV);
            float dist = Vector3.Distance(box.ClosestPoint(center), center);
            if (dist > radius) continue;
            DamageBox(Destructibles[d], damage * (1 - dist / radius * 0.5f) * 1.5f, Vector3.Normalize(box.Center - center + new Vector3(0, 0.5f, 0)));
        }
    }

    private void UpdateDestructibles(float dt)
    {
        if (DestroyedMask == UInt128.Zero) return;
        for (int d = 0; d < Destructibles.Length; d++)
        {
            if ((DestroyedMask & Bit(d)) == 0) continue;
            _boxRestore[d] -= dt;
            if (_boxRestore[d] > 0) continue;
            var b = Arena.Boxes[Destructibles[d]];
            var box = new Aabb(b.MinV, b.MaxV);
            // A layer can't come back while the piece it rests on is still broken.
            bool blocked = LostSupport(d);
            for (int i = 0; i < Players.Length && !blocked; i++) if (Players[i].Active && Players[i].Alive && BodyBox(Players[i]).Intersects(box)) blocked = true;
            if (blocked) { _boxRestore[d] = 2f; continue; }
            _boxHp[d] = b.Health;
            DestroyedMask &= ~Bit(d);
            Collision.SetBoxEnabled(Destructibles[d], true);
            Push(new GameEvent { Type = GameEventType.BoxRestored, Value = (short)d, Position = box.Center });
        }
    }
}
