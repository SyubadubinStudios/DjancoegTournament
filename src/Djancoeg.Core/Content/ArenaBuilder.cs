using System.Numerics;
using Djancoeg.Core.Localization;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Core.Content;

/// <summary>Fluent helper that turns level-design intent (stairs, jump pads aimed at a target) into an <see cref="ArenaDef"/>.</summary>
public sealed class ArenaBuilder
{
    private readonly List<ArenaBox> _boxes = [];
    private readonly List<ArenaSpawn> _spawns = [];
    private readonly List<ArenaPickup> _pickups = [];
    private readonly List<ArenaJumpPad> _pads = [];
    private readonly List<ArenaProp> _props = [];
    private readonly List<ArenaLight> _lights = [];

    public ArenaBuilder Box(float x0, float y0, float z0, float x1, float y1, float z1, SurfaceKind s, string color, bool solid = true)
    {
        _boxes.Add(new ArenaBox
        {
            Min = [MathF.Min(x0, x1), MathF.Min(y0, y1), MathF.Min(z0, z1)],
            Max = [MathF.Max(x0, x1), MathF.Max(y0, y1), MathF.Max(z0, z1)],
            Surface = s, Color = color, Solid = solid,
        });
        return this;
    }

    /// <summary>Box from its bottom-center point and full size.</summary>
    public ArenaBuilder Block(float cx, float y, float cz, float sx, float sy, float sz, SurfaceKind s, string color, bool solid = true) =>
        Box(cx - sx / 2, y, cz - sz / 2, cx + sx / 2, y + sy, cz + sz / 2, s, color, solid);

    /// <summary>A flight of stairs whose first (lowest) step starts at (x, y, z) and climbs along (dx, dz).</summary>
    public ArenaBuilder Stairs(float x, float y, float z, int dx, int dz, float width, int steps, float stepHeight, float stepDepth, SurfaceKind s, string color)
    {
        for (int i = 0; i < steps; i++)
        {
            float a = i * stepDepth, b = (i + 1) * stepDepth, top = y + (i + 1) * stepHeight;
            if (dx != 0)
            {
                float xa = x + dx * a, xb = x + dx * b;
                Box(xa, y, z - width / 2, xb, top, z + width / 2, s, i % 2 == 0 ? color : Shade(color, 0.9f));
            }
            else
            {
                float za = z + dz * a, zb = z + dz * b;
                Box(x - width / 2, y, za, x + width / 2, top, zb, s, i % 2 == 0 ? color : Shade(color, 0.9f));
            }
        }
        return this;
    }

    public ArenaBuilder Spawn(float x, float y, float z, float yawDegrees, int team = -1)
    {
        _spawns.Add(new ArenaSpawn { Position = [x, y, z], Yaw = yawDegrees * MathF.PI / 180f, Team = team });
        return this;
    }

    public ArenaBuilder Pickup(float x, float y, float z, PickupKind kind, string? weapon = null, float respawn = 0)
    {
        if (respawn <= 0)
        {
            respawn = kind switch
            {
                PickupKind.HealthMega => 35f,
                PickupKind.Quad => 60f,
                PickupKind.Armor => 25f,
                PickupKind.Weapon => 15f,
                _ => 18f,
            };
        }
        _pickups.Add(new ArenaPickup { Position = [x, y, z], Kind = kind, Weapon = weapon, RespawnSeconds = respawn });
        return this;
    }

    /// <summary>Jump pad at <paramref name="from"/> that throws a player so they land on <paramref name="to"/>.</summary>
    public ArenaBuilder PadTo(Vector3 from, Vector3 to, float apexAbove = 1.5f, float size = 1.6f)
    {
        var launch = SolveLaunch(from, to, apexAbove, PlayerMovement.Gravity);
        _pads.Add(new ArenaJumpPad
        {
            Min = [from.X - size / 2, from.Y, from.Z - size / 2],
            Max = [from.X + size / 2, from.Y + 0.25f, from.Z + size / 2],
            Launch = [launch.X, launch.Y, launch.Z],
        });
        return this;
    }

    public static Vector3 SolveLaunch(Vector3 from, Vector3 to, float apexAbove, float gravity)
    {
        float apex = MathF.Max(from.Y, to.Y) + apexAbove;
        float vy = MathF.Sqrt(2f * gravity * (apex - from.Y));
        float tUp = vy / gravity;
        float tDown = MathF.Sqrt(2f * (apex - to.Y) / gravity);
        float t = tUp + tDown;
        return new Vector3((to.X - from.X) / t, vy, (to.Z - from.Z) / t);
    }

    public ArenaBuilder Prop(string kind, float x, float y, float z, float hx, float hy, float hz, float mass, string color)
    {
        _props.Add(new ArenaProp { Kind = kind, Position = [x, y + hy, z], HalfExtents = [hx, hy, hz], Mass = mass, Color = color });
        return this;
    }

    public ArenaBuilder Light(float x, float y, float z, string color, float radius = 8f)
    {
        _lights.Add(new ArenaLight { Position = [x, y, z], Color = color, Radius = radius });
        return this;
    }

    public ArenaDef Build(string id, LocalizedText name, LocalizedText description, Func<ArenaDef, ArenaDef> configure) =>
        configure(new ArenaDef
        {
            Id = id, Name = name, Description = description,
            Boxes = _boxes, Spawns = _spawns, Pickups = _pickups, JumpPads = _pads, Props = _props, Lights = _lights,
        });

    public static string Shade(string hex, float factor)
    {
        uint c = ColorUtil.ParseRgba(hex);
        int r = (int)MathF.Min(255, ((c >> 24) & 0xFF) * factor);
        int g = (int)MathF.Min(255, ((c >> 16) & 0xFF) * factor);
        int b = (int)MathF.Min(255, ((c >> 8) & 0xFF) * factor);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
