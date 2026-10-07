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
        private static bool s_InsideFlagParkedWarned;

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

        // ─── NO ALWAYS-ON "IS THE PLAYER IN A ROOM" CHECK, ON PURPOSE ───
        //
        // One was written on 2026-09-18 and taken out the same hour. It compared the inside flag
        // with the player's position, first twice a second and then every frame, and rebuilt a
        // parked room round a player found standing in one. The owner's objection stands as the
        // reason it is gone: "I don't like per frame. If it's already caught by the author's load
        // code, then I guess we don't need it", and "This would only be critical right after a
        // load, not all the time."
        //
        // Every failure seen that day was at a load, and the load is covered where it happens: the
        // author's sync runs when the building is ready and asks ResolvePlayerInside, which for a
        // parked room lets position override a record that wrongly says "outside". A discrepancy
        // in the middle of play was imagined, never observed. If one is ever seen, the place to
        // fix it is whichever door path produced it. The removed code is in history at 4dbeea7
        // and 2fed391.

        public static void TickPlayerInsideCloneFlagGuard()
        {
            if (!s_IsPlayerInsideClone)
            {
                s_InsideFlagOutsideConfirmations = 0;
                return;
            }

            if (Time.time < s_NextInsideFlagCheckTime) return;
            s_NextInsideFlagCheckTime = Time.time + INSIDE_FLAG_CHECK_INTERVAL;

            // NOT AT THE MAIN MENU, AND NEVER AGAINST A SAVE THAT IS NOT LOADED.
            //
            // This guard used to keep running after an exit to the menu. There, no clone scene is
            // open by definition, so after its six confirmations it concluded the player was
            // outside, cleared the flag, AND wiped the save's own record of which building the
            // player was in, for whatever save name the game still had in hand. That record lives
            // in PlayerPrefs and persists. The save file still had the player inside, so the next
            // load of it treated them as outside: no early activation, no teleport, nothing built
            // round them. With a parked interior that is a 40 m fall onto the shell's roof.
            //
            // Found 2026-09-18 from two such falls and one load that was fine. 13:09:09 exit from
            // inside, 13:09:34 "[ICERIDE-FLAG] ... temizlendi", next load fell. 13:23:02 exit,
            // game relaunched 15 s later, before the guard's confirmations ran out, next two loads
            // fine. 13:36:33 exit, player waited at the menu, 13:36:58 the same line, next load
            // fell; read live afterwards, the saved record was the empty string.
            //
            // At the menu the in-memory flag may go, quietly. The save's record may not.
            if (Il2Cpp.GameManager.IsMainMenuActive() || ActiveInteriors.Count == 0)
            {
                s_InsideFlagOutsideConfirmations = 0;
                if (Il2Cpp.GameManager.IsMainMenuActive())
                {
                    MarkPlayerOutside("ana menu");
                    SetAudioOcclusion(false);
                }
                return;
            }

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

            // "NO CLONE IS OPEN" IS NOT THE SAME AS "THE PLAYER IS OUTSIDE". The test above infers
            // where the player is from what happens to be switched on. If something closed a room
            // round a player who is still standing in it, that inference is exactly backwards, and
            // acting on it erases the one record that could put things right at the next load
            // (owner, 2026-09-18: "resetting it to outside even though the player's inside is still
            // wrong"). So where position CAN settle it, it does.
            //
            // It can for a PARKED building: its box stands ParkOffset away from anything the player
            // can reach outdoors and is only ever entered by teleport, so "in the box" means "in
            // the room", with no doorway and no wall to argue about. For a co-located building the
            // box reaches out past the walls, which is the stuck-flag case this guard exists to
            // clear, so there the author's rule stands alone.
            Transform playerT = Il2Cpp.GameManager.GetPlayerTransform();
            if (playerT != null)
            {
                foreach (var instance in ActiveInteriors.Values)
                {
                    if (instance == null || !instance.IsPositionInParkedRoom(playerT.position, 2f)) continue;

                    s_InsideFlagOutsideConfirmations = 0;
                    if (!s_InsideFlagParkedWarned)
                    {
                        s_InsideFlagParkedWarned = true;
                        MelonLogger.Warning($"[ICERIDE-FLAG] {instance.Config.ResolvedInstanceId}: hicbir klon acik degil ama oyuncu park edilmis ic mekanin hacminde duruyor - bayrak ve kayit OLDUGU GIBI birakildi. Ic mekan oyuncunun etrafinda kapanmis; asil hata o.");
                    }
                    return;
                }
            }
            s_InsideFlagParkedWarned = false;

            s_InsideFlagOutsideConfirmations++;
            if (s_InsideFlagOutsideConfirmations < INSIDE_FLAG_CONFIRMATIONS) return;
            s_InsideFlagOutsideConfirmations = 0;

            MarkPlayerOutside("takili bayrak bekcisi");
            SetAudioOcclusion(false);

            // The saved entry is what keeps bringing the flag back on every scene load, so
            // it goes too. The player is outside; that is what the save should say.
            ClearSavedPlayerInsideState();

            MelonLogger.Msg("[ICERIDE-FLAG] Hicbir klon sahne acik degilken 'oyuncu iceride' " +
                            "bayragi acik kalmisti - temizlendi (ruzgar korumasi ve ic mekan sicakligi normale dondu).");
        }
    }
}
