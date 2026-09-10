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

        // Decides whether this building's loot still has to be generated, then removes
        // duplicated items and duplicated GUIDs.
        private void ProcessSpawnsAndDeduplication(SeamlessInteriorInstance instance)
        {
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            string saveKey = instance.Config.SaveKeyPrefix + currentSaveName;
            bool isAlreadyGenerated = UnityEngine.PlayerPrefs.GetInt(saveKey, 0) == 1;

            if (isAlreadyGenerated)
            {
                // Loot already exists in the save: anything without a GUID is a rogue
                // copy created by the clone itself and would duplicate items.
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

            foreach (var rso in allRSO)
            {
                if (rso == null) continue;
                if (RollSingleSpawner(rso, ref candidateTotal, ref quotaTotal, ref preInactiveTotal)) rolled++;
            }

            int after = CountActiveGear(instance.MasterInterior);

            MelonLogger.Msg($"[LOOT-ROLL] {instance.Config.ResolvedInstanceId} (mod={GetCurrentModeName()}): " +
                            $"{allRSO.Length} RandomSpawnObject'in {rolled} tanesi elendi. " +
                            $"Aday={candidateTotal} (elemeden once kapali={preInactiveTotal}), " +
                            $"zorlugun istedigi={quotaTotal}. Acik esya: {before} -> {after}.");

            // The loot really has been generated now: only NOW write the flag.
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            if (!string.IsNullOrEmpty(currentSaveName))
            {
                UnityEngine.PlayerPrefs.SetInt(instance.Config.SaveKeyPrefix + currentSaveName, 1);
                UnityEngine.PlayerPrefs.Save();
            }

            instance.PendingInitialLootRoll = false;
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
            foreach (var gear in interiorRoot.GetComponentsInChildren<Il2Cpp.GearItem>(true))
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

            var allGearInside = interiorRoot.GetComponentsInChildren<Il2Cpp.GearItem>(true);
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
            var placeables = interiorRoot.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in placeables) { if (p != null) p.m_Invalidated = true; }
        }

        private static void CollectInteriorPlaceableGuids(GameObject interiorRoot)
        {
            PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Clear(); // safe to be global: only one building is cloned at a time
            if (interiorRoot == null) return;
            var placeables = interiorRoot.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
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

        // Sets the visibility and collision of an object (and all its children).
        // NOTE: Renderer, not MeshRenderer - SkinnedMeshRenderer, ParticleSystemRenderer
        // and LineRenderer have to be switched off too, otherwise part of the item stays
        // visible from outside.
        private static void SetObjectVisualState(GameObject go, bool visible)
        {
            if (go == null) return;

            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r != null) r.enabled = visible;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (c != null) c.enabled = visible;
        }

        public static int AdoptStrayInteriorObjects(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return 0;
            if (instance.InteriorTrigger == null) return 0; // no volume test possible: leave it alone

            Transform interiorT = instance.MasterInterior.transform;
            int adopted = 0;

            // The coarse world AABB is computed once and applied to every candidate:
            // nearly all of the thousands of items in the scene are rejected with a single
            // float comparison, without walking any parent chain.
            Bounds filter;
            bool hasFilter = TryGetWorldFilterBounds(instance, out filter);

            foreach (var gear in SceneScan.GearAll())
            {
                if (gear == null || gear.gameObject == null) continue;
                if (!IsAdoptableStray(instance, hasFilter, filter, gear.transform)) continue;

                gear.transform.SetParent(interiorT, true);
                adopted++;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[ADOPT] {instance.Config.ResolvedInstanceId}: gear '{gear.gameObject.name}' klon sahneye baglandi.");
            }

            foreach (var p in SceneScan.PlaceablesAll())
            {
                if (p == null || p.gameObject == null) continue;
                if (!IsAdoptableStray(instance, hasFilter, filter, p.transform)) continue;

                p.transform.SetParent(interiorT, true);
                adopted++;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[ADOPT] {instance.Config.ResolvedInstanceId}: placeable '{p.gameObject.name}' klon sahneye baglandi.");
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
            // The block only REPARENTS, it never creates or destroys objects, so sharing
            // is safe.
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
            if (!visible) AdoptStrayInteriorObjects(instance);

            // Gear and placeables that are children of MasterInterior.
            //
            // WHY THERE IS NO "activeInHierarchy" FILTER: re-enabling (visible=true) used
            // to skip items whose hierarchy was inactive. Their colliders had been
            // disabled while hiding, so they STAYED disabled. Renderers, on the other
            // hand, are re-enabled in bulk unconditionally in three places (end of Run(),
            // TryBatchUpdateEnvironment, PortalPatches), so once the item became visible
            // again it was "visible but not interactive": neither pickable nor selectable
            // in placement (Y) mode.
            //
            // Enabling the collider/renderer of a disabled GameObject is harmless - while
            // the object is off it is neither drawn nor part of physics - and leaves it in
            // the right state for when it is switched on.
            Transform masterT = instance.MasterInterior.transform;

            var childGear = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true);
            foreach (var gear in childGear)
            {
                if (gear == null || gear.gameObject == null) continue;
                if (PlayerRefs.IsPlayerRoot(gear.transform.root)) continue;

                SetObjectVisualState(gear.gameObject, visible);
            }

            var childPlaceables = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in childPlaceables)
            {
                if (p == null || p.gameObject == null) continue;
                if (PlayerRefs.IsPlayerRoot(p.transform.root)) continue;

                SetObjectVisualState(p.gameObject, visible);
            }

            // The old renderer-based path remains as a safety net for moved objects that
            // could not be adopted (e.g. found while InteriorTrigger was missing).
            //
            // CONDITION ORDER: the AABB pre-filter now comes FIRST. It used to be last, so
            // every item in the scene first ran IsChildOf + IsPlayerOrInventory +
            // BelongsToAnotherInstance (15 instances x 2 IsChildOf).
            Bounds filterBounds;
            if (TryGetWorldFilterBounds(instance, out filterBounds, 1.2f))
            {
                foreach (var gear in SceneScan.GearAll())
                {
                    if (gear == null || gear.gameObject == null) continue;
                    if (!filterBounds.Contains(gear.transform.position)) continue; // AABB pre-filter
                    if (gear.transform.IsChildOf(masterT)) continue; // already handled above
                    if (IsPlayerOrInventory(gear.transform)) continue;
                    if (BelongsToAnotherInstance(instance, gear.transform)) continue;

                    if (IsPositionInsideFull(instance, gear.transform.position))
                        SetObjectVisualState(gear.gameObject, visible);
                }

                foreach (var p in SceneScan.PlaceablesAll())
                {
                    if (p == null || p.gameObject == null) continue;
                    if (!filterBounds.Contains(p.transform.position)) continue;
                    if (p.transform.IsChildOf(masterT)) continue;
                    if (IsPlayerOrInventory(p.transform)) continue;
                    if (BelongsToAnotherInstance(instance, p.transform)) continue;

                    if (IsPositionInsideFull(instance, p.transform.position))
                        SetObjectVisualState(p.gameObject, visible);
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
