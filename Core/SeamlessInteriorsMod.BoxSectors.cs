using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // BoxSectors draw a decoration only while the sector it is filed under is in view; SI's restore left moved
        // pieces filed under their old room (invisible but solid). Re-file each one where it stands, room open.
        internal static void SyncBoxSectors(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null || !instance.MasterInterior.activeSelf) return;

            var manager = instance.MasterInterior.GetComponentInChildren<BoxSectorManager>(true);
            if (manager == null) return;

            // The game's pick-up and placement reach the sectors through this one static.
            BoxSectorManager.s_Instance = manager;
            if (instance.BoxSectorsSynced) return;
            instance.BoxSectorsSynced = true;

            int filed = 0;
            try
            {
                // The contact test is a physics query; restored positions reach physics only at its next step.
                Physics.SyncTransforms();
                foreach (var decoration in instance.MasterInterior.GetComponentsInChildren<DecorationItem>(true))
                    if (RefileDecoration(manager, decoration)) filed++;

                // Moved pieces wait under the game's placement root, outside the clone.
                GameObject placementRoot = GameObject.Find(PlacementRootName());
                if (placementRoot != null)
                    foreach (var decoration in placementRoot.GetComponentsInChildren<DecorationItem>(true))
                    {
                        if (decoration == null || !instance.IsPositionInVolume(decoration.transform.position, 0f)) continue;
                        if (BelongsToAnotherInstance(instance, decoration.transform)) continue;
                        if (RefileDecoration(manager, decoration)) filed++;
                    }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[SEKTOR] {instance.Config.ResolvedInstanceId}: {ex.Message}",
                                    $"[SECTOR] {instance.Config.ResolvedInstanceId}: {ex.Message}");
                return;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[SEKTOR] {instance.Config.ResolvedInstanceId}: {filed} dekorasyon durdugu odanin sektorune yeniden kaydedildi.",
                                $"[SECTOR] {instance.Config.ResolvedInstanceId}: {filed} decorations filed again under the sector they stand in.");
        }

        // Out of every sector, then into the one it touches; a switched-off piece is only taken out.
        private static bool RefileDecoration(BoxSectorManager manager, DecorationItem decoration)
        {
            if (decoration == null || decoration.gameObject == null) return false;
            manager.RemoveDecorationFromSystem(decoration);
            if (!decoration.gameObject.activeInHierarchy) return false;
            manager.MaybeAddDecorationIntoSystem(decoration);
            return true;
        }
    }
}
