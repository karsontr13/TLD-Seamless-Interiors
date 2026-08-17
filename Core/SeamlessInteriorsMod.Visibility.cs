using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── Başlangıç görünürlük senkronizasyonu ve Watchdog ───

        private void InitializeVisibilityAndWatchdog(SeamlessInteriorInstance instance)
        {
            PlayerManager pmInit = GameManager.GetPlayerManagerComponent();
            if (pmInit != null && pmInit.transform.position.sqrMagnitude > 1f)
            {
                ApplyInitialSyncState(instance, pmInit.transform.position);
            }
            else
            {
                ApplyInitialSyncState(instance);
            }

            MelonCoroutines.Start(DelayedInitialVisibilityCheck(instance));
            MelonCoroutines.Start(DelayedSaveLoadVisibilityFix(instance));

            if (!instance.WatchdogStarted)
            {
                instance.WatchdogStarted = true;
                MelonCoroutines.Start(VisibilityWatchdog(instance));
            }
        }

        public static void ApplyInitialSyncState(SeamlessInteriorInstance instance, Vector3? overridePos = null)
        {
            Vector3 pos;
            if (overridePos.HasValue) pos = overridePos.Value;
            else
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) return;
                pos = playerT.position;
            }

            if (instance.MasterInterior == null || instance.ExteriorShell == null) return;
            bool isInside = instance.IsPositionInside(pos);

            var uniStorm = GetCachedUniStorm();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null && instance.CustomKillers != null)
            {
                foreach (var pk in instance.CustomKillers)
                {
                    uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(pk);
                    if (isInside) uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Add(pk);
                }
            }

            if (isInside)
            {
                instance.MasterInterior.SetActive(true);
                instance.ExteriorShell.SetActive(false);
                SetInteriorItemsVisible(instance, true);

                foreach (var obj in instance.ResolvedExternalHiddenObjects) if (obj != null) obj.SetActive(false);
            }
            else
            {
                // DÜZELTME: Eşyaları gizleme işlemi MasterInterior kapanmadan ÖNCE yapılmalı!
                SetInteriorItemsVisible(instance, false);

                // SONRA MasterInterior kapatılmalı
                instance.MasterInterior.SetActive(false);
                instance.ExteriorShell.SetActive(true);

                foreach (var obj in instance.ResolvedExternalHiddenObjects) if (obj != null) obj.SetActive(true);
            }

            SetAudioOcclusion(instance, isInside);
        }

        public static void ApplyVisibilityState(SeamlessInteriorInstance instance, Vector3? overridePos = null)
        {
            Vector3 pos;
            if (overridePos.HasValue) pos = overridePos.Value;
            else
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) return;
                pos = playerT.position;
            }

            if (instance.MasterInterior == null || instance.ExteriorShell == null) return;

            bool isInside = instance.IsPositionInside(pos);
            var uniStorm = GetCachedUniStorm();

            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null && instance.CustomKillers != null && instance.CustomKillers.Count > 0)
            {
                // YENİ KOD: Sadece durum değiştiğinde listeye müdahale et
                bool isCurrentlyApplied = uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Contains(instance.CustomKillers[0]);

                if (isInside && !isCurrentlyApplied)
                {
                    foreach (var pk in instance.CustomKillers)
                        uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Add(pk);
                }
                else if (!isInside && isCurrentlyApplied)
                {
                    foreach (var pk in instance.CustomKillers)
                        uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(pk);
                }
            }
        }

        private IEnumerator DelayedInitialVisibilityCheck(SeamlessInteriorInstance instance)
        {
            yield return new WaitForSeconds(10f);
            if (!instance.RunCompleted) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT != null) ApplyInitialSyncState(instance, playerT.position);
        }

        // OPTİMİZASYON: Mesafe bazlı frekans - uzaktaki evler daha seyrek kontrol edilir
        private const float WATCHDOG_NEAR_DISTANCE = 80f;   // Bu mesafe içinde sık kontrol
        private const float WATCHDOG_FAR_DISTANCE = 200f;   // Bu mesafenin ötesinde çok seyrek
        private const float WATCHDOG_NEAR_INTERVAL = 0.5f;  // Yakın: 0.5s (eskisi 0.3s)
        private const float WATCHDOG_FAR_INTERVAL = 3.0f;   // Uzak: 3s
        private const float WATCHDOG_SKIP_INTERVAL = 8.0f;  // Çok uzak: 8s

        private IEnumerator VisibilityWatchdog(SeamlessInteriorInstance instance)
        {
            while (instance.RunCompleted)
            {
                bool suppressed = Time.time - s_LastPortalUseTime <= PORTAL_SUPPRESS_WINDOW;
                if (!suppressed)
                {
                    Transform playerT = GameManager.GetPlayerTransform();
                    float interval = WATCHDOG_NEAR_INTERVAL;

                    if (playerT != null)
                    {
                        float dist = Vector3.Distance(playerT.position, instance.Config.FallbackPosition);

                        if (dist > WATCHDOG_FAR_DISTANCE)
                        {
                            // Çok uzak: raycast bile yapma, sadece deaktif olduğundan emin ol
                            interval = WATCHDOG_SKIP_INTERVAL;
                            if (instance.MasterInterior != null && instance.MasterInterior.activeSelf)
                            {
                                SetInteriorItemsVisible(instance, false);
                                instance.MasterInterior.SetActive(false);
                                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(true);
                                foreach (var obj in instance.ResolvedExternalHiddenObjects)
                                    if (obj != null) obj.SetActive(true);
                                SetAudioOcclusion(instance, false);
                            }
                        }
                        else if (dist > WATCHDOG_NEAR_DISTANCE)
                        {
                            interval = WATCHDOG_FAR_INTERVAL;
                            ApplyVisibilityState(instance);
                        }
                        else
                        {
                            ApplyVisibilityState(instance);
                        }
                    }

                    yield return new WaitForSeconds(interval);
                }
                else
                {
                    yield return new WaitForSeconds(WATCHDOG_NEAR_INTERVAL);
                }
            }
        }

        // ─── Save/Load sonrası görünürlük düzeltmesi ───

        public static IEnumerator DelayedSaveLoadVisibilityFix(SeamlessInteriorInstance instance)
        {
            yield return new WaitForSeconds(0.1f);
            if (!instance.RunCompleted) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) yield break;

            bool isInside = instance.IsPositionInside(playerT.position);

            if (s_DebugBounds)
                MelonLogger.Msg($"[SAVE-LOAD-FIX] {instance.Config.InteriorSceneBaseName} isInside={isInside} | pos={playerT.position}");

            bool shellActiveWrong = instance.ExteriorShell != null && instance.ExteriorShell.activeSelf == isInside;
            bool interiorActiveWrong = instance.MasterInterior != null && instance.MasterInterior.activeSelf != isInside;

            if (shellActiveWrong || interiorActiveWrong)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[SAVE-LOAD-FIX] {instance.Config.InteriorSceneBaseName} Yanlis gorunum tespit edildi, duzeltiliyor.");
                ApplyInitialSyncState(instance, playerT.position);
            }

            if (isInside && instance.MasterInterior != null)
            {
                // Oyuncu zeminin altına düşmüş mü kontrol et - raycast ile doğru zemin bul
                Vector3 correctedPos = EnsureAboveGround(playerT.position, instance);
                float heightDiff = correctedPos.y - playerT.position.y;
                
                if (heightDiff > 0.1f)
                {
                    playerT.position = correctedPos;
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[SAVE-LOAD-FIX] Oyuncu Y duzeltildi (raycast): {playerT.position} (yukari: {heightDiff:F2}m)");
                }
            }
        }
    }
}
