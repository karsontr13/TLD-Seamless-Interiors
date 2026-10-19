using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    // How a window opens. Only the sash that slides up exists so far; side-hung ones come with their models.
    internal enum WindowOpenKind { SlideUp }

    // One window the player can open, by the room's window number (the cyan tags).
    internal sealed class OpenableWindow
    {
        public int RoomWindow;
        public WindowOpenKind Kind = WindowOpenKind.SlideUp;
    }

    public partial class SeamlessInteriorsMod
    {
        // ─── OPENING A WINDOW ───
        //
        // Owner, 2026-10-18: windows of a sash model (SLIDE_UP_WINDOW_MODELS), or listed in the building's row, open and close by
        // holding the interact key on the glass; the frost stays as it was. The game's window has no
        // sash of its own (frame and meeting rail are one mesh), so the lower sash is built here from
        // the frame's measure and slides up on the room side; the frame's rail keeps its outer half.
        // Prototyped live on Camp Office window 4 and passed by the owner's eye the same day.
        //
        // The glass and the frost layer are cut at the meeting rail: the upper half stays, the lower
        // half rides on the sash. Open or closed is kept per room window with the window record.

        private const float SASH_SLIDE_SECONDS = 1.2f;
        private const float SASH_BAR = 0.045f, SASH_TOP_RAIL = 0.06f, SASH_RAIL_OVER_SEAM = 0.05f;
        // Off the frame's faces: coplanar faces flickered at the jambs, sill and corners (owner, live).
        private const float SASH_INSET = 0.002f, SASH_DEPTH_GAP = 0.006f;
        // Rides up this share of the lower half's height.
        private const float SASH_SLIDE_SHARE = 0.95f;
        // 6 mm still showed a strip from below; 3 cm passed by the owner's eye (2026-10-18).
        private const float SASH_GLASS_INTO_RAIL = 0.03f;
        // The farmhouse WindowC frame read 4 cm out on both sides.
        private const float SASH_EDGE_OFF_GLASS = 0.035f;
        // The frame's room-side trim strip in GLB_WoodWallNatural_C01_Flat, so the sash matches its colour.
        private const float SASH_UV_U0 = 0.25f, SASH_UV_V0 = 0.303f, SASH_UV_PER_M = 0.27f;
        private const float SASH_OPEN_HOLD_SECONDS = 1f;
        // The cleaned-glass cover on a raised sash, seen from outside: just outward of its glass.
        private const float SASH_COVER_PROUD = 0.004f;

        private class WindowSash
        {
            public int Opening;
            public GameObject Go;
            public Mesh SashMesh;
            public Transform PaneRoot;
            public MeshFilter FrameMf;
            public Mesh FrameOwn, FrameCut;
            public MeshFilter GlassMf;
            public Mesh GlassOwn, GlassUpper, GlassLower;
            public MeshRenderer GlassRenderer, LowGlass;
            public GameObject TrackedFrost, LowFrost;
            public Mesh FrostOwn, FrostUpper, FrostLower;
            public Vector3 Up, Right, Outward;   // the room window's, world
            public float Seam;          // up from the glass centre, metres
            public float SashMid;       // the sash's depth, out from the glass plane
            public float Slide;         // metres
            public float EdgeLeft, EdgeRight, EdgeBottom;   // the sash's outer edges, from the glass centre; its pane stays inside them
            public float RailTop;       // the frame's meeting rail's top, from the glass centre
            public float Shown = -1f;   // 0 closed .. 1 open
            public float AnimFrom, AnimStart = -1f;
            public bool NoGameGlass;    // its pane is drawn by a cover layer instead of the game's glass
            // A double window's side-by-side sashes, which move as one: each bay's left and right edge; null for one.
            public float[] Bays;
            // Its own open state when not its room window's (the hall's casement); -1 for that.
            public int StateId = -1;
            // Hinged instead of sliding (hall): turned about this line by HingeAngle; its panes as (left, right, bottom, top).
            public float HingeAngle;
            public Vector3 HingeLocal, HingeAxisLocal;
            public List<Vector4> HingedPanes;
            public bool SharedGlass;    // the glass is the whole building's: its collider stays on
        }

        private static readonly List<WindowSash> s_WindowSashes = new List<WindowSash>();
        private static string s_WindowSashesKey;
        private static bool s_WindowSashesFailed;

        // Room windows standing open, by building. Written with the window record at the game's save.
        private static readonly Dictionary<string, HashSet<int>> s_RoomWindowOpen = new Dictionary<string, HashSet<int>>();

        public static bool IsRoomWindowOpen(string instanceId, int roomWindow)
        {
            if (instanceId == null || roomWindow < 0) return false;
            EnsureWindowStateLoaded(instanceId);
            HashSet<int> open;
            return s_RoomWindowOpen.TryGetValue(instanceId, out open) && open.Contains(roomWindow);
        }

        public static void SetRoomWindowOpen(string instanceId, int roomWindow, bool open)
        {
            if (instanceId == null || roomWindow < 0) return;
            EnsureWindowStateLoaded(instanceId);
            HashSet<int> set;
            if (!s_RoomWindowOpen.TryGetValue(instanceId, out set)) { set = new HashSet<int>(); s_RoomWindowOpen[instanceId] = set; }
            if (open) set.Add(roomWindow); else set.Remove(roomWindow);
        }

        // A sash for this room window of the room the player is in, or null.
        private static WindowSash SashOf(int opening)
        {
            for (int i = 0; i < s_WindowSashes.Count; i++) if (s_WindowSashes[i].Opening == opening) return s_WindowSashes[i];
            return null;
        }

        private static bool IsSashOpen(int opening) { return SashOf(opening) != null && IsRoomWindowOpen(s_WindowPortalInstanceId, opening); }

        private static int StateOf(WindowSash sash) { return sash.StateId >= 0 ? sash.StateId : sash.Opening; }

        // Any of the window's sashes open: the hall's casement is its own state (owner, 2026-10-19: no snow wiped through it).
        private static bool IsAnySashOpen(int opening)
        {
            foreach (var sash in s_WindowSashes) if (sash.Opening == opening && IsRoomWindowOpen(s_WindowPortalInstanceId, StateOf(sash))) return true;
            return false;
        }

        private static WindowSash SashOfState(int state)
        {
            for (int i = 0; i < s_WindowSashes.Count; i++) if (StateOf(s_WindowSashes[i]) == state) return s_WindowSashes[i];
            return null;
        }

        // Every frame from OnUpdate, after the cover layers.
        private static void TickWindowSashes()
        {
            if (s_WindowSashesFailed) return;
            try
            {
                string key = s_WindowPortalInstanceId == null ? null : s_WindowPortalInstanceId + ":" + s_WindowPortalOpenings.Count;
                // The cover layers measure the game's own window (glass panes, bars, posts), so the sash
                // waits until they are built and steps aside while they are built again: measured with the
                // sash in place, its stiles read as posts and its cut glass lost the lower pane (2026-10-18).
                if (key != null && WindowCoverLayers && (s_CoverLayersInstance != s_WindowPortalInstanceId || s_CoverLayersBuilt < s_CoverLayers.Count))
                    key = null;
                // Nor while the view from outside still wears its resized copy of this room's frames.
                if (key != null && OutsideInHoldsFrames(s_WindowPortalInstanceId)) key = null;
                // Nor while the raised sash made for that view still stands in the room.
                if (key != null && HasOutsideSashes(s_WindowPortalInstanceId)) key = null;
                if (key != s_WindowSashesKey)
                {
                    TeardownWindowSashes();
                    s_WindowSashesKey = key;
                    if (key != null) BuildWindowSashes();
                }

                float now = Time.realtimeSinceStartup;
                for (int i = 0; i < s_WindowSashes.Count; i++)
                {
                    var sash = s_WindowSashes[i];
                    if (sash.Go == null || sash.PaneRoot == null) { s_WindowSashesKey = null; return; }   // gone with its scene
                    // A hinged pair's frost is cut by its first sash for both.
                    if (sash.HingedPanes == null) FollowFrostLayer(sash);
                    else if (IsFirstHingedSash(sash)) FollowHallFrost(sash);
                    FollowGlass(sash);
                    SlideSash(sash, IsRoomWindowOpen(s_WindowPortalInstanceId, StateOf(sash)) ? 1f : 0f, now);
                }
            }
            catch (System.Exception ex)
            {
                s_WindowSashesFailed = true;
                try { TeardownWindowSashes(); } catch { }
                MelonLogger.Warning($"[PENCERE-KANAT] Kapatildi: {ex.Message}", $"[WINDOW-SASH] Turned off after an error: {ex.Message}");
            }
        }

        private static void BuildWindowSashes()
        {
            SeamlessInteriorInstance instance;
            if (!ActiveInteriors.TryGetValue(s_WindowPortalInstanceId, out instance) || instance == null) return;
            BuildingWindowOverride row;
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out row)) row = null;

            for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
            {
                if (!IsOpenableWindow(row, i, s_WindowPortalOpenings[i])) continue;
                string why;
                if (IsHingedWindow(s_WindowPortalOpenings[i]))
                {
                    var hinged = BuildHallSashes(s_WindowPortalOpenings[i], i, out why);
                    if (hinged != null) s_WindowSashes.AddRange(hinged);
                    if (why != null)
                        MelonLogger.Warning($"[PENCERE-KANAT] {s_WindowPortalInstanceId}: oda penceresi {i} kanatsiz kaldi: {why}",
                                            $"[WINDOW-SASH] {s_WindowPortalInstanceId}: room window {i} got no sash: {why}");
                    continue;
                }
                var sash = BuildSash(s_WindowPortalOpenings[i], i, true, out why);
                if (sash == null)
                {
                    MelonLogger.Warning($"[PENCERE-KANAT] {s_WindowPortalInstanceId}: oda penceresi {i} kanatsiz kaldi: {why}",
                                        $"[WINDOW-SASH] {s_WindowPortalInstanceId}: room window {i} got no sash: {why}");
                    continue;
                }
                s_WindowSashes.Add(sash);
            }
            if (s_DebugBounds)
                MelonLogger.Msg($"[PENCERE-KANAT] {s_WindowPortalInstanceId}: {s_WindowSashes.Count} acilir pencere.",
                                $"[WINDOW-SASH] {s_WindowPortalInstanceId}: {s_WindowSashes.Count} openable windows.");
        }

        // The frame: the window's widest mesh that is not glass, frost or snow and is not deeper than a frame.
        private static MeshFilter FindSashFrame(InteriorWindowOpening opening)
        {
            MeshFilter best = null;
            float bestArea = 0f;
            // Trapper's frame is the window object itself, 30 cm deep, with a flat painted view behind it.
            for (int c = -1; c < opening.PaneRoot.childCount; c++)
            {
                var child = c < 0 ? opening.PaneRoot : opening.PaneRoot.GetChild(c);
                var mf = child.GetComponent<MeshFilter>();
                var renderer = child.GetComponent<MeshRenderer>();
                if (mf == null || renderer == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                if (opening.Glass != null && opening.Glass.Contains(renderer)) continue;
                if (opening.Frost != null && opening.Frost.Contains(renderer)) continue;
                // A "_Shadow" child is the glass again in black, for shadows (farmhouse parlour).
                if (opening.Backdrop == renderer || child.name.Contains("Snow") || child.name.Contains("Shadow")) continue;
                // Measured on the mesh: a world box of a window on a slanted wall is far deeper than the window.
                float depth = MeshExtentAlong(mf, opening.Outward);
                if (depth > 0.35f || depth < 0.01f) continue;
                float area = MeshExtentAlong(mf, opening.Right) * MeshExtentAlong(mf, opening.Up);
                if (area > bestArea) { bestArea = area; best = mf; }
            }
            return best;
        }

        private static float MeshExtentAlong(MeshFilter mf, Vector3 axis)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            var t = mf.transform;
            foreach (var v in mf.sharedMesh.vertices)
            {
                float d = Vector3.Dot(t.TransformPoint(v), axis);
                lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d);
            }
            return hi - lo;
        }

        private static Vector3[] CopyVerts(Mesh mesh)
        {
            var src = mesh.vertices;
            var v = new Vector3[src.Length];
            for (int i = 0; i < v.Length; i++) v[i] = src[i];
            return v;
        }

        private static WindowSash BuildSash(InteriorWindowOpening opening, int index, bool cutFrame, out string why)
        {
            if (IsSlideRightWindowModel(opening)) return BuildPaneSash(opening, index, false, out why);
            if (IsWindowModel(opening, PANE_SLIDE_UP_WINDOW_MODELS)) return BuildPaneSash(opening, index, true, out why);
            return BuildSlideUpSash(opening, index, cutFrame, out why);
        }

        // Windows with a pane per sash and the frame in the building's window mesh (Coastal Highway): the
        // left or lower pane goes on a sash that slides in front of the frame's face. A is the way it
        // slides, B across it; a side slider's sash has its axes turned (its Up is the window's right).
        // The frame is found by its post between the panes.
        private static WindowSash BuildPaneSash(InteriorWindowOpening opening, int index, bool up, out string why)
        {
            why = null;
            Transform pr = opening.PaneRoot;
            if (pr == null) { why = "no window object"; return null; }
            if (pr.Find("SI_Sash") != null) { why = "already has one"; return null; }
            MeshRenderer glassRenderer = null;
            MeshFilter glassMf = null;
            if (opening.Glass != null)
                foreach (var g in opening.Glass)
                {
                    var mf = g != null ? g.GetComponent<MeshFilter>() : null;
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) { glassRenderer = g as MeshRenderer; glassMf = mf; break; }
                }
            if (glassMf == null || glassRenderer == null) { why = "no readable glass"; return null; }

            Vector3 origin = opening.GlassCenter, A = up ? opening.Up : opening.Right, B = up ? opening.Right : opening.Up, O = opening.Outward;
            // The panes: the widest stretch across the window that no glass triangle covers is the post.
            var glassVerts = CopyVerts(glassMf.sharedMesh);
            var across = new float[glassVerts.Length];
            float gBottom = float.MaxValue, gTop = float.MinValue;
            for (int i = 0; i < glassVerts.Length; i++)
            {
                Vector3 p = glassMf.transform.TransformPoint(glassVerts[i]) - origin;
                across[i] = Vector3.Dot(p, A);
                float u = Vector3.Dot(p, B);
                gBottom = Mathf.Min(gBottom, u); gTop = Mathf.Max(gTop, u);
            }
            var spans = new List<Vector2>();
            var tris = glassMf.sharedMesh.triangles;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                float a = across[tris[i]], b = across[tris[i + 1]], c = across[tris[i + 2]];
                spans.Add(new Vector2(Mathf.Min(a, Mathf.Min(b, c)), Mathf.Max(a, Mathf.Max(b, c))));
            }
            if (spans.Count == 0) { why = "no glass triangles"; return null; }
            spans.Sort((x, y) => x.x.CompareTo(y.x));
            float gLeft = spans[0].x, gRight = spans[0].y, postL = 0f, postR = 0f;
            foreach (var s in spans)
            {
                if (s.x > gRight && s.x - gRight > postR - postL) { postL = gRight; postR = s.x; }
                gRight = Mathf.Max(gRight, s.y);
            }
            if (postR - postL < 0.02f || postL - gLeft < 0.2f || gRight - postR < 0.2f) { why = "not two panes"; return null; }
            float seam = (postL + postR) * 0.5f;

            // The frame's room face on the post, and its material and colour there.
            MeshFilter frameMf = null;
            float face = float.MaxValue;
            Vector2 frameUv = Vector2.zero;
            if (pr.parent != null)
                foreach (var mf in pr.parent.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == glassMf || mf.transform.IsChildOf(pr) || mf.sharedMesh == null || !mf.sharedMesh.isReadable || mf.name.StartsWith("SI_") || mf.name.Contains("Glass")) continue;
                    var renderer = mf.GetComponent<MeshRenderer>();
                    if (renderer == null || !renderer.bounds.Contains(origin)) continue;
                    var verts = mf.sharedMesh.vertices;
                    var uvs = mf.sharedMesh.uv;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 p = mf.transform.TransformPoint(verts[i]) - origin;
                        float r = Vector3.Dot(p, A), u = Vector3.Dot(p, B), o = Vector3.Dot(p, O);
                        if (r < postL - 0.005f || r > postR + 0.005f || u < gBottom - 0.02f || u > gTop + 0.02f || o > 0.01f || o < -0.15f) continue;
                        if (o < face) { face = o; frameMf = mf; frameUv = i < uvs.Length ? uvs[i] : Vector2.zero; }
                    }
                }
            if (frameMf == null) { why = "no frame post between the panes"; return null; }
            // A corner's colour lay on the texture's dark outline (owner, 2026-10-18: too dark); the post's middle is the frame's.
            frameUv = FaceUvAt(frameMf, origin, A, B, O, new Vector2(seam, (gBottom + gTop) * 0.5f), frameUv);

            // Over the frame round the moving pane by a few centimetres, so no gap shows at its edges.
            const float OVER = 0.03f;
            var sash = new WindowSash { Opening = index, PaneRoot = pr, Up = A, Right = B, Outward = O, Seam = seam };
            float o1 = face - SASH_DEPTH_GAP, o0 = o1 - 0.024f;
            sash.SashMid = (o0 + o1) * 0.5f;
            sash.EdgeLeft = gBottom - OVER; sash.EdgeRight = gTop + OVER; sash.EdgeBottom = gLeft - OVER;
            sash.RailTop = postL + OVER;
            // Its back edge stops at the post, clear of the pane it opens.
            sash.Slide = postL - sash.EdgeBottom;

            sash.Go = new GameObject("SI_Sash");
            sash.Go.layer = frameMf.gameObject.layer;
            sash.Go.transform.SetParent(pr, false);
            sash.SashMesh = BuildSashMesh(pr, origin, B, A, O, sash.EdgeLeft, sash.EdgeRight, sash.EdgeBottom, sash.RailTop, o0, o1);
            // Each bar is laid on the post's face as the frame textures it, along and across, so it carries the
            // frame's grain and wear: one point was a flat colour, and one line of it streaked (owner, 2026-10-18).
            LayBarsOnFrame(sash.SashMesh, pr, frameMf, origin, A, B, O, postL, postR, sash.EdgeLeft + 0.04f, frameUv);
            sash.Go.AddComponent<MeshFilter>().sharedMesh = sash.SashMesh;
            var frameRenderer = frameMf.GetComponent<MeshRenderer>();
            var sashRenderer = sash.Go.AddComponent<MeshRenderer>();
            sashRenderer.sharedMaterial = frameRenderer.sharedMaterial;
            sashRenderer.lightProbeUsage = frameRenderer.lightProbeUsage;

            sash.GlassMf = glassMf;
            sash.GlassRenderer = glassRenderer;
            sash.GlassOwn = glassMf.sharedMesh;
            sash.GlassUpper = CutMeshAtSeam(sash.GlassOwn, glassMf.transform, origin, A, seam, true);
            sash.GlassLower = CutMeshAtSeam(sash.GlassOwn, glassMf.transform, origin, A, seam, false);
            KeepInsideSash(sash.GlassLower, glassMf.transform, origin, B, A, sash);
            glassMf.sharedMesh = sash.GlassUpper;
            var lowGlassGo = new GameObject("SI_SashGlass");
            lowGlassGo.layer = glassMf.gameObject.layer;
            lowGlassGo.transform.SetParent(sash.Go.transform, false);
            lowGlassGo.transform.position = glassMf.transform.position + O * sash.SashMid;
            lowGlassGo.transform.rotation = glassMf.transform.rotation;
            lowGlassGo.transform.localScale = glassMf.transform.localScale;
            lowGlassGo.AddComponent<MeshFilter>().sharedMesh = sash.GlassLower;
            sash.LowGlass = lowGlassGo.AddComponent<MeshRenderer>();
            FollowGlass(sash);
            return sash;
        }

        // Each bar is laid on the frame's face as the frame textures it, along and across, so it carries the
        // frame's grain and wear: one point was a flat colour, and one line of it streaked (owner, 2026-10-18).
        // A is across the post or rail (it spans postLow..postHigh), B along it from bStart; both from the sash's centre.
        private static void LayBarsOnFrame(Mesh sashMesh, Transform pr, MeshFilter frameMf, Vector3 origin, Vector3 A, Vector3 B, Vector3 O,
                                           float postLow, float postHigh, float bStart, Vector2 fallback)
        {
            var frame = new FrameFaces(frameMf, origin, A, B, O);
            float postIn = postLow + 0.005f;
            int tri;
            Vector2 uv0 = frame.UvAt(new Vector2(postIn, bStart), fallback, out tri);
            Vector2 perB = (frame.UvAt(new Vector2(postIn, bStart + 0.3f), uv0, out tri) - uv0) / 0.3f;
            Vector2 perA = (frame.UvAt(new Vector2(postIn + 0.02f, bStart), uv0, out tri) - uv0) / 0.02f;
            float postSpan = Mathf.Max(postHigh - postLow - 0.01f, 0.005f);
            var local = sashMesh.vertices;
            var barUv = new Vector2[local.Length];
            // BuildSashMesh: 24 points a bar; the two rails first (along B), then the two stiles (along A).
            for (int bar = 0; bar * 24 < local.Length; bar++)
            {
                Vector3 along = bar < 2 ? B : A, crossAxis = bar < 2 ? A : B;
                float loAlong = float.MaxValue, loAcross = float.MaxValue;
                for (int i = bar * 24; i < bar * 24 + 24; i++)
                {
                    Vector3 p = pr.TransformPoint(local[i]) - origin;
                    loAlong = Mathf.Min(loAlong, Vector3.Dot(p, along));
                    loAcross = Mathf.Min(loAcross, Vector3.Dot(p, crossAxis) + Vector3.Dot(p, O));
                }
                for (int i = bar * 24; i < bar * 24 + 24; i++)
                {
                    Vector3 p = pr.TransformPoint(local[i]) - origin;
                    // A face spans the bar's length and one of its width or depth; the other is fixed on it.
                    float d = Vector3.Dot(p, along) - loAlong, c = Vector3.Dot(p, crossAxis) + Vector3.Dot(p, O) - loAcross;
                    barUv[i] = uv0 + perB * d + perA * Mathf.Repeat(c, postSpan);
                }
            }
            sashMesh.uv = barUv;
        }

        // A mesh's points in world space, worked out here from its matrix: one engine call a point froze the game
        // on a house's whole interior mesh (12 000 points, Grey Mother's).
        private static Vector3[] WorldVerts(MeshFilter mf)
        {
            var m = mf.transform.localToWorldMatrix;
            var v = mf.sharedMesh.vertices;
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float x = v[i].x, y = v[i].y, z = v[i].z;
                w[i] = new Vector3(m.m00 * x + m.m01 * y + m.m02 * z + m.m03,
                                   m.m10 * x + m.m11 * y + m.m12 * z + m.m13,
                                   m.m20 * x + m.m21 * y + m.m22 * z + m.m23);
            }
            return w;
        }

        // A frame mesh's room-facing flat faces, in (A, B) from origin, to read its texture and material at a point.
        private sealed class FrameFaces
        {
            private readonly Vector2[] w, uvs;
            private readonly float[] o;
            private readonly int[] tris;
            public readonly Mesh Mesh;

            public FrameFaces(MeshFilter mf, Vector3 origin, Vector3 A, Vector3 B, Vector3 O)
            {
                Mesh = mf.sharedMesh;
                var world = WorldVerts(mf);
                uvs = Mesh.uv;
                tris = Mesh.triangles;
                w = new Vector2[world.Length];
                o = new float[world.Length];
                for (int i = 0; i < world.Length; i++)
                {
                    Vector3 p = world[i] - origin;
                    w[i] = new Vector2(Vector3.Dot(p, A), Vector3.Dot(p, B));
                    o[i] = Vector3.Dot(p, O);
                }
            }

            // The texture point on the room-most face at (a, b), or fallback when no face lies there; tri is that
            // face's first index in the triangle list, or -1.
            public Vector2 UvAt(Vector2 at, Vector2 fallback, out int tri)
            {
                tri = -1;
                if (uvs.Length != w.Length) return fallback;
                float best = float.MaxValue;
                Vector2 uv = fallback;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    // Flat faces facing the room only, near the glass.
                    if (o[a] > 0.01f || o[a] < -0.15f || Mathf.Abs(o[a] - o[b]) > 0.003f || Mathf.Abs(o[a] - o[c]) > 0.003f || o[a] >= best) continue;
                    Vector2 e0 = w[b] - w[a], e1 = w[c] - w[a], e2 = at - w[a];
                    float d = e0.x * e1.y - e1.x * e0.y;
                    if (Mathf.Abs(d) < 1e-8f) continue;
                    float l1 = (e2.x * e1.y - e1.x * e2.y) / d, l2 = (e0.x * e2.y - e2.x * e0.y) / d;
                    if (l1 < 0f || l2 < 0f || l1 + l2 > 1f) continue;
                    best = o[a];
                    tri = t;
                    uv = uvs[a] * (1f - l1 - l2) + uvs[b] * l1 + uvs[c] * l2;
                }
                return uv;
            }

            // The material of the sub-mesh holding the face starting at tri, or fallback.
            public Material MaterialOf(int tri, MeshRenderer renderer, Material fallback)
            {
                if (tri < 0 || renderer == null) return fallback;
                var mats = renderer.sharedMaterials;
                for (int s = 0; s < Mesh.subMeshCount && s < mats.Length; s++)
                {
                    var sub = Mesh.GetSubMesh(s);
                    if (tri >= sub.indexStart && tri < sub.indexStart + sub.indexCount) return mats[s] ?? fallback;
                }
                return fallback;
            }
        }

        // The texture point on the room-most face of the mesh at (a, b), or fallback when no face lies there.
        private static Vector2 FaceUvAt(MeshFilter mf, Vector3 origin, Vector3 A, Vector3 B, Vector3 O, Vector2 at, Vector2 fallback)
        {
            int tri;
            return new FrameFaces(mf, origin, A, B, O).UvAt(at, fallback, out tri);
        }

        // cutFrame: the frame's rail gives up its room half so the sash can pass it. Off for the view from
        // outside, which resizes that frame itself; the raised sash's bottom rail then sits inside the rail.
        private static WindowSash BuildSlideUpSash(InteriorWindowOpening opening, int index, bool cutFrame, out string why)
        {
            why = null;
            Transform pr = opening.PaneRoot;
            if (pr == null) { why = "no window object"; return null; }

            MeshRenderer glassRenderer = null;
            MeshFilter glassMf = null;
            if (opening.Glass != null)
                foreach (var g in opening.Glass)
                {
                    var mf = g != null ? g.GetComponent<MeshFilter>() : null;
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) { glassRenderer = g as MeshRenderer; glassMf = mf; break; }
                }
            if (glassMf == null || glassRenderer == null) { why = "no readable glass"; return null; }

            Vector3 origin = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;
            // Coastal House H: each pane is its own glass object, one over the other (owner, 2026-10-18). The
            // lower one rides on the sash whole; the upper one is left as it is.
            MeshFilter upperGlassMf = null;
            float paneGapLow = 0f, paneGapHigh = 0f;
            if (opening.Glass != null && opening.Glass.Count >= 2)
            {
                MeshFilter low = null, high = null;
                float lowTop = float.MaxValue, highBottom = float.MinValue;
                foreach (var g in opening.Glass)
                {
                    var mf = g != null ? g.GetComponent<MeshFilter>() : null;
                    if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var v in mf.sharedMesh.vertices) { float u = Vector3.Dot(mf.transform.TransformPoint(v) - origin, U); lo = Mathf.Min(lo, u); hi = Mathf.Max(hi, u); }
                    if (hi < 0.02f && (low == null || hi < lowTop)) { low = mf; lowTop = hi; }
                    else if (lo > -0.02f && (high == null || lo > highBottom)) { high = mf; highBottom = lo; }
                }
                if (low != null && high != null)
                {
                    glassMf = low; glassRenderer = low.GetComponent<MeshRenderer>(); upperGlassMf = high;
                    paneGapLow = lowTop; paneGapHigh = highBottom;
                    if (glassRenderer == null) { why = "no readable glass"; return null; }
                }
            }
            // The farmhouse parlour's bay: its three windows are one glass mesh and one frame mesh beside it
            // (owner, 2026-10-18). Each window takes its own part of them; its sash is named for it.
            var glassSource = OriginalGlassMesh(glassMf.sharedMesh);
            // Shared glass leaves the window's plane (the bay's side windows stand at an angle). Most farmhouse
            // windows report no glass width, so reach is only set when there is one.
            float reach = opening.GlassWidth > 0.05f ? opening.GlassWidth * 0.5f + 0.1f : float.MaxValue;
            bool sharedGlass = false;
            foreach (var v in glassSource.vertices)
                if (Mathf.Abs(Vector3.Dot(glassMf.transform.TransformPoint(v) - origin, O)) > 0.03f) { sharedGlass = true; break; }
            if (sharedGlass && reach == float.MaxValue) { why = "glass shared with other windows, and no width to part it"; return null; }
            string sashName = sharedGlass ? "SI_Sash_" + index : "SI_Sash";
            if (pr.Find(sashName) != null) { why = "already has one"; return null; }
            var frameMf = FindSashFrame(opening);
            bool frameBeside = false;
            if (frameMf == null) { frameMf = FindFrameBeside(opening, reach); frameBeside = frameMf != null; }
            if (frameMf == null) { why = "no frame mesh"; return null; }

            // The meeting rail: the glass is two panes, and the seam is the height between them.
            var glassVerts = OwnGlassVerts(glassSource, glassMf.transform, origin, R, O, sharedGlass ? reach : float.MaxValue);
            if (glassVerts.Length < 4) { why = "no glass of its own"; return null; }
            var heights = new List<float>();
            foreach (var v in glassVerts) heights.Add(Vector3.Dot(glassMf.transform.TransformPoint(v) - origin, U));
            if (upperGlassMf != null)
                foreach (var v in upperGlassMf.sharedMesh.vertices) heights.Add(Vector3.Dot(upperGlassMf.transform.TransformPoint(v) - origin, U));
            heights.Sort();
            float lowest = heights[0], highest = heights[heights.Count - 1];
            float seam = float.NaN;
            foreach (float h in heights)
                if (h > lowest + 0.05f && h < highest - 0.05f && (float.IsNaN(seam) || Mathf.Abs(h) < Mathf.Abs(seam))) seam = h;
            if (float.IsNaN(seam)) { why = "the glass is one pane"; return null; }

            // The opening inside the frame, from its room face, leaving out the rail round the seam.
            var frameVerts = CopyVerts(frameMf.sharedMesh);
            var frameT = frameMf.transform;
            float roomFace = float.MaxValue;
            var world = WorldVerts(frameMf);
            // A frame beside the glass may hold other windows' frames too: only this window's part counts.
            var near = new bool[frameVerts.Length];
            for (int i = 0; i < frameVerts.Length; i++)
            {
                near[i] = !frameBeside || IsNearOpening(world[i] - origin, opening, R, U, O);
                if (near[i]) roomFace = Mathf.Min(roomFace, Vector3.Dot(world[i] - origin, O));
            }
            float left = float.MinValue, right = float.MaxValue, bottom = float.MinValue;
            for (int i = 0; i < world.Length; i++)
            {
                if (!near[i]) continue;
                Vector3 p = world[i] - origin;
                float o = Vector3.Dot(p, O), r = Vector3.Dot(p, R), u = Vector3.Dot(p, U);
                if (o > roomFace + 0.01f || Mathf.Abs(u - seam) < 0.1f) continue;
                if (r < 0f) left = Mathf.Max(left, r); else right = Mathf.Min(right, r);
                if (u < seam) bottom = Mathf.Max(bottom, u);
            }
            // Trapper's frame is not one flat face on the room side: its room-most points stand at one side
            // only, so the edges read from them missed the glass by 10 cm one way and 20 cm the other. An edge
            // more than a few centimetres off the glass's own is taken from the glass instead.
            float gLeft = float.MaxValue, gRight = float.MinValue;
            foreach (var v in glassVerts)
            {
                float r = Vector3.Dot(glassMf.transform.TransformPoint(v) - origin, R);
                gLeft = Mathf.Min(gLeft, r); gRight = Mathf.Max(gRight, r);
            }
            // A double window (farmhouse) has its glass in two bays with a post between: each bay is a sash,
            // and both ride up together (owner, 2026-10-18). Its sides are the glass's own.
            float[] bays = GlassBays(glassMf, origin, R, O, sharedGlass ? reach : float.MaxValue);
            if (bays != null) { left = gLeft; right = gRight; }
            if (left == float.MinValue || Mathf.Abs(left - gLeft) > SASH_EDGE_OFF_GLASS) left = gLeft;
            if (right == float.MaxValue || Mathf.Abs(right - gRight) > SASH_EDGE_OFF_GLASS) right = gRight;
            if (bottom == float.MinValue || Mathf.Abs(bottom - lowest) > SASH_EDGE_OFF_GLASS) bottom = lowest;
            // The sides stand alike round the glass: the farmhouse rail's end read as the right side, 4.6 cm out.
            float outL = gLeft - left, outR = right - gRight;
            if (Mathf.Abs(outL - outR) > 0.02f) { if (outL > outR) left = gLeft - outR; else right = gRight + outL; }
            if (left == float.MinValue || right == float.MaxValue || bottom == float.MinValue || right - left < 0.2f || seam - bottom < 0.2f)
            { why = "the frame's opening could not be read"; return null; }

            // The frame's own meeting rail says where the seam is: the glass is mounted upside down in half
            // the Camp Office's windows, which put its pane seam 7 cm off the rail.
            float railLow = float.MaxValue, railHigh = float.MinValue;
            for (int i = 0; i < world.Length; i++)
            {
                if (!near[i]) continue;
                Vector3 p = world[i] - origin;
                float r = Vector3.Dot(p, R), u = Vector3.Dot(p, U);
                // The rail runs into the jambs, past the opening's sides. Room side only: Trapper's outer trim
                // is ragged round the seam.
                if (Vector3.Dot(p, O) >= 0f || !InBays(r, bays, left, right, 0.1f) || Mathf.Abs(u - seam) > 0.15f) continue;
                railLow = Mathf.Min(railLow, u); railHigh = Mathf.Max(railHigh, u);
            }
            // Two panes of their own: the rail is the gap between them. Read from the frame, Grey Mother's took
            // in a mullion's end 14 cm above it.
            if (upperGlassMf != null) { railLow = paneGapLow; railHigh = paneGapHigh; }
            float sashTop = seam + SASH_RAIL_OVER_SEAM;
            if (railLow < railHigh) { seam = (railLow + railHigh) * 0.5f; sashTop = railHigh; }
            // A bay's sides are where its rail meets the jamb or post: the glass runs 2 cm into them, and a
            // sash that wide stood in the jamb (owner, 2026-10-18).
            if (bays != null)
            {
                for (int b = 0; b < bays.Length; b += 2)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int i = 0; i < world.Length; i++)
                    {
                        if (!near[i]) continue;
                        Vector3 p = world[i] - origin;
                        float r = Vector3.Dot(p, R), u = Vector3.Dot(p, U);
                        if (Vector3.Dot(p, O) >= 0f || Mathf.Abs(u - seam) > 0.1f || r < bays[b] - 0.03f || r > bays[b + 1] + 0.03f) continue;
                        lo = Mathf.Min(lo, r); hi = Mathf.Max(hi, r);
                    }
                    if (lo > bays[b] && lo - bays[b] < SASH_EDGE_OFF_GLASS) bays[b] = lo;
                    if (hi < bays[b + 1] && bays[b + 1] - hi < SASH_EDGE_OFF_GLASS) bays[b + 1] = hi;
                }
                left = bays[0]; right = bays[bays.Length - 1];
            }
            if (roomFace > -2f * SASH_DEPTH_GAP - 0.02f) { why = "no room for a sash between the frame's face and the glass"; return null; }

            var sash = new WindowSash { Opening = index, PaneRoot = pr, Up = U, Right = R, Outward = O, Seam = seam };
            float o0 = roomFace + SASH_DEPTH_GAP, o1 = -SASH_DEPTH_GAP;
            // A double window's frame has no faces behind its rail: cut, the rail left holes by the jambs and
            // the post (owner, 2026-10-18). Its sashes stand in front of the rail instead, between the jambs;
            // so does a sash whose frame stands beside the glass, shared with other windows.
            if (bays != null || frameBeside)
            {
                float railFace = 0f;
                for (int i = 0; i < world.Length; i++)
                {
                    if (!near[i]) continue;
                    Vector3 p = world[i] - origin;
                    if (Mathf.Abs(Vector3.Dot(p, U) - seam) < 0.1f && InBays(Vector3.Dot(p, R), bays, left, right, 0.01f))
                        railFace = Mathf.Min(railFace, Vector3.Dot(p, O));
                }
                // Coastal House H's frame is one flat face: the sash stands proud of it.
                o1 = railFace - SASH_DEPTH_GAP;
                o0 = o1 - 0.024f;
                cutFrame = false;
            }
            sash.SashMid = (o0 + o1) * 0.5f;
            sash.Slide = (seam - bottom) * SASH_SLIDE_SHARE;
            // The glass runs on below the frame's sill, so raised it showed a strip under the rail (owner,
            // 2026-10-18): it goes up until its bottom edge is behind the rail.
            if (railLow < railHigh) sash.Slide = Mathf.Min(Mathf.Max(sash.Slide, railLow + SASH_GLASS_INTO_RAIL - lowest), seam - bottom + 0.03f);
            // Trapper's upper pane is shorter than the lower, so raised it ran past the head (owner, 2026-10-18).
            sash.Slide = Mathf.Min(sash.Slide, highest - SASH_GLASS_INTO_RAIL - sashTop);

            // The frame's rail keeps its outer half, so the sash passes it on the room side.
            if (cutFrame)
            {
                sash.FrameMf = frameMf;
                sash.FrameOwn = frameMf.sharedMesh;
                sash.FrameCut = Object.Instantiate(sash.FrameOwn);
                for (int i = 0; i < world.Length; i++)
                {
                    if (!near[i]) continue;
                    Vector3 p = world[i] - origin;
                    float o = Vector3.Dot(p, O), r = Vector3.Dot(p, R), u = Vector3.Dot(p, U);
                    // A double window's bays end at the jambs already: its cut stays inside them.
                    if (o >= 0f || Mathf.Abs(u - seam) >= 0.1f || !InBays(r, bays, left, right, bays != null ? 0.01f : 0.1f)) continue;
                    frameVerts[i] = frameT.InverseTransformPoint(world[i] - O * o);
                }
                sash.FrameCut.vertices = frameVerts;
                sash.FrameCut.RecalculateBounds();
                frameMf.sharedMesh = sash.FrameCut;
            }

            sash.Go = new GameObject(sashName);
            sash.Go.layer = frameMf.gameObject.layer;
            sash.Go.transform.SetParent(pr, false);
            sash.RailTop = sashTop;
            sash.EdgeLeft = left + SASH_INSET; sash.EdgeRight = right - SASH_INSET; sash.EdgeBottom = bottom + SASH_INSET;
            if (bays != null)
            {
                sash.Bays = new float[bays.Length];
                for (int b = 0; b < bays.Length; b += 2) { sash.Bays[b] = bays[b] + SASH_INSET; sash.Bays[b + 1] = bays[b + 1] - SASH_INSET; }
            }
            sash.SashMesh = BuildSashMesh(pr, origin, R, U, O, sash.Bays ?? new[] { sash.EdgeLeft, sash.EdgeRight }, sash.EdgeBottom, sashTop, o0, o1);
            var frameRenderer = frameMf.GetComponent<MeshRenderer>();
            var sashMaterial = frameRenderer.sharedMaterial;
            // A frame beside the glass is not the wood strip SASH_UV reads: the bars take its own texture, and
            // the material of the part the rail is in (Grey Mother's frame is in the house's 7-material mesh).
            if (frameBeside && railLow < railHigh)
            {
                LayBarsOnFrame(sash.SashMesh, pr, frameMf, origin, U, R, O, railLow, railHigh, left + 0.04f, Vector2.zero);
                int tri;
                var faces = new FrameFaces(frameMf, origin, U, R, O);
                faces.UvAt(new Vector2(seam, (left + right) * 0.5f), Vector2.zero, out tri);
                sashMaterial = faces.MaterialOf(tri, frameRenderer, sashMaterial);
            }
            sash.Go.AddComponent<MeshFilter>().sharedMesh = sash.SashMesh;
            var sashRenderer = sash.Go.AddComponent<MeshRenderer>();
            sashRenderer.sharedMaterial = sashMaterial;
            if (frameBeside) sashRenderer.lightProbeUsage = frameRenderer.lightProbeUsage;

            // The glass: its upper pane stays, its lower pane rides on the sash.
            sash.GlassMf = glassMf;
            sash.GlassRenderer = glassRenderer;
            sash.GlassOwn = glassSource;
            sash.GlassUpper = CutMeshAtSeam(sash.GlassOwn, glassMf.transform, origin, U, seam, true);
            sash.GlassLower = CutMeshAtSeam(sash.GlassOwn, glassMf.transform, origin, U, seam, false);
            // The glass runs on into the frame; on the sash it would show past it as the sash moves (owner, 2026-10-18).
            KeepInsideSash(sash.GlassLower, glassMf.transform, origin, R, U, sash);
            if (sharedGlass) KeepOwnTriangles(sash.GlassLower, glassSource, glassMf.transform, origin, R, O, reach);
            glassMf.sharedMesh = sash.GlassUpper;
            var lowGlassGo = new GameObject("SI_SashGlass");
            lowGlassGo.layer = glassMf.gameObject.layer;
            lowGlassGo.transform.SetParent(sash.Go.transform, false);
            lowGlassGo.transform.position = glassMf.transform.position + O * sash.SashMid;
            lowGlassGo.transform.rotation = glassMf.transform.rotation;
            lowGlassGo.transform.localScale = glassMf.transform.localScale;
            lowGlassGo.AddComponent<MeshFilter>().sharedMesh = sash.GlassLower;
            sash.LowGlass = lowGlassGo.AddComponent<MeshRenderer>();
            FollowGlass(sash);

            return sash;
        }

        // A copy of the mesh with every point on the wrong side of the seam brought onto it.
        private static void KeepInsideSash(Mesh mesh, Transform t, Vector3 origin, Vector3 right, Vector3 up, WindowSash sash)
        {
            var v = CopyVerts(mesh);
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = t.TransformPoint(v[i]);
                float r = Vector3.Dot(w - origin, right), u = Vector3.Dot(w - origin, up);
                float rc = Mathf.Clamp(r, sash.EdgeLeft, sash.EdgeRight), uc = Mathf.Max(u, sash.EdgeBottom);
                if (rc != r || uc != u) v[i] = t.InverseTransformPoint(w + right * (rc - r) + up * (uc - u));
            }
            mesh.vertices = v;
            mesh.RecalculateBounds();
        }

        private static Mesh CutMeshAtSeam(Mesh own, Transform t, Vector3 origin, Vector3 up, float seam, bool keepUpper)
        {
            var mesh = Object.Instantiate(own);
            var v = CopyVerts(own);
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = t.TransformPoint(v[i]);
                float u = Vector3.Dot(w - origin, up);
                if (keepUpper ? u < seam : u > seam) v[i] = t.InverseTransformPoint(w + up * (seam - u));
            }
            mesh.vertices = v;
            mesh.RecalculateBounds();
            return mesh;
        }

        // Four bars round the lower pane: rails between the stiles, so no faces overlap at the corners.
        // The game's glass mesh, when a sash of a window sharing it has swapped in its cut copy.
        private static Mesh OriginalGlassMesh(Mesh current)
        {
            foreach (var s in s_WindowSashes) if (s.GlassUpper == current && s.GlassOwn != null) return s.GlassOwn;
            foreach (var held in s_OutsideSashes) if (held.Sash != null && held.Sash.GlassUpper == current && held.Sash.GlassOwn != null) return held.Sash.GlassOwn;
            return current;
        }

        private static bool IsOwnGlassPoint(Vector3 fromCentre, Vector3 R, Vector3 O, float reach)
        {
            return Mathf.Abs(Vector3.Dot(fromCentre, R)) <= reach && Mathf.Abs(Vector3.Dot(fromCentre, O)) <= 0.03f;
        }

        // The glass mesh's points (its own space) on this window's plane and within reach across it.
        private static Vector3[] OwnGlassVerts(Mesh mesh, Transform t, Vector3 origin, Vector3 R, Vector3 O, float reach)
        {
            var own = new List<Vector3>();
            foreach (var v in mesh.vertices) if (reach == float.MaxValue || IsOwnGlassPoint(t.TransformPoint(v) - origin, R, O, reach)) own.Add(v);
            return own.ToArray();
        }

        // Drops the triangles of the other windows sharing the glass.
        private static void KeepOwnTriangles(Mesh mesh, Mesh source, Transform t, Vector3 origin, Vector3 R, Vector3 O, float reach)
        {
            var v = source.vertices;
            var tris = mesh.triangles;
            var kept = new List<int>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                bool own = true;
                for (int k = 0; k < 3; k++) if (!IsOwnGlassPoint(t.TransformPoint(v[tris[i + k]]) - origin, R, O, reach)) own = false;
                if (own) { kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]); }
            }
            mesh.triangles = kept.ToArray();
        }

        private static bool IsNearOpening(Vector3 fromCentre, InteriorWindowOpening opening, Vector3 R, Vector3 U, Vector3 O)
        {
            float o = Vector3.Dot(fromCentre, O);
            return Mathf.Abs(Vector3.Dot(fromCentre, R)) <= opening.GlassWidth * 0.5f + 0.15f
                && Mathf.Abs(Vector3.Dot(fromCentre, U)) <= opening.GlassHeight * 0.5f + 0.2f && o > -0.25f && o < 0.1f;
        }

        // A frame mesh beside the glass, not under the window object: the one with the most points round
        // this window (the farmhouse parlour bay's frames are one mesh for its three windows).
        private static MeshFilter FindFrameBeside(InteriorWindowOpening opening, float reach)
        {
            var pr = opening.PaneRoot;
            if (pr == null || pr.parent == null) return null;
            MeshFilter best = null;
            int bestCount = 7;
            Vector3 origin = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;
            // Beside the window object, beside the group holding the house's glass (Coastal House H), or the
            // group itself (Grey Mother's: the house's interior mesh holds the frames, its panes under it).
            var candidates = new List<Transform> { pr.parent };
            var places = pr.parent.parent != null ? new[] { pr.parent, pr.parent.parent } : new[] { pr.parent };
            foreach (var place in places)
                for (int c = 0; c < place.childCount; c++) if (place.GetChild(c) != pr.parent) candidates.Add(place.GetChild(c));
            foreach (var child in candidates)
                {
                    if (child == pr || child.name.Contains("Glass") || child.name.Contains("Glow") || child.name.Contains("Shadow")
                        || child.name.Contains("Walls") || child.name.StartsWith("SI_")) continue;
                    var mf = child.GetComponent<MeshFilter>();
                    var renderer = child.GetComponent<MeshRenderer>();
                    if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || renderer == null) continue;
                    if (opening.Glass != null && opening.Glass.Contains(renderer)) continue;
                    int count = 0;
                    foreach (var p in WorldVerts(mf)) if (IsNearOpening(p - origin, opening, R, U, O)) count++;
                    if (count > bestCount) { bestCount = count; best = mf; }
                }
            return best;
        }

        // True when r is on a sash: within a bay, or between left and right for one sash, give or take margin.
        private static bool InBays(float r, float[] bays, float left, float right, float margin)
        {
            if (bays == null) return r >= left - margin && r <= right + margin;
            // The margin reaches into the jambs only; the posts between bays keep their shape (they were notched).
            for (int b = 0; b < bays.Length; b += 2)
            {
                float lo = bays[b] - (b == 0 ? margin : 0.01f), hi = bays[b + 1] + (b + 2 >= bays.Length ? margin : 0.01f);
                if (r >= lo && r <= hi) return true;
            }
            return false;
        }

        // The stretches across the window that glass covers, as left/right pairs, when there are two or more
        // with a post of at least 5 cm between; otherwise null.
        // reach: for glass shared with other windows, how far across this window's part goes; the rest is skipped.
        private static float[] GlassBays(MeshFilter glassMf, Vector3 origin, Vector3 R, Vector3 O, float reach)
        {
            var verts = OriginalGlassMesh(glassMf.sharedMesh).vertices;
            var across = new float[verts.Length];
            var own = new bool[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = glassMf.transform.TransformPoint(verts[i]) - origin;
                across[i] = Vector3.Dot(p, R);
                own[i] = Mathf.Abs(across[i]) <= reach && Mathf.Abs(Vector3.Dot(p, O)) <= 0.03f;
            }
            var spans = new List<Vector2>();
            var tris = OriginalGlassMesh(glassMf.sharedMesh).triangles;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                if (!own[tris[i]] || !own[tris[i + 1]] || !own[tris[i + 2]]) continue;
                float a = across[tris[i]], b = across[tris[i + 1]], c = across[tris[i + 2]];
                spans.Add(new Vector2(Mathf.Min(a, Mathf.Min(b, c)), Mathf.Max(a, Mathf.Max(b, c))));
            }
            if (spans.Count == 0) return null;
            spans.Sort((x, y) => x.x.CompareTo(y.x));
            var bays = new List<float> { spans[0].x, spans[0].y };
            foreach (var s in spans)
            {
                if (s.x > bays[bays.Count - 1] + 0.05f) { bays.Add(s.x); bays.Add(s.y); }
                else bays[bays.Count - 1] = Mathf.Max(bays[bays.Count - 1], s.y);
            }
            return bays.Count >= 4 ? bays.ToArray() : null;
        }

        private static Mesh BuildSashMesh(Transform pr, Vector3 origin, Vector3 R, Vector3 U, Vector3 O,
                                          float r0, float r1, float u0, float u1, float o0, float o1)
        {
            return BuildSashMesh(pr, origin, R, U, O, new[] { r0, r1 }, u0, u1, o0, o1);
        }

        private static Mesh BuildSashMesh(Transform pr, Vector3 origin, Vector3 R, Vector3 U, Vector3 O,
                                          float[] bays, float u0, float u1, float o0, float o1)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float w = SASH_BAR;
            for (int b = 0; b + 1 < bays.Length; b += 2)
            {
                float r0 = bays[b], r1 = bays[b + 1];
                AddSashBar(verts, uvs, tris, r0 + w, r1 - w, u0, u0 + w, o0, o1);
                AddSashBar(verts, uvs, tris, r0 + w, r1 - w, u1 - SASH_TOP_RAIL, u1, o0, o1);
                AddSashBar(verts, uvs, tris, r0, r0 + w, u0, u1, o0, o1);
                AddSashBar(verts, uvs, tris, r1 - w, r1, u0, u1, o0, o1);
            }

            var local = new Vector3[verts.Count];
            for (int i = 0; i < local.Length; i++)
                local[i] = pr.InverseTransformPoint(origin + R * verts[i].x + U * verts[i].y + O * verts[i].z);
            WindSashFaces(local, tris);
            var mesh = new Mesh { name = "SI_Sash" };
            mesh.vertices = local;
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // One box in (right, up, out), grain along its long side.
        private static void AddSashBar(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                       float r0, float r1, float u0, float u1, float o0, float o1)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++) c[i] = new Vector3((i & 1) == 0 ? r0 : r1, (i & 2) == 0 ? u0 : u1, (i & 4) == 0 ? o0 : o1);
            int longAxis = u1 - u0 > r1 - r0 ? 1 : 0;
            int[][] faces = { new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 } };
            int[] faceAxis = { 0, 0, 1, 1, 2, 2 };
            for (int f = 0; f < 6; f++)
            {
                int s = verts.Count;
                int nAxis = faceAxis[f];
                int uA = nAxis == longAxis ? (nAxis == 1 ? 0 : 1) : longAxis;
                int vA = 3 - nAxis - uA;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = c[faces[f][k]];
                    verts.Add(p);
                    Vector3 d = p - c[0];
                    uvs.Add(new Vector2(SASH_UV_U0 + SASH_UV_PER_M * d[uA], SASH_UV_V0 + SASH_UV_PER_M * d[vA]));
                }
                // Wound once the points are in the mesh's own space (WindSashFaces).
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3);
            }
        }

        // Each face turned to face away from its bar's middle; both windings at once made the normals cancel (black).
        private static void WindSashFaces(Vector3[] v, List<int> tris)
        {
            for (int b = 0; b < v.Length; b += 24)
            {
                Vector3 mid = Vector3.zero;
                for (int i = 0; i < 24; i++) mid += v[b + i];
                mid /= 24f;
                for (int f = 0; f < 6; f++)
                {
                    int s = b + f * 4, t = (b / 24) * 36 + f * 6;
                    Vector3 n = Vector3.Cross(v[s + 1] - v[s], v[s + 2] - v[s]);
                    Vector3 fc = (v[s] + v[s + 1] + v[s + 2] + v[s + 3]) * 0.25f;
                    if (Vector3.Dot(n, fc - mid) > 0f) continue;
                    tris[t + 1] = s + 2; tris[t + 2] = s + 1;
                    tris[t + 4] = s + 3; tris[t + 5] = s + 2;
                }
            }
        }

        // The lower pane's glass shows when the game's glass does, in the material it has.
        private static void FollowGlass(WindowSash sash)
        {
            if (sash.LowGlass == null || sash.GlassRenderer == null) return;
            bool shown = sash.GlassRenderer.enabled && !sash.NoGameGlass;
            if (sash.LowGlass.enabled != shown) sash.LowGlass.enabled = shown;
            if (sash.LowGlass.sharedMaterial != sash.GlassRenderer.sharedMaterial) sash.LowGlass.sharedMaterial = sash.GlassRenderer.sharedMaterial;
        }

        // The cover layer's frost quad is cut at the seam once it exists, and again whenever it is remade.
        private static void FollowFrostLayer(WindowSash sash)
        {
            GameObject frost = null;
            WindowCoverLayer layer = null;
            if (WindowCoverLayers && sash.Opening < s_CoverLayers.Count && sash.Opening < s_WindowPortalOpenings.Count) layer = s_CoverLayers[sash.Opening];
            if (layer != null) frost = layer.FrostGo;

            if (frost != sash.TrackedFrost)
            {
                DropSashFrost(sash);
                sash.TrackedFrost = frost;
                if (frost != null) SplitSashFrost(sash, layer);
            }
            if (sash.LowFrost != null && frost != null && sash.LowFrost.activeSelf != frost.activeSelf) sash.LowFrost.SetActive(frost.activeSelf);
        }

        private static void SplitSashFrost(WindowSash sash, WindowCoverLayer layer)
        {
            var mf = layer.FrostGo.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var opening = s_WindowPortalOpenings[sash.Opening];
            // Along the sash's own axes: its Up is the way it slides, for a side slider the window's right.
            Vector3 origin = opening.GlassCenter, A = sash.Up, B = sash.Right, O = opening.Outward;
            // A rectangle in world space; each corner is kept in its place in the quad, so the faces keep their winding.
            var v = CopyVerts(mf.sharedMesh);
            if (v.Length != 4) return;
            Transform t = mf.transform;
            var uv0 = mf.sharedMesh.uv;
            if (uv0.Length != 4) return;
            var a = new float[4];
            var b = new float[4];
            for (int i = 0; i < 4; i++) { Vector3 p = t.TransformPoint(v[i]) - origin; a[i] = Vector3.Dot(p, A); b[i] = Vector3.Dot(p, B); }
            float aMin = Mathf.Min(Mathf.Min(a[0], a[1]), Mathf.Min(a[2], a[3])), aMax = Mathf.Max(Mathf.Max(a[0], a[1]), Mathf.Max(a[2], a[3]));
            float bMin = Mathf.Min(Mathf.Min(b[0], b[1]), Mathf.Min(b[2], b[3])), bMax = Mathf.Max(Mathf.Max(b[0], b[1]), Mathf.Max(b[2], b[3]));
            if (aMax - aMin < 0.05f || bMax - bMin < 0.05f) return;
            float f = (sash.Seam - aMin) / (aMax - aMin);
            if (f <= 0.01f || f >= 0.99f) return;

            // The upper part starts at the rail's top: from the seam it lay over the rail, hidden by the shut
            // sash's top rail, and showed below the raised one (owner, 2026-10-18).
            float upperBottom = Mathf.Max(sash.Seam, sash.RailTop);
            sash.FrostOwn = mf.sharedMesh;
            sash.FrostUpper = Object.Instantiate(sash.FrostOwn);
            var up = new Vector3[4];
            var uvUp = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                float ai = Mathf.Max(a[i], upperBottom);
                up[i] = t.InverseTransformPoint(origin + A * ai + B * b[i] + O * Vector3.Dot(t.TransformPoint(v[i]) - origin, O));
                uvUp[i] = QuadUv(a, b, uv0, aMin, aMax, bMin, bMax, ai, b[i]);
            }
            sash.FrostUpper.vertices = up;
            sash.FrostUpper.uv = uvUp;
            sash.FrostUpper.RecalculateBounds();
            mf.sharedMesh = sash.FrostUpper;

            // The lower half on the sash, at the sash's depth, cut to the sash's edges: wider or lower, its
            // rim showed past the sash and moved with it (owner, 2026-10-18).
            float left = Mathf.Max(bMin, sash.EdgeLeft), right = Mathf.Min(bMax, sash.EdgeRight), bottom = Mathf.Max(aMin, sash.EdgeBottom);
            // A side slider's sash ends short of the seam, at the post.
            float top = Mathf.Min(sash.Seam, sash.RailTop);
            if (right - left < 0.05f || top - bottom < 0.05f) return;
            Transform st = sash.Go.transform;
            Vector3 plane = origin + O * sash.SashMid;
            // One quad a bay, so a double window's post shows no frost between its sashes.
            var spans = sash.Bays ?? new[] { left, right };
            var ownTris = sash.FrostOwn.triangles;
            var ownNormals = sash.FrostOwn.normals;
            var ownColors = sash.FrostOwn.colors;
            var ownTangents = sash.FrostOwn.tangents;
            var low = new List<Vector3>();
            var uvLow = new List<Vector2>();
            var nLow = new List<Vector3>();
            var cLow = new List<Color>();
            var tLow = new List<Vector4>();
            var lowTris = new List<int>();
            for (int s = 0; s + 1 < spans.Length; s += 2)
            {
                float sl = Mathf.Max(left, spans[s]), sr = Mathf.Min(right, spans[s + 1]);
                if (sr - sl < 0.05f) continue;
                int first = low.Count;
                for (int i = 0; i < 4; i++)
                {
                    float ai = a[i] - aMin < aMax - a[i] ? bottom : top, bi = b[i] - bMin < bMax - b[i] ? sl : sr;
                    low.Add(st.InverseTransformPoint(plane + A * ai + B * bi));
                    uvLow.Add(QuadUv(a, b, uv0, aMin, aMax, bMin, bMax, ai, bi));
                    if (ownNormals.Length == 4) nLow.Add(ownNormals[i]);
                    if (ownColors.Length == 4) cLow.Add(ownColors[i]);
                    if (ownTangents.Length == 4) tLow.Add(ownTangents[i]);
                }
                foreach (int k in ownTris) lowTris.Add(first + k);
            }
            if (low.Count == 0) return;
            sash.FrostLower = new Mesh { name = "SI_SashFrost" };
            sash.FrostLower.vertices = low.ToArray();
            sash.FrostLower.uv = uvLow.ToArray();
            if (nLow.Count == low.Count) sash.FrostLower.normals = nLow.ToArray();
            if (cLow.Count == low.Count) sash.FrostLower.colors = cLow.ToArray();
            if (tLow.Count == low.Count) sash.FrostLower.tangents = tLow.ToArray();
            sash.FrostLower.triangles = lowTris.ToArray();
            sash.FrostLower.RecalculateBounds();

            sash.LowFrost = new GameObject("SI_SashFrost");
            sash.LowFrost.transform.SetParent(st, false);
            sash.LowFrost.AddComponent<MeshFilter>().sharedMesh = sash.FrostLower;
            var renderer = sash.LowFrost.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = layer.FrostMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // The texture point at (a, b) of a rectangle quad, from its four corners' points.
        private static Vector2 QuadUv(float[] a, float[] b, Vector2[] uv, float aMin, float aMax, float bMin, float bMax, float at, float bt)
        {
            float s = (at - aMin) / (aMax - aMin), q = (bt - bMin) / (bMax - bMin);
            Vector2 c00 = Vector2.zero, c10 = Vector2.zero, c01 = Vector2.zero, c11 = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                bool hiA = a[i] - aMin > aMax - a[i], hiB = b[i] - bMin > bMax - b[i];
                if (!hiA && !hiB) c00 = uv[i]; else if (hiA && !hiB) c10 = uv[i]; else if (!hiA) c01 = uv[i]; else c11 = uv[i];
            }
            return Vector2.Lerp(Vector2.Lerp(c00, c10, s), Vector2.Lerp(c01, c11, s), q);
        }

        private static void DropSashFrost(WindowSash sash)
        {
            if (sash.TrackedFrost != null && sash.FrostOwn != null)
            {
                var mf = sash.TrackedFrost.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh == sash.FrostUpper) mf.sharedMesh = sash.FrostOwn;
            }
            if (sash.LowFrost != null) Object.Destroy(sash.LowFrost);
            if (sash.FrostUpper != null) Object.Destroy(sash.FrostUpper);
            if (sash.FrostLower != null) Object.Destroy(sash.FrostLower);
            sash.LowFrost = null; sash.FrostUpper = null; sash.FrostLower = null; sash.FrostOwn = null; sash.TrackedFrost = null;
        }

        // Eased over SASH_SLIDE_SECONDS; the first frame after building takes the state as it is.
        private static void SlideSash(WindowSash sash, float target, float now)
        {
            if (sash.Shown < 0f) { sash.Shown = target; PlaceSash(sash); return; }
            if (sash.AnimStart < 0f)
            {
                if (sash.Shown == target) return;
                sash.AnimFrom = sash.Shown;
                sash.AnimStart = now;
            }
            float k = Mathf.Clamp01((now - sash.AnimStart) / SASH_SLIDE_SECONDS);
            k = k * k * (3f - 2f * k);
            sash.Shown = Mathf.Lerp(sash.AnimFrom, target, k);
            if (k >= 1f) { sash.Shown = target; sash.AnimStart = -1f; }
            PlaceSash(sash);
        }

        private static void PlaceSash(WindowSash sash)
        {
            if (sash.HingedPanes != null) { PlaceHingedSash(sash, sash.Shown); return; }
            sash.Go.transform.localPosition = sash.PaneRoot.InverseTransformVector(sash.Up * (sash.Slide * sash.Shown));
        }

        // The interact hold on an openable window's target lands here.
        private static void ToggleWindowOpen(int opening, GameObject soundAt)
        {
            // state: the room window, or the hall casement's own (room window + CASEMENT_STATE).
            if (SashOfState(opening) == null || s_WindowPortalInstanceId == null) return;
            bool open = !IsRoomWindowOpen(s_WindowPortalInstanceId, opening);
            SetRoomWindowOpen(s_WindowPortalInstanceId, opening, open);
            try { if (soundAt != null) GameAudioManager.PlaySound(open ? "Play_SndMechDoorWoodOpen1" : "Play_SndMechDoorWoodClose1", soundAt); } catch { }
            MelonLogger.Msg($"[PENCERE-KANAT] {s_WindowPortalInstanceId}: oda penceresi {opening} " + (open ? "acildi." : "kapandi."),
                            $"[WINDOW-SASH] {s_WindowPortalInstanceId}: room window {opening} " + (open ? "opened." : "closed."));
        }

        // ─── AN OPEN WINDOW SEEN FROM OUTSIDE ───
        // The raised sash stands behind the upper pane, so from outside an open window is its lower pane
        // with no frost on it (owner, 2026-10-18: it looked shut). The outside frost is cut between the
        // building's own two panes; the snow on the sill stays.
        private class OutsideOpenCut { public GameObject Frost; public Mesh Own, Cut; public float Seam; public int Mask; }   // Seam: up the texture, 0..1; Mask: the hall's open panes
        private static readonly Dictionary<WindowCoverLayer, OutsideOpenCut> s_OutsideOpenCuts = new Dictionary<WindowCoverLayer, OutsideOpenCut>();
        private static readonly List<WindowCoverLayer> s_OutsideOpenStale = new List<WindowCoverLayer>();
        private static bool s_OutsideOpenFailed;

        // The raised sash itself, in a room borrowed for the view from outside (owner, 2026-10-18: "show the
        // raised sash"). Built like the room's own, glass and all, but the frame is left to that view.
        // Null Sash: tried and failed, not tried again while the room stays borrowed.
        private class OutsideSash
        {
            public string InstanceId; public int RoomWindow; public WindowSash Sash; public bool Wanted; public float RetryAt;
            public List<WindowSash> Extra;   // the hall's sashes, Sash first
            public List<GameObject> HallCovers; public List<Mesh> HallCoverMeshes; public int CoverMask;
            // The sash's pane, the lower part of the window's outside cover (owner, 2026-10-18: like cleaned glass).
            public GameObject Cover, CoverFor; public Mesh CoverMesh;
        }
        private static readonly List<OutsideSash> s_OutsideSashes = new List<OutsideSash>();

        // Every frame: a room handed back must lose its sash before the player stands in it.
        private static void TickOutsideOpenWindows()
        {
            if (s_OutsideOpenFailed) return;
            try
            {
                // Layers made again or gone take their own uncut quad with them.
                s_OutsideOpenStale.Clear();
                foreach (var pair in s_OutsideOpenCuts)
                    if (pair.Key.FrostGo == null || pair.Key.FrostGo != pair.Value.Frost) s_OutsideOpenStale.Add(pair.Key);
                foreach (var layer in s_OutsideOpenStale) UncutOutsideFrost(layer);

                foreach (var held in s_OutsideSashes) held.Wanted = false;
                foreach (var b in s_OutsideInBuildings)
                {
                    if (b == null || b.InstanceId == null) continue;
                    bool roomShown = b.Borrowed && b.InstanceId != s_WindowPortalInstanceId;
                    BuildingWindowOverride row = null;
                    if (roomShown && (b.Instance == null || b.Instance.Config == null || !TryGetBuildingWindowOverride(b.Instance.Config.ExteriorShellPrefabName, out row))) row = null;
                    int count = System.Math.Max(b.Covers.Count, b.ApertureRoomWindow.Count);
                    for (int a = 0; a < count; a++)
                    {
                        int room = a < b.ApertureRoomWindow.Count ? b.ApertureRoomWindow[a] : -1;
                        bool open = room >= 0 && IsRoomWindowOpen(b.InstanceId, room);

                        var layer = a < b.Covers.Count ? b.Covers[a] : null;

                        // Open or shut: the sash is part of the window (owner, 2026-10-18).
                        bool sashHere = roomShown && room >= 0 && a < b.ApertureOpenings.Count && IsOpenableWindow(row, room, b.ApertureOpenings[a])
                                     && KeepOutsideSash(b.InstanceId, room, b.ApertureOpenings[a], layer, open);

                        // Open, the building's cover is cut at the rail and the sash carries its lower part up. Shut, the
                        // whole cover is the glass: it stands on the snow bank's plane, and the sash's own pane drawn
                        // back in the room left a gap behind the bank that no fill closed well (owner, 2026-10-18).
                        bool hall = room >= 0 && a < b.ApertureOpenings.Count && IsHingedWindow(b.ApertureOpenings[a]);
                        if (layer != null && layer.FrostGo != null && hall)
                        {
                            // The hall's cover loses the cells of whichever of its panes stand open.
                            int mask = (open ? 1 : 0) | (IsRoomWindowOpen(b.InstanceId, room + CASEMENT_STATE) ? 2 : 0);
                            OutsideOpenCut had;
                            bool cut = s_OutsideOpenCuts.TryGetValue(layer, out had);
                            if (cut && had.Mask != mask) { UncutOutsideFrost(layer); cut = false; }
                            if (mask != 0 && !cut) CutHallOutsideFrost(layer, mask, IsGarageWindow(b.ApertureOpenings[a]));
                            if (sashHere) KeepHallSashCovers(b.InstanceId, room, layer, b.ApertureOpenings[a]);
                            ShowMullionTop(b.Instance, a, IsGarageWindow(b.ApertureOpenings[a]) ? mask == 0 : (mask & 2) == 0);
                        }
                        else if (layer != null && layer.FrostGo != null)
                        {
                            bool wantCut = open;
                            bool cut = s_OutsideOpenCuts.ContainsKey(layer);
                            if (wantCut && !cut) CutOutsideFrost(layer);
                            else if (!wantCut && cut) UncutOutsideFrost(layer);
                            if (sashHere) KeepSashCover(b.InstanceId, room, layer);
                        }
                    }
                }
                for (int i = s_OutsideSashes.Count - 1; i >= 0; i--)
                {
                    var held = s_OutsideSashes[i];
                    if (held.Wanted && (held.Sash == null || (held.Sash.Go != null && held.Sash.PaneRoot != null)))
                    {
                        if (held.Extra != null) foreach (var s in held.Extra) FollowGlass(s);
                        else if (held.Sash != null) FollowGlass(held.Sash);
                        continue;
                    }
                    DropSashCover(held);
                    TeardownOutsideSash(held);
                    s_OutsideSashes.RemoveAt(i);
                }
            }
            catch (System.Exception ex)
            {
                s_OutsideOpenFailed = true;
                try { TeardownOutsideSashes(); } catch { }
                MelonLogger.Warning($"[PENCERE-KANAT] Disaridan acik pencere kapatildi: {ex.Message}", $"[WINDOW-SASH] Open windows seen from outside turned off after an error: {ex.Message}");
            }
        }

        // Window models that are sash windows sliding up, wherever they stand (owner, 2026-10-18): Camp
        // Office's, Trapper's tall one and the farmhouse windows, its doubles with both sashes as one; Trapper's single Obj_WindowCabinB stays shut (owner). Matched on
        // the window object or a child of it, so LOD names count.
        private static readonly string[] SLIDE_UP_WINDOW_MODELS =
        {
            "OBJ_LakeCabinWindowA", "OBJ_LakeCabinWindowB", "Obj_WindowCabinA2", "STR_FarmHouseWindowA", "STR_FarmHouseWindowB", "STR_FarmHouseWindowC",
            "STR_FarmHouseWindowDoubleA", "STR_FarmHouseWindowDoubleB", "STR_FarmHouseParlourRoomWindowGlass",
            "STR_CoastalHouseHWindowsGlass", "STR_GreyMothersHouseInt",
        };

        // Two panes side by side; the left one slides right (owner, 2026-10-18, Coastal Highway house).
        private static readonly string[] SLIDE_RIGHT_WINDOW_MODELS = { "STR_GlassPane_Single" };
        // Two panes one over the other, a pane per sash; the lower one slides up (owner, 2026-10-18).
        private static readonly string[] PANE_SLIDE_UP_WINDOW_MODELS = { "STR_GlassPaneVertical_Single" };

        private static bool IsSlideUpWindowModel(InteriorWindowOpening opening) { return IsWindowModel(opening, SLIDE_UP_WINDOW_MODELS); }
        private static bool IsSlideRightWindowModel(InteriorWindowOpening opening) { return IsWindowModel(opening, SLIDE_RIGHT_WINDOW_MODELS); }

        private static bool IsWindowModel(InteriorWindowOpening opening, string[] models)
        {
            var pr = opening.PaneRoot;
            if (pr == null) return false;
            if (StartsWithModel(pr.name, models)) return true;
            // Coastal House H and Grey Mother's number their panes (Object022, GlassPane029); their group carries the model.
            if (pr.parent != null && StartsWithModel(pr.parent.name, models)) return true;
            for (int c = 0; c < pr.childCount; c++) if (StartsWithModel(pr.GetChild(c).name, models)) return true;
            return false;
        }

        private static bool StartsWithModel(string name, string[] models)
        {
            foreach (var model in models)
                if (name.StartsWith(model, System.StringComparison.OrdinalIgnoreCase)
                    && (name.Length == model.Length || !char.IsLetterOrDigit(name[model.Length]))) return true;
            return false;
        }

        // By its model, or listed in the building's row (row may be null).
        private static bool IsOpenableWindow(BuildingWindowOverride row, int roomWindow, InteriorWindowOpening opening)
        {
            if (opening.ViewOutOnly) return false;
            if (IsSlideUpWindowModel(opening) || IsSlideRightWindowModel(opening) || IsWindowModel(opening, PANE_SLIDE_UP_WINDOW_MODELS) || IsHingedWindow(opening)) return true;
            if (row == null || row.OpenableWindows == null) return false;
            foreach (var entry in row.OpenableWindows) if (entry != null && entry.RoomWindow == roomWindow) return true;
            return false;
        }

        private const float OUTSIDE_SASH_RETRY_SECONDS = 2f;

        // True while the window has its sash standing in the borrowed room.
        private static bool KeepOutsideSash(string instanceId, int roomWindow, InteriorWindowOpening opening, WindowCoverLayer layer, bool open)
        {
            // The room's own sash, there until the frame after the player leaves, still holds the glass cut.
            if (opening.PaneRoot == null) return false;
            // The hall's pair of hinged sashes.
            if (IsHingedWindow(opening)) return KeepOutsideHallSashes(instanceId, roomWindow, opening);
            foreach (var inside in s_WindowSashes) if (inside.PaneRoot == opening.PaneRoot) return false;

            OutsideSash kept = null;
            foreach (var held in s_OutsideSashes)
                if (held.InstanceId == instanceId && held.RoomWindow == roomWindow) { kept = held; break; }
            if (kept == null) { kept = new OutsideSash { InstanceId = instanceId, RoomWindow = roomWindow }; s_OutsideSashes.Add(kept); }
            kept.Wanted = true;

            // A failed build is tried again a little later, not given up for the whole borrowing.
            if (kept.Sash == null && Time.realtimeSinceStartup >= kept.RetryAt)
            {
                string why;
                kept.Sash = BuildSash(opening, roomWindow, false, out why);
                if (kept.Sash == null)
                {
                    kept.RetryAt = Time.realtimeSinceStartup + OUTSIDE_SASH_RETRY_SECONDS;
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[PENCERE-KANAT] {instanceId}: disaridan oda penceresi {roomWindow} kanatsiz: {why}",
                                        $"[WINDOW-SASH] {instanceId}: room window {roomWindow} seen from outside got no sash: {why}");
                }
            }
            if (kept.Sash == null || kept.Sash.Go == null) return false;

            float target = open ? 1f : 0f;
            if (kept.Sash.Shown != target) { kept.Sash.Shown = target; PlaceSash(kept.Sash); }
            // Its pane is the building's cover, open or shut; the room's glass would be a second sheet.
            kept.Sash.NoGameGlass = true;
            return true;
        }

        // The sash's pane, once the building's cover has been cut at the rail.
        private static void KeepSashCover(string instanceId, int roomWindow, WindowCoverLayer layer)
        {
            OutsideSash kept = null;
            foreach (var held in s_OutsideSashes)
                if (held.InstanceId == instanceId && held.RoomWindow == roomWindow) { kept = held; break; }
            if (kept == null || kept.Sash == null) return;

            bool coverWanted = s_OutsideOpenCuts.ContainsKey(layer);
            if (!coverWanted || kept.CoverFor != layer.FrostGo) { DropSashCover(kept); if (coverWanted) MakeSashCover(kept, layer); }
            if (kept.Cover != null && kept.Cover.activeSelf != layer.FrostGo.activeSelf) kept.Cover.SetActive(layer.FrostGo.activeSelf);
        }

        // The sash's pane in the room, textured with the lower part of the window's outside cover.
        private static void MakeSashCover(OutsideSash held, WindowCoverLayer layer)
        {
            var sash = held.Sash;
            var cut = s_OutsideOpenCuts[layer];
            if (sash.LowGlass == null || sash.GlassLower == null) return;

            // In the sash's own space, so it rides up with it. The cover's texture runs along the room's right.
            Transform st = sash.Go.transform, gt = sash.LowGlass.transform;
            Vector3 right = st.InverseTransformDirection(sash.Right).normalized, up = st.InverseTransformDirection(sash.Up).normalized;
            Vector3 outward = st.InverseTransformDirection(sash.Outward).normalized;
            float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue, o = 0f;
            var verts = CopyVerts(sash.GlassLower);
            foreach (var v in verts)
            {
                Vector3 p = st.InverseTransformPoint(gt.TransformPoint(v));
                float r = Vector3.Dot(p, right), u = Vector3.Dot(p, up);
                r0 = Mathf.Min(r0, r); r1 = Mathf.Max(r1, r); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                o += Vector3.Dot(p, outward);
            }
            o = o / Mathf.Max(verts.Length, 1) + SASH_COVER_PROUD;
            if (r1 - r0 < 0.05f || u1 - u0 < 0.05f) return;

            var mesh = new Mesh { name = "SI_SashCover" };
            mesh.vertices = new[] { right * r0 + up * u0 + outward * o, right * r1 + up * u0 + outward * o, right * r0 + up * u1 + outward * o, right * r1 + up * u1 + outward * o };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, cut.Seam), new Vector2(1f, cut.Seam) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            held.Cover = new GameObject("SI_SashCover");
            held.Cover.layer = sash.Go.layer;
            held.Cover.transform.SetParent(st, false);
            held.Cover.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = held.Cover.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = layer.FrostMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            held.CoverMesh = mesh;
            held.CoverFor = layer.FrostGo;
        }

        private static void DropSashCover(OutsideSash held)
        {
            if (held.Cover != null) Object.Destroy(held.Cover);
            if (held.CoverMesh != null) Object.Destroy(held.CoverMesh);
            held.Cover = null; held.CoverMesh = null; held.CoverFor = null;
            DropHallSashCovers(held);
        }

        private static bool HasOutsideSashes(string instanceId)
        {
            foreach (var held in s_OutsideSashes) if (held.InstanceId == instanceId && held.Sash != null) return true;
            return false;
        }

        private static void TeardownOutsideSashes()
        {
            foreach (var held in s_OutsideSashes) { DropSashCover(held); TeardownOutsideSash(held); }
            s_OutsideSashes.Clear();
        }

        // Between the building's lower and upper pane, as the outside layer measured them; nothing for one pane.
        private static void CutOutsideFrost(WindowCoverLayer layer)
        {
            if (layer.Panes == null || layer.Panes.Count < 2) return;
            float lowest = float.MaxValue;
            foreach (var p in layer.Panes) lowest = Mathf.Min(lowest, p[2]);
            float upperBottom = float.MaxValue;
            foreach (var p in layer.Panes) if (p[2] > lowest + 0.1f) upperBottom = Mathf.Min(upperBottom, p[2]);
            if (upperBottom == float.MaxValue) return;
            float lowerTop = float.MinValue;
            foreach (var p in layer.Panes) if (p[3] < upperBottom + 0.01f) lowerTop = Mathf.Max(lowerTop, p[3]);
            if (lowerTop == float.MinValue) return;
            float seam = (lowerTop + upperBottom) * 0.5f;

            var mf = layer.FrostGo.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var v = CopyVerts(mf.sharedMesh);
            if (v.Length != 4) return;
            Transform t = mf.transform;
            float ub = Vector3.Dot(t.TransformPoint(v[0]) - layer.Center, layer.Up), ut = Vector3.Dot(t.TransformPoint(v[2]) - layer.Center, layer.Up);
            float f = (seam - ub) / (ut - ub);
            if (f <= 0.01f || f >= 0.99f) return;

            var cut = new OutsideOpenCut { Frost = layer.FrostGo, Own = mf.sharedMesh, Cut = Object.Instantiate(mf.sharedMesh), Seam = f };
            v[0] = t.InverseTransformPoint(t.TransformPoint(v[0]) + layer.Up * (seam - ub));
            v[1] = t.InverseTransformPoint(t.TransformPoint(v[1]) + layer.Up * (seam - ub));
            cut.Cut.vertices = v;
            cut.Cut.uv = new[] { new Vector2(0, f), new Vector2(1, f), new Vector2(0, 1), new Vector2(1, 1) };
            cut.Cut.RecalculateBounds();
            mf.sharedMesh = cut.Cut;
            s_OutsideOpenCuts[layer] = cut;
        }

        private static void UncutOutsideFrost(WindowCoverLayer layer)
        {
            OutsideOpenCut cut;
            if (!s_OutsideOpenCuts.TryGetValue(layer, out cut)) return;
            s_OutsideOpenCuts.Remove(layer);
            if (cut.Frost != null)
            {
                var mf = cut.Frost.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh == cut.Cut) mf.sharedMesh = cut.Own;
            }
            if (cut.Cut != null) Object.Destroy(cut.Cut);
        }

        private static bool OutsideInHoldsFrames(string instanceId)
        {
            foreach (var b in s_OutsideInBuildings)
                if (b != null && b.InstanceId == instanceId && b.ResizedFrames.Count > 0) return true;
            return false;
        }

        // Everything the game's window had is given back: frame, glass and frost meshes.
        private static void TeardownWindowSashes()
        {
            foreach (var sash in s_WindowSashes) TeardownSash(sash);
            s_WindowSashes.Clear();
            s_WindowSashesKey = null;
        }

        private static void TeardownSash(WindowSash sash)
        {
            try
            {
                // The view from outside resized the cut frame on the way out and keeps it to give back:
                // it gets the game's own mesh instead, or it would give back a destroyed one (owner, 2026-10-18).
                if (sash.FrameCut != null)
                    foreach (var b in s_OutsideInBuildings)
                        if (b != null)
                            for (int i = 0; i < b.ResizedFrameOriginals.Count; i++)
                                if (b.ResizedFrameOriginals[i] == sash.FrameCut) b.ResizedFrameOriginals[i] = sash.FrameOwn;
                DropSashFrost(sash);
                if (sash.FrameMf != null && sash.FrameMf.sharedMesh == sash.FrameCut) sash.FrameMf.sharedMesh = sash.FrameOwn;
                if (sash.GlassMf != null && sash.GlassMf.sharedMesh == sash.GlassUpper) sash.GlassMf.sharedMesh = sash.GlassOwn;
                // Renamed first: Destroy waits for the frame's end, and a sash built the same frame looks for the name.
                if (sash.Go != null) { sash.Go.name = "SI_Sash_Gone"; Object.Destroy(sash.Go); }
                if (sash.SashMesh != null) Object.Destroy(sash.SashMesh);
                if (sash.FrameCut != null) Object.Destroy(sash.FrameCut);
                if (sash.GlassUpper != null) Object.Destroy(sash.GlassUpper);
                if (sash.GlassLower != null) Object.Destroy(sash.GlassLower);
                if (sash.SharedGlass) ShowSharedGlass(sash);
                ShowFrameTriangles(sash);
            }
            catch { }
        }
    }
}
