using System.Globalization;
using System.Numerics;
using System.Text;
using Djancoeg.Client.Game;
using Djancoeg.Client.Render;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;
using Djancoeg.Modding;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Screens;

/// <summary>
/// In-game level editor. Free-fly camera, click to select or place, keyboard to move/resize/restyle,
/// Ctrl+S saves into the local "arena-buatanku" mod and F5 test-plays it against bots.
/// </summary>
public sealed class EditorScreen : Screen
{
    private enum Tool { Select, Box, Spawn, Pickup, JumpPad, Prop, Light }

    private ArenaEditor _ed;
    private ArenaRenderer? _renderer;
    private int _builtVersion = -1;
    private Camera3D _cam;
    private float _yaw = MathF.PI * 0.9f, _pitch = -0.55f;
    private Tool _tool = Tool.Select;
    private string _status = "";
    private float _statusTime;
    private bool _dirty;
    private bool _renaming;
    private readonly StringBuilder _nameBuf = new();
    private float _time;
    private Vector3 _hoverPoint;
    private bool _hoverValid;

    public EditorScreen(App app, string? arenaId = null) : base(app)
    {
        var saved = ArenaEditor.LoadSaved(App.ModsDir);
        var start = (arenaId is null ? saved.LastOrDefault() : saved.FirstOrDefault(a => a.Id == arenaId)) ?? ArenaEditor.NewArena(FreeId(saved));
        _ed = new ArenaEditor(start);
        _cam = new Camera3D { Position = new Vector3(6, 16, 30), Up = Vector3.UnitY, FovY = 70, Projection = CameraProjection.Perspective };
        if (app.Options.Shot is not null) _ed.Selection = new EditorSelection(EditorItem.Pickup, 0);
    }

    private static string FreeId(List<ArenaDef> saved)
    {
        for (int n = 1; ; n++)
        {
            string id = "arena-ku-" + n.ToString(CultureInfo.InvariantCulture);
            if (saved.All(a => a.Id != id)) return id;
        }
    }

    public override void Enter()
    {
        base.Enter();
        _builtVersion = -1;
    }

    public override void Leave()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    private void Status(string text) { _status = text; _statusTime = 3.5f; }

    public override void Update(float dt)
    {
        _time += dt;
        _statusTime -= dt;
        if (_renaming) { UpdateRename(); return; }
        if (Gui.Back) { App.Go(new MainMenuScreen(App)); return; }

        bool ctrl = IsKeyDown(KeyboardKey.LeftControl) || IsKeyDown(KeyboardKey.RightControl);
        bool shift = IsKeyDown(KeyboardKey.LeftShift) || IsKeyDown(KeyboardKey.RightShift);

        // Fly camera: hold right mouse to look.
        if (IsMouseButtonPressed(MouseButton.Right)) DisableCursor();
        if (IsMouseButtonReleased(MouseButton.Right)) EnableCursor();
        if (IsMouseButtonDown(MouseButton.Right))
        {
            var md = GetMouseDelta();
            _yaw -= md.X * 0.0035f;
            _pitch = Math.Clamp(_pitch - md.Y * 0.0035f, -1.5f, 1.5f);
        }
        var fwd = new Vector3(MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), MathF.Cos(_yaw) * MathF.Cos(_pitch));
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        if (!ctrl)
        {
            float speed = (shift ? 30f : 12f) * dt;
            var move = Vector3.Zero;
            if (IsKeyDown(KeyboardKey.W)) move += fwd;
            if (IsKeyDown(KeyboardKey.S)) move -= fwd;
            if (IsKeyDown(KeyboardKey.D)) move += right;
            if (IsKeyDown(KeyboardKey.A)) move -= right;
            if (IsKeyDown(KeyboardKey.E)) move += Vector3.UnitY;
            if (IsKeyDown(KeyboardKey.Q)) move -= Vector3.UnitY;
            if (move != Vector3.Zero) _cam.Position += Vector3.Normalize(move) * speed;
        }
        _cam.Target = _cam.Position + fwd;

        // Tools.
        KeyboardKey[] toolKeys = [KeyboardKey.Zero, KeyboardKey.One, KeyboardKey.Two, KeyboardKey.Three, KeyboardKey.Four, KeyboardKey.Five, KeyboardKey.Six];
        for (int i = 0; i < toolKeys.Length; i++) if (IsKeyPressed(toolKeys[i])) _tool = (Tool)i;
        if (IsKeyPressed(KeyboardKey.Grave)) _tool = Tool.Select;

        // Hover / click.
        var ray = GetScreenToWorldRay(GetMousePosition(), _cam);
        var hit = _ed.Pick(ray.Position, ray.Direction, out float dist, out var normal);
        _hoverValid = !hit.IsNone || ray.Direction.Y < -0.01f;
        if (!hit.IsNone)
        {
            _hoverPoint = ray.Position + ray.Direction * dist;
            if (hit.Kind == EditorItem.Box && normal.Y > 0.5f) _hoverPoint.Y = _ed.Bounds(hit).Max.Y;
        }
        else if (ray.Direction.Y < -0.01f) _hoverPoint = ray.Position + ray.Direction * (-ray.Position.Y / ray.Direction.Y);

        bool overPanel = GetMousePosition().X > GetScreenWidth() - PanelWidth;
        if (IsMouseButtonPressed(MouseButton.Left) && !overPanel)
        {
            if (_tool == Tool.Select) _ed.Selection = hit;
            else if (_hoverValid)
            {
                var kind = (EditorItem)((int)_tool - 1);
                _ed.Place(kind, _hoverPoint, "#8D6E63", SurfaceKind.Wood);
                _dirty = true;
                App.Sounds.Play(Audio.Sfx.UiClick, 0.6f);
            }
        }

        // Edits on the selection.
        int before = _ed.Version;
        float g = MathF.Max(0.25f, _ed.Grid);
        var step = Vector3.Zero;
        // Arrow keys move relative to the camera's facing, snapped to the dominant world axis.
        var flatF = MathF.Abs(fwd.X) > MathF.Abs(fwd.Z) ? new Vector3(MathF.Sign(fwd.X), 0, 0) : new Vector3(0, 0, MathF.Sign(fwd.Z));
        var flatR = new Vector3(-flatF.Z, 0, flatF.X);
        if (IsKeyPressed(KeyboardKey.Up)) step += flatF * g;
        if (IsKeyPressed(KeyboardKey.Down)) step -= flatF * g;
        if (IsKeyPressed(KeyboardKey.Right)) step -= flatR * g;
        if (IsKeyPressed(KeyboardKey.Left)) step += flatR * g;
        if (IsKeyPressed(KeyboardKey.PageUp)) step += Vector3.UnitY * g;
        if (IsKeyPressed(KeyboardKey.PageDown)) step -= Vector3.UnitY * g;
        if (step != Vector3.Zero)
        {
            if (ctrl) _ed.Resize(step);
            else _ed.Move(step);
        }
        if (IsKeyPressed(KeyboardKey.RightBracket)) _ed.CycleStyle(1);
        if (IsKeyPressed(KeyboardKey.LeftBracket)) _ed.CycleStyle(-1);
        if (IsKeyPressed(KeyboardKey.C) && !ctrl) _ed.CycleStyle(1, colorOnly: true);
        if (IsKeyPressed(KeyboardKey.H)) _ed.CycleBoxKind();
        if (IsKeyPressed(KeyboardKey.Delete) || IsKeyPressed(KeyboardKey.Backspace)) _ed.Delete();
        if (ctrl && IsKeyPressed(KeyboardKey.D)) _ed.Duplicate();
        if (ctrl && IsKeyPressed(KeyboardKey.Z)) _ed.Undo();
        if (ctrl && IsKeyPressed(KeyboardKey.Y)) _ed.Redo();
        if (IsKeyPressed(KeyboardKey.Z) && !ctrl && _hoverValid) _ed.SetZoneHere(_hoverPoint);
        if (IsKeyPressed(KeyboardKey.G))
        {
            _ed.Grid = _ed.Grid switch { 0.25f => 0.5f, 0.5f => 1f, _ => 0.25f };
            Status(S.T("ed.grid") + " " + _ed.Grid.ToString("0.##", CultureInfo.InvariantCulture));
        }
        if (_ed.Version != before) _dirty = true;

        if (IsKeyPressed(KeyboardKey.F2)) { _renaming = true; _nameBuf.Clear().Append(S.T(_ed.Arena.Name)); }
        if (ctrl && IsKeyPressed(KeyboardKey.S)) Save();
        if (ctrl && IsKeyPressed(KeyboardKey.N)) NewArena();
        if (IsKeyPressed(KeyboardKey.F5)) TestPlay();
    }

    private void UpdateRename()
    {
        for (int c = GetCharPressed(); c > 0; c = GetCharPressed())
            if (_nameBuf.Length < 32 && c >= 32 && c < 127) _nameBuf.Append((char)c);
        if (IsKeyPressed(KeyboardKey.Backspace) && _nameBuf.Length > 0) _nameBuf.Length--;
        if (IsKeyPressed(KeyboardKey.Escape)) { _renaming = false; return; }
        if (IsKeyPressed(KeyboardKey.Enter) && _nameBuf.Length > 0)
        {
            string name = _nameBuf.ToString().Trim();
            string id = Slug(name);
            if (App.Content.ArenaIndexOf(id) >= 0 && ArenaEditor.LoadSaved(App.ModsDir).All(a => a.Id != id)) id = "ku-" + id;
            _ed.Rename(id, name, name);
            _dirty = true;
            _renaming = false;
        }
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var s = sb.ToString().Trim('-');
        if (s.Length < 2) s = "arena-ku";
        return s.Length > 40 ? s[..40].TrimEnd('-') : s;
    }

    private bool Save()
    {
        var problems = _ed.Problems();
        if (problems.Count > 0) { Status(S.T("ed.cannotSave") + " " + problems[0]); return false; }
        var path = _ed.Save(App.ModsDir);
        ModLoader.SetEnabled(App.ModsDir, ArenaEditor.ModId, true);
        _dirty = false;
        App.Achievements.OnEditorSaved();
        Status(S.T("ed.saved") + " " + Path.GetFileName(path));
        return true;
    }

    private void NewArena()
    {
        _ed = new ArenaEditor(ArenaEditor.NewArena(FreeId(ArenaEditor.LoadSaved(App.ModsDir))));
        _builtVersion = -1;
        _dirty = true;
        Status(S.T("ed.new"));
    }

    private void TestPlay()
    {
        if (!Save()) return;
        App.ReloadContent();
        var st = App.Settings;
        var match = new MatchSettings { ArenaId = _ed.Arena.Id, BotCount = 3, BotSkill = 0.4f, ScoreLimit = 999, TimeLimitSeconds = 0, ServerName = "Editor", Creeps = st.Creeps };
        try
        {
            var session = Session.StartLocal(App.Content, st, match, host: false, master: null, persistStats: false);
            App.Go(new PlayScreen(App, session) { ReturnTo = () => new EditorScreen(App, _ed.Arena.Id) });
        }
        catch (System.Net.Sockets.SocketException ex) { Status(ex.Message); }
    }

    // ------------------------------------------------------------------ drawing

    private float PanelWidth => 400 * U;

    public override void Draw()
    {
        var arena = _ed.Arena;
        if (_builtVersion != _ed.Version || _renderer is null)
        {
            _renderer?.Dispose();
            _renderer = new ArenaRenderer(arena, App.Shader);
            App.Shader.SetEnvironment(arena);
            _builtVersion = _ed.Version;
        }
        ClearBackground(Theme.Rgba(ColorUtil.ParseRgba(arena.SkyBottom)));
        var shader = App.Shader;
        shader.SetFrame(_cam.Position, _time);
        shader.BeginLights();
        foreach (var l in arena.Lights.Take(10)) shader.AddLight(l.PositionV, l.Radius, ColorUtil.ToVector3(ColorUtil.ParseRgba(l.Color)));
        shader.CommitLights();
        shader.SetEmissive(0);

        BeginMode3D(_cam);
        App.Sky.Draw(arena, _cam, _time);
        BeginShaderMode(shader.Shader);
        _renderer.Draw(shader);
        _renderer.DrawDestructibles(shader, 0);
        foreach (var pad in arena.JumpPads) Primitives.JumpPad(pad.MinV, pad.MaxV, _time);
        foreach (var p in arena.Props) DrawCubeV(p.PositionV, p.HalfExtentsV * 2, Theme.Rgba(ColorUtil.ParseRgba(p.Color)));
        EndShaderMode();

        // Markers (unlit so they read clearly).
        foreach (var s in arena.Spawns)
        {
            var c = s.Team switch { 0 => Theme.Cabe, 1 => Theme.TarpLight, _ => Theme.Kapur };
            DrawCylinderEx(s.PositionV, s.PositionV + new Vector3(0, 1.8f, 0), 0.35f, 0.35f, 10, Theme.Fade(c, 0.75f));
            var f = new Vector3(MathF.Sin(s.Yaw), 0, MathF.Cos(s.Yaw));
            DrawLine3D(s.PositionV + new Vector3(0, 1.2f, 0), s.PositionV + new Vector3(0, 1.2f, 0) + f * 1.2f, Theme.Kuning);
        }
        foreach (var p in arena.Pickups)
            DrawCubeV(p.PositionV + new Vector3(0, 0.1f * MathF.Sin(_time * 3), 0), new Vector3(0.5f), PickupColor(p.Kind));
        foreach (var l in arena.Lights)
        {
            var lc = Theme.Rgba(ColorUtil.ParseRgba(l.Color));
            DrawSphere(l.PositionV, 0.25f, lc);
            DrawCircle3D(l.PositionV, l.Radius, Vector3.UnitX, 90, Theme.Fade(lc, 0.35f));
        }
        DrawCircle3D(arena.ZoneCenterV + new Vector3(0, 0.05f, 0), arena.ZoneRadius, Vector3.UnitX, 90, Theme.Kuning);
        DrawGridAround();

        if (!_ed.Selection.IsNone && _ed.IsValid(_ed.Selection))
        {
            var b = _ed.Bounds(_ed.Selection);
            float pulse = 0.02f + 0.02f * MathF.Sin(_time * 8);
            var center = (b.Min + b.Max) * 0.5f;
            DrawCubeWiresV(center, b.Max - b.Min + new Vector3(pulse * 4), Theme.Kuning);
            DrawCubeWiresV(center, b.Max - b.Min + new Vector3(0.12f + pulse * 4), Theme.Aspal);
        }
        if (_tool != Tool.Select && _hoverValid)
        {
            var p = _ed.Snap(_hoverPoint);
            DrawCubeWiresV(p + new Vector3(0, 0.5f, 0), _tool == Tool.Box ? new Vector3(2, 1, 2) : new Vector3(0.6f), Theme.Cabe);
        }
        EndMode3D();

        DrawPanel();
        if (_renaming) DrawRename();
    }

    private void DrawGridAround()
    {
        float g = MathF.Max(1f, _ed.Grid * 2);
        var c = Theme.Fade(Theme.Kapur, 0.18f);
        float cx = MathF.Round(_hoverPoint.X / g) * g, cz = MathF.Round(_hoverPoint.Z / g) * g, y = _hoverPoint.Y + 0.02f;
        for (int i = -6; i <= 6; i++)
        {
            DrawLine3D(new Vector3(cx - 6 * g, y, cz + i * g), new Vector3(cx + 6 * g, y, cz + i * g), c);
            DrawLine3D(new Vector3(cx + i * g, y, cz - 6 * g), new Vector3(cx + i * g, y, cz + 6 * g), c);
        }
    }

    private static Color PickupColor(PickupKind k) => k switch
    {
        PickupKind.HealthSmall or PickupKind.HealthMega => Theme.Daun,
        PickupKind.Armor or PickupKind.ArmorSmall => Theme.TarpLight,
        PickupKind.Quad => Theme.Cabe,
        _ => Theme.Kuning,
    };

    private void DrawPanel()
    {
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        float u = U, pw = PanelWidth;
        var r = new Rectangle(sw - pw, 0, pw, sh);
        DrawRectangleRec(r, Theme.Fade(Theme.TarpDeep, 0.92f));
        float x = r.X + 22 * u, y = 22 * u;
        Theme.SignText("EDITOR ARENA", new Vector2(x, y), 38 * u, Theme.Kuning);
        y += 52 * u;
        Theme.Text(Theme.BodyBold, S.T(_ed.Arena.Name) + (_dirty ? "  *" : ""), new Vector2(x, y), 22 * u, Theme.Kapur);
        y += 28 * u;
        Theme.Text(Theme.Mono, "mods/" + ArenaEditor.ModId + "/arenas/" + _ed.Arena.Id + ".json", new Vector2(x, y), 13 * u, Theme.Fade(Theme.Kapur, 0.7f));
        y += 30 * u;

        string[] tools = ["ed.tool0", "ed.tool1", "ed.tool2", "ed.tool3", "ed.tool4", "ed.tool5", "ed.tool6"];
        for (int i = 0; i < tools.Length; i++)
        {
            var tr = new Rectangle(x, y, pw - 44 * u, 30 * u);
            bool on = (int)_tool == i;
            bool hover = CheckCollisionPointRec(GetMousePosition(), tr);
            DrawRectangleRec(tr, on ? Theme.Kuning : hover ? Theme.Fade(Theme.Kapur, 0.25f) : Theme.Fade(Theme.Kapur, 0.08f));
            Theme.Text(Theme.BodyBold, $"{i}  {S.T(tools[i])}", new Vector2(tr.X + 10 * u, tr.Y + 5 * u), 18 * u, on ? Theme.Aspal : Theme.Kapur);
            if (hover && IsMouseButtonPressed(MouseButton.Left)) _tool = (Tool)i;
            y += 34 * u;
        }
        y += 10 * u;

        Theme.Text(Theme.Mono, S.T("ed.selection").ToUpperInvariant(), new Vector2(x, y), 14 * u, Theme.Kuning);
        y += 22 * u;
        Theme.Paragraph(Theme.Body, DescribeSelection(), new Vector2(x, y), 16 * u, pw - 44 * u, Theme.Kapur);
        y += 70 * u;

        Theme.Text(Theme.Mono, S.T("ed.keys").ToUpperInvariant(), new Vector2(x, y), 14 * u, Theme.Kuning);
        y += 22 * u;
        Theme.Paragraph(Theme.Body, S.T("ed.help"), new Vector2(x, y), 15 * u, pw - 44 * u, Theme.Kapur, 1.3f);

        var counts = $"{_ed.Arena.Boxes.Count} box · {_ed.Arena.Spawns.Count} spawn · {_ed.Arena.Pickups.Count} pickup · {_ed.Arena.Props.Count} prop";
        Theme.Text(Theme.Mono, counts, new Vector2(x, sh - 66 * u), 13 * u, Theme.Fade(Theme.Kapur, 0.7f));
        if (_statusTime > 0)
        {
            var m = Theme.Measure(Theme.BodyBold, _status, 20 * u);
            var sr = new Rectangle(24 * u, sh - 70 * u, m.X + 30 * u, 40 * u);
            DrawRectangleRec(sr, Theme.Kuning);
            Theme.Text(Theme.BodyBold, _status, new Vector2(sr.X + 15 * u, sr.Y + 9 * u), 20 * u, Theme.Aspal);
        }
        var hm = Theme.Measure(Theme.BodyBold, S.T("ed.hint"), 18 * u);
        DrawRectangleRec(new Rectangle(14 * u, 12 * u, hm.X + 20 * u, hm.Y + 14 * u), Theme.Fade(Theme.Aspal, 0.7f));
        Theme.Text(Theme.BodyBold, S.T("ed.hint"), new Vector2(24 * u, 19 * u), 18 * u, Theme.Kapur);
    }

    private string DescribeSelection()
    {
        var s = _ed.Selection;
        if (s.IsNone || !_ed.IsValid(s)) return S.T("ed.nothing");
        var a = _ed.Arena;
        var inv = CultureInfo.InvariantCulture;
        switch (s.Kind)
        {
            case EditorItem.Box:
            {
                var b = a.Boxes[s.Index];
                var size = b.MaxV - b.MinV;
                string kind = !b.Solid ? S.T("ed.deco") : b.Health > 0 ? S.T("ed.breakable") + " " + b.Health.ToString("0", inv) + " HP" : S.T("ed.solid");
                return $"Box #{s.Index}  {b.Surface}  {b.Color}\n{size.X.ToString("0.##", inv)} × {size.Y.ToString("0.##", inv)} × {size.Z.ToString("0.##", inv)}  ·  {kind}";
            }
            case EditorItem.Spawn: return $"Spawn #{s.Index}  team {a.Spawns[s.Index].Team}";
            case EditorItem.Pickup: return $"Pickup #{s.Index}  {a.Pickups[s.Index].Kind}";
            case EditorItem.JumpPad: return $"Jump pad #{s.Index}  launch {a.JumpPads[s.Index].Launch[1].ToString("0", inv)}";
            case EditorItem.Prop: return $"Prop #{s.Index}  {a.Props[s.Index].Kind}";
            default: return $"Light #{s.Index}  r={a.Lights[s.Index].Radius.ToString("0", inv)}  {a.Lights[s.Index].Color}";
        }
    }

    private void DrawRename()
    {
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        float u = U;
        DrawRectangle(0, 0, sw, sh, Theme.Fade(Theme.Aspal, 0.6f));
        var r = new Rectangle(sw / 2f - 300 * u, sh / 2f - 80 * u, 600 * u, 160 * u);
        Gui.Panel(r, Theme.Kapur);
        Theme.Text(Theme.BodyBold, S.T("ed.rename"), new Vector2(r.X + 24 * u, r.Y + 20 * u), 20 * u, Theme.Aspal);
        string caret = ((int)(_time * 2) & 1) == 0 ? "_" : " ";
        Theme.Text(Theme.Display, _nameBuf + caret, new Vector2(r.X + 24 * u, r.Y + 64 * u), 36 * u, Theme.Cabe);
    }
}
