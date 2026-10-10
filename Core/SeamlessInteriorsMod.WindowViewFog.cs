using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── A WINDOW'S VIEW IS FOGGED THE WAY THE PLAYER'S IS: DEFERRED, THEN THE GAME'S FOG PASS ───
        //
        // The player's camera renders deferred and fogs opaque geometry afterwards, in one pass of its
        // PostProcessLayer over the whole picture from depth (TLD's own shader,
        // Hidden/PostProcessing/DeferredFogCustomTLD, with its distance gradient and silhouette fog).
        // The window cameras were switched to forward on 2026-09-22 because in deferred, with no such
        // pass, they had no fog at all; forward made every SHADER fog itself instead. Most TLD shaders
        // do. Some do not, and those showed unfogged through a window (2026-09-28): the fishing huts'
        // roof snow (Custom/SnowShader01_WithTex, also on some rock and cliff snow; switched in the
        // console to TLD_StandardSnow it fogged, owner: "The roofs look better"), a gear item on a hut
        // shelf, and the game's instanced trees and rocks, which drew black past the player's far
        // plane. So a lone window's camera renders deferred again, and this command buffer runs the
        // game's own fog shader over its picture at the point the player's camera does
        // (BeforeImageEffectsOpaque, sky excluded, pass 1: FPSCamera's fog.excludeSkybox is on).
        // Transparent things are drawn after it and fog themselves, as in the player's view. Tried in
        // the console on the Camp Office's five slot cameras; owner: "That looks pretty good", and "We
        // should have done it this way the whole time."
        //
        // The shader takes every camera-dependent value from Unity's built-ins (unity_CameraInvProjection,
        // _ZBufferParams, _ProjectionParams), and the distance gradient, silhouette parameters and the
        // FOG_EXP2 and SILHOUETTE_FOG keywords are globals set by UniStormWeatherSystem, so only the fog
        // color and density are set here: RenderSettings, gamma (isLinearColorSpace is false), as the
        // post-processing stack sets them.
        //
        // WALL CAMERAS TOO, WITH THE FOG COUNTED FROM THE GLASS. A wall camera stands at the player's
        // eye, so a pass counting from the camera would fog the room's own air (the 2026-09-27 fault:
        // "the fog density is including the volume of the room I'm in... not starting at the window").
        // The pass's fog term is (Linear01Depth * _ProjectionParams.z - _ProjectionParams.y) * density,
        // so the group's buffer sets _ProjectionParams.y to the camera's near plus the glass's depth
        // along the gaze (FogFromTheGlass, WallCameras.cs), draws, and puts the camera's own values back
        // with SetupCameraProperties. A lone window needs nothing: its camera's near plane is the glass.
        // Transparent things drawn after the pass (flakes, glass) still fog themselves from the camera.
        //
        // NEVER BEND A WINDOW CAMERA'S PROJECTION. From 2026-09-27 the wall groups bent row 2 of theirs
        // so forward shader fog would count from the glass, and Unity silently renders a camera with
        // such a projection FORWARD while renderingPath still reads DeferredShading. So the wall groups
        // never rendered deferred: the pass fogged over forward shader fog, twice, and where the bend
        // came out exactly zero the camera did go deferred with no fog at all, a snap over a few
        // centimeters (2026-09-28, evidence 0928-14 to 0928-18). Unbent, it rendered deferred for the
        // first time and matched the view from outside at the hangar front in dense fog, where the bent
        // forward camera was about 30% too foggy. Read actualRenderingPath, never renderingPath.
        //
        // A slot camera draws either one window (DrawOneWindowPortal) or a group (DrawWallGroup), frame
        // by frame, so each slot has its own buffer and it is recorded again at every draw.
        private const int WINDOW_FOG_PASS_SKY_EXCLUDED = 1;

        private static Material s_WindowFogMaterial;
        private static Mesh s_WindowFogTriangle;
        private static CommandBuffer[] s_WindowFogBuffers = new CommandBuffer[0];
        private static int[] s_WindowFogBufferOn = new int[0];   // instance id of the camera holding the slot's buffer, 0: none
        private static readonly int s_ProjectionParamsId = Shader.PropertyToID("_ProjectionParams");
        private static readonly int s_WindowFogCopyId = Shader.PropertyToID("_SIWindowFogCopy");
        private static readonly int s_MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int s_FogColorId = Shader.PropertyToID("_FogColor");
        private static readonly int s_FogParamsId = Shader.PropertyToID("_FogParams");
        private static readonly int s_ViewportScaleId = Shader.PropertyToID("_RenderViewportScaleFactor");
        private static bool s_WindowFogFailed;
        private static int s_WindowFogUpdatedFrame = -1;

        // Deferred, as the player's camera, with the game's fog pass on top. fogStartsAt is the view
        // depth the pass counts from (0: the camera's near plane). Deferred with no fog if the pass
        // cannot be built.
        private static void UseWindowViewFogPass(Camera cam, int slot, Camera main, float fogStartsAt = 0f, bool wallCamera = false)
        {
            if (cam == null) return;
            if (cam.renderingPath != RenderingPath.DeferredShading)
            {
                cam.renderingPath = RenderingPath.DeferredShading;
                cam.allowMSAA = false;
            }
            if (!EnsureWindowFogMaterial(main)) return;

            if (slot >= s_WindowFogBuffers.Length)
            {
                System.Array.Resize(ref s_WindowFogBuffers, slot + 1);
                System.Array.Resize(ref s_WindowFogBufferOn, slot + 1);
            }
            if (s_WindowFogBuffers[slot] == null) s_WindowFogBuffers[slot] = new CommandBuffer();
            CommandBuffer buffer = s_WindowFogBuffers[slot];

            // Remembered by the camera's instance id, not a flag: a slot's camera is made anew after a
            // region load, without the buffer, and a flag would then never attach it.
            int id = cam.GetInstanceID();
            if (s_WindowFogBufferOn[slot] != id)
            {
                cam.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, buffer);
                s_WindowFogBufferOn[slot] = id;
            }

            if (s_WindowFogUpdatedFrame != Time.frameCount)
            {
                s_WindowFogUpdatedFrame = Time.frameCount;
                s_WindowFogMaterial.SetColor(s_FogColorId, RenderSettings.fogColor);
                s_WindowFogMaterial.SetVector(s_FogParamsId, new Vector4(RenderSettings.fogDensity, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, 0f));
                s_WindowFogMaterial.SetFloat(s_ViewportScaleId, 1f);
            }

            var target = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            buffer.Clear();
            buffer.GetTemporaryRT(s_WindowFogCopyId, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
            buffer.Blit(target, new RenderTargetIdentifier(s_WindowFogCopyId));
            buffer.SetGlobalTexture(s_MainTexId, new RenderTargetIdentifier(s_WindowFogCopyId));
            buffer.SetRenderTarget(target);
            // A wall camera's own near is where its view starts, at its windows (StartingAtTheWindows), so
            // the fog is counted from the player's near, as it was before the view started there.
            bool fromWall = fogStartsAt > 0f || (wallCamera && cam.nearClipPlane != main.nearClipPlane);
            if (fromWall)
            {
                float far = cam.farClipPlane;
                buffer.SetGlobalVector(s_ProjectionParamsId, new Vector4(1f, main.nearClipPlane + fogStartsAt, far, 1f / far));
            }
            buffer.DrawMesh(s_WindowFogTriangle, Matrix4x4.identity, s_WindowFogMaterial, 0, WINDOW_FOG_PASS_SKY_EXCLUDED);
            if (fromWall) buffer.SetupCameraProperties(cam);
            buffer.ReleaseTemporaryRT(s_WindowFogCopyId);
        }

        // ─── THE PLAYER'S WORLD-DRAWING COMMAND BUFFERS, SHARED ───
        //
        // FPSCamera draws parts of the world through command buffers of its own, and a window camera
        // had none of them. Blowing snow first: the patches of thicker, drifting fog seen outside were
        // missing from the views (owner, 2026-09-28: "a localized intermittent increased fog effect we
        // do not see through the window"); the same buffer attached in the console brought it:
        // "Working. Looks good." Then the rest that draw the world, attached together in the console
        // at the hangar with no errors: the snow patches, the screen-space decals, the deferred ambient
        // occlusion and the distance-field shadows. Owner: "It's probably hard to see them so lets keep
        // them all. Again, we're trying to reproduce the player's camera as closely as possible."
        //
        // NOT shared: "Opaque Only Post-processing" carries the player's own fog pass (the window
        // cameras run theirs, counted from the glass, WindowViewFog above), "Post-processing" is the
        // final pass applied once on screen, and the motion vectors, snow planes (switched off in a
        // parked room, ParkedWeather.cs) and SetShaderParametersWorldCamera are the player's own. Found
        // on FPSCamera by name once a frame, so a window camera carries each buffer exactly while the
        // player's camera does. SharePlayerBuffers switches all of it off live.
        public static bool SharePlayerBuffers = true;

        private static readonly CameraEvent[] s_SharedBufferEvents =
            { CameraEvent.BeforeLighting, CameraEvent.BeforeReflections, CameraEvent.BeforeImageEffectsOpaque, CameraEvent.AfterForwardAlpha };
        private static readonly List<CommandBuffer> s_PlayerShared = new List<CommandBuffer>();
        private static readonly List<CameraEvent> s_PlayerSharedEvents = new List<CameraEvent>();
        private static int s_PlayerSharedFrame = -1;
        private static int s_PlayerSharedVersion;
        private static int[] s_SharedOnCamera = new int[0];
        private static int[] s_SharedOnVersion = new int[0];
        private static List<CommandBuffer>[] s_SharedHeld = new List<CommandBuffer>[0];
        private static List<CameraEvent>[] s_SharedHeldEvents = new List<CameraEvent>[0];

        // NOT the patch height maps: that buffer renders the snow patches' persistent height textures,
        // and run from a window camera it wrote them from the parked room's view; the patches stayed
        // dark squares round the player outdoors, until they were rebuilt (owner, 2026-10-10).
        private static bool IsSharedPlayerBuffer(string name)
        {
            if (name == "SnowPatchHeightMapCommandBuffer") return false;
            return name == "Snow_Blowing" || name == "Distance Field Shadows" || name == "Deferred Ambient Occlusion"
                || name.StartsWith("SnowPatch") || name.StartsWith("ScreenSpaceDecal");
        }

        private static void SharePlayerBuffersWith(Camera cam, int slot, Camera main)
        {
            if (cam == null || main == null) return;
            if (s_PlayerSharedFrame != Time.frameCount)
            {
                s_PlayerSharedFrame = Time.frameCount;
                int before = s_PlayerShared.Count;
                bool changed = false;
                var found = new List<CommandBuffer>();
                var foundEvents = new List<CameraEvent>();
                if (SharePlayerBuffers)
                {
                    foreach (var ev in s_SharedBufferEvents)
                    {
                        var buffers = main.GetCommandBuffers(ev);
                        for (int b = 0; b < buffers.Length; b++)
                            if (buffers[b] != null && IsSharedPlayerBuffer(buffers[b].name)) { found.Add(buffers[b]); foundEvents.Add(ev); }
                    }
                }
                if (found.Count != before) changed = true;
                else for (int i = 0; i < found.Count; i++) if (found[i] != s_PlayerShared[i] || foundEvents[i] != s_PlayerSharedEvents[i]) { changed = true; break; }
                if (changed)
                {
                    s_PlayerShared.Clear(); s_PlayerShared.AddRange(found);
                    s_PlayerSharedEvents.Clear(); s_PlayerSharedEvents.AddRange(foundEvents);
                    s_PlayerSharedVersion++;
                }
            }

            if (slot >= s_SharedOnCamera.Length)
            {
                System.Array.Resize(ref s_SharedOnCamera, slot + 1);
                System.Array.Resize(ref s_SharedOnVersion, slot + 1);
                System.Array.Resize(ref s_SharedHeld, slot + 1);
                System.Array.Resize(ref s_SharedHeldEvents, slot + 1);
            }
            int id = cam.GetInstanceID();
            if (s_SharedOnCamera[slot] == id && s_SharedOnVersion[slot] == s_PlayerSharedVersion) return;

            // A camera made anew after a region load holds nothing; the same camera gives back what it holds.
            if (s_SharedOnCamera[slot] == id && s_SharedHeld[slot] != null)
                for (int i = 0; i < s_SharedHeld[slot].Count; i++) cam.RemoveCommandBuffer(s_SharedHeldEvents[slot][i], s_SharedHeld[slot][i]);
            s_SharedHeld[slot] = new List<CommandBuffer>(s_PlayerShared);
            s_SharedHeldEvents[slot] = new List<CameraEvent>(s_PlayerSharedEvents);
            for (int i = 0; i < s_PlayerShared.Count; i++) cam.AddCommandBuffer(s_PlayerSharedEvents[i], s_PlayerShared[i]);
            s_SharedOnCamera[slot] = id;
            s_SharedOnVersion[slot] = s_PlayerSharedVersion;
        }

        private static bool EnsureWindowFogMaterial(Camera main)
        {
            if (s_WindowFogMaterial != null) return true;
            if (s_WindowFogFailed || main == null) return false;

            var layer = main.GetComponent<PostProcessLayer>();
            Shader shader = layer != null && layer.m_Resources != null && layer.m_Resources.shaders != null ? layer.m_Resources.shaders.deferredFog : null;
            if (shader == null)
            {
                s_WindowFogFailed = true;
                MelonLogger.Warning("[WINDOW-FOG] The player's fog shader was not found; window views have no fog.");
                return false;
            }

            s_WindowFogMaterial = new Material(shader);
            s_WindowFogMaterial.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            // The post-processing stack's full-screen triangle: its vertex stage takes positions as
            // clip coordinates, so Graphics.Blit's quad would cover a quarter of the picture.
            s_WindowFogTriangle = new Mesh();
            s_WindowFogTriangle.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(-1f, 3f, 0f), new Vector3(3f, -1f, 0f) };
            s_WindowFogTriangle.triangles = new[] { 0, 1, 2 };
            s_WindowFogTriangle.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            s_WindowFogTriangle.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            MelonLogger.Msg($"[WINDOW-FOG] Window views fog like the player's view: deferred, then '{shader.name}'.");
            return true;
        }
    }
}
