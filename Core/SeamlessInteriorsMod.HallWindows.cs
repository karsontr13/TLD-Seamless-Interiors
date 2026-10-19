using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── COMMUNITY HALL WINDOWS ───
        // Owner, 2026-10-19: the lower three panes tilt in as hoppers, the upper middle one swings in as
        // a casement, each on its own hold. First the frame is squared: its mullions lean 4 cm, its transom slopes 3 cm.

        private const string HALL_FRAMES = "STR_CommunityHallA_Interior_WinDoors_Prefab";
        private static readonly string[] HALL_GLASS_MODELS = { "STR_CommunityHallA_GlassWin" };
        // The casement's open state is kept as its room window plus this.
        internal const int CASEMENT_STATE = 1000;
        private const float HOPPER_ANGLE = 35f, CASEMENT_ANGLE = 80f;
        private const float HINGED_SASH_DEPTH = 0.034f;
        // The hall's window parts, across its 1.67 m: mullions within these of the middle, the transom in this band.
        private const float HALL_MULLION_NEAR = 0.19f, HALL_MULLION_FAR = 0.42f;
        private const float HALL_TRANSOM_LOW = -0.37f, HALL_TRANSOM_HIGH = -0.17f;

        private static bool IsHallWindow(InteriorWindowOpening opening) { return IsWindowModel(opening, HALL_GLASS_MODELS); }

        // ─── SQUARING THE FRAME ───

        private struct HallPoint { public bool Frame; public int Index; public float R, U, O; }

        // Before the room's covers measure its glass. The frame becomes a readable copy read from the GPU; the
        // glass a copy of its own. Each window's mullions and transom are then made straight, even and symmetric.
        private static void SquareHallWindows(List<InteriorWindowOpening> openings)
        {
            try
            {
                MeshFilter glassMf = null;
                var hall = new List<InteriorWindowOpening>();
                foreach (var o in openings)
                    if (o.PaneRoot != null && IsHallWindow(o)) { hall.Add(o); if (glassMf == null) glassMf = o.PaneRoot.GetComponent<MeshFilter>(); }
                if (hall.Count == 0 || glassMf == null || glassMf.sharedMesh == null || !glassMf.sharedMesh.isReadable) return;
                MeshFilter frameMf = FindHallFrames(glassMf.transform);
                if (frameMf == null || frameMf.sharedMesh == null) return;
                if (!frameMf.sharedMesh.isReadable)
                {
                    var renderer = frameMf.GetComponent<MeshRenderer>();
                    var copy = ReadableCopyFromGpu(frameMf.sharedMesh, renderer != null ? renderer.sharedMaterials.Length : 1);
                    if (copy == null)
                    {
                        MelonLogger.Warning("[SALON-PENCERE] Cerceve GPU'dan kopyalanamadi.", "[HALL-WINDOW] The frame could not be copied from the GPU.");
                        return;
                    }
                    frameMf.sharedMesh = copy;
                }
                if (!glassMf.sharedMesh.name.EndsWith("_SI"))
                {
                    var copy = Object.Instantiate(glassMf.sharedMesh);
                    copy.name = glassMf.sharedMesh.name + "_SI";
                    glassMf.sharedMesh = copy;
                }

                var frameWorld = WorldVerts(frameMf);
                var glassWorld = WorldVerts(glassMf);
                // Plain arrays: the game's array type is copied on the way into a call, and the changes were lost.
                Vector3[] frameLocal = frameMf.sharedMesh.vertices;
                Vector3[] glassLocal = glassMf.sharedMesh.vertices;
                var toFrame = frameMf.transform.worldToLocalMatrix;
                var toGlass = glassMf.transform.worldToLocalMatrix;
                int moved = 0;
                foreach (var o in hall) moved += SquareHallWindow(o, frameWorld, glassWorld, frameLocal, glassLocal, toFrame, toGlass);
                frameMf.sharedMesh.vertices = frameLocal;
                frameMf.sharedMesh.RecalculateBounds();
                glassMf.sharedMesh.vertices = glassLocal;
                glassMf.sharedMesh.RecalculateBounds();
                if (s_DebugBounds)
                    MelonLogger.Msg($"[SALON-PENCERE] {hall.Count} pencere duzeltildi, {moved} nokta.", $"[HALL-WINDOW] {hall.Count} windows squared, {moved} points moved.");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[SALON-PENCERE] Duzeltilemedi: {ex.Message}", $"[HALL-WINDOW] Could not square the windows: {ex.Message}");
            }
        }

        private static MeshFilter FindHallFrames(Transform near)
        {
            foreach (var mf in near.root.GetComponentsInChildren<MeshFilter>(true)) if (mf.name == HALL_FRAMES) return mf;
            return null;
        }

        private static int SquareHallWindow(InteriorWindowOpening opening, Vector3[] frameWorld, Vector3[] glassWorld,
                                            Vector3[] frameLocal, Vector3[] glassLocal, Matrix4x4 toFrame, Matrix4x4 toGlass)
        {
            Vector3 c = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;
            float halfW = opening.GlassWidth * 0.5f + 0.05f, halfH = opening.GlassHeight * 0.5f + 0.08f;
            var pts = new List<HallPoint>();
            for (int pass = 0; pass < 2; pass++)
            {
                var world = pass == 0 ? frameWorld : glassWorld;
                for (int i = 0; i < world.Length; i++)
                {
                    Vector3 p = world[i] - c;
                    float r = Vector3.Dot(p, R), u = Vector3.Dot(p, U), o = Vector3.Dot(p, O);
                    if (Mathf.Abs(r) < halfW && Mathf.Abs(u) < halfH && o > -0.09f && o < 0.035f)
                        pts.Add(new HallPoint { Frame = pass == 0, Index = i, R = r, U = u, O = o });
                }
            }
            float midH = opening.GlassHeight * 0.5f + 0.04f;
            System.Func<HallPoint, bool> inLeft = p => p.R > -HALL_MULLION_FAR && p.R < -HALL_MULLION_NEAR && Mathf.Abs(p.U) < midH;
            System.Func<HallPoint, bool> inRight = p => p.R > HALL_MULLION_NEAR && p.R < HALL_MULLION_FAR && Mathf.Abs(p.U) < midH;
            System.Func<HallPoint, bool> inTransom = p => p.U > HALL_TRANSOM_LOW && p.U < HALL_TRANSOM_HIGH && Mathf.Abs(p.R) < halfW;

            Vector2 left = FitCentreLine(pts, inLeft, true), right = FitCentreLine(pts, inRight, true), transom = FitCentreLine(pts, inTransom, false);
            if (float.IsNaN(left.x) || float.IsNaN(right.x) || float.IsNaN(transom.x)) return 0;
            // Mullions mirrored about their own middle; the transom level where it crosses that middle.
            float middle = (left.x + right.x) * 0.5f, half = (right.x - left.x) * 0.5f;
            float leftAt = middle - half, rightAt = middle + half, transomAt = transom.x + transom.y * middle;

            // One width for each face of a part, the same on both mullions.
            var mullionSides = new Dictionary<string, Vector2>();
            var transomSides = new Dictionary<string, Vector2>();
            foreach (var p in pts)
            {
                if (inLeft(p) || inRight(p))
                {
                    bool isLeft = inLeft(p);
                    Vector2 line = isLeft ? left : right;
                    float off = p.R - (line.x + line.y * p.U);
                    bool inner = isLeft ? off > 0f : off < 0f;
                    Add(mullionSides, Layer(p.O) + (inner ? "in" : "out"), Mathf.Abs(off));
                }
                if (inTransom(p))
                {
                    float off = p.U - (transom.x + transom.y * p.R);
                    Add(transomSides, Layer(p.O) + (off > 0f ? "+" : "-"), off);
                }
            }

            int moved = 0;
            foreach (var p in pts)
            {
                float r = p.R, u = p.U;
                if (inLeft(p) || inRight(p))
                {
                    bool isLeft = inLeft(p);
                    Vector2 line = isLeft ? left : right;
                    float off = p.R - (line.x + line.y * p.U);
                    bool inner = isLeft ? off > 0f : off < 0f;
                    Vector2 side = mullionSides[Layer(p.O) + (inner ? "in" : "out")];
                    float width = side.x / side.y;
                    r = isLeft ? leftAt + (inner ? width : -width) : rightAt + (inner ? -width : width);
                }
                if (inTransom(p))
                {
                    float off = p.U - (transom.x + transom.y * p.R);
                    Vector2 side = transomSides[Layer(p.O) + (off > 0f ? "+" : "-")];
                    u = transomAt + side.x / side.y;
                }
                if (r == p.R && u == p.U) continue;
                Vector3 world = c + R * r + U * u + O * p.O;
                if (p.Frame) frameLocal[p.Index] = toFrame.MultiplyPoint3x4(world);
                else glassLocal[p.Index] = toGlass.MultiplyPoint3x4(world);
                moved++;
            }
            return moved;
        }

        // Depth layers a centimetre apart; the glass and the frame's faces on its plane are one.
        private static string Layer(float o) { return ((int)Mathf.Round(o * 100f) + 0).ToString() + "|"; }

        private static void Add(Dictionary<string, Vector2> sums, string key, float value)
        {
            Vector2 s;
            sums.TryGetValue(key, out s);
            sums[key] = new Vector2(s.x + value, s.y + 1f);
        }

        // The part's centre line, across = a + b * along, through the middle of its extent at each level along it.
        private static Vector2 FitCentreLine(List<HallPoint> pts, System.Func<HallPoint, bool> inPart, bool vertical)
        {
            var levels = new Dictionary<int, Vector2>();
            foreach (var p in pts)
            {
                if (!inPart(p)) continue;
                float along = vertical ? p.U : p.R, across = vertical ? p.R : p.U;
                int key = Mathf.RoundToInt(along * 200f);
                Vector2 span;
                if (!levels.TryGetValue(key, out span)) span = new Vector2(float.MaxValue, float.MinValue);
                levels[key] = new Vector2(Mathf.Min(span.x, across), Mathf.Max(span.y, across));
            }
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            int n = 0;
            foreach (var kv in levels)
            {
                if (kv.Value.y - kv.Value.x < 0.05f) continue;   // one side only
                double x = kv.Key / 200.0, y = (kv.Value.x + kv.Value.y) * 0.5;
                sx += x; sy += y; sxx += x * x; sxy += x * y; n++;
            }
            if (n < 2) return new Vector2(float.NaN, 0f);
            double b = (n * sxy - sx * sy) / (n * sxx - sx * sx);
            return new Vector2((float)((sy - b * sx) / n), (float)b);
        }

        // ─── THE PANES ───

        // The window's panes on the (squared) glass, as (left, right, bottom, top) from its centre; lower row
        // and upper row, each left to right. Pane triangles share a plane and pair into rectangles.
        private static bool HallPanes(InteriorWindowOpening opening, out MeshFilter glassMf, out List<Vector4> lower, out List<Vector4> upper)
        {
            lower = new List<Vector4>(); upper = new List<Vector4>();
            glassMf = opening.PaneRoot != null ? opening.PaneRoot.GetComponent<MeshFilter>() : null;
            if (glassMf == null || glassMf.sharedMesh == null || !glassMf.sharedMesh.isReadable) return false;
            var mesh = OriginalGlassMesh(glassMf.sharedMesh);
            var world = WorldVerts(glassMf);
            var tris = SharedGlassBaseTriangles(mesh);
            Vector3 c = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;
            float halfW = opening.GlassWidth * 0.5f + 0.05f, halfH = opening.GlassHeight * 0.5f + 0.05f;
            var rects = new List<Vector4>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue;
                bool own = true;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = world[tris[t + k]] - c;
                    float r = Vector3.Dot(p, R), u = Vector3.Dot(p, U);
                    if (Mathf.Abs(Vector3.Dot(p, O)) > 0.03f || Mathf.Abs(r) > halfW || Mathf.Abs(u) > halfH) { own = false; break; }
                    r0 = Mathf.Min(r0, r); r1 = Mathf.Max(r1, r); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                }
                if (!own) continue;
                var box = new Vector4(r0, r1, u0, u1);
                bool merged = false;
                for (int i = 0; i < rects.Count; i++)
                {
                    var b = rects[i];
                    // Two halves of one pane share most of their box.
                    if (Mathf.Abs(b.x - box.x) < 0.05f && Mathf.Abs(b.y - box.y) < 0.05f && Mathf.Abs(b.z - box.z) < 0.05f && Mathf.Abs(b.w - box.w) < 0.05f)
                    { rects[i] = new Vector4(Mathf.Min(b.x, box.x), Mathf.Max(b.y, box.y), Mathf.Min(b.z, box.z), Mathf.Max(b.w, box.w)); merged = true; break; }
                }
                if (!merged) rects.Add(box);
            }
            if (rects.Count != 6) return false;
            rects.Sort((a, b) => a.z.CompareTo(b.z));
            for (int i = 0; i < 6; i++) (i < 3 ? lower : upper).Add(rects[i]);
            lower.Sort((a, b) => a.x.CompareTo(b.x));
            upper.Sort((a, b) => a.x.CompareTo(b.x));
            return lower[2].w < upper[0].z;
        }

        // The glass triangles (starts in the list) inside any of these rects.
        private static HashSet<int> GlassTrianglesIn(MeshFilter glassMf, InteriorWindowOpening opening, List<Vector4> rects)
        {
            var found = new HashSet<int>();
            var world = WorldVerts(glassMf);
            var tris = SharedGlassBaseTriangles(OriginalGlassMesh(glassMf.sharedMesh));
            Vector3 c = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 mid = (world[tris[t]] + world[tris[t + 1]] + world[tris[t + 2]]) / 3f - c;
                float r = Vector3.Dot(mid, R), u = Vector3.Dot(mid, U);
                if (Mathf.Abs(Vector3.Dot(mid, O)) > 0.03f) continue;
                foreach (var b in rects) if (r > b.x && r < b.y && u > b.z && u < b.w) { found.Add(t); break; }
            }
            return found;
        }

        // ─── THE HINGED SASHES ───

        private static List<WindowSash> BuildHallSashes(InteriorWindowOpening opening, int index, out string why)
        {
            why = null;
            MeshFilter glassMf;
            List<Vector4> lower, upper;
            if (!HallPanes(opening, out glassMf, out lower, out upper)) { why = "its six panes could not be read"; return null; }
            var frameMf = FindHallFrames(opening.PaneRoot);
            if (frameMf == null || frameMf.sharedMesh == null || !frameMf.sharedMesh.isReadable) { why = "no readable frame"; return null; }
            // The left mullion stands between the lower row's first and second panes; the bars take their texture from
            // its face low down, where the atlas is the frame's wood (it holds glass and other parts beside it).
            float mullionLeft = lower[0].y, mullionRight = lower[1].x, mullionBottom = lower[0].z;
            var sashes = new List<WindowSash>();
            var hopper = BuildHingedSash(opening, index, index, lower, false, glassMf, frameMf, mullionLeft, mullionRight, mullionBottom);
            if (hopper != null) sashes.Add(hopper);
            var casement = BuildHingedSash(opening, index, index + CASEMENT_STATE, new List<Vector4> { upper[1] }, true, glassMf, frameMf, mullionLeft, mullionRight, mullionBottom);
            if (casement != null) sashes.Add(casement);
            if (sashes.Count == 0) why = "no sash could be built";
            return sashes;
        }

        // A sash over the given panes, hinged on their bottom edge (tilting in) or on their left side (swinging in).
        private static WindowSash BuildHingedSash(InteriorWindowOpening opening, int index, int state, List<Vector4> panes, bool side,
                                                  MeshFilter glassMf, MeshFilter frameMf, float mullionLeft, float mullionRight, float mullionBottom)
        {
            Transform pr = opening.PaneRoot;
            string name = "SI_Sash_" + state;
            if (pr.Find(name) != null) return null;
            Vector3 origin = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;
            float o1 = -SASH_DEPTH_GAP, o0 = o1 - HINGED_SASH_DEPTH;
            var bays = new float[panes.Count * 2];
            float u0 = float.MaxValue, u1 = float.MinValue;
            for (int i = 0; i < panes.Count; i++)
            {
                bays[i * 2] = panes[i].x + SASH_INSET; bays[i * 2 + 1] = panes[i].y - SASH_INSET;
                u0 = Mathf.Min(u0, panes[i].z); u1 = Mathf.Max(u1, panes[i].w);
            }
            u0 += SASH_INSET; u1 -= SASH_INSET;

            var sash = new WindowSash
            {
                Opening = index, StateId = state, PaneRoot = pr, Up = U, Right = R, Outward = O, SharedGlass = true,
                Bays = bays, EdgeLeft = bays[0], EdgeRight = bays[bays.Length - 1], EdgeBottom = u0, RailTop = u1,
                Seam = u1 - 0.001f, Slide = u1 - u0, SashMid = (o0 + o1) * 0.5f,
                HingeAngle = side ? CASEMENT_ANGLE : HOPPER_ANGLE,
            };
            // The hinge on the room face, turning the free edge into the room.
            Vector3 hinge = side ? origin + R * bays[0] + O * o0 : origin + U * u0 + O * o0;
            Vector3 axis = side ? U : R;
            Vector3 free = side ? R : U;
            if (Vector3.Dot(Quaternion.AngleAxis(10f, axis) * free, O) > 0f) axis = -axis;
            sash.HingeLocal = pr.InverseTransformPoint(hinge);
            sash.HingeAxisLocal = pr.InverseTransformDirection(axis);

            sash.Go = new GameObject(name);
            sash.Go.layer = frameMf.gameObject.layer;
            sash.Go.transform.SetParent(pr, false);
            sash.SashMesh = BuildSegmentedSashMesh(pr, origin, R, U, O, bays, u0, u1, o0, o1);
            // Textured from the left mullion's face, its middle half: the transom's strip is narrow and edged with
            // painted lines, which streaked the bars (owner, 2026-10-19).
            LayBarFacesOnFrame(sash.SashMesh, pr, frameMf, origin, R, U, O, mullionLeft + 0.02f, mullionBottom + 0.06f);
            sash.Go.AddComponent<MeshFilter>().sharedMesh = sash.SashMesh;
            var frameRenderer = frameMf.GetComponent<MeshRenderer>();
            var sashRenderer = sash.Go.AddComponent<MeshRenderer>();
            sashRenderer.sharedMaterial = frameRenderer.sharedMaterial;
            sashRenderer.lightProbeUsage = frameRenderer.lightProbeUsage;

            // Its panes' glass rides on it; the shared glass gives those triangles up.
            sash.GlassMf = glassMf;
            sash.GlassRenderer = glassMf.GetComponent<MeshRenderer>();
            var own = GlassTrianglesIn(glassMf, opening, panes);
            var baseMesh = OriginalGlassMesh(glassMf.sharedMesh);
            var baseTris = SharedGlassBaseTriangles(baseMesh);
            var ownTris = new List<int>();
            foreach (int t in own) { ownTris.Add(baseTris[t]); ownTris.Add(baseTris[t + 1]); ownTris.Add(baseTris[t + 2]); }
            sash.GlassLower = Object.Instantiate(baseMesh);
            sash.GlassLower.triangles = ownTris.ToArray();
            HideSharedGlass(glassMf.sharedMesh, sash, own);
            var lowGlassGo = new GameObject("SI_SashGlass");
            lowGlassGo.layer = glassMf.gameObject.layer;
            lowGlassGo.transform.SetParent(sash.Go.transform, false);
            lowGlassGo.transform.position = glassMf.transform.position + O * sash.SashMid;
            lowGlassGo.transform.rotation = glassMf.transform.rotation;
            lowGlassGo.transform.localScale = glassMf.transform.localScale;
            lowGlassGo.AddComponent<MeshFilter>().sharedMesh = sash.GlassLower;
            sash.LowGlass = lowGlassGo.AddComponent<MeshRenderer>();
            sash.HingedPanes = panes;
            FollowGlass(sash);
            return sash;
        }

        // Each face of each bar laid on the frame's face from (a0, b0), along it and across it by the face's own
        // size: wrapping the texture across a narrow strip tore it inside faces (owner, 2026-10-19).
        private static void LayBarFacesOnFrame(Mesh sashMesh, Transform pr, MeshFilter frameMf, Vector3 origin, Vector3 A, Vector3 B, Vector3 O, float a0, float b0)
        {
            var frame = new FrameFaces(frameMf, origin, A, B, O);
            int tri;
            Vector2 uv0 = frame.UvAt(new Vector2(a0, b0), Vector2.zero, out tri);
            Vector2 perB = (frame.UvAt(new Vector2(a0, b0 + 0.3f), uv0, out tri) - uv0) / 0.3f;
            Vector2 perA = (frame.UvAt(new Vector2(a0 + 0.03f, b0), uv0, out tri) - uv0) / 0.03f;
            var local = sashMesh.vertices;
            var world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) world[i] = pr.TransformPoint(local[i]) - origin;
            var uv = new Vector2[local.Length];
            // 24 points a box, 4 a face; a box runs along whichever of A and B it is longer in.
            for (int bar = 0; bar * 24 < local.Length; bar++)
            {
                float a0Min = float.MaxValue, a0Max = float.MinValue, b0Min = float.MaxValue, b0Max = float.MinValue;
                for (int i = bar * 24; i < bar * 24 + 24; i++)
                {
                    float av = Vector3.Dot(world[i], A), bv = Vector3.Dot(world[i], B);
                    a0Min = Mathf.Min(a0Min, av); a0Max = Mathf.Max(a0Max, av); b0Min = Mathf.Min(b0Min, bv); b0Max = Mathf.Max(b0Max, bv);
                }
                bool alongA = a0Max - a0Min > b0Max - b0Min;
                Vector3 along = alongA ? A : B, cross = alongA ? B : A;
                float lo = alongA ? a0Min : b0Min;
                for (int f = 0; f < 6; f++)
                {
                    int s = bar * 24 + f * 4;
                    float aMin = float.MaxValue, aMax = float.MinValue, cMin = float.MaxValue, cMax = float.MinValue, oMin = float.MaxValue;
                    for (int q = 0; q < 4; q++)
                    {
                        float av = Vector3.Dot(world[s + q], along), cv = Vector3.Dot(world[s + q], cross);
                        aMin = Mathf.Min(aMin, av); aMax = Mathf.Max(aMax, av); cMin = Mathf.Min(cMin, cv); cMax = Mathf.Max(cMax, cv);
                        oMin = Mathf.Min(oMin, Vector3.Dot(world[s + q], O));
                    }
                    bool alongVaries = aMax - aMin > 0.001f, crossVaries = cMax - cMin > 0.001f;
                    for (int q = 0; q < 4; q++)
                    {
                        Vector3 p = world[s + q];
                        float d, c;
                        if (alongVaries) { d = Vector3.Dot(p, along) - lo; c = crossVaries ? Vector3.Dot(p, cross) - cMin : Vector3.Dot(p, O) - oMin; }
                        else { d = Vector3.Dot(p, cross) - cMin; c = Vector3.Dot(p, O) - oMin; }
                        uv[s + q] = uv0 + perB * d + perA * c;
                    }
                }
            }
            sashMesh.uv = uv;
        }

        // BuildSashMesh's bars, each cut into lengths of at most HINGED_BAR_PIECE: a piece's texture then stays on
        // the stretch of the frame's it is taken from.
        private const float HINGED_BAR_PIECE = 0.2f;

        private static Mesh BuildSegmentedSashMesh(Transform pr, Vector3 origin, Vector3 R, Vector3 U, Vector3 O,
                                                   float[] bays, float u0, float u1, float o0, float o1)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float w = SASH_BAR;
            System.Action<float, float, float, float, bool> bar = (r0, r1, v0, v1, alongR) =>
            {
                float from = alongR ? r0 : v0, to = alongR ? r1 : v1;
                int pieces = Mathf.Max(1, Mathf.CeilToInt((to - from) / HINGED_BAR_PIECE));
                for (int p = 0; p < pieces; p++)
                {
                    float s = Mathf.Lerp(from, to, p / (float)pieces), e = Mathf.Lerp(from, to, (p + 1) / (float)pieces);
                    if (alongR) AddSashBar(verts, uvs, tris, s, e, v0, v1, o0, o1);
                    else AddSashBar(verts, uvs, tris, r0, r1, s, e, o0, o1);
                }
            };
            for (int b = 0; b + 1 < bays.Length; b += 2)
            {
                float r0 = bays[b], r1 = bays[b + 1];
                bar(r0 + w, r1 - w, u0, u0 + w, true);
                bar(r0 + w, r1 - w, u1 - w, u1, true);
                bar(r0, r0 + w, u0, u1, false);
                bar(r1 - w, r1, u0, u1, false);
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

        // Turned about its hinge by its share of the way open.
        private static void PlaceHingedSash(WindowSash sash, float shown)
        {
            Transform pr = sash.PaneRoot;
            Vector3 hinge = pr.TransformPoint(sash.HingeLocal), axis = pr.TransformDirection(sash.HingeAxisLocal);
            Quaternion turn = Quaternion.AngleAxis(sash.HingeAngle * shown, axis);
            sash.Go.transform.position = hinge + turn * (pr.position - hinge);
            sash.Go.transform.rotation = turn * pr.rotation;
        }

        // ─── THE SHARED GLASS ───
        // One mesh holds the whole hall's glass: each sash takes its panes' triangles out of it while it stands.

        private sealed class SharedGlassCut
        {
            public int[] Base;
            public readonly Dictionary<WindowSash, HashSet<int>> Hidden = new Dictionary<WindowSash, HashSet<int>>();
        }
        private static readonly Dictionary<Mesh, SharedGlassCut> s_SharedGlassCuts = new Dictionary<Mesh, SharedGlassCut>();

        private static int[] SharedGlassBaseTriangles(Mesh mesh)
        {
            SharedGlassCut cut;
            if (s_SharedGlassCuts.TryGetValue(mesh, out cut)) return cut.Base;
            int[] all = mesh.triangles;
            return all;
        }

        private static void HideSharedGlass(Mesh mesh, WindowSash sash, HashSet<int> triangles)
        {
            SharedGlassCut cut;
            if (!s_SharedGlassCuts.TryGetValue(mesh, out cut)) { cut = new SharedGlassCut { Base = mesh.triangles }; s_SharedGlassCuts[mesh] = cut; }
            cut.Hidden[sash] = triangles;
            ApplySharedGlass(mesh, cut);
        }

        private static void ShowSharedGlass(WindowSash sash)
        {
            Mesh done = null;
            foreach (var pair in s_SharedGlassCuts)
            {
                if (!pair.Value.Hidden.Remove(sash)) continue;
                if (pair.Key != null) ApplySharedGlass(pair.Key, pair.Value);
                if (pair.Value.Hidden.Count == 0) done = pair.Key;
                break;
            }
            if (done != null) s_SharedGlassCuts.Remove(done);
        }

        private static void ApplySharedGlass(Mesh mesh, SharedGlassCut cut)
        {
            var kept = new List<int>(cut.Base.Length);
            for (int t = 0; t + 2 < cut.Base.Length; t += 3)
            {
                bool hidden = false;
                foreach (var set in cut.Hidden.Values) if (set.Contains(t)) { hidden = true; break; }
                if (!hidden) { kept.Add(cut.Base[t]); kept.Add(cut.Base[t + 1]); kept.Add(cut.Base[t + 2]); }
            }
            mesh.triangles = kept.ToArray();
        }

        // ─── THE FROST ───
        // The window's frost quad is cut round its moving panes; each sash carries its panes' part.

        private static void FollowHallFrost(WindowSash hopper)
        {
            GameObject frost = null;
            WindowCoverLayer layer = null;
            if (WindowCoverLayers && hopper.Opening < s_CoverLayers.Count) layer = s_CoverLayers[hopper.Opening];
            if (layer != null) frost = layer.FrostGo;
            WindowSash casement = null;
            foreach (var s in s_WindowSashes) if (s != hopper && s.Opening == hopper.Opening && s.HingedPanes != null) casement = s;

            if (frost != hopper.TrackedFrost)
            {
                DropSashFrost(hopper);
                if (casement != null) DropSashFrost(casement);
                hopper.TrackedFrost = frost;
                if (frost != null) SplitHallFrost(hopper, casement, layer);
            }
            foreach (var s in new[] { hopper, casement })
                if (s != null && s.LowFrost != null && frost != null && s.LowFrost.activeSelf != frost.activeSelf) s.LowFrost.SetActive(frost.activeSelf);
        }

        private static void SplitHallFrost(WindowSash hopper, WindowSash casement, WindowCoverLayer layer)
        {
            var mf = layer.FrostGo.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var opening = s_WindowPortalOpenings[hopper.Opening];
            Vector3 origin = opening.GlassCenter, A = opening.Up, B = opening.Right, O = opening.Outward;
            var v = CopyVerts(mf.sharedMesh);
            var uv0 = mf.sharedMesh.uv;
            if (v.Length != 4 || uv0.Length != 4) return;
            Transform t = mf.transform;
            var a = new float[4];
            var b = new float[4];
            float depth = 0f;
            for (int i = 0; i < 4; i++) { Vector3 p = t.TransformPoint(v[i]) - origin; a[i] = Vector3.Dot(p, A); b[i] = Vector3.Dot(p, B); depth += Vector3.Dot(p, O) * 0.25f; }
            float aMin = Mathf.Min(Mathf.Min(a[0], a[1]), Mathf.Min(a[2], a[3])), aMax = Mathf.Max(Mathf.Max(a[0], a[1]), Mathf.Max(a[2], a[3]));
            float bMin = Mathf.Min(Mathf.Min(b[0], b[1]), Mathf.Min(b[2], b[3])), bMax = Mathf.Max(Mathf.Max(b[0], b[1]), Mathf.Max(b[2], b[3]));
            if (aMax - aMin < 0.05f || bMax - bMin < 0.05f) return;

            var moving = new List<KeyValuePair<WindowSash, Vector4>>();
            foreach (var s in new[] { hopper, casement })
                if (s != null && s.HingedPanes != null) foreach (var pane in s.HingedPanes) moving.Add(new KeyValuePair<WindowSash, Vector4>(s, pane));
            // The cells between every pane edge; each is fixed or one sash's.
            var aCuts = new List<float> { aMin, aMax };
            var bCuts = new List<float> { bMin, bMax };
            foreach (var m in moving)
            {
                aCuts.Add(Mathf.Clamp(m.Value.z, aMin, aMax)); aCuts.Add(Mathf.Clamp(m.Value.w, aMin, aMax));
                bCuts.Add(Mathf.Clamp(m.Value.x, bMin, bMax)); bCuts.Add(Mathf.Clamp(m.Value.y, bMin, bMax));
            }
            aCuts.Sort(); bCuts.Sort();
            var fixedPart = new FrostCells();
            var parts = new Dictionary<WindowSash, FrostCells>();
            var ownTris = mf.sharedMesh.triangles;
            for (int i = 0; i + 1 < aCuts.Count; i++)
                for (int j = 0; j + 1 < bCuts.Count; j++)
                {
                    float a0 = aCuts[i], a1 = aCuts[i + 1], b0 = bCuts[j], b1 = bCuts[j + 1];
                    if (a1 - a0 < 0.001f || b1 - b0 < 0.001f) continue;
                    float am = (a0 + a1) * 0.5f, bm = (b0 + b1) * 0.5f;
                    WindowSash owner = null;
                    foreach (var m in moving) if (bm > m.Value.x && bm < m.Value.y && am > m.Value.z && am < m.Value.w) { owner = m.Key; break; }
                    FrostCells cells;
                    if (owner == null) cells = fixedPart;
                    else if (!parts.TryGetValue(owner, out cells)) { cells = new FrostCells(); parts[owner] = cells; }
                    int first = cells.Points.Count;
                    for (int k = 0; k < 4; k++)
                    {
                        float ak = a[k] - aMin < aMax - a[k] ? a0 : a1, bk = b[k] - bMin < bMax - b[k] ? b0 : b1;
                        cells.Points.Add(new Vector3(ak, bk, 0f));
                        cells.Uvs.Add(QuadUv(a, b, uv0, aMin, aMax, bMin, bMax, ak, bk));
                    }
                    foreach (int k in ownTris) cells.Tris.Add(first + k);
                }

            // The fixed part stays on the cover's own quad, in its plane.
            hopper.FrostOwn = mf.sharedMesh;
            hopper.FrostUpper = fixedPart.ToMesh(p => t.InverseTransformPoint(origin + A * p.x + B * p.y + O * depth));
            mf.sharedMesh = hopper.FrostUpper;
            // Each sash's part on the sash, in its plane, in the window object's space as the sash is when shut.
            foreach (var pair in parts)
            {
                var s = pair.Key;
                Transform pr = s.PaneRoot;
                s.FrostLower = pair.Value.ToMesh(p => pr.InverseTransformPoint(origin + A * p.x + B * p.y + O * s.SashMid));
                s.LowFrost = new GameObject("SI_SashFrost");
                s.LowFrost.transform.SetParent(s.Go.transform, false);
                s.LowFrost.AddComponent<MeshFilter>().sharedMesh = s.FrostLower;
                var renderer = s.LowFrost.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = layer.FrostMat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private sealed class FrostCells
        {
            public readonly List<Vector3> Points = new List<Vector3>();   // (along A, along B, -)
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Tris = new List<int>();

            public Mesh ToMesh(System.Func<Vector3, Vector3> place)
            {
                var mesh = new Mesh { name = "SI_HallFrost" };
                var verts = new Vector3[Points.Count];
                for (int i = 0; i < verts.Length; i++) verts[i] = place(Points[i]);
                mesh.vertices = verts;
                mesh.uv = Uvs.ToArray();
                mesh.triangles = Tris.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // The casement's hold target, in front of the window's own over its pane.
        private static void AddHallCasementTarget(int index)
        {
            var opening = s_WindowPortalOpenings[index];
            MeshFilter glassMf;
            List<Vector4> lower, upper;
            if (!HallPanes(opening, out glassMf, out lower, out upper)) return;
            var pane = upper[1];
            Vector3 centre = opening.GlassCenter + opening.Right * ((pane.x + pane.y) * 0.5f) + opening.Up * ((pane.z + pane.w) * 0.5f)
                           - opening.Outward * (CLEAN_TARGET_PROUD + CLEAN_TARGET_DEPTH + 0.01f);
            var target = AddCleanTarget(true, index, centre, opening.Outward, opening.Up, pane.y - pane.x, pane.w - pane.z);
            if (target != null) target.StateId = index + CASEMENT_STATE;
        }
    }
}
