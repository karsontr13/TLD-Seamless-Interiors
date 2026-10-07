using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── A PARKED ROOM HAS NO FOG, AND ITS WINDOWS STILL DO ───
        //
        // The game's fog is global: RenderSettings.fog, ExponentialSquared, 0.055 in a blizzard. Vanilla
        // never switches it off indoors and gets away with it because its same-scene rooms are a few
        // meters across. The hangar is not: standing in it in a blizzard it was a whiteout
        // (2026-09-22). Weather.m_IsIndoors = Indoors does clear it, found 2026-09-14, but it clears it
        // by driving the GLOBAL density to zero, which took the fog out of the window views too.
        // Tried again 2026-09-22, owner: "that cleared the camera view too."
        //
        // FPSCamera renders deferred, and in deferred the fog on anything opaque is drawn by one pass
        // of its PostProcessLayer, switched by fog.enabled. The window cameras run their own copy of
        // that pass, counted from the glass (WindowViewFog.cs), which this flag does not touch. So
        // switching off only the player's pass clears the room and leaves the fog in the views. Tried in the console
        // with the owner standing in the hangar in a blizzard: "That worked beautifully."
        //
        // NOT PostProcessLayer.enabled. Switching the whole layer off on FPSCamera broke the view and
        // forced a relaunch the same afternoon. Only the fog member is touched.
        //
        // Only while the stand-in eye is in force, that is inside a PARKED room. A room left in place
        // has real openings onto the outdoors in the player's own view, and those must keep the fog.
        private static PostProcessLayer s_ParkedFogLayer;
        private static bool s_ParkedFogSavedEnabled;

        internal static void TickParkedRoomFog(Camera main)
        {
            if (!ParkedCullEyeInForce || main == null)
            {
                ReleaseParkedRoomFog();
                return;
            }

            if (s_ParkedFogLayer == null)
            {
                var layer = main.GetComponent<PostProcessLayer>();
                if (layer == null || layer.fog == null) return;
                s_ParkedFogLayer = layer;
                s_ParkedFogSavedEnabled = layer.fog.enabled;
            }

            // Re-applied every frame rather than once: the game could write it back, and a read of a
            // managed bool field is cheap.
            if (s_ParkedFogLayer.fog.enabled) s_ParkedFogLayer.fog.enabled = false;
            HookParkedSheetFog();
        }

        // ─── AND THE WINDOW SHEETS: THE PLAYER'S CAMERA PAINTS A PARKED ROOM WITH NO FOG AT ALL ───
        //
        // The pass above clears the room's OPAQUE surfaces. The window sheets the views are shown on
        // are Unlit/Texture at queue 3001, drawn forward, and take Unity's built-in fog by the
        // player's distance to the glass: from 22.8 m in a blizzard (0.055) that laid 79% fog over
        // the whole window, on top of the fog already in the picture, and it cleared as he walked
        // closer (owner, 2026-09-27: "Fog clears as I walk closer"; switching the snow off changed
        // nothing). So while FPSCamera paints in a parked room, RenderSettings.fog is off, and it is
        // put back straight after, before any window camera paints its outdoor view with it. Tried
        // in the console first: "I think you got it!" (LOG-2026-09-27 0927-13).
        private static Camera.CameraCallback s_SheetFogPreCull, s_SheetFogPostRender;
        private static bool s_SheetFogHooked, s_SheetFogOff, s_SheetFogWas;

        private static void HookParkedSheetFog()
        {
            if (s_SheetFogHooked) return;
            if (s_SheetFogPreCull == null)
            {
                s_SheetFogPreCull = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnSheetFogPreCull));
                s_SheetFogPostRender = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnSheetFogPostRender));
            }
            Camera.onPreCull += s_SheetFogPreCull;
            Camera.onPostRender += s_SheetFogPostRender;
            s_SheetFogHooked = true;
        }

        private static void UnhookParkedSheetFog()
        {
            if (!s_SheetFogHooked) return;
            Camera.onPreCull -= s_SheetFogPreCull;
            Camera.onPostRender -= s_SheetFogPostRender;
            s_SheetFogHooked = false;
            if (s_SheetFogOff) { RenderSettings.fog = s_SheetFogWas; s_SheetFogOff = false; }
        }

        private static void OnSheetFogPreCull(Camera camera)
        {
            try
            {
                if (!ParkedCullEyeInForce || s_SheetFogOff || camera == null || camera != GameManager.GetMainCamera()) return;
                s_SheetFogWas = RenderSettings.fog;
                RenderSettings.fog = false;
                s_SheetFogOff = true;
            }
            catch (System.Exception e) { MelonLogger.Warning("[PARK-FOG] The window sheets' fog could not be switched off: " + e.Message); }
        }

        private static void OnSheetFogPostRender(Camera camera)
        {
            if (!s_SheetFogOff) return;
            try
            {
                if (camera != GameManager.GetMainCamera()) return;
                RenderSettings.fog = s_SheetFogWas;
                s_SheetFogOff = false;
            }
            catch (System.Exception e) { s_SheetFogOff = false; MelonLogger.Warning("[PARK-FOG] The fog could not be switched back on after the player's camera: " + e.Message); }
        }

        // ─── THE SNOW IS BORN AT THE GROUND, NOT IN THE ROOM ───
        //
        // Moving the game's snow box down (Patches/ParkedSnowBoxPatches.cs) decides where flakes are
        // KEPT. Where they are BORN is the emitter: Snow_Falling is a child of FPSCamera, and
        // Snow_Blowing is placed at the player's feet, so in a parked room both kept making flakes
        // 40 m up, in the room. Owner, 2026-09-22, after the box fix: snow still visible at the far
        // end of the room, "because, like I said, it's still up here where the prefab is parked",
        // and "we wouldn't need the stand-in if we just moved the snow down to the ground where it's
        // supposed to be." Measured in the console with both emitters moved: flakes centered 39.7 m
        // below the eye, emitter 40.0 m below. In use since, and the room is clear of snow (2ef6eee).
        //
        // Placed here, from OnLateUpdate, after the game has positioned its snow for the frame and
        // before the particle update emits: world-space particles are born where the emitter
        // stands at that moment. Both go back on the player when the stand-in eye is released.
        private static bool s_ParkedSnowEmittersMoved;

        internal static void TickParkedSnowEmitters()
        {
            var weather = GameManager.GetWeatherComponent();
            if (weather == null) return;
            GameObject falling = weather.m_FallingSnow;
            GameObject blowing = weather.m_BlowingSnow;
            Transform player = GameManager.GetPlayerTransform();
            Camera main = GameManager.GetMainCamera();

            if (!ParkedCullEyeInForce || main == null || player == null)
            {
                ReleaseParkedFlakeSize();
                if (!s_ParkedSnowEmittersMoved) return;
                s_ParkedSnowEmittersMoved = false;
                if (falling != null) falling.transform.localPosition = Vector3.zero;
                if (blowing != null && player != null) blowing.transform.position = player.position;
                return;
            }

            Vector3 down = ParkedCullEyeOffset;
            if (falling != null) falling.transform.position = main.transform.position - down;
            if (blowing != null) blowing.transform.position = player.position - down;
            s_ParkedSnowEmittersMoved = true;
            TickParkedFlakeSize();
        }

        // ─── IN A PARKED ROOM THE FALLING FLAKES ARE BORN AT HALF SIZE ───
        //
        // Seen through a window, the snow has no foreground: everything from the eye to the glass is
        // room, so only flakes out past the glass show, and at the game's 0.3 m they looked far too
        // big from across the room (owner, 2026-09-27: "when I'm looking at a distant window, the slow
        // flakes appear as big as they would be if I was against the glass"). Neither the snow's
        // settings, its job, its shader, its simulation space nor the window cameras size flakes by
        // distance (LOG-2026-09-27 0927-19 to 0927-22), so the cause is not pinned down; the size is
        // simply what he judged. Half size, 0.15 m, from about 36 m back: "It looks pretty good!", and
        // a distance-based size was offered and declined: "it looks pretty good the way it is now."
        // Only while the stand-in eye is in force: the player's own view shows no snow inside a
        // parked room, so only the window views change, and the game's size is put back on leaving.
        // New flakes take the size as they are born; old ones live out their 12 s.
        private const float PARKED_FLAKE_SIZE_SCALE = 0.5f;
        private static float s_ParkedFlakeGameSize = -1f, s_ParkedFlakeApplied = -1f;

        private static void TickParkedFlakeSize()
        {
            var ps = FallingSnowSystem();
            if (ps == null) return;
            var main = ps.main;
            float now = main.startSize.constant;
            // Anything but our own value is the game's (a weather change may set it): take it as the size to halve.
            if (!Mathf.Approximately(now, s_ParkedFlakeApplied)) s_ParkedFlakeGameSize = now;
            float want = s_ParkedFlakeGameSize * PARKED_FLAKE_SIZE_SCALE;
            if (!Mathf.Approximately(now, want)) main.startSize = new ParticleSystem.MinMaxCurve(want);
            s_ParkedFlakeApplied = want;
        }

        private static void ReleaseParkedFlakeSize()
        {
            if (s_ParkedFlakeGameSize < 0f) return;
            var ps = FallingSnowSystem();
            if (ps != null)
            {
                var main = ps.main;
                if (Mathf.Approximately(main.startSize.constant, s_ParkedFlakeApplied)) main.startSize = new ParticleSystem.MinMaxCurve(s_ParkedFlakeGameSize);
            }
            s_ParkedFlakeGameSize = -1f; s_ParkedFlakeApplied = -1f;
        }

        private static ParticleSystem FallingSnowSystem()
        {
            var uniStorm = GetCachedUniStorm();
            return uniStorm != null && uniStorm.m_WeatherParticleManager != null ? uniStorm.m_WeatherParticleManager.m_FallingSnowSystem : null;
        }

        // ─── THE SNOW PLANES ARE SWITCHED OFF IN A PARKED ROOM ───
        //
        // SnowPlaneManager draws sheets of snowflakes at fixed depths round the player's camera
        // (m_LayerDepths), on the TransparentFX layer. In a parked room they stood in the room, so
        // the owner saw "normal snowflakes just inside the building and only in a thin region of
        // depth that's far away", which disappeared when he looked up. It was NOT the falling snow
        // (hidden with no change), not reachable by a killer (the room's killer shrunk from 115 m to
        // 1 m with no change), and not the window views (switched off with no change). Found on
        // 2026-09-22 by hiding the camera's layers by halves: gone with TransparentFX hidden, and
        // gone with only the Planes renderer hidden.
        //
        // Moving the planes down to the stand-in eye each LateUpdate did NOT work in the console:
        // the snow was back on the far wall. The game places or draws them somewhere that override
        // does not reach (it has OnRenderObject and a command buffer). So in a parked room they are
        // simply off, and they come back to what the game wants (SnowPlaneManager.IsEnabled) when
        // the room is left. The blizzard in the window views comes from the falling and blowing
        // snow and the fog, and was approved before the planes were touched.
        private static SnowPlaneManager s_SnowPlanes;
        private static bool s_SnowPlanesHidden;

        internal static void TickParkedSnowPlanes()
        {
            if (!ParkedCullEyeInForce)
            {
                if (!s_SnowPlanesHidden) return;
                s_SnowPlanesHidden = false;
                if (s_SnowPlanes != null && s_SnowPlanes.m_MeshRenderer != null)
                    s_SnowPlanes.m_MeshRenderer.enabled = s_SnowPlanes.IsEnabled;
                return;
            }

            if (s_SnowPlanes == null)
            {
                s_SnowPlanes = Object.FindObjectOfType<SnowPlaneManager>();
                if (s_SnowPlanes == null) return;
            }

            var renderer = s_SnowPlanes.m_MeshRenderer;
            if (renderer == null) return;
            if (renderer.enabled) renderer.enabled = false;
            s_SnowPlanesHidden = true;
        }

        // How far forward the snow box starts (Patches/ParkedSnowBoxPatches.cs): the depth along the
        // gaze of the nearest room window glass on screen, less 0.3 m; 0 when none is in view. On screen
        // is the wall cameras' own flag, or for a window with none, the glass's middle.
        internal static float NearestWindowGlassAlongGaze(Camera main)
        {
            if (main == null || s_WindowPortalOpenings.Count == 0) return 0f;
            Vector3 eye = main.transform.position, gaze = main.transform.forward;
            bool flags = s_WallCamerasActive && s_WallMemberOnScreen.Count == s_WindowPortalOpenings.Count;
            float nearest = float.MaxValue;
            for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
            {
                Vector3 glass = s_WindowPortalOpenings[i].GlassCenter;
                if (!(flags && s_WallMemberOnScreen[i]))
                {
                    // A window alone on its own camera has no wall flag: its glass's middle decides.
                    Vector3 v = main.WorldToViewportPoint(glass);
                    if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
                }
                float d = Vector3.Dot(glass - eye, gaze);
                if (d > 0f && d < nearest) nearest = d;
            }
            return nearest == float.MaxValue ? 0f : Mathf.Max(0f, nearest - 0.3f);
        }

        private static void ReleaseParkedRoomFog()
        {
            UnhookParkedSheetFog();
            if (object.ReferenceEquals(s_ParkedFogLayer, null)) return;
            var layer = s_ParkedFogLayer;
            s_ParkedFogLayer = null;
            // Destroyed with its scene: nothing to restore, and the next FPSCamera starts with its own.
            if (layer == null) return;
            try { layer.fog.enabled = s_ParkedFogSavedEnabled; }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"[PARK-SIS] Kamera sisi geri acilamadi: {e.Message}",
                                    $"[PARK-FOG] The camera's fog could not be switched back on: {e.Message}");
            }
        }
    }
}
