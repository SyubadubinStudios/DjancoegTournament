using System.Numerics;
using Djancoeg.Client.Audio;
using Djancoeg.Client.Render;
using Djancoeg.Client.Screens;
using Djancoeg.Client.Ui;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Modding;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client;

public sealed record AppOptions
{
    public string? Shot { get; init; }          // scene to capture (menu, setup, fighters, browser, workshop, settings, credits, game, game-tps)
    public string? ShotOut { get; init; }       // output png
    public float ShotDelay { get; init; } = 1.5f;
    public string? Arena { get; init; }
    public string? Connect { get; init; }       // host:port to join directly
    public int Width { get; init; } = 1600;
    public int Height { get; init; } = 900;
    public string? Language { get; init; }
    public bool NoVsync { get; init; }
    public string? Mode { get; init; }      // ffa | tdm | koth | ctf | race (screenshot matches)
}

/// <summary>Owns the window, shared resources and the active screen.</summary>
public sealed class App : IDisposable
{
    public ClientSettings Settings { get; }
    public ContentRegistry Content { get; private set; } = null!;
    public List<ModInfo> Mods { get; private set; } = [];
    public string AssetsDir { get; }
    public string ModsDir { get; }
    public WorldShader Shader { get; private set; } = null!;
    public ModelLibrary Models { get; private set; } = null!;
    public SoundBank Sounds { get; private set; } = null!;
    public SkyRenderer Sky { get; private set; } = null!;
    public Gui Gui { get; } = new();
    public AppOptions Options { get; }
    public Gui.Marquee Marquee { get; } = new();
    public float Time { get; private set; }
    public Texture2D? KeyArt { get; private set; }
    public Achievements Achievements { get; }

    private Screen _screen = null!;
    private Screen? _pending;
    private bool _quit;

    public App(AppOptions options)
    {
        Options = options;
        Settings = ClientSettings.Load();
        // Screenshot/autopilot runs must never unlock the player's real achievements.
        Achievements = new Achievements(options.Shot is null ? null : Path.Combine(Path.GetTempPath(), "djancoeg-shot-achievements.json"));
        if (options.Language is "en") Settings.Language = Core.Localization.Language.English;
        if (options.Language is "id") Settings.Language = Core.Localization.Language.Indonesian;
        S.Lang = Settings.Language;
        Controls.Settings = Settings;
        if (Settings.PlayerKey.Length == 0) { Settings.PlayerKey = Guid.NewGuid().ToString("N"); Settings.Save(); }
        AssetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
        ModsDir = ResolveModsDir();
        ReloadContent();
    }

    /// <summary>
    /// Portable zips keep mods next to the game. Installed builds (Program Files, AppImage, macOS .app) are read-only,
    /// so mods live in the player's app-data folder instead. DJANCOEG_MODS overrides both.
    /// </summary>
    private static string ResolveModsDir()
    {
        if (Environment.GetEnvironmentVariable("DJANCOEG_MODS") is { Length: > 0 } custom) return custom;
        string baseDir = AppContext.BaseDirectory;
        bool packaged = Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 }
            || baseDir.Contains(".app/Contents/", StringComparison.Ordinal)
            || File.Exists(Path.Combine(baseDir, "installed.marker"));
        return ModLoader.ResolveWritableModsDirectory(ModLoader.DefaultModsDirectory, Path.Combine(ClientSettings.Folder, "mods"), packaged);
    }

    /// <summary>Rebuilds the content registry from built-ins plus enabled mods (only safe outside a match).</summary>
    public void ReloadContent()
    {
        ClearThumbnails();
        Content = ContentRegistry.CreateBuiltIn();
        Mods = ModLoader.ApplyEnabled(Content, ModsDir, msg => Console.WriteLine(msg));
        if (Content.CharacterIndexOf(Settings.CharacterId) < 0) Settings.CharacterId = Content.Characters[0].Id;
        if (Content.ArenaIndexOf(Settings.ArenaId) < 0) Settings.ArenaId = Content.Arenas[0].Id;
    }

    public void Run()
    {
        var flags = (Settings.Msaa ? ConfigFlags.Msaa4xHint : 0) | (Options.NoVsync ? 0 : ConfigFlags.VSyncHint);
        flags |= Options.Shot is null ? ConfigFlags.ResizableWindow : ConfigFlags.UndecoratedWindow;
        SetConfigFlags(flags);
        SetTraceLogLevel(TraceLogLevel.Warning);
        InitWindow(Options.Width, Options.Height, $"{GameInfo.Name} - {GameInfo.Tagline}");
        FitWindowToMonitor();
        SetWindowMinSize(960, 540);
        SetExitKey(KeyboardKey.Null);
        var icon = Path.Combine(AssetsDir, "icon", "djancoeg-icon.png");
        if (File.Exists(icon)) { var img = LoadImage(icon); SetWindowIcon(img); UnloadImage(img); }
        if (Settings.Fullscreen && Options.Shot is null) ToggleFullscreen();

        Theme.Load(AssetsDir);
        Shader = new WorldShader();
        Models = new ModelLibrary(AssetsDir, Shader);
        Sky = new SkyRenderer(Models);
        Sounds = new SoundBank(AssetsDir);
        ApplyVolume();
        KeyArt = Models.Texture("art/key-art.png");
        Gui.OnHover = () => Sounds.Play(Sfx.UiMove, 0.35f);
        Gui.OnClick = () => Sounds.Play(Sfx.UiClick, 0.5f);

        _screen = InitialScreen();
        _screen.Enter();

        float shotTimer = 0;
        while (!WindowShouldClose() && !_quit)
        {
            float dt = MathF.Min(GetFrameTime(), 0.1f);
            Time += dt;
            if (_pending is not null)
            {
                _screen.Leave();
                Achievements.Save();
                _screen = _pending;
                _pending = null;
                Gui.Reset();
                _screen.Enter();
            }
            Gui.BeginFrame(dt);
            _screen.Update(dt);

            BeginDrawing();
            _screen.Draw();
            if (Options.Shot is null) Achievements.DrawToast(dt, this);
            EndDrawing();

            if (Options.Shot is not null)
            {
                shotTimer += dt;
                if (shotTimer >= Options.ShotDelay && _screen.ReadyForScreenshot)
                {
                    var img = LoadImageFromScreen();
                    var path = Path.GetFullPath(Options.ShotOut ?? $"{Options.Shot}.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    ExportImage(img, path);
                    UnloadImage(img);
                    Console.WriteLine($"Screenshot saved: {path}");
                    _quit = true;
                }
            }
        }
        _screen.Leave();
        if (Options.Shot is null) Settings.Save(); // screenshot runs must not change the player's settings
    }

    /// <summary>Shrinks the window (keeping 16:9) when the monitor is smaller than the requested size.</summary>
    private void FitWindowToMonitor()
    {
        int m = GetCurrentMonitor();
        int mw = GetMonitorWidth(m), mh = GetMonitorHeight(m);
        if (mw <= 0 || mh <= 0) return;
        int w = Options.Width, h = Options.Height;
        float scale = MathF.Min(1f, MathF.Min((mw - 40) / (float)w, (mh - 80) / (float)h));
        if (Options.Shot is not null) scale = MathF.Min(1f, MathF.Min(mw / (float)w, mh / (float)h));
        if (scale >= 1f) return;
        w = (int)(w * scale); h = (int)(h * scale);
        SetWindowSize(w, h);
        SetWindowPosition((mw - w) / 2, Math.Max(0, (mh - h) / 2));
    }

    private Screen InitialScreen()
    {
        if (Options.Connect is { } target && System.Net.IPEndPoint.TryParse(target, out var ep))
            return new PlayScreen(this, Game.Session.Join(Content, Settings, ep));
        return Options.Shot switch
        {
            "setup" => new SetupScreen(this, host: false),
            "host" => new SetupScreen(this, host: true),
            "fighters" => new FightersScreen(this),
            "browser" => new BrowserScreen(this),
            "workshop" => new WorkshopScreen(this),
            "workshop-online" => new WorkshopScreen(this, online: true),
            "leaderboard" => new LeaderboardScreen(this),
            "settings" => new SettingsScreen(this),
            "settings-graphics" => new SettingsScreen(this, null, 1),
            "settings-access" => new SettingsScreen(this, null, 2),
            "controls" => new ControlsScreen(this),
            "credits" => new CreditsScreen(this),
            "campaign" => new CampaignScreen(this),
            "editor" => new EditorScreen(this),
            "achievements" => new AchievementsScreen(this, new MainMenuScreen(this)),
            "boss" => StartShotBoss(),
            "game" or "game-tps" or "scoreboard" or "spectate" or "creeps" or "ragdoll" => StartShotGame(),
            _ => new MainMenuScreen(this),
        };
    }

    private Screen StartShotGame()
    {
        var match = new MatchSettings
        {
            ArenaId = Options.Arena ?? Settings.ArenaId, BotCount = Options.Shot == "creeps" ? 3 : Options.Shot == "ragdoll" ? 0 : 7, BotSkill = 0.6f, ScoreLimit = 999, TimeLimitSeconds = 600,
            CreepIntervalMin = Options.Shot == "creeps" ? 1.5f : 25f, CreepIntervalMax = Options.Shot == "creeps" ? 3f : 55f,
            Mode = Options.Mode switch { "tdm" => GameModeKind.TeamDeathmatch, "koth" => GameModeKind.KingOfTheHill, "ctf" => GameModeKind.CaptureTheFlag, "race" => GameModeKind.Race, _ => GameModeKind.FreeForAll },
        };
        var session = Game.Session.StartLocal(Content, Settings, match, host: false, null, spectator: Options.Shot == "spectate", persistStats: false);
        session.Autopilot = Options.Shot != "ragdoll";
        session.ThirdPerson = Options.Shot is "game-tps" or "creeps" or "ragdoll";
        return new PlayScreen(this, session) { ShowScoreboardForShot = Options.Shot == "scoreboard" };
    }

    private Screen StartShotBoss()
    {
        var stage = Core.Content.Campaign.Stage("jawa-boss")!;
        var session = Game.Session.StartLocal(Content, Settings, stage.ToMatch(), host: false, null, persistStats: false);
        session.Autopilot = true;
        session.ThirdPerson = true;
        return new PlayScreen(this, session) { Campaign = new CampaignRun(stage) };
    }

    public void Go(Screen next) => _pending = next;

    // ------------------------------------------------------------------ arena thumbnails

    private readonly Dictionary<string, RenderTexture2D> _thumbs = [];

    /// <summary>A small rendered preview of an arena (cached; built on first request).</summary>
    public Texture2D ArenaThumbnail(ArenaDef arena)
    {
        if (_thumbs.TryGetValue(arena.Id, out var rt)) return rt.Texture;
        rt = LoadRenderTexture(320, 180);
        SetTextureFilter(rt.Texture, TextureFilter.Bilinear);
        using (var renderer = new ArenaRenderer(arena, Shader))
        {
            var min = new System.Numerics.Vector3(float.MaxValue);
            var max = new System.Numerics.Vector3(float.MinValue);
            foreach (var b in arena.Boxes) { min = System.Numerics.Vector3.Min(min, b.MinV); max = System.Numerics.Vector3.Max(max, b.MaxV); }
            var center = (min + max) * 0.5f;
            var size = max - min;
            var cam = new Camera3D
            {
                Position = new System.Numerics.Vector3(center.X + size.X * 0.55f, MathF.Max(max.Y, 4) + size.Z * 0.45f, center.Z - size.Z * 0.75f),
                Target = new System.Numerics.Vector3(center.X, 0, center.Z),
                Up = System.Numerics.Vector3.UnitY, FovY = 45, Projection = CameraProjection.Perspective,
            };
            Shader.SetEnvironment(arena);
            Shader.SetFrame(cam.Position, 0);
            Shader.BeginLights();
            foreach (var l in arena.Lights.Take(WorldShader.MaxLights))
                Shader.AddLight(l.PositionV, l.Radius, ColorUtil.ToVector3(ColorUtil.ParseRgba(l.Color)));
            Shader.CommitLights();
            Shader.SetEmissive(0);
            BeginTextureMode(rt);
            ClearBackground(Theme.Rgba(ColorUtil.ParseRgba(arena.SkyBottom)));
            DrawRectangleGradientV(0, 0, 320, 180, Theme.Rgba(ColorUtil.ParseRgba(arena.SkyTop)), Theme.Rgba(ColorUtil.ParseRgba(arena.SkyBottom)));
            BeginMode3D(cam);
            Sky.Draw(arena, cam, 0);
            BeginShaderMode(Shader.Shader);
            renderer.Draw(Shader);
            EndShaderMode();
            EndMode3D();
            EndTextureMode();
        }
        _thumbs[arena.Id] = rt;
        return rt.Texture;
    }

    private void ClearThumbnails()
    {
        foreach (var t in _thumbs.Values) UnloadRenderTexture(t);
        _thumbs.Clear();
    }
    public void Quit() => _quit = true;

    public void ApplyVolume()
    {
        SetMasterVolume(Settings.MasterVolume);
        Sounds.Volume = Settings.SfxVolume;
    }

    // ------------------------------------------------------------------ shared menu backdrop

    /// <summary>The market tarp: cobalt weave, sagging string lights, warm vignette.</summary>
    public void DrawBackdrop()
    {
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        ClearBackground(Theme.Tarp);
        DrawRectangleGradientV(0, 0, sw, sh, Theme.Tarp, Theme.TarpDeep);
        // Woven tarp texture: fine diagonal threads.
        var thread = Theme.Fade(Theme.TarpLight, 0.10f);
        for (int x = -sh; x < sw; x += 14) DrawLine(x, 0, x + sh, sh, thread);
        var thread2 = Theme.Fade(Theme.TarpDeep, 0.18f);
        for (int x = 0; x < sw + sh; x += 14) DrawLine(x, 0, x - sh, sh, thread2);
        // Tarp folds (soft horizontal shading bands).
        for (int i = 0; i < 4; i++)
        {
            int y = (int)(sh * (0.18f + i * 0.22f));
            DrawRectangleGradientV(0, y, sw, 40, Theme.Fade(Theme.Aspal, 0), Theme.Fade(Theme.Aspal, 0.12f));
        }
        // String lights sagging across the top.
        float u = sh / 900f;
        int bulbs = 14;
        Vector2? prev = null;
        for (int i = 0; i <= bulbs; i++)
        {
            float t = i / (float)bulbs;
            float x = t * sw;
            float y = 26 * u + MathF.Sin(t * MathF.PI) * 34 * u + MathF.Sin(t * MathF.PI * 3) * 6 * u;
            var p = new Vector2(x, y);
            if (prev is { } q) DrawLineEx(q, p, 2, Theme.Aspal);
            prev = p;
            float flick = 0.75f + 0.25f * MathF.Sin(Time * 3 + i * 1.7f);
            DrawCircleV(p + new Vector2(0, 10 * u), 16 * u, Theme.Fade(Theme.Kuning, 0.12f * flick));
            DrawCircleV(p + new Vector2(0, 10 * u), 6 * u, Theme.Lerp(Theme.Kuning, Theme.Kapur, 0.4f * flick));
        }
    }

    /// <summary>Screen title in sign-painter lettering, with an optional subtitle.</summary>
    public void DrawHeader(string title, string? subtitle = null)
    {
        float u = GetScreenHeight() / 900f;
        Theme.SignText(title.ToUpperInvariant(), new Vector2(80 * u, 82 * u), 64 * u, Theme.Kuning, doubleShadow: true);
        if (subtitle is not null) Theme.Text(Theme.Body, subtitle, new Vector2(84 * u, 162 * u), 22 * u, Theme.Kapur);
    }

    public void DrawFooter()
    {
        float u = GetScreenHeight() / 900f;
        Theme.Text(Theme.Mono, $"{GameInfo.Name} v{GameInfo.Version}  ·  {S.T("credits.made")}", new Vector2(24 * u, GetScreenHeight() - 30 * u), 15 * u, Theme.Fade(Theme.Kapur, 0.7f));
    }

    public void Dispose()
    {
        if (IsWindowReady()) ClearThumbnails();
        Sky?.Dispose();
        Models?.Dispose();
        Shader?.Dispose();
        Sounds?.Dispose();
        Theme.Unload();
        if (IsWindowReady()) CloseWindow();
    }
}

/// <summary>Base class for menu and game screens.</summary>
public abstract class Screen(App app)
{
    protected App App { get; } = app;
    protected Gui Gui => App.Gui;
    protected float U => GetScreenHeight() / 900f;
    public virtual bool ReadyForScreenshot => true;
    /// <summary>Called by overlay screens so a suspended match keeps its connection alive.</summary>
    public virtual void BackgroundTick(float dt) { }
    public virtual void Enter() { EnableCursor(); }
    public virtual void Leave() { }
    public abstract void Update(float dt);
    public abstract void Draw();
}
