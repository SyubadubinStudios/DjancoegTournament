using System.Numerics;
using Djancoeg.Core.Content;
using Raylib_cs;

namespace Djancoeg.Client.Render;

/// <summary>
/// Bakes all arena boxes into a few static vertex-coloured meshes (one draw call per 16k vertices).
/// The surface kind travels in texcoord.x so the world shader can paint planks, stripes and tiles.
/// </summary>
public sealed unsafe class ArenaRenderer : IDisposable
{
    private const int MaxVertsPerChunk = 16000;
    private readonly List<Model> _chunks = [];
    public ArenaDef Arena { get; }
    public int VertexCount { get; private set; }

    public ArenaRenderer(ArenaDef arena, WorldShader shader)
    {
        Arena = arena;
        var boxes = arena.Boxes;
        int i = 0;
        while (i < boxes.Count)
        {
            int take = Math.Min(boxes.Count - i, MaxVertsPerChunk / 24);
            _chunks.Add(BuildChunk(boxes, i, take, shader.Shader));
            i += take;
        }
    }

    private Model BuildChunk(List<ArenaBox> boxes, int start, int count, Shader shader)
    {
        int faces = count * 6;
        var mesh = new Mesh { VertexCount = faces * 4, TriangleCount = faces * 2 };
        mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocColors(); mesh.AllocIndices();
        var v = mesh.VerticesAs<Vector3>();
        var n = mesh.NormalsAs<Vector3>();
        var t = mesh.TexCoordsAs<Vector2>();
        var c = mesh.ColorsAs<Color>();
        var idx = mesh.IndicesAs<ushort>();
        int vi = 0, ii = 0;
        for (int b = start; b < start + count; b++)
        {
            var box = boxes[b];
            var mn = box.MinV; var mx = box.MaxV;
            var col = Ui.Theme.Rgba(ColorUtil.ParseRgba(box.Color));
            float kind = (float)box.Surface;
            for (int f = 0; f < 6; f++)
            {
                Vector3 nrm; Vector3 a, b1, c1, d;
                switch (f)
                {
                    case 0: nrm = Vector3.UnitY; a = new(mn.X, mx.Y, mn.Z); b1 = new(mn.X, mx.Y, mx.Z); c1 = new(mx.X, mx.Y, mx.Z); d = new(mx.X, mx.Y, mn.Z); break;
                    case 1: nrm = -Vector3.UnitY; a = new(mn.X, mn.Y, mn.Z); b1 = new(mx.X, mn.Y, mn.Z); c1 = new(mx.X, mn.Y, mx.Z); d = new(mn.X, mn.Y, mx.Z); break;
                    case 2: nrm = Vector3.UnitX; a = new(mx.X, mn.Y, mn.Z); b1 = new(mx.X, mx.Y, mn.Z); c1 = new(mx.X, mx.Y, mx.Z); d = new(mx.X, mn.Y, mx.Z); break;
                    case 3: nrm = -Vector3.UnitX; a = new(mn.X, mn.Y, mn.Z); b1 = new(mn.X, mn.Y, mx.Z); c1 = new(mn.X, mx.Y, mx.Z); d = new(mn.X, mx.Y, mn.Z); break;
                    case 4: nrm = Vector3.UnitZ; a = new(mn.X, mn.Y, mx.Z); b1 = new(mx.X, mn.Y, mx.Z); c1 = new(mx.X, mx.Y, mx.Z); d = new(mn.X, mx.Y, mx.Z); break;
                    default: nrm = -Vector3.UnitZ; a = new(mn.X, mn.Y, mn.Z); b1 = new(mn.X, mx.Y, mn.Z); c1 = new(mx.X, mx.Y, mn.Z); d = new(mx.X, mn.Y, mn.Z); break;
                }
                // Cheap baked ambient occlusion: darken side faces near the ground and bottoms.
                var shade = f == 1 ? 0.55f : f >= 2 ? 0.92f : 1f;
                var fc = new Color((byte)(col.R * shade), (byte)(col.G * shade), (byte)(col.B * shade), col.A);
                v[vi] = a; v[vi + 1] = b1; v[vi + 2] = c1; v[vi + 3] = d;
                for (int k = 0; k < 4; k++) { n[vi + k] = nrm; t[vi + k] = new Vector2(kind, 0); c[vi + k] = fc; }
                idx[ii++] = (ushort)vi; idx[ii++] = (ushort)(vi + 1); idx[ii++] = (ushort)(vi + 2);
                idx[ii++] = (ushort)vi; idx[ii++] = (ushort)(vi + 2); idx[ii++] = (ushort)(vi + 3);
                vi += 4;
            }
        }
        VertexCount += vi;
        Raylib.UploadMesh(ref mesh, false);
        var model = Raylib.LoadModelFromMesh(mesh);
        model.Materials[0].Shader = shader;
        return model;
    }

    public void Draw(WorldShader shader)
    {
        shader.SetPattern(true);
        foreach (var m in _chunks) Raylib.DrawModel(m, Vector3.Zero, 1f, Color.White);
        shader.SetPattern(false);
    }

    public void Dispose()
    {
        foreach (var m in _chunks) Raylib.UnloadModel(m);
        _chunks.Clear();
    }
}
