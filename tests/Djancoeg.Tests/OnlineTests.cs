using System.Net;
using System.Text;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;
using Djancoeg.Modding;
using Djancoeg.Net;

namespace Djancoeg.Tests;

/// <summary>v1.3 "Kampung Online": crypto, handshake, delta snapshots, admin, votes, stats, NAT punch, Workshop.</summary>
public class CryptoTests
{
    private static (SecureChannel client, SecureChannel server) Pair()
    {
        using var a = new KeyExchange();
        using var b = new KeyExchange();
        var salt = "cookie-salt-1234"u8.ToArray();
        return (a.Derive(b.PublicKey, salt, isServer: false)!, b.Derive(a.PublicKey, salt, isServer: true)!);
    }

    [Fact]
    public void Sealed_packets_round_trip_both_ways()
    {
        var (c, s) = Pair();
        var aad = "DJ\u0004\u0010"u8.ToArray();
        var msg = Encoding.UTF8.GetBytes("Tawuran dimulai!");
        var buf = new byte[256];
        int n = c.Seal(msg, aad, buf);
        var plain = new byte[256];
        Assert.True(s.Open(buf.AsSpan(0, n), aad, plain, out int len));
        Assert.Equal(msg, plain[..len]);
        n = s.Seal(msg, aad, buf);
        Assert.True(c.Open(buf.AsSpan(0, n), aad, plain, out len));
        Assert.Equal(msg, plain[..len]);
    }

    [Fact]
    public void Tampering_replay_and_wrong_direction_are_rejected()
    {
        var (c, s) = Pair();
        var aad = new byte[] { 1, 2, 3, 4 };
        var buf = new byte[128];
        var plain = new byte[128];
        int n = c.Seal("hello"u8, aad, buf);
        var packet = buf[..n];
        Assert.True(s.Open(packet, aad, plain, out _));
        Assert.False(s.Open(packet, aad, plain, out _));                // replay
        int m = c.Seal("again"u8, aad, buf);
        var tampered = buf[..m];
        tampered[^1] ^= 0x40;
        Assert.False(s.Open(tampered, aad, plain, out _));              // forged tag
        int k = c.Seal("third"u8, aad, buf);
        Assert.False(c.Open(buf.AsSpan(0, k), aad, plain, out _));      // reflected back at the sender
        Assert.False(s.Open(buf.AsSpan(0, k), new byte[] { 9, 9, 9, 9 }, plain, out _)); // header changed
    }

    [Fact]
    public void Out_of_order_packets_within_the_window_are_accepted_once()
    {
        var (c, s) = Pair();
        var aad = new byte[4];
        var packets = new List<byte[]>();
        var buf = new byte[64];
        for (int i = 0; i < 5; i++) { int n = c.Seal(new[] { (byte)i }, aad, buf); packets.Add(buf[..n]); }
        var plain = new byte[64];
        Assert.True(s.Open(packets[4], aad, plain, out _));
        Assert.True(s.Open(packets[1], aad, plain, out _));
        Assert.True(s.Open(packets[3], aad, plain, out _));
        Assert.False(s.Open(packets[1], aad, plain, out _));
    }

    [Fact]
    public void Cookies_expire_and_are_bound_to_the_address()
    {
        var jar = new CookieJar();
        var addr = new byte[] { 2, 0, 1, 2, 127, 0, 0, 1 };
        var cookie = new byte[CookieJar.CookieSize];
        jar.Issue(addr, 1000, cookie);
        Assert.True(jar.Verify(addr, cookie, 1005));
        Assert.False(jar.Verify(addr, cookie, 1020));
        Assert.False(jar.Verify(new byte[] { 2, 0, 1, 2, 10, 0, 0, 9 }, cookie, 1005));
    }
}

[Collection("Network")]
public class OnlineServerTests
{
    private static GameServer Server(ContentRegistry content, int bots = 2, string? password = null, string? dataDir = null, int max = 16, GameModeKind mode = GameModeKind.FreeForAll) =>
        new(content, new ServerConfig
        {
            Port = 0, Bind = IPAddress.Loopback, Password = password, DataDirectory = dataDir, TrustLoopbackAdmin = false,
            Match = new MatchSettings { BotCount = bots, ArenaId = "pasar-malam", TimeLimitSeconds = 0, ScoreLimit = 1000, MaxPlayers = max, Mode = mode },
        });

    private static GameClient Join(ContentRegistry content, GameServer server, string name = "Tester", string? password = null, string key = "key-tester")
    {
        var client = new GameClient(content);
        client.Connect(new ConnectOptions { Server = new IPEndPoint(IPAddress.Loopback, server.Port), Name = name, CharacterId = "pocong", Password = password, PlayerKey = key });
        return client;
    }

    private static void Pump(GameServer server, params GameClient[] clients) => Pump(server, 1, clients);

    private static void Pump(GameServer server, int ticks, params GameClient[] clients)
    {
        for (int i = 0; i < ticks; i++)
        {
            foreach (var c in clients) { c.Update(); if (c.State == ClientState.Connected) c.SendInput(default); }
            Thread.Sleep(1);
            server.TickOnce();
        }
        foreach (var c in clients) c.Update();
    }

    private static void WaitConnected(GameServer server, GameClient client)
    {
        for (int i = 0; i < 400 && (client.State != ClientState.Connected || client.Latest is null); i++)
        {
            if (client.State is ClientState.Rejected or ClientState.TimedOut) break;
            Pump(server, client);
        }
    }

    [Fact]
    public void Connection_is_encrypted_after_the_handshake()
    {
        var content = ContentRegistry.CreateBuiltIn();
        using var server = Server(content);
        using var client = Join(content, server);
        WaitConnected(server, client);
        Assert.Equal(ClientState.Connected, client.State);
        Assert.True(client.IsEncrypted);
    }

    [Fact]
    public void Password_is_required_and_checked()
    {
        var content = ContentRegistry.CreateBuiltIn();
        using var server = Server(content, password: "rahasia");
        using (var none = Join(content, server))
        {
            WaitConnected(server, none);
            Assert.Equal(ClientState.Rejected, none.State);
            Assert.Equal(RejectCode.Password, none.RejectCode);
        }
        using (var wrong = Join(content, server, password: "salah"))
        {
            WaitConnected(server, wrong);
            Assert.Equal(RejectCode.Password, wrong.RejectCode);
        }
        using var right = Join(content, server, password: "rahasia");
        WaitConnected(server, right);
        Assert.Equal(ClientState.Connected, right.State);
    }

    [Fact]
    public void Thirty_two_players_fit_in_one_datagram_and_deltas_are_smaller()
    {
        var content = ContentRegistry.CreateBuiltIn();
        using var server = Server(content, bots: 31, max: 32);
        using var client = Join(content, server);
        WaitConnected(server, client);
        Assert.Equal(32, client.Latest!.PlayerCount);
        int full = server.LastSnapshotBytes;
        Pump(server, 120, client);
        var sizes = new List<int>();
        for (int i = 0; i < 30; i++) { Pump(server, client); sizes.Add(server.LastSnapshotBytes); }
        Assert.All(sizes, s => Assert.True(s <= Protocol.MaxPacketSize + 64, $"snapshot {s} bytes"));
        Assert.True(client.Latest!.BaselineTick != 0, "client should be receiving deltas");
        Assert.True(sizes.Average() < full, $"deltas {sizes.Average():0} vs full {full}");
        // Decoded state matches the server.
        server.WithWorld(w =>
        {
            for (int i = 0; i < client.Latest!.PlayerCount; i++)
            {
                var p = client.Latest.Players[i];
                Assert.True(w.Players[p.Id].Active);
                Assert.Equal(w.Players[p.Id].CharacterIndex, (int)p.Character);
                Assert.Equal(w.Players[p.Id].Team, p.Team);
            }
        });
    }

    [Fact]
    public void Admin_can_kick_and_ban_from_the_console()
    {
        var content = ContentRegistry.CreateBuiltIn();
        var dir = Path.Combine(Path.GetTempPath(), "djancoeg-admin-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var server = Server(content, dataDir: dir);
            using var a = Join(content, server, "Asep");
            WaitConnected(server, a);
            string status = server.Execute("status", null);
            Assert.Contains("Asep", status);
            string result = server.Execute("ban Asep spamming", null);
            Assert.Contains("banned", result);
            Pump(server, 5, a);
            Assert.Equal(ClientState.Rejected, a.State);
            Assert.Equal(RejectCode.Banned, a.RejectCode);
            Assert.Contains("127.0.0.1", File.ReadAllText(Path.Combine(dir, "bans.txt")));

            using var again = Join(content, server, "Asep2");
            WaitConnected(server, again);
            Assert.Equal(RejectCode.Banned, again.RejectCode);
            Assert.Contains("Unbanned", server.Execute("unban 127.0.0.1", null));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Map_command_and_vote_change_the_arena()
    {
        var content = ContentRegistry.CreateBuiltIn();
        using var server = Server(content);
        using var a = Join(content, server, "Voter");
        WaitConnected(server, a);
        server.Execute("map sawah-ronda koth", null);
        server.WithWorld(w => { Assert.Equal("sawah-ronda", w.Arena.Id); Assert.Equal(GameModeKind.KingOfTheHill, w.Settings.Mode); });

        a.SendChat("!vote map pelabuhan");   // the only human: their own yes passes the vote
        for (int i = 0; i < 200; i++) { Pump(server, a); string id = ""; server.WithWorld(w => id = w.Arena.Id); if (id == "pelabuhan") break; }
        server.WithWorld(w => Assert.Equal("pelabuhan", w.Arena.Id));
        Pump(server, 40, a);
        Assert.Equal("pelabuhan", a.Arena!.Id);
    }

    [Fact]
    public void Rcon_login_gives_admin_rights()
    {
        var content = ContentRegistry.CreateBuiltIn();
        using var server = new GameServer(content, new ServerConfig
        {
            Port = 0, Bind = IPAddress.Loopback, RconPassword = "kentongan", TrustLoopbackAdmin = false,
            Match = new MatchSettings { BotCount = 1, TimeLimitSeconds = 0 },
        });
        using var a = Join(content, server, "Pak RT");
        WaitConnected(server, a);
        a.SendChat("!bots 4");
        Pump(server, 10, a);
        server.WithWorld(w => Assert.Equal(1, w.Settings.BotCount));
        a.SendChat("!login kentongan");
        Pump(server, 5, a);
        a.SendChat("!bots 4");
        Pump(server, 10, a);
        server.WithWorld(w => Assert.Equal(4, w.Settings.BotCount));
    }

    [Fact]
    public void Stats_persist_and_the_leaderboard_reaches_the_client()
    {
        var content = ContentRegistry.CreateBuiltIn();
        var dir = Path.Combine(Path.GetTempPath(), "djancoeg-stats-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var server = Server(content, bots: 1, dataDir: dir))
            {
                using var a = Join(content, server, "Jagoan", key: "player-key-1");
                WaitConnected(server, a);
                // Give our player a kill on the bot.
                server.WithWorld(w =>
                {
                    int me = a.LocalId, bot = Enumerable.Range(0, w.Players.Length).First(i => w.Players[i].Active && w.Players[i].IsBot);
                    w.Players[bot].SpawnProtect = 0;
                    w.ApplyDamage(bot, me, 1, 999, System.Numerics.Vector3.UnitX, 0, KillFlags.None);
                });
                Pump(server, 5, a);
                a.RequestLeaderboard();
                for (int i = 0; i < 50 && a.LeaderboardVersion == 0; i++) Pump(server, a);
                var me = Assert.Single(a.Leaderboard, e => e.IsYou);
                Assert.Equal("Jagoan", me.Name);
                Assert.Equal(1, me.Kills);
                server.Stop();
            }
            var store = new StatsStore(Path.Combine(dir, "stats.json"));
            Assert.Equal(1, store.Get("player-key-1")!.Kills);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Master_relays_punch_requests_to_the_server()
    {
        using var master = new MasterServer(0);
        var content = ContentRegistry.CreateBuiltIn();
        using var server = new GameServer(content, new ServerConfig { Port = 0, Bind = IPAddress.Loopback, MasterServer = $"127.0.0.1:{master.Port}", Match = new MatchSettings { BotCount = 0 } });
        for (int i = 0; i < 40 && master.Count == 0; i++) { server.TickOnce(); Thread.Sleep(5); master.PumpOnce(); }
        Assert.Equal(1, master.Count);

        // A "client" socket asks the master to punch; the server should then send Punch packets to it.
        using var fake = new UdpTransport(0, IPAddress.Loopback);
        var w = new NetWriter(fake.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.PunchRequest);
#pragma warning disable CS0618
        w.U32((uint)IPAddress.Loopback.Address);
#pragma warning restore CS0618
        w.U16((ushort)server.Port);
        fake.Send(w.Written, new IPEndPoint(IPAddress.Loopback, master.Port));
        bool punched = false;
        for (int i = 0; i < 100 && !punched; i++)
        {
            master.PumpOnce();
            server.TickOnce();
            Thread.Sleep(2);
            int n = fake.Receive(out _);
            if (n > 0)
            {
                var r = new NetReader(fake.ReceiveBuffer.AsSpan(0, n));
                punched = Protocol.ReadHeader(ref r) == PacketType.Punch;
            }
        }
        Assert.True(punched);
    }
}

[Collection("Network")]
public class WorkshopOnlineTests
{
    [Fact]
    public async Task Publish_serve_download_and_install()
    {
        var root = Path.Combine(Path.GetTempPath(), "djancoeg-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            var src = ModLoader.Scaffold(Path.Combine(root, "src"), "ws-uji");
            var package = ModLoader.Pack(src, Path.Combine(root, "out"));
            var entry = WorkshopServer.Publish(package, Path.Combine(root, "workshop"));
            Assert.Equal(64, entry.Sha256.Length);

            using var server = new WorkshopServer(Path.Combine(root, "workshop"), 0, IPAddress.Loopback);
            server.Start();
            using var http = new HttpClient();
            var client = new WorkshopClient(http);
            string url = $"http://127.0.0.1:{server.Port}/";
            var index = await client.FetchIndexAsync(url);
            var listed = Assert.Single(index.Packages);
            Assert.Equal("ws-uji", listed.Id);

            var mods = Path.Combine(root, "mods");
            var installed = await client.InstallAsync(url, listed, mods);
            Assert.True(installed.IsValid, string.Join("; ", installed.Errors));

            var traversal = await http.GetAsync($"{url}packages/..%2Findex.json");
            Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);

            var tampered = listed with { Sha256 = new string('0', 64) };
            await Assert.ThrowsAsync<InvalidDataException>(() => client.InstallAsync(url, tampered, Path.Combine(root, "mods2")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
