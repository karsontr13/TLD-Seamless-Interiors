using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace SeamlessInteriors
{
    // The interior scene we load additively still contains its own copy of the GameManager
    // prefab (a singleton the game expects to exist exactly once). While cloning is active,
    // if a GameManager wakes up in the "CampOffice" scene specifically, it's this unwanted
    // duplicate, so we destroy it immediately instead of letting it conflict with the real
    // GameManager already running in the main scene.
    [HarmonyLib.HarmonyPatch(typeof(GameManager), "Awake")]
    public class PreventFakeManagerPatch
    {
        public static bool Prefix(GameManager __instance)
        {
            if (SeamlessInteriorsMod.s_IsCloningRoutineActive && __instance.gameObject.scene.name == "CampOffice")
            {
                UnityEngine.Object.Destroy(__instance.gameObject);
                return false;
            }
            return true;
        }
    }
}