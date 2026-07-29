using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Detects a brand-new game (very low hours played) and clears our "gear already
        // generated" lock plus any leftover placeable-position JSON from a previous save
        // that happened to reuse the same save name, so the new game doesn't inherit stale
        // loot/positions from an old playthrough.
        private void CheckNewGameLootLock()
        {
            var tod = GameManager.GetTimeOfDayComponent();
            if (tod != null && tod.GetHoursPlayedNotPaused() < 0.05f)
            {
                string keyToReset = "CampOfficeGen_" + SaveGameSystem.m_CurrentSaveName;
                UnityEngine.PlayerPrefs.SetInt(keyToReset, 0);
                UnityEngine.PlayerPrefs.Save();
                string jsonPath = GetPlaceableSavePath();
                if (jsonPath != null && File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[NEW GAME] Eski Placeable JSON silindi: {jsonPath}");
                }

                if (s_DebugBounds)
                    MelonLogger.Msg("[NEW GAME DETECTED] Broke old loot generation lock! Gear will spawn.");
            }
        }

        private void HandleInitialPlaceables()
        {
            InvalidateInteriorPlaceables(s_MasterInterior);
            CollectInteriorPlaceableGuids(s_MasterInterior);
        }

        // Handles two different situations depending on whether loot for this save was
        // already generated once before:
        //  - First time ever: assign deterministic PDIDs to gear so it can be tracked/saved.
        //  - Every time after: any gear without a matching GUID component is a "rogue" spawn
        //    (e.g. the game's own random-spawn system trying to re-populate the interior)
        //    and gets destroyed, since our clone already has its items locked in place.
        // Also runs spatial + GUID-based deduplication so the same physical item can't exist
        // both inside the clone and in the original scene at once.
        private void ProcessSpawnsAndDeduplication()
        {
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            string saveKey = "CampOfficeGen_" + currentSaveName;
            bool isAlreadyGenerated = UnityEngine.PlayerPrefs.GetInt(saveKey, 0) == 1;

            if (isAlreadyGenerated)
            {
                var allGearInside = s_MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true);
                int deletedRogueCount = 0;

                foreach (var gear in allGearInside)
                {
                    if (gear == null) continue;
                    var guidComponent = gear.GetComponent<Il2Cpp.ObjectGuid>();

                    if (guidComponent == null || string.IsNullOrEmpty(guidComponent.m_Guid))
                    {
                        UnityEngine.Object.Destroy(gear.gameObject);
                        deletedRogueCount++;
                    }
                }
                if (s_DebugBounds) MelonLogger.Msg($"[Rogue-Cleanup] Force cleared {deletedRogueCount} rogue/new RSO objects.");
            }
            else
            {
                GenerateDeterministicPDIDs(s_MasterInterior);

                if (!string.IsNullOrEmpty(currentSaveName))
                {
                    UnityEngine.PlayerPrefs.SetInt(saveKey, 1);
                    UnityEngine.PlayerPrefs.Save();
                }
            }

            SpatialDeduplication(s_MasterInterior);

            // Global Deduplication by GUID
            var allGuids = UnityEngine.Object.FindObjectsOfType<Il2Cpp.ObjectGuid>(true);
            Dictionary<string, List<Il2Cpp.ObjectGuid>> guidDict = new Dictionary<string, List<Il2Cpp.ObjectGuid>>();

            foreach (var guid in allGuids)
            {
                string currentGuid = guid.m_Guid ?? guid.PDID;
                if (string.IsNullOrEmpty(currentGuid)) continue;

                if (!guidDict.ContainsKey(currentGuid))
                    guidDict[currentGuid] = new List<Il2Cpp.ObjectGuid>();
                guidDict[currentGuid].Add(guid);
            }

            foreach (var kvp in guidDict)
            {
                if (kvp.Value.Count > 1)
                {
                    foreach (var guidObj in kvp.Value)
                    {
                        if (guidObj != null && s_MasterInterior != null && guidObj.transform.IsChildOf(s_MasterInterior.transform))
                        {
                            if (guidObj.GetComponent<Il2Cpp.GearItem>() != null)
                            {
                                UnityEngine.Object.Destroy(guidObj.gameObject);
                            }
                        }
                    }
                }
            }
        }

        public void CleanupLostAndFoundBoxes()
        {
            // Root cause: the base game creates a box named "CONTAINER_InaccessibleGear"
            // for items it thinks got lost (e.g. displaced by our cloning). We destroy the
            // whole container object (not just clear it) so any gear stuck inside it is
            // removed too, rather than leaving an inaccessible lost-and-found box behind.
            var containers = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Container>(true);
            int count = 0;
            foreach (var c in containers)
            {
                if (c == null || c.gameObject == null) continue;
                if (c.gameObject.name.IndexOf("CONTAINER_InaccessibleGear", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    c.gameObject.name.IndexOf("LostAndFound", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.Destroy(c.gameObject);
                    count++;
                }
            }
            if (s_DebugBounds) MelonLogger.Msg($"[L&F-CLEANUP] Destroyed {count} InaccessibleGear / Lost and Found boxes.");
        }

        // Gives gear items that don't already have a GUID a stable, deterministic ID derived
        // from their name + local position. This means the same physical loot spawn gets the
        // same ID across sessions (instead of a random one), so our save/restore and
        // deduplication logic can reliably recognize "the same item" again later.
        private static void GenerateDeterministicPDIDs(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;
            var allGear = interiorRoot.GetComponentsInChildren<Il2Cpp.GearItem>(true);

            foreach (var gear in allGear)
            {
                if (gear == null) continue;
                var guidComponent = gear.GetComponent<Il2Cpp.ObjectGuid>();
                if (guidComponent == null) guidComponent = gear.gameObject.AddComponent<Il2Cpp.ObjectGuid>();

                if (string.IsNullOrEmpty(guidComponent.m_Guid))
                {
                    Vector3 localPos = interiorRoot.transform.InverseTransformPoint(gear.transform.position);
                    string cleanName = gear.gameObject.name.Replace("(Clone)", "").Trim();
                    guidComponent.m_Guid = $"CampOffice_{cleanName}_{localPos.x:F2}_{localPos.y:F2}_{localPos.z:F2}";
                }
            }
        }

        // Because both the cloned interior and the original interior scene can contain the
        // same gear item (same name, same relative position), this removes the clone's copy
        // whenever a matching item is found just outside the clone at (almost) the same
        // world position - preventing visible duplicate items.
        private static void SpatialDeduplication(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;

            var allGearInside = interiorRoot.GetComponentsInChildren<Il2Cpp.GearItem>(true);
            var allGearOutside = new List<Il2Cpp.GearItem>();

            foreach (var g in UnityEngine.Object.FindObjectsOfType<Il2Cpp.GearItem>(true))
            {
                if (g != null && g.transform.root != interiorRoot.transform && !g.transform.IsChildOf(interiorRoot.transform))
                    allGearOutside.Add(g);
            }

            foreach (var insideGear in allGearInside)
            {
                if (insideGear == null) continue;
                string insideName = insideGear.gameObject.name.Replace("(Clone)", "").Trim();

                foreach (var outsideGear in allGearOutside)
                {
                    if (outsideGear == null) continue;
                    string outsideName = outsideGear.gameObject.name.Replace("(Clone)", "").Trim();

                    if (insideName == outsideName && Vector3.Distance(insideGear.transform.position, outsideGear.transform.position) < 0.05f)
                    {
                        UnityEngine.Object.Destroy(insideGear.gameObject);
                        break;
                    }
                }
            }
        }

        private static void InvalidateInteriorPlaceables(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;
            var placeables = interiorRoot.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in placeables) { if (p != null) p.m_Invalidated = true; }
        }

        private static void CollectInteriorPlaceableGuids(GameObject interiorRoot)
        {
            PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Clear();
            if (interiorRoot == null) return;
            var placeables = interiorRoot.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in placeables)
            {
                if (p != null && !string.IsNullOrEmpty(p.m_Guid))
                    PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Add(p.m_Guid);
            }
        }

        // Cleans up placed decoration objects that have ended up orphaned - either sitting in
        // the DontDestroyOnLoad scene with no real parent, or attached under the player's own
        // transform - which can happen from odd re-parenting edge cases and would otherwise
        // leave a stray, permanently-placed object with the "(PLACED)" name suffix.
        private static void CleanupOrphanPlaceables()
        {
            string suffix = Il2CppTLD.Placement.Placeable.SPAWNED_NAME_SUFFIX;
            if (string.IsNullOrEmpty(suffix)) suffix = " (PLACED)";

            var allPlaceables = UnityEngine.Object.FindObjectsOfType<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in allPlaceables)
            {
                if (p == null || p.gameObject == null || !p.gameObject.name.Contains(suffix)) continue;
                if (s_MasterInterior != null && p.transform.IsChildOf(s_MasterInterior.transform)) continue;

                string sceneName = p.gameObject.scene.name;
                bool isOrphan = (sceneName == "DontDestroyOnLoad" || sceneName == null || sceneName == "");
                Transform root = p.transform.root;
                if (root != null && root.name.Contains("CHARACTER_FPSPlayer")) isOrphan = true;

                if (isOrphan) UnityEngine.Object.Destroy(p.gameObject);
            }
        }

        // Shows/hides gear items that are physically inside the cabin bounds but live outside
        // the clone hierarchy (i.e. gear belonging to the "real" interior scene). This keeps
        // them hidden while the player is looking at the exterior-side view of the building,
        // and visible once the clone (and thus this gear's real counterpart) is shown.
        public static void SetInteriorItemsVisible(bool visible)
        {
            if (s_MasterInterior == null) return;

            var allGear = UnityEngine.Object.FindObjectsOfType<Il2Cpp.GearItem>(true);
            foreach (var gear in allGear)
            {
                if (gear == null || gear.gameObject == null) continue;
                if (gear.transform.IsChildOf(s_MasterInterior.transform)) continue;

                if (IsPositionInsideCabinFull(gear.transform.position))
                {
                    foreach (var r in gear.GetComponentsInChildren<MeshRenderer>(true)) if (r != null) r.enabled = visible;
                    foreach (var c in gear.GetComponentsInChildren<Collider>(true)) if (c != null) c.enabled = visible;
                }
            }
        }
    }
}