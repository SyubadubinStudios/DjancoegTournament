using System.Diagnostics;
using System.Net;
using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Net;

public enum ClientState { Disconnected, Connecting, Connected, Rejected, TimedOut }

/// <summary>
/// Network client: connects to a <see cref="GameServer"/>, sends inputs every tick, stores snapshots for
/// interpolation, predicts the local player's movement and reconciles with the server.
/// </summary>
public sealed class GameClient : IDisposable
{
    private const int SnapshotRing = 32;
    private const int InputHistory = 256;

    private readonly ContentRegistry _content;
    private readonly UdpTransport _transport;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private SocketAddress? _server;
    private string _name = "", _characterId = "";
    private bool _spectator;
    private double _nextConnectAttempt, _connectStarted, _lastHeard;

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
    public int LocalId { get; private set; } = -1;
    /// <summary>True when connected as a spectator (no player slot; <see cref="LocalId"/> is 255).</summary>
    public bool IsSpectator => LocalId == Protocol.SpectatorSlot;
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

    /// <summary>Raised when the server switches (or first reports) the arena. Rebuild level visuals here.</summary>
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

    public void Connect(IPEndPoint server, string name, string characterId, bool spectator = false)
    {
        _spectator = spectator;
        ServerEndPoint = server;
        _server = server.Serialize();
        _name = name; _characterId = characterId;
        State = ClientState.Connecting;
        _connectStarted = Now;
        _nextConnectAttempt = 0;
        _ringCount = 0; _ringHead = -1; _lastEventId = 0; _haveGeneration = false; _predictionValid = false;
    }

    public void Disconnect()
    {
        if (_server is not null && State == ClientState.Connected)
        {
            var w = new NetWriter(_transport.SendBuffer);
            Protocol.WriteHeader(ref w, PacketType.Disconnect);
            for (int i = 0; i < 3; i++) _transport.Send(w.Written, _server);
        }
        State = ClientState.Disconnected;
    }

    public void ChangeCharacter(string characterId)
    {
        _characterId = characterId;
        if (_server is null || State != ClientState.Connected) return;
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.ChangeCharacter);
        w.Str(characterId);
        _transport.Send(w.Written, _server);
    }

    public void SendChat(string text)
    {
        if (_server is null || State != ClientState.Connected || string.IsNullOrWhiteSpace(text)) return;
        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Chat);
        w.Str(text);
        _transport.Send(w.Written, _server);
    }

    /// <summary>Drains game events received since the last call (oldest first).</summary>
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

    // ------------------------------------------------------------------ receive

    public void Update()
    {
        if (_server is null) return;
        double now = Now;
        if (State == ClientState.Connecting)
        {
            if (now - _connectStarted > 8) { State = ClientState.TimedOut; RejectReason = "No answer from the server."; return; }
            if (now >= _nextConnectAttempt)
            {
                _nextConnectAttempt = now + 0.5;
                var w = new NetWriter(_transport.SendBuffer);
                Protocol.WriteHeader(ref w, PacketType.Connect);
                w.Str(_name); w.Str(_characterId); w.U32(_content.Signature); w.U8(_spectator ? (byte)1 : (byte)0);
                _transport.Send(w.Written, _server);
            }
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
            var r = new NetReader(_transport.ReceiveBuffer.AsSpan(0, n));
            var type = Protocol.ReadHeader(ref r);
            _lastHeard = now;
            switch (type)
            {
                case PacketType.Accept: HandleAccept(ref r); break;
                case PacketType.Reject:
                    RejectReason = r.Str();
                    State = ClientState.Rejected;
                    break;
                case PacketType.Snapshot when State == ClientState.Connected: HandleSnapshot(ref r, now); break;
                case PacketType.PlayerInfo: HandlePlayerInfo(ref r); break;
                case PacketType.Chat:
                {
                    int from2 = r.U8(); string text = r.Str();
                    if (!r.Error) _chat.Add((from2, text));
                    break;
                }
                case PacketType.Disconnect:
                    RejectReason = r.Str();
                    if (RejectReason.Length == 0) RejectReason = "Disconnected by server.";
                    State = ClientState.Rejected;
                    break;
            }
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
        if (r.Error) return;
        if (sig != _content.Signature) { State = ClientState.Rejected; RejectReason = "Content mismatch with server."; return; }
        LocalId = id; Mode = mode; ScoreLimit = scoreLimit; TimeLimit = timeLimit; ServerName = serverName;
        bool changed = Arena is null || Arena.Id != arenaId;
        if (changed)
        {
            int idx = _content.ArenaIndexOf(arenaId);
            if (idx < 0) { State = ClientState.Rejected; RejectReason = $"Missing arena '{arenaId}'."; return; }
            Arena = _content.Arenas[idx];
            Collision = new CollisionWorld(Arena);
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

    private void HandleSnapshot(ref NetReader r, double now)
    {
        int slot = (_ringHead + 1) % SnapshotRing;
        var s = _ring[slot];
        s.Tick = r.U32();
        if (_ringCount > 0 && s.Tick <= _ring[_ringHead].Tick && _ring[_ringHead].Generation == _generation)
        {
            // Out-of-order or duplicate snapshot - ignore unless the world was replaced.
            var peek = r; // copy to look ahead at generation
            for (int k = 0; k < 4 + 4 + 1 + 2 + 1 + 2 + 2 + 1 + 1 + 1 + 1; k++) peek.U8(); // generation follows LocalId
            if (peek.U8() == _generation) return;
        }
        s.AckInput = r.U32();
        s.EchoTime = r.U32();
        s.Phase = (MatchPhase)r.U8();
        s.TimeLeft = r.U16();
        s.PhaseTimer = r.U8();
        s.TeamScore0 = r.I16(); s.TeamScore1 = r.I16();
        s.ZoneHolder = r.I8(); s.Winner = r.I8();
        s.ArenaIndex = r.U8(); s.LocalId = r.U8(); s.Generation = r.U8();
        for (int t = 0; t < 2; t++) s.Flags[t] = new NetFlag { State = r.U8(), Carrier = r.I8(), Position = r.Pos() };

        s.PlayerCount = Math.Min((int)r.U8(), SimConstants.MaxPlayers);
        for (int i = 0; i < s.PlayerCount; i++) s.Players[i] = NetPlayer.Read(ref r);
        s.OwnedWeapons = r.U16();
        int wc = Math.Min((int)r.U8(), SimConstants.MaxWeapons);
        for (int i = 0; i < wc; i++) s.Ammo[i] = r.U16();
        s.PickupMask = r.U64();
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

        Reconcile(s);
    }

    // ------------------------------------------------------------------ input & prediction

    /// <summary>Sends this tick's input to the server and advances local prediction. Call at the fixed tick rate.</summary>
    public void SendInput(PlayerInput input)
    {
        if (_server is null || State != ClientState.Connected) return;
        input.Sequence = _nextSeq++;
        _inputs[input.Sequence % InputHistory] = input;

        if (_predictionValid && Collision is not null && TryLocal(out var me) && me.Has(NetPlayerFlags.Alive))
            PlayerMovement.Step(ref _predicted, input, ParamsFor(me), Collision, SimConstants.TickDelta);

        var w = new NetWriter(_transport.SendBuffer);
        Protocol.WriteHeader(ref w, PacketType.Input);
        w.U32(NowMs);
        w.U32(_lastEventId);
        w.U16((ushort)Math.Clamp(RttMs, 0, ushort.MaxValue));
        int count = (int)Math.Min(Protocol.InputRedundancy, input.Sequence);
        w.U8((byte)count);
        for (int i = 0; i < count; i++) Protocol.WriteInput(ref w, _inputs[(input.Sequence - (uint)i) % InputHistory]);
        _transport.Send(w.Written, _server);
    }

    private MoveParams ParamsFor(in NetPlayer me)
    {
        var c = _content.Characters[Math.Min(me.Character, _content.Characters.Count - 1)];
        float speed = c.SpeedMultiplier;
        // Hasted covers both the Haste ability and the Rally boost (x1.25) - mirror GameWorld.ParamsFor.
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

    /// <summary>Predicted local feet position, including a decaying visual correction offset.</summary>
    public bool TryGetPredicted(float frameDt, out MoveState state, out Vector3 visualPosition)
    {
        state = _predicted;
        _smoothingOffset *= MathF.Max(0, 1f - frameDt * 12f);
        visualPosition = _predicted.Position + _smoothingOffset;
        return _predictionValid;
    }

    /// <summary>Drops local prediction until the next snapshot (after server-side teleports).</summary>
    public void ResetPrediction() => _predictionValid = false;

    // ------------------------------------------------------------------ interpolation

    /// <summary>Server tick the renderer should display (slightly in the past for smooth interpolation).</summary>
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

    /// <summary>Finds the two snapshots around <paramref name="tick"/> and the blend factor.</summary>
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
        // Don't interpolate across teleports (respawn, jump pads at high speed are fine).
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
        _transport.Dispose();
    }
}
