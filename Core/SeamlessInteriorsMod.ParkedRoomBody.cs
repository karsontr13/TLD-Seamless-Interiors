using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── THE PLAYER'S INDOOR BODY, IN A PARKED ROOM ───
        //
        // The game keeps two bodies for the player and picks one per scene: outdoors the
        // CharacterController is 100 cm wide and climbs 45 degrees, in an interior scene it is
        // 60 cm wide and climbs 60. The numbers are the game's own, GlobalParameters
        // m_PlayerRadiusIndoors / m_PlayerRadiusOutdoors (0.3 / 0.5 m) and PlayerMovement
        // m_MaxSlopeIndoors / m_MaxSlopeOutdoors (60 / 45 degrees), stored in one prefab and
        // overridden by none of the game's scenes.
        //
        // A parked room is one of those interior scenes stood in the outdoor scene, so the game
        // never makes the switch and the player walks a room built for the indoor body with the
        // outdoor one. MEASURED 2026-09-21, both at scale 1: the gas station's bathroom doorway
        // is 98.5 cm clear and stops a 100 cm body; the step-up in the farmhouse basement has a
        // 45.4 degree patch on its ramp and stops a 45 degree limit. The author's stretch of some
        // rooms hid both (a doorway 2% wider, a ramp 15% longer) until a parked room was loaded
        // at its own size. In the game's own gas station scene the body read 60 cm.
        //
        // Every parked room was an interior scene, so the rule is for all of them and needs no
        // table. The values are read from the game each time and the mod holds no number of its
        // own. A width set this way was read back unchanged after 26 s of walking: the game
        // applies its choice at scene load and does not keep rewriting it.
        //
        // THE CAMERA'S NEAR CLIP is the third thing the game switches, GlobalParameters
        // m_NearClipPlaneIndoors / m_NearClipPlaneOutdoors (0.10 / 0.15 m), and it belongs with
        // the width: a body 20 cm nearer the wall wants a near plane nearer the eye. Unlike the
        // other two it cannot be written to the camera, the game put 0.15 back within 7 s. The
        // game's own lever is vp_FPSCamera.SetNearPlaneOverride, which held, a save reload
        // included. StopNearPlaneOverride did NOT bring 0.15 back, so the outdoor value is
        // handed back with the same call. The override is also what the game's animated
        // interactions use, so this only ever trades the game's outdoor value for its indoor one
        // and back, and leaves any other value in force alone.
        //
        // Follows the flag, like the registration in IndoorState.cs and for the same reasons:
        // s_IsPlayerInsideClone is written from eight places, and a body left indoor-sized
        // outside is a player who climbs 60 degree rock. Only what this code set is handed
        // back, so the game's own choice in an interior scene the mod does not own is never
        // touched. In its own file because the merge with the author's upload replaces
        // IndoorState.cs.

        private static bool s_ParkedRoomBodyInForce = false;
        private static bool s_ParkedRoomBodyFaulted = false;

        public static void TickParkedRoomBody()
        {
            if (s_ParkedRoomBodyFaulted) return;
            try
            {
                SeamlessInteriorInstance wanted = s_IsPlayerInsideClone ? ResolveOccupiedInstance() : null;
                if (wanted != null && (wanted.Config == null || !wanted.Config.IsParked)) wanted = null;

                // Almost every tick outdoors: nothing in force and nothing wanted.
                if (wanted == null && !s_ParkedRoomBodyInForce) return;

                Transform player = GameManager.GetPlayerTransform();
                GlobalParameters parameters = GameManager.GetGlobalParameters();
                PlayerMovement movement = GameManager.GetPlayerMovementComponent();
                if (player == null || parameters == null || movement == null) return;
                CharacterController body = player.GetComponent<CharacterController>();
                if (body == null) return;

                bool indoors = wanted != null;
                float radius = indoors ? parameters.m_PlayerRadiusIndoors : parameters.m_PlayerRadiusOutdoors;
                float slope = indoors ? movement.m_MaxSlopeIndoors : movement.m_MaxSlopeOutdoors;

                // A parameter nobody filled in must not collapse the body to a line.
                if (radius < 0.05f || slope < 1f) return;

                bool moved = !Mathf.Approximately(body.radius, radius) || !Mathf.Approximately(body.slopeLimit, slope);
                if (moved)
                {
                    float oldRadius = body.radius, oldSlope = body.slopeLimit;
                    body.radius = radius;
                    body.slopeLimit = slope;
                    // Once per change of state, not per tick: the second pass finds nothing moved.
                    if (indoors != s_ParkedRoomBodyInForce)
                        MelonLogger.Msg($"[PARK-GOVDE] Oyuncu govdesi {(indoors ? "ic mekan" : "dis mekan")} olcusune alindi: genislik {oldRadius * 200f:F0} -> {radius * 200f:F0} cm, egim siniri {oldSlope:F0} -> {slope:F0} derece.",
                                        $"[PARKED-BODY] The player's body was set to the game's {(indoors ? "indoor" : "outdoor")} size: width {oldRadius * 200f:F0} -> {radius * 200f:F0} cm, slope limit {oldSlope:F0} -> {slope:F0} degrees.");
                }

                vp_FPSCamera eye = GameManager.GetVpFPSCamera();
                float nearIn = parameters.m_NearClipPlaneIndoors, nearOut = parameters.m_NearClipPlaneOutdoors;
                if (eye != null && nearIn > 0.001f && nearOut > 0.001f)
                {
                    float nearNow = eye.GetNearPlaneOverride();
                    float nearFrom = indoors ? nearOut : nearIn, nearTo = indoors ? nearIn : nearOut;
                    if (Mathf.Abs(nearNow - nearFrom) < 0.0005f && Mathf.Abs(nearNow - nearTo) > 0.0005f)
                    {
                        eye.SetNearPlaneOverride(nearTo);
                        MelonLogger.Msg($"[PARK-GOVDE] Kamera yakin kesme duzlemi {nearFrom:F2} -> {nearTo:F2} m.",
                                        $"[PARKED-BODY] The camera's near clip was set to the game's {(indoors ? "indoor" : "outdoor")} value: {nearFrom:F2} -> {nearTo:F2} m.");
                    }
                }

                s_ParkedRoomBodyInForce = indoors;
            }
            catch (System.Exception e)
            {
                s_ParkedRoomBodyFaulted = true;
                MelonLogger.Warning("[PARKED-BODY] The player's indoor body could not be set, and will not be tried again this session: " + e.Message);
            }
        }
    }
}
