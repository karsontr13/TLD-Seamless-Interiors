using Il2Cpp;
using UnityEngine;
using MelonLoader;
using Il2CppTLD.Placement;

namespace SeamlessInteriors
{
    // Safehouse partial class metotları -> Core/SeamlessInteriorsMod.Safehouse.cs dosyasına taşındı.

    [HarmonyLib.HarmonyPatch(typeof(SafehouseManager), nameof(SafehouseManager.InCustomizableSafehouse))]
    public class AllowCustomizationOutdoorsPatch
    {
        public static void Postfix(ref bool __result)
        {
            if (__result) return;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT != null && SeamlessInteriorsMod.IsPositionInsideAnyInstance(playerT.position))
            {
                __result = true;
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(SafehouseManager), nameof(SafehouseManager.IsDecorationItemInsideSafehouse))]
    public class AllowDecorationPlacementPatch
    {
        public static void Postfix(Il2Cpp.DecorationItem item, ref bool __result)
        {
            if (__result) return;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT != null && SeamlessInteriorsMod.IsPositionInsideAnyInstance(playerT.position))
            {
                __result = true;
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Weather), nameof(Il2Cpp.Weather.IsIndoorEnvironment))]
    public class FakeIndoorForPlacementPatch
    {
        public static void Postfix(ref bool __result)
        {
            if (__result) return;

            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm != null && pm.m_ObjectToPlace != null)
            {
                if (SeamlessInteriorsMod.IsPositionInsideAnyInstance(pm.transform.position))
                {
                    __result = true;
                }
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.ObjectToPlaceOverlapsWithObjectsThatBlockPlacement))]
    public class FreePlacement_OverlapPatch
    {
        public static bool Prefix(ref Collider __result)
        {
            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT != null && SeamlessInteriorsMod.IsPositionInsideAnyInstance(playerT.position))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.HasCollisionPenetration))]
    public class FreePlacement_PenetrationPatch
    {
        public static bool Prefix(ref bool __result)
        {
            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT != null && SeamlessInteriorsMod.IsPositionInsideAnyInstance(playerT.position))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.IsHitPointOutOfBounds))]
    public class FreePlacement_OutOfBoundsPatch
    {
        public static bool Prefix(ref bool __result)
        {
            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT != null && SeamlessInteriorsMod.IsPositionInsideAnyInstance(playerT.position))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
