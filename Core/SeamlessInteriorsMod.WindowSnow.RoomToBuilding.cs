using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── WINDOW SNOW THAT IS PART OF THE FRAME'S MESH, LIFTED OUT AND SET ON THE OUTSIDE ───
        //
        // The room-to-building half of the window snow; WindowSnow.BuildingToRoom.cs is the other
        // direction (the building's snow carried in, and outdoor snow against the glass, for the views out).
        //
        // The drift outside a window is a copy of the snow inside it, and the mod knew that snow
        // only as an OBJECT named '...WindowSnow...'. Safe House A has none, so its windows got no
        // drifts and the log said "frost and snow set up" all the same. The owner, standing in the
        // room on 2026-09-19: "every window has snow drifts." They are a SUBMESH of each frame mesh
        // (Obj_WindowCabinAMain: 146 triangles of wood, 132 of material Obj_WindowCabinSnowA). The
        // probe that had reported "this room has none" counted objects.
        //
        // Such snow comes in up to TWO PARTS, told apart by the gap in height between them: a drift
        // on the sill, and snow that WRAPS the horizontal bar. They cannot be carried as one piece,
        // because the room's bar and the building's are not at the same height, and neither are
        // their depths. What follows is from the owner's sliders on all seven Safe House A windows
        // (_dev/ROADMAP.md 6zzzn, 6zzzo); each rule landed within 3 cm of his eye:
        //
        //   THE SILL PART sits on the sill as the copied slabs do, and is slid out until its front
        //   edge stands just behind the front of the building's sill ledge.
        //
        //   THE BAR PART is seated BAR TOP TO BAR TOP: the room's bar top, which lies inside the
        //   snow that wraps it, is carried onto the building's. Its BOTTOM edge is not a resting
        //   edge (the first live attempt seated it by that and he said "You went the wrong way").
        //   It is built only where the building's pane HAS a bar (Safe House A's upper small pane
        //   has none). In depth it is squashed to stand between the glass and the front face of
        //   the building's bar: that bar stands 5.5 cm out on one window and 1.2 to 1.6 on the
        //   rest, where the snow is 4.8 cm deep, and he thinned it to a skin at the bar's front
        //   rather than let it overhang ("doesn't seem to stick out enough").
        //
        // Built in world space on an identity transform, like the frost overlays, MIRRORED through
        // the glass so the faces modeled for the room look outward, winding reversed to match.
        private const float LIFTED_SNOW_PART_GAP = 0.05f;
        private const float LIFTED_SNOW_BEHIND_LEDGE_FRONT = 0.005f;
        private const float LIFTED_SNOW_BEHIND_BAR_FRONT = 0.0015f;
        private const float LIFTED_SNOW_PROUD_OF_GLASS = 0.002f;

        private static GameObject BuildLiftedWindowSnow(int a, ShellAperture pane, Vector4 glass, Vector3[] shellWorld, int[] shellTriangles, out bool hasBarPart)
        {
            hasBarPart = false;
            if (a >= s_OutsideInApertureOpenings.Count || shellWorld == null || shellTriangles == null) return null;
            var opening = s_OutsideInApertureOpenings[a];
            if (opening.PaneRoot == null) return null;

            // The frame that carries a snow submesh.
            MeshFilter frame = null; MeshRenderer frameRenderer = null; int snowSubmesh = -1;
            var renderers = opening.PaneRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length && frame == null; i++)
            {
                var r = renderers[i];
                if (r == null || r.gameObject.name.StartsWith("SeamlessInteriors_", System.StringComparison.Ordinal)) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                Material[] materials = r.sharedMaterials;
                if (materials.Length < 2) continue;
                for (int k = 0; k < materials.Length && k < filter.sharedMesh.subMeshCount; k++)
                {
                    if (materials[k] == null || materials[k].name.IndexOf("Snow", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    frame = filter; frameRenderer = r; snowSubmesh = k;
                    break;
                }
            }
            if (frame == null) return null;

            Mesh source = frame.sharedMesh;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uvs = source.uv;
            int[] snowTriangles = source.GetTriangles(snowSubmesh);
            if (snowTriangles == null || snowTriangles.Length < 3) return null;

            // The ROOM's frame for this window: level and upright whatever the building does.
            Vector3 roomOrigin = s_OutsideInApertureGlass[a];
            Vector3 roomRight = pane.RoomRight, roomUp = Vector3.up, roomOut = pane.RoomOutward;
            Transform frameT = frame.transform;

            // Only the snow's own corners, in the room's window frame (across, up, out of the building).
            var indexOf = new Dictionary<int, int>();
            var room = new List<Vector3>(); var roomNormals = new List<Vector3>(); var outUvs = new List<Vector2>();
            var triangles = new int[snowTriangles.Length];
            for (int i = 0; i < snowTriangles.Length; i++)
            {
                int old = snowTriangles[i], index;
                if (!indexOf.TryGetValue(old, out index))
                {
                    index = room.Count; indexOf[old] = index;
                    Vector3 p = frameT.TransformPoint(vertices[old]) - roomOrigin;
                    room.Add(new Vector3(Vector3.Dot(p, roomRight), Vector3.Dot(p, roomUp), Vector3.Dot(p, roomOut)));
                    Vector3 n = normals != null && normals.Length == vertices.Length ? frameT.TransformDirection(normals[old]) : roomUp;
                    roomNormals.Add(new Vector3(Vector3.Dot(n, roomRight), Vector3.Dot(n, roomUp), Vector3.Dot(n, roomOut)));
                    outUvs.Add(uvs != null && uvs.Length == vertices.Length ? uvs[old] : Vector2.zero);
                }
                triangles[i] = index;
            }

            // Two parts where the heights have a gap.
            var heights = new float[room.Count];
            for (int i = 0; i < heights.Length; i++) heights[i] = room[i].y;
            System.Array.Sort(heights);
            float gap = LIFTED_SNOW_PART_GAP, upperFrom = float.MaxValue;
            for (int i = 1; i < heights.Length; i++)
                if (heights[i] - heights[i - 1] > gap) { gap = heights[i] - heights[i - 1]; upperFrom = heights[i] - 0.001f; }

            // Each part's extents in the room.
            float acrossLo = float.MaxValue, acrossHi = float.MinValue;
            float sillLo = float.MaxValue, sillFront = float.MinValue;              // the sill part's bottom, and its face nearest the glass's far side once mirrored
            float barLo = float.MaxValue, barHi = float.MinValue, barBack = float.MaxValue, barFrontRoom = float.MinValue;
            for (int i = 0; i < room.Count; i++)
            {
                Vector3 p = room[i]; float outside = -p.z;      // mirrored through the glass
                acrossLo = Mathf.Min(acrossLo, p.x); acrossHi = Mathf.Max(acrossHi, p.x);
                if (p.y >= upperFrom) { barLo = Mathf.Min(barLo, p.y); barHi = Mathf.Max(barHi, p.y); barBack = Mathf.Min(barBack, outside); barFrontRoom = Mathf.Max(barFrontRoom, outside); }
                else { sillLo = Mathf.Min(sillLo, p.y); sillFront = Mathf.Max(sillFront, outside); }
            }
            if (acrossHi - acrossLo < 0.01f || sillLo == float.MaxValue) return null;
            bool roomHasBarPart = barLo != float.MaxValue;

            // The room's bar top: the highest long, up-looking face of the frame's own wood that lies
            // inside the snow wrapping it.
            float roomBarTop = float.MinValue;
            if (roomHasBarPart)
            {
                int[] all = source.triangles;
                var snowStart = source.GetSubMesh(snowSubmesh);
                for (int t = 0; t + 2 < all.Length; t += 3)
                {
                    if (t >= snowStart.indexStart && t < snowStart.indexStart + snowStart.indexCount) continue;
                    Vector3 p0 = frameT.TransformPoint(vertices[all[t]]) - roomOrigin, p1 = frameT.TransformPoint(vertices[all[t + 1]]) - roomOrigin, p2 = frameT.TransformPoint(vertices[all[t + 2]]) - roomOrigin;
                    Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                    if (n.sqrMagnitude < 1e-10f || Vector3.Dot(n.normalized, roomUp) < 0.7f) continue;
                    float longest = Mathf.Max((p0 - p1).magnitude, Mathf.Max((p1 - p2).magnitude, (p0 - p2).magnitude));
                    if (longest < (acrossHi - acrossLo) * 0.5f) continue;
                    float height = Vector3.Dot(p0 + p1 + p2, roomUp) / 3f;
                    if (height < barLo || height > barHi) continue;
                    if (height > roomBarTop) roomBarTop = height;
                }
            }

            // The building's bar: its top, and how far its front face stands out from the pane's plane.
            float shellBarLow, shellBarTop, shellBarFront = float.MinValue;
            bool shellHasBar = TryMeasureShellBar(pane, shellTriangles, shellWorld, out shellBarLow, out shellBarTop);
            if (shellHasBar)
            {
                for (int t = 0; t + 2 < shellTriangles.Length; t += 3)
                {
                    Vector3 p0 = shellWorld[shellTriangles[t]] - pane.Center, p1 = shellWorld[shellTriangles[t + 1]] - pane.Center, p2 = shellWorld[shellTriangles[t + 2]] - pane.Center;
                    Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                    if (n.sqrMagnitude < 1e-10f || Vector3.Dot(n.normalized, pane.Outward) < 0.7f) continue;
                    Vector3 middle = (p0 + p1 + p2) / 3f;
                    float height = Vector3.Dot(middle, pane.Up), depth = Vector3.Dot(middle, pane.Outward);
                    if (height < shellBarLow - 0.002f || height > shellBarTop + 0.002f) continue;
                    if (Mathf.Abs(Vector3.Dot(middle, pane.Right)) > pane.Width * 0.5f || depth < 0.002f || depth > 0.15f) continue;
                    if (depth > shellBarFront) shellBarFront = depth;
                }
            }
            hasBarPart = roomHasBarPart && roomBarTop != float.MinValue && shellHasBar && shellBarFront > LIFTED_SNOW_PROUD_OF_GLASS + 0.004f;

            // The front of the building's sill ledge: up-looking faces at the glass's bottom edge.
            float ledgeFront = float.MinValue;
            for (int t = 0; t + 2 < shellTriangles.Length; t += 3)
            {
                Vector3 p0 = shellWorld[shellTriangles[t]] - pane.Center, p1 = shellWorld[shellTriangles[t + 1]] - pane.Center, p2 = shellWorld[shellTriangles[t + 2]] - pane.Center;
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude < 1e-10f || Vector3.Dot(n.normalized, pane.Up) < 0.7f) continue;
                Vector3 middle = (p0 + p1 + p2) / 3f;
                float across = Vector3.Dot(middle, pane.Right);
                if (across < glass.x || across > glass.y) continue;
                if (Mathf.Abs(Vector3.Dot(middle, pane.Up) - glass.z) > 0.03f) continue;
                for (int c = 0; c < 3; c++)
                {
                    float depth = Vector3.Dot(c == 0 ? p0 : c == 1 ? p1 : p2, pane.Outward);
                    if (depth > 0.3f || depth < -0.05f) continue;
                    if (depth > ledgeFront) ledgeFront = depth;
                }
            }

            // Across: as wide as what the frost covers (or the glass) plus the margin, centered on it.
            float slideRight = Vector3.Dot(pane.Center - pane.BaseCenter, pane.Right);
            float coverLeft = glass.x, coverRight = glass.y;
            if (a < s_OI.FrostRects.Count && s_OI.FrostRects[a].y - s_OI.FrostRects[a].x > 0.01f)
            {
                coverLeft = s_OI.FrostRects[a].x - slideRight;
                coverRight = s_OI.FrostRects[a].y - slideRight;
            }
            float widthWanted = (coverRight - coverLeft) + 2f * (OUTSIDE_IN_DRIFT_PAST_GLASS + Mathf.Max(OutsideInDriftOverlap, 0f));
            float scaleAcross = widthWanted / (acrossHi - acrossLo);
            float acrossMiddleRoom = (acrossLo + acrossHi) * 0.5f, acrossMiddleShell = (coverLeft + coverRight) * 0.5f;

            Vector2 interiorSize = s_OutsideInApertureInteriorSize[a];
            // By the pane AS MEASURED: the snow is fitted to the building's frame, which a picture set
            // taller by hand does not make any taller.
            float scaleUp = interiorSize.y > 0.01f ? (pane.BaseHeight > 0f ? pane.BaseHeight : pane.Height) / interiorSize.y : 1f;

            float lip, sillRise = 0f;
            if (TryMeasureSillLip(pane, glass, shellWorld, shellTriangles, out lip))
                sillRise = Mathf.Clamp(lip * OUTSIDE_IN_DRIFT_RISES_BY_SHARE_OF_SILL_LIP, 0f, 0.015f);

            // Sill part in depth: front edge just behind the ledge's front. Where no ledge can be
            // read, the rule the copied slabs use.
            float sillSlide;
            if (ledgeFront > 0.005f) sillSlide = (ledgeFront - LIFTED_SNOW_BEHIND_LEDGE_FRONT) - sillFront;
            else
            {
                float relief;
                sillSlide = TryMeasureWindowRelief(pane, glass, shellWorld, shellTriangles, out relief)
                          ? (relief - OUTSIDE_IN_DRIFT_BEHIND_RELIEF) - sillFront
                          : OUTSIDE_IN_DRIFT_LEGACY_OFFSET;
            }

            // Bar part in depth: from just behind the bar's front face back toward the glass, never
            // deeper than it was modeled and never behind the glass.
            float barFrontWanted = 0f, barBackWanted = 0f, barDepthRoom = barFrontRoom - barBack;
            if (hasBarPart)
            {
                barFrontWanted = shellBarFront - LIFTED_SNOW_BEHIND_BAR_FRONT;
                barBackWanted = Mathf.Max(LIFTED_SNOW_PROUD_OF_GLASS, barFrontWanted - barDepthRoom);
            }

            var world = new Vector3[room.Count];
            var worldNormals = new Vector3[room.Count];
            var keep = new bool[room.Count];
            for (int i = 0; i < room.Count; i++)
            {
                Vector3 p = room[i]; bool upper = p.y >= upperFrom;
                if (upper && !hasBarPart) continue;
                keep[i] = true;

                float across = acrossMiddleShell + (p.x - acrossMiddleRoom) * scaleAcross;
                float outside = -p.z, up, depth;
                if (upper)
                {
                    up = shellBarTop + (p.y - roomBarTop) * scaleUp;
                    depth = barDepthRoom > 0.0005f ? barBackWanted + (outside - barBack) * ((barFrontWanted - barBackWanted) / barDepthRoom) : barFrontWanted;
                }
                else
                {
                    up = glass.z + sillRise + (p.y - sillLo) * scaleUp;
                    depth = outside + sillSlide;
                }
                world[i] = pane.Center + pane.Right * across + pane.Up * up + pane.Outward * depth;

                Vector3 n = roomNormals[i];
                worldNormals[i] = (pane.Right * n.x + pane.Up * n.y - pane.Outward * n.z).normalized;
            }

            // Mirrored, so every triangle is wound the other way round. A triangle of a part that is
            // not built is dropped whole.
            var kept = new List<int>(triangles.Length);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                if (!keep[triangles[t]] || !keep[triangles[t + 1]] || !keep[triangles[t + 2]]) continue;
                kept.Add(triangles[t]); kept.Add(triangles[t + 2]); kept.Add(triangles[t + 1]);
            }
            if (kept.Count == 0) return null;

            var mesh = new Mesh();
            mesh.name = "SeamlessInteriors_LiftedWindowSnowMesh";
            mesh.vertices = world;
            mesh.normals = worldNormals;
            mesh.uv = outUvs.ToArray();
            mesh.triangles = kept.ToArray();
            mesh.RecalculateBounds();

            var drift = new GameObject("SeamlessInteriors_LedgeDrift_" + a);
            drift.AddComponent<MeshFilter>().sharedMesh = mesh;
            var driftRenderer = drift.AddComponent<MeshRenderer>();
            driftRenderer.sharedMaterial = frameRenderer.sharedMaterials[snowSubmesh];
            driftRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            drift.SetActive(false);
            return drift;
        }
    }
}
