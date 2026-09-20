using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SeamlessInteriors
{
    // Where this building's pre-mod data was found in the save slot.
    public enum LegacySourceKind
    {
        None = 0,
        Guid = 1,   // addressed by the entrance door's LoadScene GUID (instanced scenes)
        Name = 2,   // addressed by the plain interior scene name
    }

    public enum LegacyImportState
    {
        Unknown = 0,
        NotNeeded = 1,   // nothing to import (new game, already imported, never visited)
        Pending = 2,     // data found, waiting to be transcoded
        Done = 3,
        Failed = 4,
    }

    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────────
        // BACKWARDS COMPATIBILITY WITH PRE-MOD SAVES
        //
        // THE PROBLEM
        // A player with a 1000 day save has spent those 1000 days in the ORIGINAL
        // interior scenes: the loading-screen Camp Office, a separate scene the game
        // saves under its own key. Everything they did in there - items dropped on the
        // floor, container contents, harvested furniture, opened lockers, placed
        // decorations, cleared junk - lives in that scene's SceneSaveGameFormat.
        //
        // The mod never reads it. It builds a fresh clone from the scene template and
        // restores only its own JSON files, which for such a save do not exist. The
        // result: a pristine, never-visited Camp Office, and (worse) a FRESH LOOT ROLL,
        // so the building the player emptied 900 days ago is full again.
        //
        // THE KEY INSIGHT THAT MAKES THIS CHEAP
        // LoadInteriorScenes reparents the interior scene roots with
        // SetParent(MasterInterior, worldPositionStays: false) while MasterInterior is
        // still at identity. So every child's local position IS its original
        // interior-scene world position, and MasterInterior.InverseTransformPoint(w)
        // gives that coordinate back after the clone has been moved onto the shell.
        //
        // The mod's own JSON files already store positions in exactly that space - and
        // so does the game's own scene save data. The two formats are in the SAME
        // COORDINATE SYSTEM. No transform maths is needed, only a transcode.
        //
        // THE DESIGN
        // This file is a TRANSCODER, not a second restore path:
        //
        //   vanilla SceneSaveGameFormat  ->  the mod's own JSON files
        //
        // Once the files are written, the ordinary RestoreInactiveSceneGearItems /
        // RestoreContainerData / RestoreInteractiveState / RestoreJunkState do all the
        // actual work, completely unchanged. From the second load onwards this building
        // is indistinguishable from one that was always played with the mod.
        //
        // Placed furniture is the one exception: those objects do not exist in the
        // scene template at all, so a transform-only record has nothing to attach to.
        // They go into the mod's own _spawn_placeables.json and are re-created through
        // the game's Placeable.FindOrCreateAndDeserialize
        // (see ApplyLegacyPlaceablesRoutine and RestoreSpawnedPlaceables).
        //
        // Everything is gated on a per-instance marker file, so the import can only
        // ever happen once per building per save.
        //
        // The marker records a FACT ABOUT THE PLAYTHROUGH - "already imported", "the mod
        // owns this building", "never visited". A statement that is merely true right now
        // is not one of those and must not be written down; see the "brand new game" step
        // in ProbeLegacySave for what persisting one costs.
        // ─────────────────────────────────────────────────────────────────────

        // Marker: written once the import has been decided, whatever the outcome.
        // Its existence alone stops the import from ever being attempted again.
        private static string GetLegacyMarkerPath(SeamlessInteriorInstance instance)
        {
            return GetInstanceSavePath(instance, "_legacy.json");
        }

        // The untouched vanilla blob, kept next to the marker so nothing is ever lost
        // and the import can be re-checked by hand.
        private static string GetLegacyBackupPath(SeamlessInteriorInstance instance)
        {
            return GetInstanceSavePath(instance, "_legacy_backup.json");
        }

        // ─────────────────────────────────────────────────────────────────────
        // PROBE - runs during Run(), must stay cheap
        //
        // Only decides WHETHER there is anything to import. The expensive JSON parse
        // and the transcode happen later, during hydration.
        //
        // The result feeds one important decision at Run() time: a building the player
        // visited before must NOT roll fresh loot (see the LegacyImportPending check in
        // Run()).
        // ─────────────────────────────────────────────────────────────────────
        // finalAttempt: called from hydration, after every building has been probed once.
        // Nothing more will be learned by waiting, so an unresolved building is settled
        // here instead of being asked about again.
        public static void ProbeLegacySave(SeamlessInteriorInstance instance, bool finalAttempt = false)
        {
            if (instance == null || instance.MasterInterior == null) return;

            instance.LegacyImport = LegacyImportState.NotNeeded;

            // Left Unknown rather than NotNeeded when the feature is off, so switching it
            // on later still gives this building a chance to be probed.
            if (!IsLegacySaveImportEnabled)
            {
                instance.LegacyImport = LegacyImportState.Unknown;
                return;
            }

            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return;

            // 1) Already decided for this building in this save.
            //
            //    ...with ONE exception, and it is the exception that used to eat a whole
            //    playthrough's worth of interior history. See ShouldHonourMarker.
            string markerPath = GetLegacyMarkerPath(instance);
            if (markerPath == null) return;
            if (File.Exists(markerPath))
            {
                if (ShouldHonourMarker(instance, markerPath)) return;
                TryDelete(markerPath);
            }

            // 2) The mod already owns this save's data for this building - it has been
            //    played with the mod at least once, so there is nothing legacy about it.
            string gearPath = GetInactiveSceneGearSavePath(instance);
            if (gearPath != null && File.Exists(gearPath))
            {
                WriteLegacyMarker(instance, "mod-data-exists", null, 0, 0, 0, 0);
                return;
            }

            // 3) Brand new game: there is no history to inherit YET.
            //    (CheckNewGameLootLock uses the same threshold.)
            //
            //    DELIBERATELY NOT WRITTEN TO DISK. "This game is three minutes old" is a
            //    statement about this moment, not about the save, and it stops being true
            //    a few minutes later - unlike "mod-data-exists" or "never-visited", which
            //    are facts about the playthrough and are safe to record forever.
            //
            //    Persisting it cost a whole test session: the player started a new game,
            //    spawned INSIDE the original loading-screen Camp Office, spent hours in
            //    there filling the vanilla scene save with real history, walked out - and
            //    met a pristine clone, because a marker written at minute zero had already
            //    answered the question for good.
            if (IsBrandNewGame())
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[LEGACY] {instance.Config.ResolvedInstanceId}: oyun henuz yeni " +
                                    $"(<0.05sa), devralinacak gecmis yok - karar diske YAZILMIYOR, " +
                                    $"sonraki bolge yuklemesinde yeniden sorulacak.");
                return;
            }

            // 4) Does the save actually contain this interior?
            //    A building the player never entered in 1000 days has no entry at all -
            //    and that is exactly right: the mod then does its normal first-time loot
            //    roll, so the house is found untouched, as it should be.
            bool ambiguous;
            if (!ResolveLegacySource(instance, out ambiguous))
            {
                if (ambiguous && !finalAttempt)
                {
                    // Buildings are probed in config order, so a sibling sharing this
                    // scene may simply not have claimed its key yet. Deciding now would
                    // be deciding on incomplete information, so no marker is written and
                    // the question is asked again at hydration time, by which point every
                    // sibling has been probed (see HydrateRoutine).
                    instance.LegacyImport = LegacyImportState.Unknown;
                    return;
                }

                if (ambiguous)
                {
                    MelonLogger.Warning($"[LEGACY] {instance.Config.ResolvedInstanceId}: " +
                                        $"'{instance.Config.InteriorSceneBaseName}' sahnesini birden fazla bina paylasiyor ve bu bina " +
                                        $"hicbir kayit anahtarina baglanamadi. Yanlis binaya veri aktarmamak icin ATLANIYOR. " +
                                        $"(F6 ile kayittaki anahtarlari listeleyip config'e LegacySceneKeyOverride yazabilirsiniz.)");
                    WriteLegacyMarker(instance, "ambiguous", null, 0, 0, 0, 0);
                    return;
                }

                WriteLegacyMarker(instance, "never-visited", null, 0, 0, 0, 0);
                if (s_DebugBounds)
                    MelonLogger.Msg($"[LEGACY] {instance.Config.ResolvedInstanceId}: eski kayitta bu bina yok (hic girilmemis), normal ilk loot akisi calisacak.");
                return;
            }

            instance.LegacyImport = LegacyImportState.Pending;

            MelonLogger.Msg($"[LEGACY] {instance.Config.ResolvedInstanceId}: eski (mod oncesi) kayit verisi bulundu " +
                            $"(kaynak={instance.LegacySource}, id={instance.LegacySourceId}). Oyuncu yaklastiginda aktarilacak.");
        }

        public static bool IsLegacyImportPending(SeamlessInteriorInstance instance)
        {
            return instance != null && instance.LegacyImport == LegacyImportState.Pending;
        }

        // Under ~3 minutes of played time means the save was made moments ago. The same
        // threshold CheckNewGameLootLock uses; the two must agree, because one decides
        // whether the previous playthrough's files are wiped and the other whether this
        // one has any history to inherit.
        private static bool IsBrandNewGame()
        {
            try
            {
                var tod = GameManager.GetTimeOfDayComponent();
                return tod != null && tod.GetHoursPlayedNotPaused() < 0.05f;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // IS AN EXISTING MARKER STILL AN ANSWER?
        //
        // Normally yes - that is the whole point of the marker, and re-importing over
        // the mod's own data would be far worse than not importing at all.
        //
        // The exception is a marker that says "new-game". Older builds wrote that at
        // minute zero of a playthrough and treated it as final, which is wrong twice
        // over:
        //
        //   * within one playthrough, the player can walk into an ORIGINAL interior
        //     scene afterwards (the spawn point put them there, or the mod's own
        //     fall-back portal did) and fill it with real history that then had to be
        //     imported - and never was;
        //
        //   * across playthroughs, the game hands the slot name of a deleted save to the
        //     next new game, and the mod's files are not deleted with the save. A
        //     "new-game" marker from the previous occupant of "sandbox30" answered for
        //     its replacement.
        //
        // So a "new-game" marker is honoured only while the game really is still new.
        // Once the clock has moved on it is deleted and the question asked properly.
        // Every other result stays final.
        private static bool ShouldHonourMarker(SeamlessInteriorInstance instance, string markerPath)
        {
            string result = ReadLegacyMarkerResult(markerPath);

            if (result == "new-game" && !IsBrandNewGame())
            {
                MelonLogger.Msg($"[LEGACY] {instance.Config.ResolvedInstanceId}: 'yeni oyun' isareti bulundu " +
                                $"ama oyun artik yeni degil - isaret siliniyor, eski kayit yeniden kontrol edilecek.");
                return false;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[LEGACY] {instance.Config.ResolvedInstanceId}: karar zaten verilmis " +
                                $"(isaret='{result ?? "?"}'), aktarim atlaniyor.");
            return true;
        }

        // The marker is the mod's own one-line JSON, so the value is pulled out by hand
        // rather than deserialized - a malformed file must not throw inside Run().
        private static string ReadLegacyMarkerResult(string markerPath)
        {
            try { return ExtractString(File.ReadAllText(markerPath), "\"result\":\"", "\""); }
            catch { return null; }
        }

        // For the F6 dump: the marker's verdict and when it was written. The timestamp
        // matters - a marker older than the save it is answering for is the signature of
        // a recycled slot name.
        private static string DescribeLegacyMarker(SeamlessInteriorInstance instance)
        {
            string path = GetLegacyMarkerPath(instance);
            if (path == null) return "(yol yok)";

            try
            {
                if (!File.Exists(path)) return "(yok)";
                return $"'{ReadLegacyMarkerResult(path) ?? "?"}' " +
                       $"({File.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss})";
            }
            catch { return "(okunamadi)"; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SOURCE RESOLUTION
        //
        // Several buildings can share one interior scene (LakeCabinA_1/2/3 all use the
        // "LakeCabinA" scene). The game keeps them apart by storing INSTANCED scene
        // saves addressed by the entrance door's LoadScene GUID, so that is tried
        // first. The plain scene name is only accepted when this region has exactly ONE
        // building using that scene - otherwise three identical cabins would all import
        // the same data.
        // ─────────────────────────────────────────────────────────────────────
        private static bool ResolveLegacySource(SeamlessInteriorInstance instance, out bool ambiguous)
        {
            ambiguous = false;
            string saveName = SaveGameSystem.m_CurrentSaveName;
            string raw;

            // 0) Manual override from the config always wins.
            string over = instance.Config.LegacySceneKeyOverride;
            if (!string.IsNullOrEmpty(over) && TryLoadSlotString(saveName, over, false, out raw))
            {
                instance.LegacySource = LegacySourceKind.Name;
                instance.LegacySourceId = over;
                instance.LegacyRawBlob = raw;
                return true;
            }

            string baseName = instance.Config.InteriorSceneBaseName;

            // 1) The entrance door's GUID (handles instanced scenes exactly).
            //
            // The game stores an instanced scene under the key "<SceneName>_<door GUID>"
            // - confirmed against a real save's key list. The plain key name is tried
            // first because it needs no guid->file map to be intact, with the dedicated
            // guid lookup as a second chance.
            string doorGuid = FindLegacyDoorGuid(instance);
            if (!string.IsNullOrEmpty(doorGuid))
            {
                string instancedKey = baseName + "_" + doorGuid;

                if (TryLoadSlotString(saveName, instancedKey, false, out raw))
                {
                    instance.LegacySource = LegacySourceKind.Name;
                    instance.LegacySourceId = instancedKey;
                    instance.LegacyRawBlob = raw;
                    return true;
                }

                if (TryLoadSlotString(saveName, doorGuid, true, out raw))
                {
                    instance.LegacySource = LegacySourceKind.Guid;
                    instance.LegacySourceId = doorGuid;
                    instance.LegacyRawBlob = raw;
                    return true;
                }
            }

            // 2) LAST UNCLAIMED INSTANCED KEY.
            //
            // Door resolution can fail for one building out of a set - the trigger may be
            // a loose object on the map rather than a child of the shell, or sit further
            // from the building than the search radius. When every OTHER building sharing
            // this scene has already claimed its key and exactly one key and one building
            // are left over, the pairing is forced and there is nothing to guess.
            string leftover = FindLastUnclaimedInstancedKey(instance, baseName);
            if (leftover != null && TryLoadSlotString(saveName, leftover, false, out raw))
            {
                MelonLogger.Msg($"[LEGACY] {instance.Config.ResolvedInstanceId}: kapi GUID'i bulunamadi, " +
                                $"geriye kalan tek anahtar '{leftover}' eslendi.");
                instance.LegacySource = LegacySourceKind.Name;
                instance.LegacySourceId = leftover;
                instance.LegacyRawBlob = raw;
                return true;
            }

            // 3) The plain scene name - only when it cannot be ambiguous.
            if (CountInstancesSharingScene(instance) > 1)
            {
                ambiguous = true;
                return false;
            }

            if (TryLoadSlotString(saveName, baseName, false, out raw))
            {
                instance.LegacySource = LegacySourceKind.Name;
                instance.LegacySourceId = baseName;
                instance.LegacyRawBlob = raw;
                return true;
            }

            return false;
        }

        // When exactly one instanced key and one building are left unpaired, that pairing
        // is the only one possible. Returns null in every other case - a guess between
        // two candidates would put one house's belongings in another.
        private static string FindLastUnclaimedInstancedKey(SeamlessInteriorInstance instance, string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) return null;

            Il2CppSystem.Collections.Generic.List<string> keys;
            try { keys = SaveGameSlots.GetSceneKeysForCurrentSaveSlot(); }
            catch { return null; }
            if (keys == null) return null;

            string prefix = baseName + "_";

            // Keys this scene owns, minus the ones other buildings already took.
            var unclaimed = new List<string>();
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                if (string.IsNullOrEmpty(k) || !k.StartsWith(prefix, System.StringComparison.Ordinal)) continue;

                bool taken = false;
                foreach (var other in ActiveInteriors.Values)
                {
                    if (other == instance) continue;
                    if (other.LegacySourceId == k) { taken = true; break; }

                    // A sibling resolved by the dedicated guid lookup stores the bare
                    // guid, so compare against the key's guid part too.
                    if (other.LegacySource == LegacySourceKind.Guid
                        && !string.IsNullOrEmpty(other.LegacySourceId)
                        && k.EndsWith("_" + other.LegacySourceId, System.StringComparison.Ordinal))
                    {
                        taken = true;
                        break;
                    }
                }

                if (!taken) unclaimed.Add(k);
            }

            if (unclaimed.Count != 1) return null;

            // And exactly one building still has to be paired: this one.
            int unresolvedSiblings = 0;
            foreach (var cfg in SupportedInteriors)
            {
                if (cfg.ExteriorSceneName != instance.Config.ExteriorSceneName) continue;
                if (cfg.InteriorSceneBaseName != baseName) continue;
                if (cfg.ResolvedInstanceId == instance.Config.ResolvedInstanceId) continue;

                SeamlessInteriorInstance other;
                if (!ActiveInteriors.TryGetValue(cfg.ResolvedInstanceId, out other) || other == null)
                {
                    unresolvedSiblings++;
                    continue;
                }

                // Still Unknown means it has not been probed yet - its claim is unknown,
                // so nothing can be concluded from what is left over.
                if (other.LegacyImport == LegacyImportState.Unknown) unresolvedSiblings++;
                else if (other.LegacySource == LegacySourceKind.None
                         && other.LegacyImport == LegacyImportState.NotNeeded) { /* settled: never visited */ }
                else if (string.IsNullOrEmpty(other.LegacySourceId)) unresolvedSiblings++;
            }

            return unresolvedSiblings == 0 ? unclaimed[0] : null;
        }

        private static int CountInstancesSharingScene(SeamlessInteriorInstance instance)
        {
            int count = 0;
            foreach (var cfg in SupportedInteriors)
            {
                if (cfg.ExteriorSceneName != instance.Config.ExteriorSceneName) continue;
                if (cfg.InteriorSceneBaseName != instance.Config.InteriorSceneBaseName) continue;
                count++;
            }
            return count;
        }

        // Finds the exterior door that WOULD have loaded this interior in the vanilla
        // game, and returns its LoadScene GUID.
        //
        // The doors inside a clone are skipped: their m_SceneToLoad points back at the
        // region, not at the interior.
        private static string FindLegacyDoorGuid(SeamlessInteriorInstance instance)
        {
            string wanted = instance.Config.InteriorSceneBaseName;
            if (string.IsNullOrEmpty(wanted)) return null;

            Il2CppSystem.Collections.Generic.List<LoadScene> doors = null;
            try { doors = LoadScene.m_LoadScenesList; }
            catch { }
            if (doors == null) return null;

            string best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < doors.Count; i++)
            {
                LoadScene d = doors[i];
                if (d == null || d.gameObject == null) continue;
                if (d.m_SceneToLoad != wanted) continue;
                if (string.IsNullOrEmpty(d.m_GUID)) continue;

                // A door that already lives inside a clone is the mod's own copy of the
                // interior-side door; it never addressed a vanilla scene save.
                if (IsUnderAnyMasterInterior(d.transform)) continue;

                float dist = Vector3.Distance(d.transform.position, instance.Config.FallbackPosition);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = d.m_GUID;
                }
            }

            // A door more than 60m from the building's own position is somebody else's.
            if (best != null && bestDist > 60f) return null;
            return best;
        }

        // Reads one entry out of the current save slot as a raw string.
        //
        // The game stores scene data either as a JSON string or as the serialized
        // object itself depending on version, so both are tried. Whichever works, the
        // caller ends up with the JSON text of a SceneSaveGameFormat.
        private static bool TryLoadSlotString(string saveName, string id, bool byGuid, out string raw)
        {
            raw = null;
            if (string.IsNullOrEmpty(saveName) || string.IsNullOrEmpty(id)) return false;

            try
            {
                string asString;
                bool ok = byGuid
                    ? SaveGameSlots.TryLoadDataFromSlotUsingGuid<string>(saveName, id, out asString)
                    : SaveGameSlots.TryLoadDataFromSlot<string>(saveName, id, out asString);

                // The result is only trusted when it actually looks like scene save data.
                // Asking for a string back can succeed and still hand over something
                // useless, depending on how the slot stored the entry.
                if (ok && LooksLikeSceneSave(asString))
                {
                    raw = asString;
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[LEGACY] Slot okuma (string) basarisiz: id={id} byGuid={byGuid} - {ex.Message}");
            }

            // Fallback: the entry is stored as the object, not as text.
            try
            {
                SceneSaveGameFormat asObject;
                bool ok = byGuid
                    ? SaveGameSlots.TryLoadDataFromSlotUsingGuid<SceneSaveGameFormat>(saveName, id, out asObject)
                    : SaveGameSlots.TryLoadDataFromSlot<SceneSaveGameFormat>(saveName, id, out asObject);

                if (ok && asObject != null)
                {
                    raw = Utils.SerializeObject(asObject);
                    return !string.IsNullOrEmpty(raw);
                }
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[LEGACY] Slot okuma (obje) basarisiz: id={id} byGuid={byGuid} - {ex.Message}");
            }

            return false;
        }

        // A scene save is a JSON object carrying the SceneSaveGameFormat fields. This is
        // deliberately loose - the exact field set varies between game versions - but
        // strict enough to reject a value that is not scene data at all.
        private static bool LooksLikeSceneSave(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length || text[i] != '{') return false;

            return text.IndexOf("\"m_", System.StringComparison.Ordinal) >= 0;
        }

        private static bool TryReadLegacyBlob(SeamlessInteriorInstance instance, out string raw)
        {
            // The probe already read it; re-reading would parse the same data twice.
            raw = instance.LegacyRawBlob;
            if (!string.IsNullOrEmpty(raw)) return true;

            if (instance.LegacySource == LegacySourceKind.None || string.IsNullOrEmpty(instance.LegacySourceId))
                return false;

            return TryLoadSlotString(
                SaveGameSystem.m_CurrentSaveName,
                instance.LegacySourceId,
                instance.LegacySource == LegacySourceKind.Guid,
                out raw);
        }

        // ─────────────────────────────────────────────────────────────────────
        // TRANSCODE - vanilla scene save -> the mod's own JSON files
        //
        // Runs inside the hydration coroutine, so the work is spread over frames: a
        // 1000 day building can hold a few hundred entries and parsing them all in one
        // frame would be a visible hitch.
        // ─────────────────────────────────────────────────────────────────────
        public static IEnumerator TranscodeLegacySaveRoutine(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null)
            {
                if (instance != null) instance.LegacyImport = LegacyImportState.Failed;
                yield break;
            }

            string id = instance.Config.ResolvedInstanceId;

            string raw = null;
            bool read = false;
            try { read = TryReadLegacyBlob(instance, out raw); }
            catch (System.Exception ex) { MelonLogger.Warning($"[LEGACY] {id}: kayit okunamadi - {ex.Message}"); }

            if (!read || string.IsNullOrEmpty(raw))
            {
                FailLegacyImport(instance, "read-failed", "veri bulunmustu ama okunamadi");
                yield break;
            }

            // Keep the untouched original next to the mod's own files. If anything ever
            // goes wrong with the transcode the player's real data is still on disk.
            try
            {
                string backupPath = GetLegacyBackupPath(instance);
                if (backupPath != null) File.WriteAllText(backupPath, raw);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[LEGACY] {id}: yedek yazilamadi - {ex.Message}");
            }

            yield return null;

            SceneSaveGameFormat fmt = null;
            try { fmt = Utils.DeserializeObject<SceneSaveGameFormat>(raw); }
            catch (System.Exception ex) { MelonLogger.Warning($"[LEGACY] {id}: SceneSaveGameFormat cozulemedi - {ex.Message}"); }

            if (fmt == null)
            {
                FailLegacyImport(instance, "parse-failed", "SceneSaveGameFormat cozulemedi");
                yield break;
            }

            // The blob can be hundreds of KB and it has been backed up to disk and parsed
            // into fmt; both the local and the cached reference go now.
            raw = null;
            instance.LegacyRawBlob = null;

            int gearCount = 0, containerCount = 0, stateCount = 0, placeableCount = 0;

            // ─── 1. Placed furniture and decorations ───
            //
            // THIS MUST COME FIRST, AND THE REASON IS NOT OBVIOUS.
            //
            // In The Long Dark a container IS a placeable: lockers, crates and drawers
            // can all be picked up and put down somewhere else. In a base the player has
            // lived in for a thousand days most of them HAVE been moved.
            //
            // The vanilla container records address a container by the position it ended
            // up at. The clone, freshly built from the scene template, still has every
            // container at its original factory position - so matching them before the
            // furniture has been moved compares two completely different layouts and
            // almost everything misses. (Measured on a real save: 5 matched, 68 missed.)
            //
            // Moving the furniture first puts the clone into the layout the save is
            // describing, and the container pass then lines up. It also creates the
            // containers the player crafted, which do not exist in the template at all.
            IEnumerator placeRoutine = ApplyLegacyPlaceablesRoutine(instance, fmt, r => placeableCount = r);
            while (placeRoutine.MoveNext()) yield return placeRoutine.Current;

            if (instance.MasterInterior == null)
            {
                instance.LegacyImport = LegacyImportState.Failed;
                yield break;
            }

            // Create the player's own furniture right now, for the same reason: a crafted
            // container has to EXIST before the container pass can find it.
            try { RestoreSpawnedPlaceables(instance); }
            catch (System.Exception ex) { MelonLogger.Warning($"[LEGACY] {id}: yerlestirilmis obje olusturma hatasi - {ex.Message}"); }
            yield return null;

            // ─── 2. Container contents ───
            try { containerCount = TranscodeContainers(instance, fmt); }
            catch (System.Exception ex) { MelonLogger.Warning($"[LEGACY] {id}: konteyner aktarimi hatasi - {ex.Message}"); }
            yield return null;

            // ─── 3. Loose gear ───
            try { gearCount = TranscodeGear(instance, fmt); }
            catch (System.Exception ex) { MelonLogger.Warning($"[LEGACY] {id}: esya aktarimi hatasi - {ex.Message}"); }
            yield return null;

            // ─── 4. Broken / harvested / opened objects ───
            try { stateCount = TranscodeInteractiveStates(instance, fmt); }
            catch (System.Exception ex) { MelonLogger.Warning($"[LEGACY] {id}: etkilesim durumu aktarimi hatasi - {ex.Message}"); }
            yield return null;

            // ─── 5. Safehouse "clear junk" flag ───
            try { TranscodeJunk(instance, fmt); }
            catch (System.Exception ex) { MelonLogger.Warning($"[LEGACY] {id}: cop durumu aktarimi hatasi - {ex.Message}"); }

            WriteLegacyMarker(instance, "imported", instance.LegacySourceId,
                              gearCount, containerCount, stateCount, placeableCount);
            instance.LegacyImport = LegacyImportState.Done;

            MelonLogger.Msg($"[LEGACY] {id}: eski kayit aktarildi - {gearCount} esya, {containerCount} konteyner, " +
                            $"{stateCount} etkilesim durumu, {placeableCount} yerlestirilmis obje.");
        }

        // The import was promised but could not be delivered.
        //
        // SELF-HEALING: Run() suppressed this building's initial loot roll because the
        // import was going to provide the real contents. That promise is now broken, so
        // the "loot already generated" flag is cleared again. The marker written here
        // stops the import from being retried, so the NEXT region load takes the ordinary
        // first-time path and rolls the building's loot properly.
        //
        // For this session the building is left with the scene template's full candidate
        // set, i.e. too much loot rather than none - the harmless direction to fail in.
        private static void FailLegacyImport(SeamlessInteriorInstance instance, string result, string why)
        {
            string id = instance.Config.ResolvedInstanceId;

            MelonLogger.Warning($"[LEGACY] {id}: {why}. Aktarim iptal edildi.");
            MelonLogger.Warning($"[LEGACY] {id}: bu bina bu oturumda fazla loot ile gorunebilir; " +
                                $"bir sonraki bolge yuklemesinde normal loot akisina donecek. " +
                                $"Yeniden denemek icin SeamlessInteriorsData icindeki '*_{id}_legacy.json' dosyasini silin.");

            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (!string.IsNullOrEmpty(saveName))
            {
                UnityEngine.PlayerPrefs.SetInt(instance.Config.SaveKeyPrefix + saveName, 0);
                UnityEngine.PlayerPrefs.Save();
            }

            WriteLegacyMarker(instance, result, instance.LegacySourceId, 0, 0, 0, 0);
            instance.LegacyImport = LegacyImportState.Failed;
            instance.LegacyRawBlob = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // RANDOM SPAWN CANDIDATES
        //
        // A RandomSpawnObject is not a spawner, it is a FILTER: the scene ships with
        // every candidate object present and enabled, and at runtime the component
        // leaves only as many enabled as the difficulty allows.
        //
        // Because a pre-mod building must not re-roll its loot, Run() suppresses the roll
        // and destroys the filter components - which leaves EVERY candidate enabled. For
        // gear that does not matter, since all loose gear is destroyed and respawned from
        // the imported list anyway. For everything else that is not gear it very much
        // does: the room fills up with objects the player's real save never had.
        //
        // The vanilla save records exactly which object each filter settled on, so that
        // decision can simply be replayed.
        //
        // MUST BE CALLED FROM Run(), BEFORE THE FILTER COMPONENTS ARE DESTROYED - once
        // they are gone there is no way to tell which objects were candidates.
        //
        // Deliberately conservative: without a usable record nothing is touched at all,
        // so a format surprise can only ever leave the old behaviour, never empty a room.
        public static int ApplyLegacyRandomSpawns(SeamlessInteriorInstance instance)
        {
            if (!IsLegacyImportPending(instance)) return 0;
            if (instance.MasterInterior == null) return 0;

            string raw = instance.LegacyRawBlob;
            if (string.IsNullOrEmpty(raw)) return 0;

            // Only this one field is pulled out of the blob: parsing the whole
            // SceneSaveGameFormat here would put the cost of the entire import back into
            // the loading screen, which is exactly what hydration exists to avoid.
            string blob = ExtractNestedJsonField(raw, "m_RandomSpawnObjectManagerSerialized");
            if (string.IsNullOrEmpty(blob)) return 0;

            RandomSpawnObjectSaveList list = null;
            try { list = Utils.DeserializeObject<RandomSpawnObjectSaveList>(blob); }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[LEGACY-RSO] {instance.Config.ResolvedInstanceId}: liste cozulemedi - {ex.Message}");
                return 0;
            }

            if (list == null || list.m_SaveDataList == null || list.m_SaveDataList.Count == 0) return 0;

            // What the player's save says is standing in this building.
            var chosenNames = new List<string>(list.m_SaveDataList.Count);
            var chosenPositions = new List<Vector3>(list.m_SaveDataList.Count);

            for (int i = 0; i < list.m_SaveDataList.Count; i++)
            {
                var sd = list.m_SaveDataList[i];
                if (sd == null || string.IsNullOrEmpty(sd.m_ObjectName)) continue;
                chosenNames.Add(CleanGearName(sd.m_ObjectName));
                chosenPositions.Add(sd.m_Position);
            }

            if (chosenNames.Count == 0) return 0;

            Transform interiorT = instance.MasterInterior.transform;
            float sqrTolerance = RSO_MATCH_RADIUS * RSO_MATCH_RADIUS;

            int spawners = 0, candidates = 0, kept = 0, disabled = 0;

            foreach (var rso in instance.MasterInterior.GetComponentsInChildren<RandomSpawnObject>(true))
            {
                if (rso == null) continue;

                var objects = rso.m_ObjectList;
                if (objects == null || objects.Length == 0) continue;
                spawners++;

                for (int i = 0; i < objects.Length; i++)
                {
                    GameObject go = objects[i];
                    if (go == null) continue;
                    candidates++;

                    Vector3 localPos = interiorT.InverseTransformPoint(go.transform.position);
                    string name = CleanGearName(go.name);

                    bool wasChosen = false;
                    for (int c = 0; c < chosenNames.Count; c++)
                    {
                        if (!string.Equals(chosenNames[c], name, System.StringComparison.Ordinal)) continue;
                        if ((chosenPositions[c] - localPos).sqrMagnitude > sqrTolerance) continue;
                        wasChosen = true;
                        break;
                    }

                    if (wasChosen)
                    {
                        if (!go.activeSelf) go.SetActive(true);
                        kept++;
                    }
                    else
                    {
                        if (go.activeSelf) go.SetActive(false);
                        disabled++;
                    }
                }
            }

            MelonLogger.Msg($"[LEGACY-RSO] {instance.Config.ResolvedInstanceId}: {spawners} filtre, {candidates} aday - " +
                            $"{kept} acik birakildi, {disabled} kapatildi (kayitta {chosenNames.Count} secilmis obje).");

            return kept;
        }

        // How far a candidate may sit from the position recorded for it and still be the
        // same object. The clone is an exact copy of the scene, so this only absorbs
        // float round-tripping.
        private const float RSO_MATCH_RADIUS = 0.35f;

        // Pulls one nested "…Serialized" string out of a SceneSaveGameFormat blob without
        // deserializing the whole thing. The game escapes those payloads, so the value is
        // unescaped on the way out.
        private static string ExtractNestedJsonField(string blob, string fieldName)
        {
            string marker = "\"" + fieldName + "\"";
            int i = blob.IndexOf(marker, System.StringComparison.Ordinal);
            if (i < 0) return null;

            i = blob.IndexOf(':', i + marker.Length);
            if (i < 0) return null;

            int q1 = blob.IndexOf('"', i);
            if (q1 < 0) return null;

            int q2 = FindClosingQuote(blob, q1 + 1);
            if (q2 < 0) return null;

            return UnescapeJson(blob.Substring(q1 + 1, q2 - q1 - 1));
        }

        // ─── 1. Gear ───────────────────────────────────────────────────────────
        //
        // Vanilla: GearSaveList { m_SerializedItems: [ { m_PrefabName, m_SearializedGear } ] }
        // The position and rotation live inside m_SearializedGear as a
        // GearItemSaveDataProxy, already in interior-scene space = clone-local space.
        //
        // The mod's format keeps the serialized payload verbatim in "s", so the item's
        // full internal state (condition, decay, food/liquid, cooking pot contents)
        // survives untouched - exactly as it would have in the original scene.
        private static int TranscodeGear(SeamlessInteriorInstance instance, SceneSaveGameFormat fmt)
        {
            string blob = fmt.m_GearManagerSerialized;
            if (string.IsNullOrEmpty(blob)) return 0;

            GearSaveList list = null;
            try { list = Utils.DeserializeObject<GearSaveList>(blob); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[LEGACY-GEAR] GearSaveList cozulemedi: {ex.Message}");
                return 0;
            }

            if (list == null || list.m_SerializedItems == null) return 0;

            var entries = new List<string>();

            for (int i = 0; i < list.m_SerializedItems.Count; i++)
            {
                GearSaveData gsd = list.m_SerializedItems[i];
                if (gsd == null) continue;

                string prefabName = gsd.m_PrefabName;
                string serialized = gsd.m_SearializedGear;
                if (string.IsNullOrEmpty(prefabName) || string.IsNullOrEmpty(serialized)) continue;

                GearItemSaveDataProxy proxy = null;
                try { proxy = Utils.DeserializeObject<GearItemSaveDataProxy>(serialized); }
                catch { }
                if (proxy == null) continue;

                Vector3 pos = proxy.m_Position;
                Quaternion rot = proxy.m_Rotation;

                // A degenerate quaternion would make the item vanish; identity is safer.
                if (rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f) rot = Quaternion.identity;

                // The guid keeps the item's identity stable across later save/loads,
                // exactly as the mod's own save path expects.
                string guid = ExtractGuidFromSerialized(proxy.m_ObjectGuidSerialized);
                if (string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(proxy.m_ObjectGuidSerialized))
                {
                    // Some versions store the bare guid rather than a wrapper object.
                    string bare = proxy.m_ObjectGuidSerialized.Trim();
                    if (bare.Length > 0 && bare[0] != '{' && bare[0] != '[') guid = bare.Trim('"');
                }

                // SCALE IS DELIBERATELY WRITTEN AS ZERO, MEANING "DO NOT TOUCH IT".
                //
                // The vanilla gear record has no scale field at all, and writing 1 in its
                // place is not a harmless default: the game parents some items to a place
                // point on a shelf or workbench, and those anchors carry their own scale.
                // Forcing localScale to 1 threw that away and the item ballooned - a
                // hacksaw the height of the player, a bandage bigger than the player.
                //
                // The restore skips the assignment for a zero scale, leaving whatever the
                // game's own deserialize produced, which is by definition correct.
                entries.Add(
                    $"{{\"name\":\"{EscapeJson(prefabName)}\",\"guid\":\"{EscapeJson(guid ?? "")}\"," +
                    $"\"px\":{pos.x:R},\"py\":{pos.y:R},\"pz\":{pos.z:R}," +
                    $"\"rx\":{rot.x:R},\"ry\":{rot.y:R},\"rz\":{rot.z:R},\"rw\":{rot.w:R}," +
                    $"\"sx\":0,\"sy\":0,\"sz\":0,\"a\":true," +
                    $"\"s\":\"{EscapeJson(serialized)}\"}}");
            }

            string path = GetInactiveSceneGearSavePath(instance);
            if (path == null) return 0;

            // NOTE: the file is written even when it ends up empty. An empty gear file
            // is meaningful - it tells RestoreInactiveSceneGearItems to clear the raw
            // template loot out of a building the player already stripped bare.
            JsonWriteCache.Write(path, "[\n" + string.Join(",\n", entries) + "\n]");
            return entries.Count;
        }

        // ─── 2. Containers ─────────────────────────────────────────────────────
        //
        // Vanilla addresses a container by GUID or by its world position in the
        // original scene. The mod addresses it by "<name>###<index>", where the index
        // counts equal names in GetComponentsInChildren order.
        //
        // So the vanilla entries are matched against the clone's own containers - by
        // guid where possible, by local position otherwise - and rewritten under the
        // mod's key.
        private static int TranscodeContainers(SeamlessInteriorInstance instance, SceneSaveGameFormat fmt)
        {
            string blob = fmt.m_ContainerManagerSerialized;
            if (string.IsNullOrEmpty(blob)) return 0;

            ContainerSaveList list = null;
            try { list = Utils.DeserializeObject<ContainerSaveList>(blob); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[LEGACY-CONTAINER] ContainerSaveList cozulemedi: {ex.Message}");
                return 0;
            }

            if (list == null || list.m_SerializedContainers == null) return 0;

            Transform interiorT = instance.MasterInterior.transform;

            // Build the clone's container table in exactly the order SaveContainerData
            // and RestoreContainerData use, so the keys line up.
            var containers = instance.MasterInterior.GetComponentsInChildren<Container>(true);
            var nameCounter = new Dictionary<string, int>();
            var keys = new string[containers.Length];
            var localPositions = new Vector3[containers.Length];
            var guids = new string[containers.Length];
            var claimed = new bool[containers.Length];

            for (int i = 0; i < containers.Length; i++)
            {
                var c = containers[i];
                if (c == null) { keys[i] = null; continue; }

                string cname = c.gameObject.name;
                int idx;
                if (!nameCounter.TryGetValue(cname, out idx)) idx = 0;
                nameCounter[cname] = idx + 1;

                keys[i] = $"{cname}###{idx}";
                localPositions[i] = interiorT.InverseTransformPoint(c.transform.position);

                var guidComp = c.GetComponent<ObjectGuid>();
                string g = (guidComp != null) ? guidComp.m_Guid : null;
                // CloneContainerGuidFixPatch appends "_CLONE" so the clone's containers
                // do not mirror the originals; strip it back off for matching.
                if (!string.IsNullOrEmpty(g) && g.EndsWith("_CLONE"))
                    g = g.Substring(0, g.Length - "_CLONE".Length);
                guids[i] = g;
            }

            // Clean object names, for the prefab-name fallback below.
            var names = new string[containers.Length];
            for (int i = 0; i < containers.Length; i++)
                names[i] = (containers[i] != null) ? CleanGearName(containers[i].gameObject.name) : null;

            var entries = new List<string>();
            int byGuid = 0, byPos = 0, byName = 0, missed = 0;

            for (int e = 0; e < list.m_SerializedContainers.Count; e++)
            {
                ContainerSaveData csd = list.m_SerializedContainers[e];
                if (csd == null) continue;
                if (string.IsNullOrEmpty(csd.m_SearializedContainer)) continue;

                int target = -1;

                // 1) Guid: exact and position independent.
                if (!string.IsNullOrEmpty(csd.m_Guid))
                {
                    for (int i = 0; i < containers.Length; i++)
                    {
                        if (claimed[i] || keys[i] == null) continue;
                        if (guids[i] == csd.m_Guid) { target = i; break; }
                    }
                    if (target >= 0) byGuid++;
                }

                // 2) Position. By now the furniture has been moved into the layout the
                //    save describes (see the ordering note in TranscodeLegacySaveRoutine),
                //    so this is an almost exact comparison.
                if (target < 0)
                {
                    float bestSqr = CONTAINER_MATCH_RADIUS * CONTAINER_MATCH_RADIUS;
                    for (int i = 0; i < containers.Length; i++)
                    {
                        if (claimed[i] || keys[i] == null) continue;
                        float sqr = (localPositions[i] - csd.m_Position).sqrMagnitude;
                        if (sqr < bestSqr) { bestSqr = sqr; target = i; }
                    }
                    if (target >= 0) byPos++;
                }

                // 3) Same prefab, nearest one. Last resort for a container whose exact
                //    position could not be reproduced - putting its contents in the same
                //    kind of container a metre away is far better than losing them.
                if (target < 0 && !string.IsNullOrEmpty(csd.m_PrefabName))
                {
                    string wanted = CleanGearName(csd.m_PrefabName);
                    float bestSqr = CONTAINER_NAME_MATCH_RADIUS * CONTAINER_NAME_MATCH_RADIUS;
                    for (int i = 0; i < containers.Length; i++)
                    {
                        if (claimed[i] || keys[i] == null) continue;
                        if (!string.Equals(names[i], wanted, System.StringComparison.Ordinal)) continue;

                        float sqr = (localPositions[i] - csd.m_Position).sqrMagnitude;
                        if (sqr < bestSqr) { bestSqr = sqr; target = i; }
                    }
                    if (target >= 0) byName++;
                }

                if (target < 0)
                {
                    // The first few misses are reported at normal log level, together with
                    // the closest thing the clone does have. Without that pairing a
                    // mismatch is impossible to diagnose from a log - the numbers alone
                    // never say whether the positions, the names or the guids drifted.
                    if (missed < CONTAINER_MISS_SAMPLES)
                    {
                        int nearest = -1;
                        float nearestSqr = float.MaxValue;
                        for (int i = 0; i < containers.Length; i++)
                        {
                            if (keys[i] == null) continue;
                            float sqr = (localPositions[i] - csd.m_Position).sqrMagnitude;
                            if (sqr < nearestSqr) { nearestSqr = sqr; nearest = i; }
                        }

                        string near = (nearest >= 0)
                            ? $"en yakin klon konteyneri='{names[nearest]}' mesafe={Mathf.Sqrt(nearestSqr):F2}m guid='{guids[nearest] ?? "-"}'"
                            : "klonda hic konteyner yok";

                        MelonLogger.Msg($"[LEGACY-CONTAINER] ESLESMEDI prefab='{csd.m_PrefabName}' guid='{csd.m_Guid}' " +
                                        $"pos={csd.m_Position} | {near}");
                    }

                    missed++;
                    continue;
                }

                claimed[target] = true;

                // Same shape SaveContainerData writes: the position is the identity the
                // restore matches on, the key is only kept for older files.
                Vector3 lp = localPositions[target];
                entries.Add($"{{\"key\":\"{EscapeJson(keys[target])}\",\"n\":\"{EscapeJson(names[target] ?? "")}\"," +
                            $"\"px\":{lp.x:R},\"py\":{lp.y:R},\"pz\":{lp.z:R}," +
                            $"\"data\":\"{EscapeJson(csd.m_SearializedContainer)}\"}}");
            }

            int matched = byGuid + byPos + byName;
            MelonLogger.Msg($"[LEGACY-CONTAINER] {instance.Config.ResolvedInstanceId}: {matched}/{list.m_SerializedContainers.Count} konteyner eslesti " +
                            $"(guid={byGuid}, pozisyon={byPos}, isim={byName}), {missed} eslesmedi. " +
                            $"Klonda {containers.Length} konteyner var.");

            // When most of them missed, the numbers alone cannot say why. Showing what
            // the clone actually holds next to what the save expected is what turns
            // "68 missed" into an answer.
            if (missed > matched && containers.Length > 0)
            {
                int shown = 0;
                for (int i = 0; i < containers.Length && shown < CONTAINER_MISS_SAMPLES; i++)
                {
                    if (keys[i] == null) continue;
                    MelonLogger.Msg($"[LEGACY-CONTAINER] KLONDA: '{names[i]}' local={localPositions[i]} guid='{guids[i] ?? "-"}'");
                    shown++;
                }
            }

            string path = GetContainerSavePath(instance);
            if (path == null) return 0;

            JsonWriteCache.Write(path, "[\n" + string.Join(",\n", entries) + "\n]");
            return matched;
        }

        // How close a vanilla container position has to be to a clone container's local
        // position to count as the same object. The furniture has already been moved into
        // the saved layout, so this only has to absorb float round-tripping.
        private const float CONTAINER_MATCH_RADIUS = 0.25f;

        // Radius for the "same prefab, nearest one" fallback. Generous on purpose: it is
        // only ever reached when neither the guid nor the exact position matched.
        private const float CONTAINER_NAME_MATCH_RADIUS = 3.0f;

        // How many unmatched containers to describe in the log before going quiet.
        private const int CONTAINER_MISS_SAMPLES = 6;

        // ─── 3. Interaction state ──────────────────────────────────────────────
        //
        // Harvestables, open/closers and broken-down objects. Locks and smashables need
        // no work of their own: the game nests them inside the OpenClose and GearItem
        // payloads, so they come along for free.
        //
        // The mod's key scheme is "<TAG>|g:<guid>", built by BuildStateKey from the
        // guid inside the serialized payload. The vanilla proxies carry the same guid
        // (the clone inherits the scene template's ObjectGuid values), so the key can be
        // written directly without touching the clone at all.
        private static int TranscodeInteractiveStates(SeamlessInteriorInstance instance, SceneSaveGameFormat fmt)
        {
            var entries = new List<string>();
            var usedKeys = new HashSet<string>();

            int n = 0;
            n += CollectLegacyStates(fmt.m_BreakDownObjectsSerialized, TAG_BREAKDOWN, instance, entries, usedKeys);
            n += CollectLegacyStates(fmt.m_HarvestablesSerialized, TAG_HARVESTABLE, instance, entries, usedKeys);
            n += CollectLegacyStates(fmt.m_OpenClosersSerialized, TAG_OPENCLOSE, instance, entries, usedKeys);

            string path = GetInteractiveStateSavePath(instance);
            if (path == null) return 0;

            JsonWriteCache.Write(path, "[\n" + string.Join(",\n", entries) + "\n]");
            return n;
        }

        private static int CollectLegacyStates(string blob, string typeTag, SeamlessInteriorInstance instance,
                                               List<string> entries, HashSet<string> usedKeys)
        {
            if (string.IsNullOrEmpty(blob)) return 0;

            var elements = SplitJsonArray(blob);
            int count = 0;

            foreach (var rawElement in elements)
            {
                string element = UnwrapJsonStringElement(rawElement);
                if (string.IsNullOrEmpty(element)) continue;

                string guid = ExtractGuidFromSerialized(element);
                if (string.IsNullOrEmpty(guid))
                {
                    // Without a guid the mod's fallback key needs the object's NAME,
                    // which the vanilla proxy does not carry. Skipping is the only safe
                    // choice: an ambiguous entry applied to the wrong object is worse
                    // than a missing one (BuildStateKey makes the same trade-off).
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[LEGACY-STATE] {typeTag}: guid'siz kayit atlandi.");
                    continue;
                }

                string key = $"{typeTag}|g:{guid}";
                if (!usedKeys.Add(key)) continue;

                entries.Add($"{{\"k\":\"{EscapeJson(key)}\",\"d\":\"{EscapeJson(element)}\"}}");
                count++;
            }

            return count;
        }

        // ─── 4. Junk ───────────────────────────────────────────────────────────
        private static void TranscodeJunk(SeamlessInteriorInstance instance, SceneSaveGameFormat fmt)
        {
            bool cleared = false;
            try { cleared = fmt.m_JunkManagedSerialized.m_ClearedJunk; }
            catch { }

            string path = GetJunkStateSavePath(instance);
            if (path == null) return;

            JsonWriteCache.Write(path, $"{{\"cleared\":{(cleared ? "true" : "false")}}}");
        }

        // ─── 5. Placed furniture and decorations ───────────────────────────────
        //
        // The vanilla placement list holds two very different kinds of thing, and they
        // need opposite treatment:
        //
        //   a) SCENE FURNITURE THE PLAYER MOVED. The object exists in the clone already
        //      (same guid, straight from the scene template), so only its transform has
        //      to be corrected - in place, right now. It must NOT be handed to the
        //      game's placement system: the clone's template placeables are deliberately
        //      invalidated so the vanilla save never adopts them.
        //
        //   b) FURNITURE THE PLAYER PUT DOWN. There is nothing in the clone to match, so
        //      the object has to be re-created. Those entries are written into the mod's
        //      own _spawn_placeables.json, which is re-applied on every load from then on
        //      (see SaveSpawnedPlaceables / RestoreSpawnedPlaceables). Handing them to
        //      the vanilla registry instead would not survive: PlaceableManager.Add is
        //      blocked while any building is still cloning.
        //
        // The vanilla positions are already in interior-scene space, which IS the mod's
        // clone-local space, so the spawn file can be written without any conversion.
        private static IEnumerator ApplyLegacyPlaceablesRoutine(SeamlessInteriorInstance instance,
                                                                SceneSaveGameFormat fmt,
                                                                System.Action<int> onDone)
        {
            int applied = 0;

            var list = fmt.m_PlacementListSerialized;
            if (list == null || list.Count == 0 || instance.MasterInterior == null)
            {
                if (onDone != null) onDone(applied);
                yield break;
            }

            Transform interiorT = instance.MasterInterior.transform;

            // Guid -> the clone's own copy of a template placeable.
            var existing = new Dictionary<string, Il2CppTLD.Placement.Placeable>();
            foreach (var p in InteriorScan.Placeables(instance.MasterInterior))
            {
                if (p == null || p.gameObject == null || string.IsNullOrEmpty(p.m_Guid)) continue;
                if (!existing.ContainsKey(p.m_Guid)) existing[p.m_Guid] = p;
            }

            var spawnEntries = new List<string>();
            int movedCount = 0, spawnCount = 0, removedCount = 0, missingRemovals = 0;

            // Census of what the vanilla list contains, for the breakdown logged below.
            int stActive = 0, stRemoved = 0, stOnPlayer = 0, stInactive = 0, stUnregistered = 0;
            int withPayload = 0, guidHits = 0;


            for (int i = 0; i < list.Count; i++)
            {
                Il2CppTLD.Placement.PlaceableInfoSaveData info = null;
                try { info = list[i]; }
                catch { }

                // NOTE: only the guid is required here.
                //
                // A "removed" record carries NOTHING ELSE - no name, no position, no
                // payload at all; m_Serialized is null. It is a tombstone, and the guid is
                // the whole of its content. Demanding a payload up front (as this loop
                // originally did) silently threw away every one of them - 406 of the 849
                // records in a real thousand-day save - which is precisely why the
                // imported building looked like the player's belongings dropped into an
                // untouched room.
                if (info == null || string.IsNullOrEmpty(info.m_Guid)) continue;

                try
                {
                    var state = info.m_State;

                    Il2CppTLD.Placement.Placeable target;
                    bool inClone = existing.TryGetValue(info.m_Guid, out target) && target != null;

                    // Census only - no behaviour depends on these.
                    switch (state)
                    {
                        case Il2CppTLD.Placement.PlacementState.Active: stActive++; break;
                        case Il2CppTLD.Placement.PlacementState.Removed: stRemoved++; break;
                        case Il2CppTLD.Placement.PlacementState.OnPlayer: stOnPlayer++; break;
                        case Il2CppTLD.Placement.PlacementState.Inactive: stInactive++; break;
                        case Il2CppTLD.Placement.PlacementState.Unregistered: stUnregistered++; break;
                    }
                    if (info.m_Serialized != null) withPayload++;
                    if (inClone) guidHits++;

                    // NEVER touch something the player is holding or carrying. A guid can
                    // resolve to an object that has since ended up on the player, and
                    // moving or disabling that would take an item out of their hands.
                    if (inClone && IsPlayerOrInventory(target.transform)) continue;

                    // ─── (c) THINGS THAT ARE NO LONGER IN THE BUILDING ───
                    //
                    // The player picked this chair up, carried it away, or broke it down.
                    // The clone was built from the untouched scene template, so it still
                    // HAS that chair. The template copy has to be switched off.
                    if (state == Il2CppTLD.Placement.PlacementState.Removed ||
                        state == Il2CppTLD.Placement.PlacementState.OnPlayer)
                    {
                        if (!IsLegacyRemovedHandlingEnabled) continue;

                        if (inClone)
                        {
                            target.gameObject.SetActive(false);
                            target.m_Invalidated = true;
                            instance.LegacyRemovedGuids.Add(info.m_Guid);
                            removedCount++;

                            if (s_DebugBounds)
                                MelonLogger.Msg($"[LEGACY-PLACEABLE] KALDIRILDI: '{target.gameObject.name}' guid={info.m_Guid}");
                        }
                        else
                        {
                            missingRemovals++;
                        }
                        continue;
                    }

                    var data = info.m_Serialized;
                    if (data == null) continue;

                    if (inClone)
                    {
                        // (a) Template furniture the player moved.
                        target.transform.position = interiorT.TransformPoint(data.m_Position);
                        target.transform.rotation = interiorT.rotation * data.m_Rotation;
                        if (data.m_Scale != Vector3.zero) target.transform.localScale = data.m_Scale;
                        target.gameObject.SetActive(data.m_ActiveSelf);

                        // Stays invalidated: the clone's own furniture is the mod's
                        // business, never the vanilla placement save's.
                        target.m_Invalidated = true;
                        movedCount++;
                        applied++;
                    }
                    else
                    {
                        // (b) Furniture the player put down. Persist it verbatim - the
                        // position is already clone-local, so nothing is converted here.
                        string serialized = Utils.SerializeObject(data);
                        if (!string.IsNullOrEmpty(serialized))
                        {
                            spawnEntries.Add($"{{\"g\":\"{EscapeJson(info.m_Guid)}\",\"d\":\"{EscapeJson(serialized)}\"}}");

                            // Mark it as the player's own from the moment it is imported,
                            // so the passes that follow never mistake it for scene
                            // furniture and switch it off or drop it from the save.
                            instance.SpawnedPlaceableGuids.Add(info.m_Guid);

                            spawnCount++;
                            applied++;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[LEGACY-PLACEABLE] '{info.m_Guid}' uygulanamadi: {ex.Message}");
                }

                if (FrameBudget.Exceeded())
                {
                    yield return null;
                    if (instance.MasterInterior == null) break;
                }
            }

            // Written even when empty: an empty file means "the player placed nothing
            // here", and RestoreSpawnedPlaceables must not resurrect a stale list.
            string spawnPath = GetSpawnedPlaceableSavePath(instance);
            if (spawnPath != null)
                JsonWriteCache.Write(spawnPath, "[\n" + string.Join(",\n", spawnEntries) + "\n]");

            MelonLogger.Msg($"[LEGACY-PLACEABLE] {instance.Config.ResolvedInstanceId}: {movedCount} tasinmis, " +
                            $"{spawnCount} oyuncunun koydugu, {removedCount} kaldirilmis (klonda kapatildi), " +
                            $"{missingRemovals} kaldirilmis ama klonda yok (toplam kayit {list.Count}).");

            // WHAT THE SAVE ACTUALLY HANDED OVER.
            //
            // "0 tasinmis" has two completely different causes and the counts above
            // cannot tell them apart: either the records were there and none of them
            // matched a guid in the clone, or the save carried no transforms in the
            // first place (every record a bare Removed tombstone). Only the second is
            // normal, and only this breakdown says which one happened.
            MelonLogger.Msg($"[LEGACY-PLACEABLE] {instance.Config.ResolvedInstanceId}: kayit dokumu - " +
                            $"Active={stActive} Removed={stRemoved} OnPlayer={stOnPlayer} " +
                            $"Inactive={stInactive} Unregistered={stUnregistered} " +
                            $"| konum tasiyan kayit={withPayload}, klonda guid'i bulunan={guidHits}.");

            // A save with records but no transforms among them is the signature of one
            // specific regression: something refusing the game's own
            // PlaceableManager.Add while the player was inside the original interior, so
            // the save kept every "picked up" and lost every "put down".
            // (see PreventPlaceableAutoRegisterPatch / IsInteriorLoadedAsCloneTemplate)
            if (withPayload == 0 && list.Count > 0)
                MelonLogger.Warning($"[LEGACY-PLACEABLE] {instance.Config.ResolvedInstanceId}: eski kayittaki " +
                                    $"yerlesim listesi {list.Count} kayit iceriyor ama hicbirinde konum yok " +
                                    $"(hepsi 'kaldirildi'). Orijinal sahnedeki yerlestirmeler oyunun kaydina " +
                                    $"hic yazilmamis - mobilya konumlari aktarilamaz.");

            if (onDone != null) onDone(applied);
        }


        // ─── Marker ────────────────────────────────────────────────────────────
        private static void WriteLegacyMarker(SeamlessInteriorInstance instance, string result, string sourceId,
                                              int gear, int containers, int states, int placeables)
        {
            string path = GetLegacyMarkerPath(instance);
            if (path == null) return;

            string json =
                $"{{\"v\":1,\"result\":\"{EscapeJson(result)}\",\"source\":\"{EscapeJson(sourceId ?? "")}\"," +
                $"\"gear\":{gear},\"containers\":{containers},\"states\":{states},\"placeables\":{placeables}}}";

            try { File.WriteAllText(path, json); }
            catch (System.Exception ex) { MelonLogger.Warning($"[LEGACY] Marker yazilamadi: {ex.Message}"); }

            JsonWriteCache.Forget(path);
        }

        // Called by CheckNewGameLootLock: a brand new game must not inherit the previous
        // run's import markers.
        public static void ClearLegacyImportFiles(SeamlessInteriorInstance instance)
        {
            TryDelete(GetLegacyMarkerPath(instance));
            TryDelete(GetLegacyBackupPath(instance));
            instance.LegacyImport = LegacyImportState.Unknown;
            instance.LegacySource = LegacySourceKind.None;
            instance.LegacySourceId = null;
            instance.LegacyRawBlob = null;
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                JsonWriteCache.Forget(path);
            }
            catch { }
        }

        // ─── JSON helpers ──────────────────────────────────────────────────────

        // Splits a top-level JSON array into its element substrings, without
        // deserializing anything. Quote and escape aware, so a payload that itself
        // contains an escaped JSON document (which the game's nested "*Serialized"
        // fields all do) cannot break the split.
        private static List<string> SplitJsonArray(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json)) return result;

            int i = json.IndexOf('[');
            if (i < 0) return result;
            i++;

            int depth = 0;
            bool inString = false;
            int elemStart = -1;

            for (; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    if (depth == 0 && elemStart < 0) elemStart = i;
                    continue;
                }

                if (c == '{' || c == '[')
                {
                    if (depth == 0 && elemStart < 0) elemStart = i;
                    depth++;
                    continue;
                }

                if (c == '}' || c == ']')
                {
                    if (depth == 0)
                    {
                        // The closing bracket of the outer array.
                        if (elemStart >= 0) AddTrimmed(result, json, elemStart, i - elemStart);
                        return result;
                    }

                    depth--;
                    if (depth == 0 && elemStart >= 0)
                    {
                        AddTrimmed(result, json, elemStart, i - elemStart + 1);
                        elemStart = -1;
                    }
                    continue;
                }

                if (c == ',')
                {
                    if (depth == 0 && elemStart >= 0)
                    {
                        AddTrimmed(result, json, elemStart, i - elemStart);
                        elemStart = -1;
                    }
                    continue;
                }

                if (depth == 0 && elemStart < 0 && !char.IsWhiteSpace(c)) elemStart = i;
            }

            if (elemStart >= 0) AddTrimmed(result, json, elemStart, json.Length - elemStart);
            return result;
        }

        private static void AddTrimmed(List<string> target, string src, int start, int length)
        {
            if (length <= 0) return;
            string s = src.Substring(start, length).Trim();
            if (s.Length > 0) target.Add(s);
        }

        // The game writes these aggregate lists either as an array of objects or as an
        // array of already-serialized strings, depending on the type. A string element
        // is unwrapped so the caller always sees the object text itself.
        private static string UnwrapJsonStringElement(string element)
        {
            if (string.IsNullOrEmpty(element) || element[0] != '"') return element;

            int end = FindClosingQuote(element, 1);
            if (end < 0) return element;

            return UnescapeJson(element.Substring(1, end - 1));
        }

        // ─────────────────────────────────────────────────────────────────────
        // F6 DIAGNOSTIC
        //
        // The scene keys a save really contains cannot be guessed from outside the
        // game. This dumps them, plus what the mod resolved for every building, so a
        // mismatch can be spotted and fixed with LegacySceneKeyOverride.
        // ─────────────────────────────────────────────────────────────────────
        public static void DumpLegacySaveDiagnostics()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            MelonLogger.Msg($"[LEGACY-TESHIS] ───── Aktif kayit: '{saveName}' ─────");

            try
            {
                var keys = SaveGameSlots.GetSceneKeysForCurrentSaveSlot();
                if (keys == null)
                {
                    MelonLogger.Msg("[LEGACY-TESHIS] Sahne anahtari listesi alinamadi (null).");
                }
                else
                {
                    MelonLogger.Msg($"[LEGACY-TESHIS] Kayitta {keys.Count} sahne anahtari var:");
                    for (int i = 0; i < keys.Count; i++)
                        MelonLogger.Msg($"[LEGACY-TESHIS]   [{i}] {keys[i]}");
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[LEGACY-TESHIS] Sahne anahtarlari okunamadi: {ex.Message}");
            }

            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null) continue;

                string doorGuid = null;
                try { doorGuid = FindLegacyDoorGuid(instance); } catch { }

                // WHY the state is what it is, not just what it is.
                //
                // "durum=NotNeeded kaynak=None" on its own is unreadable: it is the same
                // line whether the building was never visited, was already imported, or
                // was silenced by a leftover marker from a deleted playthrough. The
                // marker file holds the actual reason, so it is printed with it.
                MelonLogger.Msg($"[LEGACY-TESHIS] {instance.Config.ResolvedInstanceId}: " +
                                $"sahne='{instance.Config.InteriorSceneBaseName}' " +
                                $"durum={instance.LegacyImport} kaynak={instance.LegacySource} " +
                                $"id='{instance.LegacySourceId ?? "-"}' kapiGUID='{doorGuid ?? "-"}' " +
                                $"hidrate={instance.ContentHydrated} isaret={DescribeLegacyMarker(instance)}");
            }

            DumpCarriedWeightDiagnostics();
            DumpOversizedItemDiagnostics();
        }

        // ─── Carried weight ───
        //
        // The backpack listing and the carried-weight readout come from different places:
        // the list shows each item, the readout sums what the game believes is being
        // carried. When they disagree - items adding up to 27kg while the readout says 2 -
        // the interesting values are the override flag and the decoration total.
        private static void DumpCarriedWeightDiagnostics()
        {
            try
            {
                var inv = GameManager.GetInventoryComponent();
                if (inv == null)
                {
                    MelonLogger.Msg("[AGIRLIK-TESHIS] Inventory bulunamadi.");
                    return;
                }

                float total = inv.GetTotalWeightKG().m_Units / 1e9f;
                float decoration = inv.GetTotalDecorationWeight().m_Units / 1e9f;
                float extra = inv.GetExtraWeightKG().m_Units / 1e9f;
                float overrided = inv.m_OverridedWeight.m_Units / 1e9f;

                MelonLogger.Msg($"[AGIRLIK-TESHIS] toplam={total:F2}kg dekorasyon={decoration:F2}kg ekstra={extra:F2}kg " +
                                $"| zorlamaOverride={inv.m_ForceOverrideWeight} overrideDegeri={overrided:F2}kg");

                if (inv.m_ForceOverrideWeight)
                {
                    MelonLogger.Warning("[AGIRLIK-TESHIS] Agirlik override'i ACIK - HUD gercek yuku degil sabit bir degeri gosteriyor.");
                }

                // How many of the player's carried decorations the placement registry
                // still knows about. If the backpack shows furniture but this is 0, the
                // records were lost and their weight cannot be counted.
                int onPlayer = 0;
                var dict = Il2CppTLD.Placement.PlaceableManager.s_Placeables;
                if (dict != null)
                {
                    foreach (var e in dict)
                    {
                        if (e.Value != null && e.Value.m_State == Il2CppTLD.Placement.PlacementState.OnPlayer)
                            onPlayer++;
                    }
                }
                MelonLogger.Msg($"[AGIRLIK-TESHIS] Oyuncunun uzerindeki kayitli yerlestirilebilir esya: {onPlayer}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[AGIRLIK-TESHIS] Okunamadi: {ex.Message}");
            }
        }

        // ─── Oversized items ───
        //
        // Reports anything in the building the player is standing in whose world scale is
        // far from its prefab's, which is what "a bandage bigger than the player" looks
        // like from code.
        private static void DumpOversizedItemDiagnostics()
        {
            var instance = GetInstancePlayerIsIn();
            if (instance == null || instance.MasterInterior == null)
            {
                MelonLogger.Msg("[OLCEK-TESHIS] Oyuncu bir klon sahnenin icinde degil, tarama atlandi.");
                return;
            }

            Transform interiorT = instance.MasterInterior.transform;
            int checked_ = 0, odd = 0;

            foreach (var gear in InteriorScan.Gear(instance.MasterInterior))
            {
                if (gear == null || gear.gameObject == null) continue;

                // Items inside a container are scaled by the game itself so they fit the
                // drawer or box they sit in - a fish at 0.35 in a fridge door is correct,
                // not a bug. Reporting those buries the real cases in noise.
                if (IsInsideContainer(gear.transform, interiorT)) continue;

                checked_++;

                Vector3 s = gear.transform.lossyScale;
                float max = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                float min = Mathf.Min(Mathf.Abs(s.x), Mathf.Min(Mathf.Abs(s.y), Mathf.Abs(s.z)));

                // 1.35 catches a real problem while ignoring the clone's own
                // ScaleAdjustment, which is a few percent.
                if (max <= 1.35f && min >= 0.65f) continue;

                odd++;
                if (odd <= 15)
                {
                    string parent = gear.transform.parent != null ? gear.transform.parent.name : "ROOT";
                    MelonLogger.Msg($"[OLCEK-TESHIS] '{gear.gameObject.name}' local={gear.transform.localScale} " +
                                    $"world={s} parent='{parent}'");
                }
            }

            MelonLogger.Msg($"[OLCEK-TESHIS] {instance.Config.ResolvedInstanceId}: {checked_} esya tarandi, {odd} tanesinin olcegi bozuk.");

            DumpMissingObjectDiagnostics(instance);
        }

        // ─── Why is something in this building not on screen? ───
        //
        // "It is gone" has three completely different causes and they need different
        // fixes, so the report separates them:
        //
        //   the GameObject is switched off        - something decided it should not exist
        //   switched on but every renderer is off - the visibility pass never reached it
        //   neither                               - it is fine, look elsewhere
        //
        // For a switched-off object it also says WHO switched it off, which is the part
        // no amount of staring at the hierarchy can tell you.
        private static void DumpMissingObjectDiagnostics(SeamlessInteriorInstance instance)
        {
            int inactive = 0, invisible = 0, fine = 0;

            // Counted separately, because they mean completely different things.
            //
            // A dark piece of SCENE furniture is usually correct - the player broke it
            // down for wood or cleared it as junk, and the interior is supposed to come
            // back stripped. A dark object the PLAYER PUT THERE is always a bug, and it
            // is the one worth reading about, so those are the ones listed.
            int offTemplate = 0, offSpawned = 0, offBrokenDown = 0;
            int shown = 0;

            foreach (var p in InteriorScan.Placeables(instance.MasterInterior))
            {
                if (p == null || p.gameObject == null) continue;

                bool isOff = !p.gameObject.activeSelf;

                int renderers = 0, enabledRenderers = 0;
                foreach (var r in InteriorScan.Renderers(p.gameObject))
                {
                    if (r == null) continue;
                    renderers++;
                    if (r.enabled) enabledRenderers++;
                }

                bool isDark = !isOff && renderers > 0 && enabledRenderers == 0;

                if (!isOff && !isDark) { fine++; continue; }
                if (isOff) inactive++; else invisible++;

                string guid = p.m_Guid ?? "";
                bool spawned = instance.SpawnedPlaceableGuids.Contains(guid);

                // Did the game itself hide this because the player broke it down? That is
                // the single most common legitimate reason for scene furniture to be dark,
                // and until it is ruled out every count here is unreadable.
                bool brokenDown = false;
                try
                {
                    var bd = p.gameObject.GetComponent<Il2Cpp.BreakDown>();
                    if (bd != null)
                    {
                        string s = bd.Serialize();
                        brokenDown = !string.IsNullOrEmpty(s)
                                     && s.IndexOf("\"m_HasBeenBrokenDown\":true", System.StringComparison.Ordinal) >= 0;
                    }
                }
                catch { }

                if (isOff)
                {
                    if (spawned) offSpawned++;
                    else if (brokenDown) offBrokenDown++;
                    else offTemplate++;
                }

                // Only the player's own objects are worth printing one by one.
                if (!spawned && !isDark) continue;
                if (shown >= 20) continue;
                shown++;

                string why;
                if (instance.LegacyRemovedGuids.Contains(guid))
                    why = "eski kayit 'kaldirilmis' dedigi icin ithalat kapatti";
                else if (spawned)
                    why = "OYUNCUNUN KOYDUGU OBJE - kapali olmamali";
                else if (brokenDown)
                    why = "oyuncu parcalamis (BreakDown), dogru";
                else
                    why = "sablon objesi, kapatani bilmiyoruz";

                MelonLogger.Msg($"[KAYIP-TESHIS] {(isOff ? "KAPALI  " : "GORUNMEZ")} '{p.gameObject.name}' " +
                                $"renderer={enabledRenderers}/{renderers} guid={guid} | {why}");
            }

            MelonLogger.Msg($"[KAYIP-TESHIS] {instance.Config.ResolvedInstanceId}: {inactive} kapali " +
                            $"({offSpawned} oyuncunun koydugu, {offBrokenDown} parcalanmis, {offTemplate} sebebi bilinmeyen sablon), " +
                            $"{invisible} acik ama renderer'i kapali, {fine} saglam. " +
                            $"(ithalatin kapattigi: {instance.LegacyRemovedGuids.Count}, kayitli yerlestirilmis: {instance.SpawnedPlaceableGuids.Count})");

            // The outside world's objects that poke into this building.
            int hideList = instance.ResolvedExternalHiddenObjects != null ? instance.ResolvedExternalHiddenObjects.Count : 0;
            int stillVisible = 0;
            if (instance.ResolvedExternalHiddenObjects != null)
            {
                foreach (var go in instance.ResolvedExternalHiddenObjects)
                    if (go != null && go.activeSelf) stillVisible++;
            }

            MelonLogger.Msg($"[KAYIP-TESHIS] Dis dunya gizleme listesi: {hideList} obje, bunlardan {stillVisible} tanesi hala acik " +
                            $"(klon acik={instance.MasterInterior.activeSelf}).");
        }
    }
}
