using System.Numerics;
using Djancoeg.Core.Localization;

namespace Djancoeg.Core.Content;

/// <summary>v1.2 "Pasar Rakyat" arenas: harbour, train station and the village office.</summary>
public static partial class BuiltInArenas
{
    // ------------------------------------------------------------------ Pelabuhan (harbour)
    private static ArenaDef Pelabuhan()
    {
        var b = new ArenaBuilder();
        const string concrete = "#7A7F85", hull = "#7B4A2A", deck = "#A0703F", sail = "#F2E4C4";
        b.Box(-40, -2.2f, -30, 40, -1.6f, 30, SurfaceKind.Water, "#1E5F7A", solid: false);
        b.Box(-38, -1, -28, 38, 0, -6, SurfaceKind.Ground, concrete).Box(-38, -1, 6, 38, 0, 28, SurfaceKind.Ground, concrete);
        b.Box(-39, -3, -29, 39, 6, -28, SurfaceKind.Wall, "#8E8574").Box(-39, -3, 28, 39, 6, 29, SurfaceKind.Wall, "#8E8574")
         .Box(-39, -3, -28, -38, 6, 28, SurfaceKind.Wall, "#8E8574").Box(38, -3, -28, 39, 6, 28, SurfaceKind.Wall, "#8E8574");
        for (int x = -32; x <= 32; x += 8)
        {
            b.Block(x, 0, -6.3f, 0.4f, 0.45f, 0.4f, SurfaceKind.Metal, "#263238");
            b.Block(-x, 0, 6.3f, 0.4f, 0.45f, 0.4f, SurfaceKind.Metal, "#263238");
        }

        // Pinisi ship in the channel.
        b.Box(-14, -1.6f, -3, 14, 1.2f, 3, SurfaceKind.Wood, hull);
        b.Box(-14, 1.19f, -2.9f, 14, 1.2f, 2.9f, SurfaceKind.Wood, deck, solid: false);
        b.Box(-14, 1.2f, -3, -9, 2.4f, 3, SurfaceKind.Wood, "#8D5A34");                 // stern cabin
        b.Box(11, 1.2f, -2.2f, 14, 1.8f, 2.2f, SurfaceKind.Wood, "#8D5A34");             // bow deck
        b.Stairs(-6.5f, 1.2f, 0, -1, 0, 2f, 3, 0.4f, 0.8f, SurfaceKind.Wood, deck);
        // Railings with gaps for the gangplanks (x 5..7 south, -7..-5 north).
        b.Box(-9, 1.2f, -3, 5, 1.8f, -2.85f, SurfaceKind.Wood, hull).Box(7, 1.2f, -3, 11, 1.8f, -2.85f, SurfaceKind.Wood, hull);
        b.Box(-9, 1.2f, 2.85f, -7, 1.8f, 3, SurfaceKind.Wood, hull).Box(-5, 1.2f, 2.85f, 11, 1.8f, 3, SurfaceKind.Wood, hull);
        b.Block(-3, 1.2f, 0, 0.4f, 12f, 0.4f, SurfaceKind.Wood, "#5D4037");
        b.Block(5, 1.2f, 0, 0.35f, 9f, 0.35f, SurfaceKind.Wood, "#5D4037");
        b.Block(-2.9f, 4.2f, 0, 0.08f, 5.5f, 5.4f, SurfaceKind.Fabric, sail, solid: false);
        b.Block(5.1f, 3.8f, 0, 0.08f, 4.5f, 4.6f, SurfaceKind.Fabric, sail, solid: false);
        b.Box(-4.2f, 9.0f, -1.2f, -1.8f, 9.3f, 1.2f, SurfaceKind.Wood, "#6D4C41");         // crow's nest
        // Gangplanks.
        b.Stairs(6, 0, -7.4f, 0, 1, 2f, 3, 0.4f, 1.4f, SurfaceKind.Wood, "#8D6E63");
        b.Stairs(-6, 0, 7.4f, 0, -1, 2f, 3, 0.4f, 1.4f, SurfaceKind.Wood, "#8D6E63");

        // Container stacks, mirrored for the two teams.
        string[] colors = ["#C0392B", "#1F618D", "#F39C12", "#27AE60", "#8E44AD", "#16A085"];
        var stacks = new (float x, float z, int h)[] { (-28, -11, 2), (-20, -11, 1), (22, -11, 3), (30, -11, 1), (-30, -20, 1), (-22, -20, 3), (18, -20, 2), (26, -20, 1) };
        int ci = 0;
        foreach (var (x, z, h) in stacks)
        {
            foreach (int m in (ReadOnlySpan<int>)[1, -1])
            {
                for (int level = 0; level < h; level++)
                    b.Block(x * m, level * 2.44f, z * m, 6.1f, 2.44f, 2.6f, SurfaceKind.Metal, colors[ci++ % colors.Length]);
                float top = h * 2.44f;
                if (h == 1)
                {
                    // Stairs from the open side.
                    float sz = z == -11 ? -7f : -24f;
                    int dir = z == -11 ? -1 : 1;
                    b.Stairs(x * m, 0, sz * m, 0, dir * m, 2f, 6, top / 6f, 0.45f, SurfaceKind.Metal, "#546E7A");
                }
                else b.PadTo(new(x * m, 0, -15.5f * m), new(x * m, top, z * m), 1.2f);
            }
        }

        // Harbour crane legs and a warehouse.
        b.Block(-6, 0, 16, 1, 14, 1, SurfaceKind.Metal, "#F9A825").Block(6, 0, 16, 1, 14, 1, SurfaceKind.Metal, "#F9A825");
        b.Box(-6.5f, 14, 15.5f, 6.5f, 15, 16.5f, SurfaceKind.Metal, "#F9A825", solid: false);
        b.Box(-6.5f, 14, -16.5f, 6.5f, 15, -15.5f, SurfaceKind.Metal, "#F9A825", solid: false);
        b.Block(-6, 0, -16, 1, 14, 1, SurfaceKind.Metal, "#F9A825").Block(6, 0, -16, 1, 14, 1, SurfaceKind.Metal, "#F9A825");
        b.PadTo(new(2.5f, 1.2f, 0.7f), new(-3.4f, 9.3f, 0.7f), 1.0f);

        for (int i = 0; i < 6; i++) b.Prop("jerigen", -10 + i * 1.1f, 0, -8f, 0.22f, 0.3f, 0.15f, 12f, "#1E88E5");
        for (int i = 0; i < 6; i++) b.Prop("jerigen", 10 - i * 1.1f, 0, 8f, 0.22f, 0.3f, 0.15f, 12f, "#E53935");
        b.Prop("crate", -5, 1.2f, -1.5f, 0.45f, 0.45f, 0.45f, 22f, "#A1887F").Prop("crate", 9, 1.2f, -1.5f, 0.45f, 0.45f, 0.45f, 22f, "#A1887F");
        b.Prop("ban", -34, 0, -8, 0.45f, 0.45f, 0.45f, 15f, "#212121").Prop("ban", 34, 0, 8, 0.45f, 0.45f, 0.45f, 15f, "#212121");
        b.Prop("kursi", -12, 0, -24, 0.25f, 0.42f, 0.25f, 6f, "#E53935").Prop("kursi", 12, 0, 24, 0.25f, 0.42f, 0.25f, 6f, "#1E88E5");

        for (int x = -30; x <= 30; x += 15) { b.Light(x, 5, -7, "#FFE0B2", 10f).Light(-x, 5, 7, "#FFE0B2", 10f); }
        b.Light(-3, 10.5f, 0, "#FFCA28", 8f).Light(0, 3, 0, "#FFB74D", 9f);

        b.Pickup(-3.4f, 9.3f, 0.7f, PickupKind.Quad).Pickup(8, 1.2f, 0, PickupKind.Weapon, "kipas-sate")
         .Pickup(-11.5f, 2.4f, 0, PickupKind.Weapon, "bambu-runcing").Pickup(12.5f, 1.8f, 0, PickupKind.HealthMega)
         .Pickup(22, 7.32f, -11, PickupKind.Weapon, "bazooka-sambal").Pickup(-22, 7.32f, 11, PickupKind.Weapon, "bazooka-sambal")
         .Pickup(-28, 4.88f, -11, PickupKind.Weapon, "cobek-lempar").Pickup(28, 4.88f, 11, PickupKind.Weapon, "cobek-lempar")
         .Pickup(0, 0, -14, PickupKind.Weapon, "kerupuk-shuriken").Pickup(0, 0, 14, PickupKind.Weapon, "kerupuk-shuriken")
         .Pickup(-14, 0, -24, PickupKind.Weapon, "panci-shotgun").Pickup(14, 0, 24, PickupKind.Weapon, "panci-shotgun")
         .Pickup(-20, 2.44f, -11, PickupKind.Armor).Pickup(20, 2.44f, 11, PickupKind.Armor);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-34, -20), (34, 20), (10, -24), (-10, 24), (-8, -15.5f), (8, 15.5f)])
            b.Pickup(x, 0, z, PickupKind.HealthSmall);
        b.Pickup(30, 0, -15.5f, PickupKind.Ammo).Pickup(-30, 0, 15.5f, PickupKind.Ammo);

        foreach (var (x, z, yaw) in new (float, float, float)[] { (-34, -25, 30), (-12, -25, 10), (4, -25, 0), (-4, -15, 0), (12, -8, -20), (-34, -15, 60) })
        {
            b.Spawn(x, 0, z, yaw, 0);
            b.Spawn(-x, 0, -z, yaw + 180, 1);
        }

        return b.Build("pelabuhan", T("Harbour Brawl", "Pelabuhan Sunda Kelapa"),
            T("Container stacks, a pinisi schooner and a channel you really don't want to fall into.", "Tumpukan kontainer, kapal pinisi, dan kanal yang jangan sampai kecebur."),
            a => a with
            {
                SkyTop = "#2C3E73", SkyBottom = "#F0A868", FogColor = "#9A8FA8", FogDensity = 0.010f, AmbientColor = "#7E7A8C",
                SunColor = "#FFD3A1", SunDirection = [0.5f, -0.6f, 0.4f], KillY = -3f, ZoneCenter = [6, 1.2f, 0], ZoneRadius = 3.5f,
                FlagBases = [[-32, 0, -23], [32, 0, 23]], RaceGoal = [-3.4f, 9.3f, 0.7f],
            });
    }

    // ------------------------------------------------------------------ Stasiun Kereta (train station)
    private static ArenaDef Stasiun()
    {
        var b = new ArenaBuilder();
        const string tile = "#D7CCC8", ballast = "#6B5E55", brick = "#A0522D";
        b.Box(-40, -1, -24, 40, 0, 24, SurfaceKind.Ground, ballast);
        b.Box(-41, 0, -25, 41, 7, -24, SurfaceKind.Wall, "#BCAAA4").Box(-41, 0, 24, 41, 7, 25, SurfaceKind.Wall, "#BCAAA4")
         .Box(-41, 0, -24, -40, 7, 24, SurfaceKind.Wall, "#BCAAA4").Box(40, 0, -24, 41, 7, 24, SurfaceKind.Wall, "#BCAAA4");

        // Platforms with short steps down to the tracks.
        b.Box(-36, 0, -24, 36, 1, -14, SurfaceKind.Tile, tile).Box(-36, 0, 14, 36, 1, 24, SurfaceKind.Tile, tile).Box(-30, 0, -3, 30, 1, 3, SurfaceKind.Tile, tile);
        b.Box(-36, 1, -14.1f, 36, 1.01f, -13.8f, SurfaceKind.Neon, "#FFEB3B", solid: false).Box(-36, 1, 13.8f, 36, 1.01f, 14.1f, SurfaceKind.Neon, "#FFEB3B", solid: false);
        foreach (float x in (ReadOnlySpan<float>)[-20f, 0f, 20f])
        {
            b.Stairs(x, 0, -12.8f, 0, -1, 3, 2, 0.5f, 0.6f, SurfaceKind.Tile, "#BCAAA4");
            b.Stairs(x, 0, 12.8f, 0, 1, 3, 2, 0.5f, 0.6f, SurfaceKind.Tile, "#BCAAA4");
        }
        foreach (float x in (ReadOnlySpan<float>)[-24f, 24f])
        {
            b.Stairs(x, 0, -4.2f, 0, 1, 3, 2, 0.5f, 0.6f, SurfaceKind.Tile, "#BCAAA4");
            b.Stairs(x, 0, 4.2f, 0, -1, 3, 2, 0.5f, 0.6f, SurfaceKind.Tile, "#BCAAA4");
        }
        // Rails (decorative).
        foreach (float z in (ReadOnlySpan<float>)[-8.7f, -7.3f, 7.3f, 8.7f])
            b.Box(-40, 0, z - 0.06f, 40, 0.12f, z + 0.06f, SurfaceKind.Metal, "#9E9E9E", solid: false);

        // Commuter carriages (walkable roofs reached by jump pads).
        foreach (var (x0, x1, z) in new (float, float, float)[] { (-34, -17, -8), (-16, 1, -8), (-2, 15, 8), (16, 33, 8) })
        {
            b.Box(x0, 0, z - 1.5f, x1, 3.8f, z + 1.5f, SurfaceKind.Metal, "#ECEFF1");
            b.Box(x0, 1.6f, z - 1.52f, x1, 2.4f, z + 1.52f, SurfaceKind.Neon, "#263238", solid: false);
            b.Box(x0, 0.5f, z - 1.53f, x1, 0.8f, z + 1.53f, SurfaceKind.Neon, "#E53935", solid: false);
        }
        b.PadTo(new(-25, 1, -15.5f), new(-25, 3.8f, -8), 1.0f).PadTo(new(-8, 1, -15.5f), new(-8, 3.8f, -8), 1.0f);
        b.PadTo(new(25, 1, 15.5f), new(25, 3.8f, 8), 1.0f).PadTo(new(8, 1, 15.5f), new(8, 3.8f, 8), 1.0f);
        b.PadTo(new(-12, 1, -1.6f), new(-12, 3.8f, -8), 1.0f).PadTo(new(12, 1, 1.6f), new(12, 3.8f, 8), 1.0f);

        // Footbridge over the tracks.
        b.Box(-3, 5.6f, -20, 3, 6, 20, SurfaceKind.Tile, "#B0BEC5");
        b.Box(-3.2f, 6, -17.5f, -3, 7, 20, SurfaceKind.Metal, "#455A64").Box(3, 6, -20, 3.2f, 7, 17.5f, SurfaceKind.Metal, "#455A64");
        b.Stairs(-10.2f, 1, -19, 1, 0, 2.4f, 12, 5f / 12f, 0.6f, SurfaceKind.Tile, tile);
        b.Stairs(10.2f, 1, 19, -1, 0, 2.4f, 12, 5f / 12f, 0.6f, SurfaceKind.Tile, tile);
        b.Block(0, 1, 0, 0.6f, 4.6f, 0.6f, SurfaceKind.Metal, "#78909C");

        // Clock tower at the west end.
        b.Box(-40, 0, -3, -35, 6, 3, SurfaceKind.Wall, brick);
        b.Box(-35.02f, 3.5f, -1.2f, -34.98f, 5.5f, 1.2f, SurfaceKind.Neon, "#FFF8E1", solid: false);
        b.Stairs(-26.5f, 1, 0, -1, 0, 2.4f, 13, 5f / 13f, 0.65f, SurfaceKind.Wood, "#8D6E63");

        // Ticket hall and platform canopies (kept clear of the footbridge stairs).
        b.Box(35, 0, -10, 40, 4, 10, SurfaceKind.Wall, "#FFAB91").Box(34.5f, 4, -10.5f, 40, 4.3f, 10.5f, SurfaceKind.Roof, "#6D4C41");
        b.Box(-36, 4.6f, -24, -13, 4.8f, -17, SurfaceKind.Roof, "#90A4AE").Box(13, 4.6f, -24, 36, 4.8f, -17, SurfaceKind.Roof, "#90A4AE");
        b.Box(-36, 4.6f, 17, -13, 4.8f, 24, SurfaceKind.Roof, "#90A4AE").Box(13, 4.6f, 17, 36, 4.8f, 24, SurfaceKind.Roof, "#90A4AE");

        for (int i = 0; i < 4; i++) { b.Prop("kursi", -30 + i * 3, 1, -21.5f, 0.25f, 0.42f, 0.25f, 6f, "#1E88E5"); b.Prop("kursi", 30 - i * 3, 1, 21.5f, 0.25f, 0.42f, 0.25f, 6f, "#E53935"); }
        b.Prop("karung", 6, 1, -16, 0.4f, 0.3f, 0.3f, 20f, "#D7CCC8").Prop("karung", -6, 1, 16, 0.4f, 0.3f, 0.3f, 20f, "#D7CCC8");
        b.Prop("crate", 18, 1, 0, 0.45f, 0.45f, 0.45f, 22f, "#A1887F").Prop("crate", -18, 1, 0, 0.45f, 0.45f, 0.45f, 22f, "#A1887F");

        for (int x = -30; x <= 30; x += 12) { b.Light(x, 4.2f, -19, "#FFF3E0", 10f).Light(x, 4.2f, 19, "#FFF3E0", 10f); }
        b.Light(0, 7.5f, 0, "#FFE082", 9f).Light(-37.5f, 7, 0, "#FFF8E1", 8f);

        b.Pickup(0, 6, 0, PickupKind.Weapon, "cobek-lempar").Pickup(0, 1, 0, PickupKind.HealthMega)
         .Pickup(-25, 3.8f, -8, PickupKind.Quad).Pickup(24, 3.8f, 8, PickupKind.Weapon, "bazooka-sambal")
         .Pickup(-24, 3.8f, 8, PickupKind.Weapon, "bambu-runcing").Pickup(-5, 3.8f, -8, PickupKind.Weapon, "kipas-sate")
         .Pickup(10, 1, -18, PickupKind.Weapon, "kerupuk-shuriken").Pickup(-10, 1, 18, PickupKind.Weapon, "kerupuk-shuriken")
         .Pickup(-28, 1, -16, PickupKind.Weapon, "petasan").Pickup(28, 1, 16, PickupKind.Weapon, "panci-shotgun")
         .Pickup(0, 6, -16, PickupKind.Armor).Pickup(0, 6, 16, PickupKind.Armor);
        foreach (var (x, y, z) in (ReadOnlySpan<(float, float, float)>)[(-32, 1, -21), (32, 1, 21), (20, 0, -11), (-20, 0, 11), (-15, 1, 0), (15, 1, 0)])
            b.Pickup(x, y, z, PickupKind.HealthSmall);
        b.Pickup(-37.5f, 6, 1.8f, PickupKind.ArmorSmall).Pickup(38, 0, -12, PickupKind.Ammo).Pickup(-38, 0, 12, PickupKind.Ammo);

        foreach (var (x, z, yaw) in new (float, float, float)[] { (-32, -20, 20), (-18, -22, 0), (-4, -21, 0), (14, -21, -10), (28, -19, -30), (-20, -17.5f, 10) })
        {
            b.Spawn(x, 1, z, yaw, 0);
            b.Spawn(-x, 1, -z, yaw + 180, 1);
        }

        return b.Build("stasiun-kereta", T("Old Train Station", "Stasiun Kereta Tua"),
            T("Carriage roofs, a footbridge and a clock tower that makes a great goal.", "Atap gerbong, jembatan penyeberangan, dan menara jam yang jadi tujuan panjat."),
            a => a with
            {
                SkyTop = "#5B86C5", SkyBottom = "#F7D9A8", FogColor = "#D8C9B4", FogDensity = 0.008f, AmbientColor = "#8C8A86",
                SunColor = "#FFF1D6", SunDirection = [-0.6f, -0.7f, 0.3f], ZoneCenter = [0, 1, 0], ZoneRadius = 3f,
                FlagBases = [[-32, 1, -19], [32, 1, 19]], RaceGoal = [-37.5f, 6, 0],
            });
    }

    // ------------------------------------------------------------------ Kantor Kelurahan (village office)
    private static ArenaDef Kelurahan()
    {
        var b = new ArenaBuilder();
        const string wall = "#E8DCC0", trim = "#2E7D32", paving = "#958B7E";
        b.Box(-34, -1, -28, 34, 0, 26, SurfaceKind.Tile, paving);
        b.Box(-35, 0, -29, 35, 2.2f, -28, SurfaceKind.Wall, "#ECEFF1").Box(-35, 0, 26, 35, 2.2f, 27, SurfaceKind.Wall, "#ECEFF1")
         .Box(-35, 0, -28, -34, 2.2f, 26, SurfaceKind.Wall, "#ECEFF1").Box(34, 0, -28, 35, 2.2f, 26, SurfaceKind.Wall, "#ECEFF1");

        // Building shell: x -16..16, z 6..20, one storey with a flat walkable roof.
        void WallX(float z0, float z1, params (float a, float b)[] spans) { foreach (var (a, c) in spans) b.Box(a, 0, z0, c, 4, z1, SurfaceKind.Wall, wall); }
        void WallZ(float x0, float x1, params (float a, float b)[] spans) { foreach (var (a, c) in spans) b.Box(x0, 0, a, x1, 4, c, SurfaceKind.Wall, wall); }
        void LintelX(float z0, float z1, float a, float c) => b.Box(a, 2.4f, z0, c, 4, z1, SurfaceKind.Wall, wall);
        void LintelZ(float x0, float x1, float a, float c) => b.Box(x0, 2.4f, a, x1, 4, c, SurfaceKind.Wall, wall);
        WallX(6, 6.3f, (-16, -12), (-10, -1.5f), (1.5f, 10), (12, 16));
        LintelX(6, 6.3f, -12, -10); LintelX(6, 6.3f, -1.5f, 1.5f); LintelX(6, 6.3f, 10, 12);
        WallX(19.7f, 20, (-16, -1.5f), (1.5f, 16)); LintelX(19.7f, 20, -1.5f, 1.5f);
        WallZ(-16, -15.7f, (6, 12), (14, 20)); LintelZ(-16, -15.7f, 12, 14);
        WallZ(15.7f, 16, (6, 20));
        WallX(11, 11.2f, (-15.7f, -11), (-9, -1), (1, 9), (11, 15.7f));
        LintelX(11, 11.2f, -11, -9); LintelX(11, 11.2f, -1, 1); LintelX(11, 11.2f, 9, 11);
        WallZ(-6.1f, -5.9f, (11.2f, 14.5f), (16.5f, 19.7f)); LintelZ(-6.1f, -5.9f, 14.5f, 16.5f);
        WallZ(5.9f, 6.1f, (11.2f, 14.5f), (16.5f, 19.7f)); LintelZ(5.9f, 6.1f, 14.5f, 16.5f);
        b.Box(-16.3f, 4, 5.7f, 16.3f, 4.4f, 20.3f, SurfaceKind.Roof, "#9E9E9E");
        b.Box(-16.32f, 3.6f, 5.68f, 16.32f, 4.0f, 5.72f, SurfaceKind.Neon, trim, solid: false);
        b.Block(0, 2.6f, 5.6f, 9, 0.9f, 0.06f, SurfaceKind.Fabric, "#E53935", solid: false);   // spanduk
        // Service counter (loket) with a gap in the middle, desks in the back rooms.
        b.Box(-8, 0, 9.5f, -1.2f, 1.1f, 10.1f, SurfaceKind.Wood, "#8D6E63").Box(1.2f, 0, 9.5f, 8, 1.1f, 10.1f, SurfaceKind.Wood, "#8D6E63");
        foreach (float x in (ReadOnlySpan<float>)[-11f, 0f, 11f]) b.Block(x, 0, 17.5f, 2.4f, 0.8f, 1.2f, SurfaceKind.Wood, "#795548");
        // Exterior stairs to the roof (east side) and a toren on the roof.
        b.Stairs(17, 0, 8, 0, 1, 1.2f, 11, 0.4f, 0.7f, SurfaceKind.Tile, "#BDBDBD");
        b.Block(11, 4.4f, 17, 1.4f, 1.6f, 1.4f, SurfaceKind.Metal, "#FF7043");
        b.PadTo(new(-10, 0, 0), new(-10, 4.4f, 12), 1.0f).PadTo(new(10, 0, 0), new(6, 4.4f, 9), 1.0f);

        // Courtyard: flagpole, security booth, trees, parked motorbikes.
        b.Block(0, 0, -12, 0.2f, 9, 0.2f, SurfaceKind.Metal, "#CFD8DC");
        b.Box(0.1f, 7.6f, -12.05f, 2.4f, 8.2f, -11.95f, SurfaceKind.Fabric, "#E53935", solid: false).Box(0.1f, 7.0f, -12.05f, 2.4f, 7.6f, -11.95f, SurfaceKind.Fabric, "#FFFFFF", solid: false);
        b.Box(-1.5f, 0, -13.5f, 1.5f, 0.3f, -10.5f, SurfaceKind.Tile, "#E0E0E0");
        b.Box(24, 0, -26, 28, 2.6f, -22, SurfaceKind.Wall, "#90CAF9").Box(23.7f, 2.6f, -26.3f, 28.3f, 2.9f, -21.7f, SurfaceKind.Roof, "#455A64");
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-24, 14), (24, 14), (-26, -6), (26, -6)])
        {
            b.Block(x, 0, z, 0.6f, 5, 0.6f, SurfaceKind.Wood, "#6D4C41");
            b.Block(x, 4.4f, z, 4, 2, 4, SurfaceKind.Grass, "#2E7D32", solid: false);
        }
        for (int i = 0; i < 4; i++) { b.Prop("motor", -28 + i * 2.2f, 0, -20, 0.35f, 0.55f, 0.95f, 70f, "#455A64"); b.Prop("motor", 28 - i * 2.2f, 0, -20, 0.35f, 0.55f, 0.95f, 70f, "#455A64"); }
        for (int i = 0; i < 6; i++) b.Prop("kursi", -7 + i * 2.8f, 0, 8, 0.25f, 0.42f, 0.25f, 6f, i % 2 == 0 ? "#E53935" : "#1E88E5");
        b.Prop("galon", 14, 0, 9, 0.28f, 0.5f, 0.28f, 18f, "#4FC3F7").Prop("crate", -14, 0, 18, 0.45f, 0.45f, 0.45f, 22f, "#A1887F");

        b.Light(0, 3.4f, 8.5f, "#FFF59D", 9f).Light(-11, 3.4f, 16, "#FFF59D", 8f).Light(11, 3.4f, 16, "#FFF59D", 8f);
        b.Light(0, 5, -12, "#FFFFFF", 10f).Light(-24, 4, -20, "#FFE0B2", 9f).Light(24, 4, -20, "#FFE0B2", 9f);

        b.Pickup(0, 0, 16, PickupKind.HealthMega).Pickup(0, 4.4f, 13, PickupKind.Quad)
         .Pickup(-11, 0, 14, PickupKind.Weapon, "kipas-sate").Pickup(11, 0, 14, PickupKind.Weapon, "panci-shotgun")
         .Pickup(0, 0, -20, PickupKind.Weapon, "bazooka-sambal").Pickup(-12, 4.4f, 18, PickupKind.Weapon, "bambu-runcing")
         .Pickup(-26, 0, 0, PickupKind.Weapon, "cobek-lempar").Pickup(26, 0, 0, PickupKind.Weapon, "kerupuk-shuriken")
         .Pickup(20, 0, -26, PickupKind.Armor).Pickup(-8, 0, 8.5f, PickupKind.ArmorSmall).Pickup(8, 0, 8.5f, PickupKind.Ammo);
        foreach (var (x, z) in (ReadOnlySpan<(float, float)>)[(-30, -24), (30, -24), (-30, 22), (30, 22), (-12, -6), (12, -6)])
            b.Pickup(x, 0, z, PickupKind.HealthSmall);

        foreach (var (x, z, yaw) in new (float, float, float)[] { (-30, -14, 70), (-30, 4, 100), (-22, -24, 45), (-20, 22, 135), (-31, 16, 120), (-14, -24, 20) })
        {
            b.Spawn(x, 0, z, yaw, 0);
            b.Spawn(-x, 0, z, -yaw, 1);
        }

        return b.Build("kantor-kelurahan", T("Village Office", "Kantor Kelurahan"),
            T("Queue tickets, service counters and a rooftop with the best view in the village.", "Nomor antrean, loket pelayanan, dan atap dengan pemandangan terbaik se-kelurahan."),
            a => a with
            {
                SkyTop = "#3E8EDE", SkyBottom = "#CDE7F7", FogColor = "#CFE3F2", FogDensity = 0.007f, AmbientColor = "#9AA8B6",
                SunColor = "#FFF8E7", SunDirection = [0.3f, -0.9f, 0.25f], ZoneCenter = [0, 0, -12], ZoneRadius = 4f,
                FlagBases = [[-30, 0, 0], [30, 0, 0]], RaceGoal = [0, 4.4f, 13],
            });
    }

}
