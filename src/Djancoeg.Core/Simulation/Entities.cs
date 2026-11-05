using System.Numerics;
using System.Runtime.CompilerServices;

namespace Djancoeg.Core.Simulation;

/// <summary>Fixed-size per-weapon ammo storage, stored inline in <see cref="Player"/> (no heap array).</summary>
[InlineArray(SimConstants.MaxWeapons)]
public struct AmmoBuffer
{
    private int _element;
}

public enum MatchPhase : byte { Playing, Ended }

/// <summary>A player slot. Stored in a flat array; the slot index is the network player id.</summary>
public struct Player
{
    public bool Active;
    public bool IsBot;
    public byte Team;
    public int CharacterIndex;
    public string Name;

    public MoveState Move;
    public float Yaw, Pitch;

    public bool Alive;
    public float Health, Armor;
    public float RespawnTimer, SpawnProtect;

    public int Weapon;
    public ushort OwnedWeapons;
    public AmmoBuffer Ammo;
    public float FireCooldown;

    public int Kills, Deaths, Score, Streak;
    public float ZoneAccumulator;

    public float AbilityCooldown;
    public float AbilityTimer;
    public float DashTimer;
    public ushort DashHitMask;
    public float QuadTimer;
    public float TauntCooldown;

    public int LastAttacker;
    public float LastAttackerTime;

    public PlayerInput Input;
    public InputButtons PrevButtons;
    public uint LastInputSequence;
    /// <summary>Ticks to rewind other players when this player's hitscan shots are evaluated.</summary>
    public int LagCompensationTicks;

    public readonly bool Owns(int weapon) => (OwnedWeapons & (1 << weapon)) != 0;
    public readonly bool Pressed(InputButtons b) => (Input.Buttons & b) != 0 && (PrevButtons & b) == 0;
}

public struct Projectile
{
    public bool Active;
    public byte Owner;
    public byte Weapon;
    public ushort Id;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Life;
    public float Age;
    public bool Quad;
}

public struct Prop
{
    public bool Active;
    public byte KindIndex;
    public Vector3 Position; // center
    public Vector3 Velocity;
    public Vector3 HalfExtents;
    public Vector3 Rotation; // euler, visual only
    public Vector3 AngularVelocity;
    public float Mass;
    public int LastToucher;
    public float LastTouchTime;
    public float HitCooldown;
    public uint Color;
}

public struct PickupState
{
    public bool Available;
    public float Timer;
}

[Flags]
public enum KillFlags : byte
{
    None = 0,
    Headshot = 1,
    Suicide = 2,
    Environment = 4,
    Gibbed = 8,
    PropKill = 16,
    Ability = 32,
}

public enum GameEventType : byte
{
    Shot, Hit, Kill, Explosion, Pickup, Jump, JumpPad, Spawn, Ability, Taunt,
    MatchStart, MatchEnd, ZoneScore, PropHit, Land, Join, Leave, Streak, ProjectileBounce,
}

/// <summary>Something that happened this tick, used for effects, sounds, HUD and the kill feed.</summary>
public struct GameEvent
{
    public uint Id;
    public GameEventType Type;
    public byte A;      // primary player (shooter, victim, picker...)
    public byte B;      // secondary player (attacker...)
    public byte Weapon;
    public byte Flags;
    public short Value;
    public Vector3 Position;
    public Vector3 Vector;
}

/// <summary>Fixed-capacity ring buffer of events with monotonically increasing ids.</summary>
public sealed class EventBuffer
{
    public const int Capacity = 1024;
    private readonly GameEvent[] _ring = new GameEvent[Capacity];
    private uint _nextId = 1;

    public uint LastId => _nextId - 1;

    public void Push(GameEvent e)
    {
        e.Id = _nextId++;
        _ring[e.Id % Capacity] = e;
    }

    /// <summary>Copies events with id &gt; <paramref name="afterId"/> into <paramref name="dest"/>, oldest first.</summary>
    public int ReadSince(uint afterId, Span<GameEvent> dest)
    {
        uint last = LastId;
        if (last <= afterId) return 0;
        uint first = Math.Max(afterId + 1, last >= Capacity ? last - Capacity + 1 : 1);
        int n = 0;
        for (uint id = first; id <= last && n < dest.Length; id++) dest[n++] = _ring[id % Capacity];
        return n;
    }
}
