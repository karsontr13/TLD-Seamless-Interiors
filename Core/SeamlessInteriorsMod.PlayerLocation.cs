using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── WHERE IS THE PLAYER ───
        //
        // s_IsPlayerInsideClone used to be written directly from nine places - the doors, the
        // sub-interior links, the load paths, the unload, the stuck-flag guard and the wind
        // fix-up - and none of them recorded WHICH building the player was in. Other mods need
        // exactly that: Dynamic Temperature keeps a heat store per building, Major Miseries
        // keys its carbon monoxide state by it. So every write now goes through the two
        // functions below, which also record the building and announce the change through
        // SeamlessInteriorsApi.PlayerEnteredInterior / PlayerExitedInterior.
        //
        // The events fire only on a real change (entering the building the player is already
        // in is silent), and a handler that throws is logged and skipped, so no other mod can
        // break a door transition.

        private static string s_PlayerInteriorId = null;

        // ─── ARRIVING FROM ANOTHER SCENE ───
        // A door in a scene SI does not clone (the concentrator's mine) that leads back into a cloned
        // building loads the region instead (PortalPatches); the player then arrives at this point in the clone.
        internal static string s_PendingArrivalInstance;
        internal static string s_PendingArrivalPoint;
        internal static float s_ArrivalGraceUntil;
        internal static bool ArrivalProtected => !string.IsNullOrEmpty(s_PendingArrivalInstance) || Time.realtimeSinceStartup < s_ArrivalGraceUntil;

        // The pending arrival point inside this clone; false when none is waiting for it.
        internal static bool TryTakePendingArrival(SeamlessInteriorInstance instance, out Vector3 pos, bool consume = true)
        {
            pos = Vector3.zero;
            if (instance == null || instance.MasterInterior == null
                || s_PendingArrivalInstance != instance.Config.ResolvedInstanceId) return false;

            string pointName = s_PendingArrivalPoint;
            if (consume) { s_PendingArrivalInstance = null; s_PendingArrivalPoint = null; }

            foreach (Transform t in instance.MasterInterior.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.name != pointName) continue;
                pos = t.position;
                MelonLogger.Msg($"[DONUS] {instance.Config.ResolvedInstanceId}: oyuncu klonda '{pointName}' noktasina yerlestiriliyor.");
                return true;
            }
            MelonLogger.Warning($"[DONUS] {instance.Config.ResolvedInstanceId}: klonda '{pointName}' bulunamadi.");
            return false;
        }

        // The game spawns the player itself late in the load, so the arrival is placed again once the screen opens,
        // and once more if they still drop through the terrain.
        internal static System.Collections.IEnumerator FinishPendingArrival()
        {
            if (string.IsNullOrEmpty(s_PendingArrivalInstance)) yield break;
            s_ArrivalGraceUntil = Time.realtimeSinceStartup + 4f;   // covers the landing after the last placement
            SeamlessInteriorInstance instance = null;
            foreach (var inst in ActiveInteriors.Values)
                if (inst != null && inst.Config.ResolvedInstanceId == s_PendingArrivalInstance) instance = inst;
            if (instance == null || !TryTakePendingArrival(instance, out Vector3 arrival)) { s_PendingArrivalInstance = null; s_PendingArrivalPoint = null; yield break; }

            for (int pass = 0; pass < 3; pass++)
            {
                if (instance.MasterInterior == null) yield break;
                if (!instance.MasterInterior.activeSelf) instance.MasterInterior.SetActive(true);
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) yield break;
                if (pass == 0 || playerT.position.y < arrival.y - 2f || Vector3.Distance(playerT.position, arrival) > 40f)
                {
                    GameManager.GetPlayerManagerComponent().TeleportPlayer(EnsureAboveGround(arrival, instance), playerT.rotation);
                    MarkPlayerInside(instance, "maden donusu");
                    SyncExternalHiddenObjects();
                    if (pass > 0) MelonLogger.Msg($"[DONUS] {instance.Config.ResolvedInstanceId}: oyuncu dusuyordu, tekrar yerlestirildi.");
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        // The building the player is in (its ResolvedInstanceId), or null while outside.
        public static string PlayerInteriorId
        {
            get { return s_IsPlayerInsideClone ? s_PlayerInteriorId : null; }
        }

        // The door put the player in ANOTHER parked building, not linked to this one. Neighbors' rooms
        // overlap at park height (Milton H2/F3, 2026-10-09), so this one's box must not count then.
        internal static bool PlayerIsInAnotherParkedBuilding(SeamlessInteriorInstance instance)
        {
            string inRoom = PlayerInteriorId;
            if (instance == null || instance.Config == null || !instance.Config.IsParked || string.IsNullOrEmpty(inRoom)) return false;
            string own = instance.Config.ResolvedInstanceId;
            if (inRoom == own || IsSubInteriorLinked(instance, inRoom)) return false;
            SeamlessInteriorInstance other;
            return ActiveInteriors.TryGetValue(inRoom, out other) && other != null && !IsSubInteriorLinked(other, own);
        }

        private static bool IsSubInteriorLinked(SeamlessInteriorInstance instance, string targetId)
        {
            var links = instance.Config.SubInteriorLinks;
            if (links == null) return false;
            for (int i = 0; i < links.Count; i++) if (links[i] != null && links[i].TargetInstanceId == targetId) return true;
            return false;
        }

        public static void MarkPlayerInside(SeamlessInteriorInstance instance, string reason)
        {
            if (instance == null) return;
            MarkPlayerInside(instance.Config.ResolvedInstanceId, reason);
        }

        // The id form is for the load path, which has to set the flag before the building
        // has been built.
        public static void MarkPlayerInside(string instanceId, string reason)
        {
            if (string.IsNullOrEmpty(instanceId)) return;

            string previous = PlayerInteriorId;
            s_IsPlayerInsideClone = true;
            s_PlayerInteriorId = instanceId;

            if (previous != instanceId)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[KONUM] Oyuncu iceride: {instanceId} (onceki: {previous ?? "disarida"}, sebep: {reason})");

                // Straight from one building into another (a farmhouse into its basement) is a
                // departure AND an arrival.
                if (previous != null) SeamlessInteriorsApi.RaisePlayerExitedInterior(previous);
                SeamlessInteriorsApi.RaisePlayerEnteredInterior(instanceId);
            }

            // After the events: the game's own indoor space follows (see SeamlessInteriorsMod.VanillaView.cs).
            SyncVanillaIndoorSpace();
        }

        public static void MarkPlayerOutside(string reason)
        {
            string previous = PlayerInteriorId;
            s_IsPlayerInsideClone = false;
            s_PlayerInteriorId = null;

            if (previous != null)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[KONUM] Oyuncu disarida (onceki: {previous}, sebep: {reason})");

                SeamlessInteriorsApi.RaisePlayerExitedInterior(previous);
            }

            SyncVanillaIndoorSpace();
        }

        // ─── LEAVING A BUILDING ───
        //
        // Everything the exit door does apart from moving the player: close the clone, put the
        // shell (and a sub-interior's parent shell) back, restore the outside-world objects,
        // lift the audio occlusion and clear the inside state.
        //
        // Three callers:
        //   * the exit door (PortalPatches), which teleports the player afterwards,
        //   * the door-less exit below, for a player something else has already moved out,
        //   * the visibility watchdog, which closes clones on its own schedule.
        //
        // playerIsLeaving says the caller KNOWS the player has left this building. The
        // watchdog does not know that - it may be closing a clone the player never entered -
        // so for it the player-side work only happens when the player is recorded in THIS
        // building. That matters for sub-interiors: closing a basement must not also close the
        // farmhouse the player is standing in.
        public static void ExitInteriorState(SeamlessInteriorInstance instance, string reason, bool playerIsLeaving)
        {
            if (instance == null) return;

            bool leaving = playerIsLeaving || PlayerInteriorId == instance.Config.ResolvedInstanceId;

            // ORDER MATTERS: hide the items and deactivate MasterInterior FIRST. Anything that
            // moves the player afterwards must not meet the clone's colliders.
            HideInteriorCompletely(instance);
            if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(true);

            // SUB-INTERIOR: an instance without a shell of its own (the basement) sits under its
            // parent's shell, so the parent's shell and external objects come back with it.
            if (leaving && instance.ExteriorShell == null)
            {
                foreach (var parentInst in ActiveInteriors.Values)
                {
                    if (parentInst == null || parentInst.Config.SubInteriorLinks == null) continue;
                    foreach (var link in parentInst.Config.SubInteriorLinks)
                    {
                        if (link.TargetInstanceId != instance.Config.ResolvedInstanceId) continue;

                        HideInteriorCompletely(parentInst);
                        if (parentInst.ExteriorShell != null) parentInst.ExteriorShell.SetActive(true);
                        break;
                    }
                }
            }

            SyncExternalHiddenObjects();

            if (!leaving) return;

            SetAudioOcclusion(false);
            MarkPlayerOutside(reason);

            // Make sure wind is running again (ForceStopped may have been left set).
            var wind = UnityEngine.Object.FindObjectOfType<Il2Cpp.Wind>();
            if (wind != null) wind.m_WindAudioForceStopped = false;
        }

        // ─── LEAVING WITHOUT A DOOR ───
        //
        // The inside state follows the doors, so anything that moved the player out of a
        // building some other way left it behind: Major Miseries' sleepwalking (which writes
        // the player's position directly), a console teleport, any teleporting mod. The player
        // then stood in the snow sheltered from wind, at indoor temperature, with muffled audio
        // and the building's shell switched off. The watchdog only rescued them within 80 m or
        // beyond 200 m of the building.
        //
        // This check closes that gap. It asks for the same evidence the watchdog's force-close
        // does - outside the padded volume AND not inside by the ray test - several checks in a
        // row, and it stands back while a door transition, a load or a clone build owns the state.
        private const float DOORLESS_EXIT_CHECK_INTERVAL = 0.25f;
        private const int DOORLESS_EXIT_CONFIRMATIONS = 3;
        private const float DOORLESS_EXIT_VOLUME_PADDING = 3f;

        private static float s_NextDoorlessExitCheck = 0f;
        private static int s_DoorlessExitConfirmations = 0;

        public static void TickDoorlessExitDetection(Vector3 playerPos)
        {
            string id = PlayerInteriorId;
            if (id == null)
            {
                s_DoorlessExitConfirmations = 0;
                return;
            }

            if (Time.time < s_NextDoorlessExitCheck) return;
            s_NextDoorlessExitCheck = Time.time + DOORLESS_EXIT_CHECK_INTERVAL;

            if (Time.time - s_LastPortalUseTime <= PORTAL_SUPPRESS_WINDOW
                || IsLoadStateAuthoritative() || IsAnyCloningActive() || s_ScreenHeldBlack)
            {
                s_DoorlessExitConfirmations = 0;
                return;
            }

            SeamlessInteriorInstance instance;
            if (!ActiveInteriors.TryGetValue(id, out instance) || instance == null)
            {
                s_DoorlessExitConfirmations = 0;
                return;
            }

            // The building belongs to a region that is not loaded - parked for another region,
            // or left behind by a scene change the player made from inside it. Nobody can be
            // standing in it, and the saved entry that says otherwise is stale.
            if (instance.InteriorPersisted || !IsExteriorSceneLoaded(instance))
            {
                if (++s_DoorlessExitConfirmations < DOORLESS_EXIT_CONFIRMATIONS) return;
                s_DoorlessExitConfirmations = 0;

                MarkPlayerOutside("bina yuklu bolgede degil");
                SetAudioOcclusion(false);

                // At the main menu the saved entry belongs to the save the player just left and must survive.
                string activeScene = RealSceneName(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                if (string.IsNullOrEmpty(activeScene) || activeScene.StartsWith("MainMenu", System.StringComparison.Ordinal)) return;

                MelonLogger.Msg($"[KAPISIZ-CIKIS] {id}: bina yuklu bolgede degil, 'oyuncu iceride' durumu temizlendi.");
                ClearSavedPlayerInsideState();
                return;
            }

            // A closed clone with the flag still set is the stuck-flag guard's case (InsideFlag.cs).
            if (!instance.RunCompleted || instance.MasterInterior == null || instance.InteriorTrigger == null
                || !instance.MasterInterior.activeInHierarchy)
            {
                s_DoorlessExitConfirmations = 0;
                return;
            }

            if (instance.IsPositionInVolume(playerPos, DOORLESS_EXIT_VOLUME_PADDING) || instance.IsPositionInside(playerPos))
            {
                s_DoorlessExitConfirmations = 0;
                return;
            }

            if (++s_DoorlessExitConfirmations < DOORLESS_EXIT_CONFIRMATIONS) return;
            s_DoorlessExitConfirmations = 0;

            MelonLogger.Msg($"[KAPISIZ-CIKIS] {id}: oyuncu kapi kullanmadan binanin disina cikmis (pos={playerPos}), ic mekan kapatiliyor.");

            // Same as a door: the watchdog stands back for a moment and the post-load authority ends.
            NotifyPortalUsed();
            ExitInteriorState(instance, "kapisiz cikis", true);
        }

        public static bool IsExteriorSceneLoaded(SeamlessInteriorInstance instance)
        {
            if (instance == null || string.IsNullOrEmpty(instance.Config.ExteriorSceneName)) return false;
            return RealSceneByName(instance.Config.ExteriorSceneName).isLoaded;
        }
    }
}
