using System.Numerics;
using Djancoeg.Core;
using Djancoeg.Core.Simulation;

namespace Djancoeg.Net;

public enum PacketType : byte
{
    Connect = 1,
    Accept = 2,
    Reject = 3,
    Input = 4,
    Snapshot = 5,
    PlayerInfo = 6,
    Disconnect = 7,
    DiscoveryRequest = 8,
    DiscoveryResponse = 9,
    MasterHeartbeat = 10,
    MasterQuery = 11,
    MasterList = 12,
    ChangeCharacter = 13,
    Chat = 14,
}

/// <summary>Wire-format constants and helpers. Every packet starts with "DJ", the protocol version and a type byte.</summary>
public static class Protocol
{
    public const byte Magic0 = (byte)'D', Magic1 = (byte)'J';
    public const byte Version = 1;
    public const int DefaultPort = 27960;
    public const int DefaultMasterPort = 27950;
    public const int MaxPacketSize = 1200; // stays under typical internet MTU
    public const int HeaderSize = 4;
    public const float TimeoutSeconds = 10f;
    public const int InputRedundancy = 4;
    public const float InterpolationDelay = 0.1f;

    public static void WriteHeader(ref NetWriter w, PacketType type)
    {
        w.U8(Magic0); w.U8(Magic1); w.U8(Version); w.U8((byte)type);
    }

    /// <summary>Validates the header and returns the packet type, or 0 for foreign/old packets.</summary>
    public static PacketType ReadHeader(ref NetReader r)
    {
        if (r.Remaining < HeaderSize) return 0;
        if (r.U8() != Magic0 || r.U8() != Magic1 || r.U8() != Version) return 0;
        return (PacketType)r.U8();
    }

    public static void WriteInput(ref NetWriter w, in PlayerInput i)
    {
        w.U32(i.Sequence); w.Angle(i.Yaw); w.I16(Quant.Pitch(i.Pitch));
        w.I8(i.Forward); w.I8(i.Right); w.U16((ushort)i.Buttons); w.U8(i.WeaponSlot);
    }

    public static PlayerInput ReadInput(ref NetReader r) => new()
    {
        Sequence = r.U32(), Yaw = r.Angle(), Pitch = Quant.UnPitch(r.I16()),
        Forward = r.I8(), Right = r.I8(), Buttons = (InputButtons)r.U16(), WeaponSlot = r.U8(),
    };

    public static void WriteEvent(ref NetWriter w, in GameEvent e)
    {
        w.U32(e.Id); w.U8((byte)e.Type); w.U8(e.A); w.U8(e.B); w.U8(e.Weapon); w.U8(e.Flags); w.I16(e.Value);
        w.Pos(e.Position); w.Pos(e.Vector);
    }

    public static GameEvent ReadEvent(ref NetReader r) => new()
    {
        Id = r.U32(), Type = (GameEventType)r.U8(), A = r.U8(), B = r.U8(), Weapon = r.U8(), Flags = r.U8(), Value = r.I16(),
        Position = r.Pos(), Vector = r.Pos(),
    };

    public const int EventSize = 4 + 1 + 4 + 2 + 12;
}

[Flags]
public enum NetPlayerFlags : ushort
{
    None = 0,
    Active = 1 << 0,
    Alive = 1 << 1,
    OnGround = 1 << 2,
    Crouched = 1 << 3,
    Invisible = 1 << 4,
    Stone = 1 << 5,
    Hasted = 1 << 6,
    Bot = 1 << 7,
    Protected = 1 << 8,
    Dashing = 1 << 9,
    Quad = 1 << 10,
}

/// <summary>Replicated view of one player.</summary>
public struct NetPlayer
{
    public byte Id;
    public NetPlayerFlags Flags;
    public byte Character, Team, Weapon;
    public Vector3 Position, Velocity;
    public float Yaw, Pitch;
    public short Health;
    public byte Armor;
    public short Score, Kills, Deaths;
    public float AbilityCooldown, AbilityTimer, QuadTimer;
    public byte PingQuarterMs;

    public readonly bool Has(NetPlayerFlags f) => (Flags & f) != 0;
    public const int Size = 1 + 2 + 3 + 12 + 6 + 2 + 2 + 2 + 1 + 6 + 3 + 1;

    public readonly void Write(ref NetWriter w)
    {
        w.U8(Id); w.U16((ushort)Flags); w.U8(Character); w.U8(Team); w.U8(Weapon);
        w.Pos(Position); w.Vel(Velocity); w.Angle(Yaw); w.I16(Quant.Pitch(Pitch));
        w.I16(Health); w.U8(Armor); w.I16(Score); w.I16(Kills); w.I16(Deaths);
        w.U8(Tenths(AbilityCooldown)); w.U8(Tenths(AbilityTimer)); w.U8(Tenths(QuadTimer)); w.U8(PingQuarterMs);
    }

    public static NetPlayer Read(ref NetReader r) => new()
    {
        Id = r.U8(), Flags = (NetPlayerFlags)r.U16(), Character = r.U8(), Team = r.U8(), Weapon = r.U8(),
        Position = r.Pos(), Velocity = r.Vel(), Yaw = r.Angle(), Pitch = Quant.UnPitch(r.I16()),
        Health = r.I16(), Armor = r.U8(), Score = r.I16(), Kills = r.I16(), Deaths = r.I16(),
        AbilityCooldown = r.U8() / 10f, AbilityTimer = r.U8() / 10f, QuadTimer = r.U8() / 10f, PingQuarterMs = r.U8(),
    };

    private static byte Tenths(float s) => (byte)Math.Clamp(MathF.Ceiling(s * 10f), 0, 255);
}

public struct NetProjectile
{
    public ushort Id;
    public byte Weapon, Owner;
    public Vector3 Position, Velocity;
    public const int Size = 2 + 2 + 12;
}

public struct NetProp
{
    public byte Index;
    public Vector3 Position, Rotation;
    public const int Size = 1 + 12;
}

/// <summary>One decoded server snapshot. Instances are pooled by the client; arrays are preallocated.</summary>
public sealed class Snapshot
{
    public uint Tick;
    public uint AckInput;
    public uint EchoTime;
    public MatchPhase Phase;
    public float TimeLeft;
    public float PhaseTimer;
    public short TeamScore0, TeamScore1;
    public sbyte ZoneHolder;
    public sbyte Winner;
    public byte ArenaIndex;
    public byte LocalId;
    public byte Generation;

    public readonly NetPlayer[] Players = new NetPlayer[SimConstants.MaxPlayers];
    public int PlayerCount;
    public ushort OwnedWeapons;
    public readonly ushort[] Ammo = new ushort[SimConstants.MaxWeapons];
    public readonly NetProjectile[] Projectiles = new NetProjectile[SimConstants.MaxProjectiles];
    public int ProjectileCount;
    public readonly NetProp[] Props = new NetProp[SimConstants.MaxProps];
    public int PropCount;
    public ulong PickupMask;
    public readonly GameEvent[] Events = new GameEvent[64];
    public int EventCount;

    public bool TryGetPlayer(int id, out NetPlayer p)
    {
        for (int i = 0; i < PlayerCount; i++) if (Players[i].Id == id) { p = Players[i]; return true; }
        p = default;
        return false;
    }

    public void CopyFrom(Snapshot o)
    {
        Tick = o.Tick; AckInput = o.AckInput; EchoTime = o.EchoTime; Phase = o.Phase; TimeLeft = o.TimeLeft; PhaseTimer = o.PhaseTimer;
        TeamScore0 = o.TeamScore0; TeamScore1 = o.TeamScore1; ZoneHolder = o.ZoneHolder; Winner = o.Winner; ArenaIndex = o.ArenaIndex; LocalId = o.LocalId; Generation = o.Generation;
        PlayerCount = o.PlayerCount; Array.Copy(o.Players, Players, o.PlayerCount);
        OwnedWeapons = o.OwnedWeapons; Array.Copy(o.Ammo, Ammo, Ammo.Length);
        ProjectileCount = o.ProjectileCount; Array.Copy(o.Projectiles, Projectiles, o.ProjectileCount);
        PropCount = o.PropCount; Array.Copy(o.Props, Props, o.PropCount);
        PickupMask = o.PickupMask; EventCount = 0;
    }
}

/// <summary>Server info returned by LAN discovery and master-server pings.</summary>
public sealed record ServerInfo(string Name, string ArenaId, byte Mode, int Players, int MaxPlayers, uint ContentSignature, System.Net.IPEndPoint EndPoint)
{
    public float PingMs { get; set; }
}
