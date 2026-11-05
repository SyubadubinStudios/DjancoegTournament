using System.Numerics;
using Djancoeg.Core.Content;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Render;

/// <summary>
/// Procedural low-poly props drawn with raylib primitives inside the world shader:
/// weapons (view and world models), pickups, projectiles and fallback props.
/// Local space: +Z forward, +Y up.
/// </summary>
public static class Primitives
{
    private static readonly Color Dark = new(34, 30, 28, 255);
    private static readonly Color Wood = new(150, 104, 62, 255);
    private static readonly Color Bamboo = new(214, 186, 98, 255);
    private static readonly Color Steel = new(196, 200, 204, 255);

    public static void Push(Vector3 pos, float yawRad, float pitchRad = 0, float rollRad = 0, float scale = 1)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(pos.X, pos.Y, pos.Z);
        Rlgl.Rotatef(yawRad * 180f / MathF.PI, 0, 1, 0);
        Rlgl.Rotatef(-pitchRad * 180f / MathF.PI, 1, 0, 0);
        if (rollRad != 0) Rlgl.Rotatef(rollRad * 180f / MathF.PI, 0, 0, 1);
        if (scale != 1) Rlgl.Scalef(scale, scale, scale);
    }

    public static void Pop() => Rlgl.PopMatrix();

    private static void Rod(Vector3 a, Vector3 b, float r, Color c, int sides = 8) => DrawCylinderEx(a, b, r, r, sides, c);

    /// <summary>Draws a weapon in local space (call between Push/Pop). <paramref name="tint"/> is the weapon colour.</summary>
    public static void Weapon(string model, Color tint, float spin = 0)
    {
        switch (model)
        {
            case "racket":
                Rod(new(0, 0, -0.05f), new(0, 0, 0.22f), 0.025f, Dark);
                DrawCubeV(new(0, 0, 0.42f), new(0.26f, 0.03f, 0.36f), Theme(tint, 0.55f));
                DrawCubeWiresV(new(0, 0, 0.42f), new(0.28f, 0.04f, 0.38f), Theme(tint, 1.1f));
                DrawCubeV(new(0, 0, 0.24f), new(0.06f, 0.05f, 0.04f), Color.Yellow);
                break;
            case "slingshot":
                Rod(new(0, -0.08f, 0), new(0, 0.05f, 0.04f), 0.025f, Wood);
                Rod(new(0, 0.05f, 0.04f), new(-0.06f, 0.16f, 0.08f), 0.02f, Wood);
                Rod(new(0, 0.05f, 0.04f), new(0.06f, 0.16f, 0.08f), 0.02f, Wood);
                Rod(new(-0.06f, 0.16f, 0.08f), new(0, 0.12f, -0.06f), 0.008f, tint, 4);
                Rod(new(0.06f, 0.16f, 0.08f), new(0, 0.12f, -0.06f), 0.008f, tint, 4);
                break;
            case "pan":
                DrawCylinderEx(new(0, 0, 0.30f), new(0, 0, 0.36f), 0.13f, 0.2f, 12, Steel);
                DrawCylinderEx(new(0, 0, 0.36f), new(0, 0, 0.37f), 0.2f, 0.2f, 12, new Color(60, 60, 64, 255));
                Rod(new(0, -0.02f, -0.15f), new(0, 0, 0.3f), 0.03f, Dark);
                Rod(new(0.05f, 0, 0), new(0.05f, 0, 0.32f), 0.02f, Steel);
                Rod(new(-0.05f, 0, 0), new(-0.05f, 0, 0.32f), 0.02f, Steel);
                break;
            case "sandal":
                for (int i = 0; i < 3; i++)
                {
                    float a = spin + i * MathF.Tau / 3f;
                    var o = new Vector3(MathF.Cos(a) * 0.05f, MathF.Sin(a) * 0.05f, 0);
                    Rod(o + new Vector3(0, 0, -0.05f), o + new Vector3(0, 0, 0.45f), 0.022f, Steel, 6);
                }
                DrawCubeV(new(0, 0, 0.0f), new(0.16f, 0.16f, 0.2f), Dark);
                DrawCubeV(new(0, 0.12f, 0.05f), new(0.12f, 0.03f, 0.26f), tint);
                break;
            case "firecracker":
                Rod(new(0, 0, -0.1f), new(0, 0, 0.42f), 0.06f, tint, 10);
                Rod(new(0, 0, 0.42f), new(0, 0, 0.47f), 0.065f, Color.Yellow, 10);
                Rod(new(0, 0.06f, 0.0f), new(0, 0.12f, 0.06f), 0.008f, Color.Yellow, 4);
                DrawCubeV(new(0, -0.09f, 0.05f), new(0.04f, 0.12f, 0.05f), Dark);
                break;
            case "bazooka":
                DrawCylinderEx(new(0, 0, -0.35f), new(0, 0, 0.55f), 0.085f, 0.075f, 10, tint);
                DrawCylinderEx(new(0, 0, 0.55f), new(0, 0, 0.62f), 0.085f, 0.04f, 10, new Color(200, 40, 30, 255));
                Rod(new(0, 0, -0.35f), new(0, 0.03f, -0.45f), 0.03f, new Color(46, 125, 50, 255)); // chili stem
                DrawCubeV(new(0, 0.11f, 0.05f), new(0.04f, 0.05f, 0.2f), Dark);
                DrawCubeV(new(0, -0.12f, 0.0f), new(0.04f, 0.12f, 0.05f), Dark);
                break;
            case "bamboo":
                Rod(new(0, 0, -0.4f), new(0, 0, 0.75f), 0.035f, Bamboo);
                for (int i = 0; i < 4; i++) Rod(new(0, 0, -0.3f + i * 0.3f), new(0, 0, -0.28f + i * 0.3f), 0.04f, new Color(150, 120, 50, 255));
                DrawCylinderEx(new(0, 0, 0.75f), new(0, 0, 0.92f), 0.035f, 0.0f, 8, Bamboo);
                DrawCubeV(new(0, 0.05f, 0.0f), new(0.03f, 0.03f, 0.12f), new Color(198, 40, 40, 255));
                break;
            default:
                DrawCubeV(new(0, 0, 0.15f), new(0.08f, 0.1f, 0.4f), tint);
                break;
        }
    }

    private static Color Theme(Color c, float k) => new((byte)Math.Min(255, c.R * k), (byte)Math.Min(255, c.G * k), (byte)Math.Min(255, c.B * k), c.A);

    public static void Pickup(PickupKind kind, Vector3 pos, float t, string? weaponModel, Color weaponTint)
    {
        float bob = MathF.Sin(t * 2.5f) * 0.08f + 0.55f;
        Push(pos + new Vector3(0, bob, 0), t * 1.6f);
        switch (kind)
        {
            case PickupKind.HealthSmall: // es teh manis
                DrawCylinderEx(new(0, -0.18f, 0), new(0, 0.18f, 0), 0.11f, 0.13f, 10, new Color(166, 92, 34, 230));
                DrawCubeV(new(0.02f, 0.12f, 0.02f), new(0.07f, 0.07f, 0.07f), new Color(220, 240, 255, 255));
                Rod(new(0.04f, 0.0f, 0), new(0.08f, 0.34f, 0.0f), 0.012f, new Color(230, 40, 40, 255), 4);
                break;
            case PickupKind.HealthMega: // nasi padang
                DrawCubeV(new(0, -0.08f, 0), new(0.5f, 0.06f, 0.5f), new Color(46, 125, 50, 255));
                DrawSphereEx(new(-0.06f, 0.02f, 0), 0.15f, 6, 8, new Color(250, 250, 245, 255));
                DrawCubeV(new(0.13f, 0.0f, 0.08f), new(0.12f, 0.1f, 0.12f), new Color(110, 50, 20, 255));
                DrawSphereEx(new(0.12f, 0.0f, -0.12f), 0.06f, 4, 6, new Color(240, 200, 40, 255));
                break;
            case PickupKind.Armor: // helm SNI
                DrawSphereEx(Vector3.Zero, 0.22f, 8, 10, new Color(255, 194, 26, 255));
                DrawCylinderEx(new(0, -0.08f, 0), new(0, -0.05f, 0), 0.27f, 0.27f, 12, new Color(255, 194, 26, 255));
                DrawCubeV(new(0, 0.0f, 0.2f), new(0.22f, 0.08f, 0.04f), new Color(60, 70, 80, 255));
                break;
            case PickupKind.ArmorSmall: // rompi
                DrawCubeV(Vector3.Zero, new(0.32f, 0.36f, 0.12f), new Color(255, 112, 30, 255));
                DrawCubeV(new(0, 0.05f, 0.065f), new(0.33f, 0.04f, 0.01f), new Color(220, 220, 220, 255));
                DrawCubeV(new(0, -0.08f, 0.065f), new(0.33f, 0.04f, 0.01f), new Color(220, 220, 220, 255));
                break;
            case PickupKind.Ammo: // kardus amunisi
                DrawCubeV(Vector3.Zero, new(0.4f, 0.26f, 0.3f), new Color(196, 160, 110, 255));
                DrawCubeV(new(0, 0.05f, 0.151f), new(0.2f, 0.08f, 0.01f), new Color(227, 36, 43, 255));
                break;
            case PickupKind.Quad: // jamu kuat
                DrawCylinderEx(new(0, -0.22f, 0), new(0, 0.12f, 0), 0.12f, 0.12f, 10, new Color(110, 60, 20, 255));
                DrawCylinderEx(new(0, 0.12f, 0), new(0, 0.26f, 0), 0.12f, 0.04f, 10, new Color(110, 60, 20, 255));
                DrawCylinderEx(new(0, -0.08f, 0), new(0, 0.04f, 0), 0.125f, 0.125f, 10, new Color(255, 194, 26, 255));
                DrawSphereEx(Vector3.Zero, 0.35f + MathF.Sin(t * 6) * 0.03f, 6, 8, new Color(255, 80, 40, 40));
                break;
            case PickupKind.Weapon:
                Weapon(weaponModel ?? "", weaponTint, t * 5);
                break;
        }
        Pop();
    }

    public static void Projectile(string model, Vector3 pos, Vector3 vel, float age, Color tint)
    {
        float yaw = MathF.Atan2(vel.X, vel.Z);
        float pitch = MathF.Atan2(vel.Y, MathF.Sqrt(vel.X * vel.X + vel.Z * vel.Z));
        switch (model)
        {
            case "sandal":
                Push(pos, yaw, 0, age * 20f);
                DrawCubeV(Vector3.Zero, new(0.11f, 0.03f, 0.26f), tint);
                DrawCubeV(new(0, 0.025f, 0.03f), new(0.08f, 0.02f, 0.02f), Color.White);
                Pop();
                break;
            case "firecracker":
                Push(pos, yaw, pitch, age * 9f);
                Rod(new(0, 0, -0.12f), new(0, 0, 0.12f), 0.05f, tint, 8);
                Pop();
                DrawSphereEx(pos + new Vector3(0, 0.1f, 0), 0.04f + 0.03f * MathF.Abs(MathF.Sin(age * 40)), 4, 4, Color.Yellow);
                break;
            case "bazooka":
                Push(pos, yaw, pitch);
                DrawCylinderEx(new(0, 0, -0.18f), new(0, 0, 0.22f), 0.08f, 0.01f, 8, tint);
                Rod(new(0, 0, -0.18f), new(0, 0.02f, -0.27f), 0.02f, new Color(46, 125, 50, 255), 5);
                Pop();
                break;
            default:
                DrawSphereEx(pos, 0.12f, 6, 8, tint);
                break;
        }
    }

    /// <summary>Fallback prop shapes for kinds without a model file.</summary>
    public static void Prop(string kind, Vector3 half, Color color)
    {
        switch (kind)
        {
            case "ban": // tyre
                DrawCylinderEx(new(-half.X * 0.45f, 0, 0), new(half.X * 0.45f, 0, 0), half.Y, half.Y, 14, color);
                DrawCylinderEx(new(-half.X * 0.47f, 0, 0), new(half.X * 0.47f, 0, 0), half.Y * 0.45f, half.Y * 0.45f, 10, new Color(120, 120, 120, 255));
                break;
            case "karung": // rice sack
                DrawCubeV(Vector3.Zero, half * 2, color);
                DrawSphereEx(new(0, half.Y * 0.8f, 0), half.Z * 0.9f, 5, 6, color);
                DrawCubeV(new(0, 0, half.Z + 0.002f), new(half.X, half.Y * 0.6f, 0.01f), new Color(46, 90, 160, 255));
                break;
            case "galon":
                DrawCylinderEx(new(0, -half.Y, 0), new(0, half.Y * 0.75f, 0), half.X, half.X, 12, color);
                DrawCylinderEx(new(0, half.Y * 0.75f, 0), new(0, half.Y, 0), half.X, half.X * 0.4f, 12, color);
                break;
            default:
                DrawCubeV(Vector3.Zero, half * 2, color);
                DrawCubeWiresV(Vector3.Zero, half * 2.01f, new Color(60, 40, 30, 255));
                break;
        }
    }

    public static void JumpPad(Vector3 min, Vector3 max, float t)
    {
        var c = (min + max) * 0.5f;
        float r = MathF.Min(max.X - min.X, max.Z - min.Z) * 0.5f;
        DrawCylinderEx(new(c.X, min.Y, c.Z), new(c.X, min.Y + 0.12f, c.Z), r, r * 0.95f, 16, new Color(227, 36, 43, 255));
        DrawCylinderEx(new(c.X, min.Y + 0.12f, c.Z), new(c.X, min.Y + 0.16f, c.Z), r * 0.85f, r * 0.85f, 16, new Color(255, 194, 26, 255));
        for (int i = 0; i < 3; i++)
        {
            float h = ((t * 1.4f + i / 3f) % 1f) * 1.6f;
            byte a = (byte)(200 * (1 - h / 1.6f));
            DrawCylinderWiresEx(new(c.X, min.Y + 0.2f + h, c.Z), new(c.X, min.Y + 0.22f + h, c.Z), r * (0.8f - h * 0.2f), r * (0.8f - h * 0.2f), 16, new Color((byte)255, (byte)230, (byte)120, a));
        }
    }

    public static void Zone(Vector3 center, float radius, float t, Color color)
    {
        int segs = 40;
        for (int i = 0; i < segs; i++)
        {
            if (i % 2 == 1) continue;
            float a0 = i * MathF.Tau / segs + t * 0.4f, a1 = (i + 1) * MathF.Tau / segs + t * 0.4f;
            var p0 = center + new Vector3(MathF.Cos(a0) * radius, 0.06f, MathF.Sin(a0) * radius);
            var p1 = center + new Vector3(MathF.Cos(a1) * radius, 0.06f, MathF.Sin(a1) * radius);
            DrawCubeV((p0 + p1) * 0.5f, new Vector3(0.25f, 0.08f, 0.25f), color);
        }
        float pulse = (t * 0.6f) % 1f;
        DrawCylinderWiresEx(center + new Vector3(0, 0.05f, 0), center + new Vector3(0, 0.05f + pulse * 3f, 0), radius, radius, 32, new Color(color.R, color.G, color.B, (byte)(160 * (1 - pulse))));
    }
}
