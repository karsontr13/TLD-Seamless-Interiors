using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Il2CppTLD.Audio;

[assembly: MelonInfo(typeof(SeamlessInteriors.CampOfficeMod), "SeamlessInteriors", "v1.1.0", "Hamsi Buglama")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace SeamlessInteriors
{
    public class CampOfficeMod : MelonMod
    {
        // Base scene names for The Long Dark
        public const string EXTERIOR = "LakeRegion";
        public const string INTERIOR = "CampOffice";

        // Global state flags for the cloning and integration process
        public static bool s_RunCompleted = false;
        public static bool s_IsCloningRoutineActive = false;

        // References to the main game objects
        public static GameObject s_ExteriorShell = null;
        public static GameObject s_MasterInterior = null;

        // List to hold custom snow/weather particle killers inside the cabin
        public static List<Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance> s_CustomKillers = new List<Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance>();

        // How much the interior should be lifted from the ground to prevent snow bleeding through the floor
        public const float INTERIOR_Y_OFFSET = 2f;

        // The single source of truth for visibility/position checking
        public static BoxCollider s_InteriorTrigger = null;

        // Cooldown window to prevent the watchdog from interfering immediately after using a door portal
        public static float s_LastPortalUseTime = -10f;
        public const float PORTAL_SUPPRESS_WINDOW = 2f; // in seconds

        // Toggle for diagnostic logs in the MelonLoader console
        public static bool s_DebugBounds = true;

        public static bool s_WatchdogStarted = false;
        public static bool s_IsAudioOccluded = false;

        public static Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance s_ParticleKiller = null;

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // Only trigger the merging routine when the exterior region loads
            if (sceneName == EXTERIOR)
            {
                if (s_RunCompleted) return;
                MelonCoroutines.Start(WaitForPlayerThenRun());
            }
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            // Clean up static references to avoid memory leaks or issues on subsequent loads
            if (sceneName == EXTERIOR)
            {
                s_RunCompleted = false;
                s_ExteriorShell = null;
                s_MasterInterior = null;
                s_InteriorTrigger = null;
                s_WatchdogStarted = false;

                // The old UniStorm/GameAudioManager instances are destroyed when this scene unloads.
                // If these flags/lists aren't cleared, the new instances in the loaded scene
                // will try to sync using the old, invalid data, causing wind audio to break entirely.
                s_IsAudioOccluded = false;
                if (s_CustomKillers != null) s_CustomKillers.Clear();

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
                c.m_DisableSerialization = true; // Prevent double-saving of items
                count++;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[SERIALIZE-BLOCK] Prevented {count} interior containers from serializing.");
        }

        private static void InvalidateInteriorPlaceables(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;

            var placeables = interiorRoot.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            int count = 0;
            foreach (var p in placeables)
            {
                if (p == null) continue;
                p.m_Invalidated = true; // Forces the game to ignore these during regular spawn routines
                count++;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PLACEABLE-BLOCK] Invalidated {count} interior Placeables.");
        }

        // Safely toggles audio occlusion to simulate being indoors
        public static void SetAudioOcclusion(bool occlude)
        {
            if (GameAudioManager.Instance == null) return;

            if (occlude && !s_IsAudioOccluded)
            {
                // Heavy Occlusion provides the best "Mountaineer's Hut" sound isolation feel
                GameAudioManager.Instance.EnterOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                s_IsAudioOccluded = true;
                if (s_DebugBounds) MelonLogger.Msg("[AUDIO] Audio Occlusion ENABLED.");
            }
            else if (!occlude && s_IsAudioOccluded)
            {
                GameAudioManager.Instance.ExitOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                s_IsAudioOccluded = false;
                if (s_DebugBounds) MelonLogger.Msg("[AUDIO] Audio Occlusion DISABLED.");
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
                MelonLogger.Msg($"[PLACEABLE-GUIDS] Saved {PlaceableFindOrCreatePatch.s_InteriorPlaceableGuids.Count} interior Placeable GUIDs.");
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

                // Skip items belonging to our master interior
                if (s_MasterInterior != null && p.transform.IsChildOf(s_MasterInterior.transform))
                    continue;

                string sceneName = p.gameObject.scene.name;
                bool isOrphan = (sceneName == "DontDestroyOnLoad" || sceneName == null || sceneName == "");

                // Catch placeables accidentally parented to the player during scene transitions
                Transform root = p.transform.root;
                if (root != null && root.name.Contains("CHARACTER_FPSPlayer"))
                    isOrphan = true;

                if (!isOrphan) continue;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[ORPHAN-CLEANUP] Destroying: {p.gameObject.name} | Scene: {sceneName} | Root: {root?.name}");

                UnityEngine.Object.Destroy(p.gameObject);
                destroyed++;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[ORPHAN-CLEANUP] Cleared {destroyed} orphan Placeables.");
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

                // If a gear item lacks a Persistent Data ID, generate one based on its exact local position
                // This ensures save stability between sessions
                if (string.IsNullOrEmpty(guidComponent.m_Guid))
                {
                    Vector3 localPos = interiorRoot.transform.InverseTransformPoint(gear.transform.position);
                    string cleanName = gear.gameObject.name.Replace("(Clone)", "").Trim();

                    string newPdid = $"CampOffice_{cleanName}_{localPos.x:F2}_{localPos.y:F2}_{localPos.z:F2}";
                    guidComponent.m_Guid = newPdid;
                    generatedCount++;
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PDID-FIX] Assigned deterministic identities to {generatedCount} objects missing PDIDs.");
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

            // Remove duplicated items by comparing their world positions
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
                MelonLogger.Msg($"[SPATIAL-DEDUPE] Deleted {deletedCount} duplicate objects via spatial matching.");
        }

        private IEnumerator WaitForPlayerThenRun()
        {
            float timeout = 10f;
            float elapsed = 0f;

            // Wait until the player is fully initialized in the scene
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

            // NEW GAME DETECTION: 
            // If the same save slot is overwritten, old PlayerPrefs locks prevent gear from spawning.
            // If playtime is < 3 minutes (0.05 hours), reset the spawn lock for this slot.
            var tod = GameManager.GetTimeOfDayComponent();
            if (tod != null && tod.GetHoursPlayedNotPaused() < 0.05f)
            {
                string keyToReset = "CampOfficeGen_" + SaveGameSystem.m_CurrentSaveName;
                UnityEngine.PlayerPrefs.SetInt(keyToReset, 0);
                UnityEngine.PlayerPrefs.Save();

                if (s_DebugBounds)
                    MelonLogger.Msg("[NEW GAME DETECTED] Broke old loot generation lock! Gear will spawn.");
            }

            // Asynchronously load the interior scene and its sub-scenes additively
            var opMain = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opMain.IsDone) yield return null;
            UnityEngine.SceneManagement.Scene s_InteriorMain = opMain.Result.Scene;

            var opSandbox = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR + "_SANDBOX", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opSandbox.IsDone) yield return null;
            UnityEngine.SceneManagement.Scene s_InteriorSandbox = opSandbox.Result.Scene;

            var opDLC = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(INTERIOR + "_DLC01", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            while (!opDLC.IsDone) yield return null;
            UnityEngine.SceneManagement.Scene s_InteriorDLC = opDLC.Result.Scene;

            // Create a single master container for all interior objects
            GameObject master = new GameObject("Master_CampOffice_Interior");
            s_MasterInterior = master;
            master.SetActive(false);

            UnityEngine.SceneManagement.Scene exteriorScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(EXTERIOR);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(master, exteriorScene);

            List<UnityEngine.SceneManagement.Scene> loadedScenes = new List<UnityEngine.SceneManagement.Scene>() { s_InteriorMain, s_InteriorSandbox, s_InteriorDLC };

            // Consolidate all root objects from the loaded interior scenes into the master object
            foreach (var scn in loadedScenes)
            {
                if (!scn.isLoaded) continue;
                foreach (GameObject rootObj in scn.GetRootGameObjects())
                {
                    if (rootObj == master) continue;
                    rootObj.transform.SetParent(master.transform, false);
                }
            }

            // Strip redundant interior-specific lighting and fake windows
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

            // Find the exterior cabin model to match its position
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

            // Align the master interior exactly with the exterior shell
            if (s_ExteriorShell != null)
            {
                Vector3 shellPos = s_ExteriorShell.transform.position;
                shellPos.y += INTERIOR_Y_OFFSET;
                master.transform.position = shellPos;
                master.transform.rotation = s_ExteriorShell.transform.rotation;
                master.transform.localScale = new Vector3(1.05f, 0.98f, 1.05f);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[DEBUG-SHELL] ExteriorShell found. Position: {s_ExteriorShell.transform.position} (Offset applied: {shellPos})");
            }
            else
            {
                master.transform.position = new Vector3(1019.738f, 26.7883f + INTERIOR_Y_OFFSET, 440.6331f);
                master.transform.rotation = Quaternion.identity;
                master.transform.localScale = new Vector3(1.05f, 0.98f, 1.05f);

                if (s_DebugBounds)
                    MelonLogger.Msg("[DEBUG-SHELL] ExteriorShell not found, using hardcoded fallback coordinates.");
            }

            InvalidateInteriorPlaceables(master);
            CollectInteriorPlaceableGuids(master);

            yield return new WaitForSeconds(0.5f);

            // Handle Random Spawn Objects (RSO) for loot tables
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
                    MelonLogger.Msg($"[Rogue-Cleanup] Force cleared {deletedRogueCount} rogue/new RSO objects.");
            }
            else
            {
                GenerateDeterministicPDIDs(master);

                if (!string.IsNullOrEmpty(currentSaveName))
                {
                    UnityEngine.PlayerPrefs.SetInt(saveKey, 1);
                    UnityEngine.PlayerPrefs.Save();

                    if (s_DebugBounds)
                        MelonLogger.Msg($"[RSO-FLAG] Loot generation complete for {saveKey}, RSOs permanently silenced.");
                }
            }

            SpatialDeduplication(master);

            // Global deduplication by GUID to prevent identical items from persisting
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

            // CRITICAL: Load save data using EXTERIOR to properly deserialize containers and items
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
                
                // ==== NEW SECTION: FIRE SYNCHRONIZATION ====
                if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData))
                {
                    MelonCoroutines.Start(DelayedFireRestore());
                }
                // ==========================================================

                // [CHANGED] Loaded EXTERIOR instead of INTERIOR to fix container deserialization issues
                SaveGameSystem.LoadSceneDataAdditive(currentSaveName, EXTERIOR);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-RESTORE] CampOffice save data applied. There were {interiorGearBefore.Count} GearItems in the interior.");
            }

            yield return null;

            // Setup particle killers to stop snow and wind effects from rendering inside the cabin
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

            // INVISIBLE PHYSICAL PERIMETER AND AI BLOCKER
            // Keeps its layer as Default so physics work perfectly (players pass through, animals collide).
            GameObject solidPerimeter = new GameObject("SolidPerimeter_Blocker");
            solidPerimeter.transform.SetParent(master.transform, false);
            solidPerimeter.transform.localPosition = Vector3.zero;
            solidPerimeter.transform.localRotation = Quaternion.identity;

            // Assign to NPC layer to prevent wolves/bears from clipping into the cabin
            solidPerimeter.layer = LayerMask.NameToLayer("NPC");

            float wT = 0.5f; // Invisible wall thickness (Half a meter)

            // Front Wall (+Z)
            BoxCollider wallFront = solidPerimeter.AddComponent<BoxCollider>();
            wallFront.center = new Vector3(localBounds.center.x, localBounds.center.y, localBounds.max.z + (wT / 2f));
            wallFront.size = new Vector3(localBounds.size.x, localBounds.size.y, wT);

            // Back Wall (-Z)
            BoxCollider wallBack = solidPerimeter.AddComponent<BoxCollider>();
            wallBack.center = new Vector3(localBounds.center.x, localBounds.center.y, localBounds.min.z - (wT / 2f));
            wallBack.size = new Vector3(localBounds.size.x, localBounds.size.y, wT);

            // Right Wall (+X) - Extending Z to cover corners
            BoxCollider wallRight = solidPerimeter.AddComponent<BoxCollider>();
            wallRight.center = new Vector3(localBounds.max.x + (wT / 2f), localBounds.center.y, localBounds.center.z);
            wallRight.size = new Vector3(wT, localBounds.size.y, localBounds.size.z + (wT * 2));

            // Left Wall (-X) - Extending Z to cover corners
            BoxCollider wallLeft = solidPerimeter.AddComponent<BoxCollider>();
            wallLeft.center = new Vector3(localBounds.min.x - (wT / 2f), localBounds.center.y, localBounds.center.z);
            wallLeft.size = new Vector3(wT, localBounds.size.y, localBounds.size.z + (wT * 2));

            // NavMeshObstacle
            // Prevents animal AI from glitching against the walls by forcing them to path around it
            UnityEngine.AI.NavMeshObstacle aiObstacle = solidPerimeter.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            aiObstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            aiObstacle.center = localBounds.center;
            aiObstacle.size = localBounds.size;
            aiObstacle.carving = true;

            s_CustomKillers.Clear();

            // Slice the particle killer bounds into smaller chunks for better optimization with UniStorm
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

            // Register area as an Indoor Space so the temperature rises and campfires function properly
            IndoorSpaceTrigger spaceTrigger = particleKillerObj.AddComponent<IndoorSpaceTrigger>();
            spaceTrigger.m_UseOutdoorLighting = true;
            spaceTrigger.m_UseOutdoorTemperature = false;
            spaceTrigger.m_AllowCampfires = true;
            spaceTrigger.m_TemperatureDeltaCelsius = 25f;
            spaceTrigger.m_ValidSafehouse = true;
            spaceTrigger.m_DontCountAsInterior = true;
            spaceTrigger.m_IgnoreCabinFever = false;
            spaceTrigger.m_TriggerID = "CustomCampOffice_Trigger";

            yield return null;

            // Strip baked lightmaps to prevent glowing objects during the night in the exterior scene
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

            // Force global lighting/weather updates to recognize the merged environment
            Weather wFinal = GameManager.GetWeatherComponent();
            if (wFinal != null) wFinal.ForceOutdoorEnvironment();

            LightingManager.m_LevelLoadComplete = true;
            LightingManager.OnLevelLoadComplete();
            LightingManager.SetLightingStrengthDefault();
            UnityEngine.DynamicGI.UpdateEnvironment();

            s_IsCloningRoutineActive = false;
            s_RunCompleted = true;

            CleanupOrphanPlaceables();

            // Perform initial synchronization of visibility based on player's load-in position
            PlayerManager pmInit = GameManager.GetPlayerManagerComponent();
            if (pmInit != null && pmInit.transform.position.sqrMagnitude > 1f)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[DEBUG-INIT] Initial visibility sync using pm.transform. Pos: {pmInit.transform.position}");

                ApplyInitialSyncState(pmInit.transform.position);
            }
            else
            {
                if (s_DebugBounds)
                    MelonLogger.Msg("[DEBUG-INIT] pm.transform invalid (0,0,0 or null), falling back to GetPlayerTransform.");

                ApplyInitialSyncState();
            }

            MelonCoroutines.Start(DelayedInitialVisibilityCheck());

            // Start the watchdog to track position for weather occlusion updates
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
                    MelonLogger.Msg($"[DEBUG-INIT-DELAYED] 10-second delayed validation. Pos: {playerT.position}");

                ApplyInitialSyncState(playerT.position);
            }
        }

        // Calculates the bounding box of the interior to construct accurate colliders and particle killers
        private static Bounds ComputeLocalInteriorBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool first = true;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            foreach (var r in renderers)
            {
                if (r == null) continue;

                // Shadow_Caster proxy meshes are massive and distort actual geometry bounds
                if (r.gameObject.name.Contains("Shadow_Caster")) continue;

                Bounds wb = r.bounds;

                // Filter out massive objects (like terrain remnants) that skew the bounds
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

            // Fallback bounds just in case the calculation fails
            if (first) return new Bounds(new Vector3(0, 2f, 0), new Vector3(25f, 18f, 25f));
            Bounds result = new Bounds();
            result.SetMinMax(min, max);

            if (s_DebugBounds)
                MelonLogger.Msg($"[BOUNDS-DEBUG] RESULT -> min={min} max={max} center={result.center} size={result.size}");

            return result;
        }

        public static bool IsPositionInsideCabin(Vector3 pos)
        {
            if (s_MasterInterior == null || s_InteriorTrigger == null) return false;

            Vector3 localPos = s_MasterInterior.transform.InverseTransformPoint(pos);

            Bounds b = new Bounds(s_InteriorTrigger.center, s_InteriorTrigger.size);
            return b.Contains(localPos);
        }

        // Runs ONCE when scene/save is loaded. Syncs meshes correctly if player is inside or outside.
        // This is separate from the door interaction logic.
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

            // Adjust sound isolation depending on load position
            SetAudioOcclusion(isInside);

            if (s_DebugBounds)
                MelonLogger.Msg($"[INITIAL-SYNC] Pos: {pos} | isInside={isInside} | Mesh states synchronized.");
        }

        // Only updates wind/snow particle occlusion based on proximity.
        // Mesh visibility swapping is handled EXCLUSIVELY by PortalMagicPatch.
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

        public static IEnumerator DelayedFireRestore()
        {
            yield return null;
            yield return null;
            yield return null;

            if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData) && s_MasterInterior != null)
            {
                // 1. Forcibly register stoves and fires into the game's native FireManager
                var allFires = s_MasterInterior.GetComponentsInChildren<Il2Cpp.Fire>(true);
                foreach (var f in allFires) if (f != null && !Il2Cpp.FireManager.m_Fires.Contains(f)) Il2Cpp.FireManager.AddFire(f);

                var allWoodStoves = s_MasterInterior.GetComponentsInChildren<Il2Cpp.WoodStove>(true);
                foreach (var ws in allWoodStoves) if (ws != null && !Il2Cpp.FireManager.m_WoodStoves.Contains(ws)) Il2Cpp.FireManager.AddWoodStove(ws);

                var allCampfires = s_MasterInterior.GetComponentsInChildren<Il2Cpp.Campfire>(true);
                foreach (var cf in allCampfires) if (cf != null && !Il2Cpp.FireManager.m_Campfires.Contains(cf)) Il2Cpp.FireManager.AddCampfire(cf);

                // 2. Wait in the background for the native Start() method to execute and reset the fire
                // [CRASH FIX] If the player enters an original interior while we are waiting, our cloned interior is destroyed. We use a null check here to prevent crashes!
                while (s_MasterInterior != null && !s_MasterInterior.activeInHierarchy)
                {
                    yield return null;
                }

                // Safely abort the routine if the player left the scene and the custom interior was destroyed
                if (s_MasterInterior == null) yield break;

                yield return null;
                yield return null;

                // 3. THE NATIVE START() HAS FINISHED! NOW WE ACTIVATE THE PROTECTION SHIELD
                PreventFireDestructionPatch.s_ProtectInterior = true;

                // 4. We permanently stamp our fire data for a SECOND TIME (Stoves will begin to burn)
                // Because our shield is active, the game CANNOT DELETE the unlit stoves (nor our house)!
                Il2Cpp.FireManager.Deserialize(FireManagerStealerPatch.s_StolenFireData);

                // 5. DANGER HAS PASSED, DEACTIVATE THE PROTECTION SHIELD:
                PreventFireDestructionPatch.s_ProtectInterior = false;
                FireManagerStealerPatch.s_StolenFireData = "";

                if (s_DebugBounds)
                    MelonLogger.Msg("[FIRE-RESTORE-PERFECT] Start() was overridden, shield deployed, fire successfully restored!");
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

    // Intercepts door interactions. Instead of loading scenes, it teleports the player locally 
    // and swaps the active state of the interior and exterior meshes.
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
                MelonLogger.Msg($"[DEBUG-PORTAL] Door: {__instance.gameObject.name} | Root: {__instance.transform.root.name} | belongsToCampOffice={belongsToCampOffice} | targetScene={__instance.m_SceneToLoad}");
            }

            if (!belongsToCampOffice)
            {
                // The player is entering an ORIGINAL interior -> The EXTERIOR scene will actually be unloaded.
                // Right before the scene is destroyed, the game creates a "transition" save state.
                // In this state, only objects with activeSelf=true are serialized. Because our master interior
                // is often disabled (SetActive(false)), the items inside it are excluded from this save,
                // causing them to permanently disappear when we return.
                // Fix: Just BEFORE the original method (and the resulting save/scene-unload) triggers,
                // we temporarily activate the interior so the game detects and saves its contents.
                if (CampOfficeMod.s_MasterInterior != null && !CampOfficeMod.s_MasterInterior.activeSelf)
                {
                    CampOfficeMod.s_MasterInterior.SetActive(true);

                    if (CampOfficeMod.s_DebugBounds)
                        MelonLogger.Msg("[SAVE-FIX] Entering an original interior. Temporarily activating our custom interior to prevent save data loss.");
                }

                return true;
            }

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
                // Push the player forward slightly so they don't get stuck in the door collider
                pm.transform.position = pm.transform.position + (GameManager.GetVpFPSCamera().transform.forward * 2f);

                CampOfficeMod.SetAudioOcclusion(true);

                return false; // Skip original method
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
                // Push the player forward slightly
                pm.transform.position = pm.transform.position + (GameManager.GetVpFPSCamera().transform.forward * 2f);

                CampOfficeMod.SetAudioOcclusion(false);

                return false; // Skip original method
            }

            return true;
        }
    }

    // Prevents the game from instantiating duplicate GameManagers while we additively load interior scenes
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

    // Fakes the wind shelter status when the player's coordinates are inside the cabin bounds
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

    // Ensures fires/torches don't blow out while inside the cabin
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

    // Destroys random spawn objects during the cloning routine to prevent double-spawning
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

    // Intercepts placeables to prevent duplications from loading interior data over the exterior
    [HarmonyLib.HarmonyPatch(typeof(Il2CppTLD.Placement.Placeable), nameof(Il2CppTLD.Placement.Placeable.FindOrCreateAndDeserialize))]
    public class PlaceableFindOrCreatePatch
    {
        public static HashSet<string> s_InteriorPlaceableGuids = new HashSet<string>();

        public static bool Prefix(string guid, Il2CppTLD.Placement.PlaceableSaveData data, ref Il2CppTLD.Placement.Placeable __result)
        {
            if (!CampOfficeMod.s_RunCompleted && s_InteriorPlaceableGuids.Contains(guid))
            {
                if (CampOfficeMod.s_DebugBounds)
                    MelonLogger.Msg($"[PLACEABLE-SKIP] Blocked FindOrCreateAndDeserialize for GUID: {guid}");

                __result = null;
                return false;
            }

            return true;
        }
    }

    // =====================================================================================
    // SPECIFIC PATCHES FOR CONTAINER CRASHES AND ITEM DUPLICATION
    // =====================================================================================

    // 1. Prevents global gear duplication during the additive scene load
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
                return false; // Block deserialization while cloning is active
            }
            return true;
        }
    }

    // 2. Suppresses crashes caused by deleted containers being queried by position (Finalizer handles exceptions quietly)
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

    // 3. Prevents serial crashes when populating invalid/cloned containers with random gear
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

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.FireManager), nameof(Il2Cpp.FireManager.Deserialize))]
    public class FireManagerStealerPatch
    {
        public static string s_StolenFireData = "";

        public static void Prefix(string text)
        {
            // We intercept while the game loads (or saves) fire data on its own.
            // If our interior cloning routine is NOT active (i.e., this is the main game load), steal the data.
            if (!string.IsNullOrEmpty(text) && !CampOfficeMod.s_IsCloningRoutineActive)
            {
                s_StolenFireData = text;

                if (CampOfficeMod.s_DebugBounds)
                    MelonLogger.Msg($"[FIRE-STEAL] Fire data successfully copied ({text.Length} characters).");
            }
        }
    }

    // Prevents our custom interior from being destroyed when TLD's FireManager ruthlessly attempts to delete unlit fires/stoves
    [HarmonyLib.HarmonyPatch(typeof(UnityEngine.Object), nameof(UnityEngine.Object.Destroy), new System.Type[] { typeof(UnityEngine.Object) })]
    public class PreventFireDestructionPatch
    {
        public static bool s_ProtectInterior = false;

        public static bool Prefix(UnityEngine.Object obj)
        {
            // CANCEL the deletion ONLY if we activated the shield AND the target object is inside our custom interior
            if (s_ProtectInterior && obj != null && CampOfficeMod.s_MasterInterior != null)
            {
                GameObject go = obj.TryCast<GameObject>();
                if (go == null)
                {
                    Component comp = obj.TryCast<Component>();
                    if (comp != null) go = comp.gameObject;
                }

                if (go != null && go.transform.IsChildOf(CampOfficeMod.s_MasterInterior.transform))
                {
                    // If the object being destroyed is part of our custom interior, stop the destruction!
                    return false;
                }
            }
            return true;
        }
    }
}
