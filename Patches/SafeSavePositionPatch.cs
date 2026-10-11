using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    // A save made in a parked room stores a door's outdoor exit point, not the room 80 m up, so a game
    // loaded without SI starts at the building's door instead of falling to death. SI itself puts the
    // player back in the room from its own record (SavePlayerInsideState), so nothing changes with it.
    // The player is moved only for the length of PlayerManager.Serialize, which reads the transform.
    [HarmonyLib.HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.Serialize))]
    public class SafeSavePositionPatch
    {
        public static void Prefix(out Vector3? __state)
        {
            __state = null;
            try
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT == null) return;

                Vector3 here = playerT.position;
                SeamlessInteriorInstance room = SeamlessInteriorsMod.ParkedRoomHolding(here);
                if (room == null) return;

                if (!SeamlessInteriorsMod.TryGetSafeOutdoorPoint(room, here, out Vector3 safe))
                {
                    MelonLogger.Warning($"[KAYIT-GUVENLI] {room.Config.ResolvedInstanceId}: kapi onu noktasi bulunamadi, kayit odanin konumuyla yazildi.",
                                        $"[SAFE-SAVE] {room.Config.ResolvedInstanceId}: no door point found, the save keeps the room's position.");
                    return;
                }

                __state = here;
                playerT.position = safe;
                MelonLogger.Msg($"[KAYIT-GUVENLI] {room.Config.ResolvedInstanceId}: oyunun kaydina kapi onu yazildi ({safe.x:F1}, {safe.y:F1}, {safe.z:F1}); mod oyuncuyu odaya geri koyar.",
                                $"[SAFE-SAVE] {room.Config.ResolvedInstanceId}: the game's save holds the door point ({safe.x:F1}, {safe.y:F1}, {safe.z:F1}); SI puts the player back in the room.");
            }
            catch (System.Exception ex) { MelonLogger.Warning($"[KAYIT-GUVENLI] Prefix hatasi: {ex.Message}"); }
        }

        public static void Postfix(Vector3? __state)
        {
            if (!__state.HasValue) return;
            try
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null) playerT.position = __state.Value;
            }
            catch (System.Exception ex) { MelonLogger.Warning($"[KAYIT-GUVENLI] Postfix hatasi: {ex.Message}"); }
        }
    }

    public partial class SeamlessInteriorsMod
    {
        // The parked room the point stands in; the player's recorded building first, since neighbors overlap up there.
        internal static SeamlessInteriorInstance ParkedRoomHolding(Vector3 pos)
        {
            SeamlessInteriorInstance recorded;
            string id = PlayerInteriorId;
            if (id != null && ActiveInteriors.TryGetValue(id, out recorded) && IsLiveParkedRoomAt(recorded, pos))
                return recorded;

            foreach (var instance in ActiveInteriors.Values)
                if (IsLiveParkedRoomAt(instance, pos)) return instance;
            return null;
        }

        private static bool IsLiveParkedRoomAt(SeamlessInteriorInstance instance, Vector3 pos)
        {
            return instance != null && instance.Config != null && instance.Config.IsParked && instance.RunCompleted
                && instance.MasterInterior != null && instance.IsPositionInParkedRoom(pos);
        }

        // The exit point of the room's region door nearest to the player. A sub-interior with no such door
        // (the farmhouse basement) uses its parent building's doors.
        internal static bool TryGetSafeOutdoorPoint(SeamlessInteriorInstance room, Vector3 near, out Vector3 safe)
        {
            safe = Vector3.zero;
            if (TryNearestRegionExit(room, near, out safe)) return true;

            foreach (var parent in ActiveInteriors.Values)
            {
                if (parent == null || parent == room || !IsSubInteriorLinked(parent, room.Config.ResolvedInstanceId)) continue;
                if (TryNearestRegionExit(parent, near, out safe)) return true;
            }
            return false;
        }

        private static bool TryNearestRegionExit(SeamlessInteriorInstance instance, Vector3 near, out Vector3 safe)
        {
            safe = Vector3.zero;
            if (instance.MasterInterior == null) return false;

            LoadScene best = null;
            float bestSqr = float.MaxValue;
            foreach (var door in instance.MasterInterior.GetComponentsInChildren<LoadScene>(true))
            {
                if (!PortalMagicPatch.IsDoorToRegion(door)) continue;
                float sqr = (door.transform.position - near).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = door; }
            }
            if (best == null) return false;

            Vector3 point = PortalMagicPatch.ResolveExitPoint(instance, best);
            if (point == Vector3.zero || ParkedRoomHolding(point) != null) return false;

            // Onto the ground as the exit door does: Camp Office's front point stands 1.75 m over its porch step.
            point = PortalMagicPatch.SnapToGround(point);

            // A point still near park height is no ground: refuse it rather than save a fall.
            if (point.y > instance.MasterInterior.transform.position.y - instance.Config.ParkOffset.y * 0.5f) return false;

            safe = point;
            return true;
        }
    }
}
