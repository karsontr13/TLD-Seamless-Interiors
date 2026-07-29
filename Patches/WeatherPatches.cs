using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace SeamlessInteriors
{
    // Makes the game's wind system treat the player as sheltered from wind whenever they're
    // standing inside the cloned interior. Without this, the game would still apply outdoor
    // wind chill/sound to the player even though they're visually "inside" the cloned building.
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

    // Same idea as above but for arbitrary world positions (not just the player) - used by
    // the wind system's own occlusion checks (e.g. for other actors or effects) so anything
    // inside our cloned interior bounds is correctly treated as wind-occluded.
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