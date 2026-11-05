using System.Numerics;
using Djancoeg.Core.Content;
using Raylib_cs;

namespace Djancoeg.Client.Render;

/// <summary>
/// Procedural sky dome: analytic atmosphere gradient with a Mie sun halo, sun or moon disc, stars at night
/// and lit, wind-blown FBM clouds on a virtual cloud layer. Optionally samples an equirectangular
/// panorama (e.g. a CC0 HDRI exported to JPG/PNG) set per arena with <c>skyTexture</c>, keeping the clouds on top.
/// Drawn as a camera-centred sphere with depth writes off, before the world.
/// </summary>
public sealed unsafe class SkyRenderer : IDisposable
{
    private readonly Shader _shader;
    private readonly int _viewPos, _sunDir, _zenith, _horizon, _ground, _sunColor, _cloudCover, _cloudColor, _night, _time, _useTex, _skyTex, _cloudSpeed;
    private Texture2D? _panorama;
    private string? _panoramaPath;
    private readonly ModelLibrary _models;

    private const string Vs = """
        #version 330
        in vec3 vertexPosition;
        uniform mat4 mvp;
        uniform mat4 matModel;
        out vec3 worldPos;
        void main() {
            worldPos = vec3(matModel * vec4(vertexPosition, 1.0));
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string Fs = """
        #version 330
        in vec3 worldPos;
        uniform vec3 viewPos;
        uniform vec3 sunDir;      // direction the sunlight travels (points down)
        uniform vec3 zenithColor;
        uniform vec3 horizonColor;
        uniform vec3 groundColor;
        uniform vec3 sunColor;
        uniform vec3 cloudColor;
        uniform float cloudCover;  // 0..1
        uniform float cloudSpeed;
        uniform float night;       // 0 day .. 1 night (stars + moon)
        uniform float time;
        uniform int useTexture;
        uniform sampler2D skyTex;
        out vec4 finalColor;

        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        float hash3(vec3 p) { return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453); }
        float noise(vec2 p) {
            vec2 i = floor(p), f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
        }
        float fbm(vec2 p) {
            float v = 0.0, a = 0.5;
            mat2 rot = mat2(1.6, 1.2, -1.2, 1.6);
            for (int i = 0; i < 5; i++) { v += a * noise(p); p = rot * p; a *= 0.5; }
            return v;
        }
        float cloudDensity(vec2 uv) {
            float base = fbm(uv);
            float detail = fbm(uv * 3.1 + 7.3) * 0.35;
            float d = base + detail - (1.05 - cloudCover * 0.85);
            return clamp(d * 2.2, 0.0, 1.0);
        }

        void main() {
            vec3 dir = normalize(worldPos - viewPos);
            vec3 toSun = normalize(-sunDir);
            float up = dir.y;

            // Atmosphere: zenith-to-horizon gradient with a thicker, brighter band at the horizon.
            float h = clamp(up, 0.0, 1.0);
            vec3 sky = mix(horizonColor, zenithColor, pow(h, 0.45));
            float horizonGlow = exp(-abs(up) * 9.0);
            sky += horizonColor * horizonGlow * 0.25;

            if (useTexture == 1) {
                float u = atan(dir.z, dir.x) / 6.2831853 + 0.5;
                float v = acos(clamp(dir.y, -1.0, 1.0)) / 3.14159265;
                sky = texture(skyTex, vec2(u, v)).rgb;
            }

            // Sun (or moon) halo and disc - Mie-like forward scattering.
            float cosSun = max(dot(dir, toSun), 0.0);
            float halo = pow(cosSun, 8.0) * 0.35 + pow(cosSun, 64.0) * 0.6;
            sky += sunColor * halo * (1.0 - night * 0.7);
            float discSize = mix(0.99965, 0.9993, night);
            float disc = smoothstep(discSize, discSize + 0.00012, cosSun);
            vec3 discColor = mix(sunColor * 4.0, vec3(0.95, 0.95, 0.88) * 1.6, night);
            sky = mix(sky, discColor, disc * step(0.0, up));

            // Stars at night (fixed to the sky, gentle twinkle).
            if (night > 0.01 && up > 0.0) {
                vec3 cell = floor(dir * 220.0);
                float s = hash3(cell);
                float star = step(0.9965, s) * (0.6 + 0.4 * sin(time * 2.0 + s * 60.0));
                sky += vec3(star) * night * smoothstep(0.0, 0.25, up);
            }

            // Clouds on a virtual layer; lit from the sun by sampling density towards it.
            if (up > 0.01 && cloudCover > 0.001) {
                vec2 uv = dir.xz / (up + 0.08) * 0.9 + vec2(time * cloudSpeed, time * cloudSpeed * 0.35);
                float d = cloudDensity(uv);
                if (d > 0.0) {
                    vec2 sunOffset = normalize(toSun.xz + vec2(0.0001)) * 0.12;
                    float dl = cloudDensity(uv + sunOffset);
                    float light = clamp(1.0 - dl * 0.85, 0.15, 1.0);                     // self-shadowing
                    float silver = pow(cosSun, 6.0) * (1.0 - d) * 1.2;                    // silver lining
                    vec3 lit = mix(cloudColor * 0.55, cloudColor * 1.08 + sunColor * 0.25, light) + sunColor * silver;
                    lit = mix(lit, horizonColor, 1.0 - smoothstep(0.0, 0.35, up));       // aerial perspective
                    float alpha = d * smoothstep(0.01, 0.18, up);
                    sky = mix(sky, lit, alpha);
                }
            }

            // Below the horizon: haze into the ground colour (normally hidden by the arena).
            if (up < 0.0) sky = mix(horizonColor, groundColor, clamp(-up * 3.0, 0.0, 1.0));
            finalColor = vec4(sky, 1.0);
        }
        """;

    public SkyRenderer(ModelLibrary models)
    {
        _models = models;
        _shader = Raylib.LoadShaderFromMemory(Vs, Fs);
        int L(string n) => Raylib.GetShaderLocation(_shader, n);
        _viewPos = L("viewPos"); _sunDir = L("sunDir"); _zenith = L("zenithColor"); _horizon = L("horizonColor"); _ground = L("groundColor");
        _sunColor = L("sunColor"); _cloudCover = L("cloudCover"); _cloudColor = L("cloudColor"); _night = L("night"); _time = L("time");
        _useTex = L("useTexture"); _skyTex = L("skyTex"); _cloudSpeed = L("cloudSpeed");
        var s = _shader;
        s.Locs[(int)ShaderLocationIndex.MatrixModel] = Raylib.GetShaderLocation(_shader, "matModel");
    }

    /// <summary>Draws the sky around <paramref name="cam"/>. Call inside BeginMode3D, before the world.</summary>
    public void Draw(ArenaDef arena, Camera3D cam, float time)
    {
        Apply(arena, cam.Position, time);
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        Raylib.BeginShaderMode(_shader);
        if (_panorama is { } tex) Raylib.SetShaderValueTexture(_shader, _skyTex, tex);
        Raylib.DrawSphereEx(cam.Position, 400f, 24, 32, Color.White);
        Raylib.EndShaderMode();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }

    private void Apply(ArenaDef a, Vector3 viewPos, float time)
    {
        if (a.SkyTexture != _panoramaPath)
        {
            _panoramaPath = a.SkyTexture;
            _panorama = string.IsNullOrEmpty(a.SkyTexture) ? null : _models.Texture(a.SkyTexture);
        }
        Vec3(_viewPos, viewPos);
        Vec3(_sunDir, Vector3.Normalize(new Vector3(a.SunDirection[0], a.SunDirection[1], a.SunDirection[2])));
        Vec3(_zenith, Col(a.SkyTop));
        Vec3(_horizon, Col(a.SkyBottom));
        Vec3(_ground, Col(a.FogColor) * 0.6f);
        Vec3(_sunColor, Col(a.SunColor));
        Vec3(_cloudColor, Col(a.CloudColor));
        Float(_cloudCover, a.CloudCoverage);
        Float(_cloudSpeed, a.CloudSpeed);
        Float(_night, a.Night ? 1f : 0f);
        Float(_time, time);
        int use = _panorama is null ? 0 : 1;
        Raylib.SetShaderValue(_shader, _useTex, &use, ShaderUniformDataType.Int);
    }

    private static Vector3 Col(string hex) => ColorUtil.ToVector3(ColorUtil.ParseRgba(hex));
    private void Vec3(int loc, Vector3 v) => Raylib.SetShaderValue(_shader, loc, &v, ShaderUniformDataType.Vec3);
    private void Float(int loc, float v) => Raylib.SetShaderValue(_shader, loc, &v, ShaderUniformDataType.Float);

    public void Dispose() => Raylib.UnloadShader(_shader);
}
