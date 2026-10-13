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
            DarkLightingManager dark = instance.MasterInterior.GetComponentInChildren<DarkLightingManager>(true);
            if (dark != null && dark.isActiveAndEnabled && dark.m_HasLightShafts) return set;

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

        // ─── A WINDOW SNOWED UP LETS NO SUNLIGHT IN ───
        // Owner, 2026-10-14: once snow covers more than 60% of a window, the patch of light and window shadow it
        // throws on the floor goes. Each shaft (gimble or the simpler TOD one) belongs to the room window nearest
        // it; past the line its external intensity is held at 0 and its spotlight off, and given back below it.
        private const float SNOWED_UP_WINDOW = 0.6f;

        private sealed class DimmedShaft
        {
            internal Light Light;
            internal bool LightWasOn;
            internal float External;
        }

        private static readonly Dictionary<int, DimmedShaft> s_DimmedShafts = new Dictionary<int, DimmedShaft>();
        private static readonly Dictionary<int, Component> s_DimmedShaftOwners = new Dictionary<int, Component>();
        private static string s_ShaftWindowsRoom;
        private static readonly List<Component> s_RoomShafts = new List<Component>();
        private static readonly List<int> s_RoomShaftWindow = new List<int>();

        // From the window covers' once-a-second look, for the room the player is in.
        private static void TickSnowedUpWindowShafts()
        {
            string room = WindowCoverLayers ? s_WindowPortalInstanceId : null;
            if (room != s_ShaftWindowsRoom)
            {
                RestoreSnowedUpShafts();
                s_ShaftWindowsRoom = room;
                s_RoomShafts.Clear(); s_RoomShaftWindow.Clear();
                SeamlessInteriorInstance instance;
                if (room == null || !ActiveInteriors.TryGetValue(room, out instance) || instance == null || instance.MasterInterior == null) return;
                foreach (var g in instance.MasterInterior.GetComponentsInChildren<LightShaftGimble>(true)) AddRoomShaft(g);
                foreach (var t in instance.MasterInterior.GetComponentsInChildren<LightShaftTod>(true)) AddRoomShaft(t);
            }
            if (room == null) return;

            for (int i = 0; i < s_RoomShafts.Count; i++)
            {
                var shaft = s_RoomShafts[i];
                int window = s_RoomShaftWindow[i];
                if (shaft == null || window < 0) continue;
                float frost, snow;
                GetRoomPaneCover(window, out frost, out snow);
                bool snowedUp = (InsideOutShowSnowSlab ? snow : 0f) > SNOWED_UP_WINDOW;
                int id = shaft.GetInstanceID();
                bool dimmed = s_DimmedShafts.ContainsKey(id);
                if (snowedUp) DimShaft(shaft, id, dimmed);
                else if (dimmed) RestoreShaft(id);
            }
        }

        private static void AddRoomShaft(Component shaft)
        {
            if (shaft == null) return;
            Vector3 at = shaft.transform.position;
            int best = -1; float bestSqr = float.MaxValue;
            for (int w = 0; w < s_WindowPortalOpenings.Count; w++)
            {
                float sqr = (s_WindowPortalOpenings[w].GlassCenter - at).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = w; }
            }
            s_RoomShafts.Add(shaft);
            s_RoomShaftWindow.Add(best);
        }

        // Held at 0 every look: the room's own manager writes the external intensity too.
        private static void DimShaft(Component shaft, int id, bool already)
        {
            var gimble = shaft.TryCast<LightShaftGimble>();
            var tod = gimble == null ? shaft.TryCast<LightShaftTod>() : null;
            Light light = gimble != null ? gimble.m_Light : (tod != null ? tod.m_Light : null);
            if (!already)
            {
                s_DimmedShafts[id] = new DimmedShaft
                {
                    Light = light, LightWasOn = light != null && light.enabled,
                    External = gimble != null ? gimble.m_ExternalIntensity : (tod != null ? tod.m_ExternalIntensity : 1f)
                };
                s_DimmedShaftOwners[id] = shaft;
            }
            if (gimble != null) gimble.m_ExternalIntensity = 0f;
            if (tod != null) tod.m_ExternalIntensity = 0f;
            if (light != null && light.enabled) light.enabled = false;
        }

        private static void RestoreShaft(int id)
        {
            DimmedShaft was;
            Component shaft;
            if (s_DimmedShafts.TryGetValue(id, out was) && s_DimmedShaftOwners.TryGetValue(id, out shaft) && shaft != null)
            {
                var gimble = shaft.TryCast<LightShaftGimble>();
                var tod = gimble == null ? shaft.TryCast<LightShaftTod>() : null;
                if (gimble != null) gimble.m_ExternalIntensity = was.External;
                if (tod != null) tod.m_ExternalIntensity = was.External;
                if (was.Light != null && was.LightWasOn) was.Light.enabled = true;
            }
            s_DimmedShafts.Remove(id);
            s_DimmedShaftOwners.Remove(id);
        }

        private static void RestoreSnowedUpShafts()
        {
            foreach (var id in new List<int>(s_DimmedShafts.Keys)) RestoreShaft(id);
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
