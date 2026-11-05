using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Tests;

public class CampaignTests
{
    [Fact]
    public void Every_stage_references_real_content_and_ids_are_unique()
    {
        var content = ContentRegistry.CreateBuiltIn();
        var ids = new HashSet<string>();
        foreach (var stage in Campaign.AllStages)
        {
            Assert.True(ids.Add(stage.Id), stage.Id);
            Assert.True(content.ArenaIndexOf(stage.ArenaId) >= 0, stage.ArenaId);
            if (stage.IsBoss) Assert.True(content.CreepIndexOf(stage.Boss) >= 0, stage.Boss);
            Assert.False(string.IsNullOrWhiteSpace(stage.Name.En));
            Assert.False(string.IsNullOrWhiteSpace(stage.Name.Id));
        }
        Assert.All(Campaign.Islands, i => Assert.True(i.Stages[^1].IsBoss, i.Id));
        // Islands unlock in increasing order and are reachable with the stars of earlier islands.
        int reachable = 0;
        foreach (var island in Campaign.Islands)
        {
            Assert.True(island.StarsToUnlock <= reachable, island.Id);
            reachable += island.Stages.Count * 3;
        }
    }

    [Theory]
    [InlineData(false, 0, 10f, 0)]
    [InlineData(true, 9, 10f, 1)]
    [InlineData(true, 1, 9999f, 2)]
    [InlineData(true, 0, 10f, 3)]
    public void Stars_follow_the_rules(bool won, int deaths, float seconds, int expected) =>
        Assert.Equal(expected, Campaign.Islands[0].Stages[0].Stars(won, deaths, seconds));

    [Fact]
    public void Stage_unlocking_needs_the_previous_star()
    {
        var jawa = Campaign.Islands[0];
        var stars = new Dictionary<string, int>();
        int Of(string id) => stars.TryGetValue(id, out int s) ? s : 0;
        Assert.True(Campaign.IsStageUnlocked(jawa, 0, Of, 0));
        Assert.False(Campaign.IsStageUnlocked(jawa, 1, Of, 0));
        stars[jawa.Stages[0].Id] = 1;
        Assert.True(Campaign.IsStageUnlocked(jawa, 1, Of, 1));
        Assert.False(Campaign.IsStageUnlocked(Campaign.Islands[1], 0, Of, 1));
    }

    [Fact]
    public void Boss_spawns_tough_and_killing_it_ends_the_match()
    {
        var stage = Campaign.Stage("jawa-boss")!;
        var match = stage.ToMatch() with { BotCount = 0 };
        var w = new GameWorld(ContentRegistry.CreateBuiltIn(), match, 3);
        int a = w.AddPlayer("A", 0, false);
        for (int i = 0; i < SimConstants.TickRate * 8 && w.BossSlot < 0; i++) w.Step();
        Assert.True(w.BossSlot >= 0);
        ref var boss = ref w.Creeps[w.BossSlot];
        var def = w.CreepDefOf(boss);
        Assert.Equal(def.Health * stage.BossHealthScale, boss.MaxHealth, 0.01f);
        // No random creeps on boss stages.
        Assert.Single(w.Creeps, c => c.Active);

        for (int i = 0; i < 400 && w.Creeps[w.BossSlot].State is CreepState.Emerging; i++) w.Step();
        int scoreBefore = w.Players[a].Score;
        Assert.True(w.DamageCreep(w.BossSlot, a, boss.MaxHealth + 1, Vector3.UnitX, 0));
        Assert.True(w.BossDefeated);
        Assert.Equal(a, w.BossKiller);
        Assert.Equal(scoreBefore + Campaign.BossReward + def.ScoreReward, w.Players[a].Score);
        w.Step();
        Assert.Equal(MatchPhase.Ended, w.Phase);
        Assert.Equal(a, w.Winner);
    }

    [Fact]
    public void Boss_stage_tick_does_not_allocate()
    {
        var match = Campaign.Stage("jawa-boss")!.ToMatch() with { BotCount = 6, TimeLimitSeconds = 0, BossHealthScale = 50 };
        var w = new GameWorld(ContentRegistry.CreateBuiltIn(), match, 11);
        w.BalanceBots();
        for (int i = 0; i < 900; i++) w.Step();
        Assert.True(w.BossSlot >= 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1200; i++) w.Step();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 16 * 1024, $"allocated {allocated} bytes over 1200 ticks");
    }
}
