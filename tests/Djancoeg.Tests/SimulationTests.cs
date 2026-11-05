using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Tests;

public class GeometryTests
{
    [Fact]
    public void Raycast_hits_box_front_face()
    {
        var box = new Aabb(new Vector3(-1, -1, 5), new Vector3(1, 1, 6));
        var dir = Vector3.UnitZ;
        Assert.True(box.Raycast(Vector3.Zero, VectorMath.SafeInverse(dir), 100, out float t, out var n));
        Assert.Equal(5f, t, 3);
        Assert.Equal(new Vector3(0, 0, -1), n);
    }

    [Fact]
    public void Raycast_misses_parallel_ray_outside_slab()
    {
        var box = new Aabb(new Vector3(-1, -1, 5), new Vector3(1, 1, 6));
        Assert.False(box.Raycast(new Vector3(0, 2, 0), VectorMath.SafeInverse(Vector3.UnitZ), 100, out _, out _));
    }

    [Fact]
    public void Raycast_from_inside_reports_immediate_hit()
    {
        var box = new Aabb(new Vector3(-1), new Vector3(1));
        Assert.True(box.Raycast(Vector3.Zero, VectorMath.SafeInverse(Vector3.UnitX), 10, out float t, out _));
        Assert.Equal(0f, t);
    }

    [Fact]
    public void Forward_and_right_are_orthogonal_unit_vectors()
    {
        for (float yaw = -3f; yaw < 3f; yaw += 0.37f)
        {
            var f = VectorMath.FlatForward(yaw);
            var r = VectorMath.FlatRight(yaw);
            Assert.Equal(0f, Vector3.Dot(f, r), 4);
            Assert.Equal(1f, f.Length(), 4);
            // Right-handed, Y up: forward x right = down.
            Assert.True(Vector3.Cross(f, r).Y < 0);
        }
    }

    [Fact]
    public void Launch_solver_lands_on_target()
    {
        var from = new Vector3(0, 0, 0);
        var to = new Vector3(8, 4, -3);
        var v = ArenaBuilder.SolveLaunch(from, to, 1.5f, PlayerMovement.Gravity);
        // Integrate until we come back down to the target height.
        var p = from; float dt = 1f / 600f;
        bool rising = true;
        for (int i = 0; i < 20000; i++)
        {
            v.Y -= PlayerMovement.Gravity * dt;
            p += v * dt;
            if (v.Y < 0) rising = false;
            if (!rising && p.Y <= to.Y) break;
        }
        Assert.InRange(Vector3.Distance(VectorMath.Horizontal(p), VectorMath.Horizontal(to)), 0, 0.1f);
    }
}

public class MovementTests
{
    private static CollisionWorld FlatWorld(params ArenaBox[] extra)
    {
        var arena = new ArenaDef { Id = "test", Boxes = [new ArenaBox { Min = [-50, -1, -50], Max = [50, 0, 50] }, .. extra] };
        return new CollisionWorld(arena);
    }

    private static PlayerInput Input(sbyte fwd = 0, InputButtons b = InputButtons.None) => new() { Forward = fwd, Buttons = b };

    [Fact]
    public void Player_falls_and_lands_on_ground()
    {
        var w = FlatWorld();
        var s = new MoveState { Position = new Vector3(0, 3, 0) };
        for (int i = 0; i < 120; i++) PlayerMovement.Step(ref s, Input(), MoveParams.Default, w, SimConstants.TickDelta);
        Assert.True(s.OnGround);
        Assert.InRange(s.Position.Y, -0.01f, 0.01f);
    }

    [Fact]
    public void Walking_reaches_max_speed_and_wall_stops_player()
    {
        var w = FlatWorld(new ArenaBox { Min = [-5, 0, 10], Max = [5, 3, 11] });
        var s = new MoveState { Position = Vector3.Zero, OnGround = true };
        float maxSpeed = 0;
        for (int i = 0; i < 300; i++)
        {
            PlayerMovement.Step(ref s, Input(127), MoveParams.Default, w, SimConstants.TickDelta);
            maxSpeed = MathF.Max(maxSpeed, VectorMath.Horizontal(s.Velocity).Length());
        }
        Assert.InRange(maxSpeed, PlayerMovement.MaxSpeed * 0.95f, PlayerMovement.MaxSpeed * 1.05f);
        Assert.True(s.Position.Z < 10f - SimConstants.PlayerHalfWidth + 0.01f, $"walked through wall: z={s.Position.Z}");
        Assert.True(s.Position.Z > 9f, "should be pressed against the wall");
    }

    [Fact]
    public void Player_climbs_stairs()
    {
        var b = new ArenaBuilder();
        b.Box(-50, -1, -50, 50, 0, 50, SurfaceKind.Ground, "#000000");
        b.Stairs(0, 0, 2, 0, 1, 3, 8, 0.4f, 0.6f, SurfaceKind.Wood, "#000000");
        var arena = b.Build("stairs", "s", "s", a => a);
        var w = new CollisionWorld(arena);
        var s = new MoveState { Position = Vector3.Zero, OnGround = true };
        float peak = 0;
        for (int i = 0; i < 120; i++)
        {
            PlayerMovement.Step(ref s, Input(127), MoveParams.Default, w, SimConstants.TickDelta);
            peak = MathF.Max(peak, s.Position.Y);
        }
        Assert.True(peak > 3.1f, $"expected to reach the top, peak={peak}");
    }

    [Fact]
    public void Jump_height_is_about_one_and_a_quarter_metres()
    {
        var w = FlatWorld();
        var s = new MoveState { Position = Vector3.Zero, OnGround = true };
        float peak = 0;
        PlayerMovement.Step(ref s, Input(0, InputButtons.Jump), MoveParams.Default, w, SimConstants.TickDelta);
        for (int i = 0; i < 90; i++)
        {
            PlayerMovement.Step(ref s, Input(), MoveParams.Default, w, SimConstants.TickDelta);
            peak = MathF.Max(peak, s.Position.Y);
        }
        Assert.InRange(peak, 1.1f, 1.35f);
    }

    [Fact]
    public void Jump_pad_launches_player()
    {
        var arena = new ArenaDef
        {
            Id = "pad",
            Boxes = [new ArenaBox { Min = [-50, -1, -50], Max = [50, 0, 50] }],
            JumpPads = [new ArenaJumpPad { Min = [-1, 0, -1], Max = [1, 0.25f, 1], Launch = [0, 15, 5] }],
        };
        var w = new CollisionWorld(arena);
        var s = new MoveState { Position = Vector3.Zero, OnGround = true };
        var ev = PlayerMovement.Step(ref s, Input(), MoveParams.Default, w, SimConstants.TickDelta);
        Assert.True((ev & MoveEvents.JumpPad) != 0);
        Assert.Equal(15f, s.Velocity.Y, 3);
    }

    [Fact]
    public void Crouched_player_cannot_stand_under_low_ceiling()
    {
        var w = FlatWorld(new ArenaBox { Min = [-2, 1.4f, -2], Max = [2, 2, 2] });
        var s = new MoveState { Position = Vector3.Zero, OnGround = true, Crouched = true };
        PlayerMovement.Step(ref s, Input(), MoveParams.Default, w, SimConstants.TickDelta);
        Assert.True(s.Crouched);
    }
}

public class WorldTests
{
    private static GameWorld NewWorld(GameModeKind mode = GameModeKind.FreeForAll, int scoreLimit = 25, string arena = "pasar-malam") =>
        new(ContentRegistry.CreateBuiltIn(), new MatchSettings { Mode = mode, ScoreLimit = scoreLimit, ArenaId = arena, BotCount = 0, TimeLimitSeconds = 0 });

    private static void Place(GameWorld w, int id, Vector3 pos, float yaw)
    {
        ref var p = ref w.Players[id];
        p.Move.Position = pos; p.Move.Velocity = Vector3.Zero; p.Move.OnGround = true;
        p.Yaw = yaw; p.Input.Yaw = yaw; p.SpawnProtect = 0;
    }

    [Fact]
    public void Hitscan_damages_and_kills_target()
    {
        var w = NewWorld();
        int a = w.AddPlayer("A", 0, false), b = w.AddPlayer("B", 0, false);
        Place(w, a, new Vector3(-20, 0, -12), 0);
        Place(w, b, new Vector3(-20, 0, -4), MathF.PI);
        int ketapel = w.Content.WeaponIndexOf("ketapel");
        w.Players[a].Weapon = ketapel;
        float before = w.Players[b].Health;
        w.ApplyDamage(b, a, ketapel, 30, Vector3.UnitZ, 0, KillFlags.None);
        Assert.Equal(before - 30, w.Players[b].Health, 3);
        w.ApplyDamage(b, a, ketapel, 500, Vector3.UnitZ, 0, KillFlags.None);
        Assert.False(w.Players[b].Alive);
        Assert.Equal(1, w.Players[a].Kills);
        Assert.Equal(1, w.Players[a].Score);
        Assert.Equal(1, w.Players[b].Deaths);
    }

    [Fact]
    public void Firing_ketapel_at_target_hits_it()
    {
        var w = NewWorld();
        int a = w.AddPlayer("A", 0, false), b = w.AddPlayer("B", 0, false);
        Place(w, a, new Vector3(-24, 0, -12), 0);
        Place(w, b, new Vector3(-24, 0, -6), MathF.PI);
        w.Players[a].Weapon = w.Content.WeaponIndexOf("ketapel");
        w.Players[a].FireCooldown = 0;
        w.SetInput(a, new PlayerInput { Yaw = 0, Pitch = -0.08f, Buttons = InputButtons.Fire, Sequence = 1 });
        w.SetInput(b, new PlayerInput { Yaw = MathF.PI, Sequence = 1 });
        w.Players[b].SpawnProtect = 0;
        float hp = w.Players[b].Health;
        for (int i = 0; i < 3; i++) w.Step();
        Assert.True(w.Players[b].Health < hp, "target should take damage");
    }

    [Fact]
    public void Armor_absorbs_two_thirds()
    {
        var w = NewWorld();
        int a = w.AddPlayer("A", 0, false), b = w.AddPlayer("B", 0, false);
        w.Players[b].SpawnProtect = 0;
        w.Players[b].Armor = 100;
        w.Players[b].Health = 100;
        w.ApplyDamage(b, a, 0, 30, Vector3.UnitX, 0, KillFlags.None);
        Assert.Equal(80.2f, w.Players[b].Armor, 1);
        Assert.Equal(89.8f, w.Players[b].Health, 1);
    }

    [Fact]
    public void Team_mode_blocks_friendly_fire()
    {
        var w = NewWorld(GameModeKind.TeamDeathmatch);
        int a = w.AddPlayer("A", 0, false), b = w.AddPlayer("B", 0, false), c = w.AddPlayer("C", 0, false);
        Assert.NotEqual(w.Players[a].Team, w.Players[b].Team);
        Assert.Equal(w.Players[a].Team, w.Players[c].Team);
        w.Players[c].SpawnProtect = 0;
        float hp = w.Players[c].Health;
        w.ApplyDamage(c, a, 0, 50, Vector3.UnitX, 0, KillFlags.None);
        Assert.Equal(hp, w.Players[c].Health);
    }

    [Fact]
    public void Rocket_jump_launches_shooter_upwards_with_reduced_self_damage()
    {
        var w = NewWorld();
        int a = w.AddPlayer("A", 0, false);
        Place(w, a, new Vector3(-20, 0, -12), 0);
        int bazooka = w.Content.WeaponIndexOf("bazooka-sambal");
        var wd = w.Content.Weapons[bazooka];
        w.Explode(new Vector3(-20, 0.05f, -12), wd.SplashRadius, wd.SplashDamage, wd.Knockback, a, bazooka, -1);
        Assert.True(w.Players[a].Move.Velocity.Y > 8f, $"vy={w.Players[a].Move.Velocity.Y}");
        Assert.True(w.Players[a].Health > 50, "self damage is halved");
    }

    [Fact]
    public void Frag_limit_ends_match()
    {
        var w = NewWorld(scoreLimit: 2);
        int a = w.AddPlayer("A", 0, false), b = w.AddPlayer("B", 0, false);
        for (int i = 0; i < 2; i++)
        {
            w.Players[b].SpawnProtect = 0;
            w.ApplyDamage(b, a, 0, 999, Vector3.UnitX, 0, KillFlags.None);
            w.Players[b].Alive = true; w.Players[b].Health = 100;
        }
        w.Step();
        Assert.Equal(MatchPhase.Ended, w.Phase);
        Assert.Equal(a, w.Winner);
    }

    [Fact]
    public void King_of_the_hill_scores_lone_zone_holder()
    {
        var w = NewWorld(GameModeKind.KingOfTheHill);
        int a = w.AddPlayer("A", 0, false);
        var zone = w.Arena.ZoneCenterV;
        for (int i = 0; i < SimConstants.TickRate * 3; i++)
        {
            ref var p = ref w.Players[a];
            p.Move.Position = zone; p.Move.Velocity = Vector3.Zero; p.Move.OnGround = true;
            w.Step();
        }
        Assert.InRange(w.Players[a].Score, 2, 3);
    }

    [Fact]
    public void Falling_into_river_kills_and_credits_last_attacker()
    {
        var w = NewWorld(arena: "atap-kampung");
        int a = w.AddPlayer("A", 0, false), b = w.AddPlayer("B", 0, false);
        w.Players[b].SpawnProtect = 0;
        w.ApplyDamage(b, a, 1, 1, Vector3.UnitX, 0, KillFlags.None);
        Place(w, b, new Vector3(0, -6, 5), 0);
        w.Players[b].Move.OnGround = false;
        w.Step();
        Assert.False(w.Players[b].Alive);
        Assert.Equal(1, w.Players[a].Kills);
    }

    [Fact]
    public void Pickups_heal_and_respawn()
    {
        var w = NewWorld();
        int a = w.AddPlayer("A", 0, false);
        int idx = w.Arena.Pickups.FindIndex(p => p.Kind == PickupKind.HealthSmall);
        var pos = w.Arena.Pickups[idx].PositionV;
        w.Players[a].Health = 50;
        Place(w, a, pos, 0);
        w.Step();
        Assert.Equal(75, w.Players[a].Health, 1);
        Assert.False(w.Pickups[idx].Available);
    }

    [Theory]
    [InlineData(AbilityKind.SuperJump)]
    [InlineData(AbilityKind.Dash)]
    [InlineData(AbilityKind.Shockwave)]
    [InlineData(AbilityKind.Heal)]
    [InlineData(AbilityKind.Haste)]
    public void Every_ability_kind_activates(AbilityKind kind)
    {
        var w = NewWorld();
        int ch = w.Content.Characters.ToList().FindIndex(c => c.Ability.Kind == kind);
        int a = w.AddPlayer("A", ch, false);
        for (int i = 0; i < 200; i++) w.Step(); // let spawn cooldown expire and land
        w.SetInput(a, new PlayerInput { Buttons = InputButtons.Ability, Sequence = 2 });
        uint before = w.Events.LastId;
        w.Step();
        Span<GameEvent> buf = new GameEvent[64];
        int n = w.Events.ReadSince(before, buf);
        Assert.Contains(buf[..n].ToArray(), e => e.Type == GameEventType.Ability && e.A == a);
    }
}

public class ArenaTests
{
    public static TheoryData<string> ArenaIds()
    {
        var d = new TheoryData<string>();
        foreach (var a in BuiltInArenas.All) d.Add(a.Id);
        return d;
    }

    [Theory]
    [MemberData(nameof(ArenaIds))]
    public void Every_spawn_lands_safely(string arenaId)
    {
        var content = ContentRegistry.CreateBuiltIn();
        var arena = content.GetArena(arenaId);
        var world = new CollisionWorld(arena);
        foreach (var sp in arena.Spawns)
        {
            var s = new MoveState { Position = sp.PositionV };
            for (int k = 0; k < 40 && world.Overlaps(Aabb.FromFeet(s.Position, SimConstants.PlayerHalfWidth, SimConstants.PlayerHeight)); k++) s.Position.Y += 0.1f;
            for (int i = 0; i < 120; i++) PlayerMovement.Step(ref s, default, MoveParams.Default, world, SimConstants.TickDelta);
            Assert.True(s.OnGround, $"{arenaId}: spawn {sp.PositionV} never landed");
            Assert.True(s.Position.Y > arena.KillY + 1, $"{arenaId}: spawn {sp.PositionV} fell out of the world");
        }
    }

    [Theory]
    [MemberData(nameof(ArenaIds))]
    public void Weapon_pickups_reference_existing_weapons(string arenaId)
    {
        var content = ContentRegistry.CreateBuiltIn();
        foreach (var p in content.GetArena(arenaId).Pickups.Where(p => p.Kind == PickupKind.Weapon))
            Assert.True(content.WeaponIndexOf(p.Weapon!) >= 0, $"{arenaId}: unknown weapon {p.Weapon}");
    }

    [Theory]
    [MemberData(nameof(ArenaIds))]
    public void Jump_pads_deliver_players_to_a_floor(string arenaId)
    {
        var arena = ContentRegistry.CreateBuiltIn().GetArena(arenaId);
        var world = new CollisionWorld(arena);
        foreach (var pad in arena.JumpPads)
        {
            var c = (pad.MinV + pad.MaxV) * 0.5f;
            var s = new MoveState { Position = new Vector3(c.X, pad.MinV.Y + 0.01f, c.Z), OnGround = true };
            for (int i = 0; i < 300; i++) PlayerMovement.Step(ref s, default, MoveParams.Default, world, SimConstants.TickDelta);
            Assert.True(s.OnGround && s.Position.Y > arena.KillY + 1, $"{arenaId}: pad at {c} did not land safely (pos {s.Position})");
            Assert.True(Vector3.Distance(VectorMath.Horizontal(s.Position), VectorMath.Horizontal(c)) > 1.5f, $"{arenaId}: pad at {c} just bounced in place");
        }
    }

    [Fact]
    public void Content_signature_is_stable()
    {
        Assert.Equal(ContentRegistry.CreateBuiltIn().Signature, ContentRegistry.CreateBuiltIn().Signature);
    }
}

public class BotMatchTests
{
    [Theory]
    [MemberData(nameof(ArenaTests.ArenaIds), MemberType = typeof(ArenaTests))]
    public void Bots_fight_a_full_match_without_errors(string arenaId)
    {
        var w = new GameWorld(ContentRegistry.CreateBuiltIn(), new MatchSettings { ArenaId = arenaId, BotCount = 8, BotSkill = 0.8f, ScoreLimit = 1000, TimeLimitSeconds = 0 }, 42);
        w.BalanceBots();
        Assert.Equal(8, w.ActivePlayerCount);
        int kills = 0;
        var buf = new GameEvent[EventBuffer.Capacity];
        uint last = 0;
        for (int i = 0; i < SimConstants.TickRate * 120; i++)
        {
            w.Step();
            int n = w.Events.ReadSince(last, buf);
            for (int k = 0; k < n; k++) { if (buf[k].Type == GameEventType.Kill) kills++; last = buf[k].Id; }
            foreach (ref readonly var p in w.Players.AsSpan())
                if (p.Active) Assert.True(float.IsFinite(p.Move.Position.X) && float.IsFinite(p.Move.Position.Y), "NaN position");
        }
        Assert.True(kills >= 5, $"{arenaId}: bots only scored {kills} kills in 2 minutes");
    }

    [Fact]
    public void Simulation_tick_does_not_allocate()
    {
        var w = new GameWorld(ContentRegistry.CreateBuiltIn(), new MatchSettings { BotCount = 12, ScoreLimit = 10000, TimeLimitSeconds = 0 }, 7);
        w.BalanceBots();
        for (int i = 0; i < 600; i++) w.Step(); // warm up JIT, tiering
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1200; i++) w.Step();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 16 * 1024, $"allocated {allocated} bytes over 1200 ticks");
    }
}

public class SurfaceTests
{
    [Fact]
    public void Surface_below_reports_the_box_underfoot()
    {
        var world = new CollisionWorld(ContentRegistry.CreateBuiltIn().GetArena("sawah-ronda"));
        Assert.True(world.SurfaceBelow(new Vector3(30, 0, 5), 1f, out var grass));
        Assert.Equal(SurfaceKind.Grass, grass);
        Assert.True(world.SurfaceBelow(new Vector3(0, 2.0f, 0), 1f, out var wood)); // pos ronda floor
        Assert.Equal(SurfaceKind.Wood, wood);
        Assert.False(world.SurfaceBelow(new Vector3(0, 30, 0), 1f, out _));
    }
}
