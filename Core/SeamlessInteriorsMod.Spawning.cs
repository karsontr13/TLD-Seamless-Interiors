using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        private void CheckNewGameLootLock()
        {
            var tod = GameManager.GetTimeOfDayComponent();
            if (tod != null && tod.GetHoursPlayedNotPaused() < 0.05f)
            {
                string keyToReset = "CampOfficeGen_" + SaveGameSystem.m_CurrentSaveName;
                UnityEngine.PlayerPrefs.SetInt(keyToReset, 0);
                UnityEngine.PlayerPrefs.Save();

                if (s_DebugBounds)
                    MelonLogger.Msg("[NEW GAME DETECTED] Broke old loot generation lock! Gear will spawn.");
            }
        }

        private void HandleInitialPlaceables()
        {
            InvalidateInteriorPlaceables(s_MasterInterior);
            CollectInteriorPlaceableGuids(s_MasterInterior);
        }

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