using Il2Cpp;
using UnityEngine;
using MelonLoader;
using Il2CppTLD.Placement;
using System.Linq;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        public static void ApplySafehouseCustomizationFix(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            if (Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders == null)
            {
                Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders = new Il2CppSystem.Collections.Generic.List<Collider>();
            }

            if (instance.InteriorTrigger != null && !Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders.Contains(instance.InteriorTrigger))
            {
                Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders.Add(instance.InteriorTrigger);
            }

            var allTriggers = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.IndoorSpaceTrigger>(true);
            foreach (var trigger in allTriggers)
            {
                if (trigger != null && trigger.m_ValidSafehouse)
                {
                    var col = trigger.GetComponent<Collider>();
                    if (col != null && !Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders.Contains(col))
                    {
                        Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders.Add(col);
                    }
                }
            }

            var placeables = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in placeables)
            {
                if (p != null) p.m_Invalidated = false;
            }
        }

        // Merkezi Kontrol Fonksiyonu: Oyuncu aktif ve kurulu binalardan herhangi birinin içinde mi?
        // OPTİMİZASYON: Mesafe pre-filter - FallbackPosition'a 80m'den uzak instance'lar için raycast atlamaz
        public static bool IsPositionInsideAnyInstance(Vector3 pos)
        {
            foreach (var instance in ActiveInteriors.Values)
            {
                if (!instance.RunCompleted) continue;
                // Mesafe pre-filter: FallbackPosition'a çok uzaksa raycast yapma
                if (Vector3.SqrMagnitude(pos - instance.Config.FallbackPosition) > 6400f) continue; // 80^2 = 6400
                if (instance.IsPositionInside(pos)) return true;
            }
            return false;
        }
    }
}
