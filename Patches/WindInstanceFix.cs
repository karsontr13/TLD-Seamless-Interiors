using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    // Keeps the vanilla wind audio loop alive around cloned interiors.
    [HarmonyPatch(typeof(Il2Cpp.Wind), "Start")]
    public class WindStartFixPatch
    {
        // Clear the ForceStopped flag BEFORE Wind.Start() runs so it can start its
        // audio loop normally. If the player saved and reloaded inside a clone,
        // Wind.Start() would be entered with ForceStopped=true, the loop would never
        // start and the wind sound would be gone for the rest of the session.
        public static void Prefix(Il2Cpp.Wind __instance)
        {
            if (__instance == null) return;

            bool playerInside = SeamlessInteriorsMod.s_IsPlayerInsideClone;
            if (!playerInside)
            {
                string savedId = SeamlessInteriorsMod.GetSavedPlayerInsideInstanceId();
                if (!string.IsNullOrEmpty(savedId))
                    playerInside = true;
            }

            // The loop must always start, inside or outside: while indoors the audio
            // occlusion handles the volume. Leaving ForceStopped=true kills it entirely.
            __instance.m_WindAudioForceStopped = false;

            if (SeamlessInteriorsMod.s_DebugBounds)
                MelonLogger.Msg($"[WIND-FIX-PRE] Wind.Start oncesi ForceStopped=false yapildi, playerInside={playerInside}");
        }

        public static void Postfix(Il2Cpp.Wind __instance)
        {
            // Re-check the wind state once cloning has settled.
            if (__instance == null) return;
            MelonCoroutines.Start(DelayedWindReset(__instance));
        }

        private static IEnumerator DelayedWindReset(Il2Cpp.Wind wind)
        {
            float waited = 0f;

            // Wait while any interior is still being cloned (15s safety cap).
            while (!SeamlessInteriorsMod.AreAllInstancesReady() && waited < 15f)
            {
                yield return new WaitForSeconds(0.5f);
                waited += 0.5f;
            }

            if (!SeamlessInteriorsMod.AreAllInstancesReady())
                yield break;

            yield return new WaitForSeconds(1f);

            if (wind == null) yield break;

            bool playerInside = SeamlessInteriorsMod.s_IsPlayerInsideClone;
            if (!playerInside)
            {
                string savedId = SeamlessInteriorsMod.GetSavedPlayerInsideInstanceId();
                if (!string.IsNullOrEmpty(savedId))
                    playerInside = true;
            }

            if (!playerInside)
            {
                // Outdoors the vanilla behaviour is already correct.
                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg("[WIND-FIX] Oyuncu disarida, wind'e dokunulmadi.");
                yield break;
            }

            // Indoors: hand the loop to the indoor rule. With SilenceOutdoorWindIndoors on
            // (the default) that stops it outright, the way a loading-screen interior does;
            // with it off the old behaviour returns - the loop runs and the global occlusion
            // filter is the only thing lowering it.
            SeamlessInteriorsMod.ApplyOutdoorWindForInsideState(true, "wind-start-postfix");

            if (SeamlessInteriorsMod.s_DebugBounds)
                MelonLogger.Msg($"[WIND-FIX] Oyuncu icerde, dis ruzgar sesi ic mekan kuralina gore ayarlandi, id={wind.m_WindLoopAudioInstance}",
                                $"[WIND-FIX] The player is inside; the outdoor wind loop was set by the indoor rule, id={wind.m_WindLoopAudioInstance}");
        }
    }
}
