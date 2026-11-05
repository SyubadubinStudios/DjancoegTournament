using System.IO.Compression;
using System.Net;
using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;
using Djancoeg.Modding;
using Djancoeg.Net;

namespace Djancoeg.Tests;

public class ProtocolTests
{
    [Fact]
    public void Writer_and_reader_round_trip()
    {
        var buf = new byte[256];
        var w = new NetWriter(buf);
        Protocol.WriteHeader(ref w, PacketType.Input);
        w.U32(123456); w.Str("Asep Knalpot ⚡"); w.Pos(new Vector3(12.5f, -3.25f, 100f)); w.Angle(-2.5f); w.F32(1.5f);
        var input = new PlayerInput { Sequence = 77, Yaw = 1.2f, Pitch = -0.4f, Forward = 127, Right = -64, Buttons = InputButtons.Fire | InputButtons.Jump, WeaponSlot = 3 };
        Protocol.WriteInput(ref w, input);
        Assert.False(w.Overflow);

        var r = new NetReader(buf.AsSpan(0, w.Position));
        Assert.Equal(PacketType.Input, Protocol.ReadHeader(ref r));
        Assert.Equal(123456u, r.U32());
        Assert.Equal("Asep Knalpot ⚡", r.Str());
        var p = r.Pos();
        Assert.Equal(12.5f, p.X, 2); Assert.Equal(-3.25f, p.Y, 2); Assert.Equal(100f, p.Z, 2);
        Assert.Equal(-2.5f, r.Angle(), 3);
        Assert.Equal(1.5f, r.F32());
        var back = Protocol.ReadInput(ref r);
        Assert.Equal(77u, back.Sequence);
        Assert.Equal(1.2f, back.Yaw, 3);
        Assert.Equal(-0.4f, back.Pitch, 3);
        Assert.Equal(-64, back.Right);
        Assert.Equal(input.Buttons, back.Buttons);
        Assert.Equal(3, back.WeaponSlot);
        Assert.False(r.Error);
    }

    [Fact]
    public void Reader_flags_truncated_packets_instead_of_throwing()
    {
        var r = new NetReader(new byte[] { 1, 2 });
        Assert.Equal(0u, r.U32());
        Assert.True(r.Error);
    }

    [Fact]
    public void Foreign_packets_are_ignored()
    {
        var r = new NetReader("GET / HTTP/1.1"u8);
        Assert.Equal((PacketType)0, Protocol.ReadHeader(ref r));
    }

    [Fact]
    public void Writer_reports_overflow()
    {
        var w = new NetWriter(new byte[3]);
        w.U32(1);
        Assert.True(w.Overflow);
    }
}

[CollectionDefinition("Network", DisableParallelization = true)]
public sealed class NetworkCollection;

[Collection("Network")]
public class LoopbackTests
{
    private static GameServer StartServer(ContentRegistry content, int bots = 2) =>
        new(content, new ServerConfig
        {
            Port = 0, Bind = IPAddress.Loopback,
            Match = new MatchSettings { BotCount = bots, ArenaId = "terminal-bus", TimeLimitSeconds = 0, ScoreLimit = 1000 },
        });

    private static void Pump(GameServer server, GameClient client, int ticks, Func<int, PlayerInput>? input = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            client.Update();
            if (client.State == ClientState.Connected) client.SendInput(input?.Invoke(i) ?? default);
            Thread.Sleep(1);
            server.TickOnce();
        }
        client.Update();
    }

    [Fact]
    public void Client_connects_receives_snapshots_and_moves()
    {
        var content = ContentRegistry.CreateBuiltIn();
        using var server = StartServer(content);
        using var client = new GameClient(content);
        string? arenaChanged = null;
        client.ArenaChanged += a => arenaChanged = a.Id;
        client.Connect(new IPEndPoint(IPAddress.Loopback, server.Port), "Tester", "tukang-ojol");

        for (int i = 0; i < 300 && (client.State != ClientState.Connected || client.Latest is null || client.Names[client.LocalId] == ""); i++)
            Pump(server, client, 1);
        Assert.Equal(ClientState.Connected, client.State);
        Assert.Equal("terminal-bus", arenaChanged);
        Assert.NotNull(client.Latest);
        Assert.Equal(3, client.Latest!.PlayerCount); // tester + 2 bots
        Assert.Equal("Tester", client.Names[client.LocalId]);

        // Wait for respawn landing, then walk forward and check server agrees with prediction.
        Pump(server, client, 60);
        Assert.True(client.Latest!.TryGetPlayer(client.LocalId, out var start));
        NetPlayer after = start;
        for (int i = 0; i < 30 && Vector3.Distance(start.Position, after.Position) <= 3f; i++)
        {
            Pump(server, client, 10, _ => new PlayerInput { Forward = 127, Yaw = start.Yaw });
            client.Latest!.TryGetPlayer(client.LocalId, out after);
        }
        Assert.True(Vector3.Distance(start.Position, after.Position) > 3f, "server should have moved the player");
        Assert.True(client.TryGetPredicted(0, out var predicted, out _));
        Assert.True(Vector3.Distance(predicted.Position, after.Position) < 3f, "prediction should track the server");

        var events = new List<GameEvent>();
        client.DrainEvents(events);
        Assert.Contains(events, e => e.Type == GameEventType.Spawn);
    }

    [Fact]
    public void Spectator_receives_snapshots_without_taking_a_slot()
    {
        var content = ContentRegistry.CreateBuiltIn();
        using var server = StartServer(content, 3);
        using var client = new GameClient(content);
        client.Connect(new IPEndPoint(IPAddress.Loopback, server.Port), "Penonton", "pocong", spectator: true);
        for (int i = 0; i < 300 && (client.State != ClientState.Connected || client.Latest is null); i++) Pump(server, client, 1);
        Pump(server, client, 30);
        Assert.Equal(ClientState.Connected, client.State);
        Assert.True(client.IsSpectator);
        Assert.Equal(3, client.Latest!.PlayerCount);          // only the bots play
        Assert.Equal(3, server.World.ActivePlayerCount);
        Assert.False(client.Latest.TryGetPlayer(client.LocalId, out _));
        Assert.False(client.TryGetPredicted(0, out _, out _));
    }

    [Fact]
    public void Server_rejects_clients_with_different_content()
    {
        var serverContent = ContentRegistry.CreateBuiltIn();
        using var server = StartServer(serverContent, 0);
        var clientContent = ContentRegistry.CreateBuiltIn();
        clientContent.AddOrReplace(new WeaponDef { Id = "extra", Slot = 9 });
        using var client = new GameClient(clientContent);
        client.Connect(new IPEndPoint(IPAddress.Loopback, server.Port), "Modder", "pocong");
        Pump(server, client, 20);
        Assert.Equal(ClientState.Rejected, client.State);
        Assert.Contains("mods", client.RejectReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lan_browser_finds_server_by_direct_ping()
    {
        var content = ContentRegistry.CreateBuiltIn();
        using var server = StartServer(content, 3);
        using var browser = new ServerBrowser();
        browser.Ping(new IPEndPoint(IPAddress.Loopback, server.Port));
        for (int i = 0; i < 50 && browser.Servers.Count == 0; i++)
        {
            server.TickOnce();
            Thread.Sleep(5);
            browser.Poll();
        }
        var info = Assert.Single(browser.Servers);
        Assert.Equal("terminal-bus", info.ArenaId);
        Assert.Equal(3, info.Players);
        Assert.Equal(content.Signature, info.ContentSignature);
    }

    [Fact]
    public void Master_server_lists_heartbeating_servers()
    {
        using var master = new MasterServer(0);
        var content = ContentRegistry.CreateBuiltIn();
        using var server = new GameServer(content, new ServerConfig
        {
            Port = 0, Bind = IPAddress.Loopback, MasterServer = $"127.0.0.1:{master.Port}",
            Match = new MatchSettings { BotCount = 1 },
        });
        for (int i = 0; i < 40 && master.Count == 0; i++) { server.TickOnce(); Thread.Sleep(5); master.PumpOnce(); }
        Assert.Equal(1, master.Count);

        using var browser = new ServerBrowser();
        browser.RefreshOnline($"127.0.0.1:{master.Port}");
        for (int i = 0; i < 80 && browser.Servers.Count == 0; i++) { master.PumpOnce(); server.TickOnce(); Thread.Sleep(5); browser.Poll(); }
        Assert.Single(browser.Servers);
    }
}

public class ModTests
{
    private static string ModsDir => Path.Combine(AppContext.BaseDirectory, "mods");

    [Fact]
    public void Example_mod_is_valid_and_adds_content()
    {
        var content = ContentRegistry.CreateBuiltIn();
        int weapons = content.Weapons.Count, chars = content.Characters.Count, arenas = content.Arenas.Count;
        var mods = ModLoader.ApplyEnabled(content, ModsDir);
        var mod = Assert.Single(mods, m => m.Id == "warung-bakso");
        Assert.True(mod.IsValid, string.Join("; ", mod.Errors));
        Assert.Equal(weapons + 1, content.Weapons.Count);
        Assert.Equal(chars + 1, content.Characters.Count);
        Assert.Equal(arenas + 1, content.Arenas.Count);
        Assert.Contains("warung-bakso", content.LoadedMods);
        Assert.NotEqual(ContentRegistry.CreateBuiltIn().Signature, content.Signature);
    }

    [Fact]
    public void Example_mod_arena_is_playable_with_bots()
    {
        var content = ContentRegistry.CreateBuiltIn();
        ModLoader.ApplyEnabled(content, ModsDir);
        var w = new GameWorld(content, new MatchSettings { ArenaId = "lapangan-17an", BotCount = 6, TimeLimitSeconds = 0, ScoreLimit = 999 });
        w.BalanceBots();
        for (int i = 0; i < SimConstants.TickRate * 30; i++) w.Step();
        Assert.Equal(6, w.ActivePlayerCount);
    }

    [Fact]
    public void Missing_json_fields_keep_their_defaults()
    {
        // Regression: source-generated JSON used to reset unspecified init-only properties to null/0.
        var json = """{ "id": "mini", "name": { "en": "Mini" }, "weapons": [ { "id": "w1", "slot": 9, "damage": 5 } ] }""";
        var m = System.Text.Json.JsonSerializer.Deserialize(json, ModJsonContext.Default.ModManifest)!;
        Assert.NotNull(m.Arenas);
        Assert.NotNull(m.Characters);
        var w = Assert.Single(m.Weapons);
        Assert.Equal(200f, w.Range);
        Assert.Equal(0.5f, w.FireInterval);
        Assert.NotNull(w.KillVerb);
        Assert.Equal(5f, w.Damage);
    }

    [Fact]
    public void Validation_reports_bad_values()
    {
        var bad = new ModManifest
        {
            Id = "Bad Id!",
            Weapons = [new WeaponDef { Id = "w", Slot = 12, FireInterval = 0.001f, Pellets = 99 }],
            Characters = [new CharacterDef { Id = "c1", MaxHealth = 9999, SpeedMultiplier = 5 }],
        };
        var errors = ModLoader.Validate(bad).ToList();
        Assert.Contains(errors, e => e.Contains("Mod id"));
        Assert.Contains(errors, e => e.Contains("slot"));
        Assert.Contains(errors, e => e.Contains("fireInterval"));
        Assert.Contains(errors, e => e.Contains("maxHealth"));
        Assert.Contains(errors, e => e.Contains("speedMultiplier"));
    }

    [Fact]
    public void Pack_install_and_toggle_round_trip()
    {
        var root = Path.Combine(Path.GetTempPath(), "djancoeg-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var src = Path.Combine(root, "src");
            var dir = ModLoader.Scaffold(src, "uji-coba");
            var package = ModLoader.Pack(dir, Path.Combine(root, "out"));
            Assert.EndsWith(".djmod", package);

            var mods = Path.Combine(root, "mods");
            var installed = ModLoader.Install(package, mods);
            Assert.True(installed.IsValid, string.Join("; ", installed.Errors));

            ModLoader.SetEnabled(mods, "uji-coba", false);
            Assert.False(Assert.Single(ModLoader.Discover(mods)).Enabled);
            ModLoader.SetEnabled(mods, "uji-coba", true);
            Assert.True(Assert.Single(ModLoader.Discover(mods)).Enabled);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Install_rejects_path_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "djancoeg-evil-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var package = Path.Combine(root, "evil.djmod");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using (var s = new StreamWriter(zip.CreateEntry("mod.json").Open())) s.Write("""{ "id": "evil-mod", "name": { "en": "x" } }""");
                using (var s = new StreamWriter(zip.CreateEntry("../../escape.txt").Open())) s.Write("nope");
            }
            Assert.Throws<InvalidDataException>(() => ModLoader.Install(package, Path.Combine(root, "mods")));
            Assert.False(File.Exists(Path.Combine(root, "escape.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
