using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    // One interior window aperture, with the plane a portal quad should sit on.
    //
    // Everything is world space except PortalOffsetLocal, because the renderer works in world
    // space and the clone carries a non-uniform ScaleAdjustment (1.05, 0.98, 1.05 on the Camp
    // Office) that would otherwise have to be undone at every use site.
    public struct InteriorWindowOpening
    {
        public Transform PaneRoot;

        // A view out and nothing else: never offered to a building window, from either side. Glass named by
        // the building's row in RoomGlassViewOutOnly (the panes over a room's doors, where the building's
        // door has no glass of its own to show a view in).
        public bool ViewOutOnly;

        public Vector3 GlassCenter;     // centre of the glass plane
        public Vector3 PortalCenter;    // centre of the plane the quad goes on
        public Vector3 Outward;         // unit normal, pointing out of the building
        public Vector3 Right;           // unit, in the plane
        public Vector3 Up;              // unit, in the plane
        public float Width;
        public float Height;
        // The glass itself, where Width and Height have been widened past it (a bent glass's views
        // reach out to meet at the bends). 0 = the same as Width and Height.
        public float GlassWidth, GlassHeight;

        // The fake exterior seen through the glass. It has to be hidden for a portal to be
        // visible at all, and it is the single largest occluder in the opening.
        public Renderer Backdrop;

        // The pane itself and the frost sitting on it, in draw order outward from the room. Both
        // are deliberately left alone by default: the frost is opaque and nearer the player than
        // the portal quad, so it occludes the view exactly where it has geometry, which is how a
        // window gets frost over a real view with no shader and no blending.
        //
        // They are collected because clearing them is a thing the player should be able to do,
        // and because judging the portal itself means being able to take them out of the way.
        public List<Renderer> Glass;
        public List<Renderer> Frost;

        // What else belongs to this window but does not hang under PaneRoot, or null. A bay in one
        // bent glass keeps its frame as a SIBLING of the glass (the farmhouse's
        // STR_FarmHouseParlourRoomWindowFrames_Prefab), so nothing that walks PaneRoot finds it.
        public List<Renderer> Companions;

        public float PortalOffsetLocal; // finally used offset from the glass, clone-local units
        public bool ClampedIn;          // pushed inwards off the default
        public bool ClampedOut;         // pushed outwards off the default
        public bool HadFrame;
        public bool HadSnow;
        public Vector3[] Outline;       // the glass's own outline, world points, when it is not a rectangle; null for a rectangle

        // Windows cut from ONE band of glass in the room (see BuildingWindowOverride.RoomBands) carry
        // the same number here, from 1 up, and their Outward, Right and Up are the band's own, copied
        // and not measured again, so they are exactly equal: they stand in one plane on one wall, and
        // a view shared between them can rely on it. 0 = a window by itself, which is every other one.
        public int WallGroup;

        // How far this window's own frame in the room stands from its glass, in meters, + into the
        // room and - outboard of it, read from the frame's box where the mesh cannot be read. Known
        // only for a band strip; every other window measures its frame's reach from its mesh
        // (TryMeasureRoomFrameReach). A bool and not NaN: the struct's default must mean not known.
        public float FrameDepthIntoRoom;
        public bool FrameDepthKnown;

        // Meters out from the glass where its frost and snow stand; 0 = on the glass. Set for a hand opening
        // whose picture stands past boards, so the cover is behind them (Milton bank, 2026-10-14).
        public float CoverOut;
    }

    public partial class SeamlessInteriorsMod
    {
        // ─── FINDING A WINDOW APERTURE, AND WHERE A PORTAL QUAD SITS IN IT ───
        //
        // A window is a four-layer stack. Outward from the room, measured across all 8 Camp
        // Office panes on 2026-09-14:
        //
        //   frame   OBJ_LakeCabinWindow*_LOD0     opaque, q2000, inner and outer in one mesh
        //   glass   OBJ_LakeCabinGlass*           q3000 TRANSPARENT, zero thickness
        //   snow    OBJ_LakeCabinWindowSnow*      opaque, q2000, 87mm out, 54mm thick
        //   backdrop OBJ_LakeCabinInteriorWindow  q2900, 287mm deep, the painted "outside"
        //
        // The quad belongs BETWEEN the snow and the frame's outer face. That ordering is what
        // makes the frost work with no shader and no blending: the snow is opaque and nearer to
        // the player than the quad, so it occludes the view exactly where it has geometry and
        // the view shows through everywhere else. The glass is nearer still and transparent, so
        // it tints the result like real glass.
        //
        // Seven of the eight Camp Office panes are literally the same asset - glass 1.05 x 1.13,
        // glass-to-snow 87mm, slot 13.7 to 13.9mm - so a fixed offset is right for the common
        // case. The clamp exists for the eighth, and for other buildings.

        // Default distance out from the glass, in clone-local units. 87mm to clear the snow plus
        // 6mm, which lands inside every matched Camp Office pane's 13.7mm slot.
        private const float PORTAL_DEFAULT_OFFSET_FROM_GLASS = 0.093f;

        // Kept between the quad and whatever bounds it, so neither z-fights.
        private const float PORTAL_PLANE_CLEARANCE = 0.002f;

        private const string GLASS_MATERIAL_PREFIX = "STR_WindowGlassMix";

        // ─── A BENT GLASS: ONE OBJECT, SEVERAL WINDOWS ───
        //
        // The Pleasant Valley farmhouse's bay is ONE glass object in the room
        // (STR_FarmHouseParlourRoomWindowGlass_Prefab: 12 triangles, three panes, the side ones
        // slanted), and it hangs directly under the room's 'Structure' rather than in a window
        // prefab of its own. Everything above assumes a window is one flat pane square to the
        // room's axes inside its own root, so the walk up the hierarchy stopped at 'Structure',
        // which holds all 18 of that room's glass pieces, and declared ONE window 18.4 m wide and
        // 23 m tall. It paired with nothing and drew nothing, and the owner, inside on
        // 2026-09-19: "Inside-out bay windows are up at 40m."
        //
        // A glass whose triangles face more than one way is split here into one window per
        // facing, measured from the triangles themselves. Which side is OUT comes from the bend:
        // a bay bulges outward, so every other pane lies on a pane's INNER side. The room stands
        // upright, so up is up.
        private const float BENT_GLASS_SAME_FACING_COSINE = 0.985f;   // 10 degrees

        // WHERE A BENT GLASS'S VIEWS STAND AND HOW LARGE THEY ARE, settled by the owner with sliders on
        // the farmhouse's bay, 2026-09-19. Three planes each pushed out from the glass pull apart at
        // the bends (at the usual 9.3 cm and 39 degrees, 6.6 cm of gap), so each view is widened
        // toward its neighbor by what the distance and the angle between them call for, plus a
        // little overlap. Nearer the glass closes the gaps too but hides the room's own outer
        // casing, which shows on every ordinary window ("since it's missing the outer frame, it
        // doesn't match the other non-bay windows"); 5 cm kept the casing and looked right. The
        // glass itself is modeled about a centimeter smaller than the flat panes beside it, hence
        // the half centimeter past its edge all round.
        private const float BENT_GLASS_VIEW_OUTSIDE_GLASS = 0.05f;
        private const float BENT_GLASS_VIEW_PAST_EDGE = 0.005f;
        private const float BENT_GLASS_BEND_OVERLAP = 0.002f;
        // A facing with less of the glass than this is an edge or a sliver, not a pane. The farmhouse's
        // bay is three facings of 33% each; of that room's other 17 glass pieces none has a second.
        private const float BENT_GLASS_SMALLEST_SHARE = 0.15f;

        private sealed class BentGlassFacing
        {
            public Vector3 Normal;          // level, as modeled: may look into the room or out of it
            public float Area;
            public List<Vector3> Corners = new List<Vector3>();
            public Vector3 Middle;
        }

        private static bool TryAddBentGlassOpenings(MeshRenderer glassRenderer, MeshRenderer[] all, List<InteriorWindowOpening> result)
        {
            var filter = glassRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return false;

            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] triangles = filter.sharedMesh.triangles;
            Transform glassT = glassRenderer.transform;

            var facings = new List<BentGlassFacing>();
            float totalArea = 0f;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p0 = glassT.TransformPoint(vertices[triangles[t]]), p1 = glassT.TransformPoint(vertices[triangles[t + 1]]), p2 = glassT.TransformPoint(vertices[triangles[t + 2]]);
                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                float area = cross.magnitude * 0.5f;
                if (area < 1e-6f) continue;
                Vector3 level = new Vector3(cross.x, 0f, cross.z);
                if (level.sqrMagnitude < 1e-10f || Mathf.Abs(cross.normalized.y) > 0.3f) continue;
                level.Normalize();

                BentGlassFacing hit = null;
                for (int f = 0; f < facings.Count && hit == null; f++)
                    if (Mathf.Abs(Vector3.Dot(facings[f].Normal, level)) >= BENT_GLASS_SAME_FACING_COSINE) hit = facings[f];
                if (hit == null) { hit = new BentGlassFacing { Normal = level }; facings.Add(hit); }
                hit.Area += area; totalArea += area;
                hit.Corners.Add(p0); hit.Corners.Add(p1); hit.Corners.Add(p2);
            }

            for (int f = facings.Count - 1; f >= 0; f--)
                if (facings[f].Area < totalArea * BENT_GLASS_SMALLEST_SHARE) facings.RemoveAt(f);
            if (facings.Count < 2) return false;        // a flat pane: the ordinary way handles it

            Vector3 whole = Vector3.zero;
            for (int f = 0; f < facings.Count; f++)
            {
                Vector3 sum = Vector3.zero;
                for (int c = 0; c < facings[f].Corners.Count; c++) sum += facings[f].Corners[c];
                facings[f].Middle = sum / facings[f].Corners.Count;
                whole += facings[f].Middle;
            }
            whole /= facings.Count;

            // The painted "outside" that stands behind this glass, to be hidden with it: the nearest.
            Renderer backdrop = null; float nearest = 4f * 4f;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null || r == glassRenderer) continue;
                string matName = r.sharedMaterial != null ? r.sharedMaterial.name : string.Empty;
                if (!MatchesWindowRole(3, r.gameObject.name, matName)) continue;
                var rf = r.GetComponent<MeshFilter>();
                if (rf == null || rf.sharedMesh == null) continue;
                float distance = (r.transform.TransformPoint(rf.sharedMesh.bounds.center) - whole).sqrMagnitude;
                if (distance < nearest) { nearest = distance; backdrop = r; }
            }

            int firstAdded = result.Count;
            // The frame and whatever else stands with this glass: its siblings whose middle is the
            // glass's middle, give or take. Not glass, not the backdrop, and not half the room.
            var companions = new List<Renderer>();
            Vector3 glassMiddle = glassT.TransformPoint(filter.sharedMesh.bounds.center);
            float glassReach = Vector3.Scale(filter.sharedMesh.bounds.size, glassT.lossyScale).magnitude;
            Transform family = glassT.parent;
            for (int i = 0; i < all.Length && family != null; i++)
            {
                var r = all[i];
                if (r == null || r == glassRenderer || r.transform.parent != family) continue;
                string matName = r.sharedMaterial != null ? r.sharedMaterial.name : string.Empty;
                if (matName.StartsWith(GLASS_MATERIAL_PREFIX) || MatchesWindowRole(3, r.gameObject.name, matName)) continue;
                var rf = r.GetComponent<MeshFilter>();
                if (rf == null || rf.sharedMesh == null) continue;
                if ((r.transform.TransformPoint(rf.sharedMesh.bounds.center) - glassMiddle).magnitude > 0.3f) continue;
                if (Vector3.Scale(rf.sharedMesh.bounds.size, r.transform.lossyScale).magnitude > glassReach * 1.6f) continue;
                companions.AddRange(r.GetComponentsInChildren<Renderer>(true));
            }

            for (int f = 0; f < facings.Count; f++)
            {
                var facing = facings[f];

                float othersAhead = 0f;
                for (int g = 0; g < facings.Count; g++)
                    if (g != f) othersAhead += Vector3.Dot(facings[g].Middle - facing.Middle, facing.Normal);
                Vector3 outward = othersAhead > 0f ? -facing.Normal : facing.Normal;

                Vector3 up = Vector3.up;
                Vector3 right = Vector3.Cross(up, outward).normalized;
                float uLo = float.MaxValue, uHi = float.MinValue, vLo = float.MaxValue, vHi = float.MinValue, plane = 0f;
                for (int c = 0; c < facing.Corners.Count; c++)
                {
                    Vector3 p = facing.Corners[c];
                    float u = Vector3.Dot(p, right), v = Vector3.Dot(p, up);
                    if (u < uLo) uLo = u; if (u > uHi) uHi = u;
                    if (v < vLo) vLo = v; if (v > vHi) vHi = v;
                    plane += Vector3.Dot(p, outward);
                }
                plane /= facing.Corners.Count;

                var opening = new InteriorWindowOpening();
                opening.PaneRoot = glassRenderer.transform;
                opening.Backdrop = backdrop;
                opening.Glass = new List<Renderer> { glassRenderer };
                opening.Frost = new List<Renderer>();
                opening.Companions = companions;
                opening.PortalOffsetLocal = BENT_GLASS_VIEW_OUTSIDE_GLASS;
                opening.HadFrame = false;
                opening.HadSnow = false;
                opening.Outward = outward;
                opening.Up = up;
                opening.Right = Vector3.Cross(opening.Up, opening.Outward).normalized;
                opening.GlassCenter = right * ((uLo + uHi) * 0.5f) + up * ((vLo + vHi) * 0.5f) + outward * plane;
                opening.PortalCenter = opening.GlassCenter + outward * BENT_GLASS_VIEW_OUTSIDE_GLASS;
                opening.Width = uHi - uLo;
                opening.Height = vHi - vLo;
                opening.GlassWidth = opening.Width; opening.GlassHeight = opening.Height;
                result.Add(opening);
            }

            // Each view past its glass's edge, and out to meet its neighbor's at every bend.
            for (int i = firstAdded; i < result.Count; i++)
            {
                var opening = result[i];
                float left = BENT_GLASS_VIEW_PAST_EDGE, right = BENT_GLASS_VIEW_PAST_EDGE;
                for (int j = firstAdded; j < result.Count; j++)
                {
                    if (j == i) continue;
                    float angle = Vector3.Angle(opening.Outward, result[j].Outward);
                    if (angle < 5f || angle > 60f) continue;        // the pane next to this one, not the one beyond it
                    float reach = BENT_GLASS_VIEW_OUTSIDE_GLASS * Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad) + BENT_GLASS_BEND_OVERLAP;
                    if (Vector3.Dot(result[j].GlassCenter - opening.GlassCenter, opening.Right) > 0f) right = Mathf.Max(right, reach);
                    else left = Mathf.Max(left, reach);
                }
                opening.Width += left + right;
                opening.Height += 2f * BENT_GLASS_VIEW_PAST_EDGE;
                opening.PortalCenter += opening.Right * ((right - left) * 0.5f);
                result[i] = opening;
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[WINDOW] {glassRenderer.gameObject.name}: bukuk tek bir cam, {facings.Count} ayri pencere olarak olculdu.",
                                $"[WINDOW] {glassRenderer.gameObject.name}: one bent piece of glass, measured as {facings.Count} separate windows, one for each way it faces.");
            return true;
        }

        // ─── A MANY-PANE GLASS: ONE OBJECT, EVERY WINDOW OF THE ROOM ───
        //
        // The Coastal Highway gas station's room has ONE glass object for all its windows
        // (STR_GarageWindowsGlass, material GLB_GlassWindowCTrans_Mat): 20 flat pieces on four walls,
        // none of them in a window prefab of its own, and none wearing STR_WindowGlassMix. The finder
        // saw no window at all, nothing outside was paired, and the owner, 2026-09-19: "The outside
        // ones show blackend view inside the outer shell like they couldn't find the interior
        // counterparts."
        //
        // The pieces are found as triangles joined at their corners. Pieces that are ONE window with
        // bars between them (a pair of 52 and 53 cm, a strip of three) are joined when they stand on
        // the same wall at the same depth, share a bottom and a top, and are no further apart along
        // the wall than a bar is wide: measured there, 7 to 9 cm. That gave 9 windows, eight of them
        // the building's eight by size (log 0919-37).
        //
        // WHICH SIDE IS OUT comes from the painted backdrop (GLB_GlowExt), which stood 10 cm from the
        // glass on the same side at all nine, the room's middle on the other. A glass with no
        // backdrop near it is NOT a window and is left alone: this material family is also worn by
        // things that are not windows, and a guess here would give a cabinet door a view of the yard.
        private const float WINDOW_LARGEST = 6f;
        private const string MANY_PANE_GLASS_MATERIAL_PREFIX = "GLB_GlassWindow";
        // Where the picture of the outdoors stands, outside the room's glass. These windows used the
        // bent glass's 5 cm until 2026-09-20, a number judged on a bay and never here. A picture
        // the size of the hole standing 5 cm BEHIND the hole looks smaller than the hole from close
        // up: a band of the raw view showed all round it at arm's length and nothing from across
        // the room. Owner, at the gas station, close to the glass, by slider: 0.25 cm. Where the
        // view of the outdoors STARTS does not move with this: the eye's offset is taken from this
        // plane to the building's pane, so the start stays in front of the building's own frame.
        private const float MANY_PANE_VIEW_OUTSIDE_GLASS = 0.0025f;
        private const float MANY_PANE_SAME_DEPTH = 0.05f;
        private const float MANY_PANE_SAME_EDGE = 0.03f;
        private const float MANY_PANE_WIDEST_BAR = 0.15f;
        private const float MANY_PANE_BACKDROP_REACH = 1.5f;
        private const float MANY_PANE_BACKDROP_MARGIN = 0.3f;

        private sealed class GlassPiece
        {
            public Vector3 Normal;          // level, as modeled
            public Vector3 Along;
            public float Bottom, Top;
            public List<Vector3> Corners = new List<Vector3>();
            public int Group;
            public Renderer Source;         // the glass object this piece is part of

            // Across and depth of THIS piece's corners in ANOTHER piece's frame, from that piece's first
            // corner. Never as plain dot products of world positions: those run to a thousand meters,
            // and two panes of one wall modeled a hundredth of a degree apart then differ by 17 cm of
            // "depth" that is not there. It kept one of the gas station's three like pairs apart on
            // the first run (10 windows for 9, and a 1.06 m pane paired to a 0.54 m half).
            public void MeasureIn(GlassPiece frame, out float lo, out float hi, out float depth)
            {
                lo = float.MaxValue; hi = float.MinValue; depth = 0f;
                for (int i = 0; i < Corners.Count; i++)
                {
                    Vector3 d = Corners[i] - frame.Corners[0];
                    float x = Vector3.Dot(d, frame.Along);
                    if (x < lo) lo = x; if (x > hi) hi = x;
                    depth += Vector3.Dot(d, frame.Normal);
                }
                depth /= Corners.Count;
            }
        }

        // joinStacked: a window here is two panes HIGH as well as wide. Off for the gas station's
        // glass, where each pane is its own window; on for a sheet named by RoomManyPaneObjects,
        // because the hangar's side sheets cut each tall window into two stacked strips - 26 quads
        // that are 17 windows (measured from the GPU, 2026-09-22).
        private static int TryAddManyPaneGlassOpenings(MeshRenderer glassRenderer, MeshRenderer[] all, List<InteriorWindowOpening> result, bool joinStacked = false)
        {
            var filter = glassRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return 0;

            // Readable from the mesh, otherwise from the GPU. See TryReadMesh.
            Vector3[] vertices;
            int[] triangles;
            if (!TryReadMesh(filter.sharedMesh, out vertices, out triangles)) return 0;

            Transform glassT = glassRenderer.transform;
            if (triangles.Length < 3) return 0;

            // Corners at one place are one corner, whatever the mesh's seams say; then every triangle
            // joins its three.
            var parent = new int[vertices.Length];
            var byPlace = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var place = new Vector3Int(Mathf.RoundToInt(vertices[i].x * 1000f), Mathf.RoundToInt(vertices[i].y * 1000f), Mathf.RoundToInt(vertices[i].z * 1000f));
                int first;
                if (byPlace.TryGetValue(place, out first)) parent[i] = first; else { byPlace[place] = i; parent[i] = i; }
            }
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = RootOf(parent, triangles[t]);
                parent[RootOf(parent, triangles[t + 1])] = a;
                parent[RootOf(parent, triangles[t + 2])] = a;
            }

            var world = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) world[i] = glassT.TransformPoint(vertices[i]);

            var pieces = new Dictionary<int, GlassPiece>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p0 = world[triangles[t]], p1 = world[triangles[t + 1]], p2 = world[triangles[t + 2]];
                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                if (cross.sqrMagnitude < 1e-12f || Mathf.Abs(cross.normalized.y) > 0.3f) continue;     // a skylight is not a window
                Vector3 level = new Vector3(cross.x, 0f, cross.z).normalized;

                int root = RootOf(parent, triangles[t]);
                GlassPiece piece;
                if (!pieces.TryGetValue(root, out piece))
                {
                    piece = new GlassPiece { Normal = level, Along = Vector3.Cross(Vector3.up, level).normalized, Bottom = float.MaxValue, Top = float.MinValue, Source = glassRenderer };
                    pieces[root] = piece;
                }
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = world[triangles[t + k]];
                    if (p.y < piece.Bottom) piece.Bottom = p.y; if (p.y > piece.Top) piece.Top = p.y;
                    piece.Corners.Add(p);
                }
            }
            if (pieces.Count == 0) return 0;

            var list = new List<GlassPiece>(pieces.Count);
            foreach (var pair in pieces) list.Add(pair.Value);
            return AddOpeningsFromGlassPieces(list, joinStacked, all, result, glassRenderer.gameObject.name);
        }

        // Is this room object one a row asked to be read as many windows? See
        // BuildingWindowOverride.RoomManyPaneObjects.
        private static bool IsRoomManyPaneObject(SeamlessInteriorInstance instance, string objectName)
        {
            if (instance == null || instance.Config == null || string.IsNullOrEmpty(objectName)) return false;

            BuildingWindowOverride byHand;
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out byHand)) return false;
            if (byHand.RoomManyPaneObjects == null) return false;

            for (int i = 0; i < byHand.RoomManyPaneObjects.Length; i++)
            {
                string want = byHand.RoomManyPaneObjects[i];
                if (!string.IsNullOrEmpty(want) && objectName.StartsWith(want, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ONE WALL GROUP PER TIER, derived rather than hand-numbered.
        //
        // A shared wall camera can only serve windows that are CO-PLANAR and share a facing, so the
        // group has to be the plane, not the object. The hangar's side sheet is one object holding
        // two tiers 7.3 m apart in depth - the walls are stepped, not curved - so one group for the
        // sheet would be wrong and a group per window would be useless. Bucketing the openings by
        // their distance along their own outward gives two groups a side, four for the building,
        // with no number typed anywhere.
        private static void GroupOpeningsByTier(List<InteriorWindowOpening> openings, int from, ref int wallGroups, string label)
        {
            if (from < 0 || from >= openings.Count) return;

            // 5 cm buckets: far finer than the 7.3 m between the hangar's tiers, far coarser than
            // the millimetre wobble of corners read back from a GPU buffer.
            var groupOfPlane = new Dictionary<int, int>();
            for (int i = from; i < openings.Count; i++)
            {
                var opening = openings[i];
                int plane = Mathf.RoundToInt(Vector3.Dot(opening.PortalCenter, opening.Outward) * 20f);

                int group;
                if (!groupOfPlane.TryGetValue(plane, out group))
                {
                    group = ++wallGroups;
                    groupOfPlane[plane] = group;
                }

                opening.WallGroup = group;
                openings[i] = opening;                      // a struct in a list: written back
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[WINDOW-WALL] {label}: {openings.Count - from} pencere, ayni duzlemde olanlar {groupOfPlane.Count} duvar grubuna ayrildi.",
                                $"[WINDOW-WALL] {label}: {openings.Count - from} windows, sorted into {groupOfPlane.Count} wall groups by the plane they stand on.");
        }

        // ─── THE THIRD KIND OF ROOM GLASS: EVERY PANE ITS OWN OBJECT, ALL UNDER ONE PARENT ───
        //
        // Grey Mother's house in Mountain Town, first visited 2026-09-20: 34 objects named
        // STR_GreyMothersHouse_GlassPane001 to 034, four to nine corners each, all hanging directly
        // under the room's one Geo object. A window's panes are told from another window's by the
        // parent they share (FindWindowRoot), and here every pane in the house shares one, so the
        // whole house came out as ONE window: 13.2 m wide, 16.8 m tall and, the flattest way through
        // 34 panes on four walls being down, facing straight up. From outside nothing paired ("All
        // the ground level windows look into the outdoor shell") and the set-up was tried again
        // every second; from inside a picture of the outdoors the size of the house lay flat across
        // the room ("Most of the upstairs is gone when I'm downstairs").
        //
        // The panes are pieces of glass like the gas station's, only handed over one object at a
        // time, so they go through the same joining. One thing is added for them: a window here is
        // two panes wide AND two high, so a piece also joins the piece standing on it. Dry run in
        // the running game before this was written: 34 panes, 12 windows, five of 122 x 241 cm on
        // the ground floor and seven of 68 x 112 upstairs, against the author's table of 14 panes
        // on the building (six tall, seven upstairs, one small and wide) wall for wall.
        private const int LOOSE_PANES_AT_LEAST = 3;
        private const float LOOSE_PANES_DIFFERENT_WALL_COSINE = 0.5f;
        private const float MANY_PANE_STACKED_GAP = 0.15f;

        private static int TryAddLoosePaneOpenings(List<MeshRenderer> panes, MeshRenderer[] all, List<InteriorWindowOpening> result, string label)
        {
            var list = new List<GlassPiece>(panes.Count);
            for (int i = 0; i < panes.Count; i++)
            {
                GlassPiece piece = GlassPieceOf(panes[i]);
                if (piece != null) list.Add(piece);
            }
            if (list.Count == 0) return 0;
            return AddOpeningsFromGlassPieces(list, true, all, result, label);
        }

        // One whole glass object as one piece. null for a skylight, a mesh that cannot be read, or
        // nothing with an area.
        private static GlassPiece GlassPieceOf(MeshRenderer pane)
        {
            var filter = pane == null ? null : pane.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return null;
            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] triangles = filter.sharedMesh.triangles;
            if (vertices == null || triangles == null || triangles.Length < 3) return null;

            Transform paneT = pane.transform;
            GlassPiece piece = null;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p0 = paneT.TransformPoint(vertices[triangles[t]]), p1 = paneT.TransformPoint(vertices[triangles[t + 1]]), p2 = paneT.TransformPoint(vertices[triangles[t + 2]]);
                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                if (cross.sqrMagnitude < 1e-12f) continue;
                if (Mathf.Abs(cross.normalized.y) > 0.3f) return null;                                  // a skylight is not a window
                if (piece == null)
                {
                    Vector3 level = new Vector3(cross.x, 0f, cross.z).normalized;
                    piece = new GlassPiece { Normal = level, Along = Vector3.Cross(Vector3.up, level).normalized, Bottom = float.MaxValue, Top = float.MinValue, Source = pane };
                }
                Vector3[] corners = { p0, p1, p2 };
                for (int k = 0; k < 3; k++)
                {
                    if (corners[k].y < piece.Bottom) piece.Bottom = corners[k].y; if (corners[k].y > piece.Top) piece.Top = corners[k].y;
                    piece.Corners.Add(corners[k]);
                }
            }
            return piece;
        }

        // The middle of the room, from every switched-on mesh in it. The room is still switched off when it is
        // scanned: activeSelf, and mesh bounds (renderer bounds read zero).
        private static bool TryGetRoomMiddle(MeshRenderer[] all, out Vector3 middle)
        {
            middle = Vector3.zero; int counted = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !all[i].gameObject.activeSelf) continue;
                var f = all[i].GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                middle += all[i].transform.TransformPoint(f.sharedMesh.bounds.center); counted++;
            }
            if (counted == 0) return false;
            middle /= counted;
            return true;
        }

        // Would turning this pane round leave it facing the middle of the room? The middle is read once a
        // scan, on the first pane that asks (read: 0 not yet, 1 read, -1 nothing to read).
        private static bool TurningFacesTheRoomMiddle(MeshRenderer[] all, Vector3 paneMiddle, Vector3 outwardSide, ref Vector3 roomMiddle, ref int read)
        {
            if (read == 0) read = all != null && TryGetRoomMiddle(all, out roomMiddle) ? 1 : -1;
            return read > 0 && Vector3.Dot(roomMiddle - paneMiddle, -outwardSide) > 0f;
        }

        // Pieces of glass into windows, windows into openings. Shared by the one-glass-object room
        // (the gas station) and the loose-pane room (Grey Mother's house).
        private static int AddOpeningsFromGlassPieces(List<GlassPiece> list, bool joinStacked, MeshRenderer[] all, List<InteriorWindowOpening> result, string label)
        {
            for (int i = 0; i < list.Count; i++) list[i].Group = i;
            var joinedByRow = JoinNamedPieces(list);
            var sources = new HashSet<Renderer>();
            for (int i = 0; i < list.Count; i++) if (list[i].Source != null) sources.Add(list[i].Source);

            // Joined until nothing more joins, each pair compared in the first one's own frame.
            bool joined = true;
            while (joined)
            {
                joined = false;
                for (int a = 0; a < list.Count; a++)
                    for (int b = a + 1; b < list.Count; b++)
                    {
                        GlassPiece A = list[a], B = list[b];
                        if (A.Group == B.Group) continue;
                        if (Vector3.Dot(A.Normal, B.Normal) < BENT_GLASS_SAME_FACING_COSINE) continue;
                        float aLo, aHi, aDepth, bLo, bHi, bDepth;
                        A.MeasureIn(A, out aLo, out aHi, out aDepth); B.MeasureIn(A, out bLo, out bHi, out bDepth);
                        if (Mathf.Abs(aDepth - bDepth) > MANY_PANE_SAME_DEPTH) continue;
                        bool sideBySide = Mathf.Abs(A.Bottom - B.Bottom) <= MANY_PANE_SAME_EDGE && Mathf.Abs(A.Top - B.Top) <= MANY_PANE_SAME_EDGE
                                       && Mathf.Max(aLo, bLo) - Mathf.Min(aHi, bHi) <= MANY_PANE_WIDEST_BAR;
                        // One standing on the other: a bar's height apart, and over each other for at
                        // least half of the narrower one's width.
                        bool stacked = false;
                        if (joinStacked && !sideBySide)
                        {
                            float between = Mathf.Max(A.Bottom, B.Bottom) - Mathf.Min(A.Top, B.Top);
                            float shared = Mathf.Min(aHi, bHi) - Mathf.Max(aLo, bLo);
                            stacked = between <= MANY_PANE_STACKED_GAP && between > -MANY_PANE_SAME_EDGE
                                   && shared >= 0.5f * Mathf.Min(aHi - aLo, bHi - bLo);
                        }
                        if (!sideBySide && !stacked) continue;
                        int from = B.Group, into = A.Group;
                        for (int k = 0; k < list.Count; k++) if (list[k].Group == from) list[k].Group = into;
                        joined = true;
                    }
            }

            // The row's wider joining (RoomManyPaneJoinWithinCm), on whole windows as joined above: side
            // by side on one wall, the gap at most the row's, sharing half the shorter one's height. The
            // group keeps the lower index, so the windows after it move down and nothing before it moves.
            if (s_ScanManyPaneJoinWithin >= 0f)
            {
                joined = true;
                while (joined)
                {
                    joined = false;
                    for (int a = 0; a < list.Count && !joined; a++)
                        for (int b = 0; b < list.Count && !joined; b++)
                        {
                            GlassPiece A = list[a], B = list[b];
                            if (A.Group == B.Group || A.Group > B.Group) continue;
                            if (Vector3.Dot(A.Normal, B.Normal) < BENT_GLASS_SAME_FACING_COSINE) continue;
                            float aLo = float.MaxValue, aHi = float.MinValue, aBottom = float.MaxValue, aTop = float.MinValue, aDepth = 0f;
                            float bLo = float.MaxValue, bHi = float.MinValue, bBottom = float.MaxValue, bTop = float.MinValue, bDepth = 0f;
                            for (int k = 0; k < list.Count; k++)
                            {
                                if (list[k].Group != A.Group && list[k].Group != B.Group) continue;
                                float lo, hi, depth;
                                list[k].MeasureIn(A, out lo, out hi, out depth);
                                if (list[k].Group == A.Group) { aLo = Mathf.Min(aLo, lo); aHi = Mathf.Max(aHi, hi); aBottom = Mathf.Min(aBottom, list[k].Bottom); aTop = Mathf.Max(aTop, list[k].Top); aDepth = depth; }
                                else { bLo = Mathf.Min(bLo, lo); bHi = Mathf.Max(bHi, hi); bBottom = Mathf.Min(bBottom, list[k].Bottom); bTop = Mathf.Max(bTop, list[k].Top); bDepth = depth; }
                            }
                            if (Mathf.Abs(aDepth - bDepth) > MANY_PANE_SAME_DEPTH) continue;
                            float gap = Mathf.Max(aLo, bLo) - Mathf.Min(aHi, bHi);
                            float sharedHeight = Mathf.Min(aTop, bTop) - Mathf.Max(aBottom, bBottom);
                            if (gap > s_ScanManyPaneJoinWithin || sharedHeight < 0.5f * Mathf.Min(aTop - aBottom, bTop - bBottom)) continue;
                            int from = B.Group, into = A.Group;
                            for (int k = 0; k < list.Count; k++) if (list[k].Group == from) list[k].Group = into;
                            joined = true;
                        }
                }
            }

            // Every backdrop's corners, once.
            var backdrops = new List<Renderer>();
            var backdropCorners = new List<Vector3[]>();
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null || sources.Contains(r)) continue;
                string matName = r.sharedMaterial != null ? r.sharedMaterial.name : string.Empty;
                if (!MatchesWindowRole(3, r.gameObject.name, matName)) continue;
                var rf = r.GetComponent<MeshFilter>();
                if (rf == null || rf.sharedMesh == null) continue;
                // THE BACKDROP HAS TO BE READ THE SAME WAY THE GLASS IS. This is how a window's
                // outside is told from its inside, so a backdrop that cannot be read means every
                // pane in front of it is thrown away as "no painted outside behind it". The
                // hangar's side glass read 17 panes from the GPU on the first live run and then
                // lost all 17 here, because its backdrop (1386 vertices) is unreadable too and
                // this line still asked the mesh directly (2026-09-22).
                Vector3[] local;
                int[] unusedTriangles;
                if (!TryReadMesh(rf.sharedMesh, out local, out unusedTriangles)) continue;
                var corners = new Vector3[local.Length];
                for (int c = 0; c < local.Length; c++) corners[c] = r.transform.TransformPoint(local[c]);
                backdrops.Add(r); backdropCorners.Add(corners);
            }

            int added = 0, notWindows = 0;
            Vector3 roomMiddle = Vector3.zero; int roomMiddleRead = 0;
            var withoutBackdrop = new List<PendingWindow>();
            var done = new HashSet<int>();
            for (int g = 0; g < list.Count; g++)
            {
                if (!done.Add(list[g].Group)) continue;
                GlassPiece lead = list[g];
                float lo = float.MaxValue, hi = float.MinValue, bottom = float.MaxValue, top = float.MinValue, depth = 0f; int members = 0;
                for (int k = 0; k < list.Count; k++)
                {
                    if (list[k].Group != lead.Group) continue;
                    float kLo, kHi, kDepth;
                    list[k].MeasureIn(lead, out kLo, out kHi, out kDepth);
                    lo = Mathf.Min(lo, kLo); hi = Mathf.Max(hi, kHi);
                    bottom = Mathf.Min(bottom, list[k].Bottom); top = Mathf.Max(top, list[k].Top);
                    depth += kDepth; members++;
                }
                depth /= members;
                if (hi - lo < 0.05f || top - bottom < 0.05f) continue;

                // The backdrop's corners that stand by this window, and which side of the glass they are on.
                Renderer backdrop = null; float side = 0f; int near = 0;
                for (int b = 0; b < backdrops.Count; b++)
                {
                    float sum = 0f; int count = 0;
                    Vector3[] corners = backdropCorners[b];
                    for (int c = 0; c < corners.Length; c++)
                    {
                        Vector3 fromLead = corners[c] - lead.Corners[0];
                        float x = Vector3.Dot(fromLead, lead.Along), d = Vector3.Dot(fromLead, lead.Normal) - depth;
                        if (x < lo - MANY_PANE_BACKDROP_MARGIN || x > hi + MANY_PANE_BACKDROP_MARGIN) continue;
                        if (corners[c].y < bottom - MANY_PANE_BACKDROP_MARGIN || corners[c].y > top + MANY_PANE_BACKDROP_MARGIN) continue;
                        if (Mathf.Abs(d) > MANY_PANE_BACKDROP_REACH) continue;
                        sum += d; count++;
                    }
                    if (count > near) { near = count; side = sum / count; backdrop = backdrops[b]; }
                }
                if (joinedByRow.Contains(lead.Group))
                {
                    withoutBackdrop.Add(new PendingWindow { Lead = lead, Lo = lo, Hi = hi, Bottom = bottom, Top = top, Depth = depth });
                    continue;
                }
                if (backdrop == null || Mathf.Abs(side) < 0.005f)
                {
                    // Named by the building's row: a window all the same, built after the others.
                    if (GroupNamedWithoutBackdrop(list, lead.Group)) { withoutBackdrop.Add(new PendingWindow { Lead = lead, Lo = lo, Hi = hi, Bottom = bottom, Top = top, Depth = depth }); continue; }
                    notWindows++;
                    NoteGlassWithoutBackdrop(list, lead.Group);
                    continue;
                }

                // THE OUTSIDE IS NEVER THE SIDE TOWARD THE MIDDLE OF THE ROOM'S GLASS. The side comes from where the
                // backdrop stands, and what passes for a backdrop is not always the painted outside: at Thompson's
                // Store it was the room's own window frames (STR_RuralStoreAInteriorWindows_Prefab), which stand
                // on the ROOM side of the glass, so six of its seven windows were recorded facing into the room.
                // Each then paired with whatever faced the same way across the room: its three tall windows,
                // which sit directly behind the building's three tall ones, went unpaired, the views started
                // meters into the room and the views out stood at the parked room's height (owner, 2026-10-09:
                // "Why aren't they pairing though?"). Read live: the room's windows sit wall for wall where the
                // building's do, 7 for 7, and only their facing was wrong. The Community Hall and the two Thompson
                // houses loaded at the time have none facing in, so this changes nothing there. Glass all in one
                // wall is within half a meter of its own middle and is left as the backdrop says.
                //
                // NOR IS A PANE TURNED TO FACE THE MIDDLE OF THE ROOM. A sheet that is one stepped wall has its
                // middle between the steps: each of the hangar's two side sheets is 11 windows on the wall and 6
                // on the tier above, 7.3 m further in, so the upper 6 stood on the room side of their own sheet's
                // middle (about 4.7 m by that split) and all 12 were turned to face in (read live 2026-10-12:
                // 19.9 m from the room's middle, facing it). They drew no view out. From outside the owner saw
                // the wall going by in the side windows; that those 12 paired with the far side's panes is what
                // the pairing rule would do with them and was not read before the fix.
                Vector3 outwardSide = side > 0f ? lead.Normal : -lead.Normal;
                {
                    Vector3 sheetMiddle = Vector3.zero;
                    for (int k = 0; k < list.Count; k++) sheetMiddle += list[k].Corners[0];
                    sheetMiddle /= Mathf.Max(1, list.Count);
                    Vector3 paneMiddle = lead.Corners[0] + lead.Along * ((lo + hi) * 0.5f) + lead.Normal * depth;
                    if (Vector3.Dot(paneMiddle - sheetMiddle, outwardSide) < -0.5f
                        && !TurningFacesTheRoomMiddle(all, paneMiddle, outwardSide, ref roomMiddle, ref roomMiddleRead))
                    {
                        outwardSide = -outwardSide;
                        MelonLogger.Msg($"[WINDOW] {s_ScanBuildingId}: '{(lead.Source != null ? lead.Source.gameObject.name : "?")}' camin disi odanin ortasina bakiyordu ('{backdrop.gameObject.name}' camin oda tarafinda); cevrildi.",
                                        $"[WINDOW] {s_ScanBuildingId}: a pane of '{(lead.Source != null ? lead.Source.gameObject.name : "?")}' had its outside toward the middle of the room, because '{backdrop.gameObject.name}' stands on the room side of the glass; turned to face out.");
                    }
                }
                AddGlassPieceOpening(list, lead, lo, hi, bottom, top, depth, outwardSide, backdrop, result);
                added++;
            }

            // THE ROW'S WINDOWS WITH NO BACKDROP, LAST, so the room windows found by the rule keep the
            // numbers they had before the row named any (the owner had already passed them by number).
            // With nothing painted behind the glass to say which side is outside, the side is taken
            // from the window already found nearest in the same wall: parallel glass, the nearest plane,
            // within a meter. With none, the side facing away from the middle of the room's glass: the
            // Radio Control Hut's middle end-wall pane (2026-10-01) stands between two triangles, which are
            // left out, so no window shares its wall. All the glass in one wall leaves the middle on that
            // wall, which cannot say, and the window is left out and said so.
            int beforeNamed = result.Count;
            Vector3 glassMiddle = Vector3.zero; int glassCount = 0;
            for (int k = 0; k < list.Count; k++) { glassMiddle += list[k].Corners[0]; glassCount++; }
            if (glassCount > 0) glassMiddle /= glassCount;
            for (int w = 0; w < withoutBackdrop.Count; w++)
            {
                var p = withoutBackdrop[w];
                Vector3 center = p.Lead.Corners[0] + p.Lead.Along * ((p.Lo + p.Hi) * 0.5f) + p.Lead.Normal * p.Depth;
                center.y = (p.Bottom + p.Top) * 0.5f;
                int nearest = -1; float nearestPlane = 1f;
                for (int o = 0; o < beforeNamed; o++)
                {
                    if (Mathf.Abs(Vector3.Dot(result[o].Outward, p.Lead.Normal)) < 0.95f) continue;
                    float plane = Mathf.Abs(Vector3.Dot(center - result[o].GlassCenter, p.Lead.Normal));
                    if (plane <= nearestPlane) { nearestPlane = plane; nearest = o; }
                }
                string name = p.Lead.Source != null ? p.Lead.Source.gameObject.name : "?";
                float fromMiddle = glassCount > 0 ? Vector3.Dot(center - glassMiddle, p.Lead.Normal) : 0f;
                if (nearest < 0 && Mathf.Abs(fromMiddle) >= 0.5f)
                {
                    Vector3 away = fromMiddle > 0f ? p.Lead.Normal : -p.Lead.Normal;
                    AddGlassPieceOpening(list, p.Lead, p.Lo, p.Hi, p.Bottom, p.Top, p.Depth, away, null, result);
                    added++;
                    MelonLogger.Msg($"[WINDOW] {s_ScanBuildingId}: '{name}' arkasinda manzara olmadan pencere sayildi (oda penceresi {result.Count - 1}), yonu odanin ortasindan disari.",
                                    $"[WINDOW] {s_ScanBuildingId}: '{name}' taken as a window with no backdrop, by the building's row (room window {result.Count - 1}); its outside is the side facing away from the middle of the room's glass ({fromMiddle:F1} m).");
                    continue;
                }
                if (nearest < 0)
                {
                    MelonLogger.Warning($"[WINDOW] {s_ScanBuildingId}: '{name}' arkasinda manzara yok ve ayni duvarda yonunu verecek pencere yok; atlandi.",
                                        $"[WINDOW] {s_ScanBuildingId}: '{name}' is named as a window with no backdrop, but no window in the same wall says which side is outside; left out.");
                    continue;
                }
                Vector3 outward = Vector3.Dot(result[nearest].Outward, p.Lead.Normal) > 0f ? p.Lead.Normal : -p.Lead.Normal;
                AddGlassPieceOpening(list, p.Lead, p.Lo, p.Hi, p.Bottom, p.Top, p.Depth, outward, null, result);
                added++;
                MelonLogger.Msg($"[WINDOW] {s_ScanBuildingId}: '{name}' arkasinda manzara olmadan pencere sayildi (oda penceresi {result.Count - 1}), yonu oda penceresi {nearest}'den.",
                                $"[WINDOW] {s_ScanBuildingId}: '{name}' taken as a window with no backdrop, by the building's row (room window {result.Count - 1}); its outside is the side room window {nearest}'s faces.");
            }

            if (s_DebugBounds)
                MelonLogger.Msg(joinStacked
                                    ? $"[WINDOW] {label}: tek ebeveyn altinda {list.Count} ayri cam, {added} pencere ({notWindows} tanesinin arkasinda manzara yok, atlandi)."
                                    : $"[WINDOW] {label}: tek cam nesnesinde {list.Count} parca, {added} pencere ({notWindows} tanesinin arkasinda manzara yok, atlandi).",
                                joinStacked
                                    ? $"[WINDOW] {label}: {list.Count} separate panes of glass under one parent, measured as {added} windows ({notWindows} more with no painted outside behind them were left alone as not windows)."
                                    : $"[WINDOW] {label}: one glass object holding {list.Count} pieces of glass, measured as {added} windows ({notWindows} more with no painted outside behind them were left alone as not windows).");
            return added;
        }

        private struct PendingWindow { public GlassPiece Lead; public float Lo, Hi, Bottom, Top, Depth; }

        private static void AddGlassPieceOpening(List<GlassPiece> list, GlassPiece lead, float lo, float hi, float bottom, float top, float depth, Vector3 outward, Renderer backdrop, List<InteriorWindowOpening> result)
        {
            {
                Vector3 up = Vector3.up;
                Vector3 right = Vector3.Cross(up, outward).normalized;
                Vector3 glassCenter = lead.Corners[0] + lead.Along * ((lo + hi) * 0.5f) + lead.Normal * depth;
                glassCenter.y = (bottom + top) * 0.5f;

                var opening = new InteriorWindowOpening();
                opening.PaneRoot = lead.Source != null ? lead.Source.transform : null;
                opening.Backdrop = backdrop;
                opening.Glass = new List<Renderer>();
                for (int k = 0; k < list.Count; k++)
                    if (list[k].Group == lead.Group && list[k].Source != null && !opening.Glass.Contains(list[k].Source)) opening.Glass.Add(list[k].Source);
                opening.Frost = new List<Renderer>();
                opening.Companions = new List<Renderer>();
                opening.PortalOffsetLocal = MANY_PANE_VIEW_OUTSIDE_GLASS;
                opening.HadFrame = false;
                opening.HadSnow = false;
                opening.Outward = outward;
                opening.Up = up;
                opening.Right = right;
                opening.GlassCenter = glassCenter;
                opening.PortalCenter = glassCenter + outward * MANY_PANE_VIEW_OUTSIDE_GLASS;
                opening.Width = (hi - lo) + 2f * BENT_GLASS_VIEW_PAST_EDGE;
                opening.Height = (top - bottom) + 2f * BENT_GLASS_VIEW_PAST_EDGE;
                opening.GlassWidth = hi - lo; opening.GlassHeight = top - bottom;
                result.Add(opening);
            }
        }

        // ─── WINDOW GLASS WITH NO BACKDROP ───
        //
        // A room pane counts as a window only with one of the game's painted outsides behind it, which
        // keeps glass that is not a window out. The Small Barn (2026-10-01) is the first building where
        // Hinterland left a real window without one, so a row can name such panes. Every building also
        // says, once a session, which window glass it left out for this reason, so how common it is gets
        // counted building by building before the rule itself is touched (owner's call, same day).
        private static string[] s_ScanWindowsWithoutBackdrop;
        private static string[][] s_ScanWindowJoins;
        private static float s_ScanManyPaneJoinWithin = -1f;   // meters; < 0 = the ordinary joining only (BuildingWindowOverride.RoomManyPaneJoinWithinCm)

        // Every piece named in one of the row's sets goes into one group before the joining starts, so
        // the set is one window however far apart its pieces stand. Returns the groups so made.
        private static HashSet<int> JoinNamedPieces(List<GlassPiece> list)
        {
            var made = new HashSet<int>();
            if (s_ScanWindowJoins == null) return made;
            foreach (var set in s_ScanWindowJoins)
            {
                if (set == null) continue;
                int group = -1;
                for (int k = 0; k < list.Count; k++)
                {
                    if (list[k].Source == null || System.Array.IndexOf(set, list[k].Source.gameObject.name) < 0) continue;
                    if (group < 0) group = list[k].Group;
                    else { int from = list[k].Group; for (int j = 0; j < list.Count; j++) if (list[j].Group == from) list[j].Group = group; }
                }
                if (group >= 0) made.Add(group);
            }
            return made;
        }
        private static string s_ScanBuildingId;
        private static readonly HashSet<string> s_NotedGlassWithoutBackdrop = new HashSet<string>();

        private static bool GroupNamedWithoutBackdrop(List<GlassPiece> list, int group)
        {
            if (s_ScanWindowsWithoutBackdrop == null) return false;
            for (int k = 0; k < list.Count; k++)
            {
                if (list[k].Group != group || list[k].Source == null) continue;
                string name = list[k].Source.gameObject.name;
                for (int n = 0; n < s_ScanWindowsWithoutBackdrop.Length; n++)
                    if (name == s_ScanWindowsWithoutBackdrop[n]) return true;
            }
            return false;
        }

        private static void NoteGlassWithoutBackdrop(List<GlassPiece> list, int group)
        {
            var names = new List<string>();
            for (int k = 0; k < list.Count; k++)
                if (list[k].Group == group && list[k].Source != null && !names.Contains(list[k].Source.gameObject.name)) names.Add(list[k].Source.gameObject.name);
            string joined = string.Join(" + ", names);
            if (!s_NotedGlassWithoutBackdrop.Add((s_ScanBuildingId ?? "?") + "|" + joined)) return;
            MelonLogger.Msg($"[WINDOW-NO-BACKDROP] {s_ScanBuildingId}: '{joined}' pencere cami ama arkasinda manzara yok; pencere sayilmadi.",
                            $"[WINDOW-NO-BACKDROP] {s_ScanBuildingId}: window glass '{joined}' has no painted outside behind it, so it is not taken as a window. A building row can name it (RoomWindowsWithoutBackdrop).");
        }

        private static int RootOf(int[] parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        // ─── A ROOM'S WINDOWS ARE FOUND ONCE, NOT ONCE PER VIEW ───
        //
        // The view into a room sets up on the approach and the views out of it on the door frame, and
        // each scanned the whole room for its windows: 48 to 52 ms at the hangar every time, twice a
        // visit, both as a hitch (2026-09-23, evidence 0923-13). The answer is kept per room and handed
        // out as a copy, because the views out add panes to their own list. It is thrown away when the
        // room is not the same object any more, or has moved or turned: the openings are in world space.
        private class WindowOpeningsFound
        {
            public GameObject Room;
            public Matrix4x4 Placed;
            public List<InteriorWindowOpening> Openings;
        }
        private static readonly Dictionary<int, WindowOpeningsFound> s_WindowOpeningsFound = new Dictionary<int, WindowOpeningsFound>();

        // A room window whose glass is not a rectangle gets its outline, so its view is cut to the glass
        // (ShapedViews.cs). Measured from the glass's own mesh, in the window's plane, against the
        // rectangle the window was given; the same 80% test as the building's windows.
        private static void MeasureRoomWindowOutlines(List<InteriorWindowOpening> openings, string id)
        {
            string odd = "";
            for (int i = 0; i < openings.Count; i++)
            {
                var o = openings[i];
                if (o.Glass == null || o.Glass.Count == 0) continue;
                float boxWidth = o.GlassWidth > 0.01f ? o.GlassWidth : o.Width, boxHeight = o.GlassHeight > 0.01f ? o.GlassHeight : o.Height;
                if (boxWidth < 0.01f || boxHeight < 0.01f) continue;
                var corners = new List<Vector2>();
                foreach (var r in o.Glass)
                {
                    var filter = r != null ? r.GetComponent<MeshFilter>() : null;
                    Vector3[] vertices; int[] triangles;
                    if (filter == null || !TryReadMesh(filter.sharedMesh, out vertices, out triangles)) continue;
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        Vector3 d = r.transform.TransformPoint(vertices[v]) - o.GlassCenter;
                        corners.Add(new Vector2(Vector3.Dot(d, o.Right), Vector3.Dot(d, o.Up)));
                    }
                }
                var hull = OutlineOf(corners);
                if (hull.Count < 3 || AreaOf(hull) / (boxWidth * boxHeight) >= SHELL_WINDOW_RECTANGLE_SHARE) continue;
                o.Outline = new Vector3[hull.Count];
                for (int h = 0; h < hull.Count; h++) o.Outline[h] = o.GlassCenter + o.Right * hull[h].x + o.Up * hull[h].y;
                openings[i] = o;
                odd += (odd.Length > 0 ? ", " : "") + i + " (" + (AreaOf(hull) / (boxWidth * boxHeight) * 100f).ToString("F0") + "%)";
            }
            if (odd.Length > 0)
                MelonLogger.Msg($"[WINDOW] {id}: dikdortgen olmayan oda pencereleri, goruntu camin kendi seklinde: {odd}.",
                                $"[WINDOW] {id}: room windows that are not rectangles, their views cut to the glass's own outline (share of their rectangle): {odd}.");
        }

        // The room windows a row names as views out only (BuildingWindowOverride.RoomGlassViewOutOnly), marked
        // once when the room is read.
        private static void MarkViewOutOnly(List<InteriorWindowOpening> openings, SeamlessInteriorInstance instance)
        {
            BuildingWindowOverride byHand;
            if (openings == null || instance == null || instance.Config == null) return;
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out byHand) || byHand.RoomGlassViewOutOnly == null) return;
            // STRIPS ARE NOT WINDOWS. Glass set into a door comes with thin pieces besides the pane one looks
            // through: Thompson's Store's doors gave two panes of 83 x 44 cm and three strips 11 and 20 cm
            // wide, one of them 267 cm tall, and each strip became a numbered window with a view of its own in
            // the middle of the room (owner, 2026-10-09: "You have window number 11 in the middle of the
            // building"). Anything of such glass narrower than this is dropped. They come after the room's real
            // windows in the list, so dropping them moves no number the owner has judged.
            const float VIEW_OUT_ONLY_NARROWEST = 0.30f;
            int marked = 0, strips = 0;
            for (int i = openings.Count - 1; i >= 0; i--)
            {
                var o = openings[i];
                if (o.PaneRoot == null || System.Array.IndexOf(byHand.RoomGlassViewOutOnly, o.PaneRoot.name) < 0) continue;
                float wide = o.GlassWidth > 0.01f ? o.GlassWidth : o.Width;
                if (wide < VIEW_OUT_ONLY_NARROWEST) { openings.RemoveAt(i); strips++; continue; }
                o.ViewOutOnly = true; openings[i] = o; marked++;
            }
            if (strips > 0)
                MelonLogger.Msg($"[WINDOW] {instance.Config.InstanceId}: yalnizca disari bakan camdan {strips} dar serit pencere sayilmadi.",
                                $"[WINDOW] {instance.Config.InstanceId}: {strips} narrow strips of the views-out-only glass were left out; they are not windows.");
            if (marked > 0)
                MelonLogger.Msg($"[WINDOW] {instance.Config.InstanceId}: {marked} oda penceresi yalnizca iceriden disari bakiyor (binanin satiri); bina pencereleriyle eslestirilmez.",
                                $"[WINDOW] {instance.Config.InstanceId}: {marked} room windows are views out only, by the building's row; they are never paired with a building window.");
        }

        // The room's windows that may be paired with the building's: all but the views out only.
        internal static List<InteriorWindowOpening> WithoutViewOutOnly(List<InteriorWindowOpening> openings)
        {
            bool any = false;
            for (int i = 0; i < openings.Count && !any; i++) any = openings[i].ViewOutOnly;
            if (!any) return openings;
            var kept = new List<InteriorWindowOpening>(openings.Count);
            for (int i = 0; i < openings.Count; i++) if (!openings[i].ViewOutOnly) kept.Add(openings[i]);
            return kept;
        }

        public static List<InteriorWindowOpening> FindInteriorWindowOpenings(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return new List<InteriorWindowOpening>();
            GameObject room = instance.MasterInterior;
            int key = room.GetInstanceID();
            Matrix4x4 placed = room.transform.localToWorldMatrix;

            WindowOpeningsFound found;
            if (!s_WindowOpeningsFound.TryGetValue(key, out found) || found.Room != room || found.Placed != placed)
            {
                // Room panes the building's row accepts with no backdrop behind them (see
                // BuildingWindowOverride.RoomWindowsWithoutBackdrop), held for this one scan.
                BuildingWindowOverride byHand;
                s_ScanWindowsWithoutBackdrop = instance.Config != null && TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out byHand)
                    ? byHand.RoomWindowsWithoutBackdrop : null;
                s_ScanWindowJoins = instance.Config != null && TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out byHand)
                    ? byHand.RoomWindowJoins : null;
                s_ScanManyPaneJoinWithin = instance.Config != null && TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out byHand)
                    && !float.IsNaN(byHand.RoomManyPaneJoinWithinCm) ? byHand.RoomManyPaneJoinWithinCm / 100f : -1f;
                s_ScanBuildingId = instance.Config != null ? instance.Config.InstanceId : null;
                try { found = new WindowOpeningsFound { Room = room, Placed = placed, Openings = ScanInteriorWindowOpenings(instance) }; }
                finally { s_ScanWindowsWithoutBackdrop = null; s_ScanWindowJoins = null; s_ScanManyPaneJoinWithin = -1f; s_ScanBuildingId = null; }
                MarkViewOutOnly(found.Openings, instance);
                MeasureRoomWindowOutlines(found.Openings, instance.Config != null ? instance.Config.InstanceId : "?");
                s_WindowOpeningsFound[key] = found;
            }

            var copy = new List<InteriorWindowOpening>(found.Openings.Count);
            foreach (var o in found.Openings)
            {
                var c = o;
                c.Glass = o.Glass != null ? new List<Renderer>(o.Glass) : null;
                c.Frost = o.Frost != null ? new List<Renderer>(o.Frost) : null;
                c.Companions = o.Companions != null ? new List<Renderer>(o.Companions) : null;
                copy.Add(c);
            }
            return copy;
        }

        private static List<InteriorWindowOpening> ScanInteriorWindowOpenings(SeamlessInteriorInstance instance)
        {
            var result = new List<InteriorWindowOpening>();
            if (instance == null || instance.MasterInterior == null) return result;

            Transform rootT = instance.MasterInterior.transform;
            Matrix4x4 worldToLocal = rootT.worldToLocalMatrix;

            // The glass is the aperture: a zero-thickness plane carrying the STR_WindowGlassMix
            // material, which is the same material family the author used offline to build the
            // exterior s_ShellWindows table. Finding it by MATERIAL rather than by object name is
            // what lets one rule serve both halves.
            var seenPaneRoots = new HashSet<int>();
            var renderers = instance.MasterInterior.GetComponentsInChildren<MeshRenderer>(true);

            // LOOSE PANES FIRST: a parent holding glass on more than one wall is not a window, it is
            // a house. Its panes are joined into windows by where they stand, and the loop below
            // leaves them alone. See TryAddLoosePaneOpenings.
            var loosePanes = new HashSet<int>();
            {
                var byRoot = new Dictionary<int, List<MeshRenderer>>();
                var rootNames = new Dictionary<int, string>();
                foreach (var candidate in renderers)
                {
                    if (candidate == null || candidate.sharedMaterial == null) continue;
                    if (!candidate.sharedMaterial.name.StartsWith(GLASS_MATERIAL_PREFIX)) continue;
                    if (!candidate.gameObject.activeSelf) continue;   // switched off by the game; see the loop below
                    // A row's many-pane sheet is cut by its own geometry below, not read as one piece.
                    if (IsRoomManyPaneObject(instance, candidate.gameObject.name)) continue;
                    Transform root = FindWindowRoot(candidate.transform, rootT);
                    if (root == null) continue;
                    List<MeshRenderer> under;
                    if (!byRoot.TryGetValue(root.GetInstanceID(), out under)) { under = new List<MeshRenderer>(); byRoot[root.GetInstanceID()] = under; rootNames[root.GetInstanceID()] = root.name; }
                    under.Add(candidate);
                }
                foreach (var pair in byRoot)
                {
                    List<MeshRenderer> under = pair.Value;
                    if (under.Count < LOOSE_PANES_AT_LEAST) continue;
                    GlassPiece first = null; bool otherWall = false;
                    for (int i = 0; i < under.Count && !otherWall; i++)
                    {
                        GlassPiece piece = GlassPieceOf(under[i]);
                        if (piece == null) continue;
                        if (first == null) first = piece;
                        else if (Vector3.Dot(first.Normal, piece.Normal) < LOOSE_PANES_DIFFERENT_WALL_COSINE) otherWall = true;
                    }
                    if (!otherWall) continue;
                    for (int i = 0; i < under.Count; i++) loosePanes.Add(under[i].GetInstanceID());
                    TryAddLoosePaneOpenings(under, renderers, result, rootNames[pair.Key]);
                }
            }

            int wallGroups = 0;
            foreach (var glassRenderer in renderers)
            {
                if (glassRenderer == null) continue;
                if (loosePanes.Contains(glassRenderer.GetInstanceID())) continue;
                // Glass the game itself keeps switched off is no window: Milton house A's sheet copies
                // every pane in one 13 m mesh and was read as an 11 m window mid-room (2026-10-10).
                if (!glassRenderer.gameObject.activeSelf) continue;
                // A SHEET NAMED BY A ROW: read it whether or not the mesh is readable, and cut it
                // into windows by its own geometry. Named per building so no building already
                // looked at gains or loses a window: the hangar's side sheets wear the ordinary
                // glass material, so without a row they take the single-window path and measure as
                // one pane 34.4 x 9.3 m, which the size limit rightly refuses.
                if (IsRoomManyPaneObject(instance, glassRenderer.gameObject.name))
                {
                    int before = result.Count;
                    if (TryAddManyPaneGlassOpenings(glassRenderer, renderers, result, true) > 0)
                        GroupOpeningsByTier(result, before, ref wallGroups, glassRenderer.gameObject.name);
                    continue;
                }

                var mat = glassRenderer.sharedMaterial;
                if (mat == null) continue;
                // The second kind of room glass: one object for every window. See TryAddManyPaneGlassOpenings.
                if (mat.name.StartsWith(MANY_PANE_GLASS_MATERIAL_PREFIX)) { TryAddManyPaneGlassOpenings(glassRenderer, renderers, result); continue; }
                if (!mat.name.StartsWith(GLASS_MATERIAL_PREFIX)) continue;

                // THE WINDOW'S ROOT IS THE NEAREST ANCESTOR THAT OWNS A BACKDROP, not always the
                // glass's parent. Three window styles were surveyed live on 2026-09-18 across all
                // fifteen LakeRegion buildings. The lake-cabin style (Camp Office, Lake Cabins A to F)
                // hangs glass, snow, frame and backdrop directly under one prefab. The Cabin A style
                // (SafeHouseA) does the same with other names and no snow. The trailer style puts
                // the glass one level further down, under its own holder. Taking the parent found 0
                // openings in SafeHouseA and in all five trailers; this finds 7, and 6, 4, 7, 6, 4,
                // with the nine buildings that already worked unchanged (the control).
                // A glass that is bent (a bay in one piece) is several windows. See TryAddBentGlassOpenings.
                if (TryAddBentGlassOpenings(glassRenderer, renderers, result)) continue;

                Transform paneRoot = FindWindowRoot(glassRenderer.transform, rootT);
                if (paneRoot == null) continue;
                if (!seenPaneRoots.Add(paneRoot.GetInstanceID())) continue;   // one opening per window

                Bounds glass, snow, frame, backdrop;
                Renderer backdropRenderer;
                bool hasGlass = TryLocalBoundsOfRole(paneRoot, rootT, worldToLocal, 0, out glass, out backdropRenderer);
                if (!hasGlass) continue;
                bool hasSnow = TryLocalBoundsOfRole(paneRoot, rootT, worldToLocal, 1, out snow, out backdropRenderer);
                bool hasFrame = TryLocalBoundsOfRole(paneRoot, rootT, worldToLocal, 2, out frame, out backdropRenderer);
                bool hasBackdrop = TryLocalBoundsOfRole(paneRoot, rootT, worldToLocal, 3, out backdrop, out backdropRenderer);

                // The thinnest axis of a zero-thickness plane is its normal.
                Vector3 glassSize = glass.size;
                int axis = 0;
                if (glassSize.y < glassSize.x && glassSize.y < glassSize.z) axis = 1;
                else if (glassSize.z < glassSize.x && glassSize.z < glassSize.y) axis = 2;

                // OUTWARD COMES FROM GLASS-TO-SNOW, NOT FROM THE ROOM'S CENTRE. The snow always
                // sits outboard of the glass, so that vector gives the sign directly. A centroid
                // comparison fails on a window near the middle of a room: the Camp Office's
                // WindowC glass sits at local X 0.015 against a room centre of 0.49, and the
                // centroid rule reported it facing the wrong way, contradicting the author's own
                // s_ShellWindows normal for that pane.
                float sign;
                if (hasSnow)
                {
                    sign = (snow.center[axis] - glass.center[axis]) >= 0f ? 1f : -1f;
                }
                else if (hasBackdrop)
                {
                    // No snow: the backdrop is also always outboard, so it answers the same
                    // question a little less precisely.
                    sign = (backdrop.center[axis] - glass.center[axis]) >= 0f ? 1f : -1f;
                }
                else
                {
                    // Nothing to orient against. Skipping is better than guessing: a portal facing
                    // into the room renders the world backwards and looks like a rendering bug
                    // rather than a placement one.
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[WINDOW] {paneRoot.name}: yon belirlenemedi (kar ve arka plan yok), atlandi.",
                                        $"[WINDOW] {paneRoot.name}: could not tell which way it faces (no snow and no backdrop), skipped.");
                    continue;
                }

                float glassPlane = glass.center[axis];
                float target = glassPlane + sign * PORTAL_DEFAULT_OFFSET_FROM_GLASS;

                // Clamp into whatever this pane actually has. Erring inwards only loses snow,
                // which nobody notices; erring outwards slices through a deeper drift or pushes
                // the quad out past the wall, which reads as a bug.
                float inner = glassPlane + sign * PORTAL_PLANE_CLEARANCE;
                if (hasSnow)
                {
                    float snowOuter = sign > 0f ? snow.max[axis] : snow.min[axis];
                    inner = snowOuter + sign * PORTAL_PLANE_CLEARANCE;
                }

                bool hasOuterLimit = false;
                float outer = 0f;
                if (hasFrame)
                {
                    float frameOuter = sign > 0f ? frame.max[axis] : frame.min[axis];
                    outer = frameOuter - sign * PORTAL_PLANE_CLEARANCE;
                    hasOuterLimit = true;
                }
                if (hasBackdrop)
                {
                    float backdropInner = sign > 0f ? backdrop.min[axis] : backdrop.max[axis];
                    float candidate = backdropInner - sign * PORTAL_PLANE_CLEARANCE;
                    if (!hasOuterLimit || (sign > 0f ? candidate < outer : candidate > outer))
                    {
                        outer = candidate;
                        hasOuterLimit = true;
                    }
                }

                float chosen = target;
                bool clampedIn = false, clampedOut = false;
                if (hasOuterLimit && (sign > 0f ? chosen > outer : chosen < outer)) { chosen = outer; clampedIn = true; }
                if (sign > 0f ? chosen < inner : chosen > inner) { chosen = inner; clampedOut = true; }

                // A pane whose limits cross - snow already past the frame - cannot be satisfied.
                // Falling back to the default keeps the portal roughly right rather than putting
                // the quad somewhere arbitrary, and the warning says a building needs a look.
                if (clampedIn && clampedOut)
                {
                    chosen = target;
                    MelonLogger.Warning($"[WINDOW] {paneRoot.name}: kar ve cerceve sinirlari cakisiyor, " +
                                        "sabit mesafeye donuldu.",
                                        $"[WINDOW] {paneRoot.name}: the snow and the frame overlap, fell back to the fixed distance.");
                }

                var opening = new InteriorWindowOpening();
                opening.PaneRoot = paneRoot;
                opening.Backdrop = backdropRenderer;
                opening.Glass = CollectRoleRenderers(paneRoot, 0);
                opening.Frost = CollectRoleRenderers(paneRoot, 1);
                HideFrameSnowSlots(paneRoot);
                opening.PortalOffsetLocal = sign * (chosen - glassPlane);
                opening.ClampedIn = clampedIn;
                opening.ClampedOut = clampedOut;
                opening.HadFrame = hasFrame;
                opening.HadSnow = hasSnow;

                // Local -> world. The in-plane axes are the two that are not the normal, but WHICH
                // of them is "up" cannot be decided by index order: for an X-facing window that
                // would make world Z the up axis, i.e. horizontal. Pick the one actually closest
                // to world up instead.
                //
                // The error is invisible on a square pane, because the camera renders a rotated
                // image and the quad maps it back through the same rotated axes, so the two
                // cancel - and every Camp Office pane is 1.11 x 1.11. On a non-square window the
                // swapped aspect shows immediately.
                int a0 = (axis + 1) % 3, a1 = (axis + 2) % 3;
                Vector3 candidate0 = Vector3.zero; candidate0[a0] = 1f;
                Vector3 candidate1 = Vector3.zero; candidate1[a1] = 1f;
                float up0 = Mathf.Abs(Vector3.Dot(rootT.TransformDirection(candidate0).normalized, Vector3.up));
                float up1 = Mathf.Abs(Vector3.Dot(rootT.TransformDirection(candidate1).normalized, Vector3.up));
                if (up0 > up1) { int swap = a0; a0 = a1; a1 = swap; }   // a1 is the up axis

                Vector3 localGlassCentre = glass.center;
                Vector3 localPortalCentre = glass.center;
                localPortalCentre[axis] = chosen;

                Vector3 n = Vector3.zero; n[axis] = sign;
                Vector3 r = Vector3.zero; r[a0] = 1f;
                Vector3 u = Vector3.zero; u[a1] = 1f;

                opening.GlassCenter = rootT.TransformPoint(localGlassCentre);
                opening.PortalCenter = rootT.TransformPoint(localPortalCentre);
                opening.Outward = rootT.TransformDirection(n).normalized;
                opening.Up = rootT.TransformDirection(u).normalized;

                // Right is DERIVED, not taken from an axis index, so that cross(Right, Up) is
                // Outward. An off-axis projection built from these vectors assumes that
                // handedness; get it wrong and the view comes out mirrored.
                opening.Right = Vector3.Cross(opening.Up, opening.Outward).normalized;

                // Sizes go through the transform too: the clone's scale is non-uniform, so a
                // local extent is not a world extent.
                opening.Width = (rootT.TransformVector(r * glassSize[a0])).magnitude;
                opening.Height = (rootT.TransformVector(u * glassSize[a1])).magnitude;

                // NOT A WINDOW: it does not stand upright, or it is larger than any window is. Before
                // the loose panes were told apart, Grey Mother's whole house came through here as
                // one window 13.2 x 16.8 m facing straight up, and a picture of the outdoors that
                // size lay across the room. A grouping mistake should cost a missing view, never that.
                if (TryAddBandOfWindows(instance, opening, result, ref wallGroups)) continue;
                if (TryAddWindowsCarriedFromBuilding(instance, opening, result)) continue;

                if (Mathf.Abs(opening.Outward.y) > 0.3f || opening.Width > WINDOW_LARGEST || opening.Height > WINDOW_LARGEST)
                {
                    MelonLogger.Warning($"[WINDOW] {paneRoot.name}: pencere degil, atlandi ({opening.Width:F1} x {opening.Height:F1} m, disari yonunun dikey bileseni {opening.Outward.y:F2}).",
                                        $"[WINDOW] {paneRoot.name}: not a window, skipped ({opening.Width:F1} x {opening.Height:F1} m, its outward pointing {opening.Outward.y:F2} up or down).");
                    continue;
                }

                result.Add(opening);
            }

            BuildingWindowOverride byHand;
            if (TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out byHand) && byHand.OpeningsWithNoGlass)
                TryAddOpeningsWithNoGlass(instance, renderers, result);
            if (byHand != null && byHand.RoomGlassSlotObjects != null)
                AddRoomGlassSlotOpenings(byHand.RoomGlassSlotObjects, renderers, rootT, result, instance.Config.InstanceId, GlassSlotJoinWithin(byHand));
            if (byHand != null && byHand.RoomOpeningsByHand != null)
                AddRoomOpeningsByHand(byHand.RoomOpeningsByHand, result, instance.Config.InstanceId);

            if (s_DebugBounds)
            {
                int clamped = 0;
                foreach (var o in result) if (o.ClampedIn || o.ClampedOut) clamped++;
                MelonLogger.Msg($"[WINDOW] {instance.Config.ResolvedInstanceId}: {result.Count} pencere acikligi, " +
                                $"{clamped} tanesi sabit mesafeden kaydirildi.",
                                $"[WINDOW] {instance.Config.ResolvedInstanceId}: {result.Count} window openings in the room, " +
                                $"{clamped} moved off the fixed distance.");
            }

            return result;
        }

        // Windows a row names (RoomGlassSlotObjects) whose glass the rule misses: measured from the submesh
        // whose material says Glass alone; added after the rule's windows so those keep their numbers.
        private static void AddRoomGlassSlotOpenings(string[] names, MeshRenderer[] all, Transform rootT, List<InteriorWindowOpening> result, string id, float joinWithin)
        {
            Vector3 middle;
            if (!TryGetRoomMiddle(all, out middle)) return;

            int added = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null || !r.gameObject.activeSelf) continue;
                bool named = false;
                for (int n = 0; n < names.Length && !named; n++) named = r.gameObject.name.StartsWith(names[n], System.StringComparison.Ordinal);
                if (!named) continue;

                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                Material[] mats = r.sharedMaterials;
                int slot = -1;
                for (int k = 0; k < mats.Length && k < mesh.subMeshCount && slot < 0; k++)
                    if (mats[k] != null && mats[k].name.IndexOf("Glass", System.StringComparison.Ordinal) >= 0) slot = k;
                if (slot < 0) continue;

                Vector3[] vertices; int[] slotTriangles;
                if (!TryReadSlot(mesh, slot, out vertices, out slotTriangles)) continue;
                // An object big enough to hold rooms (the mine's 66 m connector) faces out from its own middle.
                Vector3 own = Vector3.Scale(mesh.bounds.size, r.transform.lossyScale);
                Vector3 from = own.magnitude > SLOT_GLASS_OWN_MIDDLE_ABOVE ? r.transform.TransformPoint(mesh.bounds.center) : middle;
                foreach (var corners in SplitSlotGlassIntoWindows(vertices, slotTriangles, r.transform, joinWithin))
                {
                    AddSlotGlassOpening(r, corners, from, result);
                    added++;
                }
            }
            if (added > 0)
                MelonLogger.Msg($"[WINDOW] {id}: satirdaki {added} pencere cami, cercevesiyle ayni nesnenin alt-mesh'inden olculdu.",
                                $"[WINDOW] {id}: {added} window(s) the row names measured from the glass slot they share with their frame.");
        }

        // A glass submesh can hold several windows and roof lights (the maintenance shed: two garage-door
        // strips of five panes, and skylights). Upright triangles only; pieces joined on one wall across bars
        // and across the shed's cardboarded panes (0.76 m), its two doors standing 7.6 m apart.
        private const float SLOT_GLASS_BAR_GAP = 1.0f;
        // The frame's opening stands past the glass: the view is drawn this much wider and taller in all
        // (the room's frame hides the rest). Set by eye on the shed's garage strips, 2026-10-10.
        private const float SLOT_GLASS_VIEW_EXTRA_WIDTH = 0.20f;
        private const float SLOT_GLASS_VIEW_EXTRA_HEIGHT = 0.08f;
        private const float SLOT_GLASS_OWN_MIDDLE_ABOVE = 30f;

        // One submesh's triangles; an unreadable mesh is read from the GPU, whose index buffer is cut by the
        // submesh's own range (the mine's conveyor connectors, 2026-10-10).
        private static bool TryReadSlot(Mesh mesh, int slot, out Vector3[] vertices, out int[] triangles)
        {
            vertices = null; triangles = null;
            if (mesh.isReadable) { vertices = mesh.vertices; triangles = mesh.GetTriangles(slot); return true; }

            int[] all;
            if (!TryReadMesh(mesh, out vertices, out all)) return false;
            var sub = mesh.GetSubMesh(slot);
            if (sub.indexStart < 0 || sub.indexStart + sub.indexCount > all.Length) return false;
            triangles = new int[sub.indexCount];
            for (int i = 0; i < sub.indexCount; i++)
            {
                int v = all[sub.indexStart + i] + sub.baseVertex;
                if (v < 0 || v >= vertices.Length) return false;
                triangles[i] = v;
            }
            return true;
        }

        // A row's GlassSlotJoinWithinCm, or the bar gap when it has none.
        internal static float GlassSlotJoinWithin(BuildingWindowOverride row)
        {
            return row != null && !float.IsNaN(row.GlassSlotJoinWithinCm) ? row.GlassSlotJoinWithinCm / 100f : SLOT_GLASS_BAR_GAP;
        }

        // joinStacked: rows of panes one above the other are one window too (the mine's three-row windows).
        internal static List<List<Vector3>> SplitSlotGlassIntoWindows(Vector3[] vertices, int[] triangles, Transform t, float joinWithin, bool joinStacked = false)
        {
            var tris = new List<Vector3[]>();
            var levels = new List<Vector3>();
            var parent = new List<int>();
            var byCorner = new Dictionary<Vector3Int, int>();
            for (int k = 0; k + 2 < triangles.Length; k += 3)
            {
                Vector3 p0 = t.TransformPoint(vertices[triangles[k]]), p1 = t.TransformPoint(vertices[triangles[k + 1]]), p2 = t.TransformPoint(vertices[triangles[k + 2]]);
                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                if (cross.sqrMagnitude < 1e-12f || Mathf.Abs(cross.normalized.y) > 0.3f) continue;
                int me = tris.Count;
                tris.Add(new[] { p0, p1, p2 });
                levels.Add(new Vector3(cross.x, 0f, cross.z).normalized);
                parent.Add(me);
                foreach (var p in tris[me])
                {
                    var key = new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));
                    int other;
                    if (byCorner.TryGetValue(key, out other)) parent[FindPiece(parent, me)] = FindPiece(parent, other);
                    else byCorner[key] = me;
                }
            }

            // Pieces, each with its facing, then joined where they share a wall and stand a bar apart.
            var pieces = new Dictionary<int, List<int>>();
            for (int i = 0; i < tris.Count; i++)
            {
                int root = FindPiece(parent, i);
                List<int> members;
                if (!pieces.TryGetValue(root, out members)) { members = new List<int>(); pieces[root] = members; }
                members.Add(i);
            }
            var groups = new List<List<Vector3>>();
            var normals = new List<Vector3>();
            foreach (var members in pieces.Values)
            {
                var corners = new List<Vector3>();
                Vector3 n = Vector3.zero;
                foreach (int i in members)
                {
                    Vector3 level = levels[i];
                    if (n.sqrMagnitude > 0f && Vector3.Dot(level, n) < 0f) level = -level;
                    n += level;
                    corners.AddRange(tris[i]);
                }
                groups.Add(corners);
                normals.Add(n.normalized);
            }
            for (bool joined = true; joined;)
            {
                joined = false;
                for (int a = 0; a < groups.Count && !joined; a++)
                    for (int b = a + 1; b < groups.Count && !joined; b++)
                    {
                        if (Mathf.Abs(Vector3.Dot(normals[a], normals[b])) < BENT_GLASS_SAME_FACING_COSINE) continue;
                        Vector3 n = normals[a], right = Vector3.Cross(Vector3.up, n).normalized;
                        float aLo, aHi, aBottom, aTop, aPlane, bLo, bHi, bBottom, bTop, bPlane;
                        SpanOf(groups[a], right, n, out aLo, out aHi, out aBottom, out aTop, out aPlane);
                        SpanOf(groups[b], right, n, out bLo, out bHi, out bBottom, out bTop, out bPlane);
                        if (Mathf.Abs(aPlane - bPlane) > 0.1f) continue;
                        bool sideBySide = Mathf.Min(aTop, bTop) - Mathf.Max(aBottom, bBottom) >= 0.5f * Mathf.Min(aTop - aBottom, bTop - bBottom)
                                          && Mathf.Max(bLo - aHi, aLo - bHi) <= joinWithin;
                        bool stacked = joinStacked && Mathf.Min(aHi, bHi) - Mathf.Max(aLo, bLo) >= 0.5f * Mathf.Min(aHi - aLo, bHi - bLo)
                                       && Mathf.Max(bBottom - aTop, aBottom - bTop) <= joinWithin;
                        if (!sideBySide && !stacked) continue;
                        groups[a].AddRange(groups[b]);
                        groups.RemoveAt(b); normals.RemoveAt(b);
                        joined = true;
                    }
            }
            return groups;
        }

        private static int FindPiece(List<int> parent, int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        private static void SpanOf(List<Vector3> corners, Vector3 right, Vector3 n, out float lo, out float hi, out float bottom, out float top, out float plane)
        {
            lo = bottom = float.MaxValue; hi = top = float.MinValue; plane = 0f;
            foreach (var p in corners)
            {
                float u = Vector3.Dot(p, right), v = p.y;
                if (u < lo) lo = u; if (u > hi) hi = u;
                if (v < bottom) bottom = v; if (v > top) top = v;
                plane += Vector3.Dot(p, n);
            }
            plane /= corners.Count;
        }

        private static void AddSlotGlassOpening(MeshRenderer r, List<Vector3> corners, Vector3 middle, List<InteriorWindowOpening> result)
        {
            {
                Vector3 normal = Vector3.zero, sum = Vector3.zero;
                for (int k = 0; k + 2 < corners.Count; k += 3)
                {
                    Vector3 cross = Vector3.Cross(corners[k + 1] - corners[k], corners[k + 2] - corners[k]);
                    Vector3 level = new Vector3(cross.x, 0f, cross.z);
                    if (normal.sqrMagnitude > 0f && Vector3.Dot(level, normal) < 0f) level = -level;
                    normal += level;
                    sum += corners[k] + corners[k + 1] + corners[k + 2];
                }
                Vector3 glassMiddle = sum / corners.Count;
                Vector3 outward = normal.normalized;
                if (Vector3.Dot(glassMiddle - middle, outward) < 0f) outward = -outward;

                Vector3 up = Vector3.up;
                Vector3 right = Vector3.Cross(up, outward).normalized;
                float uLo = float.MaxValue, uHi = float.MinValue, vLo = float.MaxValue, vHi = float.MinValue, plane = 0f;
                foreach (var p in corners)
                {
                    float u = Vector3.Dot(p, right), v = Vector3.Dot(p, up);
                    if (u < uLo) uLo = u; if (u > uHi) uHi = u;
                    if (v < vLo) vLo = v; if (v > vHi) vHi = v;
                    plane += Vector3.Dot(p, outward);
                }
                plane /= corners.Count;

                var opening = new InteriorWindowOpening();
                opening.PaneRoot = r.transform;
                opening.Glass = new List<Renderer> { r };
                opening.Frost = new List<Renderer>();
                opening.Companions = new List<Renderer>();
                opening.PortalOffsetLocal = BENT_GLASS_VIEW_OUTSIDE_GLASS;
                opening.Outward = outward;
                opening.Up = up;
                opening.Right = Vector3.Cross(up, outward).normalized;
                opening.GlassCenter = right * ((uLo + uHi) * 0.5f) + up * ((vLo + vHi) * 0.5f) + outward * plane;
                opening.PortalCenter = opening.GlassCenter + outward * BENT_GLASS_VIEW_OUTSIDE_GLASS;
                opening.GlassWidth = uHi - uLo;
                opening.GlassHeight = vHi - vLo;
                opening.Width = opening.GlassWidth + SLOT_GLASS_VIEW_EXTRA_WIDTH;
                opening.Height = opening.GlassHeight + SLOT_GLASS_VIEW_EXTRA_HEIGHT;
                result.Add(opening);
            }
        }

        // Openings set by hand beside a found room window: its wall, width and facing, their own height.
        // Added last, with no glass, so the found windows keep their numbers. See RoomHandOpening.
        private static void AddRoomOpeningsByHand(RoomHandOpening[] rows, List<InteriorWindowOpening> result, string id)
        {
            int found = result.Count;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null || row.LikeRoomWindow < 0 || row.LikeRoomWindow >= found || row.TopCm <= row.BottomCm)
                {
                    MelonLogger.Warning($"[WINDOW] {id}: elle acilan pencere {i} gecersiz, atlandi.", $"[WINDOW] {id}: hand-set room opening {i} is not valid, skipped.");
                    continue;
                }
                var like = result[row.LikeRoomWindow];
                var opening = like;
                opening.Backdrop = null;
                opening.Glass = new List<Renderer>();
                opening.Frost = new List<Renderer>();
                opening.Companions = new List<Renderer>();
                opening.Outline = null;
                opening.WallGroup = 0;
                float middle = (row.BottomCm + row.TopCm) * 0.005f;
                Vector3 slide = like.Up * middle + like.Right * (row.AcrossCm * 0.01f);
                opening.GlassCenter = like.GlassCenter + slide;
                opening.PortalCenter = float.IsNaN(row.PictureOutCm)
                    ? like.PortalCenter + slide
                    : opening.GlassCenter + like.Outward * (row.PictureOutCm * 0.01f);
                if (!float.IsNaN(row.PictureOutCm))
                    opening.CoverOut = Mathf.Max(0f, row.PictureOutCm * 0.01f - COVER_BANK_OUTSIDE_NEEDS - 0.002f);
                if (!float.IsNaN(row.WidthCm))
                {
                    float glassWidth = row.WidthCm * 0.01f;
                    opening.Width = glassWidth + (like.Width - (like.GlassWidth > 0.01f ? like.GlassWidth : like.Width));
                    opening.GlassWidth = like.GlassWidth > 0.01f ? glassWidth : 0f;
                }
                float glassHeight = (row.TopCm - row.BottomCm) * 0.01f;
                opening.Height = glassHeight + (like.Height - (like.GlassHeight > 0.01f ? like.GlassHeight : like.Height));
                opening.GlassHeight = like.GlassHeight > 0.01f ? glassHeight : 0f;
                result.Add(opening);
            }
        }

        // ─── AN OPENING WITH NO GLASS IN IT ───
        //
        // Grey Mother's house has one over its front door: a hole in the wall, 146 x 35 cm, and a
        // piece of the game's painted outside standing in it, 5 cm in from the wall's outer face.
        // There is no glass to read it by, so it is read from the three things that are there: the
        // painted piece says WHERE (a connected piece of a painted sheet with none of the windows
        // found so far within a meter of it), a window on the same wall says which way is out, and
        // the wall's own corners say HOW LARGE, by growing a rectangle from the piece's middle until
        // a corner of the wall stands in the way. Measured in the running game before this was written; the width came
        // out 1.10 times the building's pane, which is what every other window of that house is.
        //
        // The picture of the outdoors stands in the hole, on the glass plane of the windows beside it,
        // and a view from outside starts there too: in the hole, not in the wall beside it.
        private const float NO_GLASS_NEAR_A_WINDOW = 1.0f;
        private const float NO_GLASS_SMALLEST_PIECE = 0.3f;
        private const float NO_GLASS_WALL_EITHER_SIDE = 0.30f;       // of the same wall's glass plane
        private const float NO_GLASS_SAME_WALL_WITHIN = 8f, NO_GLASS_SAME_WALL_PLANE = 0.75f;
        private const float NO_GLASS_MIDDLE_BAND = 0.10f, NO_GLASS_CLEAR_OF_THE_SIDES = 0.05f;
        private const float NO_GLASS_WIDEST = 4f, NO_GLASS_TALLEST = 3f, NO_GLASS_SMALLEST_HOLE = 0.15f;

        // The windows of one wall group each carry their own offset from the room to the building,
        // and a view shared between them will use one offset for all. So how far they disagree is
        // said once per group, when they are paired: in the wall's own frame, in centimeters, across
        // and up and outward, never as a world position. Called from PairWindowOpeningsToShell.
        internal static void LogWallGroupOffsets(string shellName, List<InteriorWindowOpening> openings, List<Vector3> offsets)
        {
            if (openings == null || offsets == null) return;
            int groups = 0;
            for (int i = 0; i < openings.Count; i++) if (openings[i].WallGroup > groups) groups = openings[i].WallGroup;

            for (int g = 1; g <= groups; g++)
            {
                // Each against the group's mean: the offsets themselves hold the whole way from a room
                // parked in the sky to its building, and only how they differ says anything.
                int members = 0; Vector3 mean = Vector3.zero;
                for (int i = 0; i < openings.Count && i < offsets.Count; i++)
                    if (openings[i].WallGroup == g) { mean += offsets[i]; members++; }
                if (members > 0) mean /= members;

                Vector3 lo = Vector3.zero, hi = Vector3.zero; bool first = true;
                var each = new System.Text.StringBuilder();
                for (int i = 0; i < openings.Count && i < offsets.Count; i++)
                {
                    if (openings[i].WallGroup != g) continue;
                    var o = openings[i];
                    Vector3 off = offsets[i] - mean;
                    Vector3 inWall = new Vector3(Vector3.Dot(off, o.Right), Vector3.Dot(off, o.Up), Vector3.Dot(off, o.Outward)) * 100f;
                    if (first) { lo = inWall; hi = inWall; first = false; } else { lo = Vector3.Min(lo, inWall); hi = Vector3.Max(hi, inWall); }
                    each.Append(i).Append(": ").Append(inWall.x.ToString("F1")).Append(" / ").Append(inWall.y.ToString("F1")).Append(" / ").Append(inWall.z.ToString("F1")).Append("; ");
                }
                if (members == 0) continue;
                Vector3 spread = hi - lo;
                MelonLogger.Msg($"[PENCERE-DUVAR] {shellName}: duvar grubu {g}, {members} pencere; odadan binaya kaymanin grup ortalamasindan farki (saga / yukari / disari, cm) {each}en buyuk fark {spread.x:F1} / {spread.y:F1} / {spread.z:F1} cm.",
                                $"[WINDOW-WALL] {shellName}: wall group {g}, {members} windows; how far each window's offset from the room to the building is from the group's mean, across / up / outward in cm: {each}they differ by at most {spread.x:F1} / {spread.y:F1} / {spread.z:F1} cm.");
            }
        }

        // ─── ROOM GLASS WHOSE WINDOWS ARE THE BUILDING'S, CARRIED IN ───
        //
        // The hangar's side walls are glazed in the room by one unreadable sheet a side, each a box
        // round two tiers of windows of five sizes (26 quads, 17 windows). No count and width can cut
        // that. The building's side windows CAN be read, all 34 upright, and the building has said
        // where the room's windows are before: the front band stood within 0.6 m of the door panels
        // and the room's door frames within 2 cm of them. So every window measured on the building
        // is carried into the parked room the way the room itself was carried, and one that lands in
        // this sheet's box is a window of the sheet: that place, that size, facing that way, its
        // picture at the carried point. The depth of the room's own glass there is NOT known, and the
        // carried point may stand some centimeters off it; his eye is the instrument for that.
        private const float CARRIED_WINDOW_BOX_MARGIN = 0.8f;     // m past the sheet's box a carried window may land
        private static bool TryAddWindowsCarriedFromBuilding(SeamlessInteriorInstance instance, InteriorWindowOpening sheet, List<InteriorWindowOpening> result)
        {
            BuildingWindowOverride byHand;
            if (instance == null || instance.Config == null || sheet.PaneRoot == null || instance.ExteriorShell == null) return false;
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out byHand) || byHand.RoomWindowsFromBuilding == null) return false;
            bool named = false;
            for (int i = 0; i < byHand.RoomWindowsFromBuilding.Length && !named; i++)
                if (byHand.RoomWindowsFromBuilding[i] != null && sheet.PaneRoot.name.StartsWith(byHand.RoomWindowsFromBuilding[i], System.StringComparison.Ordinal)) named = true;
            if (!named) return false;

            WindowSpot[] spots;
            if (!TryGetShellWindows(instance, out spots))
            {
                MelonLogger.Warning($"[PENCERE] {sheet.PaneRoot.name}: pencereleri binadan alinacakti ama binanin pencereleri olculemedi; atlandi.",
                                    $"[WINDOW] {sheet.PaneRoot.name}: its windows were to be taken from the building's, and the building's could not be measured; skipped.");
                return true;
            }

            // The sheet's box, from its glass renderers, with room for the carry's error.
            bool any = false; Bounds box = new Bounds();
            if (sheet.Glass != null)
                foreach (var r in sheet.Glass) { if (r == null) continue; if (!any) { box = r.bounds; any = true; } else box.Encapsulate(r.bounds); }
            if (!any) return false;
            box.Expand(CARRIED_WINDOW_BOX_MARGIN * 2f);

            Transform shell = instance.ExteriorShell.transform;
            Quaternion turn = Quaternion.AngleAxis(instance.ParkedYawCorrection, Vector3.up);
            int added = 0;
            for (int s = 0; s < spots.Length; s++)
            {
                Vector3 carried = ParkedInteriorPoint(instance, shell.TransformPoint(new Vector3(spots[s].X, spots[s].Y, spots[s].Z)));
                if (!box.Contains(carried)) continue;
                Vector3 outward = (turn * shell.TransformDirection(new Vector3(spots[s].NX, 0f, spots[s].NZ))).normalized;

                InteriorWindowOpening w = new InteriorWindowOpening();
                w.PaneRoot = sheet.PaneRoot;
                w.GlassCenter = carried;
                w.Outward = outward; w.Up = Vector3.up; w.Right = Vector3.Cross(w.Up, w.Outward).normalized;
                w.Width = spots[s].Width; w.Height = spots[s].Height;
                w.GlassWidth = w.Width; w.GlassHeight = w.Height;
                w.PortalCenter = carried + outward * MANY_PANE_VIEW_OUTSIDE_GLASS;
                w.PortalOffsetLocal = MANY_PANE_VIEW_OUTSIDE_GLASS;
                // The sheet's glass, frost and painted backdrop belong to the first window carried
                // into it: they are one renderer each and are handled once.
                w.Backdrop = added == 0 ? sheet.Backdrop : null;
                w.Glass = added == 0 ? sheet.Glass : new List<Renderer>();
                w.Frost = added == 0 ? sheet.Frost : new List<Renderer>();
                result.Add(w);
                added++;
            }

            MelonLogger.Msg($"[PENCERE-DUVAR] {sheet.PaneRoot.name}: binanin {spots.Length} penceresinden {added} tanesi bu cama tasindi.",
                            $"[WINDOW-WALL] {sheet.PaneRoot.name}: {added} of the building's {spots.Length} windows were carried into this sheet of glass.");
            return true;
        }

        // ─── ONE BAND OF GLASS THAT IS SEVERAL WINDOWS, AND CANNOT BE READ ───
        //
        // The Airfield hangar's end wall is glazed by one object, 23.88 m x 538 cm, whose mesh is not
        // readable: only its box is, and the size limit below rightly refuses a window that size. The
        // building it stands in shows what the band is: eight door panels, the glass in each 121 cm
        // wide, 324 cm from one to the next, and 7 x 324 + 121 = 2389 against the band's 2388, with
        // the heights and the bottoms equal to the centimeter (2026-09-20). So the table says the
        // band is N windows of a width, and the box is cut: first and last flush with its ends, the
        // rest evenly between. Every strip keeps the band's own frame, exactly.
        //
        // The band's glass and frost stay with the FIRST strip only. They are one renderer, and what
        // is done to a window's glass (the frost material on the way in, given back on the way out)
        // must be done to it once.
        private const float BAND_FRAME_DEPTH = 0.6f;                  // m either side of the glass a room frame may stand
        private const float BAND_FRAME_ABOVE_BELOW = 1.5f;            // m a frame's middle may sit above or below the glass's
        private const float BAND_PICTURE_PAST_OUTBOARD_FRAME = 0.04f; // his: the picture 4 cm past a frame standing outside the glass
        private const float BAND_PICTURE_PAST_INBOARD_FRAME = 0.08f;  // and 8 cm past one standing inside it
        internal const float BAND_VIEW_PAST_INBOARD_FRAME = 0.15f;    // looking in: the view starts 15 cm past a frame inside the glass
        private const float BAND_ROOM_PIECE_LARGEST = 100f;     // m; no room piece is larger, a sky or a particle system is
        private static bool TryAddBandOfWindows(SeamlessInteriorInstance instance, InteriorWindowOpening band, List<InteriorWindowOpening> result, ref int wallGroups)
        {
            BuildingWindowOverride byHand;
            if (instance == null || instance.Config == null || band.PaneRoot == null) return false;
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out byHand) || byHand.RoomBands == null) return false;

            RoomWindowBand row = null;
            for (int i = 0; i < byHand.RoomBands.Length && row == null; i++)
                if (byHand.RoomBands[i] != null && band.PaneRoot.name.StartsWith(byHand.RoomBands[i].Object, System.StringComparison.Ordinal)) row = byHand.RoomBands[i];
            if (row == null) return false;

            float width = row.WindowWidthCm * 0.01f;
            if (row.Windows < 2 || width < 0.15f || width * row.Windows > band.Width || band.Height > WINDOW_LARGEST || Mathf.Abs(band.Outward.y) > 0.3f)
            {
                MelonLogger.Warning($"[WINDOW] {band.PaneRoot.name}: tablodaki serit ({row.Windows} x {row.WindowWidthCm:F0} cm) {band.Width:F2} x {band.Height:F2} m'lik cama uymuyor, atlandi.",
                                    $"[WINDOW] {band.PaneRoot.name}: the table's band ({row.Windows} windows of {row.WindowWidthCm:F0} cm) does not fit glass of {band.Width:F2} x {band.Height:F2} m, its outward pointing {band.Outward.y:F2} up or down; skipped.");
                return true;        // named in the table and wrong: no windows, never one 24 m picture
            }

            // WHICH WAY IS OUT. The ordinary path above read it from where the snow piece sits, and at
            // the hangar that put the whole band facing INTO the room: measured live 2026-09-21, the
            // building's eight panels facing +Z in the building's frame and the room's eight strips
            // -Z, positions agreeing to 0.6 m, and the pairing refuses a pair that faces apart, so it
            // found none. A band that glazes a whole wall stands at the edge of its room, and the
            // room's own middle is a safe judge of which side is out, which it is not for a single
            // window near a room's middle (see the ordinary path). Flipped: outward and right, so the
            // frame stays right-handed, and the portal plane put back on the outer side of the glass.
            // The middle is of the room's MESHES of a believable size. Every renderer together gave a
            // box 253 x 228 x 742 m at the hangar, its middle 200 m away, from one particle system or
            // sky piece with enormous bounds, and against that the band looked as if it faced out.
            Vector3 roomMiddle = band.GlassCenter; bool anyRenderer = false; Bounds room = new Bounds();
            if (instance.MasterInterior != null)
                foreach (var r in instance.MasterInterior.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r == null) continue;
                    Bounds rb = r.bounds;
                    if (rb.size.x > BAND_ROOM_PIECE_LARGEST || rb.size.y > BAND_ROOM_PIECE_LARGEST || rb.size.z > BAND_ROOM_PIECE_LARGEST) continue;
                    if (!anyRenderer) { room = rb; anyRenderer = true; } else room.Encapsulate(rb);
                }
            if (anyRenderer) roomMiddle = room.center;
            bool flipped = false;
            if (Vector3.Dot(band.Outward, band.GlassCenter - roomMiddle) < 0f)
            {
                float pastGlass = Mathf.Abs(Vector3.Dot(band.PortalCenter - band.GlassCenter, band.Outward));
                band.Outward = -band.Outward; band.Right = -band.Right;
                band.PortalCenter = band.GlassCenter + band.Outward * pastGlass;
                flipped = true;
            }

            wallGroups++;
            float pitch = (band.Width - width) / (row.Windows - 1);

            // WHERE THE WINDOWS ARE, AND HOW DEEP THEIR FRAMES STAND: from the room's own frames. The
            // hangar's eight interior door panels stand at the building's uneven pitch (311 to 350 cm)
            // and staggered in depth like its sliding doors, 22 cm inside the glass sheet behind the
            // middle doors to 24 cm outside it behind the outer ones. An even cut put pictures up to
            // 27 cm off their frames, and one picture plane for all hid the outer frames or floated
            // behind the middle ones (2026-09-21, six failures in a row, each caught by his eye). His
            // rule: the picture just past the room's frame, the view from just outside the building's.
            // The frames' meshes cannot be read; their box CENTERS can be trusted (equal to the
            // building's paired panes within 2 cm) and their extents cannot (a 3.53 m panel reads
            // 1 m deep once the building's yaw inflates its box), so only centers are used, and the
            // margins past a frame are his: 4 cm where the frame stands outside the glass, 8 cm where
            // it stands inside.
            var frameAcross = new List<float>(); var frameDepth = new List<float>();
            if (anyRenderer)
                foreach (var r in instance.MasterInterior.GetComponentsInChildren<MeshRenderer>(true))
                {
                    // The band's own glass and painted backdrop are not frames; everything else under
                    // the window root may be, and at the hangar the door panels are its children.
                    if (r == null || r == band.Backdrop || (band.Glass != null && band.Glass.Contains(r))) continue;
                    Bounds rb = r.bounds;
                    Vector3 rel = rb.center - band.GlassCenter;
                    float across = Vector3.Dot(rel, band.Right), depthIn = -Vector3.Dot(rel, band.Outward), up = Vector3.Dot(rel, band.Up);
                    if (Mathf.Abs(across) > band.Width * 0.5f || Mathf.Abs(up) > band.Height * 0.5f + BAND_FRAME_ABOVE_BELOW || Mathf.Abs(depthIn) > BAND_FRAME_DEPTH) continue;
                    // A frame is at least as tall as most of the glass and no wider than a few windows:
                    // furniture against the wall and the glass sheet's own pieces are neither.
                    if (rb.size.y < band.Height * 0.6f || rb.size.y > band.Height * 2f) continue;
                    float wide = Mathf.Max(rb.size.x, rb.size.z);
                    if (wide < width * 0.8f || wide > width * 4f) continue;
                    int at = -1;
                    for (int k = 0; k < frameAcross.Count && at < 0; k++) if (Mathf.Abs(frameAcross[k] - across) < width * 0.5f) at = k;
                    if (at < 0) { frameAcross.Add(across); frameDepth.Add(depthIn); }
                }
            bool framesFound = frameAcross.Count == row.Windows;
            if (framesFound)
            {
                // Sorted along the band's right.
                for (int i = 0; i < frameAcross.Count; i++)
                    for (int k = i + 1; k < frameAcross.Count; k++)
                        if (frameAcross[k] < frameAcross[i]) { float t = frameAcross[i]; frameAcross[i] = frameAcross[k]; frameAcross[k] = t; t = frameDepth[i]; frameDepth[i] = frameDepth[k]; frameDepth[k] = t; }
            }

            for (int i = 0; i < row.Windows; i++)
            {
                float along = framesFound ? frameAcross[i] : -band.Width * 0.5f + width * 0.5f + pitch * i;
                InteriorWindowOpening strip = band;         // the band's frame and depth, as they are
                strip.GlassCenter = band.GlassCenter + band.Right * along;
                strip.PortalCenter = band.PortalCenter + band.Right * along;
                strip.FrameDepthKnown = false;
                if (framesFound)
                {
                    float depthIn = frameDepth[i];
                    float plane = -depthIn + (depthIn < 0f ? BAND_PICTURE_PAST_OUTBOARD_FRAME : BAND_PICTURE_PAST_INBOARD_FRAME);
                    strip.PortalCenter = strip.GlassCenter + band.Outward * plane;
                    strip.PortalOffsetLocal = plane;
                    strip.FrameDepthIntoRoom = depthIn; strip.FrameDepthKnown = true;
                }
                strip.Width = width;
                strip.GlassWidth = width; strip.GlassHeight = band.Height;
                strip.WallGroup = wallGroups;
                if (i > 0)
                {
                    strip.Backdrop = null;
                    strip.Glass = new List<Renderer>(); strip.Frost = new List<Renderer>();
                    strip.Companions = null;
                }
                result.Add(strip);
            }

            string framesTr = framesFound ? $", pencereler odanin {row.Windows} cercevesinin ustune" : $", odada {frameAcross.Count} cerceve bulundu ({row.Windows} gerekliydi), esit araliklarla kesildi";
            string framesEn = framesFound ? $", the windows placed on the room's {row.Windows} frames" : $", {frameAcross.Count} frames found in the room ({row.Windows} needed), so cut evenly";
            MelonLogger.Msg($"[PENCERE-DUVAR] {band.PaneRoot.name}: {band.Width:F2} x {band.Height:F2} m'lik cam seridi tabloya gore {row.Windows} pencereye bolundu ({row.WindowWidthCm:F0} cm, araligi {pitch * 100f:F1} cm), duvar grubu {wallGroups}{(flipped ? ", odanin ortasindan disari bakacak sekilde cevrildi" : "")}{framesTr}.",
                            $"[WINDOW-WALL] {band.PaneRoot.name}: a band of glass {band.Width:F2} x {band.Height:F2} m was cut into {row.Windows} windows by the table ({row.WindowWidthCm:F0} cm wide, {pitch * 100f:F1} cm from one to the next), wall group {wallGroups}{(flipped ? ", turned to face away from the room's middle" : "")}{framesEn}.");
            return true;
        }

        private static void TryAddOpeningsWithNoGlass(SeamlessInteriorInstance instance, MeshRenderer[] renderers, List<InteriorWindowOpening> result)
        {
            Transform rootT = instance.MasterInterior.transform;
            string id = instance.Config.ResolvedInstanceId;

            // The room's middle, to check which way is out against.
            bool any = false; Bounds room = new Bounds();
            for (int i = 0; i < result.Count; i++) { if (!any) { room = new Bounds(result[i].GlassCenter, Vector3.zero); any = true; } else room.Encapsulate(result[i].GlassCenter); }
            if (!any) return;                                                                            // a room with no window found is not helped by this

            int found = result.Count;
            for (int i = 0; i < renderers.Length; i++)
            {
                var sheet = renderers[i];
                if (sheet == null || sheet.sharedMaterial == null) continue;
                if (!MatchesWindowRole(3, sheet.gameObject.name, sheet.sharedMaterial.name)) continue;
                var filter = sheet.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                Vector3[] vertices = filter.sharedMesh.vertices;
                int[] triangles = filter.sharedMesh.triangles;
                if (vertices == null || triangles == null || triangles.Length < 3) continue;

                var parent = new int[vertices.Length];
                var byPlace = new Dictionary<Vector3Int, int>();
                for (int v = 0; v < vertices.Length; v++)
                {
                    var place = new Vector3Int(Mathf.RoundToInt(vertices[v].x * 100f), Mathf.RoundToInt(vertices[v].y * 100f), Mathf.RoundToInt(vertices[v].z * 100f));
                    int first;
                    if (byPlace.TryGetValue(place, out first)) parent[v] = first; else { byPlace[place] = v; parent[v] = v; }
                }
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = RootOf(parent, triangles[t]);
                    parent[RootOf(parent, triangles[t + 1])] = a;
                    parent[RootOf(parent, triangles[t + 2])] = a;
                }

                Transform sheetT = sheet.transform;
                var world = new Vector3[vertices.Length];
                var pieces = new Dictionary<int, Bounds>();
                for (int v = 0; v < vertices.Length; v++)
                {
                    world[v] = sheetT.TransformPoint(vertices[v]);
                    int piece = RootOf(parent, v);
                    Bounds bounds;
                    if (pieces.TryGetValue(piece, out bounds)) { bounds.Encapsulate(world[v]); pieces[piece] = bounds; }
                    else pieces[piece] = new Bounds(world[v], Vector3.zero);
                }

                foreach (var pair in pieces)
                {
                    Bounds pieceBounds = pair.Value;
                    if (pieceBounds.size.magnitude < NO_GLASS_SMALLEST_PIECE) continue;
                    bool byAWindow = false;
                    for (int o = 0; o < found && !byAWindow; o++)
                        if (pieceBounds.SqrDistance(result[o].GlassCenter) < NO_GLASS_NEAR_A_WINDOW * NO_GLASS_NEAR_A_WINDOW) byAWindow = true;
                    if (byAWindow) continue;

                    // WHICH WAY THE WALL FACES COMES FROM A WINDOW ON THE SAME WALL, never from the painted
                    // piece. The first version took the piece's first triangle, and the piece over
                    // Grey Mother's door is not flat: it is a bowl, its triangles facing fifty ways
                    // and its corners spread 111 cm in depth. The console trial happened on a
                    // triangle facing nearly right and measured 146 x 35 cm; the mod's own run took
                    // one 40 degrees off, searched a skewed slab of the wall and measured 208 x 143,
                    // which the size check below refused. Same method, a lucky and an unlucky start.
                    // The window whose glass plane passes nearest the piece's middle is on its wall
                    // (here an upstairs window right above, 8 cm); its outward is the wall's, and its
                    // glass plane is where this opening's picture stands, in line with its neighbors.
                    Vector3 pieceMiddle = pieceBounds.center;
                    int sameWall = -1; float nearestPlane = float.MaxValue;
                    for (int o = 0; o < found; o++)
                    {
                        Vector3 toPiece = pieceMiddle - result[o].GlassCenter;
                        Vector3 level = new Vector3(toPiece.x, 0f, toPiece.z);
                        if (level.magnitude > NO_GLASS_SAME_WALL_WITHIN) continue;
                        float fromPlane = Mathf.Abs(Vector3.Dot(toPiece, result[o].Outward));
                        if (fromPlane < nearestPlane) { nearestPlane = fromPlane; sameWall = o; }
                    }
                    if (sameWall < 0 || nearestPlane > NO_GLASS_SAME_WALL_PLANE) continue;
                    Vector3 wallOutward = new Vector3(result[sameWall].Outward.x, 0f, result[sameWall].Outward.z).normalized;
                    Vector3 intoRoom = -wallOutward;
                    Vector3 along = Vector3.Cross(Vector3.up, intoRoom).normalized;
                    // The piece's middle carried onto that glass plane: depth is measured from there.
                    Vector3 middle = pieceMiddle - wallOutward * Vector3.Dot(pieceMiddle - result[sameWall].GlassCenter, wallOutward);

                    // The wall's corners round the piece, in the piece's own frame.
                    float reachAlong = 0f;
                    for (int v = 0; v < world.Length; v++)
                        if (RootOf(parent, v) == pair.Key) reachAlong = Mathf.Max(reachAlong, Mathf.Abs(Vector3.Dot(world[v] - middle, along)));
                    reachAlong = Mathf.Max(reachAlong, 1.2f);
                    float reachUp = Mathf.Max(pieceBounds.extents.y, 0.6f);
                    var corners = new List<Vector2>();
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        var wall = renderers[r];
                        if (wall == null || wall == sheet || wall.sharedMaterial == null) continue;
                        if (MatchesWindowRole(3, wall.gameObject.name, wall.sharedMaterial.name) || MatchesWindowRole(0, wall.gameObject.name, wall.sharedMaterial.name)) continue;
                        if (wall.gameObject.name.IndexOf("Shadow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        var wallFilter = wall.GetComponent<MeshFilter>();
                        if (wallFilter == null || wallFilter.sharedMesh == null || !wallFilter.sharedMesh.isReadable) continue;
                        Transform wallT = wall.transform;
                        // Only a mesh that CONTAINS the hole. By the mesh's own box: a renderer's bounds
                        // cannot be trusted while the room is switched off, which it is when a building
                        // is set up from outside.
                        if (wallFilter.sharedMesh.bounds.SqrDistance(wallT.InverseTransformPoint(middle)) > 0.01f) continue;
                        Vector3[] wallVertices = wallFilter.sharedMesh.vertices;
                        for (int v = 0; v < wallVertices.Length; v++)
                        {
                            Vector3 p = wallT.TransformPoint(wallVertices[v]) - middle;
                            float depth = Vector3.Dot(p, intoRoom);
                            if (Mathf.Abs(depth) > NO_GLASS_WALL_EITHER_SIDE) continue;
                            float x = Vector3.Dot(p, along);
                            if (Mathf.Abs(x) > reachAlong + 0.3f || Mathf.Abs(p.y) > reachUp + 0.3f) continue;
                            corners.Add(new Vector2(x, p.y));
                        }
                    }
                    if (corners.Count == 0) continue;

                    float left = -(reachAlong + 0.3f), right = reachAlong + 0.3f, bottom = -(reachUp + 0.3f), top = reachUp + 0.3f;
                    for (int c = 0; c < corners.Count; c++)
                    {
                        if (Mathf.Abs(corners[c].y) >= NO_GLASS_MIDDLE_BAND) continue;
                        if (corners[c].x < 0f && corners[c].x > left) left = corners[c].x;
                        if (corners[c].x > 0f && corners[c].x < right) right = corners[c].x;
                    }
                    for (int c = 0; c < corners.Count; c++)
                    {
                        if (corners[c].x <= left + NO_GLASS_CLEAR_OF_THE_SIDES || corners[c].x >= right - NO_GLASS_CLEAR_OF_THE_SIDES) continue;
                        if (corners[c].y < 0f && corners[c].y > bottom) bottom = corners[c].y;
                        if (corners[c].y > 0f && corners[c].y < top) top = corners[c].y;
                    }
                    float width = right - left, height = top - bottom;
                    if (width < NO_GLASS_SMALLEST_HOLE || height < NO_GLASS_SMALLEST_HOLE || width > NO_GLASS_WIDEST || height > NO_GLASS_TALLEST
                        || width > 2f * reachAlong + 0.5f || height > 2f * reachUp + 0.5f)
                    {
                        MelonLogger.Warning($"[WINDOW] {id}: camsiz acikligin olcusu anlamsiz ({width * 100f:F0} x {height * 100f:F0} cm), atlandi.",
                                            $"[WINDOW] {id}: an opening with no glass measured {width * 100f:F0} x {height * 100f:F0} cm, which no hole in a wall is; skipped.");
                        continue;
                    }

                    Vector3 outward = -intoRoom;
                    var opening = new InteriorWindowOpening();
                    opening.PaneRoot = sheetT;
                    opening.Backdrop = sheet;
                    opening.Glass = new List<Renderer>();
                    opening.Frost = new List<Renderer>();
                    opening.Companions = new List<Renderer>();
                    opening.PortalOffsetLocal = 0f;
                    opening.HadFrame = false;
                    opening.HadSnow = false;
                    opening.Outward = outward;
                    opening.Up = Vector3.up;
                    opening.Right = Vector3.Cross(Vector3.up, outward).normalized;
                    opening.GlassCenter = middle + along * ((left + right) * 0.5f) + Vector3.up * ((bottom + top) * 0.5f);
                    opening.PortalCenter = opening.GlassCenter;
                    opening.Width = width + 2f * BENT_GLASS_VIEW_PAST_EDGE;
                    opening.Height = height + 2f * BENT_GLASS_VIEW_PAST_EDGE;
                    opening.GlassWidth = width; opening.GlassHeight = height;
                    result.Add(opening);

                    if (s_DebugBounds)
                        MelonLogger.Msg($"[WINDOW] {id}: camsiz bir aciklik pencere olarak okundu, {width * 100f:F0} x {height * 100f:F0} cm ('{sheet.gameObject.name}' boyali parcasinin onundeki duvar deligi).",
                                        $"[WINDOW] {id}: an opening with no glass read as a window, {width * 100f:F0} x {height * 100f:F0} cm (the hole in the wall in front of a piece of the painted outside '{sheet.gameObject.name}').");
                }
            }
        }

        // THE BACKDROP IS KNOWN BY ITS MATERIAL. Every style's painted "outside" wears a material
        // of the GLB_GlowExt family (GLB_GlowExt_B01 on the lake-cabin style, GLB_GlowExt_C on the
        // other two), while its object is named OBJ_LakeCabinInteriorWindow, Obj_WindowCabinABG or
        // OBJ_TrailerWindow. The backdrop is what tells which way a window faces when it carries no
        // snow, which is every window in SafeHouseA and the trailers, so matching it by one
        // style's name is what left those buildings with no windows at all.
        private const string BACKDROP_MATERIAL_PREFIX = "GLB_GlowExt";

        // role: 0 glass, 1 snow, 2 frame, 3 backdrop.
        private static bool MatchesWindowRole(int role, string objName, string matName)
        {
            if (role == 0) return matName.StartsWith(GLASS_MATERIAL_PREFIX);
            if (role == 1) return objName.IndexOf("WindowSnow", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (role == 2) return objName.EndsWith("_LOD0")
                               || objName.IndexOf("WindowFrame", System.StringComparison.OrdinalIgnoreCase) >= 0;
            return objName.IndexOf("InteriorWindow", System.StringComparison.OrdinalIgnoreCase) >= 0
                || matName.StartsWith(BACKDROP_MATERIAL_PREFIX);
        }

        // The nearest ancestor of a glass pane, at most three levels up and never the clone's own
        // root, that has a backdrop somewhere beneath it. Falls back to the glass's parent, which
        // is what this used before, so a window with no backdrop is handled exactly as it was.
        private static Transform FindWindowRoot(Transform glass, Transform cloneRoot)
        {
            Transform current = glass.parent;
            for (int up = 0; up < 3 && current != null && current != cloneRoot; up++, current = current.parent)
            {
                var renderers = current.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null) continue;
                    string matName = r.sharedMaterial != null ? r.sharedMaterial.name : string.Empty;
                    if (MatchesWindowRole(3, r.gameObject.name, matName)) return current;
                }
            }
            return glass.parent;
        }

        // Every renderer of one role under a pane. A pane can carry more than one of each: the
        // frost is a separate mesh per variant, and taking out only the first would clear part of
        // a window and leave the rest of it iced over.
        private static List<Renderer> CollectRoleRenderers(Transform paneRoot, int role)
        {
            var found = new List<Renderer>();

            var renderers = paneRoot.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in renderers)
            {
                if (mr == null) continue;

                string matName = mr.sharedMaterial != null ? mr.sharedMaterial.name : string.Empty;
                string objName = mr.gameObject.name;

                bool match = MatchesWindowRole(role, objName, matName);

                if (match) found.Add(mr);
            }

            return found;
        }

        // role: 0 glass (by material), 1 snow, 2 frame, 3 backdrop.
        //
        // Mesh bounds through the transform rather than Renderer.bounds, because a clone spends
        // most of its life inactive and Renderer.bounds on an inactive object reports a box at
        // the world origin.
        private static bool TryLocalBoundsOfRole(Transform paneRoot, Transform rootT, Matrix4x4 worldToLocal,
                                                 int role, out Bounds bounds, out Renderer roleRenderer)
        {
            bounds = default(Bounds);
            roleRenderer = null;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            var filters = paneRoot.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;

                var mr = mf.GetComponent<MeshRenderer>();
                string matName = (mr != null && mr.sharedMaterial != null) ? mr.sharedMaterial.name : string.Empty;
                string objName = mf.gameObject.name;

                bool match = MatchesWindowRole(role, objName, matName);
                if (!match) continue;

                if (role == 3 && mr != null) roleRenderer = mr;

                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 localToWorld = mf.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = mb.center + new Vector3(
                        ((k & 1) == 0) ? mb.extents.x : -mb.extents.x,
                        ((k & 2) == 0) ? mb.extents.y : -mb.extents.y,
                        ((k & 4) == 0) ? mb.extents.z : -mb.extents.z);
                    Vector3 local = worldToLocal.MultiplyPoint3x4(localToWorld.MultiplyPoint3x4(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
                any = true;
            }

            if (!any) return false;
            bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return true;
        }
    }
}
