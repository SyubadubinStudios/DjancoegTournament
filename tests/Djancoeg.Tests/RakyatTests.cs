using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Tests;

/// <summary>v1.2 "Pasar Rakyat": new modes, weapons and abilities.</summary>
public class RakyatTests
{
    private static GameWorld NewWorld(GameModeKind mode, string arena = "kantor-kelurahan", int score = 3) =>
        new(ContentRegistry.CreateBuiltIn(), new MatchSettings { Mode = mode, ArenaId = arena, BotCount = 0, ScoreLimit = score, TimeLimitSeconds = 0 });

    private static void Place(GameWorld w, int id, Vector3 pos, float yaw = 0)
    {
        ref var p = ref w.Players[id];
        p.Move.Position = pos; p.Move.Velocity = Vector3.Zero; p.Move.OnGround = true;
        p.Yaw = yaw; p.Input.Yaw = yaw; p.SpawnProtect = 0;
    }

    private static List<GameEvent> EventsAfter(GameWorld w, uint after)
    {
        var buf = new GameEvent[EventBuffer.Capacity];
        int n = w.Events.ReadSince(after, buf);
        return [.. buf.AsSpan(0, n).ToArray()];
    }

    [Fact]
    public void Gorengan_can_be_taken_and_captured()
    {
        var w = NewWorld(GameModeKind.CaptureTheFlag);
        int a = w.AddPlayer("A", 0, false), b = w.AddPlayer("B", 0, false);
        Assert.NotEqual(w.Players[a].Team, w.Players[b].Team);
        int team = w.Players[a].Team, enemy = 1 - team;

        Place(w, a, w.Flags[enemy].Home);
        w.Step();
        Assert.Equal(a, w.Flags[enemy].Carrier);

        Place(w, a, w.Flags[team].Home);
        uint before = w.Events.LastId;
        w.Step();
        Assert.Equal(1, w.TeamScores[team]);
        Assert.True(w.Flags[enemy].AtHome);
        Assert.Contains(EventsAfter(w, before), e => e.Type == GameEventType.FlagCaptured);
    }

    [Fact]
    public void Dropped_gorengan_returns_when_a_defender_touches_it()
    {
        var w = NewWorld(GameModeKind.CaptureTheFlag);
        int a = w.AddPlayer("A", 0, false), b = w.AddPlayer("B", 0, false);
        int enemy = 1 - w.Players[a].Team;
        Place(w, a, w.Flags[enemy].Home);
        w.Step();
        Place(w, a, new Vector3(0, 0, -20));
        w.Step();
        w.ApplyDamage(a, b, 0, 999, Vector3.UnitX, 0, KillFlags.None);
        w.Step();
        Assert.Equal(-1, w.Flags[enemy].Carrier);
        Assert.False(w.Flags[enemy].AtHome);
        Place(w, b, w.Flags[enemy].Position);
        w.Step();
        Assert.True(w.Flags[enemy].AtHome);
    }

    [Fact]
    public void Panjat_pinang_scores_and_respawns_on_reaching_the_goal()
    {
        var w = NewWorld(GameModeKind.Race, score: 2);
        int a = w.AddPlayer("A", 0, false);
        Place(w, a, w.RaceGoal);
        w.Step();
        Assert.Equal(1, w.Players[a].Score);
        Assert.True(Vector3.Distance(w.Players[a].Move.Position, w.RaceGoal) > 3f, "should be sent back to a spawn");
        Place(w, a, w.RaceGoal);
        w.Step();
        w.Step();
        Assert.Equal(MatchPhase.Ended, w.Phase);
        Assert.Equal(a, w.Winner);
    }

    [Fact]
    public void Satay_fan_shoves_players_and_turns_projectiles_around()
    {
        var w = NewWorld(GameModeKind.FreeForAll);
        int a = w.AddPlayer("A", 0, false), b = w.AddPlayer("B", 0, false);
        Place(w, a, new Vector3(0, 0, -20), 0);
        Place(w, b, new Vector3(0, 0, -16), MathF.PI);
        int fan = w.Content.WeaponIndexOf("kipas-sate");
        ref var pa = ref w.Players[a];
        pa.OwnedWeapons |= (ushort)(1 << fan); pa.Ammo[fan] = 10; pa.Weapon = fan; pa.FireCooldown = 0;
        // A rocket from B heading at A.
        int rocket = w.Content.WeaponIndexOf("bazooka-sambal");
        w.Projectiles[0] = new Projectile { Active = true, Owner = (byte)b, Weapon = (byte)rocket, Position = new Vector3(0, 1.6f, -18), Velocity = new Vector3(0, 0, -20), Life = 5, Age = 1 };
        w.SetInput(a, new PlayerInput { Sequence = 1, Yaw = 0, Buttons = InputButtons.Fire });
        w.SetInput(b, new PlayerInput { Sequence = 1, Yaw = MathF.PI });
        w.Step();
        Assert.True(w.Players[b].Move.Velocity.Z > 5f, $"b should be pushed away, vz={w.Players[b].Move.Velocity.Z}");
        Assert.True(!w.Projectiles[0].Active || (w.Projectiles[0].Owner == a && w.Projectiles[0].Velocity.Z > 0), "rocket should be deflected back");
    }

    [Fact]
    public void Shared_slot_cycles_between_weapons()
    {
        var w = NewWorld(GameModeKind.FreeForAll);
        int a = w.AddPlayer("A", 0, false);
        int gatling = w.Content.WeaponIndexOf("sandal-gatling"), kerupuk = w.Content.WeaponIndexOf("kerupuk-shuriken");
        ref var p = ref w.Players[a];
        p.OwnedWeapons |= (ushort)((1 << gatling) | (1 << kerupuk));
        p.Ammo[gatling] = 10; p.Ammo[kerupuk] = 10;
        w.SetInput(a, new PlayerInput { Sequence = 1, WeaponSlot = 4 });
        w.Step();
        int first = w.Players[a].Weapon;
        w.SetInput(a, new PlayerInput { Sequence = 2, WeaponSlot = 4 });
        w.Step();
        Assert.Contains(first, new[] { gatling, kerupuk });
        Assert.NotEqual(first, w.Players[a].Weapon);
    }

    private static int SetupAbility(GameWorld w, string characterId)
    {
        int id = w.AddPlayer("A", w.Content.CharacterIndexOf(characterId), false);
        for (int i = 0; i < 200; i++) w.Step();
        return id;
    }

    [Fact]
    public void Kancil_teleports_forward()
    {
        var w = NewWorld(GameModeKind.FreeForAll);
        int a = SetupAbility(w, "si-kancil");
        Place(w, a, new Vector3(0, 0, -20), 0);
        var before = w.Players[a].Move.Position;
        w.SetInput(a, new PlayerInput { Sequence = 5, Yaw = 0, Buttons = InputButtons.Ability });
        w.Step();
        Assert.True(w.Players[a].Move.Position.Z - before.Z > 4f);
    }

    [Fact]
    public void Leak_levitates_slowly()
    {
        var w = NewWorld(GameModeKind.FreeForAll);
        int a = SetupAbility(w, "leak");
        Place(w, a, new Vector3(0, 0, -20), 0);
        w.SetInput(a, new PlayerInput { Sequence = 5, Buttons = InputButtons.Ability });
        w.Step();
        Assert.True(w.IsLevitating(w.Players[a]));
        var mp = w.ParamsFor(w.Players[a]);
        Assert.True(mp.Levitate);
        Assert.True(mp.GravityScale < 0.5f);
    }

    [Fact]
    public void Parking_attendant_pulls_enemies_closer()
    {
        var w = NewWorld(GameModeKind.FreeForAll);
        int a = SetupAbility(w, "tukang-parkir");
        int b = w.AddPlayer("B", 0, false);
        Place(w, a, new Vector3(0, 0, -20));
        Place(w, b, new Vector3(0, 0, -14));
        w.SetInput(a, new PlayerInput { Sequence = 5, Buttons = InputButtons.Ability });
        w.SetInput(b, new PlayerInput { Sequence = 5 });
        w.Step();
        Assert.True(w.Players[b].Move.Velocity.Z < -2f, $"vz={w.Players[b].Move.Velocity.Z}");
    }

    [Fact]
    public void Ronda_rally_heals_teammates()
    {
        var w = NewWorld(GameModeKind.TeamDeathmatch);
        int a = SetupAbility(w, "bapak-ronda");
        int b = w.AddPlayer("B", 0, false), c = w.AddPlayer("C", 0, false);
        int mate = w.Players[b].Team == w.Players[a].Team ? b : c;
        Place(w, a, new Vector3(-20, 0, -20));
        Place(w, mate, new Vector3(-18, 0, -20));
        w.Players[mate].Health = 40;
        w.SetInput(a, new PlayerInput { Sequence = 5, Buttons = InputButtons.Ability });
        w.Step();
        Assert.True(w.Players[mate].Health >= 69);
        Assert.True(w.IsHasted(w.Players[mate]));
    }
}
