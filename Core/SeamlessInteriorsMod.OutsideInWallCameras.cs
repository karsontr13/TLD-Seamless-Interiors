using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── ONE CAMERA PER WALL FOR THE VIEWS INTO A ROOM, TOO ───
        //
        // The views out of a parked room draw a wall's windows with one camera (WallCameras.cs). The
        // views in did not, and from outside the hangar 25 panes wanted a view while the limit of 10
        // gave one to 10 and left 15 on the dark backing (owner, 2026-09-26: "Many of the outside-in
        // cameras aren't rendering"). This is the same scheme turned round:
        //
        // - A pane's camera stands where the player's eye lands when it is carried from the building
        //   into the room (RenderOutsideInPane): the room window's offset, the room's own axes, and
        //   the fit across and up. That carry is one affine map per pane. Panes whose maps agree, to
        //   tighter allowances than the views out (2 cm sideways and up, 5 cm along the view, and
        //   the fits within 1 percent), share one camera.
        // - That camera is the player's lens with the map folded into its view matrix, narrowed to
        //   the screen rectangle round the group's visible panes. Each pane is a grid whose texture
        //   coordinates are its points' screen positions, so the picture lines up with every hole.
        // - Drawn in the postfix on vp_FPSCamera.LateUpdate, after the zoom, like the views out; the
        //   mod's tick only hands the group a slot. Rendered by hand, like every view in, inside the
        //   room's own ambient.
        // - The per-pane camera cut its picture at the pane, so nothing between the eye and the room
        //   window was ever drawn. The group camera draws from the eye, so the room's pieces standing
        //   wholly outside its windows (on the viewer's side of the glass) are switched off while it
        //   renders. The room's walls are seen from behind from there and draw nothing.
        // - A pane alone in its group keeps the per-pane camera, as it does looking out. A failed
        //   group camera leaves its panes on the backing, never on the per-pane cameras, and the
        //   failure is logged once. OutsideInWallCameras off brings the per-pane cameras back at once.

        private const float OUTSIDE_IN_WALL_SAME_FIT = 0.01f;
        // TIGHTER THAN THE VIEWS OUT, because a view in shows the room's own window frame right behind
        // the building's, so an eye in the wrong place shows as a frame beside a frame. With the views
        // out's 10 cm and 25 cm, the hangar's eight front doors, on four staggered planes, shared one
        // camera whose eye stood up to 9.8 cm from where a door's own would; up close and zoomed the
        // room's frames stood visibly off the doors', and gone with each door on its own camera
        // (owner, 2026-09-26: "The interior frames are obvious", then, per door, "Yeah that looks
        // great"). At 2 cm and 5 cm the hangar keeps 8 wall cameras, the long sides mostly whole.
        // LIVE LEVERS, in meters, so the old and new allowances can be compared at one standing spot
        // (0.10 and 0.25 are the views out's). A change regroups every building on its next frame.
        internal static float OutsideInWallSameAcross = 0.02f;
        internal static float OutsideInWallSameAlong = 0.05f;

        // THE PICTURE STARTS AT THE NEAREST GLASS, a cut square to the gaze at the closest point of the
        // group's visible panes, so the room's pieces reaching out past its windows are not drawn,
        // as the per-pane camera, whose picture starts at its own pane, never drew them. The hangar's
        // front doors are thick: paired on one camera, 0+35 and 36+40 "look bad from off-angle" while
        // 37, 38, 39 and 41 on their own "look great from any angle" (owner, 2026-09-26), with the
        // pairs' eyes within 0.9 cm of their own. Only the near distance moves, so the far edge and
        // the depth of what is drawn are the player's (the turned cut, rejected for the views out,
        // moved the far edge). A lever for the owner's A/B: false = from the eye, as before.
        internal static bool OutsideInWallCutAtGlass = true;

        // ─── THE PLANE CAMERA: ONE CAMERA LOOKING SQUARE AT A PLANE, THE DEFAULT SINCE 2026-09-26 ───
        //
        // The owner's plan from the start, one camera per plane, taken literally. It is the per-pane
        // camera he tuned (RenderOutsideInPane) stretched over the rectangle round every visible pane
        // of the group: the eye carried into the room by the group's map, the camera looking along the
        // room's facing, and an off-axis frustum whose near plane IS the plane, so the picture starts
        // on the glass at any angle and the room's frames in front of it are never drawn. Each pane is
        // a four-corner quad on its own glass taking its own part of the rectangle; with the near plane
        // on the plane, the picture is linear in the plane's coordinates, so no grid is needed.
        //
        // Why it replaced the screen-aligned camera for the views in: that camera draws from the eye,
        // and a cut square to the gaze cannot follow a plane seen at an angle; at X 137.11, Z -637.88
        // its cut stood at 6.72 m while pane 35's glass was 9.44 m out along the gaze, and the room's
        // thick door frames showed on 35 and 0 (owner: "35/0 show visible interior frames", 0926-25).
        // Drawn in the mod's tick, like the per-pane camera. The known cost: seen at a steep angle,
        // the far end of a long plane gets more texels than it needs; sized, as the per-pane camera
        // is, from the rectangle's on-screen footprint. Rollback: OutsideInWallMode = 1.
        internal static int OutsideInWallMode = 0;     // 0 plane camera, 1 screen-aligned (a1ffc65 to 6712d91)

        // ─── GROUPS MADE BY HAND, PER BUILDING (BuildingWindowOverride.ViewInGroups) ───
        //
        // A building's row may force panes onto one camera that the rules would split, with room
        // objects to switch off while it renders. Such a group is drawn by the screen-aligned camera
        // whatever OutsideInWallMode says, in the late postfix after the zoom, from the mean of its
        // panes' eyes, with no per-pane correction (see the row's comment for why). The hangar's
        // front: one camera for eight doors in place of seven (2026-09-26). Live lever; a change
        // regroups every building on its next frame. False = the rules alone, as before.
        internal static bool OutsideInHandGroups = true;
        private static readonly List<MeshRenderer> s_OutsideInPlaneShown = new List<MeshRenderer>();
        private static readonly List<int> s_OutsideInPlaneOnScreen = new List<int>();

        // Called at the top of the tick's drawing: last frame's plane-camera quads off.
        private static void BeginOutsideInPlaneFrame()
        {
            for (int i = 0; i < s_OutsideInPlaneShown.Count; i++) if (s_OutsideInPlaneShown[i] != null) s_OutsideInPlaneShown[i].enabled = false;
            s_OutsideInPlaneShown.Clear();
        }

        private static OutsideInWallGrid EnsureOutsideInPlaneQuad(OutsideInBuilding b, int index)
        {
            var grid = b.WallGrids[index];
            if (grid != null && grid.Quad != null && grid.Plain) return grid;
            if (grid != null)
            {
                if (grid.Mesh != null) Object.Destroy(grid.Mesh);
                if (grid.Quad != null) Object.Destroy(grid.Quad);
            }
            grid = new OutsideInWallGrid { Plain = true };
            grid.Mesh = new Mesh();
            grid.Mesh.vertices = new Vector3[4];
            grid.Mesh.uv = new Vector2[4];
            grid.Mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            grid.Mesh.MarkDynamic();
            grid.Quad = new GameObject("SI_OutsideInPlaneQuad");
            grid.Quad.layer = WINDOW_PORTAL_QUAD_LAYER;
            grid.Quad.AddComponent<MeshFilter>().sharedMesh = grid.Mesh;
            grid.Renderer = grid.Quad.AddComponent<MeshRenderer>();
            grid.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            grid.Renderer.receiveShadows = false;
            grid.Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            grid.Renderer.enabled = false;
            b.WallGrids[index] = grid;
            return grid;
        }

        // Draws the group into the slot now. False when nothing of it is on screen.
        private static bool RenderOutsideInPlaneGroup(OutsideInBuilding b, OutsideInWallGroup group, int slot, Camera main, Vector3 eye)
        {
            // The plane's frame: the first member's. Members stand within WallSamePlane of it.
            var reference = OutsideInViewOf(b.Apertures[group.Members[0]]);
            Vector3 right = reference.Right, up = reference.Up, outward = reference.Outward;
            float recess = Mathf.Max(OutsideInWindowRecess, 0f);
            Vector3 origin = reference.Center - outward * recess;

            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            s_OutsideInPlaneOnScreen.Clear();
            foreach (int m in group.Members)
            {
                var v = OutsideInViewOf(b.Apertures[m]);
                if (Vector3.Dot(eye - v.Center, v.Outward) <= OUTSIDE_IN_MIN_EYE_DISTANCE) continue;
                if (!OutsideInPaneOnScreen(v)) continue;
                s_OutsideInPlaneOnScreen.Add(m);
                for (int p = 0; p < s_WindowPortalPoly.Count; p++)
                {
                    Vector3 d = s_WindowPortalPoly[p] - origin;
                    float u = Vector3.Dot(d, right), w = Vector3.Dot(d, up);
                    if (u < u0) u0 = u; if (u > u1) u1 = u;
                    if (w < v0) v0 = w; if (w > v1) v1 = w;
                }
            }
            if (s_OutsideInPlaneOnScreen.Count == 0 || u1 - u0 < 0.005f || v1 - v0 < 0.005f) return false;
            float marginU = Mathf.Max((u1 - u0) * OUTSIDE_IN_SUBRECT_MARGIN, OUTSIDE_IN_SUBRECT_MARGIN_FLOOR);
            float marginV = Mathf.Max((v1 - v0) * OUTSIDE_IN_SUBRECT_MARGIN, OUTSIDE_IN_SUBRECT_MARGIN_FLOOR);
            u0 -= marginU; u1 += marginU; v0 -= marginV; v1 += marginV;

            Vector3 qa = origin + right * u0 + up * v0, qb = origin + right * u1 + up * v0, qc = origin + right * u0 + up * v1;
            Vector3 va = qa - eye, vb = qb - eye, vc = qc - eye;
            Vector3 forward = -outward;
            float near = Vector3.Dot(va, forward);
            if (near <= OUTSIDE_IN_MIN_EYE_DISTANCE) return false;

            int width, height;
            ComputeWindowPortalPixelSize(qa, qb, qc, main, out width, out height);
            if (!EnsureOutsideInSlot(slot, main, width, height)) return false;
            var slotQuad = s_OutsideInQuads[slot];
            if (slotQuad != null) { var sq = slotQuad.GetComponent<MeshRenderer>(); if (sq != null) sq.enabled = false; }

            // Exactly RenderOutsideInPane's frustum and carry, over the group's rectangle.
            float fa = group.FitAcross, fu = group.FitUp;
            float left = Vector3.Dot(right, va) * fa, rightExtent = Vector3.Dot(right, vb) * fa;
            float bottom = Vector3.Dot(up, va) * fu, top = Vector3.Dot(up, vc) * fu;
            Camera cam = s_OutsideInCams[slot];
            cam.ResetWorldToCameraMatrix();
            cam.transform.position = group.BuildingToRoom.MultiplyPoint3x4(eye);
            cam.transform.rotation = Quaternion.LookRotation(-reference.RoomOutward, Vector3.up);
            cam.nearClipPlane = near;
            cam.farClipPlane = OUTSIDE_IN_FAR_CLIP;
            cam.fieldOfView = s_FrustumCapturedLate ? s_LateFieldOfView : main.fieldOfView;
            cam.aspect = (rightExtent - left) / (top - bottom);
            cam.projectionMatrix = Matrix4x4.Frustum(left, rightExtent, bottom, top, near, OUTSIDE_IN_FAR_CLIP);

            // Each visible pane: a quad on its own glass taking its part of the rectangle.
            float du = u1 - u0, dv = v1 - v0;
            foreach (int m in s_OutsideInPlaneOnScreen)
            {
                var v = OutsideInViewOf(b.Apertures[m]);
                var quad = EnsureOutsideInPlaneQuad(b, m);
                Vector3 pc = v.Center - v.Outward * recess;
                Vector3 pa = pc - v.Right * (v.Width * 0.5f) - v.Up * (v.Height * 0.5f);
                Vector3 pb = pa + v.Right * v.Width, pd = pa + v.Up * v.Height, pe = pb + v.Up * v.Height;
                System.Func<Vector3, Vector2> uv = p => new Vector2((Vector3.Dot(p - origin, right) - u0) / du, (Vector3.Dot(p - origin, up) - v0) / dv);
                quad.Mesh.vertices = new Vector3[] { pa, pd, pe, pb };
                quad.Mesh.uv = new Vector2[] { uv(pa), uv(pd), uv(pe), uv(pb) };
                quad.Mesh.RecalculateBounds();
                ApplyViewFade(quad.Renderer, slot, OutsideInPaneLevel(b, m));
                quad.Renderer.enabled = true;
                s_OutsideInPlaneShown.Add(quad.Renderer);
            }

            var savedMode = RenderSettings.ambientMode;
            var savedLight = RenderSettings.ambientLight;
            try
            {
                SwitchOffForViewIn(OutsideInBackdrops(b), s_OutsideInWallSwitchedOff);
                ApplyOutsideInAmbient();
                cam.Render();
            }
            finally
            {
                RenderSettings.ambientMode = savedMode; RenderSettings.ambientLight = savedLight;
                SwitchBackOn(s_OutsideInWallSwitchedOff);
            }
            return true;
        }
        private const float OUTSIDE_IN_WALL_HIDE_REACH = 40f;

        private static MelonPreferences_Entry<bool> s_PrefOutsideInWallCameras;

        private sealed class OutsideInWallGroup
        {
            public readonly List<int> Members = new List<int>();
            public Matrix4x4 BuildingToRoom;          // the carry, averaged over the members
            public Vector3 RoomOutward;
            public int ClaimedFrame = -1;
            public List<Renderer> Hidden;             // built on first draw, from the room's relayered renderers
            public float FitAcross = 1f, FitUp = 1f;  // the members' fits, averaged (they agree within 1%)
            public bool ScreenAligned;                // drawn by the screen-aligned camera in the postfix, whatever the mode
            public string[] HideByName;               // a hand-made group's room objects to switch off while it renders
            public OutsideInHandGroup HandRow;        // the row entry a hand-made group came from, or null
            public Renderer ClearOn;                  // the nearest copy whose materials are cleared while it renders, or null
            public int[] ClearSlots;                  // ClearOn's material slots drawn transparent while it renders
        }

        private sealed class OutsideInWallGrid
        {
            public GameObject Quad;
            public MeshRenderer Renderer;
            public bool Plain;                        // a four-corner quad for the plane camera, not a grid
            public Mesh Mesh;
            public float[] VX, VY, VZ, UVF;
            public Il2CppStructArray<Vector2> UV;
        }

        private static bool s_OutsideInWallFaulted;
        private static int s_OutsideInWallFrame = -1;
        private static OutsideInBuilding[] s_OutsideInSlotWallBuilding;
        private static int[] s_OutsideInSlotWallGroup;
        private static readonly List<MeshRenderer> s_OutsideInWallShown = new List<MeshRenderer>();
        private static readonly List<Renderer> s_OutsideInWallSwitchedOff = new List<Renderer>();
        private static readonly List<int> s_OutsideInWallOnScreen = new List<int>();

        private static bool OutsideInWallCamerasWanted()
        {
            return s_PrefOutsideInWallCameras != null && s_PrefOutsideInWallCameras.Value;
        }

        // A pane as its camera sees it: its view rectangle, everything toward the viewer. The same
        // three lines RenderOutsideInPane starts with.
        private static ShellAperture OutsideInViewOf(ShellAperture pane)
        {
            pane.Center = pane.ViewCenter; pane.Delta = pane.ViewDelta;
            pane.Width = pane.ViewWidth; pane.Height = pane.ViewHeight;
            pane.Center += pane.Outward * OutsideInViewTowardViewer;
            pane.Delta -= pane.Outward * OutsideInViewTowardViewer + pane.RoomOutward * OutsideInViewTowardViewer;
            return pane;
        }

        // The carry of RenderOutsideInPane as one matrix: a building point p lands in the room at
        // Center + Delta + RoomRight (p-Center).Right fa + up (p-Center).Up fu + RoomOutward (p-Center).Outward.
        private static Matrix4x4 OutsideInBuildingToRoom(ShellAperture v, out float fa, out float fu)
        {
            fa = Mathf.Lerp(1f, v.FitAcross > 0f ? v.FitAcross : 1f, OutsideInViewFit);
            fu = Mathf.Lerp(1f, v.FitUp > 0f ? v.FitUp : 1f, OutsideInViewFit);
            Vector3 a = v.RoomRight * fa, b = Vector3.up * fu, c = v.RoomOutward;
            Vector3 R = v.Right, U = v.Up, O = v.Outward;
            var m = Matrix4x4.identity;
            m.m00 = a.x * R.x + b.x * U.x + c.x * O.x; m.m01 = a.x * R.y + b.x * U.y + c.x * O.y; m.m02 = a.x * R.z + b.x * U.z + c.x * O.z;
            m.m10 = a.y * R.x + b.y * U.x + c.y * O.x; m.m11 = a.y * R.y + b.y * U.y + c.y * O.y; m.m12 = a.y * R.z + b.y * U.z + c.y * O.z;
            m.m20 = a.z * R.x + b.z * U.x + c.z * O.x; m.m21 = a.z * R.y + b.z * U.y + c.z * O.y; m.m22 = a.z * R.z + b.z * U.z + c.z * O.z;
            Vector3 C = v.Center;
            Vector3 mc = new Vector3(m.m00 * C.x + m.m01 * C.y + m.m02 * C.z, m.m10 * C.x + m.m11 * C.y + m.m12 * C.z, m.m20 * C.x + m.m21 * C.y + m.m22 * C.z);
            Vector3 t = C + v.Delta - mc;
            m.m03 = t.x; m.m13 = t.y; m.m23 = t.z;
            return m;
        }

        // ─── SETUP, PER BUILDING, ON FIRST NEED ───

        private static void EnsureOutsideInWallGroups(OutsideInBuilding b)
        {
            if (b.WallGroups != null && b.WallBuiltFit == OutsideInViewFit && b.WallBuiltToward == OutsideInViewTowardViewer
                && b.WallBuiltPaired == b.PairedCount && b.WallBuiltCount == b.Apertures.Count
                && b.WallBuiltAcross == OutsideInWallSameAcross && b.WallBuiltAlong == OutsideInWallSameAlong
                && b.WallBuiltPlane == WallSamePlane && b.WallBuiltSorted == WallGroupingSorted && b.WallBuiltHand == OutsideInHandGroups) return;
            DestroyOutsideInWall(b);

            int n = b.Apertures.Count;
            b.WallGroups = new List<OutsideInWallGroup>();
            b.ApertureWallGroup = new int[n];
            b.WallGrids = new OutsideInWallGrid[n];
            var maps = new Matrix4x4[n];
            var fits = new Vector2[n];
            var views = new ShellAperture[n];
            var valid = new bool[n];
            for (int i = 0; i < n; i++)
            {
                b.ApertureWallGroup[i] = -1;
                var pane = b.Apertures[i];
                if (!pane.Paired || pane.ViewFollows >= 0) continue;
                valid[i] = true;
                views[i] = OutsideInViewOf(pane);
                float fa0, fu0;
                maps[i] = OutsideInBuildingToRoom(views[i], out fa0, out fu0);
                fits[i] = new Vector2(fa0, fu0);
            }
            // The building's hand-made groups first; the rules then group what is left.
            int handMade = 0;
            BuildingWindowOverride row;
            string shellName = b.Instance != null && b.Instance.Config != null ? b.Instance.Config.ExteriorShellPrefabName : null;
            if (OutsideInHandGroups && TryGetBuildingWindowOverride(shellName, out row) && row.ViewInGroups != null)
            {
                foreach (var hand in row.ViewInGroups)
                {
                    if (hand == null || hand.Panes == null) continue;
                    var group = new OutsideInWallGroup { ScreenAligned = true, HideByName = hand.HideWhileRendering ?? new string[0], HandRow = hand };
                    foreach (int p in hand.Panes)
                    {
                        if (p < 0 || p >= n || !valid[p] || b.ApertureWallGroup[p] >= 0) continue;
                        if (group.Members.Count == 0) group.RoomOutward = views[p].RoomOutward;
                        group.Members.Add(p);
                        b.ApertureWallGroup[p] = b.WallGroups.Count;
                    }
                    if (group.Members.Count == 0) continue;
                    b.WallGroups.Add(group);
                    handMade++;
                }
            }

            var groupingOrder = WallGroupingOrder(n, i => valid[i] && b.ApertureWallGroup[i] < 0, i => views[i].Outward, i => Vector3.Dot(views[i].Center, views[i].Outward),
                                                  i => new Vector3(maps[i].m03, maps[i].m13, maps[i].m23));
            foreach (int i in groupingOrder)
            {
                float fa = fits[i].x, fu = fits[i].y;
                int home = -1;
                for (int g = 0; g < b.WallGroups.Count && home < 0; g++)
                {
                    var group = b.WallGroups[g];
                    if (group.HideByName != null) continue;       // hand-made: its members are fixed
                    bool agrees = true;
                    for (int k = 0; k < group.Members.Count && agrees; k++)
                    {
                        int o = group.Members[k];
                        if (Vector3.Dot(views[o].Outward, views[i].Outward) < WALL_SAME_FACING
                            || Vector3.Dot(views[o].RoomOutward, views[i].RoomOutward) < WALL_SAME_FACING
                            || Mathf.Abs(fits[o].x - fa) > OUTSIDE_IN_WALL_SAME_FIT || Mathf.Abs(fits[o].y - fu) > OUTSIDE_IN_WALL_SAME_FIT) { agrees = false; break; }
                        // One plane (WallSamePlane, WallCameras.cs), then one eye.
                        if (Mathf.Abs(Vector3.Dot(views[o].Center - views[i].Center, views[i].Outward)) > WallSamePlane) { agrees = false; break; }
                        // With the same map, the eye lands this far apart in the room.
                        Vector3 apart = new Vector3(maps[o].m03 - maps[i].m03, maps[o].m13 - maps[i].m13, maps[o].m23 - maps[i].m23);
                        float along = Vector3.Dot(apart, views[i].RoomOutward);
                        agrees = Mathf.Abs(along) <= OutsideInWallSameAlong
                              && (apart - views[i].RoomOutward * along).magnitude <= OutsideInWallSameAcross;
                    }
                    if (agrees) home = g;
                }
                if (home < 0)
                {
                    b.WallGroups.Add(new OutsideInWallGroup { RoomOutward = views[i].RoomOutward });
                    home = b.WallGroups.Count - 1;
                }
                b.WallGroups[home].Members.Add(i);
                b.ApertureWallGroup[i] = home;
            }

            int grouped = 0, alone = 0;
            var report = new System.Text.StringBuilder();
            for (int g = 0; g < b.WallGroups.Count; g++)
            {
                var group = b.WallGroups[g];
                // Averaged field by field: the members agree to within the allowances above.
                var mean = Matrix4x4.identity;
                float w = 1f / group.Members.Count;
                mean.m00 = 0f; mean.m11 = 0f; mean.m22 = 0f;
                foreach (int m in group.Members)
                {
                    var q = maps[m];
                    mean.m00 += q.m00 * w; mean.m01 += q.m01 * w; mean.m02 += q.m02 * w; mean.m03 += q.m03 * w;
                    mean.m10 += q.m10 * w; mean.m11 += q.m11 * w; mean.m12 += q.m12 * w; mean.m13 += q.m13 * w;
                    mean.m20 += q.m20 * w; mean.m21 += q.m21 * w; mean.m22 += q.m22 * w; mean.m23 += q.m23 * w;
                }
                group.BuildingToRoom = mean;
                group.FitAcross = 0f; group.FitUp = 0f;
                foreach (int m in group.Members) { group.FitAcross += fits[m].x * w; group.FitUp += fits[m].y * w; }
                if (group.Members.Count > 1) { grouped++; report.Append(grouped).Append(": ").Append(string.Join(",", group.Members)).Append(group.HideByName != null ? " (by hand, screen camera); " : "; "); }
                else alone++;
            }
            b.WallBuiltFit = OutsideInViewFit; b.WallBuiltToward = OutsideInViewTowardViewer;
            b.WallBuiltPaired = b.PairedCount; b.WallBuiltCount = n;
            b.WallBuiltAcross = OutsideInWallSameAcross; b.WallBuiltAlong = OutsideInWallSameAlong; b.WallBuiltPlane = WallSamePlane; b.WallBuiltSorted = WallGroupingSorted;
            b.WallBuiltHand = OutsideInHandGroups;
            MelonLogger.Msg($"[DUVAR-KAMERA-ICERI] {b.InstanceId}: {grouped} duvar kamerasi, {alone} tek pencere kendi kamerasiyla. Gruplar: {report}",
                            $"[WALL-CAMERA-IN] {b.InstanceId}: views into the room from outside: {grouped} wall cameras for grouped panes, {alone} panes alone on their own camera ({handMade} by hand from the building's row; allowance {OutsideInWallSameAcross * 100f:F1} cm sideways, {OutsideInWallSameAlong * 100f:F1} cm along, plane {WallSamePlane * 100f:F1} cm, {(WallGroupingSorted ? "sorted" : "by index")}; {(OutsideInWallMode == 0 ? "plane cameras" : "screen-aligned cameras, cut at the glass " + (OutsideInWallCutAtGlass ? "on" : "off"))}). Groups (pane numbers): {report}");
        }

        private static void DestroyOutsideInWall(OutsideInBuilding b)
        {
            if (b == null) return;
            if (b.WallGrids != null)
                foreach (var grid in b.WallGrids)
                {
                    if (grid == null) continue;
                    if (grid.Mesh != null) Object.Destroy(grid.Mesh);
                    if (grid.Quad != null) Object.Destroy(grid.Quad);
                }
            b.WallGrids = null;
            b.WallGroups = null;
            b.ApertureWallGroup = null;
            b.Backdrops = null;
            // A room given back and borrowed again starts its views from nothing.
            b.PaneLevel.Clear(); b.PaneDrawnFrame.Clear(); b.PaneWanted.Clear(); b.PaneDrawnLastFrame.Clear();
        }

        // The same perspective with its near distance moved, far kept. Left as it was if the matrix is
        // not a plain perspective one or the new near would not be nearer than the far.
        private static Matrix4x4 WithNearDistance(Matrix4x4 proj, float near, float minNear)
        {
            if (Mathf.Abs(proj.m32 + 1f) > 1e-4f || Mathf.Abs(proj.m33) > 1e-4f) return proj;
            float a = proj.m22, b = proj.m23;
            if (Mathf.Abs(a - 1f) < 1e-6f || Mathf.Abs(a + 1f) < 1e-6f) return proj;
            float oldNear = b / (a - 1f), far = b / (a + 1f);
            if (oldNear <= 0f || far <= oldNear) return proj;
            near = Mathf.Max(near, minNear);
            if (near >= far * 0.5f) return proj;
            proj.m22 = -(far + near) / (far - near);
            proj.m23 = -2f * far * near / (far - near);
            return proj;
        }

        // ─── A WINDOW'S FAR-AWAY MODEL MUST NOT COVER ITS VIEW ───
        //
        // The mod hides the painted glass of the building's window models, but only on the model it
        // measured, the detailed one. From about 45 m the game swaps a window for its far-away model,
        // whose glass is untouched and stands over the view: at the hangar panes 25 and 26 at 47 and
        // 50 m and five more past 66 m showed the vanilla window while their wall camera drew them
        // (2026-09-26). His rule: a wall camera's panes are not dropped by distance. So while the
        // building's cameras are on, each window model is held at its detailed version, and let go
        // the moment they switch off, when the far-away model's painted glass is what the building
        // should show again: held past the camera range, the pin left the hole with nothing in it
        // and the inside of the shell showing through (owner, the same day). Only a model whose
        // detailed version is the renderer the mod hides is held.
        private static void PinOutsideInWindowModels(OutsideInBuilding b, bool pin)
        {
            if (!pin)
            {
                if (b.PinnedWindowModels == null) return;
                foreach (var group in b.PinnedWindowModels) if (group != null) group.ForceLOD(-1);
                b.PinnedWindowModels = null;
                return;
            }
            if (b.PinnedWindowModels != null || b.PaneRenderers.Count == 0) return;
            b.PinnedWindowModels = new List<LODGroup>();
            var seen = new HashSet<int>();
            foreach (var pane in b.PaneRenderers)
            {
                if (pane == null) continue;
                var group = pane.GetComponentInParent<LODGroup>();
                if (group == null || !seen.Add(group.GetInstanceID())) continue;
                var lods = group.GetLODs();
                if (lods.Length < 2) continue;
                bool detailed = false;
                foreach (var r in lods[0].renderers) if (r != null && r.GetInstanceID() == pane.GetInstanceID()) { detailed = true; break; }
                if (!detailed) continue;
                group.ForceLOD(0);
                b.PinnedWindowModels.Add(group);
            }
        }

        // ─── THE ROOM'S PAINTED OUTSIDE IS NOT DRAWN INTO A VIEW IN ───
        //
        // Every room carries a glowing sheet of "daylight" just outside its windows, so they look
        // bright from inside; it wears a material of the GLB_GlowExt family (BACKDROP_MATERIAL_PREFIX,
        // the same rule the window finder uses). Seen from outside at a steep angle, it showed as
        // bright white strips between the room window's bars: at the hangar with the per-pane and the
        // wall cameras alike, and gone with the two STR_AF_Hangar_Interior_Side_Window_Glow_Prefab
        // switched off (owner, 2026-09-26: "White is gone"). Switched off only while a view-in camera
        // renders, so the room keeps it for everything else.
        private static List<Renderer> OutsideInBackdrops(OutsideInBuilding b)
        {
            if (b.Backdrops != null) return b.Backdrops;
            b.Backdrops = new List<Renderer>();
            for (int i = 0; i < b.Relayered.Count; i++)
            {
                var r = b.Relayered[i];
                if (r == null) continue;
                var materials = r.sharedMaterials;
                for (int k = 0; k < materials.Length; k++)
                {
                    var m = materials[k];
                    if (m == null || !m.name.StartsWith(BACKDROP_MATERIAL_PREFIX)) continue;
                    b.Backdrops.Add(r);
                    break;
                }
            }
            return b.Backdrops;
        }

        // Switches off the enabled ones and remembers them in `off`, for SwitchBackOn.
        private static void SwitchOffForViewIn(List<Renderer> renderers, List<Renderer> off)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r != null && r.enabled) { r.enabled = false; off.Add(r); }
            }
        }

        private static void SwitchBackOn(List<Renderer> off)
        {
            for (int i = 0; i < off.Count; i++) if (off[i] != null) off[i].enabled = true;
            off.Clear();
        }

        private static OutsideInWallGrid EnsureOutsideInWallGrid(OutsideInBuilding b, int index, ShellAperture v)
        {
            var grid = b.WallGrids[index];
            if (grid != null && grid.Quad != null && !grid.Plain) return grid;
            if (grid != null)
            {
                if (grid.Mesh != null) Object.Destroy(grid.Mesh);
                if (grid.Quad != null) Object.Destroy(grid.Quad);
            }
            grid = new OutsideInWallGrid();
            // The pane's plane, exactly where RenderOutsideInPane puts its quad (recess is 0).
            float recess = Mathf.Max(OutsideInWindowRecess, 0f);
            Vector3 planeCenter = v.Center - v.Outward * recess;
            Vector3 corner = planeCenter - v.Right * (v.Width * 0.5f) - v.Up * (v.Height * 0.5f);
            grid.Mesh = BuildScreenGridMesh(corner, v.Right * v.Width, v.Up * v.Height, out grid.VX, out grid.VY, out grid.VZ, out grid.UV);
            grid.UVF = new float[grid.VX.Length * 2];

            grid.Quad = new GameObject("SI_OutsideInWallGrid");
            grid.Quad.layer = WINDOW_PORTAL_QUAD_LAYER;
            grid.Quad.AddComponent<MeshFilter>().sharedMesh = grid.Mesh;
            grid.Renderer = grid.Quad.AddComponent<MeshRenderer>();
            grid.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            grid.Renderer.receiveShadows = false;
            grid.Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;   // see EnsureOutsideInSlot
            grid.Renderer.enabled = false;
            b.WallGrids[index] = grid;
            return grid;
        }

        // A hand-made group's room objects, by name: the name itself or one of its "(n)" clones.
        private static void FindOutsideInWallHiddenByName(OutsideInBuilding b, OutsideInWallGroup group)
        {
            group.Hidden = new List<Renderer>();
            if (group.HideByName == null) return;
            for (int i = 0; i < b.Relayered.Count; i++)
            {
                var r = b.Relayered[i];
                if (r == null) continue;
                string name = r.gameObject.name;
                foreach (var wanted in group.HideByName)
                {
                    if (string.IsNullOrEmpty(wanted)) continue;
                    if (IsNameOrClone(name, wanted)) { group.Hidden.Add(r); break; }
                }
            }
            FindNearestCopyPieces(b, group);
        }

        private static bool IsNameOrClone(string name, string wanted)
        {
            return name == wanted || (name.StartsWith(wanted) && name.Length > wanted.Length + 2 && name[wanted.Length] == ' ' && name[wanted.Length + 1] == '(');
        }

        // The hand-made group's NearestCopyOf: of the room pieces with that name (or its "(n)" copies),
        // the one whose middle is nearest the middle of the group's room glass. Its named children join
        // the group's hidden list; its named materials become slots cleared while the group renders.
        // The hangar's sides, 2026-09-27: each side's copy of STR_AF_Hangar_Interior_Side_Prefab,
        // whose window frames are its material GLB_MetalRed_A01 alone (all 1496 of that material's
        // triangles lie within 30 cm of a side window; LOG-2026-09-27 0927-06).
        private static void FindNearestCopyPieces(OutsideInBuilding b, OutsideInWallGroup group)
        {
            group.ClearOn = null; group.ClearSlots = null;
            var hand = group.HandRow;
            if (hand == null || string.IsNullOrEmpty(hand.NearestCopyOf)) return;

            Vector3 glass = Vector3.zero; int counted = 0;
            foreach (int m in group.Members)
                if (m < b.ApertureOpenings.Count) { glass += b.ApertureOpenings[m].GlassCenter; counted++; }
            if (counted == 0) return;
            glass /= counted;

            Renderer copy = null; float best = float.MaxValue;
            for (int i = 0; i < b.Relayered.Count; i++)
            {
                var r = b.Relayered[i];
                if (r == null || !IsNameOrClone(r.gameObject.name, hand.NearestCopyOf)) continue;
                float d = (r.bounds.center - glass).sqrMagnitude;
                if (d < best) { best = d; copy = r; }
            }
            if (copy == null)
            {
                MelonLogger.Msg($"[DUVAR-KAMERA-ICERI] {b.InstanceId}: '{hand.NearestCopyOf}' odada bulunamadi; cerceveler gizlenmeyecek.",
                                $"[WALL-CAMERA-IN] {b.InstanceId}: the room piece '{hand.NearestCopyOf}' was not found, so its frames will not be hidden.");
                return;
            }

            int children = 0;
            if (hand.NearestCopyHideChildren != null)
                for (int i = 0; i < b.Relayered.Count; i++)
                {
                    var r = b.Relayered[i];
                    if (r == null || r.transform.parent != copy.transform) continue;
                    foreach (var wanted in hand.NearestCopyHideChildren)
                        if (!string.IsNullOrEmpty(wanted) && IsNameOrClone(r.gameObject.name, wanted)) { group.Hidden.Add(r); children++; break; }
                }

            var slots = new List<int>();
            if (hand.NearestCopyClearMaterials != null)
            {
                var mats = copy.sharedMaterials;
                for (int s = 0; s < mats.Length; s++)
                {
                    if (mats[s] == null) continue;
                    foreach (var wanted in hand.NearestCopyClearMaterials)
                        if (!string.IsNullOrEmpty(wanted) && IsNameOrClone(mats[s].name, wanted)) { slots.Add(s); break; }
                }
            }
            if (slots.Count > 0 && b.HiddenPaneMaterial != null) { group.ClearOn = copy; group.ClearSlots = slots.ToArray(); }
            MelonLogger.Msg($"[DUVAR-KAMERA-ICERI] {b.InstanceId}: el yapimi grup ({string.Join(",", group.Members)}): en yakin kopya {copy.gameObject.name}, {children} parca gizli, {(group.ClearOn != null ? slots.Count : 0)} malzeme saydam.",
                            $"[WALL-CAMERA-IN] {b.InstanceId}: hand-made group ({string.Join(",", group.Members)}): nearest copy {copy.gameObject.name}, {children} of its pieces switched off and {(group.ClearOn != null ? slots.Count : 0)} of its materials drawn clear while it renders{(slots.Count > 0 && b.HiddenPaneMaterial == null ? " (NOT cleared: the transparent material is missing)" : "")}.");
        }

        // Draws ClearOn with its ClearSlots transparent; returns what to put back, or null.
        private static Il2CppReferenceArray<Material> ClearNearestCopy(OutsideInBuilding b, OutsideInWallGroup group)
        {
            if (group.ClearOn == null || group.ClearSlots == null || b.HiddenPaneMaterial == null) return null;
            var saved = group.ClearOn.sharedMaterials;
            var swapped = new Material[saved.Length];
            for (int s = 0; s < saved.Length; s++) swapped[s] = saved[s];
            foreach (int s in group.ClearSlots) if (s < swapped.Length) swapped[s] = b.HiddenPaneMaterial;
            group.ClearOn.sharedMaterials = swapped;
            return saved;
        }

        // The room's pieces standing wholly on the viewer's side of any member's room window, near it.
        // KNOWN TO MISS A TURNED ROOM: Renderer.bounds is a world-axis box, and at the hangar (turned
        // about 30 degrees) every door object's box straddles its glass by meters while all of its
        // vertices stand in front of it (0926-28). A hand-made group names its objects instead.
        private static void FindOutsideInWallHidden(OutsideInBuilding b, OutsideInWallGroup group)
        {
            group.Hidden = new List<Renderer>();
            Vector3 outward = group.RoomOutward;
            for (int i = 0; i < b.Relayered.Count; i++)
            {
                var r = b.Relayered[i];
                if (r == null) continue;
                Bounds bounds = r.bounds;
                float ext = Mathf.Abs(bounds.extents.x * outward.x) + Mathf.Abs(bounds.extents.y * outward.y) + Mathf.Abs(bounds.extents.z * outward.z);
                foreach (int m in group.Members)
                {
                    var v = OutsideInViewOf(b.Apertures[m]);
                    Vector3 glass = v.Center + v.Delta;
                    if (Vector3.Dot(bounds.center - glass, outward) - ext <= 0.05f) continue;
                    if ((bounds.center - glass).magnitude > OUTSIDE_IN_WALL_HIDE_REACH) continue;
                    group.Hidden.Add(r);
                    break;
                }
            }
        }

        // ─── EVERY FRAME, IN THE MOD'S TICK: WHICH GROUPS GET A SLOT ───

        private static void BeginOutsideInWallFrame()
        {
            if (s_OutsideInSlotWallBuilding == null || s_OutsideInSlotWallBuilding.Length != s_OutsideInSlotCount)
            {
                s_OutsideInSlotWallBuilding = new OutsideInBuilding[s_OutsideInSlotCount];
                s_OutsideInSlotWallGroup = new int[s_OutsideInSlotCount];
            }
            for (int k = 0; k < s_OutsideInSlotWallGroup.Length; k++) { s_OutsideInSlotWallBuilding[k] = null; s_OutsideInSlotWallGroup[k] = -1; }
            s_OutsideInWallFrame = Time.frameCount;
        }

        // 1: the pane's group took this slot. 0: the pane is in a group but takes no slot (its group
        // has one already, or it is off screen, or the group cameras have failed). -1: not grouped,
        // the per-pane camera draws it.
        private static int ClaimOutsideInWallSlot(OutsideInBuilding b, int index, int slot, bool wanted)
        {
            EnsureOutsideInWallGroups(b);
            int g = b.ApertureWallGroup[index];
            if (g < 0) return -1;
            var group = b.WallGroups[g];
            if (group.Members.Count < 2) return -1;
            if (s_OutsideInWallFaulted || group.ClaimedFrame == Time.frameCount) return 0;
            if (!OutsideInPaneOnScreen(OutsideInViewOf(b.Apertures[index]))) return 0;

            group.ClaimedFrame = Time.frameCount;
            HideOutsideInSlot(slot);
            s_OutsideInSlotWallBuilding[slot] = b;
            s_OutsideInSlotWallGroup[slot] = g;
            // The group fades as one: every member takes the claiming pane's level and wish.
            float level = OutsideInPaneLevel(b, index);
            while (b.PaneLevel.Count < b.Apertures.Count) b.PaneLevel.Add(0f);
            foreach (int m in group.Members)
            {
                if (m < b.WasDrawn.Count) b.WasDrawn[m] = true;
                MarkOutsideInPaneDrawn(b, m, wanted);
                b.PaneLevel[m] = level;
            }
            if (OutsideInWallMode == 0 && !group.ScreenAligned)
            {
                // The plane camera draws now, in the tick, as the per-pane camera does; the late
                // postfix has nothing to do for this slot. A hand-made group is screen-aligned
                // whatever the mode and is drawn in the postfix instead.
                s_OutsideInSlotWallGroup[slot] = -1;
                s_OutsideInSlotWallBuilding[slot] = null;
                Camera main = GameManager.GetMainCamera();
                if (main != null) RenderOutsideInPlaneGroup(b, group, slot, main, main.transform.position);
            }
            return 1;
        }

        // Leaves the visible part of the pane in s_WindowPortalPoly.
        private static bool OutsideInPaneOnScreen(ShellAperture v)
        {
            float recess = Mathf.Max(OutsideInWindowRecess, 0f);
            Vector3 planeCenter = v.Center - v.Outward * recess;
            Vector3 pa = planeCenter - v.Right * (v.Width * 0.5f) - v.Up * (v.Height * 0.5f);
            s_WindowPortalPoly.Clear();
            s_WindowPortalPoly.Add(pa);
            s_WindowPortalPoly.Add(pa + v.Right * v.Width);
            s_WindowPortalPoly.Add(pa + v.Right * v.Width + v.Up * v.Height);
            s_WindowPortalPoly.Add(pa + v.Up * v.Height);
            for (int i = 0; i < s_WindowPortalPlanes.Length; i++)
            {
                ClipWindowPortalPolygon(s_WindowPortalPoly, s_WindowPortalPolyB, s_WindowPortalPlanes[i]);
                s_WindowPortalPoly.Clear();
                for (int k = 0; k < s_WindowPortalPolyB.Count; k++) s_WindowPortalPoly.Add(s_WindowPortalPolyB[k]);
                if (s_WindowPortalPoly.Count == 0) return false;
            }
            return s_WindowPortalPoly.Count >= 3;
        }

        // ─── EVERY FRAME, FROM THE POSTFIX ON vp_FPSCamera.LateUpdate: DRAW THEM ───

        internal static void TickOutsideInWallCameras()
        {
            // Last frame's grids off first, whatever happens below.
            for (int i = 0; i < s_OutsideInWallShown.Count; i++) if (s_OutsideInWallShown[i] != null) s_OutsideInWallShown[i].enabled = false;
            s_OutsideInWallShown.Clear();

            if (s_OutsideInWallFaulted || s_OutsideInWallFrame != Time.frameCount || s_OutsideInCams == null || s_OutsideInSlotWallGroup == null) return;
            var bound = s_OI;
            try
            {
                for (int slot = 0; slot < s_OutsideInSlotWallGroup.Length && slot < s_OutsideInSlotCount; slot++)
                {
                    var b = s_OutsideInSlotWallBuilding[slot];
                    int g = s_OutsideInSlotWallGroup[slot];
                    if (b == null || g < 0 || !b.Borrowed || b.WallGroups == null || g >= b.WallGroups.Count || !s_OutsideInBuildings.Contains(b)) continue;
                    BindOutsideIn(b);
                    DrawOutsideInWallGroup(b, b.WallGroups[g], slot);
                }
            }
            catch (System.Exception e)
            {
                s_OutsideInWallFaulted = true;
                for (int i = 0; i < s_OutsideInWallShown.Count; i++) if (s_OutsideInWallShown[i] != null) s_OutsideInWallShown[i].enabled = false;
                s_OutsideInWallShown.Clear();
                MelonLogger.Warning("[WALL-CAMERA-IN] The wall cameras for the views into rooms failed and are off until the game is restarted; those windows show the dark backing: " + e);
            }
            finally { BindOutsideIn(bound); }
        }

        private static void DrawOutsideInWallGroup(OutsideInBuilding b, OutsideInWallGroup group, int slot)
        {
            Camera main = GameManager.GetMainCamera();
            if (main == null) return;
            Vector3 eye = main.transform.position;
            Matrix4x4 view = main.worldToCameraMatrix, proj = main.projectionMatrix;
            Matrix4x4 vp = proj * view;
            int sw = Screen.width, sh = Screen.height;

            // The screen rectangle round the members on screen.
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            s_OutsideInWallOnScreen.Clear();
            float nearestGlass = float.MaxValue;     // depth along the gaze of the nearest visible glass
            foreach (int m in group.Members)
            {
                var v = OutsideInViewOf(b.Apertures[m]);
                if (Vector3.Dot(eye - v.Center, v.Outward) <= OUTSIDE_IN_MIN_EYE_DISTANCE) continue;
                if (!OutsideInPaneOnScreen(v)) continue;
                s_OutsideInWallOnScreen.Add(m);
                for (int p = 0; p < s_WindowPortalPoly.Count; p++)
                {
                    Vector3 q = s_WindowPortalPoly[p];
                    Vector4 clip = vp * new Vector4(q.x, q.y, q.z, 1f);
                    if (clip.w > 1e-5f && clip.w < nearestGlass) nearestGlass = clip.w;
                    if (clip.w <= 1e-5f) continue;
                    float sx = (clip.x / clip.w * 0.5f + 0.5f) * sw, sy = (clip.y / clip.w * 0.5f + 0.5f) * sh;
                    if (sx < x0) x0 = sx; if (sx > x1) x1 = sx;
                    if (sy < y0) y0 = sy; if (sy > y1) y1 = sy;
                }
            }
            if (s_OutsideInWallOnScreen.Count == 0 || x1 <= x0 || y1 <= y0) return;

            int px0 = Mathf.Max(0, Mathf.FloorToInt(x0) - WALL_SCREEN_MARGIN), py0 = Mathf.Max(0, Mathf.FloorToInt(y0) - WALL_SCREEN_MARGIN);
            int px1 = Mathf.Min(sw, Mathf.CeilToInt(x1) + WALL_SCREEN_MARGIN), py1 = Mathf.Min(sh, Mathf.CeilToInt(y1) + WALL_SCREEN_MARGIN);
            if (px1 <= px0 || py1 <= py0) return;

            var current = s_OutsideInTargets[slot];
            int tw = WallTargetSize(px1 - px0, current != null ? current.width : 0);
            int th = WallTargetSize(py1 - py0, current != null ? current.height : 0);
            if (!EnsureOutsideInSlot(slot, main, tw, th)) return;
            Camera cam = s_OutsideInCams[slot];
            var slotQuad = s_OutsideInQuads[slot];
            if (slotQuad != null) { var sq = slotQuad.GetComponent<MeshRenderer>(); if (sq != null) sq.enabled = false; }

            float xa = px0 / (float)sw * 2f - 1f, xb = (px0 + tw) / (float)sw * 2f - 1f;
            float ya = py0 / (float)sh * 2f - 1f, yb = (py0 + th) / (float)sh * 2f - 1f;
            Matrix4x4 narrow = Matrix4x4.identity;
            narrow.m00 = 2f / (xb - xa); narrow.m03 = -(xa + xb) / (xb - xa);
            narrow.m11 = 2f / (yb - ya); narrow.m13 = -(ya + yb) / (yb - ya);

            Matrix4x4 map = group.BuildingToRoom;
            Vector3 roomEye = map.MultiplyPoint3x4(eye);
            Vector3 roomForward = map.MultiplyVector(main.transform.forward), roomUp = map.MultiplyVector(main.transform.up);
            cam.transform.SetPositionAndRotation(roomEye, Quaternion.LookRotation(roomForward, roomUp));
            // ORDER MATTERS: these reset an explicit projection matrix, so they go first.
            cam.fieldOfView = s_FrustumCapturedLate ? s_LateFieldOfView : main.fieldOfView;
            cam.aspect = main.aspect;
            cam.nearClipPlane = main.nearClipPlane;
            cam.farClipPlane = OUTSIDE_IN_FAR_CLIP;
            cam.worldToCameraMatrix = view * map.inverse;
            cam.projectionMatrix = narrow * (OutsideInWallCutAtGlass ? WithNearDistance(proj, nearestGlass - 0.005f, main.nearClipPlane) : proj);

            foreach (int m in s_OutsideInWallOnScreen)
            {
                var grid = EnsureOutsideInWallGrid(b, m, OutsideInViewOf(b.Apertures[m]));
                WriteScreenGridUVs(grid.VX, grid.VY, grid.VZ, grid.UVF, grid.UV, vp, sw, sh, px0, py0, tw, th);
                grid.Mesh.uv = grid.UV;
                ApplyViewFade(grid.Renderer, slot, OutsideInPaneLevel(b, m));
                grid.Renderer.enabled = true;
                s_OutsideInWallShown.Add(grid.Renderer);
            }

            if (group.Hidden == null) { if (group.HideByName != null) FindOutsideInWallHiddenByName(b, group); else FindOutsideInWallHidden(b, group); }
            s_OutsideInWallSwitchedOff.Clear();
            var savedMode = RenderSettings.ambientMode;
            var savedLight = RenderSettings.ambientLight;
            Il2CppReferenceArray<Material> clearedFrom = null;
            try
            {
                SwitchOffForViewIn(group.Hidden, s_OutsideInWallSwitchedOff);
                SwitchOffForViewIn(OutsideInBackdrops(b), s_OutsideInWallSwitchedOff);
                clearedFrom = ClearNearestCopy(b, group);
                ApplyOutsideInAmbient();
                cam.Render();
            }
            finally
            {
                RenderSettings.ambientMode = savedMode; RenderSettings.ambientLight = savedLight;
                if (clearedFrom != null && group.ClearOn != null) group.ClearOn.sharedMaterials = clearedFrom;
                SwitchBackOn(s_OutsideInWallSwitchedOff);
            }
        }
    }
}
