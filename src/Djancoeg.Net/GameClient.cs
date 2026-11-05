using System.Diagnostics;
using System.Net;
using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Net;

public enum ClientState { Disconnected, Connecting, Connected, Rejected, TimedOut }

/// <summary>Everything needed to join a server.</summary>
public sealed record ConnectOptions
{
    public required IPEndPoint Server { get; init; }
    public string Name { get; init; } = "Pemain";
    public string CharacterId { get; init; } = "";
    public bool Spectator { get; init; }
    public string? Password { get; init; }
    /// <summary>Random per-install key so the server can keep career stats without accounts.</summary>
    public string PlayerKey { get; init; } = "";
    /// <summary>Master server to ask for NAT punch-through (servers found through the online browser).</summary>
    public IPEndPoint? ViaMaster { get; init; }
}

/// <summary>
/// Network client: performs the cookie/password/ECDH handshake, then sends encrypted inputs every tick,
/// decodes delta snapshots for interpolation, predicts the local player's movement and reconciles with the server.
/// </summary>
public sealed class GameClient : IDisposable
{
    private const int SnapshotRing = 32;
    private const int InputHistory = 256;

    private readonly ContentRegistry _content;
    private readonly UdpTransport _transport;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private SocketAddress? _server;
    private ConnectOptions? _options;
    private double _nextConnectAttempt, _connectStarted, _lastHeard, _nextPunch;
    private KeyExchange? _keys;
    private SecureChannel? _channel;
    private byte[]? _cookie;
    private readonly byte[] _plain = new byte[Protocol.MaxPacketSize];
    private readonly byte[] _sealed = new byte[Protocol.MaxPacketSize + 64];
    private readonly byte[] _opened = new byte[2048];

    private readonly Snapshot[] _ring = new Snapshot[SnapshotRing];
    private int _ringHead = -1, _ringCount;
    private double _latestRecvTime;
    private uint _lastEventId;
    private byte _generation;
    private bool _haveGeneration;

    private readonly PlayerInput[] _inputs = new PlayerInput[InputHistory];
    private uint _nextSeq = 1;
    private MoveState _predicted;
    private bool _predictionValid;
    private Vector3 _smoothingOffset;

    private readonly List<GameEvent> _events = new(128);
    private readonly List<(int From, string Text)> _chat = [];

    public ClientState State { get; private set; }
    public string RejectReason { get; private set; } = "";
    public RejectCode RejectCode { get; private set; }
    public int LocalId { get; private set; } = -1;
    public bool IsSpectator => LocalId == Protocol.SpectatorSlot;
    public bool IsAdmin { get; private set; }
    public bool IsEncrypted => _channel is not null;
    public string ServerName { get; private set; } = "";
    public ArenaDef? Arena { get; private set; }
    public CollisionWorld? Collision { get; private set; }
    public GameModeKind Mode { get; private set; }
    public int ScoreLimit { get; private set; }
    public int TimeLimit { get; private set; }
    public float RttMs { get; private set; }
    public IPEndPoint? ServerEndPoint { get; private set; }
    public readonly string[] Names = new string[SimConstants.MaxPlayers];
    public readonly bool[] IsBot = new bool[SimConstants.MaxPlayers];
    public ContentRegistry Content => _content;
    public long BytesReceived => _transport.BytesReceived;
    public long BytesSent => _transport.BytesSent;
    public int LocalPort => _transport.LocalPort;

    /// <summary>Latest leaderboard received from the server (after <see cref="RequestLeaderboard"/>).</summary>
    public IReadOnlyList<LeaderboardEntry> Leaderboard { get; private set; } = [];
    public int LeaderboardVersion { get; private set; }
    /// <summary>Running vote, or null.</summary>
    public VoteInfo? Vote { get; private set; }

    public event Action<ArenaDef>? ArenaChanged;

    public Snapshot? Latest => _ringCount > 0 ? _ring[_ringHead] : null;

    public GameClient(ContentRegistry content)
    {
        _content = content;
        _transport = new UdpTransport(0, IPAddress.Any, broadcast: false);
        for (int i = 0; i < _ring.Length; i++) _ring[i] = new Snapshot();
        Array.Fill(Names, "");
    }

    private double Now => _clock.Elapsed.TotalSeconds;
    private uint NowMs => (uint)_clock.ElapsedMilliseconds;

    public void Connect(IPEndPoint server, string name, string characterId, bool spectator = false) =>
        Connect(new ConnectOptions { Server = server, Name = name, CharacterId = characterId, Spectator = spectator });

    public void Connect(ConnectOptions options)
    {
        _options = options;
        ServerEndPoint = options.Server;
        _server = options.Server.Serialize();
        State = ClientState.Connecting;
        RejectCode = RejectCode.Generic;
        RejectReason = "";
        _connectStarted = Now;
        _nextConnectAttempt = 0;
        _nextPunch = 0;
        _cookie = null;
        _keys?.Dispose(); _keys = new KeyExchange();
        _channel?.Dispose(); _channel = null;
        _ringCount = 0; _ringHead = -1; _lastEventId = 0; _haveGeneration = false; _predictionValid = false;
    }

    public void Disconnect()
    {
        if (_server is not null && State == ClientState.Connected && _channel is not null)
        {
            var w = Inner(PacketType.Disconnect);
            for (int i = 0; i < 3; i++) SendSecure(w.Written);
        }
        State = ClientState.Disconnected;
    }

    private NetWriter Inner(PacketType type)
    {
        var w = new NetWriter(_plain);
        w.U8((byte)type);
        return w;
    }

    private void SendSecure(ReadOnlySpan<byte> inner)
    {
        if (_channel is null || _server is null) return;
        Span<byte> header = stackalloc byte[Protocol.HeaderSize];
        header[0] = Protocol.Magic0; header[1] = Protocol.Magic1; header[2] = Protocol.Version; header[3] = (byte)PacketType.Secure;
        header.CopyTo(_sealed);
        int n = _channel.Seal(inner, header, _sealed.AsSpan(Protocol.HeaderSize));
        _transport.Send(_sealed.AsSpan(0, Protocol.HeaderSize + n), _server);
    }

    public void ChangeCharacter(string characterId)
    {
        if (_options is not null) _options = _options with { CharacterId = characterId };
        if (State != ClientState.Connected) return;
        var w = Inner(PacketType.ChangeCharacter);
        w.Str(characterId);
        SendSecure(w.Written);
    }

    public void SendChat(string text)
    {
        if (State != ClientState.Connected || string.IsNullOrWhiteSpace(text)) return;
        var w = Inner(PacketType.Chat);
        w.Str(text);
        SendSecure(w.Written);
    }

    public void RequestLeaderboard()
    {
        if (State != ClientState.Connected) return;
        var w = Inner(PacketType.LeaderboardRequest);
        SendSecure(w.Written);
    }

    /// <summary>Votes on the running vote (same as typing !yes / !no).</summary>
    public void CastVote(bool yes) => SendChat(yes ? "!yes" : "!no");

    public int DrainEvents(List<GameEvent> into)
    {
        int n = _events.Count;
        into.AddRange(_events);
        _events.Clear();
        return n;
    }

    public int DrainChat(List<(int From, string Text)> into)
    {
        int n = _chat.Count;
        into.AddRange(_chat);
        _chat.Clear();
        return n;
    }

    // ------------------------------------------------------------------ handshake

    private void SendConnect()
    {
        var o = _options!;
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Connect);
        w.Str(o.Name); w.Str(o.CharacterId); w.U32(_content.Signature); w.U8(o.Spectator ? (byte)1 : (byte)0);
        w.Str(o.PlayerKey);
        w.U16((ushort)_keys!.PublicKey.Length);
        foreach (byte b in _keys.PublicKey) w.U8(b);
        if (_cookie is null) w.U8(0);
        else { w.U8((byte)_cookie.Length); foreach (byte b in _cookie) w.U8(b); }
        if (_cookie is not null && !string.IsNullOrEmpty(o.Password))
        {
            var proof = CookieJar.PasswordProof(o.Password, _cookie);
            w.U8((byte)proof.Length);
            foreach (byte b in proof) w.U8(b);
        }
        else w.U8(0);
        _transport.Send(w.Written, _server!);
    }

    private void SendPunchRequest()
    {
        var master = _options?.ViaMaster;
        if (master is null || ServerEndPoint is null) return;
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.PunchRequest);
#pragma warning disable CS0618 // IPv4 address as uint is the wire format
        w.U32((uint)ServerEndPoint.Address.Address);
#pragma warning restore CS0618
        w.U16((ushort)ServerEndPoint.Port);
        _transport.Send(w.Written, master);
    }

    // ------------------------------------------------------------------ receive

    public void Update()
    {
        if (_server is null) return;
        double now = Now;
        if (State == ClientState.Connecting)
        {
            if (now - _connectStarted > 10) { State = ClientState.TimedOut; RejectReason = "No answer from the server."; return; }
            if (now >= _nextPunch && _options?.ViaMaster is not null) { _nextPunch = now + 1; SendPunchRequest(); }
            if (now >= _nextConnectAttempt) { _nextConnectAttempt = now + 0.5; SendConnect(); }
        }
        else if (State == ClientState.Connected && now - _lastHeard > Protocol.TimeoutSeconds)
        {
            State = ClientState.TimedOut;
            RejectReason = "Lost connection to the server.";
        }

        for (int guard = 0; guard < 256; guard++)
        {
            int n = _transport.Receive(out var from);
            if (n <= 0) break;
            if (!from.Equals(_server)) continue;
            var packet = _transport.ReceiveBuffer.AsSpan(0, n);
            var r = new NetReader(packet);
            var type = Protocol.ReadHeader(ref r);
            switch (type)
            {
                case PacketType.Challenge when State == ClientState.Connecting:
                    HandleChallenge(ref r);
                    break;
                case PacketType.Accept: _lastHeard = now; HandleAccept(ref r); break;
                case PacketType.Reject when State == ClientState.Connecting:
                    RejectCode = (RejectCode)r.U8();
                    RejectReason = r.Str();
                    State = ClientState.Rejected;
                    break;
                case PacketType.Secure when _channel is not null:
                    if (_channel.Open(packet[Protocol.HeaderSize..], packet[..Protocol.HeaderSize], _opened, out int len) && len > 0)
                    {
                        _lastHeard = now;
                        HandleInner(len, now);
                    }
                    break;
            }
        }
    }

    private void HandleChallenge(ref NetReader r)
    {
        int len = r.U8();
        var cookie = new byte[len];
        for (int i = 0; i < len; i++) cookie[i] = r.U8();
        bool needsPassword = r.Bool();
        if (r.Error) return;
        if (needsPassword && string.IsNullOrEmpty(_options?.Password))
        {
            State = ClientState.Rejected;
            RejectCode = RejectCode.Password;
            RejectReason = "This server needs a password.";
            return;
        }
        _cookie = cookie;
        SendConnect();
        _nextConnectAttempt = Now + 0.5;
    }

    private void HandleInner(int len, double now)
    {
        var r = new NetReader(_opened.AsSpan(1, len - 1));
        switch ((PacketType)_opened[0])
        {
            case PacketType.Snapshot when State == ClientState.Connected: HandleSnapshot(ref r, now); break;
            case PacketType.PlayerInfo: HandlePlayerInfo(ref r); break;
            case PacketType.Chat:
            {
                int from = r.U8(); string text = r.Str();
                if (!r.Error) _chat.Add((from, text));
                break;
            }
            case PacketType.Disconnect:
                RejectCode = (RejectCode)r.U8();
                RejectReason = r.Str();
                if (RejectReason.Length == 0) RejectReason = "Disconnected by server.";
                State = ClientState.Rejected;
                break;
            case PacketType.LeaderboardResponse: HandleLeaderboard(ref r); break;
            case PacketType.VoteStatus:
                Vote = r.Bool() ? new VoteInfo(r.Str(), r.U8(), r.U8(), r.U8(), r.U8()) : null;
                break;
        }
    }

    private void HandleAccept(ref NetReader r)
    {
        int id = r.U8();
        string arenaId = r.Str();
        var mode = (GameModeKind)r.U8();
        int scoreLimit = r.U16(), timeLimit = r.U16();
        uint sig = r.U32();
        string serverName = r.Str();
        r.U32();
        bool admin = r.Bool();
        int keyLen = r.U16();
        var serverKey = new byte[Math.Min(keyLen, 256)];
        for (int i = 0; i < serverKey.Length; i++) serverKey[i] = r.U8();
        if (r.Error) return;
        if (sig != _content.Signature) { State = ClientState.Rejected; RejectCode = RejectCode.Content; RejectReason = "Content mismatch with server."; return; }

        if (_channel is null)
        {
            if (_cookie is null || _keys is null) return;
            _channel = _keys.Derive(serverKey, _cookie, isServer: false);
            if (_channel is null) { State = ClientState.Rejected; RejectReason = "Key exchange failed."; return; }
        }

        LocalId = id; Mode = mode; ScoreLimit = scoreLimit; TimeLimit = timeLimit; ServerName = serverName; IsAdmin = admin;
        bool changed = Arena is null || Arena.Id != arenaId;
        if (changed)
        {
            int idx = _content.ArenaIndexOf(arenaId);
            if (idx < 0) { State = ClientState.Rejected; RejectReason = $"Missing arena '{arenaId}'."; return; }
            Arena = _content.Arenas[idx];
            Collision = new CollisionWorld(Arena);
            _appliedDestroyed = 0;
            _predictionValid = false;
            ArenaChanged?.Invoke(Arena);
        }
        State = ClientState.Connected;
    }

    private void HandlePlayerInfo(ref NetReader r)
    {
        int n = r.U8();
        for (int i = 0; i < n; i++)
        {
            int id = r.U8(); string name = r.Str(); bool bot = r.Bool();
            if (r.Error || id >= Names.Length) return;
            Names[id] = name; IsBot[id] = bot;
        }
    }

    private void HandleLeaderboard(ref NetReader r)
    {
        int n = r.U8();
        var list = new List<LeaderboardEntry>(n);
        for (int i = 0; i < n; i++)
        {
            var e = new LeaderboardEntry(r.U16(), r.Str(), (int)r.U32(), (int)r.U32(), r.U16(), r.U16(), r.U16(), r.U16(), r.U8(), (int)r.U32(), r.Bool());
            if (r.Error) return;
            list.Add(e);
        }
        Leaderboard = list;
        LeaderboardVersion++;
    }

    private Snapshot? FindSnapshot(uint tick)
    {
        for (int i = 0; i < _ringCount; i++)
        {
            var s = _ring[(_ringHead - i + SnapshotRing) % SnapshotRing];
            if (s.Tick == tick && s.Generation == _generation) return s;
        }
        return null;
    }

    private void HandleSnapshot(ref NetReader r, double now)
    {
        uint tick = r.U32();
        uint baselineTick = r.U32();
        if (_ringCount > 0 && tick <= _ring[_ringHead].Tick && _haveGeneration)
        {
            // Out-of-order or duplicate - ignore unless the world was replaced (generation byte follows).
            var peek = r;
            for (int k = 0; k < 4 + 4 + 1 + 2 + 1 + 2 + 2 + 1 + 1 + 1 + 1; k++) peek.U8();
            if (peek.U8() == _generation) return;
        }
        Snapshot? baseline = null;
        if (baselineTick != 0)
        {
            baseline = FindSnapshot(baselineTick);
            if (baseline is null) return; // can't decode; the server falls back to a full snapshot when acks stop
        }

        int slot = (_ringHead + 1) % SnapshotRing;
        var s = _ring[slot];
        s.Tick = tick;
        s.BaselineTick = baselineTick;
        s.AckInput = r.U32();
        s.EchoTime = r.U32();
        s.Phase = (MatchPhase)r.U8();
        s.TimeLeft = r.U16();
        s.PhaseTimer = r.U8();
        s.TeamScore0 = r.I16(); s.TeamScore1 = r.I16();
        s.ZoneHolder = r.I8(); s.Winner = r.I8();
        s.ArenaIndex = r.U8(); s.LocalId = r.U8(); s.Generation = r.U8();
        for (int t = 0; t < 2; t++) s.Flags[t] = new NetFlag { State = r.U8(), Carrier = r.I8(), Position = r.Pos() };

        if (baseline is not null && s.Generation != baseline.Generation) return;
        s.PlayerCount = Math.Min((int)r.U8(), SimConstants.MaxPlayers);
        for (int i = 0; i < s.PlayerCount; i++)
            s.Players[i] = NetPlayer.ReadDelta(ref r, id => baseline is not null && baseline.TryGetPlayer(id, out var b) ? b : null);
        s.OwnedWeapons = r.U16();
        int wc = Math.Min((int)r.U8(), SimConstants.MaxWeapons);
        for (int i = 0; i < wc; i++) s.Ammo[i] = r.U16();
        s.PickupMask = r.U64();
        s.DestroyedMask = r.U64();
        s.CreepCount = Math.Min((int)r.U8(), s.Creeps.Length);
        for (int i = 0; i < s.CreepCount; i++)
            s.Creeps[i] = new NetCreep { Index = r.U8(), Kind = r.U8(), State = (CreepState)r.U8(), Position = r.Pos(), Yaw = r.Angle(), HealthPct = r.U8() };
        s.EventCount = Math.Min((int)r.U8(), s.Events.Length);
        for (int i = 0; i < s.EventCount; i++) s.Events[i] = Protocol.ReadEvent(ref r);
        s.ProjectileCount = Math.Min((int)r.U8(), s.Projectiles.Length);
        for (int i = 0; i < s.ProjectileCount; i++)
            s.Projectiles[i] = new NetProjectile { Id = r.U16(), Weapon = r.U8(), Owner = r.U8(), Position = r.Pos(), Velocity = r.Vel() };
        s.PropCount = Math.Min((int)r.U8(), s.Props.Length);
        for (int i = 0; i < s.PropCount; i++)
            s.Props[i] = new NetProp { Index = r.U8(), Position = r.Pos(), Rotation = new Vector3(Quant.UnPitch(r.I16()), Quant.UnPitch(r.I16()), Quant.UnPitch(r.I16())) };
        if (r.Error) return;

        if (!_haveGeneration || s.Generation != _generation)
        {
            _generation = s.Generation; _haveGeneration = true;
            _lastEventId = 0; _ringCount = 0; _predictionValid = false;
        }

        _ringHead = slot;
        _ringCount = Math.Min(_ringCount + 1, SnapshotRing);
        _latestRecvTime = now;
        LocalId = s.LocalId;

        uint rtt = NowMs - s.EchoTime;
        if (rtt < 5000) RttMs = RttMs <= 0 ? rtt : RttMs * 0.9f + rtt * 0.1f;

        for (int i = 0; i < s.EventCount; i++)
        {
            if (s.Events[i].Id <= _lastEventId) continue;
            _lastEventId = s.Events[i].Id;
            _events.Add(s.Events[i]);
        }

        ApplyDestruction(s.DestroyedMask);
        Reconcile(s);
    }

    private ulong _appliedDestroyed;

    /// <summary>Mirrors destroyed boxes into the client's collision so prediction walks through broken stalls.</summary>
    private void ApplyDestruction(ulong mask)
    {
        if (mask == _appliedDestroyed || Collision is null || Arena is null) return;
        int d = 0;
        for (int i = 0; i < Arena.Boxes.Count && d < GameWorld.MaxDestructibles; i++)
        {
            if (!Arena.Boxes[i].Solid || Arena.Boxes[i].Health <= 0) continue;
            Collision.SetBoxEnabled(i, (mask & (1UL << d)) == 0);
            d++;
        }
        _appliedDestroyed = mask;
    }

    // ------------------------------------------------------------------ input & prediction

    public void SendInput(PlayerInput input)
    {
        if (_server is null || State != ClientState.Connected) return;
        input.Sequence = _nextSeq++;
        _inputs[input.Sequence % InputHistory] = input;

        if (_predictionValid && Collision is not null && TryLocal(out var me) && me.Has(NetPlayerFlags.Alive))
            PlayerMovement.Step(ref _predicted, input, ParamsFor(me), Collision, SimConstants.TickDelta);

        var w = Inner(PacketType.Input);
        w.U32(NowMs);
        w.U32(_lastEventId);
        w.U32(Latest?.Tick ?? 0);
        w.U16((ushort)Math.Clamp(RttMs, 0, ushort.MaxValue));
        int count = (int)Math.Min(Protocol.InputRedundancy, input.Sequence);
        w.U8((byte)count);
        for (int i = 0; i < count; i++) Protocol.WriteInput(ref w, _inputs[(input.Sequence - (uint)i) % InputHistory]);
        SendSecure(w.Written);
    }

    private MoveParams ParamsFor(in NetPlayer me)
    {
        var c = _content.Characters[Math.Min(me.Character, _content.Characters.Count - 1)];
        float speed = c.SpeedMultiplier;
        if (me.Has(NetPlayerFlags.Hasted)) speed *= c.Ability.Kind == AbilityKind.Haste ? c.Ability.Power : 1.25f;
        if (me.Has(NetPlayerFlags.Stone)) speed *= 0.75f;
        bool lev = me.Has(NetPlayerFlags.Levitating);
        return new MoveParams(speed, c.JumpMultiplier, c.Scale, lev ? 0.2f : 1f, lev);
    }

    private bool TryLocal(out NetPlayer me)
    {
        var s = Latest;
        if (s is null) { me = default; return false; }
        return s.TryGetPlayer(LocalId, out me);
    }

    private void Reconcile(Snapshot s)
    {
        if (Collision is null || !s.TryGetPlayer(s.LocalId, out var me)) return;
        if (!me.Has(NetPlayerFlags.Alive)) { _predictionValid = false; return; }

        var before = _predicted.Position;
        bool had = _predictionValid;
        _predicted = new MoveState
        {
            Position = me.Position, Velocity = me.Velocity, OnGround = me.Has(NetPlayerFlags.OnGround), Crouched = me.Has(NetPlayerFlags.Crouched),
            PadCooldown = _predicted.PadCooldown,
        };
        var mp = ParamsFor(me);
        uint from = s.AckInput + 1, to = _nextSeq - 1;
        if (to >= from && to - from < InputHistory)
            for (uint seq = from; seq <= to; seq++) PlayerMovement.Step(ref _predicted, _inputs[seq % InputHistory], mp, Collision, SimConstants.TickDelta);
        _predictionValid = true;

        if (had)
        {
            var err = before - _predicted.Position;
            _smoothingOffset = err.LengthSquared() > 4f ? Vector3.Zero : _smoothingOffset + err;
        }
    }

    public bool TryGetPredicted(float frameDt, out MoveState state, out Vector3 visualPosition)
    {
        state = _predicted;
        _smoothingOffset *= MathF.Max(0, 1f - frameDt * 12f);
        visualPosition = _predicted.Position + _smoothingOffset;
        return _predictionValid;
    }

    public void ResetPrediction() => _predictionValid = false;

    // ------------------------------------------------------------------ interpolation

    public double RenderTick
    {
        get
        {
            var latest = Latest;
            if (latest is null) return 0;
            double est = latest.Tick + (Now - _latestRecvTime) * SimConstants.TickRate;
            return est - Protocol.InterpolationDelay * SimConstants.TickRate;
        }
    }

    public bool Bracket(double tick, out Snapshot a, out Snapshot b, out float t)
    {
        a = b = null!; t = 0;
        if (_ringCount == 0) return false;
        Snapshot? newer = null, older = null;
        for (int i = 0; i < _ringCount; i++)
        {
            var s = _ring[(_ringHead - i + SnapshotRing) % SnapshotRing];
            if (s.Generation != _generation) break;
            if (s.Tick >= tick) newer = s;
            else { older = s; break; }
        }
        if (older is null && newer is null) return false;
        if (older is null) { a = b = newer!; return true; }
        if (newer is null) { a = b = older; return true; }
        a = older; b = newer;
        t = (float)Math.Clamp((tick - older.Tick) / Math.Max(1, newer.Tick - older.Tick), 0, 1);
        return true;
    }

    public static NetPlayer Lerp(in NetPlayer a, in NetPlayer b, float t)
    {
        var r = b;
        if (Vector3.DistanceSquared(a.Position, b.Position) < 36f)
        {
            r.Position = Vector3.Lerp(a.Position, b.Position, t);
            r.Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t);
            r.Yaw = a.Yaw + Core.Geometry.VectorMath.WrapAngle(b.Yaw - a.Yaw) * t;
            r.Pitch = a.Pitch + (b.Pitch - a.Pitch) * t;
        }
        return r;
    }

    public void Dispose()
    {
        Disconnect();
        _channel?.Dispose();
        _keys?.Dispose();
        _transport.Dispose();
    }
}
