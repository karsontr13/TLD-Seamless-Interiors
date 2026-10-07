using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── TELLING THE GAME THE PLAYER WENT INDOORS ───
        //
        // Every clone already carries an IndoorSpaceTrigger, built in SetupWeatherAndParticles
        // on the ParticleKiller object, on layer 25, geometrically containing the player. The
        // game never learns about it, because IndoorSpaceTrigger works through OnTriggerEnter
        // and the player is TELEPORTED into a clone rather than walking into it. So
        // PlayerManager.IndoorSpaceTriggerCount stays 0 for the entire time the player is
        // inside any building this mod builds.
        //
        // WHAT THAT COSTS, MEASURED: standing in the Camp Office in a blizzard, registering the
        // trigger by hand moved the temperature from -3.00C to +7.00C - exactly the trigger's
        // own m_TemperatureDeltaCelsius of 10. Unregistering dropped it back to -3.00C, and
        // re-registering raised it again, so the effect tracks the registration in both
        // directions. Every interior in the mod has been running 10 degrees colder than its own
        // configuration asks for.
        //
        // Patches/WeatherPatches.cs compensates for pieces of this by hand - it forces wind
        // shelter, wind occlusion and IsIndoorEnvironment, and zeroes the wind chill - but it
        // never granted the temperature delta, and it cannot, because that number lives on the
        // trigger rather than in the patch. Those patches are deliberately LEFT IN PLACE here.
        // Registering the trigger should make at least the IsIndoorEnvironment one redundant,
        // but "should" is not a measurement, and vanilla gets its wind shelter from occlusion
        // raycasts against real geometry rather than from the trigger. Retiring them is a
        // separate change that has to be measured on its own.
        //
        // WHY A RECONCILER RATHER THAN A PAIR OF CALLS ON THE DOOR PATH:
        //
        //   1. s_IsPlayerInsideClone is written from eight places, not two - the door
        //      transition, the sub-interior transitions, the visibility watchdog, the unload
        //      path, and a PlayerPrefs restore at scene load. A matching Exit call on the door
        //      path would leave the other six unbalanced.
        //   2. The PlayerPrefs restore sets the flag BEFORE the clone exists, so there is
        //      nothing to register at the moment the flag turns on. Something has to retry.
        //   3. A missed Exit is worse than a missed Enter. A stuck registration leaves the
        //      player at +10C with IsIndoorEnvironment true while stood outside in a blizzard,
        //      and it holds a live reference inside PlayerManager's own list. This is the same
        //      failure shape the stuck-flag guard in InsideFlag.cs already exists to catch.
        //
        // So registration FOLLOWS the flag rather than driving it, and every tick asks the same
        // question: given what the flag says right now, should a trigger be registered, and is
        // it the one that is actually registered?

        private static IndoorSpaceTrigger s_RegisteredIndoorTrigger = null;
        private static string s_RegisteredIndoorInstanceId = null;

        public static void TickIndoorSpaceRegistration()
        {
            var playerManager = GameManager.GetPlayerManagerComponent();
            if (playerManager == null) return;

            SeamlessInteriorInstance wanted = s_IsPlayerInsideClone ? ResolveOccupiedInstance() : null;
            IndoorSpaceTrigger wantedTrigger = (wanted == null) ? null : GetIndoorTrigger(wanted);

            // Already in the right state, which is the case on almost every tick.
            if (ReferenceEquals(wantedTrigger, s_RegisteredIndoorTrigger) && s_RegisteredIndoorTrigger != null)
                return;
            if (wantedTrigger == null && s_RegisteredIndoorTrigger == null)
                return;

            // Drop the old registration first, even when a new one is about to replace it:
            // moving between a building and its sub-interior must not leave two registered.
            if (s_RegisteredIndoorTrigger != null)
                ReleaseIndoorRegistration(playerManager);

            if (wantedTrigger == null) return;

            playerManager.EnterIndoorSpaceTrigger(wantedTrigger);
            s_RegisteredIndoorTrigger = wantedTrigger;
            s_RegisteredIndoorInstanceId = wanted.Config.ResolvedInstanceId;

            if (s_DebugBounds)
                MelonLogger.Msg($"[INDOOR-STATE] {s_RegisteredIndoorInstanceId}: ic mekan tetigi kaydedildi " +
                                $"(count={playerManager.IndoorSpaceTriggerCount}).");
        }

        // Hands the registration back. Safe to call when nothing is registered.
        private static void ReleaseIndoorRegistration(PlayerManager playerManager)
        {
            if (s_RegisteredIndoorTrigger == null) return;

            string releasedId = s_RegisteredIndoorInstanceId;

            // Unity's overloaded == is what detects a destroyed native object here, so this is
            // deliberately not ReferenceEquals: a destroyed trigger cannot be handed back, and
            // calling into it would be reaching into freed native memory.
            if (s_RegisteredIndoorTrigger != null && !s_RegisteredIndoorTrigger.Equals(null))
            {
                playerManager.ExitIndoorSpaceTrigger(s_RegisteredIndoorTrigger);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[INDOOR-STATE] {releasedId}: ic mekan tetigi birakildi " +
                                    $"(count={playerManager.IndoorSpaceTriggerCount}).");
            }
            else
            {
                // The clone was destroyed with a registration outstanding. The mod deactivates
                // a clone before it destroys one, and deactivation already releases the
                // registration on the next tick, so reaching here means a path that skipped
                // that. Worth a warning rather than silence: the stale entry is left inside
                // PlayerManager's list and the player may stay "indoors" until the next load.
                MelonLogger.Warning($"[INDOOR-STATE] {releasedId}: tetigi kayitliyken klon yok edildi - " +
                                    "kayit birakilamadi.");
            }

            s_RegisteredIndoorTrigger = null;
            s_RegisteredIndoorInstanceId = null;
        }

        // Which building is the player actually standing in?
        //
        // Entering a building is what opens its clone, so a closed clone cannot be the one the
        // player is inside - the same reasoning the stuck-flag guard uses, and for the same
        // reason: it needs no raycast and cannot flicker frame to frame. Where more than one
        // clone is open (a building and its sub-interior are both open during the transition
        // between them) the volume test picks the right one.
        private static SeamlessInteriorInstance ResolveOccupiedInstance()
        {
            SeamlessInteriorInstance onlyOpen = null;
            int openCount = 0;

            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || instance.MasterInterior == null) continue;
                if (!instance.MasterInterior.activeInHierarchy) continue;

                openCount++;
                if (onlyOpen == null) onlyOpen = instance;
            }

            if (openCount == 0) return null;
            if (openCount == 1) return onlyOpen;

            Vector3 playerPos = GameManager.GetPlayerTransform().position;
            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || instance.MasterInterior == null) continue;
                if (!instance.MasterInterior.activeInHierarchy) continue;
                if (instance.IsPositionInVolume(playerPos)) return instance;
            }

            // Several open and the player in none of their volumes: mid-transition. Registering
            // a guess here would be worse than waiting a tick for it to settle.
            return null;
        }

        private static IndoorSpaceTrigger GetIndoorTrigger(SeamlessInteriorInstance instance)
        {
            // The trigger shares the ParticleKiller object with the instance's box collider.
            if (instance.InteriorTrigger == null) return null;
            return instance.InteriorTrigger.gameObject.GetComponent<IndoorSpaceTrigger>();
        }
    }
}
