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
        // Key prefix recording which clone scene the player was in.
        private const string PLAYER_INSIDE_KEY_PREFIX = "SeamlessInteriors_PlayerInside_";

        // Key prefixes recording the player's position relative (local-space) to the clone scene.
        private const string PLAYER_LOCALPOS_KEY_PREFIX = "SeamlessInteriors_PlayerLocalPos_";
        private const string PLAYER_LOCALROT_KEY_PREFIX = "SeamlessInteriors_PlayerLocalRot_";

        public static void SavePlayerInsideState()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) return;

            string insideKey = PLAYER_INSIDE_KEY_PREFIX + saveName;
            string posKey = PLAYER_LOCALPOS_KEY_PREFIX + saveName;
            string rotKey = PLAYER_LOCALROT_KEY_PREFIX + saveName;

            string insideInstanceId = "";
            bool localTransformWritten = false;

            foreach (var instance in ActiveInteriors.Values)
            {
                if (!instance.RunCompleted) continue;
                if (instance.IsPositionInside(playerT.position))
                {
                    insideInstanceId = instance.Config.ResolvedInstanceId;

                    // Store the player's position relative to the clone scene.
                    if (instance.MasterInterior != null)
                    {
                        Vector3 localPos = instance.MasterInterior.transform.InverseTransformPoint(playerT.position);
                        Quaternion localRot = Quaternion.Inverse(instance.MasterInterior.transform.rotation) * playerT.rotation;

                        // ":R" keeps full float precision through the round trip.
                        UnityEngine.PlayerPrefs.SetString(posKey, $"{localPos.x:R},{localPos.y:R},{localPos.z:R}");
                        UnityEngine.PlayerPrefs.SetString(rotKey, $"{localRot.x:R},{localRot.y:R},{localRot.z:R},{localRot.w:R}");
                        localTransformWritten = true;

                        if (s_DebugBounds)
                            MelonLogger.Msg($"[SAVE-STATE] Oyuncu local pozisyon kaydedildi: {localPos} rot: {localRot.eulerAngles}");
                    }
                    break;
                }
            }

            // CRITICAL: if the player is OUTSIDE a clone scene in this save, the local
            // position left over from the previous save MUST be deleted. Otherwise the old
            // record stays in PlayerPrefs and the next load teleports the player to the
            // point where they last saved inside a clone scene, even though they saved outside.
            if (!localTransformWritten)
            {
                UnityEngine.PlayerPrefs.DeleteKey(posKey);
                UnityEngine.PlayerPrefs.DeleteKey(rotKey);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[SAVE-STATE] Oyuncu disarida kaydetti, eski local pozisyon kaydi silindi.");
            }

            UnityEngine.PlayerPrefs.SetString(insideKey, insideInstanceId);
            UnityEngine.PlayerPrefs.Save();

            if (s_DebugBounds)
                MelonLogger.Msg($"[SAVE-STATE] Oyuncu kayit pozisyonu: {(string.IsNullOrEmpty(insideInstanceId) ? "DISARIDA" : insideInstanceId)}");
        }

        public static void ClearSavedPlayerInsideState()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return;

            UnityEngine.PlayerPrefs.SetString(PLAYER_INSIDE_KEY_PREFIX + saveName, "");
            UnityEngine.PlayerPrefs.DeleteKey(PLAYER_LOCALPOS_KEY_PREFIX + saveName);
            UnityEngine.PlayerPrefs.DeleteKey(PLAYER_LOCALROT_KEY_PREFIX + saveName);
            UnityEngine.PlayerPrefs.Save();
        }
        public static string GetSavedPlayerInsideInstanceId()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return "";

            string insideKey = PLAYER_INSIDE_KEY_PREFIX + saveName;
            return UnityEngine.PlayerPrefs.GetString(insideKey, "");
        }

        public static bool HasSavedPlayerInsideState()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return false;

            return UnityEngine.PlayerPrefs.HasKey(PLAYER_INSIDE_KEY_PREFIX + saveName);
        }

        public static Vector3? GetSavedPlayerLocalPosition(string instanceId = null)
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return null;

            if (instanceId != null && GetSavedPlayerInsideInstanceId() != instanceId) return null;

            string posKey = PLAYER_LOCALPOS_KEY_PREFIX + saveName;
            string posStr = UnityEngine.PlayerPrefs.GetString(posKey, "");
            if (string.IsNullOrEmpty(posStr)) return null;

            string[] parts = posStr.Split(',');
            if (parts.Length != 3) return null;

            // InvariantCulture: the value must read back the same regardless of the
            // system's decimal separator.
            if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                return new Vector3(x, y, z);
            }
            return null;
        }

        public static Quaternion? GetSavedPlayerLocalRotation(string instanceId = null)
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return null;

            if (instanceId != null && GetSavedPlayerInsideInstanceId() != instanceId) return null;

            string rotKey = PLAYER_LOCALROT_KEY_PREFIX + saveName;
            string rotStr = UnityEngine.PlayerPrefs.GetString(rotKey, "");
            if (string.IsNullOrEmpty(rotStr)) return null;

            string[] parts = rotStr.Split(',');
            if (parts.Length != 4) return null;

            if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z) &&
                float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w))
            {
                return new Quaternion(x, y, z, w);
            }
            return null;
        }

        private static string GetInstanceSavePath(SeamlessInteriorInstance instance, string suffix)
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return null;
            return Path.Combine(ModPaths.DataDir(), saveName + "_" + instance.Config.ResolvedInstanceId + suffix);
        }

        // Each building's data now lives in its own file, named after its instance id.
        private static string GetPlaceableSavePath(SeamlessInteriorInstance instance)
        {
            return GetInstanceSavePath(instance, "_placeables.json");
        }

        // Global save trigger (called from the Harmony patch).
        public static void SaveAllPlaceablePositions()
        {
            foreach (var instance in ActiveInteriors.Values)
            {
                SavePlaceablePositions(instance);
            }
        }

        // Writes the positions of the placed objects (furniture, decorations) of one
        // building: the game's own save system cannot match them inside a clone.
        public static void SavePlaceablePositions(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null || !instance.RunCompleted) return;

            string path = GetPlaceableSavePath(instance);
            if (path == null) return;

            Transform interiorT = instance.MasterInterior.transform;

            var placeablesInInterior = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            var entries = new List<string>();
            var savedGuids = new HashSet<string>();

            // Positions are stored relative to the clone, so they survive the clone being
            // rebuilt somewhere else.
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

            // ─── Moved (non-child) placeables ───
            //
            // PERFORMANCE: there used to be three expensive things here, each repeated PER
            // BUILDING (15 times on Mystery Lake):
            //   1. a full scene scan via FindObjectsOfType
            //   2. toggling the clone scene with SetActive(true)/SetActive(false) - which
            //      sends OnEnable/OnDisable to thousands of components and rebuilds the
            //      physics broadphase; the main source of the save stall
            //   3. a ray test for every object
            //
            // Now: the scan is shared through SceneScan, the coarse AABB pre-filter drops
            // ~99% of the objects on the very first line, and SetActive only happens when
            // there REALLY is a borderline candidate (pending.Count > 0). In practice
            // buildings the player never visited produce no candidates at all.
            var allPlaceables = SceneScan.PlaceablesAll();
            int movedCount = 0;

            Bounds filter;
            bool hasFilter = TryGetWorldFilterBounds(instance, out filter);
            List<Il2CppTLD.Placement.Placeable> pending = null;

            foreach (var p in allPlaceables)
            {
                if (p == null || string.IsNullOrEmpty(p.m_Guid)) continue;
                if (savedGuids.Contains(p.m_Guid)) continue;

                Vector3 pos = p.transform.position;
                StrayVerdict verdict = ClassifyStray(instance, hasFilter, filter, pos);
                if (verdict == StrayVerdict.Outside) continue;

                // NEVER touch objects on the player, in their hands, in their inventory or
                // under WorldView.
                if (IsPlayerOrInventory(p.transform)) continue;

                if (verdict == StrayVerdict.NeedsRaycast)
                {
                    // The ray test requires the clone scene to be OPEN; candidates are
                    // collected and processed together below.
                    if (pending == null) pending = new List<Il2CppTLD.Placement.Placeable>();
                    pending.Add(p);
                    continue;
                }

                AppendPlaceableEntry(p, interiorT, entries, savedGuids);
                movedCount++;
            }

            if (pending != null && pending.Count > 0)
            {
                bool wasActive = instance.MasterInterior.activeSelf;
                if (!wasActive) instance.MasterInterior.SetActive(true);

                foreach (var p in pending)
                {
                    if (p == null || p.gameObject == null) continue;
                    if (savedGuids.Contains(p.m_Guid)) continue;
                    if (!instance.IsPositionInsideRaycastOnly(p.transform.position, SeamlessInteriorInstance.ITEM_RAY_ORIGIN_LIFT)) continue;

                    AppendPlaceableEntry(p, interiorT, entries, savedGuids);
                    movedCount++;
                }

                if (!wasActive) instance.MasterInterior.SetActive(false);
            }

            // NOTE: this used to write "\\n" (backslash + n) instead of a real newline.
            // The parser ignores the separators, so old files still read fine.
            string json = "[\n" + string.Join(",\n", entries) + "\n]";
            JsonWriteCache.Write(path, json);

            if (s_DebugBounds)
                MelonLogger.Msg($"[PLACEABLE-SAVE] {instance.Config.InteriorSceneBaseName}: {entries.Count} Placeable kaydedildi ({movedCount} tasinmis): {path}");
        }

        // Shared writer for the "moved placeable" paths above.
        private static void AppendPlaceableEntry(Il2CppTLD.Placement.Placeable p, Transform interiorT,
                                                 List<string> entries, HashSet<string> savedGuids)
        {
            Vector3 relPos = interiorT.InverseTransformPoint(p.transform.position);
            Quaternion relRot = Quaternion.Inverse(interiorT.rotation) * p.transform.rotation;
            Vector3 scl = p.transform.localScale;
            bool active = p.gameObject.activeSelf;

            entries.Add($"{{\"g\":\"{p.m_Guid}\",\"px\":{relPos.x:R},\"py\":{relPos.y:R},\"pz\":{relPos.z:R},\"rx\":{relRot.x:R},\"ry\":{relRot.y:R},\"rz\":{relRot.z:R},\"rw\":{relRot.w:R},\"sx\":{scl.x:R},\"sy\":{scl.y:R},\"sz\":{scl.z:R},\"a\":{(active ? "true" : "false")}}}");
            savedGuids.Add(p.m_Guid);

            if (s_DebugBounds)
                MelonLogger.Msg($"[PLACEABLE-SAVE] MOVED obje: guid={p.m_Guid} parent={p.transform.parent?.name ?? "ROOT"} relPos={relPos}");
        }

        // Applies the saved placeable positions back onto the freshly built clone.
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

                if (!guidToPosRot.ContainsKey(p.m_Guid))
                {
                    // CRITICAL: an object missing from the save file was taken into the
                    // player's inventory through the safehouse feature, or destroyed. The
                    // Addressables template respawns it, so leaving it in the scene would
                    // both DUPLICATE the inventory copy and create double records / errors
                    // while saving. Switch it off.
                    p.gameObject.SetActive(false);
                    continue;
                }

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

            // MOVED objects: placeables that were inside the bounds at save time but are not
            // children of the clone (e.g. furniture the player carried around). These have
            // to be searched for scene-wide.
            if (restoredGuids.Count < guidToPosRot.Count)
            {
                var allPlaceables = SceneScan.PlaceablesAll();
                foreach (var p in allPlaceables)
                {
                    if (p == null || string.IsNullOrEmpty(p.m_Guid)) continue;
                    if (restoredGuids.Contains(p.m_Guid)) continue;
                    if (!guidToPosRot.ContainsKey(p.m_Guid)) continue;

                    // NEVER touch objects on the player, in their hands or in their inventory.
                    if (IsPlayerOrInventory(p.transform)) continue;

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

        // Minimal reader for one placeable record. The mod writes these files itself, so a
        // full JSON library is not worth pulling in.
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

        // Reads the number following prefix, accepting exponent notation (the ":R" format
        // can produce values like 1E-07).
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
                // LoadSceneDataAdditive was removed.
                // Gear/Container/Placeable data is now managed entirely through JSON.
                // LoadSceneDataAdditive spawned extra gear from the game's own save data and
                // duplicated it against the JSON restore.
                // Fire data is handled separately by FireManagerStealerPatch.
            }
        }

        // Re-applies the fire data we stole, looping over every active clone.
        public static IEnumerator DelayedFireRestore()
        {
            // A few frames, so the clones' fire objects have come up.
            yield return null;
            yield return null;
            yield return null;

            if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData))
            {
                // FireManager only knows the fires registered with it; clone fires have to
                // be added by hand.
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

                // CRITICAL: try/finally is mandatory. If Deserialize throws,
                // s_ProtectInterior stays true forever and EVERY Destroy() under
                // MasterInterior is blocked (broken objects stay in the hierarchy with
                // activeSelf=true and come back after a save/load).
                PreventFireDestructionPatch.s_ProtectInterior = true;
                try
                {
                    Il2Cpp.FireManager.Deserialize(FireManagerStealerPatch.s_StolenFireData);
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[FIRE-RESTORE] FireManager.Deserialize hatasi: {ex.Message}");
                }
                finally
                {
                    PreventFireDestructionPatch.s_ProtectInterior = false;
                }

                FireManagerStealerPatch.s_StolenFireData = "";
            }
        }

        // Marks the clone's containers as invalidated so the game's placement system does
        // not serialize them; their contents are handled by _containers.json instead.
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

        // ─── Loose GearItems inside clone scenes, saved to JSON ───
        private static string GetInactiveSceneGearSavePath(SeamlessInteriorInstance instance)
        {
            return GetInstanceSavePath(instance, "_inactive_scene_gear.json");
        }

        public static void SaveAllInactiveSceneGearItems()
        {
            foreach (var instance in ActiveInteriors.Values)
            {
                SaveInactiveSceneGearItems(instance);
            }
        }

        private static bool IsInsideContainer(Transform t, Transform stopAt)
        {
            if (t == null) return false;

            Transform p = t.parent;
            while (p != null && p != stopAt)
            {
                if (p.GetComponent<Il2Cpp.Container>() != null) return true;
                p = p.parent;
            }
            return false;
        }

        private static bool IsHierarchyActiveUpTo(Transform t, Transform stopAt)
        {
            Transform cur = t;
            while (cur != null)
            {
                if (cur == stopAt) break;
                if (!cur.gameObject.activeSelf) return false;
                cur = cur.parent;
            }
            return true;
        }

        private static bool ShouldPersistGear(Il2Cpp.GearItem gear, Transform interiorT)
        {
            if (gear == null || gear.gameObject == null) return false;

            // Filter off: the old behaviour, everything is saved (for troubleshooting).
            if (!IsGearSaveFilterEnabled) return true;

            if (!gear.gameObject.activeSelf) return false;
            if (IsInsideContainer(gear.transform, interiorT)) return false;
            return IsHierarchyActiveUpTo(gear.transform, interiorT);
        }

        private static string BuildGearPosKey(Il2Cpp.GearItem gear, Transform interiorT)
        {
            Vector3 relPos = interiorT.InverseTransformPoint(gear.transform.position);
            return $"{gear.gameObject.name}_{relPos.x:F2}_{relPos.y:F2}_{relPos.z:F2}";
        }

        private static string BuildGearEntryJson(Il2Cpp.GearItem gear, Transform interiorT)
        {
            string gearName = gear.gameObject.name;
            var guidComp = gear.GetComponent<Il2Cpp.ObjectGuid>();
            string guid = (guidComp != null) ? guidComp.m_Guid : "";
            bool active = gear.gameObject.activeSelf;

            Vector3 relPos = interiorT.InverseTransformPoint(gear.transform.position);
            Quaternion relRot = Quaternion.Inverse(interiorT.rotation) * gear.transform.rotation;
            Vector3 scl = gear.transform.localScale;

            string serialized = null;
            try
            {
                serialized = gear.SerializeToString();
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Warning($"[GEAR-SAVE] SerializeToString hatasi: {gearName} - {ex.Message}");
            }

            string entry = $"{{\"name\":\"{gearName}\",\"guid\":\"{guid}\",\"px\":{relPos.x:R},\"py\":{relPos.y:R},\"pz\":{relPos.z:R},\"rx\":{relRot.x:R},\"ry\":{relRot.y:R},\"rz\":{relRot.z:R},\"rw\":{relRot.w:R},\"sx\":{scl.x:R},\"sy\":{scl.y:R},\"sz\":{scl.z:R},\"a\":{(active ? "true" : "false")}";

            if (!string.IsNullOrEmpty(serialized))
                entry += $",\"s\":\"{EscapeJson(serialized)}\"";

            return entry + "}";
        }

        public static void SaveInactiveSceneGearItems(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null || !instance.RunCompleted) return;

            string path = GetInactiveSceneGearSavePath(instance);
            if (path == null) return;

            Transform interiorT = instance.MasterInterior.transform;

            // includeInactive=true so every GearItem is visible even while the clone scene
            // is switched off. (ShouldPersistGear decides which ones are SAVED.)
            var allGear = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true);
            var entries = new List<string>();
            var savedPositions = new HashSet<string>(); // de-duplication
            int skippedCount = 0;

            foreach (var gear in allGear)
            {
                if (gear == null) continue;

                // Container contents / items the player took / eliminated loot candidates are
                // not saved - otherwise another copy is added to the scene on every load.
                if (!ShouldPersistGear(gear, interiorT))
                {
                    skippedCount++;
                    continue;
                }

                savedPositions.Add(BuildGearPosKey(gear, interiorT));
                entries.Add(BuildGearEntryJson(gear, interiorT));
            }

            // ─── Gear inside the bounds but not a child (dropped items, items on a stove) ───
            //
            // PERFORMANCE: see the same pattern in SavePlaceablePositions. The scene scan is
            // shared across all buildings, the coarse AABB pre-filter drops almost every
            // object on the first line, and the clone scene is only SetActive-toggled when
            // there really is a borderline candidate.
            int extraCount = 0;
            Transform masterT = instance.MasterInterior.transform;

            Bounds filter;
            bool hasFilter = TryGetWorldFilterBounds(instance, out filter);
            List<Il2Cpp.GearItem> pending = null;

            var allSceneGear = SceneScan.GearAll();
            foreach (var gear in allSceneGear)
            {
                if (gear == null || gear.gameObject == null) continue;

                Vector3 pos = gear.transform.position;
                StrayVerdict verdict = ClassifyStray(instance, hasFilter, filter, pos);
                if (verdict == StrayVerdict.Outside) continue;

                if (gear.transform.IsChildOf(masterT)) continue; // already saved above
                if (IsPlayerOrInventory(gear.transform)) continue; // in the player's hands

                // Only items truly standing in the world (see ShouldPersistGear).
                // A disabled item was picked up, is in a container, or was eliminated.
                //
                // NOTE: only items inside THIS building are counted - otherwise the disabled
                // items of the other 14 clones in the scene were counted too and the log
                // showed a meaningless figure like ~900 for every building.
                if (IsGearSaveFilterEnabled && !gear.gameObject.activeInHierarchy)
                {
                    if (verdict == StrayVerdict.Inside) skippedCount++;
                    continue;
                }

                if (verdict == StrayVerdict.NeedsRaycast)
                {
                    if (pending == null) pending = new List<Il2Cpp.GearItem>();
                    pending.Add(gear);
                    continue;
                }

                if (AppendGearEntry(gear, interiorT, entries, savedPositions)) extraCount++;
            }

            if (pending != null && pending.Count > 0)
            {
                // The ray test requires the clone scene to be OPEN.
                bool wasGearActive = instance.MasterInterior.activeSelf;
                if (!wasGearActive) instance.MasterInterior.SetActive(true);

                foreach (var gear in pending)
                {
                    if (gear == null || gear.gameObject == null) continue;
                    if (!instance.IsPositionInsideRaycastOnly(gear.transform.position, SeamlessInteriorInstance.ITEM_RAY_ORIGIN_LIFT)) continue;

                    if (AppendGearEntry(gear, interiorT, entries, savedPositions)) extraCount++;
                }

                if (!wasGearActive) instance.MasterInterior.SetActive(false);
            }

            string json = "[\n" + string.Join(",\n", entries) + "\n]";
            JsonWriteCache.Write(path, json);

            // Saved = items standing in the world. Skipped = container contents, items the
            // player took and eliminated loot candidates (see ShouldPersistGear).
            if (s_DebugBounds)
            {
                MelonLogger.Msg($"[GEAR-TEST] {instance.Config.InteriorSceneBaseName}: {entries.Count} esya kaydedildi " +
                                $"({extraCount} extra), {skippedCount} atlandi (konteyner/alinmis/elenmis): {path}");

                foreach (var gear in allGear)
                {
                    if (gear == null || gear.gameObject == null) continue;

                    if (ShouldPersistGear(gear, interiorT))
                    {
                        MelonLogger.Msg($"[GEAR-TEST]   -> {gear.gameObject.name} pos={gear.transform.position}");
                    }
                    else
                    {
                        string parentName = gear.transform.parent != null ? gear.transform.parent.name : "ROOT";
                        bool inContainer = IsInsideContainer(gear.transform, interiorT);
                        MelonLogger.Msg($"[GEAR-SKIP]   XX {gear.gameObject.name} parent={parentName} konteynerde={inContainer} acik={gear.gameObject.activeSelf}");
                    }
                }
            }
        }

        private static bool AppendGearEntry(Il2Cpp.GearItem gear, Transform interiorT,
                                            List<string> entries, HashSet<string> savedPositions)
        {
            string posKey = BuildGearPosKey(gear, interiorT);
            if (!savedPositions.Add(posKey)) return false; // de-duplication

            entries.Add(BuildGearEntryJson(gear, interiorT));

            if (s_DebugBounds)
                MelonLogger.Msg($"[GEAR-SAVE] EXTRA obje: {gear.gameObject.name} parent={gear.transform.parent?.name ?? "ROOT"} posKey={posKey}");

            return true;
        }

        // ─── Restoring saved gear (for items lost across a game restart) ───
        private class GearSaveEntry
        {
            public string name;
            public string guid;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool active = true;
            // The output of the game's own GearItem.SerializeToString(). Older save files
            // do not have this field; it then stays null and only the transform is
            // restored (the old behaviour).
            public string serialized;
        }

        // Marks the start of a record in the gear save file.
        // Since the quotes inside the escaped "s" data appear as \", this sequence can
        // NEVER occur inside a data body - it can safely be used to find record boundaries.
        private const string GEAR_ENTRY_MARKER = "{\"name\":\"";

        private static List<GearSaveEntry> ParseGearFile(string json)
        {
            var result = new List<GearSaveEntry>();
            int idx = 0;

            while (idx < json.Length)
            {
                int start = json.IndexOf(GEAR_ENTRY_MARKER, idx);
                if (start < 0) break;

                int nextStart = json.IndexOf(GEAR_ENTRY_MARKER, start + 1);
                int sIdx = json.IndexOf(",\"s\":\"", start);

                // If sIdx lies inside the NEXT record, this record has no "s" field.
                bool hasSerialized = sIdx >= 0 && (nextStart < 0 || sIdx < nextStart);

                string head;
                string serialized = null;

                if (hasSerialized)
                {
                    int sStart = sIdx + 6; // length of ,"s":"
                    int sEnd = FindClosingQuote(json, sStart);
                    if (sEnd < 0) break;

                    head = json.Substring(start + 1, sIdx - start - 1);
                    serialized = UnescapeJson(json.Substring(sStart, sEnd - sStart));
                    idx = sEnd + 1;
                }
                else
                {
                    int end = json.IndexOf('}', start);
                    if (end < 0) break;

                    head = json.Substring(start + 1, end - start - 1);
                    idx = end + 1;
                }

                var entry = ParseGearEntry(head);
                if (entry != null && !string.IsNullOrEmpty(entry.name))
                {
                    entry.serialized = serialized;
                    result.Add(entry);
                }
            }

            return result;
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
                // Without an "a" field (the old format) treat the item as active.
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

            var savedEntries = ParseGearFile(File.ReadAllText(path));

            if (savedEntries.Count == 0)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName} kayitli gear yok.");
                return;
            }

            Transform interiorT = instance.MasterInterior.transform;

            // BACKWARDS COMPATIBILITY / CLEANUP: old save files also hold container
            // contents, items the player took and eliminated loot candidates, as "disabled"
            // (a:false). Those were respawned into the scene on every load and grew the file
            // forever. Disabled records are now skipped - container contents come back
            // through RestoreContainerData anyway.
            int ghostCount = IsGearSaveFilterEnabled
                ? savedEntries.RemoveAll(e => e != null && !e.active)
                : 0;
            if (ghostCount > 0)
                MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName}: {ghostCount} kapali (hayalet) kayit atlandi — konteyner icerigi/alinmis esya/elenmis loot.");

            // NOTE: the cleanup below MUST run even when savedEntries ends up empty -
            // otherwise the raw loot from the scene template stays in place.

            // Delete every existing GearItem in the clone scene before restoring
            // (de-duplication). DestroyImmediate is used because Destroy is deferred and
            // would clash with the gear spawned in the same frame.
            //
            // CONTAINER CONTENTS ARE LEFT ALONE: RestoreContainerData owns them
            // (Container.Deserialize overwrites the existing contents). Deleting them here
            // would lose the contents entirely whenever a container does not match the save.
            var existingGear = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true);
            int deletedCount = 0;
            foreach (var gear in existingGear)
            {
                if (gear == null || gear.gameObject == null) continue;
                if (IsGearSaveFilterEnabled && IsInsideContainer(gear.transform, interiorT)) continue;

                UnityEngine.Object.DestroyImmediate(gear.gameObject);
                deletedCount++;
            }

            // Also delete gear that is inside the bounds but not a child (dropped items,
            // items placed on a stove).
            //
            // PRE-FILTER: no expensive test (parent-chain walk, raycast) runs for items
            // outside the coarse world AABB.
            Bounds delFilter;
            bool hasDelFilter = TryGetWorldFilterBounds(instance, out delFilter);

            var allSceneGear = SceneScan.GearAll();
            foreach (var gear in allSceneGear)
            {
                if (gear == null || gear.gameObject == null) continue;
                if (hasDelFilter && !delFilter.Contains(gear.transform.position)) continue;
                if (IsPlayerOrInventory(gear.transform)) continue;
                // Container contents have to be protected here too: this loop works by
                // volume and would otherwise catch the container items skipped above.
                if (IsGearSaveFilterEnabled && IsInsideContainer(gear.transform, interiorT)) continue;
                if (!IsPositionInsideFull(instance, gear.transform.position)) continue;

                UnityEngine.Object.DestroyImmediate(gear.gameObject);
                deletedCount++;
            }

            // DestroyImmediate changed the hierarchy INSTANTLY: the shared scan cache now
            // holds dead references and has to be refreshed.
            SceneScan.InvalidateVolatile();

            if (s_DebugBounds)
                MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName}: {deletedCount} mevcut gear silindi (dupelama onleme).");

            int restoredCount = 0;
            int failedCount = 0;
            int statefulCount = 0;

            foreach (var entry in savedEntries)
            {

                // Strip "(Clone)" and the trailing numbers from the name to get the
                // Addressables key.
                string cleanName = entry.name.Replace("(Clone)", "").Trim();
                int parenIdx = cleanName.LastIndexOf(" (");
                if (parenIdx > 0 && cleanName.EndsWith(")"))
                    cleanName = cleanName.Substring(0, parenIdx).Trim();

                GameObject spawned = null;

                // 1) PREFERRED: the game's own "spawn + deserialize" path.
                //    It also restores the item's internal state (condition, decay,
                //    liquid/food state and the CookingPotItem data). A plain Instantiate
                //    resets the item to its prefab defaults and, for instance, the water
                //    boiling in the pot on the stove disappears.
                if (!string.IsNullOrEmpty(entry.serialized))
                {
                    try
                    {
                        var gi = Il2Cpp.GearItem.InstantiateAndDeserializeGearItem(
                            cleanName,
                            entry.serialized,
                            instance.MasterInterior.transform,
                            false,   // applyPositioningFix: the position is set by us below
                            false);  // instantiatedInContainer: not inside a container

                        if (gi != null) spawned = gi.gameObject;
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"[GEAR-RESTORE] Durumlu spawn basarisiz ({cleanName}): {ex.Message} — duz spawn'a dusuluyor.");
                        spawned = null;
                    }

                    if (spawned != null) statefulCount++;
                }

                // 2) FALLBACK: the old path - prefab + transform only.
                //    Used when the record has no "s" field (an old file) or the
                //    deserializing spawn failed.
                if (spawned == null)
                {
                    GameObject prefab = null;
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

                    spawned = UnityEngine.Object.Instantiate(prefab);
                }

                // The stored position is clone-local, so convert it to world space.
                Vector3 worldPos = interiorT.TransformPoint(entry.position);
                Quaternion worldRot = interiorT.rotation * entry.rotation;

                if (spawned.transform.parent != instance.MasterInterior.transform)
                    spawned.transform.SetParent(instance.MasterInterior.transform, true);

                spawned.transform.position = worldPos;
                spawned.transform.rotation = worldRot;
                spawned.transform.localScale = entry.scale;
                spawned.SetActive(entry.active);

                // The GUID has to be restored too, otherwise the item is seen as a new
                // object on the next save.
                if (!string.IsNullOrEmpty(entry.guid))
                {
                    var guidComp = spawned.GetComponent<Il2Cpp.ObjectGuid>();
                    if (guidComp == null) guidComp = spawned.AddComponent<Il2Cpp.ObjectGuid>();
                    guidComp.m_Guid = entry.guid;
                }

                restoredCount++;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-RESTORE] SPAWNED: {entry.name} worldPos={worldPos} durumlu={(!string.IsNullOrEmpty(entry.serialized))}");
            }

            MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName}: {restoredCount} gear geri yuklendi ({statefulCount} durum verisiyle), {failedCount} prefab bulunamadi (toplam kayit: {savedEntries.Count})");
        }

        // ─── Saving/loading the container data of a clone scene ───
        private static string GetContainerSavePath(SeamlessInteriorInstance instance)
        {
            return GetInstanceSavePath(instance, "_containers.json");
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

            // There can be several containers with the same name, so each gets an index.
            var nameCounter = new Dictionary<string, int>();

            foreach (var c in containers)
            {
                if (c == null) continue;

                string containerName = c.gameObject.name;

                // How many containers with this name have we seen?
                if (!nameCounter.ContainsKey(containerName)) nameCounter[containerName] = 0;
                int nameIndex = nameCounter[containerName]++;

                // Unique key: name + index. The enumeration order is stable, so the same
                // key is produced on load.
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

                // JSON-safe: escape the quotes and newlines inside the serialized data.
                string escapedData = serialized.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                entries.Add($"{{\"key\":\"{matchKey}\",\"data\":\"{escapedData}\"}}");

                if (s_DebugBounds)
                    MelonLogger.Msg($"[CONTAINER-SAVE] Kaydedildi: {matchKey} dataLen={serialized.Length}");
            }

            string json = "[\n" + string.Join(",\n", entries) + "\n]";
            JsonWriteCache.Write(path, json);

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

            // Build the key -> serialized data map.
            var keyToData = new Dictionary<string, string>();
            int idx = 0;
            while (idx < json.Length)
            {
                int keyIdx = json.IndexOf("\"key\":\"", idx);
                if (keyIdx < 0) break;
                int keyStart = keyIdx + 7; // length of "key":"
                int keyEnd = json.IndexOf("\"", keyStart);
                if (keyEnd < 0) break;
                string key = json.Substring(keyStart, keyEnd - keyStart);

                int dataKeyIdx = json.IndexOf("\"data\":\"", keyEnd);
                if (dataKeyIdx < 0) break;
                int dataStart = dataKeyIdx + 8; // length of "data":"

                // Find the closing quote, skipping escaped quotes (an odd number of
                // preceding backslashes means the quote is escaped).
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

            // Push the data back into the clone's containers, matched by name + index.
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

        // Helper: decides whether a non-child item (dropped on the floor, placed on a table)
        // is inside the clone scene.
        //
        // FIX: only a raycast used to be used, with its origin lifted 2.5m. For items on a
        // table or shelf that origin ends up above the ceiling, the ceiling ray is missed
        // and the item looked like it was "outside" - which is why those items were not
        // hidden when the player went out and appeared to float in mid-air.
        //
        // The purely geometric volume test now comes FIRST (it needs no raycast and no
        // collider and is unaffected by height), with the raycast as a backup.
        public static bool IsPositionInsideFull(SeamlessInteriorInstance instance, Vector3 pos)
        {
            if (instance == null || instance.MasterInterior == null) return false;

            // 1) Geometric volume test - the volume is shrunk slightly so outdoor items in
            //    a doorway or against a wall are not swept in.
            if (instance.IsPositionInVolume(pos, -0.7f)) return true;

            // 2) Raycast fallback (the volume test cannot run without an InteriorTrigger).
            if (instance.MasterInterior.activeSelf)
                return instance.IsPositionInsideRaycastOnly(pos, SeamlessInteriorInstance.ITEM_RAY_ORIGIN_LIFT);

            // With MasterInterior inactive the raycast cannot work, so it is activated
            // temporarily and switched off again.
            instance.MasterInterior.SetActive(true);
            bool result = instance.IsPositionInsideRaycastOnly(pos, SeamlessInteriorInstance.ITEM_RAY_ORIGIN_LIFT);
            instance.MasterInterior.SetActive(false);
            return result;
        }
    }
}
