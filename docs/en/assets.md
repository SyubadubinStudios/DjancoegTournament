# Art pipeline

*Made by Ariana Mischa Fadhila of Syubadubin Studios.*

| Asset | Tool | Location |
|---|---|---|
| Key art, 14 fighter portraits, app icon | Rodin (image generation), Indonesian truck-art style | `assets/art`, `assets/icon` |
| Cart, water jug, crate props | Rodin (text-to-3D), then cleaned up in Blender | `assets/models/source` (raw), `assets/models/props` (game-ready) |
| 14 rigged & animated fighters | Blender via Blender MCP (`tools/blender/build_characters.py`) | `assets/models/characters` |
| Announcer voice lines | Rodin (ElevenLabs v3, Bahasa Indonesia) | `assets/audio/voice` |
| Sound effects | Synthesized in code at start-up | `src/Djancoeg.Client/Audio/SoundBank.cs` |
| Fonts | Bungee, Plus Jakarta Sans, Space Mono (OFL) | `assets/fonts` |

## Fighters (Blender)

`tools/blender/build_characters.py` sculpts every fighter as a low-poly "toy figure": vertex-coloured primitives rigidly bound to a shared 12-bone rig (hips, spine, chest, head, upper/fore arms, thighs, shins), then keys four clips as NLA tracks and exports glTF:

| Clip | Frames | Notes |
|---|---|---|
| `idle` | 49 | Breathing, weapon held forward |
| `run` | 21 | Leg swing and bob (pocong hops, kuntilanak floats) |
| `shoot` | 9 | Recoil |
| `die` | 24 | Falls backwards (not looped) |

Run it from the Blender MCP (`execute_blender_code`) or headless:

```bash
blender -b --python tools/blender/build_characters.py -- all
blender -b --python tools/blender/build_characters.py -- pocong kuntilanak
```

Conventions: Z up in Blender, characters face −Y (glTF +Z = game forward), feet at the origin, 1.8 m tall. Colours are stored as raw sRGB in a `FLOAT_COLOR` attribute so they arrive exactly as authored. Every keyframe stores a full pose so clips never inherit channels from each other.

To add a fighter, add an entry to `CHARACTERS` (shape + three colours) and, if you like, accessories in `sculpt()`. The game finds `assets/models/characters/<id>.glb` automatically.

## Props (Rodin → Blender)

Rodin's raw props were 7–10 MB with 2K PBR textures. In Blender they were decimated to 1.2k–3k faces, centred, and their diffuse map resized to 512 px (normal/metal maps dropped, since the game shader uses base colour only). The game fits each model into its physics box and rotates it if its long axis differs.

## Screenshots

The client can render any screen and save it:

```bash
DjancoegTournament --shot menu --lang en --out docs/screenshots/menu-en.png
DjancoegTournament --shot game-tps --arena atap-kampung --out shot.png
```

Scenes: `menu`, `setup`, `host`, `fighters`, `browser`, `workshop`, `settings`, `credits`, `game` (first person), `game-tps`, `scoreboard`. Game scenes start a practice match with 7 bots and an autopilot, and wait until the player is alive and engaging an enemy.

Regenerate every documentation screenshot (Windows, needs a desktop session):

```powershell
pwsh scripts/screenshots.ps1
```
