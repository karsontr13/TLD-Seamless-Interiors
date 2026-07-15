using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System.Collections.Generic;

namespace SeamlessInteriors
{
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.RandomSpawnObject), "Start")]
    public class RandomSpawnBlockerPatch
    {
        public static bool Prefix(Il2Cpp.RandomSpawnObject __instance)
        {
            if (SeamlessInteriorsMod.s_IsCloningRoutineActive)
            {
                string saveKey = "CampOfficeGen_" + Il2Cpp.SaveGameSystem.m_CurrentSaveName;

                if (UnityEngine.PlayerPrefs.GetInt(saveKey, 0) == 1)
                {
                    UnityEngine.Object.Destroy(__instance.gameObject);
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2CppTLD.Placement.Placeable), nameof(Il2CppTLD.Placement.Placeable.FindOrCreateAndDeserialize))]
    public class PlaceableFindOrCreatePatch
    {
        public static HashSet<string> s_InteriorPlaceableGuids = new HashSet<string>();

        public static bool Prefix(string guid, Il2CppTLD.Placement.PlaceableSaveData data, ref Il2CppTLD.Placement.Placeable __result)
        {
            if (!SeamlessInteriorsMod.s_RunCompleted && s_InteriorPlaceableGuids.Contains(guid))
            {
                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg($"[PLACEABLE-SKIP] Blocked FindOrCreateAndDeserialize for GUID: {guid}");

                __result = null;
                return false;
            }

            return true;
        }
    }

    [HarmonyLib.HarmonyPatch]
    public class PreventGearManagerDuplicationPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var method in typeof(Il2Cpp.GearManager).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (method.Name == "Deserialize")
                {
                    yield return method;
                }
            }
        }

        public static bool Prefix()
        {
            if (SeamlessInteriorsMod.s_IsCloningRoutineActive)
            {
                return false;
            }
            return true;
        }
    }
}