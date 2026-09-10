using Il2Cpp;
using UnityEngine;
using MelonLoader;
using Il2CppTLD.Placement;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Registers a cloned interior with the safehouse system so that decorating
        // and item placement work inside it just like in a vanilla interior.
        public static void ApplySafehouseCustomizationFix(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            if (Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders == null)
            {
                Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders = new Il2CppSystem.Collections.Generic.List<Collider>();
            }

            // The instance's own trigger volume counts as safehouse space.
            if (instance.InteriorTrigger != null && !Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders.Contains(instance.InteriorTrigger))
            {
                Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders.Add(instance.InteriorTrigger);
            }

            // Plus every IndoorSpaceTrigger the original scene already marked as valid.
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

            // Cloning invalidates placeables; clear the flag so they stay interactive.
            var placeables = instance.MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in placeables)
            {
                if (p != null) p.m_Invalidated = false;
            }
        }

        // One-slot cache for repeated queries within the same frame.
        //
        // WHY: SIX safehouse/placement patches (InCustomizableSafehouse,
        // IsDecorationItemInsideSafehouse, IsIndoorEnvironment, ObjectToPlaceOverlaps...,
        // HasCollisionPenetration, IsHitPointOutOfBounds) call this for the same player
        // position in the same frame. While placing an item that meant repeating the
        // same ray test 5-6 times every frame.
        private static int s_InsideAnyFrame = -1;
        private static Vector3 s_InsideAnyPos;
        private static bool s_InsideAnyResult;

        // Central check: is the player inside any active, fully built instance?
        // Optimization: a distance pre-filter skips the raycast for instances more
        // than 80m away from their FallbackPosition.
        public static bool IsPositionInsideAnyInstance(Vector3 pos)
        {
            int frame = Time.frameCount;
            if (s_InsideAnyFrame == frame && (s_InsideAnyPos - pos).sqrMagnitude < 0.0001f)
                return s_InsideAnyResult;

            bool result = false;
            foreach (var instance in ActiveInteriors.Values)
            {
                if (!instance.RunCompleted) continue;

                // A hidden clone cannot contain the player. IsPositionInside would say
                // the same, but bailing here also skips the config/distance work.
                if (instance.MasterInterior == null || !instance.MasterInterior.activeSelf) continue;

                // Distance pre-filter: too far from FallbackPosition, no raycast.
                if (Vector3.SqrMagnitude(pos - instance.Config.FallbackPosition) > 6400f) continue; // 80^2 = 6400

                if (instance.IsPositionInside(pos)) { result = true; break; }
            }

            s_InsideAnyFrame = frame;
            s_InsideAnyPos = pos;
            s_InsideAnyResult = result;
            return result;
        }

        public static bool IsAnyCloningActive()
        {
            foreach (var instance in ActiveInteriors.Values)
                if (instance != null && instance.IsCloningRoutineActive) return true;
            return false;
        }

        public static bool AreAllInstancesReady()
        {
            foreach (var instance in ActiveInteriors.Values)
                if (instance != null && !instance.RunCompleted) return false;
            return true;
        }
    }
}
