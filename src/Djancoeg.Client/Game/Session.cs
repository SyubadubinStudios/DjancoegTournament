using System.Net;
using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;
using Djancoeg.Core.Simulation;
using Djancoeg.Net;
using Raylib_cs;

namespace Djancoeg.Client.Game;

public enum SessionKind { Practice, Host, Join }

/// <summary>
/// One playing session. Practice and Host run an in-process <see cref="GameServer"/> on a background
/// thread (practice binds to loopback only); every session talks to it through a <see cref="GameClient"/>,
/// so offline, LAN and online play all use exactly the same code path.
/// </summary>
public sealed class Session : IDisposable
{
    public SessionKind Kind { get; }
    public GameClient Client { get; }
    public GameServer? Server { get; }
    public ContentRegistry Content { get; }
    public string Endpoint { get; }

    // View angles are updated every frame from the mouse; inputs are sampled at the fixed tick.
    public float Yaw, Pitch;
    public bool ThirdPerson;
    public bool Autopilot;
    /// <summary>True while the autopilot has an enemy in its sights (used to time screenshots).</summary>
    public bool AutopilotEngaged { get; private set; }
    private float _accumulator;
    private float _wheel;
    private byte _slotPress;
    private InputButtons _latched;

    private Session(SessionKind kind, ContentRegistry content, GameServer? server, IPEndPoint ep, ClientSettings settings)
    {
        Kind = kind;
        Content = content;
        Server = server;
        Endpoint = ep.ToString();
        Client = new GameClient(content);
        Client.Connect(ep, settings.PlayerName, settings.CharacterId);
        ThirdPerson = settings.ThirdPerson;
    }

    public static Session StartLocal(ContentRegistry content, ClientSettings settings, MatchSettings match, bool host, string? master)
    {
        var config = new ServerConfig
        {
            Match = match,
            Port = host ? settings.HostPort : 0,
            Bind = host ? IPAddress.Any : IPAddress.Loopback,
            MasterServer = host ? master : null,
            ArenaRotation = host ? [.. content.Arenas.Select(a => a.Id)] : [],
        };
        var server = new GameServer(content, config);
        server.Start();
        return new Session(host ? SessionKind.Host : SessionKind.Practice, content, server, new IPEndPoint(IPAddress.Loopback, server.Port), settings);
    }

    public static Session Join(ContentRegistry content, ClientSettings settings, IPEndPoint ep) =>
        new(SessionKind.Join, content, null, ep, settings);

    /// <summary>Per-frame update: network receive, mouse look, fixed-rate input sending.</summary>
    public void Update(float dt, ClientSettings settings, bool inputEnabled)
    {
        Client.Update();
        if (inputEnabled && !Autopilot)
        {
            var md = Raylib.GetMouseDelta();
            float sens = 0.0022f * settings.MouseSensitivity;
            Yaw -= md.X * sens;
            Pitch += (settings.InvertY ? md.Y : -md.Y) * sens;
            Pitch = Math.Clamp(Pitch, -1.5f, 1.5f);
            Yaw = VectorMath.WrapAngle(Yaw);
            _wheel += Raylib.GetMouseWheelMove();
            for (int k = 0; k < 9; k++) if (Raylib.IsKeyPressed(KeyboardKey.One + k)) _slotPress = (byte)(k + 1);
            // Latch quick taps so a press between two ticks is never lost.
            if (Raylib.IsKeyPressed(KeyboardKey.Space)) _latched |= InputButtons.Jump;
            if (Raylib.IsKeyPressed(KeyboardKey.Q) || Raylib.IsMouseButtonPressed(MouseButton.Right)) _latched |= InputButtons.Ability;
            if (Raylib.IsKeyPressed(KeyboardKey.T)) _latched |= InputButtons.Taunt;
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) _latched |= InputButtons.Fire;
            if (Raylib.IsKeyPressed(KeyboardKey.V)) ThirdPerson = !ThirdPerson;
        }

        _accumulator += MathF.Min(dt, 0.25f);
        while (_accumulator >= SimConstants.TickDelta)
        {
            _accumulator -= SimConstants.TickDelta;
            var input = inputEnabled ? (Autopilot ? AutopilotInput() : Sample()) : new PlayerInput { Yaw = Yaw, Pitch = Pitch };
            Client.SendInput(input);
        }
    }

    private PlayerInput Sample()
    {
        sbyte fwd = 0, right = 0;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) fwd += 127;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) fwd -= 127;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) right += 127;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) right -= 127;
        var b = _latched;
        _latched = InputButtons.None;
        if (Raylib.IsKeyDown(KeyboardKey.Space)) b |= InputButtons.Jump;
        if (Raylib.IsMouseButtonDown(MouseButton.Left)) b |= InputButtons.Fire;
        if (Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.C)) b |= InputButtons.Crouch;
        if (Raylib.IsKeyDown(KeyboardKey.Q)) b |= InputButtons.Ability;
        if (_wheel > 0.1f) { b |= InputButtons.PrevWeapon; _wheel = 0; }
        else if (_wheel < -0.1f) { b |= InputButtons.NextWeapon; _wheel = 0; }
        var input = new PlayerInput { Yaw = Yaw, Pitch = Pitch, Forward = fwd, Right = right, Buttons = b, WeaponSlot = _slotPress };
        _slotPress = 0;
        return input;
    }

    // ------------------------------------------------------------------ attract-mode autopilot

    private float _autoTimer;
    private int _autoStrafe = 1;
    private bool _autoFire;

    /// <summary>Simple "demo" pilot used by screenshot/attract mode: face the nearest enemy and fight.</summary>
    private PlayerInput AutopilotInput()
    {
        var s = Client.Latest;
        var input = new PlayerInput { Yaw = Yaw, Pitch = Pitch };
        if (s is null || !s.TryGetPlayer(Client.LocalId, out var me)) return input;
        if (!me.Has(NetPlayerFlags.Alive)) { _autoFire = !_autoFire; input.Buttons = _autoFire ? InputButtons.Fire : 0; return input; }
        float best = float.MaxValue; Vector3 target = default; bool found = false;
        for (int i = 0; i < s.PlayerCount; i++)
        {
            var p = s.Players[i];
            if (p.Id == me.Id || !p.Has(NetPlayerFlags.Alive)) continue;
            float d = Vector3.DistanceSquared(p.Position, me.Position);
            if (d < best && Client.Collision is { } col && col.LineOfSight(me.Position + new Vector3(0, 1.6f, 0), p.Position + new Vector3(0, 1.2f, 0)))
            {
                best = d; target = p.Position; found = true;
            }
        }
        _autoTimer -= SimConstants.TickDelta;
        if (_autoTimer <= 0) { _autoStrafe = -_autoStrafe; _autoTimer = 1.2f; }
        if (found)
        {
            var to = target + new Vector3(0, 1.1f, 0) - (me.Position + new Vector3(0, 1.6f, 0));
            float wantYaw = MathF.Atan2(to.X, to.Z);
            float wantPitch = MathF.Atan2(to.Y, MathF.Sqrt(to.X * to.X + to.Z * to.Z));
            Yaw += Math.Clamp(VectorMath.WrapAngle(wantYaw - Yaw), -0.08f, 0.08f);
            Pitch += Math.Clamp(wantPitch - Pitch, -0.05f, 0.05f);
            input.Right = (sbyte)(_autoStrafe * 100);
            input.Forward = best > 100 ? (sbyte)100 : (sbyte)0;
            bool aimed = MathF.Abs(VectorMath.WrapAngle(wantYaw - Yaw)) < 0.15f;
            if (aimed) input.Buttons |= InputButtons.Fire;
            AutopilotEngaged = aimed && best < 30 * 30;
        }
        else
        {
            AutopilotEngaged = false;
            Yaw += 0.01f;
            input.Forward = 90;
        }
        input.Yaw = Yaw; input.Pitch = Pitch;
        return input;
    }

    public void Dispose()
    {
        Client.Dispose();
        Server?.Dispose();
    }
}
