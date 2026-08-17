using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace SeamlessInteriors
{
    [HarmonyPatch(typeof(Il2Cpp.Wind), "Start")]
    public class WindStartFixPatch
    {
        private static int s_pendingRestarts = 0;

        public static void Postfix(Il2Cpp.Wind __instance)
        {
            if (__instance == null) return;
            s_pendingRestarts++;
            MelonCoroutines.Start(DelayedWindReset(__instance));
        }

        private static IEnumerator DelayedWindReset(Il2Cpp.Wind wind)
        {
            float waited = 0f;

            // Bekleme süresini aktif binalardan herhangi birinin yüklemesi sürdüğü müddetçe uzat
            while (SeamlessInteriorsMod.ActiveInteriors.Values.Any(i => !i.RunCompleted) && waited < 15f)
            {
                yield return new WaitForSeconds(0.5f);
                waited += 0.5f;
            }

            if (SeamlessInteriorsMod.ActiveInteriors.Values.Any(i => !i.RunCompleted))
            {
                s_pendingRestarts--;
                yield break;
            }

            yield return new WaitForSeconds(1f);

            s_pendingRestarts--;
            if (wind == null) yield break;
            if (wind.m_WindAudioForceStopped) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            bool playerInside = playerT != null && SeamlessInteriorsMod.IsPositionInsideAnyInstance(playerT.position);

            if (playerInside)
            {
                wind.m_WindLoopAudioInstance = 0;
                wind.m_WindAudioForceStopped = false;
                yield return null;
                yield return null;
                if (wind != null) wind.m_WindAudioForceStopped = true;
                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg("[WIND-FIX] Oyuncu herhangi bir evin icinde, Wind durduruldu.");
                yield break;
            }

            uint idBefore = wind.m_WindLoopAudioInstance;
            wind.m_WindLoopAudioInstance = 0;
            wind.m_WindAudioForceStopped = false;

            yield return null;
            yield return null;

            if (wind == null) yield break;

            uint idAfter = wind.m_WindLoopAudioInstance;

            if (idAfter == 0)
            {
                try
                {
                    uint newId = wind.PlayProceduralWindAudio();
                    if (newId != 0)
                    {
                        wind.m_WindLoopAudioInstance = newId;
                    }
                }
                catch { }
            }
        }
    }
}