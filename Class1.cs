using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[assembly: MelonInfo(typeof(CampOfficeOverhaul.CampOfficeMod), "Camp Office Overhaul", "Deneme", "HamsiBuglama")]
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
                float extScale = 1.51f;
                s_ExteriorShell.transform.localScale = new Vector3(extScale, extScale, extScale);

                master.transform.position = new Vector3(1020.938f, 26.7883f, 440.6331f);
                master.transform.rotation = s_ExteriorShell.transform.rotation;
                master.transform.localScale = Vector3.one;
            }
            else
            {
                master.transform.position = new Vector3(1020.938f, 26.7883f, 440.6331f);
                master.transform.rotation = Quaternion.identity;
                master.transform.localScale = Vector3.one;
            }

            GameObject customTriggerBoxObj = new GameObject("CampOffice_CustomIndoorTrigger");
            customTriggerBoxObj.transform.SetParent(master.transform, false);
            customTriggerBoxObj.transform.localPosition = new Vector3(0f, 2.5f, 0f);

            BoxCollider triggerBox = customTriggerBoxObj.AddComponent<BoxCollider>();
            triggerBox.isTrigger = true;
            triggerBox.size = new Vector3(30f, 25f, 30f);

            IndoorSpaceTrigger spaceTrigger = customTriggerBoxObj.AddComponent<IndoorSpaceTrigger>();
            spaceTrigger.m_UseOutdoorLighting = true;
            spaceTrigger.m_UseOutdoorTemperature = false;
            spaceTrigger.m_AllowCampfires = true;
            spaceTrigger.m_TemperatureDeltaCelsius = 15f;
            spaceTrigger.m_ValidSafehouse = true;
            spaceTrigger.m_DontCountAsInterior = true;
            spaceTrigger.m_IgnoreCabinFever = false;
            spaceTrigger.m_TriggerID = "CustomCampOffice_Trigger";

            try
            {
                Weather w = GameManager.GetWeatherComponent();
                if (w != null)
                {
                    Il2CppTLD.WeatherParticle.WeatherParticleManager wpm = null;
                    foreach (var p in typeof(Weather).GetProperties())
                    {
                        if (p.PropertyType == typeof(Il2CppTLD.WeatherParticle.WeatherParticleManager))
                        {
                            wpm = p.GetValue(w) as Il2CppTLD.WeatherParticle.WeatherParticleManager;
                            break;
                        }
                    }

                    if (wpm != null)
                    {
                        Bounds safeBounds = new Bounds(master.transform.position, new Vector3(30f, 25f, 30f));
                        var killer = new Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance();
                        killer.m_Bounds = safeBounds;
                        killer.m_KillsFallingSnow = true;
                        killer.m_KillsBlowingSnow = true;
                        wpm.m_AllParticleKillers.Add(killer);
                    }
                }
            }
            catch { }

            master.SetActive(true);
            yield return null;

            MonoBehaviour[] tldScripts = master.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var script in tldScripts)
            {
                if (script == null) continue;
                if (script.GetIl2CppType().Name == "LoadScene") continue;

                UnityEngine.Object.DestroyImmediate(script);
            }

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

            MeshRenderer[] extRenderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach (var mr in extRenderers)
            {
                if (mr == null) continue;
                string objName = mr.gameObject.name.ToLower();
                if ((objName.Contains("window") || objName.Contains("glass")) && Vector3.Distance(mr.transform.position, master.transform.position) < 30f)
                {
                    mr.enabled = false;
                }
            }

            yield return null;

            var unloadOp = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(interior);
            while (!unloadOp.isDone) yield return null;

            Weather wFinal = GameManager.GetWeatherComponent();
            if (wFinal != null) wFinal.ForceOutdoorEnvironment();

            LightingManager.m_LevelLoadComplete = true;
            LightingManager.OnLevelLoadComplete();
            LightingManager.SetLightingStrengthDefault();
            UnityEngine.DynamicGI.UpdateEnvironment();

            s_IsCloningRoutineActive = false;
            s_RunCompleted = true;
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
                pm.TeleportPlayer(new Vector3(1018.888f, 26.8318f, 444.2298f), Quaternion.identity);

                if (CampOfficeMod.s_ExteriorShell != null)
                {
                    CampOfficeMod.s_ExteriorShell.SetActive(false);
                }

                LightingManager.m_LevelLoadComplete = true;
                LightingManager.OnLevelLoadComplete();
                LightingManager.SetLightingStrengthDefault();
                UnityEngine.DynamicGI.UpdateEnvironment();

                return false;
            }

            if (targetScene == CampOfficeMod.EXTERIOR)
            {
                pm.TeleportPlayer(new Vector3(1018.497f, 27.0182f, 445.81f), Quaternion.identity);

                if (CampOfficeMod.s_ExteriorShell != null)
                {
                    CampOfficeMod.s_ExteriorShell.SetActive(true);
                }

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
}
