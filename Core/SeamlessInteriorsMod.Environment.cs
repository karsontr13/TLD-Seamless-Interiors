using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        private IEnumerator LoadInteriorScenes()
        {
            var opMain = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opMain.IsDone) yield return null;
            var s_InteriorMain = opMain.Result.Scene;

            var opSandbox = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR + "_SANDBOX", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opSandbox.IsDone) yield return null;
            var s_InteriorSandbox = opSandbox.Result.Scene;

            var opDLC = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR + "_DLC01", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opDLC.IsDone) yield return null;
            var s_InteriorDLC = opDLC.Result.Scene;

            s_MasterInterior = new GameObject("Master_CampOffice_Interior");
            s_MasterInterior.SetActive(false);

            var exteriorScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(EXTERIOR);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(s_MasterInterior, exteriorScene);

            List<UnityEngine.SceneManagement.Scene> loadedScenes = new List<UnityEngine.SceneManagement.Scene>() { s_InteriorMain, s_InteriorSandbox, s_InteriorDLC };

            foreach (var scn in loadedScenes)
            {
                if (!scn.isLoaded) continue;
                foreach (GameObject rootObj in scn.GetRootGameObjects())
                {
                    if (rootObj == s_MasterInterior) continue;
                    rootObj.transform.SetParent(s_MasterInterior.transform, false);
                }
            }
        }

        private void PrepareMasterInterior()
        {
            Transform[] masterChildren = s_MasterInterior.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in masterChildren)
            {
                if (t == null || t.gameObject == null) continue;

                if (t.name.Contains("FX_LightShaft_B") || t.name.Contains("WindowLight") ||
                    t.name.Contains("InteriorLightingManager_Prefab") || t.name.Contains("CONTAINER_InaccessibleGear") ||
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
        }

        private void AlignWithExteriorShell()
        {
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
                s_MasterInterior.transform.localScale = new Vector3(1.05f, 0.98f, 1.05f);

                if (s_DebugBounds) MelonLogger.Msg($"[DEBUG-SHELL] ExteriorShell found. Target: {shellPos}");
            }
            else
            {
                s_MasterInterior.transform.position = new Vector3(1019.738f, 26.7883f + INTERIOR_Y_OFFSET, 440.6331f);
                s_MasterInterior.transform.localScale = new Vector3(1.05f, 0.98f, 1.05f);
                if (s_DebugBounds) MelonLogger.Msg("[DEBUG-SHELL] ExteriorShell not found, using fallback coordinates.");
            }
        }

        private void SetupSolidPerimeter(Bounds localBounds)
        {
            GameObject solidPerimeter = new GameObject("SolidPerimeter_Blocker");
            solidPerimeter.transform.SetParent(s_MasterInterior.transform, false);
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

        private void StripBakedLightmaps()
        {
            Renderer[] allRenderersAfter = s_MasterInterior.GetComponentsInChildren<Renderer>(true);
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

        private void UpdateGlobalEnvironment()
        {
            Weather wFinal = GameManager.GetWeatherComponent();
            if (wFinal != null) wFinal.ForceOutdoorEnvironment();

            LightingManager.m_LevelLoadComplete = true;
            LightingManager.OnLevelLoadComplete();
            LightingManager.SetLightingStrengthDefault();
            UnityEngine.DynamicGI.UpdateEnvironment();
        }

        public static Bounds ComputeLocalInteriorBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool first = true;
            Vector3 min = Vector3.zero, max = Vector3.zero;

            foreach (var r in renderers)
            {
                if (r == null || r.gameObject.name.Contains("Shadow_Caster")) continue;

                Bounds wb = r.bounds;
                if (wb.size.x > 40f || wb.size.y > 40f || wb.size.z > 40f) continue;

                Vector3 localCenterCheck = root.transform.InverseTransformPoint(wb.center);
                if (Mathf.Abs(localCenterCheck.x) > 30f || Mathf.Abs(localCenterCheck.y) > 30f || Mathf.Abs(localCenterCheck.z) > 30f) continue;

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
            return result;
        }
    }
}