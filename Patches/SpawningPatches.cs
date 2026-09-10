using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System.Collections.Generic;

namespace SeamlessInteriors
{
    // Stops cloned interiors from rolling their random loot a second time.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.RandomSpawnObject), "Start")]
    public class RandomSpawnBlockerPatch
    {
        public static bool Prefix(Il2Cpp.RandomSpawnObject __instance)
        {
            // Only RandomSpawnObjects belonging to a MasterInterior are our business.
            var instance = SeamlessInteriorsMod.FindInstanceOwning(__instance.transform);
            if (instance == null) return true;

            string saveKey = instance.Config.SaveKeyPrefix + Il2Cpp.SaveGameSystem.m_CurrentSaveName;

            // Loot was already generated for this save (key == 1): drop the spawner.
            //
            // NOTE: clone loot filtering no longer depends on this patch.
            // PerformInitialLootRoll does it before the scene is activated and then
            // destroys every RandomSpawnObject component. This patch is only a safety
            // net, kept as-is because it has worked without trouble for a long time.
            if (UnityEngine.PlayerPrefs.GetInt(saveKey, 0) == 1)
            {
                UnityEngine.Object.Destroy(__instance.gameObject);
                return false;
            }

            return true;
        }
    }

    // GUIDs of placeables that belong to cloned interiors.
    public class PlaceableFindOrCreatePatch
    {
        public static HashSet<string> s_InteriorPlaceableGuids = new HashSet<string>();
    }

    // Clone placeables are tracked by the mod's own save data, so they must never
    // enter the vanilla PlaceableManager registry (it would duplicate them).
    [HarmonyLib.HarmonyPatch(typeof(Il2CppTLD.Placement.PlaceableManager), nameof(Il2CppTLD.Placement.PlaceableManager.Add))]
    public class PreventPlaceableAutoRegisterPatch
    {
        public static bool Prefix(Il2CppTLD.Placement.Placeable placeable)
        {
            if (placeable == null) return true;

            // While any building is cloning, block every registration.
            bool isAnyCloningActive = SeamlessInteriorsMod.IsAnyCloningActive();
            if (isAnyCloningActive)
            {
                return false;
            }

            // Cloning done: block only items parented to a finished MasterInterior.
            var owner = SeamlessInteriorsMod.FindInstanceOwning(placeable.transform);
            if (owner != null && owner.RunCompleted) return false;

            return true;
        }
    }

    // Writes the mod's own save data alongside the game's scene data.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SaveGameSystem), nameof(Il2Cpp.SaveGameSystem.SaveSceneData))]
    public class SavePlaceablePositionsPatch
    {
        public static void Postfix()
        {
            SceneScan.Begin();
            try
            {
                // Which interior the player is currently in.
                SeamlessInteriorsMod.SavePlayerInsideState();

                // Placed-object positions for every building.
                SeamlessInteriorsMod.SaveAllPlaceablePositions();

                // Loose GearItems sitting in deactivated clone scenes.
                SeamlessInteriorsMod.SaveAllInactiveSceneGearItems();

                // Container contents inside clone scenes.
                SeamlessInteriorsMod.SaveAllContainerData();

                // State of broken / harvested / opened objects
                // (BreakDown, Harvestable, Smashable, OpenClose, Lock).
                SeamlessInteriorsMod.SaveAllInteractiveStates();

                // Safehouse "clear junk" (R) state.
                SeamlessInteriorsMod.SaveAllJunkStates();
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[PLACEABLE-SAVE] Hata: {ex.Message}");
            }
            finally
            {
                SceneScan.End();
            }
        }
    }

    // GearManager.Deserialize would respawn gear into a half-built clone, so it is
    // held off until cloning finishes. TargetMethods is used because the method is
    // overloaded and cannot be addressed by name alone.
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
            if (SeamlessInteriorsMod.IsAnyCloningActive())
            {
                return false;
            }
            return true;
        }
    }

    // Diagnostics: surfaces the reason the game gives for a failed save.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Panel_Confirmation), nameof(Il2Cpp.Panel_Confirmation.ShowSaveGameFailedSaveNotification))]
    public class SaveFailedDiagPatch
    {
        public static void Prefix(string debugInfo)
        {
            MelonLogger.Error($"[SAVE-FAILED] Oyun kayit hatasi verdi! debugInfo: {debugInfo}");
        }
    }

    // Destroyed clone objects leave dangling entries in PlaceableManager, and
    // serializing one of those aborts the whole save. Prune them first.
    [HarmonyLib.HarmonyPatch(typeof(Il2CppTLD.Placement.PlaceableManager), nameof(Il2CppTLD.Placement.PlaceableManager.SerializeInventory))]
    public class FixPlaceableManagerSerializeInventoryPatch
    {
        public static void Prefix()
        {
            try
            {
                var dict = Il2CppTLD.Placement.PlaceableManager.s_Placeables;
                if (dict == null) return;

                var toRemove = new System.Collections.Generic.List<string>();

                // Collect first, remove after: the dictionary cannot be modified
                // while it is being enumerated.
                foreach (var entry in dict)
                {
                    var info = entry.Value;
                    if (info == null || info.m_Handle == null || info.m_Handle.gameObject == null)
                    {
                        toRemove.Add(entry.Key);
                    }
                }

                foreach (var key in toRemove)
                {
                    dict.Remove(key);
                    MelonLogger.Warning($"[PLACEABLE-FIX] SerializeInventory oncesi olu/silinmis referans temizlendi: {key}");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[PLACEABLE-FIX] Hata: {ex.Message}");
            }
        }
    }

    // The scene placement path needs the same cleanup.
    [HarmonyLib.HarmonyPatch(typeof(Il2CppTLD.Placement.PlaceableManager), nameof(Il2CppTLD.Placement.PlaceableManager.SerializeScenePlacements))]
    public class FixPlaceableManagerSerializeScenePatch
    {
        public static void Prefix()
        {
            FixPlaceableManagerSerializeInventoryPatch.Prefix();
        }
    }
}
