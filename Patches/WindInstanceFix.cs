// ============================================================
// DOSYA: WindInstanceFix.cs
// AÇIKLAMA: Rüzgar sesi kesme sorununu çözen patch.
//
// KÖK NEDEN:
//   LakeCabinA gibi orijinal iç mekanlara girince LakeRegion
//   unload/reload oluyor. Yeni Wind objesi doğuyor ve
//   PlayProceduralWindAudio() çağrılıyor — Wwise ID geliyor
//   ama ses çıkmıyor, çünkü AkGameObj emitter sahne tam
//   yüklenmeden önce kayıt ediliyor.
//
// ÇÖZÜM:
//   Wind.Start() sonrası 2 saniye bekle (sahne tam yüklensin),
//   sonra m_WindLoopAudioInstance'ı 0'a sıfırla ve
//   m_WindAudioForceStopped=false yap.
//   Bu, Wind'in kendi UpdateProceduralWind() döngüsünün
//   "ses çalmıyor" tespiti yapıp PlayProceduralWindAudio'yu
//   sahne hazırken yeniden çağırmasını tetikler.
// ============================================================

using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    [HarmonyPatch(typeof(Il2Cpp.Wind), "Start")]
    public class WindStartFixPatch
    {
        // Kaç Wind.Start() beklemedeyiz — çakışmayı önle
        private static int s_pendingRestarts = 0;

        public static void Postfix(Il2Cpp.Wind __instance)
        {
            if (__instance == null) return;
            s_pendingRestarts++;
            MelonCoroutines.Start(DelayedWindReset(__instance));
        }

        private static IEnumerator DelayedWindReset(Il2Cpp.Wind wind)
        {
            // Önce Run()'ın tamamlanmasını bekle (max 15s)
            float waited = 0f;
            while (!SeamlessInteriorsMod.s_RunCompleted && waited < 15f)
            {
                yield return new WaitForSeconds(0.5f);
                waited += 0.5f;
            }

            if (!SeamlessInteriorsMod.s_RunCompleted)
            {
                s_pendingRestarts--;
                yield break;
            }

            // Sahne tam yüklenene kadar ekstra bekle
            yield return new WaitForSeconds(1f);

            s_pendingRestarts--;
            if (wind == null) yield break;
            if (wind.m_WindAudioForceStopped) yield break;

            // Oyuncu içerideyse Wind'i durdur ama sıfırlama yapma
            Transform playerT = GameManager.GetPlayerTransform();
            bool playerInside = playerT != null && SeamlessInteriorsMod.IsPositionInsideCabin(playerT.position);

            if (playerInside)
            {
                // İçerideyken Wind çalmamalı — ama yine de instance'ı düzelt
                // ki dışarı çıkınca Wind düzgün başlasın
                wind.m_WindLoopAudioInstance = 0;
                wind.m_WindAudioForceStopped = false;
                yield return null;
                yield return null;
                // Şimdi UpdateProceduralWind tetikledi, ID aldı
                // Ama içerideyiz, sesi kapat
                if (wind != null) wind.m_WindAudioForceStopped = true;
                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg("[WIND-FIX] Oyuncu icerde, Wind durduruldu.");
                yield break;
            }

            uint idBefore = wind.m_WindLoopAudioInstance;

            if (SeamlessInteriorsMod.s_DebugBounds)
                MelonLogger.Msg($"[WIND-FIX] 2.5s sonra reset basliyor. ID oncesi={idBefore} | InstanceID={wind.GetInstanceID()}");

            // m_WindLoopAudioInstance = 0 yaparak Wind'in kendi
            // UpdateProceduralWind döngüsünü "ses yok" moduna sok.
            // Bir sonraki frame'de UpdateProceduralWind bunu tespit edip
            // PlayProceduralWindAudio'yu sahne hazırken yeniden çağıracak.
            wind.m_WindLoopAudioInstance = 0;
            wind.m_WindAudioForceStopped = false;

            // 2 frame bekle, UpdateProceduralWind çalışsın
            yield return null;
            yield return null;

            if (wind == null) yield break;

            uint idAfter = wind.m_WindLoopAudioInstance;

            if (SeamlessInteriorsMod.s_DebugBounds)
                MelonLogger.Msg($"[WIND-FIX] Reset sonrasi ID={idAfter} | InstanceID={wind.GetInstanceID()}");

            // Hâlâ 0'sa UpdateProceduralWind tetiklenmedi —
            // manuel olarak PlayProceduralWindAudio çağır
            if (idAfter == 0)
            {
                try
                {
                    uint newId = wind.PlayProceduralWindAudio();
                    if (newId != 0)
                    {
                        wind.m_WindLoopAudioInstance = newId;
                        if (SeamlessInteriorsMod.s_DebugBounds)
                            MelonLogger.Msg($"[WIND-FIX] Manuel tetikleme: yeni ID={newId}");
                    }
                    else
                    {
                        if (SeamlessInteriorsMod.s_DebugBounds)
                            MelonLogger.Msg("[WIND-FIX] Manuel tetikleme de 0 dondu. Wwise emitter sorunlu.");
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[WIND-FIX] PlayProceduralWindAudio hatasi: {ex.Message}");
                }
            }
        }
    }
}
