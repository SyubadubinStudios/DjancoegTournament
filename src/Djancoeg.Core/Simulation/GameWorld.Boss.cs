using Djancoeg.Core.Content;

namespace Djancoeg.Core.Simulation;

/// <summary>Campaign boss stages: one beefed-up creep that keeps coming back until someone lands the killing blow.</summary>
public sealed partial class GameWorld
{
    /// <summary>Creep slot of the boss, or -1.</summary>
    public int BossSlot { get; private set; } = -1;
    public bool BossDefeated { get; private set; }
    /// <summary>Player who landed the killing blow on the boss (-1 = none / creep / world).</summary>
    public int BossKiller { get; private set; } = -1;
    private float _bossTimer = 5f;
    private int _bossKind = -1;
    /// <summary>Health the boss had when it last left the arena (-1 = fresh).</summary>
    private float _bossHealthLeft = -1;

    public bool HasBoss => _bossKind >= 0;

    private void ResetBoss()
    {
        BossSlot = -1; BossDefeated = false; BossKiller = -1; _bossTimer = 5f; _bossHealthLeft = -1;
        // Resolved once: the tick must not allocate (CreepIndexOf uses a lambda).
        _bossKind = Settings.BossCreepId.Length > 0 ? Content.CreepIndexOf(Settings.BossCreepId) : -1;
    }

    private void UpdateBoss(float dt)
    {
        if (!HasBoss || BossDefeated || Phase != MatchPhase.Playing) return;
        if (BossSlot >= 0 && Creeps[BossSlot].Active) return;
        if (BossSlot >= 0) { _bossHealthLeft = Creeps[BossSlot].Health; BossSlot = -1; }
        _bossTimer -= dt;
        if (_bossTimer > 0) return;
        _bossTimer = 4f;
        int kind = _bossKind;
        int slot = SpawnCreep(kind);
        if (slot < 0) return;
        ref var c = ref Creeps[slot];
        c.MaxHealth = Content.Creeps[kind].Health * MathF.Max(1f, Settings.BossHealthScale);
        c.Health = _bossHealthLeft > 0 ? MathF.Min(_bossHealthLeft, c.MaxHealth) : c.MaxHealth;
        c.Life = float.MaxValue;
        BossSlot = slot;
        Push(new GameEvent { Type = GameEventType.CreepRoar, A = (byte)slot, Value = (short)kind, Position = c.Move.Position });
    }

    /// <summary>Called from <see cref="DamageCreep"/> when a creep dies.</summary>
    private void OnCreepDied(int index, int attacker)
    {
        if (index != BossSlot || BossDefeated) return;
        BossDefeated = true;
        BossKiller = attacker;
        if (attacker >= 0 && Players[attacker].Active)
        {
            if (IsTeamMode) TeamScores[Players[attacker].Team] += Campaign.BossReward;
            else Players[attacker].Score += Campaign.BossReward;
        }
    }
}
