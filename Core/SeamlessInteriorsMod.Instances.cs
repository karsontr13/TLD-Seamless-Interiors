using Il2CppTLD.WeatherParticle;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SeamlessInteriors
{
    // Birden fazla kapılı evler için kapı bazlı spawn koordinatları
    public class DoorSpawnPoint
    {
        public string DoorName;        // Kapı objesinin adı (veya adının içerdiği kısım)
        public Vector3 DoorTransformPosition;
        public Vector3 EntryPosition;  // Bu kapıdan girince oyuncunun spawn edileceği iç mekan koordinatı
        public Vector3 ExitPosition;   // Bu kapıdan çıkınca oyuncunun spawn edileceği dış mekan koordinatı
    }

    // İç mekanların sabit ayarlarını tutacak veri sınıfı
    public class InteriorConfig
    {
        // BENZERSİZ KİMLİK: Aynı sahne/shell'e sahip birden fazla bina olabilir.
        // Her config'in kendine özgü bir ID'si olmalı.
        // Boş bırakılırsa InteriorSceneBaseName kullanılır (geriye uyumluluk).
        public string InstanceId;

        // Otomatik: InstanceId set edilmişse onu, yoksa InteriorSceneBaseName'i döndürür
        public string ResolvedInstanceId => string.IsNullOrEmpty(InstanceId) ? InteriorSceneBaseName : InstanceId;

        // DIŞARIDA KALAN VE İÇ MEKANA TAŞAN OBJELERİN İSİMLERİ VEYA PREFAB ADLARI
        public List<string> ExternalObjectsToHide = new List<string>();
        public string ExteriorSceneName;
        public string InteriorSceneBaseName;
        public string ExteriorShellPrefabName;
        public float YOffset;
        public Vector3 ScaleAdjustment;
        public Vector3 FallbackPosition;

        // YENİ EKLENENLER:
        public List<string> ObjectsToDestroy = new List<string>();
        public List<string> ObjectsToDisable = new List<string>();
        public Vector3 EntrySpawnPosition;  // Tek kapılı evler için fallback
        public Vector3 ExitSpawnPosition;   // Tek kapılı evler için fallback

        // Birden fazla kapılı evler için kapı bazlı spawn noktaları
        public List<DoorSpawnPoint> DoorSpawnPoints = new List<DoorSpawnPoint>();

        // DÖNÜŞ AYARI İÇİN YENİ EKLENDİ:
        public Vector3 RotationOffset;

        // POZİSYON SABİTLEME İÇİN YENİ EKLENDİ:
        public bool ForceExactPosition;

        // SaveKeyPrefix artık InstanceId bazlı (aynı sahneye sahip farklı binalar çakışmaz)
        public string SaveKeyPrefix => $"{ResolvedInstanceId}Gen_";
    }

    // Her binanın oyun içindeki aktif durumunu yönetecek sınıf
    public class SeamlessInteriorInstance
    {
        // OYUN İÇİNDE BULUNUP EŞLEŞTİRİLMİŞ DIŞ MEKAN OBJELERİ
        public List<GameObject> ResolvedExternalHiddenObjects = new List<GameObject>();
        public InteriorConfig Config { get; private set; }

        public bool RunCompleted = false;
        public bool IsCloningRoutineActive = false;
        public bool InteriorPersisted = false;

        public GameObject ExteriorShell = null;
        public GameObject MasterInterior = null;
        public BoxCollider InteriorTrigger = null;

        public List<WeatherParticleManager.ParticleKillerInstance> CustomKillers = new List<WeatherParticleManager.ParticleKillerInstance>();
        public WeatherParticleManager.ParticleKillerInstance ParticleKiller = null;

        public bool WatchdogStarted = false;
        public bool IsAudioOccluded = false;

        public SeamlessInteriorInstance(InteriorConfig config)
        {
            Config = config;
        }

        public bool IsPositionInside(Vector3 pos)
        {
            if (MasterInterior == null) return false;

            // MasterInterior deaktifse collider'lar çalışmaz, raycast yanıltıcı sonuç verir
            // Deaktif MasterInterior = oyuncu dışarıda demektir
            if (!MasterInterior.activeSelf) return false;

            // DÜZELTME: Oyun load sırasında oyuncuyu terrain'e (yerin dibine) yapıştırdığı için 
            // ışınların evin zeminini ıskalamaması adına başlangıç noktasını 2.5 metre yukarı çekiyoruz.
            Vector3 rayOrigin = pos + (Vector3.up * 2.5f);

            // 1. TAVAN KONTROLU (Yukarı doğru ışın)
            bool roofHit = CheckDirectionForInterior(rayOrigin, Vector3.up, 30f);

            // 2. ZEMIN KONTROLU (Aşağı doğru ışın)
            bool floorHit = CheckDirectionForInterior(rayOrigin, Vector3.down, 30f);

            // Eğer tepemizde evin bir çatısı/tavanı, altımızda da evin bir zemini varsa KESİNLİKLE içerideyizdir.
            if (roofHit && floorHit) return true;

            // FALLBACK: Raycast başarısız olabilir (eğimli çatı, ince collider, büyük hangar yapıları vb.)
            // InteriorTrigger bounds'u içindeyse yine "içeride" say.
            // Bu, hangar gibi karmaşık yapılarda raycast ıskalamalarını telafi eder.
            if (InteriorTrigger != null)
            {
                // InteriorTrigger local-space bounds'unu world-space'e çevirip kontrol et
                Vector3 localPos = InteriorTrigger.transform.InverseTransformPoint(pos);
                Bounds localBounds = new Bounds(InteriorTrigger.center, InteriorTrigger.size);
                // Küçük bir shrink uygula ki kapı eşiklerinde yanlış pozitif olmasın
                localBounds.Expand(-0.5f);
                if (localBounds.Contains(localPos))
                    return true;
            }

            return false;
        }

        private bool CheckDirectionForInterior(Vector3 origin, Vector3 direction, float maxDistance)
        {
            // QueryTriggerInteraction.Ignore: Görünmez tetikleyicilere çarpmamak için
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            // Çarpışmaları yakından uzağa doğru sırala
            var sortedHits = hits.OrderBy(h => h.distance).ToArray();

            foreach (var hit in sortedHits)
            {
                // 1. Oyuncunun kendi bedenini (Kapsül collider'a) çarparsa yoksay
                if (hit.collider.transform.root.name.Contains("CHARACTER_FPSPlayer"))
                    continue;

                // 2. Işın dış kabuğa (ExteriorShell) çarparsa delip geç
                if (ExteriorShell != null && hit.collider.transform.IsChildOf(ExteriorShell.transform))
                    continue;

                // 3. Işın yere atılmış bir GearItem veya Placeable (Mobilya) objesine çarparsa delip geç
                if (hit.collider.GetComponentInParent<Il2Cpp.GearItem>() != null || hit.collider.GetComponentInParent<Il2CppTLD.Placement.Placeable>() != null)
                    continue;

                // Işının çarptığı İLK geçerli obje MasterInterior'ın bir parçası mı?
                if (hit.collider.transform.IsChildOf(MasterInterior.transform))
                {
                    return true; // İç mekanın bir duvarına/tavanına/zeminine çarptık.
                }

                // Eğer MasterInterior'a ait olmayan bir şeye çarptıysak demek ki dışarıya çıktık.
                return false;
            }

            // Hiçbir şeye çarpmadıysak dışarıdayızdır.
            return false;
        }
    }
}
