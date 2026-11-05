using System.Numerics;
using System.Runtime.InteropServices;
using Djancoeg.Core.Content;
using Raylib_cs;

namespace Djancoeg.Client.Render;

/// <summary>A rigged fighter model (from the Blender pipeline) with its idle/run/shoot/die clips.</summary>
public sealed unsafe class CharacterModel
{
    public Model Model;
    public ModelAnimation* Animations;
    public int AnimationCount;
    public int Idle = -1, Run = -1, Shoot = -1, Die = -1;
    public bool HasAnimations => AnimationCount > 0;

    public int FrameCount(int clip) => clip < 0 ? 0 : Animations[clip].FrameCount;
}

/// <summary>
/// Loads and caches GPU resources: fighter models, prop models and portrait textures.
/// Everything is loaded once at startup (or when mods change) and reused for every instance.
/// </summary>
public sealed unsafe class ModelLibrary : IDisposable
{
    private readonly string _assets;
    private readonly Dictionary<string, CharacterModel?> _characters = [];
    private readonly Dictionary<string, Model?> _props = [];
    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly WorldShader _shader;

    public ModelLibrary(string assetsDir, WorldShader shader)
    {
        _assets = assetsDir;
        _shader = shader;
    }

    public CharacterModel? Character(CharacterDef def)
    {
        if (_characters.TryGetValue(def.Id, out var cached)) return cached;
        string path = def.Model is { Length: > 0 } ? Resolve(def.Model) : Path.Combine(_assets, "models", "characters", def.Id + ".glb");
        CharacterModel? result = null;
        if (File.Exists(path))
        {
            var model = Raylib.LoadModel(path);
            if (Raylib.IsModelValid(model))
            {
                for (int i = 0; i < model.MaterialCount; i++) model.Materials[i].Shader = _shader.Shader;
                result = new CharacterModel { Model = model };
                int count = 0;
                var anims = Raylib.LoadModelAnimations(path, ref count);
                result.Animations = anims;
                result.AnimationCount = count;
                for (int i = 0; i < count; i++)
                {
                    string name = AnimName(anims[i]).ToLowerInvariant();
                    if (name.Contains("idle")) result.Idle = i;
                    else if (name.Contains("run")) result.Run = i;
                    else if (name.Contains("shoot")) result.Shoot = i;
                    else if (name.Contains("die")) result.Die = i;
                }
            }
        }
        _characters[def.Id] = result;
        return result;
    }

    private static string AnimName(ModelAnimation a)
    {
        sbyte* p = a.Name;
        return Marshal.PtrToStringAnsi((nint)p) ?? "";
    }

    public Model? Prop(string kind)
    {
        if (_props.TryGetValue(kind, out var cached)) return cached;
        string path = Path.Combine(_assets, "models", "props", kind + ".glb");
        Model? result = null;
        if (File.Exists(path))
        {
            var m = Raylib.LoadModel(path);
            if (Raylib.IsModelValid(m))
            {
                for (int i = 0; i < m.MaterialCount; i++) m.Materials[i].Shader = _shader.Shader;
                result = m;
            }
        }
        _props[kind] = result;
        return result;
    }

    public Texture2D? Texture(string relativePath)
    {
        if (_textures.TryGetValue(relativePath, out var cached)) return cached;
        string path = Resolve(relativePath);
        Texture2D? result = null;
        if (File.Exists(path))
        {
            var t = Raylib.LoadTexture(path);
            Raylib.GenTextureMipmaps(ref t);
            Raylib.SetTextureFilter(t, TextureFilter.Trilinear);
            result = t;
        }
        _textures[relativePath] = result;
        return result;
    }

    public Texture2D? Portrait(CharacterDef def) => Texture(def.Portrait is { Length: > 0 } ? def.Portrait : $"art/portraits/{def.Id}.png");

    private string Resolve(string relative) => Path.IsPathRooted(relative) ? relative : Path.Combine(_assets, relative);

    /// <summary>Bounding box of a model (for fitting props into their physics box).</summary>
    public static BoundingBox Bounds(Model m) => Raylib.GetModelBoundingBox(m);

    public void Dispose()
    {
        foreach (var c in _characters.Values)
        {
            if (c is null) continue;
            if (c.AnimationCount > 0) Raylib.UnloadModelAnimations(c.Animations, c.AnimationCount);
            Raylib.UnloadModel(c.Model);
        }
        foreach (var p in _props.Values) if (p is { } m) Raylib.UnloadModel(m);
        foreach (var t in _textures.Values) if (t is { } tex) Raylib.UnloadTexture(tex);
        _characters.Clear(); _props.Clear(); _textures.Clear();
    }
}
