using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── WINDOW LIGHT SHAFTS ───
        // Outdoor lighting removes a clone's shaft meshes and its InteriorLightingManager; the gimbles aiming the window
        // spotlights then fail on the missing meshes and stay at daytime strength. SI keeps them as the manager would.

        private const float SHAFT_MESH_CHECK_SECONDS = 1f;

        private sealed class LightShaftSet
        {
            internal GameObject Clone;
            internal readonly List<LightShaftGimble> Gimbles = new List<LightShaftGimble>();
            internal readonly List<LightShaftTod> Tods = new List<LightShaftTod>();
            internal float NextMeshCheck;
        }

        private static readonly Dictionary<string, LightShaftSet> s_LightShafts = new Dictionary<string, LightShaftSet>();

        internal static void TickWindowLightShafts()
        {
            if (IsDarkAtmosphereMode || ActiveInteriors.Count == 0) return;

            float intensity = -1f;
            foreach (var pair in ActiveInteriors)
            {
                SeamlessInteriorInstance instance = pair.Value;
                if (instance == null || instance.MasterInterior == null || !instance.MasterInterior.activeSelf) continue;

                LightShaftSet set = LightShaftsOf(pair.Key, instance);
                if (set.Gimbles.Count == 0 && set.Tods.Count == 0) continue;
                if (intensity < 0f) intensity = WindowShaftTodIntensity();

                if (Time.realtimeSinceStartup >= set.NextMeshCheck)
                {
                    set.NextMeshCheck = Time.realtimeSinceStartup + SHAFT_MESH_CHECK_SECONDS;
                    foreach (LightShaftGimble gimble in set.Gimbles) DropRemovedShaftMeshes(gimble);
                }

                foreach (LightShaftGimble gimble in set.Gimbles)
                {
                    if (gimble == null) continue;
                    gimble.m_FollowTod = false;
                    gimble.m_TodIntensity = intensity;
                }
                foreach (LightShaftTod tod in set.Tods)
                {
                    if (tod == null) continue;
                    tod.followTod = false;
                    tod.m_TodIntensity = intensity;
                }
            }
        }

        // Empty for a clone whose own manager still runs: it sets its shafts itself.
        private static LightShaftSet LightShaftsOf(string id, SeamlessInteriorInstance instance)
        {
            LightShaftSet set;
            if (s_LightShafts.TryGetValue(id, out set) && set.Clone == instance.MasterInterior) return set;

            set = new LightShaftSet { Clone = instance.MasterInterior };
            s_LightShafts[id] = set;

            InteriorLightingManager manager = instance.MasterInterior.GetComponentInChildren<InteriorLightingManager>(true);
            if (manager != null && manager.isActiveAndEnabled) return set;

            foreach (LightShaftGimble gimble in instance.MasterInterior.GetComponentsInChildren<LightShaftGimble>(true))
                if (gimble != null) set.Gimbles.Add(gimble);
            foreach (LightShaftTod tod in instance.MasterInterior.GetComponentsInChildren<LightShaftTod>(true))
                if (tod != null) set.Tods.Add(tod);
            return set;
        }

        // Keeps only the shaft meshes that still exist, so the gimble's Update reaches its spotlight again.
        private static void DropRemovedShaftMeshes(LightShaftGimble gimble)
        {
            if (gimble == null) return;
            Il2CppReferenceArray<Renderer> meshes = gimble.m_LightShaftRenderer;
            if (meshes == null) return;

            var alive = new List<Renderer>();
            for (int i = 0; i < meshes.Length; i++)
                if (meshes[i] != null) alive.Add(meshes[i]);
            if (alive.Count == meshes.Length) return;

            var kept = new Il2CppReferenceArray<Renderer>(alive.Count);
            for (int i = 0; i < alive.Count; i++) kept[i] = alive[i];
            gimble.m_LightShaftRenderer = kept;
        }

        // InteriorLightingManager.GetTimeOfDayIntensity: rising through dawn, full by day, falling through dusk, none at night.
        private static float WindowShaftTodIntensity()
        {
            UniStormWeatherSystem uniStorm = GameManager.GetUniStorm();
            if (uniStorm == null) return 1f;

            TODBlendState state = uniStorm.GetTODBlendState();
            switch (state)
            {
                case TODBlendState.DawnToMorning: return uniStorm.GetTODBlendPercent(state);
                case TODBlendState.MorningToMidday:
                case TODBlendState.MiddayToAfternoon: return 1f;
                case TODBlendState.AfternoonToDusk: return 1f - uniStorm.GetTODBlendPercent(state);
                default: return 0f;
            }
        }
    }
}
