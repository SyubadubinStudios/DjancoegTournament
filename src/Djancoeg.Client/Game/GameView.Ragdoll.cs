using System.Numerics;
using System.Runtime.InteropServices;
using Djancoeg.Client.Audio;
using Djancoeg.Client.Render;
using Djancoeg.Client.Ui;
using Djancoeg.Core.Content;
using Djancoeg.Core.Simulation;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Djancoeg.Client.Game;

/// <summary>
/// v2.2 per-limb ragdolls: fighter corpses are driven by a <see cref="Ragdoll"/> and skinned through a
/// one-frame synthetic animation whose bone poses are rewritten before each draw.
/// </summary>
public sealed unsafe partial class GameView
{
    private readonly Ragdoll[] _ragdolls = InitRagdolls();
    private readonly Dictionary<CharacterModel, int[]?> _rigMaps = [];
    private Transform** _ragFrames;
    private const int MaxBones = 64;
    private readonly Vector3[] _ragHeads = new Vector3[12];
    private readonly Quaternion[] _ragRots = new Quaternion[12];
    private readonly float[] _ragLastCenterY = new float[40];

    private static Ragdoll[] InitRagdolls()
    {
        var r = new Ragdoll[40];
        for (int i = 0; i < r.Length; i++) r[i] = new Ragdoll();
        return r;
    }

    /// <summary>Bone indices for <see cref="Ragdoll.BoneNames"/> in this model, or null if it doesn't use the shared fighter rig.</summary>
    private int[]? RigMap(CharacterModel m)
    {
        if (_rigMaps.TryGetValue(m, out var map)) return map;
        map = null;
        var model = m.Model;
        if (model.BoneCount is > 0 and <= MaxBones && model.BindPose != null)
        {
            var found = new int[Ragdoll.BoneNames.Length];
            bool ok = true;
            for (int n = 0; n < Ragdoll.BoneNames.Length && ok; n++)
            {
                found[n] = -1;
                for (int b = 0; b < model.BoneCount; b++)
                {
                    string name = Marshal.PtrToStringAnsi((nint)model.Bones[b].Name) ?? "";
                    if (name == Ragdoll.BoneNames[n]) { found[n] = b; break; }
                }
                // The bind pose must match the rig the ragdoll was built for.
                ok = found[n] >= 0 && Vector3.Distance(model.BindPose[found[n]].Translation, Ragdoll.BoneHeads[n]) < 0.06f;
            }
            if (ok) map = found;
        }
        _rigMaps[m] = map;
        return map;
    }

    /// <summary>Same layout as raylib's ModelAnimation (boneCount, frameCount, bones, framePoses, name[32]).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RawAnimation
    {
        public int BoneCount;
        public int FrameCount;
        public BoneInfo* Bones;
        public Transform** FramePoses;
        public fixed sbyte Name[32];
    }

    private static readonly bool LayoutMatches = sizeof(RawAnimation) == sizeof(ModelAnimation);

    private bool TryStartRagdoll(int slot, CharacterModel? model, Vector3 feet, float yaw, float scale, Vector3 velocity, Vector3 kick)
    {
        if (!LayoutMatches || model is null || RigMap(model) is null) return false;
        _ragdolls[slot].Reset(feet, yaw, scale, velocity, kick, _time * 13f + slot);
        _ragLastCenterY[slot] = _ragdolls[slot].Center.Y;
        return true;
    }

    private void StepRagdoll(int slot, ref Corpse c, float dt)
    {
        var rag = _ragdolls[slot];
        var col = _session.Client.Collision;
        rag.Step(dt, col);
        float cy = rag.Center.Y;
        float vy = (cy - _ragLastCenterY[slot]) / MathF.Max(dt, 1e-4f);
        // Impact: the body was falling fast and just stopped.
        if (c.Vel.Y < -5f && vy > -1f)
        {
            _fx.Dust(rag.Center - new Vector3(0, 0.3f * c.Scale, 0), 6, 1.6f);
            if (_app.Settings.Blood && c.Bounces < 3)
                _fx.SplatBelow(rag.Center, 0.7f * c.Scale, new Color((byte)(c.Blood.R * 0.6f), (byte)(c.Blood.G * 0.6f), (byte)(c.Blood.B * 0.6f), (byte)220), 30f);
            _app.Sounds.PlayAt(Sfx.Land, rag.Center, ListenerPos, _session.Yaw, 0.7f, 0.7f);
            c.Bounces++;
        }
        c.Vel.Y = vy;
        c.Pos = rag.Center;
        _ragLastCenterY[slot] = cy;
        if (col is not null && rag.LowestY < col.KillY) c.Active = false;
    }

    private void DrawRagdoll(int slot, in Corpse c, CharacterModel m, Color tint)
    {
        var map = RigMap(m)!;
        var model = m.Model;
        if (_ragFrames == null)
        {
            _ragFrames = (Transform**)NativeMemory.AllocZeroed((nuint)sizeof(Transform*));
            _ragFrames[0] = (Transform*)NativeMemory.AllocZeroed((nuint)(sizeof(Transform) * MaxBones));
        }
        var poses = _ragFrames[0];
        // Bones the ragdoll doesn't drive stay in their bind pose.
        for (int b = 0; b < model.BoneCount; b++) poses[b] = model.BindPose[b];
        var rag = _ragdolls[slot];
        rag.Pose(_ragHeads, _ragRots);
        float inv = 1f / MathF.Max(0.01f, c.Scale);
        for (int n = 0; n < map.Length; n++)
        {
            int b = map[n];
            poses[b].Translation = _ragHeads[n] * inv;
            poses[b].Rotation = Quaternion.Normalize(_ragRots[n] * model.BindPose[b].Rotation);
        }
        // Raylib-cs exposes ModelAnimation's fields read-only; build it through an identical native layout.
        var raw = new RawAnimation { BoneCount = model.BoneCount, FrameCount = 1, Bones = model.Bones, FramePoses = _ragFrames };
        UpdateModelAnimation(model, *(ModelAnimation*)&raw, 0);
        DrawModelEx(model, Vector3.Zero, Vector3.UnitY, 0, new Vector3(c.Scale), tint);
    }

    /// <summary>Screenshot helper: throws a few fighter ragdolls in front of the local player.</summary>
    public void DebugThrowRagdolls()
    {
        if (_latest is null || !_latest.TryGetPlayer(_session.Client.LocalId, out var me)) return;
        var fwd = new Vector3(MathF.Sin(me.Yaw), 0, MathF.Cos(me.Yaw));
        var right = new Vector3(fwd.Z, 0, -fwd.X);
        var chars = _session.Content.Characters;
        for (int k = 0; k < 4; k++)
        {
            var cd = chars[(k * 5 + 1) % chars.Count];
            var model = _app.Models.Character(cd);
            var feet = me.Position + fwd * (3.2f + k * 0.4f) + right * ((k - 1.5f) * 1.6f) + new Vector3(0, 0.6f, 0);
            int slot = SpawnCorpse(-1, model, cd.Scale, feet, Vector3.Zero, me.Yaw + MathF.PI + k, Theme.Rgba(ColorUtil.ParseRgba(cd.PrimaryColor)), new Color(140, 8, 10, 255));
            var kick = fwd * (1.5f + k * 0.5f) + right * (k % 2 == 0 ? 1.2f : -1.2f) + new Vector3(0, 1.5f, 0);
            _corpses[slot].Rag = TryStartRagdoll(slot, model, feet, me.Yaw + MathF.PI + k, cd.Scale, Vector3.Zero, kick);
        }
    }

    private void FreeRagdollFrames()
    {
        if (_ragFrames == null) return;
        NativeMemory.Free(_ragFrames[0]);
        NativeMemory.Free(_ragFrames);
        _ragFrames = null;
    }
}
