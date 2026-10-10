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

        // Muffles outside audio while the player is inside a clone.
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
            LogRoomSoundscape(occlude ? "enter" : "exit");

            // Guarded by the flag so Enter/Exit stay balanced (see the counter note below).
            if (occlude && !s_IsGlobalAudioOccluded)
            {
                GameAudioManager.Instance.EnterOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                s_IsGlobalAudioOccluded = true;
                if (s_DebugBounds) MelonLogger.Msg($"[AUDIO] Global Audio Occlusion ENABLED (kapidan giris).");
            }
            else if (!occlude && s_IsGlobalAudioOccluded)
            {
                GameAudioManager.Instance.ExitOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                s_IsGlobalAudioOccluded = false;
                if (s_DebugBounds) MelonLogger.Msg($"[AUDIO] Global Audio Occlusion DISABLED (kapidan cikis).");
            }
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

        // ─── Outdoor wind audio while the player is inside a parked room ───
        //
        // A vanilla loading-screen interior is its OWN SCENE, so the outdoor wind loop does not
        // exist in it at all - that is why a hut in a blizzard is a low background. A parked room
        // is in the outdoor scene, so the loop keeps playing and the only thing between it and the
        // player is the global occlusion filter. Measured 2026-09-22 in the Airfield hangar: the
        // engine was still being told WINDACTUALSPEED 33.4 and WINDINTENSITYBLEND 62.9, the full
        // outdoor values, with HeavyOcclusion applied over the top. Occlusion is a fixed cut and
        // the wind parameters are not, so a blizzard wins.
        //
        // The game's own mechanism for this is m_WindAudioForceStopped, which it sets on entering
        // an interior scene. This does the same for a parked room.
        //
        // Measured the same day in two hand-authored in-world buildings (Mountaineer's Hut,
        // Timberwolf Mountain; an Emergency Shelter cabin, Sundered Pass): NEITHER reduces the
        // wind parameters either - they only raise the occlusion level, Medium in the leaky hut
        // and Heavy in the sealed cabin. So there is no in-world building to copy here; the
        // loading-screen interiors are the thing a parked room is standing in for.
        public static void ApplyOutdoorWindForInsideState(bool inside, string tag)
        {
            bool silence = inside && IsOutdoorWindSilencedIndoors;

            Il2Cpp.Wind[] winds;
            try { winds = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Wind>(); }
            catch { return; }
            if (winds == null || winds.Length == 0) return;

            foreach (var wind in winds)
            {
                if (wind == null) continue;

                if (silence)
                {
                    if (wind.m_WindAudioForceStopped && wind.m_WindLoopAudioInstance == 0) continue;

                    uint before = wind.m_WindLoopAudioInstance;
                    wind.ForceStopAudioLoop();
                    // ForceStopAudioLoop does not reliably leave the flag set, and a stale id
                    // makes the restart path below think a live loop is already running.
                    wind.m_WindAudioForceStopped = true;
                    wind.m_WindLoopAudioInstance = 0;

                    if (s_DebugBounds)
                        MelonLogger.Msg($"[WIND-INDOOR:{tag}] Ic mekan: dis ruzgar sesi durduruldu (id {before} -> 0).",
                                        $"[WIND-INDOOR:{tag}] Inside: the outdoor wind loop was stopped (id {before} -> 0).");
                }
                else
                {
                    wind.m_WindAudioForceStopped = false;
                    if (wind.m_WindLoopAudioInstance != 0) continue;

                    wind.m_WindLoopAudioInstance = wind.PlayProceduralWindAudio();

                    if (s_DebugBounds)
                        MelonLogger.Msg($"[WIND-INDOOR:{tag}] Disarisi: dis ruzgar sesi yeniden baslatildi (id {wind.m_WindLoopAudioInstance}).",
                                        $"[WIND-INDOOR:{tag}] Outside: the outdoor wind loop was started again (id {wind.m_WindLoopAudioInstance}).");
                }
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
