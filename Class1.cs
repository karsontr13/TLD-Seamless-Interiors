using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Il2CppTLD.Audio;

[assembly: MelonInfo(typeof(CampOfficeOverhaul.CampOfficeMod), "Seamless-CampOffice", "Deneme", "Emir")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace CampOfficeOverhaul
{
    public class CampOfficeMod : MelonMod
    {
        public const string EXTERIOR = "LakeRegion";
        public const string INTERIOR = "CampOffice";

        public static bool s_RunCompleted = false;
        public static bool s_IsCloningRoutineActive = false;
        public static GameObject s_ExteriorShell = null;
        public static GameObject s_MasterInterior = null;
        public static List<Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance> s_CustomKillers = new List<Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance>();

        // İç kısmın zeminden ne kadar yukarı kaldırılacağı (kar sızıntısını önlemek için).
        public const float INTERIOR_Y_OFFSET = 1.5f;

        // Gerçek görünürlük/pozisyon kontrolü için TEK kaynak: bu collider.
        public static BoxCollider s_InteriorTrigger = null;

        // Kapı geçişi sonrası watchdog'un araya girmesini engelleyen "soğuma" penceresi.
        public static float s_LastPortalUseTime = -10f;
        public const float PORTAL_SUPPRESS_WINDOW = 2f; // saniye

        // Teşhis logları. Sorun kesin çözülünce false yapabilirsin.
        public static bool s_DebugBounds = true;

        // YENİ EKLENDİ:
        public static bool s_WatchdogStarted = false;

        public static bool s_IsAudioOccluded = false;

        public static Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance s_ParticleKiller = null;

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
                s_WatchdogStarted = false; // YENİ EKLENDİ

                if (s_ParticleKiller != null)
                {
                    var uniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
                    if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
                    {
                        uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(s_ParticleKiller);
                    }
                    s_ParticleKiller = null;
                }
            }
        }

        private static void DisableInteriorContainerSerialization(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;

            var containers = interiorRoot.GetComponentsInChildren<Il2Cpp.Container>(true);
            int count = 0;
            foreach (var c in containers)
            {
                if (c == null) continue;
                c.m_DisableSerialization = true;
                count++;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[SERIALIZE-BLOCK] {count} interior container'ın serialize edilmesi engellendi.");
        }

        private static void InvalidateInteriorPlaceables(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;

            var placeables = interiorRoot.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            int count = 0;
            foreach (var p in placeables)
            {
                if (p == null) continue;
                p.m_Invalidated = true;
                count++;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PLACEABLE-BLOCK] {count} interior Placeable invalidate edildi.");
        }

        // Ses boğuklaştırmasını güvenle açıp kapatan metod
        public static void SetAudioOcclusion(bool occlude)
        {
            if (GameAudioManager.Instance == null) return;

            if (occlude && !s_IsAudioOccluded)
            {
                // Medium (Orta) boğukluk Dağcı Kulübesi hissiyatı için en idealidir. 
                // İstersen Heavy (Ağır) veya Mild (Hafif) olarak değiştirebilirsin.
                GameAudioManager.Instance.EnterOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                s_IsAudioOccluded = true;
                if (s_DebugBounds) MelonLogger.Msg("[AUDIO] Ses boğuklaştırma (Occlusion) AÇILDI.");
            }
            else if (!occlude && s_IsAudioOccluded)
            {
                GameAudioManager.Instance.ExitOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                s_IsAudioOccluded = false;
                if (s_DebugBounds) MelonLogger.Msg("[AUDIO] Ses boğuklaştırma (Occlusion) KAPATILDI.");
            }
        }

        private static void CollectInteriorPlaceableGuids(GameObject interiorRoot)
        {
            PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Clear();

            if (interiorRoot == null) return;

            var placeables = interiorRoot.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in placeables)
            {
                if (p == null) continue;
                string guid = p.m_Guid;
                if (!string.IsNullOrEmpty(guid))
                {
                    PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Add(guid);
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PLACEABLE-GUIDS] {PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Count} interior Placeable GUID'i kaydedildi.");
        }

        private static void CleanupOrphanPlaceables()
        {
            string suffix = Il2CppTLD.Placement.Placeable.SPAWNED_NAME_SUFFIX;
            if (string.IsNullOrEmpty(suffix)) suffix = " (PLACED)";

            var allPlaceables = UnityEngine.Object.FindObjectsOfType<Il2CppTLD.Placement.Placeable>(true);
            int destroyed = 0;

            foreach (var p in allPlaceables)
            {
                if (p == null || p.gameObject == null) continue;
                if (!p.gameObject.name.Contains(suffix)) continue;

                if (s_MasterInterior != null && p.transform.IsChildOf(s_MasterInterior.transform))
                    continue;

                string sceneName = p.gameObject.scene.name;
                bool isOrphan = (sceneName == "DontDestroyOnLoad" || sceneName == null || sceneName == "");

                Transform root = p.transform.root;
                if (root != null && root.name.Contains("CHARACTER_FPSPlayer"))
                    isOrphan = true;

                if (!isOrphan) continue;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[ORPHAN-CLEANUP] Siliniyor: {p.gameObject.name} | Scene: {sceneName} | Root: {root?.name}");

                UnityEngine.Object.Destroy(p.gameObject);
                destroyed++;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[ORPHAN-CLEANUP] Toplam {destroyed} orphan Placeable temizlendi.");
        }

        private static void GenerateDeterministicPDIDs(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;

            var allGear = interiorRoot.GetComponentsInChildren<Il2Cpp.GearItem>(true);
            int generatedCount = 0;

            foreach (var gear in allGear)
            {
                if (gear == null) continue;

                var guidComponent = gear.GetComponent<Il2Cpp.ObjectGuid>();
                if (guidComponent == null)
                {
                    guidComponent = gear.gameObject.AddComponent<Il2Cpp.ObjectGuid>();
                }

                if (string.IsNullOrEmpty(guidComponent.m_Guid))
                {
                    Vector3 localPos = interiorRoot.transform.InverseTransformPoint(gear.transform.position);
                    string cleanName = gear.gameObject.name.Replace("(Clone)", "").Trim();

                    string yeniPdid = $"CampOffice_{cleanName}_{localPos.x:F2}_{localPos.y:F2}_{localPos.z:F2}";
                    guidComponent.m_Guid = yeniPdid;
                    generatedCount++;
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PDID-FIX] {generatedCount} adet eksik PDID'li objeye deterministik kimlik atandi.");
        }

        private static void SpatialDeduplication(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;

            var allGearInside = interiorRoot.GetComponentsInChildren<Il2Cpp.GearItem>(true);
            var allGearOutside = new List<Il2Cpp.GearItem>();

            foreach (var g in UnityEngine.Object.FindObjectsOfType<Il2Cpp.GearItem>(true))
            {
                if (g != null && g.transform.root != interiorRoot.transform && !g.transform.IsChildOf(interiorRoot.transform))
                {
                    allGearOutside.Add(g);
                }
            }

            int deletedCount = 0;

            foreach (var insideGear in allGearInside)
            {
                if (insideGear == null) continue;

                string insideName = insideGear.gameObject.name.Replace("(Clone)", "").Trim();

                foreach (var outsideGear in allGearOutside)
                {
                    if (outsideGear == null) continue;

                    string outsideName = outsideGear.gameObject.name.Replace("(Clone)", "").Trim();

                    if (insideName == outsideName && Vector3.Distance(insideGear.transform.position, outsideGear.transform.position) < 0.05f)
                    {
                        UnityEngine.Object.Destroy(insideGear.gameObject);
                        deletedCount++;
                        break;
                    }
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[SPATIAL-DEDUPE] {deletedCount} adet obje konumsal eslesme ile silindi.");
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

        private IEnumerator Run()
        {
            s_IsCloningRoutineActive = true;

            // --- YENİ EKLENEN: YENİ OYUN KONTROLÜ VE LOOT KİLİDİ SIFIRLAMA ---
            // Eğer aynı save slotu (örneğin sandbox1) silinip tekrar açıldıysa, 
            // eski PlayerPrefs kilidi yüzünden eşyalar spawn olmaz.
            // Oyunun henüz çok başında olduğumuzu (0.05 saat = 3 dakikadan az oynandığını) 
            // tespit edersek eski kilidi kırıyoruz!
            var tod = GameManager.GetTimeOfDayComponent();
            if (tod != null && tod.GetHoursPlayedNotPaused() < 0.05f)
            {
                string keyToReset = "CampOfficeGen_" + SaveGameSystem.m_CurrentSaveName;
                UnityEngine.PlayerPrefs.SetInt(keyToReset, 0);
                UnityEngine.PlayerPrefs.Save();

                if (s_DebugBounds)
                    MelonLogger.Msg("[NEW GAME DETECTED] Eski loot üretim kilidi kırıldı! Ganimetler spawn edilecek.");
            }
            // ----------------------------------------------------------------

            var opMain = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opMain.IsDone) yield return null;
            UnityEngine.SceneManagement.Scene s_InteriorMain = opMain.Result.Scene;

            var opSandbox = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR + "_SANDBOX", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opSandbox.IsDone) yield return null;
            UnityEngine.SceneManagement.Scene s_InteriorSandbox = opSandbox.Result.Scene;

            var opDLC = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR + "_DLC01", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opDLC.IsDone) yield return null;
            UnityEngine.SceneManagement.Scene s_InteriorDLC = opDLC.Result.Scene;

            GameObject master = new GameObject("Master_CampOffice_Interior");
            s_MasterInterior = master;
            master.SetActive(false);

            UnityEngine.SceneManagement.Scene exteriorScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(EXTERIOR);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(master, exteriorScene);

            List<UnityEngine.SceneManagement.Scene> loadedScenes = new List<UnityEngine.SceneManagement.Scene>() { s_InteriorMain, s_InteriorSandbox, s_InteriorDLC };

            foreach (var scn in loadedScenes)
            {
                if (!scn.isLoaded) continue;
                foreach (GameObject rootObj in scn.GetRootGameObjects())
                {
                    if (rootObj == master) continue;
                    rootObj.transform.SetParent(master.transform, false);
                }
            }

            Transform[] masterChildren = master.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in masterChildren)
            {
                if (t == null || t.gameObject == null) continue;

                if (t.name.Contains("FX_LightShaft_B") ||
                    t.name.Contains("WindowLight") ||
                    t.name.Contains("InteriorLightingManager_Prefab") ||
                    t.name.Contains("CONTAINER_InaccessibleGear") ||
                    t.name.Contains("Daytime"))
                {
                    UnityEngine.Object.Destroy(t.gameObject);
                    continue;
                }

                if (t.name.Contains("OBJ_LakeCabinInteriorWindow"))
                {
                    t.gameObject.SetActive(false);
                }
            }

            s_ExteriorShell = GameObject.Find("STRSPAWN_CampOffice_Prefab");
            if (s_ExteriorShell == null)
            {
                foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                {
                    if (go.name.Contains("CampOffice") && go.name.Contains("Prefab"))
                    {
                        s_ExteriorShell = go;
                        break;
                    }
                }
            }

            if (s_ExteriorShell != null)
            {
                Vector3 shellPos = s_ExteriorShell.transform.position;
                shellPos.y += INTERIOR_Y_OFFSET;
                master.transform.position = shellPos;
                master.transform.rotation = s_ExteriorShell.transform.rotation;
                master.transform.localScale = new Vector3(1.05f, 0.98f, 1.05f);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[DEBUG-SHELL] ExteriorShell bulundu. Position: {s_ExteriorShell.transform.position} (Offset uygulandi: {shellPos})");
            }
            else
            {
                master.transform.position = new Vector3(1019.738f, 26.7883f + INTERIOR_Y_OFFSET, 440.6331f);
                master.transform.rotation = Quaternion.identity;
                master.transform.localScale = new Vector3(1.05f, 0.98f, 1.05f);

                if (s_DebugBounds)
                    MelonLogger.Msg("[DEBUG-SHELL] ExteriorShell bulunamadi, hardcoded konum kullaniliyor.");
            }

            //DisableInteriorContainerSerialization(master);
            InvalidateInteriorPlaceables(master);
            CollectInteriorPlaceableGuids(master);

            yield return new WaitForSeconds(0.5f);

            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            string saveKey = "CampOfficeGen_" + currentSaveName;
            bool isAlreadyGenerated = UnityEngine.PlayerPrefs.GetInt(saveKey, 0) == 1;

            if (isAlreadyGenerated)
            {
                var allGearInside = master.GetComponentsInChildren<Il2Cpp.GearItem>(true);
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

                if (s_DebugBounds)
                    MelonLogger.Msg($"[Rogue-Cleanup] {deletedRogueCount} adet kacak/yeni RSO objesi zorla temizlendi.");
            }
            else
            {
                GenerateDeterministicPDIDs(master);

                if (!string.IsNullOrEmpty(currentSaveName))
                {
                    UnityEngine.PlayerPrefs.SetInt(saveKey, 1);
                    UnityEngine.PlayerPrefs.Save();

                    if (s_DebugBounds)
                        MelonLogger.Msg($"[RSO-FLAG] {saveKey} icin loot uretimi tamamlandi, RSO'lar kalici olarak susturuldu.");
                }
            }

            SpatialDeduplication(master);
            var allGuids = UnityEngine.Object.FindObjectsOfType<Il2Cpp.ObjectGuid>(true);
            Dictionary<string, List<Il2Cpp.ObjectGuid>> guidDict = new Dictionary<string, List<Il2Cpp.ObjectGuid>>();

            foreach (var guid in allGuids)
            {
                string currentGuid = guid.m_Guid ?? guid.PDID;
                if (string.IsNullOrEmpty(currentGuid)) continue;

                if (!guidDict.ContainsKey(currentGuid))
                    guidDict[currentGuid] = new List<Il2Cpp.ObjectGuid>();

                guidDict[currentGuid].Add(guid);
            }

            foreach (var kvp in guidDict)
            {
                if (kvp.Value.Count > 1)
                {
                    foreach (var guidObj in kvp.Value)
                    {
                        if (guidObj != null && s_MasterInterior != null)
                        {
                            if (guidObj.transform.IsChildOf(s_MasterInterior.transform))
                            {
                                if (guidObj.GetComponent<Il2Cpp.GearItem>() != null)
                                {
                                    UnityEngine.Object.Destroy(guidObj.gameObject);
                                }
                            }
                        }
                    }
                }
            }

            // --- ADIM 4: Konteynerlerin doğru yüklenmesi için EXTERIOR yapıldı ---
            if (!string.IsNullOrEmpty(currentSaveName))
            {
                var interiorGearBefore = new HashSet<string>();
                var allGearInInterior = master.GetComponentsInChildren<Il2Cpp.GearItem>(true);
                foreach (var g in allGearInInterior)
                {
                    if (g == null) continue;
                    var og = g.GetComponent<Il2Cpp.ObjectGuid>();
                    if (og != null && !string.IsNullOrEmpty(og.m_Guid))
                        interiorGearBefore.Add(og.m_Guid);
                }

                // [DEĞİŞTİRİLDİ] Container fix için INTERIOR yerine EXTERIOR okutuldu
                SaveGameSystem.LoadSceneDataAdditive(currentSaveName, EXTERIOR);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-RESTORE] CampOffice save datasi uygulandi. Interior'da {interiorGearBefore.Count} GearItem vardi.");
            }

            yield return null;

            GameObject particleKillerObj = new GameObject("ParticleKiller");
            particleKillerObj.transform.SetParent(master.transform, false);
            particleKillerObj.transform.localPosition = Vector3.zero;
            particleKillerObj.transform.localRotation = Quaternion.identity;
            particleKillerObj.layer = LayerMask.NameToLayer("TriggerIgnoreRaycast");

            Bounds localBounds = ComputeLocalInteriorBounds(master);

            BoxCollider triggerBox = particleKillerObj.AddComponent<BoxCollider>();
            triggerBox.isTrigger = true;
            triggerBox.center = localBounds.center;
            triggerBox.size = localBounds.size;

            s_InteriorTrigger = triggerBox;

            s_InteriorTrigger = triggerBox;

            // --- YENİ EKLENEN: GÖRÜNMEZ FİZİKSEL KAFES VE AI ENGELLEYİCİ ---
            // Bu objeyi ayrı oluşturuyoruz ki Layer'ı Default kalsın ve fizikleri tam çalışsın.
            GameObject solidPerimeter = new GameObject("SolidPerimeter_Blocker");
            solidPerimeter.transform.SetParent(master.transform, false);
            solidPerimeter.transform.localPosition = Vector3.zero;
            solidPerimeter.transform.localRotation = Quaternion.identity;
            // Bu objeyi sadece AI/NPC katmanına atar. Oyuncu içinden hayalet gibi geçer, hayvanlar duvara toslar.
            // Not: "NPC" katmanı The Long Dark'ta genellikle 17 veya 12'dir, ama NameToLayer kullanmak en güvenlisidir.
            solidPerimeter.layer = LayerMask.NameToLayer("NPC");

            float wT = 0.5f; // Görünmez duvarın kalınlığı (Yarım metre yeterli)

            // 1. Ön Duvar (+Z)
            BoxCollider wallFront = solidPerimeter.AddComponent<BoxCollider>();
            wallFront.center = new Vector3(localBounds.center.x, localBounds.center.y, localBounds.max.z + (wT / 2f));
            wallFront.size = new Vector3(localBounds.size.x, localBounds.size.y, wT);

            // 2. Arka Duvar (-Z)
            BoxCollider wallBack = solidPerimeter.AddComponent<BoxCollider>();
            wallBack.center = new Vector3(localBounds.center.x, localBounds.center.y, localBounds.min.z - (wT / 2f));
            wallBack.size = new Vector3(localBounds.size.x, localBounds.size.y, wT);

            // 3. Sağ Duvar (+X)
            BoxCollider wallRight = solidPerimeter.AddComponent<BoxCollider>();
            wallRight.center = new Vector3(localBounds.max.x + (wT / 2f), localBounds.center.y, localBounds.center.z);
            // Köşelerde açık kalmaması için Z boyutunu duvar kalınlığı kadar uzatıyoruz:
            wallRight.size = new Vector3(wT, localBounds.size.y, localBounds.size.z + (wT * 2));

            // 4. Sol Duvar (-X)
            BoxCollider wallLeft = solidPerimeter.AddComponent<BoxCollider>();
            wallLeft.center = new Vector3(localBounds.min.x - (wT / 2f), localBounds.center.y, localBounds.center.z);
            wallLeft.size = new Vector3(wT, localBounds.size.y, localBounds.size.z + (wT * 2));

            // 5. Yapay Zeka Engeli (NavMeshObstacle)
            // Hayvanların bu duvarlara sürekli kafa atıp titreşmesini (glitch) engeller, etrafından dolandırır.
            UnityEngine.AI.NavMeshObstacle aiObstacle = solidPerimeter.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            aiObstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            aiObstacle.center = localBounds.center;
            aiObstacle.size = localBounds.size;
            aiObstacle.carving = true;
            // ----------------------------------------------------------------

            s_CustomKillers.Clear();

            var uniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
            {
                int sliceCount = 6;
                float sliceZ = localBounds.size.z / sliceCount;
                float startZ = localBounds.center.z - (localBounds.size.z / 2f) + (sliceZ / 2f);

                for (int i = 0; i < sliceCount; i++)
                {
                    var pki = new Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance();
                    pki.m_OwnerGameObject = particleKillerObj;
                    pki.m_KillsFallingSnow = true;
                    pki.m_KillsBlowingSnow = true;

                    Vector3 sliceLocalCenter = new Vector3(localBounds.center.x, localBounds.center.y, startZ + (i * sliceZ));
                    Vector3 sliceExtents = new Vector3(localBounds.size.x / 2f, localBounds.size.y / 2f, sliceZ / 2f);

                    Vector3[] corners = new Vector3[8] {
            sliceLocalCenter + new Vector3( sliceExtents.x,  sliceExtents.y,  sliceExtents.z),
            sliceLocalCenter + new Vector3( sliceExtents.x,  sliceExtents.y, -sliceExtents.z),
            sliceLocalCenter + new Vector3( sliceExtents.x, -sliceExtents.y,  sliceExtents.z),
            sliceLocalCenter + new Vector3( sliceExtents.x, -sliceExtents.y, -sliceExtents.z),
            sliceLocalCenter + new Vector3(-sliceExtents.x,  sliceExtents.y,  sliceExtents.z),
            sliceLocalCenter + new Vector3(-sliceExtents.x,  sliceExtents.y, -sliceExtents.z),
            sliceLocalCenter + new Vector3(-sliceExtents.x, -sliceExtents.y,  sliceExtents.z),
            sliceLocalCenter + new Vector3(-sliceExtents.x, -sliceExtents.y, -sliceExtents.z)
        };

                    Vector3 min = particleKillerObj.transform.TransformPoint(corners[0]);
                    Vector3 max = min;
                    for (int j = 1; j < 8; j++)
                    {
                        Vector3 wp = particleKillerObj.transform.TransformPoint(corners[j]);
                        min = Vector3.Min(min, wp);
                        max = Vector3.Max(max, wp);
                    }

                    Bounds sliceAABB = new Bounds();
                    sliceAABB.SetMinMax(min, max);
                    sliceAABB.Expand(0.2f);

                    pki.m_Bounds = sliceAABB;
                    s_CustomKillers.Add(pki);
                }
            }

            IndoorSpaceTrigger spaceTrigger = particleKillerObj.AddComponent<IndoorSpaceTrigger>();
            spaceTrigger.m_UseOutdoorLighting = true;
            spaceTrigger.m_UseOutdoorTemperature = false;
            spaceTrigger.m_AllowCampfires = true;
            spaceTrigger.m_TemperatureDeltaCelsius = 15f;
            spaceTrigger.m_ValidSafehouse = true;
            spaceTrigger.m_DontCountAsInterior = true;
            spaceTrigger.m_IgnoreCabinFever = false;
            spaceTrigger.m_TriggerID = "CustomCampOffice_Trigger";

            yield return null;

            Renderer[] allRenderersAfter = master.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderersAfter)
            {
                r.lightmapIndex = -1;
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].IsKeywordEnabled("LIGHTMAP_ON"))
                    {
                        mats[i] = UnityEngine.Object.Instantiate(mats[i]);
                        mats[i].DisableKeyword("LIGHTMAP_ON");
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }

            yield return null;

            Weather wFinal = GameManager.GetWeatherComponent();
            if (wFinal != null) wFinal.ForceOutdoorEnvironment();

            LightingManager.m_LevelLoadComplete = true;
            LightingManager.OnLevelLoadComplete();
            LightingManager.SetLightingStrengthDefault();
            UnityEngine.DynamicGI.UpdateEnvironment();

            s_IsCloningRoutineActive = false;
            s_RunCompleted = true;

            CleanupOrphanPlaceables();

            PlayerManager pmInit = GameManager.GetPlayerManagerComponent();
            if (pmInit != null && pmInit.transform.position.sqrMagnitude > 1f)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[DEBUG-INIT] pm.transform ile ilk gorunurluk senkronizasyonu. Pos: {pmInit.transform.position}");

                ApplyInitialSyncState(pmInit.transform.position);
            }
            else
            {
                if (s_DebugBounds)
                    MelonLogger.Msg("[DEBUG-INIT] pm.transform henuz gecersiz (0,0,0 veya null), GetPlayerTransform ile devam ediliyor.");

                ApplyInitialSyncState();
            }

            MelonCoroutines.Start(DelayedInitialVisibilityCheck());

            // EKLENDİ: Watchdog hiç başlatılmıyordu, bu yüzden particle killer'lar
            // sadece mod yüklenirken bir kez senkronize ediliyordu ve kapıdan
            // girip çıkarken hiç güncellenmiyordu -> içeri kar/rüzgar sızması.
            if (!s_WatchdogStarted)
            {
                s_WatchdogStarted = true;
                MelonCoroutines.Start(VisibilityWatchdog());
            }
        }

        private IEnumerator DelayedInitialVisibilityCheck()
        {
            yield return new WaitForSeconds(10f);

            if (!s_RunCompleted) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT != null)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[DEBUG-INIT-DELAYED] 10sn gecikmeli tek seferlik dogrulama. Pos: {playerT.position}");

                ApplyInitialSyncState(playerT.position);
            }
        }

        private static Bounds ComputeLocalInteriorBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool first = true;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            foreach (var r in renderers)
            {
                if (r == null) continue;

                // YENİ EKLENDİ: Shadow_Caster proxy mesh'leri devasa boyutlu ve gerçek
                // geometriyi temsil etmiyor (sadece gölge render optimizasyonu içindir).
                // Bounds hesabına dahil edilmemeli.
                if (r.gameObject.name.Contains("Shadow_Caster")) continue;

                Bounds wb = r.bounds;

                if (wb.size.x > 40f || wb.size.y > 40f || wb.size.z > 40f) continue;

                Vector3 localCenterCheck = root.transform.InverseTransformPoint(wb.center);
                if (Mathf.Abs(localCenterCheck.x) > 30f || Mathf.Abs(localCenterCheck.y) > 30f || Mathf.Abs(localCenterCheck.z) > 30f) continue;

                if (s_DebugBounds)
                {
                    MelonLogger.Msg($"[BOUNDS-DEBUG] {r.gameObject.name} | LocalCenter: {localCenterCheck} | Size: {wb.size}");
                }

                Vector3 c = wb.center;
                Vector3 e = wb.extents;
                Vector3[] corners = new Vector3[8] {
            c + new Vector3( e.x,  e.y,  e.z), c + new Vector3( e.x,  e.y, -e.z),
            c + new Vector3( e.x, -e.y,  e.z), c + new Vector3( e.x, -e.y, -e.z),
            c + new Vector3(-e.x,  e.y,  e.z), c + new Vector3(-e.x,  e.y, -e.z),
            c + new Vector3(-e.x, -e.y,  e.z), c + new Vector3(-e.x, -e.y, -e.z)
        };

                foreach (var corner in corners)
                {
                    Vector3 local = root.transform.InverseTransformPoint(corner);
                    if (first) { min = local; max = local; first = false; }
                    else { min = Vector3.Min(min, local); max = Vector3.Max(max, local); }
                }
            }

            if (first) return new Bounds(new Vector3(0, 2f, 0), new Vector3(25f, 18f, 25f));
            Bounds result = new Bounds();
            result.SetMinMax(min, max);

            if (s_DebugBounds)
                MelonLogger.Msg($"[BOUNDS-DEBUG] SONUÇ -> min={min} max={max} center={result.center} size={result.size}");

            return result;
        }

        public static bool IsPositionInsideCabin(Vector3 pos)
        {
            if (s_MasterInterior == null || s_InteriorTrigger == null) return false;

            Vector3 localPos = s_MasterInterior.transform.InverseTransformPoint(pos);

            Bounds b = new Bounds(s_InteriorTrigger.center, s_InteriorTrigger.size);
            return b.Contains(localPos);
        }

        // YENİ EKLENDİ: Sahne/save yüklendiğinde BİR KEZ çalışır. Oyuncu save anında
        // fiziksel olarak iç mekandaysa (veya dışarıdaysa), mesh durumunu buna göre
        // doğru şekilde senkronize eder. Bu, "kapıya yaklaşınca açılma" davranışından
        // FARKLIDIR -- watchdog artık mesh'e hiç dokunmuyor, sadece bu ilk senkronizasyon
        // ve gerçek kapı tıklaması (PortalMagicPatch) mesh'i değiştirebilir.
        public static void ApplyInitialSyncState(Vector3? overridePos = null)
        {
            Vector3 pos;

            if (overridePos.HasValue) pos = overridePos.Value;
            else
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) return;
                pos = playerT.position;
            }

            if (s_MasterInterior == null || s_ExteriorShell == null) return;

            bool isInside = IsPositionInsideCabin(pos);

            var uniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null && s_CustomKillers != null)
            {
                foreach (var pk in s_CustomKillers)
                {
                    uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(pk);
                    if (isInside) uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Add(pk);
                }
            }

            if (isInside)
            {
                s_MasterInterior.SetActive(true);
                s_ExteriorShell.SetActive(false);
                SetInteriorItemsVisible(true);
            }
            else
            {
                s_MasterInterior.SetActive(false);
                s_ExteriorShell.SetActive(true);
                SetInteriorItemsVisible(false);
            }

            // YENİ EKLENEN SATIR: Oyun ilk açıldığında veya save yüklendiğinde sesi duruma göre ayarla
            SetAudioOcclusion(isInside);

            if (s_DebugBounds)
                MelonLogger.Msg($"[INITIAL-SYNC] Pos: {pos} | isInside={isInside} | Mesh durumu senkronize edildi.");
        }

        public static void ApplyVisibilityState(Vector3? overridePos = null)
        {
            Vector3 pos;

            if (overridePos.HasValue) pos = overridePos.Value;
            else
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) return;
                pos = playerT.position;
            }

            if (s_MasterInterior == null || s_ExteriorShell == null) return;

            bool isInside = IsPositionInsideCabin(pos);

            // NOT: Mesh/eşya görünürlük takası buradan KALDIRILDI.
            // Artık SADECE kapı etkileşiminde (PortalMagicPatch) değişiyor.
            // Bu metod artık sadece rüzgar/kar parçacık occlusion'ını proximity'e göre günceller.

            var uniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null && s_CustomKillers != null)
            {
                foreach (var pk in s_CustomKillers)
                {
                    uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(pk);

                    if (isInside)
                    {
                        uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Add(pk);
                    }
                }
            }
        }

        private IEnumerator VisibilityWatchdog()
        {
            while (s_RunCompleted)
            {
                bool suppressed = Time.time - s_LastPortalUseTime <= PORTAL_SUPPRESS_WINDOW;

                if (s_DebugBounds)
                {
                    Transform playerTDbg = GameManager.GetPlayerTransform();
                    if (playerTDbg != null && s_MasterInterior != null)
                    {
                        Vector3 localPos = s_MasterInterior.transform.InverseTransformPoint(playerTDbg.position);
                        bool inside = IsPositionInsideCabin(playerTDbg.position);
                        MelonLogger.Msg($"[DEBUG-WATCHDOG] t={Time.time:F1} suppressed={suppressed} localPos={localPos} inside={inside}");
                    }
                }

                if (!suppressed)
                {
                    ApplyVisibilityState();
                }

                yield return new WaitForSeconds(0.3f);
            }
        }

        public static void SetInteriorItemsVisible(bool visible)
        {
            if (s_MasterInterior == null) return;

            var allGear = UnityEngine.Object.FindObjectsOfType<Il2Cpp.GearItem>(true);
            foreach (var gear in allGear)
            {
                if (gear == null || gear.gameObject == null) continue;
                if (gear.transform.IsChildOf(s_MasterInterior.transform)) continue;

                if (IsPositionInsideCabin(gear.transform.position))
                {
                    var renderers = gear.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (var r in renderers)
                    {
                        if (r != null) r.enabled = visible;
                    }

                    var colliders = gear.GetComponentsInChildren<Collider>(true);
                    foreach (var c in colliders)
                    {
                        if (c != null) c.enabled = visible;
                    }
                }
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(LoadScene), nameof(LoadScene.PerformInteraction))]
    public class PortalMagicPatch
    {
        public static bool Prefix(LoadScene __instance)
        {
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null || !CampOfficeMod.s_RunCompleted) return true;

            bool belongsToCampOffice =
                (CampOfficeMod.s_ExteriorShell != null && __instance.transform.IsChildOf(CampOfficeMod.s_ExteriorShell.transform)) ||
                (CampOfficeMod.s_MasterInterior != null && __instance.transform.IsChildOf(CampOfficeMod.s_MasterInterior.transform));

            if (CampOfficeMod.s_DebugBounds)
            {
                MelonLogger.Msg($"[DEBUG-PORTAL] Kapı: {__instance.gameObject.name} | Root: {__instance.transform.root.name} | belongsToCampOffice={belongsToCampOffice} | targetScene={__instance.m_SceneToLoad}");
            }

            if (!belongsToCampOffice) return true;

            string targetScene = __instance.m_SceneToLoad;

            CampOfficeMod.s_LastPortalUseTime = Time.time;

            if (targetScene == CampOfficeMod.INTERIOR)
            {
                if (CampOfficeMod.s_MasterInterior != null) CampOfficeMod.s_MasterInterior.SetActive(true);
                if (CampOfficeMod.s_ExteriorShell != null) CampOfficeMod.s_ExteriorShell.SetActive(false);

                LightingManager.m_LevelLoadComplete = true;
                LightingManager.OnLevelLoadComplete();
                LightingManager.SetLightingStrengthDefault();
                UnityEngine.DynamicGI.UpdateEnvironment();

                CampOfficeMod.SetInteriorItemsVisible(true);
                pm.transform.position = pm.transform.position + (GameManager.GetVpFPSCamera().transform.forward * 2f);

                // YENİ EKLENEN SATIR: İçeri girince sesi boğuklaştır
                CampOfficeMod.SetAudioOcclusion(true);

                return false;
            }

            if (targetScene == CampOfficeMod.EXTERIOR)
            {
                if (CampOfficeMod.s_MasterInterior != null) CampOfficeMod.s_MasterInterior.SetActive(false);
                if (CampOfficeMod.s_ExteriorShell != null) CampOfficeMod.s_ExteriorShell.SetActive(true);

                LightingManager.m_LevelLoadComplete = true;
                LightingManager.OnLevelLoadComplete();
                LightingManager.SetLightingStrengthDefault();
                UnityEngine.DynamicGI.UpdateEnvironment();

                CampOfficeMod.SetInteriorItemsVisible(false);
                pm.transform.position = pm.transform.position + (GameManager.GetVpFPSCamera().transform.forward * 2f);

                // YENİ EKLENEN SATIR: Dışarı çıkınca sesi normale döndür
                CampOfficeMod.SetAudioOcclusion(false);

                return false;
            }

            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(GameManager), "Awake")]
    public class PreventFakeManagerPatch
    {
        public static bool Prefix(GameManager __instance)
        {
            if (CampOfficeMod.s_IsCloningRoutineActive && __instance.gameObject.scene.name == "CampOffice")
            {
                UnityEngine.Object.Destroy(__instance.gameObject);
                return false;
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.PlayerShelteredFromWind))]
    public class PlayerWindShelterPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (CampOfficeMod.s_RunCompleted)
            {
                Transform playerTransform = GameManager.GetPlayerTransform();
                if (playerTransform != null && CampOfficeMod.IsPositionInsideCabin(playerTransform.position))
                {
                    __result = true;
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.IsPositionOccludedFromWind))]
    public class WindOcclusionPatch
    {
        public static bool Prefix(Vector3 pos, ref bool __result)
        {
            if (CampOfficeMod.s_RunCompleted && CampOfficeMod.IsPositionInsideCabin(pos))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.RandomSpawnObject), "Start")]
    public class RandomSpawnBlockerPatch
    {
        public static bool Prefix(Il2Cpp.RandomSpawnObject __instance)
        {
            if (CampOfficeMod.s_IsCloningRoutineActive)
            {
                string saveKey = "CampOfficeGen_" + Il2Cpp.SaveGameSystem.m_CurrentSaveName;

                if (UnityEngine.PlayerPrefs.GetInt(saveKey, 0) == 1)
                {
                    UnityEngine.Object.Destroy(__instance.gameObject);
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2CppTLD.Placement.Placeable), nameof(Il2CppTLD.Placement.Placeable.FindOrCreateAndDeserialize))]
    public class PlaceableFindOrCreatePatch
    {
        public static HashSet<string> s_InteriorPlaceableGuids = new HashSet<string>();

        public static bool Prefix(string guid, Il2CppTLD.Placement.PlaceableSaveData data, ref Il2CppTLD.Placement.Placeable __result)
        {
            if (!CampOfficeMod.s_RunCompleted && s_InteriorPlaceableGuids.Contains(guid))
            {
                if (CampOfficeMod.s_DebugBounds)
                    MelonLogger.Msg($"[PLACEABLE-SKIP] FindOrCreateAndDeserialize engellendi. GUID: {guid}");

                __result = null;
                return false;
            }

            return true;
        }
    }

    // =====================================================================================
    // [EKLENDİ] AŞAĞIDAKİLER SADECE KONTEYNER VE ITEM DUPLICATE ÇÖZÜMLERİ İÇİN EKLENMİŞTİR
    // =====================================================================================

    // 1. Eşya çoğalmasını (Dupe) engelleyen yama
    [HarmonyLib.HarmonyPatch]
    public class PreventGearManagerDuplicationPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var method in typeof(Il2Cpp.GearManager).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (method.Name == "Deserialize")
                {
                    yield return method;
                }
            }
        }

        public static bool Prefix()
        {
            if (CampOfficeMod.s_IsCloningRoutineActive)
            {
                return false;
            }
            return true;
        }
    }

    // 2. Silinmiş konteynerlerin "FindContainerByPosition" aramasında oyunu çökertmesini engelleyen yama (Finalizer)
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.ContainerManager), nameof(Il2Cpp.ContainerManager.FindContainerByPosition))]
    public class FixContainerManagerCrashPatch
    {
        public static System.Exception Finalizer(System.Exception __exception, ref Il2Cpp.Container __result)
        {
            if (__exception != null)
            {
                __result = null;
                return null;
            }
            return null;
        }
    }

    // 3. Konteyner içi loot üretimi sırasında (PopulateWithRandomGear) oluşan seri çökmeleri engelleyen yama
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Container), nameof(Il2Cpp.Container.PopulateWithRandomGear))]
    public class FixContainerPopulateCrashPatch
    {
        public static bool Prefix(Il2Cpp.Container __instance)
        {
            if (__instance == null || __instance.gameObject == null)
            {
                return false;
            }
            return true;
        }
    }
}
