using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace SeamlessInteriors
{
    // Treat the player as sheltered while inside a clone.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.PlayerShelteredFromWind))]
    public class PlayerWindShelterPatch
    {
        public static bool Prefix(ref bool __result)
        {
            // Driven by the door-transition flag: no bounds tests, no raycasts,
            // so the result cannot flicker frame to frame.
            if (SeamlessInteriorsMod.s_IsPlayerInsideClone)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    // Same for the per-position wind occlusion query.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.IsPositionOccludedFromWind))]
    public class WindOcclusionPatch
    {
        public static bool Prefix(Vector3 pos, ref bool __result)
        {
            // Same flag-based approach: stable, no raycast cost.
            if (SeamlessInteriorsMod.s_IsPlayerInsideClone)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    // ─── TEMPERATURE PATCHES ───
    // The game does not recognise cloned scenes as real interiors, so it never
    // grants the indoor temperature bonus. The s_IsPlayerInsideClone flag forces it.

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Weather), nameof(Il2Cpp.Weather.CalculateCurrentTemperature))]
    public class WeatherTemperaturePatch
    {
        public static void Postfix(Il2Cpp.Weather __instance)
        {
            if (!SeamlessInteriorsMod.s_IsPlayerInsideClone) return;

            __instance.m_CurrentWindChill = 0f;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Weather), nameof(Il2Cpp.Weather.IsIndoorEnvironment))]
    public class WeatherIndoorEnvironmentPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (SeamlessInteriorsMod.s_IsPlayerInsideClone)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
