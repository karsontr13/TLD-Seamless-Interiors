using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System.Collections.Generic;

namespace SeamlessInteriors
{
    // The heart of the mod: every door that would normally trigger a scene load is
    // intercepted here and turned into a teleport inside the current region, so the
    // loading screen never appears.
    [HarmonyLib.HarmonyPatch(typeof(LoadScene), nameof(LoadScene.PerformInteraction))]
    public class PortalMagicPatch
    {
        public static bool Prefix(LoadScene __instance)
        {
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null) return true;

            // ─── SUB-INTERIOR LINK CHECK ───
            // Transition from inside one interior scene into another
            // (e.g. the basement door inside FarmHouse, the doors inside Carter Dam).
            // This has to run BEFORE the normal enter/exit logic.
            {
                var subResult = TryHandleSubInteriorLink(__instance);
                if (subResult.HasValue)
                    return subResult.Value; // true = let the game handle it, false = mod handled it
            }
            // ─── END SUB-INTERIOR LINK CHECK ───

            SeamlessInteriorInstance matchedInstance = null;
            bool isEntering = false;
            bool isExiting = false;

            // Does this door belong to one of the active buildings?
            // IMPORTANT: the RunCompleted check was REMOVED. Instances that are not ready
            // yet must match too, otherwise the game treats the door as an original
            // scene transition.
            foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                bool isShellDoor = instance.ExteriorShell != null && __instance.transform.IsChildOf(instance.ExteriorShell.transform);
                bool isInteriorDoor = instance.MasterInterior != null && __instance.transform.IsChildOf(instance.MasterInterior.transform);

                if (isShellDoor || isInteriorDoor)
                {
                    // Entering: the door's target scene is this interior's scene.
                    if (__instance.m_SceneToLoad == instance.Config.InteriorSceneBaseName)
                    {
                        matchedInstance = instance;
                        isEntering = true;
                        break;
                    }
                    // Exiting: the door is a child of MasterInterior and is not an entry.
                    else if (isInteriorDoor)
                    {
                        matchedInstance = instance;
                        isExiting = true;
                        break;
                    }
                    // SHELL CHILD DOOR POINTING AT A DIFFERENT SCENE:
                    // the door is a child of the shell, but m_SceneToLoad is not this
                    // instance's scene. It may be another instance's InteriorSceneBaseName
                    // (e.g. the basement's outer door sits on the FarmHouse shell but
                    // points at the FarmHouseABasement scene). Find that instance and
                    // redirect to it.
                    else if (isShellDoor)
                    {
                        foreach (var otherInstance in SeamlessInteriorsMod.ActiveInteriors.Values)
                        {
                            if (otherInstance == instance) continue;
                            if (__instance.m_SceneToLoad == otherInstance.Config.InteriorSceneBaseName)
                            {
                                matchedInstance = otherInstance;
                                isEntering = true;
                                break;
                            }
                        }
                        if (matchedInstance != null) break;

                        // No instance matched -> block it, so the original scene is not loaded.
                        matchedInstance = instance;
                        break;
                    }
                }
            }

            // A shell door was expected but nothing matched?
            // Door triggers (LoadScene/InteriorLoadTrigger) are not necessarily children
            // of the shell - they can exist as their own root objects on the map.
            // Match those to the right instance by proximity to FallbackPosition.
            // CRITICAL: the m_SceneToLoad match is MANDATORY. Without it the basement
            // door matches FarmHouse but neither the enter nor the exit flag can be set,
            // and it falls through to the original scene transition.
            if (matchedInstance == null)
            {
                float bestDist = float.MaxValue;
                foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
                {
                    float distToFallback = Vector3.Distance(__instance.transform.position, instance.Config.FallbackPosition);
                    if (distToFallback < 30f && distToFallback < bestDist)
                    {
                        bool canEnter = __instance.m_SceneToLoad == instance.Config.InteriorSceneBaseName;
                        bool canExit = instance.MasterInterior != null && __instance.transform.IsChildOf(instance.MasterInterior.transform);

                        // At least one must hold, otherwise this door is not this instance's.
                        if (!canEnter && !canExit) continue;

                        bestDist = distToFallback;
                        matchedInstance = instance;
                        isEntering = canEnter;
                        isExiting = !canEnter && canExit;
                    }
                }
            }

            if (matchedInstance == null)
            {
                // Transitioning into an original (unmodded) interior: save the clone data
                // first so none of it is lost.
                //
                // PERFORMANCE: this used to SetActive(true) every clone scene one by one
                // and close them again after saving - on a 15-building map that was 30
                // activations per door use, i.e. OnEnable/OnDisable on thousands of
                // components. No longer needed: the save functions only open a clone
                // scene briefly when there REALLY is a borderline candidate
                // (see SavePlaceablePositions / SaveInactiveSceneGearItems).
                SceneScan.Begin();
                try
                {
                    SeamlessInteriorsMod.SaveAllPlaceablePositions();
                    SeamlessInteriorsMod.SaveAllContainerData();
                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg("[PORTAL-SAVE] Orijinal ic mekana gecis oncesi Placeable pozisyonlari ve konteyner verileri kaydedildi.");
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[PORTAL-SAVE] Hata: {ex.Message}");
                }
                finally
                {
                    SceneScan.End();
                }

                return true;
            }

            // Matched but not ready yet - BLOCK the original scene transition.
            if (!matchedInstance.RunCompleted)
            {
                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg($"[PORTAL-BLOCK] {matchedInstance.Config.ResolvedInstanceId} henuz hazir degil, gecis engellendi.");
                return false; // neither load the original scene nor teleport; just block
            }

            if (SeamlessInteriorsMod.s_DebugBounds)
            {
                MelonLogger.Msg($"[DEBUG-PORTAL] Door: {__instance.gameObject.name} | Instance: {matchedInstance.Config.ResolvedInstanceId} | targetScene={__instance.m_SceneToLoad}");
            }

            // Tells the visibility watchdog to stand back for a moment.
            SeamlessInteriorsMod.NotifyPortalUsed();

            // Door sound effect, as in the vanilla transition.
            SeamlessInteriorsMod.PlayDoorTransitionSound();

            if (isEntering)
            {
                if (matchedInstance.MasterInterior != null)
                {
                    matchedInstance.MasterInterior.SetActive(true);
                    // Force every structural renderer on (they can get lost across an
                    // activate/deactivate cycle).
                    foreach (var r in matchedInstance.MasterInterior.GetComponentsInChildren<Renderer>(true))
                        if (r != null) r.enabled = true;

                    // Renderers forced on means colliders must be too, otherwise items are
                    // visible but non-interactive (Interactivity.cs).
                    SeamlessInteriorsMod.RestoreInteriorItemColliders(matchedInstance);
                }
                if (matchedInstance.ExteriorShell != null) matchedInstance.ExteriorShell.SetActive(false);

                // SUB-INTERIOR FIX: if the entered instance has no shell (a sub-interior),
                // also close the parent instance's shell and MasterInterior.
                if (matchedInstance.ExteriorShell == null)
                {
                    foreach (var parentInst in SeamlessInteriorsMod.ActiveInteriors.Values)
                    {
                        if (parentInst.Config.SubInteriorLinks == null) continue;
                        foreach (var link in parentInst.Config.SubInteriorLinks)
                        {
                            if (link.TargetInstanceId == matchedInstance.Config.ResolvedInstanceId)
                            {
                                if (parentInst.ExteriorShell != null) parentInst.ExteriorShell.SetActive(false);
                                SeamlessInteriorsMod.HideInteriorCompletely(parentInst);
                                break;
                            }
                        }
                    }
                }

                // Hiding outside-world objects: applied once, after ALL activity changes
                // are done, and independently of order.
                SeamlessInteriorsMod.SyncExternalHiddenObjects();

                SeamlessInteriorsMod.SetInteriorItemsVisible(matchedInstance, true);
                SeamlessInteriorsMod.SetAudioOcclusion(true);
                SeamlessInteriorsMod.s_IsPlayerInsideClone = true;

                Vector3 spawnPos = GetDoorEntryPosition(matchedInstance, __instance);
                spawnPos = SnapToGround(spawnPos);
                GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);

                return false;
            }

            if (isExiting)
            {
                Vector3 spawnPos = GetDoorExitPosition(matchedInstance, __instance);

                // DYNAMIC ELEVATION CORRECTION (guards against config mistakes).
                // If the author raised ExitPosition by hand instead of using YOffset,
                // spawnPos.y ends up hanging in the air.
                if (matchedInstance.ExteriorShell != null && matchedInstance.MasterInterior != null)
                {
                    float yDelta = matchedInstance.MasterInterior.transform.position.y - matchedInstance.ExteriorShell.transform.position.y;

                    if (Mathf.Abs(yDelta) > 1.0f)
                    {
                        float distToHigh = Mathf.Abs(spawnPos.y - matchedInstance.MasterInterior.transform.position.y);
                        float distToLow = Mathf.Abs(spawnPos.y - matchedInstance.ExteriorShell.transform.position.y);

                        // Closer to the RAISED clone scene (MasterInterior) means that lift
                        // (yDelta) is baked into spawnPos. Subtract it to bring the point
                        // down to the exterior shell's ground level.
                        if (distToHigh < distToLow)
                        {
                            spawnPos.y -= yDelta;
                            if (SeamlessInteriorsMod.s_DebugBounds)
                                MelonLogger.Msg($"[PORTAL-FIX] Dısarı cıkıs Y ekseni otomatik duzeltildi. Cıkarılan Yükseklik: {yDelta}");
                        }
                    }
                }

                // ORDER MATTERS: hide the items and deactivate MasterInterior FIRST.
                // Teleporting before that lets the game's own mechanics (or TeleportPlayer)
                // collide with the still-active clone scene colliders up in the air and
                // fling the player upwards (the "spawned in mid-air" bug).
                SeamlessInteriorsMod.HideInteriorCompletely(matchedInstance);
                if (matchedInstance.ExteriorShell != null) matchedInstance.ExteriorShell.SetActive(true);

                // SUB-INTERIOR FIX: if the exited instance has no shell (a sub-interior),
                // bring the parent instance's shell and external objects back too.
                if (matchedInstance.ExteriorShell == null)
                {
                    foreach (var parentInst in SeamlessInteriorsMod.ActiveInteriors.Values)
                    {
                        if (parentInst.Config.SubInteriorLinks == null) continue;
                        foreach (var link in parentInst.Config.SubInteriorLinks)
                        {
                            if (link.TargetInstanceId == matchedInstance.Config.ResolvedInstanceId)
                            {
                                SeamlessInteriorsMod.HideInteriorCompletely(parentInst);
                                if (parentInst.ExteriorShell != null) parentInst.ExteriorShell.SetActive(true);
                                break;
                            }
                        }
                    }
                }

                SeamlessInteriorsMod.SyncExternalHiddenObjects();

                SeamlessInteriorsMod.SetAudioOcclusion(false);
                SeamlessInteriorsMod.s_IsPlayerInsideClone = false;

                // Make sure wind is running again (ForceStopped may have been left set).
                var windExit = UnityEngine.Object.FindObjectOfType<Il2Cpp.Wind>();
                if (windExit != null)
                {
                    windExit.m_WindAudioForceStopped = false;
                }

                // Ground is resolved and the teleport performed LAST, so the outside
                // world's ground is the reference.
                spawnPos = SnapToGround(spawnPos);
                GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);

                return false;
            }

            return true;
        }

        // Resolves the interior spawn point for the door that was used.
        private static Vector3 GetDoorEntryPosition(SeamlessInteriorInstance instance, LoadScene door)
        {
            Vector3 clickedDoorPos = door.transform.position;

            if (instance.Config.DoorSpawnPoints != null && instance.Config.DoorSpawnPoints.Count > 0)
            {
                DoorSpawnPoint closestDoor = null;
                float minDistance = float.MaxValue;

                foreach (var dsp in instance.Config.DoorSpawnPoints)
                {
                    // The NAME CHECK WAS DROPPED ENTIRELY: in The Long Dark the name of
                    // the clicked trigger usually differs from the prefab name.
                    // Matching is done on XZ distance only.
                    float dist = Vector2.Distance(new Vector2(clickedDoorPos.x, clickedDoorPos.z), new Vector2(dsp.DoorTransformPosition.x, dsp.DoorTransformPosition.z));

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestDoor = dsp;
                    }
                }

                // A wide 15m tolerance: it will certainly find the nearest door.
                if (closestDoor != null && minDistance < 15.0f)
                {
                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg($"[DEBUG-DOOR] GIRIS BASARILI - Tiklanan Obj: {door.gameObject.name}, Eslesen Kapi: {closestDoor.DoorName}, Mesafe: {minDistance}");

                    return closestDoor.EntryPosition;
                }
            }

            // Fallback
            if (instance.Config.EntrySpawnPosition != Vector3.zero) return instance.Config.EntrySpawnPosition;
            Transform sp = door.transform.Find("SpawnPoint");
            return sp != null ? sp.position : door.transform.position;
        }

        // Resolves the outdoor spawn point for the door that was used.
        private static Vector3 GetDoorExitPosition(SeamlessInteriorInstance instance, LoadScene door)
        {
            Vector3 clickedDoorPos = door.transform.position;

            if (instance.Config.DoorSpawnPoints != null && instance.Config.DoorSpawnPoints.Count > 0)
            {
                DoorSpawnPoint closestDoor = null;
                float minDistance = float.MaxValue;

                foreach (var dsp in instance.Config.DoorSpawnPoints)
                {
                    // NOTE: when exiting, the player is INSIDE. So the clicked door is
                    // compared against the interior 'EntryPosition', not the outdoor
                    // 'DoorTransformPosition'.
                    float dist = Vector2.Distance(new Vector2(clickedDoorPos.x, clickedDoorPos.z), new Vector2(dsp.EntryPosition.x, dsp.EntryPosition.z));

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestDoor = dsp;
                    }
                }

                if (closestDoor != null && minDistance < 15.0f)
                {
                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg($"[DEBUG-DOOR] CIKIS BASARILI - Tiklanan Obj: {door.gameObject.name}, Eslesen Kapi: {closestDoor.DoorName}, Mesafe: {minDistance}");

                    return closestDoor.ExitPosition;
                }
            }

            // Fallback
            if (instance.Config.ExitSpawnPosition != Vector3.zero) return instance.Config.ExitSpawnPosition;
            Transform sp = door.transform.Find("SpawnPoint");
            Vector3 fallbackPos = sp != null ? sp.position : door.transform.position;
            // On the way out the door lives in the clone scene, so it is raised on Y.
            // Subtract YOffset to spawn at the outside world's door level.
            fallbackPos.y -= instance.Config.YOffset;
            return fallbackPos;
        }

        private static Vector3 SnapToGround(Vector3 pos)
        {
            Vector3 rayOrigin = pos + Vector3.up * 0.5f;
            float maxDrop = 2.5f;

            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, maxDrop + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            RaycastHit? bestHit = null;
            float bestDist = float.MaxValue;

            // Nearest non-trigger surface that is not the player themselves.
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.isTrigger) continue;
                if (hit.collider.transform.root.name.Contains("CHARACTER_FPSPlayer")) continue;

                if (hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    bestHit = hit;
                }
            }

            if (bestHit.HasValue)
                return new Vector3(pos.x, bestHit.Value.point.y + 0.05f, pos.z);

            return pos;
        }

        private static bool? TryHandleSubInteriorLink(LoadScene door)
        {
            Vector3 doorPos = door.transform.position;
            string sceneToLoad = door.m_SceneToLoad;

            // 1) PARENT -> CHILD: the player is inside the parent instance, going down.
            //    CRITICAL FILTER: the door's m_SceneToLoad MUST point at the child scene,
            //    otherwise FarmHouse's normal exit doors (leading to RuralRegion) are
            //    caught too.
            //    EXTRA FILTER: the door MUST be a child of the parent MasterInterior.
            //    Outside doors (on the shell or on the map) must not enter this flow -
            //    they are routed straight to the basement instance by the normal flow.
            foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                if (!instance.RunCompleted) continue;
                if (instance.Config.SubInteriorLinks == null || instance.Config.SubInteriorLinks.Count == 0) continue;
                if (instance.MasterInterior == null) continue;

                bool isDoorInParent = door.transform.IsChildOf(instance.MasterInterior.transform);
                if (!isDoorInParent) continue;

                foreach (var link in instance.Config.SubInteriorLinks)
                {
                    var targetInstance = FindInstance(link.TargetInstanceId);
                    if (targetInstance == null) continue;

                    // Is the door loading the child scene?
                    if (sceneToLoad != targetInstance.Config.InteriorSceneBaseName) continue;

                    // Position check (there can be several SubInteriorLinks).
                    float dist = Vector2.Distance(
                        new Vector2(doorPos.x, doorPos.z),
                        new Vector2(link.ParentDoorPosition.x, link.ParentDoorPosition.z));
                    if (dist > 15f) continue;

                    if (!targetInstance.RunCompleted)
                    {
                        if (SeamlessInteriorsMod.s_DebugBounds)
                            MelonLogger.Msg($"[SUB-LINK-BLOCK] {link.TargetInstanceId} henuz hazir degil.");
                        return false;
                    }

                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg($"[SUB-LINK] PARENT→CHILD: {instance.Config.ResolvedInstanceId} → {link.TargetInstanceId} (door={door.gameObject.name}, m_SceneToLoad={sceneToLoad})");

                    SeamlessInteriorsMod.NotifyPortalUsed();

                    SeamlessInteriorsMod.PlayDoorTransitionSound();

                    // Activate the child and force all of its renderers on.
                    if (targetInstance.MasterInterior != null)
                    {
                        targetInstance.MasterInterior.SetActive(true);
                        foreach (var r in targetInstance.MasterInterior.GetComponentsInChildren<Renderer>(true))
                            if (r != null) r.enabled = true;

                        SeamlessInteriorsMod.RestoreInteriorItemColliders(targetInstance);
                    }
                    if (targetInstance.ExteriorShell != null) targetInstance.ExteriorShell.SetActive(false);

                    // ORDER MATTERS: close the parent FIRST, then show the child's items.
                    // The parent's closing step scans the items in its own volume and, in
                    // nested buildings (basement/upper floor), also covers the child's
                    // items. In the reverse order the child's colliders were switched off
                    // again immediately.
                    SeamlessInteriorsMod.HideInteriorCompletely(instance);
                    if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(false);

                    SeamlessInteriorsMod.SyncExternalHiddenObjects();

                    SeamlessInteriorsMod.SetInteriorItemsVisible(targetInstance, true);

                    // Teleport the player to the child spawn point.
                    Vector3 spawnPos = SnapToGround(link.ChildSpawnPosition);
                    GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);

                    SeamlessInteriorsMod.SetAudioOcclusion(true);
                    SeamlessInteriorsMod.s_IsPlayerInsideClone = true;

                    return false;
                }
            }

            // 2) CHILD -> PARENT: the player is inside the child instance, going back up.
            //    CRITICAL FILTER: the door's m_SceneToLoad MUST point at the parent scene.
            //    The basement's outside door has m_SceneToLoad = "RuralRegion", which is
            //    not the parent scene "FarmHouseA", so it does not match here.
            //    The basement's upstairs door has m_SceneToLoad = "FarmHouseA", so it
            //    matches and teleports to the parent.
            foreach (var parentInstance in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                if (!parentInstance.RunCompleted) continue;
                if (parentInstance.Config.SubInteriorLinks == null || parentInstance.Config.SubInteriorLinks.Count == 0) continue;

                foreach (var link in parentInstance.Config.SubInteriorLinks)
                {
                    var childInstance = FindInstance(link.TargetInstanceId);
                    if (childInstance == null || !childInstance.RunCompleted) continue;
                    if (childInstance.MasterInterior == null) continue;

                    // Is the door loading the parent scene?
                    if (sceneToLoad != parentInstance.Config.InteriorSceneBaseName) continue;

                    // The door MUST be a child of the child MasterInterior.
                    bool isDoorInChild = door.transform.IsChildOf(childInstance.MasterInterior.transform);
                    if (!isDoorInChild) continue;

                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg($"[SUB-LINK] CHILD→PARENT: {link.TargetInstanceId} → {parentInstance.Config.ResolvedInstanceId} (door={door.gameObject.name}, m_SceneToLoad={sceneToLoad})");

                    SeamlessInteriorsMod.NotifyPortalUsed();

                    SeamlessInteriorsMod.PlayDoorTransitionSound();

                    // Activate the parent and force all of its renderers on.
                    if (parentInstance.MasterInterior != null)
                    {
                        parentInstance.MasterInterior.SetActive(true);
                        foreach (var r in parentInstance.MasterInterior.GetComponentsInChildren<Renderer>(true))
                            if (r != null) r.enabled = true;

                        SeamlessInteriorsMod.RestoreInteriorItemColliders(parentInstance);
                    }
                    if (parentInstance.ExteriorShell != null) parentInstance.ExteriorShell.SetActive(false);

                    // ORDER MATTERS: close the child FIRST, then show the parent's items
                    // (same reason as the downward transition above).
                    SeamlessInteriorsMod.HideInteriorCompletely(childInstance);
                    if (childInstance.ExteriorShell != null) childInstance.ExteriorShell.SetActive(true);

                    // Outside-world objects: the child's list OVERLAPS heavily with the
                    // parent's (shared ancestor colliders). The child's list used to be
                    // re-enabled unconditionally here, bringing back the objects the
                    // parent had just hidden. The decision is now made in one place, based
                    // on which clones are currently open.
                    SeamlessInteriorsMod.SyncExternalHiddenObjects();

                    SeamlessInteriorsMod.SetInteriorItemsVisible(parentInstance, true);

                    // Teleport the player to the parent spawn point.
                    Vector3 spawnPos = SnapToGround(link.ParentSpawnPosition);
                    GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);

                    SeamlessInteriorsMod.SetAudioOcclusion(true);
                    SeamlessInteriorsMod.s_IsPlayerInsideClone = true;

                    return false;
                }
            }

            return null; // no SubInteriorLink matched
        }

        private static SeamlessInteriorInstance FindInstance(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            SeamlessInteriorsMod.ActiveInteriors.TryGetValue(instanceId, out var inst);
            return inst;
        }
    }
}
