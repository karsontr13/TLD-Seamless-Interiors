using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── ONE CAMERA PER WALL, NOT ONE PER WINDOW ───
        //
        // Every group of room windows standing on ONE plane with ONE room-to-building offset is drawn
        // by one camera: the player's own lens, moved by the group's offset and narrowed to the screen
        // rectangle round the group's visible windows at one texel per screen pixel. Each window is a
        // grid whose texture coordinates are its own points' screen positions, so the picture lines up
        // with the hole exactly without a frustum per window. Proven in the console as SIWall7
        // (_dev/wall-camera-screen-aligned-v7-console.cs) on the hangar's outer front door pair in a
        // blizzard; owner, 2026-09-22: "It looked great under all conditions."
        //
        // WHAT HAD TO BE TRUE FOR IT TO WORK, each learned the hard way (evidence 0922-24 to 0922-55):
        // - No tilted near plane. The oblique matrix pulled the far cutoff in to a few meters and broke
        //   the fog's depth. So the camera draws from the lens outward, and the building's own shell
        //   between the lens and the glass has to be kept out of it: its pieces reaching up to
        //   WALL_SHELL_HIDE_DEPTH inside the glass are drawn as shadows only while this camera renders.
        //   (The console hid them by layer, which also took their shadows off the ground outside.)
        // - Far distance is the player's, every frame. The borrowed lens carries the player's far (445.5 m
        //   in a blizzard) and the fog turns depth into meters with the camera's near and far; saying
        //   15000 made the fog 34 times too thin, a flat white window.
        // - Drawn after the light absorbers (WINDOW_VIEW_OUT_RENDER_QUEUE), like the other views out.
        // - The texture coordinates must be the frame's own: the camera is placed in the postfix on
        //   vp_FPSCamera.LateUpdate, after the zoom, not in the mod's tick.
        //
        // A WINDOW ALONE IN ITS GROUP is drawn by the per-window camera instead (BuildWallGroups), at the
        // same moment and from the same slots, so a building like the Camp Office, whose room windows
        // are spaced unlike its building's, looks exactly as it did.
        //
        // ONLY FOR PARKED ROOMS. The lens draws everything from the eye outward, which in a parked room
        // is the empty air under the room and then the building. In a room left standing in its
        // building, the eye plus the offset is inside the room, and the room's own walls would be drawn
        // over the glass. Those rooms keep the per-window cameras.
        //
        // A stopped or undrawn wall leaves its windows on the painted pane, never on the per-window
        // cameras (owner's rule). WindowWallCameras off brings the per-window cameras back for the
        // frame-time comparison he asked for; it takes effect at once.

        private const int WALL_GRID = 96;                      // what passed in the console
        // GROUPED BY PLANE AND BY WHERE THE CAMERA MUST STAND (see WallSamePlane below; the plane
        // test was dropped on 2026-09-23 and restored on 2026-09-26). The texture
        // coordinates are screen positions, so members on different planes cost nothing; only the eye
        // has to be shared. Sideways and up, an eye in the wrong place shifts the picture against the
        // hole one for one, so that allowance is tight. Along the view it only changes the scale of
        // what is close outside, by a few percent at 25 cm, so that one is loose. The first two tries
        // grouped by plane: 2 cm split every mirrored pair of the hangar's front doors, 5 cm put door 5
        // with 1 and 6 and left its mirror 2 alone, and the view start inside the offset pushed 3 and
        // 4 11.7 cm apart along the view (2026-09-23).
        private const float WALL_SAME_FACING = 0.999f;
        private const float WALL_SAME_OFFSET_ACROSS = 0.10f;   // sideways and up, every member against every other
        private const float WALL_SAME_OFFSET_ALONG = 0.25f;    // along the facing
        // ONE CAMERA PER PLANE, the owner's rule from the start (agreed 2026-09-22), restored on
        // 2026-09-26 after the agent had dropped it on 2026-09-23 without his agreement ("the plan was
        // always one wall camera per plane, where more than one window matches that plane, and then
        // use masking ... it was wrong"). Every member within this of every other along the facing.
        // The hangar's front doors are four planes in mirror pairs 0.5 to 3.1 cm apart, the nearest
        // doors on different planes 3.9 cm apart (0922-20). Views in and out alike; a live lever.
        internal static float WallSamePlane = 0.035f;

        // WINDOWS ARE OFFERED TO THE GROUPS IN ORDER OF PLACE, NOT OF INDEX (2026-09-26). The groups
        // fill first come, first served, and a member must agree with every other; taken by index,
        // the hangar's pane 12 (1.3 cm from 23) and 25 (0.7 cm from 24) were shut out of groups that
        // had already taken members they do not match. Sorted by facing, then plane, then where the
        // camera stands across and up the wall, neighbors meet first. The rules are unchanged: only
        // the order. A lever for rollback: false = by index, as before.
        internal static bool WallGroupingSorted = true;

        // The order in which windows are offered to the groups. `valid` false = not grouped at all.
        private static List<int> WallGroupingOrder(int n, System.Func<int, bool> valid, System.Func<int, Vector3> facing,
                                                   System.Func<int, float> plane, System.Func<int, Vector3> eye)
        {
            var order = new List<int>(n);
            for (int i = 0; i < n; i++) if (valid(i)) order.Add(i);
            if (!WallGroupingSorted) return order;
            var keys = new Dictionary<int, Vector4>(order.Count);
            foreach (int i in order)
            {
                Vector3 f = facing(i);
                Vector3 across = Vector3.Cross(Vector3.up, f).normalized;
                Vector3 e = eye(i);
                keys[i] = new Vector4(Mathf.Round(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg), Mathf.Round(plane(i) / Mathf.Max(WallSamePlane, 0.001f)),
                                      Vector3.Dot(e, across), e.y);
            }
            order.Sort((a, b) =>
            {
                Vector4 ka = keys[a], kb = keys[b];
                int c = ka.x.CompareTo(kb.x); if (c != 0) return c;
                c = ka.y.CompareTo(kb.y); if (c != 0) return c;
                c = ka.z.CompareTo(kb.z); if (c != 0) return c;
                c = ka.w.CompareTo(kb.w); if (c != 0) return c;
                return a.CompareTo(b);
            });
            return order;
        }
        private const float WALL_SHELL_HIDE_DEPTH = 3f;        // what passed: pieces reaching up to 3 m inside the glass
        private const float WALL_SHELL_HIDE_REACH = 40f;       // and within this of the group's glass
        private const int WALL_TARGET_STEP = 128;
        private const int WALL_TARGET_SLACK = 256;             // a target is kept until it is this much too big
        private const int WALL_SCREEN_MARGIN = 2;

        private static MelonPreferences_Entry<bool> s_PrefWindowWallCameras;

        private class WallGroup
        {
            public readonly List<int> Members = new List<int>();
            public Vector3 Offset;
            public Vector3 Outward;
            public float Priority;
            public bool Visible, WasDrawn;
            public bool Alone;                     // one window: drawn by the per-window camera, see BuildWallGroups
            public float X0, X1, Y0, Y1;           // screen rectangle this frame, pixels
            public List<Renderer> Hidden;          // built on first draw, when the shell is certainly on
            public List<UnityEngine.Rendering.ShadowCastingMode> HiddenModes;
            public List<bool> HiddenOff;           // hidden by switching off (casts nothing anyway)
            public bool Hand;                      // made from the building's row (ViewOutGroups); its members are fixed
            public float SidewaysCm;               // the row's ViewOutGroupSidewaysCm for a hand group: + right looking out
            public int HandIndex = -1;             // which of the row's ViewOutGroups this is; -1 for a group made by the rules
        }

        // Groups made by hand from the building's row (BuildingWindowOverride.ViewOutGroups) come first;
        // the rules group what is left. Live lever, in force at the next entry into a room. False = the
        // rules alone, as before. The views in have OutsideInHandGroups.
        internal static bool WindowHandGroups = true;

        private static bool s_WallCamerasActive, s_WallCamerasFaulted;
        private static readonly List<WallGroup> s_WallGroups = new List<WallGroup>();
        private static int[] s_WallSlotGroup;
        private static readonly List<int> s_WallOrder = new List<int>();

        // Per opening, wall mode only: the grid's world points and its texture coordinates.
        private static readonly List<float[]> s_WallVX = new List<float[]>(), s_WallVY = new List<float[]>(), s_WallVZ = new List<float[]>();
        private static readonly List<Il2CppStructArray<Vector2>> s_WallUV = new List<Il2CppStructArray<Vector2>>();
        private static readonly List<float[]> s_WallUVF = new List<float[]>();

        // THE TEXTURE COORDINATES ARE WRITTEN IN ONE BLOCK, NOT ONE BY ONE. Every element written
        // through the interop array's indexer crosses into the game's memory on its own: 0.23 ms for
        // one window's 9409 points, two thirds of the whole cost, and the wall tick took 10.1 ms a
        // frame at the hangar with 9 cameras drawing (2026-09-23, evidence 0923-09). So the points are
        // worked out in a plain float array and copied in with one Marshal.Copy, 0.0013 ms. Uploading
        // a managed array instead is as quick but allocates a new game-side array every time. The
        // data starts after the array's header, four pointers on this runtime; checked once at
        // setup, and the indexer is used if the check ever fails.
        private static Il2CppStructArray<int> s_WallGridTriangles;
        private static int s_WallUVBlockCopy;   // 0 unchecked, 1 works, -1 use the indexer
        private static readonly List<bool> s_WallMemberOnScreen = new List<bool>();

        private static Camera.CameraCallback s_WallShellPreCull, s_WallShellPostRender;
        private static bool s_WallShellHooked;
        private static int s_WallShellHiddenGroup = -1;

        private static bool WallCamerasWanted(SeamlessInteriorInstance instance)
        {
            // Not the fault flag: a failed wall camera leaves its windows on the painted pane, never on
            // the per-window cameras (owner's rule), so a fault must not switch the mode.
            if (s_PrefWindowWallCameras == null || !s_PrefWindowWallCameras.Value) return false;
            return instance != null && instance.Config != null && instance.Config.IsParked;
        }

        // ─── SETUP ───

        private static GameObject CreateWallQuad(InteriorWindowOpening opening)
        {
            var quad = new GameObject("SI_WallQuad");
            quad.layer = WINDOW_PORTAL_QUAD_LAYER;

            Vector3 pa = opening.PortalCenter - opening.Right * (opening.Width * 0.5f) - opening.Up * (opening.Height * 0.5f);
            float[] vx, vy, vz;
            Il2CppStructArray<Vector2> uv;
            var mesh = BuildScreenGridMesh(pa, opening.Right * opening.Width, opening.Up * opening.Height, out vx, out vy, out vz, out uv);
            int n = vx.Length;

            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = quad.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;

            s_WallVX.Add(vx); s_WallVY.Add(vy); s_WallVZ.Add(vz); s_WallUV.Add(uv); s_WallUVF.Add(new float[n * 2]);
            s_WallMemberOnScreen.Add(false);
            return quad;
        }

        // A flat grid of WALL_GRID x WALL_GRID cells over the rectangle corner + across + up, with its
        // points kept in plain float arrays for WriteScreenGridUVs. Shared by the wall cameras
        // looking out and looking in (OutsideInWallCameras.cs).
        private static Mesh BuildScreenGridMesh(Vector3 corner, Vector3 across, Vector3 upSpan,
                                                out float[] vx, out float[] vy, out float[] vz, out Il2CppStructArray<Vector2> uv)
        {
            // PLAIN FLOATS, NOT Vector3 ARITHMETIC. Each Vector3 operation is a native call in this
            // build: the same loop written with them took 3.29 ms a grid against 0.14, and the 42
            // grids of the hangar held the door's frame for 174 ms of a 271 ms setup (2026-09-23).
            float px = corner.x, py = corner.y, pz = corner.z;
            float rx = across.x / WALL_GRID, ry = across.y / WALL_GRID, rz = across.z / WALL_GRID;
            float ux = upSpan.x / WALL_GRID, uy = upSpan.y / WALL_GRID, uz = upSpan.z / WALL_GRID;
            int n = (WALL_GRID + 1) * (WALL_GRID + 1);
            var verts = new Vector3[n];
            vx = new float[n]; vy = new float[n]; vz = new float[n];
            for (int y = 0; y <= WALL_GRID; y++)
                for (int x = 0; x <= WALL_GRID; x++)
                {
                    int i = y * (WALL_GRID + 1) + x;
                    float qx = px + rx * x + ux * y, qy = py + ry * x + uy * y, qz = pz + rz * x + uz * y;
                    vx[i] = qx; vy[i] = qy; vz[i] = qz;
                    verts[i] = new Vector3(qx, qy, qz);
                }
            // Every grid has the same triangles, so one game-side list serves them all.
            if (s_WallGridTriangles == null)
            {
                var tris = new int[WALL_GRID * WALL_GRID * 6];
                for (int y = 0, t = 0; y < WALL_GRID; y++)
                    for (int x = 0; x < WALL_GRID; x++)
                    {
                        int a = y * (WALL_GRID + 1) + x;
                        tris[t++] = a; tris[t++] = a + WALL_GRID + 1; tris[t++] = a + WALL_GRID + 2;
                        tris[t++] = a; tris[t++] = a + WALL_GRID + 2; tris[t++] = a + 1;
                    }
                s_WallGridTriangles = tris;
            }

            uv = new Il2CppStructArray<Vector2>(n);
            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = s_WallGridTriangles;
            mesh.RecalculateBounds();
            // Rewritten every frame it is drawn.
            mesh.MarkDynamic();
            if (s_WallUVBlockCopy == 0) s_WallUVBlockCopy = WallUVBlockCopyWorks(uv) ? 1 : -1;
            return mesh;
        }

        // Every grid point's own screen position, in the render target's texture coordinates: the
        // target covers screen pixels px0..px0+tw, py0..py0+th of the lens vp.
        // Plain floats: Unity's vector arithmetic is a native call per operation in this build.
        private static void WriteScreenGridUVs(float[] vx, float[] vy, float[] vz, float[] f, Il2CppStructArray<Vector2> uvs,
                                               Matrix4x4 vp, int sw, int sh, int px0, int py0, int tw, int th)
        {
            float m00 = vp.m00, m01 = vp.m01, m02 = vp.m02, m03 = vp.m03, m10 = vp.m10, m11 = vp.m11, m12 = vp.m12, m13 = vp.m13, m30 = vp.m30, m31 = vp.m31, m32 = vp.m32, m33 = vp.m33;
            float ux = 0.5f * sw / tw, uy = 0.5f * sh / th, ox = (0.5f * sw - px0) / tw, oy = (0.5f * sh - py0) / th;
            for (int n = 0; n < vx.Length; n++)
            {
                float x = vx[n], y = vy[n], z = vz[n];
                float w = m30 * x + m31 * y + m32 * z + m33;
                if (w <= 1e-4f) { f[2 * n] = 0f; f[2 * n + 1] = 0f; continue; }
                float cx = (m00 * x + m01 * y + m02 * z + m03) / w, cy = (m10 * x + m11 * y + m12 * z + m13) / w;
                f[2 * n] = cx * ux + ox; f[2 * n + 1] = cy * uy + oy;
            }
            if (s_WallUVBlockCopy > 0) System.Runtime.InteropServices.Marshal.Copy(f, 0, uvs.Pointer + 4 * System.IntPtr.Size, f.Length);
            else for (int n = 0; n < vx.Length; n++) uvs[n] = new Vector2(f[2 * n], f[2 * n + 1]);
        }

        private static bool WallUVBlockCopyWorks(Il2CppStructArray<Vector2> uv)
        {
            try
            {
                int last = uv.Length - 1;
                var probe = new float[uv.Length * 2];
                probe[0] = 0.25f; probe[1] = 0.5f; probe[last * 2] = 0.75f; probe[last * 2 + 1] = 1f;
                System.Runtime.InteropServices.Marshal.Copy(probe, 0, uv.Pointer + 4 * System.IntPtr.Size, probe.Length);
                bool ok = uv[0] == new Vector2(0.25f, 0.5f) && uv[last] == new Vector2(0.75f, 1f);
                uv[0] = Vector2.zero; uv[last] = Vector2.zero;
                if (!ok) MelonLogger.Warning("[WALL-CAMERA] The fast texture coordinate copy read back wrong; using the slow one.");
                return ok;
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning("[WALL-CAMERA] The fast texture coordinate copy failed; using the slow one: " + e.Message);
                return false;
            }
        }

        // After PairWindowOpeningsToShell: needs each opening's offset and view start.
        private static void BuildWallGroups(string id)
        {
            s_WallGroups.Clear();
            // The room-to-building offset alone. The view start is the per-window cameras' way of
            // leaving the building's own frame out: they start their picture past it. A wall camera
            // hides the frame instead (FindWallShellPieces), so its eye goes exactly where the player's
            // would stand at the building. The console version added the view start; on the pair it
            // was tried on that was 4 and 6 cm, and on the hangar's inner doors it is 52 and 61.
            var offsets = new List<Vector3>(s_WindowPortalOpenings.Count);
            for (int i = 0; i < s_WindowPortalOpenings.Count; i++) offsets.Add(s_WindowPortalDelta[i]);

            // The building's hand-made groups first. A window is taken by the first group naming it;
            // one facing another way than the group's first window is left to the rules.
            var taken = new bool[s_WindowPortalOpenings.Count];
            int handMade = 0;
            SeamlessInteriorInstance rowInstance;
            BuildingWindowOverride row;
            if (WindowHandGroups && id != null && ActiveInteriors.TryGetValue(id, out rowInstance) && rowInstance != null && rowInstance.Config != null
                && TryGetBuildingWindowOverride(rowInstance.Config.ExteriorShellPrefabName, out row) && row.ViewOutGroups != null)
            {
                for (int h = 0; h < row.ViewOutGroups.Length; h++)
                {
                    var hand = row.ViewOutGroups[h];
                    if (hand == null) continue;
                    WallGroup group = null;
                    foreach (int w in hand)
                    {
                        if (w < 0 || w >= s_WindowPortalOpenings.Count || taken[w]) continue;
                        if (group == null) group = new WallGroup { Outward = s_WindowPortalOpenings[w].Outward, Hand = true };
                        else if (Vector3.Dot(group.Outward, s_WindowPortalOpenings[w].Outward) < WALL_SAME_FACING) continue;
                        group.Members.Add(w);
                        taken[w] = true;
                    }
                    if (group == null) continue;
                    group.HandIndex = h;
                    if (row.ViewOutGroupSidewaysCm != null && h < row.ViewOutGroupSidewaysCm.Length) group.SidewaysCm = row.ViewOutGroupSidewaysCm[h];
                    s_WallGroups.Add(group);
                    handMade++;
                }
            }

            var groupingOrder = WallGroupingOrder(s_WindowPortalOpenings.Count, i => !taken[i], i => s_WindowPortalOpenings[i].Outward,
                                                  i => Vector3.Dot(s_WindowPortalOpenings[i].PortalCenter, s_WindowPortalOpenings[i].Outward), i => offsets[i]);
            foreach (int i in groupingOrder)
            {
                var opening = s_WindowPortalOpenings[i];
                WallGroup home = null;
                // A window that is not a rectangle stays alone, on its own camera (ShapedViews.cs).
                for (int g = 0; g < s_WallGroups.Count && home == null && opening.Outline == null; g++)
                {
                    var group = s_WallGroups[g];
                    if (group.Hand) continue;
                    if (s_WindowPortalOpenings[group.Members[0]].Outline != null) continue;
                    if (Vector3.Dot(group.Outward, opening.Outward) < WALL_SAME_FACING) continue;
                    bool agrees = true;
                    for (int m = 0; m < group.Members.Count && agrees; m++)
                    {
                        Vector3 apart = offsets[group.Members[m]] - offsets[i];
                        float along = Vector3.Dot(apart, group.Outward);
                        float plane = Vector3.Dot(s_WindowPortalOpenings[group.Members[m]].PortalCenter - opening.PortalCenter, group.Outward);
                        agrees = Mathf.Abs(plane) <= WallSamePlane
                              && Mathf.Abs(along) <= WALL_SAME_OFFSET_ALONG
                              && (apart - group.Outward * along).magnitude <= WALL_SAME_OFFSET_ACROSS;
                    }
                    if (agrees) home = group;
                }
                if (home == null)
                {
                    home = new WallGroup { Outward = opening.Outward };
                    s_WallGroups.Add(home);
                }
                home.Members.Add(i);
            }

            // A WINDOW ALONE IN ITS GROUP KEEPS THE PER-WINDOW CAMERA. It gains nothing from a wall
            // camera, and the wall camera's way of keeping the building out of its view, hiding the
            // building's pieces between the lens and the glass, hides whole meshes: the Camp Office's
            // walls, porch roof and roof snow are one, STR_CampOfficeBase, 12 x 9.4 x 11.9 m, and its
            // front windows lost the porch with its icicles left floating (owner, 2026-09-23). The
            // per-window camera starts its picture at the glass and never had that problem. Its quad is
            // the plain four-corner one that camera draws on.
            foreach (var group in s_WallGroups)
            {
                if (group.Members.Count != 1) continue;
                group.Alone = true;
                int m = group.Members[0];
                var old = s_WindowPortalQuads[m];
                if (old != null)
                {
                    var oldFilter = old.GetComponent<MeshFilter>();
                    if (oldFilter != null && oldFilter.sharedMesh != null) Object.Destroy(oldFilter.sharedMesh);
                    Object.Destroy(old);
                }
                s_WindowPortalQuads[m] = CreateWindowPortalQuad(s_WindowPortalOpenings[m]);
            }

            var report = new System.Text.StringBuilder();
            for (int g = 0; g < s_WallGroups.Count; g++)
            {
                var group = s_WallGroups[g];
                Vector3 mean = Vector3.zero;
                foreach (int m in group.Members) mean += offsets[m];
                group.Offset = mean / group.Members.Count;
                // Slid along the wall by hand; the report still says how far the members stand from
                // their own mean, and the slide beside it.
                Vector3 meanOffset = group.Offset;
                if (group.SidewaysCm != 0f) group.Offset += Vector3.Cross(Vector3.up, group.Outward).normalized * (group.SidewaysCm / 100f);
                float spread = 0f;
                foreach (int m in group.Members) spread = Mathf.Max(spread, (offsets[m] - meanOffset).magnitude);
                report.Append(g + 1).Append(": ").Append(string.Join(",", group.Members)).Append($" ({spread * 100f:F1}{(group.Hand ? ", by hand" : "")}{(group.SidewaysCm != 0f ? $", slid {group.SidewaysCm:F0} cm" : "")}); ");
            }
            int alone = 0; foreach (var group in s_WallGroups) if (group.Members.Count == 1) alone++;
            MelonLogger.Msg($"[DUVAR-KAMERA] {id}: {s_WindowPortalOpenings.Count} pencere, {s_WallGroups.Count - alone} duvar kamerasi, {alone} tek pencere kendi kamerasiyla. Grup: pencereler (ortalamadan en buyuk fark, cm): {report}",
                            $"[WALL-CAMERA] {id}: {s_WindowPortalOpenings.Count} windows: {s_WallGroups.Count - alone} wall cameras for grouped windows, {alone} windows alone on their own per-window camera ({handMade} by hand from the building's row; allowance {WALL_SAME_OFFSET_ACROSS * 100f:F0} cm sideways, {WALL_SAME_OFFSET_ALONG * 100f:F0} cm along, plane {WallSamePlane * 100f:F1} cm, {(WallGroupingSorted ? "sorted" : "by index")}). Group: windows (furthest from the group's mean offset, cm): {report}");

            s_WallSlotGroup = new int[s_WindowPortalSlotCount];
            for (int k = 0; k < s_WallSlotGroup.Length; k++) s_WallSlotGroup[k] = -1;

            if (s_WallShellPreCull == null)
            {
                s_WallShellPreCull = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnWallShellPreCull));
                s_WallShellPostRender = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnWallShellPostRender));
            }
            if (!s_WallShellHooked)
            {
                Camera.onPreCull += s_WallShellPreCull;
                Camera.onPostRender += s_WallShellPostRender;
                s_WallShellHooked = true;
            }
        }

        // The shell's pieces standing between the lens and any of this group's panes: from just outside
        // a pane's glass to WALL_SHELL_HIDE_DEPTH inside it, and near that pane. Each member is looked
        // at on its own plane, since a group's windows may stand on several (the hangar's staggered
        // front doors). Read on first draw, when KeepParkedShellOn has certainly had the shell on.
        private static void FindWallShellPieces(WallGroup group, SeamlessInteriorInstance instance)
        {
            group.Hidden = new List<Renderer>();
            group.HiddenModes = new List<UnityEngine.Rendering.ShadowCastingMode>();
            group.HiddenOff = new List<bool>();
            if (instance == null || instance.ExteriorShell == null) return;

            Vector3 outward = group.Outward;
            var renderers = instance.ExteriorShell.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                var mode = renderer.shadowCastingMode;
                if (mode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;     // never drawn anyway
                Bounds b = renderer.bounds;
                float ext = Mathf.Abs(b.extents.x * outward.x) + Mathf.Abs(b.extents.y * outward.y) + Mathf.Abs(b.extents.z * outward.z);
                bool between = false;
                foreach (int m in group.Members)
                {
                    Vector3 glass = s_WindowPortalOpenings[m].PortalCenter + group.Offset;
                    float c = Vector3.Dot(b.center - glass, outward);
                    if (c - ext > 0.05f || c + ext < -WALL_SHELL_HIDE_DEPTH) continue;
                    if ((b.center - glass).magnitude > WALL_SHELL_HIDE_REACH) continue;
                    between = true; break;
                }
                if (!between) continue;
                group.Hidden.Add(renderer);
                group.HiddenModes.Add(mode);
                // Shadows only keeps its shadow on the ground outside. A piece that casts none is
                // switched off instead: ShadowsOnly would give it one.
                group.HiddenOff.Add(mode == UnityEngine.Rendering.ShadowCastingMode.Off);
            }

            AddViewOutHides(group, instance);
        }

        // The building row's ViewOutHides: world objects by name prefix, near this group's glass, hidden
        // from this camera like the shell pieces above. Searched once, on first draw, and only for a row
        // that asks, so no other building pays for the search.
        private static void AddViewOutHides(WallGroup group, SeamlessInteriorInstance instance)
        {
            if (instance.Config == null) return;
            BuildingWindowOverride row;
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out row) || row.ViewOutHides == null) return;

            Renderer[] all = null;
            foreach (var hide in row.ViewOutHides)
            {
                if (hide == null || hide.RoomWindows == null || hide.NamePrefixes == null) continue;
                bool ours = false;
                foreach (int w in hide.RoomWindows) if (group.Members.Contains(w)) { ours = true; break; }
                if (!ours) continue;

                if (all == null) all = Resources.FindObjectsOfTypeAll<Renderer>();
                for (int i = 0; i < all.Length; i++)
                {
                    var renderer = all[i];
                    if (renderer == null || !renderer.gameObject.activeInHierarchy || group.Hidden.Contains(renderer)) continue;
                    string name = renderer.gameObject.name;
                    bool named = false;
                    foreach (var prefix in hide.NamePrefixes) if (!string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, System.StringComparison.Ordinal)) { named = true; break; }
                    if (!named) continue;
                    bool near = false;
                    foreach (int m in group.Members)
                    {
                        Vector3 glass = s_WindowPortalOpenings[m].GlassCenter + group.Offset;
                        if ((renderer.bounds.ClosestPoint(glass) - glass).magnitude <= hide.WithinMeters) { near = true; break; }
                    }
                    if (!near) continue;
                    var mode = renderer.shadowCastingMode;
                    group.Hidden.Add(renderer);
                    group.HiddenModes.Add(mode);
                    group.HiddenOff.Add(mode == UnityEngine.Rendering.ShadowCastingMode.Off);
                }
            }
        }

        // ─── EVERY FRAME, FROM THE POSTFIX ON vp_FPSCamera.LateUpdate ───

        internal static void TickWallCameras()
        {
            if (!s_WallCamerasActive || s_WallCamerasFaulted || s_WindowPortalCams == null || s_WallGroups.Count == 0) return;
            try { DrawWallCameras(); }
            catch (System.Exception e)
            {
                s_WallCamerasFaulted = true;
                try { StopWallCameras(); } catch { }
                MelonLogger.Warning("[WALL-CAMERA] The wall cameras failed and are off until the game is restarted; the windows show the painted pane: " + e);
            }
        }

        private static void StopWallCameras()
        {
            RestoreWallShell();
            if (s_WindowPortalCams != null)
                foreach (var cam in s_WindowPortalCams) if (cam != null) cam.enabled = false;
            foreach (var quad in s_WindowPortalQuads)
            {
                if (quad == null) continue;
                var r = quad.GetComponent<MeshRenderer>();
                if (r != null) r.enabled = false;
            }
        }

        private static void DrawWallCameras()
        {
            Camera main = GameManager.GetMainCamera();
            if (main == null) return;

            RestoreParkedRoomShadow();      // the backstops; a camera switched off between its callbacks
            RestoreWallShell();

            SeamlessInteriorInstance instance = null;
            if (s_WindowPortalInstanceId != null) ActiveInteriors.TryGetValue(s_WindowPortalInstanceId, out instance);

            Vector3 eye = main.transform.position;
            Matrix4x4 view = main.worldToCameraMatrix, proj = main.projectionMatrix;
            Matrix4x4 vp = proj * view;
            int sw = Screen.width, sh = Screen.height;

            // Which windows are on screen, each group's rectangle round them, and its priority.
            s_WallOrder.Clear();
            for (int g = 0; g < s_WallGroups.Count; g++)
            {
                var group = s_WallGroups[g];
                group.Visible = false; group.Priority = 0f;
                group.X0 = float.MaxValue; group.X1 = float.MinValue; group.Y0 = float.MaxValue; group.Y1 = float.MinValue;
                foreach (int i in group.Members)
                {
                    s_WallMemberOnScreen[i] = false;
                    var opening = s_WindowPortalOpenings[i];
                    Vector3 corner = opening.PortalCenter - opening.Right * (opening.Width * 0.5f) - opening.Up * (opening.Height * 0.5f);
                    float eyeDistance = Vector3.Dot(corner - eye, opening.Outward);
                    if (eyeDistance <= WINDOW_PORTAL_MIN_EYE_DISTANCE) continue;
                    // Leaves the visible part in s_WindowPortalPoly.
                    if (!IsWindowOpeningOnScreen(opening, corner)) continue;
                    s_WallMemberOnScreen[i] = true;
                    group.Visible = true;
                    for (int p = 0; p < s_WindowPortalPoly.Count; p++)
                    {
                        Vector3 q = s_WindowPortalPoly[p];
                        Vector4 clip = vp * new Vector4(q.x, q.y, q.z, 1f);
                        if (clip.w <= 1e-5f) continue;
                        float sx = (clip.x / clip.w * 0.5f + 0.5f) * sw, sy = (clip.y / clip.w * 0.5f + 0.5f) * sh;
                        if (sx < group.X0) group.X0 = sx; if (sx > group.X1) group.X1 = sx;
                        if (sy < group.Y0) group.Y0 = sy; if (sy > group.Y1) group.Y1 = sy;
                    }
                    // The same nearest-and-largest measure as the per-window cameras, summed over the wall.
                    float straight = Mathf.Max((opening.PortalCenter - eye).magnitude, 0.1f);
                    group.Priority += (opening.Width * opening.Height) * (eyeDistance / straight) / (straight * straight);
                }
                if (!group.Visible || group.X1 <= group.X0 || group.Y1 <= group.Y0) { group.Visible = false; continue; }
                if (group.WasDrawn) group.Priority *= WINDOW_PORTAL_HYSTERESIS;
                s_WallOrder.Add(g);
            }

            // Nearest and largest first, as many as there are slots.
            s_WallOrder.Sort((a, b) => s_WallGroups[b].Priority.CompareTo(s_WallGroups[a].Priority));
            int drawCount = Mathf.Min(s_WallOrder.Count, s_WindowPortalSlotCount);

            for (int g = 0; g < s_WallGroups.Count; g++) s_WallGroups[g].WasDrawn = false;
            for (int k = 0; k < s_WallSlotGroup.Length; k++) s_WallSlotGroup[k] = -1;

            // Every window not drawn this frame shows the painted pane: its quad off.
            for (int i = 0; i < s_WindowPortalQuads.Count; i++)
            {
                if (s_WindowPortalQuads[i] == null) continue;
                var r = s_WindowPortalQuads[i].GetComponent<MeshRenderer>();
                if (r != null) r.enabled = false;
            }
            for (int k = drawCount; k < s_WindowPortalCams.Length; k++)
                if (s_WindowPortalCams[k] != null) s_WindowPortalCams[k].enabled = false;

            for (int k = 0; k < drawCount; k++)
            {
                var group = s_WallGroups[s_WallOrder[k]];
                // Won fairly: it keeps defending its slot even if drawing it fails this frame.
                group.WasDrawn = true;
                if (group.Alone)
                {
                    // The slot's camera may last have drawn a wall with a view matrix of its own.
                    var cam = s_WindowPortalCams[k];
                    if (cam != null) cam.ResetWorldToCameraMatrix();
                    DrawOneWindowPortal(group.Members[0], k, main, eye);
                    continue;
                }
                if (group.Hidden == null) FindWallShellPieces(group, instance);
                DrawWallGroup(group, s_WallOrder[k], k, main, eye, view, proj, vp, sw, sh);
            }
        }

        // ─── THE FOG IN A VIEW OUT COUNTS FROM THE WALL, NOT FROM THE PLAYER'S EYE ───
        //
        // The wall camera stands where the player's eye is, which the perspective needs, and Unity's
        // fog grows with depth from the camera, so the room's own air was fogging the view: from
        // 44.7 m back, something 50 m past the glass carried 16% fog instead of 5%, and a blizzard
        // (0.055) turned the glass white (owner, 2026-09-27: "the fog density is including the volume
        // of the room I'm in... not starting at the window"). A room has no fog.
        //
        // WHAT THE FOG READS: F = k (n . P - h), for P in the player's view space: n the group's
        // outward, h the depth along n of the group's INNERMOST glass (all members, on screen or not,
        // so it never jumps), k = 1 / (n . gaze) so the middle of the view is exact, clamped at a
        // cosine of 0.2. A first version counted from the nearest ON-SCREEN member's glass, and the
        // fog jumped as windows left or entered the screen: "The fog now changes with zoom level",
        // "a sharp cuttof ... by panning up and back down". The wall version: "This looks good enough
        // that I can't see any transition" (LOG-2026-09-27 0927-16, 0927-17). What it costs: at a
        // slant, the fog is exact only at the middle of the view, and a group whose windows stand on
        // two planes (the hangar's sides) fogs its outer row by the planes' distance.
        //
        // Until 2026-09-28 this bent the camera's projection so forward shader fog read F; a bent
        // camera is rendered forward by Unity, so it now only returns the depth for the fog pass
        // (WindowViewFog.cs), which subtracts it. A window alone on its own camera needs none of this:
        // that camera's near plane is the window, and the pass already counts from it.
        private const float FOG_FROM_WALL_MIN_COS = 0.2f;

        // Returns the view depth the fog counts from, h / cos (0: from the camera), for the fog pass
        // (WindowViewFog.cs). Never bends the projection: Unity renders a bent camera forward.
        private static float FogFromTheGlass(WallGroup group, Camera main, Vector3 eye)
        {
            Vector3 outward = group.Outward;
            float h = float.MaxValue;
            foreach (int i in group.Members)
            {
                float d = Vector3.Dot(s_WindowPortalOpenings[i].GlassCenter - eye, outward);
                if (d < h) h = d;
            }
            if (h == float.MaxValue || h <= 0f) return 0f;               // standing past the wall
            Vector3 nv = main.worldToCameraMatrix.MultiplyVector(outward); // view space, z toward the viewer
            float cos = -nv.z;                                             // n . gaze
            if (cos <= 0.01f) return 0f;
            return h / Mathf.Max(cos, FOG_FROM_WALL_MIN_COS);
        }

        private static int WallTargetSize(int need, int current)
        {
            if (current >= need && current - need < WALL_TARGET_SLACK) return current;
            return Mathf.Max(WALL_TARGET_STEP, ((need + WALL_TARGET_STEP - 1) / WALL_TARGET_STEP) * WALL_TARGET_STEP);
        }

        // THE WALL CAMERA DRAWS NOTHING NEARER THAN ITS WINDOWS. It stands where the player's eye is, carried
        // over to the building, and a room is deeper than its building: from the back of Thompson's Store the
        // lens stood 11.4 m behind the front wall, out in the yard, and everything between was drawn and slid
        // against the strafe (owner, 2026-10-10: "parts of the exterior shell are still visible zooming by").
        // The hide list cannot cover that: it holds whole pieces of the building's own body within 3 m. So the
        // view starts at the nearest point of the group's panes on screen. Only the near distance changes, in
        // the two terms that carry it, so the projection is not bent. One on and off pair at the store showed
        // it; his look from the back of the room is owed. NOT CHECKED: fog and falling snow in these views.
        internal static bool WallCameraStartsAtItsWindows = true;
        private const float WALL_VIEW_STARTS_BEFORE_THE_PANE = 0.05f;

        private static Matrix4x4 StartingAtTheWindows(WallGroup group, Camera main, Vector3 eye, Matrix4x4 projection, out float startsAt)
        {
            startsAt = main.nearClipPlane;
            if (!WallCameraStartsAtItsWindows) return projection;
            Vector3 gaze = main.transform.forward;
            float nearest = float.MaxValue;
            foreach (int i in group.Members)
            {
                if (i >= s_WallVX.Count || !s_WallMemberOnScreen[i]) continue;
                float[] vx = s_WallVX[i], vy = s_WallVY[i], vz = s_WallVZ[i];
                for (int k = 0; k < vx.Length; k++)
                {
                    float depth = (vx[k] - eye.x) * gaze.x + (vy[k] - eye.y) * gaze.y + (vz[k] - eye.z) * gaze.z;
                    if (depth < nearest) nearest = depth;
                }
            }
            float near = nearest - WALL_VIEW_STARTS_BEFORE_THE_PANE, far = main.farClipPlane;
            if (nearest == float.MaxValue || near <= main.nearClipPlane || far - near < 1f) return projection;
            projection.m22 = -(far + near) / (far - near);
            projection.m23 = -2f * far * near / (far - near);
            startsAt = near;
            return projection;
        }

        private static void DrawWallGroup(WallGroup group, int groupIndex, int slot, Camera main, Vector3 eye,
                                          Matrix4x4 view, Matrix4x4 proj, Matrix4x4 vp, int sw, int sh)
        {
            int px0 = Mathf.Max(0, Mathf.FloorToInt(group.X0) - WALL_SCREEN_MARGIN), py0 = Mathf.Max(0, Mathf.FloorToInt(group.Y0) - WALL_SCREEN_MARGIN);
            int px1 = Mathf.Min(sw, Mathf.CeilToInt(group.X1) + WALL_SCREEN_MARGIN), py1 = Mathf.Min(sh, Mathf.CeilToInt(group.Y1) + WALL_SCREEN_MARGIN);
            if (px1 <= px0 || py1 <= py0) return;

            var current = s_WindowPortalSlotTargets[slot];
            int tw = WallTargetSize(px1 - px0, current != null ? current.width : 0);
            int th = WallTargetSize(py1 - py0, current != null ? current.height : 0);
            if (!EnsureWindowPortalSlot(slot, main, tw, th)) return;
            Camera cam = s_WindowPortalCams[slot];

            // The player's lens, narrowed to the target's pixels on screen: one texel per pixel.
            float xa = px0 / (float)sw * 2f - 1f, xb = (px0 + tw) / (float)sw * 2f - 1f;
            float ya = py0 / (float)sh * 2f - 1f, yb = (py0 + th) / (float)sh * 2f - 1f;
            Matrix4x4 narrow = Matrix4x4.identity;
            narrow.m00 = 2f / (xb - xa); narrow.m03 = -(xa + xb) / (xb - xa);
            narrow.m11 = 2f / (yb - ya); narrow.m13 = -(ya + yb) / (yb - ya);

            Vector3 offset = group.Offset;
            cam.transform.SetPositionAndRotation(eye + offset, main.transform.rotation);
            // LOD from the player's field of view, captured after the zoom (see DrawOneWindowPortal).
            // ORDER MATTERS: these reset an explicit projection matrix, so they go first.
            cam.fieldOfView = s_LateFieldOfView;
            cam.aspect = main.aspect;
            // THE CAMERA IS TOLD WHERE ITS VIEW STARTS. Unity turns the depth buffer back into distance from
            // the camera's own near and far, not from the matrix. With the view starting at the windows and
            // the camera still holding the player's near, the far scenery read as a few meters away and the
            // fog left it (owner, 2026-10-10: "the fog is mostly absent from the far distant scenery. It
            // partially comes back if you get right up to the window", "View angle also changes it").
            float startsAt;
            Matrix4x4 lens = StartingAtTheWindows(group, main, eye, narrow * proj, out startsAt);
            cam.nearClipPlane = startsAt;
            cam.farClipPlane = main.farClipPlane;
            cam.worldToCameraMatrix = view * Matrix4x4.Translate(-offset);
            cam.projectionMatrix = lens;
            float fogStartsAt = ParkedCullEyeInForce ? FogFromTheGlass(group, main, eye) : 0f;
            UseWindowViewFogPass(cam, slot, main, fogStartsAt, true);   // see WindowViewFog.cs
            SharePlayerBuffersWith(cam, slot, main);   // see WindowViewFog.cs
            cam.enabled = true;
            s_WallSlotGroup[slot] = groupIndex;

            // Each visible window's grid: every point's own screen position, in the target's pixels.
            Material material = s_WindowPortalSlotMaterials[slot];
            foreach (int i in group.Members)
            {
                if (!s_WallMemberOnScreen[i] || s_WindowPortalQuads[i] == null) continue;
                var uvs = s_WallUV[i];
                WriteScreenGridUVs(s_WallVX[i], s_WallVY[i], s_WallVZ[i], s_WallUVF[i], uvs, vp, sw, sh, px0, py0, tw, th);
                var quad = s_WindowPortalQuads[i];
                quad.GetComponent<MeshFilter>().sharedMesh.uv = uvs;
                var renderer = quad.GetComponent<MeshRenderer>();
                if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
                renderer.enabled = true;
            }
        }

        // ─── THE SHELL BETWEEN THE LENS AND THE GLASS, KEPT OUT OF EACH WALL CAMERA ───
        //
        // Per camera, in onPreCull and put back in onPostRender, the same way the parked room's shadow
        // is kept out of the views (ParkedRoomShadow.cs, which also explains the two IL2CPP traps).

        private static int WallSlotOf(Camera camera)
        {
            if (camera == null || s_WindowPortalCams == null) return -1;
            int id = camera.GetInstanceID();
            for (int k = 0; k < s_WindowPortalCams.Length; k++)
                if (s_WindowPortalCams[k] != null && s_WindowPortalCams[k].GetInstanceID() == id) return k;
            return -1;
        }

        private static void OnWallShellPreCull(Camera camera)
        {
            try
            {
                if (!s_WallCamerasActive || s_WallShellHiddenGroup >= 0 || s_WallSlotGroup == null) return;
                int slot = WallSlotOf(camera);
                if (slot < 0 || slot >= s_WallSlotGroup.Length) return;
                int g = s_WallSlotGroup[slot];
                if (g < 0 || g >= s_WallGroups.Count || s_WallGroups[g].Hidden == null) return;
                var group = s_WallGroups[g];
                s_WallShellHiddenGroup = g;
                for (int i = 0; i < group.Hidden.Count; i++)
                {
                    var r = group.Hidden[i];
                    if (r == null) continue;
                    if (group.HiddenOff[i]) r.enabled = false;
                    else r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
            }
            catch (System.Exception e) { FaultWallShell(e); }
        }

        private static void OnWallShellPostRender(Camera camera)
        {
            try
            {
                if (s_WallShellHiddenGroup < 0 || WallSlotOf(camera) < 0) return;
                RestoreWallShell();
            }
            catch (System.Exception e) { FaultWallShell(e); }
        }

        private static void RestoreWallShell()
        {
            int g = s_WallShellHiddenGroup;
            if (g < 0) return;
            s_WallShellHiddenGroup = -1;
            if (g >= s_WallGroups.Count || s_WallGroups[g].Hidden == null) return;
            var group = s_WallGroups[g];
            for (int i = 0; i < group.Hidden.Count; i++)
            {
                var r = group.Hidden[i];
                if (r == null) continue;
                if (group.HiddenOff[i]) r.enabled = true;
                else r.shadowCastingMode = group.HiddenModes[i];
            }
        }

        private static void FaultWallShell(System.Exception e)
        {
            if (s_WallCamerasFaulted) return;
            s_WallCamerasFaulted = true;
            try { StopWallCameras(); UnhookWallShell(); } catch { }
            MelonLogger.Warning("[WALL-CAMERA] Keeping the building out of the wall cameras failed; the wall cameras are off until the game is restarted: " + e.Message);
        }

        private static void UnhookWallShell()
        {
            if (!s_WallShellHooked) return;
            Camera.onPreCull -= s_WallShellPreCull;
            Camera.onPostRender -= s_WallShellPostRender;
            s_WallShellHooked = false;
        }

        private static void TeardownWallCameras()
        {
            try { RestoreWallShell(); } catch { }
            try { UnhookWallShell(); } catch { }
            s_WallGroups.Clear();
            s_WallSlotGroup = null;
            s_WallVX.Clear(); s_WallVY.Clear(); s_WallVZ.Clear(); s_WallUV.Clear(); s_WallUVF.Clear();
            s_WallMemberOnScreen.Clear();
            s_WallCamerasActive = false;
        }
    }
}
