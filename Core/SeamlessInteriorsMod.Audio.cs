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

        // Is the player inside a cloned scene? Set on door transitions.
        // Wind shelter and wind occlusion checks read this flag.
        public static bool s_IsPlayerInsideClone = false;

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
