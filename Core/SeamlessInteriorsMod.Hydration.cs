using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────────
        // LAZY CONTENT HYDRATION
        //
        // THE PROBLEM
        // Building a clone is cheap; filling it is not. RestoreInactiveSceneGearItems
        // destroys every loose item in the clone and respawns each one through
        // Addressables plus a full component deserialize, and RestoreContainerData
        // spawns the entire contents of every container. On a long-lived save that is
        // hundreds of items PER BUILDING - and a region has up to 15 buildings, all of
        // which used to be filled during the region load, behind the black screen,
        // whether or not the player ever goes near them.
        //
        // THE FIX
        // Content is restored only when the player actually approaches a building.
        // Until then the clone keeps the raw scene template content, which nobody can
        // see: the clone is SetActive(false) the whole time the player is outside.
        //
        // WHAT MAKES THIS SAFE
        // Exactly one invariant: a building that has not been hydrated must never have
        // its content save files written. Its clone does not hold the player's items
        // yet, so saving would replace a thousand days of hoarding with the scene
        // template. Every Save* function starts with CanPersistContent(instance).
        // Skipping the write is the correct answer anyway - nothing inside a building
        // the player never opened can have changed.
        //
        // WHEN HYDRATION HAPPENS
        //   * immediately, for the building the player saved inside (Run knows this);
        //   * as a prefetch, once the player comes within HYDRATE_DISTANCE - the
        //     visibility watchdog already measures that distance every tick;
        //   * forced and synchronous, the instant a door is used, in case the player
        //     got there faster than the prefetch (a teleport, a load right at the door).
        //
        // Hydration runs ONE BUILDING AT A TIME through a queue, so walking into a
        // settlement can never start five restores in the same frame.
        // ─────────────────────────────────────────────────────────────────────

        // Prefetch radius.
        //
        // Sized from what the restore actually costs, not from what looks tidy: filling a
        // thousand-day base measured ~18 seconds of streaming work. At walking pace 140m
        // is roughly a minute, which leaves the queue time to finish - including the
        // buildings queued ahead of it - before the player reaches the door.
        //
        // The watchdog ticks every 3 seconds at this range, which is plenty.
        public const float HYDRATE_DISTANCE = 140f;

        // Buildings waiting to be filled, oldest request first.
        private static readonly List<SeamlessInteriorInstance> s_HydrationQueue = new List<SeamlessInteriorInstance>();
        private static bool s_HydrationPumpRunning = false;

        // True while the mod is allowed to write this instance's content save files.
        // See the invariant above.
        public static bool CanPersistContent(SeamlessInteriorInstance instance)
        {
            if (instance == null) return false;
            if (!IsLazyContentEnabled) return true;
            return instance.ContentHydrated;
        }

        public static bool NeedsHydration(SeamlessInteriorInstance instance)
        {
            return instance != null
                && instance.RunCompleted
                && instance.MasterInterior != null
                && !instance.ContentHydrated
                && !instance.HydrationInProgress;
        }

        // ─── Deferred request (prefetch) ───
        public static void RequestHydration(SeamlessInteriorInstance instance)
        {
            if (!NeedsHydration(instance)) return;
            if (s_HydrationQueue.Contains(instance)) return;

            s_HydrationQueue.Add(instance);

            if (s_DebugBounds)
                MelonLogger.Msg($"[HYDRATE] {instance.Config.ResolvedInstanceId}: siraya alindi (kuyruk={s_HydrationQueue.Count}).");

            if (!s_HydrationPumpRunning)
            {
                s_HydrationPumpRunning = true;
                MelonCoroutines.Start(HydrationPump());
            }
        }

        private static IEnumerator HydrationPump()
        {
            while (s_HydrationQueue.Count > 0)
            {
                SeamlessInteriorInstance next = s_HydrationQueue[0];
                s_HydrationQueue.RemoveAt(0);

                if (!NeedsHydration(next)) continue;

                IEnumerator routine = HydrateRoutine(next, true);
                next.HydrationRoutine = routine;

                // MoveNext can return false because EnsureHydratedNow took this routine
                // over and finished it; that is exactly the intent, and the loop simply
                // falls through to the next building.
                while (routine.MoveNext()) yield return routine.Current;
            }

            s_HydrationPumpRunning = false;
        }

        // ─── Immediate, blocking hydration ───
        //
        // Used where the player is about to SEE the inside of the building and there is
        // no time left to spread the work: a door interaction, or a load that puts them
        // straight into the clone. Draining the enumerator ignores its yields, so
        // everything finishes before this call returns.
        public static void EnsureHydratedNow(SeamlessInteriorInstance instance, string reason)
        {
            if (instance == null || instance.MasterInterior == null) return;
            if (instance.ContentHydrated) return;

            // ALREADY RUNNING IN THE BACKGROUND.
            //
            // The prefetch started while the player was walking up, and they got to the
            // door before it finished. Returning here would let them into a building that
            // is still half empty, so the running coroutine is TAKEN OVER and drained to
            // the end right now. Its yields are simply consumed as fast as MoveNext can
            // return, which is what makes the rest of the restore synchronous.
            //
            // The background pump keeps its own reference; when it next calls MoveNext the
            // iterator is already finished and it moves on to the next building.
            if (instance.HydrationInProgress)
            {
                IEnumerator running = instance.HydrationRoutine;
                if (running == null) return;   // nothing to drain; do not spin

                if (s_DebugBounds)
                    MelonLogger.Msg($"[HYDRATE] {instance.Config.ResolvedInstanceId}: arka plandaki dolum devralindi ({reason}).");

                while (running.MoveNext()) { }
                return;
            }

            if (!NeedsHydration(instance)) return;

            s_HydrationQueue.Remove(instance);

            if (s_DebugBounds)
                MelonLogger.Msg($"[HYDRATE] {instance.Config.ResolvedInstanceId}: aninda dolduruluyor ({reason}).");

            IEnumerator routine = HydrateRoutine(instance, false);
            instance.HydrationRoutine = routine;
            while (routine.MoveNext()) { }
        }

        // ─── The actual work ───
        //
        // Runs the same restore functions Run() used to call inline, in the same order.
        // The only additions are the one-time legacy import in front of them and the
        // visibility fix-up behind them.
        private static IEnumerator HydrateRoutine(SeamlessInteriorInstance instance, bool spread)
        {
            if (instance == null || instance.MasterInterior == null) yield break;

            instance.HydrationInProgress = true;
            string id = instance.Config.ResolvedInstanceId;
            float startedAt = Time.realtimeSinceStartup;

            // Per-step timing. A single total tells you a building was slow; it never
            // tells you WHICH part was slow, and the parts differ by an order of
            // magnitude between buildings. s_StepStart is reset before each step and
            // read after it.
            float stepStart = startedAt;
            float tImport = 0f, tPlace = 0f, tGear = 0f, tContainers = 0f, tState = 0f, tVisual = 0f;

            // How many placeables are switched off, sampled after each step.
            //
            // "Things are missing from my building" is always reported as one symptom, but
            // half a dozen different steps here can switch an object off and they are not
            // equally suspicious. Watching the count move tells you which step did it,
            // which no amount of reading the final state can.
            int offStart = CountInactivePlaceables(instance);
            int offAfterPlace = offStart, offAfterGear = offStart;
            int offAfterContainers = offStart, offAfterState = offStart;

            // ─── 0. Second chance for an undecided pre-mod probe ───
            //
            // Several buildings can share one interior scene, and the probe runs building
            // by building as each clone is built. A building probed early cannot tell
            // which of the scene's saved copies belongs to it while its siblings are
            // still unprobed, so it deliberately left the question open. By now every
            // sibling has been through Run(), so asking again can resolve it.
            HydrateStep(instance, "0/7 eski kayit yoklamasi");
            if (instance.LegacyImport == LegacyImportState.Unknown)
                ProbeLegacySave(instance, true);

            // ─── 1. One-time import of the player's pre-mod history ───
            //
            // This only writes the mod's own JSON files; the restore calls below then
            // pick them up exactly as they would for a save that always had the mod.
            bool importedNow = false;

            HydrateStep(instance, "1/7 mod oncesi kayit aktarimi");
            if (IsLegacyImportPending(instance))
            {
                IEnumerator legacy = TranscodeLegacySaveRoutine(instance);
                while (legacy.MoveNext())
                {
                    if (spread) yield return legacy.Current;
                }

                if (instance.MasterInterior == null)
                {
                    instance.HydrationInProgress = false; instance.HydrationRoutine = null;
                    yield break;
                }

                importedNow = true;
            }

            tImport = Time.realtimeSinceStartup - stepStart; stepStart = Time.realtimeSinceStartup;

            // ─── 2. Furniture the player put down ───
            //
            // Must come BEFORE RestorePlaceablePositions: these objects do not exist in
            // the clone yet, and RestorePlaceablePositions switches off every placeable
            // it cannot find in its own record.
            //
            // Skipped straight after an import, which already had to create this
            // furniture before it could match up the containers built into it. Repeating
            // it would re-process several hundred objects for nothing.
            HydrateStep(instance, "2/7 oyuncunun yerlestirdigi mobilya");
            if (!importedNow) RestoreSpawnedPlaceables(instance);
            if (spread) yield return null;
            if (instance.MasterInterior == null) { instance.HydrationInProgress = false; instance.HydrationRoutine = null; yield break; }

            // ─── 3. Scene furniture the player moved ───
            HydrateStep(instance, "3/7 tasinmis sahne mobilyasi");
            RestorePlaceablePositions(instance);
            if (spread) yield return null;
            tPlace = Time.realtimeSinceStartup - stepStart; stepStart = Time.realtimeSinceStartup;
            offAfterPlace = CountInactivePlaceables(instance);
            if (instance.MasterInterior == null) { instance.HydrationInProgress = false; instance.HydrationRoutine = null; yield break; }

            // ─── 4. Loose items (the expensive one) ───
            HydrateStep(instance, "4/7 yerdeki esyalar");
            IEnumerator gear = RestoreInactiveSceneGearItemsRoutine(instance, spread);
            while (gear.MoveNext())
            {
                if (spread) yield return gear.Current;
            }
            tGear = Time.realtimeSinceStartup - stepStart; stepStart = Time.realtimeSinceStartup;
            offAfterGear = CountInactivePlaceables(instance);
            if (instance.MasterInterior == null) { instance.HydrationInProgress = false; instance.HydrationRoutine = null; yield break; }

            // ─── 5. Container contents ───
            HydrateStep(instance, "5/7 konteyner icerikleri");
            IEnumerator containers = RestoreContainerDataRoutine(instance, spread);
            while (containers.MoveNext())
            {
                if (spread) yield return containers.Current;
            }
            tContainers = Time.realtimeSinceStartup - stepStart; stepStart = Time.realtimeSinceStartup;
            offAfterContainers = CountInactivePlaceables(instance);
            if (instance.MasterInterior == null) { instance.HydrationInProgress = false; instance.HydrationRoutine = null; yield break; }

            // ─── 6. Broken / harvested / opened objects, and cleared junk ───
            HydrateStep(instance, "6/7 kirik/hasat edilmis objeler ve cop durumu");
            RestoreInteractiveState(instance);
            RestoreJunkState(instance);
            tState = Time.realtimeSinceStartup - stepStart; stepStart = Time.realtimeSinceStartup;
            offAfterState = CountInactivePlaceables(instance);

            instance.ContentHydrated = true;
            instance.HydrationInProgress = false; instance.HydrationRoutine = null;

            // ─── 7. Put the freshly spawned objects into the right visual state ───
            //
            // ONLY WHEN THE BUILDING IS OPEN.
            //
            // This step used to run unconditionally and it was the single most expensive
            // thing the mod did: SetInteriorItemsVisible walks every GearItem and every
            // Placeable in the clone and asks each one for its Renderers and its
            // Colliders. On a thousand-day base that is well over a thousand objects and
            // several thousand IL2CPP component scans - measured at 35 SECONDS on a real
            // save, which is most of where the freeze came from.
            //
            // With the building CLOSED all of it is wasted work. Everything the steps
            // above created is a child of MasterInterior, and MasterInterior is inactive:
            // the whole subtree is already invisible and non-interactive, whatever the
            // individual enabled flags say. The flags are fixed up the moment the player
            // opens the building, by the door and visibility paths that already do this.
            //
            // With the building OPEN the player is standing in it and the pass is
            // genuinely needed, so it still runs.
            if (instance.MasterInterior.activeSelf)
            {
                HydrateStep(instance, "7/7 gorunurluk ve colliderlar");
                SetInteriorItemsVisible(instance, true);
                RestoreInteriorItemColliders(instance);
            }
            tVisual = Time.realtimeSinceStartup - stepStart;

            int offEnd = CountInactivePlaceables(instance);

            float ms = (Time.realtimeSinceStartup - startedAt) * 1000f;
            MelonLogger.Msg($"[HYDRATE] {id}: icerik yuklendi ({(spread ? "yayilmis" : "aninda")}, {ms:F0} ms) | " +
                            $"aktarim={tImport * 1000f:F0} mobilya={tPlace * 1000f:F0} esya={tGear * 1000f:F0} " +
                            $"konteyner={tContainers * 1000f:F0} durum={tState * 1000f:F0} gorunum={tVisual * 1000f:F0} ms");

            MelonLogger.Msg($"[HYDRATE] {id}: kapali placeable sayisi adim adim - " +
                            $"basta={offStart} mobilyadan sonra={offAfterPlace} esyadan sonra={offAfterGear} " +
                            $"konteynerden sonra={offAfterContainers} durumdan sonra={offAfterState} sonda={offEnd}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // WHICH STEP WAS IT IN?
        //
        // Hydration is seven steps and four of them say nothing at all when their file is
        // missing or their work is trivial. A step that takes the process down with it -
        // an IL2CPP call landing on something the game never expected to be asked about -
        // leaves no managed exception and no log line, so the whole event reads as
        // "[HYDRATE] ... aninda dolduruluyor" and then the file simply stops.
        //
        // That happened, and the step had to be worked out from which save files existed.
        // One line per step in front of the work costs nothing and makes the log say it.
        private static void HydrateStep(SeamlessInteriorInstance instance, string step)
        {
            if (!s_DebugBounds) return;
            MelonLogger.Msg($"[HYDRATE-ADIM] {instance.Config.ResolvedInstanceId}: {step}");
        }

        private static int CountInactivePlaceables(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return 0;

            int off = 0;
            foreach (var p in instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true))
            {
                if (p == null || p.gameObject == null) continue;
                if (!p.gameObject.activeSelf) off++;
            }
            return off;
        }

        // ─── Prefetch driver ───
        //
        // Called from the visibility watchdog, which already knows the distance, so this
        // costs one comparison per tick per building.
        public static void MaybeRequestHydrationByDistance(SeamlessInteriorInstance instance, float distance)
        {
            if (!IsLazyContentEnabled) return;
            if (distance > HYDRATE_DISTANCE) return;
            RequestHydration(instance);
        }

        // A region change invalidates every pending request: the clones those requests
        // pointed at are either gone or have been parked by the persist flow.
        public static void ResetHydrationQueue()
        {
            s_HydrationQueue.Clear();
        }
    }
}
