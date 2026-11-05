using System.Numerics;
using Djancoeg.Core.Content;
using Raylib_cs;

namespace Djancoeg.Client.Render;

/// <summary>
/// One shader for the whole world: sun + ambient + up to 16 point lights, exponential fog,
/// and procedural surface patterns (planks, tarp stripes, tiles, corrugated roofs, rippling
/// water, neon) selected per vertex so an entire arena is a single draw call.
/// </summary>
public sealed unsafe class WorldShader : IDisposable
{
    public const int MaxLights = 16;
    public Shader Shader { get; }
    private readonly int _viewPos, _sunDir, _sunColor, _ambient, _fogColor, _fogDensity, _lightCount, _lightPos, _lightColor, _pattern, _emissive, _time, _skyColor;
    private readonly Vector4[] _lp = new Vector4[MaxLights];
    private readonly Vector3[] _lc = new Vector3[MaxLights];
    private int _count;

    private const string Vs = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec3 vertexNormal;
        in vec4 vertexColor;
        uniform mat4 mvp;
        uniform mat4 matModel;
        uniform mat4 matNormal;
        out vec3 fragPos;
        out vec3 fragNormal;
        out vec4 fragColor;
        out vec2 fragTex;
        void main() {
            fragPos = vec3(matModel * vec4(vertexPosition, 1.0));
            fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
            fragColor = vertexColor;
            fragTex = vertexTexCoord;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string Fs = """
        #version 330
        in vec3 fragPos;
        in vec3 fragNormal;
        in vec4 fragColor;
        in vec2 fragTex;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform vec3 viewPos;
        uniform vec3 sunDir;
        uniform vec3 sunColor;
        uniform vec3 ambient;
        uniform vec3 skyColor;
        uniform vec3 fogColor;
        uniform float fogDensity;
        uniform int lightCount;
        uniform vec4 lightPos[16];
        uniform vec3 lightColor[16];
        uniform int patternMode;
        uniform float emissive;
        uniform float time;
        out vec4 finalColor;

        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        float noise(vec2 p) {
            vec2 i = floor(p), f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
        }

        void main() {
            vec4 base = fragColor * colDiffuse;
            vec3 n = normalize(fragNormal);
            float glow = emissive;
            if (patternMode == 1) {
                int kind = int(fragTex.x + 0.5);
                vec2 uv = abs(n.y) > 0.5 ? fragPos.xz : (abs(n.x) > 0.5 ? fragPos.zy : fragPos.xy);
                float k = 1.0;
                if (kind == 0) { k = 0.9 + 0.2 * noise(uv * 3.0) + 0.05 * noise(uv * 17.0); }                       // ground
                else if (kind == 1) { k = 0.93 + 0.08 * noise(uv * 6.0); if (fract(fragPos.y * 0.5) < 0.04) k *= 0.85; } // wall
                else if (kind == 2) { float pl = fract(dot(uv, vec2(1.0, 0.0)) * 3.2); k = (pl < 0.06 ? 0.7 : 1.0) * (0.92 + 0.1 * noise(uv * vec2(1.5, 14.0))); } // wood
                else if (kind == 3) { k = 0.9 + 0.12 * noise(uv * vec2(40.0, 2.0)); vec2 g = fract(uv); if (g.x < 0.02 || g.y < 0.02) k *= 0.75; }   // metal
                else if (kind == 4) { k = fract((uv.x + uv.y) * 1.2) < 0.5 ? 1.0 : 0.0; base.rgb = mix(base.rgb, vec3(0.97, 0.95, 0.9), (1.0 - k) * 0.85); k = 1.0; } // tarp stripes
                else if (kind == 5) { float w = sin(uv.x * 3.0 + time * 1.7) * sin(uv.y * 2.3 - time * 1.3); base.rgb *= 0.85 + 0.25 * w; glow = 0.25; } // water
                else if (kind == 6) { k = 0.8 + 0.35 * noise(uv * 1.3) + 0.1 * noise(uv * 12.0); }                    // grass
                else if (kind == 7) { vec2 g = fract(uv * 2.0); k = (g.x < 0.05 || g.y < 0.05) ? 0.72 : 0.98 + 0.04 * hash(floor(uv * 2.0)); } // tile
                else if (kind == 8) { glow = 0.85; }                                                                   // neon
                else if (kind == 9) { k = 0.82 + 0.18 * (0.5 + 0.5 * sin(uv.x * 18.0)); }                             // corrugated roof
                base.rgb *= k;
            } else {
                base *= texture(texture0, fragTex);
            }

            float diff = max(dot(n, -normalize(sunDir)), 0.0);
            float hemi = 0.5 + 0.5 * n.y;
            // Neutral floor keeps authored colours readable even in tinted night arenas.
            vec3 light = vec3(0.30) + ambient * mix(0.45, 0.8, hemi) + skyColor * 0.08 * hemi + sunColor * diff * 0.7;
            for (int i = 0; i < 16; i++) {
                if (i >= lightCount) break;
                vec3 d = lightPos[i].xyz - fragPos;
                float dist = length(d);
                float att = clamp(1.0 - dist / lightPos[i].w, 0.0, 1.0);
                att *= att;
                light += lightColor[i] * att * (0.3 + 0.7 * max(dot(n, d / max(dist, 0.001)), 0.0));
            }
            vec3 col = mix(base.rgb * light, base.rgb * 1.25, glow);
            float dist = length(viewPos - fragPos);
            float fog = 1.0 - exp(-pow(dist * fogDensity, 1.6));
            col = mix(col, fogColor, clamp(fog, 0.0, 0.92));
            finalColor = vec4(col, base.a);
        }
        """;

    public WorldShader()
    {
        Shader = Raylib.LoadShaderFromMemory(Vs, Fs);
        _viewPos = Loc("viewPos"); _sunDir = Loc("sunDir"); _sunColor = Loc("sunColor"); _ambient = Loc("ambient");
        _fogColor = Loc("fogColor"); _fogDensity = Loc("fogDensity"); _lightCount = Loc("lightCount");
        _lightPos = Loc("lightPos"); _lightColor = Loc("lightColor"); _pattern = Loc("patternMode"); _emissive = Loc("emissive");
        _time = Loc("time"); _skyColor = Loc("skyColor");
        var s = Shader;
        s.Locs[(int)ShaderLocationIndex.MatrixModel] = Raylib.GetShaderLocation(Shader, "matModel");
        s.Locs[(int)ShaderLocationIndex.MatrixNormal] = Raylib.GetShaderLocation(Shader, "matNormal");
    }

    private int Loc(string n) => Raylib.GetShaderLocation(Shader, n);

    public void SetEnvironment(ArenaDef arena)
    {
        Vec3(_sunDir, Vector3.Normalize(new Vector3(arena.SunDirection[0], arena.SunDirection[1], arena.SunDirection[2])));
        Vec3(_sunColor, ColorUtil.ToVector3(ColorUtil.ParseRgba(arena.SunColor)));
        Vec3(_ambient, ColorUtil.ToVector3(ColorUtil.ParseRgba(arena.AmbientColor)));
        Vec3(_fogColor, ColorUtil.ToVector3(ColorUtil.ParseRgba(arena.FogColor)));
        Vec3(_skyColor, ColorUtil.ToVector3(ColorUtil.ParseRgba(arena.SkyTop)));
        float d = arena.FogDensity;
        Raylib.SetShaderValue(Shader, _fogDensity, &d, ShaderUniformDataType.Float);
    }

    public void SetFrame(Vector3 viewPos, float time)
    {
        Vec3(_viewPos, viewPos);
        Raylib.SetShaderValue(Shader, _time, &time, ShaderUniformDataType.Float);
    }

    public void BeginLights() => _count = 0;

    public void AddLight(Vector3 pos, float radius, Vector3 color)
    {
        if (_count >= MaxLights) return;
        _lp[_count] = new Vector4(pos, radius);
        _lc[_count] = color;
        _count++;
    }

    public void CommitLights()
    {
        int n = _count;
        Raylib.SetShaderValue(Shader, _lightCount, &n, ShaderUniformDataType.Int);
        if (n == 0) return;
        fixed (Vector4* p = _lp) Raylib.SetShaderValueV(Shader, _lightPos, p, ShaderUniformDataType.Vec4, n);
        fixed (Vector3* c = _lc) Raylib.SetShaderValueV(Shader, _lightColor, c, ShaderUniformDataType.Vec3, n);
    }

    public void SetPattern(bool arena)
    {
        int v = arena ? 1 : 0;
        Raylib.SetShaderValue(Shader, _pattern, &v, ShaderUniformDataType.Int);
    }

    public void SetEmissive(float e) => Raylib.SetShaderValue(Shader, _emissive, &e, ShaderUniformDataType.Float);

    private void Vec3(int loc, Vector3 v) => Raylib.SetShaderValue(Shader, loc, &v, ShaderUniformDataType.Vec3);

    public void Dispose() => Raylib.UnloadShader(Shader);
}
