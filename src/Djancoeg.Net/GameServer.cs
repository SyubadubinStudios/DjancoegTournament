using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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
    public Action<string>? Log { get; set; }
}

/// <summary>
/// Authoritative game server. Runs the <see cref="GameWorld"/> at 60 Hz, consumes client inputs in order,
/// and sends 30 Hz delta-free snapshots sized to fit one UDP datagram. Used by the dedicated server,
/// "Host game" (listen server) and offline practice (bound to loopback).
/// </summary>
public sealed class GameServer : IDisposable
{
    private sealed class Client
    {
        public required SocketAddress Address;
        public int Slot; // -1 for spectators
        public bool IsSpectator => Slot < 0;
        public string Name = "";
        public double LastHeard;
        public uint EventAck;
        public uint EchoTime;
        public float RttMs;
        public double LastInfo;
        public int PropCursor;
        public uint LastQueuedSeq;
        public uint AppliedSeq;
        public readonly PlayerInput[] Queue = new PlayerInput[16];
        public int QHead, QCount;
        public PlayerInput Last;
    }

    private readonly ContentRegistry _content;
    private readonly ServerConfig _config;
    private readonly UdpTransport _transport;
    private readonly Dictionary<SocketAddress, Client> _clients = [];
    private readonly Client?[] _bySlot = new Client?[SimConstants.MaxPlayers];
    private readonly GameEvent[] _eventScratch = new GameEvent[64];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Lock _gate = new();
    private Thread? _thread;
    private volatile bool _running;
    private int _rotationIndex;
    private byte _generation;
    private double _nextHeartbeat;
    private SocketAddress? _master;

    public GameWorld World { get; private set; }
    public int Port => _transport.LocalPort;
    public int ClientCount => _clients.Count;
    public bool IsRunning => _running;
    public long BytesSent => _transport.BytesSent;

    public GameServer(ContentRegistry content, ServerConfig config)
    {
        _content = content;
        _config = config;
        _transport = new UdpTransport(config.Port, config.Bind, broadcast: false);
        World = new GameWorld(content, config.Match, (ulong)Environment.TickCount64);
        World.BalanceBots();
        if (!string.IsNullOrWhiteSpace(config.MasterServer)) _master = ResolveMaster(config.MasterServer);
        Log($"{GameInfo.Name} server v{GameInfo.Version} on UDP {Port} | arena {World.Arena.Id} | mode {config.Match.Mode} | bots {config.Match.BotCount}");
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

    public void Stop()
    {
        _running = false;
        _thread?.Join(2000);
        // Tell everyone we are leaving.
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Disconnect);
        w.Str("Server shutting down");
        foreach (var c in _clients.Values) _transport.Send(w.Written, c.Address);
    }

    private void Loop()
    {
        double next = Now;
        while (_running)
        {
            double now = Now;
            if (now < next)
            {
                // Sleep while far from the next tick, then wait on the socket for precise wakeups.
                double wait = next - now;
                if (wait > 0.002) _transport.Poll((int)((wait - 0.001) * 1_000_000));
                ReceiveAll();
                continue;
            }
            lock (_gate) TickOnce();
            next += SimConstants.TickDelta;
            if (Now - next > 0.25) next = Now; // hitch: don't spiral trying to catch up
        }
    }

    /// <summary>Processes network input and advances exactly one tick. Public for tests and tools.</summary>
    public void TickOnce()
    {
        ReceiveAll();
        ApplyQueuedInputs();
        World.Step();
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
            var r = new NetReader(_transport.ReceiveBuffer.AsSpan(0, n));
            var type = Protocol.ReadHeader(ref r);
            if (type == 0) continue;
            _clients.TryGetValue(from, out var client);
            switch (type)
            {
                case PacketType.Connect: HandleConnect(ref r, from, client); break;
                case PacketType.Input when client is not null: HandleInput(ref r, client); break;
                case PacketType.Disconnect when client is not null: Drop(client, "left the game"); break;
                case PacketType.ChangeCharacter when client is not null:
                {
                    int ch = _content.CharacterIndexOf(r.Str());
                    if (ch >= 0) World.SetCharacter(client.Slot, ch);
                    break;
                }
                case PacketType.Chat when client is not null: HandleChat(ref r, client); break;
                case PacketType.DiscoveryRequest: HandleDiscovery(ref r, from); break;
            }
        }
    }

    private void HandleConnect(ref NetReader r, SocketAddress from, Client? existing)
    {
        string name = r.Str();
        string characterId = r.Str();
        uint signature = r.U32();
        bool spectator = (r.U8() & 1) != 0;
        if (r.Error) return;

        if (existing is not null) { SendAccept(existing); return; } // duplicate connect (lost accept)

        if (signature != _content.Signature)
        {
            SendReject(from, $"Content mismatch: the server runs mods [{string.Join(", ", _content.LoadedMods)}]. Enable the same mods to join.");
            return;
        }
        if (spectator)
        {
            if (_clients.Values.Count(c => c.IsSpectator) >= Protocol.MaxSpectators) { SendReject(from, "Spectator seats are full."); return; }
            var sc = new Client { Address = Clone(from), Slot = -1, Name = string.IsNullOrWhiteSpace(name) ? "Penonton" : name.Trim(), LastHeard = Now, EventAck = World.Events.LastId };
            _clients[sc.Address] = sc;
            Log($"+ {sc.Name} is watching from {UdpTransport.ToEndPoint(from)}");
            SendAccept(sc);
            BroadcastPlayerInfo();
            return;
        }
        int ch = Math.Max(0, _content.CharacterIndexOf(characterId));
        uint eventAck = World.Events.LastId; // the new client also receives its own join/spawn events
        // Make room by removing a bot if needed.
        int slot = World.AddPlayer(name, ch, isBot: false);
        if (slot < 0)
        {
            for (int i = World.Players.Length - 1; i >= 0 && slot < 0; i--)
                if (World.Players[i].Active && World.Players[i].IsBot) { World.RemovePlayer(i); slot = World.AddPlayer(name, ch, false); }
        }
        if (slot < 0) { SendReject(from, "Server is full."); return; }

        var c = new Client { Address = Clone(from), Slot = slot, Name = World.Players[slot].Name, LastHeard = Now, EventAck = eventAck };
        _clients[c.Address] = c;
        _bySlot[slot] = c;
        World.BalanceBots();
        Log($"+ {c.Name} joined from {UdpTransport.ToEndPoint(from)} (slot {slot})");
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
        _transport.Send(w.Written, c.Address);
    }

    private void SendReject(SocketAddress to, string reason)
    {
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Reject);
        w.Str(reason);
        _transport.Send(w.Written, to);
    }

    private void HandleInput(ref NetReader r, Client c)
    {
        c.LastHeard = Now;
        c.EchoTime = r.U32();
        uint ack = r.U32();
        if (ack > c.EventAck && ack <= World.Events.LastId) c.EventAck = ack;
        c.RttMs = r.U16();
        int count = Math.Min((int)r.U8(), Protocol.InputRedundancy);
        Span<PlayerInput> inputs = stackalloc PlayerInput[Protocol.InputRedundancy];
        for (int i = 0; i < count; i++) inputs[i] = Protocol.ReadInput(ref r);
        if (r.Error) return;
        // Inputs arrive newest-first; enqueue the unseen ones oldest-first.
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
            // Drain a backlog quickly so latency doesn't accumulate.
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
        Log($"<{c.Name}> {text}");
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Chat);
        w.U8(c.IsSpectator ? Protocol.SpectatorSlot : (byte)c.Slot);
        w.Str(c.IsSpectator ? "[" + c.Name + "] " + text : text);
        foreach (var other in _clients.Values) _transport.Send(w.Written, other.Address);
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
        _transport.Send(w.Written, from);
    }

    private void Drop(Client c, string reason)
    {
        _clients.Remove(c.Address);
        if (!c.IsSpectator)
        {
            _bySlot[c.Slot] = null;
            World.RemovePlayer(c.Slot);
        }
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
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Snapshot);
        w.U32(world.Tick);
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

        // Players (always all of them).
        int countPos = w.Position; w.U8(0);
        byte count = 0;
        for (int i = 0; i < world.Players.Length; i++)
        {
            ref readonly var p = ref world.Players[i];
            if (!p.Active) continue;
            ToNet(world, i, p).Write(ref w);
            count++;
        }
        w.PatchU8(countPos, count);

        // Local inventory (empty for spectators).
        ref readonly var me = ref world.Players[c.IsSpectator ? 0 : c.Slot];
        w.U16(c.IsSpectator ? (ushort)0 : me.OwnedWeapons);
        w.U8(c.IsSpectator ? (byte)0 : (byte)_content.Weapons.Count);
        if (!c.IsSpectator)
            for (int k = 0; k < _content.Weapons.Count; k++) w.U16((ushort)Math.Clamp(me.Ammo[k], 0, ushort.MaxValue));

        ulong mask = 0;
        for (int k = 0; k < world.Pickups.Length; k++) if (world.Pickups[k].Available) mask |= 1UL << k;
        w.U64(mask);

        // Events: oldest unacked first, as many as fit (keep room for projectiles/props).
        int evN = world.Events.ReadSince(c.EventAck, _eventScratch);
        countPos = w.Position; w.U8(0); count = 0;
        for (int k = 0; k < evN && w.Remaining > Protocol.EventSize + 200; k++)
        {
            Protocol.WriteEvent(ref w, _eventScratch[k]);
            count++;
        }
        w.PatchU8(countPos, count);

        // Projectiles: nearest to the player get priority when the packet is full.
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

        // Props: moving ones first, then a rotating slice of resting ones.
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

        if (!w.Overflow) _transport.Send(w.Written, c.Address);
    }

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
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.PlayerInfo);
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
        foreach (var c in _clients.Values) { _transport.Send(w.Written, c.Address); c.LastInfo = Now; }
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
        }

        if (World.Phase == MatchPhase.Ended && World.PhaseTimer <= 0) NextMatch();

        if (_master is not null && now >= _nextHeartbeat)
        {
            _nextHeartbeat = now + 20;
            var w = new NetWriter(_transport.SendBuffer);
            Protocol.WriteHeader(ref w, PacketType.MasterHeartbeat);
            w.U16((ushort)Port);
            _transport.Send(w.Written, _master);
        }
    }

    private void NextMatch()
    {
        var settings = World.Settings;
        if (_config.ArenaRotation.Count > 0)
        {
            _rotationIndex = (_rotationIndex + 1) % _config.ArenaRotation.Count;
            settings = settings with { ArenaId = _config.ArenaRotation[_rotationIndex] };
        }
        if (settings.ArenaId == World.Arena.Id)
        {
            World.RestartMatch();
            Log("Match restarted.");
            return;
        }
        var next = new GameWorld(_content, settings, (ulong)Environment.TickCount64);
        foreach (var c in _clients.Values)
        {
            c.EventAck = 0;
            if (c.IsSpectator) continue;
            ref readonly var old = ref World.Players[c.Slot];
            next.AddPlayer(old.Name, old.CharacterIndex, false, c.Slot);
        }
        World = next;
        World.BalanceBots();
        _generation++;
        Log($"Next match on {World.Arena.Id}.");
        foreach (var c in _clients.Values) SendAccept(c);
        BroadcastPlayerInfo();
    }

    /// <summary>Thread-safe helper for UIs (listen server) to read the world.</summary>
    public void WithWorld(Action<GameWorld> action) { lock (_gate) action(World); }

    public void Dispose()
    {
        if (_running) Stop();
        _transport.Dispose();
    }
}
