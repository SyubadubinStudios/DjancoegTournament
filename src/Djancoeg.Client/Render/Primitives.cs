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
            case "cobek":
                DrawCylinderEx(new(0, -0.02f, 0.25f), new(0, 0.08f, 0.25f), 0.12f, 0.17f, 10, tint);
                DrawCylinderEx(new(0, 0.06f, 0.25f), new(0, 0.085f, 0.25f), 0.13f, 0.13f, 10, new Color(70, 66, 62, 255));
                Rod(new(0.04f, 0.05f, 0.22f), new(0.1f, 0.22f, 0.12f), 0.025f, new Color(120, 110, 100, 255), 6); // ulekan
                Rod(new(0, -0.1f, 0.0f), new(0, -0.02f, 0.2f), 0.025f, Wood);
                break;
            case "fan":
                Rod(new(0, -0.1f, 0), new(0, 0.02f, 0.1f), 0.018f, new Color(120, 80, 40, 255), 5);
                DrawCubeV(new(0, 0.12f, 0.22f), new(0.3f, 0.26f, 0.02f), tint);
                DrawCubeWiresV(new(0, 0.12f, 0.22f), new(0.31f, 0.27f, 0.025f), new Color(120, 80, 40, 255));
                for (int i = -2; i <= 2; i++) DrawCubeV(new(i * 0.055f, 0.12f, 0.232f), new(0.008f, 0.25f, 0.005f), new Color(150, 110, 60, 255));
                break;
            case "kerupuk":
                DrawCubeV(new(0, -0.04f, 0.05f), new(0.1f, 0.12f, 0.22f), new Color(196, 160, 110, 255)); // kaleng kerupuk
                for (int i = 0; i < 3; i++)
                {
                    float a = spin + i * 2.1f;
                    DrawCylinderEx(new(MathF.Cos(a) * 0.03f, 0.04f + i * 0.02f, 0.05f), new(MathF.Cos(a) * 0.03f, 0.05f + i * 0.02f, 0.05f), 0.09f, 0.09f, 8, tint);
                }
                break;
            default:
                DrawCubeV(new(0, 0, 0.15f), new(0.08f, 0.1f, 0.4f), tint);
                break;
        }
    }

    /// <summary>The gorengan "flag": a giant fried banana on a bamboo skewer with a team banner.</summary>
    public static void Gorengan(Vector3 pos, float t, Color team, bool carried)
    {
        float bob = carried ? 0 : MathF.Sin(t * 2.2f) * 0.1f;
        Push(pos + new Vector3(0, bob, 0), carried ? 0 : t * 1.2f);
        Rod(new(0, 0, 0), new(0, 2.2f, 0), 0.035f, Bamboo, 6);
        DrawCapsule(new(0, 1.75f, -0.25f), new(0, 2.05f, 0.25f), 0.17f, 6, 6, new Color(214, 150, 40, 255));
        DrawCubeV(new(0.32f, 1.35f, 0), new(0.6f, 0.38f, 0.02f), team);
        DrawSphereEx(new(0, 1.9f, 0), 0.55f + MathF.Sin(t * 5) * 0.05f, 5, 6, new Color(team.R, team.G, team.B, (byte)45));
        Pop();
    }

    /// <summary>The team base where gorengan are brought home: a big plate on a tray stand.</summary>
    public static void FlagBase(Vector3 pos, Color team)
    {
        DrawCylinderEx(pos, pos + new Vector3(0, 0.12f, 0), 1.3f, 1.25f, 20, new Color(240, 240, 235, 255));
        DrawCylinderEx(pos + new Vector3(0, 0.12f, 0), pos + new Vector3(0, 0.14f, 0), 1.0f, 1.0f, 20, team);
    }

    /// <summary>Panjat Pinang goal: a ring of prizes and a column of light.</summary>
    public static void RaceGoal(Vector3 pos, float radius, float t)
    {
        Color[] prizes = [new(227, 36, 43, 255), new(255, 194, 26, 255), new(31, 166, 74, 255), new(29, 63, 187, 255)];
        for (int i = 0; i < 8; i++)
        {
            float a = t * 0.8f + i * MathF.Tau / 8;
            var p = pos + new Vector3(MathF.Cos(a) * radius * 0.8f, 1.2f + MathF.Sin(t * 3 + i) * 0.1f, MathF.Sin(a) * radius * 0.8f);
            DrawCubeV(p, new Vector3(0.25f), prizes[i % prizes.Length]);
        }
        DrawCylinderEx(pos, pos + new Vector3(0, 30, 0), radius * 0.35f, radius * 0.1f, 12, new Color((byte)255, (byte)230, (byte)120, (byte)(40 + 20 * MathF.Sin(t * 4))));
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
            case "cobek":
                Push(pos, yaw, 0, age * 6f);
                DrawCylinderEx(new(0, -0.08f, 0), new(0, 0.08f, 0), 0.14f, 0.2f, 10, tint);
                Pop();
                break;
            case "kerupuk":
                Push(pos, yaw, 0, 0);
                Rlgl.Rotatef(age * 900f, 0, 1, 0);
                DrawCylinderEx(new(0, -0.01f, 0), new(0, 0.01f, 0), 0.13f, 0.13f, 7, tint);
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
            case "kursi": // plastic chair
                DrawCubeV(new(0, 0, 0), new(half.X * 2, 0.06f, half.Z * 2), color);
                DrawCubeV(new(0, half.Y * 0.5f, half.Z * 0.9f), new(half.X * 2, half.Y, 0.05f), color);
                for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2)
                    Rod(new(i * half.X * 0.85f, -half.Y, j * half.Z * 0.85f), new(i * half.X * 0.85f, 0, j * half.Z * 0.85f), 0.025f, color, 5);
                break;
            case "jerigen":
                DrawCubeV(Vector3.Zero, half * 2, color);
                DrawCubeV(new(0, half.Y + 0.04f, -half.Z * 0.3f), new(half.X, 0.08f, 0.05f), color);
                break;
            case "motor":
                DrawCubeV(new(0, -half.Y * 0.1f, 0), new(half.X * 1.2f, half.Y, half.Z * 1.6f), color);
                DrawCylinderEx(new(-half.X * 0.3f, -half.Y * 0.55f, half.Z * 0.75f), new(half.X * 0.3f, -half.Y * 0.55f, half.Z * 0.75f), half.Y * 0.45f, half.Y * 0.45f, 12, new Color(30, 30, 30, 255));
                DrawCylinderEx(new(-half.X * 0.3f, -half.Y * 0.55f, -half.Z * 0.75f), new(half.X * 0.3f, -half.Y * 0.55f, -half.Z * 0.75f), half.Y * 0.45f, half.Y * 0.45f, 12, new Color(30, 30, 30, 255));
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
