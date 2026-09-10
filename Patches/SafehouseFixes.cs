using Il2Cpp;
using UnityEngine;
using MelonLoader;
using Il2CppTLD.Placement;

namespace SeamlessInteriors
{
    // The Safehouse partial-class methods live in Core/SeamlessInteriorsMod.Safehouse.cs.

    // Cloned interiors sit in the region scene, which the game calls "outdoors",
    // so safehouse customization would be refused. Allow it inside any instance.
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

    // ─── "CLEAR JUNK" (R) — see Core/SeamlessInteriorsMod.Junk.cs ───
    //
    // The game's JunkManager stores a SINGLE bool per scene. Pressing R inside a
    // clone writes that bool onto the region scene; after a reload the clone is
    // rebuilt so the junk is back, but the bool is still true, CanClearJunk()
    // returns false and R never works again.
    //
    // This patch overrides the global flag whenever the player stands inside a
    // clone whose junk has not been cleared yet, re-enabling R and the HUD hint.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.JunkManager), nameof(Il2Cpp.JunkManager.CanClearJunk))]
    public class AllowClearJunkInsideClonePatch
    {
        public static void Postfix(ref bool __result)
        {
            if (__result) return;

            if (SeamlessInteriorsMod.PlayerIsInInstanceWithClearableJunk())
                __result = true;
        }
    }

    // After the game clears junk, mark the clone and also hide the junk that is
    // parked in the deactivated hierarchy.
    //
    // BOTH ClearJunk and MaybeClearJunk are patched: IL2CPP may have inlined the
    // body of ClearJunk into MaybeClearJunk, in which case the ClearJunk patch
    // never fires. The handler is idempotent, so running twice is harmless.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.JunkManager), nameof(Il2Cpp.JunkManager.ClearJunk))]
    public class MarkCloneJunkClearedPatch
    {
        public static void Postfix()
        {
            try { SeamlessInteriorsMod.OnGameClearedJunk(); }
            catch (System.Exception ex) { MelonLogger.Warning($"[JUNK] ClearJunk postfix hatasi: {ex.Message}"); }
        }
    }

    // Fallback for the inlined case described above.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.JunkManager), nameof(Il2Cpp.JunkManager.MaybeClearJunk))]
    public class MarkCloneJunkClearedFallbackPatch
    {
        public static void Postfix(bool __result)
        {
            if (!__result) return;

            try { SeamlessInteriorsMod.OnGameClearedJunk(); }
            catch (System.Exception ex) { MelonLogger.Warning($"[JUNK] MaybeClearJunk postfix hatasi: {ex.Message}"); }
        }
    }

    // Decoration items dropped inside a clone must count as "inside the safehouse".
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

    // Placement mode has its own indoor check; report indoors while an object is
    // being placed inside an instance.
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

    // ─── FREE PLACEMENT INSIDE CLONES ───
    // Cloned geometry overlaps the region's own colliders, so the vanilla
    // placement validation rejects almost every spot. The next three patches
    // short-circuit those checks while the player is inside an instance.

    // No blocking-collider test.
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

    // No collision-penetration test.
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

    // No out-of-bounds test.
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
