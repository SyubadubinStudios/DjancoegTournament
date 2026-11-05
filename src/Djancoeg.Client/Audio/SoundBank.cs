using System.Numerics;
using Raylib_cs;

namespace Djancoeg.Client.Audio;

public enum Sfx
{
    UiMove, UiClick, Slingshot, Shotgun, Gatling, Thump, Rocket, Zap, Spear, Explosion, Jump, Pad, Land, Pickup, Hit, Kill, Death, Ability, Taunt, Gong, Bounce, PropHit,
    Wind, Crunch, Whistle, Drum, Blink,
    CreepMoan, CreepWail, CreepClick, CreepChirp, CreepChest, CreepRumble, CreepRoar, Siren, Splat,
    HitHead, StepGround, StepWood, StepMetal, StepGrass, StepTile, StepWater, StepFabric,
}

/// <summary>
/// All sound effects are synthesized at startup (no audio files needed), then played through
/// small alias pools so overlapping shots don't cut each other off. Positional sounds get
/// distance attenuation and stereo pan. Optional voice lines are loaded from assets/audio/voice.
/// </summary>
public sealed unsafe class SoundBank : IDisposable
{
    private const int Rate = 22050;
    private const int Voices = 6;
    private readonly Sound[][] _pool;
    private readonly int[] _next;
    private readonly Dictionary<string, Sound> _voice = [];
    private bool _ready;
    public float Volume { get; set; } = 0.9f;

    private readonly string _storyDir;
    private Sound _story;
    private bool _hasStory;

    public SoundBank(string assetsDir)
    {
        _storyDir = Path.Combine(assetsDir, "audio", "story");
        int n = Enum.GetValues<Sfx>().Length;
        _pool = new Sound[n][];
        _next = new int[n];
        Raylib.InitAudioDevice();
        _ready = Raylib.IsAudioDeviceReady();
        if (!_ready) return;
        for (int i = 0; i < n; i++)
        {
            var samples = Synthesize((Sfx)i);
            var baseSound = FromSamples(samples);
            _pool[i] = new Sound[Voices];
            _pool[i][0] = baseSound;
            for (int k = 1; k < Voices; k++) _pool[i][k] = Raylib.LoadSoundAlias(baseSound);
        }
        var voiceDir = Path.Combine(assetsDir, "audio", "voice");
        if (Directory.Exists(voiceDir))
        {
            foreach (var f in Directory.EnumerateFiles(voiceDir))
            {
                if (!f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) continue;
                var s = Raylib.LoadSound(f);
                if (Raylib.IsSoundValid(s)) _voice[Path.GetFileNameWithoutExtension(f)] = s;
            }
        }
    }

    public void Play(Sfx sfx, float volume = 1f, float pitch = 1f, float pan = 0.5f)
    {
        if (!_ready) return;
        int i = (int)sfx;
        var s = _pool[i][_next[i]];
        _next[i] = (_next[i] + 1) % Voices;
        Raylib.SetSoundVolume(s, Math.Clamp(volume * Volume, 0, 1));
        Raylib.SetSoundPitch(s, pitch);
        Raylib.SetSoundPan(s, pan);
        Raylib.PlaySound(s);
    }

    /// <summary>3D sound relative to a listener (position + yaw).</summary>
    public void PlayAt(Sfx sfx, Vector3 pos, Vector3 listener, float listenerYaw, float volume = 1f, float pitch = 1f)
    {
        var d = pos - listener;
        float dist = d.Length();
        float att = 1f / (1f + dist * dist * 0.004f);
        if (att < 0.02f) return;
        var right = Core.Geometry.VectorMath.FlatRight(listenerYaw);
        float side = dist > 0.01f ? Vector3.Dot(d / dist, right) : 0;
        // raylib pan: 0.5 = centre, 0 = right, 1 = left.
        Play(sfx, volume * att, pitch, 0.5f - side * 0.4f);
    }

    public bool PlayVoice(string name, float volume = 1f)
    {
        if (!_ready || !_voice.TryGetValue(name, out var s)) return false;
        Raylib.SetSoundVolume(s, volume * Volume);
        Raylib.PlaySound(s);
        return true;
    }

    /// <summary>Plays one narration line from assets/audio/story (loaded on demand, replacing the previous one).</summary>
    public bool PlayStory(string fileName, float volume = 1f)
    {
        StopStory();
        if (!_ready) return false;
        var path = Path.Combine(_storyDir, fileName);
        if (!File.Exists(path)) return false;
        var s = Raylib.LoadSound(path);
        if (!Raylib.IsSoundValid(s)) return false;
        _story = s;
        _hasStory = true;
        Raylib.SetSoundVolume(s, Math.Clamp(volume * Volume, 0, 1));
        Raylib.PlaySound(s);
        return true;
    }

    public bool StoryPlaying => _hasStory && Raylib.IsSoundPlaying(_story);

    public void StopStory()
    {
        if (!_hasStory) return;
        Raylib.StopSound(_story);
        Raylib.UnloadSound(_story);
        _hasStory = false;
    }

    private static Sound FromSamples(short[] samples)
    {
        var wave = new Wave { SampleCount = (uint)samples.Length, SampleRate = Rate, SampleSize = 16, Channels = 1 };
        fixed (short* p = samples)
        {
            wave.Data = p;
            return Raylib.LoadSoundFromWave(wave); // copies the data
        }
    }

    // ------------------------------------------------------------------ synthesis

    private static short[] Synthesize(Sfx s)
    {
        var rng = new Core.Geometry.FastRandom((ulong)s * 977 + 3);
        float Noise() => rng.Range(-1f, 1f);
        return s switch
        {
            Sfx.UiMove => Tone(0.05f, t => Sq(t, 880) * Env(t, 0.05f, 0.002f) * 0.25f),
            Sfx.UiClick => Tone(0.12f, t => (Sq(t, 660) * (t < 0.05f ? 1 : 0) + Sq(t, 990) * (t >= 0.05f ? 1 : 0)) * Env(t, 0.12f, 0.003f) * 0.3f),
            Sfx.Slingshot => Tone(0.18f, t => (Sin(t, 300 - t * 900) * 0.6f + Noise() * 0.3f * Exp(t, 40)) * Env(t, 0.18f, 0.002f)),
            Sfx.Shotgun => Tone(0.45f, t => (Noise() * Exp(t, 9) + Sin(t, 70) * Exp(t, 12) * 0.8f + Sin(t, 1800 + 300 * MathF.Sin(t * 90)) * Exp(t, 6) * 0.25f) * 0.9f),
            Sfx.Gatling => Tone(0.09f, t => (Noise() * 0.5f + Sq(t, 180) * 0.4f) * Exp(t, 35)),
            Sfx.Thump => Tone(0.25f, t => (Sin(t, 160 - t * 300) + Noise() * 0.3f) * Exp(t, 14)),
            Sfx.Rocket => Tone(0.6f, t => (Noise() * 0.5f * Exp(t, 4) + Sin(t, 90 + t * 60) * 0.5f) * Env(t, 0.6f, 0.01f)),
            Sfx.Zap => Tone(0.25f, t => Sq(t, 120 + 40 * MathF.Sin(t * 300)) * 0.5f * Exp(t, 10) + Noise() * 0.2f * Exp(t, 20)),
            Sfx.Spear => Tone(0.5f, t => (Sin(t, 1400 * Exp(t, 3) + 200) * 0.5f + Noise() * 0.4f * Exp(t, 15)) * Env(t, 0.5f, 0.001f)),
            Sfx.Explosion => Tone(1.1f, t => (Noise() * Exp(t, 3.5f) + Sin(t, 48) * Exp(t, 4) * 0.9f + Sin(t, 31) * Exp(t, 2) * 0.5f) * 0.95f),
            Sfx.Jump => Tone(0.16f, t => Sin(t, 260 + t * 1400) * 0.35f * Env(t, 0.16f, 0.005f)),
            Sfx.Pad => Tone(0.45f, t => Sin(t, 180 + 600 * t + 40 * MathF.Sin(t * 60)) * 0.5f * Env(t, 0.45f, 0.005f)), // boing
            Sfx.Land => Tone(0.12f, t => (Noise() * 0.4f + Sin(t, 90)) * Exp(t, 30) * 0.6f),
            Sfx.Pickup => Tone(0.3f, t => (Sin(t, 988) * (t < 0.1f ? 1 : 0) + Sin(t, 1319) * (t >= 0.1f ? 1 : 0)) * Env(t, 0.3f, 0.003f) * 0.4f),
            Sfx.Hit => Tone(0.07f, t => Sq(t, 1600) * Exp(t, 40) * 0.25f),
            Sfx.Kill => Tone(0.5f, t => (Sin(t, 784) * (t < 0.12f ? 1 : 0) + Sin(t, 1047) * (t >= 0.12f ? 1 : 0)) * Env(t, 0.5f, 0.004f) * 0.45f),
            Sfx.Death => Tone(0.7f, t => Sq(t, 330 - t * 300) * 0.35f * Env(t, 0.7f, 0.01f)), // sad trombone-ish slide
            Sfx.Ability => Tone(0.5f, t => (Sin(t, 220 + t * 800) * 0.4f + Noise() * 0.3f * Exp(t, 6)) * Env(t, 0.5f, 0.02f)),
            Sfx.Taunt => Tone(0.35f, t => Sq(t, 520 + 200 * MathF.Sin(t * 40)) * 0.25f * Env(t, 0.35f, 0.01f)),
            Sfx.Gong => Tone(2.2f, t => (Sin(t, 110) + Sin(t, 113.5f) * 0.8f + Sin(t, 220.7f) * 0.3f) * Exp(t, 1.4f) * 0.5f), // gamelan gong with beating
            Sfx.Bounce => Tone(0.08f, t => Sin(t, 600) * Exp(t, 50) * 0.4f),
            Sfx.PropHit => Tone(0.25f, t => (Noise() * 0.6f + Sin(t, 120)) * Exp(t, 16) * 0.7f),
            Sfx.Wind => Tone(0.45f, t => Noise() * Env(t, 0.45f, 0.05f) * (0.4f + 0.3f * MathF.Sin(t * 30)) * 0.6f),
            Sfx.Crunch => Tone(0.12f, t => (Noise() * (MathF.Sin(t * 400) > 0 ? 1 : 0.3f)) * Exp(t, 25) * 0.6f),
            Sfx.Whistle => Tone(0.6f, t => Sin(t, 2800 + 120 * MathF.Sin(t * 70)) * Env(t, 0.6f, 0.01f) * 0.35f),
            Sfx.Drum => Tone(0.5f, t => (Sin(t, 330) * Exp(t, 9) + Sin(t, 330) * Exp(MathF.Max(0, t - 0.18f), 9) * (t > 0.18f ? 1 : 0)) * 0.5f), // kentongan "tok tok"
            Sfx.Blink => Tone(0.25f, t => Sin(t, 600 + t * 3000) * Env(t, 0.25f, 0.005f) * 0.35f),
            Sfx.CreepMoan => Tone(1.1f, t => (Sq(t, 95 + 12 * MathF.Sin(t * 9)) * 0.4f + Noise() * 0.15f) * Env(t, 1.1f, 0.15f)),
            Sfx.CreepWail => Tone(1.2f, t => Sin(t, 300 + 180 * MathF.Sin(t * 5)) * 0.45f * Env(t, 1.2f, 0.2f) + Noise() * 0.08f * Env(t, 1.2f, 0.1f)),
            Sfx.CreepClick => Tone(0.6f, t => (MathF.Sin(t * 90) > 0.6f ? Noise() * 0.7f : 0f) * Env(t, 0.6f, 0.01f)),
            Sfx.CreepChirp => Tone(0.5f, t => Sq(t, 900 + 700 * MathF.Sin(t * 28)) * 0.25f * Env(t, 0.5f, 0.01f)),
            Sfx.CreepChest => Tone(0.9f, t => Sin(t, 75) * (MathF.Sin(t * 50) > 0 ? 1 : 0.2f) * Exp(t, 3) * 0.8f),
            Sfx.CreepRumble => Tone(1.4f, t => (Sin(t, 38) * 0.6f + Noise() * 0.35f) * Env(t, 1.4f, 0.2f)),
            Sfx.CreepRoar => Tone(1.3f, t => (Noise() * 0.5f + Sq(t, 70 + 30 * MathF.Sin(t * 7)) * 0.4f) * Env(t, 1.3f, 0.08f)),
            Sfx.Siren => Tone(1.2f, t => Sin(t, 650 + 350 * MathF.Sin(t * 6)) * 0.3f * Env(t, 1.2f, 0.05f)),
            Sfx.Splat => Tone(0.35f, t => (Noise() * 0.7f + Sin(t, 110 - t * 120)) * Exp(t, 12) * 0.7f),
            Sfx.HitHead => Tone(0.16f, t => (Sin(t, 2400) * 0.5f + Sin(t, 3600) * 0.3f) * Exp(t, 22) * 0.45f),
            Sfx.StepGround => Tone(0.09f, t => (Noise() * 0.5f + Sin(t, 70)) * Exp(t, 45) * 0.5f),
            Sfx.StepWood => Tone(0.1f, t => (Sin(t, 210 - t * 400) + Noise() * 0.2f) * Exp(t, 38) * 0.55f),
            Sfx.StepMetal => Tone(0.14f, t => (Sin(t, 900) * 0.4f + Sin(t, 1370) * 0.3f + Noise() * 0.2f) * Exp(t, 30) * 0.45f),
            Sfx.StepGrass => Tone(0.12f, t => Noise() * Env(t, 0.12f, 0.03f) * 0.35f),
            Sfx.StepTile => Tone(0.07f, t => (Sq(t, 1200) * 0.3f + Noise() * 0.3f) * Exp(t, 60) * 0.5f),
            Sfx.StepWater => Tone(0.2f, t => (Noise() * 0.5f + Sin(t, 300 + 500 * t)) * Env(t, 0.2f, 0.02f) * 0.4f),
            Sfx.StepFabric => Tone(0.1f, t => (Noise() * 0.6f + Sin(t, 110)) * Exp(t, 35) * 0.4f),
            _ => Tone(0.1f, _ => 0),
        };
    }

    private static short[] Tone(float seconds, Func<float, float> f)
    {
        int n = (int)(seconds * Rate);
        var data = new short[n];
        for (int i = 0; i < n; i++)
        {
            float v = Math.Clamp(f(i / (float)Rate), -1f, 1f);
            data[i] = (short)(v * 30000);
        }
        return data;
    }

    private static float Sin(float t, float hz) => MathF.Sin(MathF.Tau * hz * t);
    private static float Sq(float t, float hz) => MathF.Sin(MathF.Tau * hz * t) >= 0 ? 0.7f : -0.7f;
    private static float Exp(float t, float k) => MathF.Exp(-t * k);
    private static float Env(float t, float len, float attack) => (t < attack ? t / attack : 1f) * MathF.Max(0, 1f - t / len);

    public void Dispose()
    {
        if (!_ready) return;
        StopStory();
        for (int i = 0; i < _pool.Length; i++)
        {
            if (_pool[i] is null) continue;
            for (int k = 1; k < Voices; k++) Raylib.UnloadSoundAlias(_pool[i][k]);
            Raylib.UnloadSound(_pool[i][0]);
        }
        foreach (var v in _voice.Values) Raylib.UnloadSound(v);
        Raylib.CloseAudioDevice();
        _ready = false;
    }
}
