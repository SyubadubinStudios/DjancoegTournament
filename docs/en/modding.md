

## Creeps

Mods can add creeps (`"creeps": [ ... ]` in `mod.json`). The model is looked up at `assets/models/creeps/<id>.glb` (build one with `tools/blender/build_characters.py`; without it the game draws a capsule).

```json
{ "id": "babi-ngepet", "shape": "Gorilla", "name": { "en": "Wish Boar", "id": "Babi Ngepet" },
  "health": 250, "speed": 1.3, "scale": 1.2, "damage": 20, "attackRange": 2, "attackInterval": 1.0, "knockback": 12,
  "horde": 1, "lifetime": 45, "scoreReward": 2, "weight": 1, "color": "#3E2723", "bloodColor": "#8B0000",
  "killVerb": { "en": "the wish boar trampled {victim}", "id": "babi ngepet menyeruduk {victim}" } }
```

`shape`: Zombie, FloatingHead (flies), LongLegs, Alien (hops), Gorilla, Giant, Cyclops, Worm (burrows and erupts). `weight` is the relative chance of being picked.
