using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using System.Linq;

namespace SeamlessInteriors
{
    [HarmonyLib.HarmonyPatch(typeof(GameManager), "Awake")]
    public class PreventFakeManagerPatch
    {
        public static bool Prefix(GameManager __instance)
        {
            string sceneName = __instance.gameObject.scene.name;

            // Klonlama işlemi aktif olan evlerden birinin sahnesi yükleniyorsa, o sahnede uyanan GameManager sahtedir (fake).
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