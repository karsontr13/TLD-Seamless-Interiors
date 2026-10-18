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
        // Owner, 2026-10-18: windows listed in the building's row (OpenableWindows) open and close by
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
        // The frame's room-side trim strip in GLB_WoodWallNatural_C01_Flat, so the sash matches its colour.
        private const float SASH_UV_U0 = 0.25f, SASH_UV_V0 = 0.303f, SASH_UV_PER_M = 0.27f;
        private const float SASH_OPEN_HOLD_SECONDS = 1f;

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
            public Vector3 Up;          // world
            public float Seam;          // up from the glass centre, metres
            public float SashMid;       // the sash's depth, out from the glass plane
            public float Slide;         // metres
            public float Shown = -1f;   // 0 closed .. 1 open
            public float AnimFrom, AnimStart = -1f;
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
                    FollowFrostLayer(sash);
                    FollowGlass(sash);
                    SlideSash(sash, IsRoomWindowOpen(s_WindowPortalInstanceId, sash.Opening) ? 1f : 0f, now);
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
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out row) || row.OpenableWindows == null) return;

            foreach (var entry in row.OpenableWindows)
            {
                if (entry == null || entry.RoomWindow < 0 || entry.RoomWindow >= s_WindowPortalOpenings.Count) continue;
                string why;
                var sash = BuildSlideUpSash(entry.RoomWindow, out why);
                if (sash == null)
                {
                    MelonLogger.Warning($"[PENCERE-KANAT] {s_WindowPortalInstanceId}: oda penceresi {entry.RoomWindow} kanatsiz kaldi: {why}",
                                        $"[WINDOW-SASH] {s_WindowPortalInstanceId}: room window {entry.RoomWindow} got no sash: {why}");
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
            for (int c = 0; c < opening.PaneRoot.childCount; c++)
            {
                var child = opening.PaneRoot.GetChild(c);
                var mf = child.GetComponent<MeshFilter>();
                var renderer = child.GetComponent<MeshRenderer>();
                if (mf == null || renderer == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                if (opening.Glass != null && opening.Glass.Contains(renderer)) continue;
                if (opening.Frost != null && opening.Frost.Contains(renderer)) continue;
                if (child.name.Contains("Snow")) continue;
                var b = renderer.bounds;
                float depth = Mathf.Abs(Vector3.Dot(b.size, opening.Outward));
                if (depth > 0.25f) continue;
                float area = b.size.sqrMagnitude - depth * depth;
                if (area > bestArea) { bestArea = area; best = mf; }
            }
            return best;
        }

        private static Vector3[] CopyVerts(Mesh mesh)
        {
            var src = mesh.vertices;
            var v = new Vector3[src.Length];
            for (int i = 0; i < v.Length; i++) v[i] = src[i];
            return v;
        }

        private static WindowSash BuildSlideUpSash(int index, out string why)
        {
            why = null;
            var opening = s_WindowPortalOpenings[index];
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
            var frameMf = FindSashFrame(opening);
            if (frameMf == null) { why = "no frame mesh"; return null; }

            Vector3 origin = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;

            // The meeting rail: the glass is two panes, and the seam is the height between them.
            var glassVerts = CopyVerts(glassMf.sharedMesh);
            var heights = new List<float>();
            foreach (var v in glassVerts) heights.Add(Vector3.Dot(glassMf.transform.TransformPoint(v) - origin, U));
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
            var world = new Vector3[frameVerts.Length];
            for (int i = 0; i < frameVerts.Length; i++)
            {
                world[i] = frameT.TransformPoint(frameVerts[i]);
                roomFace = Mathf.Min(roomFace, Vector3.Dot(world[i] - origin, O));
            }
            float left = float.MinValue, right = float.MaxValue, bottom = float.MinValue;
            for (int i = 0; i < world.Length; i++)
            {
                Vector3 p = world[i] - origin;
                float o = Vector3.Dot(p, O), r = Vector3.Dot(p, R), u = Vector3.Dot(p, U);
                if (o > roomFace + 0.01f || Mathf.Abs(u - seam) < 0.1f) continue;
                if (r < 0f) left = Mathf.Max(left, r); else right = Mathf.Min(right, r);
                if (u < seam) bottom = Mathf.Max(bottom, u);
            }
            if (left == float.MinValue || right == float.MaxValue || bottom == float.MinValue || right - left < 0.2f || seam - bottom < 0.2f)
            { why = "the frame's opening could not be read"; return null; }

            // The frame's own meeting rail says where the seam is: the glass is mounted upside down in half
            // the Camp Office's windows, which put its pane seam 7 cm off the rail.
            float railLow = float.MaxValue, railHigh = float.MinValue;
            for (int i = 0; i < world.Length; i++)
            {
                Vector3 p = world[i] - origin;
                float r = Vector3.Dot(p, R), u = Vector3.Dot(p, U);
                // The rail runs into the jambs, past the opening's sides.
                if (r < left - 0.1f || r > right + 0.1f || Mathf.Abs(u - seam) > 0.15f) continue;
                railLow = Mathf.Min(railLow, u); railHigh = Mathf.Max(railHigh, u);
            }
            float sashTop = seam + SASH_RAIL_OVER_SEAM;
            if (railLow < railHigh) { seam = (railLow + railHigh) * 0.5f; sashTop = railHigh; }
            if (roomFace > -2f * SASH_DEPTH_GAP - 0.02f) { why = "no room for a sash between the frame's face and the glass"; return null; }

            var sash = new WindowSash { Opening = index, PaneRoot = pr, Up = U, Seam = seam };
            float o0 = roomFace + SASH_DEPTH_GAP, o1 = -SASH_DEPTH_GAP;
            sash.SashMid = (o0 + o1) * 0.5f;
            sash.Slide = (seam - bottom) * SASH_SLIDE_SHARE;

            // The frame's rail keeps its outer half, so the sash passes it on the room side.
            sash.FrameMf = frameMf;
            sash.FrameOwn = frameMf.sharedMesh;
            sash.FrameCut = Object.Instantiate(sash.FrameOwn);
            for (int i = 0; i < world.Length; i++)
            {
                Vector3 p = world[i] - origin;
                float o = Vector3.Dot(p, O), r = Vector3.Dot(p, R), u = Vector3.Dot(p, U);
                if (o >= 0f || Mathf.Abs(u - seam) >= 0.1f || r < left - 0.1f || r > right + 0.1f) continue;
                frameVerts[i] = frameT.InverseTransformPoint(world[i] - O * o);
            }
            sash.FrameCut.vertices = frameVerts;
            sash.FrameCut.RecalculateBounds();
            frameMf.sharedMesh = sash.FrameCut;

            sash.Go = new GameObject("SI_Sash");
            sash.Go.layer = frameMf.gameObject.layer;
            sash.Go.transform.SetParent(pr, false);
            sash.SashMesh = BuildSashMesh(pr, origin, R, U, O, left + SASH_INSET, right - SASH_INSET, bottom + SASH_INSET, sashTop, o0, o1);
            sash.Go.AddComponent<MeshFilter>().sharedMesh = sash.SashMesh;
            sash.Go.AddComponent<MeshRenderer>().sharedMaterial = frameMf.GetComponent<MeshRenderer>().sharedMaterial;

            // The glass: its upper pane stays, its lower pane rides on the sash.
            sash.GlassMf = glassMf;
            sash.GlassRenderer = glassRenderer;
            sash.GlassOwn = glassMf.sharedMesh;
            sash.GlassUpper = CutMeshAtSeam(sash.GlassOwn, glassMf.transform, origin, U, seam, true);
            sash.GlassLower = CutMeshAtSeam(sash.GlassOwn, glassMf.transform, origin, U, seam, false);
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
        private static Mesh BuildSashMesh(Transform pr, Vector3 origin, Vector3 R, Vector3 U, Vector3 O,
                                          float r0, float r1, float u0, float u1, float o0, float o1)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float w = SASH_BAR;
            AddSashBar(verts, uvs, tris, r0 + w, r1 - w, u0, u0 + w, o0, o1);
            AddSashBar(verts, uvs, tris, r0 + w, r1 - w, u1 - SASH_TOP_RAIL, u1, o0, o1);
            AddSashBar(verts, uvs, tris, r0, r0 + w, u0, u1, o0, o1);
            AddSashBar(verts, uvs, tris, r1 - w, r1, u0, u1, o0, o1);

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
            if (sash.LowGlass.enabled != sash.GlassRenderer.enabled) sash.LowGlass.enabled = sash.GlassRenderer.enabled;
            if (sash.LowGlass.sharedMaterial != sash.GlassRenderer.sharedMaterial) sash.LowGlass.sharedMaterial = sash.GlassRenderer.sharedMaterial;
        }

        // The cover layer's frost quad is cut at the seam once it exists, and again whenever it is remade.
        private static void FollowFrostLayer(WindowSash sash)
        {
            GameObject frost = null;
            WindowCoverLayer layer = null;
            if (WindowCoverLayers && sash.Opening < s_CoverLayers.Count) layer = s_CoverLayers[sash.Opening];
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
            Vector3 origin = opening.GlassCenter, U = opening.Up, O = opening.Outward;
            // The quad is in world space: 0, 1 along the bottom, 2, 3 along the top.
            var v = CopyVerts(mf.sharedMesh);
            if (v.Length != 4) return;
            Transform t = mf.transform;
            float ub = Vector3.Dot(t.TransformPoint(v[0]) - origin, U), ut = Vector3.Dot(t.TransformPoint(v[2]) - origin, U);
            float f = (sash.Seam - ub) / (ut - ub);
            if (f <= 0.01f || f >= 0.99f) return;

            sash.FrostOwn = mf.sharedMesh;
            sash.FrostUpper = Object.Instantiate(sash.FrostOwn);
            var up = CopyVerts(sash.FrostOwn);
            var uvUp = new[] { new Vector2(0, f), new Vector2(1, f), new Vector2(0, 1), new Vector2(1, 1) };
            up[0] = t.InverseTransformPoint(t.TransformPoint(v[0]) + U * (sash.Seam - ub));
            up[1] = t.InverseTransformPoint(t.TransformPoint(v[1]) + U * (sash.Seam - ub));
            sash.FrostUpper.vertices = up;
            sash.FrostUpper.uv = uvUp;
            sash.FrostUpper.RecalculateBounds();
            mf.sharedMesh = sash.FrostUpper;

            // The lower half on the sash, at the sash's depth.
            float frostOut = Vector3.Dot(t.TransformPoint(v[0]) - origin, O);
            Vector3 shift = O * (sash.SashMid - frostOut);
            var low = new Vector3[4];
            Transform st = sash.Go.transform;
            low[0] = st.InverseTransformPoint(t.TransformPoint(v[0]) + shift);
            low[1] = st.InverseTransformPoint(t.TransformPoint(v[1]) + shift);
            low[2] = st.InverseTransformPoint(t.TransformPoint(v[2]) + U * (sash.Seam - ut) + shift);
            low[3] = st.InverseTransformPoint(t.TransformPoint(v[3]) + U * (sash.Seam - ut) + shift);
            sash.FrostLower = Object.Instantiate(sash.FrostOwn);
            sash.FrostLower.vertices = low;
            sash.FrostLower.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, f), new Vector2(1, f) };
            sash.FrostLower.RecalculateBounds();

            sash.LowFrost = new GameObject("SI_SashFrost");
            sash.LowFrost.transform.SetParent(st, false);
            sash.LowFrost.AddComponent<MeshFilter>().sharedMesh = sash.FrostLower;
            var renderer = sash.LowFrost.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = layer.FrostMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
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
            sash.Go.transform.localPosition = sash.PaneRoot.InverseTransformVector(sash.Up * (sash.Slide * sash.Shown));
        }

        // The interact hold on an openable window's target lands here.
        private static void ToggleWindowOpen(int opening, GameObject soundAt)
        {
            if (SashOf(opening) == null || s_WindowPortalInstanceId == null) return;
            bool open = !IsRoomWindowOpen(s_WindowPortalInstanceId, opening);
            SetRoomWindowOpen(s_WindowPortalInstanceId, opening, open);
            try { if (soundAt != null) GameAudioManager.PlaySound(open ? "Play_SndMechDoorWoodOpen1" : "Play_SndMechDoorWoodClose1", soundAt); } catch { }
            MelonLogger.Msg($"[PENCERE-KANAT] {s_WindowPortalInstanceId}: oda penceresi {opening} " + (open ? "acildi." : "kapandi."),
                            $"[WINDOW-SASH] {s_WindowPortalInstanceId}: room window {opening} " + (open ? "opened." : "closed."));
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
            foreach (var sash in s_WindowSashes)
            {
                try
                {
                    // The view from outside resized the cut frame on the way out and keeps it to give back:
                    // it gets the game's own mesh instead, or it would give back a destroyed one (owner, 2026-10-18).
                    foreach (var b in s_OutsideInBuildings)
                        if (b != null)
                            for (int i = 0; i < b.ResizedFrameOriginals.Count; i++)
                                if (b.ResizedFrameOriginals[i] == sash.FrameCut) b.ResizedFrameOriginals[i] = sash.FrameOwn;
                    DropSashFrost(sash);
                    if (sash.FrameMf != null && sash.FrameMf.sharedMesh == sash.FrameCut) sash.FrameMf.sharedMesh = sash.FrameOwn;
                    if (sash.GlassMf != null && sash.GlassMf.sharedMesh == sash.GlassUpper) sash.GlassMf.sharedMesh = sash.GlassOwn;
                    if (sash.Go != null) Object.Destroy(sash.Go);
                    if (sash.SashMesh != null) Object.Destroy(sash.SashMesh);
                    if (sash.FrameCut != null) Object.Destroy(sash.FrameCut);
                    if (sash.GlassUpper != null) Object.Destroy(sash.GlassUpper);
                    if (sash.GlassLower != null) Object.Destroy(sash.GlassLower);
                }
                catch { }
            }
            s_WindowSashes.Clear();
            s_WindowSashesKey = null;
        }
    }
}
