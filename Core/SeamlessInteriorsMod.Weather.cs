using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Margin added to the real interior bounds when building the ParticleKiller
        // trigger. 3m extra on Y (stops snow leaking through sloped/curved roofs),
        // 1m on XZ.
        // NOTE: SeamlessInteriorInstance.TRIGGER_EXPANSION is the inverse of this
        // value - the two must be changed together.
        private static readonly Vector3 TRIGGER_PADDING = new Vector3(1.0f, 3.0f, 1.0f);

        private static void BuildParticleKillerSlices(SeamlessInteriorInstance instance,
                                                      GameObject particleKillerObj,
                                                      Bounds expandedBounds)
        {
            instance.CustomKillers.Clear();

            var uniStorm = GetCachedUniStorm();
            if (uniStorm == null || uniStorm.m_WeatherParticleManager == null) return;

            // Slice on both Z and X to form a 2D grid. A single axis is not enough
            // for wide or irregular buildings (e.g. the hangar).
            int sliceCountZ = 6;
            int sliceCountX = Mathf.Max(1, Mathf.CeilToInt(expandedBounds.size.x / expandedBounds.size.z * sliceCountZ));
            if (sliceCountX > 6) sliceCountX = 6;   // cap the slice count

            float sliceZ = expandedBounds.size.z / sliceCountZ;
            float sliceX = expandedBounds.size.x / sliceCountX;
            float startZ = expandedBounds.center.z - (expandedBounds.size.z / 2f) + (sliceZ / 2f);
            float startX = expandedBounds.center.x - (expandedBounds.size.x / 2f) + (sliceX / 2f);

            Transform killerT = particleKillerObj.transform;

            for (int ix = 0; ix < sliceCountX; ix++)
            {
                for (int iz = 0; iz < sliceCountZ; iz++)
                {
                    var pki = new Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance();
                    pki.m_OwnerGameObject = particleKillerObj;
                    pki.m_KillsFallingSnow = true;
                    pki.m_KillsBlowingSnow = true;

                    Vector3 c = new Vector3(startX + (ix * sliceX), expandedBounds.center.y, startZ + (iz * sliceZ));
                    Vector3 e = new Vector3(sliceX / 2f, expandedBounds.size.y / 2f, sliceZ / 2f);

                    // Killers take world-space AABBs, so transform the eight corners
                    // and take their axis-aligned envelope.
                    Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 corner = c + new Vector3(
                            ((k & 1) == 0) ? e.x : -e.x,
                            ((k & 2) == 0) ? e.y : -e.y,
                            ((k & 4) == 0) ? e.z : -e.z);

                        Vector3 wp = killerT.TransformPoint(corner);
                        min = Vector3.Min(min, wp);
                        max = Vector3.Max(max, wp);
                    }

                    Bounds sliceAABB = new Bounds();
                    sliceAABB.SetMinMax(min, max);
                    sliceAABB.Expand(0.5f);   // small overlap so no gap forms between slices

                    pki.m_Bounds = sliceAABB;
                    instance.CustomKillers.Add(pki);
                }
            }
        }

        // Rebuilds only the particle killers for an instance whose trigger object
        // already exists (used after a reload).
        private void SetupWeatherParticleKillersOnly(SeamlessInteriorInstance instance, Bounds localBounds)
        {
            if (instance.MasterInterior == null) return;

            var particleKillerT = instance.MasterInterior.transform.Find("ParticleKiller");
            if (particleKillerT == null) return;

            Bounds expandedBounds = localBounds;
            expandedBounds.Expand(TRIGGER_PADDING);

            BuildParticleKillerSlices(instance, particleKillerT.gameObject, expandedBounds);
        }

        // First-time setup: creates the ParticleKiller object, its trigger volume,
        // the particle killer slices and the IndoorSpaceTrigger that makes the game
        // treat the clone as an indoor space.
        private void SetupWeatherAndParticles(SeamlessInteriorInstance instance, Bounds localBounds)
        {
            Bounds expandedBounds = localBounds;
            expandedBounds.Expand(TRIGGER_PADDING);

            GameObject particleKillerObj = new GameObject("ParticleKiller");
            particleKillerObj.transform.SetParent(instance.MasterInterior.transform, false);
            particleKillerObj.transform.localPosition = Vector3.zero;
            particleKillerObj.transform.localRotation = Quaternion.identity;
            particleKillerObj.layer = LayerMask.NameToLayer("TriggerIgnoreRaycast");

            // Trigger volume covering the whole interior; also used as the safehouse
            // collider and for inside/outside tests.
            BoxCollider triggerBox = particleKillerObj.AddComponent<BoxCollider>();
            triggerBox.isTrigger = true;
            triggerBox.center = expandedBounds.center;
            triggerBox.size = expandedBounds.size;
            instance.InteriorTrigger = triggerBox;

            BuildParticleKillerSlices(instance, particleKillerObj, expandedBounds);

            IndoorSpaceTrigger spaceTrigger = particleKillerObj.AddComponent<IndoorSpaceTrigger>();
            // Outdoor mode: keep using outdoor lighting (the existing behaviour).
            // Dark mode: let the interior light itself.
            spaceTrigger.m_UseOutdoorLighting = !IsDarkAtmosphereMode;
            spaceTrigger.m_UseOutdoorTemperature = false;
            spaceTrigger.m_AllowCampfires = true;
            spaceTrigger.m_TemperatureDeltaCelsius = 10f;
            spaceTrigger.m_ValidSafehouse = true;
            spaceTrigger.m_DontCountAsInterior = true;
            spaceTrigger.m_IgnoreCabinFever = false;
            spaceTrigger.m_TriggerID = $"Custom_{instance.Config.InteriorSceneBaseName}_Trigger";
        }

        // Unregisters an instance's particle killer from the weather manager.
        public static void ResetWeatherParticles(SeamlessInteriorInstance instance)
        {
            if (instance.ParticleKiller != null)
            {
                var uniStorm = GetCachedUniStorm();
                if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
                {
                    uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(instance.ParticleKiller);
                }
                instance.ParticleKiller = null;
            }
        }

        // ─── Wind audio loop restart ───

        public static void ForceRestartWindAudio(Il2Cpp.Wind wind, string tag)
        {
            if (wind == null) return;

            uint before = wind.m_WindLoopAudioInstance;

            // Drop the stale / dead instance.
            wind.ForceStopAudioLoop();

            // ForceStopAudioLoop may have set the flag - clear it again.
            wind.m_WindAudioForceStopped = false;
            wind.m_WindLoopAudioInstance = 0;

            // Restart and STORE THE RETURNED ID (the step the old code skipped).
            wind.m_WindLoopAudioInstance = wind.PlayProceduralWindAudio();

            MelonLogger.Msg($"[WIND-RESTART:{tag}] loop id {before} -> {wind.m_WindLoopAudioInstance}, " +
                            $"forceStopped={wind.m_WindAudioForceStopped}, occl=({DescribeAudioOcclusion()})");
        }

        public static IEnumerator RestoreWindAudioAfterLoad()
        {
            // Let Wwise and the scene settle.
            yield return new WaitForSeconds(1.5f);

            var winds = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Wind>();
            if (winds == null || winds.Length == 0)
            {
                MelonLogger.Msg("[WIND-RESTORE] Sahnede Wind bulunamadi.");
                yield break;
            }

            MelonLogger.Msg($"[WIND-RESTORE] {winds.Length} Wind bulundu, insideClone={s_IsPlayerInsideClone}, occl=({DescribeAudioOcclusion()})");

            foreach (var w in winds)
            {
                if (w == null) continue;
                ForceRestartWindAudio(w, "after-load");
            }

            // Diagnostics: watch for the state degrading over the next 10 seconds.
            // Verbose logging only - doing 5 FindObjectOfType + log passes after
            // every load has no value in normal play.
            if (!s_DebugBounds) yield break;

            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForSeconds(2f);
                var w0 = UnityEngine.Object.FindObjectOfType<Il2Cpp.Wind>();
                if (w0 == null) continue;
                MelonLogger.Msg($"[WIND-WATCH t+{(i + 1) * 2}s] id={w0.m_WindLoopAudioInstance} " +
                                $"forceStopped={w0.m_WindAudioForceStopped} occluded={w0.m_PlayerOccluded} " +
                                $"mph={w0.GetSpeedMPH():F1} occl=({DescribeAudioOcclusion()})");
            }
        }

        // ─── Wind fix-up (after Run) ───

        private IEnumerator FixWindAfterRun(SeamlessInteriorInstance instance)
        {
            // Give the wind system a few frames to stabilise.
            yield return new WaitForSeconds(2f);

            var wind = UnityEngine.Object.FindObjectOfType<Il2Cpp.Wind>();
            if (wind == null) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) yield break;

            bool playerInside = s_IsPlayerInsideClone;

            // Fallback: if the flag was wrongly left false, trust the saved state.
            if (!playerInside)
            {
                string savedId = GetSavedPlayerInsideInstanceId();
                if (!string.IsNullOrEmpty(savedId))
                {
                    playerInside = true;
                    s_IsPlayerInsideClone = true;
                    SetAudioOcclusion(true);
                    if (s_DebugBounds) MelonLogger.Msg("[WIND-POST-RUN] Flag false ama saved state icerde, duzeltildi.");
                }
            }

            // Outdoors: DO NOT touch wind, the game's own system is in charge.
            if (!playerInside)
            {
                if (s_DebugBounds) MelonLogger.Msg("[WIND-POST-RUN] Oyuncu disarida, wind'e dokunulmadi.");
                yield break;
            }

            // Indoors: clear ForceStopped, audio occlusion lowers the volume.
            wind.m_WindAudioForceStopped = false;

            // Force a restart if the loop never started.
            // NOTE: a non-zero id does not prove the Wwise instance is alive; the real
            // clean restart happens in RestoreWindAudioAfterLoad.
            if (wind.m_WindLoopAudioInstance == 0)
            {
                ForceRestartWindAudio(wind, "post-run");
            }

            if (s_DebugBounds) MelonLogger.Msg($"[WIND-POST-RUN] Oyuncu icerde, wind ForceStopped=false, id={wind.m_WindLoopAudioInstance}");
        }
    }
}
