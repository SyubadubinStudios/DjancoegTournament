using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Tests;

public class DestructionTests
{
    private static GameWorld World(string arena = "pasar-malam") =>
        new(ContentRegistry.CreateBuiltIn(), new MatchSettings { ArenaId = arena, BotCount = 0, TimeLimitSeconds = 0, ScoreLimit = 999, Creeps = false }, 7);

    [Fact]
    public void Built_in_arenas_have_destructibles_within_the_network_limit()
    {
        var content = ContentRegistry.CreateBuiltIn();
        int withAny = 0;
        foreach (var a in content.Arenas)
        {
            int n = a.Boxes.Count(b => b.Solid && b.Health > 0);
            Assert.True(n <= GameWorld.MaxDestructibles, a.Id);
            if (n > 0) withAny++;
        }
        Assert.True(withAny >= 3);
    }

    [Fact]
    public void Damage_breaks_box_disables_collision_and_it_comes_back()
    {
        var w = World();
        w.AddPlayer("A", 0, false);
        w.Step();
        Assert.NotEmpty(w.Destructibles);
        int box = w.Destructibles[0];
        var b = w.Arena.Boxes[box];
        var center = (b.MinV + b.MaxV) * 0.5f;
        Assert.True(w.Collision.IsBoxEnabled(box));

        w.DamageBox(box, b.Health * 0.5f, Vector3.UnitX);
        Assert.Equal(UInt128.Zero, w.DestroyedMask);
        w.DamageBox(box, b.Health, Vector3.UnitX);
        Assert.Equal(UInt128.One, w.DestroyedMask & UInt128.One);
        Assert.False(w.Collision.IsBoxEnabled(box));
        // A ray through the broken box no longer reports it.
        var origin = center - new Vector3(5, 0, 0);
        if (w.Collision.Raycast(origin, Vector3.UnitX, 5, out _, out _, out int hit)) Assert.NotEqual(box, hit);

        // Restores after the timer (move the player away so nothing blocks it).
        w.Players[0].Move.Position = new Vector3(0, 30, 0);
        bool restored = false;
        for (int i = 0; i < SimConstants.TickRate * 60 && !restored; i++)
        {
            w.Players[0].Move.Position = new Vector3(0, 30, 0);
            w.Players[0].Move.Velocity = default;
            w.Step();
            restored = (w.DestroyedMask & UInt128.One) == UInt128.Zero;
        }
        Assert.True(restored);
        Assert.True(w.Collision.IsBoxEnabled(box));
    }

    [Fact]
    public void Indestructible_boxes_ignore_damage()
    {
        var w = World();
        int wall = -1;
        for (int i = 0; i < w.Arena.Boxes.Count; i++) if (w.Arena.Boxes[i].Solid && w.Arena.Boxes[i].Health <= 0) { wall = i; break; }
        Assert.True(wall >= 0);
        w.DamageBox(wall, 99999, Vector3.UnitY);
        Assert.Equal(UInt128.Zero, w.DestroyedMask);
        Assert.True(w.Collision.IsBoxEnabled(wall));
    }

    [Fact]
    public void Reset_map_restores_everything()
    {
        var w = World();
        foreach (int box in w.Destructibles) w.DamageBox(box, 99999, Vector3.UnitY);
        Assert.NotEqual(UInt128.Zero, w.DestroyedMask);
        w.RestartMatch();
        Assert.Equal(UInt128.Zero, w.DestroyedMask);
        foreach (int box in w.Destructibles) Assert.True(w.Collision.IsBoxEnabled(box));
    }
}
