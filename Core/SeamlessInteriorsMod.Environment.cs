using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // The game's layer for geometry the character controller collides with but nobody is
        // meant to see. Resolved BY NAME rather than hardcoded to 26, so a layer reshuffle in a
        // game update cannot silently start hiding something else; NameToLayer gives -1 if it
        // ever disappears, which matches no object. See the collision-only pass in ApplyEnvironment.
        // Unity's placeholder material, worn by a MeshRenderer that was never given one.
        private const string UNAUTHORED_MATERIAL = "Default-Material";

        private static int s_CollideOnlyLayer = -2;
        private static int COLLIDE_ONLY_LAYER
        {
            get
            {
                if (s_CollideOnlyLayer == -2) s_CollideOnlyLayer = LayerMask.NameToLayer("CharacterControllerCollideOnly");
                return s_CollideOnlyLayer;
            }
        }

        // When several instances share the same scene, it is loaded once and copied for
        // each instance from an untouched snapshot. Lives for the load only (DestroyPristineTemplates).
        private static Dictionary<string, GameObject> s_PristineTemplates = new Dictionary<string, GameObject>();

        // True while a template is being copied. The copy's placeables Awake before it is anyone's
        // MasterInterior; registered, copies sharing guids made the game destroy each other's furniture.
        public static bool IsCopyingCloneTemplate { get; private set; }

        // Is a batched environment fix already scheduled? Stops several coroutines starting.
        private static bool s_BatchEnvironmentPending = false;
        private IEnumerator TryBatchUpdateEnvironment()
        {
            if (s_BatchEnvironmentPending)
                yield break;

            s_BatchEnvironmentPending = true;

            // Wait for all active instances to finish (30 second cap).
            float timeout = 30f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                bool allDone = true;
                foreach (var inst in ActiveInteriors.Values)
                {
                    if (inst.IsCloningRoutineActive || !inst.RunCompleted)
                    {
                        allDone = false;
                        break;
                    }
                }
                if (allDone) break;
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            // Fix the environment in one go (one flash, not 15).
            SeamlessInteriorInstance anyInstance = null;
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst.RunCompleted && inst.MasterInterior != null)
                {
                    anyInstance = inst;
                    break;
                }
            }

            if (anyInstance != null)
                UpdateGlobalEnvironment(anyInstance);

            // Enable the renderers of every instance.
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst.RunCompleted && inst.MasterInterior != null)
                {
                    ShowCloneRenderers(inst);

                    // Wherever renderers are enabled unconditionally the colliders have to
                    // be enabled too, otherwise items are visible but non-interactive
                    // (see SeamlessInteriorsMod.Interactivity.cs).
                    RestoreInteriorItemColliders(inst);
                }
            }

            // Force the scene-wide cleanups once more at the END of loading: the game's own
            // save restore may have produced new "InaccessibleGear" boxes / orphaned
            // (PLACED) items while loading.
            CleanupLostAndFoundBoxes(true);
            CleanupOrphanPlaceables(true);

            s_BatchEnvironmentPending = false;

            // Close the load window: the shared scan snapshots and the name index (tens of
            // thousands of entries) are released here.
            SceneScan.SetLoadPhase(false);

            // After the load window, so the scans are fresh and the variant groups are settled.
            RevalidateExteriorShells();
            FixPlaceableManagerSerializeInventoryPatch.PruneCloneEntries("yukleme sonu");
            DestroyPristineTemplates();

            // RELEASE THE SCREEN: all instances are ready.
            if (s_ScreenHeldBlack)
            {
                s_ScreenHeldBlack = false;
                HideLoadingOverlay();
                if (s_DebugBounds)
                    MelonLogger.Msg($"[SCREEN-RELEASE] Tum instance'lar hazir, ekran aciliyor.");

                CameraFade.FadeIn(0.5f, 0f, null);
                MelonCoroutines.Start(FinishPendingArrival());
            }

            // Cleanly restart the wind audio loop after a save/load. By this point the
            // Wwise pipeline and the scene are fully up.
            MelonCoroutines.Start(RestoreWindAudioAfterLoad());

            if (s_DebugBounds)
                MelonLogger.Msg($"[BATCH-ENV] Tum instance'lar icin toplu ortam duzeltmesi tamamlandi.");
        }

        // Loads the three interior scene variants (base / SANDBOX / DLC01) and merges
        // them under a single MasterInterior object.
        private IEnumerator LoadInteriorScenes(SeamlessInteriorInstance instance)
        {
            string baseName = instance.Config.InteriorSceneBaseName;

            // Was this scene already loaded? (Another instance on the same map with the
            // same InteriorSceneBaseName.)
            GameObject pristine;
            if (s_PristineTemplates.TryGetValue(baseName, out pristine) && pristine != null)
            {
                // Deep copy of the scene as it loaded, so this building rolls its own loot from full lists.
                IsCopyingCloneTemplate = true;
                try { instance.MasterInterior = UnityEngine.Object.Instantiate(pristine); }
                finally { IsCopyingCloneTemplate = false; }
                instance.MasterInterior.name = $"Master_{instance.Config.ResolvedInstanceId}_Interior";
                instance.MasterInterior.SetActive(false);

                // Untouched since the load, so its renderer states are the ones the scene shipped.
                instance.SceneDisabledRenderers = CollectDisabledRenderers(instance.MasterInterior);

                var exteriorScene = RealSceneByName(instance.Config.ExteriorSceneName);
                if (exteriorScene.isLoaded)
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance.MasterInterior, exteriorScene);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[CLONE] {instance.Config.ResolvedInstanceId}: Template '{baseName}' kopyalandı (Instantiate).");

                yield break;
            }

            // --- FIX 1: BACK UP THE OUTDOOR SCENE'S INTACT LIGHTMAPS ---
            // Loading an interior scene overwrites the global LightmapSettings.
            var cachedLightmaps = UnityEngine.LightmapSettings.lightmaps;
            var cachedLightProbes = UnityEngine.LightmapSettings.lightProbes;
            // ---------------------------------------------------------------

            // All three scenes are requested AT ONCE, then awaited together.
            //
            // PERFORMANCE: they used to be loaded in sequence, each awaited frame by frame
            // on its own. Because of the load lock (s_SceneLoadLock) that waiting
            // SERIALIZES across buildings: on Mystery Lake 11 different interior scenes x 3
            // = 33 consecutive loads, each with at least a frame of delay at its start.
            // Opening the requests together lets Addressables overlap all three.
            // A mod that subscribed to scene events late is shielded before these loads.
            RefreshSceneEventShield();
            var opMain =UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(baseName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            var opSandbox = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(baseName + "_SANDBOX", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            var opDLC = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(baseName + "_DLC01", UnityEngine.SceneManagement.LoadSceneMode.Additive);

            while (!opMain.IsDone || !opSandbox.IsDone || !opDLC.IsDone) yield return null;

            var s_InteriorMain = opMain.Result.Scene;
            var s_InteriorSandbox = opSandbox.Result.Scene;
            var s_InteriorDLC = opDLC.Result.Scene;

            // The interior's baked probes, read before anything is moved (dark mode lights items with them).
            try { CaptureInteriorProbes(baseName, s_InteriorMain); }
            catch (System.Exception ex) { MelonLogger.Warning($"[INTERIOR-PROBES] {baseName}: {ex.Message}"); }

            // BOTH MODES merge the interior lightmaps into the outdoor array, keeping the outdoor
            // ones intact; outdoor mode only stops the renderers using them, so F8 can switch live.
            {
                var interiorLightmaps = UnityEngine.LightmapSettings.lightmaps;

                if (cachedLightmaps != null && interiorLightmaps != null && interiorLightmaps.Length > cachedLightmaps.Length)
                {
                    // The interior scene load added new lightmaps - merge them in.
                    var merged = new LightmapData[interiorLightmaps.Length];
                    for (int i = 0; i < interiorLightmaps.Length; i++)
                    {
                        if (i < cachedLightmaps.Length)
                            merged[i] = cachedLightmaps[i]; // keep the outdoor lightmaps
                        else
                            merged[i] = interiorLightmaps[i]; // append the interior ones
                    }
                    UnityEngine.LightmapSettings.lightmaps = merged;
                }
                else
                {
                    // No new lightmaps: just restore the outdoor ones.
                    UnityEngine.LightmapSettings.lightmaps = cachedLightmaps;
                }
                UnityEngine.LightmapSettings.lightProbes = cachedLightProbes;
            }

            instance.MasterInterior = new GameObject($"Master_{instance.Config.ResolvedInstanceId}_Interior");
            instance.MasterInterior.SetActive(false);

            var exteriorScene2 = RealSceneByName(instance.Config.ExteriorSceneName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance.MasterInterior, exteriorScene2);

            // Reparent the roots of all three scenes under MasterInterior; the now empty
            // scenes are left behind.
            List<UnityEngine.SceneManagement.Scene> loadedScenes = new List<UnityEngine.SceneManagement.Scene>() { s_InteriorMain, s_InteriorSandbox, s_InteriorDLC };

            foreach (var scn in loadedScenes)
            {
                if (!scn.isLoaded) continue;
                foreach (GameObject rootObj in RealRootObjects(scn))
                {
                    if (rootObj == instance.MasterInterior) continue;
                    rootObj.transform.SetParent(instance.MasterInterior.transform, false);
                }
            }

            // Nothing has touched the renderers yet, so this is the state the scene shipped.
            instance.SceneDisabledRenderers = CollectDisabledRenderers(instance.MasterInterior);

            // The merged lightmaps are already set in both modes - do not touch them.
            // ------------------------------------------------------------------------------------

            // Pin the active scene back to the outdoor one.
            if (exteriorScene2.isLoaded)
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(exteriorScene2);
            }

            // The other buildings of this scene copy a snapshot taken now, before this clone's loot roll,
            // spawn chance, destroy lists and content touch it. Inactive, so nothing in it wakes up; parked
            // in the emptied interior scene, which other mods are shown as empty.
            if (IsSceneSharedInRegion(instance.Config))
            {
                GameObject snapshot = UnityEngine.Object.Instantiate(instance.MasterInterior);
                snapshot.name = $"SI_PristineTemplate_{baseName}";
                if (s_InteriorMain.isLoaded)
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(snapshot, s_InteriorMain);
                s_PristineTemplates[baseName] = snapshot;
            }
        }

        private static bool IsSceneSharedInRegion(InteriorConfig config)
        {
            int count = 0;
            foreach (var other in SupportedInteriors)
            {
                if (other.ExteriorSceneName == config.ExteriorSceneName &&
                    other.InteriorSceneBaseName == config.InteriorSceneBaseName && ++count > 1) return true;
            }
            return false;
        }

        // Every building of the region has copied what it needs by the end of the load. A later rebuild
        // without a snapshot loads the scene again.
        private static void DestroyPristineTemplates()
        {
            foreach (var snapshot in s_PristineTemplates.Values)
                if (snapshot != null) UnityEngine.Object.Destroy(snapshot);
            s_PristineTemplates.Clear();
        }

        private static List<Renderer> CollectDisabledRenderers(GameObject root)
        {
            var result = new List<Renderer>();
            foreach (var r in InteriorScan.Renderers(root))
                if (r != null && !r.enabled) result.Add(r);
            return result;
        }

        // Turns the clone's renderers on, except the scene-disabled ones: those keep their current state.
        public static void ShowCloneRenderers(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;

            var keepOff = s_KeepOffScratch;
            keepOff.Clear();
            foreach (var r in instance.SceneDisabledRenderers)
                if (r != null && !r.enabled) keepOff.Add(r);

            foreach (var r in InteriorScan.Renderers(instance.MasterInterior))
                if (r != null) r.enabled = true;

            for (int i = 0; i < keepOff.Count; i++)
                keepOff[i].enabled = false;
            keepOff.Clear();
        }

        private static readonly List<Renderer> s_KeepOffScratch = new List<Renderer>();

        public void AutoResolveOverlappingExternalObjects(SeamlessInteriorInstance instance)
        {
            instance.ResolvedExternalHiddenObjects.Clear();

            if (instance.MasterInterior == null) return;

            // A PARKED ROOM HAS NOTHING TO HIDE. This list exists because a co-located interior
            // shares its space with the outdoors: rocks, trees and props near the building end up
            // inside the room. A parked room stands ParkOffset up in open air, and the only
            // renderers that can ever be found there are this mod's own portal objects, which is
            // the bug of 2026-09-18 (see IsOwnPortalObject). So the scan, a pass over every active
            // renderer in the scene, is skipped, and the list stays empty. Park every building and
            // this whole system has no work left; that is a conversation for the author.
            if (instance.Config != null && instance.Config.IsParked) return;

            // World AABB grown by 1.2x to allow for rotation (same as the old behaviour).
            Bounds preFilter;
            bool hasPreFilter = TryGetWorldFilterBounds(instance, out preFilter, 1.2f);

            Transform masterT = instance.MasterInterior.transform;
            Transform shellT = (instance.ExteriorShell != null) ? instance.ExteriorShell.transform : null;

            var hiddenIds = new HashSet<int>();
            var placeableInside = new Dictionary<int, bool>();
            var allRenderers = SceneScan.RenderersActive();

            // What mods built there (an Architect wall rebuilt by its own load) is the building's content, not scenery.
            var modOwned = SeamlessInteriorsMod.ModOwnedRootIds();

            foreach (var renderer in allRenderers)
            {
                if (renderer == null) continue;

                Bounds b = renderer.bounds;

                // PRE-FILTER (now first): no intersection with the trigger bounds means it
                // definitely is not inside.
                if (hasPreFilter && !preFilter.Intersects(b)) continue;

                if (renderer.gameObject == null) continue;

                Transform rt = renderer.transform;
                if (rt.IsChildOf(masterT)) continue;
                if (shellT != null && rt.IsChildOf(shellT)) continue;
                if (PlayerRefs.IsPlayerRoot(rt.root)) continue;
                if (modOwned.Contains(rt.root.GetInstanceID())) continue;
                if (renderer.gameObject.scene.name == "DontDestroyOnLoad") continue;

                // Gear can be picked up: once in the backpack the hide list would switch it on at the player's feet.
                if (rt.GetComponentInParent<GearItem>() != null) continue;

                // A placeable standing inside the building is its content (the player's furniture), not scenery.
                if (IsPlaceableInsideBuilding(instance, rt, placeableInside)) continue;

                // CRITICAL: skip anything that is a child of another instance's
                // MasterInterior or ExteriorShell. Otherwise the basement meshes
                // (STR_FarmHouseABasementWalls_Prefab etc.) fall inside the FarmHouse
                // bounds and get hidden as "outdoor objects".
                if (BelongsToAnyOtherInstanceHierarchy(instance, rt)) continue;

                if (!IsRendererInsideInterior(instance, b)) continue;

                AddHidden(instance, hiddenIds, renderer.gameObject);

                // The colliders above and below the renderer have to go as well, otherwise
                // the player still bumps into an invisible obstacle.
                foreach (var col in renderer.GetComponentsInParent<Collider>(true))
                    if (col != null && !col.isTrigger) AddHidden(instance, hiddenIds, col.gameObject);

                foreach (var col in InteriorScan.Colliders(renderer.gameObject))
                    if (col != null && !col.isTrigger) AddHidden(instance, hiddenIds, col.gameObject);
            }

            if (SeamlessInteriorsMod.s_DebugBounds)
            {
                MelonLogger.Msg($"[AUTO-HIDE] {instance.Config.InteriorSceneBaseName} için {instance.ResolvedExternalHiddenObjects.Count} adet obje gizlenecek.");
            }
        }

        // THE MOD'S OWN PORTAL OBJECTS ARE NOT OUTDOOR SCENERY.
        //
        // This list is for outside-world objects poking into a room: whatever has a renderer
        // inside the interior's space and belongs to no clone. A window-portal quad is a root
        // object with a renderer standing in a window opening, which is exactly that description,
        // so whenever this scan ran while the quads existed they were listed and then switched off
        // for as long as the room was active. That is a load with the player already inside.
        //
        // Found 2026-09-18 after an hour of looking in the wrong place. The windows showed the raw
        // world from the parked height; all eight quads read activeSelf=False with their cameras,
        // targets and materials healthy. A Harmony prefix on GameObject.SetActive, installed from
        // the console and filtered on the quad's name, named the caller on its first hit:
        // Run -> InitializeVisibilityAndWatchdog -> ApplyInitialSyncState ->
        // SyncExternalHiddenObjects. An earlier search of this mod for who deactivates root
        // objects had come up empty because it looked for the text "SetActive(false)", and that
        // function passes a variable. Search for the call, not for one spelling of it.
        //
        // Every object this mod creates for the portals is named with one of these two prefixes.
        internal static bool IsOwnPortalObject(GameObject go)
        {
            if (go == null) return false;
            string name = go.name;
            return name.StartsWith("SI_", System.StringComparison.Ordinal)
                || name.StartsWith("SeamlessInteriors_", System.StringComparison.Ordinal);
        }

        // Is the renderer part of a placeable whose origin stands inside this building? Cached per placeable.
        private static bool IsPlaceableInsideBuilding(SeamlessInteriorInstance instance, Transform t, Dictionary<int, bool> cache)
        {
            var p = t.GetComponentInParent<Il2CppTLD.Placement.Placeable>();
            if (p == null) return false;

            int id = p.GetInstanceID();
            bool inside;
            if (!cache.TryGetValue(id, out inside))
            {
                inside = IsPositionInsideFull(instance, p.transform.position);
                cache[id] = inside;
            }
            return inside;
        }

        // Saves hit by the old hide pass hold the player's furniture as Inactive; switch it back on.
        // Only " (PLACED)" objects standing inside an SI building are touched.
        private static void ReviveHiddenPlacedDecorations(SeamlessInteriorInstance instance)
        {
            var dict = Il2CppTLD.Placement.PlaceableManager.s_Placeables;
            if (dict == null) return;

            var revive = new List<GameObject>();
            foreach (var entry in dict)
            {
                var info = entry.Value;
                if (info == null || info.m_State != Il2CppTLD.Placement.PlacementState.Inactive) continue;

                var p = info.m_Handle;
                if (p == null || p.gameObject == null || p.gameObject.activeSelf) continue;
                if (PlayerRefs.IsPlayerRoot(p.transform.root)) continue;
                if (IsUnderAnyMasterInterior(p.transform)) continue;
                if (p.gameObject.name.IndexOf(SpawnedNameSuffix(), System.StringComparison.Ordinal) < 0) continue;
                if (!instance.IsPositionInVolume(p.transform.position)) continue;

                revive.Add(p.gameObject);
            }

            // Switched on after the walk: OnEnable writes back into the registry.
            foreach (var go in revive)
            {
                go.SetActive(true);
                MelonLogger.Msg($"[PLACED-FIX] {instance.Config.ResolvedInstanceId}: '{go.name}' kapali kalmisti, geri acildi.");
            }
        }

        // Adds an object to the hide list, guarding against duplicates.
        private static void AddHidden(SeamlessInteriorInstance instance, HashSet<int> seen, GameObject go)
        {
            if (go == null) return;
            if (IsOwnPortalObject(go)) return;

            // Never a placeable's own object: switching it off marks it Inactive in the save for good.
            if (go.GetComponent<Il2CppTLD.Placement.Placeable>() != null) return;

            if (!seen.Add(go.GetInstanceID())) return;
            instance.ResolvedExternalHiddenObjects.Add(go);
        }

        // Footprint test only: shell pivots sit on the ground, below a clone lifted over the terrain.
        private static bool ShellStandsInside(SeamlessInteriorInstance instance, Vector3 pivot)
        {
            BoxCollider box = instance.InteriorTrigger;
            if (box == null) return false;
            pivot.y = box.transform.TransformPoint(box.center).y;
            return instance.IsPositionInVolume(pivot);
        }

        private static bool BelongsToAnyOtherInstanceHierarchy(SeamlessInteriorInstance instance, Transform t)
        {
            foreach (var other in ActiveInteriors.Values)
            {
                if (other == instance) continue;
                if (other.MasterInterior != null && t.IsChildOf(other.MasterInterior.transform)) return true;
                // A neighbour's shell stays, unless that building stands inside this clone
                // (TrailerSShape's middle trailer is CampTrailerB_3's shell).
                if (other.ExteriorShell != null && t.IsChildOf(other.ExteriorShell.transform)
                    && !ShellStandsInside(instance, other.ExteriorShell.transform.position)) return true;
            }
            return false;
        }

        private static bool IsRendererInsideInterior(SeamlessInteriorInstance instance, Bounds b)
        {
            if (instance.IsPositionInside(b.center)) return true;

            Vector3 min = b.min;
            Vector3 max = b.max;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    ((i & 1) == 0) ? min.x : max.x,
                    ((i & 2) == 0) ? min.y : max.y,
                    ((i & 4) == 0) ? min.z : max.z);

                if (instance.IsPositionInside(corner)) return true;
            }

            return false;
        }

        private static readonly System.Collections.Generic.HashSet<string> s_DarkModeLightContainers =
            new System.Collections.Generic.HashSet<string>
            {
                "WindowLight",
                "WindowLightGroup",
            };

        private static readonly System.Collections.Generic.HashSet<string> s_LightShaftObjects =
            new System.Collections.Generic.HashSet<string>
            {
                "FX_LightShaft_B",
                "FX_LightShaft_E",
            };

        private static void StripLightContainer(GameObject container)
        {
            if (container == null) return;

            Transform ct = container.transform;
            for (int i = 0; i < ct.childCount; i++)
            {
                Transform child = ct.GetChild(i);
                if (child != null && child.gameObject != null)
                    child.gameObject.SetActive(false);
            }

            foreach (var l in container.GetComponentsInChildren<Light>(true))
            {
                if (l == null) continue;
                l.cullingMask = 0;              // lights no layer at all - ScrubUpdate does not undo this
                l.shadows = LightShadows.None;
                l.intensity = 0f;
                l.range = 0.01f;
                l.enabled = false;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[DARK-LIGHT] '{container.name}' bosaltildi (silinmedi) — InteriorLightingManager referansi korundu.");
        }

        private static bool EnableLightingManagerHierarchy(Behaviour manager, Transform masterRoot, SeamlessInteriorInstance record = null)
        {
            if (manager == null || masterRoot == null) return false;

            bool changed = false;

            Transform t = manager.transform;
            while (t != null && t != masterRoot)
            {
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                    record?.DarkActivatedObjects.Add(t.gameObject);
                    changed = true;
                }
                t = t.parent;
            }

            if (!manager.enabled)
            {
                manager.enabled = true;
                record?.DarkEnabledManagers.Add(manager);
                changed = true;
            }

            return changed;
        }

        private static void ActivateDarkModeLightingManagers(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;

            Transform masterRoot = instance.MasterInterior.transform;
            int activated = 0;

            foreach (var m in instance.MasterInterior.GetComponentsInChildren<InteriorLightingManager>(true))
            {
                if (m == null || IsLegacyLightingManager(m)) continue;
                if (EnableLightingManagerHierarchy(m, masterRoot, instance))
                {
                    activated++;
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[DARK-LIGHT] {instance.Config.ResolvedInstanceId}: " +
                                        $"InteriorLightingManager '{m.gameObject.name}' aktif edildi.");
                }
            }

            foreach (var m in instance.MasterInterior.GetComponentsInChildren<DarkLightingManager>(true))
            {
                if (m == null) continue;
                if (EnableLightingManagerHierarchy(m, masterRoot, instance))
                {
                    activated++;
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[DARK-LIGHT] {instance.Config.ResolvedInstanceId}: " +
                                        $"DarkLightingManager '{m.gameObject.name}' aktif edildi.");
                }
            }

            if (s_DebugBounds && activated == 0)
                MelonLogger.Msg($"[DARK-LIGHT] {instance.Config.ResolvedInstanceId}: " +
                                $"acilmasi gereken kapali isik yoneticisi bulunamadi (hepsi zaten acikti).");

            // The managers must be bound to the clone's own ambient object - see
            // BindLightingManagersToOwnAmbient. (It is retried when ownership is taken:
            // DarkLightingManager.Start() may not have run yet at this point.)
            BindLightingManagersToOwnAmbient(instance);
        }

        // A legacy InteriorLightingManager sharing its object with a DarkLightingManager stays off
        // in vanilla; switched on it wrote ambient x3 and switched the fill lights off.
        private static bool IsLegacyLightingManager(InteriorLightingManager m)
        {
            return m.GetComponent<DarkLightingManager>() != null;
        }

        // DarkLightingManager records light positions at Start, before the clone was moved;
        // its distance fade would cull every light against the interior scene's coordinates.
        private static void RefreshDarkLightPositions(SeamlessInteriorInstance instance)
        {
            foreach (var m in instance.MasterInterior.GetComponentsInChildren<DarkLightingManager>(true))
            {
                if (m == null || !m.m_UseDistanceFade) continue;
                foreach (var list in new[] { m.m_ConstantLightList, m.m_ColouredLightList, m.m_NightLightList, m.m_NonTodLightList })
                {
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var ld = list[i];
                        if (ld == null || ld.m_Light == null) continue;
                        ld.m_Position = ld.m_Light.transform.position;
                        list[i] = ld;
                    }
                }
            }
        }

        public static bool BindLightingManagersToOwnAmbient(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return false;

            try { RefreshDarkLightPositions(instance); }
            catch (System.Exception ex) { MelonLogger.Warning($"[LIGHT-BIND] {instance.Config.ResolvedInstanceId}: {ex.Message}"); }

            Transform masterT = instance.MasterInterior.transform;

            // The clone's own ambient source, if there is one.
            TodAmbientLight own = null;
            foreach (var tod in instance.MasterInterior.GetComponentsInChildren<TodAmbientLight>(true))
            {
                if (tod == null) continue;
                own = tod;
                break;
            }

            bool darkMode = IsDarkAtmosphereMode;

            int managers = 0;      // total managers under the clone
            int running = 0;       // actually receiving Update (active + enabled)
            int reactivated = 0;   // enabled by this call
            int rebound = 0;       // ambient source corrected
            int orphanBindings = 0;// source empty or outside the clone

            foreach (var m in instance.MasterInterior.GetComponentsInChildren<InteriorLightingManager>(true))
            {
                if (m == null || IsLegacyLightingManager(m)) continue;
                managers++;

                // PrepareMasterInterior never runs again for persisted clones, so the
                // manager being enabled is guaranteed here.
                if (darkMode && EnableLightingManagerHierarchy(m, masterT, instance)) reactivated++;
                if (m.isActiveAndEnabled) running++;

                TodAmbientLight cur = m.m_AmbientLight;
                if (cur != null && cur.transform.IsChildOf(masterT)) continue;

                orphanBindings++;
                if (own == null) continue;

                m.m_AmbientLight = own;
                rebound++;
            }

            foreach (var m in instance.MasterInterior.GetComponentsInChildren<DarkLightingManager>(true))
            {
                if (m == null) continue;
                managers++;

                if (darkMode && EnableLightingManagerHierarchy(m, masterT, instance)) reactivated++;
                if (m.isActiveAndEnabled) running++;

                TodAmbientLight cur = m.m_AmbientLight;
                if (cur != null && cur.transform.IsChildOf(masterT)) continue;

                orphanBindings++;
                if (own == null) continue;

                m.m_AmbientLight = own;
                rebound++;
            }

            if (s_DebugBounds)
            {
                MelonLogger.Msg($"[LIGHT-BIND] {instance.Config.ResolvedInstanceId}: " +
                                $"yonetici={managers} (calisan={running}, acilan={reactivated}), " +
                                $"klon disina bagli={orphanBindings}, yeniden baglanan={rebound}, " +
                                $"klon ambient objesi={(own != null ? own.gameObject.name : "YOK")}");
            }

            return own != null;
        }

        private static readonly System.Collections.Generic.HashSet<string> s_DarkModeProtectedFromDestroy =
            new System.Collections.Generic.HashSet<string>
            {
                "InteriorLightingManager_Prefab",
                "DLM_AFHangar_LGT_Prefab",
                "DarkLightingManager_Prefab",
                "Daytime",
                "Daylight",
                "Midday",
                "AmbientLight",
                "Nighttime",
                "NightTime",
            };

        // Strips the freshly cloned scene: removes/disables the objects listed in the
        // config, cleans up lights and probes that would leak into the outside world, and
        // in dark mode brings the interior lighting managers up.
        private void PrepareMasterInterior(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            bool darkMode = IsDarkAtmosphereMode;

            // In dark mode InteriorLightingManager and the light effects must be preserved.
            var darkModeProtectedFromDestroy = s_DarkModeProtectedFromDestroy;

            // Name lookups go through constant-time sets (see InteriorConfig.DestroySet).
            // Also, t.name is read ONLY ONCE: under IL2CPP every .name read allocates a new
            // managed string and this loop runs over thousands of objects.
            HashSet<string> destroySet = instance.Config.DestroySet;
            HashSet<string> disableSet = instance.Config.DisableSet;

            Transform[] masterChildren = instance.MasterInterior.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in masterChildren)
            {
                if (t == null || t.gameObject == null) continue;

                string tName = t.name;

                // Light shafts: no need to list them in a config, they are removed mod-wide.
                if (s_LightShaftObjects.Contains(tName))
                {
                    // NEVER Destroy, in either mode. These objects are held by
                    // LightShaftGimble.m_LightShaftRenderer (an array of Renderer) and by
                    // InteriorLightingManager's m_LightShaftParent / m_LightShaftGimbleList.
                    // Destroying them leaves the gimble on the parent calling
                    // SetPropertyBlock on a destroyed Renderer from Update, once per frame
                    // per gimble, for the lifetime of the scene. SetActive(false) looks the
                    // same and keeps every reference intact.
                    //
                    // Dark mode already did this. Outdoor lighting, the DEFAULT mode, still
                    // destroyed them: measured on TLD 2.55 at 161,774 NullReferenceExceptions
                    // and a 40 MB Player.log within a few minutes of play, 8 per frame while
                    // stood in one building, every one with a full stack trace.
                    t.gameObject.SetActive(false);
                    continue;
                }

                // Is this object's name in the config's Destroy list?
                // FIX: exact match. With Contains(), searching for "OBJ_TrailerWindow" also
                // matched "OBJ_TrailerWindow_Prefab" (the parent) and closed the parent.
                bool shouldDestroy = destroySet.Contains(tName);

                // The dark-mode lighting objects are kept in BOTH modes and switched by
                // ApplyLightingMode, so F8 can change mode live.
                if ((shouldDestroy || disableSet.Contains(tName)) && darkModeProtectedFromDestroy.Contains(tName))
                {
                    instance.DarkOnlyObjects.Add(t.gameObject);
                    instance.DarkOnlyWasActive.Add(t.gameObject.activeSelf);
                    continue;
                }

                // Light containers such as WindowLight / WindowLightGroup are emptied, never
                // destroyed, in either mode. See StripLightContainer for why.
                if (shouldDestroy && s_DarkModeLightContainers.Contains(tName))
                {
                    StripLightContainer(t.gameObject);
                    shouldDestroy = false;
                }

                if (shouldDestroy)
                {
                    UnityEngine.Object.Destroy(t.gameObject);
                    continue;
                }

                // Is this object's name in the config's Disable list?
                bool shouldDisable = disableSet.Contains(tName);

                // Protected objects are not disabled in dark mode either.
                // (The FarmHouseABasement config puts DarkLightingManager_Prefab in its
                // Disable list, which killed all of the basement's interior lighting in
                // dark mode.)
                if (shouldDisable && darkMode && darkModeProtectedFromDestroy.Contains(tName))
                    shouldDisable = false;

                if (shouldDisable)
                {
                    t.gameObject.SetActive(false);
                }
            }

            // --- SHADER AND REFLECTION FIXES ---

            // Reflection probes are kept in both modes, scoped so they cannot reach the outside;
            // ApplyLightingMode switches them on only in dark mode. LightProbeGroups stay too.
            foreach (var probe in instance.MasterInterior.GetComponentsInChildren<ReflectionProbe>(true))
            {
                if (probe == null) continue;
                probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
                probe.boxProjection = true;
                probe.importance = 0;
                instance.DarkOnlyProbes.Add(probe);
            }

            // --- CLEAN UP LIGHT BLEEDING ---
            var allLights = instance.MasterInterior.GetComponentsInChildren<Light>(true);
            foreach (var l in allLights)
            {
                if (l == null) continue;

                // 1. The fake sun/moon (Directional) inside interior scenes breaks the whole
                // outdoor map. It must go in BOTH modes - a directional light is global.
                if (l.type == LightType.Directional)
                {
                    UnityEngine.Object.Destroy(l.gameObject);
                    continue;
                }

                // 2. Big point lights spill through the walls: off in outdoor mode, capped at
                // 25 m in dark mode (ApplyLightingMode).
                if (l.type == LightType.Point && l.range > 15f)
                {
                    if (l.range > 25f) l.range = 25f;
                    instance.BigPointLights.Add(l);
                    instance.BigPointLightWasEnabled.Add(l.enabled);
                }
            }
            // ---------------------------------------------------

            // A camera left in the interior scene (HuntingLodgeA's Root/Art/Geo/Camera) takes over
            // the screen once the clone is active and the player's own FPSCamera switches off.
            foreach (var cam in instance.MasterInterior.GetComponentsInChildren<Camera>(true))
                if (cam != null) UnityEngine.Object.Destroy(cam);
            foreach (var listener in instance.MasterInterior.GetComponentsInChildren<AudioListener>(true))
                if (listener != null) UnityEngine.Object.Destroy(listener);

            // --- STATIC BATCHING FIX, AND THE COLLISION-ONLY GEOMETRY ---
            // The clone is moved after loading, and static renderers cannot move.
            //
            // HIDE THE COLLISION HELPERS THAT HAVE NO MATERIAL. An interior scene's own camera
            // never draws them, so nobody ever had to switch their renderers off; a parked room is
            // in the OUTDOOR scene, whose camera does draw them, and they appear as flat white
            // shapes over the real geometry.
            //
            // Measured 2026-09-22 in the Airfield hangar, owner: "The steps have a white inclined
            // plane over top of the steps mesh." THE LAYER IS NOT THE SIGNATURE: six objects named
            // "STAIR COLLIDER" were on CharacterControllerCollideOnly, but hiding those left the
            // white plane exactly where it was, because the one he could see was called "Collider"
            // and sat on layer 8, Ground. Eight of those plus a "Col_tail" were the visible ones.
            //
            // What all fifteen share is Unity's Default-Material, the placeholder a MeshRenderer
            // gets when no material was ever assigned. A shipped art asset never wears it, so it
            // is a safe signature and a meaningful one: it means nobody intended this to be seen.
            // The collision-only layer is kept as a second test because it is equally safe by
            // definition. Owner, after both were hidden live: "Stairs look good now".
            int collideOnlyHidden = 0;
            foreach (var r in instance.MasterInterior.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null) continue;

                if (r.gameObject.isStatic)
                {
                    r.gameObject.isStatic = false;
                }

                if (!r.enabled) continue;

                var mat = r.sharedMaterial;
                bool unauthored = mat == null || mat.name.StartsWith(UNAUTHORED_MATERIAL);
                if (unauthored || r.gameObject.layer == COLLIDE_ONLY_LAYER)
                {
                    r.enabled = false;
                    collideOnlyHidden++;
                }
            }
            if (collideOnlyHidden > 0)
            {
                MelonLogger.Msg($"[CLONE-HIDE] {instance.Config.ResolvedInstanceId}: gorunmesi amaclanmayan {collideOnlyHidden} carpisma yardimcisinin gorunumu kapatildi (ic mekan sahnesinin kamerasi da onlari cizmez).",
                                $"[CLONE-HIDE] {instance.Config.ResolvedInstanceId}: {collideOnlyHidden} collision helpers that were never meant to be seen were hidden, which is what an interior scene's own camera does with them.");
            }
            // ---------------------------------

            // Enable the interior lighting managers that arrive disabled in dark mode.
            // Must run AFTER the Destroy/Disable steps: otherwise the loop above could
            // close the object we just enabled.
            ApplyLightingMode(instance, darkMode);
        }

        // Places the clone exactly on top of its exterior shell (position, rotation, scale).
        private void AlignWithExteriorShell(SeamlessInteriorInstance instance)
        {
            // There can be several shells with the same name. Find them all and match the
            // one closest to FallbackPosition.
            instance.ExteriorShell = FindClosestShell(instance.Config.ExteriorShellPrefabName, instance.Config.FallbackPosition);

            if (instance.ExteriorShell == null && !string.IsNullOrEmpty(instance.Config.ExteriorShellPrefabName))
            {
                // Fallback: any object whose name contains InteriorSceneBaseName + "Prefab".
                // NOTE: no fallback when ExteriorShellPrefabName is empty (a sub-interior) -
                // it has no shell.
                // (The search again uses the name index built once per scene.)
                var fallbackCandidates = new List<GameObject>();
                SceneObjectIndex.CollectByContainsBoth(instance.Config.InteriorSceneBaseName, "Prefab", fallbackCandidates);

                float bestDist = float.MaxValue;
                foreach (var go in fallbackCandidates)
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    // Never a piece of a clone (its window glow or lighting prefab) or another building's shell.
                    if (IsUnderAnyCloneOrClaimedShell(go.transform)) continue;
                    float dist = Vector3.Distance(go.transform.position, instance.Config.FallbackPosition);
                    if (dist <= SHELL_MATCH_MAX_DISTANCE && dist < bestDist)
                    {
                        bestDist = dist;
                        instance.ExteriorShell = go;
                    }
                }
            }

            // Even with a shell found, an exact position from the config wins when requested.
            Vector3 targetPos;

            if (instance.Config.ForceExactPosition || instance.ExteriorShell == null)
            {
                targetPos = instance.Config.FallbackPosition;
                targetPos.y += instance.Config.YOffset;

                if (s_DebugBounds) MelonLogger.Msg($"[DEBUG-SHELL] {instance.Config.InteriorSceneBaseName} için Fallback/Exact pozisyon kullanılıyor: {targetPos}");
            }
            else
            {
                targetPos = instance.ExteriorShell.transform.position;
                targetPos.y += instance.Config.YOffset;

                if (s_DebugBounds) MelonLogger.Msg($"[DEBUG-SHELL] {instance.Config.InteriorSceneBaseName} ExteriorShell bulundu. Target: {targetPos}");
            }

            // Parked buildings stand away from their shell rather than on it. Applied last
            // so everything above (shell lookup, YOffset, ForceExactPosition) is unchanged,
            // and so a zero offset reproduces the original placement exactly.
            targetPos += instance.Config.ParkOffset;

            instance.MasterInterior.transform.position = targetPos;

            if (instance.ExteriorShell != null && !instance.Config.ForceExactPosition)
            {
                instance.MasterInterior.transform.rotation = instance.ExteriorShell.transform.rotation * Quaternion.Euler(instance.Config.RotationOffset);
            }
            else
            {
                instance.MasterInterior.transform.rotation = Quaternion.Euler(instance.Config.RotationOffset);
            }

            // A PARKED ROOM LOADS AT ITS OWN SIZE. The author's ScaleAdjustment stretches six rooms to
            // fill their buildings (Grey Mother's house and the farmhouse 1.15 across and deep, the
            // Camp Office 1.05 x 0.98 x 1.05, the hangar, the gas station, the farmhouse basement),
            // because their room stands INSIDE the building. A parked room has no building round it
            // to fill, and the stretch shows: owner, 2026-09-20, in Grey Mother's house, "the
            // proportions of this are stretched ... All of the interior prefabs should load in with
            // original dimensions", "that scale adjustment should be one for every building." The
            // config's numbers are left as the author wrote them and still apply to a room that is
            // not parked. Everything the mod saves about a room is in the room's own coordinates, so
            // a save made with the stretch loads onto the same spots without it; the hand-typed
            // entry points were tuned WITH it and are carried by ParkedInteriorPoint.
            instance.MasterInterior.transform.localScale = instance.Config.IsParked ? Vector3.one : instance.Config.ScaleAdjustment;

            // Last, because it reads the room's windows where the lines above have just put them.
            TurnParkedRoomToShell(instance);
        }

        // Builds an invisible wall around the interior: keeps the player from walking out
        // through the clone's outer edge, and carves the NavMesh so wildlife stays out.
        private void SetupSolidPerimeter(SeamlessInteriorInstance instance, Bounds localBounds)
        {
            if (instance.MasterInterior == null) return;

            GameObject solidPerimeter = new GameObject("SolidPerimeter_Blocker");
            solidPerimeter.transform.SetParent(instance.MasterInterior.transform, false);
            solidPerimeter.transform.localPosition = Vector3.zero;
            solidPerimeter.transform.localRotation = Quaternion.identity;
            solidPerimeter.layer = LayerMask.NameToLayer("NPC");

            float wT = 0.5f;   // wall thickness

            BoxCollider wallFront = solidPerimeter.AddComponent<BoxCollider>();
            wallFront.center = new Vector3(localBounds.center.x, localBounds.center.y, localBounds.max.z + (wT / 2f));
            wallFront.size = new Vector3(localBounds.size.x, localBounds.size.y, wT);

            BoxCollider wallBack = solidPerimeter.AddComponent<BoxCollider>();
            wallBack.center = new Vector3(localBounds.center.x, localBounds.center.y, localBounds.min.z - (wT / 2f));
            wallBack.size = new Vector3(localBounds.size.x, localBounds.size.y, wT);

            // The side walls are lengthened by wT on each end so the corners have no gaps.
            BoxCollider wallRight = solidPerimeter.AddComponent<BoxCollider>();
            wallRight.center = new Vector3(localBounds.max.x + (wT / 2f), localBounds.center.y, localBounds.center.z);
            wallRight.size = new Vector3(wT, localBounds.size.y, localBounds.size.z + (wT * 2));

            BoxCollider wallLeft = solidPerimeter.AddComponent<BoxCollider>();
            wallLeft.center = new Vector3(localBounds.min.x - (wT / 2f), localBounds.center.y, localBounds.center.z);
            wallLeft.size = new Vector3(wT, localBounds.size.y, localBounds.size.z + (wT * 2));

            UnityEngine.AI.NavMeshObstacle aiObstacle = solidPerimeter.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            aiObstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            aiObstacle.center = localBounds.center;
            aiObstacle.size = localBounds.size;
            aiObstacle.carving = true;
        }

        // Lightmap-stripped material copies: source material InstanceID -> copy.
        //
        // WHY IT MATTERS: the old code Instantiated the material SEPARATELY for every
        // renderer. With 300 renderers sharing the same wall material that produced 300
        // unique materials - 300 copies during loading, and batching broken for the rest of
        // the session (a separate draw call per renderer). Across 15 buildings that meant
        // thousands of pointless materials.
        //
        // Sharing the copy is the right behaviour here: all we do is switch off a shader
        // keyword, so every copy made from the same source is identical anyway.
        private static readonly Dictionary<int, Material> s_StrippedMaterialCache = new Dictionary<int, Material>();

        private static void PruneStrippedMaterialCache()
        {
            if (s_StrippedMaterialCache.Count == 0) return;

            var dead = new List<int>();
            foreach (var kvp in s_StrippedMaterialCache)
                if (kvp.Value == null) dead.Add(kvp.Key);

            foreach (int id in dead) s_StrippedMaterialCache.Remove(id);
        }

        private static Material GetLightmapStrippedMaterial(Material source)
        {
            int id = source.GetInstanceID();

            Material cached;
            if (s_StrippedMaterialCache.TryGetValue(id, out cached) && cached != null)
                return cached;

            Material copy = UnityEngine.Object.Instantiate(source);
            copy.DisableKeyword("LIGHTMAP_ON");
            s_StrippedMaterialCache[id] = copy;
            return copy;
        }

        // Removes the interior's baked lighting so it is lit by the outside world instead.
        private void StripBakedLightmaps(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            // Every lightmapped renderer is recorded with both looks: baked (dark mode) and
            // stripped (outdoor mode, lit by the outside world). ApplyLightmapMode picks one.
            // Recorded once per clone: a second pass would find the stripped state and lose the baked one.
            if (instance.LightmappedRenderers.Count > 0) { ApplyLightmapMode(instance, IsDarkAtmosphereMode); return; }

            Renderer[] allRenderersAfter = InteriorScan.Renderers(instance.MasterInterior);
            foreach (var r in allRenderersAfter)
            {
                if (r == null) continue;
                int index = r.lightmapIndex;
                if (index < 0 || index >= 65534) continue;

                Material[] mats = r.sharedMaterials;
                Material[] stripped = new Material[mats.Length];
                bool swap = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    stripped[i] = mats[i];
                    if (mats[i] != null && mats[i].IsKeywordEnabled("LIGHTMAP_ON"))
                    {
                        stripped[i] = GetLightmapStrippedMaterial(mats[i]);
                        swap = true;
                    }
                }

                instance.LightmappedRenderers.Add(new LightmappedRenderer
                {
                    Renderer = r, Index = index, Baked = mats, Stripped = swap ? stripped : null
                });
            }

            ApplyLightmapMode(instance, IsDarkAtmosphereMode);
        }

        internal static void ApplyLightmapMode(SeamlessInteriorInstance instance, bool dark)
        {
            foreach (var lr in instance.LightmappedRenderers)
            {
                Renderer r = lr.Renderer;
                if (r == null) continue;
                r.lightmapIndex = dark ? lr.Index : -1;
                if (lr.Stripped == null) continue;

                // Slot by slot, and only where our own material still sits: window/frost code may
                // have put its own material on a slot since, and that one is left alone.
                Material[] cur = r.sharedMaterials;
                Material[] from = dark ? lr.Stripped : lr.Baked;
                Material[] to = dark ? lr.Baked : lr.Stripped;
                bool changed = false;
                for (int i = 0; i < cur.Length && i < from.Length; i++)
                {
                    if (cur[i] == from[i] && from[i] != to[i]) { cur[i] = to[i]; changed = true; }
                }
                if (changed) r.sharedMaterials = cur;
            }
        }

        // Makes the game recompute its global lighting/environment after the clones have
        // been added, and revives the aurora electrolizers inside them.
        private void UpdateGlobalEnvironment(SeamlessInteriorInstance instance)
        {
            // Back up the outdoor lightmaps - ForceOutdoorEnvironment and DynamicGI can
            // corrupt them, which the player sees as a "colour flicker".
            var cachedLightmaps = UnityEngine.LightmapSettings.lightmaps;
            var cachedLightProbes = UnityEngine.LightmapSettings.lightProbes;

            Weather wFinal = GameManager.GetWeatherComponent();
            if (wFinal != null) wFinal.ForceOutdoorEnvironment();
            // It also set the outdoor player capsule; a player inside a building keeps the indoor one.
            TickIndoorPlayerCapsule();

            LightingManager.m_LevelLoadComplete = true;
            LightingManager.OnLevelLoadComplete();
            LightingManager.SetLightingStrengthDefault();

            UnityEngine.DynamicGI.UpdateEnvironment();

            // Restore the lightmaps - the outdoor ones must survive in both modes.
            UnityEngine.LightmapSettings.lightmaps = cachedLightmaps;
            UnityEngine.LightmapSettings.lightProbes = cachedLightProbes;

            if (instance.MasterInterior != null)
            {
                // Aurora electrolizers normally set themselves up and register from Awake/OnEnable,
                // which a clone kept inactive since it was copied has not run. They are prepared
                // and registered by hand here.
                var electrolizers = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.ModularElectrolizer.AuroraModularElectrolizer>(true);
                int failed = 0;
                string firstError = null;
                foreach (var electrolizer in electrolizers)
                {
                    if (electrolizer == null) continue;

                    try
                    {
                        PrepareCloneElectrolizer(electrolizer);
                    }
                    catch (System.Exception ex)
                    {
                        if (failed++ == 0) firstError = electrolizer.gameObject.name + ": " + ex.Message;
                    }
                }

                if (failed > 0)
                    MelonLoader.MelonLogger.Warning($"[ELEKTROLIZOR] {instance.Config.ResolvedInstanceId}: {failed} elektrolizor hazirlanamadi, " +
                                                    $"aurora sistemine kaydedilmedi. Ilk hata: {firstError}");
            }
        }

        // AuroraModularElectrolizer.Initialize (private), looked up by name. It used to be invoked
        // through a metadata token copied from one build of the game; a token only names the same
        // method in that build.
        private static System.IntPtr s_ElectrolizerInitialize = System.IntPtr.Zero;
        private static bool s_ElectrolizerInitializeResolved = false;

        private static void PrepareCloneElectrolizer(Il2CppTLD.ModularElectrolizer.AuroraModularElectrolizer electrolizer)
        {
            // Awake creates the property block the emissives are drawn with, and Initialize already
            // draws them (through StopAll). On a clone that never ran Awake, Initialize threw there,
            // before marking the electrolizer initialised; it was registered anyway and then threw
            // inside AuroraManager on every frame of an aurora.
            if (electrolizer.m_EmissivePropertyBlock == null)
                electrolizer.m_EmissivePropertyBlock = new UnityEngine.MaterialPropertyBlock();

            if (!electrolizer.m_IsInitialized)
            {
                if (!s_ElectrolizerInitializeResolved)
                {
                    s_ElectrolizerInitializeResolved = true;
                    s_ElectrolizerInitialize = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                        Il2CppInterop.Runtime.Il2CppClassPointerStore<Il2CppTLD.ModularElectrolizer.AuroraModularElectrolizer>.NativeClassPtr,
                        "Initialize", 0);
                    if (s_ElectrolizerInitialize == System.IntPtr.Zero)
                        MelonLoader.MelonLogger.Warning("[ELEKTROLIZOR] AuroraModularElectrolizer.Initialize bulunamadi; " +
                                                        "baslatilmamis klon elektrolizorleri aurora sistemine kaydedilmeyecek.");
                }

                if (s_ElectrolizerInitialize == System.IntPtr.Zero) return;

                System.IntPtr exc = System.IntPtr.Zero;
                unsafe
                {
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(s_ElectrolizerInitialize, electrolizer.Pointer, (void**)0, ref exc);
                }
                Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc);
            }

            // Initialize sets this as its last step: still false means it did not finish, and the
            // electrolizer would fail in the aurora update.
            if (!electrolizer.m_IsInitialized) return;

            Il2Cpp.AuroraManager.RegisterAuroraElectrolizer(electrolizer);
            electrolizer.m_HasStopped = false;
        }

        public static List<Bounds> ComputeInteriorSubBounds(GameObject root, float cellSize = 2.0f, float radius = 30f)
        {
            Renderer[] renderers = InteriorScan.Renderers(root);
            Transform rootT = root.transform;

            // 1. Collect the local-space centres of all renderers (filtered).
            //
            // PERFORMANCE: the renderer's WORLD bounds and its grid cell are computed ONCE
            // here and stored. The old code recomputed them for every rectangle in the loop
            // below (r.bounds is an IL2CPP call).
            var valid = new List<RendererCell>(renderers.Length);
            foreach (var r in renderers)
            {
                // Shadow casters and oversized/far-off renderers would blow up the bounds.
                if (r == null || r.gameObject.name.Contains("Shadow_Caster")) continue;
                Bounds wb = r.bounds;
                if (wb.size.x > 40f || wb.size.y > 40f || wb.size.z > 40f) continue;
                Vector3 localCenter = rootT.InverseTransformPoint(wb.center);
                if (Mathf.Abs(localCenter.x) > radius || Mathf.Abs(localCenter.y) > radius || Mathf.Abs(localCenter.z) > radius) continue;

                RendererCell rc;
                rc.world = wb;
                rc.gx = Mathf.FloorToInt(localCenter.x / cellSize);
                rc.gz = Mathf.FloorToInt(localCenter.z / cellSize);
                valid.Add(rc);
            }

            if (valid.Count == 0)
                return new List<Bounds> { new Bounds(new Vector3(0, 2f, 0), new Vector3(25f, 18f, 25f)) };

            // 2. Find the grid extents.
            int gxMin = int.MaxValue, gxMax = int.MinValue;
            int gzMin = int.MaxValue, gzMax = int.MinValue;
            var occupiedCells = new HashSet<long>(); // hashed as gx * 100000 + gz

            for (int i = 0; i < valid.Count; i++)
            {
                int gx = valid[i].gx;
                int gz = valid[i].gz;
                occupiedCells.Add((long)gx * 100000L + gz);
                if (gx < gxMin) gxMin = gx;
                if (gx > gxMax) gxMax = gx;
                if (gz < gzMin) gzMin = gz;
                if (gz > gzMax) gzMax = gz;
            }

            // 3. Build a 2D bool grid.
            int width = gxMax - gxMin + 1;
            int height = gzMax - gzMin + 1;
            bool[,] grid = new bool[width, height];
            bool[,] used = new bool[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    grid[x, z] = occupiedCells.Contains((long)(gxMin + x) * 100000L + (gzMin + z));
                }
            }

            // 4. Greedy rectangle decomposition: split the occupied cells into maximal rectangles.
            var rects = new List<System.Tuple<int, int, int, int>>(); // x0, z0, x1, z1 (inclusive)

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    if (!grid[x, z] || used[x, z]) continue;

                    // Extend to the right from this cell.
                    int maxX = x;
                    while (maxX + 1 < width && grid[maxX + 1, z] && !used[maxX + 1, z])
                        maxX++;

                    // Then extend downwards, but only while the whole row is free.
                    int maxZ = z;
                    bool canExtendZ = true;
                    while (canExtendZ && maxZ + 1 < height)
                    {
                        for (int cx = x; cx <= maxX; cx++)
                        {
                            if (!grid[cx, maxZ + 1] || used[cx, maxZ + 1])
                            {
                                canExtendZ = false;
                                break;
                            }
                        }
                        if (canExtendZ) maxZ++;
                    }

                    // Mark the cells as consumed.
                    for (int cx = x; cx <= maxX; cx++)
                        for (int cz = z; cz <= maxZ; cz++)
                            used[cx, cz] = true;

                    rects.Add(new System.Tuple<int, int, int, int>(gxMin + x, gzMin + z, gxMin + maxX, gzMin + maxZ));
                }
            }

            // 5. For each rectangle, compute tight bounds from the renderers it contains.
            //
            // PERFORMANCE: the whole renderer list used to be rescanned for every rectangle
            // - O(rectangles x renderers). A cell -> rectangle map is built first, then the
            // renderer list is walked ONCE and each renderer written straight into its own
            // rectangle: O(renderers + cells).
            var cellToRect = new Dictionary<long, int>(occupiedCells.Count);
            for (int ri = 0; ri < rects.Count; ri++)
            {
                var rect = rects[ri];
                for (int cx = rect.Item1; cx <= rect.Item3; cx++)
                    for (int cz = rect.Item2; cz <= rect.Item4; cz++)
                        cellToRect[(long)cx * 100000L + cz] = ri;
            }

            int rectCount = rects.Count;
            var rectMin = new Vector3[rectCount];
            var rectMax = new Vector3[rectCount];
            var rectHits = new int[rectCount];

            for (int ri = 0; ri < rectCount; ri++)
            {
                rectMin[ri] = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                rectMax[ri] = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            }

            for (int i = 0; i < valid.Count; i++)
            {
                RendererCell rc = valid[i];

                int ri;
                if (!cellToRect.TryGetValue((long)rc.gx * 100000L + rc.gz, out ri)) continue;

                Vector3 c = rc.world.center;
                Vector3 e = rc.world.extents;

                // The eight world corners are converted to local space, since the clone can
                // be rotated relative to the world.
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = c + new Vector3(
                        ((k & 1) == 0) ? e.x : -e.x,
                        ((k & 2) == 0) ? e.y : -e.y,
                        ((k & 4) == 0) ? e.z : -e.z);

                    Vector3 local = rootT.InverseTransformPoint(corner);
                    rectMin[ri] = Vector3.Min(rectMin[ri], local);
                    rectMax[ri] = Vector3.Max(rectMax[ri], local);
                }

                rectHits[ri]++;
            }

            var subBounds = new List<Bounds>();
            for (int ri = 0; ri < rectCount; ri++)
            {
                // Skip rectangles with very few renderers (noise filter).
                if (rectHits[ri] < 3) continue;

                Vector3 size = rectMax[ri] - rectMin[ri];
                if (size.x < 1f || size.z < 1f) continue;

                Bounds regionBounds = new Bounds();
                regionBounds.SetMinMax(rectMin[ri], rectMax[ri]);
                subBounds.Add(regionBounds);
            }

            // 6. Merge rectangles that are tiny and overlap a large neighbour.
            // (Greedy decomposition can produce needless small fragments.)
            subBounds = MergeSmallBounds(subBounds);

            // Fallback when no region was found at all.
            if (subBounds.Count == 0)
                subBounds.Add(new Bounds(new Vector3(0, 2f, 0), new Vector3(25f, 18f, 25f)));

            return subBounds;
        }

        private struct RendererCell
        {
            public Bounds world;
            public int gx;
            public int gz;
        }

        private static List<Bounds> MergeSmallBounds(List<Bounds> bounds, float minVolume = 8f)
        {
            if (bounds.Count <= 1) return bounds;

            var merged = new bool[bounds.Count];

            for (int i = 0; i < bounds.Count; i++)
            {
                if (merged[i]) continue;

                Bounds b = bounds[i];
                float vol = b.size.x * b.size.y * b.size.z;

                if (vol < minVolume)
                {
                    // Find the closest remaining neighbour and merge into it.
                    float minDist = float.MaxValue;
                    int closestIdx = -1;

                    for (int j = 0; j < bounds.Count; j++)
                    {
                        if (i == j || merged[j]) continue;
                        float dist = Vector3.Distance(b.center, bounds[j].center);
                        if (dist < minDist) { minDist = dist; closestIdx = j; }
                    }

                    if (closestIdx >= 0)
                    {
                        Bounds target = bounds[closestIdx];
                        target.Encapsulate(b);
                        bounds[closestIdx] = target;
                        merged[i] = true;
                    }
                }
            }

            var result = new List<Bounds>();
            for (int i = 0; i < bounds.Count; i++)
            {
                if (!merged[i]) result.Add(bounds[i]);
            }
            return result;
        }

        // No single interior mesh piece is legitimately bigger than this. A renderer that
        // is - a skybox proxy, a backdrop, a stray effect - would swallow the whole answer.
        private const float PREFAB_BOUNDS_OUTLIER_SIZE = 40f;

        // The clone's own bounding box in its local space: literally the dimensions of the
        // prefab we brought into the world, for callers that need the real extent rather
        // than a room-shaped approximation of it.
        //
        // WHY THIS EXISTS SEPARATELY FROM ComputeInteriorSubBounds: that function is built
        // to hug an L-shaped or multi-part floor plan, and it pays for that with three
        // filters that can each throw away real extent - a +/-30m clamp on the renderer's
        // local centre, a "fewer than 3 renderers" rectangle drop, and a "thinner than 1m"
        // rectangle drop. The long end of a big building decomposes into exactly the thin,
        // sparse rectangles those last two discard, so the combined bounds come back short.
        // That is tolerable for a floor-plan approximation and not tolerable for a weather
        // kill volume, which has to cover the whole building or snow falls through the part
        // it missed. Sub-bounds keeps its behaviour; this is a second, blunter measurement.
        //
        // MESHES, NOT RENDERER BOUNDS: Renderer.bounds on an INACTIVE object reports a box
        // at the world origin, and clones spend most of their life inactive - reading them
        // in that state is what once inflated a probe box to a kilometre. Taking the mesh's
        // own local bounds through the renderer's transform gives the same answer and does
        // not care whether anything is currently active.
        public static bool TryComputePrefabLocalBounds(GameObject root, out Bounds localBounds,
                                                       out int meshesUsed, out int meshesSkipped)
        {
            localBounds = default;
            meshesUsed = 0;
            meshesSkipped = 0;
            if (root == null) return false;

            Transform rootT = root.transform;
            Matrix4x4 worldToLocal = rootT.worldToLocalMatrix;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in filters)
            {
                if (mf == null) continue;

                // Shadow-only duplicates of real geometry; they add nothing and some are
                // deliberately oversized.
                if (mf.gameObject.name.Contains("Shadow_Caster")) { meshesSkipped++; continue; }

                var mesh = mf.sharedMesh;
                if (mesh == null) { meshesSkipped++; continue; }

                Bounds mb = mesh.bounds;
                Matrix4x4 localToWorld = mf.transform.localToWorldMatrix;

                Vector3 c = mb.center;
                Vector3 e = mb.extents;

                // All eight corners, because the piece can sit at any rotation relative to
                // the clone root. World first, so the outlier test below can be made against
                // the piece's true axis-aligned world size.
                Vector3 wMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 wMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = c + new Vector3(
                        ((k & 1) == 0) ? e.x : -e.x,
                        ((k & 2) == 0) ? e.y : -e.y,
                        ((k & 4) == 0) ? e.z : -e.z);

                    Vector3 world = localToWorld.MultiplyPoint3x4(corner);
                    wMin = Vector3.Min(wMin, world);
                    wMax = Vector3.Max(wMax, world);
                }

                // Reject the outlier on its real world size, with scale and rotation already
                // applied - a small mesh scaled up is caught, and a long one rotated so its
                // length is spread across all three axes cannot slip under the threshold.
                Vector3 worldSize = wMax - wMin;
                if (worldSize.x > PREFAB_BOUNDS_OUTLIER_SIZE ||
                    worldSize.y > PREFAB_BOUNDS_OUTLIER_SIZE ||
                    worldSize.z > PREFAB_BOUNDS_OUTLIER_SIZE)
                {
                    meshesSkipped++;
                    continue;
                }

                // Accumulate from the MESH corners, not from the world AABB just built: that
                // AABB is already a loose envelope around a rotated piece, and enveloping it
                // a second time in the root's space would lose more on every rotated child.
                // It exists only to make the outlier test above exact. Recomputing the eight
                // corners is cheaper than the scratch array that would avoid it.
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = c + new Vector3(
                        ((k & 1) == 0) ? e.x : -e.x,
                        ((k & 2) == 0) ? e.y : -e.y,
                        ((k & 4) == 0) ? e.z : -e.z);

                    Vector3 local = worldToLocal.MultiplyPoint3x4(localToWorld.MultiplyPoint3x4(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }

                meshesUsed++;
            }

            if (meshesUsed == 0) return false;

            localBounds = new Bounds();
            localBounds.SetMinMax(min, max);
            return true;
        }

        // The old ComputeLocalInteriorBounds, kept for compatibility.
        // It now returns the combined version of ComputeInteriorSubBounds.
        public static Bounds ComputeLocalInteriorBounds(GameObject root, float radius = 30f)
        {
            var subs = ComputeInteriorSubBounds(root, 2.0f, radius);
            if (subs.Count == 0) return new Bounds(new Vector3(0, 2f, 0), new Vector3(25f, 18f, 25f));
            Bounds combined = subs[0];
            for (int i = 1; i < subs.Count; i++)
                combined.Encapsulate(subs[i]);
            return combined;
        }

        // Further than this, the shell is another building's: with a house burned in this save
        // (RandomBurnedHouses), its door used to lead into this clone. Real shells sit within ~4m.
        private const float SHELL_MATCH_MAX_DISTANCE = 10f;

        // The game switches the RandomBurnedHouses groups 1-3 s after their RandomSpawnObject starts,
        // which can be after AlignWithExteriorShell ran: re-match shells left in a group it turned off.
        private static void RevalidateExteriorShells()
        {
            SceneObjectIndex.Invalidate();
            try
            {
                foreach (var inst in ActiveInteriors.Values)
                    RevalidateExteriorShell(inst, "yukleme sonu");
            }
            finally
            {
                SceneObjectIndex.Invalidate();
            }
        }

        // When the groups switch varies per load, often after the load's own revalidation. A parent check
        // every 2 s catches it; the re-match itself runs once per stale shell.
        private const float SHELL_VARIANT_CHECK_INTERVAL = 2f;
        private static float s_NextShellVariantCheck;

        public static void TickShellVariantGuard()
        {
            float now = Time.realtimeSinceStartup;
            if (now < s_NextShellVariantCheck) return;
            s_NextShellVariantCheck = now + SHELL_VARIANT_CHECK_INTERVAL;
            if (s_ScreenHeldBlack || SceneScan.InLoadPhase) return;

            bool stale = false;
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst.RunCompleted && IsShellInDisabledVariant(inst.ExteriorShell)) { stale = true; break; }
            }
            if (!stale) return;

            // A load window for the duration: one renderer scan shared by every hide list rebuilt here.
            SceneScan.SetLoadPhase(true);
            try { RevalidateExteriorShells(); }
            finally { SceneScan.SetLoadPhase(false); }
        }

        // Is this shell in a variant group the game switched off? (activeSelf false alone is the mod hiding it.)
        private static bool IsShellInDisabledVariant(GameObject shell)
        {
            if (shell == null) return false;
            Transform parent = shell.transform.parent;
            return parent != null && !parent.gameObject.activeInHierarchy;
        }

        internal static bool RevalidateExteriorShell(SeamlessInteriorInstance instance, string reason)
        {
            if (instance == null || !instance.RunCompleted || instance.MasterInterior == null) return false;
            if (string.IsNullOrEmpty(instance.Config.ExteriorShellPrefabName)) return false;

            // Parked clones of other regions have no shell to look for.
            if (RealSceneName(instance.MasterInterior.scene) != instance.Config.ExteriorSceneName) return false;

            // No shell stays no shell: the groups are only ever switched off during a load.
            GameObject old = instance.ExteriorShell;
            if (!IsShellInDisabledVariant(old)) return false;

            instance.ExteriorShell = null;
            GameObject found = FindClosestShell(instance.Config.ExteriorShellPrefabName, instance.Config.FallbackPosition);
            if (found == null)
            {
                // Burned in this save: no shell, as if it had never matched. Undo the mod's hiding of the old one.
                if (!old.activeSelf) old.SetActive(true);
                MelonLogger.Msg($"[SHELL-MATCH] {instance.Config.ResolvedInstanceId}: shell kapali varyant grubundaydi " +
                                $"('{old.transform.parent.name}'), bu kayitta bina yok - shell birakildi ({reason}).");
                return true;
            }

            bool inside = PlayerInteriorId == instance.Config.ResolvedInstanceId;

            // Give the old one back the state the scene had; the mod may have hidden it.
            if (old != null && !old.activeSelf) old.SetActive(true);
            instance.ExteriorShell = found;
            found.SetActive(!inside);

            // The hide list was built around the wrong shell, so the real one's walls are in it.
            // Its inside test needs the clone's colliders, as in IsPositionInsideFull.
            bool wasActive = instance.MasterInterior.activeSelf;
            if (!wasActive) instance.MasterInterior.SetActive(true);
            try
            {
                if (s_Instance != null) s_Instance.AutoResolveOverlappingExternalObjects(instance);
            }
            finally
            {
                if (!wasActive) instance.MasterInterior.SetActive(false);
            }
            SyncExternalHiddenObjects();

            MelonLogger.Msg($"[SHELL-MATCH] {instance.Config.ResolvedInstanceId}: shell kapali varyant grubundaydi " +
                            $"('{old.transform.parent.name}'), '{found.transform.parent.name}' ile degistirildi ({reason}).");
            return true;
        }

        // Door-time safety net for a shell that went stale after the load.
        internal static void RevalidateExteriorShellAtDoor(SeamlessInteriorInstance instance)
        {
            if (instance == null || !IsShellInDisabledVariant(instance.ExteriorShell)) return;
            SceneObjectIndex.Invalidate();
            try { RevalidateExteriorShell(instance, "kapidan giris"); }
            finally { SceneObjectIndex.Invalidate(); }
        }

        private static bool IsUnderAnyCloneOrClaimedShell(Transform t)
        {
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst.MasterInterior != null && t.IsChildOf(inst.MasterInterior.transform)) return true;
                if (inst.ExteriorShell != null && t.IsChildOf(inst.ExteriorShell.transform)) return true;
            }
            return false;
        }

        private static GameObject FindClosestShell(string shellPrefabName, Vector3 fallbackPosition)
        {
            if (string.IsNullOrEmpty(shellPrefabName)) return null;

            // Collect the shells already claimed by other instances.
            var usedShells = new HashSet<int>();
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst.ExteriorShell != null)
                    usedShells.Add(inst.ExteriorShell.GetInstanceID());
            }

            // Exact matches plus prefix matches (there may be a "(Clone)" suffix).
            var candidates = new List<GameObject>();
            SceneObjectIndex.CollectByPrefix(shellPrefabName, candidates);

            GameObject best = null;
            float bestDist = float.MaxValue;

            foreach (var go in candidates)
            {
                if (go == null) continue;
                if (!go.activeInHierarchy) continue; // the name index can predate the variant pick
                if (usedShells.Contains(go.GetInstanceID())) continue; // already in use

                float dist = Vector3.Distance(go.transform.position, fallbackPosition);
                if (dist <= SHELL_MATCH_MAX_DISTANCE && dist < bestDist)
                {
                    bestDist = dist;
                    best = go;
                }
            }

            if (s_DebugBounds && best != null)
                MelonLogger.Msg($"[SHELL-MATCH] '{shellPrefabName}' -> '{best.name}' pos={best.transform.position} dist={bestDist:F1} (fallback={fallbackPosition})");
            else if (s_DebugBounds)
                MelonLogger.Msg($"[SHELL-MATCH] '{shellPrefabName}': {SHELL_MATCH_MAX_DISTANCE}m icinde aktif shell yok (fallback={fallbackPosition}) - bu kayitta bina yok/yanmis olabilir.");

            return best;
        }
    }
}
