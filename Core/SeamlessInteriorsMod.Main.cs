using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[assembly: MelonInfo(typeof(SeamlessInteriors.SeamlessInteriorsMod), "SeamlessInteriors", "v1.1.0", "Hamsi Buglama")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod : MelonMod
    {
        // YENİ YÖNETİCİ DEĞİŞKENLERİ
        public static Dictionary<string, SeamlessInteriorInstance> ActiveInteriors = new Dictionary<string, SeamlessInteriorInstance>();

        // SupportedInteriors config listesi -> SeamlessInteriorsMod.Configs.cs dosyasına taşındı.

        // GENEL AYARLAR (Tüm mekanlar için ortak)
        public const float PORTAL_SUPPRESS_WINDOW = 2f;
        public static float s_LastPortalUseTime = -10f;
        public static bool s_DebugBounds = true;

        // OPTİMİZASYON: UniStorm cache - FindObjectOfType her frame çağırmak yerine cache'le
        private static Il2Cpp.UniStormWeatherSystem s_CachedUniStorm = null;
        public static Il2Cpp.UniStormWeatherSystem GetCachedUniStorm()
        {
            if (s_CachedUniStorm == null)
                s_CachedUniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
            return s_CachedUniStorm;
        }

        // Sahne yükleme sırasında birden fazla coroutine'in aynı anda çalışmasını engelleyen kilit.
        // Aynı InteriorSceneBaseName'e sahip instance'lar sırayla yüklenir,
        // ilki sahneyi yükler ve template olarak kaydeder, diğerleri kopyalar.
        private static bool s_SceneLoadLock = false;

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // Oyuncunun hangi klon sahnede kaydettiğini öğren (erken aktivasyon için)
            string savedInsideId = GetSavedPlayerInsideInstanceId();

            // Öncelikli instance (oyuncunun içinde olduğu) ve diğerlerini ayır
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
                    // Bu instance öncelikli — hemen başlat
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
                    // Bu instance ertelenecek
                    deferredConfigs.Add(config);

                    // Persist akışları ertelenmez
                    if (instance.InteriorPersisted && instance.MasterInterior != null)
                    {
                        MelonCoroutines.Start(ReattachPersistedInterior(instance, false));
                        deferredConfigs.Remove(config);
                    }
                }
            }

            // Öncelikli olmayan instance'ları geciktirilmiş başlat
            if (deferredConfigs.Count > 0)
            {
                MelonCoroutines.Start(StartDeferredInstances(deferredConfigs, priorityInstance));
            }
        }

        private IEnumerator StartDeferredInstances(List<InteriorConfig> configs, SeamlessInteriorInstance priorityInstance)
        {
            // Öncelikli instance varsa onun bitmesini bekle
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

            // Diğer instance'ları başlat
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

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            // UniStorm cache'i temizle
            s_CachedUniStorm = null;
            // Sadece bu sahneye ait olan binaları bul ve unload işlemlerini yap
            foreach (var instance in ActiveInteriors.Values)
            {
                if (sceneName == instance.Config.ExteriorSceneName)
                {
                    if (instance.RunCompleted && instance.MasterInterior != null)
                    {
                        UnityEngine.Object.DontDestroyOnLoad(instance.MasterInterior);
                        instance.MasterInterior.SetActive(false);
                        instance.InteriorPersisted = true;

                        if (s_DebugBounds)
                            MelonLogger.Msg($"[PERSIST] {instance.Config.InteriorSceneBaseName} DontDestroyOnLoad'a tasindi.");

                        instance.ExteriorShell = null;
                        instance.WatchdogStarted = false;
                        instance.IsAudioOccluded = false;
                        if (instance.CustomKillers != null) instance.CustomKillers.Clear();

                        // NOT: ResetWeatherParticles ileride instance parametresi alacak
                        // ResetWeatherParticles(instance); 
                        continue;
                    }

                    instance.RunCompleted = false;
                    instance.ExteriorShell = null;
                    instance.MasterInterior = null;
                    instance.InteriorTrigger = null;
                    instance.WatchdogStarted = false;
                    instance.IsAudioOccluded = false;
                    instance.InteriorPersisted = false;

                    if (instance.CustomKillers != null) instance.CustomKillers.Clear();
                    // ResetWeatherParticles(instance);
                }
            }

            // Template cache'i temizle: Bu sahneye ait yüklenen şablonlar artık geçersiz
            var keysToRemove = new List<string>();
            foreach (var kvp in s_LoadedSceneTemplates)
            {
                // Template'in ait olduğu config'leri bul
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


        private IEnumerator WaitForPlayerThenRun(SeamlessInteriorInstance instance, bool playerSavedInside = false)
        {
            // Oyuncu içerideyse bekleme süresini kısalt — hemen yüklemeye başla
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
            // Ekranı hemen karart (0 saniye = anında)
            Il2Cpp.CameraFade.FadeOut(0f, 0f, null);

            if (s_DebugBounds)
                MelonLogger.Msg($"[FADE] {instance.Config.ResolvedInstanceId}: Ekran karartildi, klon sahne yukleniyor...");

            // Run() tamamlanana kadar bekle
            float maxWait = 30f;
            float waited = 0f;
            while (!instance.RunCompleted && waited < maxWait)
            {
                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;
            }

            // Birkaç frame daha bekle (renderer'lar açılsın)
            yield return null;
            yield return null;

            // Ekranı yumuşak aç (0.5 saniye)
            Il2Cpp.CameraFade.FadeIn(0.5f, 0f, null);

            if (s_DebugBounds)
                MelonLogger.Msg($"[FADE] {instance.Config.ResolvedInstanceId}: Klon sahne hazir, ekran aciliyor.");
        }

        private IEnumerator ReattachPersistedInterior(SeamlessInteriorInstance instance, bool playerSavedInside = false)
        {
            // Oyuncu içerideyse bekleme süresini çok kısa tut
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

            // Oyuncu içerideyse hemen aktif et ve shell'i kapat
            if (playerSavedInside && instance.MasterInterior != null)
            {
                instance.MasterInterior.SetActive(true);
                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(false);
            }

            if (instance.InteriorTrigger == null && instance.MasterInterior != null)
            {
                var killer = instance.MasterInterior.transform.Find("ParticleKiller");
                if (killer != null)
                    instance.InteriorTrigger = killer.GetComponent<BoxCollider>();
            }

            Bounds interiorBounds = ComputeLocalInteriorBounds(instance.MasterInterior);

            // InteriorTrigger bounds'unu da expand'li haliye güncelle (IsPositionInside fallback için)
            if (instance.InteriorTrigger != null)
            {
                Bounds expandedBounds = interiorBounds;
                expandedBounds.Expand(new Vector3(1.0f, 3.0f, 1.0f));
                instance.InteriorTrigger.center = expandedBounds.center;
                instance.InteriorTrigger.size = expandedBounds.size;
            }

            SetupWeatherParticleKillersOnly(instance, interiorBounds);

            ApplySafehouseCustomizationFix(instance);

            instance.InteriorPersisted = false;
            InitializeVisibilityAndWatchdog(instance);

            // Persist sonrası oyuncu zeminin altında olabilir — düzelt
            Transform playerTFix = GameManager.GetPlayerTransform();
            if (playerTFix != null && instance.MasterInterior != null && instance.MasterInterior.activeSelf)
            {
                if (instance.IsPositionInside(playerTFix.position))
                {
                    Vector3 correctedPos = EnsureAboveGround(playerTFix.position, instance);
                    if (correctedPos.y - playerTFix.position.y > 0.1f)
                    {
                        playerTFix.position = correctedPos;
                        if (s_DebugBounds)
                            MelonLogger.Msg($"[PERSIST-FIX] Oyuncu Y duzeltildi: {correctedPos}");
                    }
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PERSIST] Klon interior {instance.Config.ExteriorSceneName}'a geri baglandi: {instance.Config.InteriorSceneBaseName}");
        }

        private IEnumerator Run(SeamlessInteriorInstance instance, bool playerSavedInside = false)
        {
            instance.IsCloningRoutineActive = true;

            CheckNewGameLootLock(instance);

            // Sahne yükleme kilidini bekle: Aynı sahneyi yükleyen başka bir coroutine varsa
            // onun bitip template'i kaydetmesini bekle
            while (s_SceneLoadLock)
                yield return null;

            s_SceneLoadLock = true;
            yield return LoadInteriorScenes(instance);
            s_SceneLoadLock = false;
            PrepareMasterInterior(instance);
            AlignWithExteriorShell(instance);

            // ERKEN AKTİVASYON: Oyuncu bu sahnenin içinde kaydettiyse,
            // MasterInterior'ı hemen aktif et ve oyuncuyu spawn pozisyonuna taşı.
            // Böylece Run() tamamlanana kadar oyuncu dışarıda gibi görünmez.
            if (playerSavedInside && instance.MasterInterior != null)
            {
                instance.MasterInterior.SetActive(true);
                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(false);

                // Oyuncuyu giriş pozisyonuna taşı (zeminde spawn olmasını önle)
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null)
                {
                    Vector3 safePos = instance.Config.EntrySpawnPosition;
                    if (safePos != Vector3.zero)
                    {
                        // 1 frame bekle ki collider'lar aktif olsun
                        yield return null;
                        safePos = EnsureAboveGround(safePos, instance);
                        GameManager.GetPlayerManagerComponent().TeleportPlayer(safePos, playerT.rotation);
                    }
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

            // Sahneyi görünmez şekilde aktif et (container Awake tetikleme için)
            // Rendererları kapat ki oyuncu fark etmesin
            if (instance.MasterInterior != null && !instance.MasterInterior.activeSelf)
            {
                // isAlreadyGenerated: ProcessSpawnsAndDeduplication'da saveKey 1'e set edilmiş olabilir
                // Bu yüzden gear JSON dosyasının varlığına bakıyoruz - dosya varsa daha önce kaydedilmiş demektir
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

                // Önce tüm rendererları deaktif et
                foreach (var r in instance.MasterInterior.GetComponentsInChildren<Renderer>(true))
                    if (r != null) r.enabled = false;

                instance.MasterInterior.SetActive(true);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-INIT] {instance.Config.InteriorSceneBaseName} gorunmez olarak aktif edildi (container Awake tetikleme).");
            }

            // Container Awake tetiklenmesi için biraz bekle
            // Oyuncu içerideyse minimum bekle — dışarıdaki sahneler için normal süre
            if (!playerSavedInside)
                yield return new WaitForSeconds(3f);
            else
                yield return new WaitForSeconds(0.5f);

            // NOT: Renderer'ları henüz AÇMA - önce lightmap sökme ve ortam düzeltmesi yapılacak.
            // Renderer'lar açıkken StripBakedLightmaps çalışırsa oyuncu bozuk ışıkları görür.

            Bounds interiorBounds = ComputeLocalInteriorBounds(instance.MasterInterior);

            SetupWeatherAndParticles(instance, interiorBounds);

            AutoResolveOverlappingExternalObjects(instance);

            SetupSolidPerimeter(instance, interiorBounds);

            // Lightmap sökme - ortam düzeltmesi artık tüm instance'lar bitince toplu yapılıyor
            StripBakedLightmaps(instance);

            // Oyuncu bu sahnedeyse renderer'ları ve ortamı hemen aç — bekleme yok
            if (playerSavedInside && instance.MasterInterior != null)
            {
                UpdateGlobalEnvironment(instance);
                foreach (var r in instance.MasterInterior.GetComponentsInChildren<Renderer>(true))
                    if (r != null) r.enabled = true;
            }

            // Renderer'ları henüz AÇMA - UpdateGlobalEnvironment toplu çağrıda açılacak
            instance.IsCloningRoutineActive = false;
            instance.RunCompleted = true;

            // Tüm instance'lar hazır mı kontrol et, hazırsa toplu ortam düzeltmesi yap
            MelonCoroutines.Start(TryBatchUpdateEnvironment());

            CleanupOrphanPlaceables(instance);
            CleanupLostAndFoundBoxes(); // Bu genel bir işlem, parametre almasına gerek yok

            ApplySafehouseCustomizationFix(instance);

            RestorePlaceablePositions(instance);

            // Kaybolan gear'ları JSON'dan geri yükle
            RestoreInactiveSceneGearItems(instance);

            // Konteyner verilerini geri yükle (içindeki itemlar)
            RestoreContainerData(instance);

            InitializeVisibilityAndWatchdog(instance);

            // Yeni save'de Run() bittiğinde Wind sesini düzelt
            MelonCoroutines.Start(FixWindAfterRun(instance));
        }

        // FixWindAfterRun -> SeamlessInteriorsMod.Weather.cs dosyasına taşındı.
        // EnsureAboveGround -> SeamlessInteriorsMod.Utility.cs dosyasına taşındı.
    }
}
