using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── OUTDOOR SNOW THAT TOUCHES A WINDOW, SEEN FROM INSIDE ───
        //
        // The building-to-room half of the window snow; WindowSnow.RoomToBuilding.cs is the other
        // direction (a room's frame snow carried out onto the building, for the views in).
        //
        // A view out starts at the glass. Any outdoor surface that crosses the glass (a ground drift
        // piled against the wall, the building's own snow on a sill) is then seen from INSIDE the
        // solid: its back faces are culled and it shows as floating shards (owner, 2026-09-28, the
        // Quonset gas station: "the inside-out ledge snow ... doesn't have a back face", then "It's the
        // ground! This window has a giant ground snow drift against it"). Two pieces, both prototyped
        // in the console with the owner's eye (evidence LOG-2026-09-28 0928-26):
        //
        //   THE FILL, in the building, seen by the window cameras: a flat piece of snow on the glass,
        //   facing the room, in the exact outline of where outdoor snow meets the glass. A point of the
        //   glass is in snow when it is below the terrain, or when the first snow surface straight out
        //   from it is a BACK face, which is the renderer's own rule for "looking out from inside".
        //   Traced on a grid, outline refined by bisection, one mesh with shared vertices (no seams),
        //   the building's snow material at its measured texel scale. 0.5 mm outside the glass: at
        //   0.1 mm it flickered in vertical bars (float precision some 800 m from the world origin);
        //   2 cm of padding upward only, so the top overlaps where the snow meets the glass (sideways
        //   padding carried the middle's height to the ends and flattened the contour); 15 cm past
        //   the frame so no crack shows. Owner: "Good enough for now."
        //
        //   THE DRIFT'S BACK SIDE, in the room, seen by the player: the building's snow within 35 cm
        //   outside the glass, mirrored through it (the Camp Office's lifted snow, backwards) and
        //   squashed to 3.5% of its depth and set just outside the room's glass (DriftDistanceOutside), so it reads as the back of the drift pressed on the window.
        //   Owner: "It needs to be flattened so it's just the backside of the exterior drift"; 0.04
        //   from his slider (0.05 z-fought), then 0.035 with the clamp when it clipped the pane.
        //
        // Built once per room, one window per frame from a queue, and removed with the views.
        // Windows with nothing against the glass cost one quick check.
        //
        // ONLY FOR A BUILDING WHOSE ROW ASKS FOR IT (BuildingWindowOverride.GlassSnow), since 2026-10-02:
        // the Quonset gas station, the one building found with the ground piled against a window. Owner:
        // "it should only be working at the Quonset garage", the rest kept for another building like it.
        // This switch stays the master: off, no building gets it.
        public static bool GlassSnow = true;

        private const float GLASS_SNOW_GAP = 0.0005f;
        private const float GLASS_SNOW_PAD_UP = 0.02f;
        private const float GLASS_SNOW_MARGIN = 0.15f;
        private const float GLASS_SNOW_REACH = 1.5f;
        private const float GLASS_SNOW_DRIFT_DEPTH = 0.35f;
        private const float GLASS_SNOW_DRIFT_SQUASH = 0.035f;
        // Where a drift seen from inside sits: OUTSIDE the room's glass, where snow on a window really is,
        // from half a millimetre out to 2.5 mm out, and never nearer than half a millimetre to the plane the
        // view starts on. Measured 2026-09-28: the Quonset's view starts 2.5 mm outside its glass, the Camp
        // Office's 9.3 cm. The first placement, on the view's start plane, stood the Camp Office's drifts
        // "outside the frame"; the second, on the glass from 0.5 mm out to 2 mm in (the owner's value at
        // the Camp Office), put the Quonset's "inside past the glass". Outside the glass fits both.
        private const float GLASS_SNOW_DRIFT_NEAREST = 0.0005f, GLASS_SNOW_DRIFT_FURTHEST = 0.0025f, GLASS_SNOW_DRIFT_BEFORE_VIEW = 0.0005f;
        private const float GLASS_SNOW_UV_PER_METRE_FALLBACK = 0.264f;
        private const int GLASS_SNOW_GRID_X = 64, GLASS_SNOW_GRID_Y = 32, GLASS_SNOW_BUCKETS_X = 16, GLASS_SNOW_BUCKETS_Y = 8;

        private class GlassSnowSource
        {
            public Material Material;
            public Vector3[] A, B, C;          // world corners
            public Vector3[] NA, NB, NC;       // world normals
            public Vector2[] UA, UB, UC;       // texture coordinates
            public float UvPerMetre;
        }

        private static readonly List<int> s_GlassSnowQueue = new List<int>();
        private static readonly List<GameObject> s_GlassSnowMade = new List<GameObject>();
        private static List<GlassSnowSource> s_GlassSnowSources;
        private static string s_GlassSnowFor;
        private static int s_GlassSnowFills, s_GlassSnowDrifts;

        // The building's snow meshes, read once per room: every readable mesh with a snow material
        // near the shell, outside the room, not the mod's own.
        private static void QueueGlassSnow(SeamlessInteriorInstance instance, string id)
        {
            TeardownGlassSnow();
            if (!GlassSnow || instance == null || instance.ExteriorShell == null || instance.Config == null) return;
            BuildingWindowOverride glassSnowRow;
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out glassSnowRow) || !glassSnowRow.GlassSnow) return;

            s_GlassSnowSources = new List<GlassSnowSource>();
            Bounds area = new Bounds(ShellPosition(instance), Vector3.zero);
            // Mesh renderers only. The Community Hall's shell carries eight particle effects (smoke, snow
            // sheets) whose bounds stand at the world origin while idle, which stretched this box to
            // 2.4 x 2.3 km, half of Pleasant Valley: 712 snow meshes read in one frame, a 738 ms freeze on
            // every entry (2026-10-02). Its meshes alone give 22 x 21 m.
            foreach (var r in instance.ExteriorShell.GetComponentsInChildren<MeshRenderer>(true)) area.Encapsulate(r.bounds);
            area.Expand(8f);
            Transform room = instance.MasterInterior != null ? instance.MasterInterior.transform : null;

            // Switched off or inactive counts too: at setup the mod may still be holding parts of the
            // building off (the Quonset's own snow was missed that way on the first run, 2026-09-28),
            // and what matters here is where the snow is, not whether it is drawn this frame.
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r == null || !area.Intersects(r.bounds)) continue;
                string name = r.gameObject.name;
                if (name.StartsWith("SeamlessInteriors_", System.StringComparison.Ordinal) || name.StartsWith("SI_", System.StringComparison.Ordinal)) continue;
                if (name.Contains("LOD1") || name.Contains("LOD2")) continue;
                if (room != null && r.transform.IsChildOf(room)) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;

                Mesh mesh = filter.sharedMesh;
                Material[] materials = r.sharedMaterials;
                Vector3[] v = mesh.vertices; Vector3[] n = mesh.normals; Vector2[] uv = mesh.uv;
                bool hasN = n != null && n.Length == v.Length, hasUv = uv != null && uv.Length == v.Length;
                Matrix4x4 l2w = r.transform.localToWorldMatrix;
                for (int sm = 0; sm < mesh.subMeshCount && sm < materials.Length; sm++)
                {
                    if (materials[sm] == null || materials[sm].name.IndexOf("Snow", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int[] t = mesh.GetTriangles(sm);
                    int count = t.Length / 3;
                    if (count == 0) continue;
                    var s = new GlassSnowSource
                    {
                        Material = materials[sm],
                        A = new Vector3[count], B = new Vector3[count], C = new Vector3[count],
                        NA = new Vector3[count], NB = new Vector3[count], NC = new Vector3[count],
                        UA = new Vector2[count], UB = new Vector2[count], UC = new Vector2[count],
                    };
                    var densities = new List<float>();
                    for (int k = 0; k < count; k++)
                    {
                        int i0 = t[k * 3], i1 = t[k * 3 + 1], i2 = t[k * 3 + 2];
                        s.A[k] = l2w.MultiplyPoint3x4(v[i0]); s.B[k] = l2w.MultiplyPoint3x4(v[i1]); s.C[k] = l2w.MultiplyPoint3x4(v[i2]);
                        s.NA[k] = hasN ? l2w.MultiplyVector(n[i0]).normalized : Vector3.up;
                        s.NB[k] = hasN ? l2w.MultiplyVector(n[i1]).normalized : Vector3.up;
                        s.NC[k] = hasN ? l2w.MultiplyVector(n[i2]).normalized : Vector3.up;
                        if (hasUv)
                        {
                            s.UA[k] = uv[i0]; s.UB[k] = uv[i1]; s.UC[k] = uv[i2];
                            float edge = (s.B[k] - s.A[k]).magnitude;
                            if (edge > 0.05f && densities.Count < 400) densities.Add((uv[i1] - uv[i0]).magnitude / edge);
                        }
                    }
                    densities.Sort();
                    s.UvPerMetre = densities.Count > 0 ? densities[densities.Count / 2] : GLASS_SNOW_UV_PER_METRE_FALLBACK;
                    s_GlassSnowSources.Add(s);
                }
            }

            for (int i = 0; i < s_WindowPortalOpenings.Count; i++) s_GlassSnowQueue.Add(i);
            FlattenRoomWindowDrifts();
            s_GlassSnowFor = id;
            s_GlassSnowFills = 0; s_GlassSnowDrifts = 0;
        }

        // One window a frame, from TickWindowPortals.
        private static void TickGlassSnow()
        {
            if (s_GlassSnowQueue.Count == 0 || s_GlassSnowSources == null) return;
            if (!GlassSnow) { TeardownGlassSnow(); return; }
            int i = s_GlassSnowQueue[s_GlassSnowQueue.Count - 1];
            s_GlassSnowQueue.RemoveAt(s_GlassSnowQueue.Count - 1);
            try { BuildGlassSnow(i); }
            catch (System.Exception e) { MelonLogger.Warning("[GLASS-SNOW] Window " + i + " was skipped: " + e.Message); }

            if (s_GlassSnowQueue.Count == 0 && (s_GlassSnowFills > 0 || s_GlassSnowDrifts > 0))
                MelonLogger.Msg($"[GLASS-SNOW] {s_GlassSnowFor}: snow against the glass on {s_GlassSnowFills} window(s), a drift's back side on {s_GlassSnowDrifts}, of {s_WindowPortalOpenings.Count}.");
        }

        private static void TeardownGlassSnow()
        {
            for (int i = 0; i < s_GlassSnowMade.Count; i++)
                if (s_GlassSnowMade[i] != null)
                {
                    var filter = s_GlassSnowMade[i].GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
                    Object.Destroy(s_GlassSnowMade[i]);
                }
            s_GlassSnowMade.Clear();
            RestoreRoomWindowDrifts();
            s_GlassSnowQueue.Clear();
            s_GlassSnowSources = null;
            s_GlassSnowFor = null;
        }

        // The window's glass in the building: the room opening carried by the same offset its view uses.
        private static void BuildGlassSnow(int i)
        {
            if (i >= s_WindowPortalOpenings.Count || i >= s_WindowPortalDelta.Count) return;
            var opening = s_WindowPortalOpenings[i];
            float viewStart = i < s_WindowPortalViewStart.Count ? s_WindowPortalViewStart[i] : WINDOW_PORTAL_VIEW_STARTS_OUTSIDE_SHELL_GLASS;
            Vector3 toBuilding = s_WindowPortalDelta[i] + opening.Outward * viewStart;
            Vector3 ax = opening.Right, ay = opening.Up, outward = opening.Outward;
            float w = opening.Width, h = opening.Height;
            Vector3 g00 = opening.PortalCenter + toBuilding - ax * (w * 0.5f) - ay * (h * 0.5f);

            var fill = new GlassSnowFill(g00, ax, ay, outward, w, h, s_GlassSnowSources, GlassSnowDriftBuilt(g00, ax, ay, outward, w, h));
            if (fill.Build(out Mesh fillMesh, out Material fillMaterial))
            {
                var go = new GameObject("SeamlessInteriors_GlassSnowFill_" + i);
                go.AddComponent<MeshFilter>().sharedMesh = fillMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = fillMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                s_GlassSnowMade.Add(go);
                s_GlassSnowFills++;
            }

            // Anchored to the room's GLASS, not to the plane the view starts on: at the Camp Office that
            // plane is 9.3 cm outside the glass and a drift placed on it stood out in the frame. Clamped
            // to 2 mm in, so it never reaches the pane's far side and its room-facing side is a true plane
            // (owner, 2026-09-28: "a slight bit of z-clip with the window pane").
            float portalToGlass = Vector3.Dot(opening.PortalCenter - opening.GlassCenter, outward);
            Mesh driftMesh; Material driftMaterial;
            if (BuildGlassSnowDrift(g00, ax, ay, outward, w, h, -toBuilding - outward * portalToGlass, portalToGlass, out driftMesh, out driftMaterial))
            {
                var go = new GameObject("SeamlessInteriors_GlassSnowDrift_" + i);
                go.AddComponent<MeshFilter>().sharedMesh = driftMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = driftMaterial;
                s_GlassSnowMade.Add(go);
                s_GlassSnowDrifts++;
            }
        }

        // Which snow the drift's back side takes: on this window's sill, within reach outside the glass.
        // The fill skips exactly these, so the two never draw the same snow twice (owner, 2026-09-28:
        // "both the ground snow detection and the window drift detection are competing").
        private static bool IsGlassSnowDriftTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 g00, Vector3 ax, Vector3 ay, Vector3 outward, float w, float h)
        {
            Vector3 centre = (a + b + c) / 3f;
            float depth = Vector3.Dot(centre - g00, outward);
            if (depth < -0.02f || depth > GLASS_SNOW_DRIFT_DEPTH) return false;
            float x = Vector3.Dot(centre - g00, ax), y = Vector3.Dot(centre - g00, ay);
            return x >= -0.1f && x <= w + 0.1f && y >= -0.3f && y <= h;
        }

        // The building's snow on this window, mirrored through the glass into the room and squashed.
        private static bool BuildGlassSnowDrift(Vector3 g00, Vector3 ax, Vector3 ay, Vector3 outward, float w, float h, Vector3 toRoomGlass, float viewStartsOutside, out Mesh mesh, out Material material)
        {
            mesh = null; material = null;
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            foreach (var s in s_GlassSnowSources)
            {
                for (int k = 0; k < s.A.Length; k++)
                {
                    if (!IsGlassSnowDriftTriangle(s.A[k], s.B[k], s.C[k], g00, ax, ay, outward, w, h)) continue;
                    if (material == null) material = s.Material;
                    if (s.Material != material) continue;   // one material per mesh
                    int b = verts.Count;
                    AddMirrored(s.A[k], s.NA[k], s.UA[k]); AddMirrored(s.B[k], s.NB[k], s.UB[k]); AddMirrored(s.C[k], s.NC[k], s.UC[k]);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);   // mirrored, so the winding turns round
                }
            }
            if (tris.Count == 0) return false;

            mesh = new Mesh();
            mesh.vertices = verts.ToArray(); mesh.normals = normals.ToArray(); mesh.uv = uvs.ToArray(); mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return true;

            void AddMirrored(Vector3 p, Vector3 n, Vector2 uv)
            {
                float d = Vector3.Dot(p - g00, outward);
                verts.Add(p - outward * d + outward * DriftDistanceOutside(d, viewStartsOutside) + toRoomGlass);
                normals.Add(n - outward * (2f * Vector3.Dot(n, outward)));
                uvs.Add(uv);
            }
        }

        // Where a point of a drift is shown, as a distance OUTSIDE the room's glass: its own depth (outside
        // the glass for the building's snow, inside for the room's own) squashed, starting half a millimetre
        // out, and clamped before the furthest plane and before the plane the view starts on.
        // A window with no glass has one made up from its frame or painted sheet, and some start their view
        // right on it (owner, 2026-09-28: "I thought some buildings don't have actual glass?"). So the band
        // hugs the glass where there is room before the view, and otherwise sits just on the room's side of
        // the view: the Quonset (view 2.5 mm out) 0 to 2 mm out, the Camp Office (9.3 cm) 0.5 to 2.5 mm
        // out, a window whose view starts at its glass 2.5 to 0.5 mm in.
        private static float DriftDistanceOutside(float depth, float viewStartsOutside)
        {
            float furthest = Mathf.Min(GLASS_SNOW_DRIFT_FURTHEST, viewStartsOutside - GLASS_SNOW_DRIFT_BEFORE_VIEW);
            float nearest = furthest - (GLASS_SNOW_DRIFT_FURTHEST - GLASS_SNOW_DRIFT_NEAREST);
            return Mathf.Clamp(nearest + Mathf.Max(depth, 0f) * GLASS_SNOW_DRIFT_SQUASH, nearest, furthest);
        }

        // ─── AND THE ROOM'S OWN WINDOW DRIFTS, PRESSED FLAT THE SAME WAY ───
        //
        // Some rooms model a drift on the inside sill (the Camp Office's OBJ_LakeCabinWindowSnowA to C,
        // 6 cm deep). Owner, 2026-09-28: "Can the window drifts here get the same treatment so they're
        // flat against the glass?" Each gets a flattened copy of its mesh while the views out are up,
        // and its own mesh back when they come down.
        private static readonly List<MeshFilter> s_FlattenedRoomDrifts = new List<MeshFilter>();
        private static readonly List<Mesh> s_FlattenedRoomDriftMeshes = new List<Mesh>();

        private static void FlattenRoomWindowDrifts()
        {
            for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
            {
                var opening = s_WindowPortalOpenings[i];
                if (opening.PaneRoot == null) continue;
                float viewStartsOutside = Vector3.Dot(opening.PortalCenter - opening.GlassCenter, opening.Outward);
                foreach (var r in opening.PaneRoot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r == null || r.gameObject.name.IndexOf("WindowSnow", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable || s_FlattenedRoomDrifts.Contains(filter)) continue;
                    Mesh original = filter.sharedMesh;
                    Mesh flat = Object.Instantiate(original);
                    Vector3[] v = flat.vertices;
                    Matrix4x4 l2w = r.transform.localToWorldMatrix, w2l = r.transform.worldToLocalMatrix;
                    for (int k = 0; k < v.Length; k++)
                    {
                        Vector3 p = l2w.MultiplyPoint3x4(v[k]);
                        float depth = Vector3.Dot(opening.GlassCenter - p, opening.Outward);   // into the room from the glass
                        v[k] = w2l.MultiplyPoint3x4(p + opening.Outward * (depth + DriftDistanceOutside(depth, viewStartsOutside)));
                    }
                    flat.vertices = v;
                    flat.RecalculateBounds();
                    s_FlattenedRoomDrifts.Add(filter);
                    s_FlattenedRoomDriftMeshes.Add(original);
                    filter.sharedMesh = flat;
                }
            }
        }

        private static void RestoreRoomWindowDrifts()
        {
            for (int i = 0; i < s_FlattenedRoomDrifts.Count; i++)
            {
                var filter = s_FlattenedRoomDrifts[i];
                if (filter == null) continue;
                Mesh flat = filter.sharedMesh;
                filter.sharedMesh = s_FlattenedRoomDriftMeshes[i];
                if (flat != null && flat != s_FlattenedRoomDriftMeshes[i]) Object.Destroy(flat);
            }
            s_FlattenedRoomDrifts.Clear();
            s_FlattenedRoomDriftMeshes.Clear();
        }

        // Whether this window gets a drift's back side at all (only then does the fill leave its snow alone).
        private static bool GlassSnowDriftBuilt(Vector3 g00, Vector3 ax, Vector3 ay, Vector3 outward, float w, float h)
        {
            foreach (var s in s_GlassSnowSources)
                for (int k = 0; k < s.A.Length; k++)
                    if (IsGlassSnowDriftTriangle(s.A[k], s.B[k], s.C[k], g00, ax, ay, outward, w, h)) return true;
            return false;
        }

        // The flat snow on the glass: marching squares over the glass's own rectangle.
        private class GlassSnowFill
        {
            private readonly Vector3 m_P0, m_Ax, m_Ay, m_Out;
            private readonly float m_W, m_H;
            private readonly List<GlassSnowSource> m_Sources;
            private readonly List<Vector3> m_A = new List<Vector3>(), m_B = new List<Vector3>(), m_C = new List<Vector3>();
            private readonly List<int>[,] m_Buckets = new List<int>[GLASS_SNOW_BUCKETS_X, GLASS_SNOW_BUCKETS_Y];
            private readonly Terrain[] m_Terrains = Terrain.activeTerrains;
            private Material m_Material;
            private float m_UvPerMetre = GLASS_SNOW_UV_PER_METRE_FALLBACK;

            private readonly Vector3 m_G00; private readonly float m_GlassW, m_GlassH; private readonly bool m_SkipDrift;

            public GlassSnowFill(Vector3 g00, Vector3 ax, Vector3 ay, Vector3 outward, float w, float h, List<GlassSnowSource> sources, bool skipDrift)
            {
                m_Ax = ax; m_Ay = ay; m_Out = outward; m_Sources = sources;
                m_G00 = g00; m_GlassW = w; m_GlassH = h; m_SkipDrift = skipDrift;
                m_P0 = g00 - ax * GLASS_SNOW_MARGIN - ay * GLASS_SNOW_MARGIN;
                m_W = w + 2f * GLASS_SNOW_MARGIN; m_H = h + 2f * GLASS_SNOW_MARGIN;
            }

            public bool Build(out Mesh mesh, out Material material)
            {
                mesh = null; material = null;
                Gather();

                const int gx = GLASS_SNOW_GRID_X, gy = GLASS_SNOW_GRID_Y;
                var inside = new bool[gx + 1, gy + 1];
                int count = 0;
                for (int i = 0; i <= gx; i++)
                    for (int j = 0; j <= gy; j++)
                        if (inside[i, j] = Inside(P(m_W * i / gx, m_H * j / gy))) count++;
                if (count == 0) return false;
                if (m_Material == null) m_Material = FirstSnowMaterial();
                if (m_Material == null) return false;

                var verts = new List<Vector3>(); var keys = new Dictionary<long, int>(); var tris = new List<int>();
                Vector3 off = m_Out * GLASS_SNOW_GAP;
                int Corner(int i, int j)
                {
                    long key = ((long)i * 100000 + j) * 3;
                    if (!keys.TryGetValue(key, out int idx)) { idx = verts.Count; verts.Add(P(m_W * i / gx, m_H * j / gy) + off); keys[key] = idx; }
                    return idx;
                }
                int Edge(int i, int j, int i2, int j2)
                {
                    long key = ((long)i * 100000 + j) * 3 + (i2 != i ? 1 : 2);
                    if (keys.TryGetValue(key, out int idx)) return idx;
                    float x0 = m_W * i / gx, y0 = m_H * j / gy, x1 = m_W * i2 / gx, y1 = m_H * j2 / gy;
                    bool s0 = inside[i, j]; float lo = 0f, hi = 1f;
                    for (int it = 0; it < 10; it++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Inside(P(x0 + (x1 - x0) * mid, y0 + (y1 - y0) * mid)) == s0) lo = mid; else hi = mid;
                    }
                    float f = (lo + hi) * 0.5f;
                    idx = verts.Count; verts.Add(P(x0 + (x1 - x0) * f, y0 + (y1 - y0) * f) + off); keys[key] = idx;
                    return idx;
                }
                int[] ci = new int[4], cj = new int[4];
                var poly = new List<int>(8);
                for (int i = 0; i < gx; i++)
                    for (int j = 0; j < gy; j++)
                    {
                        ci[0] = i; ci[1] = i + 1; ci[2] = i + 1; ci[3] = i;
                        cj[0] = j; cj[1] = j; cj[2] = j + 1; cj[3] = j + 1;
                        poly.Clear();
                        for (int c = 0; c < 4; c++)
                        {
                            int a = c, b = (c + 1) % 4;
                            if (inside[ci[a], cj[a]]) poly.Add(Corner(ci[a], cj[a]));
                            if (inside[ci[a], cj[a]] != inside[ci[b], cj[b]])
                            {
                                int ei = System.Math.Min(ci[a], ci[b]), ej = System.Math.Min(cj[a], cj[b]);
                                poly.Add(Edge(ei, ej, ci[a] == ci[b] ? ei : ei + 1, cj[a] == cj[b] ? ej : ej + 1));
                            }
                        }
                        for (int k = 1; k + 1 < poly.Count; k++) { tris.Add(poly[0]); tris.Add(poly[k]); tris.Add(poly[k + 1]); }
                    }
                if (tris.Count < 3) return false;

                // Facing the room.
                Vector3 fn = Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]);
                if (Vector3.Dot(fn, -m_Out) < 0f)
                    for (int k = 0; k < tris.Count; k += 3) { int tmp = tris[k + 1]; tris[k + 1] = tris[k + 2]; tris[k + 2] = tmp; }

                var uvs = new List<Vector2>(verts.Count); var normals = new List<Vector3>(verts.Count);
                for (int k = 0; k < verts.Count; k++)
                {
                    Vector3 d = verts[k] - m_P0;
                    uvs.Add(new Vector2(Vector3.Dot(d, m_Ax), Vector3.Dot(d, m_Ay)) * m_UvPerMetre);
                    normals.Add(-m_Out);
                }
                mesh = new Mesh();
                mesh.vertices = verts.ToArray(); mesh.normals = normals.ToArray(); mesh.uv = uvs.ToArray(); mesh.triangles = tris.ToArray();
                mesh.RecalculateBounds();
                material = m_Material;
                return true;
            }

            private Vector3 P(float x, float y) { return m_P0 + m_Ax * x + m_Ay * y; }

            // Triangles in front of the glass within reach, bucketed by where they fall on it.
            private void Gather()
            {
                for (int i = 0; i < GLASS_SNOW_BUCKETS_X; i++)
                    for (int j = 0; j < GLASS_SNOW_BUCKETS_Y; j++) m_Buckets[i, j] = new List<int>();
                foreach (var s in m_Sources)
                    for (int k = 0; k < s.A.Length; k++)
                    {
                        Vector3 a = s.A[k], b = s.B[k], c = s.C[k];
                        if (m_SkipDrift && IsGlassSnowDriftTriangle(a, b, c, m_G00, m_Ax, m_Ay, m_Out, m_GlassW, m_GlassH)) continue;
                        float da = Vector3.Dot(a - m_P0, m_Out), db = Vector3.Dot(b - m_P0, m_Out), dc = Vector3.Dot(c - m_P0, m_Out);
                        if (Mathf.Max(da, Mathf.Max(db, dc)) < 0f || Mathf.Min(da, Mathf.Min(db, dc)) > GLASS_SNOW_REACH) continue;
                        float xa = Vector3.Dot(a - m_P0, m_Ax), xb = Vector3.Dot(b - m_P0, m_Ax), xc = Vector3.Dot(c - m_P0, m_Ax);
                        float ya = Vector3.Dot(a - m_P0, m_Ay), yb = Vector3.Dot(b - m_P0, m_Ay), yc = Vector3.Dot(c - m_P0, m_Ay);
                        int x0 = Mathf.FloorToInt(Mathf.Min(xa, Mathf.Min(xb, xc)) / m_W * GLASS_SNOW_BUCKETS_X);
                        int x1 = Mathf.FloorToInt(Mathf.Max(xa, Mathf.Max(xb, xc)) / m_W * GLASS_SNOW_BUCKETS_X);
                        int y0 = Mathf.FloorToInt(Mathf.Min(ya, Mathf.Min(yb, yc)) / m_H * GLASS_SNOW_BUCKETS_Y);
                        int y1 = Mathf.FloorToInt(Mathf.Max(ya, Mathf.Max(yb, yc)) / m_H * GLASS_SNOW_BUCKETS_Y);
                        if (x1 < 0 || y1 < 0 || x0 >= GLASS_SNOW_BUCKETS_X || y0 >= GLASS_SNOW_BUCKETS_Y) continue;
                        if (m_Material == null) { m_Material = s.Material; m_UvPerMetre = s.UvPerMetre; }
                        int idx = m_A.Count; m_A.Add(a); m_B.Add(b); m_C.Add(c);
                        for (int i = System.Math.Max(0, x0); i <= System.Math.Min(GLASS_SNOW_BUCKETS_X - 1, x1); i++)
                            for (int j = System.Math.Max(0, y0); j <= System.Math.Min(GLASS_SNOW_BUCKETS_Y - 1, y1); j++) m_Buckets[i, j].Add(idx);
                    }
            }

            private Material FirstSnowMaterial()
            {
                foreach (var s in m_Sources) if (s.Material != null) { m_UvPerMetre = s.UvPerMetre; return s.Material; }
                return null;
            }

            private bool Inside(Vector3 g)
            {
                return Raw(g) || Raw(g - m_Ay * GLASS_SNOW_PAD_UP);
            }

            // Below the terrain, or the first snow surface straight out is a back face.
            private bool Raw(Vector3 g)
            {
                Vector3 p = g + m_Out * 0.001f;
                foreach (var tr in m_Terrains)
                {
                    if (tr == null || tr.terrainData == null) continue;
                    Vector3 tp = tr.transform.position, size = tr.terrainData.size;
                    if (p.x < tp.x || p.z < tp.z || p.x > tp.x + size.x || p.z > tp.z + size.z) continue;
                    if (p.y < tr.SampleHeight(p) + tp.y) return true;
                }
                Vector3 d = g - m_P0;
                int bx = (int)(Vector3.Dot(d, m_Ax) / m_W * GLASS_SNOW_BUCKETS_X), by = (int)(Vector3.Dot(d, m_Ay) / m_H * GLASS_SNOW_BUCKETS_Y);
                if (bx < 0 || by < 0 || bx >= GLASS_SNOW_BUCKETS_X || by >= GLASS_SNOW_BUCKETS_Y) return false;
                float best = GLASS_SNOW_REACH; bool back = false;
                foreach (int k in m_Buckets[bx, by])
                {
                    Vector3 a = m_A[k], e1 = m_B[k] - a, e2 = m_C[k] - a;
                    Vector3 pv = Vector3.Cross(m_Out, e2);
                    float det = Vector3.Dot(e1, pv);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    float inv = 1f / det; Vector3 tv = p - a;
                    float u = Vector3.Dot(tv, pv) * inv; if (u < 0f || u > 1f) continue;
                    Vector3 qv = Vector3.Cross(tv, e1);
                    float v = Vector3.Dot(m_Out, qv) * inv; if (v < 0f || u + v > 1f) continue;
                    float t = Vector3.Dot(e2, qv) * inv; if (t <= 0f || t >= best) continue;
                    best = t; back = Vector3.Dot(Vector3.Cross(e1, e2), m_Out) > 0f;
                }
                return back;
            }
        }
    }
}
