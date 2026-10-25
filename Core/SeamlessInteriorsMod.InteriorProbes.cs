using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── INTERIOR LIGHT PROBES (dark mode) ───
        // In the vanilla interior, doors, stoves and items take their indirect light from the
        // scene's baked probes. A clone sits in the region scene, which has none, so everything
        // that is not lightmapped went black. The interior's probes are read once at load and
        // fed to each renderer of the building the player is in, sampled at its clone-local
        // position (= the interior scene's own coordinates).

        private class ProbeSet
        {
            public Vector3[] Positions;
            public float[] Coeffs;   // 27 per probe: [rgb][9]
        }

        private static readonly Dictionary<string, ProbeSet> s_InteriorProbes = new Dictionary<string, ProbeSet>();

        internal static void CaptureInteriorProbes(string sceneName, UnityEngine.SceneManagement.Scene scene)
        {
            if (string.IsNullOrEmpty(sceneName) || s_InteriorProbes.ContainsKey(sceneName) || !scene.IsValid()) return;

            LightProbes probes = LightProbes.GetSharedLightProbesForScene(scene);
            if (probes == null || probes.count == 0) return;

            var pos = probes.positions;
            var baked = probes.bakedProbes;
            int n = Mathf.Min(pos.Length, baked.Length);
            if (n == 0) return;

            var set = new ProbeSet { Positions = new Vector3[n], Coeffs = new float[n * 27] };
            for (int i = 0; i < n; i++)
            {
                set.Positions[i] = pos[i];
                SphericalHarmonicsL2 sh = baked[i];
                for (int c = 0; c < 3; c++)
                    for (int k = 0; k < 9; k++)
                        set.Coeffs[i * 27 + c * 9 + k] = sh[c, k];
            }
            s_InteriorProbes[sceneName] = set;
            MelonLogger.Msg($"[INTERIOR-PROBES] {sceneName}: {n} probes read.");
        }

        // Does this room light its own objects from its baked probes (TickInteriorProbes)? Then the ambient
        // probe in force is not theirs to lose (ClonedInteriorLightingGuard.KeepExteriorProbeInForce).
        internal static bool RoomHasItsOwnProbes(SeamlessInteriorInstance inst)
        {
            return inst != null && inst.Config != null && s_InteriorProbes.ContainsKey(inst.Config.InteriorSceneBaseName ?? "");
        }

        // Inverse-distance blend of the 4 nearest probes, plain managed math.
        private static void SampleProbes(ProbeSet set, Vector3 q, float[] outCoeffs)
        {
            int[] best = { -1, -1, -1, -1 };
            float[] bd = { float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue };
            var pos = set.Positions;
            for (int i = 0; i < pos.Length; i++)
            {
                float d = (pos[i] - q).sqrMagnitude;
                if (d >= bd[3]) continue;
                int k = 3;
                while (k > 0 && d < bd[k - 1]) { bd[k] = bd[k - 1]; best[k] = best[k - 1]; k--; }
                bd[k] = d; best[k] = i;
            }

            System.Array.Clear(outCoeffs, 0, 27);
            float wsum = 0f;
            for (int k = 0; k < 4; k++)
            {
                if (best[k] < 0) continue;
                float w = 1f / (Mathf.Sqrt(bd[k]) + 0.05f);
                wsum += w;
                int o = best[k] * 27;
                for (int j = 0; j < 27; j++) outCoeffs[j] += set.Coeffs[o + j] * w;
            }
            if (wsum > 0f) for (int j = 0; j < 27; j++) outCoeffs[j] /= wsum;
        }

        private static readonly int s_SHAr = Shader.PropertyToID("unity_SHAr");
        private static readonly int s_SHAg = Shader.PropertyToID("unity_SHAg");
        private static readonly int s_SHAb = Shader.PropertyToID("unity_SHAb");
        private static readonly int s_SHBr = Shader.PropertyToID("unity_SHBr");
        private static readonly int s_SHBg = Shader.PropertyToID("unity_SHBg");
        private static readonly int s_SHBb = Shader.PropertyToID("unity_SHBb");
        private static readonly int s_SHC = Shader.PropertyToID("unity_SHC");
        private static readonly int s_ProbesOcclusion = Shader.PropertyToID("unity_ProbesOcclusion");

        // The shader-side layout Unity itself writes for a probe (vectors, no struct arrays).
        private static void WriteShVectors(MaterialPropertyBlock mpb, float[] s)
        {
            int[] a = { s_SHAr, s_SHAg, s_SHAb };
            int[] b = { s_SHBr, s_SHBg, s_SHBb };
            for (int c = 0; c < 3; c++)
            {
                int o = c * 9;
                mpb.SetVector(a[c], new Vector4(s[o + 3], s[o + 1], s[o + 2], s[o + 0] - s[o + 6]));
                mpb.SetVector(b[c], new Vector4(s[o + 4], s[o + 5], s[o + 6] * 3f, s[o + 7]));
            }
            mpb.SetVector(s_SHC, new Vector4(s[8], s[17], s[26], 1f));
            mpb.SetVector(s_ProbesOcclusion, Vector4.one);
        }

        private const int PROBE_RENDERERS_PER_FRAME = 40;
        private const float PROBE_RESCAN_SECONDS = 2f;

        private static SeamlessInteriorInstance s_ProbeTarget;
        private static Renderer[] s_ProbeQueue;
        private static int s_ProbeCursor;
        private static float s_ProbeNextScan;
        private static readonly float[] s_ProbeScratch = new float[27];
        private static MaterialPropertyBlock s_ProbeMpb;

        // Called every frame: works through the building the player is in, a slice at a time.
        internal static void TickInteriorProbes()
        {
            SeamlessInteriorInstance inst = IsDarkAtmosphereMode ? ClonedInteriorLightingGuard.Owner : null;
            if (inst == null || inst.MasterInterior == null || inst.Config == null) { s_ProbeTarget = null; return; }

            ProbeSet set;
            if (!s_InteriorProbes.TryGetValue(inst.Config.InteriorSceneBaseName ?? "", out set)) return;

            if (!ReferenceEquals(inst, s_ProbeTarget) || (s_ProbeQueue != null && s_ProbeCursor >= s_ProbeQueue.Length && Time.time >= s_ProbeNextScan))
            {
                s_ProbeTarget = inst;
                s_ProbeQueue = inst.MasterInterior.GetComponentsInChildren<Renderer>(false);
                s_ProbeCursor = 0;
                s_ProbeNextScan = Time.time + PROBE_RESCAN_SECONDS;
            }
            if (s_ProbeQueue == null || s_ProbeCursor >= s_ProbeQueue.Length) return;
            if (s_ProbeMpb == null) s_ProbeMpb = new MaterialPropertyBlock();

            Transform mt = inst.MasterInterior.transform;
            int end = System.Math.Min(s_ProbeQueue.Length, s_ProbeCursor + PROBE_RENDERERS_PER_FRAME);
            for (; s_ProbeCursor < end; s_ProbeCursor++)
            {
                Renderer r = s_ProbeQueue[s_ProbeCursor];
                try { ApplyInteriorProbe(inst, set, mt, r); }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[INTERIOR-PROBES] {inst.Config.ResolvedInstanceId}: {ex.Message}");
                    s_ProbeCursor = s_ProbeQueue.Length;
                    return;
                }
            }
        }

        private static void ApplyInteriorProbe(SeamlessInteriorInstance inst, ProbeSet set, Transform mt, Renderer r)
        {
            // Runs on 40 renderers a frame, every frame, so no Unity "== null" and no Unity vector math: each
            // leaves garbage in the game (NoGarbageMath.cs). TryCast answers a C# null when the cast fails.
            if (!IsAliveDirect(r)) return;
            if ((object)r.TryCast<MeshRenderer>() == null && (object)r.TryCast<SkinnedMeshRenderer>() == null) return;

            int id = r.GetInstanceID();
            Vector3 local = mt.InverseTransformPoint(r.bounds.m_Center);

            ProbedRenderer done;
            if (inst.ProbedRenderers.TryGetValue(id, out done))
            {
                // Already lit; only an object that moved needs a new sample.
                if (NoGarbageMath.SqrLength(NoGarbageMath.Sub(done.Local, local)) < 0.25f) return;
            }
            else
            {
                LightProbeUsage usage = r.lightProbeUsage;
                if (usage == LightProbeUsage.Off || usage == LightProbeUsage.CustomProvided) return;
                if (r.lightmapIndex >= 0 && r.lightmapIndex < 65534) return;
                if (r.HasPropertyBlock()) return;   // someone else's block: left alone, cannot be restored
                done = new ProbedRenderer { Renderer = r, Usage = usage };
            }

            SampleProbes(set, local, s_ProbeScratch);
            s_ProbeMpb.Clear();
            WriteShVectors(s_ProbeMpb, s_ProbeScratch);
            r.SetPropertyBlock(s_ProbeMpb);
            r.lightProbeUsage = LightProbeUsage.CustomProvided;

            done.Local = local;
            inst.ProbedRenderers[id] = done;
        }

        // Safehouse customization swaps each outlined renderer's block for its own, dropping our probe:
        // decorations went black (player report, 2026-10-20). Put the probe back into its block.
        private static MaterialPropertyBlock s_ProbeMergeMpb;

        // The vanilla callers inline their own helpers; Utils.ApplyPropertyBlockToRenderers is where the blocks land.
        internal static void ReapplyInteriorProbes(Il2CppSystem.Collections.Generic.List<Renderer> renderers)
        {
            ProbeSet set;
            SeamlessInteriorInstance inst = ProbeMergeTarget(out set);
            if (renderers == null || inst == null) return;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer r = renderers[i];
                ProbedRenderer done;
                if (r != null && inst.ProbedRenderers.TryGetValue(r.GetInstanceID(), out done)) MergeInteriorProbe(set, done);
            }
        }

        // Start and stop of customization touch every decoration at once.
        internal static void ReapplyAllInteriorProbes()
        {
            ProbeSet set;
            SeamlessInteriorInstance inst = ProbeMergeTarget(out set);
            if (inst == null) return;
            foreach (var done in inst.ProbedRenderers.Values)
                if (done.Renderer != null) MergeInteriorProbe(set, done);
        }

        private static SeamlessInteriorInstance ProbeMergeTarget(out ProbeSet set)
        {
            set = null;
            SeamlessInteriorInstance inst = s_ProbeTarget;
            if (inst == null || inst.MasterInterior == null || inst.Config == null || inst.ProbedRenderers.Count == 0) return null;
            if (!s_InteriorProbes.TryGetValue(inst.Config.InteriorSceneBaseName ?? "", out set)) return null;
            if (s_ProbeMergeMpb == null) s_ProbeMergeMpb = new MaterialPropertyBlock();
            return inst;
        }

        private static void MergeInteriorProbe(ProbeSet set, ProbedRenderer done)
        {
            Renderer r = done.Renderer;
            r.GetPropertyBlock(s_ProbeMergeMpb);
            SampleProbes(set, done.Local, s_ProbeScratch);
            WriteShVectors(s_ProbeMergeMpb, s_ProbeScratch);
            r.SetPropertyBlock(s_ProbeMergeMpb);
        }

        // Outdoor mode: every renderer goes back to what the scene shipped.
        internal static void RestoreInteriorProbes(SeamlessInteriorInstance inst)
        {
            if (inst == null || inst.ProbedRenderers.Count == 0) return;
            foreach (var done in inst.ProbedRenderers.Values)
            {
                Renderer r = done.Renderer;
                if (r == null) continue;
                try { r.SetPropertyBlock(null); r.lightProbeUsage = done.Usage; }
                catch { }
            }
            inst.ProbedRenderers.Clear();
            if (ReferenceEquals(inst, s_ProbeTarget)) s_ProbeTarget = null;
        }
    }

    public class LightmappedRenderer
    {
        public Renderer Renderer;
        public int Index;
        public Material[] Baked;
        public Material[] Stripped;   // null when no material had LIGHTMAP_ON
    }

    public class ProbedRenderer
    {
        public Renderer Renderer;
        public LightProbeUsage Usage;
        public Vector3 Local;
    }
}
