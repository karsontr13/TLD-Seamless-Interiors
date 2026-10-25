using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // THE VIEWS TELL THE GAME'S TEMPORAL ANTIALIASING HOW THEY MOVE.
        //
        // With TAA on, every view through a window or a door smeared while the player walked and not
        // while he turned (owner, 2026-09-15: "a ghost copy of the scene that takes longer to catch
        // up"). TAA rebuilds each pixel from where that pixel was a frame ago, and it learns where
        // from a texture of screen motion. The Long Dark does not use Unity's motion vectors for
        // that. CameraGlobalRT makes its own: for the player's camera, one full-screen pass of
        // Hidden/TLD_MotionVectors that works the motion out of the DEPTH texture alone, into
        // m_WorldMotionVectorRenderTexture. A view is a picture on a flat sheet, so its pixels were
        // given the motion of whatever opaque surface stands behind the sheet, never the motion of
        // the room or the land in the picture. Measured 2026-10-10 in a sideways step: looking out
        // of the Pleasant Valley store, walls 30 px a frame and the views 0.0 to 0.5 (nothing stands
        // behind a sheet in a room, so the depth is the far plane); looking into the Camp Office,
        // walls -65 to -72 and the view -68 (the building's own pane stands behind it).
        // Renderer.motionVectorGenerationMode changes none of this: nothing in the game reads it.
        //
        // THE FIX gives each view what the game gives the weapon camera, motion of its own. The
        // game's pass writes, for each texel, the texel's own position minus where its point stood
        // under the matrix _TLD_PreviousVP. It is run TWICE on the view camera's depth: once against
        // the player's camera of the frame before, carried into the room, and once against the
        // player's camera of this frame, carried the same way. The texel's own position is in both,
        // so the first minus the second is exactly how the point moved on the PLAYER'S screen,
        // whichever way the view camera faces and however its lens is narrowed or shifted. Three
        // earlier attempts that compared the view camera's own matrices between frames were wrong
        // for that reason: its lens follows the opening, and that read as motion. The sheets are
        // then drawn once more, into the game's motion texture, carrying the result.
        //
        // No shader of the mod's own: Sprites/Default multiplies a texture by the material's color
        // and blends premultiplied, so color (-1, -1, -1, 0.5) over the first pass leaves half of
        // (first minus second), and the last draw doubles it back and scales it. The two targets
        // are full float because each pass's numbers are near 1 and the motion is their small
        // difference; half floats left about two pixels of noise. Only red and green are kept
        // (RGFloat): the game's pass writes motion length in blue, but the difference of two
        // lengths is not a length, so blue is left at 0 (read as still, as distant scenery is in
        // vanilla). Measured 2026-10-24 at the Hangar: view motion unchanged, blue 0; owner: "same".
        //
        // Owner, 2026-10-10, on a console prototype of this: the wall views in the store, "I'd call
        // that good for now"; Camp Office window 7 from outside, "That looks pretty darn good."
        //
        // LEFT FOR LATER, none of it needed for the above:
        //  - A view camera that skipped a frame, or was just handed to another window, tells TAA
        //    nothing for one frame (HasBefore). The player's view of the frame before does not
        //    depend on which view asks; kept once a frame instead of per camera, the gap goes.
        //  - Never judged by eye: a frosted window (the frost is on the glass and is told it moves
        //    like the room), something standing in front of a view, the weapon over a view,
        //    turning while walking, and a single window looking out from inside.

        internal static bool ViewMotionForTaa = true;

        private class ViewMotion
        {
            public Camera Cam;
            public CommandBuffer Buffer;
            public RenderTexture Earlier, Now;      // Earlier ends up holding half of (earlier minus now)
            public Material Show, Minus;
            public Matrix4x4 PlayerView, PlayerViewBefore;
            public int Frame = -10, SheetFrame = -10;
            public bool HasBefore, Ready;
            public float LensX, LensY;               // the player's lens the motion was measured with
            public readonly List<Renderer> Sheets = new List<Renderer>();
        }

        private static readonly Dictionary<int, ViewMotion> s_ViewMotions = new Dictionary<int, ViewMotion>();
        private static readonly List<int> s_ViewMotionGone = new List<int>();
        private static CameraGlobalRT s_ViewMotionRig;
        private static float s_ViewMotionRigAskedAt = -10f;
        private static Camera s_ViewMotionPlayer;
        private static CommandBuffer s_ViewMotionOnPlayer;
        private static Mesh s_ViewMotionTriangle, s_ViewMotionSquare;
        private static Shader s_ViewMotionSprite;
        private static Camera.CameraCallback s_ViewMotionPreCull;
        private static bool s_ViewMotionHooked, s_ViewMotionFaulted;
        private static readonly int s_ViewMotionInverseId = Shader.PropertyToID("_TLD_NonJitteredVPInverse");
        private static readonly int s_ViewMotionPreviousId = Shader.PropertyToID("_TLD_PreviousVP");
        private const string VIEW_MOTION_WORLD_KEYWORD = "TLD_WORLD_CAMERA";   // without it the pass leaves the sky out

        // Only while the game is making motion vectors for TAA; with any other antialiasing nothing
        // reads them and the views cost nothing extra.
        private static bool ViewMotionWanted()
        {
            if (!ViewMotionForTaa || s_ViewMotionFaulted) return false;
            if (s_ViewMotionRig == null)
            {
                if (Time.unscaledTime - s_ViewMotionRigAskedAt < 2f) return false;
                s_ViewMotionRigAskedAt = Time.unscaledTime;
                s_ViewMotionRig = Object.FindObjectOfType<CameraGlobalRT>();
                if (s_ViewMotionRig == null) return false;
            }
            var layer = s_ViewMotionRig.m_ImageEffectsPostProcessLayer;
            return layer != null && layer.antialiasingMode == PostProcessLayer.Antialiasing.TemporalAntialiasing
                && s_ViewMotionRig.m_WorldMotionVectorRenderTexture != null && s_ViewMotionRig.m_MotionVectorsMaterial != null
                && s_ViewMotionRig.m_WorldCamera != null;
        }

        private static ViewMotion ViewMotionOf(Camera cam)
        {
            int id = cam.GetInstanceID();
            ViewMotion v;
            if (s_ViewMotions.TryGetValue(id, out v) && v.Cam != null) return v;
            if (s_ViewMotionSprite == null) s_ViewMotionSprite = Shader.Find("Sprites/Default");
            if (s_ViewMotionSprite == null) throw new System.Exception("the shader Sprites/Default is not in the game");
            v = new ViewMotion();
            v.Cam = cam;
            v.Buffer = new CommandBuffer();
            v.Buffer.name = "SI_ViewMotion";
            v.Show = new Material(s_ViewMotionSprite);
            v.Minus = new Material(s_ViewMotionSprite);
            v.Minus.color = new Color(-1f, -1f, -1f, 0.5f);
            cam.AddCommandBuffer(CameraEvent.BeforeImageEffects, v.Buffer);
            s_ViewMotions[id] = v;
            return v;
        }

        private static RenderTextureFormat ViewMotionTargetFormat()
        {
            return SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat)
                ? RenderTextureFormat.RGFloat : RenderTextureFormat.ARGBFloat;
        }

        private static RenderTexture NewViewMotionTarget(int width, int height, string name)
        {
            var target = new RenderTexture(width, height, 0, ViewMotionTargetFormat());
            target.name = name;
            target.filterMode = FilterMode.Point;
            target.Create();
            return target;
        }

        private static void EnsureViewMotionMeshes()
        {
            if (s_ViewMotionTriangle != null) return;
            // The game's pass places its triangle from the vertex number alone; any three vertices do.
            s_ViewMotionTriangle = new Mesh();
            s_ViewMotionTriangle.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(-1f, 3f, 0f), new Vector3(3f, -1f, 0f) };
            s_ViewMotionTriangle.triangles = new[] { 0, 1, 2 };
            s_ViewMotionTriangle.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            s_ViewMotionTriangle.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            s_ViewMotionSquare = new Mesh();
            s_ViewMotionSquare.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) };
            s_ViewMotionSquare.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            s_ViewMotionSquare.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            s_ViewMotionSquare.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            s_ViewMotionSquare.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }

        // Called once a frame for a view camera, AFTER its pose and lens are set and BEFORE it draws.
        // roomToBuilding takes a point the camera sees to where that point seems to stand for the
        // player: the inverse of whatever carried the player's eye to the camera.
        private static void TellTaaHowViewMoves(Camera cam, Camera main, Matrix4x4 roomToBuilding)
        {
            if (cam == null || main == null || s_ViewMotionFaulted) return;
            try
            {
                var v = ViewMotionOf(cam);
                v.Buffer.Clear();
                v.Ready = false;
                if (!ViewMotionWanted()) return;
                var target = cam.targetTexture;
                if (target == null) return;
                if (v.Earlier == null || v.Earlier.width != target.width || v.Earlier.height != target.height)
                {
                    if (v.Earlier != null) { v.Earlier.Release(); Object.Destroy(v.Earlier); }
                    if (v.Now != null) { v.Now.Release(); Object.Destroy(v.Now); }
                    v.Earlier = NewViewMotionTarget(target.width, target.height, "SI_ViewMotionEarlier");
                    v.Now = NewViewMotionTarget(target.width, target.height, "SI_ViewMotionNow");
                    v.Show.mainTexture = v.Earlier;
                    v.Minus.mainTexture = v.Now;
                }
                EnsureViewMotionMeshes();

                // A camera drawn twice in one frame measures both times from the frame before; one
                // that skipped a frame has no frame before to measure from and is left alone this once.
                // Both frames are carried into the room by THIS frame's carry. A camera handed from
                // one window to another, or a view fit that is still easing in, changes the carry
                // between frames, and the last frame's would be read as the room moving.
                int frame = Time.frameCount;
                if (frame != v.Frame)
                {
                    v.PlayerViewBefore = v.PlayerView;
                    v.HasBefore = frame - v.Frame == 1;
                    v.Frame = frame;
                }
                v.PlayerView = main.worldToCameraMatrix;
                if (!v.HasBefore) return;
                Matrix4x4 carried = v.PlayerView * roomToBuilding, carriedBefore = v.PlayerViewBefore * roomToBuilding;

                // The player's lens of THIS frame for both, so a zoom is not read as motion. Its
                // jitter, if it still carries any, is in both passes and drops out.
                Matrix4x4 playerLens = main.projectionMatrix;
                v.LensX = playerLens.m00; v.LensY = playerLens.m11;
                Matrix4x4 lens = GL.GetGPUProjectionMatrix(playerLens, true);
                Matrix4x4 own = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
                Material pass = s_ViewMotionRig.m_MotionVectorsMaterial;

                var b = v.Buffer;
                b.SetGlobalMatrix(s_ViewMotionInverseId, own.inverse);
                b.EnableShaderKeyword(VIEW_MOTION_WORLD_KEYWORD);
                b.SetGlobalMatrix(s_ViewMotionPreviousId, lens * carriedBefore);
                b.SetRenderTarget(new RenderTargetIdentifier(v.Earlier));
                b.DrawMesh(s_ViewMotionTriangle, Matrix4x4.identity, pass, 0, 1);
                b.SetGlobalMatrix(s_ViewMotionPreviousId, lens * carried);
                b.SetRenderTarget(new RenderTargetIdentifier(v.Now));
                b.DrawMesh(s_ViewMotionTriangle, Matrix4x4.identity, pass, 0, 1);
                b.DisableShaderKeyword(VIEW_MOTION_WORLD_KEYWORD);
                b.SetRenderTarget(new RenderTargetIdentifier(v.Earlier));
                b.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                b.DrawMesh(s_ViewMotionSquare, Matrix4x4.identity, v.Minus, 0, 0);
                b.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
                v.Ready = true;

                HookViewMotion();
            }
            catch (System.Exception e) { FaultViewMotion(e); }
        }

        // The sheets showing that camera's picture this frame.
        private static void TellTaaWhichSheet(Camera cam, Renderer sheet)
        {
            if (cam == null || sheet == null || s_ViewMotionFaulted) return;
            try
            {
                var v = ViewMotionOf(cam);
                int frame = Time.frameCount;
                if (v.SheetFrame != frame) { v.Sheets.Clear(); v.SheetFrame = frame; }
                v.Sheets.Add(sheet);
            }
            catch (System.Exception e) { FaultViewMotion(e); }
        }

        private static void HookViewMotion()
        {
            if (s_ViewMotionPreCull == null)
                s_ViewMotionPreCull = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnViewMotionPreCull));
            if (!s_ViewMotionHooked) { Camera.onPreCull += s_ViewMotionPreCull; s_ViewMotionHooked = true; }
        }

        // The player's camera is about to draw: the sheets go into the game's motion texture, after
        // the game's own pass at the same event, tested against the scene's depth so whatever
        // stands in front of a view keeps its own motion. Done here, not earlier, because only now
        // does the camera carry the lens the game draws that texture with (wider than the player's,
        // by the rig's padding), and the motion is scaled from the one lens to the other.
        private static void OnViewMotionPreCull(Camera camera)
        {
            if (s_ViewMotionFaulted || camera == null || s_ViewMotionRig == null) return;
            try
            {
                Camera player = s_ViewMotionRig.m_WorldCamera;
                if (player == null || camera.GetInstanceID() != player.GetInstanceID()) return;
                if (s_ViewMotionOnPlayer == null || s_ViewMotionPlayer != player)
                {
                    if (s_ViewMotionOnPlayer != null && s_ViewMotionPlayer != null)
                        s_ViewMotionPlayer.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, s_ViewMotionOnPlayer);
                    if (s_ViewMotionOnPlayer == null) { s_ViewMotionOnPlayer = new CommandBuffer(); s_ViewMotionOnPlayer.name = "SI_ViewMotionOnPlayer"; }
                    player.AddCommandBuffer(CameraEvent.BeforeImageEffects, s_ViewMotionOnPlayer);
                    s_ViewMotionPlayer = player;
                }
                var b = s_ViewMotionOnPlayer;
                b.Clear();
                if (!ViewMotionWanted()) return;

                int frame = Time.frameCount;
                Matrix4x4 drawn = player.nonJitteredProjectionMatrix;
                bool bound = false;
                s_ViewMotionGone.Clear();
                foreach (var pair in s_ViewMotions)
                {
                    var v = pair.Value;
                    if (v.Cam == null) { s_ViewMotionGone.Add(pair.Key); continue; }
                    if (!v.Ready || v.Frame != frame || v.SheetFrame != frame || v.LensX == 0f || v.LensY == 0f) continue;
                    v.Show.color = new Color(2f * drawn.m00 / v.LensX, 2f * drawn.m11 / v.LensY, 1f, 1f);
                    for (int i = 0; i < v.Sheets.Count; i++)
                    {
                        var sheet = v.Sheets[i];
                        if (sheet == null || !sheet.enabled || !sheet.gameObject.activeInHierarchy) continue;
                        var filter = sheet.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null) continue;
                        if (!bound)
                        {
                            b.SetRenderTarget(new RenderTargetIdentifier(s_ViewMotionRig.m_WorldMotionVectorRenderTexture),
                                              new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
                            bound = true;
                        }
                        // DrawMesh, never DrawRenderer: that one applies the sheet's own property
                        // block, and a view fade leaves _Color alpha 0 there (ApplyViewFade), which
                        // wiped the motion out entirely (owner: "nothing has ever changed for this
                        // window at all").
                        b.DrawMesh(filter.sharedMesh, sheet.localToWorldMatrix, v.Show, 0, 0);
                    }
                }
                for (int i = 0; i < s_ViewMotionGone.Count; i++) ForgetViewMotion(s_ViewMotionGone[i]);
            }
            catch (System.Exception e) { FaultViewMotion(e); }
        }

        private static void ForgetViewMotion(int id)
        {
            ViewMotion v;
            if (!s_ViewMotions.TryGetValue(id, out v)) return;
            s_ViewMotions.Remove(id);
            try { if (v.Cam != null && v.Buffer != null) v.Cam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, v.Buffer); } catch { }
            if (v.Earlier != null) { v.Earlier.Release(); Object.Destroy(v.Earlier); }
            if (v.Now != null) { v.Now.Release(); Object.Destroy(v.Now); }
            if (v.Show != null) Object.Destroy(v.Show);
            if (v.Minus != null) Object.Destroy(v.Minus);
        }

        private static void FaultViewMotion(System.Exception e)
        {
            if (s_ViewMotionFaulted) return;
            s_ViewMotionFaulted = true;
            try { TeardownViewMotion(); } catch { }
            MelonLogger.Warning("[VIEW-MOTION] Telling the antialiasing how the views move failed and is off until the game is restarted; with TAA the views smear as they did before: " + e);
        }

        internal static void TeardownViewMotion()
        {
            var ids = new List<int>(s_ViewMotions.Keys);
            for (int i = 0; i < ids.Count; i++) ForgetViewMotion(ids[i]);
            try
            {
                if (s_ViewMotionOnPlayer != null && s_ViewMotionPlayer != null)
                    s_ViewMotionPlayer.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, s_ViewMotionOnPlayer);
            }
            catch { }
            s_ViewMotionPlayer = null;
            if (s_ViewMotionHooked) { Camera.onPreCull -= s_ViewMotionPreCull; s_ViewMotionHooked = false; }
        }
    }
}
