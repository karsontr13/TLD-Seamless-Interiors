using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        private void SetupWeatherParticleKillersOnly(SeamlessInteriorInstance instance, Bounds localBounds)
        {
            if (instance.MasterInterior == null) return;

            var particleKillerT = instance.MasterInterior.transform.Find("ParticleKiller");
            if (particleKillerT == null) return;

            GameObject particleKillerObj = particleKillerT.gameObject;
            instance.CustomKillers.Clear();

            // ponytail: Aynı expand mantığı - çatıdan kar sızmasını önle
            Bounds expandedBounds = localBounds;
            expandedBounds.Expand(new Vector3(1.0f, 3.0f, 1.0f));

            var uniStorm = GetCachedUniStorm();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
            {
                int sliceCountZ = 6;
                int sliceCountX = Mathf.Max(1, Mathf.CeilToInt(expandedBounds.size.x / expandedBounds.size.z * sliceCountZ));
                if (sliceCountX > 6) sliceCountX = 6;

                float sliceZ = expandedBounds.size.z / sliceCountZ;
                float sliceX = expandedBounds.size.x / sliceCountX;
                float startZ = expandedBounds.center.z - (expandedBounds.size.z / 2f) + (sliceZ / 2f);
                float startX = expandedBounds.center.x - (expandedBounds.size.x / 2f) + (sliceX / 2f);

                for (int ix = 0; ix < sliceCountX; ix++)
                {
                    for (int iz = 0; iz < sliceCountZ; iz++)
                    {
                        var pki = new Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance();
                        pki.m_OwnerGameObject = particleKillerObj;
                        pki.m_KillsFallingSnow = true;
                        pki.m_KillsBlowingSnow = true;

                        Vector3 sliceLocalCenter = new Vector3(startX + (ix * sliceX), expandedBounds.center.y, startZ + (iz * sliceZ));
                        Vector3 sliceExtents = new Vector3(sliceX / 2f, expandedBounds.size.y / 2f, sliceZ / 2f);

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
                        sliceAABB.Expand(0.5f);

                        pki.m_Bounds = sliceAABB;
                        instance.CustomKillers.Add(pki);
                    }
                }
            }
        }

        private void SetupWeatherAndParticles(SeamlessInteriorInstance instance, Bounds localBounds)
        {
            // ponytail: Bounds'u üstten ekstra genişlet - eğimli/kavisli çatılardan kar sızmasını önler
            // Hangar gibi yüksek yapılarda çatı renderer'ları bounds hesabına tam girmeyebilir
            Bounds expandedBounds = localBounds;
            expandedBounds.Expand(new Vector3(1.0f, 3.0f, 1.0f)); // Y'de 3m ekstra (çatı koruma), XZ'de 1m

            GameObject particleKillerObj = new GameObject("ParticleKiller");
            particleKillerObj.transform.SetParent(instance.MasterInterior.transform, false);
            particleKillerObj.transform.localPosition = Vector3.zero;
            particleKillerObj.transform.localRotation = Quaternion.identity;
            particleKillerObj.layer = LayerMask.NameToLayer("TriggerIgnoreRaycast");

            BoxCollider triggerBox = particleKillerObj.AddComponent<BoxCollider>();
            triggerBox.isTrigger = true;
            triggerBox.center = expandedBounds.center;
            triggerBox.size = expandedBounds.size;
            instance.InteriorTrigger = triggerBox;

            instance.CustomKillers.Clear();

            var uniStorm = GetCachedUniStorm();
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
            {
                // ponytail: Hem Z hem X ekseninde slice yaparak 2D grid oluştur
                // Geniş/karmaşık yapılarda (hangar) tek eksen yetersiz kalıyor
                int sliceCountZ = 6;
                int sliceCountX = Mathf.Max(1, Mathf.CeilToInt(expandedBounds.size.x / expandedBounds.size.z * sliceCountZ));
                // Çok fazla slice üretmemek için sınırla
                if (sliceCountX > 6) sliceCountX = 6;

                float sliceZ = expandedBounds.size.z / sliceCountZ;
                float sliceX = expandedBounds.size.x / sliceCountX;
                float startZ = expandedBounds.center.z - (expandedBounds.size.z / 2f) + (sliceZ / 2f);
                float startX = expandedBounds.center.x - (expandedBounds.size.x / 2f) + (sliceX / 2f);

                for (int ix = 0; ix < sliceCountX; ix++)
                {
                    for (int iz = 0; iz < sliceCountZ; iz++)
                    {
                        var pki = new Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance();
                        pki.m_OwnerGameObject = particleKillerObj;
                        pki.m_KillsFallingSnow = true;
                        pki.m_KillsBlowingSnow = true;

                        Vector3 sliceLocalCenter = new Vector3(startX + (ix * sliceX), expandedBounds.center.y, startZ + (iz * sliceZ));
                        Vector3 sliceExtents = new Vector3(sliceX / 2f, expandedBounds.size.y / 2f, sliceZ / 2f);

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
                        sliceAABB.Expand(0.5f);

                        pki.m_Bounds = sliceAABB;
                        instance.CustomKillers.Add(pki);
                    }
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
            spaceTrigger.m_TriggerID = $"Custom_{instance.Config.InteriorSceneBaseName}_Trigger";
        }

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

        // ─── Wind düzeltmesi (Run sonrası) ───

        private IEnumerator FixWindAfterRun(SeamlessInteriorInstance instance)
        {
            // Wind sisteminin stabilize olması için birkaç frame bekle
            yield return new WaitForSeconds(2f);

            var wind = UnityEngine.Object.FindObjectOfType<Il2Cpp.Wind>();
            if (wind == null) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) yield break;

            bool playerInside = IsPositionInsideAnyInstance(playerT.position);

            if (playerInside)
            {
                // Oyuncu içeride: Wind'i durdur
                wind.m_WindLoopAudioInstance = 0;
                wind.m_WindAudioForceStopped = false;
                yield return null;
                yield return null;
                if (wind != null) wind.m_WindAudioForceStopped = true;
                if (s_DebugBounds) MelonLogger.Msg("[WIND-POST-RUN] Oyuncu icerde, Wind durduruldu.");
            }
            else
            {
                // Oyuncu dışarıda: Wind sesini yeniden başlat
                wind.m_WindLoopAudioInstance = 0;
                wind.m_WindAudioForceStopped = false;
                yield return null;
                yield return null;
                if (wind == null) yield break;

                if (wind.m_WindLoopAudioInstance == 0)
                {
                    try
                    {
                        uint newId = wind.PlayProceduralWindAudio();
                        if (newId != 0) wind.m_WindLoopAudioInstance = newId;
                        if (s_DebugBounds) MelonLogger.Msg($"[WIND-POST-RUN] Wind yeniden baslatildi, id={newId}");
                    }
                    catch { }
                }
            }
        }
    }
}
