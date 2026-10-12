using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    // An interior scene loaded for cloning brings its own GameManager along.
    // That duplicate would fight the real one, so it is killed on Awake.
    [HarmonyLib.HarmonyPatch(typeof(GameManager), "Awake")]
    public class PreventFakeManagerPatch
    {
        public static bool Prefix(GameManager __instance)
        {
            string sceneName = SeamlessInteriorsMod.RealSceneName(__instance.gameObject.scene);
            // Base name of the _SANDBOX / _DLC01 variants too: LakeCabinC_SANDBOX's manager slipped through.
            string baseName = SeamlessInteriorsMod.GetInteriorBaseName(sceneName);

            // A GameManager waking inside a template we are cloning, or inside a clone, is fake:
            // destroy it and skip the original Awake.
            if (SeamlessInteriorsMod.IsUnderAnyMasterInteriorPublic(__instance.transform))
            {
                // Only the component: its object may carry room content.
                UnityEngine.Object.Destroy(__instance);
                return false;
            }
            foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                if (instance.IsCloningRoutineActive && baseName != null && baseName == instance.Config.InteriorSceneBaseName)
                {
                    UnityEngine.Object.Destroy(__instance.gameObject);
                    return false;
                }
            }
            return true;
        }
    }
}
