using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Both lighting modes are built into every clone; this switches between them, so F8 is live.
        // Outdoor mode used to destroy the dark-mode parts at build time, leaving F8 half applied.
        internal static void ApplyLightingMode(SeamlessInteriorInstance instance, bool dark)
        {
            if (instance == null || instance.MasterInterior == null) return;

            var objs = instance.DarkOnlyObjects;
            for (int i = 0; i < objs.Count; i++)
            {
                GameObject go = objs[i];
                if (go == null) continue;
                bool want = dark && instance.DarkOnlyWasActive[i];
                if (go.activeSelf != want) go.SetActive(want);
            }

            foreach (var probe in instance.DarkOnlyProbes)
                if (probe != null) probe.enabled = dark;

            var lights = instance.BigPointLights;
            for (int i = 0; i < lights.Count; i++)
                if (lights[i] != null) lights[i].enabled = dark && instance.BigPointLightWasEnabled[i];

            if (dark)
            {
                ActivateDarkModeLightingManagers(instance);
            }
            else
            {
                // Undo exactly what dark mode switched on, nothing the template had on itself.
                foreach (var m in instance.DarkEnabledManagers) if (m != null) m.enabled = false;
                foreach (var go in instance.DarkActivatedObjects) if (go != null) go.SetActive(false);
                instance.DarkEnabledManagers.Clear();
                instance.DarkActivatedObjects.Clear();
                RestoreInteriorProbes(instance);
            }

            ApplyLightmapMode(instance, dark);
            instance.AppliedDarkMode = dark;
        }

        private static bool s_LightingModeSeen;
        private static bool s_LastLightingModeDark;

        // F8 only changes the preference; every built clone follows it here on the next frame.
        internal static void TickLightingModeSwitch()
        {
            bool dark = IsDarkAtmosphereMode;
            if (s_LightingModeSeen && dark == s_LastLightingModeDark) return;
            s_LightingModeSeen = true;
            s_LastLightingModeDark = dark;

            int switched = 0;
            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || !instance.RunCompleted || instance.MasterInterior == null) continue;
                if (instance.AppliedDarkMode == dark) continue;
                try { ApplyLightingMode(instance, dark); switched++; }
                catch (System.Exception ex) { MelonLogger.Warning($"[LIGHT-MODE] {instance.Config.ResolvedInstanceId}: {ex.Message}"); }
            }

            if (switched > 0)
                MelonLogger.Msg($"[LIGHT-MODE] {(dark ? "Dark Atmosphere" : "Outdoor Lighting")} {switched} binaya hemen uygulandi.");
        }

        // The clone's own ambient, as the room was authored. In outdoor mode one that sits in a
        // dark-only part is ignored: that part used to be destroyed and nothing may read it now.
        internal static TodAmbientLight FindRoomAmbient(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return null;
            bool dark = IsDarkAtmosphereMode;

            foreach (var tod in instance.MasterInterior.GetComponentsInChildren<TodAmbientLight>(true))
            {
                if (tod == null) continue;
                if (!dark && IsUnderDarkOnlyPart(instance, tod.transform)) continue;
                return tod;
            }
            return null;
        }

        private static bool IsUnderDarkOnlyPart(SeamlessInteriorInstance instance, Transform t)
        {
            foreach (var go in instance.DarkOnlyObjects)
                if (go != null && t.IsChildOf(go.transform)) return true;
            return false;
        }
    }
}
