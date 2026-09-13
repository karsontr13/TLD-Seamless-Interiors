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
    // The game keeps ONE "junk cleared" flag, and a clone lives inside the region scene.
    // These patches keep that flag answering for the clone the player is customizing in,
    // keep the clone's value out of the region's save, and keep ClearJunk from reaching
    // into any other clone.
    //
    // There is deliberately no patch on CanClearJunk or MaybeClearJunk any more: with the
    // flag right, the game's own gates give the right answer by themselves.

    // StartCustomizing asks CanClearJunk for the "R" prompt inside the same call, so the
    // clone's state has to be in the flag before it runs.
    [HarmonyLib.HarmonyPatch(typeof(SafehouseManager), nameof(SafehouseManager.StartCustomizing))]
    public class JunkFlagOnStartCustomizingPatch
    {
        public static void Prefix()
        {
            try { SeamlessInteriorsMod.OnStartCustomizing(); }
            catch (System.Exception ex) { MelonLogger.Warning($"[JUNK] StartCustomizing prefix hatasi: {ex.Message}"); }
        }
    }

    // ClearJunk switches off every JunkTag in every loaded scene, inactive ones included.
    // Junk it had no business touching is switched back on afterwards.
    //
    // Every clear goes through here: MaybeClearJunk (the player's action), the console and
    // LoadSceneData all CALL ClearJunk, and nothing inlines it (measured, see Junk.cs).
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.JunkManager), nameof(Il2Cpp.JunkManager.ClearJunk))]
    public class KeepClearJunkInsideOneClonePatch
    {
        public static void Prefix()
        {
            try { SeamlessInteriorsMod.BeforeGameClearJunk(); }
            catch (System.Exception ex) { MelonLogger.Warning($"[JUNK] ClearJunk prefix hatasi: {ex.Message}"); }
        }

        public static void Postfix()
        {
            try { SeamlessInteriorsMod.AfterGameClearJunk(); }
            catch (System.Exception ex) { MelonLogger.Warning($"[JUNK] ClearJunk postfix hatasi: {ex.Message}"); }
        }
    }

    // A scene save writes the flag into the REGION's save, so it must see the region's own
    // value while it runs. (StopCustomizing triggers a save of its own.)
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SaveGameSystem), nameof(Il2Cpp.SaveGameSystem.SaveSceneData))]
    public class RegionJunkFlagForSceneSavePatch
    {
        public static void Prefix()
        {
            try { SeamlessInteriorsMod.BeforeSceneSave(); }
            catch (System.Exception ex) { MelonLogger.Warning($"[JUNK] SaveSceneData prefix hatasi: {ex.Message}"); }
        }

        public static void Postfix()
        {
            try { SeamlessInteriorsMod.AfterSceneSave(); }
            catch (System.Exception ex) { MelonLogger.Warning($"[JUNK] SaveSceneData postfix hatasi: {ex.Message}"); }
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
