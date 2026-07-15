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

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == EXTERIOR)
            {
                if (s_RunCompleted) return;
                MelonCoroutines.Start(WaitForPlayerThenRun());
            }
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (sceneName == EXTERIOR)
            {
                s_RunCompleted = false;
                s_ExteriorShell = null;
                s_MasterInterior = null;
                s_InteriorTrigger = null;
                s_WatchdogStarted = false;
                s_IsAudioOccluded = false;

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

        // İŞTE YENİ, TERTEMİZ RUN METODUMUZ!
        private IEnumerator Run()
        {
            s_IsCloningRoutineActive = true;

            // 1. Spawning Sistemi: Yeni oyun ganimet kilitlerini kontrol et
            CheckNewGameLootLock();

            // 2. Çevre/Environment Sistemi: Sahneleri yükle ve birleştir
            yield return LoadInteriorScenes();
            PrepareMasterInterior();
            AlignWithExteriorShell();

            // 3. Spawning Sistemi: Yerleştirilebilir eşyaları (Placeables) hazırla
            HandleInitialPlaceables();
            yield return new WaitForSeconds(0.5f);

            // 4. Spawning Sistemi: Rastgele eşyalar (RSO) ve obje kopyalarını temizle
            ProcessSpawnsAndDeduplication();

            // 5. Kayıt Sistemi: Önceki kayıt verilerini yükle (Ateş vb.)
            RestoreSceneSaveData();
            yield return null;

            // Çevre koordinatlarını hesapla
            Bounds interiorBounds = ComputeLocalInteriorBounds(s_MasterInterior);

            // 6. Hava Durumu Sistemi: Kar ve rüzgar engelleyicileri kur
            SetupWeatherAndParticles(interiorBounds);

            // 7. Çevre Sistemi: Hayvanlar için NavMesh ve görünmez duvarları çek
            SetupSolidPerimeter(interiorBounds);
            yield return null;

            // 8. Çevre Sistemi: Işık haritalarını temizle ve dış mekana entegre et
            StripBakedLightmaps();
            yield return null;
            UpdateGlobalEnvironment();

            // İşlemler Bitti
            s_IsCloningRoutineActive = false;
            s_RunCompleted = true;

            // Son rötuşlar
            CleanupOrphanPlaceables();
            InitializeVisibilityAndWatchdog();
        }
    }
}