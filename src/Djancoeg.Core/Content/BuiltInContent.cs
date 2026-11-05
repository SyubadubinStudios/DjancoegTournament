using Djancoeg.Core.Localization;

namespace Djancoeg.Core.Content;

/// <summary>The weapons and fighters that ship with the game. Arenas live in <see cref="BuiltInArenas"/>.</summary>
public static class BuiltInContent
{
    private static LocalizedText T(string en, string id) => new(en, id);

    public static IReadOnlyList<WeaponDef> Weapons { get; } =
    [
        new WeaponDef
        {
            Id = "raket-nyamuk", Slot = 1, Kind = WeaponKind.Melee, Model = "racket", Color = "#7FDBFF",
            Name = T("Electric Mosquito Racket", "Raket Nyamuk Listrik"),
            Description = T("Close range zapper. Never runs out.", "Penyetrum jarak dekat. Nggak pernah habis."),
            KillVerb = T("{killer} zapped {victim} like a mosquito", "{killer} nyetrum {victim} kayak nyamuk"),
            Damage = 45, FireInterval = 0.45f, Range = 2.4f, Knockback = 6f, InfiniteAmmo = true, StartsWith = true,
        },
        new WeaponDef
        {
            Id = "ketapel", Slot = 2, Kind = WeaponKind.Hitscan, Model = "slingshot", Color = "#E8C07D",
            Name = T("Village Slingshot", "Ketapel Kampung"),
            Description = T("Accurate pebble shots. Reliable all-rounder.", "Tembakan kerikil yang akurat. Andalan segala situasi."),
            KillVerb = T("{killer} sniped {victim} with a pebble", "{killer} nyentil {victim} pakai kerikil"),
            Damage = 18, FireInterval = 0.35f, SpreadDegrees = 0.6f, MaxAmmo = 100, StartAmmo = 40, PickupAmmo = 20, StartsWith = true, Knockback = 1.5f,
        },
        new WeaponDef
        {
            Id = "panci-shotgun", Slot = 3, Kind = WeaponKind.Hitscan, Model = "pan", Color = "#D0D3D4",
            Name = T("Wok Shotgun", "Shotgun Panci"),
            Description = T("Nine pellets of kitchen fury. Get close.", "Sembilan peluru amarah dapur. Dekati musuhmu."),
            KillVerb = T("{killer} panned {victim} flat", "{killer} menggepengkan {victim} pakai panci"),
            Damage = 9, Pellets = 9, SpreadDegrees = 7f, FireInterval = 0.95f, Range = 60f, Knockback = 1.2f,
            MaxAmmo = 40, StartAmmo = 10, PickupAmmo = 10,
        },
        new WeaponDef
        {
            Id = "sandal-gatling", Slot = 4, Kind = WeaponKind.Projectile, Model = "sandal", Color = "#2ECC71",
            Name = T("Flip-Flop Gatling", "Gatling Sandal Jepit"),
            Description = T("A storm of flip-flops. Lead your target.", "Hujan sandal jepit. Arahkan ke depan target."),
            KillVerb = T("{killer} flip-flopped {victim} into next week", "{killer} nyambit {victim} pakai sandal jepit"),
            Damage = 11, FireInterval = 0.09f, SpreadDegrees = 2.5f, ProjectileSpeed = 45f, ProjectileGravity = 3f, ProjectileLifetime = 2f,
            Knockback = 0.8f, MaxAmmo = 200, StartAmmo = 50, PickupAmmo = 50,
        },
        new WeaponDef
        {
            Id = "petasan", Slot = 5, Kind = WeaponKind.Projectile, Model = "firecracker", Color = "#E74C3C",
            Name = T("Bouncing Firecracker", "Petasan Banting"),
            Description = T("Bounces, then booms. Great around corners.", "Mantul dulu, baru meledak. Cocok buat pojokan."),
            KillVerb = T("{killer} turned {victim} into holiday fireworks", "{killer} meledakkan {victim} kayak petasan lebaran"),
            Damage = 30, SplashDamage = 70, SplashRadius = 4f, Knockback = 10f, FireInterval = 0.8f,
            ProjectileSpeed = 18f, ProjectileGravity = 20f, ProjectileLifetime = 2.2f, ProjectileBounces = true,
            MaxAmmo = 20, StartAmmo = 5, PickupAmmo = 5,
        },
        new WeaponDef
        {
            Id = "bazooka-sambal", Slot = 6, Kind = WeaponKind.Projectile, Model = "bazooka", Color = "#FF4500",
            Name = T("Chili Sauce Bazooka", "Bazooka Sambal"),
            Description = T("Level-30 spicy rockets. Rocket-jump with it.", "Roket sambal level 30. Bisa buat rocket jump."),
            KillVerb = T("{killer} served {victim} extra-spicy sambal", "{killer} nyuapin {victim} sambal level 30"),
            Damage = 100, SplashDamage = 90, SplashRadius = 3.5f, Knockback = 14f, FireInterval = 0.85f,
            ProjectileSpeed = 26f, ProjectileLifetime = 6f, MaxAmmo = 25, StartAmmo = 6, PickupAmmo = 6,
        },
        new WeaponDef
        {
            Id = "bambu-runcing", Slot = 7, Kind = WeaponKind.Hitscan, Model = "bamboo", Color = "#F4D03F",
            Name = T("Sacred Bamboo Spear", "Bambu Runcing Sakti"),
            Description = T("Pierces through everyone in a line.", "Menembus semua yang berbaris."),
            KillVerb = T("{killer} skewered {victim} like satay", "{killer} nyate {victim} pakai bambu runcing"),
            Damage = 85, FireInterval = 1.4f, Range = 300f, Knockback = 4f, Piercing = true,
            MaxAmmo = 20, StartAmmo = 5, PickupAmmo = 5,
        },
    ];

    public static IReadOnlyList<CharacterDef> Characters { get; } =
    [
        new CharacterDef
        {
            Id = "gatot-kaca", Category = CharacterCategory.Legend, Shape = BodyShape.Humanoid,
            Name = T("Gatot Kaca", "Gatot Kaca"),
            Description = T("Wayang hero with copper muscles and iron bones. Flies, sort of.", "Ksatria wayang berotot kawat tulang besi. Bisa terbang, kurang lebih."),
            PrimaryColor = "#1F3A93", SecondaryColor = "#F5B041", SkinColor = "#8D5524", MaxHealth = 110,
            Ability = new AbilityDef { Kind = AbilityKind.SuperJump, Cooldown = 8, Power = 2.4f, Name = T("Copper Muscles", "Otot Kawat Tulang Besi"), Description = T("Launch into the sky.", "Melesat ke angkasa.") },
            Taunts = [T("Copper muscles, iron bones!", "Otot kawat, tulang besi!"), T("Come down here and fight!", "Sini turun kalau berani!")],
        },
        new CharacterDef
        {
            Id = "si-pitung", Category = CharacterCategory.Legend, Shape = BodyShape.Humanoid,
            Name = T("Si Pitung", "Si Pitung"),
            Description = T("Betawi's legendary hero. Faster than the colonial police.", "Jagoan legendaris Betawi. Lebih cepat dari kompeni."),
            PrimaryColor = "#111111", SecondaryColor = "#C0392B", SkinColor = "#A0663C", SpeedMultiplier = 1.05f,
            Ability = new AbilityDef { Kind = AbilityKind.Dash, Cooldown = 5, Power = 1f, Name = T("Lightning Machete", "Golok Kilat"), Description = T("Dash forward in a blur.", "Melesat ke depan secepat kilat.") },
            Taunts = [T("Catch me if you can, kompeni!", "Tangkep aye kalau bisa, kompeni!"), T("Mind your manners!", "Jangan belagu, bang!")],
        },
        new CharacterDef
        {
            Id = "malin-kundang", Category = CharacterCategory.Legend, Shape = BodyShape.Humanoid,
            Name = T("Malin Kundang", "Malin Kundang"),
            Description = T("Cursed into stone for ignoring his mother. Now uses it.", "Dikutuk jadi batu karena durhaka. Sekarang dimanfaatkan."),
            PrimaryColor = "#7F8C8D", SecondaryColor = "#16A085", SkinColor = "#B07D56", MaxHealth = 120, SpeedMultiplier = 0.95f,
            Ability = new AbilityDef { Kind = AbilityKind.StoneSkin, Cooldown = 14, Duration = 5, Power = 0.35f, Name = T("Mother's Curse", "Kutukan Bunda"), Description = T("Turn to stone: take far less damage.", "Jadi batu: damage yang diterima jauh berkurang.") },
            Taunts = [T("Sorry, Mom!", "Ampun, Mak!"), T("Hard as a rock.", "Keras kayak batu.")],
        },
        new CharacterDef
        {
            Id = "tukang-ojol", Category = CharacterCategory.Profession, Shape = BodyShape.Humanoid,
            Name = T("Ride-Hailing Driver", "Tukang Ojol"),
            Description = T("Five-star rating, zero-star mercy. Always on the gas.", "Rating bintang lima, belas kasihan bintang nol. Selalu gas pol."),
            PrimaryColor = "#27AE60", SecondaryColor = "#1E8449", SkinColor = "#9C6B43", SpeedMultiplier = 1.1f,
            Ability = new AbilityDef { Kind = AbilityKind.Haste, Cooldown = 12, Duration = 6, Power = 1.5f, Name = T("Full Throttle Order", "Gas Pol Orderan"), Description = T("Move and shoot faster.", "Gerak dan tembak lebih cepat.") },
            Taunts = [T("Please leave a 5-star rating!", "Jangan lupa bintang lima ya, kak!"), T("Order complete!", "Orderan selesai!")],
        },
        new CharacterDef
        {
            Id = "emak-daster", Category = CharacterCategory.Profession, Shape = BodyShape.Humanoid,
            Name = T("Mom in a Housedress", "Emak-Emak Daster"),
            Description = T("Signals left, turns right. Feared by every motorist.", "Sein kiri belok kanan. Ditakuti semua pengendara."),
            PrimaryColor = "#8E44AD", SecondaryColor = "#F1948A", SkinColor = "#C68642", MaxHealth = 115,
            Ability = new AbilityDef { Kind = AbilityKind.Shockwave, Cooldown = 10, Power = 1.2f, Name = T("Mom's Lecture", "Omelan Emak"), Description = T("A scolding so loud it knocks everyone back.", "Omelan super keras yang mementalkan semua orang.") },
            Taunts = [T("Who left the rice cooker on?!", "Siapa yang nggak matiin magic com?!"), T("Signal left, turn right!", "Sein kiri, belok kanan!")],
        },
        new CharacterDef
        {
            Id = "satpam-komplek", Category = CharacterCategory.Profession, Shape = BodyShape.Humanoid,
            Name = T("Neighborhood Guard", "Satpam Komplek"),
            Description = T("Guards the gate with coffee and a night-watch drum.", "Menjaga gerbang dengan kopi dan kentongan."),
            PrimaryColor = "#2C3E50", SecondaryColor = "#ECF0F1", SkinColor = "#8D5524", MaxHealth = 110,
            Ability = new AbilityDef { Kind = AbilityKind.Heal, Cooldown = 15, Power = 45, Name = T("Night-Watch Coffee", "Kopi Pos Ronda"), Description = T("Restore health instantly.", "Pulihkan darah seketika.") },
            Taunts = [T("Where's your guest pass?", "Mau ke rumah siapa, mas?"), T("Ronda time!", "Waktunya ronda!")],
        },
        new CharacterDef
        {
            Id = "pocong", Category = CharacterCategory.Ghost, Shape = BodyShape.Shrouded,
            Name = T("Pocong", "Pocong"),
            Description = T("Wrapped in a shroud, gets around by hopping. Surprisingly good at it.", "Terbungkus kain kafan, jalannya lompat-lompat. Ternyata jago."),
            PrimaryColor = "#F4F6F7", SecondaryColor = "#BDC3C7", SkinColor = "#AAB7B8", JumpMultiplier = 1.25f,
            Ability = new AbilityDef { Kind = AbilityKind.SuperJump, Cooldown = 6, Power = 2.0f, Name = T("Pocong Hop", "Lompat Pocong"), Description = T("A giant spooky hop.", "Lompatan raksasa yang seram.") },
            Taunts = [T("Untie me, please!", "Tolong lepasin talinya dong!"), T("Boing boing!", "Boing boing!")],
        },
        new CharacterDef
        {
            Id = "kuntilanak", Category = CharacterCategory.Ghost, Shape = BodyShape.Ghostly,
            Name = T("Kuntilanak", "Kuntilanak"),
            Description = T("Hihihi... if it sounds far, she's right behind you.", "Hihihi... kalau suaranya jauh, dia ada di belakangmu."),
            PrimaryColor = "#FDFEFE", SecondaryColor = "#1C1C1C", SkinColor = "#E5E8E8", MaxHealth = 90, SpeedMultiplier = 1.05f,
            Ability = new AbilityDef { Kind = AbilityKind.Invisibility, Cooldown = 14, Duration = 5, Name = T("Vanishing Giggle", "Hihihi Menghilang"), Description = T("Become nearly invisible until you shoot.", "Hampir tak terlihat sampai kamu menembak.") },
            Taunts = [T("Hihihihi...", "Hihihihi..."), T("Behind you~", "Di belakangmu~")],
        },
        new CharacterDef
        {
            Id = "tuyul", Category = CharacterCategory.Ghost, Shape = BodyShape.Tiny,
            Name = T("Tuyul", "Tuyul"),
            Description = T("Tiny bald money thief. Small target, big trouble.", "Pencuri uang botak mungil. Target kecil, masalah besar."),
            PrimaryColor = "#A9CCE3", SecondaryColor = "#5D6D7E", SkinColor = "#B2BABB", MaxHealth = 75, SpeedMultiplier = 1.2f, Scale = 0.7f,
            Ability = new AbilityDef { Kind = AbilityKind.Haste, Cooldown = 10, Duration = 5, Power = 1.6f, Name = T("Pickpocket Sprint", "Kabur Nyolong"), Description = T("Zoom around at double speed.", "Lari kencang dua kali lipat.") },
            Taunts = [T("Your wallet is mine!", "Dompetmu punyaku!"), T("Hehehe, catch me!", "Hehehe, kejar dong!")],
        },
        new CharacterDef
        {
            Id = "genderuwo", Category = CharacterCategory.Ghost, Shape = BodyShape.Brute,
            Name = T("Genderuwo", "Genderuwo"),
            Description = T("Hairy forest giant. Slow, huge, and grumpy.", "Raksasa berbulu penunggu hutan. Lambat, besar, dan pemarah."),
            PrimaryColor = "#3E2723", SecondaryColor = "#C0392B", SkinColor = "#4E342E", MaxHealth = 150, SpeedMultiplier = 0.85f, Scale = 1.25f,
            Ability = new AbilityDef { Kind = AbilityKind.Shockwave, Cooldown = 9, Power = 1.5f, Name = T("Forest Roar", "Raungan Rimba"), Description = T("A roar that sends everyone flying.", "Raungan yang menerbangkan semua orang.") },
            Taunts = [T("WHO'S IN MY TREE?!", "SIAPA DI POHONKU?!"), T("Grrraaah!", "Hrrraaahh!")],
        },
        new CharacterDef
        {
            Id = "komodo", Category = CharacterCategory.Animal, Shape = BodyShape.Reptile,
            Name = T("Komodo Dragon", "Komodo"),
            Description = T("The real dragon of Indonesia. Bites first, asks never.", "Naga asli Indonesia. Gigit dulu, tanya kemudian."),
            PrimaryColor = "#5D6D3B", SecondaryColor = "#C9B458", SkinColor = "#6E7F43", MaxHealth = 130, SpeedMultiplier = 0.95f,
            Ability = new AbilityDef { Kind = AbilityKind.Dash, Cooldown = 7, Power = 1.3f, Name = T("Dragon Lunge", "Terjangan Naga"), Description = T("Lunge forward and bite whoever you hit.", "Terjang ke depan dan gigit yang tertabrak.") },
            Taunts = [T("Hsssss.", "Hsssss."), T("Pure Indonesian dragon!", "Naga asli Indonesia!")],
        },
        new CharacterDef
        {
            Id = "kucing-oren", Category = CharacterCategory.Animal, Shape = BodyShape.Cat,
            Name = T("Orange Cat", "Kucing Oren"),
            Description = T("Every neighborhood's chaos agent. Zero brain cells, all aggression.", "Agen kekacauan di setiap gang. Otak satu sel, barbar maksimal."),
            PrimaryColor = "#E67E22", SecondaryColor = "#FDEBD0", SkinColor = "#E67E22", MaxHealth = 85, SpeedMultiplier = 1.15f, Scale = 0.8f,
            Ability = new AbilityDef { Kind = AbilityKind.Haste, Cooldown = 9, Duration = 5, Power = 1.7f, Name = T("Feral Mode", "Mode Barbar"), Description = T("Become a blur of orange fur.", "Berubah jadi bayangan bulu oren.") },
            Taunts = [T("Meow. (threatening)", "Meong. (mengancam)"), T("I knocked your phone off the table.", "HP-mu kujatuhin dari meja.")],
        },
        new CharacterDef
        {
            Id = "bocil-kematian", Category = CharacterCategory.Meme, Shape = BodyShape.Tiny,
            Name = T("Deadly Kid", "Bocil Kematian"),
            Description = T("Spams the mic, never misses. Bedtime is at 9.", "Spam mic, nggak pernah meleset. Jam 9 harus tidur."),
            PrimaryColor = "#E74C3C", SecondaryColor = "#2980B9", SkinColor = "#C68642", MaxHealth = 80, SpeedMultiplier = 1.12f, Scale = 0.75f,
            Ability = new AbilityDef { Kind = AbilityKind.Shockwave, Cooldown = 8, Power = 0.9f, Name = T("Mic Spam", "Teriak Mabar"), Description = T("A scream that pushes everyone away.", "Teriakan yang mendorong semua orang.") },
            Taunts = [T("Noob! Uninstall!", "Noob! Uninstall aja!"), T("Mom said one more game.", "Kata Mama satu game lagi.")],
        },
    ];
}
