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

    // A cloned container inherits the GUID of its original, so the save system
    // would mirror their contents. Suffix cloned GUIDs to keep them separate.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Container), nameof(Il2Cpp.Container.Awake))]
    public class CloneContainerGuidFixPatch
    {
        public static void Postfix(Il2Cpp.Container __instance)
        {
            if (__instance == null) return;

            // This runs THOUSANDS of times per scene load (once per Container.Awake),
            // so the cheap root comparison is used instead of a per-instance IsChildOf walk.
            var instance = SeamlessInteriorsMod.FindInstanceOwning(__instance.transform);
            if (instance == null) return;

            var guidComp = __instance.GetComponent<ObjectGuid>();
            if (guidComp == null || string.IsNullOrEmpty(guidComp.m_Guid)) return;

            if (!guidComp.m_Guid.EndsWith("_CLONE"))
                guidComp.m_Guid = guidComp.m_Guid + "_CLONE";
        }
    }
}
