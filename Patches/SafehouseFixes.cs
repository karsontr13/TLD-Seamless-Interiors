using Il2Cpp;
using UnityEngine;
using MelonLoader;
using Il2CppTLD.Placement;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Registers our cloned interior's trigger collider(s) with the base game's
        // SafehouseManager so the game recognizes the clone as a valid, customizable
        // safehouse space (letting the player decorate/rearrange items in it), and
        // unlocks all placeables inside so they can be picked up/moved/deleted again.
        public static void ApplySafehouseCustomizationFix()
        {
            if (s_MasterInterior == null) return;

            if (Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders == null)
            {
                Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders = new Il2CppSystem.Collections.Generic.List<Collider>();
            }

            if (s_InteriorTrigger != null && !Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders.Contains(s_InteriorTrigger))
            {
                Il2Cpp.SafehouseManager.s_SafehouseIndoorSpaceTriggerColliders.Add(s_InteriorTrigger);
            }

            var allTriggers = s_MasterInterior.GetComponentsInChildren<Il2Cpp.IndoorSpaceTrigger>(true);
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

            var placeables = s_MasterInterior.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true);
            foreach (var p in placeables)
            {
                if (p != null) p.m_Invalidated = false;
            }
        }

        // Lets the player open the customization menu (Y key) even while standing in
        // the cloned interior, which the game otherwise thinks is still "outdoors"
        // (LakeRegion) rather than a real, self-contained safehouse scene.
        [HarmonyLib.HarmonyPatch(typeof(SafehouseManager), nameof(SafehouseManager.InCustomizableSafehouse))]
        public class AllowCustomizationOutdoorsPatch
        {
            public static void Postfix(ref bool __result)
            {
                if (__result) return;

                if (SeamlessInteriorsMod.s_RunCompleted)
                {
                    Transform playerT = GameManager.GetPlayerTransform();
                    if (playerT != null && SeamlessInteriorsMod.IsPositionInsideCabin(playerT.position))
                    {
                        __result = true;
                    }
                }
            }
        }

        // Prevents decoration items from showing the "rejected" (red) highlight when the
        // player tries to place them inside the cloned interior.
        [HarmonyLib.HarmonyPatch(typeof(SafehouseManager), nameof(SafehouseManager.IsDecorationItemInsideSafehouse))]
        public class AllowDecorationPlacementPatch
        {
            public static void Postfix(Il2Cpp.DecorationItem item, ref bool __result)
            {
                if (__result) return;

                if (SeamlessInteriorsMod.s_RunCompleted)
                {
                    Transform playerT = GameManager.GetPlayerTransform();
                    if (playerT != null && SeamlessInteriorsMod.IsPositionInsideCabin(playerT.position))
                    {
                        __result = true;
                    }
                }
            }
        }

        // Tricks the item-placement system whenever it asks "are we indoors?" while the
        // player is holding an object to place, so it treats our clone the same way it
        // would treat a real interior.
        [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Weather), nameof(Il2Cpp.Weather.IsIndoorEnvironment))]
        public class FakeIndoorForPlacementPatch
        {
            public static void Postfix(ref bool __result)
            {
                if (__result) return;

                if (SeamlessInteriorsMod.s_RunCompleted)
                {
                    PlayerManager pm = GameManager.GetPlayerManagerComponent();
                    if (pm != null && pm.m_ObjectToPlace != null)
                    {
                        if (SeamlessInteriorsMod.IsPositionInsideCabin(pm.transform.position))
                        {
                            __result = true;
                        }
                    }
                }
            }
        }
    }

    // These three patches all do the same thing for three different checks the game runs
    // when the player tries to place a decoration item: if the player is standing inside our
    // cloned interior, skip the game's own collision/placement-blocking logic entirely and
    // report "no obstruction". This is needed because the clone's geometry/colliders don't
    // match what the placement system expects from a "real" interior, so without this it
    // would constantly refuse valid placements as if they were blocked or out of bounds.

    [HarmonyLib.HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.ObjectToPlaceOverlapsWithObjectsThatBlockPlacement))]
    public class FreePlacement_OverlapPatch
    {
        public static bool Prefix(ref Collider __result)
        {
            if (SeamlessInteriorsMod.s_RunCompleted)
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null && SeamlessInteriorsMod.IsPositionInsideCabin(playerT.position))
                {
                    __result = null;
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.HasCollisionPenetration))]
    public class FreePlacement_PenetrationPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (SeamlessInteriorsMod.s_RunCompleted)
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null && SeamlessInteriorsMod.IsPositionInsideCabin(playerT.position))
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.IsHitPointOutOfBounds))]
    public class FreePlacement_OutOfBoundsPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (SeamlessInteriorsMod.s_RunCompleted)
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null && SeamlessInteriorsMod.IsPositionInsideCabin(playerT.position))
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }
    }
}