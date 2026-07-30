using MelonLoader;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Il2Cpp;

[assembly: MelonInfo(typeof(SeamlessInteriors.SeamlessInteriorsMod), "SeamlessInteriors", "v1.1.0", "Hamsi Buglama")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod : MelonMod
    {
        public const string EXTERIOR = "LakeRegion";
        public const string INTERIOR = "CampOffice";

        public static bool s_RunCompleted = false;
        public static bool s_IsCloningRoutineActive = false;
        public static bool s_InteriorPersisted = false;

        public static GameObject s_ExteriorShell = null;
        public static GameObject s_MasterInterior = null;
        public static BoxCollider s_InteriorTrigger = null;

        public static List<Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance> s_CustomKillers = new List<Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance>();
        public static Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance s_ParticleKiller = null;

        public const float INTERIOR_Y_OFFSET = 2f;
        public static float s_LastPortalUseTime = -10f;
        public const float PORTAL_SUPPRESS_WINDOW = 2f;
        public static bool s_DebugBounds = true;
        public static bool s_WatchdogStarted = false;
        public static bool s_IsAudioOccluded = false;

        public static System.Collections.Generic.List<Il2CppTLD.Placement.Placeable> s_PendingCampOfficePlaceables = new System.Collections.Generic.List<Il2CppTLD.Placement.Placeable>();

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == EXTERIOR)
            {
                if (s_InteriorPersisted && s_MasterInterior != null)
                {
                    MelonCoroutines.Start(ReattachPersistedInterior());
                    return;
                }
                if (s_RunCompleted) return;
                MelonCoroutines.Start(WaitForPlayerThenRun());
            }
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (sceneName == EXTERIOR)
            {
                if (s_RunCompleted && s_MasterInterior != null)
                {
                    UnityEngine.Object.DontDestroyOnLoad(s_MasterInterior);
                    s_MasterInterior.SetActive(false);
                    s_InteriorPersisted = true;

                    if (s_DebugBounds)
                        MelonLogger.Msg("[PERSIST] Klon interior DontDestroyOnLoad'a tasindi, korunuyor.");

                    s_ExteriorShell = null;
                    s_WatchdogStarted = false;
                    s_IsAudioOccluded = false;
                    if (s_CustomKillers != null) s_CustomKillers.Clear();
                    ResetWeatherParticles();
                    return;
                }

                s_RunCompleted = false;
                s_ExteriorShell = null;
                s_MasterInterior = null;
                s_InteriorTrigger = null;
                s_WatchdogStarted = false;
                s_IsAudioOccluded = false;
                s_InteriorPersisted = false;

                if (s_CustomKillers != null) s_CustomKillers.Clear();
                ResetWeatherParticles();
            }
        }


        private IEnumerator WaitForPlayerThenRun()
        {
            float timeout = 10f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null && playerT.position.sqrMagnitude > 1f) break;

                yield return null;
                elapsed += 0.5f;
            }

            MelonCoroutines.Start(Run());
        }

        // Reattaches a previously persisted (DontDestroyOnLoad) cloned interior back into
        // the freshly loaded LakeRegion scene. This path runs instead of the full Run()
        // routine once the clone already exists, since we don't want to rebuild it from
        // scratch (and duplicate/regenerate loot, placeables, etc.) every time the player
        // re-enters LakeRegion.
        private IEnumerator ReattachPersistedInterior()
        {
            float timeout = 10f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null && playerT.position.sqrMagnitude > 1f) break;
                yield return null;
                elapsed += 0.5f;
            }

            var exteriorScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(EXTERIOR);
            if (exteriorScene.isLoaded && s_MasterInterior != null)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(s_MasterInterior, exteriorScene);
            }

            s_ExteriorShell = GameObject.Find("STRSPAWN_CampOffice_Prefab");
            if (s_ExteriorShell == null)
            {
                foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                {
                    if (go.name.Contains("CampOffice") && go.name.Contains("Prefab"))
                    {
                        s_ExteriorShell = go; break;
                    }
                }
            }

            if (s_ExteriorShell != null)
            {
                Vector3 shellPos = s_ExteriorShell.transform.position;
                shellPos.y += INTERIOR_Y_OFFSET;
                s_MasterInterior.transform.position = shellPos;
                s_MasterInterior.transform.rotation = s_ExteriorShell.transform.rotation;
            }

            if (s_InteriorTrigger == null && s_MasterInterior != null)
            {
                var killer = s_MasterInterior.transform.Find("ParticleKiller");
                if (killer != null)
                    s_InteriorTrigger = killer.GetComponent<BoxCollider>();
            }

            Bounds interiorBounds = ComputeLocalInteriorBounds(s_MasterInterior);
            SetupWeatherParticleKillersOnly(interiorBounds);

            ApplySafehouseCustomizationFix();

            s_InteriorPersisted = false;
            InitializeVisibilityAndWatchdog();

            if (s_DebugBounds)
                MelonLogger.Msg("[PERSIST] Klon interior LakeRegion'a geri baglandi.");
        }

        // Main cloning routine: loads the interior scene, merges it into the exterior world,
        // sets up spawning/dedup/weather/collision, and restores any previously saved state.
        // Runs once per fresh load of LakeRegion (see WaitForPlayerThenRun / OnSceneWasInitialized).
        private IEnumerator Run()
        {
            s_IsCloningRoutineActive = true;

            CheckNewGameLootLock();

            yield return LoadInteriorScenes();
            PrepareMasterInterior();
            AlignWithExteriorShell();

            HandleInitialPlaceables();
            yield return new WaitForSeconds(0.5f);

            ProcessSpawnsAndDeduplication();

            DisableInteriorContainerSerialization(s_MasterInterior);
            RestoreSceneSaveData();
            yield return null;

            Bounds interiorBounds = ComputeLocalInteriorBounds(s_MasterInterior);

            SetupWeatherAndParticles(interiorBounds);

            SetupSolidPerimeter(interiorBounds);
            yield return null;

            StripBakedLightmaps();
            yield return null;
            UpdateGlobalEnvironment();

            s_IsCloningRoutineActive = false;
            s_RunCompleted = true;

            CleanupOrphanPlaceables();
            CleanupLostAndFoundBoxes();

            ApplySafehouseCustomizationFix();

            RestorePlaceablePositions();

            InitializeVisibilityAndWatchdog();
        }
    }
}