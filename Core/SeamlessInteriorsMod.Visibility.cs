using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── Initial visibility sync and watchdog ───

        private void InitializeVisibilityAndWatchdog(SeamlessInteriorInstance instance)
        {
            // Prefer the real player position; fall back to the transform-less path
            // when the player has not been placed in the world yet.
            PlayerManager pmInit = GameManager.GetPlayerManagerComponent();
            if (pmInit != null && pmInit.transform.position.sqrMagnitude > 1f)
            {
                ApplyInitialSyncState(instance, pmInit.transform.position);
            }
            else
            {
                ApplyInitialSyncState(instance);
            }

            MelonCoroutines.Start(DelayedInitialVisibilityCheck(instance));
            MelonCoroutines.Start(DelayedSaveLoadVisibilityFix(instance));

            if (!instance.WatchdogStarted)
            {
                instance.WatchdogStarted = true;
                MelonCoroutines.Start(VisibilityWatchdog(instance));
            }
        }

        // ─── Hiding outside-world objects (a decision shared by all instances) ───
        //
        // PROBLEM: each instance used to enable/disable its own
        // ResolvedExternalHiddenObjects list SEPARATELY. When two clones' lists
        // overlapped, whichever ran last won. Overlap is the rule, not the exception:
        // while adding a renderer to the list, AutoResolveOverlappingExternalObjects
        // also adds the GameObjects of its ANCESTOR colliders, and those ancestors are
        // usually objects shared by the house and the terrain. So the basement's list
        // and the farmhouse's list largely contain the same objects.
        //
        // Walking from the basement up into the farmhouse (sub-interior -> parent) the
        // order was:
        //   1) parent list -> SetActive(false)  (correct)
        //   2) child list  -> SetActive(true)   (re-enables the shared objects)
        // Result: outside-world objects poking into the house stayed visible; leaving
        // and re-entering fixed it because (1) ran again.
        //
        // FIX: the decision is made per object and INDEPENDENTLY OF ORDER: an object
        // stays hidden if it is in the hide list of ANY clone scene that is currently
        // active, and is shown if it is in none of them. Every transition calls this
        // one function.
        private static readonly System.Collections.Generic.HashSet<int> s_ExternalHideSet =
            new System.Collections.Generic.HashSet<int>();

        public static void SyncExternalHiddenObjects()
        {
            // 1) Union of the hide lists of the clones that are currently active.
            s_ExternalHideSet.Clear();
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst == null || inst.MasterInterior == null) continue;
                if (!inst.MasterInterior.activeSelf) continue;
                if (inst.ResolvedExternalHiddenObjects == null) continue;

                foreach (var obj in inst.ResolvedExternalHiddenObjects)
                    if (obj != null) s_ExternalHideSet.Add(obj.GetInstanceID());
            }

            // 2) Apply that union to every list.
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst == null || inst.ResolvedExternalHiddenObjects == null) continue;

                foreach (var obj in inst.ResolvedExternalHiddenObjects)
                {
                    if (obj == null) continue;
                    bool shouldHide = s_ExternalHideSet.Contains(obj.GetInstanceID());
                    if (obj.activeSelf != shouldHide) continue; // already in the right state
                    obj.SetActive(!shouldHide);
                }
            }
        }

        // Full visibility setup used right after loading: decides inside/outside and
        // puts the interior, the shell, the particle killers and the audio flags into
        // a consistent state.
        public static void ApplyInitialSyncState(SeamlessInteriorInstance instance, Vector3? overridePos = null)
        {
            Vector3 pos;
            if (overridePos.HasValue) pos = overridePos.Value;
            else
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) return;
                pos = playerT.position;
            }

            if (instance.MasterInterior == null) return;
            // ExteriorShell may be null (sub-interiors can share a shell or have none).
            //
            // ResolvePlayerInside: after a load the saved state outranks geometry.
            // When the player saved just OUTSIDE a door, the bounds fallback in
            // IsPositionInside gave a false positive and the interior started visible.
            bool isInside = ResolvePlayerInside(instance, pos);

            // Particle killers are only registered while the player is inside.
            var uniStorm = GetCachedUniStorm();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null && instance.CustomKillers != null)
            {
                foreach (var pk in instance.CustomKillers)
                {
                    uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(pk);
                    if (isInside) uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Add(pk);
                }
            }

            if (isInside)
            {
                instance.MasterInterior.SetActive(true);
                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(false);
                SetInteriorItemsVisible(instance, true);

                SyncExternalHiddenObjects();

                // Player is inside after the load: set the global flags.
                s_IsPlayerInsideClone = true;
                SetAudioOcclusion(true);
            }
            else
            {
                // Hiding items and adopting stray objects must happen BEFORE
                // MasterInterior goes inactive; HideInteriorCompletely guarantees that order.
                HideInteriorCompletely(instance);
                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(true);

                SyncExternalHiddenObjects();
            }

            // Audio occlusion is no longer handled here - only on door transitions (PortalPatches).
        }

        // Lightweight periodic update: only keeps the particle killers in sync with
        // whether the player is inside.
        public static void ApplyVisibilityState(SeamlessInteriorInstance instance, Vector3? overridePos = null)
        {
            Vector3 pos;
            if (overridePos.HasValue) pos = overridePos.Value;
            else
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) return;
                pos = playerT.position;
            }

            if (instance.MasterInterior == null) return;
            // ExteriorShell may be null (sub-interiors).

            bool isInside = instance.IsPositionInside(pos);
            var uniStorm = GetCachedUniStorm();

            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null && instance.CustomKillers != null && instance.CustomKillers.Count > 0)
            {
                // Only touch the manager's list when the state actually changed.
                // The first killer stands in for the whole set - they are always
                // added and removed together.
                bool isCurrentlyApplied = uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Contains(instance.CustomKillers[0]);

                if (isInside && !isCurrentlyApplied)
                {
                    foreach (var pk in instance.CustomKillers)
                        uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Add(pk);
                }
                else if (!isInside && isCurrentlyApplied)
                {
                    foreach (var pk in instance.CustomKillers)
                        uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(pk);
                }
            }
        }

        // Late re-check: other systems may still move the player around for several
        // seconds after a load.
        private IEnumerator DelayedInitialVisibilityCheck(SeamlessInteriorInstance instance)
        {
            yield return new WaitForSeconds(10f);
            if (!instance.RunCompleted) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT != null) ApplyInitialSyncState(instance, playerT.position);
        }

        // Optimization: distance based frequency - distant buildings are checked less often.
        private const float WATCHDOG_NEAR_DISTANCE = 80f;   // frequent checks within this range
        private const float WATCHDOG_FAR_DISTANCE = 200f;   // very rare checks beyond this
        private const float WATCHDOG_NEAR_INTERVAL = 0.5f;  // near: 0.5s (was 0.3s)
        private const float WATCHDOG_FAR_INTERVAL = 3.0f;   // far: 3s
        private const float WATCHDOG_SKIP_INTERVAL = 8.0f;  // very far: 8s

        // SAFETY NET: periodically collects and hides objects that reappear in the
        // clone scene while the player is outside (loot dropped by a broken object,
        // gear respawned after a save/load...). FindObjectsOfType is expensive, so
        // this only runs when nearby, and rarely.
        private const float STRAY_SWEEP_INTERVAL = 5.0f;

        // How many CONSECUTIVE checks must say "player is outside" before an open
        // clone scene is force-closed. Several confirmations are required so a single
        // missed raycast never throws the player out of the building.
        private const int FORCE_HIDE_CONFIRMATIONS = 6;

        // The interactivity repair interval widens gradually while repairs find nothing.
        //
        // WHY: the repair scan builds a collider list for EVERY item in the clone scene
        // (hundreds of IL2CPP calls). With the player sitting indoors that meant a few
        // hundred calls per second even when nothing was broken. A scan that repairs
        // nothing delays the next one; the moment something is repaired the interval
        // snaps back to the shortest value, so the safety net is not weakened.
        private const float INTERACTIVITY_REPAIR_MAX_INTERVAL = 10f;

        // Per-instance loop that keeps visibility, interactivity and stray objects in
        // order for as long as the instance is alive.
        private IEnumerator VisibilityWatchdog(SeamlessInteriorInstance instance)
        {
            float lastStraySweep = 0f;
            float lastInteractivityRepair = 0f;
            float interactivityRepairInterval = INTERACTIVITY_REPAIR_INTERVAL;
            int outsideConfirmations = 0;

            while (instance.RunCompleted)
            {
                // Right after a door transition the portal code owns the state; stay out of its way.
                bool suppressed = Time.time - s_LastPortalUseTime <= PORTAL_SUPPRESS_WINDOW;
                if (!suppressed)
                {
                    Transform playerT = GameManager.GetPlayerTransform();
                    float interval = WATCHDOG_NEAR_INTERVAL;

                    if (playerT != null)
                    {
                        float dist = Vector3.Distance(playerT.position, instance.Config.FallbackPosition);

                        if (dist > WATCHDOG_FAR_DISTANCE)
                        {
                            // Very far: do not even raycast, just make sure it is closed.
                            interval = WATCHDOG_SKIP_INTERVAL;
                            if (instance.MasterInterior != null && instance.MasterInterior.activeSelf)
                            {
                                HideInteriorCompletely(instance);
                                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(true);
                                SyncExternalHiddenObjects();
                            }
                        }
                        else if (dist > WATCHDOG_NEAR_DISTANCE)
                        {
                            interval = WATCHDOG_FAR_INTERVAL;
                            ApplyVisibilityState(instance);
                        }
                        else
                        {
                            ApplyVisibilityState(instance);

                            if (instance.MasterInterior != null && instance.MasterInterior.activeSelf)
                            {
                                // SAFETY NET: while the clone scene is open, periodically
                                // re-enable item colliders that were left disabled. Item
                                // renderers are enabled unconditionally in three places; if
                                // the collider stays off the item is visible but
                                // non-interactive - it can be neither picked up nor moved
                                // in placement (Y) mode.
                                // (see SeamlessInteriorsMod.Interactivity.cs)
                                if (Time.time - lastInteractivityRepair >= interactivityRepairInterval)
                                {
                                    lastInteractivityRepair = Time.time;
                                    int fixedColliders = RestoreInteriorItemColliders(instance, true);
                                    if (fixedColliders > 0)
                                    {
                                        // Something did break: go back to the shortest interval.
                                        interactivityRepairInterval = INTERACTIVITY_REPAIR_INTERVAL;
                                        MelonLogger.Msg($"[ETKILESIM-ONARIM] {instance.Config.ResolvedInstanceId}: " +
                                                        $"{fixedColliders} kapali esya collider'i geri acildi.");
                                    }
                                    else
                                    {
                                        // Quiet: widen the interval step by step.
                                        interactivityRepairInterval = Mathf.Min(
                                            interactivityRepairInterval * 1.5f,
                                            INTERACTIVITY_REPAIR_MAX_INTERVAL);
                                    }
                                }

                                // Clone scene open but the player looks outside by both the
                                // raycast and the volume test: a door transition was left
                                // half-finished. The volume test is required as well, and
                                // several consecutive confirmations are awaited, so a single
                                // missed raycast never ejects the player from the building.
                                bool looksOutside = !instance.IsPositionInside(playerT.position)
                                                    && !instance.IsPositionInVolume(playerT.position, 2.0f);

                                outsideConfirmations = looksOutside ? outsideConfirmations + 1 : 0;

                                if (outsideConfirmations >= FORCE_HIDE_CONFIRMATIONS)
                                {
                                    outsideConfirmations = 0;
                                    MelonLogger.Msg($"[WATCHDOG] {instance.Config.ResolvedInstanceId}: oyuncu disarida ama klon sahne acik kalmis, kapatiliyor.");

                                    HideInteriorCompletely(instance);
                                    if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(true);
                                    SyncExternalHiddenObjects();
                                }
                            }
                            else if (instance.MasterInterior != null
                                     && Time.time - lastStraySweep >= STRAY_SWEEP_INTERVAL)
                            {
                                // Clone scene closed: sweep up stray objects that reappeared
                                // inside. Once reparented under MasterInterior they become
                                // invisible automatically, because that hierarchy is inactive.
                                outsideConfirmations = 0;
                                lastStraySweep = Time.time;

                                int adopted = AdoptStrayInteriorObjects(instance);
                                if (adopted > 0)
                                    MelonLogger.Msg($"[STRAY-SWEEP] {instance.Config.ResolvedInstanceId}: {adopted} sahipsiz obje klon sahneye alindi ve gizlendi.");
                            }
                        }
                    }

                    yield return new WaitForSeconds(interval);
                }
                else
                {
                    // A door transition just happened: visibility was changed in bulk, so
                    // this is the moment a collider is most likely to be left disabled.
                    // Reset the repair interval to its shortest value.
                    interactivityRepairInterval = INTERACTIVITY_REPAIR_INTERVAL;
                    lastInteractivityRepair = 0f;

                    yield return new WaitForSeconds(WATCHDOG_NEAR_INTERVAL);
                }
            }
        }

        // ─── Post save/load visibility correction ───

        public static IEnumerator DelayedSaveLoadVisibilityFix(SeamlessInteriorInstance instance)
        {
            yield return new WaitForSeconds(0.1f);
            if (!instance.RunCompleted) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) yield break;

            bool isInside = ResolvePlayerInside(instance, playerT.position);

            if (s_DebugBounds)
                MelonLogger.Msg($"[SAVE-LOAD-FIX] {instance.Config.InteriorSceneBaseName} isInside={isInside} | pos={playerT.position}");

            // Shell and interior must be exact opposites of each other.
            bool shellActiveWrong = instance.ExteriorShell != null && instance.ExteriorShell.activeSelf == isInside;
            bool interiorActiveWrong = instance.MasterInterior != null && instance.MasterInterior.activeSelf != isInside;

            if (shellActiveWrong || interiorActiveWrong)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[SAVE-LOAD-FIX] {instance.Config.InteriorSceneBaseName} Yanlis gorunum tespit edildi, duzeltiliyor.");
                ApplyInitialSyncState(instance, playerT.position);
            }

            if (isInside && instance.MasterInterior != null)
            {
                // Restore from the saved local-space position (the most reliable source).
                //
                // CRITICAL: only used when the save belongs to THIS instance. Otherwise,
                // even for a player who saved outside (the geometric isInside test can
                // give a false positive in a doorway), they would be teleported to the
                // point stored for a previous clone scene.
                string instanceId = instance.Config.ResolvedInstanceId;
                Vector3? savedLocalPos = GetSavedPlayerLocalPosition(instanceId);
                Quaternion? savedLocalRot = GetSavedPlayerLocalRotation(instanceId);

                if (savedLocalPos.HasValue)
                {
                    Vector3 restoredWorldPos = instance.MasterInterior.transform.TransformPoint(savedLocalPos.Value);
                    Quaternion restoredWorldRot = savedLocalRot.HasValue
                        ? instance.MasterInterior.transform.rotation * savedLocalRot.Value
                        : playerT.rotation;

                    float heightDiff = restoredWorldPos.y - playerT.position.y;
                    GameManager.GetPlayerManagerComponent().TeleportPlayer(restoredWorldPos, restoredWorldRot);

                    if (s_DebugBounds)
                        MelonLogger.Msg($"[SAVE-LOAD-FIX] Kaydedilmis local pozisyon ile duzeltildi: {restoredWorldPos} (yukari: {heightDiff:F2}m)");
                }
                else
                {
                    // Fallback: find the floor with a raycast.
                    Vector3 correctedPos = EnsureAboveGround(playerT.position, instance);
                    float heightDiff = correctedPos.y - playerT.position.y;

                    if (heightDiff > 0.1f)
                    {
                        playerT.position = correctedPos;
                        if (s_DebugBounds)
                            MelonLogger.Msg($"[SAVE-LOAD-FIX] Oyuncu Y duzeltildi (raycast fallback): {playerT.position} (yukari: {heightDiff:F2}m)");
                    }
                }
            }
        }
    }
}
