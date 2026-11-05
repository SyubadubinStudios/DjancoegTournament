using System.Numerics;
using Djancoeg.Core.Localization;

namespace Djancoeg.Core.Content;

/// <summary>The four arenas that ship with the game, generated procedurally from box primitives.</summary>
public static partial class BuiltInArenas
{
    private static LocalizedText T(string en, string id) => new(en, id);

    public static IReadOnlyList<ArenaDef> All { get; } = [PasarMalam(), TerminalBus(), SawahRonda(), AtapKampung(), Pelabuhan(), Stasiun(), Kelurahan()];

    // ------------------------------------------------------------------ Pasar Malam
    private static ArenaDef PasarMalam()
    {
        var b = new ArenaBuilder();
        const string earth = "#4A3B35", fence = "#6D4C41", stage = "#9E2A2B", wood = "#8D6E63";
        b.Box(-34, -1, -34, 34, 0, 34, SurfaceKind.Ground, earth);
        b.Box(-35, 0, -35, 35, 5, -34, SurfaceKind.Wood, fence).Box(-35, 0, 34, 35, 5, 35, SurfaceKind.Wood, fence)
         .Box(-35, 0, -34, -34, 5, 34, SurfaceKind.Wood, fence).Box(34, 0, -34, 35, 5, 34, SurfaceKind.Wood, fence);

        // Dangdut stage in the middle, with a canopy you can reach by jump pad.
        b.Box(-5, 0, -4, 5, 1.4f, 4, SurfaceKind.Wood, stage);
        b.Stairs(0, 0, 7.6f, 0, -1, 3, 4, 0.35f, 0.9f, SurfaceKind.Wood, wood);
        b.Stairs(0, 0, -7.6f, 0, 1, 3, 4, 0.35f, 0.9f, SurfaceKind.Wood, wood);
        b.Box(-6.6f, 0, -1.2f, -5.2f, 2.8f, 1.2f, SurfaceKind.Metal, "#212121").Box(5.2f, 0, -1.2f, 6.6f, 2.8f, 1.2f, SurfaceKind.Metal, "#212121");
        for (int i = -1; i <= 1; i += 2)
            for (int j = -1; j <= 1; j += 2)
                b.Box(i * 4.8f - 0.15f, 1.4f, j * 3.8f - 0.15f, i * 4.8f + 0.15f, 4.8f, j * 3.8f + 0.15f, SurfaceKind.Metal, "#B0BEC5");
        b.Box(-5.2f, 4.8f, -4.2f, 5.2f, 5.1f, 4.2f, SurfaceKind.Fabric, "#F9A825");

        // Market stalls (warung tenda) with striped canopies you can stand on.
        string[] canopy = ["#C62828", "#1565C0", "#F9A825", "#2E7D32", "#6A1B9A"];
        int k = 0;
        for (int i = -2; i <= 2; i++)
        {
            foreach (int side in (ReadOnlySpan<int>)[-1, 1])
            {
                float cx = i * 9f, cz = side * 18f;
                Stall(b, cx, cz, false, canopy[k++ % canopy.Length], wood);
            }
        }
        foreach (int side in (ReadOnlySpan<int>)[-1, 1])
            for (int j = -1; j <= 1; j++)
                Stall(b, side * 18f, j * 9f, true, canopy[k++ % canopy.Length], wood);

        // Raised walkways along north and south fences.
        b.Box(-33.9f, 3.2f, 28, 33.9f, 3.6f, 33.9f, SurfaceKind.Wood, "#795548");
        b.Box(-33.9f, 3.2f, -33.9f, 33.9f, 3.6f, -28, SurfaceKind.Wood, "#795548");
        b.Box(-29, 3.6f, 27.8f, 29, 4.4f, 28, SurfaceKind.Wood, "#5D4037").Box(-29, 3.6f, -28, 29, 4.4f, -27.8f, SurfaceKind.Wood, "#5D4037");
        b.Stairs(-31.5f, 0, 22f, 0, 1, 2.6f, 9, 0.4f, 0.65f, SurfaceKind.Wood, wood);
        b.Stairs(31.5f, 0, 22f, 0, 1, 2.6f, 9, 0.4f, 0.65f, SurfaceKind.Wood, wood);
        b.Stairs(-31.5f, 0, -22f, 0, -1, 2.6f, 9, 0.4f, 0.65f, SurfaceKind.Wood, wood);
        b.Stairs(31.5f, 0, -22f, 0, -1, 2.6f, 9, 0.4f, 0.65f, SurfaceKind.Wood, wood);

        // Jump pads ("kasur lompat") up to walkways and the stage canopy.
        b.PadTo(new(-10, 0, 11), new(-10, 3.6f, 30.5f), 1.2f).PadTo(new(10, 0, -11), new(10, 3.6f, -30.5f), 1.2f);
        b.PadTo(new(10, 0, 11), new(2, 5.1f, 1.5f), 1.2f).PadTo(new(-10, 0, -11), new(-2, 5.1f, -1.5f), 1.2f);

        // Ferris wheel (bianglala) - decorative ring plus solid legs.
        b.Box(-28.4f, 0, -3.6f, -27.6f, 9, -2.8f, SurfaceKind.Metal, "#90A4AE").Box(-28.4f, 0, 2.8f, -27.6f, 9, 3.6f, SurfaceKind.Metal, "#90A4AE");
        string[] neon = ["#FF4081", "#00E5FF", "#FFEA00", "#76FF03"];
        for (int i = 0; i < 16; i++)
        {
            float a = i * MathF.Tau / 16f;
            float y = 9.5f + MathF.Sin(a) * 6.5f, z = MathF.Cos(a) * 6.5f;
            b.Block(-28f, y - 0.5f, z, 1.2f, 1.0f, 1.0f, SurfaceKind.Neon, neon[i % 4], solid: false);
        }

        // Kickable chaos props.
        for (int i = 0; i < 6; i++) b.Prop("galon", -6 + i * 2.4f, 0, 12.5f, 0.28f, 0.5f, 0.28f, 18f, "#4FC3F7");
        for (int i = 0; i < 4; i++) b.Prop("crate", 22f, 0, -6 + i * 4f, 0.5f, 0.5f, 0.5f, 25f, "#A1887F");
        b.Prop("gerobak", -22f, 0, 10f, 1.1f, 0.7f, 0.6f, 80f, "#D84315").Prop("gerobak", 22f, 0, 12f, 1.1f, 0.7f, 0.6f, 80f, "#2E7D32");

        // Light bulbs strung across the market.
        for (int i = -3; i <= 3; i++)
        {
            b.Light(i * 9f, 4.2f, 0, i % 2 == 0 ? "#FFD180" : "#FF80AB", 10f);
            b.Box(i * 9f - 0.15f, 4.05f, -0.15f, i * 9f + 0.15f, 4.35f, 0.15f, SurfaceKind.Neon, i % 2 == 0 ? "#FFE082" : "#FF80AB", solid: false);
        }
        b.Light(-28, 9.5f, 0, "#00E5FF", 14f).Light(0, 6.2f, 0, "#FFAB40", 12f);

        // Pickups.
        b.Pickup(0, 5.1f, 0, PickupKind.Weapon, "bazooka-sambal").Pickup(0, 3.6f, 30.5f, PickupKind.Weapon, "bambu-runcing")
         .Pickup(0, 3.6f, -30.5f, PickupKind.Weapon, "petasan").Pickup(14, 0, 0, PickupKind.Weapon, "panci-shotgun")
         .Pickup(-14, 0, 0, PickupKind.Weapon, "sandal-gatling").Pickup(0, 1.4f, 0, PickupKind.HealthMega)
         .Pickup(-27, 3.6f, 30.5f, PickupKind.Armor).Pickup(27, 3.6f, -30.5f, PickupKind.Armor)
         .Pickup(-24, 0, 0, PickupKind.Quad);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(9, 9), (-9, -9), (9, -9), (-9, 9), (26, 26), (-26, -26), (26, -26), (-26, 26)])
            b.Pickup(x, 0, z, PickupKind.HealthSmall);
        b.Pickup(4.5f, 0, 22, PickupKind.Ammo).Pickup(-4.5f, 0, -22, PickupKind.Ammo).Pickup(22, 0, 4.5f, PickupKind.ArmorSmall).Pickup(-22, 0, -4.5f, PickupKind.ArmorSmall);

        // Spawns.
        b.Spawn(-28, 0, -24, 45, 0).Spawn(-28, 0, 24, 135, 0).Spawn(-12, 0, -26, 20, 0).Spawn(-12, 0, 26, 160, 0).Spawn(-24, 0, -12, 80, 0).Spawn(-24, 0, 12, 100, 0)
         .Spawn(28, 0, -24, -45, 1).Spawn(28, 0, 24, -135, 1).Spawn(12, 0, -26, -20, 1).Spawn(12, 0, 26, -160, 1).Spawn(24, 0, -12, -80, 1).Spawn(24, 0, 12, -100, 1);

        return b.Build("pasar-malam", T("Night Market Brawl", "Pasar Malam Ceria"),
            T("Fight between satay stalls and a dangdut stage under string lights.", "Tawuran di antara tenda sate dan panggung dangdut, di bawah lampu bohlam."),
            a => a with
            {
                CloudCoverage = 0.30f, CloudColor = "#3B3656", Night = true,
                SkyTop = "#04071A", SkyBottom = "#2C2547", FogColor = "#1E1A33", FogDensity = 0.012f, AmbientColor = "#5E5873",
                SunColor = "#C9D4FF", SunDirection = [-0.3f, -0.9f, 0.4f], ZoneCenter = [0, 1.4f, 0], ZoneRadius = 4f, KillY = -20,
            });
    }

    private static void Stall(ArenaBuilder b, float cx, float cz, bool rotated, string canopyColor, string wood)
    {
        float hx = rotated ? 1.4f : 2f, hz = rotated ? 2f : 1.4f;
        b.Block(cx, 0, cz, rotated ? 1.6f : 3.2f, 0.95f, rotated ? 3.2f : 1.6f, SurfaceKind.Wood, wood);
        b.Box(cx - hx, 2.6f, cz - hz, cx + hx, 2.75f, cz + hz, SurfaceKind.Fabric, canopyColor);
        for (int i = -1; i <= 1; i += 2)
            for (int j = -1; j <= 1; j += 2)
                b.Block(cx + i * (hx - 0.1f), 0, cz + j * (hz - 0.1f), 0.12f, 2.6f, 0.12f, SurfaceKind.Metal, "#9E9E9E");
    }

    // ------------------------------------------------------------------ Terminal Bus
    private static ArenaDef TerminalBus()
    {
        var b = new ArenaBuilder();
        const string asphalt = "#37474F", wall = "#B0BEC5", tile = "#CFD8DC", concrete = "#90A4AE";
        b.Box(-36, -1, -26, 36, 0, 26, SurfaceKind.Ground, asphalt);
        b.Box(-37, 0, -27, 37, 6, -26, SurfaceKind.Wall, wall).Box(-37, 0, 26, 37, 6, 27, SurfaceKind.Wall, wall)
         .Box(-37, 0, -26, -36, 6, 26, SurfaceKind.Wall, wall).Box(36, 0, -26, 37, 6, 26, SurfaceKind.Wall, wall);

        // Lane markings (decorative).
        for (int i = -6; i <= 6; i++) b.Box(i * 5 - 1.2f, 0, -0.08f, i * 5 + 1.2f, 0.02f, 0.08f, SurfaceKind.Neon, "#FFF59D", solid: false);

        // Pedestrian overpass (JPO) along the X axis with stairs at each end.
        b.Box(-20, 4.6f, -2, 20, 5f, 2, SurfaceKind.Metal, concrete);
        b.Box(-20, 5f, -2, 20, 6f, -1.85f, SurfaceKind.Metal, "#455A64").Box(-20, 5f, 1.85f, 20, 6f, 2, SurfaceKind.Metal, "#455A64");
        b.Stairs(-28, 0, 0, 1, 0, 3.6f, 12, 5f / 12f, 0.66f, SurfaceKind.Tile, tile);
        b.Stairs(28, 0, 0, -1, 0, 3.6f, 12, 5f / 12f, 0.66f, SurfaceKind.Tile, tile);
        b.Box(-10.4f, 0, -0.4f, -9.6f, 4.6f, 0.4f, SurfaceKind.Metal, concrete).Box(9.6f, 0, -0.4f, 10.4f, 4.6f, 0.4f, SurfaceKind.Metal, concrete);

        // Passenger platforms (peron) with short stairs.
        b.Box(-24, 0, 12, 24, 1.2f, 16, SurfaceKind.Tile, tile).Box(-24, 0, -16, 24, 1.2f, -12, SurfaceKind.Tile, tile);
        foreach (float x in (ReadOnlySpan<float>)[-18f, 0f, 18f])
        {
            b.Stairs(x, 0, 10.2f, 0, 1, 2.4f, 3, 0.4f, 0.6f, SurfaceKind.Tile, concrete);
            b.Stairs(x, 0, -10.2f, 0, -1, 2.4f, 3, 0.4f, 0.6f, SurfaceKind.Tile, concrete);
        }
        // Platform shelters with benches.
        for (int i = -2; i <= 2; i++)
        {
            b.Block(i * 9f, 1.2f, 15.4f, 3f, 0.45f, 0.5f, SurfaceKind.Wood, "#8D6E63");
            b.Block(i * 9f, 1.2f, -15.4f, 3f, 0.45f, 0.5f, SurfaceKind.Wood, "#8D6E63");
        }

        // Buses you can climb onto via pads.
        Bus(b, -21, 6.3f, "#00897B"); Bus(b, 19, 6.3f, "#E53935");
        Bus(b, -19, -6.3f, "#FDD835"); Bus(b, 21, -6.3f, "#1E88E5");
        // Angkot (minibus) parked sideways.
        b.Block(0, 0, 8.8f, 4.6f, 2.1f, 2.0f, SurfaceKind.Metal, "#26C6DA");
        b.Block(0, 1.1f, 8.8f, 4.62f, 0.5f, 2.02f, SurfaceKind.Neon, "#263238", solid: false);
        b.PadTo(new(-21, 0, 10.2f), new(-21, 3.2f, 6.3f), 1.0f).PadTo(new(19, 0, 10.2f), new(19, 3.2f, 6.3f), 1.0f);
        b.PadTo(new(-19, 0, -10.2f), new(-19, 3.2f, -6.3f), 1.0f).PadTo(new(21, 0, -10.2f), new(21, 3.2f, -6.3f), 1.0f);
        b.PadTo(new(-6, 0, 5f), new(-4, 5f, 0), 2.0f).PadTo(new(6, 0, -5f), new(4, 5f, 0), 2.0f);

        // Ticket booths.
        b.Box(-35.9f, 0, -5, -31, 3, 5, SurfaceKind.Wall, "#FF7043").Box(31, 0, -5, 35.9f, 3, 5, SurfaceKind.Wall, "#FF7043");
        b.Box(-36, 3, -5.5f, -30.5f, 3.3f, 5.5f, SurfaceKind.Roof, "#5D4037").Box(30.5f, 3, -5.5f, 36, 3.3f, 5.5f, SurfaceKind.Roof, "#5D4037");

        for (int i = 0; i < 5; i++) b.Prop("crate", -30 + i * 1.3f, 0, 20, 0.45f, 0.45f, 0.45f, 22f, "#BCAAA4");
        for (int i = 0; i < 5; i++) b.Prop("galon", 26 + (i % 2), 0, -20 + i * 1.2f, 0.28f, 0.5f, 0.28f, 18f, "#4FC3F7");
        b.Prop("gerobak", 0, 0, -20, 1.1f, 0.7f, 0.6f, 80f, "#6D4C41").Prop("ban", -30, 0, -20, 0.45f, 0.45f, 0.45f, 15f, "#212121");

        for (int i = -3; i <= 3; i++) b.Light(i * 10f, 5.5f, 14, "#FFF3E0", 11f).Light(i * 10f, 5.5f, -14, "#FFF3E0", 11f);

        b.Pickup(0, 5f, 0, PickupKind.Weapon, "bambu-runcing").Pickup(-21, 3.2f, 6.3f, PickupKind.Weapon, "bazooka-sambal")
         .Pickup(21, 3.2f, -6.3f, PickupKind.Weapon, "petasan").Pickup(0, 1.2f, 14, PickupKind.Weapon, "panci-shotgun")
         .Pickup(0, 1.2f, -14, PickupKind.Weapon, "sandal-gatling").Pickup(0, 0, -6, PickupKind.HealthMega)
         .Pickup(19, 3.2f, 6.3f, PickupKind.Armor).Pickup(-19, 3.2f, -6.3f, PickupKind.Armor).Pickup(-33, 0, 12, PickupKind.Quad);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-12, 20), (12, 20), (-12, -20), (12, -20), (-30, 10), (30, -10), (30, 10), (-30, -10)])
            b.Pickup(x, 0, z, PickupKind.HealthSmall);
        b.Pickup(-14, 1.2f, 14, PickupKind.Ammo).Pickup(14, 1.2f, -14, PickupKind.Ammo).Pickup(14, 1.2f, 14, PickupKind.ArmorSmall).Pickup(-14, 1.2f, -14, PickupKind.ArmorSmall);

        b.Spawn(-32, 0, -20, 30, 0).Spawn(-32, 0, 20, 150, 0).Spawn(-24, 1.2f, 14, 90, 0).Spawn(-24, 1.2f, -14, 90, 0).Spawn(-30, 0, 0, 90, 0).Spawn(-14, 0, 22, 120, 0)
         .Spawn(32, 0, -20, -30, 1).Spawn(32, 0, 20, -150, 1).Spawn(24, 1.2f, 14, -90, 1).Spawn(24, 1.2f, -14, -90, 1).Spawn(30, 0, 0, -90, 1).Spawn(14, 0, -22, -60, 1);

        return b.Build("terminal-bus", T("Intercity Bus Terminal", "Terminal Antar Kota"),
            T("Buses, platforms and an overpass. Watch out for the touts.", "Bus, peron, dan jembatan penyeberangan. Awas calo."),
            a => a with
            {
                CloudCoverage = 0.45f, CloudColor = "#FFD9B5", Night = false,
                SkyTop = "#2D5B8E", SkyBottom = "#F3A35E", FogColor = "#C99474", FogDensity = 0.010f, AmbientColor = "#8C7A70",
                SunColor = "#FFC98E", SunDirection = [0.75f, -0.38f, 0.3f], ZoneCenter = [0, 5f, 0], ZoneRadius = 3.5f,
            });
    }

    private static void Bus(ArenaBuilder b, float cx, float cz, string color)
    {
        b.Block(cx, 0, cz, 10f, 3.2f, 2.6f, SurfaceKind.Metal, color);
        b.Block(cx, 1.6f, cz, 10.04f, 0.9f, 2.64f, SurfaceKind.Neon, "#263238", solid: false);
        b.Block(cx, 0.6f, cz, 10.05f, 0.25f, 2.65f, SurfaceKind.Neon, "#FFFFFF", solid: false);
    }

    // ------------------------------------------------------------------ Sawah & Pos Ronda
    private static ArenaDef SawahRonda()
    {
        var b = new ArenaBuilder();
        const string grass = "#5B8C2A", dike = "#7A5C3A", bamboo = "#C8A951", thatch = "#A1887F";
        b.Box(-40, -1, -40, 40, 0, 40, SurfaceKind.Grass, grass);
        // Invisible-ish bamboo fence boundary.
        b.Box(-41, 0, -41, 41, 4, -40, SurfaceKind.Wood, bamboo).Box(-41, 0, 40, 41, 4, 41, SurfaceKind.Wood, bamboo)
         .Box(-41, 0, -40, -40, 4, 40, SurfaceKind.Wood, bamboo).Box(40, 0, -40, 41, 4, 40, SurfaceKind.Wood, bamboo);

        // Rice paddies (decorative water) separated by walkable dikes.
        for (int i = -3; i <= 3; i++)
        {
            b.Box(-40, 0, i * 10 - 0.5f, -12, 0.35f, i * 10 + 0.5f, SurfaceKind.Ground, dike);
            b.Box(12, 0, i * 10 - 0.5f, 40, 0.35f, i * 10 + 0.5f, SurfaceKind.Ground, dike);
        }
        for (int i = -3; i <= 2; i++)
        {
            b.Box(-39.5f, 0, i * 10 + 0.5f, -12.5f, 0.05f, i * 10 + 9.5f, SurfaceKind.Water, "#5DADE2", solid: false);
            b.Box(12.5f, 0, i * 10 + 0.5f, 39.5f, 0.05f, i * 10 + 9.5f, SurfaceKind.Water, "#5DADE2", solid: false);
        }

        // Pos ronda (night-watch hut) on stilts in the middle.
        b.Box(-3, 1.6f, -3, 3, 2.0f, 3, SurfaceKind.Wood, "#8D6E63");
        for (int i = -1; i <= 1; i += 2)
            for (int j = -1; j <= 1; j += 2)
                b.Block(i * 2.7f, 0, j * 2.7f, 0.3f, 4.2f, 0.3f, SurfaceKind.Wood, "#6D4C41");
        b.Box(-3.6f, 4.2f, -3.6f, 3.6f, 4.5f, 3.6f, SurfaceKind.Roof, thatch);
        b.Stairs(0, 0, 5f, 0, -1, 2f, 5, 0.4f, 0.4f, SurfaceKind.Wood, "#A1887F");
        b.Stairs(0, 0, -5f, 0, 1, 2f, 5, 0.4f, 0.4f, SurfaceKind.Wood, "#A1887F");
        b.Block(2.2f, 2.0f, 0, 0.25f, 1.1f, 0.25f, SurfaceKind.Wood, "#4E342E", solid: false); // kentongan

        // Terraced hills (terasering) in two corners.
        Terrace(b, 26, 26, grass);
        Terrace(b, -26, -26, grass);

        // Coconut trees.
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-8, 14), (8, -14), (-16, 30), (16, -30), (-30, 8), (30, -8), (20, 8), (-20, -8)])
        {
            b.Block(x, 0, z, 0.5f, 7f, 0.5f, SurfaceKind.Wood, "#795548");
            b.Block(x, 6.6f, z, 3.6f, 0.4f, 0.8f, SurfaceKind.Grass, "#2E7D32", solid: false);
            b.Block(x, 6.6f, z, 0.8f, 0.4f, 3.6f, SurfaceKind.Grass, "#388E3C", solid: false);
        }

        // Scarecrows (orang-orangan sawah).
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-25, 15), (25, -15)])
        {
            b.Block(x, 0, z, 0.15f, 2.2f, 0.15f, SurfaceKind.Wood, "#6D4C41");
            b.Block(x, 1.5f, z, 1.6f, 0.12f, 0.12f, SurfaceKind.Wood, "#6D4C41", solid: false);
            b.Block(x, 2.2f, z, 0.9f, 0.3f, 0.9f, SurfaceKind.Fabric, "#F9A825", solid: false);
        }

        b.PadTo(new(-6, 0, 0), new(-1.5f, 4.5f, 0), 1.0f).PadTo(new(6, 0, 0), new(1.5f, 4.5f, 0), 1.0f);
        b.PadTo(new(10, 0, 18), new(26, 5f, 26), 1.5f).PadTo(new(-10, 0, -18), new(-26, 5f, -26), 1.5f);

        for (int i = 0; i < 6; i++) b.Prop("karung", -6 + i * 2.2f, 0, 20f, 0.4f, 0.3f, 0.3f, 20f, "#D7CCC8");
        for (int i = 0; i < 6; i++) b.Prop("karung", -6 + i * 2.2f, 0, -20f, 0.4f, 0.3f, 0.3f, 20f, "#D7CCC8");
        b.Prop("gerobak", 8, 0, 6, 1.1f, 0.7f, 0.6f, 80f, "#F57F17").Prop("ban", -8, 0, -6, 0.45f, 0.45f, 0.45f, 15f, "#212121");

        b.Light(0, 3.8f, 0, "#FFCC80", 9f).Light(26, 6.5f, 26, "#FFF59D", 10f).Light(-26, 6.5f, -26, "#FFF59D", 10f);

        b.Pickup(0, 4.5f, 0, PickupKind.Weapon, "bambu-runcing").Pickup(26, 5f, 26, PickupKind.Weapon, "bazooka-sambal")
         .Pickup(-26, 5f, -26, PickupKind.Weapon, "petasan").Pickup(0, 2.0f, 0, PickupKind.HealthMega)
         .Pickup(-20, 0, 20, PickupKind.Weapon, "panci-shotgun").Pickup(20, 0, -20, PickupKind.Weapon, "sandal-gatling")
         .Pickup(26, 0, -35, PickupKind.Armor).Pickup(-26, 0, 35, PickupKind.Armor).Pickup(0, 0, 32, PickupKind.Quad);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-30, -5), (30, 5), (5, 30), (-5, -30), (14, 14), (-14, -14), (14, -6), (-14, 6)])
            b.Pickup(x, 0, z, PickupKind.HealthSmall);
        b.Pickup(-34, 0, 0, PickupKind.Ammo).Pickup(34, 0, 0, PickupKind.Ammo).Pickup(0, 0, -32, PickupKind.ArmorSmall).Pickup(0, 0, 10, PickupKind.ArmorSmall);

        b.Spawn(-34, 0, 34, 135, 0).Spawn(-34, 0, 10, 100, 0).Spawn(-20, 0, 34, 160, 0).Spawn(-34, 0, -10, 70, 0).Spawn(-10, 0, 34, 180, 0).Spawn(-30, 0, 24, 120, 0)
         .Spawn(34, 0, -34, -45, 1).Spawn(34, 0, -10, -80, 1).Spawn(20, 0, -34, -20, 1).Spawn(34, 0, 10, -110, 1).Spawn(10, 0, -34, 0, 1).Spawn(30, 0, -24, -60, 1);

        return b.Build("sawah-ronda", T("Rice Fields & Watch Post", "Sawah & Pos Ronda"),
            T("Open rice terraces, coconut trees and a night-watch hut. Snipers love it.", "Terasering terbuka, pohon kelapa, dan pos ronda. Surganya penembak jitu."),
            a => a with
            {
                CloudCoverage = 0.55f, CloudColor = "#FFFFFF", Night = false,
                SkyTop = "#2C6FC6", SkyBottom = "#BFE0F5", FogColor = "#C6E2F2", FogDensity = 0.007f, AmbientColor = "#8FA8B8",
                SunColor = "#FFF6E0", SunDirection = [-0.5f, -0.8f, -0.35f], ZoneCenter = [0, 2.0f, 0], ZoneRadius = 3.2f,
            });
    }

    private static void Terrace(ArenaBuilder b, float cx, float cz, string grass)
    {
        for (int level = 0; level < 5; level++)
        {
            float half = 11f - level * 2.2f;
            string c = ArenaBuilder.Shade(grass, 1f + level * 0.07f);
            b.Box(cx - half, level, cz - half, cx + half, level + 1, cz + half, SurfaceKind.Grass, c);
        }
    }

    // ------------------------------------------------------------------ Atap Kampung
    private static ArenaDef AtapKampung()
    {
        var b = new ArenaBuilder();
        const string alley = "#6E6259", wall = "#9E9E9E";
        // Two ground halves with a river (kali) in between - fall in and you're out.
        b.Box(-27, -1, -27, -1.5f, 0, 27, SurfaceKind.Ground, alley).Box(1.5f, -1, -27, 27, 0, 27, SurfaceKind.Ground, alley);
        b.Box(-1.5f, -6, -27, 1.5f, -5.6f, 27, SurfaceKind.Water, "#33691E", solid: false);
        b.Box(-28, -6, -28, 28, 12, -27, SurfaceKind.Wall, wall).Box(-28, -6, 27, 28, 12, 28, SurfaceKind.Wall, wall)
         .Box(-28, -6, -27, -27, 12, 27, SurfaceKind.Wall, wall).Box(27, -6, -27, 28, 12, 27, SurfaceKind.Wall, wall);

        float[] centers = [-19.5f, -6.5f, 6.5f, 19.5f];
        float[,] heights =
        {
            { 3.2f, 4.8f, 6.4f, 4.0f },
            { 6.4f, 3.2f, 8.0f, 4.8f },
            { 4.8f, 8.0f, 3.2f, 6.4f },
            { 4.0f, 6.4f, 4.8f, 3.2f },
        };
        string[] paint = ["#80CBC4", "#F48FB1", "#FFE082", "#90CAF9", "#CE93D8", "#A5D6A7", "#FFAB91", "#B0BEC5"];
        for (int ix = 0; ix < 4; ix++)
        {
            for (int iz = 0; iz < 4; iz++)
            {
                float cx = centers[ix], cz = centers[iz], h = heights[ix, iz];
                string color = paint[(ix * 4 + iz) % paint.Length];
                b.Box(cx - 5, 0, cz - 5, cx + 5, h, cz + 5, SurfaceKind.Wall, color);
                b.Box(cx - 5.05f, h, cz - 5.05f, cx + 5.05f, h + 0.5f, cz - 4.85f, SurfaceKind.Wall, ArenaBuilder.Shade(color, 0.8f));
                b.Box(cx - 5.05f, 1.0f, cz - 1.0f, cx - 4.95f, 2.2f, cz + 1.0f, SurfaceKind.Neon, "#4E342E", solid: false);
                int steps = (int)MathF.Round(h / 0.4f);
                float side = ix < 2 ? -1 : 1;
                float sx = cx + side * 5.6f;
                b.Stairs(sx, 0, cz + 4.6f, 0, -1, 1.2f, steps, 0.4f, 9.2f / steps, SurfaceKind.Tile, "#BDBDBD");
                // Water tank (toren) on every other roof.
                if (((ix + iz) & 1) == 0) b.Block(cx + 2.5f, h, cz + 2.5f, 1.3f, 1.6f, 1.3f, SurfaceKind.Metal, "#FF7043");
            }
        }
        // Bridges over the river.
        foreach (float z in (ReadOnlySpan<float>)[-13f, 0f, 13f])
            b.Box(-1.6f, -0.3f, z - 1.4f, 1.6f, 0, z + 1.4f, SurfaceKind.Wood, "#8D6E63");
        // Clotheslines (decorative).
        string[] cloth = ["#E53935", "#FDD835", "#1E88E5", "#FFFFFF"];
        for (int i = 0; i < 10; i++)
            b.Block(-10 + i * 2.2f, 2.8f, -13f, 0.8f, 0.9f, 0.05f, SurfaceKind.Fabric, cloth[i % 4], solid: false);

        // Jump pads from alleys up to rooftops.
        b.PadTo(new(-13, 0, 0), new(-6.5f, 3.2f, -6.5f), 1.5f).PadTo(new(13, 0, 0), new(6.5f, 3.2f, 6.5f), 1.5f);
        b.PadTo(new(-13, 0, 13), new(-6.5f, 8.0f, 6.5f), 1.0f).PadTo(new(13, 0, -13), new(6.5f, 8.0f, -6.5f), 1.0f);
        b.PadTo(new(-24.5f, 0, 0), new(-19.5f, 6.4f, 6.5f), 1.0f).PadTo(new(24.5f, 0, 0), new(19.5f, 6.4f, -6.5f), 1.0f);

        for (int i = 0; i < 4; i++) b.Prop("galon", -24 + i * 0.8f, 0, 13f, 0.28f, 0.5f, 0.28f, 18f, "#4FC3F7");
        for (int i = 0; i < 4; i++) b.Prop("crate", 24 - i * 1.1f, 0, -13f, 0.45f, 0.45f, 0.45f, 22f, "#A1887F");
        b.Prop("ban", 8, 0, 0, 0.45f, 0.45f, 0.45f, 15f, "#212121").Prop("gerobak", -8, 0, 13, 1.1f, 0.7f, 0.6f, 80f, "#0277BD");

        for (int i = -2; i <= 2; i++) b.Light(i * 6.5f, 3.5f, 0, "#FFE0B2", 9f).Light(i * 6.5f, 3.5f, 13, "#FFCCBC", 9f).Light(i * 6.5f, 3.5f, -13, "#FFCCBC", 9f);

        b.Pickup(6.5f, 8.0f, -6.5f, PickupKind.Weapon, "bambu-runcing").Pickup(-6.5f, 8.0f, 6.5f, PickupKind.Weapon, "bazooka-sambal")
         .Pickup(-19.5f, 6.4f, 6.5f, PickupKind.Weapon, "petasan").Pickup(19.5f, 6.4f, -6.5f, PickupKind.Weapon, "panci-shotgun")
         .Pickup(0, 0, 13, PickupKind.Weapon, "sandal-gatling").Pickup(0, 0, 0, PickupKind.HealthMega)
         .Pickup(-6.5f, 3.2f, -6.5f, PickupKind.Armor).Pickup(6.5f, 3.2f, 6.5f, PickupKind.Armor).Pickup(0, 0, -13, PickupKind.Quad);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-13, -20), (13, 20), (-13, 20), (13, -20), (-26.3f, -20), (26.3f, 20), (-19.5f, -19.5f), (19.5f, 19.5f)])
            b.Pickup(x, x is -19.5f or 19.5f ? 3.2f : 0, z, PickupKind.HealthSmall);
        b.Pickup(-13, 0, -6.5f, PickupKind.Ammo).Pickup(13, 0, 6.5f, PickupKind.Ammo).Pickup(-26.3f, 0, 20, PickupKind.ArmorSmall).Pickup(26.3f, 0, -20, PickupKind.ArmorSmall);

        b.Spawn(-24.5f, 0, -24.5f, 45, 0).Spawn(-24.5f, 0, 24.5f, 135, 0).Spawn(-13, 0, -24.5f, 20, 0).Spawn(-13, 0, 24.5f, 160, 0).Spawn(-19.5f, 3.2f, -19.5f, 45, 0).Spawn(-13, 0, 6.5f, 90, 0)
         .Spawn(24.5f, 0, 24.5f, -135, 1).Spawn(24.5f, 0, -24.5f, -45, 1).Spawn(13, 0, 24.5f, -160, 1).Spawn(13, 0, -24.5f, -20, 1).Spawn(19.5f, 3.2f, 19.5f, -135, 1).Spawn(13, 0, -6.5f, -90, 1);

        return b.Build("atap-kampung", T("Kampung Rooftops", "Atap Kampung"),
            T("Rooftops, alleys, water tanks and a river you really don't want to fall in.", "Atap rumah, gang sempit, toren air, dan kali yang jangan sampai kecebur."),
            a => a with
            {
                CloudCoverage = 0.40f, CloudColor = "#FFB89A", Night = false,
                SkyTop = "#1B2D55", SkyBottom = "#FF8A57", FogColor = "#7A4A55", FogDensity = 0.011f, AmbientColor = "#7D6070",
                SunColor = "#FFB38A", SunDirection = [0.72f, -0.32f, -0.4f], ZoneCenter = [0, 0f, 0], ZoneRadius = 3.0f, KillY = -4f,
            });
    }

    public static Vector3 ToV(float[] a) => new(a[0], a[1], a[2]);
}
