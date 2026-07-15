using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace SeamlessInteriors
{
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.PlayerShelteredFromWind))]
    public class PlayerWindShelterPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (SeamlessInteriorsMod.s_RunCompleted)
            {
                Transform playerTransform = GameManager.GetPlayerTransform();
                if (playerTransform != null && SeamlessInteriorsMod.IsPositionInsideCabin(playerTransform.position))
                {
                    __result = true;
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.IsPositionOccludedFromWind))]
    public class WindOcclusionPatch
    {
        public static bool Prefix(Vector3 pos, ref bool __result)
        {
            if (SeamlessInteriorsMod.s_RunCompleted && SeamlessInteriorsMod.IsPositionInsideCabin(pos))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}