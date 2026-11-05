

## Makhluk (creeps)

Mod bisa menambah makhluk (`"creeps": [ ... ]` di `mod.json`). Modelnya dicari di `assets/models/creeps/<id>.glb` (buat dengan `tools/blender/build_characters.py`; tanpa model, game menggambar kapsul).

```json
{ "id": "babi-ngepet", "shape": "Gorilla", "name": { "en": "Wish Boar", "id": "Babi Ngepet" },
  "health": 250, "speed": 1.3, "scale": 1.2, "damage": 20, "attackRange": 2, "attackInterval": 1.0, "knockback": 12,
  "horde": 1, "lifetime": 45, "scoreReward": 2, "weight": 1, "color": "#3E2723", "bloodColor": "#8B0000",
  "killVerb": { "en": "the wish boar trampled {victim}", "id": "babi ngepet menyeruduk {victim}" } }
```

`shape`: Zombie, FloatingHead (terbang), LongLegs, Alien (melompat), Gorilla, Giant, Cyclops, Worm (menggali lalu menyembur). `weight` adalah peluang relatif terpilih.
