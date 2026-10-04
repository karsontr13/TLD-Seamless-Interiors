using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── INDOOR PLAYER CAPSULE ───
        // A vanilla interior gives the player a 0.3 m capsule and a 60° slope limit (Weather.ForceIndoorEnvironment).
        // A clone stands in an outdoor scene, so the 0.5 m / 45° outdoor capsule stuck in doorways and on stair ramps.

        private static bool s_IndoorCapsuleApplied;
        private static bool s_ReportedIndoorCapsule;
        private static CharacterController s_PlayerCapsule;

        // Checked every frame inside, because a ForceOutdoorEnvironment (a clone build) puts the outdoor capsule back.
        internal static void TickIndoorPlayerCapsule()
        {
            bool inside = PlayerInteriorId != null;
            if (!inside && !s_IndoorCapsuleApplied) return;

            GlobalParameters gp = GameManager.GetGlobalParameters();
            PlayerMovement movement = GameManager.GetPlayerMovementComponent();
            CharacterController capsule = GetPlayerCapsule();
            if (gp == null || movement == null || capsule == null) return;

            // On leaving, the values the scene's own Start set.
            bool indoorValues = inside;
            if (!inside)
            {
                Weather weather = GameManager.GetWeatherComponent();
                indoorValues = weather != null && weather.IsIndoorScene();
            }

            float radius = indoorValues ? gp.m_PlayerRadiusIndoors : gp.m_PlayerRadiusOutdoors;
            float slope = indoorValues ? movement.m_MaxSlopeIndoors : movement.m_MaxSlopeOutdoors;
            if (capsule.radius != radius) capsule.radius = radius;
            if (capsule.slopeLimit != slope) capsule.slopeLimit = slope;

            if (inside == s_IndoorCapsuleApplied) return;
            s_IndoorCapsuleApplied = inside;
            SwapNearPlaneDefault(gp, indoorValues);

            if (!s_ReportedIndoorCapsule || s_DebugBounds)
            {
                s_ReportedIndoorCapsule = true;
                MelonLogger.Msg($"[IC-HAREKET] Oyuncu kapsulu {(inside ? "ic mekan" : "dis mekan")} degerlerinde: " +
                                $"yaricap {radius}, egim limiti {slope}");
            }
        }

        private static CharacterController GetPlayerCapsule()
        {
            if (s_PlayerCapsule == null)
            {
                GameObject player = GameManager.GetPlayerObject();
                s_PlayerCapsule = player != null ? player.GetComponent<CharacterController>() : null;
            }
            return s_PlayerCapsule;
        }

        // The camera's near plane follows the smaller capsule as in a vanilla interior.
        // Only the other environment's default is swapped, so an active SetNearPlaneOverride stays.
        private static void SwapNearPlaneDefault(GlobalParameters gp, bool indoor)
        {
            vp_FPSCamera cam = GameManager.GetVpFPSCamera();
            if (cam == null) return;

            float from = indoor ? gp.m_NearClipPlaneOutdoors : gp.m_NearClipPlaneIndoors;
            float to = indoor ? gp.m_NearClipPlaneIndoors : gp.m_NearClipPlaneOutdoors;
            if (Mathf.Approximately(cam.m_NearClipPlane, from)) cam.m_NearClipPlane = to;
        }
    }
}
