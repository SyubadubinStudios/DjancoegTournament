using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;
using Djancoeg.Modding;

namespace Djancoeg.Tests;

public class RagdollTests
{
    private static CollisionWorld Floor()
    {
        var arena = new ArenaDef { Id = "floor", Boxes = [new() { Min = [-20, -1, -20], Max = [20, 0, 20] }] };
        return new CollisionWorld(arena);
    }

    [Fact]
    public void Ragdoll_falls_settles_on_the_floor_and_keeps_its_shape()
    {
        var rag = new Ragdoll();
        rag.Reset(new Vector3(0, 0.5f, 0), 0.7f, 1f, Vector3.Zero, new Vector3(4, 2, 0));
        var col = Floor();
        for (int i = 0; i < 60 * 6 && !rag.Sleeping; i++) rag.Step(1f / 60f, col);

        Assert.True(rag.Sleeping, "ragdoll should come to rest");
        Assert.All(rag.Position, p => Assert.True(p.Y > -0.02f, $"joint below floor: {p}"));
        // Lying down: the head is not much higher than the pelvis any more.
        Assert.True(rag.Position[Ragdoll.HeadTop].Y - rag.Position[Ragdoll.Pelvis].Y < 0.5f);
        // Bones keep (roughly) their length.
        float thigh = Vector3.Distance(rag.Position[Ragdoll.HipR], rag.Position[Ragdoll.KneeR]);
        Assert.InRange(thigh, 0.38f, 0.46f);
        float shoulders = Vector3.Distance(rag.Position[Ragdoll.ShoulderL], rag.Position[Ragdoll.ShoulderR]);
        Assert.InRange(shoulders, 0.43f, 0.49f);
    }

    [Fact]
    public void Pose_of_a_fresh_ragdoll_is_the_bind_pose_rotated_by_yaw()
    {
        var rag = new Ragdoll();
        float yaw = 1.1f;
        rag.Reset(Vector3.Zero, yaw, 1f, Vector3.Zero, Vector3.Zero);
        var heads = new Vector3[12];
        var rots = new Quaternion[12];
        rag.Pose(heads, rots);
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        for (int b = 0; b < 12; b++)
        {
            Assert.True(Vector3.Distance(heads[b], Vector3.Transform(Ragdoll.BoneHeads[b], expected)) < 0.01f, Ragdoll.BoneNames[b]);
            Assert.True(MathF.Abs(Quaternion.Dot(rots[b], expected)) > 0.999f, Ragdoll.BoneNames[b]);
        }
    }
}

public class PartialDestructionTests
{
    [Fact]
    public void Long_breakable_boxes_split_into_pieces_that_tile_the_original()
    {
        var box = new ArenaBox { Min = [0, 0, 0], Max = [3.2f, 1, 1.6f], Health = 120, Surface = SurfaceKind.Wood, Color = "#8D6E63" };
        var pieces = ArenaPieces.Pieces(box).ToList();
        Assert.Equal(3, pieces.Count);
        Assert.Equal(0f, pieces[0].Min[0]);
        Assert.Equal(3.2f, pieces[^1].Max[0]);
        for (int i = 1; i < pieces.Count; i++) Assert.Equal(pieces[i - 1].Max[0], pieces[i].Min[0], 0.001f);
        Assert.All(pieces, p => Assert.Equal(50f, p.Health));
        Assert.Single(ArenaPieces.Pieces(box with { Max = [0.9f, 1, 0.9f] }));
    }

    [Fact]
    public void Registry_splits_arenas_once_and_stays_within_the_network_limit()
    {
        var content = ContentRegistry.CreateBuiltIn();
        var market = content.GetArena("pasar-malam");
        Assert.True(market.PiecesSplit);
        int raw = BuiltInArenas.All.First(a => a.Id == "pasar-malam").Boxes.Count(b => b.Solid && b.Health > 0);
        int split = market.Boxes.Count(b => b.Solid && b.Health > 0);
        Assert.True(split > raw);
        content.AddOrReplace(market); // already split: no double split
        Assert.Equal(split, content.GetArena("pasar-malam").Boxes.Count(b => b.Solid && b.Health > 0));
        foreach (var a in content.Arenas) Assert.True(a.Boxes.Count(b => b.Solid && b.Health > 0) <= GameWorld.MaxDestructibles, a.Id);
    }

    [Fact]
    public void Breaking_one_piece_leaves_its_neighbours_standing()
    {
        var w = new GameWorld(ContentRegistry.CreateBuiltIn(), new MatchSettings { ArenaId = "pasar-malam", BotCount = 0, Creeps = false }, 3);
        int last = w.Destructibles.Length - 1;
        Assert.True(last > 1);
        w.DamageBox(w.Destructibles[last], 9999, Vector3.UnitX);
        Assert.Equal(GameWorld.Bit(last), w.DestroyedMask);
        Assert.False(w.Collision.IsBoxEnabled(w.Destructibles[last]));
        Assert.True(w.Collision.IsBoxEnabled(w.Destructibles[last - 1]));
    }

    [Fact]
    public void Destroyed_mask_bits_above_64_work()
    {
        var mask = GameWorld.Bit(100) | GameWorld.Bit(3);
        ulong lo = (ulong)mask, hi = (ulong)(mask >> 64);
        var back = ((UInt128)hi << 64) | lo;
        Assert.Equal(mask, back);
        Assert.NotEqual(UInt128.Zero, back & GameWorld.Bit(100));
    }
}

public class EditorV22Tests
{
    [Fact]
    public void A_whole_drag_is_one_undo_step()
    {
        var ed = new ArenaEditor(ArenaEditor.NewArena("drag")) { Grid = 0.5f };
        var s = ed.Place(EditorItem.Box, Vector3.Zero);
        float x0 = ed.Arena.Boxes[s.Index].Min[0];
        ed.BeginGesture();
        for (int i = 0; i < 6; i++) ed.Move(new Vector3(0.5f, 0, 0));
        ed.EndGesture();
        Assert.Equal(x0 + 3f, ed.Arena.Boxes[s.Index].Min[0]);
        ed.Undo();
        Assert.Equal(x0, ed.Arena.Boxes[s.Index].Min[0]);
    }

    [Fact]
    public void Copy_in_one_arena_paste_in_another()
    {
        var a = new ArenaEditor(ArenaEditor.NewArena("a"));
        var s = a.Place(EditorItem.Box, new Vector3(2, 0, 2));
        a.Resize(new Vector3(1, 2, 0));
        a.CycleBoxKind();
        Assert.True(a.Copy());

        var b = new ArenaEditor(ArenaEditor.NewArena("b"));
        int before = b.Arena.Boxes.Count;
        var p = b.Paste(new Vector3(-5, 0, 5));
        Assert.Equal(before + 1, b.Arena.Boxes.Count);
        var src = a.Arena.Boxes[s.Index];
        var dst = b.Arena.Boxes[p.Index];
        Assert.Equal(src.MaxV - src.MinV, dst.MaxV - dst.MinV);
        Assert.Equal(src.Health, dst.Health);
        Assert.Equal(new Vector3(-5, 0, 5), new Vector3((dst.Min[0] + dst.Max[0]) / 2, dst.Min[1], (dst.Min[2] + dst.Max[2]) / 2));
        // Editing the paste does not touch the source.
        b.Move(new Vector3(1, 0, 0));
        Assert.Equal(src.Min[0], a.Arena.Boxes[s.Index].Min[0]);
    }

    [Fact]
    public void Copy_of_a_built_in_arena_is_valid_and_independent()
    {
        var src = BuiltInArenas.All.First(x => x.Id == "terminal-bus");
        var copy = ArenaEditor.CopyOf(src, "ku-terminal", "Copy", "Salinan");
        var ed = new ArenaEditor(copy);
        Assert.Empty(ed.Problems());
        ed.Selection = new EditorSelection(EditorItem.Box, 0);
        ed.Delete();
        Assert.Equal(src.Boxes.Count - 1, ed.Arena.Boxes.Count);
        Assert.Equal("terminal-bus", src.Id);
    }
}

public class CoopAndStoryTests
{
    [Fact]
    public void Coop_turns_free_for_all_into_humans_versus_bots()
    {
        var stage = Campaign.Stage("jawa-1")!;
        var m = stage.ToMatch(coop: true);
        Assert.Equal(GameModeKind.TeamDeathmatch, m.Mode);
        Assert.True(m.CoopHumans);
        Assert.Equal(stage.ScoreLimit * 2, m.ScoreLimit);
        Assert.Equal(GameModeKind.FreeForAll, stage.ToMatch().Mode);

        var w = new GameWorld(ContentRegistry.CreateBuiltIn(), m, 9);
        int a = w.AddPlayer("A", 0, false);
        int b = w.AddPlayer("B", 1, false);
        w.BalanceBots();
        Assert.Equal(0, w.Players[a].Team);
        Assert.Equal(0, w.Players[b].Team);
        Assert.Contains(w.Players, p => p.Active && p.IsBot);
        Assert.All(w.Players.Where(p => p.Active && p.IsBot), p => Assert.Equal(1, p.Team));
    }

    [Fact]
    public void Every_island_has_an_intro_and_the_story_images_exist()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "assets", "ui"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        foreach (var island in Campaign.Islands)
        {
            Assert.NotEmpty(island.Intro);
            Assert.All(island.Intro, l => { Assert.False(string.IsNullOrWhiteSpace(l.En)); Assert.False(string.IsNullOrWhiteSpace(l.Id)); });
            Assert.True(File.Exists(Path.Combine(root!, "assets", island.IntroImage)), island.IntroImage);
        }
        Assert.True(File.Exists(Path.Combine(root!, "assets", Campaign.Finale.Image)));
        Assert.NotNull(Campaign.Stage(Campaign.FinaleStage));
    }
}
