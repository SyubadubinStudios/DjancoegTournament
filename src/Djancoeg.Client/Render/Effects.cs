using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Render;

/// <summary>
/// Pooled, allocation-free visual effects: confetti/kerupuk particles instead of gore, smoke,
/// fire, tracers, dynamic explosion lights, floating damage numbers and screen shake.
/// </summary>
public sealed class Effects
{
    private struct Particle
    {
        public Vector3 Pos, Vel;
        public float Life, MaxLife, Size, Gravity, Drag;
        public Color Color;
        public bool Glow;
    }

    private struct Tracer { public Vector3 A, B; public float Life, MaxLife; public Color Color; public float Width; }
    private struct Flash { public Vector3 Pos; public float Life, MaxLife, Radius; public Vector3 Color; }
    public struct FloatingText { public Vector3 Pos; public float Life; public string Text; public Color Color; public float Size; }

    private readonly Particle[] _particles = new Particle[4096];
    private readonly Tracer[] _tracers = new Tracer[256];
    private readonly Flash[] _flashes = new Flash[32];
    private readonly FloatingText[] _texts = new FloatingText[64];
    private int _pNext, _tNext, _fNext, _xNext;
    private Core.Geometry.FastRandom _rng = new(1234);

    public float Shake { get; private set; }
    public ReadOnlySpan<FloatingText> Texts => _texts;

    private static readonly Color[] Confetti =
    [
        new(227, 36, 43, 255), new(255, 194, 26, 255), new(29, 63, 187, 255), new(31, 166, 74, 255), new(247, 243, 232, 255), new(255, 112, 30, 255),
    ];

    public void AddShake(float amount) => Shake = MathF.Min(1.2f, Shake + amount);

    public void Emit(Vector3 pos, Vector3 vel, Color color, float life, float size, float gravity = 9f, float drag = 1f, bool glow = false)
    {
        ref var p = ref _particles[_pNext];
        _pNext = (_pNext + 1) % _particles.Length;
        p = new Particle { Pos = pos, Vel = vel, Color = color, Life = life, MaxLife = life, Size = size, Gravity = gravity, Drag = drag, Glow = glow };
    }

    public void Burst(Vector3 pos, int count, float speed, Color? color = null, float life = 0.8f, float size = 0.06f)
    {
        for (int i = 0; i < count; i++)
        {
            var dir = _rng.InsideUnitSphere();
            var c = color ?? Confetti[_rng.Next(Confetti.Length)];
            Emit(pos, dir * speed * _rng.Range(0.4f, 1f) + new Vector3(0, speed * 0.4f, 0), c, life * _rng.Range(0.6f, 1.2f), size * _rng.Range(0.7f, 1.3f));
        }
    }

    public void Explosion(Vector3 pos, float radius, bool spicy)
    {
        int n = (int)(40 + radius * 25);
        for (int i = 0; i < n; i++)
        {
            var dir = _rng.InsideUnitSphere();
            float t = _rng.NextFloat();
            var c = t < 0.4f ? new Color(255, 230, 120, 255) : t < 0.8f ? new Color(255, 120, 30, 255) : new Color(227, 36, 43, 255);
            Emit(pos, dir * radius * 3.2f * _rng.Range(0.3f, 1f), c, _rng.Range(0.25f, 0.6f), _rng.Range(0.12f, 0.28f), -1f, 2.5f, glow: true);
        }
        for (int i = 0; i < 18; i++)
        {
            var dir = _rng.InsideUnitSphere();
            Emit(pos, dir * 2f + new Vector3(0, 1.5f, 0), new Color(70, 64, 60, 200), _rng.Range(0.8f, 1.6f), _rng.Range(0.3f, 0.5f), -0.6f, 1.2f);
        }
        if (spicy) Burst(pos, 25, 7f, new Color(200, 20, 20, 255), 1.2f, 0.05f); // chili flakes
        AddFlash(pos, radius * 4f, new Vector3(1.6f, 0.9f, 0.4f), 0.35f);
    }

    public void Smoke(Vector3 pos, float amount = 1f)
    {
        Emit(pos, _rng.InsideUnitSphere() * 0.4f + new Vector3(0, 0.4f, 0), new Color(200, 196, 190, 150), 0.7f * amount, 0.12f, -0.5f, 1.5f);
    }

    public void Spark(Vector3 pos, Color c) =>
        Emit(pos, _rng.InsideUnitSphere() * 2f, c, 0.25f, 0.05f, 4f, 2f, glow: true);

    public void AddTracer(Vector3 a, Vector3 b, Color c, float width = 0.025f, float life = 0.12f)
    {
        _tracers[_tNext] = new Tracer { A = a, B = b, Color = c, Life = life, MaxLife = life, Width = width };
        _tNext = (_tNext + 1) % _tracers.Length;
    }

    public void AddFlash(Vector3 pos, float radius, Vector3 color, float life)
    {
        _flashes[_fNext] = new Flash { Pos = pos, Radius = radius, Color = color, Life = life, MaxLife = life };
        _fNext = (_fNext + 1) % _flashes.Length;
    }

    public void AddText(Vector3 pos, string text, Color color, float size = 26)
    {
        _texts[_xNext] = new FloatingText { Pos = pos, Text = text, Color = color, Life = 1.1f, Size = size };
        _xNext = (_xNext + 1) % _texts.Length;
    }

    public void Update(float dt)
    {
        Shake = MathF.Max(0, Shake - dt * 2.5f);
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            if (p.Life <= 0) continue;
            p.Life -= dt;
            p.Vel.Y -= p.Gravity * dt;
            p.Vel *= MathF.Max(0, 1f - p.Drag * dt);
            p.Pos += p.Vel * dt;
        }
        for (int i = 0; i < _tracers.Length; i++) if (_tracers[i].Life > 0) _tracers[i].Life -= dt;
        for (int i = 0; i < _flashes.Length; i++) if (_flashes[i].Life > 0) _flashes[i].Life -= dt;
        for (int i = 0; i < _texts.Length; i++)
        {
            ref var t = ref _texts[i];
            if (t.Life <= 0) continue;
            t.Life -= dt;
            t.Pos.Y += dt * 1.2f;
        }
    }

    /// <summary>Feeds the brightest active flashes into the world shader's light list.</summary>
    public void PushLights(WorldShader shader)
    {
        for (int i = 0; i < _flashes.Length; i++)
        {
            ref var f = ref _flashes[i];
            if (f.Life <= 0) continue;
            float k = f.Life / f.MaxLife;
            shader.AddLight(f.Pos, f.Radius, f.Color * k * 2f);
        }
    }

    public void Draw(Vector3 camPos)
    {
        for (int i = 0; i < _tracers.Length; i++)
        {
            ref var t = ref _tracers[i];
            if (t.Life <= 0) continue;
            float k = t.Life / t.MaxLife;
            var c = new Color(t.Color.R, t.Color.G, t.Color.B, (byte)(220 * k));
            DrawCylinderEx(t.A, t.B, t.Width * k, t.Width * k, 4, c);
        }
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            if (p.Life <= 0) continue;
            float k = p.Life / p.MaxLife;
            float s = p.Glow ? p.Size * (0.4f + k) : p.Size;
            var c = p.Color;
            c.A = (byte)(c.A * MathF.Min(1, k * 2));
            DrawCubeV(p.Pos, new Vector3(s), c);
        }
    }

    public void Clear()
    {
        Array.Clear(_particles); Array.Clear(_tracers); Array.Clear(_flashes); Array.Clear(_texts);
        Shake = 0;
    }
}
