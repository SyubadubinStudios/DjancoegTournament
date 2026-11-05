using System.Numerics;
using Raylib_cs;

namespace Djancoeg.Client.Ui;

/// <summary>
/// Visual identity "Terpal &amp; Running Text": market-tarp cobalt, minibus yellow, chili red,
/// chalk white and asphalt black; hand-painted sign shadows; license-plate HUD panels.
/// </summary>
public static class Theme
{
    public static readonly Color Tarp = Hex(0x1D3FBB);       // terpal biru
    public static readonly Color TarpDeep = Hex(0x10246F);
    public static readonly Color TarpLight = Hex(0x3A5BD9);
    public static readonly Color Kuning = Hex(0xFFC21A);     // kuning angkot
    public static readonly Color Cabe = Hex(0xE3242B);       // merah cabe
    public static readonly Color Kapur = Hex(0xF7F3E8);      // putih kapur
    public static readonly Color Aspal = Hex(0x14110F);      // hitam aspal
    public static readonly Color Daun = Hex(0x1FA64A);       // hijau daun (positive)
    public static readonly Color LedOff = Hex(0x3A0D0F);

    public static Font Display, Body, BodyBold, Mono;
    public static bool Loaded { get; private set; }

    public static Color Hex(uint rgb, byte a = 255) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);
    public static Color Rgba(uint rgba) => new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
    public static Color Fade(Color c, float a) => new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * a, 0, 255));
    public static Color Lerp(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)(a.A + (b.A - a.A) * t));

    public static void Load(string assetsDir)
    {
        string f(string n) => Path.Combine(assetsDir, "fonts", n);
        Display = LoadFont(f("Bungee-Regular.ttf"), 128);
        Body = LoadFont(f("PlusJakartaSans-Medium.ttf"), 64);
        BodyBold = LoadFont(f("PlusJakartaSans-ExtraBold.ttf"), 64);
        Mono = LoadFont(f("SpaceMono-Bold.ttf"), 48);
        Loaded = true;
    }

    private static Font LoadFont(string path, int size)
    {
        if (!File.Exists(path)) return Raylib.GetFontDefault();
        // Latin-1 so names like "Pak Lurah" and accented mod names render.
        var codepoints = new int[224];
        for (int i = 0; i < codepoints.Length; i++) codepoints[i] = 32 + i;
        var font = Raylib.LoadFontEx(path, size, codepoints, codepoints.Length);
        Raylib.GenTextureMipmaps(ref font.Texture);
        Raylib.SetTextureFilter(font.Texture, TextureFilter.Trilinear);
        return font;
    }

    public static void Unload()
    {
        if (!Loaded) return;
        Raylib.UnloadFont(Display); Raylib.UnloadFont(Body); Raylib.UnloadFont(BodyBold); Raylib.UnloadFont(Mono);
        Loaded = false;
    }

    // ------------------------------------------------------------------ text helpers

    public static Vector2 Measure(Font font, string text, float size) => Raylib.MeasureTextEx(font, text, size, Spacing(font, size));
    private static float Spacing(Font font, float size) => font.Texture.Id == Display.Texture.Id ? size * 0.02f : 0f;

    public static void Text(Font font, string text, Vector2 pos, float size, Color color) =>
        Raylib.DrawTextEx(font, text, pos, size, Spacing(font, size), color);

    public static void TextCentered(Font font, string text, Vector2 center, float size, Color color)
    {
        var m = Measure(font, text, size);
        Text(font, text, new Vector2(center.X - m.X / 2, center.Y - m.Y / 2), size, color);
    }

    public static void TextRight(Font font, string text, Vector2 rightTop, float size, Color color)
    {
        var m = Measure(font, text, size);
        Text(font, text, new Vector2(rightTop.X - m.X, rightTop.Y), size, color);
    }

    /// <summary>Hand-painted sign lettering: hard offset shadow in asphalt (and optional chili under-shadow).</summary>
    public static void SignText(string text, Vector2 pos, float size, Color color, bool doubleShadow = false, float rotation = 0)
    {
        float o = MathF.Max(2, size * 0.06f);
        var sp = Spacing(Display, size);
        if (doubleShadow) Raylib.DrawTextPro(Display, text, pos + new Vector2(o * 2, o * 2), Vector2.Zero, rotation, size, sp, Cabe);
        Raylib.DrawTextPro(Display, text, pos + new Vector2(o, o), Vector2.Zero, rotation, size, sp, Aspal);
        Raylib.DrawTextPro(Display, text, pos, Vector2.Zero, rotation, size, sp, color);
    }

    /// <summary>Wraps text into lines that fit <paramref name="width"/>. Allocates; call for UI copy only.</summary>
    public static List<string> Wrap(Font font, string text, float size, float width)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var words = para.Split(' ');
            string line = "";
            foreach (var w in words)
            {
                string attempt = line.Length == 0 ? w : line + " " + w;
                if (Measure(font, attempt, size).X > width && line.Length > 0) { lines.Add(line); line = w; }
                else line = attempt;
            }
            lines.Add(line);
        }
        return lines;
    }

    public static float Paragraph(Font font, string text, Vector2 pos, float size, float width, Color color, float lineHeight = 1.35f)
    {
        float y = pos.Y;
        foreach (var l in Wrap(font, text, size, width))
        {
            Text(font, l, new Vector2(pos.X, y), size, color);
            y += size * lineHeight;
        }
        return y;
    }
}
