using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;
using Djancoeg.Modding;

namespace Djancoeg.Tests;

public class V21Tests
{
    [Fact]
    public void Rotate_swaps_box_extents_around_its_centre_and_turns_pads()
    {
        var ed = new ArenaEditor(ArenaEditor.NewArena("rot-test")) { Grid = 0.5f };
        var s = ed.Place(EditorItem.Box, new Vector3(4, 0, 4));
        ed.Resize(new Vector3(2, 0, 0)); // 4 x 1 x 2
        var before = ed.Arena.Boxes[s.Index];
        var c0 = (before.MinV + before.MaxV) * 0.5f;
        ed.RotateY();
        var after = ed.Arena.Boxes[s.Index];
        var size = after.MaxV - after.MinV;
        Assert.Equal(new Vector3(2, 1, 4), size);
        Assert.Equal(c0, (after.MinV + after.MaxV) * 0.5f);

        var pad = ed.Place(EditorItem.JumpPad, Vector3.Zero);
        ed.Arena.JumpPads[pad.Index].Launch = [5, 15, 0];
        ed.RotateY();
        Assert.Equal(new[] { 0f, 15f, 5f }, ed.Arena.JumpPads[pad.Index].Launch);
        ed.Undo();
        Assert.Equal(new[] { 5f, 15f, 0f }, ed.Arena.JumpPads[pad.Index].Launch);
    }

    [Fact]
    public void Environment_presets_cycle_and_stay_valid()
    {
        var ed = new ArenaEditor(ArenaEditor.NewArena("env-test"));
        Assert.Equal(0, ed.EnvironmentIndex);
        ed.CycleEnvironment();
        Assert.Equal(1, ed.EnvironmentIndex);
        for (int i = 0; i < ArenaEditor.Environments.Length; i++) ed.CycleEnvironment();
        Assert.Equal(1, ed.EnvironmentIndex);
        ed.CycleEnvironment(-1);
        Assert.Equal(0, ed.EnvironmentIndex);
        Assert.Empty(ed.Problems());
        Assert.Contains(ArenaEditor.Environments, e => e.Night);
    }

    [Fact]
    public void Installed_builds_use_a_seeded_user_mods_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "djancoeg-mods-" + Guid.NewGuid().ToString("N"));
        var bundled = Path.Combine(root, "bundled");
        var user = Path.Combine(root, "user");
        try
        {
            Directory.CreateDirectory(Path.Combine(bundled, "contoh", "arenas"));
            File.WriteAllText(Path.Combine(bundled, "contoh", ModLoader.ManifestFile), "{}");
            File.WriteAllText(Path.Combine(bundled, "contoh", "arenas", "a.json"), "{}");
            Directory.CreateDirectory(Path.Combine(bundled, "not-a-mod"));

            Assert.Equal(bundled, ModLoader.ResolveWritableModsDirectory(bundled, user));
            Assert.Equal(user, ModLoader.ResolveWritableModsDirectory(bundled, user, forceUser: true));
            Assert.True(File.Exists(Path.Combine(user, "contoh", "arenas", "a.json")));
            Assert.False(Directory.Exists(Path.Combine(user, "not-a-mod")));

            // A player's edited copy is never overwritten by the bundled one.
            File.WriteAllText(Path.Combine(user, "contoh", ModLoader.ManifestFile), "{\"id\":\"mine\"}");
            ModLoader.ResolveWritableModsDirectory(bundled, user, forceUser: true);
            Assert.Contains("mine", File.ReadAllText(Path.Combine(user, "contoh", ModLoader.ManifestFile)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Boss_keeps_its_wounds_when_it_comes_back()
    {
        var stage = Campaign.Stage("nusa-boss-1")!;
        var w = new GameWorld(ContentRegistry.CreateBuiltIn(), stage.ToMatch() with { BotCount = 0 }, 5);
        int a = w.AddPlayer("A", 0, false);
        for (int i = 0; i < SimConstants.TickRate * 8 && w.BossSlot < 0; i++) w.Step();
        int slot = w.BossSlot;
        Assert.True(slot >= 0);
        float max = w.Creeps[slot].MaxHealth;
        for (int i = 0; i < 300 && w.Creeps[slot].State == CreepState.Emerging; i++) w.Step();
        w.DamageCreep(slot, a, max * 0.6f, Vector3.UnitX, 0);
        float left = w.Creeps[slot].Health;

        w.Creeps[slot].Active = false; // wandered off / fell
        for (int i = 0; i < SimConstants.TickRate * 8 && (w.BossSlot < 0 || !w.Creeps[w.BossSlot].Active); i++) w.Step();
        Assert.True(w.BossSlot >= 0);
        Assert.Equal(left, w.Creeps[w.BossSlot].Health, 1f);
        Assert.Equal(max, w.Creeps[w.BossSlot].MaxHealth);
    }

    [Fact]
    public void Nusa_Tenggara_is_the_fifth_island_with_two_bosses()
    {
        var nusa = Campaign.Islands[^1];
        Assert.Equal("nusa-tenggara", nusa.Id);
        Assert.Equal(2, nusa.Stages.Count(s => s.IsBoss));
        Assert.Equal(60, Campaign.MaxStars);
    }
}
