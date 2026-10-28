using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using MelonLoader;

namespace SeamlessInteriors
{
    // Cloned interiors confuse the vanilla container lookup, which can throw.
    // Swallow the exception and report "no container" instead of crashing.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.ContainerManager), nameof(Il2Cpp.ContainerManager.FindContainerByPosition))]
    public class FixContainerManagerCrashPatch
    {
        public static System.Exception Finalizer(System.Exception __exception, ref Il2Cpp.Container __result)
        {
            if (__exception != null)
            {
                __result = null;
                return null;
            }
            return null;
        }
    }

    // Loot rolls can be queued for containers that were already destroyed
    // during cloning; skip them instead of dereferencing a dead object.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Container), nameof(Il2Cpp.Container.PopulateWithRandomGear))]
    public class FixContainerPopulateCrashPatch
    {
        public static bool Prefix(Il2Cpp.Container __instance)
        {
            if (__instance == null || __instance.gameObject == null)
            {
                return false;
            }
            return true;
        }
    }
}
