using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        private void SetupWeatherAndParticles(Bounds localBounds)
        {
            GameObject particleKillerObj = new GameObject("ParticleKiller");
            particleKillerObj.transform.SetParent(s_MasterInterior.transform, false);
            particleKillerObj.transform.localPosition = Vector3.zero;
            particleKillerObj.transform.localRotation = Quaternion.identity;
            particleKillerObj.layer = LayerMask.NameToLayer("TriggerIgnoreRaycast");

            BoxCollider triggerBox = particleKillerObj.AddComponent<BoxCollider>();
            triggerBox.isTrigger = true;
            triggerBox.center = localBounds.center;
            triggerBox.size = localBounds.size;
            s_InteriorTrigger = triggerBox;

            s_CustomKillers.Clear();

            var uniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
            {
                int sliceCount = 6;
                float sliceZ = localBounds.size.z / sliceCount;
                float startZ = localBounds.center.z - (localBounds.size.z / 2f) + (sliceZ / 2f);

                for (int i = 0; i < sliceCount; i++)
                {
                    var pki = new Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance();
                    pki.m_OwnerGameObject = particleKillerObj;
                    pki.m_KillsFallingSnow = true;
                    pki.m_KillsBlowingSnow = true;

                    Vector3 sliceLocalCenter = new Vector3(localBounds.center.x, localBounds.center.y, startZ + (i * sliceZ));
                    Vector3 sliceExtents = new Vector3(localBounds.size.x / 2f, localBounds.size.y / 2f, sliceZ / 2f);

                    Vector3[] corners = new Vector3[8] {
                        sliceLocalCenter + new Vector3( sliceExtents.x,  sliceExtents.y,  sliceExtents.z),
                        sliceLocalCenter + new Vector3( sliceExtents.x,  sliceExtents.y, -sliceExtents.z),
                        sliceLocalCenter + new Vector3( sliceExtents.x, -sliceExtents.y,  sliceExtents.z),
                        sliceLocalCenter + new Vector3( sliceExtents.x, -sliceExtents.y, -sliceExtents.z),
                        sliceLocalCenter + new Vector3(-sliceExtents.x,  sliceExtents.y,  sliceExtents.z),
                        sliceLocalCenter + new Vector3(-sliceExtents.x,  sliceExtents.y, -sliceExtents.z),
                        sliceLocalCenter + new Vector3(-sliceExtents.x, -sliceExtents.y,  sliceExtents.z),
                        sliceLocalCenter + new Vector3(-sliceExtents.x, -sliceExtents.y, -sliceExtents.z)
                    };

                    Vector3 min = particleKillerObj.transform.TransformPoint(corners[0]);
                    Vector3 max = min;
                    for (int j = 1; j < 8; j++)
                    {
                        Vector3 wp = particleKillerObj.transform.TransformPoint(corners[j]);
                        min = Vector3.Min(min, wp);
                        max = Vector3.Max(max, wp);
                    }

                    Bounds sliceAABB = new Bounds();
                    sliceAABB.SetMinMax(min, max);
                    sliceAABB.Expand(0.2f);

                    pki.m_Bounds = sliceAABB;
                    s_CustomKillers.Add(pki);
                }
            }

            IndoorSpaceTrigger spaceTrigger = particleKillerObj.AddComponent<IndoorSpaceTrigger>();
            spaceTrigger.m_UseOutdoorLighting = true;
            spaceTrigger.m_UseOutdoorTemperature = false;
            spaceTrigger.m_AllowCampfires = true;
            spaceTrigger.m_TemperatureDeltaCelsius = 25f;
            spaceTrigger.m_ValidSafehouse = true;
            spaceTrigger.m_DontCountAsInterior = true;
            spaceTrigger.m_IgnoreCabinFever = false;
            spaceTrigger.m_TriggerID = "CustomCampOffice_Trigger";
        }

        private void InitializeVisibilityAndWatchdog()
        {
            PlayerManager pmInit = GameManager.GetPlayerManagerComponent();
            if (pmInit != null && pmInit.transform.position.sqrMagnitude > 1f)
            {
                ApplyInitialSyncState(pmInit.transform.position);
            }
            else
            {
                ApplyInitialSyncState();
            }

            MelonCoroutines.Start(DelayedInitialVisibilityCheck());
            // ↓ YENİ SATIR
            MelonCoroutines.Start(DelayedSaveLoadVisibilityFix());

            if (!s_WatchdogStarted)
            {
                s_WatchdogStarted = true;
                MelonCoroutines.Start(VisibilityWatchdog());
            }
        }

        private static float s_lastCabinCheckLog = -999f;

        // Oyuncunun portal/watchdog kararı için — daraltılmış bounds (duvar geçişini önler)
        public static bool IsPositionInsideCabin(Vector3 pos)
        {
            if (s_MasterInterior == null || s_InteriorTrigger == null) return false;
            Vector3 localPos = s_MasterInterior.transform.InverseTransformPoint(pos);

            float shrink = 1.5f;
            Bounds b = new Bounds(s_InteriorTrigger.center, s_InteriorTrigger.size);

            bool insideX = localPos.x >= b.min.x + shrink && localPos.x <= b.max.x - shrink;
            bool insideZ = localPos.z >= b.min.z + shrink && localPos.z <= b.max.z - shrink;
            bool insideY = localPos.y >= b.min.y - 2f && localPos.y <= b.max.y;
            bool result = insideX && insideY && insideZ;

            if (s_DebugBounds && Time.time - s_lastCabinCheckLog > 5f)
            {
                s_lastCabinCheckLog = Time.time;
                MelonLogger.Msg($"[CABIN-CHECK] worldPos={pos} localPos={localPos} boundsCenter={b.center} boundsSize={b.size} isInside={result}");
            }
            return result;
        }

        // Gear gizleme/gösterme için — orijinal tam bounds (shrink yok)
        public static bool IsPositionInsideCabinFull(Vector3 pos)
        {
            if (s_MasterInterior == null || s_InteriorTrigger == null) return false;
            Vector3 localPos = s_MasterInterior.transform.InverseTransformPoint(pos);
            Bounds b = new Bounds(s_InteriorTrigger.center, s_InteriorTrigger.size);
            return b.Contains(localPos);
        }

        public static void ApplyInitialSyncState(Vector3? overridePos = null)
        {
            Vector3 pos;
            if (overridePos.HasValue) pos = overridePos.Value;
            else
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) return;
                pos = playerT.position;
            }

            if (s_MasterInterior == null || s_ExteriorShell == null) return;
            bool isInside = IsPositionInsideCabin(pos);

            var uniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null && s_CustomKillers != null)
            {
                foreach (var pk in s_CustomKillers)
                {
                    uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(pk);
                    if (isInside) uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Add(pk);
                }
            }

            if (isInside)
            {
                s_MasterInterior.SetActive(true);
                s_ExteriorShell.SetActive(false);
                SetInteriorItemsVisible(true);
            }
            else
            {
                s_MasterInterior.SetActive(false);
                s_ExteriorShell.SetActive(true);
                SetInteriorItemsVisible(false);
            }

            SetAudioOcclusion(isInside);
        }

        public static void ApplyVisibilityState(Vector3? overridePos = null)
        {
            Vector3 pos;
            if (overridePos.HasValue) pos = overridePos.Value;
            else
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) return;
                pos = playerT.position;
            }

            if (s_MasterInterior == null || s_ExteriorShell == null) return;

            bool isInside = IsPositionInsideCabin(pos);
            var uniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();

            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null && s_CustomKillers != null)
            {
                foreach (var pk in s_CustomKillers)
                {
                    uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(pk);
                    if (isInside) uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Add(pk);
                }
            }
        }

        private IEnumerator DelayedInitialVisibilityCheck()
        {
            yield return new WaitForSeconds(10f);
            if (!s_RunCompleted) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT != null) ApplyInitialSyncState(playerT.position);
        }

        private IEnumerator VisibilityWatchdog()
        {
            while (s_RunCompleted)
            {
                bool suppressed = Time.time - s_LastPortalUseTime <= PORTAL_SUPPRESS_WINDOW;
                if (!suppressed) ApplyVisibilityState();
                yield return new WaitForSeconds(0.3f);
            }
        }

        private void ResetWeatherParticles()
        {
            if (s_ParticleKiller != null)
            {
                var uniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
                if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
                {
                    uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(s_ParticleKiller);
                }
                s_ParticleKiller = null;
            }
        }
    }
}