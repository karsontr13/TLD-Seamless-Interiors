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
            string sceneName = __instance.gameObject.scene.name;

            // Any GameManager waking up inside an interior scene we are currently
            // cloning is a fake one: destroy it and skip the original Awake.
            foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                if (instance.IsCloningRoutineActive && sceneName == instance.Config.InteriorSceneBaseName)
                {
                    UnityEngine.Object.Destroy(__instance.gameObject);
                    return false;
                }
            }
            return true;
        }
    }
}
