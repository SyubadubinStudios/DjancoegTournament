using System.Numerics;
using System.Text;
using Raylib_cs;

namespace Djancoeg.Client.Ui;

/// <summary>
/// Small immediate-mode UI toolkit. Every interactive widget gets a focus slot in call order,
/// so menus work with mouse, keyboard (arrows/Tab/Enter) and are announced consistently.
/// </summary>
public sealed class Gui
{
    private int _slot;
    private int _slotCount;
    private bool _usingKeyboard;
    private Vector2 _lastMouse;
    private int _dragSlot = -1;
    private readonly Dictionary<int, float> _hoverAnim = [];

    public int Focus { get; set; }
    public Vector2 Mouse { get; private set; }
    public bool Clicked { get; private set; }
    public bool MouseDown { get; private set; }
    public bool Confirm { get; private set; }
    public bool Back { get; private set; }
    public float Time { get; private set; }
    public float Dt { get; private set; }
    public int TextFocus { get; set; } = -1;
    public Action? OnHover { get; set; }
    public Action? OnClick { get; set; }

    public void BeginFrame(float dt)
    {
        Dt = dt;
        Time += dt;
        Mouse = Raylib.GetMousePosition();
        if (Vector2.DistanceSquared(Mouse, _lastMouse) > 4) _usingKeyboard = false;
        _lastMouse = Mouse;
        Clicked = Raylib.IsMouseButtonPressed(MouseButton.Left);
        MouseDown = Raylib.IsMouseButtonDown(MouseButton.Left);
        if (!MouseDown) _dragSlot = -1;
        Confirm = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
        Back = Raylib.IsKeyPressed(KeyboardKey.Escape);

        int prevFocus = Focus;
        if (_slotCount > 0)
        {
            bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift);
            if (Raylib.IsKeyPressed(KeyboardKey.Down) || (Raylib.IsKeyPressed(KeyboardKey.Tab) && !shift)) { Focus = (Focus + 1) % _slotCount; _usingKeyboard = true; }
            if (Raylib.IsKeyPressed(KeyboardKey.Up) || (Raylib.IsKeyPressed(KeyboardKey.Tab) && shift)) { Focus = (Focus - 1 + _slotCount) % _slotCount; _usingKeyboard = true; }
        }
        if (Focus != prevFocus) OnHover?.Invoke();
        _slotCount = _slot;
        _slot = 0;
        if (Focus >= _slotCount && _slotCount > 0) Focus = _slotCount - 1;
    }

    /// <summary>Call when switching screens so focus starts at the first widget.</summary>
    public void Reset() { Focus = 0; _slotCount = 0; _slot = 0; TextFocus = -1; }

    private int Next() => _slot++;

    private bool Hover(Rectangle r) => Raylib.CheckCollisionPointRec(Mouse, r);

    private float Anim(int id, bool on)
    {
        _hoverAnim.TryGetValue(id, out float v);
        v += ((on ? 1f : 0f) - v) * MathF.Min(1f, Dt * 14f);
        _hoverAnim[id] = v;
        return v;
    }

    private bool Interact(int slot, Rectangle r, out bool focused)
    {
        bool hover = Hover(r);
        if (hover && !_usingKeyboard && Focus != slot) { Focus = slot; OnHover?.Invoke(); }
        focused = Focus == slot;
        bool hit = (hover && Clicked) || (focused && Confirm && TextFocus < 0);
        if (hit) OnClick?.Invoke();
        return hit;
    }

    // ------------------------------------------------------------------ widgets

    /// <summary>Angkot-window sticker: tilted label with a hard shadow that straightens when focused.</summary>
    public bool Sticker(Rectangle r, string label, float tilt, Color fill, Color text, float size = 34)
    {
        int slot = Next();
        bool hit = Interact(slot, r, out bool focused);
        float a = Anim(slot * 31 + 7, focused);
        float rot = tilt * (1 - a);
        float grow = 1 + 0.05f * a;
        var center = new Vector2(r.X + r.Width / 2 + a * 14, r.Y + r.Height / 2);
        var size2 = new Vector2(r.Width * grow, r.Height * grow);
        var body = new Rectangle(center.X, center.Y, size2.X, size2.Y);
        var origin = size2 / 2;
        Raylib.DrawRectanglePro(new Rectangle(center.X + 7, center.Y + 7, size2.X, size2.Y), origin, rot, Theme.Aspal);
        Raylib.DrawRectanglePro(new Rectangle(center.X, center.Y, size2.X + 6, size2.Y + 6), origin + new Vector2(3, 3), rot, Theme.Aspal);
        var c = focused ? Theme.Lerp(fill, Theme.Kuning, 0.0f) : fill;
        Raylib.DrawRectanglePro(body, origin, rot, focused ? Theme.Kuning : Theme.Lerp(c, Theme.Kapur, 0.0f));
        // Label, rotated with the sticker.
        var m = Theme.Measure(Theme.Display, label, size);
        var labelOrigin = new Vector2(m.X / 2, m.Y / 2 - 2);
        Raylib.DrawTextPro(Theme.Display, label, center, labelOrigin, rot, size, size * 0.02f, focused ? Theme.Aspal : text);
        if (focused)
        {
            // A chili-red arrow tab on the left edge marks the focused item.
            var tip = new Vector2(center.X - size2.X / 2 - 22, center.Y);
            Raylib.DrawTriangle(tip + new Vector2(0, -14), tip + new Vector2(0, 14), tip + new Vector2(16, 0), Theme.Cabe);
        }
        return hit;
    }

    /// <summary>Flat painted button for secondary actions.</summary>
    public bool Button(Rectangle r, string label, bool primary = false, float size = 22, bool enabled = true)
    {
        int slot = Next();
        bool focused;
        bool hit = Interact(slot, r, out focused) && enabled;
        float a = Anim(slot * 31 + 3, focused);
        var fill = !enabled ? Theme.Fade(Theme.Kapur, 0.35f) : primary ? Theme.Lerp(Theme.Cabe, Theme.Kuning, a) : Theme.Lerp(Theme.Kapur, Theme.Kuning, a);
        var off = new Vector2(5 - a * 2, 5 - a * 2);
        Raylib.DrawRectangleRec(new Rectangle(r.X + off.X, r.Y + off.Y, r.Width, r.Height), Theme.Aspal);
        Raylib.DrawRectangleRec(r, fill);
        Raylib.DrawRectangleLinesEx(r, 3, Theme.Aspal);
        var textColor = primary && a < 0.5f ? Theme.Kapur : Theme.Aspal;
        Theme.TextCentered(Theme.BodyBold, label, new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2), size, textColor);
        return hit;
    }

    /// <summary>A row that can be selected (lists of servers, mods, arenas).</summary>
    public bool Row(Rectangle r, bool selected, out bool focused)
    {
        int slot = Next();
        bool hit = Interact(slot, r, out focused);
        var bg = selected ? Theme.Kuning : focused ? Theme.Fade(Theme.Kapur, 0.22f) : Theme.Fade(Theme.Aspal, 0.35f);
        Raylib.DrawRectangleRec(r, bg);
        if (selected) Raylib.DrawRectangleLinesEx(r, 3, Theme.Aspal);
        return hit;
    }

    public bool Slider(Rectangle r, string label, ref float value, float min, float max, string valueText)
    {
        int slot = Next();
        bool hover = Hover(r);
        if (hover && !_usingKeyboard && Focus != slot) Focus = slot;
        bool focused = Focus == slot;
        float old = value;
        if (hover && Clicked) _dragSlot = slot;
        var track = new Rectangle(r.X + r.Width * 0.45f, r.Y + r.Height / 2 - 7, r.Width * 0.42f, 14);
        if (_dragSlot == slot)
            value = min + Math.Clamp((Mouse.X - track.X) / track.Width, 0, 1) * (max - min);
        if (focused)
        {
            float step = (max - min) / 20f;
            if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressedRepeat(KeyboardKey.Left)) value = MathF.Max(min, value - step);
            if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressedRepeat(KeyboardKey.Right)) value = MathF.Min(max, value + step);
        }
        DrawRowLabel(r, label, focused);
        float t = (value - min) / (max - min);
        Raylib.DrawRectangleRec(track, Theme.Aspal);
        Raylib.DrawRectangleRec(new Rectangle(track.X, track.Y, track.Width * t, track.Height), focused ? Theme.Kuning : Theme.Kapur);
        var knob = new Rectangle(track.X + track.Width * t - 9, track.Y - 8, 18, 30);
        Raylib.DrawRectangleRec(new Rectangle(knob.X + 3, knob.Y + 3, knob.Width, knob.Height), Theme.Aspal);
        Raylib.DrawRectangleRec(knob, focused ? Theme.Cabe : Theme.Kapur);
        Theme.TextRight(Theme.Mono, valueText, new Vector2(r.X + r.Width, r.Y + r.Height / 2 - 12), 22, Theme.Kapur);
        return MathF.Abs(old - value) > 1e-6f;
    }

    public bool Toggle(Rectangle r, string label, ref bool value)
    {
        int slot = Next();
        bool hit = Interact(slot, r, out bool focused);
        if (hit || (focused && (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.Right)))) { value = !value; hit = true; }
        DrawRowLabel(r, label, focused);
        var pill = new Rectangle(r.X + r.Width - 150, r.Y + r.Height / 2 - 18, 150, 36);
        Raylib.DrawRectangleRec(pill, Theme.Aspal);
        var half = new Rectangle(value ? pill.X + 75 : pill.X, pill.Y, 75, 36);
        Raylib.DrawRectangleRec(half, value ? Theme.Daun : Theme.Cabe);
        Theme.TextCentered(Theme.BodyBold, S.T("common.off"), new Vector2(pill.X + 37, pill.Y + 18), 18, value ? Theme.Fade(Theme.Kapur, 0.5f) : Theme.Kapur);
        Theme.TextCentered(Theme.BodyBold, S.T("common.on"), new Vector2(pill.X + 112, pill.Y + 18), 18, value ? Theme.Kapur : Theme.Fade(Theme.Kapur, 0.5f));
        return hit;
    }

    /// <summary>Cycles through options with left/right or clicks.</summary>
    public bool Choice(Rectangle r, string label, ref int index, IReadOnlyList<string> options)
    {
        int slot = Next();
        bool hit = Interact(slot, r, out bool focused);
        bool changed = false;
        if (hit) { index = (index + 1) % options.Count; changed = true; }
        if (focused && Raylib.IsKeyPressed(KeyboardKey.Right)) { index = (index + 1) % options.Count; changed = true; }
        if (focused && Raylib.IsKeyPressed(KeyboardKey.Left)) { index = (index - 1 + options.Count) % options.Count; changed = true; }
        DrawRowLabel(r, label, focused);
        string text = "< " + options[index] + " >";
        Theme.TextRight(Theme.BodyBold, text, new Vector2(r.X + r.Width, r.Y + r.Height / 2 - 13), 24, focused ? Theme.Kuning : Theme.Kapur);
        return changed;
    }

    public bool TextField(Rectangle r, string label, ref string value, int maxLength, string? placeholder = null)
    {
        int slot = Next();
        bool clicked = Interact(slot, r, out bool focused);
        if (clicked) TextFocus = slot;
        if (!focused && TextFocus == slot) TextFocus = -1;
        bool editing = focused;
        bool changed = false;
        if (editing)
        {
            int ch;
            var sb = new StringBuilder(value);
            while ((ch = Raylib.GetCharPressed()) > 0)
                if (ch >= 32 && ch < 256 && sb.Length < maxLength) { sb.Append((char)ch); changed = true; }
            if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && sb.Length > 0) { sb.Length--; changed = true; }
            if (changed) value = sb.ToString();
        }
        DrawRowLabel(r, label, focused);
        var box = new Rectangle(r.X + r.Width * 0.45f, r.Y + 4, r.Width * 0.55f, r.Height - 8);
        Raylib.DrawRectangleRec(box, Theme.Kapur);
        Raylib.DrawRectangleLinesEx(box, 3, focused ? Theme.Cabe : Theme.Aspal);
        string shown = value.Length == 0 && placeholder is not null && !editing ? placeholder : value;
        var col = value.Length == 0 && !editing ? Theme.Fade(Theme.Aspal, 0.45f) : Theme.Aspal;
        Theme.Text(Theme.BodyBold, shown, new Vector2(box.X + 12, box.Y + box.Height / 2 - 13), 24, col);
        if (editing && ((int)(Time * 2) & 1) == 0)
        {
            float w = Theme.Measure(Theme.BodyBold, value, 24).X;
            Raylib.DrawRectangle((int)(box.X + 14 + w), (int)(box.Y + 8), 3, (int)box.Height - 16, Theme.Cabe);
        }
        return changed;
    }

    private static void DrawRowLabel(Rectangle r, string label, bool focused)
    {
        if (focused) Raylib.DrawRectangleRec(new Rectangle(r.X - 14, r.Y + 6, 6, r.Height - 12), Theme.Kuning);
        Theme.Text(Theme.BodyBold, label, new Vector2(r.X, r.Y + r.Height / 2 - 14), 26, focused ? Theme.Kuning : Theme.Kapur);
    }

    // ------------------------------------------------------------------ decorative pieces

    /// <summary>Paper sign panel with an asphalt outline and offset shadow.</summary>
    public static void Panel(Rectangle r, Color fill, float shadow = 8)
    {
        Raylib.DrawRectangleRec(new Rectangle(r.X + shadow, r.Y + shadow, r.Width, r.Height), Theme.Aspal);
        Raylib.DrawRectangleRec(r, fill);
        Raylib.DrawRectangleLinesEx(r, 3, Theme.Aspal);
    }

    /// <summary>Indonesian license plate: black plate, thin white inner frame, white lettering.</summary>
    public static void Plate(Rectangle r, string label, string value, Color valueColor, float valueSize = 46)
    {
        Raylib.DrawRectangleRounded(new Rectangle(r.X + 4, r.Y + 5, r.Width, r.Height), 0.18f, 6, Theme.Fade(Theme.Aspal, 0.6f));
        Raylib.DrawRectangleRounded(r, 0.18f, 6, Theme.Aspal);
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10), 0.16f, 6, 2, Theme.Kapur);
        Theme.Text(Theme.Mono, label, new Vector2(r.X + 16, r.Y + 10), 15, Theme.Fade(Theme.Kapur, 0.75f));
        Theme.TextRight(Theme.Display, value, new Vector2(r.X + r.Width - 16, r.Y + r.Height / 2 - valueSize / 2 + 4), valueSize, valueColor);
    }

    /// <summary>The signature element: a red LED "running text" sign like the ones above every warung.</summary>
    public sealed class Marquee
    {
        private Color[]? _pixels;
        private int _w, _h;
        private string _text = "";
        private float _offset;

        public void SetText(string text)
        {
            if (text == _text) return;
            _text = text;
            var img = Raylib.ImageTextEx(Theme.Mono, "   " + text + "   ", 12, 1, Color.White);
            _w = img.Width; _h = img.Height;
            _pixels = new Color[_w * _h];
            unsafe
            {
                var px = Raylib.LoadImageColors(img);
                for (int i = 0; i < _pixels.Length; i++) _pixels[i] = px[i];
                Raylib.UnloadImageColors(px);
            }
            Raylib.UnloadImage(img);
            _offset = -1;
        }

        /// <summary>Draws the marquee; returns true when the text has fully scrolled past once.</summary>
        public bool Draw(Rectangle r, float dt, float speed = 38f)
        {
            Raylib.DrawRectangleRec(new Rectangle(r.X - 6, r.Y - 6, r.Width + 12, r.Height + 12), Theme.Aspal);
            if (_pixels is null) return false;
            const int rows = 12;
            float dot = r.Height / rows;
            int cols = (int)(r.Width / dot);
            if (_offset < 0) _offset = cols * 0.55f; // start with the text already on the sign
            _offset += dt * speed / dot * 3f;
            bool wrapped = false;
            if (_offset > _w + cols) { _offset = 0; wrapped = true; }
            int start = (int)_offset;
            int yOff = Math.Max(0, (_h - rows) / 2);
            for (int c = 0; c < cols; c++)
            {
                int sx = start + c - cols;
                for (int row = 0; row < rows; row++)
                {
                    int sy = row + yOff;
                    bool lit = sx >= 0 && sx < _w && sy < _h && _pixels[sy * _w + sx].A > 110;
                    float x = r.X + c * dot + dot / 2, y = r.Y + row * dot + dot / 2;
                    Raylib.DrawPoly(new Vector2(x, y), 6, dot * 0.4f, 0, lit ? Theme.Cabe : Theme.LedOff);
                }
            }
            return wrapped;
        }
    }
}
