using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Oyuncunun hangi klon sahnede olduğunu kaydeden key prefix
        private const string PLAYER_INSIDE_KEY_PREFIX = "SeamlessInteriors_PlayerInside_";

        public static void SavePlayerInsideState()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) return;

            string insideKey = PLAYER_INSIDE_KEY_PREFIX + saveName;
            string insideInstanceId = "";

            foreach (var instance in ActiveInteriors.Values)
            {
                if (!instance.RunCompleted) continue;
                if (instance.IsPositionInside(playerT.position))
                {
                    insideInstanceId = instance.Config.ResolvedInstanceId;
                    break;
                }
            }

            UnityEngine.PlayerPrefs.SetString(insideKey, insideInstanceId);
            UnityEngine.PlayerPrefs.Save();

            if (s_DebugBounds)
                MelonLogger.Msg($"[SAVE-STATE] Oyuncu kayit pozisyonu: {(string.IsNullOrEmpty(insideInstanceId) ? "DISARIDA" : insideInstanceId)}");
        }

        public static string GetSavedPlayerInsideInstanceId()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return "";

            string insideKey = PLAYER_INSIDE_KEY_PREFIX + saveName;
            return UnityEngine.PlayerPrefs.GetString(insideKey, "");
        }

        // Artık her binanın kaydı kendi baseName'ine göre ayrı bir dosyada tutuluyor.
        private static string GetPlaceableSavePath(SeamlessInteriorInstance instance)
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return null;
            string dir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SeamlessInteriorsData");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, saveName + "_" + instance.Config.ResolvedInstanceId + "_placeables.json");
        }

        // Global save tetikleyicisi (Harmony patch'inden burası çağrılacak)
        public static void SaveAllPlaceablePositions()
        {
            foreach (var instance in ActiveInteriors.Values)
            {
                SavePlaceablePositions(instance);
            }
        }

        public static void SavePlaceablePositions(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null || !instance.RunCompleted) return;

            string path = GetPlaceableSavePath(instance);
            if (path == null) return;

            Transform interiorT = instance.MasterInterior.transform;

            var placeablesInInterior = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            var entries = new List<string>();
            var savedGuids = new HashSet<string>();

            foreach (var p in placeablesInInterior)
            {
                if (p == null || string.IsNullOrEmpty(p.m_Guid)) continue;

                Vector3 relPos = interiorT.InverseTransformPoint(p.transform.position);
                Quaternion relRot = Quaternion.Inverse(interiorT.rotation) * p.transform.rotation;
                Vector3 scl = p.transform.localScale;
                bool active = p.gameObject.activeSelf;

                entries.Add($"{{\"g\":\"{p.m_Guid}\",\"px\":{relPos.x:R},\"py\":{relPos.y:R},\"pz\":{relPos.z:R},\"rx\":{relRot.x:R},\"ry\":{relRot.y:R},\"rz\":{relRot.z:R},\"rw\":{relRot.w:R},\"sx\":{scl.x:R},\"sy\":{scl.y:R},\"sz\":{scl.z:R},\"a\":{(active ? "true" : "false")}}}");
                savedGuids.Add(p.m_Guid);
            }

            var allPlaceables = UnityEngine.Object.FindObjectsOfType<Il2CppTLD.Placement.Placeable>(true);
            int movedCount = 0;
            foreach (var p in allPlaceables)
            {
                if (p == null || string.IsNullOrEmpty(p.m_Guid)) continue;
                if (savedGuids.Contains(p.m_Guid)) continue;

                // Tam boyut kontrolü
                if (!IsPositionInsideFull(instance, p.transform.position)) continue;

                Vector3 relPos = interiorT.InverseTransformPoint(p.transform.position);
                Quaternion relRot = Quaternion.Inverse(interiorT.rotation) * p.transform.rotation;
                Vector3 scl = p.transform.localScale;
                bool active = p.gameObject.activeSelf;

                entries.Add($"{{\"g\":\"{p.m_Guid}\",\"px\":{relPos.x:R},\"py\":{relPos.y:R},\"pz\":{relPos.z:R},\"rx\":{relRot.x:R},\"ry\":{relRot.y:R},\"rz\":{relRot.z:R},\"rw\":{relRot.w:R},\"sx\":{scl.x:R},\"sy\":{scl.y:R},\"sz\":{scl.z:R},\"a\":{(active ? "true" : "false")}}}");
                savedGuids.Add(p.m_Guid);
                movedCount++;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[PLACEABLE-SAVE] MOVED obje: guid={p.m_Guid} parent={p.transform.parent?.name ?? "ROOT"} relPos={relPos}");
            }

            string json = "[\n" + string.Join(",\n", entries) + "\n]";
            File.WriteAllText(path, json);

            if (s_DebugBounds)
                MelonLogger.Msg($"[PLACEABLE-SAVE] {instance.Config.InteriorSceneBaseName}: {entries.Count} Placeable kaydedildi ({movedCount} tasinmis): {path}");
        }

        private void RestorePlaceablePositions(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            string path = GetPlaceableSavePath(instance);
            if (path == null || !File.Exists(path))
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[PLACEABLE-LOAD] {instance.Config.InteriorSceneBaseName} Save dosyasi bulunamadi, atlaniyor.");
                return;
            }

            string json = File.ReadAllText(path);
            var guidToPosRot = new Dictionary<string, PlaceableEntry>();
            int idx = 0;
            while (idx < json.Length)
            {
                int start = json.IndexOf('{', idx);
                if (start < 0) break;
                int end = json.IndexOf('}', start);
                if (end < 0) break;

                string block = json.Substring(start + 1, end - start - 1);
                idx = end + 1;

                var entry = ParseEntry(block);
                if (entry != null && !string.IsNullOrEmpty(entry.guid))
                    guidToPosRot[entry.guid] = entry;
            }

            Transform interiorT = instance.MasterInterior.transform;
            var placeables = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            int restoredCount = 0;
            int changedCount = 0;
            var restoredGuids = new HashSet<string>();

            foreach (var p in placeables)
            {
                if (p == null || string.IsNullOrEmpty(p.m_Guid)) continue;
                if (!guidToPosRot.ContainsKey(p.m_Guid)) continue;

                var entry = guidToPosRot[p.m_Guid];

                Vector3 targetWorldPos = interiorT.TransformPoint(entry.position);
                Quaternion targetWorldRot = interiorT.rotation * entry.rotation;

                Vector3 oldWorldPos = p.transform.position;
                float dist = Vector3.Distance(oldWorldPos, targetWorldPos);

                p.transform.position = targetWorldPos;
                p.transform.rotation = targetWorldRot;
                p.transform.localScale = entry.scale;
                p.gameObject.SetActive(entry.active);
                p.m_Invalidated = false;

                restoredCount++;
                restoredGuids.Add(p.m_Guid);
                if (dist > 0.05f)
                {
                    changedCount++;
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[PLACEABLE-LOAD] CHANGED guid={p.m_Guid} dist={dist:F3} newWorldPos={targetWorldPos}");
                }
            }

            // MOVED objeler: Save sırasında bounds içinde ama child olmayan placeablelar
            // Bu objeler (ör. oyuncunun taşıdığı mobilyalar) sahne genelinde aranmalı
            if (restoredGuids.Count < guidToPosRot.Count)
            {
                var allPlaceables = UnityEngine.Object.FindObjectsOfType<Il2CppTLD.Placement.Placeable>(true);
                foreach (var p in allPlaceables)
                {
                    if (p == null || string.IsNullOrEmpty(p.m_Guid)) continue;
                    if (restoredGuids.Contains(p.m_Guid)) continue;
                    if (!guidToPosRot.ContainsKey(p.m_Guid)) continue;

                    var entry = guidToPosRot[p.m_Guid];

                    Vector3 targetWorldPos = interiorT.TransformPoint(entry.position);
                    Quaternion targetWorldRot = interiorT.rotation * entry.rotation;

                    Vector3 oldWorldPos = p.transform.position;
                    float dist = Vector3.Distance(oldWorldPos, targetWorldPos);

                    p.transform.position = targetWorldPos;
                    p.transform.rotation = targetWorldRot;
                    p.transform.localScale = entry.scale;
                    p.gameObject.SetActive(entry.active);
                    p.m_Invalidated = false;

                    restoredCount++;
                    restoredGuids.Add(p.m_Guid);
                    if (dist > 0.05f)
                    {
                        changedCount++;
                        if (s_DebugBounds)
                            MelonLogger.Msg($"[PLACEABLE-LOAD] MOVED-RESTORED guid={p.m_Guid} dist={dist:F3} newWorldPos={targetWorldPos}");
                    }
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PLACEABLE-LOAD] {instance.Config.InteriorSceneBaseName}: {restoredCount}/{guidToPosRot.Count} Placeable geri yuklendi ({changedCount} degismis).");
        }

        private class PlaceableEntry
        {
            public string guid;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool active;
        }

        private static PlaceableEntry ParseEntry(string block)
        {
            try
            {
                var e = new PlaceableEntry();
                e.guid = ExtractString(block, "\"g\":\"", "\"");
                e.position = new Vector3(
                    ExtractFloat(block, "\"px\":"),
                    ExtractFloat(block, "\"py\":"),
                    ExtractFloat(block, "\"pz\":"));
                e.rotation = new Quaternion(
                    ExtractFloat(block, "\"rx\":"),
                    ExtractFloat(block, "\"ry\":"),
                    ExtractFloat(block, "\"rz\":"),
                    ExtractFloat(block, "\"rw\":"));
                e.scale = new Vector3(
                    ExtractFloat(block, "\"sx\":"),
                    ExtractFloat(block, "\"sy\":"),
                    ExtractFloat(block, "\"sz\":"));
                string activeStr = ExtractString(block, "\"a\":", "}");
                if (activeStr == null) activeStr = ExtractString(block, "\"a\":", ",");
                e.active = activeStr != null && activeStr.Trim().StartsWith("true");
                return e;
            }
            catch { return null; }
        }

        private static string ExtractString(string src, string prefix, string suffix)
        {
            int i = src.IndexOf(prefix);
            if (i < 0) return null;
            i += prefix.Length;
            int j = src.IndexOf(suffix, i);
            if (j < 0) return src.Substring(i).Trim();
            return src.Substring(i, j - i).Trim();
        }

        private static float ExtractFloat(string src, string prefix)
        {
            int i = src.IndexOf(prefix);
            if (i < 0) return 0f;
            i += prefix.Length;
            int j = i;
            while (j < src.Length && (char.IsDigit(src[j]) || src[j] == '.' || src[j] == '-' || src[j] == 'E' || src[j] == 'e' || src[j] == '+'))
                j++;
            string val = src.Substring(i, j - i);
            if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result))
                return result;
            return 0f;
        }

        private void RestoreSceneSaveData(SeamlessInteriorInstance instance)
        {
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            if (!string.IsNullOrEmpty(currentSaveName))
            {
                if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData))
                {
                    MelonCoroutines.Start(DelayedFireRestore());
                }
                // ponytail: LoadSceneDataAdditive kaldırıldı.
                // Gear/Container/Placeable verileri artık tamamen JSON ile yönetiliyor.
                // LoadSceneDataAdditive oyunun kendi save verisinden ek gear spawn ediyordu
                // ve JSON restore ile dupelama yaratıyordu.
                // Ateş verileri FireManagerStealerPatch ile ayrıca yönetiliyor.
            }
        }

        // Tüm aktif klonları döngüye alarak çaldığımız (stolen) ateş verilerini yeniden kaydeder
        public static IEnumerator DelayedFireRestore()
        {
            yield return null;
            yield return null;
            yield return null;

            if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData))
            {
                foreach (var instance in ActiveInteriors.Values)
                {
                    if (instance.MasterInterior == null) continue;

                    var allFires = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Fire>(true);
                    foreach (var f in allFires) if (f != null && !Il2Cpp.FireManager.m_Fires.Contains(f)) Il2Cpp.FireManager.AddFire(f);

                    var allWoodStoves = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.WoodStove>(true);
                    foreach (var ws in allWoodStoves) if (ws != null && !Il2Cpp.FireManager.m_WoodStoves.Contains(ws)) Il2Cpp.FireManager.AddWoodStove(ws);

                    var allCampfires = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Campfire>(true);
                    foreach (var cf in allCampfires) if (cf != null && !Il2Cpp.FireManager.m_Campfires.Contains(cf)) Il2Cpp.FireManager.AddCampfire(cf);
                }

                yield return null;
                yield return null;

                PreventFireDestructionPatch.s_ProtectInterior = true;
                Il2Cpp.FireManager.Deserialize(FireManagerStealerPatch.s_StolenFireData);
                PreventFireDestructionPatch.s_ProtectInterior = false;

                FireManagerStealerPatch.s_StolenFireData = "";
            }
        }

        private static void DisableInteriorContainerSerialization(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;
            var containers = interiorRoot.GetComponentsInChildren<Il2Cpp.Container>(true);
            foreach (var c in containers)
            {
                if (c != null)
                {
                    var p = c.GetComponent<Il2CppTLD.Placement.Placeable>();
                    if (p != null) p.m_Invalidated = true;
                }
            }
        }

        // ─── TEST: Klon sahneler deaktifken aktif GearItem'ları JSON'a kaydet ───
        private static string GetInactiveSceneGearSavePath(SeamlessInteriorInstance instance)
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return null;
            string dir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SeamlessInteriorsData");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, saveName + "_" + instance.Config.ResolvedInstanceId + "_inactive_scene_gear.json");
        }

        public static void SaveAllInactiveSceneGearItems()
        {
            foreach (var instance in ActiveInteriors.Values)
            {
                SaveInactiveSceneGearItems(instance);
            }
        }

        public static void SaveInactiveSceneGearItems(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null || !instance.RunCompleted) return;

            string path = GetInactiveSceneGearSavePath(instance);
            if (path == null) return;

            Transform interiorT = instance.MasterInterior.transform;

            // includeInactive=true ile deaktif sahne altındaki tüm GearItem'ları buluyoruz
            var allGear = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true);
            var entries = new List<string>();
            var savedPositions = new HashSet<string>(); // Dupelama önleme

            foreach (var gear in allGear)
            {
                if (gear == null) continue;

                string gearName = gear.gameObject.name;
                var guidComp = gear.GetComponent<Il2Cpp.ObjectGuid>();
                string guid = (guidComp != null) ? guidComp.m_Guid : "";
                bool active = gear.gameObject.activeSelf;

                Vector3 relPos = interiorT.InverseTransformPoint(gear.transform.position);
                Quaternion relRot = Quaternion.Inverse(interiorT.rotation) * gear.transform.rotation;
                Vector3 scl = gear.transform.localScale;

                string posKey = $"{gearName}_{relPos.x:F2}_{relPos.y:F2}_{relPos.z:F2}";
                savedPositions.Add(posKey);

                entries.Add($"{{\"name\":\"{gearName}\",\"guid\":\"{guid}\",\"px\":{relPos.x:R},\"py\":{relPos.y:R},\"pz\":{relPos.z:R},\"rx\":{relRot.x:R},\"ry\":{relRot.y:R},\"rz\":{relRot.z:R},\"rw\":{relRot.w:R},\"sx\":{scl.x:R},\"sy\":{scl.y:R},\"sz\":{scl.z:R},\"a\":{(active ? "true" : "false")}}}");
            }

            // Bounds içinde ama child olmayan gearları da kaydet (yere atılan/sobaya konan itemlar)
            int extraCount = 0;
            var allSceneGear = UnityEngine.Object.FindObjectsOfType<Il2Cpp.GearItem>(true);
            foreach (var gear in allSceneGear)
            {
                if (gear == null) continue;
                if (gear.transform.IsChildOf(instance.MasterInterior.transform)) continue; // Zaten kaydedildi
                if (gear.transform.root.name.Contains("CHARACTER_FPSPlayer")) continue; // Oyuncunun elindeki item

                if (!IsPositionInsideFull(instance, gear.transform.position)) continue;

                string gearName = gear.gameObject.name;
                var guidComp = gear.GetComponent<Il2Cpp.ObjectGuid>();
                string guid = (guidComp != null) ? guidComp.m_Guid : "";
                bool active = gear.gameObject.activeSelf;

                Vector3 relPos = interiorT.InverseTransformPoint(gear.transform.position);
                Quaternion relRot = Quaternion.Inverse(interiorT.rotation) * gear.transform.rotation;
                Vector3 scl = gear.transform.localScale;

                string posKey = $"{gearName}_{relPos.x:F2}_{relPos.y:F2}_{relPos.z:F2}";
                if (savedPositions.Contains(posKey)) continue; // Dupelama önleme
                savedPositions.Add(posKey);

                entries.Add($"{{\"name\":\"{gearName}\",\"guid\":\"{guid}\",\"px\":{relPos.x:R},\"py\":{relPos.y:R},\"pz\":{relPos.z:R},\"rx\":{relRot.x:R},\"ry\":{relRot.y:R},\"rz\":{relRot.z:R},\"rw\":{relRot.w:R},\"sx\":{scl.x:R},\"sy\":{scl.y:R},\"sz\":{scl.z:R},\"a\":{(active ? "true" : "false")}}}");
                extraCount++;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-SAVE] EXTRA obje: {gearName} guid={guid} parent={gear.transform.parent?.name ?? "ROOT"} relPos={relPos}");
            }

            string json = "[\\n" + string.Join(",\\n", entries) + "\\n]";
            File.WriteAllText(path, json);

            // Aktif/deaktif sayısını logla
            int activeCount = 0;
            int inactiveCount = 0;
            foreach (var gear in allGear)
            {
                if (gear == null) continue;
                if (gear.gameObject.activeSelf) activeCount++;
                else inactiveCount++;
            }

            MelonLogger.Msg($"[GEAR-TEST] {instance.Config.InteriorSceneBaseName}: {activeCount} aktif, {inactiveCount} deaktif GearItem (toplam {entries.Count}, {extraCount} extra): {path}");

            if (s_DebugBounds)
            {
                foreach (var gear in allGear)
                {
                    if (gear == null) continue;
                    if (!gear.gameObject.activeSelf)
                    {
                        string parentName = gear.transform.parent != null ? gear.transform.parent.name : "ROOT";
                        bool inHierarchy = gear.gameObject.activeInHierarchy;
                        MelonLogger.Msg($"[GEAR-INACTIVE]   XX {gear.gameObject.name} pos={gear.transform.position} parent={parentName} activeInHierarchy={inHierarchy}");
                    }
                    else
                    {
                        MelonLogger.Msg($"[GEAR-TEST]   -> {gear.gameObject.name} pos={gear.transform.position}");
                    }
                }
            }
        }

        // ─── Kayıtlı gear'ları geri yükle (oyun çık/gir sonrası kaybolan itemlar için) ───
        private class GearSaveEntry
        {
            public string name;
            public string guid;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool active = true;
        }

        private static GearSaveEntry ParseGearEntry(string block)
        {
            try
            {
                var e = new GearSaveEntry();
                e.name = ExtractString(block, "\"name\":\"", "\"");
                e.guid = ExtractString(block, "\"guid\":\"", "\"");
                e.position = new Vector3(
                    ExtractFloat(block, "\"px\":"),
                    ExtractFloat(block, "\"py\":"),
                    ExtractFloat(block, "\"pz\":"));
                e.rotation = new Quaternion(
                    ExtractFloat(block, "\"rx\":"),
                    ExtractFloat(block, "\"ry\":"),
                    ExtractFloat(block, "\"rz\":"),
                    ExtractFloat(block, "\"rw\":"));
                e.scale = new Vector3(
                    ExtractFloat(block, "\"sx\":"),
                    ExtractFloat(block, "\"sy\":"),
                    ExtractFloat(block, "\"sz\":"));
                string activeStr = ExtractString(block, "\"a\":", "}");
                if (activeStr == null) activeStr = ExtractString(block, "\"a\":", ",");
                // Eğer "a" alanı yoksa (eski format), varsayılan olarak aktif kabul et
                e.active = (activeStr == null) || activeStr.Trim().StartsWith("true");
                return e;
            }
            catch { return null; }
        }

        public static void RestoreInactiveSceneGearItems(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            string path = GetInactiveSceneGearSavePath(instance);
            if (path == null || !File.Exists(path))
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName} gear save dosyasi bulunamadi, atlaniyor.");
                return;
            }

            string json = File.ReadAllText(path);
            var savedEntries = new List<GearSaveEntry>();
            int idx = 0;
            while (idx < json.Length)
            {
                int start = json.IndexOf('{', idx);
                if (start < 0) break;
                int end = json.IndexOf('}', start);
                if (end < 0) break;

                string block = json.Substring(start + 1, end - start - 1);
                idx = end + 1;

                var entry = ParseGearEntry(block);
                if (entry != null && !string.IsNullOrEmpty(entry.name))
                    savedEntries.Add(entry);
            }

            if (savedEntries.Count == 0)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName} kayitli gear yok.");
                return;
            }

            Transform interiorT = instance.MasterInterior.transform;

            // Restore öncesi klon sahnedeki tüm mevcut GearItem'ları anında sil (dupelama önleme)
            // DestroyImmediate kullanıyoruz çünkü Destroy gecikmeli çalışır ve
            // aynı frame'de spawn edilen yeni gearlarla çakışma yaratır
            var existingGear = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true);
            int deletedCount = 0;
            foreach (var gear in existingGear)
            {
                if (gear == null) continue;
                UnityEngine.Object.DestroyImmediate(gear.gameObject);
                deletedCount++;
            }

            // Bounds içinde ama child olmayan gearları da sil (yere atılmış/sobaya konmuş itemlar)
            var allSceneGear = UnityEngine.Object.FindObjectsOfType<Il2Cpp.GearItem>(true);
            foreach (var gear in allSceneGear)
            {
                if (gear == null || gear.gameObject == null) continue;
                if (gear.transform.root.name.Contains("CHARACTER_FPSPlayer")) continue;
                if (!IsPositionInsideFull(instance, gear.transform.position)) continue;

                UnityEngine.Object.DestroyImmediate(gear.gameObject);
                deletedCount++;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName}: {deletedCount} mevcut gear silindi (dupelama onleme).");

            int restoredCount = 0;
            int failedCount = 0;

            foreach (var entry in savedEntries)
            {

                // İsimden "(Clone)" ve numaraları temizle
                string cleanName = entry.name.Replace("(Clone)", "").Trim();
                int parenIdx = cleanName.LastIndexOf(" (");
                if (parenIdx > 0 && cleanName.EndsWith(")"))
                    cleanName = cleanName.Substring(0, parenIdx).Trim();

                GameObject prefab = null;

                // 1) Addressables ile prefab yükle
                try
                {
                    var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(cleanName);
                    handle.WaitForCompletion();
                    if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && handle.Result != null)
                        prefab = handle.Result;
                }
                catch { }

                if (prefab == null)
                {
                    failedCount++;
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[GEAR-RESTORE] PREFAB BULUNAMADI: {cleanName} (orijinal: {entry.name})");
                    continue;
                }

                Vector3 worldPos = interiorT.TransformPoint(entry.position);
                Quaternion worldRot = interiorT.rotation * entry.rotation;

                GameObject spawned = UnityEngine.Object.Instantiate(prefab, worldPos, worldRot);
                spawned.transform.SetParent(instance.MasterInterior.transform, true);
                spawned.transform.localScale = entry.scale;
                spawned.SetActive(entry.active);

                if (!string.IsNullOrEmpty(entry.guid))
                {
                    var guidComp = spawned.GetComponent<Il2Cpp.ObjectGuid>();
                    if (guidComp == null) guidComp = spawned.AddComponent<Il2Cpp.ObjectGuid>();
                    guidComp.m_Guid = entry.guid;
                }

                restoredCount++;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-RESTORE] SPAWNED: {entry.name} worldPos={worldPos}");
            }

            MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName}: {restoredCount} gear geri yuklendi, {failedCount} prefab bulunamadi (toplam kayit: {savedEntries.Count})");
        }

        // ─── Klon sahnedeki konteyner verilerini kaydet/yükle ───
        private static string GetContainerSavePath(SeamlessInteriorInstance instance)
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return null;
            string dir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SeamlessInteriorsData");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, saveName + "_" + instance.Config.ResolvedInstanceId + "_containers.json");
        }

        public static void SaveAllContainerData()
        {
            foreach (var instance in ActiveInteriors.Values)
            {
                SaveContainerData(instance);
            }
        }

        public static void SaveContainerData(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null || !instance.RunCompleted) return;

            string path = GetContainerSavePath(instance);
            if (path == null) return;

            Transform interiorT = instance.MasterInterior.transform;
            var containers = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Container>(true);
            var entries = new List<string>();

            // Aynı isimde birden fazla konteyner olabilir, her birine sıra indeksi ver
            var nameCounter = new Dictionary<string, int>();

            foreach (var c in containers)
            {
                if (c == null) continue;

                string containerName = c.gameObject.name;

                // Bu isimden kaçıncı konteyner?
                if (!nameCounter.ContainsKey(containerName)) nameCounter[containerName] = 0;
                int nameIndex = nameCounter[containerName]++;

                // Benzersiz anahtar: isim + sıra indeksi
                string matchKey = $"{containerName}###{nameIndex}";

                string serialized = "";
                try
                {
                    serialized = c.Serialize();
                }
                catch (System.Exception ex)
                {
                    if (s_DebugBounds)
                        MelonLogger.Warning($"[CONTAINER-SAVE] Serialize hatasi: {matchKey} - {ex.Message}");
                    continue;
                }

                if (string.IsNullOrEmpty(serialized)) continue;

                // JSON-safe: serialized data içindeki tırnak ve newline'ları escape et
                string escapedData = serialized.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                entries.Add($"{{\"key\":\"{matchKey}\",\"data\":\"{escapedData}\"}}");

                if (s_DebugBounds)
                    MelonLogger.Msg($"[CONTAINER-SAVE] Kaydedildi: {matchKey} dataLen={serialized.Length}");
            }

            string json = "[\n" + string.Join(",\n", entries) + "\n]";
            File.WriteAllText(path, json);

            if (s_DebugBounds)
                MelonLogger.Msg($"[CONTAINER-SAVE] {instance.Config.InteriorSceneBaseName}: {entries.Count} konteyner kaydedildi: {path}");
        }

        public static void RestoreContainerData(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            string path = GetContainerSavePath(instance);
            if (path == null || !File.Exists(path))
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[CONTAINER-LOAD] {instance.Config.InteriorSceneBaseName} konteyner save dosyasi bulunamadi, atlaniyor.");
                return;
            }

            string json = File.ReadAllText(path);

            // key -> serialized data map'i oluştur
            var keyToData = new Dictionary<string, string>();
            int idx = 0;
            while (idx < json.Length)
            {
                // "key":" ara
                int keyIdx = json.IndexOf("\"key\":\"", idx);
                if (keyIdx < 0) break;
                int keyStart = keyIdx + 7; // "key":"  uzunluğu
                int keyEnd = json.IndexOf("\"", keyStart);
                if (keyEnd < 0) break;
                string key = json.Substring(keyStart, keyEnd - keyStart);

                // "data":" ara
                int dataKeyIdx = json.IndexOf("\"data\":\"", keyEnd);
                if (dataKeyIdx < 0) break;
                int dataStart = dataKeyIdx + 8; // "data":"  uzunluğu

                // Escaped tırnaklardan kaçarak kapanış tırnağını bul
                int dataEnd = dataStart;
                while (dataEnd < json.Length)
                {
                    dataEnd = json.IndexOf("\"", dataEnd);
                    if (dataEnd < 0) { dataEnd = json.Length; break; }
                    int backslashCount = 0;
                    int checkPos = dataEnd - 1;
                    while (checkPos >= dataStart && json[checkPos] == '\\') { backslashCount++; checkPos--; }
                    if (backslashCount % 2 == 0) break;
                    dataEnd++;
                }

                string escapedData = json.Substring(dataStart, dataEnd - dataStart);
                string data = escapedData.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(data))
                    keyToData[key] = data;

                idx = dataEnd + 1;
            }

            if (keyToData.Count == 0)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[CONTAINER-LOAD] {instance.Config.InteriorSceneBaseName}: dosyada konteyner verisi yok.");
                return;
            }

            // Klon sahnedeki konteynerlere veriyi isim+indeks bazlı eşleştirmeyle geri yükle
            var containers = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Container>(true);
            int restoredCount = 0;
            var nameCounter = new Dictionary<string, int>();

            foreach (var c in containers)
            {
                if (c == null) continue;

                string containerName = c.gameObject.name;
                if (!nameCounter.ContainsKey(containerName)) nameCounter[containerName] = 0;
                int nameIndex = nameCounter[containerName]++;
                string matchKey = $"{containerName}###{nameIndex}";

                if (keyToData.ContainsKey(matchKey))
                {
                    try
                    {
                        var loadedItems = new Il2CppSystem.Collections.Generic.List<Il2Cpp.GearItem>();
                        c.Deserialize(keyToData[matchKey], loadedItems);
                        restoredCount++;

                        if (s_DebugBounds)
                            MelonLogger.Msg($"[CONTAINER-LOAD] RESTORED: {matchKey} loadedItems={loadedItems.Count}");
                    }
                    catch (System.Exception ex)
                    {
                        if (s_DebugBounds)
                            MelonLogger.Warning($"[CONTAINER-LOAD] Deserialize hatasi: {matchKey} - {ex.Message}");
                    }
                }
                else if (s_DebugBounds)
                {
                    MelonLogger.Msg($"[CONTAINER-LOAD] ESLESME YOK: {matchKey}");
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[CONTAINER-LOAD] {instance.Config.InteriorSceneBaseName}: {restoredCount}/{keyToData.Count} konteyner geri yuklendi.");
        }

        // Yardımcı Metot: Daraltmasız (Shrinksiz) Bounds Kontrolü -> ARTIK DIREKT RAYCAST KULLANIYOR
        public static bool IsPositionInsideFull(SeamlessInteriorInstance instance, Vector3 pos)
        {
            // Önce raycast tabanlı kontrolü dene (MasterInterior aktifse en doğru sonuç)
            if (instance.MasterInterior != null && instance.MasterInterior.activeSelf)
                return instance.IsPositionInside(pos);

            // MasterInterior deaktifse raycast çalışmaz.
            // InteriorTrigger bounds kontrolüne düş (save sırasında gerekli).
            if (instance.InteriorTrigger != null)
            {
                Vector3 localPos = instance.InteriorTrigger.transform.InverseTransformPoint(pos);
                Bounds localBounds = new Bounds(instance.InteriorTrigger.center, instance.InteriorTrigger.size);
                localBounds.Expand(-0.5f);
                return localBounds.Contains(localPos);
            }

            return false;
        }
    }
}
