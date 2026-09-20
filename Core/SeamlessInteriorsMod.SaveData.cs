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
            if (!CanPersistContent(instance)) return;

            string path = GetPlaceableSavePath(instance);
            if (path == null) return;

            Transform interiorT = instance.MasterInterior.transform;

            var placeablesInInterior = InteriorScan.Placeables(instance.MasterInterior);
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

            // Furniture the player put down themselves needs more than a transform - it
            // has to be re-created from scratch on the next load. See SaveSpawnedPlaceables.
            SaveSpawnedPlaceables(instance);

            if (s_DebugBounds)
                MelonLogger.Msg($"[PLACEABLE-SAVE] {instance.Config.InteriorSceneBaseName}: {entries.Count} Placeable kaydedildi ({movedCount} tasinmis): {path}");

            // Reported at normal level, because this is the number that turns "my things
            // by the door disappeared" into a fact. Anything counted here was standing
            // OUTSIDE the clone and has just been written into the building's save file -
            // if that is wrong, the object is about to be pulled indoors and lost to the
            // outside world.
            if (movedCount > 0)
                MelonLogger.Msg($"[PLACEABLE-SAVE] {instance.Config.ResolvedInstanceId}: klon disindan {movedCount} obje sahiplenildi.");
        }

        // ─────────────────────────────────────────────────────────────────
        // SPAWNED PLACEABLES (player-placed furniture and decorations)
        //
        // _placeables.json can only REPOSITION objects: it matches by guid against what
        // the clone already contains and switches off anything it does not recognise.
        // That is right for the scene template's own furniture, but a chair the player
        // crafted and put down does not exist in the template at all - once the clone is
        // rebuilt there is nothing for the position record to attach to.
        //
        // So spawned placeables get their own file, holding the game's full
        // PlaceableSaveData rather than just a transform, and hydration re-creates them
        // through the game's own Placeable.FindOrCreateAndDeserialize.
        //
        // Positions are stored in clone-local space, like everything else the mod
        // writes, so they survive the clone being rebuilt somewhere else.
        //
        // The file is rewritten from the clone on every save, so an item the player
        // picks up again simply stops being in it.
        // ─────────────────────────────────────────────────────────────────
        private static string GetSpawnedPlaceableSavePath(SeamlessInteriorInstance instance)
        {
            return GetInstanceSavePath(instance, "_spawn_placeables.json");
        }

        // Cached: reading an IL2CPP static string property allocates a fresh managed
        // string every time, and this is asked once per placeable on every save.
        private static string s_SpawnedNameSuffix;

        private static string SpawnedNameSuffix()
        {
            if (s_SpawnedNameSuffix != null) return s_SpawnedNameSuffix;

            string suffix = null;
            try { suffix = Il2CppTLD.Placement.Placeable.SPAWNED_NAME_SUFFIX; }
            catch { }

            s_SpawnedNameSuffix = string.IsNullOrEmpty(suffix) ? " (PLACED)" : suffix;
            return s_SpawnedNameSuffix;
        }

        // Is this a placeable the player put down, rather than one the scene shipped with?
        //
        // The guid set is the reliable answer - the mod recorded it when it created the
        // object. The name suffix is only the fallback, for objects placed during normal
        // play that the mod has never had to re-create itself.
        public static bool IsSpawnedPlaceable(SeamlessInteriorInstance instance, Il2CppTLD.Placement.Placeable p)
        {
            if (p == null || p.gameObject == null) return false;

            if (instance != null && !string.IsNullOrEmpty(p.m_Guid)
                && instance.SpawnedPlaceableGuids.Contains(p.m_Guid))
                return true;

            return p.gameObject.name.IndexOf(SpawnedNameSuffix(), System.StringComparison.Ordinal) >= 0;
        }

        private static void SaveSpawnedPlaceables(SeamlessInteriorInstance instance)
        {
            string path = GetSpawnedPlaceableSavePath(instance);
            if (path == null) return;

            Transform interiorT = instance.MasterInterior.transform;
            var entries = new List<string>();

            // ONE RECORD PER OBJECT, KEYED BY WHAT IT IS AND WHERE IT STANDS.
            //
            // The guid alone is not enough: a copy created from an old record can come
            // back carrying a fresh guid, and then the same crate is written twice, three
            // times, twenty times - the file doubles on every load until the building is
            // full of stacked duplicates. Name plus position is what a duplicate actually
            // shares, so that is what is compared.
            var seen = new HashSet<string>();

            foreach (var p in InteriorScan.Placeables(instance.MasterInterior))
            {
                if (p == null || p.gameObject == null) continue;
                if (string.IsNullOrEmpty(p.m_Guid)) continue;
                if (!IsSpawnedPlaceable(instance, p)) continue;
                if (IsPlayerOrInventory(p.transform)) continue;

                Vector3 lp = interiorT.InverseTransformPoint(p.transform.position);
                string identity = $"{CleanGearName(p.gameObject.name)}_{lp.x:F2}_{lp.y:F2}_{lp.z:F2}";
                if (!seen.Add(identity)) continue;

                try
                {
                    var data = p.Serialize();
                    if (data == null) continue;

                    // World -> clone-local, so the record still means the same spot after
                    // the clone is rebuilt and re-aligned.
                    data.m_Position = interiorT.InverseTransformPoint(data.m_Position);
                    data.m_Rotation = Quaternion.Inverse(interiorT.rotation) * data.m_Rotation;

                    string serialized = Il2Cpp.Utils.SerializeObject(data);
                    if (string.IsNullOrEmpty(serialized)) continue;

                    entries.Add($"{{\"g\":\"{EscapeJson(p.m_Guid)}\",\"d\":\"{EscapeJson(serialized)}\"}}");
                }
                catch (System.Exception ex)
                {
                    if (s_DebugBounds)
                        MelonLogger.Warning($"[SPAWN-PLACEABLE-SAVE] '{p.gameObject.name}' kaydedilemedi: {ex.Message}");
                }
            }

            JsonWriteCache.Write(path, "[\n" + string.Join(",\n", entries) + "\n]");

            if (s_DebugBounds)
                MelonLogger.Msg($"[SPAWN-PLACEABLE-SAVE] {instance.Config.ResolvedInstanceId}: {entries.Count} yerlestirilmis obje kaydedildi.");
        }

        // Re-creates the player-placed furniture. Idempotent: FindOrCreateAndDeserialize
        // matches on the guid, so an object the game already respawned is repositioned
        // rather than duplicated.
        public static int RestoreSpawnedPlaceables(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return 0;

            string path = GetSpawnedPlaceableSavePath(instance);
            if (path == null || !File.Exists(path)) return 0;

            string json;
            try { json = File.ReadAllText(path); }
            catch { return 0; }

            var elements = SplitJsonArray(json);
            if (elements.Count == 0) return 0;

            Transform interiorT = instance.MasterInterior.transform;
            int applied = 0;
            int carried = 0;
            var failedNames = new List<string>();

            // A file written before the de-duplication above can still hold the same
            // object many times over. Applying every copy would re-create the pile, so
            // duplicates are dropped on the way in as well.
            var seenGuids = new HashSet<string>();

            foreach (var element in elements)
            {
                string guid = ExtractString(element, "\"g\":\"", "\"");
                if (string.IsNullOrEmpty(guid)) continue;
                if (!seenGuids.Add(guid)) continue;

                int dIdx = element.IndexOf("\"d\":\"", System.StringComparison.Ordinal);
                if (dIdx < 0) continue;
                int dStart = dIdx + 5;
                int dEnd = FindClosingQuote(element, dStart);
                if (dEnd < 0) continue;

                string serialized = UnescapeJson(element.Substring(dStart, dEnd - dStart));
                if (string.IsNullOrEmpty(serialized)) continue;

                // Recorded whatever the outcome: this guid names one of the player's own
                // objects, and everything downstream has to treat it as such even if this
                // particular attempt to re-create it failed.
                instance.SpawnedPlaceableGuids.Add(guid);

                // IT IS IN THE BACKPACK NOW.
                //
                // This file was written at the last save, so it still describes the
                // decoration standing where the player had put it. They have since picked
                // it up, and handing that record to FindOrCreateAndDeserialize builds a
                // real, switched-on object for a guid the game knows is on the player -
                // which the placement registry then parks on the player, visible, moving
                // with them, until the next save rewrites this file.
                // (see SeamlessInteriorsMod.CarriedDecorations.cs)
                //
                // Nothing to restore and nothing to repair: the object belongs to the
                // backpack until the player puts it down again.
                if (IsCarriedDecoration(guid))
                {
                    carried++;
                    continue;
                }

                // Newtonsoft omits a false bool, so "no m_ActiveSelf field" means the
                // player had switched this decoration off and it must stay off.
                if (serialized.IndexOf("\"m_ActiveSelf\":true", System.StringComparison.Ordinal) >= 0)
                    instance.SpawnedShouldBeActive.Add(guid);

                if (ApplySpawnedPlaceable(interiorT, guid, serialized)) applied++;
                else failedNames.Add(ExtractSpawnedPlaceableName(serialized) ?? guid);
            }

            if (applied > 0) SceneScan.InvalidateVolatile();

            // Logged unconditionally: this is the player's own furniture and decorations,
            // and a silent shortfall here is indistinguishable from "my things vanished".
            // The carried count is part of that: those objects are not missing, they are
            // in the backpack, and saying so is what tells the two apart.
            MelonLogger.Msg($"[SPAWN-PLACEABLE-LOAD] {instance.Config.ResolvedInstanceId}: " +
                            $"{applied}/{elements.Count} yerlestirilmis obje geri yuklendi" +
                            (carried > 0 ? $" ({carried} tanesi oyuncunun cantasinda, atlandi)." : "."));

            if (failedNames.Count > 0)
            {
                int shown = 0;
                foreach (var n in failedNames)
                {
                    if (shown++ >= 10) break;
                    MelonLogger.Warning($"[SPAWN-PLACEABLE-LOAD] OLUSTURULAMADI: '{n}'");
                }
                MelonLogger.Warning($"[SPAWN-PLACEABLE-LOAD] {instance.Config.ResolvedInstanceId}: toplam {failedNames.Count} obje geri getirilemedi.");
            }

            return applied;
        }

        // ─────────────────────────────────────────────────────────────────
        // REPAIRING THE PLAYER'S OWN OBJECTS
        //
        // The clone's contents are the mod's business, but the game's scene restore also
        // has opinions about anything the placement system has ever seen, and it acts on
        // them AFTER the mod has finished rebuilding the interior. The result was a
        // building that came out perfect and then lost 174 of the player's decorations
        // to the reload that followed.
        //
        // ApplySpawnedPlaceable now unregisters these objects so the game stops tracking
        // them, but that only helps from the next save onwards - a save already carrying
        // those records will still act on them. So the state is also checked back:
        // anything the mod's own file says should be standing, that is found switched
        // off, is switched back on.
        //
        // Only objects the file marks active are touched, so a decoration the player
        // deliberately switched off stays off.
        // ─────────────────────────────────────────────────────────────────
        // EVERY renderer off, not just one of them.
        //
        // "Any renderer disabled" looks like the same question but is not: an object with
        // LOD levels always has all but one of them switched off, which is how LOD works.
        // Treating that as damage made the repair pass claim to fix several hundred
        // perfectly healthy objects on every tick - and force every LOD level on at once.
        private static bool IsFullyHidden(GameObject go)
        {
            bool any = false;
            foreach (var r in InteriorScan.Renderers(go))
            {
                if (r == null) continue;
                any = true;
                if (r.enabled) return false;
            }
            return any;
        }

        public static int RepairSpawnedPlaceables(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return 0;
            if (instance.SpawnedShouldBeActive.Count == 0) return 0;

            int repaired = 0;
            bool interiorOpen = instance.MasterInterior.activeSelf;

            foreach (var p in InteriorScan.Placeables(instance.MasterInterior))
            {
                if (p == null || p.gameObject == null) continue;
                if (string.IsNullOrEmpty(p.m_Guid)) continue;
                if (!instance.SpawnedShouldBeActive.Contains(p.m_Guid)) continue;
                if (PlayerRefs.IsPlayerRoot(p.transform.root)) continue;

                // Picked up since the file was written. "Should be active" describes where
                // it stood in the building, not what to do with something in the backpack -
                // switching it on here is how a carried decoration ends up standing in the
                // world. (see SeamlessInteriorsMod.CarriedDecorations.cs)
                if (IsCarriedDecoration(p)) continue;

                // SWITCHED ON IS NOT THE SAME AS VISIBLE.
                //
                // Before the object was adopted into the clone it looked like outdoor
                // scenery poking into the building, so the hide pass turned its renderers
                // AND its colliders off as well as deactivating it. Turning the object
                // back on without those leaves it solid and interactive but completely
                // invisible - which is exactly how this looked from inside the game.
                bool wasOff = !p.gameObject.activeSelf;
                bool dark = interiorOpen && IsFullyHidden(p.gameObject);

                if (!wasOff && !dark) continue;

                if (wasOff) p.gameObject.SetActive(true);
                if (interiorOpen) SetObjectVisualState(p.gameObject, true);

                // Whoever switched it off is tracking it; stop them doing it again.
                p.m_Invalidated = true;
                try { Il2CppTLD.Placement.PlaceableManager.Remove(p); } catch { }

                repaired++;
            }

            if (repaired > 0)
                MelonLogger.Msg($"[SPAWN-PLACEABLE-REPAIR] {instance.Config.ResolvedInstanceId}: " +
                                $"{repaired} yerlestirilmis obje disaridan kapatilmis/gizlenmisti, geri acildi.");

            return repaired;
        }

        // Pulls the object's name out of a stored PlaceableSaveData, for log messages.
        private static string ExtractSpawnedPlaceableName(string serialized)
        {
            return ExtractEscapedField(serialized, "m_Name");
        }

        // True only while the mod is inside Placeable.FindOrCreateAndDeserialize for one
        // of the clone's own objects. See PreventPlaceableAutoRegisterPatch.
        public static bool IsCreatingClonePlaceable { get; private set; }

        // Shared by the normal restore and the pre-mod import: takes a PlaceableSaveData
        // whose position is in CLONE-LOCAL space, moves it into the clone's world space
        // and hands it to the game.
        private static bool ApplySpawnedPlaceable(Transform interiorT, string guid, string serializedLocal)
        {
            try
            {
                var data = Il2Cpp.Utils.DeserializeObject<Il2CppTLD.Placement.PlaceableSaveData>(serializedLocal);
                if (data == null) return false;

                data.m_Position = interiorT.TransformPoint(data.m_Position);
                data.m_Rotation = interiorT.rotation * data.m_Rotation;

                Il2CppTLD.Placement.Placeable p;
                IsCreatingClonePlaceable = true;
                try { p = Il2CppTLD.Placement.Placeable.FindOrCreateAndDeserialize(guid, data); }
                finally { IsCreatingClonePlaceable = false; }

                if (p == null || p.gameObject == null) return false;

                // Was this object out in the world a moment ago? Then the mod has just
                // taken something that belonged to the region, and that is worth saying
                // out loud - it is exactly how the player's furniture goes missing from
                // outside the building.
                if (!p.transform.IsChildOf(interiorT) && !IsUnderPlacementRoot(p.transform))
                {
                    MelonLogger.Msg($"[SPAWN-PLACEABLE] '{p.gameObject.name}' klon disindan alindi " +
                                    $"(parent='{(p.transform.parent != null ? p.transform.parent.name : "ROOT")}').");
                }

                // NARROW ON PURPOSE: only refuse an object the player is actually holding.
                //
                // The general IsPlayerOrInventory test also treats anything sitting in
                // DontDestroyOnLoad as the player's - and a placeable the game has just
                // created starts life exactly there, under the placement category root.
                // Using that test here refused to adopt the very objects this function
                // had just restored.
                if (PlayerRefs.IsPlayerRoot(p.transform.root)) return false;

                // Under the clone, so it hides and shows with the building.
                if (!p.transform.IsChildOf(interiorT))
                    p.transform.SetParent(interiorT, true);

                // TAKE IT BACK OFF THE GAME'S BOOKS.
                //
                // FindOrCreateAndDeserialize builds the object outside the clone, so by
                // the time PlaceableManager.Add runs the mod's own guard cannot tell it
                // belongs to a clone - and the game adopts it. From then on the object is
                // in the REGION's placement list, and the game's scene restore applies
                // that list AFTER the mod has finished rebuilding the interior, switching
                // objects off according to records the mod knows nothing about.
                //
                // Measured: a building came out correct (0 of the player's objects off),
                // then a save and reload left 174 of them switched off.
                //
                // The clone's contents belong to the mod's own save files, so the object
                // is unregistered and invalidated - the same treatment the clone's
                // template placeables get in InvalidateInteriorPlaceables.
                p.m_Invalidated = true;
                try { Il2CppTLD.Placement.PlaceableManager.Remove(p); } catch { }

                return true;
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Warning($"[SPAWN-PLACEABLE] '{guid}' uygulanamadi: {ex.Message}");
                return false;
            }
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
        // (static: also called from the hydration coroutine, which is not tied to the
        //  MelonMod instance.)
        private static void RestorePlaceablePositions(SeamlessInteriorInstance instance)
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
            var placeables = InteriorScan.Placeables(instance.MasterInterior);
            int restoredCount = 0;
            int changedCount = 0;
            int protectedSpawned = 0;
            int carriedHidden = 0;
            var restoredGuids = new HashSet<string>();

            foreach (var p in placeables)
            {
                if (p == null || string.IsNullOrEmpty(p.m_Guid)) continue;

                if (!guidToPosRot.ContainsKey(p.m_Guid))
                {
                    // A SPAWNED OBJECT IS NEVER SWITCHED OFF HERE.
                    //
                    // The rule below is about the scene TEMPLATE's own furniture: the
                    // template always brings it back, so a piece missing from the record
                    // means the player took it and the copy has to go.
                    //
                    // Objects the player PUT here are the opposite case. The template
                    // knows nothing about them; they exist only because
                    // RestoreSpawnedPlaceables just created them from
                    // _spawn_placeables.json, which runs immediately before this. If its
                    // guid has not made it into _placeables.json - the two files are
                    // written by different passes and a freshly created object can carry
                    // a guid the other pass never saw - this would switch off the thing
                    // that was just correctly restored.
                    //
                    // That is what made flags, signs and looted containers disappear on
                    // the first reload while crafted furniture survived.
                    if (IsSpawnedPlaceable(instance, p))
                    {
                        protectedSpawned++;
                        continue;
                    }

                    // CRITICAL: an object missing from the save file was taken into the
                    // player's inventory through the safehouse feature, or destroyed. The
                    // Addressables template respawns it, so leaving it in the scene would
                    // both DUPLICATE the inventory copy and create double records / errors
                    // while saving. Switch it off.
                    p.gameObject.SetActive(false);
                    continue;
                }

                // IN THE BACKPACK SINCE THE FILE WAS WRITTEN.
                //
                // The record says where this decoration stood, because that is where it
                // stood at the last save - the player has picked it up since. Putting it
                // back would stand a second copy of something they are carrying in the
                // building, and the placement registry, which knows the guid is on the
                // player, then takes that copy and parks it on them: visible, at their
                // feet, moving with them until the next save.
                //
                // Same answer as the "missing from the file" branch above, for the same
                // reason: the building must not hold what the backpack holds.
                // (see SeamlessInteriorsMod.CarriedDecorations.cs)
                if (IsCarriedDecoration(p))
                {
                    p.gameObject.SetActive(false);
                    carriedHidden++;

                    // DEALT WITH - and the "moved objects" pass below must be told so.
                    //
                    // That pass looks scene-wide for guids this loop did not restore, and
                    // a guid left off this list would send it back to this very object,
                    // which is a child of the clone - the one kind of placeable its
                    // IsPlayerOrInventory test asks the placement registry about directly.
                    // The registry has been told to forget the clone's objects, and asking
                    // it about one anyway is what took the process down.
                    restoredGuids.Add(p.m_Guid);
                    continue;
                }

                var entry = guidToPosRot[p.m_Guid];

                Vector3 targetWorldPos = interiorT.TransformPoint(entry.position);
                Quaternion targetWorldRot = interiorT.rotation * entry.rotation;

                Vector3 oldWorldPos = p.transform.position;
                float dist = Vector3.Distance(oldWorldPos, targetWorldPos);

                p.transform.position = targetWorldPos;
                p.transform.rotation = targetWorldRot;
                // Zero means "no scale recorded" - never shrink an object to nothing.
                if (entry.scale.sqrMagnitude > 1e-8f) p.transform.localScale = entry.scale;
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
                    // Zero means "no scale recorded" - never shrink an object to nothing.
                    if (entry.scale.sqrMagnitude > 1e-8f) p.transform.localScale = entry.scale;
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

            if (s_DebugBounds || protectedSpawned > 0 || carriedHidden > 0)
                MelonLogger.Msg($"[PLACEABLE-LOAD] {instance.Config.InteriorSceneBaseName}: {restoredCount}/{guidToPosRot.Count} Placeable geri yuklendi " +
                                $"({changedCount} degismis, {protectedSpawned} yerlestirilmis obje kapatilmaktan korundu, " +
                                $"{carriedHidden} obje oyuncunun cantasinda oldugu icin gizlendi).");
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
                    RequestFireRestore();
                }
                // LoadSceneDataAdditive was removed.
                // Gear/Container/Placeable data is now managed entirely through JSON.
                // LoadSceneDataAdditive spawned extra gear from the game's own save data and
                // duplicated it against the JSON restore.
                // Fire data is handled separately by FireManagerStealerPatch.
            }
        }

        // ─── FIRES IN CLONE SCENES, ACROSS A SAVE/LOAD ───
        //
        // Only ONE restore may ever be in flight. Run() calls RequestFireRestore once per
        // building, and the stolen blob is a single shared payload that the restore
        // CONSUMES - so a second coroutine would find nothing and a first one running too
        // early would eat it before the rest of the region exists.
        private static bool s_FireRestorePending = false;

        // Cap on the wait below. A little longer than TryBatchUpdateEnvironment's own 30
        // second cap, so on a stalled load the fires are still restored into whatever did
        // get built instead of never being restored at all.
        private const float FIRE_RESTORE_MAX_WAIT = 35f;

        public static void RequestFireRestore()
        {
            if (s_FireRestorePending) return;

            s_FireRestorePending = true;
            MelonCoroutines.Start(DelayedFireRestore());
        }

        // Re-applies the fire data we stole, looping over every active clone.
        public static IEnumerator DelayedFireRestore()
        {
            // ─── WAIT FOR EVERY BUILDING, NOT FOR THREE FRAMES ───
            //
            // THE BUG THIS FIXES: this used to wait three frames and then consume the blob.
            // Run() is a per-building coroutine and cloning a region takes the better part
            // of ten seconds, so three frames after the FIRST building's RestoreSceneSaveData
            // only that one building existed. Its fires were registered and restored; the
            // blob was then cleared, and every building cloned after it - all fourteen of
            // them - got no fire data at all. Their stoves came back from the scene template
            // cold.
            //
            // In play that read as "I lit the stove in the camp office, saved and loaded
            // inside another building, and the camp office fire was out". It was never about
            // WHICH building the player saved in: the building that happened to be cloned
            // first kept its fire (the one the player saved inside is cloned first, which is
            // why the fire in front of them always looked fine) and every other one lost it.
            //
            // Waiting costs nothing the player can see: the screen is held black until every
            // instance is ready anyway - that is exactly what TryBatchUpdateEnvironment
            // releases - so this finishes inside the same load.
            float waited = 0f;
            while (waited < FIRE_RESTORE_MAX_WAIT)
            {
                if (AreAllInstancesReady() && !IsAnyCloningActive()) break;

                yield return new WaitForSeconds(0.25f);
                waited += 0.25f;
            }

            // A couple of frames on top, so the last building's fire objects have finished
            // waking up.
            yield return null;
            yield return null;

            if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData))
            {
                string blob = FireManagerStealerPatch.s_StolenFireData;
                int registered = RegisterCloneFiresWithManager();

                yield return null;
                yield return null;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[FIRE-RESTORE] {waited:F2} sn beklendi, {registered} klon ates objesi " +
                                    $"FireManager'a kaydedildi, ates datasi uygulaniyor.");

                // Diagnostics (verbose only, see SeamlessInteriorsMod.FireDiagnostics.cs).
                DumpFireBlob(blob, "yukleme");
                DumpFireState("yukleme-oncesi");

                // ─── NO SECOND FireManager.Deserialize ───
                //
                // This used to replay the whole blob through FireManager.Deserialize, on the
                // grounds that the game's own pass had run before the clones existed. Two
                // measurements retired that idea.
                //
                // 1. IT NEVER DID THE JOB. One load, three clone fires, one blob:
                //
                //      kayit-ani       CampOffice 'Fire' yaniyor=True durum=FullBurn  (saved fine)
                //      yukleme-oncesi  CampOffice 'Fire' elapsed=0.0  durum=Off
                //      yukleme-sonrasi CampOffice 'Fire' elapsed=0.0  durum=Off       <- untouched
                //      yukleme-oncesi  SafeHouseA 'Fire' elapsed=0.0
                //      yukleme-sonrasi SafeHouseA 'Fire' elapsed=115.8                <- updated
                //
                //    The only difference: the player had saved inside SafeHouseA, so its clone
                //    was active and every other clone was SetActive(false). Deserialize walks
                //    straight past a fire in a switched-off hierarchy - and here every building
                //    the player is not standing in is switched off. So the replay reached, at
                //    most, the one building that did not need it, and the fire in the building
                //    they came from went out every single time.
                //
                //    (The save was never at fault: the blob carries FullBurn, the elapsed
                //    seconds and the remaining life, exactly as it should.)
                //
                // 2. IT IS A WHOLE-WORLD REBUILD RUN A SECOND TIME. The game has already
                //    applied this same blob during the load. Deserialize creates a fresh fire
                //    for any record it cannot match, and a re-created fire does not carry the
                //    guid the record was written with - so the next save has two records where
                //    it had one. A save in this playthrough is already carrying four live
                //    fires stacked on each of four stove positions.
                //
                // What replaces it is below: the same per-fire restore the game itself uses,
                // applied to exactly the fires this mod owns, matched by guid, creating
                // nothing. PruneDuplicateFires then clears up what the old replay left behind.
                RestoreCloneFireStates(blob);
                PruneDuplicateFires();

                FireManagerStealerPatch.s_StolenFireData = "";

                // The same fires, straight after. Compared with the dump above this says
                // whether the restore actually reached them.
                DumpFireState("yukleme-sonrasi");

                // A pruned duplicate is a destroyed object, so the "keep the closed clones
                // ticking" lists may be holding a reference to one.
                // (see SeamlessInteriorsMod.FrozenTime.cs)
                InvalidateClosedInteriorTimeCaches();
            }

            s_FireRestorePending = false;
        }

        // ─── FIRES STACKED ON TOP OF EACH OTHER ───
        //
        // Two fires cannot share a spot: a stove has one, a campfire occupies the ground it
        // stands on. So several Fire objects within a few centimetres of each other are
        // copies, and every one of them is written into the save on every write. A save in
        // this playthrough was carrying four on each of four stove positions - sixteen
        // records where there should have been four.
        //
        // WHAT IS SAFE TO REMOVE, AND NOTHING ELSE:
        //   * never a burning fire - whatever else is true, the player is using it;
        //   * never a fire inside a clone - those are this mod's own, and the whole fire
        //     restore above depends on them;
        //   * never the keeper, which is chosen to be the most "real" of the group.
        // Anything left is an unlit, unowned copy sitting inside another fire.
        private const float DUPLICATE_FIRE_RADIUS = 0.05f;

        private static int PruneDuplicateFires()
        {
            var managerFires = RealFires();
            if (managerFires == null || managerFires.Count < 2) return 0;

            var live = new List<Il2Cpp.Fire>(managerFires.Count);
            try
            {
                foreach (var f in managerFires)
                    if (f != null && f.gameObject != null) live.Add(f);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[FIRE-DEDUP] Ates listesi okunamadi: {ex.Message}");
                return 0;
            }

            float radiusSqr = DUPLICATE_FIRE_RADIUS * DUPLICATE_FIRE_RADIUS;
            var claimed = new bool[live.Count];
            var group = new List<Il2Cpp.Fire>();
            int removed = 0;

            for (int i = 0; i < live.Count; i++)
            {
                if (claimed[i]) continue;
                claimed[i] = true;

                group.Clear();
                group.Add(live[i]);

                Vector3 anchor = live[i].transform.position;
                for (int j = i + 1; j < live.Count; j++)
                {
                    if (claimed[j]) continue;
                    if ((live[j].transform.position - anchor).sqrMagnitude > radiusSqr) continue;

                    claimed[j] = true;
                    group.Add(live[j]);
                }

                if (group.Count < 2) continue;

                int keeper = ChooseFireToKeep(group);

                for (int k = 0; k < group.Count; k++)
                {
                    if (k == keeper) continue;

                    Il2Cpp.Fire dup = group[k];
                    if (dup == null || dup.gameObject == null) continue;

                    if (IsFireStillAlive(dup)) continue;                       // in use
                    if (FindInstanceOwning(dup.transform) != null) continue;   // clone's own

                    string name = "?", parent = "ROOT";
                    try { name = dup.gameObject.name; } catch { }
                    try { if (dup.transform.parent != null) parent = dup.transform.parent.name; } catch { }

                    try
                    {
                        Il2Cpp.FireManager.RemoveFire(dup);
                        UnityEngine.Object.Destroy(dup.gameObject);
                        removed++;

                        MelonLogger.Msg($"[FIRE-DEDUP] Kopya ates silindi: '{name}' parent={parent} " +
                                        $"pos=({anchor.x:F2},{anchor.y:F2},{anchor.z:F2})");
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"[FIRE-DEDUP] '{name}' silinemedi: {ex.Message}");
                    }
                }
            }

            if (removed > 0)
                MelonLogger.Msg($"[FIRE-DEDUP] Ust uste binmis {removed} kopya ates temizlendi, " +
                                $"FireManager'da {RealFires().Count} ates kaldi.");

            return removed;
        }

        // The most "real" member of a stack: one that is alight beats everything, then one
        // the mod owns, then one actually attached to a stove or a campfire, then anything
        // with a parent at all. A tie keeps the first, which is the oldest.
        private static int ChooseFireToKeep(List<Il2Cpp.Fire> group)
        {
            int best = 0;
            int bestScore = -1;

            for (int i = 0; i < group.Count; i++)
            {
                Il2Cpp.Fire fire = group[i];
                if (fire == null || fire.gameObject == null) continue;

                int score = 0;
                try { if (IsFireStillAlive(fire)) score += 8; } catch { }
                try { if (FindInstanceOwning(fire.transform) != null) score += 4; } catch { }

                try
                {
                    Transform p = fire.transform.parent;
                    if (p != null)
                    {
                        score += 1;
                        if (p.GetComponentInParent<Il2Cpp.WoodStove>() != null
                            || p.GetComponentInParent<Il2Cpp.Campfire>() != null) score += 2;
                    }
                }
                catch { }

                if (score > bestScore) { bestScore = score; best = i; }
            }

            return best;
        }

        // ─── APPLYING THE SAVED FIRE STATE TO A CLOSED BUILDING'S FIRES ───
        //
        // What FireManager.Deserialize will not do for a switched-off clone, done here by
        // hand: the blob is split back into its per-fire records, each record is matched to
        // a clone fire by the guid the game itself wrote, and Fire.Deserialize - the game's
        // own per-fire restore, the same one FireManager would have called - is applied.
        //
        // Only fires the save says should be BURNING are touched, and only when they are
        // not. A fresh clone starts every fire Off, so that is the only direction the error
        // can go, and leaving everything else alone keeps this out of the way of whatever
        // the game did manage to restore.
        private static int RestoreCloneFireStates(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return 0;

            Dictionary<string, string> burning;
            try { burning = ParseBurningFireRecords(blob); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[FIRE-RESTORE] Ates kayitlari cozulemedi: {ex.Message}");
                return 0;
            }

            if (burning.Count == 0) return 0;

            // Which fires actually need putting back, and which buildings they are in.
            var work = new List<KeyValuePair<Il2Cpp.Fire, string>>();
            var toOpen = new List<GameObject>();

            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || instance.MasterInterior == null) continue;

                bool needsThisOne = false;

                foreach (var fire in instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Fire>(true))
                {
                    if (fire == null || fire.gameObject == null) continue;

                    string key = GetFireKey(fire);
                    if (string.IsNullOrEmpty(key)) continue;

                    string record;
                    if (!burning.TryGetValue(key, out record)) continue;

                    bool alreadyLit = false;
                    try { alreadyLit = fire.IsBurning(); } catch { }
                    if (alreadyLit) continue;

                    work.Add(new KeyValuePair<Il2Cpp.Fire, string>(fire, record));
                    needsThisOne = true;
                }

                // Only buildings that have something to put back are opened, and only for
                // the few statements below.
                if (needsThisOne && !instance.MasterInterior.activeSelf)
                    toOpen.Add(instance.MasterInterior);
            }

            if (work.Count == 0) return 0;

            int restored = 0;

            // NO yield, NO coroutine, NOTHING that can end the frame between these two
            // loops. A clone opened here must be shut again before Unity draws anything,
            // or the player sees the inside of a building they are standing outside of.
            // (The same one-frame open/close trick SaveInactiveSceneGearItems uses.)
            foreach (var go in toOpen)
                if (go != null) go.SetActive(true);

            try
            {
                foreach (var entry in work)
                {
                    Il2Cpp.Fire fire = entry.Key;
                    if (fire == null || fire.gameObject == null) continue;

                    // THE CLONE OWNS THE POSITION, THE SAVE OWNS THE STATE.
                    //
                    // The record carries the world position the fire had in the PREVIOUS
                    // session, and a clone is re-aligned to its shell on every load - on the
                    // camp office the two are already a quarter of a metre apart. Letting the
                    // record move the fire would walk it out of its stove over a few loads,
                    // so the transform is put back exactly where the clone had it.
                    Vector3 pos = fire.transform.position;
                    Quaternion rot = fire.transform.rotation;

                    try
                    {
                        fire.Deserialize(entry.Value);
                        restored++;
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"[FIRE-RESTORE] '{fire.gameObject.name}' durumu uygulanamadi: {ex.Message}");
                        continue;
                    }

                    fire.transform.position = pos;
                    fire.transform.rotation = rot;

                    if (s_DebugBounds)
                    {
                        bool lit = false; float life = 0f;
                        try { lit = fire.IsBurning(); } catch { }
                        try { life = fire.GetRemainingLifeTimeSeconds(); } catch { }

                        MelonLogger.Msg($"[FIRE-RESTORE] '{fire.gameObject.name}' geri yakildi: " +
                                        $"yaniyor={lit} kalanOmur={life:F1}sn pos=({pos.x:F2},{pos.y:F2},{pos.z:F2})");
                    }
                }
            }
            finally
            {
                foreach (var go in toOpen)
                    if (go != null) go.SetActive(false);
            }

            MelonLogger.Msg($"[FIRE-RESTORE] Kapali binalardaki {restored} ates geri yuklendi " +
                            $"({toOpen.Count} bina bunun icin bir karelik acildi).");

            return restored;
        }

        // How a fire is addressed in the save. The game writes ObjectGuid's value as the
        // record's m_Guid, and on a clone that value lives in PDID rather than m_Guid -
        // both are accepted so neither spelling can miss.
        internal static string GetFireKey(Il2Cpp.Fire fire)
        {
            try
            {
                var g = fire.GetComponent<Il2Cpp.ObjectGuid>();
                if (g == null) g = fire.GetComponentInParent<Il2Cpp.ObjectGuid>();
                if (g == null) return null;

                if (!string.IsNullOrEmpty(g.m_Guid)) return g.m_Guid;
                return g.PDID;
            }
            catch { return null; }
        }

        private const string FIRE_REC_PAYLOAD = "\"m_SearializedFire\":\"";
        private const string FIRE_REC_GUID = "\",\"m_Guid\":\"";

        // The blob is one record after another, each laid out as
        //   "m_SearializedFire":"<escaped per-fire json>","m_Guid":"<guid>"
        // and the separator cannot occur inside the payload (there it is escaped), so the
        // pair can be lifted out without a JSON parser.
        private static Dictionary<string, string> ParseBurningFireRecords(string blob)
        {
            var result = new Dictionary<string, string>();

            int idx = 0;
            while (true)
            {
                int start = blob.IndexOf(FIRE_REC_PAYLOAD, idx, System.StringComparison.Ordinal);
                if (start < 0) break;
                start += FIRE_REC_PAYLOAD.Length;

                int end = blob.IndexOf(FIRE_REC_GUID, start, System.StringComparison.Ordinal);
                if (end < 0) break;

                int guidStart = end + FIRE_REC_GUID.Length;
                int guidEnd = blob.IndexOf('"', guidStart);
                if (guidEnd < 0) break;

                idx = guidEnd + 1;

                string guid = blob.Substring(guidStart, guidEnd - guidStart);
                if (string.IsNullOrEmpty(guid)) continue;

                string payload = JsonUnescape(blob.Substring(start, end - start));
                if (!RecordSaysBurning(payload)) continue;

                result[guid] = payload;
            }

            return result;
        }

        // FireState: Off=0, Starting_*=1..5, FullBurn=6, Blownout=7. A record with no state
        // at all is a fire that was never lit - the scene template already has it that way.
        private static bool RecordSaysBurning(string payload)
        {
            string state = ExtractString(payload, "\"m_FireStateProxy\":\"", "\"");
            if (string.IsNullOrEmpty(state)) return false;

            return state != "Off" && state != "Blownout";
        }

        private static string JsonUnescape(string text)
        {
            if (text.IndexOf('\\') < 0) return text;

            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\\' || i + 1 >= text.Length) { sb.Append(c); continue; }

                char next = text[++i];
                switch (next)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    default: sb.Append('\\').Append(next); break;
                }
            }
            return sb.ToString();
        }

        // FireManager only knows the fires that were registered with it, and that list is
        // the ONLY thing FireManager.Serialize walks. A clone fire missing from it is
        // invisible twice over: it cannot be matched when the saved data is applied, and it
        // is not written into the next save at all - so it would be gone after the save
        // AFTER this one, even if it looked fine now.
        public static int RegisterCloneFiresWithManager()
        {
            int added = 0;

            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || instance.MasterInterior == null) continue;

                // includeInactive: a closed clone is switched off, and its stove still has
                // to be in the list.
                var allFires = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Fire>(true);
                foreach (var f in allFires)
                    if (f != null && !RealFires().Contains(f)) { Il2Cpp.FireManager.AddFire(f); added++; }

                var allWoodStoves = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.WoodStove>(true);
                foreach (var ws in allWoodStoves)
                    if (ws != null && !RealWoodStoves().Contains(ws)) { Il2Cpp.FireManager.AddWoodStove(ws); added++; }

                var allCampfires = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Campfire>(true);
                foreach (var cf in allCampfires)
                    if (cf != null && !RealCampfires().Contains(cf)) { Il2Cpp.FireManager.AddCampfire(cf); added++; }
            }

            return added;
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
            if (!CanPersistContent(instance)) return;

            string path = GetInactiveSceneGearSavePath(instance);
            if (path == null) return;

            Transform interiorT = instance.MasterInterior.transform;

            // includeInactive=true so every GearItem is visible even while the clone scene
            // is switched off. (ShouldPersistGear decides which ones are SAVED.)
            var allGear = InteriorScan.Gear(instance.MasterInterior);
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

            // Reported at normal level - see the same note in SavePlaceablePositions.
            // These items were lying OUTSIDE the clone and have just been written into
            // this building's file; on the next load they will be destroyed from the
            // world and respawned inside the building.
            if (extraCount > 0)
                MelonLogger.Msg($"[GEAR-SAVE] {instance.Config.ResolvedInstanceId}: klon disindan {extraCount} esya sahiplenildi.");

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

        // Prefab names Addressables could not resolve. Retrying a miss costs a full
        // synchronous load attempt and always fails again, so each dead name is only
        // ever paid for once per session.
        private static readonly HashSet<string> s_MissingGearPrefabs = new HashSet<string>();

        // Prefab names already requested this session, so a second building holding the
        // same items does not queue them again.
        private static readonly HashSet<string> s_WarmedGearPrefabs = new HashSet<string>();

        // Strips "(Clone)" and any " (2)" suffix to get the Addressables key.
        private static string CleanGearPrefabName(string raw)
        {
            string cleanName = raw.Replace("(Clone)", "").Trim();
            int parenIdx = cleanName.LastIndexOf(" (");
            if (parenIdx > 0 && cleanName.EndsWith(")"))
                cleanName = cleanName.Substring(0, parenIdx).Trim();
            return cleanName;
        }

        // Asks Addressables for every distinct prefab this restore will need, all at
        // once and without blocking, then waits for them together. See the call site for
        // why this matters so much.
        private static IEnumerator WarmGearPrefabs(List<GearSaveEntry> savedEntries)
        {
            var wanted = new List<string>();
            var seen = new HashSet<string>();

            foreach (var entry in savedEntries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.name)) continue;

                string clean = CleanGearPrefabName(entry.name);
                if (string.IsNullOrEmpty(clean)) continue;
                if (s_MissingGearPrefabs.Contains(clean)) continue;
                if (s_WarmedGearPrefabs.Contains(clean)) continue;
                if (!seen.Add(clean)) continue;

                wanted.Add(clean);
            }

            if (wanted.Count == 0) yield break;

            var handles = new List<UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject>>(wanted.Count);
            foreach (var name in wanted)
            {
                try
                {
                    handles.Add(UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(name));
                    s_WarmedGearPrefabs.Add(name);
                }
                catch
                {
                    s_MissingGearPrefabs.Add(name);
                }
            }

            // Give the loads frames to finish. They run in parallel, so this is bounded
            // by the slowest one rather than by their sum.
            float deadline = Time.realtimeSinceStartup + GEAR_WARMUP_TIMEOUT;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool allDone = true;
                for (int i = 0; i < handles.Count; i++)
                {
                    if (!handles[i].IsDone) { allDone = false; break; }
                }
                if (allDone) break;
                yield return null;
            }

            int loaded = 0, failed = 0;
            for (int i = 0; i < handles.Count; i++)
            {
                if (!handles[i].IsDone) continue;
                if (handles[i].Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded) loaded++;
                else failed++;
            }

            MelonLogger.Msg($"[GEAR-WARMUP] {wanted.Count} farkli prefab istendi: {loaded} yuklendi, {failed} basarisiz, " +
                            $"{handles.Count - loaded - failed} hala bekliyor ({(Time.realtimeSinceStartup - (deadline - GEAR_WARMUP_TIMEOUT)) * 1000f:F0} ms).");
        }

        // Upper bound on the warm-up wait. Anything still loading after this is simply
        // paid for item by item, exactly as before.
        private const float GEAR_WARMUP_TIMEOUT = 20f;

        // Synchronous wrapper, kept so every existing call site behaves exactly as
        // before. Draining the enumerator ignores its yields, so the whole restore
        // still finishes inside one frame.
        public static void RestoreInactiveSceneGearItems(SeamlessInteriorInstance instance)
        {
            var routine = RestoreInactiveSceneGearItemsRoutine(instance, false);
            while (routine.MoveNext()) { }
        }

        public static IEnumerator RestoreInactiveSceneGearItemsRoutine(SeamlessInteriorInstance instance, bool spread)
        {
            if (instance.MasterInterior == null) yield break;

            string path = GetInactiveSceneGearSavePath(instance);
            if (path == null || !File.Exists(path))
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName} gear save dosyasi bulunamadi, atlaniyor.");
                yield break;
            }

            string fileText = File.ReadAllText(path);
            var savedEntries = ParseGearFile(fileText);

            if (savedEntries.Count == 0)
            {
                // AN EMPTY FILE IS MEANINGFUL, NOT A REASON TO BAIL OUT.
                //
                // It means the player stripped this building bare. The clone was just
                // rebuilt from the scene template, so it is standing there full of raw
                // template loot; returning here would hand that loot straight back and
                // the emptied house would refill itself on every load. The cleanup below
                // has to run so the template loot is cleared - the spawn loop then simply
                // has nothing to spawn. (The same reasoning is spelled out further down
                // for the ghost-record case.)
                //
                // GUARD: a file that clearly HAS records but parsed to none is corrupt,
                // not empty. Wiping the building's contents on a parse failure would be
                // unrecoverable, so that case keeps the old bail-out.
                if (fileText.IndexOf(GEAR_ENTRY_MARKER, System.StringComparison.Ordinal) >= 0)
                {
                    MelonLogger.Warning($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName}: gear dosyasi okunamadi " +
                                        $"(kayit var gibi ama cozulemedi), esyalara dokunulmuyor.");
                    yield break;
                }

                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-RESTORE] {instance.Config.InteriorSceneBaseName} kayitli gear yok, sablon lootu temizlenecek.");
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
            var existingGear = InteriorScan.Gear(instance.MasterInterior);
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

                // Anything the player set down themselves lives under the game's own
                // placement root, and this loop DESTROYS what it matches. Being wrong
                // about a piece of scenery costs nothing; being wrong about the player's
                // own belongings destroys them, so that root is off limits here.
                if (IsUnderPlacementRoot(gear.transform)) continue;

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

            // ─── WARM THE ASSET CACHE FIRST ───
            //
            // Spawning an item loads its prefab through Addressables SYNCHRONOUSLY. Cold,
            // that costs tens of milliseconds each; warm, well under one. Measured on a
            // real save: the same 736 items took 48.7 seconds on the first load and 1.5
            // seconds on the second, purely because of this.
            //
            // A building holds many copies of few prefabs, so asking for the DISTINCT
            // names up front - asynchronously, all at once, while the frames keep
            // running - turns almost all of those cold loads into warm ones.
            //
            // The handles are intentionally not released: the items about to be spawned
            // reference these very assets, and the game keeps gear prefabs loaded for the
            // session anyway.
            if (spread)
            {
                IEnumerator warm = WarmGearPrefabs(savedEntries);
                while (warm.MoveNext()) yield return warm.Current;
                if (instance.MasterInterior == null) yield break;
            }

            int restoredCount = 0;
            int failedCount = 0;
            int statefulCount = 0;

            foreach (var entry in savedEntries)
            {
                // Frame spreading: only active when the caller asked for it (the lazy
                // hydration path). The synchronous wrapper drains this enumerator, so
                // these yields cost nothing there.
                //
                // Budget rather than a fixed count: spawn cost per item varies by two
                // orders of magnitude depending on whether Addressables has the asset
                // warm (see FrameBudget).
                if (spread && FrameBudget.Exceeded())
                {
                    yield return null;

                    // The clone can be torn down mid-restore (region change, save load).
                    if (instance.MasterInterior == null) yield break;
                }

                // Strip "(Clone)" and the trailing numbers from the name to get the
                // Addressables key. Shared with the warm-up above so both ask for
                // exactly the same key - a mismatch there would warm the wrong asset and
                // leave every spawn cold.
                string cleanName = CleanGearPrefabName(entry.name);

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

                    // A name Addressables has already failed on is not retried. Each miss
                    // costs a full synchronous WaitForCompletion, and the same handful of
                    // dead prefab names recurs in every building and on every load.
                    if (!s_MissingGearPrefabs.Contains(cleanName))
                    {
                        try
                        {
                            var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(cleanName);
                            handle.WaitForCompletion();
                            if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && handle.Result != null)
                                prefab = handle.Result;
                        }
                        catch { }

                        if (prefab == null) s_MissingGearPrefabs.Add(cleanName);
                    }

                    if (prefab == null)
                    {
                        failedCount++;
                        if (s_DebugBounds)
                            MelonLogger.Msg($"[GEAR-RESTORE] PREFAB BULUNAMADI: {cleanName} (orijinal: {entry.name})");
                        continue;
                    }

                    spawned = UnityEngine.Object.Instantiate(prefab);

                    // The record says this item is standing here, so the prefab's spawn
                    // chance must not get a say once the building opens (see
                    // PreRollGearSpawnChance). The stateful path above carries the flag in
                    // its serialized data.
                    var spawnedGear = spawned.GetComponent<Il2Cpp.GearItem>();
                    if (spawnedGear != null) spawnedGear.m_RolledSpawnChance = true;
                }

                // The stored position is clone-local, so convert it to world space.
                Vector3 worldPos = interiorT.TransformPoint(entry.position);
                Quaternion worldRot = interiorT.rotation * entry.rotation;

                if (spawned.transform.parent != instance.MasterInterior.transform)
                    spawned.transform.SetParent(instance.MasterInterior.transform, true);

                spawned.transform.position = worldPos;
                spawned.transform.rotation = worldRot;

                // A zero scale means the record has none - leave what the game's own
                // deserialize produced. Items attached to a place point on a shelf or a
                // workbench inherit that anchor's scale, and overwriting it with 1 makes
                // them balloon. (Records the mod writes itself always carry a real scale.)
                if (entry.scale.sqrMagnitude > 1e-8f)
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
            if (!CanPersistContent(instance)) return;

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

                // The name+index key is kept so files written by older versions of the mod
                // still load, but it is NO LONGER what the restore matches on. See the
                // note above RestoreContainerDataRoutine: enumeration order is not stable
                // once the player can put their own containers in the building.
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

                // Position in clone-local space - the identity the restore actually uses.
                Vector3 lp = interiorT.InverseTransformPoint(c.transform.position);

                // JSON-safe: escape the quotes and newlines inside the serialized data.
                string escapedData = serialized.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                entries.Add($"{{\"key\":\"{EscapeJson(matchKey)}\",\"n\":\"{EscapeJson(CleanGearName(containerName))}\"," +
                            $"\"px\":{lp.x:R},\"py\":{lp.y:R},\"pz\":{lp.z:R}," +
                            $"\"data\":\"{escapedData}\"}}");

                if (s_DebugBounds)
                    MelonLogger.Msg($"[CONTAINER-SAVE] Kaydedildi: {matchKey} dataLen={serialized.Length}");
            }

            string json = "[\n" + string.Join(",\n", entries) + "\n]";
            JsonWriteCache.Write(path, json);

            if (s_DebugBounds)
                MelonLogger.Msg($"[CONTAINER-SAVE] {instance.Config.InteriorSceneBaseName}: {entries.Count} konteyner kaydedildi: {path}");
        }

        // Synchronous wrapper - see RestoreInactiveSceneGearItems for the rationale.
        public static void RestoreContainerData(SeamlessInteriorInstance instance)
        {
            var routine = RestoreContainerDataRoutine(instance, false);
            while (routine.MoveNext()) { }
        }

        // ─────────────────────────────────────────────────────────────────
        // MATCHING A SAVED CONTAINER TO A CONTAINER IN THE CLONE
        //
        // This used to be keyed on "<name>###<index>", where the index counted equal
        // names in GetComponentsInChildren order. That works only while the set of
        // containers is fixed - and it is not. A player's base is full of containers
        // THEY put there: crates, lockers, suitcases, and the drawers built into placed
        // furniture. Those are created by the restore itself, and some of them the game
        // respawns from its own save first, so the order they appear in the hierarchy
        // differs from one load to the next.
        //
        // When the order shifts, every index past the shift points at the wrong
        // container and the contents are poured into the wrong box or dropped entirely.
        // On a real save this emptied almost an entire building on the first reload.
        //
        // Position is the stable identity - it is what the game's own lookup uses
        // (ContainerManager.FindContainerByPosition), and the pre-mod import matched 59
        // of 59 containers on it while matching 0 on guid. So position comes first,
        // name+nearest second, and the old key only as a fallback for files written
        // before this change.
        // ─────────────────────────────────────────────────────────────────
        private class ContainerRecord
        {
            public string key;
            public string name;
            public Vector3 position;
            public bool hasPosition;
            public string data;
        }

        // Tolerance for calling two container positions the same object. The clone is an
        // exact copy of the scene and the furniture is put back before this runs, so this
        // only has to absorb float round-tripping.
        private const float CONTAINER_RESTORE_RADIUS = 0.25f;

        // Looser radius for the "same prefab, nearest one" fallback.
        private const float CONTAINER_RESTORE_NAME_RADIUS = 3.0f;

        private static List<ContainerRecord> ParseContainerFile(string json)
        {
            var result = new List<ContainerRecord>();

            foreach (var element in SplitJsonArray(json))
            {
                string data = ExtractEscapedField(element, "data");
                if (string.IsNullOrEmpty(data)) continue;

                var rec = new ContainerRecord();
                rec.data = data;
                rec.key = ExtractEscapedField(element, "key");
                rec.name = ExtractEscapedField(element, "n");

                // Files written before positions were recorded simply have no "px".
                rec.hasPosition = element.IndexOf("\"px\":", System.StringComparison.Ordinal) >= 0;
                if (rec.hasPosition)
                {
                    rec.position = new Vector3(
                        ExtractFloat(element, "\"px\":"),
                        ExtractFloat(element, "\"py\":"),
                        ExtractFloat(element, "\"pz\":"));
                }

                result.Add(rec);
            }

            return result;
        }

        // Reads one escaped string field out of a JSON object, quote- and escape-aware.
        private static string ExtractEscapedField(string element, string field)
        {
            string marker = "\"" + field + "\":\"";
            int i = element.IndexOf(marker, System.StringComparison.Ordinal);
            if (i < 0) return null;

            int start = i + marker.Length;
            int end = FindClosingQuote(element, start);
            if (end < 0) return null;

            return UnescapeJson(element.Substring(start, end - start));
        }

        public static IEnumerator RestoreContainerDataRoutine(SeamlessInteriorInstance instance, bool spread)
        {
            if (instance.MasterInterior == null) yield break;

            string path = GetContainerSavePath(instance);
            if (path == null || !File.Exists(path))
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[CONTAINER-LOAD] {instance.Config.InteriorSceneBaseName} konteyner save dosyasi bulunamadi, atlaniyor.");
                yield break;
            }

            var records = ParseContainerFile(File.ReadAllText(path));
            if (records.Count == 0)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[CONTAINER-LOAD] {instance.Config.InteriorSceneBaseName}: dosyada konteyner verisi yok.");
                yield break;
            }

            // The clone's containers, described the same three ways the file describes them.
            Transform interiorT = instance.MasterInterior.transform;
            var containers = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Container>(true);

            var keys = new string[containers.Length];
            var names = new string[containers.Length];
            var positions = new Vector3[containers.Length];
            var claimed = new bool[containers.Length];
            var nameCounter = new Dictionary<string, int>();

            for (int i = 0; i < containers.Length; i++)
            {
                var c = containers[i];
                if (c == null) continue;

                string containerName = c.gameObject.name;
                int nameIndex;
                if (!nameCounter.TryGetValue(containerName, out nameIndex)) nameIndex = 0;
                nameCounter[containerName] = nameIndex + 1;

                keys[i] = $"{containerName}###{nameIndex}";
                names[i] = CleanGearName(containerName);
                positions[i] = interiorT.InverseTransformPoint(c.transform.position);
            }

            // Assign every record to a container, strongest evidence first, so a weak
            // match can never steal a container a stronger one needs.
            var target = new int[records.Count];
            for (int r = 0; r < records.Count; r++) target[r] = -1;

            int byPos = 0, byKey = 0, byName = 0, missed = 0;

            // 1) Exact position.
            for (int r = 0; r < records.Count; r++)
            {
                if (!records[r].hasPosition) continue;

                int best = -1;
                float bestSqr = CONTAINER_RESTORE_RADIUS * CONTAINER_RESTORE_RADIUS;
                for (int i = 0; i < containers.Length; i++)
                {
                    if (claimed[i] || keys[i] == null) continue;
                    float sqr = (positions[i] - records[r].position).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = i; }
                }

                if (best >= 0) { target[r] = best; claimed[best] = true; byPos++; }
            }

            // 2) The old name+index key, for files written before positions existed.
            for (int r = 0; r < records.Count; r++)
            {
                if (target[r] >= 0 || string.IsNullOrEmpty(records[r].key)) continue;

                for (int i = 0; i < containers.Length; i++)
                {
                    if (claimed[i] || keys[i] == null) continue;
                    if (keys[i] != records[r].key) continue;
                    target[r] = i; claimed[i] = true; byKey++;
                    break;
                }
            }

            // 3) Same prefab, nearest one. A container that moved slightly is far better
            //    served by putting its contents in the same kind of box a metre away than
            //    by losing them.
            for (int r = 0; r < records.Count; r++)
            {
                if (target[r] >= 0 || string.IsNullOrEmpty(records[r].name)) continue;

                int best = -1;
                float bestSqr = CONTAINER_RESTORE_NAME_RADIUS * CONTAINER_RESTORE_NAME_RADIUS;
                for (int i = 0; i < containers.Length; i++)
                {
                    if (claimed[i] || keys[i] == null) continue;
                    if (!string.Equals(names[i], records[r].name, System.StringComparison.Ordinal)) continue;

                    float sqr = (positions[i] - records[r].position).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = i; }
                }

                if (best >= 0) { target[r] = best; claimed[best] = true; byName++; }
            }

            int restoredCount = 0;

            for (int r = 0; r < records.Count; r++)
            {
                int i = target[r];
                if (i < 0)
                {
                    missed++;
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[CONTAINER-LOAD] ESLESME YOK: key='{records[r].key}' isim='{records[r].name}' pos={records[r].position}");
                    continue;
                }

                try
                {
                    var loadedItems = new Il2CppSystem.Collections.Generic.List<Il2Cpp.GearItem>();
                    containers[i].Deserialize(records[r].data, loadedItems);
                    restoredCount++;

                    if (s_DebugBounds)
                        MelonLogger.Msg($"[CONTAINER-LOAD] RESTORED: {keys[i]} loadedItems={loadedItems.Count}");
                }
                catch (System.Exception ex)
                {
                    if (s_DebugBounds)
                        MelonLogger.Warning($"[CONTAINER-LOAD] Deserialize hatasi: {keys[i]} - {ex.Message}");
                }

                // Frame spreading (lazy hydration path only).
                //
                // ONE CONTAINER PER FRAME, UNCONDITIONALLY. Deserializing a container
                // spawns everything inside it in a single call that cannot be split, and
                // a well used base has containers holding a couple of hundred items -
                // over 200KB of save data for one of them. Asking the frame budget
                // afterwards is too late: the frame is already gone.
                if (spread)
                {
                    yield return null;
                    if (instance.MasterInterior == null) yield break;
                }
            }

            if (missed > 0 || s_DebugBounds)
            {
                MelonLogger.Msg($"[CONTAINER-LOAD] {instance.Config.InteriorSceneBaseName}: {restoredCount}/{records.Count} konteyner geri yuklendi " +
                                $"(pozisyon={byPos}, eski anahtar={byKey}, isim={byName}), {missed} eslesmedi. " +
                                $"Klonda {containers.Length} konteyner var.");
            }
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

            bool wasActive = instance.MasterInterior.activeSelf;
            if (!wasActive) instance.MasterInterior.SetActive(true);

            try
            {
                if (instance.InteriorTrigger != null)
                {
                    // Outside the volume: not this building's, and no ray is going to
                    // change that.
                    if (!instance.IsPositionInVolume(pos, -0.7f)) return false;

                    // INSIDE THE VOLUME IS NOT ENOUGH.
                    //
                    // The volume is an axis-aligned box around a building that is not
                    // axis aligned, so it reaches past the walls. Meat left by the door,
                    // a bed against the outside wall, a rock cache by the corner - all of
                    // them sit inside the box while being unmistakably outdoors, and
                    // claiming them meant they were pulled into the clone and vanished
                    // from the world the moment the player walked away.
                    //
                    // A ROOF ALONE DOES NOT SETTLE IT either: the clone carries the
                    // building's own roof, and its eaves hang out past the walls, so
                    // something standing against the wall does have clone geometry
                    // overhead. What it does NOT have is clone FLOOR underneath - it is
                    // standing on the region's terrain.
                    //
                    // So both are required. The old objection to the floor test was the
                    // 2.5m ray origin, which cleared low ceilings; at the 0.15m item lift
                    // an object on a table still finds the floor, because the ray passes
                    // through gear and placeables on its way down.
                    return instance.IsPositionInsideRaycastOnly(pos, SeamlessInteriorInstance.ITEM_RAY_ORIGIN_LIFT);
                }

                // No trigger to test against (a sub-interior with no volume built yet):
                // fall back to the full ray test.
                return instance.IsPositionInsideRaycastOnly(pos, SeamlessInteriorInstance.ITEM_RAY_ORIGIN_LIFT);
            }
            finally
            {
                if (!wasActive) instance.MasterInterior.SetActive(false);
            }
        }
    }
}
