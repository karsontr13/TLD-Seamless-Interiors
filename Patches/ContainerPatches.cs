using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using MelonLoader;
using System.Linq;

namespace SeamlessInteriors
{
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

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Container), nameof(Il2Cpp.Container.Awake))]
    public class CloneContainerGuidFixPatch
    {
        public static void Postfix(Il2Cpp.Container __instance)
        {
            if (__instance == null) return;

            foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                if (instance.MasterInterior != null && __instance.transform.IsChildOf(instance.MasterInterior.transform))
                {
                    var guidComp = __instance.GetComponent<ObjectGuid>();
                    if (guidComp != null && !string.IsNullOrEmpty(guidComp.m_Guid))
                    {
                        if (!guidComp.m_Guid.EndsWith("_CLONE"))
                        {
                            guidComp.m_Guid = guidComp.m_Guid + "_CLONE";
                        }
                    }
                    break; // Kutunun hangi eve ait olduğunu bulduğumuz için döngüyü kırabiliriz.
                }
            }
        }
    }
}