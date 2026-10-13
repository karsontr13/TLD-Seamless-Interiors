using Il2Cpp;
using Il2CppTLD.Audio;
using MelonLoader;
using UnityEngine;
using System.Collections;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Global audio occlusion state - toggled on door transitions.
        private static bool s_IsGlobalAudioOccluded = false;

        // Is the player inside a cloned scene? Wind shelter and wind occlusion checks read
        // this flag. Written only through MarkPlayerInside / MarkPlayerOutside, which also
        // record WHICH building and tell other mods (see SeamlessInteriorsMod.PlayerLocation.cs).
        public static bool s_IsPlayerInsideClone { get; private set; }

        // Called on every way in and out of a room. The name is historical: it also makes sure the
        // right wind loop is playing (see the note at the end).
        public static void SetAudioOcclusion(bool occlude)
        {
            if (GameAudioManager.Instance == null)
            {
                // GameAudioManager not up yet - retry after a delay.
                if (occlude)
                {
                    s_IsGlobalAudioOccluded = false; // keep false so the retry actually applies it
                    MelonCoroutines.Start(RetrySetAudioOcclusion(occlude));
                }
                return;
            }

            // Deliberately OUTSIDE the balance guard below: that guard exists to keep
            // GameAudioManager's Enter/Exit counter paired, and the wind loop has no counter. It
            // must follow the player on every call, or a second Enter while already inside would
            // leave the loop running.
            ApplyOutdoorWindForInsideState(occlude, occlude ? "enter" : "exit");
            try { MakeWindLoopFollowThePlayer(occlude); }
            catch (System.Exception e) { MelonLogger.Msg($"[ROOM-SOUND] ruzgar dongusu: {e.Message}", $"[ROOM-SOUND] wind loop: {e.Message}"); }
            // The room's own sounds and the outdoor pause follow on the next frame, not up to a
            // second later: on the way out the room must not be heard outdoors.
            s_NextRoomSoundCheckTime = 0f;
            LogRoomSoundscape(occlude ? "enter" : "exit");

            // THE GAME'S OWN FILTER AGAIN, AT A LEVEL THAT CAN BE SET. Up to 2.2.0 this applied the
            // game's Heavy occlusion on the way in and lifted it on the way out. It was taken out on
            // 2026-10-11 when the room got the indoor wind loop, and the outdoor sounds were paused
            // one by one instead. That pause chased every animal sound a second late, and with no
            // filter at all a flag 150 m off was loud in the Airfield hangar. The owner's rule since
            // 2026-10-12: the room sounds like the real interior, and the outdoor sounds other than
            // the wind stay, turned down. By his ear the filter leaves the room's own sounds alone
            // ("Room's own sounds stayed the same"), and Heavy hides everything under the indoor wind.
            ApplyInsideOcclusion(occlude);
        }

        // How far the outdoors is turned down while the player is in a room: 0 none, 1 mild,
        // 2 medium, 3 heavy, the game's own levels (measured 2026-10-12 at about 2, 5 and 10 dB,
        // Heavy also cutting hard above 2 kHz). The game's hand-made in-world buildings use Medium
        // for a leaky hut and Heavy for a sealed one. Mild is the owner's choice (2026-10-12, in the
        // Airfield hangar on the first build that had this at Medium: "Medium is too much").
        // Not a constant, so it can be set by ear.
        private static int s_InsideOcclusionLevel = 1;
        // The level this mod has entered and not yet left, so Enter and Exit stay paired.
        private static int s_InsideOcclusionEntered = 0;

        private static Il2Cpp.AudioOcclusionLevel OcclusionLevelOf(int level)
        {
            if (level >= 3) return Il2Cpp.AudioOcclusionLevel.HeavyOcclusion;
            if (level == 2) return Il2Cpp.AudioOcclusionLevel.MediumOcclusion;
            return Il2Cpp.AudioOcclusionLevel.MildOcclusion;
        }

        private static void ApplyInsideOcclusion(bool inside)
        {
            var gam = GameAudioManager.Instance;
            if (gam == null) return;

            int want = inside ? Mathf.Clamp(s_InsideOcclusionLevel, 0, 3) : 0;
            if (want == s_InsideOcclusionEntered) return;

            if (s_InsideOcclusionEntered != 0) gam.ExitOcclusionTrigger(OcclusionLevelOf(s_InsideOcclusionEntered));
            if (want != 0) gam.EnterOcclusionTrigger(OcclusionLevelOf(want));
            s_InsideOcclusionEntered = want;
            s_IsGlobalAudioOccluded = want != 0;
            MelonLogger.Msg($"[ROOM-SOUND] Dis sesler icin oyunun kendi bogma seviyesi: {want} (0 yok, 1 hafif, 2 orta, 3 agir).",
                            $"[ROOM-SOUND] The game's own muffling of the outdoors is now at level {want} (0 none, 1 mild, 2 medium, 3 heavy).");
        }

        // WHAT DOES THE ROOM ITSELF SOUND LIKE?
        //
        // Silencing the outdoor wind only helps if the room has a soundscape of its own to hear
        // instead. The mod carries the interior's LIGHTING across in detail and does nothing at
        // all with its audio, so before building anything for that, this reports what is actually
        // there when the player steps in: the nearest reverb zone and whether the player is inside
        // it. A vanilla in-world cabin reads its own named zone at about 1 m (measured 2026-09-22:
        // audio_strEmergencyCabinShelter_Reverb, Sundered Pass). A parked room reading the outdoor
        // zone instead means the player is acoustically outdoors in a sealed building, which would
        // explain a dead, unreverberant room.
        public static void LogRoomSoundscape(string tag)
        {
            if (!s_DebugBounds) return;

            try
            {
                var player = GameManager.GetMainCamera();
                if (player == null) return;
                Vector3 pos = player.transform.position;

                var envs = UnityEngine.Object.FindObjectsOfType<Il2Cpp.AkEnvironment>();
                if (envs == null || envs.Length == 0)
                {
                    MelonLogger.Msg($"[ROOM-SOUND:{tag}] Sahnede hic AkEnvironment yok.",
                                    $"[ROOM-SOUND:{tag}] There is no AkEnvironment in the scene at all.");
                    return;
                }

                Il2Cpp.AkEnvironment nearest = null;
                float best = float.MaxValue;
                bool inside = false;
                foreach (var e in envs)
                {
                    if (e == null) continue;
                    float d = Vector3.Distance(e.transform.position, pos);
                    if (d >= best) continue;
                    best = d;
                    nearest = e;
                    var col = e.GetComponent<Collider>();
                    inside = col != null && col.bounds.Contains(pos);
                }

                if (nearest == null) return;

                MelonLogger.Msg($"[ROOM-SOUND:{tag}] En yakin yanki bolgesi '{nearest.gameObject.name}', {best:F1} m, oyuncu icinde={inside}. Sahnedeki toplam: {envs.Length}.",
                                $"[ROOM-SOUND:{tag}] Nearest reverb zone '{nearest.gameObject.name}', {best:F1} m away, player inside it={inside}. Total in the scene: {envs.Length}.");
            }
            catch (System.Exception e)
            {
                MelonLogger.Msg($"[ROOM-SOUND:{tag}] okunamadi: {e.Message}",
                                $"[ROOM-SOUND:{tag}] could not be read: {e.Message}");
            }
        }

        // ─── The wind loop while the player is inside a parked room ───
        //
        // Up to 2.2.0 the outdoor wind loop kept playing in a parked room and the game's Heavy
        // occlusion filter lowered it. The room now gets the indoor wind loop and no filter, as a
        // real interior does: see MakeWindLoopFollowThePlayer in SeamlessInteriorsMod.RoomSound.cs.
        //
        // This used to stop the loop outright behind a setting, SilenceOutdoorWindIndoors, on the
        // belief that the game stops its wind loop in a loading-screen interior. It does not.
        // Measured 2026-10-11 in the real Airfield hangar with this mod not loaded: the loop slot
        // holds Play_WindIndoors, not force-stopped, with the full wind speed. Stopping the loop
        // left a room with no wind at all and froze WINDACTUALSPEED and WINDINTENSITYBLEND at
        // whatever they were at the door. The setting is gone; the loop is never stopped here. In a
        // room it is paused, and the indoor loop plays in its place: MakeWindLoopFollowThePlayer.
        //
        // What is left: make sure a loop exists. The inside argument is kept for the call sites.
        public static void ApplyOutdoorWindForInsideState(bool inside, string tag)
        {
            Il2Cpp.Wind[] winds;
            try { winds = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Wind>(); }
            catch { return; }
            if (winds == null || winds.Length == 0) return;

            foreach (var wind in winds)
            {
                if (wind == null) continue;

                wind.m_WindAudioForceStopped = false;
                if (wind.m_WindLoopAudioInstance != 0) continue;

                wind.m_WindLoopAudioInstance = wind.PlayProceduralWindAudio();

                if (s_DebugBounds)
                    MelonLogger.Msg($"[WIND-INDOOR:{tag}] Ruzgar sesi dongusu yoktu, yeniden baslatildi (id {wind.m_WindLoopAudioInstance}).",
                                    $"[WIND-INDOOR:{tag}] There was no wind loop; it was started again (id {wind.m_WindLoopAudioInstance}).");
            }
        }

        // Resets GameAudioManager's occlusion counters while a scene is unloading.
        //
        // GameAudioManager tracks occlusion with a COUNTER, not a bool
        // (m_InsideHeavyOcclusionTriggerCount): EnterOcclusionTrigger increments,
        // ExitOcclusionTrigger decrements. Saving and quitting while inside a clone
        // leaves an Enter without its matching Exit, and GameAudioManager survives
        // scene changes, so the counter stays stuck at 1. After the load the mod
        // enters once more (counter 2) and the single Exit on leaving cannot bring it
        // back to 0, leaving the audio permanently muffled. Zeroing the counters on
        // unload closes that leak.
        public static void ResetAudioOcclusionCounters(string tag)
        {
            s_IsGlobalAudioOccluded = false;
            s_InsideOcclusionEntered = 0;

            var gam = GameAudioManager.Instance;
            if (gam == null)
            {
                if (s_DebugBounds) MelonLogger.Msg($"[AUDIO-RESET:{tag}] GameAudioManager null, sayac sifirlanamadi.");
                return;
            }

            int heavy = gam.m_InsideHeavyOcclusionTriggerCount;
            int medium = gam.m_InsideMediumOcclusionTriggerCount;
            int mild = gam.m_InsideMildOcclusionTriggerCount;

            if (heavy == 0 && medium == 0 && mild == 0)
            {
                if (s_DebugBounds) MelonLogger.Msg($"[AUDIO-RESET:{tag}] Sayaclar zaten 0, level={gam.m_CurrentAudioOcclusionLevel}");
                return;
            }

            gam.m_InsideHeavyOcclusionTriggerCount = 0;
            gam.m_InsideMediumOcclusionTriggerCount = 0;
            gam.m_InsideMildOcclusionTriggerCount = 0;
            gam.UpdateAudioOcclusion();

            MelonLogger.Msg($"[AUDIO-RESET:{tag}] Occlusion sayaclari sifirlandi (heavy={heavy}, medium={medium}, mild={mild}) -> level={gam.m_CurrentAudioOcclusionLevel}");
        }

        // Diagnostics: describes the occlusion counters and the current level.
        public static string DescribeAudioOcclusion()
        {
            var gam = GameAudioManager.Instance;
            if (gam == null) return "GameAudioManager=null";
            return $"heavy={gam.m_InsideHeavyOcclusionTriggerCount} medium={gam.m_InsideMediumOcclusionTriggerCount} " +
                   $"mild={gam.m_InsideMildOcclusionTriggerCount} level={gam.m_CurrentAudioOcclusionLevel} modFlag={s_IsGlobalAudioOccluded}";
        }

        private static IEnumerator RetrySetAudioOcclusion(bool occlude)
        {
            // Wait for GameAudioManager to come up (10 second cap).
            float waited = 0f;
            while (GameAudioManager.Instance == null && waited < 10f)
            {
                yield return new WaitForSeconds(0.5f);
                waited += 0.5f;
            }

            if (GameAudioManager.Instance == null)
            {
                if (s_DebugBounds) MelonLogger.Msg("[AUDIO] GameAudioManager 10s sonra hala null, occlusion uygulanamadi.");
                yield break;
            }

            SetAudioOcclusion(occlude);
        }

        // Plays the door open + close sound effects on a clone door transition,
        // reproducing the feel of the vanilla LoadScene transition.
        public static void PlayDoorTransitionSound()
        {
            GameObject playerObj = GameManager.GetPlayerObject();
            GameObject camObj = GameManager.GetMainCamera()?.gameObject;
            GameObject audioObj = GameAudioManager.Instance?.gameObject;

            // Wakes up the Wwise pipeline; without it the first sound is swallowed.
            GameAudioManager.PlayGUIButtonClick();

            // Fired on three targets at once - this is the combination that reliably plays.
            if (audioObj != null)
            {
                GameAudioManager.PlaySound("Play_SndMechDoorWoodOpen1", audioObj);
                GameAudioManager.PlaySound("Play_DoorMiscWhooshAOpen", audioObj);
            }
            if (camObj != null)
            {
                GameAudioManager.PlaySound("Play_SndMechDoorWoodOpen1", camObj);
            }
            if (playerObj != null)
            {
                GameAudioManager.PlaySound("Play_SndMechDoorWoodOpen1", playerObj);
            }

            // Closing sound follows after a short delay.
            MelonCoroutines.Start(PlayDoorCloseDelayed(playerObj, camObj, audioObj));

            if (s_DebugBounds)
                MelonLogger.Msg("[AUDIO] Kapi acilma sesi calindi, kapanma sesi gecikmeli calinacak.");
        }

        private static IEnumerator PlayDoorCloseDelayed(GameObject playerObj, GameObject camObj, GameObject audioObj)
        {
            yield return new WaitForSeconds(0.6f);

            if (audioObj != null)
            {
                GameAudioManager.PlaySound("Play_SndMechDoorWoodClose1", audioObj);
                GameAudioManager.PlaySound("Play_DoorMiscWhooshAClose", audioObj);
            }
            if (camObj != null)
            {
                GameAudioManager.PlaySound("Play_SndMechDoorWoodClose1", camObj);
            }
            if (playerObj != null)
            {
                GameAudioManager.PlaySound("Play_SndMechDoorWoodClose1", playerObj);
            }

            if (s_DebugBounds)
                MelonLogger.Msg("[AUDIO] Kapi kapanma sesi calindi.");
        }
    }
}
