using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System.Collections.Generic;

namespace SeamlessInteriors
{
    // Stops the base game's RandomSpawnObject system from re-populating the interior with
    // fresh random loot once we've already generated (and locked) loot for this save -
    // otherwise every scene reload would spawn a new batch of random items on top of
    // the ones already placed.
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

    // Tracks the GUIDs of placeables that belong to our cloned interior (populated during
    // cloning) so other patches can tell "this is one of ours" apart from placeables
    // belonging to the rest of the world.
    public class PlaceableFindOrCreatePatch
    {
        public static HashSet<string> s_InteriorPlaceableGuids = new HashSet<string>();
    }

    // Placeable.Awake() normally calls PlaceableManager.Add() to register itself with the
    // game's placement-tracking system. We block that registration for our cloned objects,
    // both while actively cloning and afterwards for anything still parented under
    // s_MasterInterior, since we manage their persistence ourselves (see SaveData.cs)
    // instead of letting the base game's placement manager track them.
    [HarmonyLib.HarmonyPatch(typeof(Il2CppTLD.Placement.PlaceableManager), nameof(Il2CppTLD.Placement.PlaceableManager.Add))]
    public class PreventPlaceableAutoRegisterPatch
    {
        public static bool Prefix(Il2CppTLD.Placement.Placeable placeable)
        {
            if (placeable == null) return true;

            if (SeamlessInteriorsMod.s_IsCloningRoutineActive)
            {
                return false;
            }

            if (SeamlessInteriorsMod.s_RunCompleted &&
                SeamlessInteriorsMod.s_MasterInterior != null &&
                placeable.transform.IsChildOf(SeamlessInteriorsMod.s_MasterInterior.transform))
            {
                return false;
            }

            return true;
        }
    }

    // Hooks into the game's own save routine so our custom placeable-position JSON gets
    // written out every time the game saves scene data, not just at specific mod-triggered
    // moments (like a portal transition).
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SaveGameSystem), nameof(Il2Cpp.SaveGameSystem.SaveSceneData))]
    public class SavePlaceablePositionsPatch
    {
        public static void Postfix()
        {
            try
            {
                SeamlessInteriorsMod.SavePlaceablePositions();
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[PLACEABLE-SAVE] Hata: {ex.Message}");
            }
        }
    }

    // Prevents GearManager.Deserialize from running while our cloning routine is active,
    // since running the base game's gear deserialization mid-clone would spawn a second,
    // duplicate set of saved gear on top of what we're already restoring ourselves.
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