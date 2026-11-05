# Gameplay

*Made by Ariana Mischa Fadhila of Syubadubin Studios.*

## Controls

| Key | Action |
|---|---|
| W A S D | Move (Quake-style: bunny-hopping keeps your speed) |
| Space | Jump (hold to hop) |
| Ctrl / C | Crouch |
| Left mouse | Fire |
| Q / right mouse | Ability (*jurus*) |
| 1–7, mouse wheel | Switch weapon |
| V | First / third person |
| T | Taunt (speech bubble) |
| Tab | Scoreboard |
| Y | Chat |
| Esc | Pause menu (change fighter, camera, settings, leave) |

## The HUD

The vitals are painted like Indonesian license plates: **HP** (*Darah*) and **Armor** (*Zirah*) bottom-left, **Ammo** (*Peluru*) bottom-right with the current weapon's name tag and the slot strip above it. The ring in the middle is your ability cooldown; it turns yellow and says **READY** (*SIAP*) when it can be used. The match clock and the score line sit at the top, and the kill feed on the right tells you who *nyentil* whom.

![HUD in first person](../screenshots/game-id-pasar-malam.png)

## Fighters

| Fighter | Category | HP | Speed | Ability |
|---|---|---|---|---|
| Gatot Kaca | Legend | 110 | 100% | **Copper Muscles** — super jump |
| Si Pitung | Legend | 100 | 105% | **Lightning Machete** — dash |
| Malin Kundang | Legend | 120 | 95% | **Mother's Curse** — stone skin (35% damage taken, 5 s) |
| Ride-Hailing Driver (*Tukang Ojol*) | Profession | 100 | 110% | **Full Throttle Order** — haste (×1.5 move, faster fire) |
| Mom in a Housedress (*Emak-Emak Daster*) | Profession | 115 | 100% | **Mom's Lecture** — shockwave knockback |
| Neighborhood Guard (*Satpam Komplek*) | Profession | 110 | 100% | **Night-Watch Coffee** — instant heal |
| Pocong | Ghost | 100 | 100% (jump 125%) | **Pocong Hop** — giant hop |
| Kuntilanak | Ghost | 90 | 105% | **Vanishing Giggle** — invisibility until you shoot |
| Tuyul | Ghost | 75 | 120%, small | **Pickpocket Sprint** — haste |
| Genderuwo | Ghost | 150 | 85%, big | **Forest Roar** — big shockwave |
| Komodo Dragon | Animal | 130 | 95% | **Dragon Lunge** — dash that bites whoever it hits |
| Orange Cat (*Kucing Oren*) | Animal | 85 | 115%, small | **Feral Mode** — haste |
| Deadly Kid (*Bocil Kematian*) | Meme | 80 | 112%, small | **Mic Spam** — shockwave |

Size matters: small fighters are harder to hit and big ones get pushed around less. The example mod adds **Abang Bakso** (meatball seller, *Runaway Cart* dash).

## Weapons

| Slot | Weapon | Type | Notes |
|---|---|---|---|
| 1 | Electric Mosquito Racket | Melee | Always available, 45 damage, big knockback |
| 2 | Village Slingshot | Hitscan | Starting weapon, accurate, headshots ×1.5 |
| 3 | Wok Shotgun | Hitscan ×9 pellets | Devastating up close |
| 4 | Flip-Flop Gatling | Projectile | Rapid sandals, lead your target |
| 5 | Bouncing Firecracker | Projectile, bounces | Splash damage, great around corners |
| 6 | Chili Sauce Bazooka | Rocket | Big splash; shoot your feet while jumping to **rocket-jump** |
| 7 | Sacred Bamboo Spear | Hitscan, piercing | Goes through everyone in a line |

Self-inflicted splash damage is halved, but the knockback is full — that is how rocket jumps work.

## Pickups

| Pickup | Effect |
|---|---|
| Sweet iced tea (*Es Teh*) | +25 HP (up to max) |
| Nasi Padang | +100 HP, can overheal to 200 (decays slowly) |
| SNI Helmet (*Helm SNI*) | +50 armor (armor absorbs ⅔ of damage) |
| Safety vest (*Rompi*) | +15 armor |
| Ammo box (*Kardus amunisi*) | Ammo for every weapon you own |
| Jamu | ×3 damage for 15 s — the holder glows red |
| Weapon | The weapon plus ammo |

## Chaos physics

- **Knockback** from every hit; explosions launch players and props.
- **Props** (carts, water jugs, crates, sacks, tyres) can be kicked, shot and blown up. A prop flying faster than ~9 m/s hurts whoever it hits — the kill is credited to whoever touched it last.
- **Jump pads** (*kasur lompat*) throw you to walkways, roofs and the stage canopy.
- **Rivers and voids**: falling out of the arena kills you; whoever hit you in the last 5 seconds gets the point.

## Arenas

| Arena | Look | Highlights |
|---|---|---|
| Night Market (*Pasar Malam Ceria*) | Night, string lights | Dangdut stage with canopy, walkable stall roofs, raised walkways, ferris wheel |
| Intercity Bus Terminal (*Terminal Antar Kota*) | Golden hour | Overpass, platforms, bus roofs reached by pads |
| Rice Fields & Watch Post (*Sawah & Pos Ronda*) | Bright day | Open paddies, terraced hills, watch-post hut — sniper heaven |
| Kampung Rooftops (*Atap Kampung*) | Sunset | Rooftops of different heights, alleys, a river you can fall into |
| Independence Day Field (*Lapangan Tujuhbelasan*, mod) | Day | Greased pole with the Jamu on top |

## Modes

| Mode | Rules |
|---|---|
| **Free-for-All Brawl** (*Tawuran Bebas*) | +1 per kill, −1 per suicide. First to the score limit, or highest at the time limit. |
| **Village vs Village** (*Perang Kampung*) | Upper vs Lower Village. Team kills count; friendly fire off by default. |
| **King of the Warung** (*Raja Warung*) | Stand in the zone **alone** to earn 1 point per second. Two people inside = contested. |

After a match ends the scoreboard shows the winner for 10 seconds; hosted servers then rotate to the next arena.
