using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── A SHOT THROUGH AN OPEN WINDOW LANDS OUTSIDE ───
        //
        // Owner, 2026-10-18. A parked room stands far from its building, so a bullet fired out of an open
        // window would fly through the empty air round the room. A rifle or revolver bullet does its whole
        // flight as one ray when it starts, so it is moved before that: to where its line crosses the open
        // part of the window, carried by that window's view delta to the building's own window, and on.

        private const float SHOT_PAST_SHELL_GLASS = 0.03f;   // metres past where the view out starts
        private static bool s_ShotThroughFailed;

        internal static void MaybeSendShotThroughWindow(vp_Bullet bullet)
        {
            if (s_ShotThroughFailed || bullet == null || s_WindowSashes.Count == 0) return;
            try
            {
                Camera main = GameManager.GetMainCamera();
                if (main == null) return;
                Transform bt = bullet.transform;
                Vector3 dir = bt.forward, from = bt.position;
                // The line is judged from the eye, since the muzzle may already stand past the glass.
                Vector3 eye = from + dir * Vector3.Dot(main.transform.position - from, dir);

                int best = -1; float bestT = float.MaxValue; Vector3 bestHit = Vector3.zero;
                foreach (var sash in s_WindowSashes)
                {
                    if (sash.Shown <= 0.5f || sash.Opening >= s_WindowPortalDelta.Count) continue;
                    var opening = s_WindowPortalOpenings[sash.Opening];
                    float facing = Vector3.Dot(dir, opening.Outward);
                    if (facing < 0.05f) continue;
                    float t = Vector3.Dot(opening.GlassCenter - eye, opening.Outward) / facing;
                    if (t <= 0f || t >= bestT || t > bullet.Range) continue;
                    Vector3 hit = eye + dir * t;
                    // Only the open part: under the raised sash, between its sides.
                    float r = Vector3.Dot(hit - opening.GlassCenter, sash.Right);
                    float u = Vector3.Dot(hit - opening.GlassCenter, sash.Up);
                    if (r < sash.EdgeLeft || r > sash.EdgeRight || u < sash.EdgeBottom || u > sash.EdgeBottom + sash.Slide * sash.Shown) continue;
                    best = sash.Opening; bestT = t; bestHit = hit;
                }
                if (best < 0) return;
                if (ShotBlockedInRoom(eye, dir, bestT, s_WindowPortalOpenings[best].PaneRoot)) return;

                var o = s_WindowPortalOpenings[best];
                float viewStart = best < s_WindowPortalViewStart.Count ? s_WindowPortalViewStart[best] : WINDOW_PORTAL_VIEW_STARTS_OUTSIDE_SHELL_GLASS;
                bt.position = bestHit + s_WindowPortalDelta[best] + o.Outward * (viewStart + SHOT_PAST_SHELL_GLASS);
                bullet.Range = Mathf.Max(bullet.Range - bestT, 1f);
                MelonLogger.Msg($"[ATIS] Mermi {best} numarali acik pencereden disari: {bt.position}", $"[SHOT] Bullet sent out through open window {best}: {bt.position}");
            }
            catch (System.Exception ex)
            {
                s_ShotThroughFailed = true;
                MelonLogger.Warning($"[ATIS] Kapatildi: {ex.Message}", $"[SHOT] Turned off after an error: {ex.Message}");
            }
        }

        // Something in the room between the eye and the window takes the shot itself.
        private static bool ShotBlockedInRoom(Vector3 eye, Vector3 dir, float t, Transform window)
        {
            Transform player = GameManager.GetPlayerTransform();
            var hits = Physics.RaycastAll(eye, dir, t, Utils.m_WeaponProjectileCollisionLayerMask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                Transform ht = h.collider != null ? h.collider.transform : null;
                if (ht == null) continue;
                if (player != null && ht.IsChildOf(player)) continue;
                if (window != null && ht.IsChildOf(window)) continue;
                // The window's own glass, frame and sash stand within a hand of its plane.
                if (h.distance > t - 0.15f) continue;
                return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(vp_Bullet), nameof(vp_Bullet.Start))]
    public class ShotThroughWindowPatch
    {
        public static void Prefix(vp_Bullet __instance) { SeamlessInteriorsMod.MaybeSendShotThroughWindow(__instance); }
    }
}
