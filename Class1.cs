using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[assembly: MelonInfo(typeof(CampOfficeOverhaul.CampOfficeMod), "Seamless-CampOffice", "Deneme", "HamsiBuglama")]
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

        // Odadaki rüzgarı kesmek için kullanacağımız hayali sınır kutusu
        public static Bounds s_CabinBounds;

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
                GameObject old = GameObject.Find("Master_CampOffice_Interior");
                if (old != null) UnityEngine.Object.Destroy(old);
            }
        }

        private IEnumerator WaitForPlayerThenRun()
        {
            float timeout = 10f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                PlayerManager pm = GameManager.GetPlayerManagerComponent();
                if (pm != null && pm.transform.position.sqrMagnitude > 1f) break;

                yield return null;
                elapsed += 0.5f;
            }

            MelonCoroutines.Start(Run());
        }

        private IEnumerator Run()
        {
            s_IsCloningRoutineActive = true;

            var op = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!op.IsDone) yield return null;
            UnityEngine.SceneManagement.Scene interior = op.Result.Scene;

            yield return null;

            UnityEngine.SceneManagement.Scene lakeRegion = UnityEngine.SceneManagement.SceneManager.GetSceneByName(EXTERIOR);
            GameObject master = new GameObject("Master_CampOffice_Interior");
            s_MasterInterior = master;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(master, lakeRegion);

            List<Transform> partsToMove = new List<Transform>();
            foreach (GameObject rootObj in interior.GetRootGameObjects())
            {
                foreach (Transform child in rootObj.GetComponentsInChildren<Transform>(true))
                {
                    string n = child.name;
                    if ((n == "A_Geo" || n == "B_Geo" || n == "Containers" || n == "Interactive")
                        && child.parent != null
                        && (child.parent.name == "Art" || child.parent.name == "Design"))
                    {
                        partsToMove.Add(child);
                    }
                }
            }

            foreach (Transform t in partsToMove) t.SetParent(master.transform, false);

            Transform[] masterChildren = master.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in masterChildren)
            {
                if (t != null && t.name.Contains("OBJ_LakeCabinInteriorWindow"))
                {
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
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
                master.transform.position = new Vector3(1019.738f, 26.7883f, 440.6331f);
                master.transform.rotation = s_ExteriorShell.transform.rotation;
                master.transform.localScale = new Vector3(1.05f, 0.98f, 1.05f);
            }
            else
            {
                master.transform.position = new Vector3(1019.738f, 26.7883f, 440.6331f);
                master.transform.rotation = Quaternion.identity;
                master.transform.localScale = new Vector3(1.05f, 0.98f, 1.05f);
            }

            // --- OTOMATİK BOYUT HESAPLAMA VE KAR KALKANI ---
            GameObject particleKillerObj = new GameObject("ParticleKiller");
            particleKillerObj.transform.SetParent(master.transform, false);
            particleKillerObj.transform.localPosition = Vector3.zero;
            particleKillerObj.layer = LayerMask.NameToLayer("TriggerIgnoreRaycast");

            Renderer[] allRenderers = master.GetComponentsInChildren<Renderer>(false);
            Bounds dynamicBounds = new Bounds(master.transform.position, Vector3.zero);
            bool hasBounds = false;

            foreach (Renderer r in allRenderers)
            {
                if (!hasBounds)
                {
                    dynamicBounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    dynamicBounds.Encapsulate(r.bounds);
                }
            }

            dynamicBounds.Expand(1.5f);

            // Harmony Yamasının kullanabilmesi için bu boyutları global değişkene kaydediyoruz
            s_CabinBounds = dynamicBounds;

            BoxCollider triggerBox = particleKillerObj.AddComponent<BoxCollider>();
            triggerBox.isTrigger = true;
            triggerBox.size = dynamicBounds.size;
            triggerBox.center = particleKillerObj.transform.InverseTransformPoint(dynamicBounds.center);

            var pki = new Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance();
            pki.m_OwnerGameObject = particleKillerObj;
            pki.m_Bounds = dynamicBounds;
            pki.m_KillsFallingSnow = true;
            pki.m_KillsBlowingSnow = true;

            var uniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
            {
                uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Add(pki);
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
            // ----------------------------------------------

            master.SetActive(true);
            yield return null;

            MeshFilter[] mFilters = master.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in mFilters) if (mf.sharedMesh != null) mf.sharedMesh = UnityEngine.Object.Instantiate(mf.sharedMesh);

            MeshCollider[] mColliders = master.GetComponentsInChildren<MeshCollider>(true);
            foreach (var mc in mColliders) if (mc.sharedMesh != null) mc.sharedMesh = UnityEngine.Object.Instantiate(mc.sharedMesh);

            Renderer[] allRenderersAfter = master.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderersAfter)
            {
                r.lightmapIndex = -1;
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null)
                    {
                        mats[i] = UnityEngine.Object.Instantiate(mats[i]);
                        mats[i].DisableKeyword("LIGHTMAP_ON");
                    }
                }
                r.sharedMaterials = mats;
            }

            yield return null;

            foreach (GameObject rootObj in interior.GetRootGameObjects())
            {
                UnityEngine.Object.Destroy(rootObj);
            }

            Weather wFinal = GameManager.GetWeatherComponent();
            if (wFinal != null) wFinal.ForceOutdoorEnvironment();

            LightingManager.m_LevelLoadComplete = true;
            LightingManager.OnLevelLoadComplete();
            LightingManager.SetLightingStrengthDefault();
            UnityEngine.DynamicGI.UpdateEnvironment();

            s_IsCloningRoutineActive = false;
            s_RunCompleted = true;

            if (s_MasterInterior != null)
            {
                s_MasterInterior.SetActive(false);
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

            string targetScene = __instance.m_SceneToLoad;

            if (targetScene == CampOfficeMod.INTERIOR)
            {
                pm.TeleportPlayer(new Vector3(1019.048f, 26.4147f, 444.2578f), Quaternion.identity);

                if (CampOfficeMod.s_MasterInterior != null) CampOfficeMod.s_MasterInterior.SetActive(true);
                if (CampOfficeMod.s_ExteriorShell != null) CampOfficeMod.s_ExteriorShell.SetActive(false);

                LightingManager.m_LevelLoadComplete = true;
                LightingManager.OnLevelLoadComplete();
                LightingManager.SetLightingStrengthDefault();
                UnityEngine.DynamicGI.UpdateEnvironment();

                return false;
            }

            if (targetScene == CampOfficeMod.EXTERIOR)
            {
                pm.TeleportPlayer(new Vector3(1019.048f, 26.4147f, 444.2578f), Quaternion.identity);

                if (CampOfficeMod.s_MasterInterior != null) CampOfficeMod.s_MasterInterior.SetActive(false);
                if (CampOfficeMod.s_ExteriorShell != null) CampOfficeMod.s_ExteriorShell.SetActive(true);

                LightingManager.m_LevelLoadComplete = true;
                LightingManager.OnLevelLoadComplete();
                LightingManager.SetLightingStrengthDefault();
                UnityEngine.DynamicGI.UpdateEnvironment();

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

    // ==============================================================================
    // --- YENİ RÜZGAR KESİCİ HARMONY YAMALARI (WIND CHILL İPTALİ) ---
    // ==============================================================================

    // 1. Yama: Oyuncu karakterinin rüzgar yemesini (Wind Chill cezasını) iptal eder
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.PlayerShelteredFromWind))]
    public class PlayerWindShelterPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (CampOfficeMod.s_RunCompleted)
            {
                Transform playerTransform = GameManager.GetPlayerTransform();
                if (playerTransform != null && CampOfficeMod.s_CabinBounds.Contains(playerTransform.position))
                {
                    __result = true; // Oyuncu "korunuyor" sayılır
                    return false;    // Orijinal metodu iptal et
                }
            }
            return true; // Kulübede değilse oyunun normal sistemini çalıştır
        }
    }

    // 2. Yama: İçeride yakılan kamp ateşlerinin veya bırakılan eşyaların rüzgardan sönmesini/uçmasını engeller
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Wind), nameof(Il2Cpp.Wind.IsPositionOccludedFromWind))]
    public class WindOcclusionPatch
    {
        // DÜZELTME: "position" parametresinin adını oyunun orijinal koduyla eşleşecek şekilde "pos" yaptık
        public static bool Prefix(Vector3 pos, ref bool __result)
        {
            if (CampOfficeMod.s_RunCompleted && CampOfficeMod.s_CabinBounds.Contains(pos))
            {
                __result = true; // O pozisyon rüzgardan "korunuyor" sayılır
                return false;    // Orijinal metodu iptal et
            }
            return true; // Kulübede değilse oyunun normal sistemini çalıştır
        }
    }
}
