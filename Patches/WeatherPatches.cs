using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using System.Linq;

namespace SeamlessInteriors
{
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.PlayerShelteredFromWind))]
    public class PlayerWindShelterPatch
    {
        public static bool Prefix(ref bool __result)
        {
            Transform playerTransform = GameManager.GetPlayerTransform();

            // Eğer oyuncu herhangi bir aktif evin içindeyse rüzgardan korunur
            if (playerTransform != null && SeamlessInteriorsMod.IsPositionInsideAnyInstance(playerTransform.position))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.IsPositionOccludedFromWind))]
    public class WindOcclusionPatch
    {
        public static bool Prefix(Vector3 pos, ref bool __result)
        {
            // Verilen koordinat herhangi bir aktif evin içindeyse rüzgar engellenir
            if (SeamlessInteriorsMod.IsPositionInsideAnyInstance(pos))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
