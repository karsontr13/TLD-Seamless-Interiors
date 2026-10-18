using Il2Cpp;
using Il2CppTLD.Interactions;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── CLEANING A WINDOW BY HAND ───
        //
        // Owner, 2026-10-01: the player cleans one window at a time, and the mod remembers it. Every
        // frosted window the player can see the real view through gets an invisible target in front of
        // its glass: a thin box collider on the InteractiveProp layer carrying the game's own
        // TimedHoldInteraction, so the crosshair shows the game's hover prompt and the hold fills the
        // game's own progress ring. No injected class and no patch: the game finds the target by its
        // own raycast, and HoldEnd calls back into the mod.
        //
        // What the hold does goes through the shared frost record (WindowFrostState.cs), so a window
        // cleaned from inside is clean from outside and the other way round, and it is written with the
        // next game save. Inside, that is SetWindowPaneCleared, which also records room windows with no
        // window outside under their own number (1001-02). Outside, SetWindowCleaned on the building
        // window.
        //
        // A target exists only while its window is frosted, so a clean window offers nothing. The
        // targets follow whatever the mod has dressed: the room the player is in, or the one building
        // whose room is borrowed for its windows from outside. They are rebuilt when that changes and
        // removed when the "Clean windows by hand" setting is off.
        //
        // Run 2026-10-01: the prompt showed and a full hold cleaned the window (owner: "Cleaning also
        // works"). Changed the same day by his eye: no hover text, and letting go cancels.

        private const float CLEAN_WINDOW_HOLD_SECONDS = 2f;
        private const float CLEAN_TARGET_PROUD = 0.03f;   // in front of the glass, toward the player
        private const float CLEAN_TARGET_DEPTH = 0.04f;
        // No hover text (owner, 2026-10-01: "It's very annoying to be looking through even a frosted
        // window and have that pop up in the middle of your view"). The hold's own "Cleaning" says what
        // is happening once the player presses.
        private const string CLEAN_WINDOW_HOVER = "";
        private const string CLEAN_WINDOW_HOLD = "Cleaning";

        // THE SOUND FOLLOWS THE PROGRESS RING (owner, 2026-10-01: "I just want the sound to match the
        // progress bar"). Play_RepairingCloth, the game's sewing sound, picked by ear from four cloth
        // loops, at a quarter of its level from the window's own target (his pick, A/B against full).
        // It starts with the hold and fades out over the hold's last quarter second, so it ends when
        // the ring fills; letting go stops it at once.
        //
        // Tried first and REVERSED the same day: timing the hold to the sound. The event is a
        // shuffled container of 11 takes (1.37 to 2.96 s) that loops until stopped; Wwise reports
        // each take's length as it starts, and the first set the hold's time. The cleaning then ended
        // with the take, but the ring keeps the time the hold STARTED with, so it ended early or
        // late (traced: hold set to 1.72 s, ring still on 2 s; a long take made him let go at a full
        // ring and cancel). Knowing the take before the press meant starting the sound paused on aim,
        // which he judged too much machinery for the result. See HISTORY 2026-10-01.
        private const string CLEAN_WINDOW_SOUND = "Play_RepairingCloth";
        private const float CLEAN_WINDOW_SOUND_GAIN = 0.25f;
        private const float CLEAN_WINDOW_SOUND_FADE_SECONDS = 0.25f;

        public static bool IsCleanWindowsByHandEnabled => s_PrefCleanWindowsByHand?.Value ?? true;

        private class CleanTarget
        {
            public GameObject Go;
            public bool Inside;     // a room window (index into s_WindowPortalOpenings) or a building window
            public int Index;
            public TimedHoldInteraction Hold;
            public bool Cancelled;  // this hold was let go of; its end must not clean
            public uint SoundId;    // the playing sound, 0 when silent
            public bool SoundFading;
            public bool Frosted = true;
            public Vector3 Position;
            public readonly List<Collider> Doors = new List<Collider>();   // door colliders over this glass
        }

        private static readonly List<CleanTarget> s_CleanTargets = new List<CleanTarget>();
        private const float CLEAN_STATE_SECONDS = 0.25f;
        private static float s_CleanStateNextAt;
        // The target whose hold is running, if any.
        private static CleanTarget s_CleanHeldTarget;
        private static string s_CleanTargetsKey;
        private static bool s_CleanTargetsFailed;

        // Every frame from OnUpdate. Rebuilds only when what is dressed changes; otherwise it is one
        // bool per window.
        private static void TickWindowClearing()
        {
            long perf = PerfProbe.Begin();
            try { TickWindowClearingTimed(); } finally { PerfProbe.End(PerfProbe.Section.CoverClean, perf); }
        }

        private static void TickWindowClearingTimed()
        {
            if (s_CleanTargetsFailed) return;
            try
            {
                string key = CleanTargetsKey();
                if (key != s_CleanTargetsKey)
                {
                    DestroyCleanTargets();
                    s_CleanTargetsKey = key;
                    if (key != null) BuildCleanTargets();
                }

                TickDoorWindowCleanKey();
                GuardCleanTargetFromDoor();

                // Which windows can be wiped changes slowly: asked four times a second, not every frame
                // for every window (27 at the farmhouse; the walk was the feature's largest steady cost).
                float now = Time.realtimeSinceStartup;
                if (now >= s_CleanStateNextAt)
                {
                    s_CleanStateNextAt = now + CLEAN_STATE_SECONDS;
                    for (int i = 0; i < s_CleanTargets.Count; i++)
                    {
                        var t = s_CleanTargets[i];
                        if (t.Go == null) { s_CleanTargetsKey = null; return; }   // gone with its scene: rebuild next frame
                        bool frosted = t.Inside ? !IsWindowPaneCleared(t.Index) : !IsWindowCleaned(s_OutsideInInstanceId, t.Index);
                        // A window frosting over again can be wiped before it is full.
                        if (t.Inside && WindowCoverLayers && !frosted) { float f, s; GetRoomPaneCover(t.Index, out f, out s); frosted = f >= 0.2f; }
                        // Outside, a window wiped from inside still has its snow to wipe.
                        if (!t.Inside && WindowCoverLayers && !frosted) { float f, s; GetWindowCover(s_OutsideInInstanceId, t.Index, false, out f, out s); frosted = f >= 0.2f || s > 0.02f; }
                        if (t.Frosted != frosted || t.Go.activeSelf != frosted) t.Go.SetActive(frosted);
                        t.Frosted = frosted;
                    }
                }

                // Every frame, only the window being wiped.
                var held = s_CleanHeldTarget;
                if (held != null && held.Go != null)
                {
                    var t = held;
                    bool frosted = t.Frosted;

                    // LETTING GO STOPS IT. The game's hold does not cancel on release by itself (its
                    // CanCancel answered true from a patch did nothing either); the lantern's does, so
                    // this one is made to. The game's own interact release, which follows the player's
                    // bindings; proven live with the fire release and the raw mouse button beside it,
                    // all three seeing the same release (2026-10-01).
                    if (frosted && t.Hold != null && t.Hold.IsActive && InputManager.GetInteractReleased(t.Hold))
                    {
                        t.Cancelled = true;
                        t.Hold.CancelHold();
                    }

                    // The sound's fade starts so that it ends with the ring.
                    if (t.SoundId != 0 && !t.SoundFading && t.Hold != null && t.Hold.IsActive
                        && t.Hold.m_Timer >= t.Hold.HoldTime - CLEAN_WINDOW_SOUND_FADE_SECONDS)
                    {
                        t.SoundFading = true;
                        try { AkSoundEngine.StopPlayingID(t.SoundId, (int)(CLEAN_WINDOW_SOUND_FADE_SECONDS * 1000f), AkCurveInterpolation.AkCurveInterpolation_Linear); } catch { }
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Retire on the first failure rather than throw every frame; the windows keep working.
                s_CleanTargetsFailed = true;
                try { DestroyCleanTargets(); } catch { }
                MelonLogger.Warning($"[PENCERE-TEMIZLE] Kapatildi: {ex.Message}",
                                    $"[WINDOW-CLEAN] Turned off after an error: {ex.Message}");
            }
        }

        // A DOOR'S TRIGGER BEHIND THE WALL WINS THE CROSSHAIR. Trapper's door trigger is a 2 x 3 x 2 m box
        // that takes in the window beside the door, and the game chose it over the nearer target: a click
        // on the window went in (owner, 2026-10-13). While the crosshair rests on a target, a door trigger
        // further along the same line is switched off, and on again as soon as it does not.
        private const float CLEAN_DOOR_GUARD_REACH = 4f;
        private static Collider s_CleanDoorSuppressed;

        private static void GuardCleanTargetFromDoor()
        {
            Collider door = null;
            bool onTarget = false;
            Camera main = GameManager.GetMainCamera();
            // No wipeable window within reach: no ray (plain float math, from where each target was put).
            bool near = false;
            if (main != null)
            {
                Vector3 eye = main.transform.position;
                float reach = CLEAN_DOOR_GUARD_REACH * CLEAN_DOOR_GUARD_REACH;
                foreach (var t in s_CleanTargets)
                {
                    if (!t.Frosted) continue;
                    float dx = t.Position.x - eye.x, dy = t.Position.y - eye.y, dz = t.Position.z - eye.z;
                    if (dx * dx + dy * dy + dz * dz < reach) { near = true; break; }
                }
            }
            if (main != null && near)
            {
                var hits = Physics.RaycastAll(main.transform.position, main.transform.forward, CLEAN_DOOR_GUARD_REACH, ~0, QueryTriggerInteraction.Collide);
                float target = float.MaxValue, solid = float.MaxValue;
                foreach (var hit in hits)
                {
                    var c = hit.collider;
                    if (c == null) continue;
                    if (c.name.StartsWith("SI_CleanWindow_")) { if (hit.distance < target) target = hit.distance; }
                    else if (!c.isTrigger && c.gameObject.layer != vp_Layer.Player && hit.distance < solid) solid = hit.distance;
                }
                onTarget = target < solid;
                if (onTarget && s_CleanDoorSuppressed == null)
                    foreach (var hit in hits)
                        if (hit.collider != null && hit.collider.isTrigger && hit.distance > target && hit.collider.GetComponent<LoadScene>() != null) { door = hit.collider; break; }
            }

            if (s_CleanDoorSuppressed != null && !onTarget) ReleaseCleanDoor();
            if (door != null) { door.enabled = false; s_CleanDoorSuppressed = door; }
        }

        private static void ReleaseCleanDoor()
        {
            if (s_CleanDoorSuppressed != null && !s_DoorWindowCleanDoors.Contains(s_CleanDoorSuppressed)) s_CleanDoorSuppressed.enabled = true;
            s_CleanDoorSuppressed = null;
        }

        // A DOOR'S WINDOW SITS INSIDE THE DOOR'S OWN COLLIDER. Community Hall's front door: a solid
        // 1.6 x 3 x 2 m box around the glass, so the crosshair always found the door (owner, 2026-10-18).
        // His pick: a key turns those doors over to cleaning, and pressing it again gives them back.
        private static bool s_DoorWindowCleanMode;
        private static readonly List<Collider> s_DoorWindowCleanDoors = new List<Collider>();

        private static void TickDoorWindowCleanKey()
        {
            KeyCode key = s_PrefDoorWindowCleanKey?.Value ?? KeyCode.Z;
            if (key == KeyCode.None || !Input.GetKeyDown(key)) return;
            bool anyDoorGlass = false;
            foreach (var t in s_CleanTargets) if (t.Doors.Count > 0) { anyDoorGlass = true; break; }
            if (!anyDoorGlass && !s_DoorWindowCleanMode) return;

            SetDoorWindowCleanMode(!s_DoorWindowCleanMode);
            s_LightingModeMessage = s_DoorWindowCleanMode ? "Door windows: cleaning" : "Doors: enter and exit";
            s_LightingModeMessageTimer = 2.5f;
        }

        private static void SetDoorWindowCleanMode(bool on)
        {
            ReleaseCleanDoor();
            foreach (var door in s_DoorWindowCleanDoors) if (door != null) door.enabled = true;
            s_DoorWindowCleanDoors.Clear();
            s_DoorWindowCleanMode = on;
            if (!on) return;
            foreach (var t in s_CleanTargets)
                foreach (var door in t.Doors)
                    if (door != null && door.enabled && !s_DoorWindowCleanDoors.Contains(door)) { door.enabled = false; s_DoorWindowCleanDoors.Add(door); }
        }

        // The doors (LoadScene) whose collider covers this target's glass. Community Hall's is a 13 cm slab
        // 8 cm in front of the target, so the search reaches 30 cm either side of the glass.
        private const float DOOR_OVER_GLASS_REACH = 0.3f;

        private static void FindDoorsOverGlass(CleanTarget target, BoxCollider box)
        {
            var half = new Vector3(box.size.x * 0.5f, box.size.y * 0.5f, DOOR_OVER_GLASS_REACH);
            var hits = Physics.OverlapBox(target.Position, half, target.Go.transform.rotation, ~0, QueryTriggerInteraction.Collide);
            foreach (var c in hits)
                if (c != null && c != box && c.GetComponent<LoadScene>() != null && !target.Doors.Contains(c)) target.Doors.Add(c);
        }

        // What the targets are built for: null when there is nothing to clean or the setting is off.
        private static string CleanTargetsKey()
        {
            if (!IsCleanWindowsByHandEnabled) return null;
            if (s_WindowPortalInstanceId != null && s_WindowPortalOpenings.Count > 0)
            {
                Vector3 c = s_WindowPortalOpenings[0].GlassCenter;
                return "in:" + s_WindowPortalInstanceId + ":" + s_WindowPortalOpenings.Count + ":" + c.x.ToString("F2") + "," + c.z.ToString("F2");
            }
            if (s_OutsideInInstanceId != null && s_OutsideInBorrowed && s_OutsideInApertures.Count > 0)
            {
                Vector3 c = s_OutsideInApertures[0].Center;
                return "out:" + s_OutsideInInstanceId + ":" + s_OutsideInApertures.Count + ":" + c.x.ToString("F2") + "," + c.z.ToString("F2");
            }
            return null;
        }

        private static void BuildCleanTargets()
        {
            if (s_CleanTargetsKey.StartsWith("in:"))
            {
                for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
                {
                    var op = s_WindowPortalOpenings[i];
                    float w = op.GlassWidth > 0f ? op.GlassWidth : op.Width;
                    float h = op.GlassHeight > 0f ? op.GlassHeight : op.Height;
                    // The room side of the glass: against Outward.
                    AddCleanTarget(true, i, op.GlassCenter - op.Outward * CLEAN_TARGET_PROUD, op.Outward, op.Up, w, h);
                }
            }
            else
            {
                for (int i = 0; i < s_OutsideInApertures.Count; i++)
                {
                    var ap = s_OutsideInApertures[i];
                    AddCleanTarget(false, i, ap.Center + ap.Outward * CleanTargetProudOutside(ap), ap.Outward, ap.Up, ap.Width, ap.Height);
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PENCERE-TEMIZLE] {s_CleanTargetsKey}: {s_CleanTargets.Count} hedef.",
                                $"[WINDOW-CLEAN] {s_CleanTargetsKey}: {s_CleanTargets.Count} clean-window targets.");
        }

        // The farmhouse's collider runs flat over its windows 10 cm out from the glass, and the target behind
        // it was never reached by the game's ray (owner, 2026-10-13). A solid face in front of the glass puts
        // the target just in front of it.
        private const float CLEAN_TARGET_PROBE = 0.5f, CLEAN_TARGET_WALL_REACH = 0.2f;

        private static float CleanTargetProud(Vector3 glass, Vector3 outward)
        {
            float proud = CLEAN_TARGET_PROUD;
            foreach (var hit in Physics.RaycastAll(glass + outward * CLEAN_TARGET_PROBE, -outward, CLEAN_TARGET_PROBE + 0.01f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.collider.name.StartsWith("SI_CleanWindow_")) continue;
                if (hit.collider.gameObject.layer == vp_Layer.Player) continue;
                float front = CLEAN_TARGET_PROBE - hit.distance;
                // Further out it is a porch post or a rail, not the wall over the glass.
                if (front > CLEAN_TARGET_WALL_REACH) continue;
                if (front + CLEAN_TARGET_DEPTH * 0.5f + 0.005f > proud) proud = front + CLEAN_TARGET_DEPTH * 0.5f + 0.005f;
            }
            return proud;
        }

        private static float CleanTargetProudOutside(ShellAperture ap) { return CleanTargetProud(ap.Center, ap.Outward); }

        private static void AddCleanTarget(bool inside, int index, Vector3 center, Vector3 outward, Vector3 up, float width, float height)
        {
            if (width <= 0.05f || height <= 0.05f) return;

            var go = new GameObject((inside ? "SI_CleanWindow_Room_" : "SI_CleanWindow_Building_") + index);
            // Still found by the interaction ray, but the player walks through it: on InteractiveProp
            // the box stood out of a corridor window and blocked the way (Milton F3, 2026-10-09).
            go.layer = vp_Layer.InteractivePropNoCollidePlayer;
            go.transform.position = center;
            go.transform.rotation = Quaternion.LookRotation(outward, up);

            // Registered here, so the quarter level can be set on it before its first sound.
            try { AkSoundEngine.RegisterGameObj(go, go.name); } catch { }

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, CLEAN_TARGET_DEPTH);

            // Set before Start runs, which copies the defaults into the live values.
            var hold = go.AddComponent<TimedHoldInteraction>();
            hold.m_DefaultHoverText = new LocalizedString { m_LocalizationID = CLEAN_WINDOW_HOVER };
            hold.m_DefaultHoldText = new LocalizedString { m_LocalizationID = CLEAN_WINDOW_HOLD };
            hold.m_DefaultHoldTime = CLEAN_WINDOW_HOLD_SECONDS;

            var target = new CleanTarget { Go = go, Inside = inside, Index = index, Hold = hold, Position = center };
            System.Action<BaseInteraction> begin = (bi) => { target.Cancelled = false; s_CleanHeldTarget = target; StartCleanSound(target); };
            System.Action<BaseInteraction> cancelled = (bi) => { target.Cancelled = true; StopCleanSound(target); if (s_CleanHeldTarget == target) s_CleanHeldTarget = null; };
            System.Action<BaseInteraction> done = (bi) => { StopCleanSound(target); OnWindowCleanedByHand(target); if (s_CleanHeldTarget == target) s_CleanHeldTarget = null; s_CleanStateNextAt = 0f; };
            hold.AddEventCallback(InteractionEventType.HoldBegin, begin);
            hold.AddEventCallback(InteractionEventType.HoldCancelled, cancelled);
            hold.AddEventCallback(InteractionEventType.HoldEnd, done);

            FindDoorsOverGlass(target, box);
            s_CleanTargets.Add(target);
        }

        private static void StartCleanSound(CleanTarget target)
        {
            try
            {
                StopCleanSound(target);
                if (target.Go == null) return;
                Camera main = GameManager.GetMainCamera();
                var listener = main != null ? main.GetComponent<AkAudioListener>() : null;
                if (listener != null) AkSoundEngine.SetGameObjectOutputBusVolume(target.Go, listener.gameObject, CLEAN_WINDOW_SOUND_GAIN);
                target.SoundFading = false;
                target.SoundId = GameAudioManager.PlaySound(CLEAN_WINDOW_SOUND, target.Go);
            }
            catch (System.Exception ex)
            {
                // The hold works without its sound.
                MelonLogger.Warning($"[PENCERE-TEMIZLE] Ses calinamadi: {ex.Message}",
                                    $"[WINDOW-CLEAN] The cleaning sound could not play: {ex.Message}");
            }
        }

        private static void StopCleanSound(CleanTarget target)
        {
            if (target == null || target.SoundId == 0) return;
            // Already fading out with the ring: let the fade finish.
            if (!target.SoundFading)
            {
                try { AkSoundEngine.StopPlayingID(target.SoundId, 50, AkCurveInterpolation.AkCurveInterpolation_Linear); } catch { }
            }
            target.SoundId = 0;
            target.SoundFading = false;
        }

        private static void OnWindowCleanedByHand(CleanTarget target)
        {
            // HoldEnd also fires when a hold is cancelled (seen live: let go, and the window cleaned
            // anyway). Only a hold that ran its full time cleans.
            if (target.Cancelled || (target.Hold != null && target.Hold.NormalizedProgress < 0.98f))
            {
                target.Cancelled = false;
                return;
            }
            try
            {
                string id;
                if (target.Inside)
                {
                    id = s_WindowPortalInstanceId;
                    SetWindowPaneCleared(target.Index, true);
                }
                else
                {
                    id = s_OutsideInInstanceId;
                    SetWindowCleaned(id, target.Index, true);
                    ClearWindowSnow(id, target.Index, false);
                }
                if (target.Go != null) target.Go.SetActive(false);
                // A cleaned door window gives its door straight back (owner, 2026-10-18: he could not go out after).
                if (s_DoorWindowCleanMode && target.Doors.Count > 0)
                {
                    SetDoorWindowCleanMode(false);
                    s_LightingModeMessage = "Doors: enter and exit";
                    s_LightingModeMessageTimer = 2.5f;
                }

                MelonLogger.Msg($"[PENCERE-TEMIZLE] {id}: {(target.Inside ? "oda" : "bina")} penceresi {target.Index} elle temizlendi.",
                                $"[WINDOW-CLEAN] {id}: {(target.Inside ? "room" : "building")} window {target.Index} cleaned by hand.");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[PENCERE-TEMIZLE] Temizlenemedi: {ex.Message}",
                                    $"[WINDOW-CLEAN] The window could not be cleaned: {ex.Message}");
            }
        }

        private static void DestroyCleanTargets()
        {
            SetDoorWindowCleanMode(false);
            s_CleanHeldTarget = null;
            s_CleanStateNextAt = 0f;
            for (int i = 0; i < s_CleanTargets.Count; i++)
            {
                StopCleanSound(s_CleanTargets[i]);
                var go = s_CleanTargets[i].Go;
                if (go == null) continue;
                try { AkSoundEngine.UnregisterGameObj(go); } catch { }
                Object.Destroy(go);
            }
            s_CleanTargets.Clear();
        }
    }
}
