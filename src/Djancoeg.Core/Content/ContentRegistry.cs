namespace Djancoeg.Core.Content;

/// <summary>
/// The active set of weapons, characters and arenas (built-in plus enabled mods).
/// Indices into these lists are sent over the network, so client and server must build
/// the registry from the same content in the same order; <see cref="Signature"/> verifies that.
/// </summary>
public sealed class ContentRegistry
{
    private readonly List<WeaponDef> _weapons = [];
    private readonly List<CharacterDef> _characters = [];
    private readonly List<ArenaDef> _arenas = [];
    private readonly List<CreepDef> _creeps = [];
    private readonly Dictionary<string, int> _weaponIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _characterIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _arenaIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _modIds = [];

    public IReadOnlyList<WeaponDef> Weapons => _weapons;
    public IReadOnlyList<CharacterDef> Characters => _characters;
    public IReadOnlyList<ArenaDef> Arenas => _arenas;
    public IReadOnlyList<CreepDef> Creeps => _creeps;
    public IReadOnlyList<string> LoadedMods => _modIds;

    /// <summary>Registry with only the content that ships with the game.</summary>
    public static ContentRegistry CreateBuiltIn()
    {
        var r = new ContentRegistry();
        foreach (var w in BuiltInContent.Weapons) r.AddOrReplace(w);
        foreach (var c in BuiltInContent.Characters) r.AddOrReplace(c);
        foreach (var a in BuiltInArenas.All) r.AddOrReplace(a);
        foreach (var c in BuiltInCreeps.All) r.AddOrReplace(c);
        return r;
    }

    public void AddOrReplace(WeaponDef w)
    {
        if (_weaponIndex.TryGetValue(w.Id, out int i)) { _weapons[i] = w; return; }
        if (_weapons.Count >= SimConstants.MaxWeapons) throw new InvalidOperationException($"Too many weapons (max {SimConstants.MaxWeapons}).");
        _weaponIndex[w.Id] = _weapons.Count;
        _weapons.Add(w);
    }

    public void AddOrReplace(CharacterDef c)
    {
        if (_characterIndex.TryGetValue(c.Id, out int i)) { _characters[i] = c; return; }
        if (_characters.Count >= 255) throw new InvalidOperationException("Too many characters (max 255).");
        _characterIndex[c.Id] = _characters.Count;
        _characters.Add(c);
    }

    public void AddOrReplace(ArenaDef a)
    {
        if (_arenaIndex.TryGetValue(a.Id, out int i)) { _arenas[i] = a; return; }
        _arenaIndex[a.Id] = _arenas.Count;
        _arenas.Add(a);
    }

    public void AddOrReplace(CreepDef c)
    {
        int i = _creeps.FindIndex(x => x.Id.Equals(c.Id, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) { _creeps[i] = c; return; }
        if (_creeps.Count >= 255) throw new InvalidOperationException("Too many creeps (max 255).");
        _creeps.Add(c);
    }

    public int CreepIndexOf(string id) => _creeps.FindIndex(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void RegisterMod(string modId) => _modIds.Add(modId);

    public int WeaponIndexOf(string id) => _weaponIndex.TryGetValue(id, out int i) ? i : -1;
    public int CharacterIndexOf(string id) => _characterIndex.TryGetValue(id, out int i) ? i : -1;
    public int ArenaIndexOf(string id) => _arenaIndex.TryGetValue(id, out int i) ? i : -1;

    public ArenaDef GetArena(string id) =>
        _arenaIndex.TryGetValue(id, out int i) ? _arenas[i] : throw new KeyNotFoundException($"Unknown arena '{id}'.");

    /// <summary>FNV-1a hash over content ids and the gameplay numbers that affect simulation.</summary>
    public uint Signature
    {
        get
        {
            uint h = 2166136261;
            void Mix(string s) { foreach (char ch in s) { h ^= ch; h *= 16777619; } h ^= 0xFF; h *= 16777619; }
            void MixF(float f) { h ^= (uint)BitConverter.SingleToInt32Bits(f); h *= 16777619; }
            foreach (var w in _weapons) { Mix(w.Id); MixF(w.Damage); MixF(w.FireInterval); MixF(w.ProjectileSpeed); }
            foreach (var c in _characters) { Mix(c.Id); MixF(c.SpeedMultiplier); MixF(c.JumpMultiplier); MixF(c.Scale); }
            foreach (var a in _arenas) { Mix(a.Id); h ^= (uint)a.Boxes.Count; h *= 16777619; }
            foreach (var c in _creeps) { Mix(c.Id); MixF(c.Health); MixF(c.Speed); }
            return h;
        }
    }
}
