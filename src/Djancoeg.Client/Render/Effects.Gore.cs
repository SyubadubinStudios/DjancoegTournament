using System.Numerics;
using Djancoeg.Core.Simulation;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Render;

/// <summary>
/// Heavier effects: blood and gore (or confetti in family mode), bullet-hole and splat decals,
/// impact dust, muzzle smoke and multi-stage explosions (fireball, smoke column, shockwave, debris, scorch).
/// </summary>
public sealed partial class Effects
{
    private struct Decal { public Vector3 Pos, Normal; public float Size, Life, MaxLife; public Color Color; }
    private struct Ring { public Vector3 Pos; public float Radius, MaxRadius, Life, MaxLife; public Color Color; }

    private readonly Decal[] _decals = new Decal[384];
    private readonly Ring[] _rings = new Ring[32];
    private int _dNext, _rNext;

    /// <summary>True = blood and gore; false = confetti (family friendly).</summary>
    public bool Blood { get; set; } = true;
    public CollisionWorld? World { get; set; }

    public void AddDecal(Vector3 pos, Vector3 normal, float size, Color color, float life = 25f)
    {
        _decals[_dNext] = new Decal { Pos = pos + normal * 0.012f, Normal = normal, Size = size, Color = color, Life = life, MaxLife = life };
        _dNext = (_dNext + 1) % _decals.Length;
    }

    /// <summary>Projects a splat onto the floor below <paramref name="pos"/> (if any).</summary>
    public void SplatBelow(Vector3 pos, float size, Color color, float life = 25f)
    {
        if (World is null) return;
        var from = pos + new Vector3(_rng.Range(-0.5f, 0.5f), 0.3f, _rng.Range(-0.5f, 0.5f));
        if (World.Raycast(from, -Vector3.UnitY, 6f, out float d, out var n) && n.Y > 0.5f)
            AddDecal(from - new Vector3(0, d, 0), Vector3.UnitY, size * _rng.Range(0.7f, 1.3f), color, life);
    }

    /// <summary>Blood spray from a hit (direction = shot direction). Falls back to confetti when gore is off.</summary>
    public void BloodHit(Vector3 pos, Vector3 dir, Color color, float amount)
    {
        if (!Blood) { Burst(pos, (int)(6 * amount), 3f, null, 0.5f, 0.04f); return; }
        int n = (int)(10 * amount);
        for (int i = 0; i < n; i++)
        {
            var v = (dir * _rng.Range(2f, 6f)) + _rng.InsideUnitSphere() * 2.5f + new Vector3(0, 1.5f, 0);
            Emit(pos, v, Shade(color, _rng.Range(0.7f, 1.1f)), _rng.Range(0.4f, 0.9f), _rng.Range(0.03f, 0.07f), 14f, 0.5f);
        }
        Emit(pos, Vector3.Zero, new Color(color.R, color.G, color.B, (byte)110), 0.35f, 0.45f, 0f, 0f); // mist
        if (amount > 0.6f) SplatBelow(pos, 0.5f + amount * 0.4f, Shade(color, 0.6f));
        // Wall splat behind the target.
        if (World is not null && World.Raycast(pos, dir, 3f, out float d, out var nrm) && nrm != Vector3.Zero)
            AddDecal(pos + dir * d, nrm, 0.35f + amount * 0.3f, Shade(color, 0.65f));
    }

    /// <summary>Death burst: blood fountain, meaty chunks and a pool. <paramref name="big"/> for gibs and giant creeps.</summary>
    public void Gore(Vector3 pos, Color color, bool big)
    {
        if (!Blood) { Burst(pos, big ? 90 : 40, big ? 9f : 6f, null, 1.4f, 0.07f); return; }
        int drops = big ? 120 : 50;
        for (int i = 0; i < drops; i++)
        {
            var v = _rng.InsideUnitSphere() * (big ? 9f : 5f) + new Vector3(0, big ? 6f : 3.5f, 0);
            Emit(pos, v, Shade(color, _rng.Range(0.6f, 1.1f)), _rng.Range(0.6f, 1.3f), _rng.Range(0.04f, 0.09f), 16f, 0.4f);
        }
        int chunks = big ? 16 : 7;
        for (int i = 0; i < chunks; i++)
        {
            var v = _rng.InsideUnitSphere() * (big ? 8f : 5f) + new Vector3(0, 5f, 0);
            Emit(pos, v, Shade(color, 0.55f), _rng.Range(1.2f, 2.2f), _rng.Range(0.12f, big ? 0.3f : 0.2f), 18f, 0.3f);
        }
        for (int i = 0; i < (big ? 7 : 3); i++) SplatBelow(pos, big ? 1.6f : 0.9f, Shade(color, 0.55f), 35f);
        AddShake(big ? 0.4f : 0.15f);
    }

    /// <summary>Where a hitscan shot lands: sparks, dust and a bullet hole.</summary>
    public void Impact(Vector3 from, Vector3 to, Color tint)
    {
        if (World is null) return;
        var dir = to - from;
        float len = dir.Length();
        if (len < 0.01f) return;
        dir /= len;
        if (!World.Raycast(from, dir, len + 0.2f, out float d, out var n) || n == Vector3.Zero) return;
        var hit = from + dir * d;
        AddDecal(hit, n, 0.09f, new Color(25, 22, 20, 230), 18f);
        for (int i = 0; i < 4; i++) Spark(hit + n * 0.05f, tint);
        for (int i = 0; i < 3; i++) Emit(hit + n * 0.05f, n * _rng.Range(0.5f, 1.5f) + _rng.InsideUnitSphere() * 0.4f, new Color(170, 160, 145, 160), _rng.Range(0.4f, 0.8f), 0.1f, -0.3f, 2.5f);
    }

    public void MuzzleSmoke(Vector3 pos, Vector3 dir)
    {
        for (int i = 0; i < 3; i++)
            Emit(pos, dir * _rng.Range(0.5f, 1.5f) + new Vector3(0, 0.4f, 0) + _rng.InsideUnitSphere() * 0.2f, new Color(210, 205, 195, 110), _rng.Range(0.3f, 0.6f), 0.08f, -0.4f, 2f);
    }

    public void Dust(Vector3 pos, int count = 10, float speed = 2.5f)
    {
        for (int i = 0; i < count; i++)
        {
            var v = _rng.InsideUnitSphere() * speed;
            v.Y = MathF.Abs(v.Y) * 0.6f;
            Emit(pos + new Vector3(0, 0.1f, 0), v, new Color(160, 140, 115, 150), _rng.Range(0.6f, 1.2f), _rng.Range(0.15f, 0.3f), -0.2f, 1.6f);
        }
    }

    /// <summary>Earth bursting upwards (giant worm eruptions, creeps emerging).</summary>
    public void Eruption(Vector3 pos, float radius)
    {
        for (int i = 0; i < 60; i++)
        {
            var v = _rng.InsideUnitSphere() * 4f + new Vector3(0, _rng.Range(6f, 12f), 0);
            Emit(pos + new Vector3(_rng.Range(-radius, radius) * 0.5f, 0, _rng.Range(-radius, radius) * 0.5f), v, _rng.Chance(0.5f) ? new Color(110, 84, 60, 255) : new Color(80, 64, 48, 255), _rng.Range(1f, 1.8f), _rng.Range(0.1f, 0.3f), 18f, 0.3f);
        }
        Dust(pos, 25, 5f);
        AddRing(pos, radius * 1.6f, new Color(140, 110, 80, 200), 0.6f);
        AddDecal(pos + new Vector3(0, 0.02f, 0), Vector3.UnitY, radius * 1.5f, new Color(60, 45, 30, 220), 40f);
        AddShake(0.6f);
    }

    public void AddRing(Vector3 pos, float maxRadius, Color color, float life)
    {
        _rings[_rNext] = new Ring { Pos = pos, MaxRadius = maxRadius, Color = color, Life = life, MaxLife = life };
        _rNext = (_rNext + 1) % _rings.Length;
    }

    /// <summary>Extra explosion layers on top of the fireball: smoke column, shockwave, debris and scorch.</summary>
    public void ExplosionExtras(Vector3 pos, float radius)
    {
        for (int i = 0; i < 16; i++)
            Emit(pos + _rng.InsideUnitSphere() * 0.4f, new Vector3(_rng.Range(-0.6f, 0.6f), _rng.Range(1.5f, 3.5f), _rng.Range(-0.6f, 0.6f)), new Color(55, 50, 48, 190), _rng.Range(1.5f, 2.8f), _rng.Range(0.35f, 0.7f), -0.8f, 0.6f);
        for (int i = 0; i < 12; i++)
            Emit(pos, _rng.InsideUnitSphere() * 7f + new Vector3(0, 5f, 0), new Color(40, 36, 32, 255), _rng.Range(1f, 2f), _rng.Range(0.06f, 0.14f), 16f, 0.2f);
        AddRing(pos, radius * 2.2f, new Color(255, 220, 160, 200), 0.35f);
        SplatBelow(pos + new Vector3(0, 0.3f, 0), radius * 1.2f, new Color(20, 18, 16, 210), 30f);
    }

    private static Color Shade(Color c, float k) => new((byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255), c.A);

    private void UpdateGore(float dt)
    {
        for (int i = 0; i < _decals.Length; i++) if (_decals[i].Life > 0) _decals[i].Life -= dt;
        for (int i = 0; i < _rings.Length; i++)
        {
            ref var r = ref _rings[i];
            if (r.Life <= 0) continue;
            r.Life -= dt;
            r.Radius = r.MaxRadius * (1 - r.Life / r.MaxLife);
        }
    }

    private void DrawGore()
    {
        for (int i = 0; i < _decals.Length; i++)
        {
            ref var d = ref _decals[i];
            if (d.Life <= 0) continue;
            var c = d.Color;
            if (d.Life < 3f) c.A = (byte)(c.A * d.Life / 3f);
            var n = d.Normal;
            var size = MathF.Abs(n.Y) > 0.5f ? new Vector3(d.Size, 0.01f, d.Size) : MathF.Abs(n.X) > 0.5f ? new Vector3(0.01f, d.Size, d.Size) : new Vector3(d.Size, d.Size, 0.01f);
            DrawCubeV(d.Pos, size, c);
        }
        for (int i = 0; i < _rings.Length; i++)
        {
            ref var r = ref _rings[i];
            if (r.Life <= 0) continue;
            var c = r.Color;
            c.A = (byte)(c.A * r.Life / r.MaxLife);
            DrawCylinderWiresEx(r.Pos + new Vector3(0, 0.1f, 0), r.Pos + new Vector3(0, 0.35f, 0), r.Radius, r.Radius * 1.05f, 28, c);
        }
    }

    private void ClearGore()
    {
        Array.Clear(_decals);
        Array.Clear(_rings);
    }
}
