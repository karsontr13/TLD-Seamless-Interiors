using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[assembly: MelonInfo(typeof(SeamlessInteriors.SeamlessInteriorsMod), "SeamlessInteriors", "2.1.0", "Hamsi Buglama")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod : MelonMod
    {
        // ─── INTERIOR LIGHTING MODE OPTION ───
        // 0 = Outdoor (lit by the outside world - the current default)
        // 1 = Dark Atmosphere (the original dark interior atmosphere)
        private static MelonPreferences_Category s_PrefCategory;
        private static MelonPreferences_Entry<int> s_PrefInteriorLightingMode;

        // Should the clone scene's initial loot roll (RandomSpawnObject) be performed?
        // Can be turned off for troubleshooting: off, the mod falls back to the old
        // behaviour of never rolling (every candidate item in the scene stays enabled).
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] EnableInitialLootRoll
        private static MelonPreferences_Entry<bool> s_PrefEnableInitialLootRoll;

        public static bool IsInitialLootRollEnabled => s_PrefEnableInitialLootRoll?.Value ?? true;

        // Gear save filter: should container contents, looted items and eliminated loot
        // candidates be kept out of the gear JSON? Can be turned off for troubleshooting.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] EnableGearSaveFilter
        private static MelonPreferences_Entry<bool> s_PrefEnableGearSaveFilter;

        public static bool IsGearSaveFilterEnabled => s_PrefEnableGearSaveFilter?.Value ?? true;

        // VERBOSE LOGGING.
        //
        // PERFORMANCE: with this flag on, the mod writes a line per object on every
        // save/load step (e.g. [GEAR-TEST] for EVERY item in a building). Because
        // MelonLogger writes SYNCHRONOUSLY to the console and to file, and each line
        // allocates an interpolated string, on a 15-building map this alone accounted for
        // a large part of the stall while saving.
        //
        // It therefore defaults to OFF. While troubleshooting:
        //   UserData\MelonPreferences.cfg -> [SeamlessInteriors] VerboseLogging = true
        // or F7 in game.
        private static MelonPreferences_Entry<bool> s_PrefVerboseLogging;

        public static int InteriorLightingMode => s_PrefInteriorLightingMode?.Value ?? 0;

        public static bool IsDarkAtmosphereMode => InteriorLightingMode == 1;

        public override void OnInitializeMelon()
        {
            s_PrefCategory = MelonPreferences.CreateCategory("SeamlessInteriors", "Seamless Interiors Settings");
            s_PrefInteriorLightingMode = s_PrefCategory.CreateEntry<int>(
                "InteriorLightingMode",
                0,
                "Interior Lighting Mode",
                "0 = Outdoor Lighting (bright, exterior-synced) | 1 = Dark Atmosphere (original indoor ambiance)"
            );

            s_PrefEnableInitialLootRoll = s_PrefCategory.CreateEntry<bool>(
                "EnableInitialLootRoll",
                true,
                "Enable Initial Loot Roll",
                "Runs the game's own RandomSpawnObject elimination once per save so cloned interiors get the correct amount of loot. Turn off only for troubleshooting."
            );

            s_PrefEnableGearSaveFilter = s_PrefCategory.CreateEntry<bool>(
                "EnableGearSaveFilter",
                true,
                "Enable Gear Save Filter",
                "Keeps container contents, looted items and eliminated loot candidates out of the cloned-interior gear save file. Turn off only for troubleshooting."
            );

            s_PrefVerboseLogging = s_PrefCategory.CreateEntry<bool>(
                "VerboseLogging",
                false,
                "Verbose Logging",
                "Writes a log line per object during save/load. Costs noticeable frame time on maps with many interiors - turn on only for troubleshooting (F7 toggles it in-game)."
            );
            s_DebugBounds = s_PrefVerboseLogging.Value;

            // Flush the settings to disk right away: otherwise newly added keys only reach
            // MelonPreferences.cfg when the game shuts down CLEANLY - after a crash they
            // never appear in the file at all.
            MelonPreferences.Save();

            MelonLogger.Msg($"[SETTINGS] Interior Lighting Mode: {(IsDarkAtmosphereMode ? "Dark Atmosphere" : "Outdoor Lighting")}");
            MelonLogger.Msg($"[SETTINGS] Initial Loot Roll: {(IsInitialLootRollEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Gear Save Filter: {(IsGearSaveFilterEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Verbose Logging: {(s_DebugBounds ? "ON" : "OFF")} (F7)");
        }

        public override void OnUpdate()
        {
            // Determine the owner of the global lighting (the clone the player is inside)
            // every frame. The light patches consult that result to decide who may write
            // the global ambient.
            ClonedInteriorLightingGuard.Tick();

            // ─── F8: SWITCH INTERIOR LIGHTING MODE ───
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8))
            {
                int newMode = IsDarkAtmosphereMode ? 0 : 1;
                s_PrefInteriorLightingMode.Value = newMode;
                MelonPreferences.Save();

                string modeName = newMode == 1 ? "Dark Atmosphere" : "Outdoor Lighting";
                s_LightingModeMessage = $"Interior Lighting: {modeName}\n(Effective after next scene load)";
                s_LightingModeMessageTimer = 4f;

                MelonLogger.Msg($"[SETTINGS] Interior Lighting Mode changed to: {modeName}");
            }

            // ─── F7: TOGGLE VERBOSE LOGGING ───
            // Verbose logging writes a line per object during save/load and costs visible
            // frame time, which is why it defaults to off.
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
            {
                s_DebugBounds = !s_DebugBounds;
                if (s_PrefVerboseLogging != null)
                {
                    s_PrefVerboseLogging.Value = s_DebugBounds;
                    MelonPreferences.Save();
                }

                s_LightingModeMessage = $"Verbose Logging: {(s_DebugBounds ? "ON" : "OFF")}";
                s_LightingModeMessageTimer = 3f;
                MelonLogger.Msg($"[SETTINGS] Verbose Logging: {(s_DebugBounds ? "ON" : "OFF")}");
            }

            // ─── F9: INTERACTION DIAGNOSTICS ───
            // Dumps the colliders in front of the crosshair and the active/collider/layer
            // state of nearby items to the MelonLoader log, to find the cause of
            // "I can see the item but cannot pick it up".
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                try { DiagnoseInteractivity(); }
                catch (System.Exception ex) { MelonLogger.Warning($"[TESHIS] Hata: {ex}"); }
            }

            // ─── F10: INTERACTION REPAIR (manual) ───
            // Re-enables item colliders left disabled in the clone the player is inside.
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
            {
                try
                {
                    int repaired = RepairInteractivityForPlayerInstance(true);
                    s_LightingModeMessage = $"Etkilesim onarimi: {repaired} collider geri acildi";
                    s_LightingModeMessageTimer = 4f;
                }
                catch (System.Exception ex) { MelonLogger.Warning($"[ETKILESIM-ONARIM] Hata: {ex}"); }
            }

            // Tick down the on-screen notification timer.
            if (s_LightingModeMessageTimer > 0f)
                s_LightingModeMessageTimer -= UnityEngine.Time.deltaTime;

            Transform playerT = Il2Cpp.GameManager.GetPlayerTransform();
            if (playerT == null) return;
            Vector3 pos = playerT.position;

            // Keep each building's terrain hole in sync with whether the player is inside it.
            foreach (var instance in ActiveInteriors.Values)
            {
                if (!instance.RunCompleted || instance.MasterInterior == null) continue;

                // PRE-FILTER: with the clone scene closed the player definitely is not
                // inside it. The terrain hole will not be open either, so there is nothing
                // to do - no need to enter IsPositionInside/SetTerrainHoleState at all.
                // (On a 15-building map this line removes 14 pointless calls per frame.)
                if (!instance.MasterInterior.activeSelf)
                {
                    if (IsTerrainHoleOpen(instance))
                        SetTerrainHoleState(instance, false);
                    continue;
                }

                // Check every frame whether the player is inside the actual clone scene.
                bool isInsideScene = instance.IsPositionInside(pos);
                SetTerrainHoleState(instance, isInsideScene);
            }
        }

        public override void OnLateUpdate()
        {
            ClonedInteriorLightingGuard.EnforceOwnerAmbient();
        }

        // Every building handled by the mod, keyed by InstanceId.
        public static Dictionary<string, SeamlessInteriorInstance> ActiveInteriors = new Dictionary<string, SeamlessInteriorInstance>();

        // LOADING SCREEN EXTENSION: holds the screen black (CameraFade) until the mod has
        // loaded every instance. While this flag is true the screen is blacked out; it is
        // released once all instances are done.
        public static bool s_ScreenHeldBlack = false;

        // LOADING OVERLAY: draws a "Loading..." label on top of the black screen (OnGUI).
        private static string s_LoadingDots = "Loading...";
        private static float s_LoadingDotsTimer = 0f;
        private static int s_LoadingDotsIndex = 0;

        // LIGHTING MODE NOTIFICATION (shown on screen when toggled with F8).
        private static string s_LightingModeMessage = "";
        private static float s_LightingModeMessageTimer = 0f;

        // The SupportedInteriors config list lives in SeamlessInteriorsMod.Configs.cs.

        // GENERAL SETTINGS (shared by all interiors).
        // After a door transition the watchdog stays out of the way for this long.
        public const float PORTAL_SUPPRESS_WINDOW = 2f;
        public static float s_LastPortalUseTime = -10f;

        public static bool s_DebugBounds = false;

        // ─── Authority over "is the player inside" after a load ───
        //
        // PROBLEM: the bounds fallback in IsPositionInside can give a false positive right
        // in front of a door (the InteriorTrigger volume is expanded by (1,3,1) and only
        // shrunk by 0.5). A player who saved outside, in front of the door, got the
        // interior shown as visible during loading.
        //
        // FIX: in the first seconds after a load, the saved "player is inside" information
        // is MORE RELIABLE than the geometric test. The save file knows exactly where the
        // player saved. That authority holds until the player uses a door (or the window
        // expires).
        private const float LOAD_STATE_AUTHORITY_WINDOW = 20f;
        private static bool s_LoadStateAuthorityActive = false;
        private static float s_LoadStateAuthorityExpireTime = 0f;

        public static void NotifyPortalUsed()
        {
            s_LastPortalUseTime = Time.time;
            s_LoadStateAuthorityActive = false;
        }

        private static bool IsLoadStateAuthoritative()
        {
            if (!s_LoadStateAuthorityActive) return false;

            if (Time.time > s_LoadStateAuthorityExpireTime)
            {
                s_LoadStateAuthorityActive = false;
                return false;
            }

            return HasSavedPlayerInsideState();
        }

        public static bool ResolvePlayerInside(SeamlessInteriorInstance instance, Vector3 pos)
        {
            bool geometric = instance.IsPositionInside(pos);

            if (!IsLoadStateAuthoritative()) return geometric;

            if (GetSavedPlayerInsideInstanceId() == instance.Config.ResolvedInstanceId)
            {
                // The save says the player was inside THIS building. Count them as inside
                // even if geometry misses - but they do have to be near the building, so a
                // stale save after a region change cannot open a distant house.
                return geometric || instance.IsPositionInVolume(pos, 2.0f);
            }

            // The save says the player was NOT inside this building. Do not trust the
            // doorway bounds false positive - the save file is right.
            if (geometric && s_DebugBounds)
                MelonLogger.Msg($"[LOAD-STATE] {instance.Config.ResolvedInstanceId}: geometri 'iceride' dedi ama kayit 'disarida' diyor, kayda uyuluyor.");

            return false;
        }

        // Optimization: cache UniStorm instead of calling FindObjectOfType every frame.
        private static Il2Cpp.UniStormWeatherSystem s_CachedUniStorm = null;
        public static Il2Cpp.UniStormWeatherSystem GetCachedUniStorm()
        {
            if (s_CachedUniStorm == null)
                s_CachedUniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
            return s_CachedUniStorm;
        }

        // Lock that stops several scene-loading coroutines from running at once.
        // Instances sharing an InteriorSceneBaseName load one after another: the first
        // loads the scene and stores it as a template, the rest copy it.
        private static bool s_SceneLoadLock = false;

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // Is this a region scene the mod cares about?
            bool isSupportedExteriorScene = false;
            foreach (var cfg in SupportedInteriors)
            {
                if (sceneName == cfg.ExteriorSceneName) { isSupportedExteriorScene = true; break; }
            }

            // Reset the caches - BUT only on a real region change, or while the load window
            // is closed.
            //
            // CRITICAL: this callback also fires for ADDITIVE scene loads, including the
            // mod's OWN interior scenes (CampOffice, CampOffice_SANDBOX, CampOffice_DLC01
            // ... more than 30 per region). Resetting unconditionally would close the
            // shared scan window in the middle of loading, and the scene-wide cleanups
            // would again repeat per building.
            if (isSupportedExteriorScene || !SceneScan.InLoadPhase)
            {
                SceneScan.InvalidateAll();
                PlayerRefs.Reset();
                ResetSceneWideCleanupState();
            }

            // New-game check: clear the "player is inside" data left over from the previous
            // save. Without this the player spawns at the wrong spot inside a clone scene.
            //
            // CRITICAL: this check must only run in a real region scene and while no restore
            // is in progress. In the intermediate scenes that appear while a save loads,
            // TimeOfDay has not been restored yet and GetHoursPlayedNotPaused() returns 0;
            // that is why the old code mistook a save load for a "new game" and DELETED the
            // PlayerInside data. With it gone, savedInsideId stays empty, EARLY-FLAG does
            // not run and Wind.Start starts as if the player were outside -> the wind sound
            // disappears.
            bool restoreInProgress = false;
            try
            {
                restoreInProgress = SaveGameSystem.IsRestoreInProgress() || SaveGameSystem.IsSceneRestoreInProgress();
            }
            catch { }

            var tod = GameManager.GetTimeOfDayComponent();
            bool looksLikeNewGame = isSupportedExteriorScene && !restoreInProgress
                                    && tod != null && tod.GetHoursPlayedNotPaused() < 0.05f;

            if (!looksLikeNewGame && s_DebugBounds && tod != null && tod.GetHoursPlayedNotPaused() < 0.05f)
            {
                MelonLogger.Msg($"[NEW GAME] Temizleme ATLANDI (scene={sceneName}, supported={isSupportedExteriorScene}, restore={restoreInProgress}).");
            }

            if (looksLikeNewGame)
            {
                // The saved local position has to go too; clearing only insideKey would
                // leave the old clone-scene point in PlayerPrefs and teleport the player there.
                ClearSavedPlayerInsideState();
                if (s_DebugBounds)
                    MelonLogger.Msg($"[NEW GAME] Onceki save'den kalan PlayerInside bilgisi temizlendi.");
            }

            // Find out which clone scene the player saved in (for the early activation).
            string savedInsideId = GetSavedPlayerInsideInstanceId();

            // Inside the post-load window the saved state outranks the geometric test.
            // (Stops the interior from starting visible when the player saved outside,
            //  right in front of the door.)
            s_LoadStateAuthorityActive = true;
            s_LoadStateAuthorityExpireTime = Time.time + LOAD_STATE_AUTHORITY_WINDOW;

            // CRITICAL: if the player saved inside a clone scene, set the flags IMMEDIATELY.
            // Wind.Start and other patches read this flag - too late and the wind/audio are
            // started as if the player were outside.
            if (!string.IsNullOrEmpty(savedInsideId))
            {
                s_IsPlayerInsideClone = true;
                SetAudioOcclusion(true);
                if (s_DebugBounds)
                    MelonLogger.Msg($"[EARLY-FLAG] Oyuncu icerde kaydetti ({savedInsideId}), flag'ler erken set edildi.");
            }

            // Count how many instances this scene will load - needed for the screen blackout.
            int expectedInstanceCount = 0;
            foreach (var config in SupportedInteriors)
            {
                if (sceneName == config.ExteriorSceneName)
                    expectedInstanceCount++;
            }

            // At least one instance to load: hold the screen black until the mod is done.
            if (expectedInstanceCount > 0)
            {
                // Open the load window: for its duration the expensive scene scans (active
                // Renderer list, name index) are SHARED by all buildings - one scan instead
                // of 15 buildings scanning 15 times.
                SceneScan.SetLoadPhase(true);

                s_ScreenHeldBlack = true;
                CameraFade.FadeOut(0f, 0f, null);
                ShowLoadingOverlay();
                if (s_DebugBounds)
                    MelonLogger.Msg($"[SCREEN-HOLD] Ekran karartildi, {expectedInstanceCount} instance yuklenecek.");

                // Safety: force the screen open if the mod is not done within 60 seconds.
                MelonCoroutines.Start(ScreenHoldFailsafe());
            }

            // Separate the priority instance (the one the player is inside) from the rest.
            SeamlessInteriorInstance priorityInstance = null;
            var deferredConfigs = new List<InteriorConfig>();

            foreach (var config in SupportedInteriors)
            {
                if (sceneName != config.ExteriorSceneName) continue;

                string key = config.ResolvedInstanceId;
                if (!ActiveInteriors.ContainsKey(key))
                    ActiveInteriors.Add(key, new SeamlessInteriorInstance(config));

                var instance = ActiveInteriors[key];
                bool playerSavedInside = !string.IsNullOrEmpty(savedInsideId) && savedInsideId == key;

                if (playerSavedInside)
                {
                    // This instance has priority - start it immediately.
                    if (instance.InteriorPersisted && instance.MasterInterior != null)
                    {
                        instance.MasterInterior.SetActive(true);
                        if (s_DebugBounds)
                            MelonLogger.Msg($"[EARLY-ACTIVATE] Persist akisi: {key} erken aktif edildi (oyuncu icerde kaydetti).");
                        MelonCoroutines.Start(ReattachPersistedInterior(instance, true));
                    }
                    else if (!instance.RunCompleted)
                    {
                        priorityInstance = instance;
                        MelonCoroutines.Start(FadeScreenForInteriorLoad(instance));
                        MelonCoroutines.Start(WaitForPlayerThenRun(instance, true));
                    }
                }
                else
                {
                    // This instance is deferred.
                    deferredConfigs.Add(config);

                    // Persist flows are never deferred.
                    if (instance.InteriorPersisted && instance.MasterInterior != null)
                    {
                        MelonCoroutines.Start(ReattachPersistedInterior(instance, false));
                        deferredConfigs.Remove(config);
                    }
                }
            }

            // Start the non-priority instances with a delay.
            if (deferredConfigs.Count > 0)
            {
                MelonCoroutines.Start(StartDeferredInstances(deferredConfigs, priorityInstance));
            }
        }

        private IEnumerator StartDeferredInstances(List<InteriorConfig> configs, SeamlessInteriorInstance priorityInstance)
        {
            // Wait for the priority instance to finish, if there is one.
            if (priorityInstance != null)
            {
                float maxWait = 30f;
                float waited = 0f;
                while (!priorityInstance.RunCompleted && waited < maxWait)
                {
                    yield return new WaitForSeconds(0.5f);
                    waited += 0.5f;
                }
            }

            foreach (var config in configs)
            {
                string key = config.ResolvedInstanceId;
                if (!ActiveInteriors.ContainsKey(key)) continue;
                var instance = ActiveInteriors[key];
                if (!instance.RunCompleted && !instance.InteriorPersisted)
                {
                    MelonCoroutines.Start(WaitForPlayerThenRun(instance, false));
                }
            }
        }

        private IEnumerator ScreenHoldFailsafe()
        {
            float maxWait = 60f;
            float waited = 0f;
            while (s_ScreenHeldBlack && waited < maxWait)
            {
                yield return new WaitForSeconds(1f);
                waited += 1f;
            }

            if (s_ScreenHeldBlack)
            {
                MelonLogger.Warning($"[SCREEN-FAILSAFE] 60 saniye doldu, ekran zorla aciliyor!");
                s_ScreenHeldBlack = false;
                HideLoadingOverlay();
                CameraFade.FadeIn(0.5f, 0f, null);
            }

            // The load window has to be closed here too: if TryBatchUpdateEnvironment never
            // completes, stale scan results must not be used for the rest of the session.
            SceneScan.SetLoadPhase(false);
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            // Scene change: clear the screen blackout flag (it may have been left set).
            s_ScreenHeldBlack = false;
            HideLoadingOverlay();

            // The scene is being torn down: the object references in the caches are dead.
            //
            // The load window is closed ONLY when a real region scene unloads. The mod's own
            // additive scenes (the interior scenes left empty after the clone is made) also
            // trigger this callback, and closing the window for them would waste the shared
            // scans.
            bool unloadedSupportedExterior = false;
            foreach (var cfg in SupportedInteriors)
            {
                if (sceneName == cfg.ExteriorSceneName) { unloadedSupportedExterior = true; break; }
            }

            if (unloadedSupportedExterior || !SceneScan.InLoadPhase)
            {
                SceneScan.InvalidateAll();
                PlayerRefs.Reset();
                PruneStrippedMaterialCache();
            }
            else
            {
                SceneScan.InvalidateVolatile();
            }

            // Reset the light ownership state: nobody owns it in the new scene, the outside
            // world writes.
            ClonedInteriorLightingGuard.Reset();

            // On a scene change, reset the mod's occlusion bool AND GameAudioManager's real
            // occlusion counters. Resetting only the bool was not enough: since
            // GameAudioManager survives scene changes, an unbalanced Enter/Exit counter
            // leaves the audio permanently muffled or inaudible.
            ResetAudioOcclusionCounters("unload");

            string savedIdOnUnload = GetSavedPlayerInsideInstanceId();
            if (string.IsNullOrEmpty(savedIdOnUnload))
            {
                s_IsPlayerInsideClone = false;
            }
            else if (s_DebugBounds)
            {
                MelonLogger.Msg($"[UNLOAD] Oyuncu icerde kaydetti ({savedIdOnUnload}), s_IsPlayerInsideClone korundu.");
            }

            s_CachedUniStorm = null;

            // Handle only the buildings that belong to this scene.
            foreach (var instance in ActiveInteriors.Values)
            {
                if (sceneName == instance.Config.ExteriorSceneName)
                {
                    if (instance.RunCompleted && instance.MasterInterior != null)
                    {
                        // PERSIST: keep the finished clone alive across the scene change so
                        // it does not have to be rebuilt from scratch on the way back.
                        UnityEngine.Object.DontDestroyOnLoad(instance.MasterInterior);
                        instance.MasterInterior.SetActive(false);
                        instance.InteriorPersisted = true;

                        if (s_DebugBounds)
                            MelonLogger.Msg($"[PERSIST] {instance.Config.InteriorSceneBaseName} DontDestroyOnLoad'a tasindi.");

                        // Scene-bound references are dead; only the clone itself survives.
                        instance.ExteriorShell = null;
                        instance.WatchdogStarted = false;
                        if (instance.CustomKillers != null) instance.CustomKillers.Clear();

                        // NOTE: ResetWeatherParticles will take an instance parameter later.
                        // ResetWeatherParticles(instance);
                        continue;
                    }

                    instance.RunCompleted = false;
                    instance.ExteriorShell = null;
                    instance.MasterInterior = null;
                    instance.InteriorTrigger = null;
                    instance.WatchdogStarted = false;
                    instance.InteriorPersisted = false;

                    // The clone was destroyed, so the references to the hidden junk objects
                    // are dead. The new clone is built from scratch and its state comes from
                    // the save file.
                    instance.JunkCleared = false;
                    instance.JunkClearedObjects.Clear();

                    if (instance.CustomKillers != null) instance.CustomKillers.Clear();
                    // ResetWeatherParticles(instance);
                }
            }

            // Clear the template cache: templates loaded for this scene are now invalid.
            var keysToRemove = new List<string>();
            foreach (var kvp in s_LoadedSceneTemplates)
            {
                foreach (var config in SupportedInteriors)
                {
                    if (config.InteriorSceneBaseName == kvp.Key && config.ExteriorSceneName == sceneName)
                    {
                        keysToRemove.Add(kvp.Key);
                        break;
                    }
                }
            }
            foreach (var k in keysToRemove) s_LoadedSceneTemplates.Remove(k);
        }


        // Cloning needs a valid player position, so it waits for the player to be placed
        // in the world before Run() starts.
        private IEnumerator WaitForPlayerThenRun(SeamlessInteriorInstance instance, bool playerSavedInside = false)
        {
            // Shorter wait when the player is inside - start loading straight away.
            float timeout = playerSavedInside ? 2f : 10f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null && playerT.position.sqrMagnitude > 1f) break;

                yield return null;
                elapsed += 0.5f;
            }

            MelonCoroutines.Start(Run(instance, playerSavedInside));
        }

        private IEnumerator FadeScreenForInteriorLoad(SeamlessInteriorInstance instance)
        {
            // Black out immediately (0 seconds = instant).
            // NOTE: s_ScreenHeldBlack may already have blacked the screen out with
            // BurnInBlack. Calling FadeOut anyway is harmless - they simply stack.
            Il2Cpp.CameraFade.FadeOut(0f, 0f, null);

            if (s_DebugBounds)
                MelonLogger.Msg($"[FADE] {instance.Config.ResolvedInstanceId}: Ekran karartildi, klon sahne yukleniyor...");

            // Wait for Run() to complete.
            float maxWait = 30f;
            float waited = 0f;
            while (!instance.RunCompleted && waited < maxWait)
            {
                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;
            }

            // A couple more frames, so the renderers come up.
            yield return null;
            yield return null;

            // Fade the screen back in - BUT only if s_ScreenHeldBlack is false.
            // If it is still true, TryBatchUpdateEnvironment will do it.
            if (!s_ScreenHeldBlack)
            {
                Il2Cpp.CameraFade.FadeIn(0.5f, 0f, null);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[FADE] {instance.Config.ResolvedInstanceId}: Klon sahne hazir, ekran aciliyor.");
            }
            else if (s_DebugBounds)
            {
                MelonLogger.Msg($"[FADE] {instance.Config.ResolvedInstanceId}: Klon sahne hazir ama diger instance'lar bekleniyor, ekran acilmadi.");
            }
        }

        // Brings a clone that survived the scene change (see PERSIST above) back into the
        // new region scene, instead of rebuilding it from scratch.
        private IEnumerator ReattachPersistedInterior(SeamlessInteriorInstance instance, bool playerSavedInside = false)
        {
            // Keep the wait very short when the player is inside.
            float timeout = playerSavedInside ? 2f : 10f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null && playerT.position.sqrMagnitude > 1f) break;
                yield return null;
                elapsed += 0.5f;
            }

            var exteriorScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(instance.Config.ExteriorSceneName);
            if (exteriorScene.isLoaded && instance.MasterInterior != null)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance.MasterInterior, exteriorScene);
            }

            AlignWithExteriorShell(instance);

            // Player inside: activate immediately and close the shell.
            if (playerSavedInside && instance.MasterInterior != null)
            {
                instance.MasterInterior.SetActive(true);
                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(false);
            }

            // The trigger reference was lost with the old scene; recover it from the clone.
            if (instance.InteriorTrigger == null && instance.MasterInterior != null)
            {
                var killer = instance.MasterInterior.transform.Find("ParticleKiller");
                if (killer != null)
                    instance.InteriorTrigger = killer.GetComponent<BoxCollider>();
            }

            Bounds interiorBounds = ComputeLocalInteriorBounds(instance.MasterInterior);

            // Refresh the InteriorTrigger bounds with the padded version too
            // (used by the IsPositionInside fallback).
            if (instance.InteriorTrigger != null)
            {
                Bounds expandedBounds = interiorBounds;
                expandedBounds.Expand(TRIGGER_PADDING);
                instance.InteriorTrigger.center = expandedBounds.center;
                instance.InteriorTrigger.size = expandedBounds.size;
            }

            SetupWeatherParticleKillersOnly(instance, interiorBounds);

            ApplySafehouseCustomizationFix(instance);

            instance.InteriorPersisted = false;

            // In the persist flow the clone GameObject stays alive. Since a DIFFERENT or
            // OLDER save may have been loaded in the same session, the junk state is
            // re-synchronised from the save file.
            RestoreJunkState(instance);

            InitializeVisibilityAndWatchdog(instance);

            // After a persist the player may be below the floor - correct with the saved position.
            Transform playerTFix = GameManager.GetPlayerTransform();
            if (playerTFix != null && instance.MasterInterior != null && instance.MasterInterior.activeSelf)
            {
                if (instance.IsPositionInside(playerTFix.position))
                {
                    // PRIORITY 1: use the saved local-space position
                    // (only when the save belongs to this instance - see GetSavedPlayerLocalPosition)
                    string instanceId = instance.Config.ResolvedInstanceId;
                    Vector3? savedLocalPos = GetSavedPlayerLocalPosition(instanceId);
                    Quaternion? savedLocalRot = GetSavedPlayerLocalRotation(instanceId);

                    if (savedLocalPos.HasValue)
                    {
                        Vector3 restoredWorldPos = instance.MasterInterior.transform.TransformPoint(savedLocalPos.Value);
                        Quaternion restoredWorldRot = savedLocalRot.HasValue
                            ? instance.MasterInterior.transform.rotation * savedLocalRot.Value
                            : playerTFix.rotation;

                        GameManager.GetPlayerManagerComponent().TeleportPlayer(restoredWorldPos, restoredWorldRot);
                        if (s_DebugBounds)
                            MelonLogger.Msg($"[PERSIST-FIX] Kaydedilmis local pozisyon ile duzeltildi: {restoredWorldPos}");
                    }
                    else
                    {
                        // Fallback: EnsureAboveGround
                        Vector3 correctedPos = EnsureAboveGround(playerTFix.position, instance);
                        if (correctedPos.y - playerTFix.position.y > 0.1f)
                        {
                            playerTFix.position = correctedPos;
                            if (s_DebugBounds)
                                MelonLogger.Msg($"[PERSIST-FIX] Oyuncu Y duzeltildi (fallback): {correctedPos}");
                        }
                    }
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PERSIST] Klon interior {instance.Config.ExteriorSceneName}'a geri baglandi: {instance.Config.InteriorSceneBaseName}");
        }

        // The full clone pipeline for one building: load the interior scene, clone it,
        // align it with the exterior shell, roll the loot, restore the saved data, and
        // finally hand it over to the visibility watchdog.
        private IEnumerator Run(SeamlessInteriorInstance instance, bool playerSavedInside = false)
        {
            instance.IsCloningRoutineActive = true;

            CheckNewGameLootLock(instance);

            // Wait for the scene load lock: if another coroutine is loading the same scene,
            // let it finish and store the template first.
            while (s_SceneLoadLock)
                yield return null;

            s_SceneLoadLock = true;
            yield return LoadInteriorScenes(instance);
            s_SceneLoadLock = false;
            PrepareMasterInterior(instance);
            AlignWithExteriorShell(instance);

            // EARLY ACTIVATION: if the player saved inside this scene, activate
            // MasterInterior right away and move the player to the spawn position, so they
            // do not appear to be outside until Run() completes.
            if (playerSavedInside && instance.MasterInterior != null)
            {
                instance.MasterInterior.SetActive(true);
                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(false);

                // Move the player to the position they saved at (and keep them off the floor).
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null)
                {
                    // One frame, so the colliders become active.
                    yield return null;

                    Vector3 safePos;
                    Quaternion safeRot = playerT.rotation;

                    // PRIORITY 1: the saved local-space position (most reliable)
                    // (only when the save belongs to this instance - see GetSavedPlayerLocalPosition)
                    string instanceId = instance.Config.ResolvedInstanceId;
                    Vector3? savedLocalPos = GetSavedPlayerLocalPosition(instanceId);
                    Quaternion? savedLocalRot = GetSavedPlayerLocalRotation(instanceId);

                    if (savedLocalPos.HasValue && instance.MasterInterior != null)
                    {
                        // Convert the local position into the current clone's world space.
                        safePos = instance.MasterInterior.transform.TransformPoint(savedLocalPos.Value);
                        if (savedLocalRot.HasValue)
                            safeRot = instance.MasterInterior.transform.rotation * savedLocalRot.Value;

                        if (s_DebugBounds)
                            MelonLogger.Msg($"[EARLY-ACTIVATE] Kaydedilmis local pozisyon kullanildi: local={savedLocalPos.Value} -> world={safePos}");
                    }
                    // PRIORITY 2: the EntrySpawnPosition fallback
                    else if (instance.Config.EntrySpawnPosition != Vector3.zero)
                    {
                        safePos = EnsureAboveGround(instance.Config.EntrySpawnPosition, instance);
                    }
                    // PRIORITY 3: lift the current position onto the floor
                    else
                    {
                        safePos = EnsureAboveGround(playerT.position, instance);
                    }

                    GameManager.GetPlayerManagerComponent().TeleportPlayer(safePos, safeRot);
                }

                if (s_DebugBounds)
                    MelonLogger.Msg($"[EARLY-ACTIVATE] Run akisi: {instance.Config.ResolvedInstanceId} erken aktif edildi, oyuncu iceri spawn edildi.");
            }

            HandleInitialPlaceables(instance);
            if (!playerSavedInside)
                yield return new WaitForSeconds(0.5f);
            else
                yield return null;

            ProcessSpawnsAndDeduplication(instance);

            DisableInteriorContainerSerialization(instance.MasterInterior);
            RestoreSceneSaveData(instance);
            yield return null;

            // Activate the scene invisibly (so container Awake fires) with the renderers
            // switched off, so the player does not notice.
            if (instance.MasterInterior != null && !instance.MasterInterior.activeSelf)
            {
                // LOOT ROLL - BEFORE SetActive, while the clone scene is still inactive.
                //
                // It only toggles candidate GameObjects (see PerformInitialLootRoll); it
                // touches no internal game state and does NOT change the flow from here on.
                // Once the roll is done the "loot generated" flag is written, so when
                // RandomSpawnObject.Start() runs during the SetActive below, the existing
                // RandomSpawnBlockerPatch meets it as usual.
                PerformInitialLootRoll(instance);

                // isAlreadyGenerated cannot be used here: ProcessSpawnsAndDeduplication may
                // already have set saveKey to 1. The existence of the gear JSON file is
                // checked instead - if the file is there, this was saved before.
                //
                // CAREFUL: this block must stay CONDITIONAL. Making it unconditional (i.e.
                // destroying the component here on the first generation too) lets the RSO
                // GameObjects survive - in the original flow those objects are deleted
                // entirely by RandomSpawnBlockerPatch at Start(). That difference crashed
                // the game natively on the first autosave.
                string gearJsonPath = GetInactiveSceneGearSavePath(instance);
                bool hasExistingSave = gearJsonPath != null && System.IO.File.Exists(gearJsonPath);

                if (hasExistingSave)
                {
                    var allRSO = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.RandomSpawnObject>(true);
                    int rsoCount = 0;
                    foreach (var rso in allRSO)
                    {
                        if (rso != null)
                        {
                            UnityEngine.Object.DestroyImmediate(rso);
                            rsoCount++;
                        }
                    }
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[RSO-CLEANUP] {instance.Config.InteriorSceneBaseName}: {rsoCount} RSO component SetActive oncesi yok edildi.");
                }

                foreach (var r in instance.MasterInterior.GetComponentsInChildren<Renderer>(true))
                    if (r != null) r.enabled = false;

                instance.MasterInterior.SetActive(true);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-INIT] {instance.Config.InteriorSceneBaseName} gorunmez olarak aktif edildi (container Awake tetikleme).");
            }

            // Give container Awake time to fire.
            // Minimal wait when the player is inside; the normal delay for other scenes.
            if (!playerSavedInside)
                yield return new WaitForSeconds(3f);
            else
                yield return new WaitForSeconds(0.5f);

            // NOTE: do NOT enable the renderers yet - lightmap stripping and the
            // environment fix come first. With the renderers on, StripBakedLightmaps would
            // let the player see the broken lighting.

            Bounds interiorBounds = ComputeLocalInteriorBounds(instance.MasterInterior);

            SetupWeatherAndParticles(instance, interiorBounds);

            AutoResolveOverlappingExternalObjects(instance);

            SetupSolidPerimeter(instance, interiorBounds);

            // Lightmap stripping - the environment fix itself is now done in one batch once
            // all instances are finished.
            StripBakedLightmaps(instance);

            // Player is in this scene: bring the renderers and the environment up
            // immediately, no waiting.
            if (playerSavedInside && instance.MasterInterior != null)
            {
                UpdateGlobalEnvironment(instance);
                foreach (var r in instance.MasterInterior.GetComponentsInChildren<Renderer>(true))
                    if (r != null) r.enabled = true;

                // Renderers were enabled unconditionally, so the colliders must be too,
                // otherwise items are visible but non-interactive
                // (see SeamlessInteriorsMod.Interactivity.cs).
                RestoreInteriorItemColliders(instance);
            }

            // Otherwise the renderers stay off - they come up in the batched
            // UpdateGlobalEnvironment call.
            instance.IsCloningRoutineActive = false;
            instance.RunCompleted = true;

            // Check whether all instances are ready; if so, run the batched environment fix.
            MelonCoroutines.Start(TryBatchUpdateEnvironment());

            // Scene-wide cleanups: these run once per scene, not once per building
            // (see ResetSceneWideCleanupState).
            CleanupOrphanPlaceables();
            CleanupLostAndFoundBoxes();

            ApplySafehouseCustomizationFix(instance);

            RestorePlaceablePositions(instance);

            // Restore the gear that went missing, from JSON.
            RestoreInactiveSceneGearItems(instance);

            // Restore the container data (the items inside them).
            RestoreContainerData(instance);

            // Restore the state of broken / harvested / opened objects. The game's own
            // global save cannot match the clone scene by position or guid, so this lives
            // in a separate JSON.
            RestoreInteractiveState(instance);

            // Restore the safehouse "clear junk" (R) state. The game's own flag is per
            // scene so it does not cover the clone, and cleared junk came back on every load.
            RestoreJunkState(instance);

            InitializeVisibilityAndWatchdog(instance);

            // Fix the wind audio once Run() finishes in a new save.
            MelonCoroutines.Start(FixWindAfterRun(instance));
        }

        // FixWindAfterRun moved to SeamlessInteriorsMod.Weather.cs.
        // EnsureAboveGround moved to SeamlessInteriorsMod.Utility.cs.

        // ─── LOADING OVERLAY (OnGUI based) ───

        // OnGUI runs every frame (twice, in fact: Layout + Repaint). Creating GUIStyle
        // objects there would mean several allocations per frame, so they are built once
        // and refreshed when the screen size changes.
        private static GUIStyle s_NotifyStyle;
        private static GUIStyle s_LoadingStyle;
        private static int s_GuiStyleScreenHeight = -1;

        private static void EnsureGuiStyles()
        {
            if (s_NotifyStyle != null && s_GuiStyleScreenHeight == Screen.height) return;
            s_GuiStyleScreenHeight = Screen.height;

            s_NotifyStyle = new GUIStyle(GUI.skin.label);
            s_NotifyStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.022f);
            s_NotifyStyle.alignment = TextAnchor.UpperCenter;
            s_NotifyStyle.fontStyle = FontStyle.Bold;

            s_LoadingStyle = new GUIStyle(GUI.skin.label);
            s_LoadingStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.026f); // ~28px @ 1080p
            s_LoadingStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 0.85f);
            s_LoadingStyle.alignment = TextAnchor.LowerRight;
            s_LoadingStyle.padding = new RectOffset(0, 30, 0, 20);
        }

        public override void OnGUI()
        {
            // Nothing to draw: bail out in a single line - the common case.
            bool showNotify = s_LightingModeMessageTimer > 0f && !string.IsNullOrEmpty(s_LightingModeMessage);
            if (!showNotify && !s_ScreenHeldBlack) return;

            EnsureGuiStyles();

            // ─── LIGHTING MODE NOTIFICATION ───
            if (showNotify)
            {
                float alpha = Mathf.Clamp01(s_LightingModeMessageTimer); // fades out over the last second
                GUIStyle notifyStyle = s_NotifyStyle;
                notifyStyle.normal.textColor = new Color(1f, 0.85f, 0.4f, alpha);

                float boxW = Screen.width * 0.4f;
                float boxH = Screen.height * 0.08f;
                float boxX = (Screen.width - boxW) / 2f;
                float boxY = Screen.height * 0.15f;

                // Semi-transparent background.
                GUI.color = new Color(0f, 0f, 0f, 0.6f * alpha);
                GUI.DrawTexture(new Rect(boxX, boxY, boxW, boxH), Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUI.Label(new Rect(boxX, boxY + 5, boxW, boxH), s_LightingModeMessage, notifyStyle);
            }

            // ─── LOADING OVERLAY ───
            if (!s_ScreenHeldBlack) return;

            // Advance the dot animation (every 0.5 seconds).
            s_LoadingDotsTimer += Time.deltaTime;
            if (s_LoadingDotsTimer >= 0.5f)
            {
                s_LoadingDotsTimer = 0f;
                s_LoadingDotsIndex = (s_LoadingDotsIndex + 1) % 3;
                switch (s_LoadingDotsIndex)
                {
                    case 0: s_LoadingDots = "Loading."; break;
                    case 1: s_LoadingDots = "Loading.."; break;
                    case 2: s_LoadingDots = "Loading..."; break;
                }
            }

            // Full-screen black background.
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            // Text area - the whole screen, aligned to the bottom right corner.
            GUI.color = Color.white;
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), s_LoadingDots, s_LoadingStyle);
        }

        private static void ShowLoadingOverlay()
        {
            s_LoadingDots = "Loading...";
            s_LoadingDotsTimer = 0f;
            s_LoadingDotsIndex = 0;
        }

        private static void HideLoadingOverlay()
        {
            s_LoadingDotsTimer = 0f;
            s_LoadingDotsIndex = 0;
        }
    }
}
