using System.Numerics;
using Djancoeg.Core.Content;
using Djancoeg.Core.Geometry;

namespace Djancoeg.Core.Simulation;

/// <summary>
/// Authoritative match simulation. Runs at a fixed <see cref="SimConstants.TickRate"/> on the server
/// (dedicated, listen or practice). All entity storage is preallocated; a tick performs no heap allocation.
/// </summary>
public sealed partial class GameWorld
{
    public ContentRegistry Content { get; }
    public ArenaDef Arena { get; }
    public int ArenaIndex { get; }
    public CollisionWorld Collision { get; }
    public MatchSettings Settings { get; private set; }

    /// <summary>Changes settings that are safe mid-match (bots, skill, limits, name). Arena and mode need a new world.</summary>
    public void UpdateSettings(MatchSettings s)
    {
        Settings = s with { ArenaId = Settings.ArenaId, Mode = Settings.Mode };
        for (int i = 0; i < _bots.Length; i++) if (Players[i].Active && Players[i].IsBot) _bots[i].Reset(Settings.BotSkill);
    }

    public readonly Player[] Players = new Player[SimConstants.MaxPlayers];
    public readonly Projectile[] Projectiles = new Projectile[SimConstants.MaxProjectiles];
    public readonly Prop[] Props;
    public readonly PickupState[] Pickups;
    public readonly EventBuffer Events = new();
    public readonly int[] TeamScores = new int[2];

    public uint Tick { get; private set; }
    public float MatchTime { get; private set; }
    public MatchPhase Phase { get; private set; }
    public float PhaseTimer { get; private set; }
    /// <summary>Winning player (FFA/KOTH) or team (TDM); -1 for none.</summary>
    public int Winner { get; private set; } = -1;
    public int ZoneHolder { get; private set; } = -1;

    public bool IsTeamMode => Settings.Mode is GameModeKind.TeamDeathmatch or GameModeKind.CaptureTheFlag;
    public readonly FlagState[] Flags = new FlagState[2];
    public float TimeLeft => MathF.Max(0, Settings.TimeLimitSeconds - MatchTime);

    private FastRandom _rng;
    private readonly BotBrain[] _bots = new BotBrain[SimConstants.MaxPlayers];
    private readonly Vector3[] _history = new Vector3[SimConstants.MaxPlayers * HistoryLength];
    private const int HistoryLength = 32;
    private ushort _nextProjectileId = 1;
    private readonly int[] _propKinds;

    public static readonly string[] PropKindNames = ["crate", "galon", "gerobak", "karung", "ban", "kursi", "motor", "jerigen"];

    public GameWorld(ContentRegistry content, MatchSettings settings, ulong seed = 0x5EED)
    {
        Content = content;
        Settings = settings;
        ArenaIndex = content.ArenaIndexOf(settings.ArenaId);
        if (ArenaIndex < 0) { ArenaIndex = 0; }
        Arena = content.Arenas[ArenaIndex];
        Collision = new CollisionWorld(Arena);
        _rng = new FastRandom(seed);

        Props = new Prop[Math.Min(Arena.Props.Count, SimConstants.MaxProps)];
        _propKinds = new int[Props.Length];
        Pickups = new PickupState[Math.Min(Arena.Pickups.Count, SimConstants.MaxPickups)];
        ResetMap();
        for (int i = 0; i < _bots.Length; i++) _bots[i] = new BotBrain(seed + (ulong)i * 7919);
    }

    private void ResetMap()
    {
        for (int i = 0; i < Props.Length; i++)
        {
            var d = Arena.Props[i];
            int kind = Array.IndexOf(PropKindNames, d.Kind);
            Props[i] = new Prop
            {
                Active = true, KindIndex = (byte)Math.Max(0, kind), Position = d.PositionV, HalfExtents = d.HalfExtentsV,
                Mass = MathF.Max(1f, d.Mass), LastToucher = -1, Color = ColorUtil.ParseRgba(d.Color),
            };
        }
        for (int i = 0; i < Pickups.Length; i++) Pickups[i] = new PickupState { Available = true };
        Array.Clear(Projectiles);
        for (int t = 0; t < 2; t++)
        {
            var home = FlagHome(t);
            Flags[t] = new FlagState { Home = home, Position = home, Carrier = -1, AtHome = true };
        }
    }

    // ------------------------------------------------------------------ players

    public int ActivePlayerCount
    {
        get { int n = 0; foreach (ref readonly var p in Players.AsSpan()) if (p.Active) n++; return n; }
    }

    public int HumanPlayerCount
    {
        get { int n = 0; foreach (ref readonly var p in Players.AsSpan()) if (p.Active && !p.IsBot) n++; return n; }
    }

    /// <summary>Adds a player and returns its slot, or -1 when the server is full.</summary>
    public int AddPlayer(string name, int characterIndex, bool isBot, int preferredSlot = -1)
    {
        int max = Math.Min(Settings.MaxPlayers, SimConstants.MaxPlayers);
        if (ActivePlayerCount >= max) return -1;
        for (int k = -1; k < SimConstants.MaxPlayers; k++)
        {
            int i = k < 0 ? preferredSlot : k;
            if ((uint)i >= SimConstants.MaxPlayers || Players[i].Active) continue;
            characterIndex = Math.Clamp(characterIndex, 0, Content.Characters.Count - 1);
            Players[i] = new Player
            {
                Active = true, IsBot = isBot, Name = Sanitize(name), CharacterIndex = characterIndex,
                Team = (byte)(IsTeamMode ? SmallerTeam() : 0), LastAttacker = -1,
            };
            if (isBot) _bots[i].Reset(Settings.BotSkill);
            Respawn(i);
            Push(new GameEvent { Type = GameEventType.Join, A = (byte)i });
            return i;
        }
        return -1;
    }

    public void RemovePlayer(int id)
    {
        if ((uint)id >= SimConstants.MaxPlayers || !Players[id].Active) return;
        Players[id].Active = false;
        Push(new GameEvent { Type = GameEventType.Leave, A = (byte)id });
        if (ZoneHolder == id) ZoneHolder = -1;
    }

    public void SetCharacter(int id, int characterIndex)
    {
        if ((uint)id >= SimConstants.MaxPlayers || !Players[id].Active) return;
        Players[id].CharacterIndex = Math.Clamp(characterIndex, 0, Content.Characters.Count - 1);
    }

    public void SetInput(int id, in PlayerInput input)
    {
        if ((uint)id >= SimConstants.MaxPlayers || !Players[id].Active) return;
        ref var p = ref Players[id];
        p.Input = input;
        p.Input.Pitch = Math.Clamp(input.Pitch, -1.55f, 1.55f);
        p.LastInputSequence = input.Sequence;
    }

    /// <summary>Adds or removes bots so humans + bots matches <see cref="MatchSettings.BotCount"/> (when there is room).</summary>
    public void BalanceBots()
    {
        int humans = HumanPlayerCount;
        int wantBots = Math.Max(0, Math.Min(Settings.BotCount, Settings.MaxPlayers - humans));
        int bots = 0;
        for (int i = 0; i < Players.Length; i++) if (Players[i].Active && Players[i].IsBot) bots++;
        while (bots < wantBots)
        {
            int ch = _rng.Next(Content.Characters.Count);
            if (AddPlayer(BotNames[_rng.Next(BotNames.Length)], ch, true) < 0) break;
            bots++;
        }
        for (int i = Players.Length - 1; i >= 0 && bots > wantBots; i--)
        {
            if (Players[i].Active && Players[i].IsBot) { RemovePlayer(i); bots--; }
        }
    }

    private static readonly string[] BotNames =
    [
        "Mas Gondrong", "Cak Kartolo", "Bang Jago", "Mbak Sri", "Pak Lurah", "Ucup", "Asep Knalpot", "Joko Santuy",
        "Neng Geulis", "Mang Oleh", "Om Telolet", "Bu Tejo", "Kang Cilok", "Bli Made", "Daeng Rewa", "Ucok Baba",
        "Mas Ojak", "Teh Euis", "Pak RW", "Bu RT", "Bang Udin", "Cak Dul", "Mbok Darmi", "Opung Tigor",
        "Uda Rizal", "Nyoman Gedeg", "Ama Keling", "Engkong Bewok", "Jeng Lastri", "Dik Bayu", "Wak Kumis", "Abah Odon",
    ];

    private int SmallerTeam()
    {
        int a = 0, b = 0;
        foreach (ref readonly var p in Players.AsSpan()) if (p.Active) { if (p.Team == 0) a++; else b++; }
        return a <= b ? 0 : 1;
    }

    private static string Sanitize(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) name = "Pemain";
        if (name.Length > 20) name = name[..20];
        return name;
    }

    public MoveParams ParamsFor(in Player p)
    {
        var c = Content.Characters[p.CharacterIndex];
        float speed = c.SpeedMultiplier;
        bool levitate = false;
        if (p.AbilityTimer > 0)
        {
            if (c.Ability.Kind == AbilityKind.Haste) speed *= c.Ability.Power;
            else if (c.Ability.Kind == AbilityKind.StoneSkin) speed *= 0.75f;
            else if (c.Ability.Kind == AbilityKind.Levitate) levitate = true;
        }
        if (p.BoostTimer > 0) speed *= 1.25f;
        return new MoveParams(speed, c.JumpMultiplier, c.Scale, levitate ? 0.2f : 1f, levitate);
    }

    public Vector3 EyePosition(in Player p) => p.Move.Position + new Vector3(0, ParamsFor(p).EyeHeight(p.Move.Crouched), 0);

    public Aabb BodyBox(in Player p)
    {
        var mp = ParamsFor(p);
        return Aabb.FromFeet(p.Move.Position, mp.HalfWidth, mp.Height(p.Move.Crouched));
    }

    public bool IsInvisible(in Player p) =>
        p.AbilityTimer > 0 && Content.Characters[p.CharacterIndex].Ability.Kind == AbilityKind.Invisibility;

    public bool IsStone(in Player p) =>
        p.AbilityTimer > 0 && Content.Characters[p.CharacterIndex].Ability.Kind == AbilityKind.StoneSkin;

    public bool IsHasted(in Player p) =>
        p.BoostTimer > 0 || (p.AbilityTimer > 0 && Content.Characters[p.CharacterIndex].Ability.Kind == AbilityKind.Haste);

    public bool IsLevitating(in Player p) =>
        p.AbilityTimer > 0 && Content.Characters[p.CharacterIndex].Ability.Kind == AbilityKind.Levitate;

    /// <summary>Where a gorengan sits when at home: the arena's base, or the centroid of that team's spawns.</summary>
    public Vector3 FlagHome(int team)
    {
        if (Arena.FlagBases.Count >= 2 && Arena.FlagBases[team].Length == 3)
            return new Vector3(Arena.FlagBases[team][0], Arena.FlagBases[team][1], Arena.FlagBases[team][2]);
        Vector3 sum = Vector3.Zero; int n = 0;
        foreach (var s in Arena.Spawns) if (s.Team == team) { sum += s.PositionV; n++; }
        if (n == 0) return Arena.ZoneCenterV + new Vector3(team == 0 ? -10 : 10, 0, 0);
        var c = sum / n;
        // Snap to the floor below the centroid.
        if (Collision.Raycast(c + new Vector3(0, 3, 0), -Vector3.UnitY, 10, out float d, out _)) c.Y = c.Y + 3 - d;
        return c;
    }

    /// <summary>The point a player should head for in objective modes (used by bots).</summary>
    public bool TryGetObjective(int id, out Vector3 target)
    {
        ref readonly var p = ref Players[id];
        switch (Settings.Mode)
        {
            case GameModeKind.KingOfTheHill:
                target = Arena.ZoneCenterV; return true;
            case GameModeKind.Race:
                target = RaceGoal; return true;
            case GameModeKind.CaptureTheFlag:
            {
                int enemy = 1 - p.Team;
                if (Flags[enemy].Carrier == id) { target = Flags[p.Team].Home; return true; }
                if (!Flags[p.Team].AtHome && Flags[p.Team].Carrier < 0) { target = Flags[p.Team].Position; return true; }
                if (Flags[enemy].Carrier < 0) { target = Flags[enemy].Position; return true; }
                target = Flags[enemy].Position; return true; // escort or chase
            }
            default:
                target = default; return false;
        }
    }

    public Vector3 RaceGoal => Arena.RaceGoal is { Length: 3 } g ? new Vector3(g[0], g[1], g[2]) : Arena.ZoneCenterV;

    // ------------------------------------------------------------------ tick

    /// <summary>Advances the simulation by exactly one fixed tick.</summary>
    public void Step()
    {
        const float dt = SimConstants.TickDelta;
        Tick++;

        if (Phase == MatchPhase.Ended)
        {
            PhaseTimer -= dt;
            for (int i = 0; i < Players.Length; i++) if (Players[i].Active) Players[i].PrevButtons = Players[i].Input.Buttons;
            return;
        }

        MatchTime += dt;
        RecordHistory();

        for (int i = 0; i < Players.Length; i++)
        {
            if (!Players[i].Active) continue;
            if (Players[i].IsBot) _bots[i].Think(this, i, ref Players[i].Input, dt);
            TickPlayer(i, dt);
        }

        UpdateProjectiles(dt);
        UpdateProps(dt);
        UpdatePickups(dt);
        UpdateRules(dt);
    }

    private void TickPlayer(int id, float dt)
    {
        ref var p = ref Players[id];
        var input = p.Input;

        if (!p.Alive)
        {
            p.RespawnTimer -= dt;
            bool wantsRespawn = p.IsBot || input.Has(InputButtons.Fire) || input.Has(InputButtons.Jump);
            if (p.RespawnTimer <= 0 && (wantsRespawn || p.RespawnTimer < -2f)) Respawn(id);
            p.PrevButtons = input.Buttons;
            return;
        }

        p.Yaw = input.Yaw;
        p.Pitch = input.Pitch;
        if (p.SpawnProtect > 0) p.SpawnProtect -= dt;
        if (p.FireCooldown > 0) p.FireCooldown -= dt;
        if (p.AbilityCooldown > 0) p.AbilityCooldown -= dt;
        if (p.AbilityTimer > 0) p.AbilityTimer -= dt;
        if (p.QuadTimer > 0) p.QuadTimer -= dt;
        if (p.BoostTimer > 0) p.BoostTimer -= dt;
        if (p.TauntCooldown > 0) p.TauntCooldown -= dt;
        if (p.Health > p.MaxHealthOf(this)) p.Health = MathF.Max(p.MaxHealthOf(this), p.Health - dt); // over-heal decays

        var mp = ParamsFor(p);
        var ev = PlayerMovement.Step(ref p.Move, input, mp, Collision, dt);
        if ((ev & MoveEvents.Jumped) != 0) Push(new GameEvent { Type = GameEventType.Jump, A = (byte)id, Position = p.Move.Position });
        if ((ev & MoveEvents.JumpPad) != 0) Push(new GameEvent { Type = GameEventType.JumpPad, A = (byte)id, Position = p.Move.Position });

        if (p.DashTimer > 0)
        {
            p.DashTimer -= dt;
            DashContact(id);
        }

        HandleWeaponSwitch(ref p, input);
        if (input.Has(InputButtons.Fire)) TryFire(id);
        if (p.Pressed(InputButtons.Ability)) TryAbility(id);
        if (p.Pressed(InputButtons.Taunt) && p.TauntCooldown <= 0)
        {
            p.TauntCooldown = 3f;
            var taunts = Content.Characters[p.CharacterIndex].Taunts;
            Push(new GameEvent { Type = GameEventType.Taunt, A = (byte)id, Value = (short)(taunts.Length == 0 ? 0 : _rng.Next(taunts.Length)), Position = p.Move.Position });
        }

        if (p.Move.Position.Y < Collision.KillY)
        {
            bool credited = p.LastAttacker >= 0 && MatchTime - p.LastAttackerTime < 5f && p.LastAttacker != id;
            Kill(id, credited ? p.LastAttacker : id, 255, KillFlags.Environment, Vector3.Zero);
        }

        p.PrevButtons = input.Buttons;
    }

    private void HandleWeaponSwitch(ref Player p, in PlayerInput input)
    {
        int target = -1;
        if (input.WeaponSlot > 0)
        {
            // Pressing the slot of the current weapon cycles to the next weapon sharing that slot.
            int n = Content.Weapons.Count;
            int start = Content.Weapons[p.Weapon].Slot == input.WeaponSlot ? p.Weapon + 1 : 0;
            for (int k = 0; k < n; k++)
            {
                int w = (start + k) % n;
                if (Content.Weapons[w].Slot == input.WeaponSlot && p.Owns(w)) { target = w; break; }
            }
        }
        else if (p.Pressed(InputButtons.NextWeapon) || p.Pressed(InputButtons.PrevWeapon))
        {
            int dir = p.Pressed(InputButtons.NextWeapon) ? 1 : -1, n = Content.Weapons.Count;
            for (int k = 1; k < n; k++)
            {
                int w = ((p.Weapon + dir * k) % n + n) % n;
                if (p.Owns(w) && (p.Ammo[w] > 0 || Content.Weapons[w].InfiniteAmmo)) { target = w; break; }
            }
        }
        if (target >= 0 && target != p.Weapon)
        {
            p.Weapon = target;
            p.FireCooldown = MathF.Max(p.FireCooldown, 0.25f);
        }
    }

    private void Respawn(int id)
    {
        ref var p = ref Players[id];
        var c = Content.Characters[p.CharacterIndex];
        var spawn = PickSpawn(id);
        var mp = new MoveParams(c.SpeedMultiplier, c.JumpMultiplier, c.Scale);
        var pos = spawn.PositionV;
        for (int k = 0; k < 40 && Collision.Overlaps(Aabb.FromFeet(pos, mp.HalfWidth, mp.Height(false))); k++) pos.Y += 0.1f;

        p.Move = new MoveState { Position = pos, OnGround = false };
        p.Yaw = spawn.Yaw;
        p.Input.Yaw = spawn.Yaw;
        p.Pitch = 0;
        p.Alive = true;
        p.Health = c.MaxHealth;
        p.Armor = 0;
        p.SpawnProtect = SimConstants.SpawnProtection;
        p.OwnedWeapons = 0;
        p.Ammo = default;
        p.Weapon = 0;
        p.FireCooldown = 0.3f;
        p.AbilityCooldown = 2f;
        p.AbilityTimer = 0;
        p.DashTimer = 0;
        p.QuadTimer = 0;
        p.BoostTimer = 0;
        p.Streak = 0;
        p.LastAttacker = -1;
        for (int w = 0; w < Content.Weapons.Count; w++)
        {
            var wd = Content.Weapons[w];
            if (!wd.StartsWith) continue;
            p.OwnedWeapons |= (ushort)(1 << w);
            p.Ammo[w] = wd.StartAmmo;
            if (!wd.InfiniteAmmo) p.Weapon = w; // prefer a ranged starting weapon
        }
        Push(new GameEvent { Type = GameEventType.Spawn, A = (byte)id, Position = pos });
    }

    private ArenaSpawn PickSpawn(int id)
    {
        var spawns = Arena.Spawns;
        if (spawns.Count == 0) return new ArenaSpawn { Position = [0, 2, 0] };
        int team = IsTeamMode ? Players[id].Team : -1;
        int best = -1; float bestScore = float.MinValue;
        for (int s = 0; s < spawns.Count; s++)
        {
            if (team >= 0 && spawns[s].Team >= 0 && spawns[s].Team != team) continue;
            var sp = spawns[s].PositionV;
            float nearest = 1000f;
            for (int i = 0; i < Players.Length; i++)
            {
                if (i == id || !Players[i].Active || !Players[i].Alive) continue;
                if (IsTeamMode && Players[i].Team == team) continue;
                nearest = MathF.Min(nearest, Vector3.Distance(sp, Players[i].Move.Position));
            }
            float score = nearest + _rng.Range(0, 8f);
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return spawns[best < 0 ? _rng.Next(spawns.Count) : best];
    }

    private void RecordHistory()
    {
        int slot = (int)(Tick % HistoryLength);
        for (int i = 0; i < Players.Length; i++) _history[i * HistoryLength + slot] = Players[i].Move.Position;
    }

    /// <summary>Position of player <paramref name="i"/> <paramref name="ticksAgo"/> ticks in the past (for lag compensation).</summary>
    public Vector3 HistoricalPosition(int i, int ticksAgo)
    {
        ticksAgo = Math.Clamp(ticksAgo, 0, Math.Min(HistoryLength - 1, (int)Tick - 1));
        if (ticksAgo == 0) return Players[i].Move.Position;
        int slot = (int)((Tick - (uint)ticksAgo) % HistoryLength);
        return _history[i * HistoryLength + slot];
    }

    internal void Push(GameEvent e) => Events.Push(e);
    internal ref FastRandom Rng => ref _rng;

    /// <summary>Restarts the match on the same arena, keeping connected players.</summary>
    public void RestartMatch()
    {
        MatchTime = 0; Phase = MatchPhase.Playing; Winner = -1; ZoneHolder = -1;
        TeamScores[0] = TeamScores[1] = 0;
        ResetMap();
        for (int i = 0; i < Players.Length; i++)
        {
            if (!Players[i].Active) continue;
            Players[i].Kills = Players[i].Deaths = Players[i].Score = 0;
            Players[i].ZoneAccumulator = 0;
            Respawn(i);
        }
        Push(new GameEvent { Type = GameEventType.MatchStart });
    }
}

public static class PlayerExtensions
{
    public static float MaxHealthOf(this in Player p, GameWorld world) => world.Content.Characters[p.CharacterIndex].MaxHealth;
}
