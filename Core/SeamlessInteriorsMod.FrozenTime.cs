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
        //   * KeroseneLampItem, TorchItem, FlareItem - the fuel and burn time of a light left lit
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

                // A clone parked for another region is left to the game's own catch-up for its
                // timers: changing region writes a save and reloads it, and FireManager.Deserialize
                // then advances these same fires from the save's timestamp. Ticking them here as
                // well would count those hours twice. Only the heat keeps running (see below).
                if (instance.InteriorPersisted)
                {
                    try { AdvanceParkedInteriorHeat(instance, todSeconds); }
                    catch (System.Exception ex)
                    {
                        instance.ParkedFires.Clear();
                        instance.ParkedFireSecondsLeft.Clear();
                        MelonLogger.Warning($"[KAPALI-ZAMAN] {instance.Config.ResolvedInstanceId} park halindeyken isitilamadi: {ex.Message}");
                    }
                    continue;
                }

                // Back in this region: the game catches the fires up itself, so the parked
                // budget is dropped.
                if (instance.ParkedHeatCacheBuilt) ClearParkedInteriorHeat(instance);

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
                    instance.ClosedLamps.Clear();
                    instance.ClosedTorches.Clear();
                    instance.ClosedFlares.Clear();
                    instance.ClosedTimeCacheDirty = false;
                    instance.ClosedTimeNextRescan = Time.time + CLOSED_INTERIOR_RESCAN_INTERVAL;

                    MelonLogger.Warning($"[KAPALI-ZAMAN] {instance.Config.ResolvedInstanceId} icin zaman ilerletilemedi: {ex.Message}");
                }
            }
        }

        // ─── HEAT WHILE PARKED FOR ANOTHER REGION ───
        //
        // In the vanilla game a fire left burning in an interior keeps warming that interior
        // after its scene is gone: a temperature mod takes the fire's remaining life with it when
        // the scene unloads and heats from that. Here nothing unloads, so the same thing is done
        // by keeping the heat source running - for exactly as long as the fire had life left.
        // The fire's own timers stay untouched; the game advances them when the region returns.
        private static void AdvanceParkedInteriorHeat(SeamlessInteriorInstance instance, float todSeconds)
        {
            if (!instance.ParkedHeatCacheBuilt) BuildParkedInteriorHeat(instance);

            var fires = instance.ParkedFires;
            for (int i = fires.Count - 1; i >= 0; i--)
            {
                Il2Cpp.Fire fire = fires[i];
                float secondsLeft = instance.ParkedFireSecondsLeft[i] - todSeconds;

                if (fire == null || secondsLeft <= 0f)
                {
                    fires.RemoveAt(i);
                    instance.ParkedFireSecondsLeft.RemoveAt(i);
                    continue;
                }

                instance.ParkedFireSecondsLeft[i] = secondsLeft;

                // Only ramps the fire's heat value; nothing else in the building runs.
                HeatSource heatSource = fire.m_HeatSource;
                if (heatSource != null) heatSource.Update();
            }
        }

        private static void BuildParkedInteriorHeat(SeamlessInteriorInstance instance)
        {
            instance.ParkedHeatCacheBuilt = true;
            instance.ParkedFires.Clear();
            instance.ParkedFireSecondsLeft.Clear();

            GameObject master = instance.MasterInterior;
            if (master == null) return;

            foreach (var fire in master.GetComponentsInChildren<Il2Cpp.Fire>(true))
            {
                if (!IsFireStillAlive(fire)) continue;

                instance.ParkedFires.Add(fire);
                instance.ParkedFireSecondsLeft.Add(fire.GetRemainingLifeTimeSeconds());
            }

            if (s_DebugBounds && instance.ParkedFires.Count > 0)
            {
                MelonLogger.Msg($"[KAPALI-ZAMAN] {instance.Config.ResolvedInstanceId}: park halinde {instance.ParkedFires.Count} " +
                                $"ates isitmaya devam ediyor.");
            }
        }

        private static void ClearParkedInteriorHeat(SeamlessInteriorInstance instance)
        {
            instance.ParkedHeatCacheBuilt = false;
            instance.ParkedFires.Clear();
            instance.ParkedFireSecondsLeft.Clear();
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
            instance.ClosedLamps.Clear();
            instance.ClosedTorches.Clear();
            instance.ClosedFlares.Clear();

            GameObject master = instance.MasterInterior;
            if (master == null) return;

            // Only what is UNDER MasterInterior matters: that is exactly the set Unity stops
            // updating when the clone closes. A fire or a pot that was never adopted into
            // the clone stays active in the world and keeps ticking on its own.
            var fires = InteriorScan.Components<Il2Cpp.Fire>(master);
            foreach (var fire in fires)
                if (fire != null) instance.ClosedFires.Add(fire);

            var pots = InteriorScan.Components<Il2Cpp.CookingPotItem>(master);
            foreach (var pot in pots)
                if (pot != null) instance.ClosedCookingPots.Add(pot);

            var lamps = InteriorScan.Components<Il2CppTLD.Gear.KeroseneLampItem>(master);
            foreach (var lamp in lamps)
                if (lamp != null) instance.ClosedLamps.Add(lamp);

            var torches = InteriorScan.Components<Il2Cpp.TorchItem>(master);
            foreach (var torch in torches)
                if (torch != null) instance.ClosedTorches.Add(torch);

            var flares = InteriorScan.Components<Il2Cpp.FlareItem>(master);
            foreach (var flare in flares)
                if (flare != null) instance.ClosedFlares.Add(flare);

            if (s_DebugBounds && (instance.ClosedFires.Count > 0 || instance.ClosedCookingPots.Count > 0
                                  || instance.ClosedLamps.Count > 0 || instance.ClosedTorches.Count > 0
                                  || instance.ClosedFlares.Count > 0))
            {
                MelonLogger.Msg($"[KAPALI-ZAMAN] {instance.Config.ResolvedInstanceId}: " +
                                $"{instance.ClosedFires.Count} ates, {instance.ClosedCookingPots.Count} tencere, " +
                                $"{instance.ClosedLamps.Count} lamba, {instance.ClosedTorches.Count} mesale, " +
                                $"{instance.ClosedFlares.Count} fisek takip ediliyor.");
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

                // The heat source runs as if the building were open, so mods listening to it keep warming it.
                // HeatSource.Update only ramps the heat value toward the fire's own maximum.
                HeatSource heatSource = fire.m_HeatSource;
                if (heatSource != null) heatSource.Update();
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

            AdvanceClosedLightSources(instance, todHours);
        }

        // Lamps, torches and flares burning inside the closed building. Only their fuel and burn
        // time are moved on - the same fields the vanilla save's catch-up writes (KeroseneLampItem
        // .Deserialize calls this very ReduceFuel). Their Update is deliberately NOT called: it
        // also re-enables renderers and objects, moves audio sources, drives the player's hands,
        // animation and control mode, and runs the ignite / extinguish paths - none of which
        // belongs to a building nobody is in. Whether the torch is spent and the lamp is out is
        // decided by the components themselves on their first Update after the building opens.
        private static void AdvanceClosedLightSources(SeamlessInteriorInstance instance, float todHours)
        {
            float todMinutes = todHours * 60f;

            var lamps = instance.ClosedLamps;
            for (int i = lamps.Count - 1; i >= 0; i--)
            {
                Il2CppTLD.Gear.KeroseneLampItem lamp = lamps[i];
                if (lamp == null) { lamps.RemoveAt(i); continue; }

                if (lamp.IsOn()) lamp.ReduceFuel(todHours);
            }

            var torches = instance.ClosedTorches;
            for (int i = torches.Count - 1; i >= 0; i--)
            {
                Il2Cpp.TorchItem torch = torches[i];
                if (torch == null) { torches.RemoveAt(i); continue; }

                // IsBurning() reports the state the last Update left behind, so the burn time
                // itself decides when there is nothing left to run down.
                if (!torch.IsBurning()) continue;
                if (torch.m_ElapsedBurnMinutes >= torch.GetModifiedBurnLifetimeMinutes()) continue;

                torch.m_ElapsedBurnMinutes += todMinutes;
            }

            var flares = instance.ClosedFlares;
            for (int i = flares.Count - 1; i >= 0; i--)
            {
                Il2Cpp.FlareItem flare = flares[i];
                if (flare == null) { flares.RemoveAt(i); continue; }

                if (!flare.IsBurning()) continue;
                if (flare.m_ElapsedBurnMinutes >= flare.GetModifiedBurnLifetimeMinutes()) continue;

                flare.m_ElapsedBurnMinutes += todMinutes;
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
