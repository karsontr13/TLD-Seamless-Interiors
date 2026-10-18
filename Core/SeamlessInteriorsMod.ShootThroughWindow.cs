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
        // Owner, 2026-10-18. A parked room stands far from its building, so a bullet fired out of an
        // open window would fly through the empty air round the room. A rifle or revolver bullet does its
        // whole flight as one ray when it starts, so it is moved before that: to where its line crosses
        // the open part of the window, carried by that window's view delta to the building's own window,
        // and on. Arrows, stones and flares fly as bodies, so each is carried the step before it crosses.

        private const float SHOT_PAST_SHELL_GLASS = 0.03f;   // metres past where the view out starts
        private static bool s_ShotThroughFailed;
        private static BaseAi s_ShotAi;   // set while a sent-out bullet is in its Start
        internal static bool s_FleeSeesOutdoors;   // true inside BaseAi.ProcessFlee while the player is in a room
        private static readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<BaseAi, float>> s_ShotHp = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<BaseAi, float>>();
        private static WeaponSource s_ShotSource;

        // The nearest open window part this line crosses within maxT, or -1.
        private static int FindOpenWindowCrossing(Vector3 from, Vector3 dir, float maxT, out float hitT, out Vector3 hit)
        {
            int best = -1; hitT = float.MaxValue; hit = Vector3.zero;
            foreach (var sash in s_WindowSashes)
            {
                if (sash.Shown <= 0.5f || sash.Opening >= s_WindowPortalDelta.Count) continue;
                var opening = s_WindowPortalOpenings[sash.Opening];
                float facing = Vector3.Dot(dir, opening.Outward);
                if (facing < 0.05f) continue;
                float t = Vector3.Dot(opening.GlassCenter - from, opening.Outward) / facing;
                if (t <= 0f || t >= hitT || t > maxT) continue;
                Vector3 p = from + dir * t;
                // Only the open part: under the raised sash, between its sides.
                float r = Vector3.Dot(p - opening.GlassCenter, sash.Right);
                float u = Vector3.Dot(p - opening.GlassCenter, sash.Up);
                if (r < sash.EdgeLeft || r > sash.EdgeRight || u < sash.EdgeBottom || u > sash.EdgeBottom + sash.Slide * sash.Shown) continue;
                best = sash.Opening; hitT = t; hit = p;
            }
            return best;
        }

        // How far a thing crossing window `opening` is carried: to just outside the building's own glass.
        private static Vector3 WindowCrossingShift(int opening)
        {
            float viewStart = opening < s_WindowPortalViewStart.Count ? s_WindowPortalViewStart[opening] : WINDOW_PORTAL_VIEW_STARTS_OUTSIDE_SHELL_GLASS;
            return s_WindowPortalDelta[opening] + s_WindowPortalOpenings[opening].Outward * (viewStart + SHOT_PAST_SHELL_GLASS);
        }

        private static bool AnyWindowOpenForShots()
        {
            foreach (var sash in s_WindowSashes) if (sash.Shown > 0.5f) return true;
            return false;
        }

        internal static void MaybeSendShotThroughWindow(vp_Bullet bullet)
        {
            s_ShotAi = null;
            if (s_ShotThroughFailed || bullet == null || !AnyWindowOpenForShots()) return;
            try
            {
                Camera main = GameManager.GetMainCamera();
                if (main == null) return;
                Transform bt = bullet.transform;
                Vector3 dir = bt.forward, from = bt.position;
                // The line is judged from the eye, since the muzzle may already stand past the glass.
                Vector3 eye = from + dir * Vector3.Dot(main.transform.position - from, dir);

                int best = FindOpenWindowCrossing(eye, dir, bullet.Range, out float t, out Vector3 hit);
                if (best < 0) return;
                if (ShotBlockedInRoom(eye, dir, t, s_WindowPortalOpenings[best].PaneRoot)) return;

                bt.position = hit + WindowCrossingShift(best);
                bullet.Range = Mathf.Max(bullet.Range - t, 1f);
                MelonLogger.Msg($"[ATIS] Mermi {best} numarali acik pencereden disari: {bt.position}", $"[SHOT] Bullet sent out through open window {best}: {bt.position}");

                // The game's own fright at a hit judges the shooter by where the player stands, far off
                // in the parked room, and never comes. A ray cannot tell which animal the game will strike
                // (it found terrain under a hit wolf), so every animal's health is noted and compared after.
                s_ShotSource = bullet.m_GunType == GunType.Revolver ? WeaponSource.Revolver : WeaponSource.Rifle;
                s_ShotHp.Clear();
                foreach (var ai in Object.FindObjectsOfType<BaseAi>())
                    if (ai != null && Vector3.Distance(ai.transform.position, bt.position) < bullet.Range)
                        s_ShotHp.Add(new System.Collections.Generic.KeyValuePair<BaseAi, float>(ai, ai.m_CurrentHP));
                s_ShotAi = s_ShotHp.Count > 0 ? s_ShotHp[0].Key : null;
            }
            catch (System.Exception ex)
            {
                s_ShotThroughFailed = true;
                MelonLogger.Warning($"[ATIS] Kapatildi: {ex.Message}", $"[SHOT] Turned off after an error: {ex.Message}");
            }
        }

        // After the bullet's own hit: the struck animal runs or turns, as from a shot outdoors.
        internal static void AfterShotThroughWindow()
        {
            if (s_ShotAi == null || s_ShotThroughFailed) { s_ShotHp.Clear(); return; }
            s_ShotAi = null;
            BaseAi ai = null;
            foreach (var kv in s_ShotHp)
                if (kv.Key != null && kv.Key.m_CurrentHP < kv.Value - 0.01f) { ai = kv.Key; break; }
            s_ShotHp.Clear();
            if (ai == null) { MelonLogger.Msg("[ATIS] Hicbir hayvanin cani dusmedi.", "[SHOT] No animal lost health."); return; }
            try
            {
                var before = ai.GetAiMode();
                if (before == AiMode.Dead || before == AiMode.Flee || before == AiMode.Struggle)
                {
                    MelonLogger.Msg($"[ATIS] {ai.name} vuruldu ({before}).", $"[SHOT] {ai.name} struck ({before}).");
                    return;
                }
                ai.MaybeFleeOrAttackFromProjectileHit(s_ShotSource);
                // A turn to attack has no way to the player in the parked room, so it runs instead.
                var after = ai.GetAiMode();
                if (after != AiMode.Flee && after != AiMode.Dead)
                {
                    ai.SetFleeReason(BaseAi.AiFleeReason.Default);
                    ai.SetAiMode(AiMode.Flee);
                }
                MelonLogger.Msg($"[ATIS] {ai.name} vuruldu: {before} -> {after} -> {ai.GetAiMode()}", $"[SHOT] {ai.name} struck: {before} -> {after} -> {ai.GetAiMode()}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[ATIS] Hayvan tepkisi: {ex.Message}", $"[SHOT] Animal reaction: {ex.Message}");
            }
        }

        // The game's glass colliders taken out of open windows, to put back when they shut.
        private static readonly System.Collections.Generic.List<Collider> s_ShotGlassOff = new System.Collections.Generic.List<Collider>();

        // An arrow, stone or flare crossing an open window, or about to this step, is carried outside.
        internal static void MaybeCarryProjectileThroughWindow(Component projectile)
        {
            if (s_ShotThroughFailed || projectile == null) return;
            if (!AnyWindowOpenForShots()) return;
            try
            {
                var rb = projectile.GetComponent<Rigidbody>();
                if (rb == null) return;
                // A nocked arrow or a stone in the hand moves with the player, not through the window.
                Transform pt = GameManager.GetPlayerTransform();
                Camera cam = GameManager.GetMainCamera();
                if ((pt != null && projectile.transform.IsChildOf(pt)) || (cam != null && projectile.transform.IsChildOf(cam.transform))) return;
                // An arrow flies kinematic, on its own velocity, sweeping from its last place.
                var arrow = projectile.TryCast<ArrowItem>();
                Vector3 cur = projectile.transform.position;
                Vector3 v = arrow != null ? arrow.m_LastVelocity : rb.velocity;
                // Just loosed it has no speed yet, and its first step hits the room's wall over the
                // window (live, 2026-10-18): it is judged on its release speed along its nose instead.
                if (arrow != null && v.sqrMagnitude < 0.25f && rb.velocity.sqrMagnitude < 0.25f) v = projectile.transform.forward * arrow.m_ReleaseVelocity;
                Vector3 from = arrow != null && (arrow.m_LastPosition - cur).sqrMagnitude < 25f ? arrow.m_LastPosition : cur;
                if (rb.isKinematic && arrow == null) return;
                if (v.sqrMagnitude < 0.25f) return;

                // From its last place to three steps and a hand ahead.
                Vector3 to = cur + v * (Time.fixedDeltaTime * 3f) + v.normalized * 0.5f;
                Vector3 seg = to - from;
                float len = seg.magnitude;
                if (len < 0.01f) return;
                int best = FindOpenWindowCrossing(from, seg / len, len, out float t, out Vector3 hit);
                if (best < 0) return;

                Vector3 shift = WindowCrossingShift(best);
                rb.position += shift;
                projectile.transform.position += shift;
                if (arrow != null)
                {
                    arrow.m_LastPosition += shift;
                    // Its trail goes along, so the streak starts at the building's window.
                    var pts = arrow.m_LinePoints;
                    if (pts != null) for (int i = 0; i < pts.Length; i++) pts[i] = pts[i] + shift;
                }
                MelonLogger.Msg($"[ATIS] {projectile.name} {best} numarali acik pencereden disari.", $"[SHOT] {projectile.name} carried out through open window {best}.");
            }
            catch (System.Exception ex)
            {
                s_ShotThroughFailed = true;
                MelonLogger.Warning($"[ATIS] Kapatildi: {ex.Message}", $"[SHOT] Turned off after an error: {ex.Message}");
            }
        }

        // Every frame after the sashes. The game's glass collider fills the whole window whatever the
        // sash does, so arrows glanced off an open one (owner, 2026-10-18). While open, the glass's collider
        // is off and the raised sash carries a box of its own; shut, the glass is given back.
        private static void TickShotWindowColliders()
        {
            if (s_ShotThroughFailed) return;
            try
            {
                var wantOff = new System.Collections.Generic.List<Collider>();
                foreach (var sash in s_WindowSashes)
                {
                    if (sash.Go == null) continue;
                    KeepSashBlock(sash);
                    if (sash.Shown <= 0.01f || sash.Opening >= s_WindowPortalOpenings.Count) continue;
                    var glass = s_WindowPortalOpenings[sash.Opening].Glass;
                    if (glass == null) continue;
                    foreach (var g in glass)
                        if (g != null) foreach (var c in g.GetComponents<Collider>()) if (c != null && !c.isTrigger) wantOff.Add(c);
                }
                for (int i = s_ShotGlassOff.Count - 1; i >= 0; i--)
                {
                    var c = s_ShotGlassOff[i];
                    if (c != null && wantOff.Contains(c)) continue;
                    if (c != null) c.enabled = true;
                    s_ShotGlassOff.RemoveAt(i);
                }
                foreach (var c in wantOff)
                    if (!s_ShotGlassOff.Contains(c)) { c.enabled = false; s_ShotGlassOff.Add(c); }
            }
            catch (System.Exception ex)
            {
                s_ShotThroughFailed = true;
                foreach (var c in s_ShotGlassOff) if (c != null) c.enabled = true;
                s_ShotGlassOff.Clear();
                MelonLogger.Warning($"[ATIS] Kapatildi: {ex.Message}", $"[SHOT] Turned off after an error: {ex.Message}");
            }
        }

        // A thin box over the sash's pane, riding with it.
        private static void KeepSashBlock(WindowSash sash)
        {
            if (sash.Go.transform.Find("SI_SashBlock") != null) return;
            var opening = s_WindowPortalOpenings[sash.Opening];
            float top = sash.RailTop > sash.Seam ? sash.RailTop : sash.Seam + 0.03f;
            var go = new GameObject("SI_SashBlock");
            go.layer = 0;
            go.transform.SetParent(sash.Go.transform, false);
            // Placed in world space for the shut sash, less the slide it already has.
            Vector3 slid = sash.Up * (sash.Slide * Mathf.Max(sash.Shown, 0f));
            go.transform.position = opening.GlassCenter + sash.Right * ((sash.EdgeLeft + sash.EdgeRight) * 0.5f)
                                  + sash.Up * ((sash.EdgeBottom + top) * 0.5f) + sash.Outward * sash.SashMid + slid;
            go.transform.rotation = Quaternion.LookRotation(sash.Outward, sash.Up);
            Vector3 ls = go.transform.lossyScale;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3((sash.EdgeRight - sash.EdgeLeft) / Mathf.Max(ls.x, 0.001f), (top - sash.EdgeBottom) / Mathf.Max(ls.y, 0.001f), 0.03f / Mathf.Max(ls.z, 0.001f));
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
        public static void Postfix() { SeamlessInteriorsMod.AfterShotThroughWindow(); }
    }

    // ProcessFlee ends a flight as soon as the player is indoors, and the player in a room stands
    // indoors: an animal shot through a window turned back to wandering within half a second (live,
    // 2026-10-18). While the player is in a room, a fleeing animal is told the player is outdoors.
    [HarmonyPatch(typeof(BaseAi), nameof(BaseAi.ProcessFlee))]
    public class BaseAiFleePatch
    {
        public static void Prefix() { SeamlessInteriorsMod.s_FleeSeesOutdoors = SeamlessInteriorsMod.s_IsPlayerInsideClone; }
        public static void Postfix() { SeamlessInteriorsMod.s_FleeSeesOutdoors = false; }
    }

    // ProcessFlee asks the native method directly, past the managed hook in PlaceContext.
    [HarmonyPatch(typeof(Weather), nameof(Weather.IsIndoorEnvironment))]
    public class FleeSeesOutdoorsPatch
    {
        public static void Postfix(ref bool __result) { if (SeamlessInteriorsMod.s_FleeSeesOutdoors) __result = false; }
    }

    [HarmonyPatch(typeof(ArrowItem), nameof(ArrowItem.FixedUpdate))]
    public class ArrowThroughWindowPatch
    {
        public static void Prefix(ArrowItem __instance) { SeamlessInteriorsMod.MaybeCarryProjectileThroughWindow(__instance); }
    }

    [HarmonyPatch(typeof(ArrowItem), nameof(ArrowItem.Fire))]
    public class ArrowFiredThroughWindowPatch
    {
        public static void Postfix(ArrowItem __instance)
        {
            SeamlessInteriorsMod.MaybeCarryProjectileThroughWindow(__instance);
        }
    }

    [HarmonyPatch(typeof(ArrowItem), nameof(ArrowItem.LateUpdate))]
    public class ArrowThroughWindowLatePatch
    {
        public static void Prefix(ArrowItem __instance) { SeamlessInteriorsMod.MaybeCarryProjectileThroughWindow(__instance); }
    }

    [HarmonyPatch(typeof(StoneItem), nameof(StoneItem.FixedUpdate))]
    public class StoneThroughWindowPatch
    {
        public static void Prefix(StoneItem __instance) { SeamlessInteriorsMod.MaybeCarryProjectileThroughWindow(__instance); }
    }

    [HarmonyPatch(typeof(FlareGunRoundItem), nameof(FlareGunRoundItem.FixedUpdate))]
    public class FlareThroughWindowPatch
    {
        public static void Prefix(FlareGunRoundItem __instance) { SeamlessInteriorsMod.MaybeCarryProjectileThroughWindow(__instance); }
    }
}
