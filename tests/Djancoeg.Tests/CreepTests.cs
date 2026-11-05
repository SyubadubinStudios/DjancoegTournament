using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Tests;

public class CreepTests
{
    private static GameWorld World(bool creeps = true, string arena = "pasar-malam") =>
        new(ContentRegistry.CreateBuiltIn(), new MatchSettings { ArenaId = arena, BotCount = 0, TimeLimitSeconds = 0, ScoreLimit = 999, Creeps = creeps, CreepIntervalMin = 2, CreepIntervalMax = 3 }, 99);

    public static TheoryData<string> CreepIds()
    {
        var d = new TheoryData<string>();
        foreach (var c in BuiltInCreeps.All) d.Add(c.Id);
        return d;
    }

    [Fact]
    public void Creeps_appear_on_their_own_and_can_be_disabled()
    {
        var on = World();
        on.AddPlayer("A", 0, false);
        for (int i = 0; i < SimConstants.TickRate * 8; i++) on.Step();
        Assert.Contains(on.Creeps, c => c.Active);

        var off = World(creeps: false);
        off.AddPlayer("A", 0, false);
        for (int i = 0; i < SimConstants.TickRate * 8; i++) off.Step();
        Assert.DoesNotContain(off.Creeps, c => c.Active);
    }

    [Theory]
    [MemberData(nameof(CreepIds))]
    public void Every_creep_chases_hurts_and_can_be_killed(string id)
    {
        var w = World(creeps: false);
        int a = w.AddPlayer("A", 0, false);
        for (int i = 0; i < 120; i++) w.Step();
        int kind = w.Content.CreepIndexOf(id);
        int slot = w.SpawnCreep(kind);
        Assert.True(slot >= 0);
        float startHp = w.Players[a].Health;
        bool hurt = false;
        for (int i = 0; i < SimConstants.TickRate * 30 && !hurt; i++)
        {
            w.Players[a].SpawnProtect = 0;
            w.Step();
            hurt = w.Players[a].Health < startHp || !w.Players[a].Alive;
            if (!w.Players[a].Alive) break;
        }
        Assert.True(hurt, $"{id} never reached the player");

        // Now kill it (respawn the creep if it already left).
        if (!w.Creeps[slot].Active || !w.CreepHittable(w.Creeps[slot]))
        {
            Array.Clear(w.Creeps);
            slot = w.SpawnCreep(kind);
            if (w.Content.Creeps[kind].Shape == CreepShape.Worm) { w.Creeps[slot].State = CreepState.Attacking; w.Creeps[slot].StateTimer = 3; }
            else { w.Creeps[slot].State = CreepState.Chasing; }
        }
        bool died = false;
        for (int k = 0; k < 200 && !died; k++) died = w.DamageCreep(slot, a, 50, Vector3.UnitX, 0);
        Assert.True(died);
        Assert.Equal(CreepState.Dying, w.Creeps[slot].State);
    }

    [Fact]
    public void Creep_kills_do_not_cost_the_victim_points()
    {
        var w = World(creeps: false);
        int a = w.AddPlayer("A", 0, false);
        w.Players[a].SpawnProtect = 0;
        w.ApplyDamage(a, -1, 255, 999, Vector3.UnitY, 0, KillFlags.Creep);
        Assert.False(w.Players[a].Alive);
        Assert.Equal(0, w.Players[a].Score);
        Assert.Equal(1, w.Players[a].Deaths);
    }

    [Fact]
    public void Shooting_a_creep_with_the_slingshot_damages_it()
    {
        var w = World(creeps: false);
        int a = w.AddPlayer("A", 0, false);
        ref var p = ref w.Players[a];
        p.Move.Position = new Vector3(-24, 0, -12); p.Move.OnGround = true; p.SpawnProtect = 0; p.FireCooldown = 0;
        p.Weapon = w.Content.WeaponIndexOf("ketapel");
        int slot = w.SpawnCreep(w.Content.CreepIndexOf("gorila-albino"));
        w.Creeps[slot].Move.Position = new Vector3(-24, 0, -4);
        w.Creeps[slot].State = CreepState.Chasing;
        float hp = w.Creeps[slot].Health;
        w.SetInput(a, new PlayerInput { Sequence = 1, Yaw = 0, Pitch = 0.05f, Buttons = InputButtons.Fire });
        w.Step();
        Assert.True(w.Creeps[slot].Health < hp);
    }

    [Fact]
    public void Simulation_with_creeps_still_does_not_allocate()
    {
        var w = new GameWorld(ContentRegistry.CreateBuiltIn(), new MatchSettings { BotCount = 10, ScoreLimit = 10000, TimeLimitSeconds = 0, CreepIntervalMin = 3, CreepIntervalMax = 5 }, 5);
        w.BalanceBots();
        for (int i = 0; i < 900; i++) w.Step();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1200; i++) w.Step();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 16 * 1024, $"allocated {allocated} bytes");
    }
}
