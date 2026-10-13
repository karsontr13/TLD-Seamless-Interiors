using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── THE GAME CULLS TREES FOR THE PLAYER'S CAMERA, AND A PARKED PLAYER'S IS IN THE WRONG PLACE ───
        //
        // The Long Dark does not draw its trees, large rocks or art objects as ordinary renderers.
        // RenderObjectInstance owns them - 34,431 trees in Mystery Lake alone - and every frame it
        // culls each instance on the CPU against ONE camera, then hands the survivors to every
        // camera that renders that frame. That camera is FPSCamera.
        //
        // Inside a parked interior FPSCamera is ParkOffset away from the eye the window portals
        // render the world from. So the portals were drawing a world culled for somewhere else:
        // trees on a hillside vanished as the player panned up, and the shadows of trees just out
        // of view came and went, because the game builds its shadow-caster volume from that same
        // frustum. This is what retired inside-out portals on 2026-09-15. It was invisible to every
        // renderer census taken at the time, because an instanced draw has no Renderer.
        //
        // THE FIX IS TO CORRECT THE EYE, NOT TO LOOSEN THE CULL. Hinterland ships a switch,
        // RenderObjectInstance.EnableFrustumTest(false), and it does bring the trees back. It does
        // it by dropping to a cruder mode that loses off-screen shadow casters, so foliage shadows
        // pop with view angle even standing outside on the ground. Measured, and it must not be
        // used here.
        //
        // Instead the cull is given a stand-in eye: a camera that never renders, carrying
        // FPSCamera's rotation and field of view at FPSCamera's position minus ParkOffset, which
        // is where the portal cameras already stand. Every window's off-axis frustum is a slice of
        // that eye's view, so one stand-in serves the whole building. Two things have to be
        // redirected, and the second was found the hard way:
        //
        //   1. Each RenderObjectInstance holds m_Camera and m_CameraComponent and reads them every
        //      frame. That moves the cull's POSITION: distances, LOD and the height adjustment.
        //   2. Its frustum planes and the corners its shadow volume is built from do NOT come from
        //      those fields. They come from MainCameraFrustrum, which is handed FPSCamera by
        //      argument. With only (1) in place the cull measured distance from one eye and built
        //      its frustum from the other. The patches in Patches/ParkedCullEyePatches.cs swap
        //      that argument.
        //
        // The player's camera, position, physics and saves are untouched. Nothing here runs
        // unless the player is inside a clone whose config parks it.
        //
        // A RECONCILER, for the same three reasons IndoorState.cs gives: s_IsPlayerInsideClone is
        // written from eight places, it can be set before the clone exists, and a missed release
        // is the dangerous direction. Left installed with the player outside, the cull's eye would
        // be ParkOffset under the ground.

        private static GameObject s_ParkedCullEyeObject;
        private static Camera s_ParkedCullEyeCamera;
        private static Vector3 s_ParkedCullEyeOffset;

        // What each instance pointed at before it was redirected, keyed by instance id. The list of
        // instances is the game's and can grow while the player is inside, so this is filled as
        // they are met rather than once at setup.
        private static readonly Dictionary<int, RenderObjectInstance> s_ParkedCullEyeInstances = new Dictionary<int, RenderObjectInstance>();
        private static readonly Dictionary<int, Transform> s_ParkedCullEyeSavedTransforms = new Dictionary<int, Transform>();
        private static readonly Dictionary<int, Camera> s_ParkedCullEyeSavedCameras = new Dictionary<int, Camera>();

        // Read by the MainCameraFrustrum patches. Null whenever the stand-in is not in force.
        internal static Camera ParkedCullEyeCamera => s_ParkedCullEyeCamera;

        // A plain managed flag for the patches to test first. They run for every player of the mod
        // on every frame - the on-screen one once per animal - and comparing a UnityEngine.Object
        // to null is not a field read, it is a call into the engine to ask whether the native
        // object is still alive. The common case, not inside a parked clone, should cost a branch.
        internal static bool ParkedCullEyeInForce;

        // The displacement from the stand-in eye up to the player's real camera. Only meaningful
        // while ParkedCullEyeCamera is not null.
        internal static Vector3 ParkedCullEyeOffset => s_ParkedCullEyeOffset;

        // ─── LET GO BEFORE THE SCENE COMES DOWN ───
        //
        // Exiting to the main menu from INSIDE a parked building threw sixteen exceptions in under
        // half a second (owner, 2026-09-18, build eec33a8): NullReferenceException in
        // Component.get_transform, under MainCameraFrustrum.RecalculateFrustumPlanes and
        // RecalculateFrustumCorners, starting 13 ms after "Exit to Menu" and stopping once the
        // scene was gone. Ten logs from that day hold one exit to the menu each; only two have the
        // exceptions, and the one that could be checked was an exit from inside.
        //
        // The reading, NOT proven by seeing inside the game's code: the stand-in is
        // DontDestroyOnLoad, so while the scene is torn down it is the one camera still alive.
        // With the player's camera destroyed the game skips this work, as it does on every other
        // exit; pointed at a camera that still answers, it runs those methods against a scene
        // that is already half gone. The tick only releases once it notices the main camera is
        // missing, which is a few frames too late.
        //
        // So the release happens at the game's own "exit to menu" calls, before anything is
        // destroyed, and the eye stays suspended until the next scene, because the player still
        // counts as inside for a moment and the tick would otherwise build it straight back.
        private static bool s_ParkedCullEyeSuspended;

        internal static void SuspendParkedCullEyeForSceneExit()
        {
            s_ParkedCullEyeSuspended = true;
            if (s_ParkedCullEyeObject != null) ReleaseParkedCullEye();
            ReleaseParkedRoomFog();
        }

        internal static void ResumeParkedCullEye()
        {
            s_ParkedCullEyeSuspended = false;
        }

        public static void TickParkedCullEye()
        {
            if (s_ParkedCullEyeSuspended)
            {
                if (s_ParkedCullEyeObject != null) ReleaseParkedCullEye();
                return;
            }

            SeamlessInteriorInstance wanted = s_IsPlayerInsideClone ? ResolveOccupiedInstance() : null;
            if (wanted != null && (wanted.Config == null || !wanted.Config.IsParked)) wanted = null;

            Camera main = (wanted == null) ? null : GameManager.GetMainCamera();
            if (wanted == null || main == null)
            {
                if (s_ParkedCullEyeObject != null) ReleaseParkedCullEye();
                return;
            }

            s_ParkedCullEyeOffset = wanted.Config.ParkOffset;

            if (s_ParkedCullEyeObject == null)
            {
                s_ParkedCullEyeObject = new GameObject("SeamlessInteriors_ParkedCullEye");
                // A region unload must not destroy this under the instances still pointing at it.
                // The release below is what removes it.
                Object.DontDestroyOnLoad(s_ParkedCullEyeObject);

                s_ParkedCullEyeCamera = s_ParkedCullEyeObject.AddComponent<Camera>();
                s_ParkedCullEyeCamera.enabled = false;
                s_ParkedCullEyeCamera.CopyFrom(main);
                // CopyFrom brings across whatever FPSCamera was rendering into and any explicit
                // matrices, including its TAA jitter. The cull wants a plain frustum from the
                // field of view, and this camera must never draw anything.
                s_ParkedCullEyeCamera.enabled = false;
                s_ParkedCullEyeCamera.targetTexture = null;
                s_ParkedCullEyeCamera.ResetProjectionMatrix();
                s_ParkedCullEyeCamera.ResetCullingMatrix();

                if (s_PrefVerboseLogging != null && s_PrefVerboseLogging.Value)
                    MelonLogger.Msg($"[PARK-GOZ] Agac ve kaya ayiklamasi icin yer seviyesinde vekil goz kuruldu, park mesafesi {s_ParkedCullEyeOffset.magnitude:F0} m.",
                                    $"[PARK-EYE] Player is inside a room kept {s_ParkedCullEyeOffset.magnitude:F0} m above its building: a stand-in viewpoint was set up at ground level so trees and rocks show correctly in the view out of the windows.");
            }

            SyncParkedCullEye(main);
            ParkedCullEyeInForce = true;

            var instances = RenderObjectInstance.s_RenderObjectInstanceList;
            if (instances == null) return;

            int eyeId = s_ParkedCullEyeCamera.GetInstanceID();
            for (int i = 0; i < instances.Count; i++)
            {
                var roi = instances[i];
                if (roi == null) continue;

                var current = roi.m_CameraComponent;
                if (current != null && current.GetInstanceID() == eyeId) continue;

                int id = roi.GetInstanceID();
                if (!s_ParkedCullEyeInstances.ContainsKey(id))
                {
                    s_ParkedCullEyeInstances[id] = roi;
                    s_ParkedCullEyeSavedTransforms[id] = roi.m_Camera;
                    s_ParkedCullEyeSavedCameras[id] = roi.m_CameraComponent;
                }

                roi.m_Camera = s_ParkedCullEyeObject.transform;
                roi.m_CameraComponent = s_ParkedCullEyeCamera;
            }
        }

        // Called from the tick and again from the frustum patches, at the moment the game asks for
        // the frustum. The game moves FPSCamera late in the frame, so a pose copied only from the
        // mod's own update can be a frame behind the head, and the cull's margin is only 5%.
        internal static void SyncParkedCullEye(Camera main)
        {
            if (s_ParkedCullEyeObject == null || s_ParkedCullEyeCamera == null || main == null) return;

            Transform source = main.transform;
            s_ParkedCullEyeObject.transform.SetPositionAndRotation(source.position - s_ParkedCullEyeOffset, source.rotation);
            s_ParkedCullEyeCamera.fieldOfView = main.fieldOfView;
            s_ParkedCullEyeCamera.aspect = main.aspect;
            // The game moves FPSCamera's far plane with the weather (445 m in a blizzard, 15000 m clear, read
            // in the Airfield hangar 2026-10-12). Copied only when the stand-in was made, a blizzard's short
            // one stayed after the sky cleared, and the views out kept their far trees and rocks culled.
            if (s_ParkedCullEyeCamera.farClipPlane != main.farClipPlane) s_ParkedCullEyeCamera.farClipPlane = main.farClipPlane;
            if (s_ParkedCullEyeCamera.nearClipPlane != main.nearClipPlane) s_ParkedCullEyeCamera.nearClipPlane = main.nearClipPlane;
            if (s_ParkedCullEyeCamera.enabled) s_ParkedCullEyeCamera.enabled = false;
        }

        // Each step stands alone, for the reason TeardownOutsideInPortals gives: one failure must
        // not leave an instance pointing at a camera that is about to be destroyed.
        private static void ReleaseParkedCullEye()
        {
            // Out of force first, so the frustum patches stop swapping before anything is restored.
            ParkedCullEyeInForce = false;
            Camera eye = s_ParkedCullEyeCamera;
            s_ParkedCullEyeCamera = null;

            Camera main = GameManager.GetMainCamera();

            foreach (var pair in s_ParkedCullEyeInstances)
            {
                var roi = pair.Value;
                // Unity's overloaded == is what detects an instance destroyed with its scene.
                if (roi == null) continue;

                try
                {
                    Transform savedTransform;
                    Camera savedCamera;
                    s_ParkedCullEyeSavedTransforms.TryGetValue(pair.Key, out savedTransform);
                    s_ParkedCullEyeSavedCameras.TryGetValue(pair.Key, out savedCamera);

                    // Whatever was saved may itself be gone by now. The real camera is the only
                    // safe thing to fall back to; the stand-in is about to be destroyed.
                    if (savedCamera == null) savedCamera = main;
                    if (savedTransform == null && savedCamera != null) savedTransform = savedCamera.transform;

                    roi.m_Camera = savedTransform;
                    roi.m_CameraComponent = savedCamera;
                }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[PARK-GOZ] Bir ornegin kamerasi geri yuklenemedi: {e.Message}",
                                        $"[PARK-EYE] One object's camera could not be restored: {e.Message}");
                }
            }

            s_ParkedCullEyeInstances.Clear();
            s_ParkedCullEyeSavedTransforms.Clear();
            s_ParkedCullEyeSavedCameras.Clear();

            if (s_ParkedCullEyeObject != null) Object.Destroy(s_ParkedCullEyeObject);
            s_ParkedCullEyeObject = null;

            if (eye != null && s_PrefVerboseLogging != null && s_PrefVerboseLogging.Value)
                MelonLogger.Msg("[PARK-GOZ] Vekil goz kaldirildi, ayiklama yeniden oyuncunun kamerasindan.",
                                "[PARK-EYE] Stand-in viewpoint removed; the game decides what to draw from the player's own camera again.");
        }
    }
}
