using System.Net;
using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;
using Djancoeg.Modding;
using Djancoeg.Net;

namespace Djancoeg.Tests;

public class VerticalDestructionTests
{
    private static ArenaDef TowerArena() => new()
    {
        Id = "tower-test",
        Boxes =
        [
            new() { Min = [-10, -1, -10], Max = [10, 0, 10] },
            new() { Min = [0, 0, 0], Max = [1, 2.7f, 1], Health = 150, Surface = SurfaceKind.Metal, Color = "#FF7043" }, // 3 layers
        ],
        Spawns = [new() { Position = [-5, 0, -5] }, new() { Position = [5, 0, 5] }],
    };

    [Fact]
    public void Tall_breakables_are_cut_into_layers()
    {
        var layers = ArenaPieces.Pieces(TowerArena().Boxes[1]).ToList();
        Assert.Equal(3, layers.Count);
        Assert.Equal(0f, layers[0].Min[1]);
        Assert.Equal(2.7f, layers[^1].Max[1]);
        for (int i = 1; i < layers.Count; i++) Assert.Equal(layers[i - 1].Max[1], layers[i].Min[1], 0.001f);
        // Short boxes stay in one layer.
        Assert.Single(ArenaPieces.Pieces(TowerArena().Boxes[1] with { Max = [1, 1.1f, 1] }));
    }

    [Fact]
    public void Breaking_the_bottom_layer_brings_the_rest_down_and_they_return_bottom_first()
    {
        var content = ContentRegistry.CreateBuiltIn();
        content.AddOrReplace(TowerArena());
        var w = new GameWorld(content, new MatchSettings { ArenaId = "tower-test", BotCount = 0, Creeps = false, TimeLimitSeconds = 0 }, 1);
        Assert.Equal(3, w.Destructibles.Length);
        uint before = w.Events.LastId;
        w.DamageBox(w.Destructibles[0], 9999, Vector3.UnitX);
        Assert.Equal(GameWorld.Bit(0) | GameWorld.Bit(1) | GameWorld.Bit(2), w.DestroyedMask);
        var events = new GameEvent[16];
        int n = w.Events.ReadSince(before, events);
        Assert.Equal(3, events.Take(n).Count(e => e.Type == GameEventType.BoxDestroyed));

        // Breaking only the top layer leaves the others.
        var w2 = new GameWorld(content, new MatchSettings { ArenaId = "tower-test", BotCount = 0, Creeps = false, TimeLimitSeconds = 0 }, 1);
        w2.DamageBox(w2.Destructibles[2], 9999, Vector3.UnitX);
        Assert.Equal(GameWorld.Bit(2), w2.DestroyedMask);

        // Everything comes back eventually, never with an upper layer floating over a missing one.
        bool restored = false;
        for (int t = 0; t < SimConstants.TickRate * 120 && !restored; t++)
        {
            w.Step();
            var m = w.DestroyedMask;
            if ((m & GameWorld.Bit(0)) != UInt128.Zero) Assert.NotEqual(UInt128.Zero, m & GameWorld.Bit(1));
            restored = m == UInt128.Zero;
        }
        Assert.True(restored);
    }
}

public class MultiSelectTests
{
    [Fact]
    public void Shift_click_selection_moves_copies_and_deletes_together()
    {
        var ed = new ArenaEditor(ArenaEditor.NewArena("multi")) { Grid = 0.5f };
        var a = ed.Place(EditorItem.Box, new Vector3(2, 0, 2));
        var b = ed.Place(EditorItem.Pickup, new Vector3(6, 0, 2));
        ed.Selection = a;
        ed.ToggleSelect(b);
        Assert.Equal(2, ed.SelectedItems.Count);
        Assert.Equal(b, ed.Selection);

        var boxBefore = ed.Arena.Boxes[a.Index].MinV;
        var pickupBefore = ed.Arena.Pickups[b.Index].PositionV;
        ed.Move(new Vector3(0, 0, 3));
        Assert.Equal(boxBefore + new Vector3(0, 0, 3), ed.Arena.Boxes[a.Index].MinV);
        Assert.Equal(pickupBefore + new Vector3(0, 0, 3), ed.Arena.Pickups[b.Index].PositionV);

        // Group copy keeps the relative layout when pasted.
        Assert.True(ed.Copy());
        int boxes = ed.Arena.Boxes.Count, pickups = ed.Arena.Pickups.Count;
        ed.Paste(new Vector3(-8, 0, -8));
        Assert.Equal(boxes + 1, ed.Arena.Boxes.Count);
        Assert.Equal(pickups + 1, ed.Arena.Pickups.Count);
        Assert.Equal(2, ed.SelectedItems.Count);
        var gapBefore = ed.Arena.Pickups[b.Index].PositionV - ed.Arena.Boxes[a.Index].MinV;
        var gapAfter = ed.Arena.Pickups[^1].PositionV - ed.Arena.Boxes[^1].MinV;
        Assert.True(Vector3.Distance(gapBefore, gapAfter) < 0.01f);

        // Deleting removes both pasted items in one undo step.
        Assert.True(ed.Delete());
        Assert.Equal(boxes, ed.Arena.Boxes.Count);
        Assert.Equal(pickups, ed.Arena.Pickups.Count);
        ed.Undo();
        Assert.Equal(boxes + 1, ed.Arena.Boxes.Count);

        // Toggling an item twice removes it again.
        ed.Selection = a;
        ed.ToggleSelect(b);
        ed.ToggleSelect(b);
        Assert.Single(ed.SelectedItems);
    }

    [Fact]
    public void Duplicate_with_several_items_copies_all_of_them()
    {
        var ed = new ArenaEditor(ArenaEditor.NewArena("dup"));
        ed.Selection = new EditorSelection(EditorItem.Spawn, 0);
        ed.ToggleSelect(new EditorSelection(EditorItem.Spawn, 1));
        int spawns = ed.Arena.Spawns.Count;
        Assert.True(ed.Duplicate());
        Assert.Equal(spawns + 2, ed.Arena.Spawns.Count);
        Assert.Equal(2, ed.SelectedItems.Count);
    }
}

[Collection("Network")]
public class WorkshopUploadTests
{
    [Fact]
    public async Task Upload_needs_a_valid_token_and_owners_keep_their_packages()
    {
        var root = Path.Combine(Path.GetTempPath(), "djancoeg-up-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(root, "workshop");
            string alice = WorkshopServer.AddToken(dir, "alice");
            string bob = WorkshopServer.AddToken(dir, "bob");
            Assert.Equal("alice", WorkshopServer.CheckToken(dir, alice));
            Assert.Null(WorkshopServer.CheckToken(dir, "nope"));

            var src = ModLoader.Scaffold(Path.Combine(root, "src"), "unggah-uji");
            var package = ModLoader.Pack(src, Path.Combine(root, "out"));

            using var server = new WorkshopServer(dir, 0, IPAddress.Loopback);
            server.Start();
            using var http = new HttpClient();
            var client = new WorkshopClient(http);
            string url = $"http://127.0.0.1:{server.Port}/";

            var bad = await Assert.ThrowsAsync<HttpRequestException>(() => client.UploadAsync(url, package, "wrong-token"));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

            var entry = await client.UploadAsync(url, package, alice);
            Assert.Equal("unggah-uji", entry.Id);
            Assert.Equal("alice", entry.UploadedBy);
            var index = await client.FetchIndexAsync(url);
            Assert.Contains(index.Packages, p => p.Id == "unggah-uji");

            // Alice can update; Bob cannot take over her package.
            await client.UploadAsync(url, package, alice);
            var taken = await Assert.ThrowsAsync<HttpRequestException>(() => client.UploadAsync(url, package, bob));
            Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

            // Garbage is rejected by validation.
            var junk = Path.Combine(root, "junk.djmod");
            await File.WriteAllBytesAsync(junk, [1, 2, 3, 4]);
            var invalid = await Assert.ThrowsAsync<HttpRequestException>(() => client.UploadAsync(url, junk, alice));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

[Collection("Network")]
public class CoopGuestTests
{
    [Fact]
    public void Joining_players_learn_the_campaign_stage()
    {
        var content = ContentRegistry.CreateBuiltIn();
        var stage = Campaign.Stage("jawa-2")!;
        using var server = new GameServer(content, new ServerConfig
        {
            Port = 0, Bind = IPAddress.Loopback, Match = stage.ToMatch(coop: true) with { BotCount = 1 },
        });
        var client = new GameClient(content);
        client.Connect(new ConnectOptions { Server = new IPEndPoint(IPAddress.Loopback, server.Port), Name = "Tamu", CharacterId = "pocong", PlayerKey = "k-guest" });
        for (int i = 0; i < 400 && client.State != ClientState.Connected; i++)
        {
            client.Update();
            Thread.Sleep(1);
            server.TickOnce();
        }
        Assert.Equal(ClientState.Connected, client.State);
        Assert.Equal("jawa-2", client.CampaignStage);
        Assert.Equal(GameModeKind.TeamDeathmatch, client.Mode);
    }

    [Fact]
    public void Normal_matches_have_no_campaign_stage() => Assert.Equal("", new MatchSettings().CampaignStage);
}
