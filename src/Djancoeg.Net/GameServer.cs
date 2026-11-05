using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Net;

public sealed class ServerConfig
{
    public MatchSettings Match { get; set; } = new();
    public int Port { get; set; } = Protocol.DefaultPort;
    public IPAddress Bind { get; set; } = IPAddress.Any;
    /// <summary>Arena ids to cycle through after each match. Empty = keep the same arena.</summary>
    public List<string> ArenaRotation { get; set; } = [];
    /// <summary>"host:port" of a master server to advertise to (online server browser). Null = LAN only.</summary>
    public string? MasterServer { get; set; }
    /// <summary>Join password (null/empty = open server).</summary>
    public string? Password { get; set; }
    /// <summary>Remote admin password for "!login" in chat (null = only the console and local players are admins).</summary>
    public string? RconPassword { get; set; }
    /// <summary>Players connecting from this machine are admins (listen servers, local testing).</summary>
    public bool TrustLoopbackAdmin { get; set; } = true;
    public bool AllowVotes { get; set; } = true;
    /// <summary>Folder for stats.json and bans.txt. Null = nothing is persisted.</summary>
    public string? DataDirectory { get; set; }
    public Action<string>? Log { get; set; }
}

/// <summary>
/// Authoritative game server. Runs the <see cref="GameWorld"/> at 60 Hz, consumes client inputs in order and sends
/// 30 Hz snapshots, delta-encoded against the last snapshot each client acknowledged. Connections use a stateless
/// cookie challenge (and optional password), then ECDH + AES-GCM encryption. Also hosts admin commands, votes,
/// bans, persistent stats and NAT punch-through answers. Used by the dedicated server, "Host game" and practice.
/// </summary>
public sealed partial class GameServer : IDisposable
{
    private sealed class SentFrame
    {
        public uint Tick;
        public int Count;
        public readonly NetPlayer[] Players = new NetPlayer[SimConstants.MaxPlayers];
    }

    private sealed class Client
    {
        public required SocketAddress Address;
        public required SecureChannel Channel;
        public required KeyExchange Keys;
        public int Slot; // -1 for spectators
        public bool IsSpectator => Slot < 0;
        public string Name = "";
        public string PlayerKey = "";
        public bool IsAdmin;
        public double LastHeard;
        public uint EventAck;
        public uint SnapshotAck;
        public uint EchoTime;
        public float RttMs;
        public int PropCursor;
        public uint LastQueuedSeq;
        public uint AppliedSeq;
        public readonly PlayerInput[] Queue = new PlayerInput[16];
        public int QHead, QCount;
        public PlayerInput Last;
        public readonly SentFrame[] Frames = CreateFrames();
        public int FrameHead;
        public double PlayAccum;

        private static SentFrame[] CreateFrames()
        {
            var f = new SentFrame[32];
            for (int i = 0; i < f.Length; i++) f[i] = new SentFrame();
            return f;
        }
    }

    private readonly ContentRegistry _content;
    private readonly ServerConfig _config;
    private readonly UdpTransport _transport;
    private readonly Dictionary<SocketAddress, Client> _clients = [];
    private readonly Client?[] _bySlot = new Client?[SimConstants.MaxPlayers];
    private readonly GameEvent[] _eventScratch = new GameEvent[64];
    private readonly GameEvent[] _statsScratch = new GameEvent[EventBuffer.Capacity];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Lock _gate = new();
    private readonly CookieJar _cookies = new();
    private readonly byte[] _plain = new byte[Protocol.MaxPacketSize];
    private readonly byte[] _sealed = new byte[Protocol.MaxPacketSize + 64];
    private readonly byte[] _opened = new byte[2048];
    private readonly ConcurrentQueue<string> _consoleQueue = new();
    private readonly HashSet<string> _bans = new(StringComparer.Ordinal);
    private Thread? _thread;
    private volatile bool _running;
    private int _rotationIndex;
    private byte _generation;
    private double _nextHeartbeat, _nextStatsSave;
    private SocketAddress? _master;
    private uint _statsCursor;

    public GameWorld World { get; private set; }
    public StatsStore Stats { get; }
    public int Port => _transport.LocalPort;
    public int ClientCount => _clients.Count;
    public bool IsRunning => _running;
    public long BytesSent => _transport.BytesSent;
    public bool PasswordProtected => !string.IsNullOrEmpty(_config.Password);

    public GameServer(ContentRegistry content, ServerConfig config)
    {
        _content = content;
        _config = config;
        _transport = new UdpTransport(config.Port, config.Bind, broadcast: false);
        World = new GameWorld(content, config.Match, (ulong)Environment.TickCount64);
        World.BalanceBots();
        _statsCursor = World.Events.LastId;
        if (!string.IsNullOrWhiteSpace(config.MasterServer)) _master = ResolveMaster(config.MasterServer);
        Stats = new StatsStore(config.DataDirectory is null ? null : Path.Combine(config.DataDirectory, "stats.json"));
        LoadBans();
        Log($"{GameInfo.Name} server v{GameInfo.Version} on UDP {Port} | arena {World.Arena.Id} | mode {config.Match.Mode} | bots {config.Match.BotCount}" +
            (PasswordProtected ? " | password" : "") + " | encrypted");
    }

    private void Log(string msg) => _config.Log?.Invoke(msg);
    private double Now => _clock.Elapsed.TotalSeconds;

    private static SocketAddress? ResolveMaster(string hostPort)
    {
        var parts = hostPort.Split(':');
        int port = parts.Length > 1 && int.TryParse(parts[1], out int p) ? p : Protocol.DefaultMasterPort;
        try
        {
            var addr = Dns.GetHostAddresses(parts[0]).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return addr is null ? null : new IPEndPoint(addr, port).Serialize();
        }
        catch (SocketException) { return null; }
    }

    /// <summary>Runs the server on a background thread.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "DjancoegServer", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Runs the server on the calling thread until cancelled (dedicated server).</summary>
    public void Run(CancellationToken token)
    {
        _running = true;
        using var reg = token.Register(() => _running = false);
        Loop();
    }

    /// <summary>Queues a console command (thread-safe); it runs on the server thread before the next tick.</summary>
    public void EnqueueCommand(string line) => _consoleQueue.Enqueue(line);

    public void Stop()
    {
        _running = false;
        _thread?.Join(2000);
        foreach (var c in _clients.Values.ToList()) SendDisconnect(c, RejectCode.Generic, "Server shutting down");
        FlushPlayTime();
        Stats.Save();
    }

    private void Loop()
    {
        double next = Now;
        while (_running)
        {
            double now = Now;
            if (now < next)
            {
                double wait = next - now;
                if (wait > 0.002) _transport.Poll((int)((wait - 0.001) * 1_000_000));
                ReceiveAll();
                continue;
            }
            lock (_gate) TickOnce();
            next += SimConstants.TickDelta;
            if (Now - next > 0.25) next = Now;
        }
    }

    /// <summary>Processes network input and advances exactly one tick. Public for tests and tools.</summary>
    public void TickOnce()
    {
        while (_consoleQueue.TryDequeue(out var line))
        {
            string output = Execute(line, null);
            if (output.Length > 0) Log(output);
        }
        ReceiveAll();
        ApplyQueuedInputs();
        World.Step();
        TrackStats();
        if (World.Tick % SimConstants.SnapshotEveryTicks == 0) SendSnapshots();
        Housekeeping();
    }

    // ------------------------------------------------------------------ receive

    private void ReceiveAll()
    {
        for (int guard = 0; guard < 512; guard++)
        {
            int n = _transport.Receive(out var from);
            if (n <= 0) break;
            var packet = _transport.ReceiveBuffer.AsSpan(0, n);
            var r = new NetReader(packet);
            var type = Protocol.ReadHeader(ref r);
            if (type == 0) continue;
            _clients.TryGetValue(from, out var client);
            switch (type)
            {
                case PacketType.Connect: HandleConnect(ref r, from, client); break;
                case PacketType.Secure when client is not null: HandleSecure(packet, client); break;
                case PacketType.DiscoveryRequest: HandleDiscovery(ref r, from); break;
                case PacketType.PunchNotify when _master is not null && from.Equals(_master): HandlePunchNotify(ref r); break;
            }
        }
    }

    private void HandleSecure(ReadOnlySpan<byte> packet, Client c)
    {
        var aad = packet[..Protocol.HeaderSize];
        if (!c.Channel.Open(packet[Protocol.HeaderSize..], aad, _opened, out int len) || len < 1) return;
        c.LastHeard = Now;
        var r = new NetReader(_opened.AsSpan(1, len - 1));
        switch ((PacketType)_opened[0])
        {
            case PacketType.Input: HandleInput(ref r, c); break;
            case PacketType.Disconnect: Drop(c, "left the game"); break;
            case PacketType.ChangeCharacter:
            {
                int ch = _content.CharacterIndexOf(r.Str());
                if (ch >= 0 && !c.IsSpectator) World.SetCharacter(c.Slot, ch);
                break;
            }
            case PacketType.Chat: HandleChat(ref r, c); break;
            case PacketType.LeaderboardRequest: SendLeaderboard(c); break;
        }
    }

    private void HandleConnect(ref NetReader r, SocketAddress from, Client? existing)
    {
        string name = r.Str();
        string characterId = r.Str();
        uint signature = r.U32();
        bool spectator = (r.U8() & 1) != 0;
        string playerKey = r.Str();
        int keyLen = r.U16();
        Span<byte> clientKey = stackalloc byte[Math.Min(keyLen, 256)];
        for (int i = 0; i < clientKey.Length; i++) clientKey[i] = r.U8();
        int cookieLen = r.U8();
        Span<byte> cookie = stackalloc byte[CookieJar.CookieSize];
        bool hasCookie = cookieLen == CookieJar.CookieSize;
        for (int i = 0; i < cookieLen; i++) { byte b = r.U8(); if (hasCookie) cookie[i] = b; }
        int proofLen = r.U8();
        Span<byte> proof = stackalloc byte[32];
        for (int i = 0; i < proofLen; i++) { byte b = r.U8(); if (i < 32) proof[i] = b; }
        if (r.Error || keyLen == 0 || keyLen > 256) return;

        if (existing is not null) { SendAccept(existing); return; } // duplicate connect (lost accept)

        var ip = UdpTransport.ToEndPoint(from).Address.ToString();
        if (_bans.Contains(ip)) { SendReject(from, RejectCode.Banned, "You are banned from this server."); return; }

        // Step 1: stateless cookie challenge (blocks spoofed addresses; the reply is smaller than the request).
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var addrBytes = from.Buffer.Span[..from.Size];
        if (!hasCookie || !_cookies.Verify(addrBytes, cookie, nowUnix))
        {
            Span<byte> fresh = stackalloc byte[CookieJar.CookieSize];
            _cookies.Issue(addrBytes, nowUnix, fresh);
            var cw = new NetWriter(_transport.SendBuffer);
            Protocol.WriteHeader(ref cw, PacketType.Challenge);
            cw.U8(CookieJar.CookieSize);
            foreach (byte b in fresh) cw.U8(b);
            cw.Bool(PasswordProtected);
            _transport.Send(cw.Written, from);
            return;
        }

        // Step 2: password proof.
        if (PasswordProtected)
        {
            var expected = CookieJar.PasswordProof(_config.Password!, cookie);
            if (proofLen != 32 || !CryptographicOperations.FixedTimeEquals(expected, proof))
            {
                SendReject(from, RejectCode.Password, "Wrong or missing password.");
                return;
            }
        }

        if (signature != _content.Signature)
        {
            SendReject(from, RejectCode.Content, $"Content mismatch: the server runs mods [{string.Join(", ", _content.LoadedMods)}]. Enable the same mods to join.");
            return;
        }

        // Step 3: key exchange.
        var keys = new KeyExchange();
        var channel = keys.Derive(clientKey, cookie, isServer: true);
        if (channel is null) { keys.Dispose(); return; }

        bool loopback = IPAddress.IsLoopback(UdpTransport.ToEndPoint(from).Address);
        Client c;
        if (spectator)
        {
            if (_clients.Values.Count(x => x.IsSpectator) >= Protocol.MaxSpectators) { SendReject(from, RejectCode.Full, "Spectator seats are full."); keys.Dispose(); channel.Dispose(); return; }
            c = new Client { Address = Clone(from), Channel = channel, Keys = keys, Slot = -1, Name = string.IsNullOrWhiteSpace(name) ? "Penonton" : name.Trim(), LastHeard = Now, EventAck = World.Events.LastId };
            Log($"+ {c.Name} is watching from {ip}");
        }
        else
        {
            int ch = Math.Max(0, _content.CharacterIndexOf(characterId));
            uint eventAck = World.Events.LastId;
            int slot = World.AddPlayer(name, ch, isBot: false);
            if (slot < 0)
            {
                for (int i = World.Players.Length - 1; i >= 0 && slot < 0; i--)
                    if (World.Players[i].Active && World.Players[i].IsBot) { World.RemovePlayer(i); slot = World.AddPlayer(name, ch, false); }
            }
            if (slot < 0) { SendReject(from, RejectCode.Full, "Server is full."); keys.Dispose(); channel.Dispose(); return; }
            c = new Client { Address = Clone(from), Channel = channel, Keys = keys, Slot = slot, Name = World.Players[slot].Name, LastHeard = Now, EventAck = eventAck };
            _bySlot[slot] = c;
            Log($"+ {c.Name} joined from {ip} (slot {slot})");
        }
        c.PlayerKey = playerKey.Length is > 0 and <= 64 ? playerKey : "";
        c.IsAdmin = loopback && _config.TrustLoopbackAdmin;
        if (!c.IsSpectator) Stats.Touch(c.PlayerKey, c.Name);
        _clients[c.Address] = c;
        World.BalanceBots();
        SendAccept(c);
        BroadcastPlayerInfo();
    }

    private static SocketAddress Clone(SocketAddress a)
    {
        var copy = new SocketAddress(a.Family, a.Size);
        a.Buffer.Span[..a.Size].CopyTo(copy.Buffer.Span);
        return copy;
    }

    private void SendAccept(Client c)
    {
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Accept);
        w.U8(c.IsSpectator ? Protocol.SpectatorSlot : (byte)c.Slot);
        w.Str(World.Arena.Id);
        w.U8((byte)World.Settings.Mode);
        w.U16((ushort)World.Settings.ScoreLimit);
        w.U16((ushort)World.Settings.TimeLimitSeconds);
        w.U32(_content.Signature);
        w.Str(World.Settings.ServerName);
        w.U32(World.Tick);
        w.Bool(c.IsAdmin);
        w.U16((ushort)c.Keys.PublicKey.Length);
        foreach (byte b in c.Keys.PublicKey) w.U8(b);
        w.Str(World.Settings.CampaignStage);
        _transport.Send(w.Written, c.Address);
    }

    private void SendReject(SocketAddress to, RejectCode code, string reason)
    {
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Reject);
        w.U8((byte)code);
        w.Str(reason);
        _transport.Send(w.Written, to);
    }

    /// <summary>Encrypts the inner packet written in <see cref="_plain"/> (type byte + body) and sends it.</summary>
    private void SendSecure(Client c, ReadOnlySpan<byte> inner)
    {
        Span<byte> header = stackalloc byte[Protocol.HeaderSize];
        header[0] = Protocol.Magic0; header[1] = Protocol.Magic1; header[2] = Protocol.Version; header[3] = (byte)PacketType.Secure;
        header.CopyTo(_sealed);
        int n = c.Channel.Seal(inner, header, _sealed.AsSpan(Protocol.HeaderSize));
        _transport.Send(_sealed.AsSpan(0, Protocol.HeaderSize + n), c.Address);
    }

    private NetWriter Inner(PacketType type)
    {
        var w = new NetWriter(_plain);
        w.U8((byte)type);
        return w;
    }

    private void SendDisconnect(Client c, RejectCode code, string reason)
    {
        var w = Inner(PacketType.Disconnect);
        w.U8((byte)code);
        w.Str(reason);
        for (int i = 0; i < 2; i++) SendSecure(c, w.Written);
    }

    private void HandleInput(ref NetReader r, Client c)
    {
        c.EchoTime = r.U32();
        uint ack = r.U32();
        if (ack > c.EventAck && ack <= World.Events.LastId) c.EventAck = ack;
        uint snapAck = r.U32();
        if (snapAck > c.SnapshotAck && snapAck <= World.Tick) c.SnapshotAck = snapAck;
        c.RttMs = r.U16();
        int count = Math.Min((int)r.U8(), Protocol.InputRedundancy);
        Span<PlayerInput> inputs = stackalloc PlayerInput[Protocol.InputRedundancy];
        for (int i = 0; i < count; i++) inputs[i] = Protocol.ReadInput(ref r);
        if (r.Error) return;
        for (int i = count - 1; i >= 0; i--)
        {
            if (inputs[i].Sequence <= c.LastQueuedSeq) continue;
            if (c.QCount == c.Queue.Length) { c.QHead = (c.QHead + 1) % c.Queue.Length; c.QCount--; }
            c.Queue[(c.QHead + c.QCount) % c.Queue.Length] = inputs[i];
            c.QCount++;
            c.LastQueuedSeq = inputs[i].Sequence;
        }
    }

    private void ApplyQueuedInputs()
    {
        foreach (var c in _clients.Values)
        {
            if (c.IsSpectator) { c.QCount = 0; continue; }
            while (c.QCount > 4) { c.QHead = (c.QHead + 1) % c.Queue.Length; c.QCount--; }
            if (c.QCount > 0)
            {
                c.Last = c.Queue[c.QHead];
                c.QHead = (c.QHead + 1) % c.Queue.Length;
                c.QCount--;
                c.AppliedSeq = c.Last.Sequence;
            }
            World.SetInput(c.Slot, c.Last);
            World.Players[c.Slot].LagCompensationTicks = (int)MathF.Round((c.RttMs * 0.5f / 1000f + Protocol.InterpolationDelay) / SimConstants.TickDelta);
        }
    }

    private void HandleChat(ref NetReader r, Client c)
    {
        string text = r.Str().Trim();
        if (text.Length == 0) return;
        if (text.Length > 120) text = text[..120];
        if (text[0] == '!') { HandleChatCommand(c, text[1..]); return; }
        Log($"<{c.Name}> {text}");
        var w = Inner(PacketType.Chat);
        w.U8(c.IsSpectator ? Protocol.SpectatorSlot : (byte)c.Slot);
        w.Str(c.IsSpectator ? "[" + c.Name + "] " + text : text);
        foreach (var other in _clients.Values) SendSecure(other, w.Written);
    }

    /// <summary>Sends a chat line from the server to one client, or everyone when <paramref name="to"/> is null.</summary>
    private void ServerSay(string text, Client? to = null)
    {
        var w = Inner(PacketType.Chat);
        w.U8(Protocol.ServerSender);
        w.Str(text.Length > 200 ? text[..200] : text);
        if (to is not null) SendSecure(to, w.Written);
        else foreach (var c in _clients.Values) SendSecure(c, w.Written);
    }

    private void HandleDiscovery(ref NetReader r, SocketAddress from)
    {
        uint nonce = r.U32();
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.DiscoveryResponse);
        w.U32(nonce);
        w.Str(World.Settings.ServerName);
        w.Str(World.Arena.Id);
        w.U8((byte)World.Settings.Mode);
        w.U8((byte)World.ActivePlayerCount);
        w.U8((byte)World.HumanPlayerCount);
        w.U8((byte)World.Settings.MaxPlayers);
        w.U32(_content.Signature);
        w.U16((ushort)Port);
        w.Bool(PasswordProtected);
        _transport.Send(w.Written, from);
    }

    /// <summary>The master tells us a client wants to join through NAT: send a few packets to open our mapping.</summary>
    private void HandlePunchNotify(ref NetReader r)
    {
        uint ip = r.U32();
        int port = r.U16();
        if (r.Error) return;
        var target = new IPEndPoint(new IPAddress(ip), port);
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Punch);
        for (int i = 0; i < 3; i++) _transport.Send(w.Written, target);
        Log($"~ punch-through towards {target}");
    }

    private void Drop(Client c, string reason)
    {
        _clients.Remove(c.Address);
        if (!c.IsSpectator)
        {
            _bySlot[c.Slot] = null;
            World.RemovePlayer(c.Slot);
            Stats.Update(c.PlayerKey, s => s.PlaySeconds += c.PlayAccum);
            c.PlayAccum = 0;
        }
        c.Channel.Dispose();
        c.Keys.Dispose();
        World.BalanceBots();
        Log($"- {c.Name} {reason}");
        BroadcastPlayerInfo();
    }

    // ------------------------------------------------------------------ send

    private void SendSnapshots()
    {
        foreach (var c in _clients.Values) SendSnapshot(c);
    }

    private void SendSnapshot(Client c)
    {
        var world = World;
        var w = Inner(PacketType.Snapshot);
        w.U32(world.Tick);

        // Delta baseline: the newest snapshot this client confirmed, if we still remember it.
        SentFrame? baseline = null;
        if (c.SnapshotAck != 0 && world.Tick - c.SnapshotAck < 60)
            foreach (var f in c.Frames) if (f.Tick == c.SnapshotAck) { baseline = f; break; }
        w.U32(baseline?.Tick ?? 0);

        w.U32(c.AppliedSeq);
        w.U32(c.EchoTime);
        w.U8((byte)world.Phase);
        w.U16((ushort)MathF.Ceiling(world.TimeLeft));
        w.U8((byte)Math.Clamp(MathF.Ceiling(world.PhaseTimer), 0, 255));
        w.I16((short)world.TeamScores[0]);
        w.I16((short)world.TeamScores[1]);
        w.I8((sbyte)world.ZoneHolder);
        w.I8((sbyte)world.Winner);
        w.U8((byte)world.ArenaIndex);
        w.U8(c.IsSpectator ? Protocol.SpectatorSlot : (byte)c.Slot);
        w.U8(_generation);
        for (int t = 0; t < 2; t++)
        {
            ref readonly var f = ref world.Flags[t];
            w.U8(f.Carrier >= 0 ? (byte)1 : f.AtHome ? (byte)0 : (byte)2);
            w.I8((sbyte)f.Carrier);
            w.Pos(f.Position);
        }

        // Players: every active player, each as a field delta against the baseline.
        var frame = c.Frames[c.FrameHead];
        c.FrameHead = (c.FrameHead + 1) % c.Frames.Length;
        frame.Tick = world.Tick;
        frame.Count = 0;
        int countPos = w.Position; w.U8(0);
        byte count = 0;
        for (int i = 0; i < world.Players.Length; i++)
        {
            ref readonly var p = ref world.Players[i];
            if (!p.Active) continue;
            var q = ToNet(world, i, p).Quantized();
            bool has = false;
            NetPlayer b = default;
            if (baseline is not null)
                for (int k = 0; k < baseline.Count; k++) if (baseline.Players[k].Id == q.Id) { b = baseline.Players[k]; has = true; break; }
            q.WriteDelta(ref w, b, has);
            frame.Players[frame.Count++] = q;
            count++;
        }
        w.PatchU8(countPos, count);

        ref readonly var me = ref world.Players[c.IsSpectator ? 0 : c.Slot];
        w.U16(c.IsSpectator ? (ushort)0 : me.OwnedWeapons);
        w.U8(c.IsSpectator ? (byte)0 : (byte)_content.Weapons.Count);
        if (!c.IsSpectator)
            for (int k = 0; k < _content.Weapons.Count; k++) w.U16((ushort)Math.Clamp(me.Ammo[k], 0, ushort.MaxValue));

        ulong mask = 0;
        for (int k = 0; k < world.Pickups.Length; k++) if (world.Pickups[k].Available) mask |= 1UL << k;
        w.U64(mask);
        w.U64((ulong)world.DestroyedMask);
        w.U64((ulong)(world.DestroyedMask >> 64));

        // Arena creeps (always all of them; at most 16 x 12 bytes).
        countPos = w.Position; w.U8(0); count = 0;
        for (int k = 0; k < world.Creeps.Length; k++)
        {
            ref readonly var cr = ref world.Creeps[k];
            if (!cr.Active) continue;
            var def = world.CreepDefOf(cr);
            w.U8((byte)k); w.U8(cr.Kind); w.U8((byte)cr.State); w.Pos(cr.Move.Position); w.Angle(cr.Yaw);
            w.U8((byte)Math.Clamp(cr.Health / MathF.Max(1f, cr.MaxHealth > 0 ? cr.MaxHealth : def.Health) * 255f, 0, 255));
            count++;
        }
        w.PatchU8(countPos, count);

        int evN = world.Events.ReadSince(c.EventAck, _eventScratch);
        countPos = w.Position; w.U8(0); count = 0;
        for (int k = 0; k < evN && w.Remaining > Protocol.EventSize + 200; k++)
        {
            Protocol.WriteEvent(ref w, _eventScratch[k]);
            count++;
        }
        w.PatchU8(countPos, count);

        countPos = w.Position; w.U8(0); count = 0;
        var eye = c.IsSpectator ? world.Arena.ZoneCenterV : me.Move.Position;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 0; k < world.Projectiles.Length && w.Remaining > NetProjectile.Size + 60; k++)
            {
                ref readonly var pr = ref world.Projectiles[k];
                if (!pr.Active) continue;
                bool near = System.Numerics.Vector3.DistanceSquared(pr.Position, eye) < 40f * 40f;
                if (near != (pass == 0)) continue;
                w.U16(pr.Id); w.U8(pr.Weapon); w.U8(pr.Owner); w.Pos(pr.Position); w.Vel(pr.Velocity);
                count++;
            }
        }
        w.PatchU8(countPos, count);

        countPos = w.Position; w.U8(0); count = 0;
        int nProps = world.Props.Length;
        for (int k = 0; k < nProps && w.Remaining > NetProp.Size; k++)
        {
            ref readonly var pr = ref world.Props[k];
            if (pr.Velocity.LengthSquared() < 0.01f && pr.AngularVelocity.LengthSquared() < 0.01f) continue;
            WriteProp(ref w, k, pr); count++;
        }
        for (int j = 0; j < 6 && nProps > 0 && w.Remaining > NetProp.Size; j++)
        {
            int k = c.PropCursor++ % nProps;
            ref readonly var pr = ref world.Props[k];
            if (pr.Velocity.LengthSquared() >= 0.01f || pr.AngularVelocity.LengthSquared() >= 0.01f) continue;
            WriteProp(ref w, k, pr); count++;
        }
        w.PatchU8(countPos, count);

        if (!w.Overflow) SendSecure(c, w.Written);
        LastSnapshotBytes = w.Position + Protocol.HeaderSize + SecureChannel.Overhead;
    }

    /// <summary>Size of the last snapshot datagram (diagnostics and tests).</summary>
    public int LastSnapshotBytes { get; private set; }

    private static void WriteProp(ref NetWriter w, int index, in Prop pr)
    {
        w.U8((byte)index);
        w.Pos(pr.Position);
        w.I16(Quant.Pitch(VectorWrap(pr.Rotation.X))); w.I16(Quant.Pitch(VectorWrap(pr.Rotation.Y))); w.I16(Quant.Pitch(VectorWrap(pr.Rotation.Z)));
    }

    private static float VectorWrap(float a) => Core.Geometry.VectorMath.WrapAngle(a);

    private NetPlayer ToNet(GameWorld world, int i, in Player p)
    {
        var f = NetPlayerFlags.Active;
        if (p.Alive) f |= NetPlayerFlags.Alive;
        if (p.Move.OnGround) f |= NetPlayerFlags.OnGround;
        if (p.Move.Crouched) f |= NetPlayerFlags.Crouched;
        if (world.IsInvisible(p)) f |= NetPlayerFlags.Invisible;
        if (world.IsStone(p)) f |= NetPlayerFlags.Stone;
        if (world.IsHasted(p)) f |= NetPlayerFlags.Hasted;
        if (p.IsBot) f |= NetPlayerFlags.Bot;
        if (p.SpawnProtect > 0) f |= NetPlayerFlags.Protected;
        if (p.DashTimer > 0) f |= NetPlayerFlags.Dashing;
        if (p.QuadTimer > 0) f |= NetPlayerFlags.Quad;
        if (world.IsLevitating(p)) f |= NetPlayerFlags.Levitating;
        var c = _bySlot[i];
        return new NetPlayer
        {
            Id = (byte)i, Flags = f, Character = (byte)p.CharacterIndex, Team = p.Team, Weapon = (byte)p.Weapon,
            Position = p.Move.Position, Velocity = p.Move.Velocity, Yaw = p.Yaw, Pitch = p.Pitch,
            Health = (short)MathF.Ceiling(p.Health), Armor = (byte)Math.Clamp(MathF.Ceiling(p.Armor), 0, 255),
            Score = (short)p.Score, Kills = (short)p.Kills, Deaths = (short)p.Deaths,
            AbilityCooldown = p.AbilityCooldown, AbilityTimer = p.AbilityTimer, QuadTimer = p.QuadTimer,
            PingQuarterMs = (byte)Math.Clamp((c?.RttMs ?? 0) / 4f, 0, 255),
        };
    }

    private void BroadcastPlayerInfo()
    {
        var w = Inner(PacketType.PlayerInfo);
        int countPos = w.Position; w.U8(0);
        byte count = 0;
        for (int i = 0; i < World.Players.Length; i++)
        {
            ref readonly var p = ref World.Players[i];
            if (!p.Active) continue;
            w.U8((byte)i); w.Str(p.Name); w.Bool(p.IsBot);
            count++;
        }
        w.PatchU8(countPos, count);
        foreach (var c in _clients.Values) SendSecure(c, w.Written);
    }

    // ------------------------------------------------------------------ housekeeping

    private void Housekeeping()
    {
        double now = Now;
        Client? stale = null;
        foreach (var c in _clients.Values) if (now - c.LastHeard > Protocol.TimeoutSeconds) { stale = c; break; }
        if (stale is not null) Drop(stale, "timed out");

        if (World.Tick % SimConstants.TickRate == 0)
        {
            World.BalanceBots();
            BroadcastPlayerInfo();
            UpdateVote();
        }

        if (World.Phase == MatchPhase.Ended && World.PhaseTimer <= 0) ChangeMatch(null);

        if (_master is not null && now >= _nextHeartbeat)
        {
            _nextHeartbeat = now + 20;
            var w = new NetWriter(_transport.SendBuffer);
            Protocol.WriteHeader(ref w, PacketType.MasterHeartbeat);
            w.U16((ushort)Port);
            _transport.Send(w.Written, _master);
        }

        if (now >= _nextStatsSave)
        {
            _nextStatsSave = now + 60;
            FlushPlayTime();
            Stats.Save();
        }
    }

    /// <summary>Starts the next match: the given settings (admin/vote), the rotation, or a restart.</summary>
    private void ChangeMatch(MatchSettings? forced)
    {
        var settings = forced ?? World.Settings;
        if (forced is null && _config.ArenaRotation.Count > 0)
        {
            _rotationIndex = (_rotationIndex + 1) % _config.ArenaRotation.Count;
            settings = settings with { ArenaId = _config.ArenaRotation[_rotationIndex] };
        }
        FlushPlayTime();
        Stats.Save();
        if (forced is null && settings.ArenaId == World.Arena.Id)
        {
            World.RestartMatch();
            _statsCursor = World.Events.LastId;
            Log("Match restarted.");
            return;
        }
        var next = new GameWorld(_content, settings, (ulong)Environment.TickCount64);
        foreach (var c in _clients.Values)
        {
            c.EventAck = 0;
            c.SnapshotAck = 0;
            foreach (var f in c.Frames) f.Tick = 0;
            if (c.IsSpectator) continue;
            ref readonly var old = ref World.Players[c.Slot];
            next.AddPlayer(old.Name, old.CharacterIndex, false, c.Slot);
        }
        World = next;
        World.BalanceBots();
        _statsCursor = World.Events.LastId;
        _generation++;
        Log($"Next match on {World.Arena.Id} ({World.Settings.Mode}).");
        foreach (var c in _clients.Values) SendAccept(c);
        BroadcastPlayerInfo();
    }

    /// <summary>Thread-safe helper for UIs (listen server) to read the world.</summary>
    public void WithWorld(Action<GameWorld> action) { lock (_gate) action(World); }

    public void Dispose()
    {
        if (_running) Stop();
        foreach (var c in _clients.Values) { c.Channel.Dispose(); c.Keys.Dispose(); }
        _transport.Dispose();
    }
}
