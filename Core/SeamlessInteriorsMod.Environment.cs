using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Loads the interior scene (plus its sandbox/DLC variants) additively, then
        // reparents every root object from those scenes under one "Master_CampOffice_Interior"
        // container. That container is what gets moved into the exterior scene and
        // positioned on top of the exterior shell, which is the core trick behind
        // the seamless interior/exterior transition.
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

            // Parking the container in the exterior scene now (instead of after all
            // reparenting is done) avoids cross-scene reference issues while we move
            // children into it below.
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

        // Removes/disables objects from the cloned interior that only make sense in the
        // original, self-contained interior scene (daytime-only light shafts, the interior's
        // own lighting manager, the game's own "inaccessible gear" lost-and-found container,
        // and the interior window prop, which would look wrong once merged into the exterior).
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

        // Positions the cloned interior on top of the exterior "shell" building so the
        // two visually line up. Tries to find the real exterior prefab by name first,
        // then falls back to a hardcoded world position if it can't be found (e.g. if
        // Hinterland ever renames the prefab). The non-uniform scale (1.05, 0.98, 1.05)
        // is a manual fudge factor to make the interior geometry match the exterior shell's
        // real-world footprint, since the two were never built to align automatically.
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

        // Builds invisible collider walls around the interior bounds plus a NavMesh
        // obstacle, so wildlife AI won't wander through the "walls" of the cloned
        // interior (which has no real architectural collision once merged into the
        // open exterior world).
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

        // The interior scene ships with baked lightmaps meant for its own isolated
        // lighting setup. Those would look wrong (or pitch black) once the geometry is
        // merged into the outdoor scene's real-time lighting, so we clear the lightmap
        // index on every renderer and strip the LIGHTMAP_ON shader keyword from any
        // material that has it (cloning the material first so we don't affect the
        // original asset shared by other instances).
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

        // Forces the outdoor weather/lighting system to refresh so the merged interior
        // geometry picks up correct real-time lighting instead of stale baked values.
        private void UpdateGlobalEnvironment()
        {
            Weather wFinal = GameManager.GetWeatherComponent();
            if (wFinal != null) wFinal.ForceOutdoorEnvironment();

            LightingManager.m_LevelLoadComplete = true;
            LightingManager.OnLevelLoadComplete();
            LightingManager.SetLightingStrengthDefault();
            UnityEngine.DynamicGI.UpdateEnvironment();
        }

        // Computes an axis-aligned bounding box (in the interior root's local space)
        // that tightly wraps the interior's actual geometry, by sampling every renderer's
        // world-space bounds corners and transforming them into local space.
        // Renderers larger than 40 units, or whose local center sits implausibly far
        // from the root (>30 units), are treated as outliers/false-positives (e.g. huge
        // skybox-style meshes or props that got parented incorrectly) and skipped so
        // they don't blow up the computed bounds. "Shadow_Caster" objects are also
        // ignored since they don't represent real interior geometry.
        // If no valid renderer is found at all, a reasonable default box is returned
        // instead of an empty/zero bounds.
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