using System.Text.Json.Serialization;
using Djancoeg.Core.Localization;

namespace Djancoeg.Core.Content;

[JsonConverter(typeof(JsonStringEnumConverter<CreepShape>))]
public enum CreepShape : byte { Zombie, FloatingHead, LongLegs, Alien, Gorilla, Giant, Cyclops, Worm }

/// <summary>
/// An "Arena Creep": an absurd creature that appears at random times and places, chases and harasses
/// players, and can be killed. Purely for chaos and surprise; matches can turn them off.
/// </summary>
public sealed record CreepDef
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public CreepShape Shape { get; set; }
    public float Health { get; set; } = 150;
    /// <summary>Multiplier on player run speed.</summary>
    public float Speed { get; set; } = 1f;
    /// <summary>Visual and hitbox scale (1 = player sized).</summary>
    public float Scale { get; set; } = 1f;
    public float Damage { get; set; } = 15;
    public float AttackRange { get; set; } = 1.6f;
    public float AttackInterval { get; set; } = 1.2f;
    public float Knockback { get; set; } = 8f;
    /// <summary>How many appear together (zombies come in hordes).</summary>
    public int Horde { get; set; } = 1;
    public float Lifetime { get; set; } = 50f;
    public int ScoreReward { get; set; } = 1;
    /// <summary>Relative chance of being picked when a creep event happens.</summary>
    public float Weight { get; set; } = 1f;
    public string Color { get; set; } = "#7FA36B";
    public string BloodColor { get; set; } = "#8B0000";
    public LocalizedText KillVerb { get; set; } = new("{creep} got {victim}", "{creep} menghabisi {victim}");
}

public static class BuiltInCreeps
{
    private static LocalizedText T(string en, string id) => new(en, id);

    public static IReadOnlyList<CreepDef> All { get; } =
    [
        new CreepDef
        {
            Id = "zombie-kondangan", Shape = CreepShape.Zombie, Name = T("Wedding-Party Zombies", "Rombongan Zombie Kondangan"),
            Health = 60, Speed = 0.62f, Damage = 10, Horde = 6, Weight = 3, Color = "#7FA36B", BloodColor = "#3D6B2E", ScoreReward = 1,
            KillVerb = T("the wedding zombies ate {victim}", "zombie kondangan menggerogoti {victim}"),
        },
        new CreepDef
        {
            Id = "kepala-melayang", Shape = CreepShape.FloatingHead, Name = T("Giant Floating Head", "Kepala Raksasa Melayang"),
            Health = 220, Speed = 0.85f, Scale = 2.2f, Damage = 18, AttackRange = 2.4f, Weight = 1.5f, Color = "#D7B899", BloodColor = "#8B0000", ScoreReward = 2,
            KillVerb = T("a giant floating head chomped {victim}", "kepala raksasa melayang menggigit {victim}"),
        },
        new CreepDef
        {
            Id = "si-jangkung", Shape = CreepShape.LongLegs, Name = T("The Lanky One", "Si Jangkung"),
            Health = 160, Speed = 1.25f, Scale = 2.4f, Damage = 14, AttackRange = 3f, Knockback = 13, Weight = 1.5f, Color = "#3B3B48", BloodColor = "#2B2B2B", ScoreReward = 2,
            KillVerb = T("the Lanky One flicked {victim} away", "Si Jangkung menyentil {victim} jauh-jauh"),
        },
        new CreepDef
        {
            Id = "alien-nyasar", Shape = CreepShape.Alien, Name = T("Lost Aliens", "Alien Nyasar"),
            Health = 90, Speed = 1.05f, Scale = 0.9f, Damage = 9, Horde = 3, Weight = 2, Color = "#8FBF6A", BloodColor = "#7E3FA6", ScoreReward = 1,
            KillVerb = T("lost aliens probed {victim}", "alien nyasar menyelidiki {victim}"),
        },
        new CreepDef
        {
            Id = "gorila-albino", Shape = CreepShape.Gorilla, Name = T("Giant Albino Gorilla", "Gorila Albino Raksasa"),
            Health = 400, Speed = 1.1f, Scale = 2.0f, Damage = 30, AttackRange = 2.6f, Knockback = 18, Weight = 1, Color = "#F2F0EA", BloodColor = "#8B0000", ScoreReward = 2,
            KillVerb = T("the albino gorilla pounded {victim}", "gorila albino menghantam {victim}"),
        },
        new CreepDef
        {
            Id = "buto-ijo", Shape = CreepShape.Giant, Name = T("Buto Ijo the Green Giant", "Buto Ijo"),
            Health = 900, Speed = 0.72f, Scale = 3.2f, Damage = 45, AttackRange = 4f, AttackInterval = 1.8f, Knockback = 22, Weight = 0.6f, Color = "#3E8E41", BloodColor = "#8B0000", ScoreReward = 3,
            KillVerb = T("Buto Ijo stomped {victim}", "Buto Ijo menginjak {victim}"),
        },
        new CreepDef
        {
            Id = "cyclops-kekar", Shape = CreepShape.Cyclops, Name = T("Buff Cyclops", "Cyclops Kekar"),
            Health = 600, Speed = 0.82f, Scale = 2.6f, Damage = 35, AttackRange = 3.4f, AttackInterval = 1.5f, Knockback = 20, Weight = 0.8f, Color = "#B07D56", BloodColor = "#8B0000", ScoreReward = 2,
            KillVerb = T("the cyclops flattened {victim}", "cyclops kekar menggepengkan {victim}"),
        },
        new CreepDef
        {
            Id = "cacing-raksasa", Shape = CreepShape.Worm, Name = T("Giant Earthworm", "Cacing Tanah Raksasa"),
            Health = 700, Speed = 0.6f, Scale = 3f, Damage = 50, AttackRange = 3.5f, Knockback = 20, Weight = 0.7f, Lifetime = 40, Color = "#C98A7A", BloodColor = "#D9A441", ScoreReward = 3,
            KillVerb = T("the giant worm swallowed {victim}", "cacing raksasa menelan {victim}"),
        },
    ];
}
