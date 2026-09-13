using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── TIME INSIDE A CLOSED CLONE ───
        //
        // PROBLEM: leaving a building closes the clone with MasterInterior.SetActive(false).
        // Unity then stops calling Update on everything underneath it, and the two things in
        // a building that measure time in Update stop with it:
        //   * CookingPotItem - m_CookingElapsedHours (cooking, melting snow, boiling water)
        //   * Fire           - m_ElapsedOnTODSeconds (how much of the fire's life is spent)
        //
        // So a pot left boiling in the camp office made no progress at all while the player
        // was outside: come back half an hour later and the timer sits exactly where it was.
        //
        // The vanilla game never hits this because leaving a building UNLOADS the interior
        // scene: the pot and the fire are serialised together with the hours played at that
        // moment (CookingPotItemSaveDataProxy.m_HoursPlayedWhenSerialized,
        // FireSaveDataProxy.m_HoursPlayed) and Deserialize catches the missing hours up when
        // the scene comes back. This mod deliberately skips that scene transition, so the
        // catch-up has to happen here instead.
        //
        // WHAT THIS DOES: on every frame in which a clone is closed, the time the game would
        // have given those components is added to their accumulators by hand. The amount
        // comes from TimeOfDay.GetTODSeconds(Time.deltaTime) - the same conversion the
        // components' own Update uses - so accelerated time (sleeping, or passing time in a
        // snow shelter while a stove burns indoors) is followed automatically.
        //
        // Derived values are deliberately NOT computed here: m_PercentCooked, the cooking
        // state and whether the fire goes out are all recomputed by the components
        // themselves from these accumulators on the first Update after the clone opens
        // again. This code only has to make sure the accumulators are not missing any hours.
        //
        // WHY IT CANNOT DOUBLE COUNT: the work is done ONLY while the clone is inactive,
        // which is exactly when Unity is not calling the components' own Update. The brief
        // activate / work / deactivate cycles the save and hydration code performs are seen
        // here as "open" and skipped, so those frames are counted once, by the game.

        // Rebuilding the component lists is cheap but not free, so a closed clone is
        // re-scanned only this often. Nothing new can appear inside a closed clone while the
        // player is outside; this interval is a safety net for the save/load paths that do
        // put objects into a closed clone (DelayedFireRestore).
        private const float CLOSED_INTERIOR_RESCAN_INTERVAL = 60f;

        public static void TickClosedInteriorTime()
        {
            // Cloning toggles MasterInterior on and off repeatedly and the screen is black
            // while it runs - there is no game time passing that anyone can observe.
            if (s_ScreenHeldBlack || IsAnyCloningActive()) return;

            var tod = GameManager.GetTimeOfDayComponent();
            if (tod == null) return;

            // The exact conversion Fire.Update and CookingPotItem.Update use. With the game
            // paused Time.deltaTime is 0, so a paused game adds nothing.
            float todSeconds = tod.GetTODSeconds(Time.deltaTime);
            if (todSeconds <= 0f) return;

            float todHours = todSeconds / 3600f;

            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || !instance.RunCompleted) continue;

                GameObject master = instance.MasterInterior;
                if (master == null) continue;

                // A clone parked for another region is left to the game's own catch-up:
                // changing region writes a save and reloads it, and FireManager.Deserialize
                // then advances these same fires from the save's timestamp. Ticking them
                // here as well would count those hours twice.
                if (instance.InteriorPersisted) continue;

                if (master.activeInHierarchy)
                {
                    // Open: the game is ticking its contents itself.
                    instance.ClosedTimeWasOpen = true;
                    continue;
                }

                bool needsRescan = instance.ClosedTimeWasOpen || instance.ClosedTimeCacheDirty
                                   || Time.time >= instance.ClosedTimeNextRescan;
                instance.ClosedTimeWasOpen = false;

                // This runs every frame and calls into IL2CPP on components whose
                // GameObject is switched off. Without the guard a single unexpected throw
                // would come out of OnUpdate and take the terrain holes and the lighting
                // guard down with it, every frame, for the rest of the session.
                try
                {
                    // Just closed (the player walked out), or first sight of an already
                    // closed clone: collect what has to be kept running.
                    if (needsRescan) RebuildClosedInteriorTimeCache(instance);

                    AdvanceClosedInterior(instance, todSeconds, todHours);
                }
                catch (System.Exception ex)
                {
                    // Drop the lists - whatever is in them is what threw - and hold off
                    // until the next scheduled rescan instead of retrying (and logging)
                    // every single frame.
                    instance.ClosedFires.Clear();
                    instance.ClosedCookingPots.Clear();
                    instance.ClosedTimeCacheDirty = false;
                    instance.ClosedTimeNextRescan = Time.time + CLOSED_INTERIOR_RESCAN_INTERVAL;

                    MelonLogger.Warning($"[KAPALI-ZAMAN] {instance.Config.ResolvedInstanceId} icin zaman ilerletilemedi: {ex.Message}");
                }
            }
        }

        // Marks a clone's fire / cooking-pot list as out of date. Used by the paths that can
        // put a new Fire into a clone while that clone is closed.
        public static void InvalidateClosedInteriorTimeCaches()
        {
            foreach (var instance in ActiveInteriors.Values)
                if (instance != null) instance.ClosedTimeCacheDirty = true;
        }

        private static void RebuildClosedInteriorTimeCache(SeamlessInteriorInstance instance)
        {
            instance.ClosedTimeCacheDirty = false;
            instance.ClosedTimeNextRescan = Time.time + CLOSED_INTERIOR_RESCAN_INTERVAL;

            instance.ClosedFires.Clear();
            instance.ClosedCookingPots.Clear();

            GameObject master = instance.MasterInterior;
            if (master == null) return;

            // Only what is UNDER MasterInterior matters: that is exactly the set Unity stops
            // updating when the clone closes. A fire or a pot that was never adopted into
            // the clone stays active in the world and keeps ticking on its own.
            var fires = master.GetComponentsInChildren<Il2Cpp.Fire>(true);
            foreach (var fire in fires)
                if (fire != null) instance.ClosedFires.Add(fire);

            var pots = master.GetComponentsInChildren<Il2Cpp.CookingPotItem>(true);
            foreach (var pot in pots)
                if (pot != null) instance.ClosedCookingPots.Add(pot);

            if (s_DebugBounds && (instance.ClosedFires.Count > 0 || instance.ClosedCookingPots.Count > 0))
            {
                MelonLogger.Msg($"[KAPALI-ZAMAN] {instance.Config.ResolvedInstanceId}: " +
                                $"{instance.ClosedFires.Count} ates, {instance.ClosedCookingPots.Count} tencere takip ediliyor.");
            }
        }

        private static void AdvanceClosedInterior(SeamlessInteriorInstance instance, float todSeconds, float todHours)
        {
            // FIRES FIRST. A fire that runs out during this frame has to look burnt out to
            // the pot sitting on it, otherwise the pot gets a free frame of cooking.
            var fires = instance.ClosedFires;
            for (int i = fires.Count - 1; i >= 0; i--)
            {
                Il2Cpp.Fire fire = fires[i];
                if (fire == null) { fires.RemoveAt(i); continue; }

                if (!IsFireStillAlive(fire)) continue;

                // Mirrors what Fire.Update accumulates. The weather adjustment
                // (Fire.GetWeatherAdjustedElapsedDuration) is deliberately skipped: it
                // reacts to cold and wind, and a fire inside a building is sheltered from
                // both, so the unmodified duration is the right answer here.
                fire.m_ElapsedOnTODSeconds += todSeconds;
                fire.m_ElapsedOnTODSecondsUnmodified += todSeconds;
                fire.m_BurningTimeTODHours += todHours;
            }

            var pots = instance.ClosedCookingPots;
            for (int i = pots.Count - 1; i >= 0; i--)
            {
                Il2Cpp.CookingPotItem pot = pots[i];
                if (pot == null) { pots.RemoveAt(i); continue; }

                if (!pot.IsCookingSomething()) continue;
                if (pot.m_CookingState == Il2Cpp.CookingPotItem.CookingState.Ruined) continue;

                if (IsPotFireStillAlive(pot))
                {
                    pot.m_CookingElapsedHours += todHours;

                    // The two countdowns the interface reads are normally derived from
                    // m_CookingElapsedHours on the next Update, which makes these two lines
                    // a no-op. They are written anyway so the numbers are already right on
                    // the frame the clone re-opens, before that Update has run.
                    float minutes = todHours * 60f;
                    pot.m_MinutesUntilCooked = Mathf.Max(0f, pot.m_MinutesUntilCooked - minutes);
                    pot.m_MinutesUntilRuined = Mathf.Max(0f, pot.m_MinutesUntilRuined - minutes);
                }
                else
                {
                    // Fire out: the game gives interrupted cooking a grace period before it
                    // is cancelled (CookingPotItem.CheckForFireBurntOut). Letting that run
                    // too is what keeps "the fire died while I was away" from turning into
                    // free, indefinitely paused cooking.
                    pot.m_GracePeriodElapsedHours += todHours;
                }
            }
        }

        // IsBurning() alone is not enough while the clone is closed: it reports the fire's
        // STATE, and the state is only re-evaluated in Fire.Update, which is not running. A
        // fire whose life has been used up therefore still says it is burning until the
        // clone opens again, so the remaining life is checked directly.
        private static bool IsFireStillAlive(Il2Cpp.Fire fire)
        {
            if (fire == null) return false;
            if (!fire.IsBurning()) return false;
            return fire.GetRemainingLifeTimeSeconds() > 0f;
        }

        private static bool IsPotFireStillAlive(Il2Cpp.CookingPotItem pot)
        {
            if (!pot.AttachedFireIsBurning()) return false;
            return IsFireStillAlive(pot.GetFireBeingUsed());
        }
    }
}
