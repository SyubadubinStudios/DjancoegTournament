using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;
using Djancoeg.Modding;

namespace Djancoeg.Tests;

public class EditorTests
{
    [Fact]
    public void New_arena_is_valid_and_playable()
    {
        var ed = new ArenaEditor(ArenaEditor.NewArena("arena-ku-1"));
        Assert.Empty(ed.Problems());
        var content = ContentRegistry.CreateBuiltIn();
        content.AddOrReplace(ed.Arena);
        var w = new GameWorld(content, new MatchSettings { ArenaId = "arena-ku-1", BotCount = 3, Creeps = false });
        w.AddPlayer("A", 0, false);
        for (int i = 0; i < 120; i++) w.Step();
        Assert.True(w.Players[0].Alive);
    }

    [Fact]
    public void Place_move_resize_duplicate_delete_with_undo_redo()
    {
        var ed = new ArenaEditor(ArenaEditor.NewArena("arena-ku-2")) { Grid = 0.5f };
        int boxes = ed.Arena.Boxes.Count;
        var sel = ed.Place(EditorItem.Box, new Vector3(3.2f, 0, 4.9f));
        Assert.Equal(boxes + 1, ed.Arena.Boxes.Count);
        var b = ed.Arena.Boxes[sel.Index];
        Assert.Equal(new Vector3(2, 0, 4), b.MinV); // snapped to 3,0,5 then +-1

        ed.Move(new Vector3(1, 0, 0));
        Assert.Equal(3f, ed.Arena.Boxes[sel.Index].Min[0]);
        ed.Resize(new Vector3(0, 2, 0));
        Assert.Equal(3f, ed.Arena.Boxes[sel.Index].Max[1]);
        ed.Resize(new Vector3(0, -10, 0));
        Assert.Equal(0.5f, ed.Arena.Boxes[sel.Index].Max[1]); // never smaller than one grid step

        ed.CycleBoxKind();
        Assert.True(ed.Arena.Boxes[sel.Index].Health > 0);
        ed.Duplicate();
        Assert.Equal(boxes + 2, ed.Arena.Boxes.Count);
        Assert.True(ed.Delete());
        Assert.Equal(boxes + 1, ed.Arena.Boxes.Count);

        ed.Undo();
        Assert.Equal(boxes + 2, ed.Arena.Boxes.Count);
        ed.Redo();
        Assert.Equal(boxes + 1, ed.Arena.Boxes.Count);
        while (ed.CanUndo) ed.Undo();
        Assert.Equal(boxes, ed.Arena.Boxes.Count);
    }

    [Fact]
    public void Pick_prefers_markers_over_the_floor_they_stand_on()
    {
        var ed = new ArenaEditor(ArenaEditor.NewArena("arena-ku-3"));
        var spawn = ed.Arena.Spawns[0].PositionV;
        var origin = spawn + new Vector3(0, 10, 0.01f);
        var hit = ed.Pick(origin, -Vector3.UnitY, out _, out _);
        Assert.Equal(EditorItem.Spawn, hit.Kind);
        Assert.Equal(0, hit.Index);

        var floorHit = ed.Pick(new Vector3(5, 10, 5), -Vector3.UnitY, out float d, out var n);
        Assert.Equal(EditorItem.Box, floorHit.Kind);
        Assert.Equal(10f, d, 0.01f);
        Assert.Equal(Vector3.UnitY, n);
    }

    [Fact]
    public void Problems_report_too_few_spawns()
    {
        var ed = new ArenaEditor(ArenaEditor.NewArena("arena-ku-4"));
        for (int i = 0; i < 3; i++) { ed.Selection = new EditorSelection(EditorItem.Spawn, 0); ed.Delete(); }
        Assert.Contains(ed.Problems(), p => p.Contains("spawn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Save_creates_a_loadable_mod()
    {
        var dir = Path.Combine(Path.GetTempPath(), "djancoeg-editor-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ed = new ArenaEditor(ArenaEditor.NewArena("arena-ku-5"));
            ed.Place(EditorItem.JumpPad, new Vector3(0, 0, 5));
            ed.Save(dir);
            ed.Rename("arena-ku-6", "Second", "Kedua");
            ed.Save(dir);

            var info = ModLoader.Load(Path.Combine(dir, ArenaEditor.ModId));
            Assert.NotNull(info.Manifest);
            Assert.Empty(info.Errors);
            Assert.Equal(2, info.Manifest!.Arenas.Count);
            Assert.Single(info.Manifest.Arenas[0].JumpPads);
            Assert.Equal(2, ArenaEditor.LoadSaved(dir).Count);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
