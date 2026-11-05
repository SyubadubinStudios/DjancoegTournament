using System.Numerics;
using Djancoeg.Client.Audio;
using Djancoeg.Client.Render;
using Djancoeg.Client.Ui;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;
using Djancoeg.Core.Simulation;
using Djancoeg.Net;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Game;

/// <summary>Everything the player sees and hears during a match: world rendering, effects, sounds and HUD.</summary>
public sealed partial class GameView : IDisposable
{
    private readonly App _app;
    private readonly Session _session;
    private readonly Effects _fx = new();
    private ArenaRenderer? _arena;
    private readonly List<GameEvent> _events = new(128);
    private readonly List<(int From, string Text)> _chatIn = [];
    private readonly NetPlayer[] _view = new NetPlayer[SimConstants.MaxPlayers];
    private int _viewCount;
    private Snapshot? _latest;
    private Camera3D _camera;
    private float _time;
    private bool _adoptYaw = true;

    // Per-player animation bookkeeping.
    private readonly float[] _lastShot = new float[SimConstants.MaxPlayers];
    private readonly float[] _deathTime = new float[SimConstants.MaxPlayers];
    private readonly float[] _runPhase = new float[SimConstants.MaxPlayers];
    private readonly float[] _tauntUntil = new float[SimConstants.MaxPlayers];
    private readonly string[] _tauntText = new string[SimConstants.MaxPlayers];
    private readonly bool[] _wasAlive = new bool[SimConstants.MaxPlayers];

    // Props: last known state (snapshots only carry a rotating subset).
    private Vector3[] _propPos = [], _propRot = [], _propTarget = [], _propRotTarget = [];

    private float _kick, _bob, _hitMarker, _damageFlash;
    private int _killerId = -1;
    private float _stepDist;
    private readonly float[] _remoteStep = new float[SimConstants.MaxPlayers];

    // Spectator camera.
    private int _followId = -1;
    private bool _freeFly;
    private Vector3 _freePos = new(0, 12, -20);

    // Optional low-resolution 3D target (Settings > Render scale).
    private RenderTexture2D _target;
    private bool _hasTarget;

    // Subtitle for the last announcer line.
    private string _caption = "";
    private float _captionUntil;
    private int _lastHealth = 100;
    private string _killedBy = "";

    public bool Paused { get; set; }
    public bool ForceScoreboard { get; set; }
    public bool Chatting { get; private set; }
    public Session Session => _session;

    public GameView(App app, Session session)
    {
        _app = app;
        _session = session;
        _session.Client.ArenaChanged += OnArena;
        if (_session.Client.Arena is { } a) OnArena(a);
        Array.Fill(_tauntText, "");
        Array.Fill(_deathTime, -100f);
    }

    private void OnArena(ArenaDef arena)
    {
        _arena?.Dispose();
        _arena = new ArenaRenderer(arena, _app.Shader);
        _app.Shader.SetEnvironment(arena);
        _fx.Clear();
        int n = Math.Min(arena.Props.Count, SimConstants.MaxProps);
        _propPos = new Vector3[n]; _propRot = new Vector3[n]; _propTarget = new Vector3[n]; _propRotTarget = new Vector3[n];
        for (int i = 0; i < n; i++) _propPos[i] = _propTarget[i] = arena.Props[i].PositionV;
        _adoptYaw = true;
        _app.Sounds.Play(Audio.Sfx.Gong, 0.8f);
        Voice("tawuran-dimulai");
        Shout(S.Lang == Core.Localization.Language.Indonesian ? "TAWURAN DIMULAI!" : "LET THE BRAWL BEGIN!", Theme.Kuning);
    }

    // ------------------------------------------------------------------ update

    public void Update(float dt, bool inputAllowed)
    {
        _time += dt;
        bool inputEnabled = inputAllowed && !Paused && !Chatting && IsWindowFocused();
        _session.Update(dt, _app.Settings, inputEnabled);
        var client = _session.Client;
        _latest = client.Latest;
        if (_latest is null || client.Arena is null) return;

        BuildView();
        if (_session.CameraTogglePressed)
        {
            _session.CameraTogglePressed = false;
            if (client.IsSpectator) { _freeFly = !_freeFly; _freePos = _camera.Position; }
            else _session.ThirdPerson = !_session.ThirdPerson;
        }
        if (client.IsSpectator) UpdateSpectator(dt, inputEnabled);
        if (_adoptYaw && _latest.TryGetPlayer(client.LocalId, out var me) && me.Has(NetPlayerFlags.Alive))
        {
            _session.Yaw = me.Yaw; _session.Pitch = 0; _adoptYaw = false;
        }

        _events.Clear();
        client.DrainEvents(_events);
        foreach (var e in _events) HandleEvent(e);
        _chatIn.Clear();
        client.DrainChat(_chatIn);
        foreach (var (from, text) in _chatIn) AddChat(NameOf(from), text, from == Protocol.ServerSender ? Theme.Daun : Theme.Kuning);
        if (inputEnabled && client.Vote is not null)
        {
            if (IsKeyPressed(KeyboardKey.F1)) client.CastVote(true);
            if (IsKeyPressed(KeyboardKey.F2)) client.CastVote(false);
        }

        UpdateProps(dt);
        UpdateCreepViews(dt);
        UpdateCorpses(dt);
        UpdateChunks(dt);
        _fx.World = client.Collision;
        _fx.Blood = _app.Settings.Blood;
        UpdateFootsteps(dt);
        _fx.Density = _app.Settings.Quality switch { 0 => 0.4f, 1 => 0.7f, _ => 1f };
        _fx.Update(dt);
        _kick = MathF.Max(0, _kick - dt * 6f);
        _hitMarker = MathF.Max(0, _hitMarker - dt * 3f);
        _damageFlash = MathF.Max(0, _damageFlash - dt * 1.5f);
        UpdateHud(dt);

        if (_latest.TryGetPlayer(client.LocalId, out var self))
        {
            if (self.Health < _lastHealth && self.Has(NetPlayerFlags.Alive)) _damageFlash = MathF.Min(1, _damageFlash + (_lastHealth - self.Health) / 60f);
            _lastHealth = self.Health;
        }
    }

    private void BuildView()
    {
        var client = _session.Client;
        _viewCount = 0;
        double tick = client.RenderTick;
        if (!client.Bracket(tick, out var a, out var b, out float t)) return;
        for (int i = 0; i < b.PlayerCount; i++)
        {
            var pb = b.Players[i];
            var p = a.TryGetPlayer(pb.Id, out var pa) ? GameClient.Lerp(pa, pb, t) : pb;
            // Health/score/flags always from the newest snapshot.
            if (_latest!.TryGetPlayer(pb.Id, out var newest))
            {
                p.Health = newest.Health; p.Armor = newest.Armor; p.Score = newest.Score; p.Kills = newest.Kills; p.Deaths = newest.Deaths;
                p.Weapon = newest.Weapon; p.Flags = (p.Flags & (NetPlayerFlags.OnGround | NetPlayerFlags.Crouched)) | (newest.Flags & ~(NetPlayerFlags.OnGround | NetPlayerFlags.Crouched));
                p.Character = newest.Character; p.Team = newest.Team; p.PingQuarterMs = newest.PingQuarterMs;
            }
            _view[_viewCount++] = p;
            bool alive = p.Has(NetPlayerFlags.Alive);
            if (!alive && _wasAlive[p.Id]) _deathTime[p.Id] = _time;
            _wasAlive[p.Id] = alive;
        }
    }

    private void UpdateProps(float dt)
    {
        var s = _latest!;
        for (int i = 0; i < s.PropCount; i++)
        {
            var pr = s.Props[i];
            if (pr.Index >= _propPos.Length) continue;
            _propTarget[pr.Index] = pr.Position;
            _propRotTarget[pr.Index] = pr.Rotation;
        }
        float k = MathF.Min(1, dt * 15f);
        for (int i = 0; i < _propPos.Length; i++)
        {
            if (Vector3.DistanceSquared(_propPos[i], _propTarget[i]) > 25f) _propPos[i] = _propTarget[i];
            _propPos[i] = Vector3.Lerp(_propPos[i], _propTarget[i], k);
            _propRot[i] = Vector3.Lerp(_propRot[i], _propRotTarget[i], k);
        }
    }

    private string NameOf(int id) => id == Protocol.ServerSender ? "SERVER" : id == Protocol.SpectatorSlot ? "" : (uint)id < SimConstants.MaxPlayers && _session.Client.Names[id].Length > 0 ? _session.Client.Names[id] : "???";

    private bool TryView(int id, out NetPlayer p)
    {
        for (int i = 0; i < _viewCount; i++) if (_view[i].Id == id) { p = _view[i]; return true; }
        p = default;
        return false;
    }

    private Vector3 ListenerPos => _camera.Position;

    private void HandleEvent(in GameEvent e)
    {
        var sounds = _app.Sounds;
        var content = _session.Content;
        int me = _session.Client.LocalId;
        switch (e.Type)
        {
            case GameEventType.Shot:
            {
                if (e.A < _lastShot.Length) _lastShot[e.A] = _time;
                var w = e.Weapon < content.Weapons.Count ? content.Weapons[e.Weapon] : null;
                var sfx = WeaponSound(w);
                var color = w is null ? Color.White : Theme.Rgba(ColorUtil.ParseRgba(w.Color));
                if (e.A == me)
                {
                    _kick = 1f;
                    sounds.Play(sfx, 0.8f, 0.95f + _fx.Shake * 0.1f);
                    if (e.Flags == 0 && w is { Kind: WeaponKind.Hitscan }) _fx.AddTracer(MuzzleWorld(), e.Vector, color, w.Piercing ? 0.05f : 0.018f, w.Piercing ? 0.35f : 0.1f);
                    _fx.AddFlash(_camera.Position + Forward() * 0.6f, 4f, new Vector3(1.2f, 0.9f, 0.5f), 0.06f);
                }
                else
                {
                    sounds.PlayAt(sfx, e.Position, ListenerPos, _session.Yaw, 0.9f);
                    if (e.Flags == 0 && w is { Kind: WeaponKind.Hitscan }) _fx.AddTracer(e.Position + new Vector3(0, -0.25f, 0), e.Vector, color, w.Piercing ? 0.05f : 0.02f, w.Piercing ? 0.35f : 0.12f);
                }
                if (w is { Kind: WeaponKind.Hitscan } && e.Flags == 0) _fx.Impact(e.Position, e.Vector + Vector3.Normalize(e.Vector - e.Position + new Vector3(0.0001f)) * 0.1f, color);
                if (w is not { Kind: WeaponKind.Melee }) _fx.MuzzleSmoke(e.A == me ? MuzzleWorld() : e.Position, Vector3.Normalize(e.Vector - e.Position + new Vector3(0, 0.0001f, 0)));
                if (e.Flags == 2) WindGust(e.Position, e.Vector);
                break;
            }
            case GameEventType.Hit:
                if (e.B == me && e.A != me) { _hitMarker = 1; bool head = (e.Flags & (byte)KillFlags.Headshot) != 0; sounds.Play(head ? Sfx.HitHead : Sfx.Hit, head ? 0.8f : 0.7f, (head ? 1f : 0.85f + e.Weapon * 0.05f) + MathF.Min(0.4f, e.Value / 250f)); _fx.AddText(e.Position + new Vector3(0, 2.0f, 0), e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), (e.Flags & (byte)KillFlags.Headshot) != 0 ? Theme.Cabe : Theme.Kuning); }
                if (e.A == me) _fx.AddShake(MathF.Min(0.5f, e.Value / 80f));
                {
                    var from = TryView(e.B, out var shooter) ? shooter.Position : ListenerPos;
                    var dirHit = e.Position - from;
                    dirHit = dirHit.LengthSquared() > 0.01f ? Vector3.Normalize(dirHit) : Vector3.UnitY;
                    _fx.BloodHit(e.Position + new Vector3(0, 1.1f, 0), dirHit, new Color(150, 10, 12, 255), MathF.Min(1.5f, e.Value / 30f));
                }
                break;
            case GameEventType.Kill:
                OnKill(e, me);
                if (e.A == me) _app.Achievements.OnLocalDeath();
                else if (e.B == me && ((KillFlags)e.Flags & (KillFlags.Suicide | KillFlags.Creep)) == 0) _app.Achievements.OnLocalKill();
                CorpseForPlayer(e);
                break;
            case GameEventType.BoxDestroyed:
            case GameEventType.BoxRestored:
                OnBoxEvent(e);
                if (e.Type == GameEventType.BoxDestroyed && Vector3.Distance(e.Position, ListenerPos) < 9f) _app.Achievements.OnBoxBroken();
                break;
            case GameEventType.Explosion:
            {
                float radius = e.Value > 0 ? e.Value / 10f : 0.6f;
                bool ability = e.Flags == 1;
                if (ability) _fx.Burst(e.Position, 50, 9f, null, 0.7f, 0.07f);
                else
                {
                    _fx.Explosion(e.Position, MathF.Max(0.8f, radius), e.Weapon < content.Weapons.Count && content.Weapons[e.Weapon].Id.Contains("sambal"));
                    if (radius > 1) _fx.ExplosionExtras(e.Position, radius);
                }
                sounds.PlayAt(radius > 1 ? Sfx.Explosion : Sfx.Thump, e.Position, ListenerPos, _session.Yaw, 1f, ability ? 1.4f : 1f);
                float d = Vector3.Distance(e.Position, ListenerPos);
                _fx.AddShake(MathF.Max(0, 0.8f - d / 25f));
                break;
            }
            case GameEventType.Pickup:
                if (e.A == me) sounds.Play(Sfx.Pickup, 0.8f, e.Value == (short)PickupKind.Quad ? 0.7f : 1f);
                else sounds.PlayAt(Sfx.Pickup, e.Position, ListenerPos, _session.Yaw, 0.5f);
                _fx.Burst(e.Position, 10, 2.5f, Theme.Kuning, 0.6f, 0.04f);
                if (e.A == me && e.Value == (short)PickupKind.Quad) Shout(S.T("hud.quad").ToUpperInvariant(), Theme.Cabe);
                break;
            case GameEventType.Jump:
                if (e.A == me) sounds.Play(Sfx.Jump, 0.35f); else sounds.PlayAt(Sfx.Jump, e.Position, ListenerPos, _session.Yaw, 0.3f);
                break;
            case GameEventType.JumpPad:
                sounds.PlayAt(Sfx.Pad, e.Position, ListenerPos, _session.Yaw, e.A == me ? 1f : 0.7f);
                _fx.Burst(e.Position, 12, 4f, Theme.Kuning, 0.5f, 0.05f);
                break;
            case GameEventType.Spawn:
                if (e.A == me) { _adoptYaw = true; _killedBy = ""; }
                for (int k = 0; k < 16; k++) _fx.Smoke(e.Position + new Vector3(0, 0.3f + k * 0.1f, 0), 1.2f);
                break;
            case GameEventType.Ability:
            {
                var kind = (AbilityKind)e.Value;
                var sfx = kind switch { AbilityKind.Teleport => Sfx.Blink, AbilityKind.Pull => Sfx.Whistle, AbilityKind.Rally => Sfx.Drum, _ => Sfx.Ability };
                sounds.PlayAt(sfx, e.Position, ListenerPos, _session.Yaw, 1f);
                _fx.Burst(e.Position + new Vector3(0, 1, 0), 24, 5f, Theme.Kuning, 0.6f, 0.05f);
                if (kind == AbilityKind.Teleport)
                {
                    for (int k = 0; k < 14; k++) _fx.Smoke(e.Vector + new Vector3(0, 0.3f + k * 0.1f, 0), 1.2f);
                    _fx.AddTracer(e.Position + new Vector3(0, 1, 0), e.Vector + new Vector3(0, 1, 0), Theme.Kuning, 0.08f, 0.35f);
                    if (e.A == _session.Client.LocalId) _session.Client.ResetPrediction();
                }
                if (kind == AbilityKind.Rally) _fx.Burst(e.Position + new Vector3(0, 1, 0), 40, 6f, Theme.Daun, 0.8f, 0.06f);
                break;
            }
            case GameEventType.Deflect:
                sounds.PlayAt(Sfx.Bounce, e.Position, ListenerPos, _session.Yaw, 1f, 0.7f);
                for (int k = 0; k < 10; k++) _fx.Spark(e.Position, Theme.Kapur);
                if (e.A == me) Shout(S.T("hud.deflect"), Theme.Kuning);
                break;
            case GameEventType.FlagTaken:
            case GameEventType.FlagDropped:
            case GameEventType.FlagReturned:
            case GameEventType.FlagCaptured:
                OnFlagEvent(e, me);
                break;
            case GameEventType.RaceGoal:
                sounds.Play(Sfx.Gong, e.A == me ? 0.9f : 0.5f, 1.3f);
                _fx.Burst(e.Position + new Vector3(0, 1, 0), 60, 8f, null, 1.4f, 0.07f);
                AddFeed(S.T("hud.raceGoal").Replace("{0}", NameOf(e.A)), e.A == me ? Theme.Kuning : Theme.Kapur);
                if (e.A == me) { Shout(S.Lang == Core.Localization.Language.Indonesian ? "DAPAT HADIAH!" : "PRIZE GET!", Theme.Kuning); _adoptYaw = true; }
                break;
            case GameEventType.Taunt:
                if (e.A < _tauntUntil.Length && TryCharacter(e.A, out var cd) && cd.Taunts.Length > 0)
                {
                    _tauntUntil[e.A] = _time + 2.8f;
                    _tauntText[e.A] = S.T(cd.Taunts[Math.Clamp(e.Value, 0, cd.Taunts.Length - 1)]);
                }
                sounds.PlayAt(Sfx.Taunt, e.Position, ListenerPos, _session.Yaw, 0.9f);
                break;
            case GameEventType.MatchStart:
                sounds.Play(Sfx.Gong, 0.9f);
                Voice("tawuran-dimulai");
                Shout(S.Lang == Core.Localization.Language.Indonesian ? "TAWURAN DIMULAI!" : "LET THE BRAWL BEGIN!", Theme.Kuning);
                break;
            case GameEventType.MatchEnd:
                sounds.Play(Sfx.Gong, 1f, 0.8f);
                break;
            case GameEventType.ZoneScore:
                if (e.A == me) sounds.Play(Sfx.UiMove, 0.6f, 1.5f);
                break;
            case GameEventType.PropHit:
                sounds.PlayAt(Sfx.PropHit, e.Position, ListenerPos, _session.Yaw, 1f);
                break;
            case GameEventType.Streak:
                if (e.A == me) { Shout(S.StreakShout(e.Value), Theme.Cabe); Voice(e.Value >= 8 ? "sakti" : "ngamuk"); }
                else AddFeed(NameOf(e.A) + " - " + S.StreakShout(e.Value), Theme.Cabe);
                break;
            case GameEventType.ProjectileBounce:
                sounds.PlayAt(Sfx.Bounce, e.Position, ListenerPos, _session.Yaw, 0.6f);
                break;
            case GameEventType.CreepSpawn:
            case GameEventType.CreepAttack:
            case GameEventType.CreepHurt:
            case GameEventType.CreepKilled:
            case GameEventType.CreepRoar:
            case GameEventType.CreepLeave:
            case GameEventType.WormRumble:
                HandleCreepEvent(e, me);
                break;
            case GameEventType.Join:
                if (e.A != me) AddChat("", (S.Lang == Core.Localization.Language.Indonesian ? "{0} ikut tawuran" : "{0} joined the brawl").Replace("{0}", NameOf(e.A)), Theme.Fade(Theme.Kapur, 0.7f));
                break;
            case GameEventType.Leave:
                AddChat("", (S.Lang == Core.Localization.Language.Indonesian ? "{0} kabur" : "{0} ran away").Replace("{0}", NameOf(e.A)), Theme.Fade(Theme.Kapur, 0.7f));
                break;
        }
    }

    private void OnKill(in GameEvent e, int me)
    {
        var content = _session.Content;
        var flags = (KillFlags)e.Flags;
        string killer = NameOf(e.B), victim = NameOf(e.A);
        string line;
        if ((flags & KillFlags.Creep) != 0 && CreepDef(e.Value) is { } cdef) line = S.T(cdef.KillVerb).Replace("{creep}", S.T(cdef.Name));
        else if ((flags & KillFlags.Environment) != 0) line = e.A == e.B ? S.T("hud.envKill") : S.T("hud.envKillBy");
        else if ((flags & KillFlags.Suicide) != 0) line = S.T("hud.selfKill");
        else if ((flags & KillFlags.PropKill) != 0) line = S.T("hud.propKill");
        else if (e.Weapon < content.Weapons.Count) line = S.T(content.Weapons[e.Weapon].KillVerb);
        else line = S.T("hud.abilityKill");
        line = line.Replace("{killer}", killer).Replace("{victim}", victim);
        if ((flags & KillFlags.Headshot) != 0) line += "  [" + S.T("hud.headshot") + "]";
        AddFeed(line, e.B == me ? Theme.Kuning : e.A == me ? Theme.Cabe : Theme.Kapur);

        _fx.Gore(e.Position + new Vector3(0, 1, 0), new Color(140, 8, 10, 255), (flags & KillFlags.Gibbed) != 0);
        if (e.B == me && e.A != me)
        {
            _app.Sounds.Play(Sfx.Kill, 0.9f);
            if ((flags & KillFlags.Headshot) != 0) Shout(S.T("hud.headshot"), Theme.Cabe);
            else Shout(S.KillShouts[(int)(e.Id % (uint)S.KillShouts.Length)], Theme.Kuning);
            if (e.Id % 3 == 0 || (flags & KillFlags.Headshot) != 0) Voice("mantap");
        }
        if (e.A == me)
        {
            _app.Sounds.Play(Sfx.Death, 0.8f);
            _killedBy = e.A == e.B || (flags & KillFlags.Suicide) != 0 ? "" : killer;
            _killerId = _killedBy.Length > 0 ? e.B : -1;
        }
        else _app.Sounds.PlayAt(Sfx.Death, e.Position, ListenerPos, _session.Yaw, 0.4f, 1.3f);
    }

    private static Sfx WeaponSound(WeaponDef? w) => w?.Model switch
    {
        "racket" => Sfx.Zap,
        "slingshot" => Sfx.Slingshot,
        "pan" => Sfx.Shotgun,
        "sandal" => Sfx.Gatling,
        "firecracker" => Sfx.Thump,
        "bazooka" => Sfx.Rocket,
        "bamboo" => Sfx.Spear,
        "fan" => Sfx.Wind,
        "kerupuk" => Sfx.Crunch,
        "cobek" => Sfx.Thump,
        _ => w?.Kind == WeaponKind.Projectile ? Sfx.Thump : Sfx.Slingshot,
    };

    private bool TryCharacter(int id, out CharacterDef def)
    {
        def = null!;
        if (!TryView(id, out var p)) return false;
        var chars = _session.Content.Characters;
        def = chars[Math.Min(p.Character, chars.Count - 1)];
        return true;
    }

    // ------------------------------------------------------------------ camera

    private Vector3 Forward() => VectorMath.Forward(_session.Yaw, _session.Pitch);

    private Vector3 MuzzleWorld()
    {
        var f = Forward();
        var r = VectorMath.FlatRight(_session.Yaw);
        return _camera.Position + f * 0.7f + r * 0.2f - new Vector3(0, 0.18f, 0);
    }

    private bool LocalState(out NetPlayer me, out Vector3 feet, out MoveParams mp)
    {
        var client = _session.Client;
        mp = MoveParams.Default;
        feet = default;
        if (_latest is null || !_latest.TryGetPlayer(client.LocalId, out me)) { me = default; return false; }
        var cd = _session.Content.Characters[Math.Min(me.Character, _session.Content.Characters.Count - 1)];
        mp = new MoveParams(1, 1, cd.Scale);
        feet = client.TryGetPredicted(GetFrameTime(), out _, out var visual) && me.Has(NetPlayerFlags.Alive) ? visual : me.Position;
        return true;
    }

    private void UpdateCamera()
    {
        var settings = _app.Settings;
        float aspect = (float)GetScreenWidth() / Math.Max(1, GetScreenHeight());
        float fovY = 2f * MathF.Atan(MathF.Tan(settings.FieldOfView * MathF.PI / 360f) / aspect) * 180f / MathF.PI;
        _camera.Up = Vector3.UnitY;
        _camera.FovY = Math.Clamp(fovY, 40, 100);
        _camera.Projection = CameraProjection.Perspective;
        if (_session.Client.IsSpectator) { SpectatorCamera(); return; }
        if (!LocalState(out var me, out var feet, out var mp)) { _camera.Position = new Vector3(0, 10, -20); _camera.Target = Vector3.Zero; return; }

        bool alive = me.Has(NetPlayerFlags.Alive);
        var eye = feet + new Vector3(0, mp.EyeHeight(me.Has(NetPlayerFlags.Crouched)), 0);
        var fwd = Forward();
        float speed = VectorMath.Horizontal(me.Velocity).Length();
        if (me.Has(NetPlayerFlags.OnGround)) _bob += GetFrameTime() * speed * 1.4f;
        var shake = _fx.Shake > 0 && !settings.ReduceShake ? new Vector3(MathF.Sin(_time * 63f), MathF.Sin(_time * 71f), MathF.Cos(_time * 57f)) * _fx.Shake * 0.08f : Vector3.Zero;

        if (!alive && _killerId >= 0 && TryView(_killerId, out var killer) && killer.Has(NetPlayerFlags.Alive))
        {
            // Kill cam: from just above your body, looking at whoever got you.
            float since = _time - _deathTime[me.Id];
            var target = killer.Position + new Vector3(0, 1.2f, 0);
            var from = me.Position + new Vector3(0, 2.4f, 0);
            var dir = target - from;
            float dist = dir.Length();
            float push = MathF.Min(1, since / 1.5f) * MathF.Max(0, dist - 5f) * 0.45f;
            var desired = from + (dist > 0.01f ? dir / dist : Vector3.UnitZ) * push;
            _camera.Position = ClipCamera(from, desired);
            _camera.Target = target;
            return;
        }
        if (!alive)
        {
            float a = (_time - _deathTime[me.Id]) * 0.4f;
            var target = me.Position + new Vector3(0, 0.6f, 0);
            var desired = target + new Vector3(MathF.Sin(a) * 4.5f, 3.2f, MathF.Cos(a) * 4.5f);
            _camera.Position = ClipCamera(target, desired);
            _camera.Target = target;
            return;
        }

        if (_session.ThirdPerson)
        {
            var right = VectorMath.FlatRight(_session.Yaw);
            var pivot = eye + new Vector3(0, 0.25f, 0);
            var desired = pivot - fwd * 3.4f + right * 0.7f;
            _camera.Position = ClipCamera(pivot, desired) + shake;
            _camera.Target = pivot + fwd * 20f + right * 0.7f;
        }
        else
        {
            float bobY = MathF.Sin(_bob * 2f) * 0.035f * MathF.Min(1, speed / 8f);
            _camera.Position = eye + new Vector3(0, bobY, 0) + shake;
            _camera.Target = _camera.Position + fwd;
        }
    }

    private Vector3 ClipCamera(Vector3 from, Vector3 to)
    {
        var col = _session.Client.Collision;
        var d = to - from;
        float len = d.Length();
        if (col is null || len < 0.01f) return to;
        var dir = d / len;
        return col.Raycast(from, dir, len, out float hit, out _) ? from + dir * MathF.Max(0.2f, hit - 0.25f) : to;
    }

    // ------------------------------------------------------------------ 3D drawing

    public void Draw()
    {
        var arena = _session.Client.Arena;
        int sw = GetScreenWidth(), sh = GetScreenHeight();
        if (arena is null || _arena is null || _latest is null)
        {
            ClearBackground(Theme.TarpDeep);
            string text = _session.Client.State == ClientState.Connecting
                ? S.T("common.connecting").Replace("{0}", _session.Endpoint)
                : _session.Client.State is ClientState.Rejected or ClientState.TimedOut ? _session.Client.RejectReason : S.T("common.loading");
            Theme.TextCentered(Theme.BodyBold, text, new Vector2(sw / 2f, sh / 2f), 30, Theme.Kapur);
            return;
        }

        UpdateCamera();
        float scale = Math.Clamp(_app.Settings.RenderScale, 0.4f, 1f);
        bool lowRes = scale < 0.99f;
        if (lowRes)
        {
            int tw = Math.Max(64, (int)(sw * scale)), th = Math.Max(36, (int)(sh * scale));
            if (!_hasTarget || _target.Texture.Width != tw || _target.Texture.Height != th)
            {
                if (_hasTarget) UnloadRenderTexture(_target);
                _target = LoadRenderTexture(tw, th);
                SetTextureFilter(_target.Texture, TextureFilter.Bilinear);
                _hasTarget = true;
            }
            BeginTextureMode(_target);
            sw = tw; sh = th;
        }
        var top = Theme.Rgba(ColorUtil.ParseRgba(arena.SkyTop));
        var bottom = Theme.Rgba(ColorUtil.ParseRgba(arena.SkyBottom));
        ClearBackground(bottom);
        DrawRectangleGradientV(0, 0, sw, sh, top, bottom);

        var shader = _app.Shader;
        shader.SetFrame(_camera.Position, _time);
        shader.BeginLights();
        PushArenaLights(arena);
        _fx.PushLights(shader, _app.Settings.ReduceFlashes ? 0.4f : 1f, _app.Settings.Quality == 0 ? 2 : 8);
        // The jamu (quad) holder glows red.
        for (int i = 0; i < _viewCount; i++)
            if (_view[i].Has(NetPlayerFlags.Quad) && _view[i].Has(NetPlayerFlags.Alive)) shader.AddLight(_view[i].Position + new Vector3(0, 1.2f, 0), 4f, new Vector3(1.2f, 0.2f, 0.1f));
        shader.CommitLights();
        shader.SetEmissive(0);

        BeginMode3D(_camera);
        _app.Sky.Draw(arena, _camera, _time);
        BeginShaderMode(shader.Shader);
        _arena.Draw(shader);
        DrawPadsAndZone(arena);
        DrawObjectives(arena);
        DrawPickups(arena);
        DrawProps(arena);
        if (_app.Settings.BlobShadows) DrawBlobShadows();
        DrawDestructibles(arena);
        DrawPlayers();
        DrawCorpses();
        DrawChunks();
        DrawCreeps();
        DrawProjectiles();
        shader.SetEmissive(0.7f);
        _fx.Draw(_camera.Position);
        shader.SetEmissive(0);
        EndShaderMode();
        if (!_session.ThirdPerson && !_session.Client.IsSpectator) DrawViewModel();
        EndMode3D();

        if (lowRes)
        {
            EndTextureMode();
            var tex = _target.Texture;
            DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, -tex.Height), new Rectangle(0, 0, GetScreenWidth(), GetScreenHeight()), Vector2.Zero, 0, Color.White);
        }

        DrawHud();
    }

    private readonly (float d, int i)[] _lightSort = new (float, int)[256];

    private void PushArenaLights(ArenaDef arena)
    {
        int n = Math.Min(arena.Lights.Count, _lightSort.Length);
        for (int i = 0; i < n; i++) _lightSort[i] = (Vector3.DistanceSquared(arena.Lights[i].PositionV, _camera.Position), i);
        var span = _lightSort.AsSpan(0, n);
        span.Sort(static (a, b) => a.d.CompareTo(b.d));
        int maxArena = _app.Settings.Quality switch { 0 => 3, 1 => 6, _ => 10 };
        for (int k = 0; k < Math.Min(n, maxArena); k++)
        {
            var l = arena.Lights[span[k].i];
            float flicker = 1f + 0.05f * MathF.Sin(_time * 7f + span[k].i);
            _app.Shader.AddLight(l.PositionV, l.Radius, ColorUtil.ToVector3(ColorUtil.ParseRgba(l.Color)) * flicker);
        }
    }

    private void DrawPadsAndZone(ArenaDef arena)
    {
        foreach (var pad in arena.JumpPads) Primitives.JumpPad(pad.MinV, pad.MaxV, _time);
        if (_session.Client.Mode == GameModeKind.KingOfTheHill)
        {
            int holder = _latest!.ZoneHolder;
            var color = holder < 0 ? Theme.Kapur : holder == _session.Client.LocalId ? Theme.Kuning : Theme.Cabe;
            Primitives.Zone(arena.ZoneCenterV, arena.ZoneRadius, _time, color);
        }
    }

    private void DrawPickups(ArenaDef arena)
    {
        ulong mask = _latest!.PickupMask;
        var weapons = _session.Content.Weapons;
        for (int i = 0; i < Math.Min(arena.Pickups.Count, 64); i++)
        {
            if ((mask & (1UL << i)) == 0) continue;
            var p = arena.Pickups[i];
            string? model = null; Color tint = Color.White;
            if (p.Kind == PickupKind.Weapon && p.Weapon is not null)
            {
                int w = _session.Content.WeaponIndexOf(p.Weapon);
                if (w >= 0) { model = weapons[w].Model; tint = Theme.Rgba(ColorUtil.ParseRgba(weapons[w].Color)); }
            }
            DrawCylinderEx(p.PositionV + new Vector3(0, 0.01f, 0), p.PositionV + new Vector3(0, 0.05f, 0), 0.45f, 0.45f, 12, Theme.Fade(Theme.Aspal, 0.6f));
            Primitives.Pickup(p.Kind, p.PositionV, _time + i, model, tint);
        }
    }

    private void DrawProps(ArenaDef arena)
    {
        for (int i = 0; i < _propPos.Length; i++)
        {
            var def = arena.Props[i];
            var half = def.HalfExtentsV;
            var rot = _propRot[i];
            var color = Theme.Rgba(ColorUtil.ParseRgba(def.Color));
            var model = _app.Models.Prop(def.Kind);
            Rlgl.PushMatrix();
            Rlgl.Translatef(_propPos[i].X, _propPos[i].Y, _propPos[i].Z);
            Rlgl.Rotatef(rot.Y * 57.2958f, 0, 1, 0);
            Rlgl.Rotatef(rot.X * 57.2958f, 1, 0, 0);
            Rlgl.Rotatef(rot.Z * 57.2958f, 0, 0, 1);
            if (model is { } m)
            {
                var bb = ModelLibrary.Bounds(m);
                var size = bb.Max - bb.Min;
                var center = (bb.Max + bb.Min) * 0.5f;
                bool swap = (size.X > size.Z) != (half.X > half.Z);
                if (swap) Rlgl.Rotatef(90, 0, 1, 0);
                var fit = swap ? new Vector3(half.Z, half.Y, half.X) : half;
                var scale = new Vector3(fit.X * 2 / MathF.Max(0.01f, size.X), fit.Y * 2 / MathF.Max(0.01f, size.Y), fit.Z * 2 / MathF.Max(0.01f, size.Z));
                Rlgl.Scalef(scale.X, scale.Y, scale.Z);
                DrawModel(m, -center, 1f, Color.White);
            }
            else Primitives.Prop(def.Kind, half, color);
            Rlgl.PopMatrix();
        }
    }

    private unsafe void DrawPlayers()
    {
        int me = _session.Client.LocalId;
        float dt = GetFrameTime();
        var chars = _session.Content.Characters;
        var weapons = _session.Content.Weapons;
        for (int i = 0; i < _viewCount; i++)
        {
            var p = _view[i];
            if (p.Id == me && !_session.ThirdPerson && p.Has(NetPlayerFlags.Alive)) continue;
            // Third person squeezed against a wall: hide our own body instead of filling the screen with it.
            if (p.Id == me && _session.ThirdPerson && p.Has(NetPlayerFlags.Alive) && Vector3.Distance(_camera.Position, p.Position + new Vector3(0, 1.4f, 0)) < 1.6f) continue;
            var pos = p.Position;
            if (p.Id == me && p.Has(NetPlayerFlags.Alive) && _session.Client.TryGetPredicted(0, out _, out var visual)) pos = visual;
            var cd = chars[Math.Min(p.Character, chars.Count - 1)];
            bool alive = p.Has(NetPlayerFlags.Alive);
            bool invisible = p.Has(NetPlayerFlags.Invisible);
            if (alive && p.Has(NetPlayerFlags.Protected) && ((int)(_time * 12) & 1) == 0) continue;
            if (!alive && HasCorpse(p.Id)) continue;

            float speed = VectorMath.Horizontal(p.Velocity).Length();
            _runPhase[p.Id] += dt * MathF.Max(0.6f, speed / 6.5f);
            var tint = Color.White;
            if (p.Has(NetPlayerFlags.Stone)) tint = new Color(150, 150, 150, 255);
            if (p.Has(NetPlayerFlags.Quad)) tint = new Color((byte)255, (byte)(140 + 60 * MathF.Sin(_time * 10)), (byte)120, (byte)255);
            if (invisible) tint.A = 28;
            float yawDeg = p.Yaw * 180f / MathF.PI;

            var cm = _app.Models.Character(cd);
            if (cm is not null && cm.HasAnimations)
            {
                int clip; float frame;
                if (!alive) { clip = cm.Die; frame = MathF.Min(cm.FrameCount(clip) - 1, (_time - _deathTime[p.Id]) * 30f); }
                else if (_time - _lastShot[p.Id] < 0.22f && cm.Shoot >= 0) { clip = cm.Shoot; frame = (_time - _lastShot[p.Id]) * 40f; }
                else if (speed > 1.2f || !p.Has(NetPlayerFlags.OnGround)) { clip = cm.Run >= 0 ? cm.Run : cm.Idle; frame = _runPhase[p.Id] * 30f; }
                else { clip = cm.Idle; frame = _time * 24f + p.Id * 7; }
                if (clip < 0) clip = 0;
                int fc = Math.Max(1, cm.FrameCount(clip));
                int f = alive ? ((int)frame % fc + fc) % fc : Math.Clamp((int)frame, 0, fc - 1);
                UpdateModelAnimation(cm.Model, cm.Animations[clip], f);
                DrawModelEx(cm.Model, pos, Vector3.UnitY, yawDeg, new Vector3(cd.Scale), tint);
            }
            else if (cm is not null)
            {
                DrawModelEx(cm.Model, pos, Vector3.UnitY, yawDeg, new Vector3(cd.Scale), tint);
            }
            else
            {
                var c = Theme.Rgba(ColorUtil.ParseRgba(cd.PrimaryColor));
                c.A = tint.A;
                float h = SimConstants.PlayerHeight * cd.Scale;
                if (alive) DrawCapsule(pos + new Vector3(0, 0.35f, 0), pos + new Vector3(0, h - 0.35f, 0), 0.35f * cd.Scale, 6, 6, c);
                else DrawCapsule(pos + new Vector3(-0.5f, 0.3f, 0), pos + new Vector3(0.5f, 0.3f, 0), 0.3f, 6, 6, c);
            }

            if (alive && !invisible && p.Weapon < weapons.Count)
            {
                var w = weapons[p.Weapon];
                var fwd = VectorMath.FlatForward(p.Yaw);
                var right = VectorMath.FlatRight(p.Yaw);
                var hand = pos + new Vector3(0, 1.22f * cd.Scale, 0) + fwd * 0.5f * cd.Scale + right * 0.12f * cd.Scale;
                Primitives.Push(hand, p.Yaw, p.Pitch, 0, 0.9f * cd.Scale);
                Primitives.Weapon(w.Model, Theme.Rgba(ColorUtil.ParseRgba(w.Color)), _time * 20);
                Primitives.Pop();
            }
        }
    }

    private void DrawProjectiles()
    {
        var s = _latest!;
        var weapons = _session.Content.Weapons;
        float age = (float)(_session.Client.RenderTick - s.Tick) / SimConstants.TickRate;
        for (int i = 0; i < s.ProjectileCount; i++)
        {
            var pr = s.Projectiles[i];
            if (pr.Weapon >= weapons.Count) continue;
            var w = weapons[pr.Weapon];
            // Extrapolate from the newest snapshot so projectiles move smoothly.
            float tAhead = Math.Clamp(age + Protocol.InterpolationDelay, 0, 0.2f);
            var pos = pr.Position + pr.Velocity * tAhead - new Vector3(0, 0.5f * w.ProjectileGravity * tAhead * tAhead, 0);
            var tint = Theme.Rgba(ColorUtil.ParseRgba(w.Color));
            Primitives.Projectile(w.Model, pos, pr.Velocity, _time + pr.Id, tint);
            if (w.Model == "bazooka" && ((int)(_time * 60) + pr.Id) % 2 == 0) _fx.Smoke(pos - Vector3.Normalize(pr.Velocity + new Vector3(0, 0.001f, 0)) * 0.3f, 1.2f);
            if (w.Model == "firecracker" && ((int)(_time * 60) + pr.Id) % 3 == 0) _fx.Spark(pos + new Vector3(0, 0.12f, 0), Theme.Kuning);
        }
    }

    private void DrawViewModel()
    {
        if (_latest is null || !_latest.TryGetPlayer(_session.Client.LocalId, out var me) || !me.Has(NetPlayerFlags.Alive)) return;
        var weapons = _session.Content.Weapons;
        if (me.Weapon >= weapons.Count) return;
        var w = weapons[me.Weapon];
        var fwd = Forward();
        var right = VectorMath.FlatRight(_session.Yaw);
        var up = Vector3.Cross(right, fwd);
        float speed = VectorMath.Horizontal(me.Velocity).Length();
        float bx = MathF.Cos(_bob) * 0.012f * MathF.Min(1, speed / 8f);
        float by = MathF.Abs(MathF.Sin(_bob)) * 0.012f * MathF.Min(1, speed / 8f);
        var pos = _camera.Position + fwd * (0.42f - _kick * 0.08f) + right * (0.24f + bx) + up * (-0.24f + by + _kick * 0.02f);
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();
        BeginShaderMode(_app.Shader.Shader);
        Primitives.Push(pos, _session.Yaw, _session.Pitch + _kick * 0.12f, 0, 0.6f);
        Primitives.Weapon(w.Model, Theme.Rgba(ColorUtil.ParseRgba(w.Color)), _time * (me.Has(NetPlayerFlags.Alive) && _time - _lastShot[me.Id] < 0.3f ? 30 : 4));
        Primitives.Pop();
        EndShaderMode();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();
    }

    public void Dispose()
    {
        _session.Client.ArenaChanged -= OnArena;
        _arena?.Dispose();
        if (_hasTarget) UnloadRenderTexture(_target);
    }
}
