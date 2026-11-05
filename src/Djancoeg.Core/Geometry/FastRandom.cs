using System.Numerics;

namespace Djancoeg.Core.Geometry;

/// <summary>Allocation-free xorshift128+ PRNG. Not thread safe; one per simulation.</summary>
public struct FastRandom
{
    private ulong _s0, _s1;

    public FastRandom(ulong seed)
    {
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        if (_s0 == 0 && _s1 == 0) _s1 = 1;
    }

    private static ulong SplitMix(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextULong()
    {
        ulong s1 = _s0, s0 = _s1;
        _s0 = s0;
        s1 ^= s1 << 23;
        _s1 = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
        return _s1 + s0;
    }

    /// <summary>Uniform float in [0,1).</summary>
    public float NextFloat() => (NextULong() >> 40) * (1f / (1UL << 24));
    public float Range(float min, float max) => min + (max - min) * NextFloat();
    public int Next(int maxExclusive) => maxExclusive <= 0 ? 0 : (int)(NextULong() % (ulong)maxExclusive);
    public bool Chance(float p) => NextFloat() < p;

    public Vector3 InsideUnitSphere()
    {
        while (true)
        {
            var v = new Vector3(Range(-1, 1), Range(-1, 1), Range(-1, 1));
            if (v.LengthSquared() <= 1f) return v;
        }
    }
}
