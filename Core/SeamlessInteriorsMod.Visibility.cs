using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
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
            // 0) DROP ANYTHING THAT HAS SINCE BECOME PART OF A CLONE.
            //
            // The hide list is built in Run(), and at that moment the game's own scene
            // restore has already spawned the player's decorations - standing inside the
            // building, but not yet parented to the clone, because the mod fills the
            // interior later. They look exactly like outdoor scenery poking through a
            // wall, so they end up on the hide list and get switched off.
            //
            // Hydration then adopts them into the clone, but the stale list entry stays,
            // and every later sync switches them off again. Measured: 172 of the player's
            // decorations, switched off and re-enabled in a loop.
            //
            // Once an object is under a clone it is interior content and this system has
            // no business touching it, so the entry is removed for good.
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst == null || inst.ResolvedExternalHiddenObjects == null) continue;

                var list = inst.ResolvedExternalHiddenObjects;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    GameObject obj = list[i];
                    if (obj == null) { list.RemoveAt(i); continue; }
                    if (IsUnderAnyMasterInterior(obj.transform)) list.RemoveAt(i);
                }
            }

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
                // The player is about to be looking at the inside of this building, so
                // its contents cannot wait for the prefetch queue. Normally a no-op:
                // entering through a door already forced it (see PortalPatches).
                EnsureHydratedNow(instance, "gorunurluk senkronu: oyuncu iceride");

                instance.MasterInterior.SetActive(true);
                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(false);
                SetInteriorItemsVisible(instance, true);

                SyncExternalHiddenObjects();

                // Player is inside after the load: set the global flags.
                MarkPlayerInside(instance, "gorunurluk senkronu");
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

        // ─────────────────────────────────────────────────────────────────────
        // THE TEN-SECOND CHECK: ONE TIMER FOR THE REGION, NOT ONE PER BUILDING
        //
        // Ten seconds after a load, every building is verified once: the game's own
        // restore has finished by then and may have switched the player's decorations off,
        // and GearManager.Deserialize has put the game's own copies of interior loot back
        // into the region.
        //
        // EACH BUILDING USED TO RUN ITS OWN, with its own whole-scene searches. Measured on
        // a 15-building Mystery Lake save that came to 12 gear searches and 12 placeable
        // searches costing 1031 ms, landing about ten seconds after the load - exactly when
        // the player has just got control back.
        //
        // Batching them as they arrive was tried and does not work: the clones finish at
        // different points of a 26-second loading screen, so per-building timers come due
        // across a 15-second spread and each building forms a batch of one. The count
        // stayed at 14 pairs of searches.
        //
        // What the check waits for is not a building, it is the region - "the restore is
        // definitely finished" is one moment, not fifteen. So it waits for the region to
        // settle, then the ten seconds, then walks every building inside a single scan
        // scope: one pair of searches instead of fourteen.
        //
        // A clone that finishes after the batch has run schedules a fresh one, because the
        // flag is clear again by then.
        // ─────────────────────────────────────────────────────────────────────
        private static bool s_InitialCheckScheduled = false;

        // A region change bumps this, the same way WatchdogGeneration retires a watchdog.
        // Without it, clearing the flag on a region change would let the next region start
        // a second coroutine while the first is still waiting, and both would run.
        private static int s_InitialCheckGeneration = 0;

        // Safety cap on waiting for the region to settle: a load interrupted halfway must
        // not leave this coroutine waiting for the rest of the session.
        private const float INITIAL_CHECK_SETTLE_TIMEOUT = 120f;

        private IEnumerator DelayedInitialVisibilityCheck(SeamlessInteriorInstance instance)
        {
            // The caller is per building; the check is per region.
            if (s_InitialCheckScheduled) yield break;
            s_InitialCheckScheduled = true;

            int myGeneration = s_InitialCheckGeneration;

            float guard = Time.realtimeSinceStartup + INITIAL_CHECK_SETTLE_TIMEOUT;
            while (Time.realtimeSinceStartup < guard && (s_ScreenHeldBlack || IsAnyCloningActive()))
                yield return new WaitForSeconds(0.5f);

            yield return new WaitForSeconds(10f);

            // The region this was scheduled for is gone; whoever owns the new one has
            // scheduled its own.
            if (myGeneration != s_InitialCheckGeneration) yield break;

            s_InitialCheckScheduled = false;

            // SHARE THE SEARCHES, SPREAD THE WORK.
            //
            // Collecting the buildings into one pass got the whole-scene searches down from
            // fourteen pairs to one - and put all fifteen buildings' work in a single
            // frame, measured at 2735 ms. One 2.7-second freeze is worse for the player
            // than the fifteen ~180 ms hitches it replaced, even though it is less work in
            // total.
            //
            // So the scan scope stays open across frames, which is what the load window
            // already does with the same snapshot, while the per-building work yields on a
            // frame budget. One pair of searches, and no frame carries more than its share.
            // Its own list, not the stray sweep's: this loop spans frames and the sweep
            // ticks in between, so sharing one buffer would have them overwrite each other.
            s_InitialCheckTargets.Clear();
            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst == null || !inst.RunCompleted) continue;
                if (inst.MasterInterior == null || inst.InteriorPersisted) continue;
                s_InitialCheckTargets.Add(inst);
            }

            SceneScan.Begin();
            try
            {
                for (int i = 0; i < s_InitialCheckTargets.Count; i++)
                {
                    SeamlessInteriorInstance inst = s_InitialCheckTargets[i];

                    // The list was taken before the first yield; a region change or a
                    // rebuild since then leaves entries that no longer describe anything.
                    if (inst == null || !inst.RunCompleted || inst.MasterInterior == null) continue;
                    if (myGeneration != s_InitialCheckGeneration) break;

                    long perf = PerfProbe.Begin();

                    // The last point at which the game's restore can have switched the
                    // player's decorations off.
                    RepairSpawnedPlaceables(inst);

                    // The game's OWN copies of this building's loot. Standing inside, the
                    // player would be looking at doubled loot; stepping outside, at loot
                    // hanging in mid-air. Either way the copies go now rather than waiting
                    // for the stray sweep, which only runs while the building is closed.
                    // (see SeamlessInteriorsMod.GearLeak.cs)
                    PurgeLeakedCloneGearDuplicates(inst);

                    Transform playerT = GameManager.GetPlayerTransform();
                    if (playerT != null) ApplyInitialSyncState(inst, playerT.position);

                    PerfProbe.End(PerfProbe.Section.InitialSync, perf);

                    if (FrameBudget.Exceeded()) yield return null;
                }
            }
            finally
            {
                SceneScan.End();
                s_InitialCheckTargets.Clear();
            }
        }

        private static readonly List<SeamlessInteriorInstance> s_InitialCheckTargets =
            new List<SeamlessInteriorInstance>();

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
        // Longest a closed building goes without a stray sweep while the world's item counts stay the same.
        private const float STRAY_SWEEP_FALLBACK_INTERVAL = 120f;

        // Quiet collider repairs in a row before the watchdog stops repairing until the next door use.
        private const int INTERACTIVITY_REPAIR_QUIET_LIMIT = 3;

        private IEnumerator VisibilityWatchdog(SeamlessInteriorInstance instance)
        {
            float lastInteractivityRepair = 0f;
            float interactivityRepairInterval = INTERACTIVITY_REPAIR_INTERVAL;
            int outsideConfirmations = 0;

            int quietRepairs = 0;
            var repairResult = new int[1];

            // The loop belongs to THIS incarnation of the instance. A region change bumps
            // the generation, which is what makes this coroutine end - RunCompleted alone
            // does not, because the persist flow keeps it true on purpose.
            // (see SeamlessInteriorInstance.WatchdogGeneration)
            int myGeneration = instance.WatchdogGeneration;

            while (instance.RunCompleted && instance.WatchdogGeneration == myGeneration)
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

                        // CONTENT PREFETCH: this loop is the only place that already
                        // knows how far the player is from every building, so the lazy
                        // content restore rides along on it for free - one float compare
                        // per tick. Starting at HYDRATE_DISTANCE gives the queue plenty
                        // of walking time to finish before the player reaches the door.
                        // (see SeamlessInteriorsMod.Hydration.cs)
                        MaybeRequestHydrationByDistance(instance, dist);

                        if (dist > WATCHDOG_FAR_DISTANCE)
                        {
                            // Very far: do not even raycast, just make sure it is closed.
                            interval = WATCHDOG_SKIP_INTERVAL;
                            if (instance.MasterInterior != null && instance.MasterInterior.activeSelf)
                            {
                                // Takes the player's inside state along only when the player is
                                // recorded in THIS building (see ExitInteriorState).
                                ExitInteriorState(instance, "watchdog: bina cok uzakta", false);
                            }
                        }
                        else if (dist > WATCHDOG_NEAR_DISTANCE)
                        {
                            interval = WATCHDOG_FAR_INTERVAL;
                            long perf = PerfProbe.Begin();
                            ApplyVisibilityState(instance);
                            PerfProbe.End(PerfProbe.Section.WatchdogVisibility, perf);
                        }
                        else
                        {
                            long perf = PerfProbe.Begin();
                            ApplyVisibilityState(instance);
                            PerfProbe.End(PerfProbe.Section.WatchdogVisibility, perf);

                            if (instance.MasterInterior != null && instance.MasterInterior.activeSelf)
                            {
                                // SAFETY NET: while the clone scene is open, periodically
                                // re-enable item colliders that were left disabled. Item
                                // renderers are enabled unconditionally in three places; if
                                // the collider stays off the item is visible but
                                // non-interactive - it can be neither picked up nor moved
                                // in placement (Y) mode.
                                // (see SeamlessInteriorsMod.Interactivity.cs)
                                if (quietRepairs < INTERACTIVITY_REPAIR_QUIET_LIMIT
                                    && Time.time - lastInteractivityRepair >= interactivityRepairInterval)
                                {
                                    lastInteractivityRepair = Time.time;

                                    // The game's own scene restore finishes AFTER the mod
                                    // has rebuilt the interior and can switch the player's
                                    // decorations off behind its back. This is the pass that
                                    // catches it, whenever it happens.
                                    perf = PerfProbe.Begin();
                                    RepairSpawnedPlaceables(instance);
                                    PerfProbe.End(PerfProbe.Section.WatchdogRepair, perf);

                                    // Reads the collider debt directly; nothing left to slice.
                                    var repair = RestoreInteriorItemCollidersSliced(instance, repairResult);
                                    while (repair.MoveNext()) yield return repair.Current;

                                    int fixedColliders = repairResult[0];
                                    if (fixedColliders > 0)
                                    {
                                        // Something did break: go back to the shortest interval.
                                        interactivityRepairInterval = INTERACTIVITY_REPAIR_INTERVAL;
                                        quietRepairs = 0;
                                        MelonLogger.Msg($"[ETKILESIM-ONARIM] {instance.Config.ResolvedInstanceId}: " +
                                                        $"{fixedColliders} kapali esya collider'i geri acildi.");
                                    }
                                    else
                                    {
                                        // Quiet: widen the interval step by step.
                                        quietRepairs++;
                                        interactivityRepairInterval = Mathf.Min(
                                            interactivityRepairInterval * 1.5f,
                                            INTERACTIVITY_REPAIR_MAX_INTERVAL);
                                    }

                                    // The repair spanned frames; the test below needs a fresh player.
                                    playerT = GameManager.GetPlayerTransform();
                                }

                                // Clone scene open but the player looks outside by both the
                                // raycast and the volume test: a door transition was left
                                // half-finished. The volume test is required as well, and
                                // several consecutive confirmations are awaited, so a single
                                // missed raycast never ejects the player from the building.
                                if (playerT != null && instance.MasterInterior != null && instance.MasterInterior.activeSelf)
                                {
                                    bool looksOutside = !instance.IsPositionInside(playerT.position)
                                                        && !instance.IsPositionInVolume(playerT.position, 2.0f);

                                    outsideConfirmations = looksOutside ? outsideConfirmations + 1 : 0;

                                    if (outsideConfirmations >= FORCE_HIDE_CONFIRMATIONS)
                                    {
                                        outsideConfirmations = 0;
                                        MelonLogger.Msg($"[WATCHDOG] {instance.Config.ResolvedInstanceId}: oyuncu disarida ama klon sahne acik kalmis, kapatiliyor.");

                                        ExitInteriorState(instance, "watchdog: oyuncu disarida", false);
                                    }
                                }
                            }
                            else
                            {
                                // Clone scene closed. The stray sweep that used to live here
                                // now runs once for the whole region instead of once per
                                // building - see TickStraySweep.
                                outsideConfirmations = 0;
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
                    quietRepairs = 0;

                    yield return new WaitForSeconds(WATCHDOG_NEAR_INTERVAL);
                }
            }
        }

        // THE SLICING IS GONE BECAUSE THE WORK IS GONE.
        //
        // Spreading this over frames was the right answer while the pass had to walk
        // thousands of items to find the ones it owed; it kept a 130 ms stall from landing
        // in one frame. Reading the debt directly costs a null check and an enabled read
        // per entry - on the measured save about a thousand once the dead entries have
        // been dropped - which is a fraction of a frame, so it runs in one go. The
        // coroutine shape is kept only because the watchdog drives it as one.
        //
        // result[0] receives the number repaired.
        private static IEnumerator RestoreInteriorItemCollidersSliced(SeamlessInteriorInstance instance, int[] result)
        {
            long perf = PerfProbe.Begin();
            result[0] = RestoreInteriorItemColliders(instance, true);
            PerfProbe.End(PerfProbe.Section.WatchdogRepair, perf);
            yield break;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ONE STRAY SWEEP FOR THE WHOLE REGION
        //
        // Objects the game puts back into the world where a building stands have to be
        // pulled under that building's clone, or they hang in the air once the player is
        // outside. Finding them means asking the scene for every GearItem and every
        // Placeable it has.
        //
        // THAT QUESTION USED TO BE ASKED ONCE PER BUILDING. Each closed building near the
        // player ran its own sweep on its own timer, and each sweep opened its own scan
        // scope - so in a settlement, five buildings meant five FindObjectsOfType pairs.
        // Measured on a full Mystery Lake save: one FindObjectsOfType<GearItem> costs
        // 43-176 ms and <Placeable> up to 132 ms, and the sweep section peaked at 362 ms
        // inside a single frame.
        //
        // The answer is the same for every building, so it is now asked once: one scan
        // scope, every eligible building processed inside it. Five pairs of scans become
        // one. The per-building work left over is an AABB compare per object, which is a
        // float comparison and does not register.
        //
        // The scope survives the loop because adoption only REPARENTS. The one step that
        // destroys, PurgeLeakedCloneGearDuplicates, invalidates the snapshot itself and
        // only when it actually destroyed something, so the next building reads fresh
        // references rather than dead ones.
        // ─────────────────────────────────────────────────────────────────────
        private static float s_NextStraySweep = 0f;
        private static float s_LastFullStraySweep = 0f;
        private static float s_StraySweepInterval = STRAY_SWEEP_INTERVAL;
        private static int s_QuietStraySweeps = 0;
        private static int s_SweptGearCount = -1;
        private static int s_SweptPlaceableCount = -1;
        private static readonly List<SeamlessInteriorInstance> s_SweepTargets = new List<SeamlessInteriorInstance>();

        // Quiet sweeps in a row before the interval starts widening.
        private const int STRAY_SWEEP_QUIET_LIMIT = 3;
        private const float STRAY_SWEEP_MAX_INTERVAL = 30f;

        public static void TickStraySweep(Vector3 playerPos)
        {
            // A door transition owns the visibility state for a moment after it runs, and
            // a load is switching clones on and off behind a black screen.
            if (Time.time - s_LastPortalUseTime <= PORTAL_SUPPRESS_WINDOW) return;
            if (s_ScreenHeldBlack || IsAnyCloningActive()) return;

            if (Time.time < s_NextStraySweep) return;
            s_NextStraySweep = Time.time + s_StraySweepInterval;

            // Strays are new objects; with the world's item counts unchanged there is
            // nothing to scan for. The fallback interval covers the case where something
            // moved without the counts changing.
            if (!HasGamePopulationChanged(s_SweptGearCount, s_SweptPlaceableCount)
                && Time.time - s_LastFullStraySweep < STRAY_SWEEP_FALLBACK_INTERVAL) return;

            // Closed buildings near the player. An OPEN building is the one the player is
            // standing in and owns its own contents; a parked one belongs to another region.
            s_SweepTargets.Clear();
            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || !instance.RunCompleted || instance.InteriorPersisted) continue;
                if (instance.MasterInterior == null || instance.MasterInterior.activeSelf) continue;
                if (Vector3.Distance(playerPos, instance.Config.FallbackPosition) > WATCHDOG_NEAR_DISTANCE) continue;

                s_SweepTargets.Add(instance);
            }

            if (s_SweepTargets.Count == 0) return;

            s_LastFullStraySweep = Time.time;

            long perf = PerfProbe.Begin();
            int adopted = 0, purged = 0;

            SceneScan.Begin();
            try
            {
                for (int i = 0; i < s_SweepTargets.Count; i++)
                {
                    SeamlessInteriorInstance instance = s_SweepTargets[i];
                    if (instance.MasterInterior == null) continue;

                    adopted += AdoptStrayInteriorObjects(instance);

                    // Copies of this building's own loot that the GAME's save restored into
                    // the region. They are what stays visible after a load once the player
                    // steps outside, and adoption only catches part of them.
                    // (see SeamlessInteriorsMod.GearLeak.cs)
                    purged += PurgeLeakedCloneGearDuplicates(instance);
                }
            }
            finally
            {
                SceneScan.End();
            }

            PerfProbe.End(PerfProbe.Section.WatchdogSweep, perf);

            s_SweptGearCount = CountGameGear();
            s_SweptPlaceableCount = CountGamePlaceables();
            s_SweepTargets.Clear();

            // Something was found: go back to the shortest interval, because whatever put
            // it there may still be doing so. Nothing for several sweeps in a row: widen,
            // the same way the collider repair does.
            if (adopted > 0 || purged > 0)
            {
                s_StraySweepInterval = STRAY_SWEEP_INTERVAL;
                s_QuietStraySweeps = 0;

                if (adopted > 0)
                    MelonLogger.Msg($"[STRAY-SWEEP] {adopted} sahipsiz obje klon sahnelere alindi ve gizlendi.");
            }
            else if (++s_QuietStraySweeps >= STRAY_SWEEP_QUIET_LIMIT)
            {
                s_StraySweepInterval = Mathf.Min(s_StraySweepInterval * 1.5f, STRAY_SWEEP_MAX_INTERVAL);
            }
        }

        // A region change invalidates the counts and the backoff: the new region's
        // buildings have never been swept. The ten-second check is released too, so the
        // new region schedules its own instead of inheriting a spent flag.
        public static void ResetStraySweep()
        {
            s_InitialCheckGeneration++;
            s_InitialCheckScheduled = false;
            s_NextStraySweep = 0f;
            s_LastFullStraySweep = 0f;
            s_StraySweepInterval = STRAY_SWEEP_INTERVAL;
            s_QuietStraySweeps = 0;
            s_SweptGearCount = -1;
            s_SweptPlaceableCount = -1;
            s_SweepTargets.Clear();
        }

        private static int CountGameGear()
        {
            try
            {
                var list = Il2Cpp.GearManager.m_Gear;
                return list != null ? list.Count : -1;
            }
            catch { return -1; }
        }

        private static int CountGamePlaceables()
        {
            try
            {
                var placeables = Il2CppTLD.Placement.PlaceableManager.s_Placeables;
                return placeables != null ? placeables.Count : -1;
            }
            catch { return -1; }
        }

        // An unreadable count counts as a change, so the sweep falls back to its old schedule.
        private static bool HasGamePopulationChanged(int gearCount, int placeableCount)
        {
            int gear = CountGameGear();
            int placeables = CountGamePlaceables();
            return gear < 0 || placeables < 0 || gear != gearCount || placeables != placeableCount;
        }

        // ─── Post save/load visibility correction ───

        public static IEnumerator DelayedSaveLoadVisibilityFix(SeamlessInteriorInstance instance)
        {
            yield return new WaitForSeconds(0.1f);
            if (!instance.RunCompleted) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) yield break;

            bool isInside = ResolvePlayerInside(instance, playerT.position);

            // By now the game's own scene restore has run, which is the moment it can
            // have switched the player's decorations off (see RepairSpawnedPlaceables).
            RepairSpawnedPlaceables(instance);

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
