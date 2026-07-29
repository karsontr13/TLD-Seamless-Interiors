using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using MelonLoader;

namespace SeamlessInteriors
{
    // ContainerManager.FindContainerByPosition can throw under certain edge cases with our
    // cloned/duplicated containers. Rather than letting that exception propagate and
    // potentially break other game systems, we swallow it here and just return null.
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

    // Solves a GUID collision between the original container and the cloned container,
    // and the resulting Lost & Found duplicate-container problem: because
    // ContainerManager's Serialize/Deserialize step finding two containers that share the
    // same GUID causes problems, we rewrite the clone's GUID to append "_CLONE" so the
    // save/load system treats the original and the clone as two distinct containers.
    // The original container out in the exterior world is left untouched.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Container), nameof(Il2Cpp.Container.Awake))]
    public class CloneContainerGuidFixPatch
    {
        public static void Postfix(Il2Cpp.Container __instance)
        {
            if (__instance == null || SeamlessInteriorsMod.s_MasterInterior == null) return;

            if (__instance.transform.IsChildOf(SeamlessInteriorsMod.s_MasterInterior.transform))
            {
                var guidComp = __instance.GetComponent<ObjectGuid>();
                if (guidComp != null && !string.IsNullOrEmpty(guidComp.m_Guid))
                {
                    if (!guidComp.m_Guid.EndsWith("_CLONE"))
                    {
                        guidComp.m_Guid = guidComp.m_Guid + "_CLONE";
                    }
                }
            }
        }
    }
}