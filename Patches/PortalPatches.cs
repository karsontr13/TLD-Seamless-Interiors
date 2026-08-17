using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace SeamlessInteriors
{
    [HarmonyLib.HarmonyPatch(typeof(LoadScene), nameof(LoadScene.PerformInteraction))]
    public class PortalMagicPatch
    {
        public static bool Prefix(LoadScene __instance)
        {
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null) return true;

            SeamlessInteriorInstance matchedInstance = null;
            bool isEntering = false;
            bool isExiting = false;

            // Kapı aktif binalardan birine ait mi kontrol et
            // ÖNEMLİ: RunCompleted kontrolünü KALDIRDIK. Henüz hazır olmayan instance'lar da eşleşmeli
            // yoksa oyun kapıyı orijinal sahne geçişi olarak işler.
            foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                bool isShellDoor = instance.ExteriorShell != null && __instance.transform.IsChildOf(instance.ExteriorShell.transform);
                bool isInteriorDoor = instance.MasterInterior != null && __instance.transform.IsChildOf(instance.MasterInterior.transform);

                if (isShellDoor || isInteriorDoor)
                {
                    matchedInstance = instance;

                    // Giriş: Kapının hedef sahnesi iç mekan sahnesi ise → giriş
                    if (__instance.m_SceneToLoad == instance.Config.InteriorSceneBaseName)
                    {
                        isEntering = true;
                    }
                    // Çıkış: Kapı iç mekanın (MasterInterior) child'ıysa ve giriş değilse → çıkış.
                    // NOT: m_SceneToLoad kontrolü KALDIRILDI. Klonlanan sahnelerin kapıları
                    // farklı bir bölgeye (ör. CoastalRegion) işaret edebilir ama biz
                    // oyuncuyu her zaman shell'in önüne spawn etmeliyiz.
                    else if (isInteriorDoor)
                    {
                        isExiting = true;
                    }

                    break;
                }
            }

            // Shell'e ait kapı bulundu ama henüz eşleşme yok?
            // Kapı tetikleyicileri (LoadScene/InteriorLoadTrigger) shell'in child'ı olmayabilir —
            // haritanın kendi root objesi olarak var olabilirler.
            // FallbackPosition'a yakınlık kontrolü ile bu kapıları doğru instance'a eşleştir.
            if (matchedInstance == null)
            {
                float bestDist = float.MaxValue;
                foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
                {
                    float distToFallback = Vector3.Distance(__instance.transform.position, instance.Config.FallbackPosition);
                    if (distToFallback < 30f && distToFallback < bestDist)
                    {
                        bestDist = distToFallback;
                        matchedInstance = instance;
                        isEntering = false;
                        isExiting = false;

                        if (__instance.m_SceneToLoad == instance.Config.InteriorSceneBaseName)
                            isEntering = true;
                        else if (instance.MasterInterior != null && __instance.transform.IsChildOf(instance.MasterInterior.transform))
                            isExiting = true;
                    }
                }
            }

            if (matchedInstance == null)
            {
                // Hangi sahnelerin geçici olarak açıldığını aklımızda tutalım
                List<SeamlessInteriorInstance> temporarilyActivated = new List<SeamlessInteriorInstance>();

                foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
                {
                    if (instance.MasterInterior != null && !instance.MasterInterior.activeSelf)
                    {
                        instance.MasterInterior.SetActive(true);
                        temporarilyActivated.Add(instance); // Açılanları listeye ekle
                        if (SeamlessInteriorsMod.s_DebugBounds)
                            MelonLogger.Msg($"[SAVE-FIX] {instance.Config.ResolvedInstanceId} orijinal ic mekana girilirken gecici olarak aktif edildi.");
                    }
                }

                try
                {
                    SeamlessInteriorsMod.SaveAllPlaceablePositions();
                    SeamlessInteriorsMod.SaveAllContainerData();
                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg("[PORTAL-SAVE] Orijinal ic mekana gecis oncesi Placeable pozisyonlari ve konteyner verileri kaydedildi.");
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[PORTAL-SAVE] Hata: {ex.Message}");
                }
                finally
                {
                    // DÜZELTME: Kayıt işlemi bittikten sonra, geçici açılan sahneleri tekrar GİZLE!
                    foreach (var instance in temporarilyActivated)
                    {
                        if (instance.MasterInterior != null)
                        {
                            instance.MasterInterior.SetActive(false);
                            if (SeamlessInteriorsMod.s_DebugBounds)
                                MelonLogger.Msg($"[SAVE-FIX] {instance.Config.ResolvedInstanceId} gecici aktiflik sonrasi tekrar kapatildi.");
                        }
                    }
                }

                return true;
            }

            // Eşleşme bulundu ama henüz hazır değil - orijinal sahne geçişini ENGELLE
            if (!matchedInstance.RunCompleted)
            {
                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg($"[PORTAL-BLOCK] {matchedInstance.Config.ResolvedInstanceId} henuz hazir degil, gecis engellendi.");
                return false; // Ne orijinal sahneye at, ne teleport yap. Sadece engelle.
            }

            if (SeamlessInteriorsMod.s_DebugBounds)
            {
                MelonLogger.Msg($"[DEBUG-PORTAL] Door: {__instance.gameObject.name} | Instance: {matchedInstance.Config.ResolvedInstanceId} | targetScene={__instance.m_SceneToLoad}");
            }

            SeamlessInteriorsMod.s_LastPortalUseTime = Time.time;

            if (isEntering)
            {
                if (matchedInstance.MasterInterior != null) matchedInstance.MasterInterior.SetActive(true);
                if (matchedInstance.ExteriorShell != null) matchedInstance.ExteriorShell.SetActive(false);

                foreach (var obj in matchedInstance.ResolvedExternalHiddenObjects) if (obj != null) obj.SetActive(false);

                SeamlessInteriorsMod.SetInteriorItemsVisible(matchedInstance, true);
                SeamlessInteriorsMod.SetAudioOcclusion(matchedInstance, true);

                Vector3 spawnPos = GetDoorEntryPosition(matchedInstance, __instance);
                spawnPos = SnapToGround(spawnPos);
                GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);

                return false;
            }

            if (isExiting)
            {
                Vector3 spawnPos = GetDoorExitPosition(matchedInstance, __instance);
                spawnPos = SnapToGround(spawnPos);
                GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);

                // DÜZELTME: Önce eşyaların görünürlüğünü kapatıyoruz
                SeamlessInteriorsMod.SetInteriorItemsVisible(matchedInstance, false);

                // SONRA MasterInterior'u deaktif ediyoruz
                if (matchedInstance.MasterInterior != null) matchedInstance.MasterInterior.SetActive(false);
                if (matchedInstance.ExteriorShell != null) matchedInstance.ExteriorShell.SetActive(true);

                foreach (var obj in matchedInstance.ResolvedExternalHiddenObjects) if (obj != null) obj.SetActive(true);

                SeamlessInteriorsMod.SetAudioOcclusion(matchedInstance, false);

                return false;
            }

            return true;
        }

        // Giriş pozisyonunu bulma
        private static Vector3 GetDoorEntryPosition(SeamlessInteriorInstance instance, LoadScene door)
        {
            Vector3 clickedDoorPos = door.transform.position;

            if (instance.Config.DoorSpawnPoints != null && instance.Config.DoorSpawnPoints.Count > 0)
            {
                DoorSpawnPoint closestDoor = null;
                float minDistance = float.MaxValue;

                foreach (var dsp in instance.Config.DoorSpawnPoints)
                {
                    // İSİM KONTROLÜNÜ TAMAMEN KALDIRDIK. 
                    // The Long Dark'ta tıklanan tetikleyicinin adı genelde prefab adından farklıdır.
                    float dist = Vector2.Distance(new Vector2(clickedDoorPos.x, clickedDoorPos.z), new Vector2(dsp.DoorTransformPosition.x, dsp.DoorTransformPosition.z));

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestDoor = dsp;
                    }
                }

                // 15 metre gibi geniş bir tolerans koyuyoruz. En yakın kapıyı kesinlikle bulacaktır.
                if (closestDoor != null && minDistance < 15.0f)
                {
                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg($"[DEBUG-DOOR] GIRIS BASARILI - Tiklanan Obj: {door.gameObject.name}, Eslesen Kapi: {closestDoor.DoorName}, Mesafe: {minDistance}");

                    return closestDoor.EntryPosition;
                }
            }

            // Fallback
            if (instance.Config.EntrySpawnPosition != Vector3.zero) return instance.Config.EntrySpawnPosition;
            Transform sp = door.transform.Find("SpawnPoint");
            return sp != null ? sp.position : door.transform.position;
        }

        // Çıkış pozisyonunu bulma
        private static Vector3 GetDoorExitPosition(SeamlessInteriorInstance instance, LoadScene door)
        {
            Vector3 clickedDoorPos = door.transform.position;

            if (instance.Config.DoorSpawnPoints != null && instance.Config.DoorSpawnPoints.Count > 0)
            {
                DoorSpawnPoint closestDoor = null;
                float minDistance = float.MaxValue;

                foreach (var dsp in instance.Config.DoorSpawnPoints)
                {
                    // DİKKAT: Çıkış yaparken oyuncu İÇERİDEDİR.
                    // Bu yüzden tıklanan kapının konumunu, dışarıdaki 'DoorTransformPosition' ile değil,
                    // içerideki 'EntryPosition' ile karşılaştırıyoruz.
                    float dist = Vector2.Distance(new Vector2(clickedDoorPos.x, clickedDoorPos.z), new Vector2(dsp.EntryPosition.x, dsp.EntryPosition.z));

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestDoor = dsp;
                    }
                }

                if (closestDoor != null && minDistance < 15.0f)
                {
                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg($"[DEBUG-DOOR] CIKIS BASARILI - Tiklanan Obj: {door.gameObject.name}, Eslesen Kapi: {closestDoor.DoorName}, Mesafe: {minDistance}");

                    return closestDoor.ExitPosition;
                }
            }

            // Fallback
            if (instance.Config.ExitSpawnPosition != Vector3.zero) return instance.Config.ExitSpawnPosition;
            Transform sp = door.transform.Find("SpawnPoint");
            return sp != null ? sp.position : door.transform.position;
        }

        /// <summary>
        /// Spawn noktasından aşağı kısa bir raycast atar.
        /// Yakın zemin bulursa oyuncuyu oraya yapıştırır (düşme efekti yok).
        /// Bulamazsa koordinatı olduğu gibi bırakır.
        /// NOT: Yukarıdan uzun raycast YAPMA — çatı collider'larına çarpar ve
        /// oyuncuyu çatıda spawn eder. Sadece ayak seviyesinden aşağı bak.
        /// </summary>
        private static Vector3 SnapToGround(Vector3 pos)
        {
            Vector3 rayOrigin = pos + Vector3.up * 0.5f;
            float maxDrop = 2.5f;

            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, maxDrop + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            RaycastHit? bestHit = null;
            float bestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.isTrigger) continue;
                if (hit.collider.transform.root.name.Contains("CHARACTER_FPSPlayer")) continue;

                if (hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    bestHit = hit;
                }
            }

            if (bestHit.HasValue)
                return new Vector3(pos.x, bestHit.Value.point.y + 0.05f, pos.z);

            return pos;
        }
    }
}