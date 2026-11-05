using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Djancoeg.Net;

/// <summary>Allocation-free little-endian writer over a caller-provided span.</summary>
public ref struct NetWriter(Span<byte> buffer)
{
    private readonly Span<byte> _buf = buffer;
    public int Position { get; private set; }
    public readonly int Remaining => _buf.Length - Position;
    public bool Overflow { get; private set; }
    public readonly ReadOnlySpan<byte> Written => _buf[..Position];

    private Span<byte> Take(int n)
    {
        if (Position + n > _buf.Length) { Overflow = true; return Span<byte>.Empty; }
        var s = _buf.Slice(Position, n);
        Position += n;
        return s;
    }

    public void U8(byte v) { var s = Take(1); if (!s.IsEmpty) s[0] = v; }
    /// <summary>Overwrites a byte written earlier (used to patch counts once items are known).</summary>
    public readonly void PatchU8(int position, byte v) => _buf[position] = v;
    public void I8(sbyte v) => U8((byte)v);
    public void Bool(bool v) => U8(v ? (byte)1 : (byte)0);
    public void U16(ushort v) { var s = Take(2); if (!s.IsEmpty) BinaryPrimitives.WriteUInt16LittleEndian(s, v); }
    public void I16(short v) { var s = Take(2); if (!s.IsEmpty) BinaryPrimitives.WriteInt16LittleEndian(s, v); }
    public void U32(uint v) { var s = Take(4); if (!s.IsEmpty) BinaryPrimitives.WriteUInt32LittleEndian(s, v); }
    public void U64(ulong v) { var s = Take(8); if (!s.IsEmpty) BinaryPrimitives.WriteUInt64LittleEndian(s, v); }
    public void F32(float v) { var s = Take(4); if (!s.IsEmpty) BinaryPrimitives.WriteSingleLittleEndian(s, v); }

    /// <summary>UTF-8 string with a one-byte length prefix (max 255 bytes, truncated safely).</summary>
    public void Str(string? v)
    {
        v ??= "";
        Span<byte> tmp = stackalloc byte[255];
        int n = Encoding.UTF8.GetMaxByteCount(v.Length) <= 255 ? Encoding.UTF8.GetBytes(v, tmp) : SafeEncode(v, tmp);
        U8((byte)n);
        var s = Take(n);
        if (!s.IsEmpty) tmp[..n].CopyTo(s);
    }

    private static int SafeEncode(string v, Span<byte> tmp)
    {
        var enc = Encoding.UTF8.GetEncoder();
        enc.Convert(v.AsSpan(), tmp, true, out _, out int bytes, out _);
        return bytes;
    }

    /// <summary>Position quantized to 1/64 m in a signed 16-bit range (±512 m).</summary>
    public void Pos(Vector3 v) { I16(Quant.Pos(v.X)); I16(Quant.Pos(v.Y)); I16(Quant.Pos(v.Z)); }
    public void Vel(Vector3 v) { I16(Quant.Vel(v.X)); I16(Quant.Vel(v.Y)); I16(Quant.Vel(v.Z)); }
    public void Angle(float a) => U16(Quant.Angle(a));
}

/// <summary>Bounds-checked reader. Reading past the end sets <see cref="Error"/> and returns zeros.</summary>
public ref struct NetReader(ReadOnlySpan<byte> buffer)
{
    private readonly ReadOnlySpan<byte> _buf = buffer;
    public int Position { get; private set; }
    public bool Error { get; private set; }
    public readonly int Remaining => _buf.Length - Position;

    private ReadOnlySpan<byte> Take(int n)
    {
        if (Position + n > _buf.Length) { Error = true; Position = _buf.Length; return ReadOnlySpan<byte>.Empty; }
        var s = _buf.Slice(Position, n);
        Position += n;
        return s;
    }

    public byte U8() { var s = Take(1); return s.IsEmpty ? (byte)0 : s[0]; }
    public sbyte I8() => (sbyte)U8();
    public bool Bool() => U8() != 0;
    public ushort U16() { var s = Take(2); return s.IsEmpty ? (ushort)0 : BinaryPrimitives.ReadUInt16LittleEndian(s); }
    public short I16() { var s = Take(2); return s.IsEmpty ? (short)0 : BinaryPrimitives.ReadInt16LittleEndian(s); }
    public uint U32() { var s = Take(4); return s.IsEmpty ? 0u : BinaryPrimitives.ReadUInt32LittleEndian(s); }
    public ulong U64() { var s = Take(8); return s.IsEmpty ? 0ul : BinaryPrimitives.ReadUInt64LittleEndian(s); }
    public float F32()
    {
        var s = Take(4);
        if (s.IsEmpty) return 0;
        float f = BinaryPrimitives.ReadSingleLittleEndian(s);
        if (!float.IsFinite(f)) { Error = true; return 0; }
        return f;
    }

    public string Str()
    {
        int n = U8();
        var s = Take(n);
        return s.IsEmpty ? "" : Encoding.UTF8.GetString(s);
    }

    public Vector3 Pos() => new(Quant.UnPos(I16()), Quant.UnPos(I16()), Quant.UnPos(I16()));
    public Vector3 Vel() => new(Quant.UnVel(I16()), Quant.UnVel(I16()), Quant.UnVel(I16()));
    public float Angle() => Quant.UnAngle(U16());
}

public static class Quant
{
    public static short Pos(float v) => (short)Math.Clamp(MathF.Round(v * 64f), short.MinValue, short.MaxValue);
    public static float UnPos(short v) => v / 64f;
    public static short Vel(float v) => (short)Math.Clamp(MathF.Round(v * 64f), short.MinValue, short.MaxValue);
    public static float UnVel(short v) => v / 64f;
    public static ushort Angle(float a)
    {
        float t = a / MathF.Tau;
        t -= MathF.Floor(t);
        return (ushort)(t * 65536f);
    }
    public static float UnAngle(ushort v)
    {
        float a = v / 65536f * MathF.Tau;
        return a > MathF.PI ? a - MathF.Tau : a;
    }
    public static short Pitch(float p) => (short)Math.Clamp(MathF.Round(p * 10000f), short.MinValue, short.MaxValue);
    public static float UnPitch(short v) => v / 10000f;
}
