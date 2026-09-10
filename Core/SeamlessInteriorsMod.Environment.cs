using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // When several instances share the same scene, it is loaded once and copied for
        // each instance. This dictionary is used during loading and cleared when the
        // scene unloads.
        private static Dictionary<string, GameObject> s_LoadedSceneTemplates = new Dictionary<string, GameObject>();

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
                    foreach (var r in inst.MasterInterior.GetComponentsInChildren<Renderer>(true))
                        if (r != null) r.enabled = true;

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

            // RELEASE THE SCREEN: all instances are ready.
            if (s_ScreenHeldBlack)
            {
                s_ScreenHeldBlack = false;
                HideLoadingOverlay();
                if (s_DebugBounds)
                    MelonLogger.Msg($"[SCREEN-RELEASE] Tum instance'lar hazir, ekran aciliyor.");

                CameraFade.FadeIn(0.5f, 0f, null);
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
            if (s_LoadedSceneTemplates.ContainsKey(baseName) && s_LoadedSceneTemplates[baseName] != null)
            {
                // Deep copy from the template.
                instance.MasterInterior = UnityEngine.Object.Instantiate(s_LoadedSceneTemplates[baseName]);
                instance.MasterInterior.name = $"Master_{instance.Config.ResolvedInstanceId}_Interior";
                instance.MasterInterior.SetActive(false);

                var exteriorScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(instance.Config.ExteriorSceneName);
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
            var opMain = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(baseName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            var opSandbox = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(baseName + "_SANDBOX", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            var opDLC = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(baseName + "_DLC01", UnityEngine.SceneManagement.LoadSceneMode.Additive);

            while (!opMain.IsDone || !opSandbox.IsDone || !opDLC.IsDone) yield return null;

            var s_InteriorMain = opMain.Result.Scene;
            var s_InteriorSandbox = opSandbox.Result.Scene;
            var s_InteriorDLC = opDLC.Result.Scene;

            if (IsDarkAtmosphereMode)
            {
                // DARK MODE: merge the interior lightmaps into the outdoor lightmap array,
                // preserving the interior's baked lighting without breaking the outdoor one.
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
            else
            {
                // OUTDOOR MODE: restore all outdoor lightmaps (the existing behaviour).
                UnityEngine.LightmapSettings.lightmaps = cachedLightmaps;
                UnityEngine.LightmapSettings.lightProbes = cachedLightProbes;
            }

            instance.MasterInterior = new GameObject($"Master_{instance.Config.ResolvedInstanceId}_Interior");
            instance.MasterInterior.SetActive(false);

            var exteriorScene2 = UnityEngine.SceneManagement.SceneManager.GetSceneByName(instance.Config.ExteriorSceneName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance.MasterInterior, exteriorScene2);

            // Reparent the roots of all three scenes under MasterInterior; the now empty
            // scenes are left behind.
            List<UnityEngine.SceneManagement.Scene> loadedScenes = new List<UnityEngine.SceneManagement.Scene>() { s_InteriorMain, s_InteriorSandbox, s_InteriorDLC };

            foreach (var scn in loadedScenes)
            {
                if (!scn.isLoaded) continue;
                foreach (GameObject rootObj in scn.GetRootGameObjects())
                {
                    if (rootObj == instance.MasterInterior) continue;
                    rootObj.transform.SetParent(instance.MasterInterior.transform, false);
                }
            }

            // --- FIX 2: RESTORE THE OUTDOOR LIGHTMAPS AFTER THE INTERIOR IS LOADED ---
            if (!IsDarkAtmosphereMode)
            {
                UnityEngine.LightmapSettings.lightmaps = cachedLightmaps;
                UnityEngine.LightmapSettings.lightProbes = cachedLightProbes;
            }
            // In dark mode the merged lightmaps are already set - do not touch them.
            // ------------------------------------------------------------------------------------

            // Pin the active scene back to the outdoor one.
            if (exteriorScene2.isLoaded)
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(exteriorScene2);
            }

            // Store as a template so other instances of the same scene can copy it.
            s_LoadedSceneTemplates[baseName] = instance.MasterInterior;
        }
        public void AutoResolveOverlappingExternalObjects(SeamlessInteriorInstance instance)
        {
            instance.ResolvedExternalHiddenObjects.Clear();

            if (instance.MasterInterior == null) return;

            // World AABB grown by 1.2x to allow for rotation (same as the old behaviour).
            Bounds preFilter;
            bool hasPreFilter = TryGetWorldFilterBounds(instance, out preFilter, 1.2f);

            Transform masterT = instance.MasterInterior.transform;
            Transform shellT = (instance.ExteriorShell != null) ? instance.ExteriorShell.transform : null;

            var hiddenIds = new HashSet<int>();
            var allRenderers = SceneScan.RenderersActive();

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
                if (renderer.gameObject.scene.name == "DontDestroyOnLoad") continue;

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

                foreach (var col in renderer.GetComponentsInChildren<Collider>(true))
                    if (col != null && !col.isTrigger) AddHidden(instance, hiddenIds, col.gameObject);
            }

            if (SeamlessInteriorsMod.s_DebugBounds)
            {
                MelonLogger.Msg($"[AUTO-HIDE] {instance.Config.InteriorSceneBaseName} için {instance.ResolvedExternalHiddenObjects.Count} adet obje gizlenecek.");
            }
        }

        // Adds an object to the hide list, guarding against duplicates.
        private static void AddHidden(SeamlessInteriorInstance instance, HashSet<int> seen, GameObject go)
        {
            if (go == null) return;
            if (!seen.Add(go.GetInstanceID())) return;
            instance.ResolvedExternalHiddenObjects.Add(go);
        }

        private static bool BelongsToAnyOtherInstanceHierarchy(SeamlessInteriorInstance instance, Transform t)
        {
            foreach (var other in ActiveInteriors.Values)
            {
                if (other == instance) continue;
                if (other.MasterInterior != null && t.IsChildOf(other.MasterInterior.transform)) return true;
                if (other.ExteriorShell != null && t.IsChildOf(other.ExteriorShell.transform)) return true;
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

        private static bool EnableLightingManagerHierarchy(Behaviour manager, Transform masterRoot)
        {
            if (manager == null || masterRoot == null) return false;

            bool changed = false;

            Transform t = manager.transform;
            while (t != null && t != masterRoot)
            {
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                    changed = true;
                }
                t = t.parent;
            }

            if (!manager.enabled)
            {
                manager.enabled = true;
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
                if (m == null) continue;
                if (EnableLightingManagerHierarchy(m, masterRoot))
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
                if (EnableLightingManagerHierarchy(m, masterRoot))
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

        public static bool BindLightingManagersToOwnAmbient(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return false;

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
                if (m == null) continue;
                managers++;

                // PrepareMasterInterior never runs again for persisted clones, so the
                // manager being enabled is guaranteed here.
                if (darkMode && EnableLightingManagerHierarchy(m, masterT)) reactivated++;
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

                if (darkMode && EnableLightingManagerHierarchy(m, masterT)) reactivated++;
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
                    if (darkMode)
                    {
                        // NO Destroy in dark mode. These objects may sit in
                        // InteriorLightingManager's m_LightShaftParent /
                        // m_LightShaftGimbleList references; destroying them triggers the
                        // same NullReference chain that stops the interior lighting.
                        // SetActive(false) looks the same and keeps the reference intact.
                        t.gameObject.SetActive(false);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(t.gameObject);
                    }
                    continue;
                }

                // Is this object's name in the config's Destroy list?
                // FIX: exact match. With Contains(), searching for "OBJ_TrailerWindow" also
                // matched "OBJ_TrailerWindow_Prefab" (the parent) and closed the parent.
                bool shouldDestroy = destroySet.Contains(tName);

                // Never destroy the protected objects in dark mode.
                if (shouldDestroy && darkMode && darkModeProtectedFromDestroy.Contains(tName))
                    shouldDestroy = false;

                // Dark mode: light containers such as WindowLight / WindowLightGroup are NOT
                // destroyed, only emptied. See StripLightContainer for why.
                if (shouldDestroy && darkMode && s_DarkModeLightContainers.Contains(tName))
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

            if (!darkMode)
            {
                // Outdoor mode: remove the ReflectionProbes and LightProbeGroups that came
                // from the interior. In dark mode they are kept (needed for the interior
                // atmosphere).

                // 1. Reflection probes.
                var reflectionProbes = instance.MasterInterior.GetComponentsInChildren<ReflectionProbe>(true);
                foreach (var probe in reflectionProbes)
                {
                    if (probe != null)
                    {
                        UnityEngine.Object.Destroy(probe.gameObject);
                    }
                }

                // 2. Light probes.
                var lightProbes = instance.MasterInterior.GetComponentsInChildren<LightProbeGroup>(true);
                foreach (var lp in lightProbes)
                {
                    if (lp != null)
                    {
                        UnityEngine.Object.Destroy(lp.gameObject);
                    }
                }
            }
            else
            {
                // Dark mode: KEEP the ReflectionProbes but limit their scope, so they do not
                // affect the outside world: switch to box projection and drop the importance.
                var reflectionProbes = instance.MasterInterior.GetComponentsInChildren<ReflectionProbe>(true);
                foreach (var probe in reflectionProbes)
                {
                    if (probe == null) continue;
                    probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
                    probe.boxProjection = true;
                    // Lower importance so it does not compete with the outdoor probes.
                    probe.importance = 0;
                }

                // LightProbeGroups are KEPT - they give the interior objects the right colour.
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

                if (!darkMode)
                {
                    // Outdoor mode: remove the huge point lights that spill through the walls.
                    if (l.type == LightType.Point && l.range > 15f)
                    {
                        UnityEngine.Object.Destroy(l.gameObject);
                    }
                }
                else
                {
                    // Dark mode: KEEP the big point lights (interior atmosphere), but cap
                    // their range so they do not spill outside.
                    if (l.type == LightType.Point && l.range > 25f)
                    {
                        l.range = 25f;
                    }
                }
            }
            // ---------------------------------------------------

            // --- STATIC BATCHING FIX ---
            // The clone is moved after loading, and static renderers cannot move.
            foreach (var r in instance.MasterInterior.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r != null && r.gameObject.isStatic)
                {
                    r.gameObject.isStatic = false;
                }
            }
            // ---------------------------------

            // Enable the interior lighting managers that arrive disabled in dark mode.
            // Must run AFTER the Destroy/Disable steps: otherwise the loop above could
            // close the object we just enabled.
            if (darkMode)
            {
                ActivateDarkModeLightingManagers(instance);
            }
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
                    if (go == null) continue;
                    float dist = Vector3.Distance(go.transform.position, instance.Config.FallbackPosition);
                    if (dist < bestDist)
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

            instance.MasterInterior.transform.position = targetPos;

            if (instance.ExteriorShell != null && !instance.Config.ForceExactPosition)
            {
                instance.MasterInterior.transform.rotation = instance.ExteriorShell.transform.rotation * Quaternion.Euler(instance.Config.RotationOffset);
            }
            else
            {
                instance.MasterInterior.transform.rotation = Quaternion.Euler(instance.Config.RotationOffset);
            }

            instance.MasterInterior.transform.localScale = instance.Config.ScaleAdjustment;
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

            // In dark mode the baked lightmaps are KEPT - they are the foundation of the
            // interior atmosphere.
            if (IsDarkAtmosphereMode) return;

            Renderer[] allRenderersAfter = instance.MasterInterior.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderersAfter)
            {
                if (r == null) continue;

                r.lightmapIndex = -1;
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].IsKeywordEnabled("LIGHTMAP_ON"))
                    {
                        mats[i] = GetLightmapStrippedMaterial(mats[i]);
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
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

            LightingManager.m_LevelLoadComplete = true;
            LightingManager.OnLevelLoadComplete();
            LightingManager.SetLightingStrengthDefault();

            UnityEngine.DynamicGI.UpdateEnvironment();

            // Restore the lightmaps - the outdoor ones must survive in both modes.
            UnityEngine.LightmapSettings.lightmaps = cachedLightmaps;
            UnityEngine.LightmapSettings.lightProbes = cachedLightProbes;

            if (instance.MasterInterior != null)
            {
                // Aurora electrolizers register themselves in Start(), which never ran for
                // the clone. They are initialised and registered by hand here.
                var electrolizers = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.ModularElectrolizer.AuroraModularElectrolizer>(true);
                foreach (var electrolizer in electrolizers)
                {
                    if (electrolizer != null)
                    {
                        if (!electrolizer.m_IsInitialized)
                        {
                            // The Initialize method is private, so it is invoked directly by
                            // its IL2CPP method token.
                            var methodInit = Il2CppInterop.Runtime.IL2CPP.GetIl2CppMethodByToken(Il2CppInterop.Runtime.Il2CppClassPointerStore<Il2CppTLD.ModularElectrolizer.AuroraModularElectrolizer>.NativeClassPtr, 100695667);
                            if (methodInit != System.IntPtr.Zero)
                            {
                                System.IntPtr exc = System.IntPtr.Zero;
                                unsafe
                                {
                                    Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(methodInit, electrolizer.Pointer, (void**)0, ref exc);
                                }
                            }
                        }

                        Il2Cpp.AuroraManager.RegisterAuroraElectrolizer(electrolizer);
                        electrolizer.m_HasStopped = false;
                    }
                }
            }
        }

        public static List<Bounds> ComputeInteriorSubBounds(GameObject root, float cellSize = 2.0f)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
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
                if (Mathf.Abs(localCenter.x) > 30f || Mathf.Abs(localCenter.y) > 30f || Mathf.Abs(localCenter.z) > 30f) continue;

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

        // The old ComputeLocalInteriorBounds, kept for compatibility.
        // It now returns the combined version of ComputeInteriorSubBounds.
        public static Bounds ComputeLocalInteriorBounds(GameObject root)
        {
            var subs = ComputeInteriorSubBounds(root);
            if (subs.Count == 0) return new Bounds(new Vector3(0, 2f, 0), new Vector3(25f, 18f, 25f));
            Bounds combined = subs[0];
            for (int i = 1; i < subs.Count; i++)
                combined.Encapsulate(subs[i]);
            return combined;
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
                if (usedShells.Contains(go.GetInstanceID())) continue; // already in use

                float dist = Vector3.Distance(go.transform.position, fallbackPosition);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = go;
                }
            }

            if (s_DebugBounds && best != null)
                MelonLogger.Msg($"[SHELL-MATCH] '{shellPrefabName}' -> '{best.name}' pos={best.transform.position} dist={bestDist:F1} (fallback={fallbackPosition})");

            return best;
        }
    }
}
