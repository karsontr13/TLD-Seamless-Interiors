using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Aynı sahne birden fazla instance tarafından paylaşıldığında,
        // sahneyi bir kez yükleyip her instance için kopyasını üretiyoruz.
        // Bu dictionary yükleme sırasında kullanılır ve sahne unload sonrası temizlenir.
        private static Dictionary<string, GameObject> s_LoadedSceneTemplates = new Dictionary<string, GameObject>();

        // Toplu ortam düzeltmesi zaten planlandı mı? Birden fazla coroutine başlatmayı önler.
        private static bool s_BatchEnvironmentPending = false;

        private IEnumerator TryBatchUpdateEnvironment()
        {
            // Zaten bekleyen bir batch varsa tekrar başlatma
            if (s_BatchEnvironmentPending)
                yield break;

            s_BatchEnvironmentPending = true;

            // Tüm aktif instance'ların tamamlanmasını bekle (max 30 saniye)
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

            // Tek seferde ortam düzeltmesi yap (1 parlama, 15 değil)
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

            // Tüm instance'ların renderer'larını aç
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst.RunCompleted && inst.MasterInterior != null)
                {
                    foreach (var r in inst.MasterInterior.GetComponentsInChildren<Renderer>(true))
                        if (r != null) r.enabled = true;
                }
            }

            s_BatchEnvironmentPending = false;

            if (s_DebugBounds)
                MelonLogger.Msg($"[BATCH-ENV] Tum instance'lar icin toplu ortam duzeltmesi tamamlandi.");
        }

        private IEnumerator LoadInteriorScenes(SeamlessInteriorInstance instance)
        {
            string baseName = instance.Config.InteriorSceneBaseName;

            // Daha önce aynı sahne yüklendi mi? (Aynı haritada aynı InteriorSceneBaseName'e sahip başka bir instance)
            if (s_LoadedSceneTemplates.ContainsKey(baseName) && s_LoadedSceneTemplates[baseName] != null)
            {
                // Template'den deep copy yap
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

            // --- 1. ÇÖZÜM: DIŞ MEKANIN SAĞLAM IŞIK HARİTALARINI YEDEKLE ---
            var cachedLightmaps = UnityEngine.LightmapSettings.lightmaps;
            var cachedLightProbes = UnityEngine.LightmapSettings.lightProbes;
            // ---------------------------------------------------------------

            var opMain = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(baseName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opMain.IsDone) yield return null;
            var s_InteriorMain = opMain.Result.Scene;

            var opSandbox = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(baseName + "_SANDBOX", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opSandbox.IsDone) yield return null;
            var s_InteriorSandbox = opSandbox.Result.Scene;

            var opDLC = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(baseName + "_DLC01", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opDLC.IsDone) yield return null;
            var s_InteriorDLC = opDLC.Result.Scene;

            // Tüm sahneler yüklendikten sonra lightmap'leri TEK SEFERDE geri yükle
            UnityEngine.LightmapSettings.lightmaps = cachedLightmaps;
            UnityEngine.LightmapSettings.lightProbes = cachedLightProbes;

            instance.MasterInterior = new GameObject($"Master_{instance.Config.ResolvedInstanceId}_Interior");
            instance.MasterInterior.SetActive(false);

            var exteriorScene2 = UnityEngine.SceneManagement.SceneManager.GetSceneByName(instance.Config.ExteriorSceneName);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance.MasterInterior, exteriorScene2);

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

            // --- 2. ÇÖZÜM: İÇ MEKAN YÜKLENDİKTEN SONRA DIŞ MEKAN IŞIK HARİTALARINI GERİ YÜKLE ---
            UnityEngine.LightmapSettings.lightmaps = cachedLightmaps;
            UnityEngine.LightmapSettings.lightProbes = cachedLightProbes;
            // ------------------------------------------------------------------------------------

            // Aktif sahneyi dış mekan olarak sabitle
            if (exteriorScene2.isLoaded)
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(exteriorScene2);
            }

            // Template olarak kaydet (aynı sahneye sahip diğer instance'lar kopyalayabilsin)
            s_LoadedSceneTemplates[baseName] = instance.MasterInterior;
        }

        public void AutoResolveOverlappingExternalObjects(SeamlessInteriorInstance instance)
        {
            instance.ResolvedExternalHiddenObjects.Clear();

            if (instance.MasterInterior == null) return;

            // OPTİMİZASYON: Önce InteriorTrigger bounds'unu world-space'e çevir
            // Sadece bu AABB içine düşen renderer'lara raycast yap (binlerce yerine onlarca)
            Bounds? preFilterBounds = null;
            if (instance.InteriorTrigger != null)
            {
                Vector3 wCenter = instance.InteriorTrigger.transform.TransformPoint(instance.InteriorTrigger.center);
                // Local size'ı world-space'e yaklaşık çevir (scale dikkate alınarak)
                Vector3 lossyScale = instance.InteriorTrigger.transform.lossyScale;
                Vector3 wSize = new Vector3(
                    instance.InteriorTrigger.size.x * Mathf.Abs(lossyScale.x),
                    instance.InteriorTrigger.size.y * Mathf.Abs(lossyScale.y),
                    instance.InteriorTrigger.size.z * Mathf.Abs(lossyScale.z));
                // Rotasyon nedeniyle biraz genişlet
                wSize *= 1.2f;
                preFilterBounds = new Bounds(wCenter, wSize);
            }

            var allRenderers = UnityEngine.Object.FindObjectsOfType<Renderer>();

            foreach (var renderer in allRenderers)
            {
                if (renderer == null || renderer.gameObject == null) continue;
                if (renderer.transform.IsChildOf(instance.MasterInterior.transform)) continue;
                if (instance.ExteriorShell != null && renderer.transform.IsChildOf(instance.ExteriorShell.transform)) continue;
                if (renderer.transform.root.name.Contains("CHARACTER_FPSPlayer") || renderer.gameObject.scene.name == "DontDestroyOnLoad") continue;

                Bounds b = renderer.bounds;

                // OPTİMİZASYON: AABB pre-filter - trigger bounds ile kesişmiyorsa kesinlikle içeride değildir
                if (preFilterBounds.HasValue && !preFilterBounds.Value.Intersects(b))
                    continue;

                bool isInside = false;

                if (instance.IsPositionInside(b.center))
                {
                    isInside = true;
                }
                else
                {
                    Vector3[] corners = new Vector3[8] {
                        new Vector3(b.min.x, b.min.y, b.min.z),
                        new Vector3(b.min.x, b.min.y, b.max.z),
                        new Vector3(b.min.x, b.max.y, b.min.z),
                        new Vector3(b.min.x, b.max.y, b.max.z),
                        new Vector3(b.max.x, b.min.y, b.min.z),
                        new Vector3(b.max.x, b.min.y, b.max.z),
                        new Vector3(b.max.x, b.max.y, b.min.z),
                        new Vector3(b.max.x, b.max.y, b.max.z)
                    };

                    foreach (var corner in corners)
                    {
                        if (instance.IsPositionInside(corner))
                        {
                            isInside = true;
                            break;
                        }
                    }
                }

                if (isInside)
                {
                    if (!instance.ResolvedExternalHiddenObjects.Contains(renderer.gameObject))
                    {
                        instance.ResolvedExternalHiddenObjects.Add(renderer.gameObject);
                    }

                    var parentColliders = renderer.GetComponentsInParent<Collider>(true);
                    foreach (var col in parentColliders)
                    {
                        if (col != null && !col.isTrigger && !instance.ResolvedExternalHiddenObjects.Contains(col.gameObject))
                        {
                            instance.ResolvedExternalHiddenObjects.Add(col.gameObject);
                        }
                    }

                    var childColliders = renderer.GetComponentsInChildren<Collider>(true);
                    foreach (var col in childColliders)
                    {
                        if (col != null && !col.isTrigger && !instance.ResolvedExternalHiddenObjects.Contains(col.gameObject))
                        {
                            instance.ResolvedExternalHiddenObjects.Add(col.gameObject);
                        }
                    }
                }
            }

            if (SeamlessInteriorsMod.s_DebugBounds)
            {
                MelonLogger.Msg($"[AUTO-HIDE] {instance.Config.InteriorSceneBaseName} için {instance.ResolvedExternalHiddenObjects.Count} adet obje gizlenecek.");
            }
        }

        private void PrepareMasterInterior(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            Transform[] masterChildren = instance.MasterInterior.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in masterChildren)
            {
                if (t == null || t.gameObject == null) continue;

                // Config içindeki Destroy listesinde bu objenin ismi var mı?
                // DÜZELTME: Exact match kullan - Contains() ile "OBJ_TrailerWindow" aranınca
                // "OBJ_TrailerWindow_Prefab" (parent) de eşleşiyordu ve parent kapanıyordu
                bool shouldDestroy = instance.Config.ObjectsToDestroy != null && instance.Config.ObjectsToDestroy.Contains(t.name);
                if (shouldDestroy)
                {
                    UnityEngine.Object.Destroy(t.gameObject);
                    continue;
                }

                // Config içindeki Disable listesinde bu objenin ismi var mı?
                bool shouldDisable = instance.Config.ObjectsToDisable != null && instance.Config.ObjectsToDisable.Contains(t.name);
                if (shouldDisable)
                {
                    t.gameObject.SetActive(false);
                }
            }

            // --- YENİ EKLENEN KISIM: SHADER VE YANSIMA DÜZELTMESİ ---

            // 1. İç mekandan gelen ve dış mekanın shader'larını (kar/cam) bozan Yansıma Sondalarını (Reflection Probes) temizle
            var reflectionProbes = instance.MasterInterior.GetComponentsInChildren<ReflectionProbe>(true);
            foreach (var probe in reflectionProbes)
            {
                if (probe != null)
                {
                    UnityEngine.Object.Destroy(probe.gameObject);
                }
            }

            // 2. Işık Sondalarını (Light Probes) temizle (Objelerin garip renk almasını önler)
            var lightProbes = instance.MasterInterior.GetComponentsInChildren<LightProbeGroup>(true);
            foreach (var lp in lightProbes)
            {
                if (lp != null)
                {
                    UnityEngine.Object.Destroy(lp.gameObject);
                }
            }

            // --- IŞIK SIZINTILARINI (LIGHT BLEEDING) TEMİZLE ---
            var allLights = instance.MasterInterior.GetComponentsInChildren<Light>(true);
            foreach (var l in allLights)
            {
                if (l == null) continue;

                // 1. İç mekan sahnelerinde bulunan sahte Güneş/Ay (Directional) tüm dış haritayı bozar. Kesinlikle silinmeli.
                if (l.type == LightType.Directional)
                {
                    UnityEngine.Object.Destroy(l.gameObject);
                    continue;
                }

                // 2. Duvarlardan dışarı taşan devasa ortam (ambient) Point ışıklarını temizle.
                // Range (menzil) değeri 15-20'den büyükse muhtemelen dışarı taşıyordur.
                if (l.type == LightType.Point && l.range > 15f)
                {
                    UnityEngine.Object.Destroy(l.gameObject);
                }
            }
            // ---------------------------------------------------
        }

        private void AlignWithExteriorShell(SeamlessInteriorInstance instance)
        {
            // Aynı isimde birden fazla shell olabilir. Hepsini bul, FallbackPosition'a en yakınını eşleştir.
            instance.ExteriorShell = FindClosestShell(instance.Config.ExteriorShellPrefabName, instance.Config.FallbackPosition);

            if (instance.ExteriorShell == null)
            {
                // Fallback: InteriorSceneBaseName + "Prefab" içeren herhangi bir obje
                float bestDist = float.MaxValue;
                foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                {
                    if (go.name.Contains(instance.Config.InteriorSceneBaseName) && go.name.Contains("Prefab"))
                    {
                        float dist = Vector3.Distance(go.transform.position, instance.Config.FallbackPosition);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            instance.ExteriorShell = go;
                        }
                    }
                }
            }

            // Dış kabuk bulunsa bile, eğer zorunlu pozisyon istiyorsak Fallback değerini kullanacağız
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

            // Pozisyonu, dönüşü ve ölçeği uygula
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

        private void SetupSolidPerimeter(SeamlessInteriorInstance instance, Bounds localBounds)
        {
            if (instance.MasterInterior == null) return;

            GameObject solidPerimeter = new GameObject("SolidPerimeter_Blocker");
            solidPerimeter.transform.SetParent(instance.MasterInterior.transform, false);
            solidPerimeter.transform.localPosition = Vector3.zero;
            solidPerimeter.transform.localRotation = Quaternion.identity;
            solidPerimeter.layer = LayerMask.NameToLayer("NPC");

            float wT = 0.5f;

            BoxCollider wallFront = solidPerimeter.AddComponent<BoxCollider>();
            wallFront.center = new Vector3(localBounds.center.x, localBounds.center.y, localBounds.max.z + (wT / 2f));
            wallFront.size = new Vector3(localBounds.size.x, localBounds.size.y, wT);

            BoxCollider wallBack = solidPerimeter.AddComponent<BoxCollider>();
            wallBack.center = new Vector3(localBounds.center.x, localBounds.center.y, localBounds.min.z - (wT / 2f));
            wallBack.size = new Vector3(localBounds.size.x, localBounds.size.y, wT);

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

        private void StripBakedLightmaps(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            Renderer[] allRenderersAfter = instance.MasterInterior.GetComponentsInChildren<Renderer>(true);
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
        }

        private void UpdateGlobalEnvironment(SeamlessInteriorInstance instance)
        {
            // Dış mekan lightmap'lerini yedekle - ForceOutdoorEnvironment ve DynamicGI
            // bunları bozabilir, oyuncu "renk titresi" görür
            var cachedLightmaps = UnityEngine.LightmapSettings.lightmaps;
            var cachedLightProbes = UnityEngine.LightmapSettings.lightProbes;

            Weather wFinal = GameManager.GetWeatherComponent();
            if (wFinal != null) wFinal.ForceOutdoorEnvironment();

            LightingManager.m_LevelLoadComplete = true;
            LightingManager.OnLevelLoadComplete();
            LightingManager.SetLightingStrengthDefault();

            UnityEngine.DynamicGI.UpdateEnvironment();

            // Lightmap'leri geri yükle
            UnityEngine.LightmapSettings.lightmaps = cachedLightmaps;
            UnityEngine.LightmapSettings.lightProbes = cachedLightProbes;

            if (instance.MasterInterior != null)
            {
                var electrolizers = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.ModularElectrolizer.AuroraModularElectrolizer>(true);
                foreach (var electrolizer in electrolizers)
                {
                    if (electrolizer != null)
                    {
                        if (!electrolizer.m_IsInitialized)
                        {
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

            // 1. Tüm renderer'ların local-space merkezlerini topla (filtreli)
            var validRenderers = new List<System.Tuple<Renderer, Vector3>>(); // renderer, localCenter
            foreach (var r in renderers)
            {
                if (r == null || r.gameObject.name.Contains("Shadow_Caster")) continue;
                Bounds wb = r.bounds;
                if (wb.size.x > 40f || wb.size.y > 40f || wb.size.z > 40f) continue;
                Vector3 localCenter = root.transform.InverseTransformPoint(wb.center);
                if (Mathf.Abs(localCenter.x) > 30f || Mathf.Abs(localCenter.y) > 30f || Mathf.Abs(localCenter.z) > 30f) continue;
                validRenderers.Add(new System.Tuple<Renderer, Vector3>(r, localCenter));
            }

            if (validRenderers.Count == 0)
                return new List<Bounds> { new Bounds(new Vector3(0, 2f, 0), new Vector3(25f, 18f, 25f)) };

            // 2. Grid sınırlarını bul
            int gxMin = int.MaxValue, gxMax = int.MinValue;
            int gzMin = int.MaxValue, gzMax = int.MinValue;
            var occupiedCells = new HashSet<long>(); // gx * 100000 + gz şeklinde hash

            foreach (var pair in validRenderers)
            {
                Vector3 lc = pair.Item2;
                int gx = Mathf.FloorToInt(lc.x / cellSize);
                int gz = Mathf.FloorToInt(lc.z / cellSize);
                occupiedCells.Add((long)gx * 100000L + gz);
                if (gx < gxMin) gxMin = gx;
                if (gx > gxMax) gxMax = gx;
                if (gz < gzMin) gzMin = gz;
                if (gz > gzMax) gzMax = gz;
            }

            // 3. 2D bool grid oluştur
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

            // 4. Greedy rectangle decomposition: grid'deki dolu hücreleri maksimal dikdörtgenlere ayır
            var rects = new List<System.Tuple<int, int, int, int>>(); // x0, z0, x1, z1 (inclusive)

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    if (!grid[x, z] || used[x, z]) continue;

                    // Bu hücreden başlayarak sağa doğru genişlet
                    int maxX = x;
                    while (maxX + 1 < width && grid[maxX + 1, z] && !used[maxX + 1, z])
                        maxX++;

                    // Sonra aşağı doğru genişlet
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

                    // Hücreleri işaretle
                    for (int cx = x; cx <= maxX; cx++)
                        for (int cz = z; cz <= maxZ; cz++)
                            used[cx, cz] = true;

                    rects.Add(new System.Tuple<int, int, int, int>(gxMin + x, gzMin + z, gxMin + maxX, gzMin + maxZ));
                }
            }

            // 5. Her dikdörtgen bölge için o bölgeye düşen renderer'lardan tight bounds hesapla
            var subBounds = new List<Bounds>();

            foreach (var rect in rects)
            {
                int rx0 = rect.Item1, rz0 = rect.Item2, rx1 = rect.Item3, rz1 = rect.Item4;

                // Bu dikdörtgenin kapsadığı alan çok küçükse atla
                if (rx1 - rx0 < 0 && rz1 - rz0 < 0) continue;

                Vector3 rMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 rMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                int rendererCount = 0;

                foreach (var pair in validRenderers)
                {
                    Vector3 lc = pair.Item2;
                    int gx = Mathf.FloorToInt(lc.x / cellSize);
                    int gz = Mathf.FloorToInt(lc.z / cellSize);

                    if (gx < rx0 || gx > rx1 || gz < rz0 || gz > rz1) continue;

                    // Bu renderer bu dikdörtgene ait
                    Bounds wb = pair.Item1.bounds;
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
                        rMin = Vector3.Min(rMin, local);
                        rMax = Vector3.Max(rMax, local);
                    }
                    rendererCount++;
                }

                // Çok az renderer olan dikdörtgenleri atla (gürültü filtresi)
                if (rendererCount < 3) continue;
                Vector3 size = rMax - rMin;
                if (size.x < 1f || size.z < 1f) continue;

                Bounds regionBounds = new Bounds();
                regionBounds.SetMinMax(rMin, rMax);
                subBounds.Add(regionBounds);
            }

            // 6. Çok küçük ve büyük bir komşu ile örtüşen dikdörtgenleri birleştir
            // (Greedy decomposition bazen gereksiz küçük parçalar üretebilir)
            subBounds = MergeSmallBounds(subBounds);

            // Hiç bölge bulunamazsa fallback
            if (subBounds.Count == 0)
                subBounds.Add(new Bounds(new Vector3(0, 2f, 0), new Vector3(25f, 18f, 25f)));

            return subBounds;
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
                    // En yakın büyük komşuyu bul ve birleştir
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

        // Eski ComputeLocalInteriorBounds - geriye uyumluluk için tutuluyor
        // Artık ComputeInteriorSubBounds'un birleştirilmiş versiyonunu döndürür
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

            // Halihazırda başka instance'lar tarafından kullanılan shell'leri topla
            var usedShells = new HashSet<int>();
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst.ExteriorShell != null)
                    usedShells.Add(inst.ExteriorShell.GetInstanceID());
            }

            GameObject best = null;
            float bestDist = float.MaxValue;

            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                // Hem exact match hem de Contains ile eşleştir (Clone eki olabilir)
                if (go.name != shellPrefabName && !go.name.StartsWith(shellPrefabName)) continue;
                if (usedShells.Contains(go.GetInstanceID())) continue; // Zaten kullanılıyor

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
