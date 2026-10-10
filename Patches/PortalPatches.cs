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
    // Arriving back from the mine the game spawns the player at a point the region lacks; the drop before
    // SI places them in the clone must not hurt (sprained ankle).
    [HarmonyLib.HarmonyPatch(typeof(FallDamage), nameof(FallDamage.ApplyFallDamage))]
    public class ArrivalFallDamagePatch
    {
        public static bool Prefix()
        {
            if (!SeamlessInteriorsMod.ArrivalProtected) return true;
            MelonLogger.Msg("[DONUS] klona varis sirasindaki dusus hasari engellendi.");
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(LoadingZone), nameof(LoadingZone.OnTriggerEnter))]
    public class LoadingZoneEnterPatch
    {
        public static void Prefix(LoadingZone __instance, Collider other)
        {
            if (__instance == null || other == null || !PlayerRefs.IsUnderPlayer(other.transform)) return;
            PortalMagicPatch.OnLoadingZone(__instance.m_PartnerLoadScene);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(LoadingZone), nameof(LoadingZone.PerformInteraction))]
    public class LoadingZoneInteractPatch
    {
        public static void Prefix(LoadingZone __instance)
        {
            if (__instance != null) PortalMagicPatch.OnLoadingZone(__instance.m_PartnerLoadScene);
        }
    }

    // Walk-in transitions (the whaling ship's hull opening) fire from OnTriggerEnter and never
    // reach PerformInteraction; route the player's contact through the same door logic.
    [HarmonyLib.HarmonyPatch(typeof(LoadScene), nameof(LoadScene.OnTriggerEnter))]
    public class PortalContactPatch
    {
        public static bool Prefix(LoadScene __instance, Collider other)
        {
            if (__instance == null || other == null || !__instance.m_TransitionOnContact) return true;
            if (!PlayerRefs.IsUnderPlayer(other.transform)) return true;
            if (PortalMagicPatch.Prefix(__instance)) return true;

            // No click ended this one: drop the hovered trigger, or the clone's exit prompt stays on screen.
            GameManager.GetPlayerManagerComponent()?.ResetInteraction();
            return false;
        }
    }

    // A hold-to-use door (the whaling ship's hull) is finished by LoadScene.Update calling Activate itself,
    // never PerformInteraction; it loaded the original scene. Doors already decided this frame pass.
    [HarmonyLib.HarmonyPatch(typeof(LoadScene), nameof(LoadScene.Activate), new[] { typeof(bool) })]
    public class PortalActivatePatch
    {
        public static bool Prefix(LoadScene __instance)
        {
            if (__instance == null || PortalMagicPatch.DecidedThisFrame(__instance)) return true;
            if (PortalMagicPatch.Prefix(__instance)) return true;
            GameManager.GetPlayerManagerComponent()?.ResetInteraction();
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(LoadScene), nameof(LoadScene.PerformInteraction))]
    public class PortalMagicPatch
    {
        private static int s_DecidedFrame = -1;
        private static LoadScene s_DecidedDoor;

        internal static bool DecidedThisFrame(LoadScene door)
        {
            return s_DecidedFrame == Time.frameCount && s_DecidedDoor != null && s_DecidedDoor.Pointer == door.Pointer;
        }

        internal static void MarkDecided(LoadScene door)
        {
            s_DecidedFrame = Time.frameCount;
            s_DecidedDoor = door;
        }

        public static bool Prefix(LoadScene __instance)
        {
            MarkDecided(__instance);
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null) return true;

            // Back into a cloned building from a scene SI does not clone: load its region instead.
            if (TryRedirectIntoClone(__instance)) return true;

            // The lighting in force now is the side being left; see ParkedRoomDoorLighting.cs.
            SeamlessInteriorsMod.RememberLightingBeforeDoor();

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
                    // A generated config's door on to another interior (the concentrator's mine shafts)
                    // is no way out: leave it to the game's own transition.
                    else if (isInteriorDoor && !LeadsToARegion(__instance.m_SceneToLoad)
                             && (instance.Config.DoorSpawnPoints == null || instance.Config.DoorSpawnPoints.Count == 0)
                             && instance.Config.ExitSpawnPosition == Vector3.zero)
                    {
                        NoteLeftThrough(instance, __instance);
                        continue;
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
                        // Several buildings can share that scene: take the one nearest the door, not the first.
                        float bestRedirectDist = float.MaxValue;
                        foreach (var otherInstance in SeamlessInteriorsMod.ActiveInteriors.Values)
                        {
                            if (otherInstance == instance) continue;
                            if (__instance.m_SceneToLoad != otherInstance.Config.InteriorSceneBaseName) continue;

                            float d = Vector3.Distance(__instance.transform.position, otherInstance.Config.FallbackPosition);
                            if (d < bestRedirectDist)
                            {
                                bestRedirectDist = d;
                                matchedInstance = otherInstance;
                                isEntering = true;
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
                    // Measured to the nearer half: the exit doors of a parked clone are
                    // nowhere near FallbackPosition, and matching them on that alone let
                    // them fall through to a vanilla scene transition.
                    float distToFallback = instance.DistanceFrom(__instance.transform.position);
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

            // A sprawling interior's far entrances (the mine concentrator's doors lie up to 130 m
            // from its clone): the region's only clone of that scene takes them.
            if (matchedInstance == null)
            {
                SeamlessInteriorInstance only = null;
                int count = 0;
                string doorRegion = SeamlessInteriorsMod.RealSceneName(__instance.gameObject.scene);
                foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
                {
                    if (instance.MasterInterior == null || !IsRegionScene(doorRegion, instance.Config.ExteriorSceneName)) continue;
                    if (__instance.m_SceneToLoad != instance.Config.InteriorSceneBaseName) continue;
                    only = instance;
                    count++;
                }
                if (count == 1)
                {
                    matchedInstance = only;
                    isEntering = true;
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

            // Through black first, when asked for: this call only starts the fade, and the door
            // is used again, for real, once the screen is black. See DeferDoorForFade.
            if (DeferDoorForFade(__instance)) return false;

            // Tells the visibility watchdog to stand back for a moment.
            SeamlessInteriorsMod.NotifyPortalUsed();

            // Door sound effect, as in the vanilla transition.
            SeamlessInteriorsMod.PlayDoorTransitionSound();

            if (isEntering)
            {
                // A shell left in a switched-off variant group would stay up around the player.
                SeamlessInteriorsMod.RevalidateExteriorShellAtDoor(matchedInstance);

                // LAST CHANCE TO FILL THE BUILDING.
                //
                // Content is normally restored by the prefetch while the player walks up
                // (see SeamlessInteriorsMod.Hydration.cs), but they can arrive without
                // any warning - a teleport, or a save loaded right on the doorstep. The
                // door is the point of no return: one frame later they are inside and
                // looking at the room, so this call blocks until the room is real.
                long perf = SeamlessInteriorsMod.PerfProbe.Begin();
                SeamlessInteriorsMod.EnsureHydratedNow(matchedInstance, "kapidan giris");
                SeamlessInteriorsMod.PerfProbe.End(SeamlessInteriorsMod.PerfProbe.Section.PortalHydrate, perf);

                if (matchedInstance.MasterInterior != null)
                {
                    // Timed apart: SetActive is Unity walking the hierarchy and cannot be
                    // avoided, the renderer sweep is this mod's own and might be.
                    perf = SeamlessInteriorsMod.PerfProbe.Begin();
                    matchedInstance.MasterInterior.SetActive(true);
                    SeamlessInteriorsMod.PerfProbe.End(SeamlessInteriorsMod.PerfProbe.Section.PortalActivate, perf);

                    // Force every structural renderer on (they can get lost across an
                    // activate/deactivate cycle).
                    perf = SeamlessInteriorsMod.PerfProbe.Begin();
                    SeamlessInteriorsMod.ShowCloneRenderers(matchedInstance);
                    SeamlessInteriorsMod.PerfProbe.End(SeamlessInteriorsMod.PerfProbe.Section.PortalForceRenderers, perf);

                    // Renderers forced on means colliders must be too, otherwise items are
                    // visible but non-interactive (Interactivity.cs).
                    perf = SeamlessInteriorsMod.PerfProbe.Begin();
                    SeamlessInteriorsMod.RestoreInteriorItemColliders(matchedInstance);
                    SeamlessInteriorsMod.PerfProbe.End(SeamlessInteriorsMod.PerfProbe.Section.PortalColliders, perf);
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
                perf = SeamlessInteriorsMod.PerfProbe.Begin();
                SeamlessInteriorsMod.SyncExternalHiddenObjects();
                SeamlessInteriorsMod.PerfProbe.End(SeamlessInteriorsMod.PerfProbe.Section.PortalHideSync, perf);

                perf = SeamlessInteriorsMod.PerfProbe.Begin();
                SeamlessInteriorsMod.SetInteriorItemsVisible(matchedInstance, true);
                SeamlessInteriorsMod.PerfProbe.End(SeamlessInteriorsMod.PerfProbe.Section.PortalItemsVisible, perf);

                perf = SeamlessInteriorsMod.PerfProbe.Begin();
                SeamlessInteriorsMod.SetAudioOcclusion(true);
                SeamlessInteriorsMod.MarkPlayerInside(matchedInstance, "kapidan giris");

                Vector3 spawnPos = GetDoorEntryPosition(matchedInstance, __instance);
                spawnPos = SnapToGround(spawnPos);
                GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, FacingIntoRoom(matchedInstance, spawnPos, GameManager.GetPlayerTransform().rotation));
                SeamlessInteriorsMod.PerfProbe.End(SeamlessInteriorsMod.PerfProbe.Section.PortalMarkInside, perf);

                // AFTER the teleport, so the player is already in the building. The
                // watchdog would otherwise do this, but NotifyPortalUsed above has just
                // told it to stand back, so the blizzard kept falling indoors until the
                // suppression expired.
                SeamlessInteriorsMod.ApplyParticleKillers(matchedInstance, true);

                // A PARKED ROOM IS SWITCHED OVER IN THIS FRAME, NOT THE NEXT. The stand-in eye, and the
                // fog, snow and snow planes that key on it, are otherwise set by the next OnUpdate, and
                // this door runs after this frame's. Measured 2026-09-22 with a per-frame probe: the
                // first frame in the room was drawn with the fog pass on and the eye off, then the next
                // frame took about 400 ms, so that one wrong frame stayed on screen - the owner's "fog
                // for a split second". Applied here in the console, the first frame had the eye on and
                // the fog off, and the owner: "fog flash is gone".
                SeamlessInteriorsMod.TickParkedCullEye();
                SeamlessInteriorsMod.TickParkedRoomFog(GameManager.GetMainCamera());

                // Its small objects were culled from 80 m away; they appear now, not faded in.
                SeamlessInteriorsMod.ArriveWithoutCrossFade(matchedInstance);
                SeamlessInteriorsMod.LightDoorFrameForArrival(matchedInstance, true);

                return false;
            }

            if (isExiting)
            {
                Vector3 spawnPos = GetDoorExitPosition(matchedInstance, __instance, out bool vanillaExitPoint);

                // DYNAMIC ELEVATION CORRECTION (guards against config mistakes).
                // If the author raised ExitPosition by hand instead of using YOffset,
                // spawnPos.y ends up hanging in the air. The game's own exit points are on the ground already.
                if (!vanillaExitPoint && matchedInstance.ExteriorShell != null && matchedInstance.MasterInterior != null)
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

                // ORDER MATTERS: close the building FIRST - hide the items, deactivate
                // MasterInterior, bring the shell (and a sub-interior's parent shell) back.
                // Teleporting before that lets the game's own mechanics (or TeleportPlayer)
                // collide with the still-active clone scene colliders up in the air and
                // fling the player upwards (the "spawned in mid-air" bug).
                // The door-less exit runs the very same sequence (PlayerLocation.cs).
                SeamlessInteriorsMod.ExitInteriorState(matchedInstance, "kapidan cikis", true);

                // Ground is resolved and the teleport performed LAST, so the outside
                // world's ground is the reference.
                spawnPos = SnapToGround(spawnPos);
                GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);

                SeamlessInteriorsMod.ApplyParticleKillers(matchedInstance, false);

                // AND SWITCHED BACK IN THIS FRAME ON THE WAY OUT. The entry above got this on
                // 2026-09-22; the exit did not, so the first frames outside kept the room's state,
                // the fog pass off most visibly (owner, 2026-09-28: "going out it was most noticeably
                // the fog being absent"). Tried in the console as a postfix doing exactly this, then
                // judged "better". The body and near clip still follow in the next OnUpdate; he: those
                // can be late, only what is seen matters.
                SeamlessInteriorsMod.TickParkedCullEye();
                SeamlessInteriorsMod.TickParkedRoomFog(GameManager.GetMainCamera());
                SeamlessInteriorsMod.TickParkedSnowEmitters();
                SeamlessInteriorsMod.TickParkedSnowPlanes();
                SeamlessInteriorsMod.LightDoorFrameForArrival(matchedInstance, false);

                return false;
            }

            return true;
        }

        // ARRIVING INDOORS, THE PLAYER FACES INTO THE ROOM, straight away from the room door they came in
        // by, as the game's own spawn points do. The config holds entry positions only, so the facing
        // used to be whatever it was outside, which is into the room only while the room's door stands
        // square to the building's. At Barn A, turned to its barn, the side door's room door is on a
        // wall a quarter turn round and the player arrived facing the wall to their left (owner,
        // 2026-09-30). The direction is from the room door to the landing spot, which the entries place
        // just in front of each door; no door within reach, and the facing is left as it was.
        private const float ENTRY_FACING_DOOR_REACH = 3f;

        private static Quaternion FacingIntoRoom(SeamlessInteriorInstance instance, Vector3 spawnPos, Quaternion asWas)
        {
            if (instance == null || instance.MasterInterior == null) return asWas;
            Vector3 best = Vector3.zero; float bestSqr = ENTRY_FACING_DOOR_REACH * ENTRY_FACING_DOOR_REACH;
            foreach (var door in instance.MasterInterior.GetComponentsInChildren<LoadScene>(true))
            {
                if (door == null) continue;
                Vector3 away = spawnPos - door.transform.position;
                away.y = 0f;
                float sqr = away.sqrMagnitude;
                if (sqr < bestSqr && sqr > 0.01f) { bestSqr = sqr; best = away; }
            }
            if (best == Vector3.zero) return asWas;
            return Quaternion.LookRotation(best.normalized, Vector3.up);
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

                    // The CONFIG coordinates only are carried to the parked room. They were
                    // authored against a clone standing on its shell, so a parked clone has
                    // moved, and perhaps turned, out from under them. Positions read from
                    // objects in the clone (below) have already moved with it and must not be
                    // carried a second time.
                    return SeamlessInteriorsMod.ParkedInteriorPoint(instance, closestDoor.EntryPosition);
                }
            }

            // Fallback
            if (instance.Config.EntrySpawnPosition != Vector3.zero) return SeamlessInteriorsMod.ParkedInteriorPoint(instance, instance.Config.EntrySpawnPosition);

            // No hand-measured point (generated configs): the clone's copy of the vanilla
            // spawn point this door names, e.g. "CoastalHouseAEnterPoint".
            if (instance.MasterInterior != null)
            {
                Transform enter = FindNamedNearest(instance.MasterInterior.transform, door.m_ExitPointName, clickedDoorPos);
                if (enter != null) return enter.position;
            }

            Transform sp = door.transform.Find("SpawnPoint");
            return sp != null ? sp.position : door.transform.position;
        }

        // Resolves the outdoor spawn point for the door that was used.
        // vanillaPoint: the position is the game's own exit point, already on the outside ground.
        private static Vector3 GetDoorExitPosition(SeamlessInteriorInstance instance, LoadScene door, out bool vanillaPoint)
        {
            vanillaPoint = false;
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
                    // The clicked door is inside the clone, so the interior coordinate it is
                    // compared against has to be moved with it.
                    Vector3 entryPos = SeamlessInteriorsMod.ParkedInteriorPoint(instance, dsp.EntryPosition);
                    float dist = Vector2.Distance(new Vector2(clickedDoorPos.x, clickedDoorPos.z), new Vector2(entryPos.x, entryPos.z));

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

            // No hand-measured point (generated configs): the vanilla exit point this clone door
            // names, e.g. "CoastalHouseAExitPoint" - a child of the shell or of its variant group.
            if (instance.ExteriorShell != null)
            {
                Transform search = instance.ExteriorShell.transform;
                for (int level = 0; level < 3 && search != null; level++, search = search.parent)
                {
                    Transform exit = FindNamedNearest(search, door.m_ExitPointName, clickedDoorPos);
                    if (exit == null) continue;
                    vanillaPoint = true;
                    return OnTheGround(exit.position);
                }
            }

            // Entrances outside the shell keep their exit point elsewhere in the region
            // (Milton trailer: Design/Transitions/MiltonTrailer). Whole-scene search, once per exit.
            // The region's sub-scenes too: the mine concentrator's exit points live in MiningRegion_SANDBOX.
            if (!string.IsNullOrEmpty(door.m_ExitPointName))
            {
                // Measured from where the door stands outdoors: a parked clone's door is ParkOffset up.
                Vector3 outdoorDoorPos = clickedDoorPos - instance.Config.ParkOffset;
                Transform best = null;
                float bestDist = 30f * 30f;
                for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded || !IsRegionScene(SeamlessInteriorsMod.RealSceneName(scene), instance.Config.ExteriorSceneName)) continue;
                    foreach (GameObject root in SeamlessInteriorsMod.RealRootObjects(scene))
                    {
                        if (root == null || (instance.MasterInterior != null && root == instance.MasterInterior)) continue;
                        Transform t = FindNamedNearest(root.transform, door.m_ExitPointName, outdoorDoorPos);
                        if (t == null) continue;
                        float d = (t.position - outdoorDoorPos).sqrMagnitude;
                        if (d < bestDist) { bestDist = d; best = t; }
                    }
                }
                if (best != null)
                {
                    vanillaPoint = true;
                    return OnTheGround(best.position);
                }
            }

            Transform sp = door.transform.Find("SpawnPoint");
            Vector3 fallbackPos = sp != null ? sp.position : door.transform.position;
            // On the way out the door lives in the clone scene, so it is raised on Y.
            // Subtract YOffset to spawn at the outside world's door level.
            fallbackPos.y -= instance.Config.YOffset;
            // Same reason, for a clone that is parked away from its shell: this position
            // came off an object inside it, and the target is outdoors.
            fallbackPos -= instance.Config.ParkOffset;
            return fallbackPos;
        }

        // A vanilla exit point set down on the ground below it: the Orca store's stand 0.6 and 1.2 m up,
        // and the player dropped on leaving; the whaling warehouse's back door 3 m (2026-10-10). Left as it is past 5.5 m.
        private static Vector3 OnTheGround(Vector3 point)
        {
            RaycastHit hit;
            if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;
            return point;
        }

        // The building whose door the player last left through into a scene SI does not clone.
        private static string s_LeftThroughInstance;
        private static string s_LeftThroughRegionPoint;

        // The region spawn point of the clone's way out nearest a door, so a load into the region lands by the building.
        // Remembered so the way back from there returns into this clone.
        private static void NoteLeftThrough(SeamlessInteriorInstance instance, LoadScene door)
        {
            s_LeftThroughInstance = instance.Config.ResolvedInstanceId;
            s_LeftThroughRegionPoint = NearestRegionExitPoint(instance, door.transform.position);
            if (!string.IsNullOrEmpty(s_LeftThroughRegionPoint)) PlayerPrefs.SetString("SI_RegionPoint_" + s_LeftThroughInstance, s_LeftThroughRegionPoint);
        }

        // A LoadingZone (the concentrator's conveyor crawl) loads its partner door's scene without PerformInteraction.
        internal static void OnLoadingZone(LoadScene door)
        {
            if (door == null || string.IsNullOrEmpty(door.m_SceneToLoad)) return;
            MarkDecided(door);
            var instance = SeamlessInteriorsMod.FindInstanceOwning(door.transform);
            if (instance == null) { TryRedirectIntoClone(door); return; }
            if (!LeadsToARegion(door.m_SceneToLoad) && door.m_SceneToLoad != instance.Config.InteriorSceneBaseName
                && (instance.Config.DoorSpawnPoints == null || instance.Config.DoorSpawnPoints.Count == 0)
                && instance.Config.ExitSpawnPosition == Vector3.zero)
                NoteLeftThrough(instance, door);
        }

        private static string NearestRegionExitPoint(SeamlessInteriorInstance instance, Vector3 near)
        {
            string best = null; float bestD = float.MaxValue;
            foreach (var ls in instance.MasterInterior.GetComponentsInChildren<LoadScene>(true))
            {
                if (ls == null || !LeadsToARegion(ls.m_SceneToLoad) || string.IsNullOrEmpty(ls.m_ExitPointName)) continue;
                float d = Vector3.Distance(ls.transform.position, near);
                if (d < bestD) { bestD = d; best = ls.m_ExitPointName; }
            }
            return best;
        }

        // In a scene of its own (the mine), a door back to a cloned building's interior scene loads the
        // building's region instead; SI then puts the player at that door's enter point inside the clone.
        internal static bool TryRedirectIntoClone(LoadScene door)
        {
            string target = door.m_SceneToLoad;
            if (string.IsNullOrEmpty(target) || LeadsToARegion(target)) return false;

            string here = SeamlessInteriorsMod.RealSceneName(door.gameObject.scene);
            // only from an interior scene: a region (or its _SANDBOX part) keeps its own doors
            if (string.IsNullOrEmpty(here) || LeadsToARegion(here.Split('_')[0])) return false;
            InteriorConfig pick = null;
            int candidates = 0;
            foreach (var cfg in SeamlessInteriorsMod.SupportedInteriors)
            {
                if (cfg.InteriorSceneBaseName != target || IsRegionScene(here, cfg.ExteriorSceneName)) continue;
                candidates++;
                // the building the player came from wins when several share the interior
                if (pick == null || cfg.ResolvedInstanceId == s_LeftThroughInstance) pick = cfg;
            }
            if (pick == null || (candidates > 1 && pick.ResolvedInstanceId != s_LeftThroughInstance)) return false;

            SeamlessInteriorsMod.s_PendingArrivalInstance = pick.ResolvedInstanceId;
            SeamlessInteriorsMod.s_PendingArrivalPoint = door.m_ExitPointName;
            door.m_SceneToLoad = pick.ExteriorSceneName;
            // the interior's point does not exist in the region: the game would drop the player under the map
            // after a load in the mine the door is unknown: the point remembered from the last time through
            string regionPoint = pick.ResolvedInstanceId == s_LeftThroughInstance ? s_LeftThroughRegionPoint : null;
            if (string.IsNullOrEmpty(regionPoint)) regionPoint = PlayerPrefs.GetString("SI_RegionPoint_" + pick.ResolvedInstanceId, "");
            if (!string.IsNullOrEmpty(regionPoint)) door.m_ExitPointName = regionPoint;
            MelonLogger.Msg($"[DONUS] '{here}' -> '{target}' kapisi {pick.ExteriorSceneName} bolgesine yonlendirildi, " +
                            $"varis: {pick.ResolvedInstanceId} / {SeamlessInteriorsMod.s_PendingArrivalPoint}, bolge noktasi {door.m_ExitPointName}");
            return true;
        }

        // A shared interior's exit names whichever region it was built for (TrailerB's says LakeRegion
        // wherever it stands), so any region counts; MiningRegionMine or HouseBasementC do not.
        private static bool LeadsToARegion(string scene)
        {
            return !string.IsNullOrEmpty(scene)
                && (scene.EndsWith("Region", System.StringComparison.Ordinal) || scene.Contains("Zone"));
        }

        // The region scene or one of its sub-scenes (MiningRegion_SANDBOX, AshCanyonRegion_EastRidge...).
        private static bool IsRegionScene(string sceneName, string region)
        {
            return !string.IsNullOrEmpty(sceneName) && !string.IsNullOrEmpty(region)
                && (sceneName == region || sceneName.StartsWith(region + "_", System.StringComparison.Ordinal));
        }

        // The transform with this exact name under root (inactive ones too) nearest to near; null if none.
        private static Transform FindNamedNearest(Transform root, string name, Vector3 near)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;

            Transform best = null;
            float bestDist = float.MaxValue;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.name != name) continue;
                float d = (t.position - near).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best;
        }

        private const float SNAP_LIFT = 0f;

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

            // ONE CENTIMETER ABOVE THE SURFACE, NOT FIVE. It was 5 cm, and the owner could see it:
            // "When exiting, the player visibly jumps downwards." Measured 2026-09-18 with a
            // frame-by-frame probe across two exits from the Camp Office: the teleport lands on the
            // right surface (the shell's own porch), grounded from the first frame, camera a constant
            // 1.750 m above the feet, so no fall and no landing dip; and the feet arrive exactly
            // 0.050 m above the porch and drop it within 91 ms, touching 3 m/s downward for a frame.
            // That drop was the whole of the "jump". The same 5 cm showed on the way in.
            //
            // ZERO, AND THE VALUE WAS FOUND IN THE RUNNING GAME, NOT GUESSED. A first fix guessed 1 cm
            // "so the controller never starts inside the floor" and was staged untested. A console
            // trial then teleported the player onto the floor under them at 5, 1 and 0 cm and
            // watched 75 frames each: 5 cm dropped 5.0, 1 cm dropped 1.0 at 0.6 m/s, 0 cm dropped
            // 0.0, never read below the surface and never left the ground. The margin guards
            // against nothing, so there is none.
            if (bestHit.HasValue)
                return new Vector3(pos.x, bestHit.Value.point.y + SNAP_LIFT, pos.z);

            return pos;
        }

        // ─── THROUGH BLACK AT THE DOOR ───
        //
        // Owner, 2026-09-30, after more pop-in at the doors: "Instead of chasing this more and more
        // like we've already done, I think we should just add a short fade in and out to black."
        // Settled live on a console prototype the same day: a quarter of a second to black, a
        // quarter held, a quarter back. Only where this mod moves the player itself: a door the
        // game handles (an unmodded interior) keeps the game's own fade. Meant to go again once
        // the doors show the other side (door portals).
        //
        // The first use of the door starts the fade and stops there; the fade uses the door again
        // with s_DoorFadeThrough set, which does the move exactly as a door with no fade does.
        // Clicks while a fade runs do nothing. The screen always comes back, even after an error.
        private const float DOOR_FADE_OUT_SECONDS = 0.25f, DOOR_FADE_HOLD_SECONDS = 0.25f, DOOR_FADE_IN_SECONDS = 0.25f;
        private static bool s_DoorFadeThrough, s_DoorFadeBusy;

        private static bool DeferDoorForFade(LoadScene door)
        {
            if (s_DoorFadeThrough || !SeamlessInteriorsMod.IsDoorFadeEnabled) return false;
            if (s_DoorFadeBusy) return true;
            s_DoorFadeBusy = true;
            MelonCoroutines.Start(DoorThroughBlack(door));
            return true;
        }

        private static System.Collections.IEnumerator DoorThroughBlack(LoadScene door)
        {
            bool dark = true;
            try { CameraFade.FadeOut(DOOR_FADE_OUT_SECONDS, 0f, null); }
            catch (System.Exception ex) { dark = false; MelonLogger.Warning($"[DOOR-FADE] The fade to black failed, the door is used without it: {ex.Message}"); }

            float until = Time.realtimeSinceStartup + DOOR_FADE_OUT_SECONDS;
            while (dark && Time.realtimeSinceStartup < until) yield return null;

            s_DoorFadeThrough = true;
            try { if (door != null) door.PerformInteraction(); }
            catch (System.Exception ex) { MelonLogger.Warning($"[DOOR-FADE] The door failed behind the fade: {ex.Message}"); }
            finally { s_DoorFadeThrough = false; }

            until = Time.realtimeSinceStartup + DOOR_FADE_HOLD_SECONDS;
            while (dark && Time.realtimeSinceStartup < until) yield return null;

            try { CameraFade.FadeIn(DOOR_FADE_IN_SECONDS, 0f, null); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[DOOR-FADE] The fade back failed, the screen is cleared at once: {ex.Message}");
                try { CameraFade.FadeIn(0f, 0f, null); } catch { }
            }
            s_DoorFadeBusy = false;
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

                    if (DeferDoorForFade(door)) return false;

                    SeamlessInteriorsMod.NotifyPortalUsed();

                    SeamlessInteriorsMod.PlayDoorTransitionSound();

                    // Same point of no return as the ordinary entry door: the child scene
                    // (a basement, an upper floor) has to be filled before it is shown.
                    SeamlessInteriorsMod.EnsureHydratedNow(targetInstance, "alt-mekan gecisi");

                    // Activate the child and force all of its renderers on.
                    if (targetInstance.MasterInterior != null)
                    {
                        targetInstance.MasterInterior.SetActive(true);
                        SeamlessInteriorsMod.ShowCloneRenderers(targetInstance);

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
                    Vector3 spawnPos = SnapToGround(SeamlessInteriorsMod.ParkedInteriorPoint(targetInstance, link.ChildSpawnPosition));
                    GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);

                    SeamlessInteriorsMod.ApplyParticleKillers(instance, false);
                    SeamlessInteriorsMod.ApplyParticleKillers(targetInstance, true);

                    SeamlessInteriorsMod.SetAudioOcclusion(true);
                    SeamlessInteriorsMod.MarkPlayerInside(targetInstance, "alt-mekan gecisi");

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

                    if (DeferDoorForFade(door)) return false;

                    SeamlessInteriorsMod.NotifyPortalUsed();

                    SeamlessInteriorsMod.PlayDoorTransitionSound();

                    // Coming back up: the parent may have been closed long enough that it
                    // was never filled (or was torn down and rebuilt), so make sure of it
                    // before it is shown again.
                    SeamlessInteriorsMod.EnsureHydratedNow(parentInstance, "alt-mekandan geri donus");

                    // Activate the parent and force all of its renderers on.
                    if (parentInstance.MasterInterior != null)
                    {
                        parentInstance.MasterInterior.SetActive(true);
                        SeamlessInteriorsMod.ShowCloneRenderers(parentInstance);

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
                    Vector3 spawnPos = SnapToGround(SeamlessInteriorsMod.ParkedInteriorPoint(parentInstance, link.ParentSpawnPosition));
                    GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);

                    SeamlessInteriorsMod.ApplyParticleKillers(childInstance, false);
                    SeamlessInteriorsMod.ApplyParticleKillers(parentInstance, true);

                    SeamlessInteriorsMod.SetAudioOcclusion(true);
                    SeamlessInteriorsMod.MarkPlayerInside(parentInstance, "alt-mekandan geri donus");

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
