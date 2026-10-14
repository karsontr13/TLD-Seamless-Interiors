using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // The same frost and snow seen from outside the building (owner, 2026-10-13): the room's frost
        // pattern on the building's glass, read from the other side, and the same bank outside, both from
        // the window's own record. They stand in for the building's painted frost overlay and the copied
        // room slabs (OutsideInPortals.cs), which only knew cleaned or not.
        private const int COVER_OUTSIDE_FROST_SIZE = 256;

        // The bank stays this far behind the frame's outer face, so the frame and bars cover its ends.
        private const float OUTSIDE_BANK_BEHIND_FRAME = 0.004f;
        // The bars in front of the glass hide about this much of a pane's top.
        private const float OUTSIDE_BANK_UNDER_BAR = 0.04f;
        // A bar standing less than this in front of the frost is drawn over by it, so the frost is cut there.
        private const float OUTSIDE_SHALLOW_BAR = 0.01f;
        private const int OUTSIDE_BANKS_PER_FRAME = 1;

        // Built with the building's dressing, after its glass has been measured.
        private static void BuildOutsideCovers(SeamlessInteriorInstance instance)
        {
            long perf = PerfProbe.Begin();
            try { BuildOutsideCoversTimed(instance); } finally { PerfProbe.End(PerfProbe.Section.CoverBuild, perf); }
        }

        private static void BuildOutsideCoversTimed(SeamlessInteriorInstance instance)
        {
            DestroyOutsideCovers();
            if (!WindowCoverLayers || instance == null) return;
            var trace = PerfProbe.Trace.Start();
            // The shell is read only when a window has not been measured yet this session.
            Vector3[] shellWorld = null; int[] shellTriangles = null; bool shellRead = false;
            double tMeasure = 0, tFrost = 0, tBank = 0; long t0;
            for (int a = 0; a < s_OutsideInApertures.Count; a++)
            {
                s_OI.Covers.Add(null);
                var pane = s_OutsideInApertures[a];
                // Pretend panes keep theirs (FarmHouse's half windows were bare, 2026-10-14); the lighthouse
                // lantern's panes are unpaired since their eye must land in the room.
                if (!pane.Paired || IsLeftAloneAt(instance.Config.ExteriorShellPrefabName, a)) continue;
                Vector4 glass = a < s_OutsideInGlassRects.Count ? s_OutsideInGlassRects[a]
                              : new Vector4(-pane.Width * 0.5f, pane.Width * 0.5f, -pane.Height * 0.5f, pane.Height * 0.5f);
                float width = glass.y - glass.x, height = glass.w - glass.z;
                if (width < 0.05f || height < 0.05f) continue;

                // Across runs the room's way (the building's right is the room's left), so the room's
                // frost pattern lands where it is seen from inside.
                var layer = new WindowCoverLayer { Opening = a, Seed = a, FrostSize = COVER_OUTSIDE_FROST_SIZE };
                Vector3 across = -pane.Right;
                Vector3 frostCenter = pane.Center + pane.Right * ((glass.x + glass.y) * 0.5f) + pane.Up * ((glass.z + glass.w) * 0.5f)
                                    + pane.Outward * (pane.Toward + OUTSIDE_IN_FROST_PROUD_OF_QUAD);
                t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!MakeCoverFrost(layer, "SeamlessInteriors_OutsideFrost_" + a, frostCenter, across, pane.Up, pane.Outward,
                                    width, height, WINDOW_VIEW_FADE_RENDER_QUEUE + 10)) continue;
                tFrost += CoverMs(t0); t0 = System.Diagnostics.Stopwatch.GetTimestamp();

                string shapeKey = s_OutsideInInstanceId + ":" + a + ":" + glass.x.ToString("F3") + "," + glass.y.ToString("F3") + "," + glass.z.ToString("F3") + "," + glass.w.ToString("F3");
                OutsideCoverShape shape;
                if (!s_OutsideCoverShapes.TryGetValue(shapeKey, out shape))
                {
                    if (!shellRead)
                    {
                        shellRead = true;
                        long tm = System.Diagnostics.Stopwatch.GetTimestamp();
                        TryGetShellWorldMesh(instance, out shellWorld, out shellTriangles);
                        trace?.Add("model", CoverMs(tm));
                    }
                    shape = MeasureOutsideCoverShape(a, pane, glass, shellWorld, shellTriangles);
                    s_OutsideCoverShapes[shapeKey] = shape;
                }
                layer.BankDepth = shape.BankDepth; layer.BankReach = shape.BankReach;
                layer.BankTuck = 0f; layer.BankUnderBar = shape.BankUnderBar;
                layer.FrostMask = shape.FrostMask;
                layer.Panes = new List<float[]>();
                foreach (var p in shape.Panes) layer.Panes.Add((float[])p.Clone());
                layer.Center = pane.Center + pane.Outward * pane.Toward; layer.Right = across; layer.Up = pane.Up; layer.Outward = pane.Outward;
                tMeasure += CoverMs(t0); t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var opening = a < s_OI.ApertureOpenings.Count ? s_OI.ApertureOpenings[a] : default(InteriorWindowOpening);
                MakeCoverBank(layer, CoverBankMaterial(opening), "SeamlessInteriors_OutsideSnow_" + a);
                // Built one a frame from TickOutsideCovers: all at once they stalled the dressing.
                tBank += CoverMs(t0);
                StartCoverTables(layer, width / height);
                s_OI.Covers[a] = layer;
            }
            if (trace != null)
            {
                trace.Add("olcum", tMeasure); trace.Add("buz", tFrost); trace.Add("kar", tBank);
                trace.Log("PERF-ORTU-DIS", s_OutsideInInstanceId, (shellRead ? (shellTriangles != null ? shellTriangles.Length / 3 : 0) + " ucgen olculdu, " : "olculer hazirdi, ") + s_OutsideInApertures.Count + " pencere");
            }
        }

        // What a building window's frame makes of the bank, measured once a session: the shell does not change,
        // and its dressing is made again each time the player comes near (14 to 20 ms a building, 2026-10-14).
        private sealed class OutsideCoverShape
        {
            public float BankDepth, BankReach, BankUnderBar;
            public float[] FrostMask;
            public List<float[]> Panes;
        }

        private static readonly Dictionary<string, OutsideCoverShape> s_OutsideCoverShapes = new Dictionary<string, OutsideCoverShape>();

        private static OutsideCoverShape MeasureOutsideCoverShape(int a, ShellAperture pane, Vector4 glass, Vector3[] shellWorld, int[] shellTriangles)
        {
            var shape = new OutsideCoverShape { BankUnderBar = OUTSIDE_BANK_UNDER_BAR };

            // In front of the picture of the room, and behind the frame's outer face: the frame then
            // covers the bank's ends from any side. Further out, it stood past the frame and over the
            // middle bar (owner, 2026-10-13; Camp Office frames stand 3.1 cm out from the glass).
            int[] near = shellWorld != null ? TrianglesNearWindow(shellWorld, shellTriangles, pane, glass) : null;
            float relief;
            if (shellWorld == null || !TryMeasureWindowRelief(pane, glass, shellWorld, near, out relief)) relief = 0.03f;
            float room = Mathf.Max(relief - pane.Toward - 0.004f - OUTSIDE_BANK_BEHIND_FRAME, 0.006f);
            shape.BankDepth = room * 0.55f; shape.BankReach = room * 0.45f;

            // Trapper's bar is a single sheet of glass behind a bar 1 cm proud (2026-10-13): the frost
            // and the bank stood in front of it. They are cut at the bar instead of going behind it.
            float barLow = 0f, barHigh = 0f;
            bool bar = shellWorld != null && TryMeasureShellBar(pane, near, shellWorld, out barLow, out barHigh);
            bool shallowBar = bar && relief < pane.Toward + OUTSIDE_IN_FROST_PROUD_OF_QUAD + OUTSIDE_SHALLOW_BAR;
            if (shallowBar)
            {
                shape.BankUnderBar = 0f;
                shape.FrostMask = BarFrostMask(COVER_OUTSIDE_FROST_SIZE, glass, barLow, barHigh);
            }
            shape.Panes = ShellPanes(a, pane, glass, bar, barLow, barHigh);
            if (shellWorld != null)
            {
                var posts = MeasureShellPosts(pane, glass, shellWorld, near);
                SplitPanesAtPosts(shape.Panes, posts);
                // The bank stays behind the posts' front, as it does behind the frame's.
                float front = PostFront(shellWorld, near, pane.Center, -pane.Right, pane.Outward, posts);
                float reaches = pane.Toward + 0.004f + shape.BankDepth + shape.BankReach, allowed = front - OUTSIDE_BANK_BEHIND_FRAME;
                if (front > 0f && reaches > allowed)
                {
                    float scale = Mathf.Max(allowed - pane.Toward - 0.004f, 0.004f) / (shape.BankDepth + shape.BankReach);
                    shape.BankDepth *= scale; shape.BankReach *= scale;
                }
            }
            return shape;
        }

        // The shell's triangles around one window, as a triangle list of its own. Every measure of a window
        // (frame depth, bar, posts) walked the whole shell, four or five times a window: 0.3 to 0.4 s at
        // Community Hall's dressing (2026-10-14). Plain float math; Vector3 calls cost a call into the game each.
        private const float COVER_NEAR_MARGIN = 0.3f, COVER_NEAR_DEPTH = 0.16f;
        private static readonly List<int> s_CoverNearTriangles = new List<int>();

        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]

        private static int[] TrianglesNearWindow(Vector3[] world, int[] triangles, ShellAperture pane, Vector4 glass)
        {
            s_CoverNearTriangles.Clear();
            Vector3 c = pane.Center, r = pane.Right, u = pane.Up, o = pane.Outward;
            float x0 = glass.x - COVER_NEAR_MARGIN, x1 = glass.y + COVER_NEAR_MARGIN;
            float y0 = glass.z - COVER_NEAR_MARGIN, y1 = glass.w + COVER_NEAR_MARGIN;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = world[triangles[t]], b = world[triangles[t + 1]], d = world[triangles[t + 2]];
                float mx = (a.x + b.x + d.x) / 3f - c.x, my = (a.y + b.y + d.y) / 3f - c.y, mz = (a.z + b.z + d.z) / 3f - c.z;
                float across = mx * r.x + my * r.y + mz * r.z;
                if (across < x0 || across > x1) continue;
                float up = mx * u.x + my * u.y + mz * u.z;
                if (up < y0 || up > y1) continue;
                float outward = mx * o.x + my * o.y + mz * o.z;
                if (outward > COVER_NEAR_DEPTH || outward < -COVER_NEAR_DEPTH) continue;
                s_CoverNearTriangles.Add(triangles[t]); s_CoverNearTriangles.Add(triangles[t + 1]); s_CoverNearTriangles.Add(triangles[t + 2]);
            }
            return s_CoverNearTriangles.ToArray();
        }

        private static double CoverMs(long start) { return (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }

        // The building's own panes: the rectangles of its glass triangles (the frost overlay was cut from
        // them), in the cover's across. The room's panes laid onto the building put the middle bar on the
        // wrong side on half the Camp Office's windows.
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static List<float[]> ShellPanes(int aperture, ShellAperture pane, Vector4 glass, bool bar, float barLow, float barHigh)
        {
            var panes = new List<float[]>();
            var overlay = aperture < s_OutsideInFrostOverlays.Count ? s_OutsideInFrostOverlays[aperture] : null;
            var filter = overlay != null ? overlay.GetComponent<MeshFilter>() : null;
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null)
            {
                var vertices = mesh.vertices;
                for (int k = 0; k + 2 < vertices.Length; k += 3)
                {
                    float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue;
                    for (int j = 0; j < 3; j++)
                    {
                        var p = vertices[k + j] - pane.Center;
                        float r = -Vector3.Dot(p, pane.Right), u = Vector3.Dot(p, pane.Up);
                        r0 = Mathf.Min(r0, r); r1 = Mathf.Max(r1, r); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                    }
                    if (u1 - u0 < 0.05f || r1 - r0 < 0.05f) continue;
                    bool seen = false;
                    foreach (var q in panes)
                        if (Mathf.Abs(q[0] - r0) < 0.01f && Mathf.Abs(q[1] - r1) < 0.01f && Mathf.Abs(q[2] - u0) < 0.01f && Mathf.Abs(q[3] - u1) < 0.01f) { seen = true; break; }
                    if (!seen) panes.Add(new[] { r0, r1, u0, u1, 0f });
                }
            }
            if (panes.Count == 0) panes.Add(new[] { -glass.y, -glass.x, glass.z, glass.w, 0f });
            MergeTouchingPanes(panes);
            if (bar) SplitPanesAtBar(panes, barLow, barHigh);
            MarkBarsAbove(panes);
            return panes;
        }

        // The shell's upright posts across one window, as spans in the cover's across (-Right): the side faces
        // that run most of the glass's height inside its width. Thompson's Store's long window is one sheet
        // of glass behind a post, and the bank stood in front of the post's lower half (2026-10-13).
        private static List<Vector2> MeasureShellPosts(ShellAperture pane, Vector4 glass, Vector3[] world, int[] triangles)
        {
            var sides = new List<float>();
            CollectPostSides(world, triangles, pane.Center, -pane.Right, pane.Up, pane.Outward, -glass.y, -glass.x, glass.z, glass.w, sides);
            return ClusterPosts(sides);
        }

        // The frost texture's rows behind the bar, left out (row 0 is the glass's bottom).
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static float[] BarFrostMask(int size, Vector4 glass, float barLow, float barHigh)
        {
            var mask = new float[size * size];
            float height = glass.w - glass.z;
            for (int y = 0; y < size; y++)
            {
                float up = glass.z + (y + 0.5f) / size * height;
                float m = up > barLow && up < barHigh ? 0f : 1f;
                for (int x = 0; x < size; x++) mask[y * size + x] = m;
            }
            return mask;
        }

        // Whether this window's frost is drawn by the cover rather than the old overlay.
        private static bool OutsideCoverShown(int aperture)
        {
            return aperture < s_OI.Covers.Count && s_OI.Covers[aperture] != null && s_OI.Covers[aperture].Shown;
        }

        // Whether the building's old frost overlay (frost true) or copied drift shows. The bank is made
        // with the dressing and needs no tables, so its window never shows the old drift. The frost waits
        // for its tables; until then the old overlay shows by the record, not by "cleaned": a window
        // cleaned from inside keeps its snow and may be frosting again (owner, 2026-10-13).
        private static bool OutsideOldDressingShows(int aperture, bool frost)
        {
            // Asked every time: asking is what lets a cleaned window frost over again (WindowFrostState.cs).
            bool cleaned = IsWindowCleaned(s_OutsideInInstanceId, aperture);
            var layer = WindowCoverLayers && aperture < s_OI.Covers.Count ? s_OI.Covers[aperture] : null;
            if (layer == null) return !cleaned;
            if (!frost) return layer.BankGo == null && !cleaned;
            if (layer.Shown && layer.FrostShown >= 0f) return false;
            float frostCover, snowCover;
            GetWindowCover(s_OutsideInInstanceId, aperture, false, out frostCover, out snowCover);
            return frostCover >= 0.5f;
        }

        // The bank follows the record right away. A bank is a few milliseconds; held to two updates a
        // second with the frost, the last window's snow came eight seconds after the building was dressed.
        private static void FollowOutsideBank(WindowCoverLayer layer, int aperture)
        {
            if (layer.BankGo == null) return;
            float frost, snow;
            GetWindowCover(s_OutsideInInstanceId, aperture, false, out frost, out snow);
            float bankSnow = InsideOutShowSnowSlab ? snow : 0f;
            if (layer.SnowShown < 0f || Mathf.Abs(bankSnow - layer.SnowShown) >= COVER_SNOW_STEP || (bankSnow == 0f) != (layer.SnowShown == 0f))
                BuildCoverBank(layer, bankSnow);
            bool show = layer.SnowShown > 0f;
            if (layer.BankGo.activeSelf != show) layer.BankGo.SetActive(show);
        }

        // From UpdateOutsideInDressing, for the building bound now. The frost shows where the old overlay
        // did (with the view drawn behind it); the bank is outside geometry and shows whenever it has snow.
        private static void TickOutsideCovers(bool live)
        {
            long perf = PerfProbe.Begin();
            try { TickOutsideCoversTimed(live); } finally { PerfProbe.End(PerfProbe.Section.CoverOutside, perf); }
        }

        private static void TickOutsideCoversTimed(bool live)
        {
            var covers = s_OI.Covers;
            if (covers.Count == 0) return;
            PumpCoverFills(covers);

            // Banks not built yet: one a frame, every frame, so the snow is up a few frames after dressing.
            int banks = 0;
            for (int a = 0; a < covers.Count && banks < OUTSIDE_BANKS_PER_FRAME; a++)
            {
                var layer = covers[a];
                if (layer == null || layer.BankGo == null || layer.SnowShown >= 0f) continue;
                FollowOutsideBank(layer, a);
                banks++;
            }

            bool force = false;
            for (int a = 0; a < covers.Count; a++)
            {
                var layer = covers[a];
                if (layer == null || layer.Shown || !layer.Ready) continue;
                if (layer.Error != null)
                {
                    MelonLogger.Warning($"[DIS-ORTU] {s_OutsideInInstanceId} pencere {a}: {layer.Error}", $"[OUTSIDE-COVER] {s_OutsideInInstanceId} window {a}: {layer.Error}");
                    DestroyCoverLayer(layer);
                    covers[a] = null;
                    continue;
                }
                layer.Shown = true;
                force = true;
            }

            float real = Time.realtimeSinceStartup;
            if (!force && !s_CoverDirty && real < s_OI.CoversNextTick && live == s_OI.CoversLive) return;
            s_OI.CoversNextTick = real + 1f;
            s_OI.CoversLive = live;

            Color light = CoverSnowLight();
            int fills = 0, firstFills = 0;
            for (int a = 0; a < covers.Count; a++)
            {
                var layer = covers[a];
                if (layer == null) continue;
                if (layer.SnowShown >= 0f) FollowOutsideBank(layer, a);
                if (!layer.Shown) continue;
                float frost, snow;
                GetWindowCover(s_OutsideInInstanceId, a, false, out frost, out snow);
                // One first fill a frame (eight at once stalled the frame); a change at most two a tick.
                bool fresh = layer.FrostShown < 0f;
                if (fresh && firstFills >= 1) { s_OI.CoversNextTick = 0f; continue; }
                if (fresh || (fills < 2 && (Mathf.Abs(frost - layer.FrostShown) >= COVER_FROST_STEP || (frost == 0f) != (layer.FrostShown == 0f))))
                {
                    FillCoverFrost(layer, frost);
                    if (fresh) firstFills++; else fills++;
                }
                if (layer.FrostMat.color != light) layer.FrostMat.color = light;
                bool showFrost = live && OutsideInFrostedPane && layer.FrostShown >= 0f;
                if (layer.FrostGo.activeSelf != showFrost) layer.FrostGo.SetActive(showFrost);
            }
        }

        private static void DestroyOutsideCovers()
        {
            foreach (var layer in s_OI.Covers) DestroyCoverLayer(layer);
            s_OI.Covers.Clear();
        }
    }
}
