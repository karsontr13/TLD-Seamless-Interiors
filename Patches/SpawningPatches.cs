using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace SeamlessInteriors
{
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.RandomSpawnObject), "Start")]
    public class RandomSpawnBlockerPatch
    {
        public static bool Prefix(Il2Cpp.RandomSpawnObject __instance)
        {
            // RSO'nun MasterInterior'a ait olup olmadığını kontrol et
            foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                if (instance.MasterInterior != null && __instance.transform.IsChildOf(instance.MasterInterior.transform))
                {
                    string saveKey = instance.Config.SaveKeyPrefix + Il2Cpp.SaveGameSystem.m_CurrentSaveName;

                    // Eşyalar daha önce oluşturulmuşsa (save key=1), RSO'yu sil
                    // Bu hem klonlama sırasında hem de persist-reattach sırasında çalışır
                    if (UnityEngine.PlayerPrefs.GetInt(saveKey, 0) == 1)
                    {
                        UnityEngine.Object.Destroy(__instance.gameObject);
                        return false;
                    }
                    break;
                }
            }
            return true;
        }
    }

    public class PlaceableFindOrCreatePatch
    {
        public static HashSet<string> s_InteriorPlaceableGuids = new HashSet<string>();
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2CppTLD.Placement.PlaceableManager), nameof(Il2CppTLD.Placement.PlaceableManager.Add))]
    public class PreventPlaceableAutoRegisterPatch
    {
        public static bool Prefix(Il2CppTLD.Placement.Placeable placeable)
        {
            if (placeable == null) return true;

            // Herhangi bir evde klonlama varsa kaydı durdur
            bool isAnyCloningActive = SeamlessInteriorsMod.ActiveInteriors.Values.Any(i => i.IsCloningRoutineActive);
            if (isAnyCloningActive)
            {
                return false;
            }

            // Klonlama bitmişse, eşya herhangi bir evin MasterInterior objesine mi bağlı?
            foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                if (instance.RunCompleted && instance.MasterInterior != null && placeable.transform.IsChildOf(instance.MasterInterior.transform))
                {
                    return false;
                }
            }

            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SaveGameSystem), nameof(Il2Cpp.SaveGameSystem.SaveSceneData))]
    public class SavePlaceablePositionsPatch
    {
        public static void Postfix()
        {
            try
            {
                // Oyuncunun hangi iç mekanda olduğunu kaydet
                SeamlessInteriorsMod.SavePlayerInsideState();

                // Tüm binaların pozisyonlarını kaydet
                SeamlessInteriorsMod.SaveAllPlaceablePositions();

                // TEST: Deaktif klon sahnelerdeki aktif GearItem'ları kaydet
                SeamlessInteriorsMod.SaveAllInactiveSceneGearItems();

                // Klon sahnelerdeki konteyner verilerini kaydet
                SeamlessInteriorsMod.SaveAllContainerData();
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[PLACEABLE-SAVE] Hata: {ex.Message}");
            }
        }
    }

    [HarmonyLib.HarmonyPatch]
    public class PreventGearManagerDuplicationPatch
    {
        public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var method in typeof(Il2Cpp.GearManager).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (method.Name == "Deserialize")
                {
                    yield return method;
                }
            }
        }

        public static bool Prefix()
        {
            // Herhangi bir klonlama devam ediyorsa ana oyunun Gear deserialize işlemini durdur
            if (SeamlessInteriorsMod.ActiveInteriors.Values.Any(i => i.IsCloningRoutineActive))
            {
                return false;
            }
            return true;
        }
    }
}