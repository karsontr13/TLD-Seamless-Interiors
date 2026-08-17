using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        private void CheckNewGameLootLock(SeamlessInteriorInstance instance)
        {
            var tod = GameManager.GetTimeOfDayComponent();
            if (tod != null && tod.GetHoursPlayedNotPaused() < 0.05f)
            {
                string keyToReset = instance.Config.SaveKeyPrefix + SaveGameSystem.m_CurrentSaveName;
                UnityEngine.PlayerPrefs.SetInt(keyToReset, 0);
                UnityEngine.PlayerPrefs.Save();

                string jsonPath = GetPlaceableSavePath(instance);
                if (jsonPath != null && File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[NEW GAME] Eski Placeable JSON silindi: {jsonPath}");
                }

                string gearJsonPath = GetInactiveSceneGearSavePath(instance);
                if (gearJsonPath != null && File.Exists(gearJsonPath))
                {
                    File.Delete(gearJsonPath);
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[NEW GAME] Eski Gear JSON silindi: {gearJsonPath}");
                }

                string containerJsonPath = GetContainerSavePath(instance);
                if (containerJsonPath != null && File.Exists(containerJsonPath))
                {
                    File.Delete(containerJsonPath);
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[NEW GAME] Eski Container JSON silindi: {containerJsonPath}");
                }

                if (s_DebugBounds)
                    MelonLogger.Msg($"[NEW GAME DETECTED] {instance.Config.InteriorSceneBaseName} loot lock resetlendi! Eşyalar doğacak.");
            }
        }

        private void HandleInitialPlaceables(SeamlessInteriorInstance instance)
        {
            InvalidateInteriorPlaceables(instance.MasterInterior);
            CollectInteriorPlaceableGuids(instance.MasterInterior);
        }

        private void ProcessSpawnsAndDeduplication(SeamlessInteriorInstance instance)
        {
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            string saveKey = instance.Config.SaveKeyPrefix + currentSaveName;
            bool isAlreadyGenerated = UnityEngine.PlayerPrefs.GetInt(saveKey, 0) == 1;

            if (isAlreadyGenerated)
            {
                var allGearInside = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true);
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
                if (s_DebugBounds) MelonLogger.Msg($"[Rogue-Cleanup] {instance.Config.ResolvedInstanceId}: Force cleared {deletedRogueCount} rogue objects.");
            }
            else
            {
                // InstanceId kullan: Aynı sahne farklı binalarda farklı GUID'ler üretsin
                GenerateDeterministicPDIDs(instance.MasterInterior, instance.Config.ResolvedInstanceId);

                if (!string.IsNullOrEmpty(currentSaveName))
                {
                    UnityEngine.PlayerPrefs.SetInt(saveKey, 1);
                    UnityEngine.PlayerPrefs.Save();
                }
            }

            SpatialDeduplication(instance.MasterInterior);

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
                        if (guidObj != null && instance.MasterInterior != null && guidObj.transform.IsChildOf(instance.MasterInterior.transform))
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
            if (s_DebugBounds && count > 0) MelonLogger.Msg($"[L&F-CLEANUP] Destroyed {count} InaccessibleGear boxes.");
        }

        private static void GenerateDeterministicPDIDs(GameObject interiorRoot, string baseName)
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
                    // ID artık binanın InstanceId'sine göre oluşuyor (aynı sahne farklı binalarda çakışmaz)
                    guidComponent.m_Guid = $"{baseName}_{cleanName}_{localPos.x:F2}_{localPos.y:F2}_{localPos.z:F2}";
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
            PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Clear(); // Bu global kalabilir, aynı anda sadece bir binayı clone'luyoruz
            if (interiorRoot == null) return;
            var placeables = interiorRoot.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in placeables)
            {
                if (p != null && !string.IsNullOrEmpty(p.m_Guid))
                    PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Add(p.m_Guid);
            }
        }

        private static void CleanupOrphanPlaceables(SeamlessInteriorInstance instance)
        {
            string suffix = Il2CppTLD.Placement.Placeable.SPAWNED_NAME_SUFFIX;
            if (string.IsNullOrEmpty(suffix)) suffix = " (PLACED)";

            var allPlaceables = UnityEngine.Object.FindObjectsOfType<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in allPlaceables)
            {
                if (p == null || p.gameObject == null || !p.gameObject.name.Contains(suffix)) continue;
                if (instance.MasterInterior != null && p.transform.IsChildOf(instance.MasterInterior.transform)) continue;

                string sceneName = p.gameObject.scene.name;
                bool isOrphan = (sceneName == "DontDestroyOnLoad" || sceneName == null || sceneName == "");
                Transform root = p.transform.root;
                if (root != null && root.name.Contains("CHARACTER_FPSPlayer")) isOrphan = true;

                if (isOrphan) UnityEngine.Object.Destroy(p.gameObject);
            }
        }

        public static void SetInteriorItemsVisible(SeamlessInteriorInstance instance, bool visible)
        {
            if (instance.MasterInterior == null) return;

            // OPTİMİZASYON: Önce MasterInterior'ın doğrudan child'ları olan gearları işle
            // Bu, FindObjectsOfType'dan çok daha hızlı
            var childGear = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true);
            foreach (var gear in childGear)
            {
                if (gear == null || gear.gameObject == null) continue;
                if (visible && !gear.gameObject.activeInHierarchy) continue;
                if (gear.transform.root.name.Contains("CHARACTER_FPSPlayer")) continue;

                foreach (var r in gear.GetComponentsInChildren<MeshRenderer>(true)) if (r != null) r.enabled = visible;
                foreach (var c in gear.GetComponentsInChildren<Collider>(true)) if (c != null) c.enabled = visible;
            }

            // MasterInterior'ın child'ı olan placeable'ları da işle
            var childPlaceables = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in childPlaceables)
            {
                if (p == null || p.gameObject == null) continue;
                if (visible && !p.gameObject.activeInHierarchy) continue;
                if (p.transform.root.name.Contains("CHARACTER_FPSPlayer")) continue;

                foreach (var r in p.GetComponentsInChildren<MeshRenderer>(true)) if (r != null) r.enabled = visible;
                foreach (var c in p.GetComponentsInChildren<Collider>(true)) if (c != null) c.enabled = visible;
            }

            // Taşınmış objeler (parent'ı MasterInterior olmayan ama bounds içinde olanlar)
            // Hem gizlerken hem gösterirken taranmalı — taşınmış placeablelar child değil
            if (instance.InteriorTrigger != null)
            {
                // AABB pre-filter için trigger bounds'u world-space'e çevir
                Vector3 wCenter = instance.InteriorTrigger.transform.TransformPoint(instance.InteriorTrigger.center);
                Vector3 lossyScale = instance.InteriorTrigger.transform.lossyScale;
                Vector3 wSize = new Vector3(
                    instance.InteriorTrigger.size.x * Mathf.Abs(lossyScale.x),
                    instance.InteriorTrigger.size.y * Mathf.Abs(lossyScale.y),
                    instance.InteriorTrigger.size.z * Mathf.Abs(lossyScale.z)) * 1.2f;
                Bounds filterBounds = new Bounds(wCenter, wSize);

                var allGear = UnityEngine.Object.FindObjectsOfType<Il2Cpp.GearItem>(true);
                foreach (var gear in allGear)
                {
                    if (gear == null || gear.gameObject == null) continue;
                    if (gear.transform.IsChildOf(instance.MasterInterior.transform)) continue; // Zaten yukarıda işlendi
                    if (gear.transform.root.name.Contains("CHARACTER_FPSPlayer")) continue;
                    if (!filterBounds.Contains(gear.transform.position)) continue; // AABB pre-filter

                    if (IsPositionInsideFull(instance, gear.transform.position))
                    {
                        foreach (var r in gear.GetComponentsInChildren<MeshRenderer>(true)) if (r != null) r.enabled = visible;
                        foreach (var c in gear.GetComponentsInChildren<Collider>(true)) if (c != null) c.enabled = visible;
                    }
                }

                var allPlaceables = UnityEngine.Object.FindObjectsOfType<Il2CppTLD.Placement.Placeable>(true);
                foreach (var p in allPlaceables)
                {
                    if (p == null || p.gameObject == null) continue;
                    if (p.transform.IsChildOf(instance.MasterInterior.transform)) continue;
                    if (p.transform.root.name.Contains("CHARACTER_FPSPlayer")) continue;
                    if (!filterBounds.Contains(p.transform.position)) continue;

                    if (IsPositionInsideFull(instance, p.transform.position))
                    {
                        foreach (var r in p.GetComponentsInChildren<MeshRenderer>(true)) if (r != null) r.enabled = visible;
                        foreach (var c in p.GetComponentsInChildren<Collider>(true)) if (c != null) c.enabled = visible;
                    }
                }
            }
        }
    }
}