using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── "THE PLAYER IS INSIDE A CLONE" FLAG ───
        //
        // s_IsPlayerInsideClone drives the wind shelter, the wind occlusion and the indoor
        // temperature patches (Patches/WeatherPatches.cs). Left true while the player is
        // standing in the open it makes them permanently sheltered from wind and stuck at
        // indoor temperature.
        //
        // HOW IT USED TO GET STUCK: the flag is restored from PlayerPrefs at scene load
        // (key SeamlessInteriors_PlayerInside_<save name>). That key is addressed by SAVE
        // NAME, and The Long Dark reuses save slot names, so a brand new game can inherit
        // the "player was inside building X" entry left behind by whatever was in that slot
        // before. The new-game cleanup that is supposed to delete it cannot always run - it
        // needs SaveGameSystem.m_CurrentSaveName, which is not always set yet when the
        // region scene initialises, and it only runs in a region the mod supports. With the
        // stale entry still in place two paths would switch the flag on for a player who was
        // never indoors:
        //
        //   1. the early flag in OnSceneWasInitialized, and
        //   2. FixWindAfterRun, which trusted the saved id on its own.
        //
        // That is why saving and reloading fixed it (the save writes "outside" over the
        // stale entry) and why walking into a clone and back out fixed it (the door
        // transition sets the flag from what actually happened).
        //
        // The guard below is the backstop for every remaining path: a player cannot be
        // inside a clone while every clone in the region is closed, because entering one is
        // what opens it. So when no clone is open, the flag - and the saved state that keeps
        // resurrecting it - are both cleared.

        // Checked this often rather than every frame; a stuck flag is not urgent.
        private const float INSIDE_FLAG_CHECK_INTERVAL = 0.5f;

        // How many consecutive checks must agree before the flag is cleared. Matches the
        // confidence the visibility watchdog demands before it force-closes a clone.
        private const int INSIDE_FLAG_CONFIRMATIONS = 6;

        private static float s_NextInsideFlagCheckTime = 0f;
        private static int s_InsideFlagOutsideConfirmations = 0;

        // Does any building the mod handles in this scene answer to this instance id?
        // Used to spot a saved "player is inside" entry that belongs to a different region -
        // i.e. one left over from another save that happened to share the slot name.
        public static bool SceneOwnsInstanceId(string sceneName, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;

            foreach (var cfg in SupportedInteriors)
            {
                if (cfg == null) continue;
                if (cfg.ExteriorSceneName == sceneName && cfg.ResolvedInstanceId == instanceId)
                    return true;
            }
            return false;
        }

        public static void TickPlayerInsideCloneFlagGuard()
        {
            if (!s_IsPlayerInsideClone)
            {
                s_InsideFlagOutsideConfirmations = 0;
                return;
            }

            if (Time.time < s_NextInsideFlagCheckTime) return;
            s_NextInsideFlagCheckTime = Time.time + INSIDE_FLAG_CHECK_INTERVAL;

            // A door transition owns the flag for a moment after it runs.
            if (Time.time - s_LastPortalUseTime <= PORTAL_SUPPRESS_WINDOW)
            {
                s_InsideFlagOutsideConfirmations = 0;
                return;
            }

            // Right after a load the saved state outranks everything else: the clone the
            // player saved in has not necessarily been built yet, and the early flag is
            // deliberately set before it is (Wind.Start reads it).
            if (IsLoadStateAuthoritative() || IsAnyCloningActive() || s_ScreenHeldBlack)
            {
                s_InsideFlagOutsideConfirmations = 0;
                return;
            }

            // THE DECIDING TEST, and deliberately not a geometric one: entering a building
            // is what opens its clone, so with every clone closed there is nothing the
            // player could be inside. No raycast, nothing that can flicker.
            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null) continue;
                if (instance.MasterInterior == null) continue;
                if (instance.MasterInterior.activeInHierarchy)
                {
                    s_InsideFlagOutsideConfirmations = 0;
                    return;
                }
            }

            s_InsideFlagOutsideConfirmations++;
            if (s_InsideFlagOutsideConfirmations < INSIDE_FLAG_CONFIRMATIONS) return;
            s_InsideFlagOutsideConfirmations = 0;

            s_IsPlayerInsideClone = false;
            SetAudioOcclusion(false);

            // The saved entry is what keeps bringing the flag back on every scene load, so
            // it goes too. The player is outside; that is what the save should say.
            ClearSavedPlayerInsideState();

            MelonLogger.Msg("[ICERIDE-FLAG] Hicbir klon sahne acik degilken 'oyuncu iceride' " +
                            "bayragi acik kalmisti - temizlendi (ruzgar korumasi ve ic mekan sicakligi normale dondu).");
        }
    }
}
