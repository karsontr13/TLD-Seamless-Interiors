using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // A brand new game must not inherit the loot lock or the save files of the
        // previous run, so everything belonging to this instance is wiped.
        private void CheckNewGameLootLock(SeamlessInteriorInstance instance)
        {
            // Less than ~3 minutes of played time means this is a fresh game.
            var tod = GameManager.GetTimeOfDayComponent();
            if (tod != null && tod.GetHoursPlayedNotPaused() < 0.05f)
            {
                string keyToReset = instance.Config.SaveKeyPrefix + SaveGameSystem.m_CurrentSaveName;
                UnityEngine.PlayerPrefs.SetInt(keyToReset, 0);

                // CRITICAL: clear the "player is inside" flag AND the saved local position
                // left over from the previous save. Without this the player spawns at the
                // wrong spot inside a clone scene and the wind audio breaks.
                ClearSavedPlayerInsideState();

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

                string statePath = GetInteractiveStateSavePath(instance);
                if (statePath != null && File.Exists(statePath))
                {
                    File.Delete(statePath);
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[NEW GAME] Eski Etkilesim Durumu JSON silindi: {statePath}");
                }

                string junkPath = GetJunkStateSavePath(instance);
                if (junkPath != null && File.Exists(junkPath))
                {
                    File.Delete(junkPath);
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[NEW GAME] Eski Cop (junk) durumu JSON silindi: {junkPath}");
                }
                instance.JunkCleared = false;

                // The pre-mod import marker and its backup belong to the PREVIOUS save
                // that happened to reuse this slot name. Leaving them behind would make
                // the new game look like an already-decided import.
                ClearLegacyImportFiles(instance);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[NEW GAME DETECTED] {instance.Config.InteriorSceneBaseName} loot lock resetlendi! Eşyalar doğacak.");
            }
        }

        // Placeables in a fresh clone must not be registered with the game yet; their
        // GUIDs are collected so the mod can recognise them later.
        private void HandleInitialPlaceables(SeamlessInteriorInstance instance)
        {
            InvalidateInteriorPlaceables(instance.MasterInterior);
            CollectInteriorPlaceableGuids(instance.MasterInterior);
        }

        // Is there a record on disk of what is lying around inside this building?
        //
        // The gear JSON is that record. An EMPTY file counts: it means the player
        // stripped the place bare (the same reasoning is spelled out in
        // RestoreInactiveSceneGearItemsRoutine). A pending pre-mod import counts too -
        // the file does not exist yet, but the game's own save holds this building's
        // real contents and they are about to replace whatever the template provides.
        public static bool HasPersistedLootRecord(SeamlessInteriorInstance instance)
        {
            if (instance == null) return false;

            string gearJsonPath = GetInactiveSceneGearSavePath(instance);
            if (gearJsonPath != null && File.Exists(gearJsonPath)) return true;

            return IsLegacyImportPending(instance);
        }

        // Decides whether this building's loot still has to be generated, then removes
        // duplicated items and duplicated GUIDs.
        private void ProcessSpawnsAndDeduplication(SeamlessInteriorInstance instance)
        {
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            string saveKey = instance.Config.SaveKeyPrefix + currentSaveName;

            // THE FLAG ALONE IS NOT ENOUGH - THE SAVED CONTENTS HAVE TO EXIST TOO.
            //
            // The flag is written for EVERY building in the region the moment its clone is
            // built: PerformInitialLootRoll runs from Run(), for all fifteen of them. The
            // content files are NOT. Lazy hydration only fills a building once the player
            // comes within HYDRATE_DISTANCE, and CanPersistContent deliberately refuses to
            // write anything for a building that was never filled.
            //
            // So for every building that was cloned but never approached before a save, the
            // two disagreed. The flag said "the loot is already in the save, throw the
            // template copy away"; the disk held nothing to put back. The cleanup below
            // emptied the building, and the hydration that came later found no file to
            // restore from and left it that way - permanently. One save/load was enough to
            // strip every building the player had not yet walked past.
            //
            // Containers were unaffected, because the game rolls their contents itself the
            // first time they are searched. That is exactly what it looked like in play:
            // searchable containers in a house with nothing lying around them.
            bool hasSavedContents = HasPersistedLootRecord(instance);
            bool lootFlagSet = UnityEngine.PlayerPrefs.GetInt(saveKey, 0) == 1;
            bool isAlreadyGenerated = lootFlagSet && hasSavedContents;

            if (lootFlagSet && !hasSavedContents)
            {
                // Never demoted to a debug-only line: this is the state that used to eat a
                // building's loot, and it is worth seeing in an ordinary log.
                MelonLogger.Msg($"[LOOT-LOCK] {instance.Config.ResolvedInstanceId}: kayitli icerik dosyasi yok, " +
                                $"'loot uretildi' bayragi yok sayiliyor - sablon lootu korunup yeniden elenecek.");
            }

            if (isAlreadyGenerated)
            {
                // Loot already exists in the save: anything without a GUID is a rogue
                // copy created by the clone itself and would duplicate items.
                var allGearInside = InteriorScan.Gear(instance.MasterInterior);
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
                // Keyed by InstanceId so the same scene produces different GUIDs in
                // different buildings.
                GenerateDeterministicPDIDs(instance.MasterInterior, instance.Config.ResolvedInstanceId);

                if (IsInitialLootRollEnabled)
                {
                    // The flag is NOT written here; PerformInitialLootRoll writes it once
                    // the roll is done. Writing it early caused the filter
                    // (RandomSpawnObject) to be destroyed before it could ever run.
                    instance.PendingInitialLootRoll = true;
                }
                else if (!string.IsNullOrEmpty(currentSaveName))
                {
                    // With the roll disabled the old behaviour is preserved exactly.
                    UnityEngine.PlayerPrefs.SetInt(saveKey, 1);
                    UnityEngine.PlayerPrefs.Save();
                }
            }

            SpatialDeduplication(instance.MasterInterior);

            DestroyDuplicateGuidGear(instance);
        }

        private static void DestroyDuplicateGuidGear(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            var insideGuidComps = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.ObjectGuid>(true);
            if (insideGuidComps.Length == 0) return;

            var insideKeys = new HashSet<string>();
            foreach (var g in insideGuidComps)
            {
                if (g == null) continue;
                string key = g.m_Guid ?? g.PDID;
                if (!string.IsNullOrEmpty(key)) insideKeys.Add(key);
            }
            if (insideKeys.Count == 0) return;

            // Scene-wide, count ONLY our own keys.
            var counts = new Dictionary<string, int>(insideKeys.Count);
            foreach (var guid in SceneScan.GuidsAll())
            {
                if (guid == null) continue;
                string key = guid.m_Guid ?? guid.PDID;
                if (string.IsNullOrEmpty(key) || !insideKeys.Contains(key)) continue;

                int c;
                counts.TryGetValue(key, out c);
                counts[key] = c + 1;
            }

            // Count > 1 means the key exists outside the clone too: drop the clone's copy.
            foreach (var g in insideGuidComps)
            {
                if (g == null || g.gameObject == null) continue;

                string key = g.m_Guid ?? g.PDID;
                if (string.IsNullOrEmpty(key)) continue;

                int c;
                if (!counts.TryGetValue(key, out c) || c <= 1) continue;
                if (g.GetComponent<Il2Cpp.GearItem>() == null) continue;

                UnityEngine.Object.Destroy(g.gameObject);
            }
        }

        public static void PerformInitialLootRoll(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;
            if (!instance.PendingInitialLootRoll) return;

            var allRSO = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.RandomSpawnObject>(true);

            int before = CountActiveGear(instance.MasterInterior);
            int rolled = 0;

            // Totals are collected so the log can show whether the difficulty level is
            // really taking effect.
            int candidateTotal = 0;    // total candidates in m_ObjectList
            int quotaTotal = 0;        // total the game wants for this difficulty
            int preInactiveTotal = 0;  // candidates already disabled before the roll

            // ─── THE SAME BUILDING MUST ROLL THE SAME LOOT EVERY TIME ───
            //
            // A building is only rolled while it has no saved contents, and it keeps no
            // saved contents until the player has been near it and saved. So every load
            // in between rolls it again - and with the global RNG that meant a different
            // cabin every time the player reloaded, which is a reload away from picking
            // the loot you want.
            //
            // Seeding from the playthrough's identity plus the building's id makes the
            // roll a property of THIS building in THIS game: stable across reloads,
            // different in the next playthrough, and different for two buildings sharing
            // one interior scene.
            //
            // The global stream is put back afterwards - the game rolls everything else
            // (weather, wildlife, its own spawns) through it, and leaving it seeded from
            // a fixed value would make all of that repeat too.
            var rngStateBefore = UnityEngine.Random.state;
            UnityEngine.Random.InitState(GetLootRollSeed(instance));

            try
            {
                foreach (var rso in allRSO)
                {
                    if (rso == null) continue;
                    if (RollSingleSpawner(rso, ref candidateTotal, ref quotaTotal, ref preInactiveTotal)) rolled++;
                }
            }
            finally
            {
                UnityEngine.Random.state = rngStateBefore;
            }

            // The per-item spawn chance comes after the filters, as in the game: only an item
            // a RandomSpawnObject kept is ever active, and only an active item rolls.
            int spawnRolled, spawnRemoved;
            PreRollGearSpawnChance(instance, out spawnRolled, out spawnRemoved);

            int after = CountActiveGear(instance.MasterInterior);

            MelonLogger.Msg($"[LOOT-ROLL] {instance.Config.ResolvedInstanceId} (mod={GetCurrentModeName()}): " +
                            $"{allRSO.Length} RandomSpawnObject'in {rolled} tanesi elendi. " +
                            $"Aday={candidateTotal} (elemeden once kapali={preInactiveTotal}), " +
                            $"zorlugun istedigi={quotaTotal}. Dogma sansi: {spawnRolled} esyadan {spawnRemoved} tanesi elendi. " +
                            $"Acik esya: {before} -> {after}.");

            // The loot really has been generated now: only NOW write the flag.
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            if (!string.IsNullOrEmpty(currentSaveName))
            {
                UnityEngine.PlayerPrefs.SetInt(instance.Config.SaveKeyPrefix + currentSaveName, 1);
                UnityEngine.PlayerPrefs.Save();
            }

            instance.PendingInitialLootRoll = false;
        }

        // ─────────────────────────────────────────────────────────────────
        // THE PER-ITEM SPAWN CHANCE, ROLLED BEFORE ANYONE CAN SEE IT
        //
        // RandomSpawnObject is not the only thing that thins out a building's loot. Every
        // GearItem carries its own m_SpawnChance, and the game rolls it in
        // GearItem.ManualStart - the first time GearManager's staggered update reaches the
        // item. That update skips anything that is not isActiveAndEnabled, and a closed
        // clone is inactive, so none of its items roll until the player opens the door. A
        // second or two later the roll lands, and the lantern the player is looking at
        // switches itself off.
        //
        // (Measured, not guessed: the xref cache and a disassembly of GameAssembly.dll,
        // 2026-09-12. ManualStart is RollSpawnChance inlined plus
        // InitializeLastUpdatedTodHours; UpdateItems gates on isActiveAndEnabled.)
        //
        // So the roll is made here, while the clone is still closed, through the game's own
        // GearItem.RollSpawnChance: the same skip rules, the same experience-mode scale, and
        // GameManager.RollSpawnChance seeds from the item's world position, so a building
        // rolls the same way on every load. It sets m_RolledSpawnChance, which is what makes
        // the later ManualStart skip the roll. A miss is SetActive(false) - exactly what an
        // eliminated RandomSpawnObject candidate gets, so the save filter treats both alike.
        //
        // Only the roll is taken over, not ManualStart itself: its other half stamps the
        // item's last-update time, and doing that in the middle of a load would start the
        // item's decay from the wrong hour.
        // ─────────────────────────────────────────────────────────────────
        private static void PreRollGearSpawnChance(SeamlessInteriorInstance instance, out int rolled, out int removed)
        {
            rolled = 0;
            removed = 0;

            Transform rootT = instance.MasterInterior.transform;
            foreach (var gear in InteriorScan.Gear(instance.MasterInterior))
            {
                if (gear == null || gear.gameObject == null) continue;
                if (gear.m_RolledSpawnChance) continue;

                // Only what will actually be standing in the building rolls in the game.
                if (!IsHierarchyActiveUpTo(gear.transform, rootT)) continue;
                if (IsInsideContainer(gear.transform, rootT)) continue;

                try
                {
                    gear.RollSpawnChance();
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[LOOT-ROLL] {instance.Config.ResolvedInstanceId}: '{gear.gameObject.name}' " +
                                        $"dogma sansi atilamadi: {ex.Message}");
                    continue;
                }

                rolled++;
                if (!gear.gameObject.activeSelf) removed++;
            }
        }

        // Which playthrough + which building. The save identity tag is preferred over the
        // save NAME, because the game hands out the lowest free slot name to every new
        // game - "sandbox29" alone would give a deleted playthrough's layout back to its
        // replacement (the whole reason SaveIdentity.cs exists).
        private static int GetLootRollSeed(SeamlessInteriorInstance instance)
        {
            string playthrough = !string.IsNullOrEmpty(s_CurrentSaveTag)
                ? s_CurrentSaveTag
                : (SaveGameSystem.m_CurrentSaveName ?? "");

            return StableHash(playthrough + "/" + instance.Config.ResolvedInstanceId);
        }

        // FNV-1a. NOT string.GetHashCode: .NET randomises that per process, which is the
        // one property a seed meant to survive a restart must not have.
        private static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        private static string GetCurrentModeName()
        {
            try { return Il2Cpp.ExperienceModeManager.GetCurrentExperienceModeType().ToString(); }
            catch { return "bilinmiyor"; }
        }

        private static int CountActiveGear(GameObject interiorRoot)
        {
            if (interiorRoot == null) return 0;

            Transform rootT = interiorRoot.transform;
            int count = 0;
            foreach (var gear in InteriorScan.Gear(interiorRoot))
            {
                if (gear == null || gear.gameObject == null) continue;
                if (IsHierarchyActiveUpTo(gear.transform, rootT)) count++;
            }
            return count;
        }

        private static bool RollSingleSpawner(Il2Cpp.RandomSpawnObject rso,
                                              ref int candidateTotal, ref int quotaTotal, ref int preInactiveTotal)
        {
            try
            {
                var list = rso.m_ObjectList;
                if (list == null || list.Length == 0) return false;

                var candidates = new List<GameObject>();
                var weights = new List<int>();

                // m_Weights is optional; missing entries default to weight 1.
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> rawWeights = null;
                try { rawWeights = rso.m_Weights; } catch { }

                for (int i = 0; i < list.Length; i++)
                {
                    var go = list[i];
                    if (go == null) continue;

                    if (!go.activeSelf) preInactiveTotal++;

                    go.SetActive(false);
                    candidates.Add(go);

                    int w = 1;
                    if (rawWeights != null && i < rawWeights.Length && rawWeights[i] > 0)
                        w = rawWeights[i];
                    weights.Add(w);
                }

                if (candidates.Count == 0) return false;
                candidateTotal += candidates.Count;

                int enableCount = 1;
                try { enableCount = rso.GetNumObjectsToEnableCurrentXPMode(); }
                catch { }

                enableCount = Mathf.Clamp(enableCount, 0, candidates.Count);
                quotaTotal += enableCount;

                // Weighted selection without repetition: each round draws one of the
                // remaining candidates by weight and removes it from the pool.
                for (int picked = 0; picked < enableCount; picked++)
                {
                    int total = 0;
                    for (int i = 0; i < weights.Count; i++) total += weights[i];
                    if (total <= 0) break;

                    int roll = UnityEngine.Random.Range(0, total);
                    int chosen = weights.Count - 1;
                    for (int i = 0; i < weights.Count; i++)
                    {
                        roll -= weights[i];
                        if (roll < 0) { chosen = i; break; }
                    }

                    candidates[chosen].SetActive(true);
                    candidates.RemoveAt(chosen);
                    weights.RemoveAt(chosen);
                }

                return true;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[LOOT-ROLL] Eleme basarisiz: {ex.Message}");
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // SCENE-WIDE CLEANUPS
        //
        // These two cleanups are NOT building specific - they concern the whole scene.
        // Because they are called from Run(), they used to repeat 15 times on a
        // 15-building map, rescanning the scene each time.
        //
        // They now run once per scene, and are forced once more at the end of loading
        // (when all buildings are ready) so the boxes produced by the game's own save
        // restore during loading are caught as well.
        // ─────────────────────────────────────────────────────────────────

        public static int s_SceneGeneration = 0;

        private static int s_LostAndFoundCleanedGeneration = -1;
        private static int s_OrphanPlaceablesCleanedGeneration = -1;

        public static void ResetSceneWideCleanupState()
        {
            s_SceneGeneration++;
            s_LostAndFoundCleanedGeneration = -1;
            s_OrphanPlaceablesCleanedGeneration = -1;
        }

        public static void CleanupLostAndFoundBoxes(bool force = false)
        {
            if (!force && s_LostAndFoundCleanedGeneration == s_SceneGeneration) return;
            s_LostAndFoundCleanedGeneration = s_SceneGeneration;

            var containers = SceneScan.ContainersAll();
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

        // Gives every item in a clone a stable, unique id derived from the building and
        // the item's local position, so the save system can tell copies apart.
        private static void GenerateDeterministicPDIDs(GameObject interiorRoot, string baseName)
        {
            if (interiorRoot == null) return;
            var allGear = InteriorScan.Gear(interiorRoot);

            foreach (var gear in allGear)
            {
                if (gear == null) continue;
                var guidComponent = gear.GetComponent<Il2Cpp.ObjectGuid>();
                if (guidComponent == null) guidComponent = gear.gameObject.AddComponent<Il2Cpp.ObjectGuid>();

                if (string.IsNullOrEmpty(guidComponent.m_Guid))
                {
                    Vector3 localPos = interiorRoot.transform.InverseTransformPoint(gear.transform.position);
                    string cleanName = gear.gameObject.name.Replace("(Clone)", "").Trim();
                    // Built from the building's InstanceId, so the same scene used by
                    // different buildings never clashes.
                    guidComponent.m_Guid = $"{baseName}_{cleanName}_{localPos.x:F2}_{localPos.y:F2}_{localPos.z:F2}";
                }
            }
        }

        // Maximum distance for two items to count as copies of each other (m).
        private const float DEDUP_RADIUS = 0.05f;

        private static void SpatialDeduplication(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;

            var allGearInside = InteriorScan.Gear(interiorRoot);
            if (allGearInside.Length == 0) return;

            // Only outside items near the inside items can be candidates; a coarse box
            // rejects the rest up front.
            Transform rootT = interiorRoot.transform;
            Bounds insideBox = new Bounds(allGearInside[0].transform.position, Vector3.zero);
            for (int i = 1; i < allGearInside.Length; i++)
            {
                if (allGearInside[i] == null) continue;
                insideBox.Encapsulate(allGearInside[i].transform.position);
            }
            insideBox.Expand(1f);

            // Put the outside candidates into the spatial hash (name cleaned once).
            var grid = new Dictionary<long, List<DedupEntry>>();
            foreach (var g in SceneScan.GearAll())
            {
                if (g == null || g.gameObject == null) continue;

                Vector3 pos = g.transform.position;
                if (!insideBox.Contains(pos)) continue;
                if (g.transform.root == rootT || g.transform.IsChildOf(rootT)) continue;

                DedupEntry entry;
                entry.name = CleanGearName(g.gameObject.name);
                entry.pos = pos;

                long key = DedupCellKey(pos);
                List<DedupEntry> bucket;
                if (!grid.TryGetValue(key, out bucket))
                {
                    bucket = new List<DedupEntry>(2);
                    grid[key] = bucket;
                }
                bucket.Add(entry);
            }

            if (grid.Count == 0) return;

            float sqrRadius = DEDUP_RADIUS * DEDUP_RADIUS;

            foreach (var insideGear in allGearInside)
            {
                if (insideGear == null || insideGear.gameObject == null) continue;

                Vector3 pos = insideGear.transform.position;
                string insideName = CleanGearName(insideGear.gameObject.name);

                if (HasDuplicateNear(grid, insideName, pos, sqrRadius))
                    UnityEngine.Object.Destroy(insideGear.gameObject);
            }
        }

        private struct DedupEntry
        {
            public string name;
            public Vector3 pos;
        }

        private static string CleanGearName(string raw)
        {
            return raw.Replace("(Clone)", "").Trim();
        }

        private static long DedupCellKey(Vector3 p)
        {
            return PackCell(
                Mathf.FloorToInt(p.x / DEDUP_RADIUS),
                Mathf.FloorToInt(p.y / DEDUP_RADIUS),
                Mathf.FloorToInt(p.z / DEDUP_RADIUS));
        }

        // 21 bits per axis: collision free up to +-52 km.
        private static long PackCell(int x, int y, int z)
        {
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
        }

        // Checks the 3x3x3 cell neighbourhood, since a match can sit just across a cell border.
        private static bool HasDuplicateNear(Dictionary<long, List<DedupEntry>> grid,
                                             string name, Vector3 pos, float sqrRadius)
        {
            int bx = Mathf.FloorToInt(pos.x / DEDUP_RADIUS);
            int by = Mathf.FloorToInt(pos.y / DEDUP_RADIUS);
            int bz = Mathf.FloorToInt(pos.z / DEDUP_RADIUS);

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        List<DedupEntry> bucket;
                        if (!grid.TryGetValue(PackCell(bx + dx, by + dy, bz + dz), out bucket)) continue;

                        for (int i = 0; i < bucket.Count; i++)
                        {
                            if (!string.Equals(bucket[i].name, name, System.StringComparison.Ordinal)) continue;
                            if ((bucket[i].pos - pos).sqrMagnitude < sqrRadius) return true;
                        }
                    }

            return false;
        }

        // Marks the clone's placeables as invalidated so the game does not adopt them.
        private static void InvalidateInteriorPlaceables(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;
            var placeables = InteriorScan.Placeables(interiorRoot);
            foreach (var p in placeables) { if (p != null) p.m_Invalidated = true; }
        }

        private static void CollectInteriorPlaceableGuids(GameObject interiorRoot)
        {
            PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Clear(); // safe to be global: only one building is cloned at a time
            if (interiorRoot == null) return;
            var placeables = InteriorScan.Placeables(interiorRoot);
            foreach (var p in placeables)
            {
                if (p != null && !string.IsNullOrEmpty(p.m_Guid))
                    PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Add(p.m_Guid);
            }
        }

        private static void CleanupOrphanPlaceables(bool force = false)
        {
            if (!force && s_OrphanPlaceablesCleanedGeneration == s_SceneGeneration) return;
            s_OrphanPlaceablesCleanedGeneration = s_SceneGeneration;

            string suffix = Il2CppTLD.Placement.Placeable.SPAWNED_NAME_SUFFIX;
            if (string.IsNullOrEmpty(suffix)) suffix = " (PLACED)";

            var allPlaceables = SceneScan.PlaceablesAll();
            foreach (var p in allPlaceables)
            {
                if (p == null || p.gameObject == null) continue;

                // Cheapest filter first: reading the scene name is a string access, but
                // still cheaper than a name search plus a parent-chain walk.
                string sceneName = p.gameObject.scene.name;
                bool isOrphan = (sceneName == "DontDestroyOnLoad" || string.IsNullOrEmpty(sceneName));
                if (!isOrphan) continue;

                if (!p.gameObject.name.Contains(suffix)) continue;
                if (IsUnderAnyMasterInterior(p.transform)) continue;

                // NEVER delete items on the player, in their hands or in their inventory.
                if (IsPlayerOrInventory(p.transform)) continue;

                // NEVER delete anything sitting under the game's own placement root.
                //
                // IsPlayerOrInventory only looks at the object's OWN name, and a placed
                // object is called something like "OBJ_BandSaw_Prefab (PLACED)" - the
                // "DesignPlaceables" it hangs under is its PARENT. So the guard above
                // misses every one of them, and this cleanup would happily destroy the
                // furniture the player set down outside the building.
                if (IsUnderPlacementRoot(p.transform))
                {
                    MelonLogger.Msg($"[ORPHAN-CLEANUP] '{p.gameObject.name}' korundu - oyunun yerlestirme kokunun altinda.");
                    continue;
                }

                MelonLogger.Warning($"[ORPHAN-CLEANUP] '{p.gameObject.name}' yok edildi " +
                                    $"(sahne='{sceneName}', parent='{(p.transform.parent != null ? p.transform.parent.name : "ROOT")}').");

                UnityEngine.Object.Destroy(p.gameObject);
            }
        }

        private static bool IsUnderAnyMasterInterior(Transform t)
        {
            if (t == null) return false;
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst.MasterInterior == null) continue;
                if (t.IsChildOf(inst.MasterInterior.transform)) return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // ONLY SWITCH BACK ON WHAT THIS MOD SWITCHED OFF
        //
        // A combination safe inside a clone could not be clicked at all: no icon, no
        // hover line, nothing. Everything readable on it was healthy - the interaction
        // reported IsEnabled and CanInteract, produced its hover text, and performing it
        // by hand opened the safe - and the game's own pick for the crosshair was still
        // NULL. The answer came from comparing it against the SAME safe in Carter Dam,
        // which the mod does not clone and where it works:
        //
        //   working : MeshCollider(off)  BoxCollider(on)
        //   broken  : MeshCollider(ON)   BoxCollider(on)
        //
        // The game ships that safe with its MeshCollider DISABLED and drives the
        // interaction off the box. The pass below used to switch every collider of every
        // GearItem and Placeable on, and the safe is a Placeable - so the mesh came back,
        // and with it in the way the crosshair never settled on the object.
        //
        // "visible = true" cannot mean "enable everything": this code has no idea why a
        // collider was off, and half the time the answer is "because the game wanted it
        // off". So the ones it disables are written down, and those are the only ones it
        // ever puts back. Anything the scene shipped disabled stays disabled, which is
        // what it was for.
        //
        // RENDERERS ARE LEFT AS THEY WERE. The same argument applies to them - an object
        // with LOD levels always has all but one switched off - but that is a separate
        // symptom with a separate blast radius, and this fix is for the one that was
        // measured.
        // ─────────────────────────────────────────────────────────────────
        // KEPT FOR THE WHOLE SESSION, ON PURPOSE.
        //
        // The obvious thing is to clear this when the region changes, since the colliders
        // it names are destroyed with the old scene. That is the dangerous thing. A
        // building can be HIDDEN when a scene reset lands - its colliders switched off and
        // written down here - and clearing the record would mean the mod no longer knows
        // it owes them, so they would stay off for good. That is precisely the "visible
        // but not interactive" defect this whole repair pass was written for.
        //
        // Left alone, the record holds a few thousand integers over a session and its only
        // failure mode is an instance id being handed out again to a different collider,
        // which would re-enable one object the scene shipped disabled - the old behaviour,
        // for one object, rarely. That is the cheaper mistake by a wide margin.
        // THE COLLIDER ITSELF IS KEPT, NOT JUST ITS ID.
        //
        // The id alone answers "do we owe this one?", which is all the hide pass needs.
        // The repair pass asks the opposite question - "which ones do we owe?" - and with
        // only ids it could not answer it: it had to walk every GearItem and every
        // Placeable in the building and ask about each collider in turn. On a base with
        // 3700 items that was 8500 component scans and 220 ms per door transition,
        // to switch a handful of colliders back on.
        //
        // Holding the reference makes the debt directly iterable, so the repair costs what
        // the debt costs instead of what the building costs.
        internal static readonly Dictionary<int, Collider> s_CollidersWeDisabled = new Dictionary<int, Collider>();

        // True when this mod is the reason the collider is off.
        private static bool WeDisabled(Collider c)
        {
            return c != null && s_CollidersWeDisabled.ContainsKey(c.GetInstanceID());
        }

        private static void ForgetDisabledCollider(Collider c)
        {
            if (c != null) s_CollidersWeDisabled.Remove(c.GetInstanceID());
        }

        // Sets the visibility and collision of an object (and all its children).
        // NOTE: Renderer, not MeshRenderer - SkinnedMeshRenderer, ParticleSystemRenderer
        // and LineRenderer have to be switched off too, otherwise part of the item stays
        // visible from outside.
        private static void SetObjectVisualState(GameObject go, bool visible)
        {
            if (go == null) return;

            foreach (var r in InteriorScan.Renderers(go)) if (r != null) r.enabled = visible;

            foreach (var c in InteriorScan.Colliders(go))
            {
                if (c == null) continue;

                if (!visible)
                {
                    // Remember it only if WE are the ones turning it off; a collider that
                    // was already off is not ours to give back later.
                    if (c.enabled)
                    {
                        s_CollidersWeDisabled[c.GetInstanceID()] = c;
                        c.enabled = false;
                    }
                    continue;
                }

                if (c.enabled) continue;
                if (!WeDisabled(c)) continue;

                c.enabled = true;
                s_CollidersWeDisabled.Remove(c.GetInstanceID());
            }
        }

        public static int AdoptStrayInteriorObjects(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return 0;
            if (instance.InteriorTrigger == null) return 0; // no volume test possible: leave it alone

            Transform interiorT = instance.MasterInterior.transform;

            // The coarse world AABB is computed once and applied to every candidate:
            // nearly all of the thousands of items in the scene are rejected with a single
            // float comparison, without walking any parent chain.
            Bounds filter;
            bool hasFilter = TryGetWorldFilterBounds(instance, out filter);

            // TWO PHASES, because the deciding test needs the clone to be OPEN.
            //
            // The cheap filters run first and reject essentially everything. Whatever
            // survives is then checked for a roof overhead - the test that separates a
            // crate genuinely standing indoors from one on the porch, which the
            // axis-aligned volume cannot tell apart. Adopting the latter dragged the
            // player's meat, beds and chairs into the building, where they vanished from
            // the world the moment the clone closed.
            //
            // Collecting first means the clone is toggled at most ONCE per sweep rather
            // than once per candidate.
            var candidates = new List<Transform>();

            foreach (var gear in SceneScan.GearAll())
            {
                if (gear == null || gear.gameObject == null) continue;
                if (!IsAdoptableStray(instance, hasFilter, filter, gear.transform)) continue;
                candidates.Add(gear.transform);
            }

            foreach (var p in SceneScan.PlaceablesAll())
            {
                if (p == null || p.gameObject == null) continue;
                if (!IsAdoptableStray(instance, hasFilter, filter, p.transform)) continue;
                candidates.Add(p.transform);
            }

            // What mods built in here (an Architect wall): no shrunk volume, it is often built right against the walls.
            foreach (var root in ModOwnedRoots())
            {
                if (root == null || (hasFilter && !filter.Contains(root.position))) continue;
                if (!instance.IsPositionInVolume(root.position) || BelongsToAnotherInstance(instance, root)) continue;
                candidates.Add(root);
            }

            if (candidates.Count == 0) return 0;

            int adopted = 0;
            bool wasActive = instance.MasterInterior.activeSelf;
            if (!wasActive) instance.MasterInterior.SetActive(true);

            try
            {
                foreach (var t in candidates)
                {
                    if (t == null || t.gameObject == null) continue;
                    if (!instance.IsPositionInsideRaycastOnly(t.position, SeamlessInteriorInstance.ITEM_RAY_ORIGIN_LIFT)) continue;

                    // Named at normal level for the first few. Adoption is what physically
                    // moves an object out of the world and into the building, so when
                    // something the player left by the door goes missing, this line is
                    // where it went.
                    if (adopted < 10)
                        MelonLogger.Msg($"[ADOPT] {instance.Config.ResolvedInstanceId}: '{t.gameObject.name}' klon sahneye baglandi.");

                    t.SetParent(interiorT, true);
                    adopted++;
                }
            }
            finally
            {
                if (!wasActive) instance.MasterInterior.SetActive(false);
            }

            return adopted;
        }

        private static bool BelongsToAnotherInstance(SeamlessInteriorInstance instance, Transform t)
        {
            if (t == null) return false;

            foreach (var other in ActiveInteriors.Values)
            {
                if (other == instance || other.MasterInterior == null) continue;
                if (t.IsChildOf(other.MasterInterior.transform)) return true;
                if (other.MasterInterior.activeSelf && other.IsPositionInVolume(t.position, -0.7f)) return true;
            }
            return false;
        }

        // Decides whether a transform is a "stray" object that can be adopted into the
        // clone scene.
        //
        // CONDITION ORDER IS FOR PERFORMANCE: they are all ANDed so the result is the
        // same, but the cheapest and most discriminating tests come first. For the vast
        // majority of the thousands of items in the scene no parent chain is walked and
        // no string is read.
        private static bool IsAdoptableStray(SeamlessInteriorInstance instance, bool hasFilter, Bounds filter, Transform t)
        {
            if (t == null) return false;

            Vector3 pos = t.position;

            // 1) Coarse AABB - a single float comparison.
            if (hasFilter && !filter.Contains(pos)) return false;

            // 2) Geometric volume test - does not rely on raycasts, so items on tables and
            //    shelves or near the ceiling are caught too. The volume is shrunk slightly
            //    so outdoor items in a doorway or against a wall are not swept in.
            if (!instance.IsPositionInVolume(pos, -0.7f)) return false;

            // 3) Already under this clone?
            if (t.IsChildOf(instance.MasterInterior.transform)) return false;

            // 4) NEVER touch objects on the player, in their hands or in their inventory.
            if (IsPlayerOrInventory(t)) return false;

            // 5) If it is part of another clone scene, or inside the volume of another
            //    clone that is currently OPEN (a basement and the floor above can overlap),
            //    leave it to that instance.
            return !BelongsToAnotherInstance(instance, t);
        }

        public static void SetInteriorItemsVisible(SeamlessInteriorInstance instance, bool visible)
        {
            if (instance == null || instance.MasterInterior == null) return;

            // Hot path, called on door transitions. The whole block runs inside one scan
            // scope: AdoptStrayInteriorObjects and the "non-child items" safety net below
            // share the same scene scan (2 full scans instead of 4).
            //
            // Sharing is safe because the block never CREATES anything. The one step that
            // destroys - PurgeLeakedCloneGearDuplicates - calls
            // SceneScan.InvalidateVolatile() itself, so the snapshot the rest of the block
            // reads is taken again after it, never over dead references.
            SceneScan.Begin();
            try
            {
                SetInteriorItemsVisibleCore(instance, visible);
            }
            finally
            {
                SceneScan.End();
            }
        }

        private static void SetInteriorItemsVisibleCore(SeamlessInteriorInstance instance, bool visible)
        {
            // WHEN HIDING: adopt the stray objects first. After that everything in the
            // clone scene is under MasterInterior and SetActive(false) switches it all off
            // at once - no item is left hanging outside.
            if (!visible)
            {
                AdoptStrayInteriorObjects(instance);

                // ...and then throw away the copies of this building's own loot that the
                // GAME's save put back into the region. Adoption cannot be relied on for
                // those: it needs a roof and a floor inside a shrunk volume, which a
                // trailer-sized interior barely has. Left behind they are not children of
                // MasterInterior, so the SetActive(false) below cannot hide them and they
                // stay hanging in the air once the player is outside.
                // (see SeamlessInteriorsMod.GearLeak.cs)
                PurgeLeakedCloneGearDuplicates(instance);
            }

            Transform masterT = instance.MasterInterior.transform;

            // ─────────────────────────────────────────────────────────────
            // THE BUILDING'S OWN CONTENTS ARE HIDDEN BY DEACTIVATING THE BUILDING
            //
            // Every object handled below is a child of MasterInterior, and the only
            // caller that hides (HideInteriorCompletely) calls MasterInterior
            // .SetActive(false) on the very next line. Unity then stops drawing and
            // colliding the whole subtree on its own, whatever each object's individual
            // enabled flags say. Switching those flags off first changes nothing that is
            // visible - and every flag switched off has to be switched back on when the
            // player returns, which is the other half of the cost.
            //
            // MEASURED on a 15-building Mystery Lake save: 4958 gear and placeables under
            // the clones, each asked for its Renderers and its Colliders on the way in and
            // again on the way out - about 10000 component searches per door. The door
            // took 885 ms, 446 of it here.
            //
            // WHAT STILL HAPPENS, AND WHY IT HAS TO:
            //   * the adoption and purge above, which decide WHICH objects are children
            //     in the first place - SetActive can only hide what it owns;
            //   * the safety net below, for objects inside the building's volume that are
            //     NOT children of it. Deactivating the clone cannot touch those, so they
            //     keep their explicit switch-off, and RepairSpawnedPlaceables still
            //     depends on that being how they got hidden;
            //   * paying back what earlier versions (or the safety net) switched off -
            //     RestoreInteriorItemColliders reads that debt directly and the door
            //     calls it, so a save upgraded mid-play gets its colliders back.
            //
            // Turned off by the FastInteriorToggle preference, which restores the old
            // object-by-object behaviour exactly.
            // ─────────────────────────────────────────────────────────────
            if (!IsFastInteriorToggleEnabled)
            {
                // WHY THERE IS NO "activeInHierarchy" FILTER: re-enabling (visible=true)
                // used to skip items whose hierarchy was inactive. Their colliders had
                // been disabled while hiding, so they STAYED disabled. Renderers, on the
                // other hand, are re-enabled in bulk unconditionally in three places (end
                // of Run(), TryBatchUpdateEnvironment, PortalPatches), so once the item
                // became visible again it was "visible but not interactive": neither
                // pickable nor selectable in placement (Y) mode.
                //
                // Enabling the collider/renderer of a disabled GameObject is harmless -
                // while the object is off it is neither drawn nor part of physics - and
                // leaves it in the right state for when it is switched on.
                var childGear = InteriorScan.Gear(instance.MasterInterior);
                foreach (var gear in childGear)
                {
                    if (gear == null || gear.gameObject == null) continue;
                    if (PlayerRefs.IsPlayerRoot(gear.transform.root)) continue;

                    SetObjectVisualState(gear.gameObject, visible);
                }

                var childPlaceables = InteriorScan.Placeables(instance.MasterInterior);
                foreach (var p in childPlaceables)
                {
                    if (p == null || p.gameObject == null) continue;
                    if (PlayerRefs.IsPlayerRoot(p.transform.root)) continue;

                    SetObjectVisualState(p.gameObject, visible);
                }
            }

            // ─────────────────────────────────────────────────────────────
            // OBJECTS IN THE BUILDING THAT THE CLONE DOES NOT OWN
            //
            // Items dropped just inside a doorway, furniture the adoption pass could not
            // claim (found while InteriorTrigger was missing). Deactivating the clone
            // cannot hide these, so they are the one set that still needs switching off
            // and on by hand.
            //
            // GIVING THEM BACK DOES NOT NEED A SEARCH. Finding them costs two
            // whole-scene searches - FindObjectsOfType over roughly 5000 gear and 1300
            // placeables, 43-176 ms each - and after Faz 3 that was most of what a door
            // still cost. The hide pass already knows which objects it touched, so it
            // writes them down and the show pass reads the list instead.
            //
            // The search still runs on the way OUT, because that is when they have to be
            // found, and it shares its scan with the adoption above.
            // ─────────────────────────────────────────────────────────────
            if (visible && instance.OutsidersRecorded)
            {
                for (int i = 0; i < instance.HiddenOutsiders.Count; i++)
                {
                    GameObject go = instance.HiddenOutsiders[i];
                    if (go == null) continue;   // destroyed since; nothing to give back

                    SetObjectVisualState(go, true);
                }
                instance.HiddenOutsiders.Clear();
                return;
            }

            // CONDITION ORDER: the AABB pre-filter now comes FIRST. It used to be last, so
            // every item in the scene first ran IsChildOf + IsPlayerOrInventory +
            // BelongsToAnotherInstance (15 instances x 2 IsChildOf).
            Bounds filterBounds;
            if (TryGetWorldFilterBounds(instance, out filterBounds, 1.2f))
            {
                // Hiding: this is the list the next show pass will read.
                if (!visible)
                {
                    instance.HiddenOutsiders.Clear();
                    instance.OutsidersRecorded = true;
                }

                foreach (var gear in SceneScan.GearAll())
                {
                    if (gear == null || gear.gameObject == null) continue;
                    if (!filterBounds.Contains(gear.transform.position)) continue; // AABB pre-filter
                    if (gear.transform.IsChildOf(masterT)) continue; // already handled above
                    if (IsPlayerOrInventory(gear.transform)) continue;
                    if (BelongsToAnotherInstance(instance, gear.transform)) continue;

                    if (IsPositionInsideFull(instance, gear.transform.position))
                    {
                        SetObjectVisualState(gear.gameObject, visible);
                        if (!visible) instance.HiddenOutsiders.Add(gear.gameObject);
                    }
                }

                foreach (var p in SceneScan.PlaceablesAll())
                {
                    if (p == null || p.gameObject == null) continue;
                    if (!filterBounds.Contains(p.transform.position)) continue;
                    if (p.transform.IsChildOf(masterT)) continue;
                    if (IsPlayerOrInventory(p.transform)) continue;
                    if (BelongsToAnotherInstance(instance, p.transform)) continue;

                    if (IsPositionInsideFull(instance, p.transform.position))
                    {
                        SetObjectVisualState(p.gameObject, visible);
                        if (!visible) instance.HiddenOutsiders.Add(p.gameObject);
                    }
                }
            }
        }

        public static void HideInteriorCompletely(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;

            SetInteriorItemsVisible(instance, false);
            instance.MasterInterior.SetActive(false);
        }
    }
}
