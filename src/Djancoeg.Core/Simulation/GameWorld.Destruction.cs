using System.Numerics;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

/// <summary>Destructible arena boxes: stalls, counters, benches and water tanks break and come back later.</summary>
public sealed partial class GameWorld
{
    public const int MaxDestructibles = 64;
    private const float RestoreSeconds = 45f;

    /// <summary>Arena box index of each destructible (network order).</summary>
    public int[] Destructibles { get; private set; } = [];
    private float[] _boxHp = [];
    private float[] _boxRestore = [];
    /// <summary>Bit i set = destructible i is currently destroyed.</summary>
    public ulong DestroyedMask { get; private set; }

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
        DestroyedMask = 0;
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
        if (d < 0 || (DestroyedMask & (1UL << d)) != 0) return;
        _boxHp[d] -= amount;
        if (_boxHp[d] > 0) return;
        DestroyedMask |= 1UL << d;
        _boxRestore[d] = RestoreSeconds;
        Collision.SetBoxEnabled(boxIndex, false);
        var b = Arena.Boxes[boxIndex];
        Push(new GameEvent { Type = GameEventType.BoxDestroyed, Value = (short)d, Position = (b.MinV + b.MaxV) * 0.5f, Vector = dir });
    }

    private void ExplodeOnBoxes(Vector3 center, float radius, float damage)
    {
        for (int d = 0; d < Destructibles.Length; d++)
        {
            if ((DestroyedMask & (1UL << d)) != 0) continue;
            var b = Arena.Boxes[Destructibles[d]];
            var box = new Aabb(b.MinV, b.MaxV);
            float dist = Vector3.Distance(box.ClosestPoint(center), center);
            if (dist > radius) continue;
            DamageBox(Destructibles[d], damage * (1 - dist / radius * 0.5f) * 1.5f, Vector3.Normalize(box.Center - center + new Vector3(0, 0.5f, 0)));
        }
    }

    private void UpdateDestructibles(float dt)
    {
        if (DestroyedMask == 0) return;
        for (int d = 0; d < Destructibles.Length; d++)
        {
            if ((DestroyedMask & (1UL << d)) == 0) continue;
            _boxRestore[d] -= dt;
            if (_boxRestore[d] > 0) continue;
            var b = Arena.Boxes[Destructibles[d]];
            var box = new Aabb(b.MinV, b.MaxV);
            bool blocked = false;
            for (int i = 0; i < Players.Length && !blocked; i++) if (Players[i].Active && Players[i].Alive && BodyBox(Players[i]).Intersects(box)) blocked = true;
            if (blocked) { _boxRestore[d] = 2f; continue; }
            _boxHp[d] = b.Health;
            DestroyedMask &= ~(1UL << d);
            Collision.SetBoxEnabled(Destructibles[d], true);
            Push(new GameEvent { Type = GameEventType.BoxRestored, Value = (short)d, Position = box.Center });
        }
    }
}
