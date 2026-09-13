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

            // THE TEST IS "WHERE IS IT", NOT "IS THE MOD BUSY".
            //
            // This used to refuse EVERY registration for as long as any building was
            // still cloning. Cloning a region takes the better part of a minute, and the
            // game restores its own scene placements inside that window - so the
            // furniture, workbenches and caches the player had set down OUTSIDE were
            // silently refused a place in the registry. Nothing was ever destroyed;
            // they simply were not in the list when the game next wrote the save, and
            // came back missing, taking the whole "DesignPlaceables" root with them.
            //
            // Only two kinds of object actually need blocking, and both are recognised
            // by where they live rather than by when they turned up.

            // 0) The mod is building one of its own objects RIGHT NOW.
            //
            // Placeable.FindOrCreateAndDeserialize creates the object before anyone can
            // reparent it, so for that instant it looks like an ordinary world placement
            // and the game adopts it into the region's save. On the next load BOTH the
            // game and the mod then create it - which is how one building's file grew
            // from 397 records to 773, with the same crate listed twenty times.
            //
            // The flag is set around that single synchronous call, so it cannot swallow
            // a registration the game is making for its own reasons.
            if (SeamlessInteriorsMod.IsCreatingClonePlaceable) return false;

            // 1) Already part of a clone. The mod's own save files own these.
            //    NOTE: no RunCompleted check - a placeable waking up midway through the
            //    build is just as much clone content as one that wakes up after.
            if (SeamlessInteriorsMod.IsUnderAnyMasterInteriorPublic(placeable.transform)) return false;

            // 2) Still sitting in a freshly loaded interior scene, before the clone has
            //    adopted its roots. This is the case the blanket block was really aimed
            //    at: those objects Awake while the scene loads, and registering them
            //    would put a copy of the building's own furniture in the region's save.
            //
            //    "IS IT A TEMPLATE", NOT "IS IT NAMED LIKE ONE".
            //
            //    This used to block on the scene NAME alone, and an original interior the
            //    player walks into through a loading screen carries exactly the same
            //    name as the template the mod clones from. So every piece of furniture
            //    the player moved while standing INSIDE the real Camp Office was refused
            //    a place in the registry: the pick-up was recorded, the putting-down was
            //    not, and on the next load the object was back where the scene shipped
            //    it. Uninstalling the mod made it work again, which is the whole
            //    diagnosis in one sentence.
            //
            //    It also quietly emptied the pre-mod import of everything it needed:
            //    m_PlacementListSerialized ended up holding nothing but "Removed"
            //    tombstones, so there were no transforms left for the transcode to carry
            //    across. (see IsInteriorLoadedAsCloneTemplate)
            if (SeamlessInteriorsMod.IsInteriorLoadedAsCloneTemplate(placeable.gameObject.scene.name))
                return false;

            return true;
        }
    }

    // Writes the mod's own save data alongside the game's scene data.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SaveGameSystem), nameof(Il2Cpp.SaveGameSystem.SaveSceneData))]
    public class SavePlaceablePositionsPatch
    {
        // __0 is the SlotData the game is about to write to disk. The identity tag has to
        // go into it BEFORE the mod's own files are written, so the save and the files
        // can only ever carry the same tag.
        public static void Postfix(Il2Cpp.SlotData __0)
        {
            SceneScan.Begin();
            try
            {
                // Which playthrough this save belongs to (see SaveIdentity.cs).
                SeamlessInteriorsMod.StampSaveIdentity(__0);

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

    // GearManager.Deserialize starts by deleting every active gear item, so it must not
    // land while an interior scene is being pulled in. It is deferred for exactly that
    // moment and replayed straight after. TargetMethods is used because the method is
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

        // What was blocked, so it can be run again the moment it is safe. The method is
        // kept alongside the arguments because Deserialize is overloaded - replaying
        // through the original MethodBase works whichever overload was called.
        //
        // A LIST, NOT A SINGLE SLOT. There used to be one field per thing, so a second
        // blocked call inside the same load window silently overwrote - and threw away -
        // the first one. The payload that gets thrown away is the region's loose gear:
        // every carcass, every piece of meat and everything the player dropped outdoors.
        private class DeferredCall
        {
            public System.Reflection.MethodBase Method;
            public object[] Args;
        }

        private static readonly System.Collections.Generic.List<DeferredCall> s_Deferred =
            new System.Collections.Generic.List<DeferredCall>();

        public static bool HasDeferred { get { return s_Deferred.Count > 0; } }

        public static bool Prefix(System.Reflection.MethodBase __originalMethod, object[] __args)
        {
            // BLOCKED ONLY WHILE AN INTERIOR SCENE IS ACTUALLY LOADING.
            //
            // This used to refuse the call for as long as ANY building was cloning, which
            // on a full region is the better part of a minute. GearManager.Deserialize is
            // how the game puts the REGION's loose gear back - the meat, the carcasses and
            // everything else the player dropped outdoors. Refusing it during that window
            // meant none of it came back, and whether it did was pure timing: a warm load
            // finished in 5 seconds and the gear survived, a cold one took 54 and it did
            // not.
            //
            // The narrow window is still worth keeping: while an interior scene is being
            // pulled in, its gear is briefly loose in the world, and a global deserialize
            // would wipe it (the call starts with DeleteAllActive) before it can be cloned.
            if (!SeamlessInteriorsMod.IsLoadingInteriorScene) return true;

            // AND WHAT IS BLOCKED IS NOT THROWN AWAY. The payload is kept and replayed as
            // soon as the scene load is done - the same trick FireManagerStealerPatch uses
            // for fire data - so nothing the game meant to restore is lost.
            s_Deferred.Add(new DeferredCall { Method = __originalMethod, Args = __args });

            MelonLogger.Warning($"[GEAR-DEFER] GearManager.Deserialize ic mekan sahnesi yuklenirken engellendi, sahne yuklenince tekrar calistirilacak. ({__originalMethod?.Name})");
            return false;
        }

        // Called once the interior scene load window has closed.
        //
        // ALSO CALLED FROM OnUpdate, every half second, whenever something is queued and
        // no interior scene is loading. It used to be reachable from exactly one place -
        // the end of Run()'s scene-load section - and Run() is not called at all when a
        // region is re-entered from a vanilla interior, because the clone is reattached
        // from the persist cache instead. Anything deferred in that window was never
        // replayed, and the region's loose gear simply never came back.
        public static void ReplayIfDeferred()
        {
            if (s_Deferred.Count == 0) return;

            // Taken and cleared BEFORE the calls: a replay goes back through this same
            // prefix, and nothing must be able to queue itself in a loop.
            var pending = s_Deferred.ToArray();
            s_Deferred.Clear();

            foreach (var call in pending)
            {
                if (call == null || call.Method == null) continue;

                try
                {
                    call.Method.Invoke(null, call.Args);
                    MelonLogger.Msg($"[GEAR-DEFER] Ertelenen GearManager.Deserialize calistirildi. ({call.Method.Name})");
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[GEAR-DEFER] Ertelenen cagri basarisiz: {ex.Message}");
                }
            }
        }

        // The region those payloads belong to is being torn down, so replaying them
        // would spawn the OLD region's gear into the new one.
        public static void DiscardDeferred()
        {
            if (s_Deferred.Count == 0) return;

            MelonLogger.Warning($"[GEAR-DEFER] Bolge degisiyor, calistirilamamis {s_Deferred.Count} ertelenmis " +
                                $"GearManager.Deserialize cagrisi atildi.");
            s_Deferred.Clear();
        }
    }

    // The mod destroys clone GearItems in several places, and a dangling entry left in
    // GearManager.m_Gear makes the game walk into a dead object while it is writing the
    // region's gear blob - which holds everything the player dropped outdoors. Prune
    // first, exactly as FixPlaceableManagerSerializeInventoryPatch does for placeables.
    // (see SeamlessInteriorsMod.GearLeak.cs)
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.GearManager), nameof(Il2Cpp.GearManager.Serialize))]
    public class PruneDeadGearBeforeSerializePatch
    {
        public static void Prefix()
        {
            SeamlessInteriorsMod.PruneDeadGearManagerEntries();
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
                    if (info == null)
                    {
                        toRemove.Add(entry.Key);
                        continue;
                    }

                    // A MISSING OBJECT IS NOT ALWAYS A BROKEN ENTRY.
                    //
                    // This prune exists to drop references to clone objects that were
                    // destroyed, because serializing one of those aborts the whole save.
                    // But two states have no scene object BY DESIGN:
                    //
                    //   OnPlayer - the furniture is in the player's backpack. It is not
                    //              instantiated while carried, and its weight is part of
                    //              the carried total (Inventory.GetTotalDecorationWeight).
                    //              Deleting it here erased the player's decorations from
                    //              the save and made the weight readout collapse - the
                    //              backpack still listed the items, the weight did not
                    //              count them.
                    //   Removed  - a tombstone saying "this scene object is gone". Losing
                    //              it lets the game put the object back on the next load.
                    //
                    // Both are legitimate, so only entries that ought to have a live
                    // object are pruned.
                    if (info.m_State == Il2CppTLD.Placement.PlacementState.OnPlayer) continue;
                    if (info.m_State == Il2CppTLD.Placement.PlacementState.Removed) continue;

                    if (info.m_Handle == null || info.m_Handle.gameObject == null)
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
