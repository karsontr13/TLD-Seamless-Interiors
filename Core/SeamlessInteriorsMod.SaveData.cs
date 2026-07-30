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
        private static string GetPlaceableSavePath()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return null;
            string dir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "SeamlessInteriorsData");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, saveName + "_placeables.json");
        }

        // --- SAVE: write the clone scene's Placeable positions to JSON ---
        // All positions are saved relative to s_MasterInterior (interior-local space),
        // not world space, so they still line up correctly even if the interior's world
        // position/rotation ever shifts (e.g. after a scene reattach).
        public static void SavePlaceablePositions()
        {
            if (s_MasterInterior == null || !s_RunCompleted) return;

            string path = GetPlaceableSavePath();
            if (path == null) return;

            Transform interiorT = s_MasterInterior.transform;

            var placeablesInInterior = s_MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
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
                if (!IsPositionInsideCabinFull(p.transform.position)) continue;

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
                MelonLogger.Msg($"[PLACEABLE-SAVE] {entries.Count} Placeable kaydedildi ({movedCount} tasinmis): {path}");
        }

        // --- LOAD: read the Placeable positions back from JSON and apply them ---
        private void RestorePlaceablePositions()
        {
            if (s_MasterInterior == null) return;

            string path = GetPlaceableSavePath();
            if (path == null || !File.Exists(path))
            {
                if (s_DebugBounds)
                    MelonLogger.Msg("[PLACEABLE-LOAD] Save dosyasi bulunamadi, atlaniyor.");
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

            Transform interiorT = s_MasterInterior.transform;
            var placeables = s_MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            int restoredCount = 0;
            int changedCount = 0;

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
                if (dist > 0.01f)
                {
                    changedCount++;
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[PLACEABLE-LOAD] CHANGED guid={p.m_Guid} dist={dist:F3} newWorldPos={targetWorldPos}");
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PLACEABLE-LOAD] {restoredCount}/{guidToPosRot.Count} Placeable geri yuklendi ({changedCount} degismis).");
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

        private void RestoreSceneSaveData()
        {
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            if (!string.IsNullOrEmpty(currentSaveName))
            {
                if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData))
                {
                    MelonCoroutines.Start(DelayedFireRestore());
                }

                SaveGameSystem.LoadSceneDataAdditive(currentSaveName, EXTERIOR);
            }
        }

        public static IEnumerator DelayedFireRestore()
        {
            yield return null;
            yield return null;
            yield return null;

            if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData) && s_MasterInterior != null)
            {
                var allFires = s_MasterInterior.GetComponentsInChildren<Il2Cpp.Fire>(true);
                foreach (var f in allFires) if (f != null && !Il2Cpp.FireManager.m_Fires.Contains(f)) Il2Cpp.FireManager.AddFire(f);

                var allWoodStoves = s_MasterInterior.GetComponentsInChildren<Il2Cpp.WoodStove>(true);
                foreach (var ws in allWoodStoves) if (ws != null && !Il2Cpp.FireManager.m_WoodStoves.Contains(ws)) Il2Cpp.FireManager.AddWoodStove(ws);

                var allCampfires = s_MasterInterior.GetComponentsInChildren<Il2Cpp.Campfire>(true);
                foreach (var cf in allCampfires) if (cf != null && !Il2Cpp.FireManager.m_Campfires.Contains(cf)) Il2Cpp.FireManager.AddCampfire(cf);

                while (s_MasterInterior != null && !s_MasterInterior.activeInHierarchy) yield return null;
                if (s_MasterInterior == null) yield break;

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
    }
}
