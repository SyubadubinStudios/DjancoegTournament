using System.Numerics;

namespace Djancoeg.Core.Content;

/// <summary>
/// Partial destruction: every breakable box is cut into 1-4 pieces along its longest horizontal side, so a stall
/// table or counter breaks bit by bit instead of all at once. Applied once when an arena enters the
/// <see cref="ContentRegistry"/>, identically on server and client, so piece order is part of the content.
/// </summary>
public static class ArenaPieces
{
    public const int MaxPieces = 4;
    /// <summary>Target piece length in metres.</summary>
    public const float PieceLength = 1.1f;

    public static ArenaDef Split(ArenaDef arena)
    {
        if (arena.PiecesSplit || !arena.Boxes.Exists(b => b.Solid && b.Health > 0)) return arena with { PiecesSplit = true };
        var boxes = new List<ArenaBox>(arena.Boxes.Count + 16);
        foreach (var b in arena.Boxes)
        {
            if (!b.Solid || b.Health <= 0) { boxes.Add(b); continue; }
            boxes.AddRange(Pieces(b));
        }
        return arena with { Boxes = boxes, PiecesSplit = true };
    }

    /// <summary>The pieces one breakable box becomes (the box itself when it is short).</summary>
    public static IEnumerable<ArenaBox> Pieces(ArenaBox b)
    {
        var min = b.MinV; var max = b.MaxV;
        var size = max - min;
        int axis = size.X >= size.Z ? 0 : 2;
        float len = axis == 0 ? size.X : size.Z;
        int n = Math.Clamp((int)MathF.Round(len / PieceLength), 1, MaxPieces);
        if (n == 1) { yield return b; yield break; }
        float hp = MathF.Max(25f, MathF.Round(b.Health / n * 1.25f));
        float step = len / n;
        for (int i = 0; i < n; i++)
        {
            var a = min; var c = max;
            if (axis == 0) { a.X = min.X + step * i; c.X = i == n - 1 ? max.X : min.X + step * (i + 1); }
            else { a.Z = min.Z + step * i; c.Z = i == n - 1 ? max.Z : min.Z + step * (i + 1); }
            yield return b with { Min = [a.X, a.Y, a.Z], Max = [c.X, c.Y, c.Z], Health = hp };
        }
    }

    /// <summary>Number of destructible pieces an arena has after splitting.</summary>
    public static int CountPieces(ArenaDef arena) =>
        arena.Boxes.Where(b => b.Solid && b.Health > 0).Sum(b => arena.PiecesSplit ? 1 : Pieces(b).Count());
}
