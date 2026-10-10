using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── WHERE A SHELL'S WINDOWS ARE: MEASURED, WITH THE AUTHOR'S TABLE AS THE OVERRIDE ───
        //
        // s_ShellWindows (ExteriorFireEffects.cs) was measured offline by the author, from the asset
        // bundles, for four shells, on the understanding that shell meshes cannot be read at runtime.
        // They can, at least in LakeRegion: all fifteen buildings' glass submeshes read on 2026-09-18.
        // So a shell with no entry there is measured here, once, the first time it is needed, by the
        // method that table's own header describes: the glass submesh's triangles, outside faces
        // only, clustered into panes, in the shell root's space.
        //
        // THE CONTROL: run against the Camp Office, this reproduced the author's eight rows digit for
        // digit and in the same order. Pane counts were then compared with the room's windows, found
        // independently: they agreed on nine of the thirteen LakeRegion buildings outside the table,
        // and where they did not, the game's own art differs (Lake Cabin D: shell 3, room 4; trailers
        // B and E: shell 6, room 4).
        //
        // THE OWNER'S REASON, 2026-09-18: "It would sure be nice if we could do this automatically. It
        // would allow for automatic windows for new buildings that are added without having to author
        // all of this information." The morning of the same day the decision had gone the other way,
        // to a hand-authored table, because measuring at runtime fails SILENTLY on a building it was
        // not tuned on. That objection is answered, not dropped: every measurement is logged once per
        // shell as a self-check, and a shell that cannot be measured gets NO windows and a warning,
        // never guessed ones.
        //
        // THE TABLE STILL WINS where it has an entry. One is known to need it: on
        // STRSPAWN_CabinAExterior_Prefab this splits the glass into eleven panes where the author,
        // and the room, have seven.
        private static readonly Dictionary<string, WindowSpot[]> s_MeasuredShellWindows = new Dictionary<string, WindowSpot[]>();

        // Panes nearer together than this, in one plane, are one window: glazing bars split a
        // window's glass into several quads (the Camp Office's bar is 6 cm).
        private const float SHELL_WINDOW_JOIN_GAP = 0.15f;
        private const float SHELL_WINDOW_PLANE_TOLERANCE = 0.12f;
        // A pane within 10 degrees of the shell's axis is a window in a square wall.
        private const float SHELL_WINDOW_SQUARE_TO_WALL_COSINE = 0.985f;

        // Shell glass: an STR_WindowGlassMix material, or a building's own-named one painted with that
        // sheet (Coastal House C: STR_HouseExteriorWindows_C01_Blend). The mod's own materials never count.
        private static readonly Dictionary<int, bool> s_ShellGlassMaterial = new Dictionary<int, bool>();

        internal static bool IsShellGlassMaterial(Material material)
        {
            if (material == null) return false;
            int id = material.GetInstanceID();
            bool glass;
            if (s_ShellGlassMaterial.TryGetValue(id, out glass)) return glass;

            string name = material.name;
            glass = name.IndexOf("WindowGlassMix", System.StringComparison.Ordinal) >= 0;
            if (!glass && !name.StartsWith("SeamlessInteriors", System.StringComparison.Ordinal))
            {
                Texture texture = material.mainTexture;
                glass = texture != null && texture.name.IndexOf("WindowGlassMix", System.StringComparison.Ordinal) >= 0;
            }
            s_ShellGlassMaterial[id] = glass;
            return glass;
        }

        internal static bool TryGetShellWindows(SeamlessInteriorInstance instance, out WindowSpot[] spots)
        {
            spots = null;
            if (instance == null || instance.Config == null || instance.ExteriorShell == null) return false;

            string shellName = instance.Config.ExteriorShellPrefabName;
            if (string.IsNullOrEmpty(shellName)) return false;

            // The bound shell has to be the configured one. In a save where a cabin is burnt the mod
            // binds the ruin, and the intact cabin's windows must not be hung on it.
            if (!instance.ExteriorShell.name.StartsWith(shellName, System.StringComparison.Ordinal)) return false;

            if (s_ShellWindows.TryGetValue(shellName, out spots)) return spots != null && spots.Length > 0;
            if (s_MeasuredShellWindows.TryGetValue(shellName, out spots)) return spots != null && spots.Length > 0;

            spots = MeasureShellWindows(instance, shellName);
            spots = AddExtraShellGlass(instance, shellName, spots);
            s_MeasuredShellWindows[shellName] = spots;      // a failure is remembered too
            return spots != null && spots.Length > 0;
        }

        // The row's ExtraShellGlassObjects, one window each, after the mesh's own windows so those keep
        // their numbers. The by-object measure caches its own result under the shell's name; the caller
        // overwrites that with the whole list straight after.
        private static WindowSpot[] AddExtraShellGlass(SeamlessInteriorInstance instance, string shellName, WindowSpot[] spots)
        {
            BuildingWindowOverride byHand;
            if (spots == null || spots.Length == 0) return spots;
            if (!TryGetBuildingWindowOverride(shellName, out byHand) || byHand.ExtraShellGlassObjects == null || byHand.ExtraShellGlassObjects.Length == 0) return spots;

            s_MeasuredShellWindows[shellName] = spots;
            WindowSpot[] extra = MeasureShellWindowsByObject(instance, shellName, byHand.ExtraShellGlassObjects);
            if (extra == null || extra.Length == 0) return spots;

            var all = new WindowSpot[spots.Length + extra.Length];
            System.Array.Copy(spots, all, spots.Length);
            System.Array.Copy(extra, 0, all, spots.Length, extra.Length);
            MelonLogger.Msg($"[PENCERE-OLCUM] {shellName}: tablodaki ek bina camindan {extra.Length} pencere eklendi, numaralari {spots.Length} ve sonrasi; toplam {all.Length}.",
                            $"[WINDOW-MEASURE] {shellName}: {extra.Length} window(s) added from the row's extra building glass, numbered from {spots.Length}; {all.Length} in all.");
            return all;
        }

        private class ShellWindowCluster
        {
            public Vector3 Normal;
            public float Plane, ULo, UHi, VLo, VHi;
            public int Triangles;
            public List<Vector2> Corners = new List<Vector2>();   // the glass's corners across and up, for its outline
        }

        // ─── WINDOWS THAT ARE NOT RECTANGLES ───
        //
        // A view is drawn on a rectangle, so a triangular or other odd-shaped building window got a
        // rectangle over its slanted edges (the Radio Control Hut's end-wall triangles, 2026-10-01,
        // owner: "number four and five are also incorrectly rectangular"). A window whose OUTLINE (the
        // convex hull round all its glass corners) covers less than this share of its rectangle keeps
        // that outline, and its view and dark backing are cut to it (ShapedViews.cs); with no room
        // window to show it is left alone, its own pane as the game made it. The outline, not the
        // glass's area, so the bars between the panes of a many-pane window cannot make a rectangular
        // window look odd. A triangle covers half. Checked in the console: rectangle 100%, triangle
        // 50%, four panes with bars 100%.
        private const float SHELL_WINDOW_RECTANGLE_SHARE = 0.8f;
        private static readonly Dictionary<string, HashSet<int>> s_NotRectangularShellWindows = new Dictionary<string, HashSet<int>>();

        // The convex hull round the points (monotone chain), counter-clockwise.
        internal static List<Vector2> OutlineOf(List<Vector2> points)
        {
            var hull = new List<Vector2>();
            if (points == null || points.Count < 3) return hull;
            var p = new List<Vector2>(points);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 q = pass == 0 ? p[i] : p[p.Count - 1 - i];
                    while (hull.Count >= start + 2)
                    {
                        Vector2 a = hull[hull.Count - 2], b = hull[hull.Count - 1];
                        if ((b.x - a.x) * (q.y - a.y) - (b.y - a.y) * (q.x - a.x) > 1e-9f) break;
                        hull.RemoveAt(hull.Count - 1);
                    }
                    hull.Add(q);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        internal static float AreaOf(List<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++) { Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count]; area += a.x * b.y - b.x * a.y; }
            return Mathf.Abs(area) * 0.5f;
        }

        // The outline of each window that is not a rectangle, in the shell root's space, by window index.
        private static readonly Dictionary<string, Dictionary<int, Vector3[]>> s_ShellWindowOutlines = new Dictionary<string, Dictionary<int, Vector3[]>>();

        internal static Vector3[] ShellWindowOutline(string shellName, int window)
        {
            Dictionary<int, Vector3[]> byWindow; Vector3[] outline;
            return !string.IsNullOrEmpty(shellName) && s_ShellWindowOutlines.TryGetValue(shellName, out byWindow) && byWindow.TryGetValue(window, out outline) ? outline : null;
        }

        internal static bool IsShellWindowNotRectangular(string shellName, int window)
        {
            HashSet<int> set;
            return !string.IsNullOrEmpty(shellName) && s_NotRectangularShellWindows.TryGetValue(shellName, out set) && set.Contains(window);
        }

        private static WindowSpot[] MeasureShellWindows(SeamlessInteriorInstance instance, string shellName)
        {
            Transform shellT = instance.ExteriorShell.transform;

            BuildingWindowOverride byHand;
            if (TryGetBuildingWindowOverride(shellName, out byHand) && byHand.ShellGlassObjects != null && byHand.ShellGlassObjects.Length > 0)
                return MeasureShellWindowsByObject(instance, shellName, byHand.ShellGlassObjects);

            // The most detailed readable mesh that carries a glass submesh.
            Mesh mesh = null; Transform meshT = null; int submesh = -1, bestTriangles = 0, unreadable = 0;
            var renderers = instance.ExteriorShell.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null) continue;

                Material[] materials = renderer.sharedMaterials;
                for (int k = 0; k < materials.Length && k < filter.sharedMesh.subMeshCount; k++)
                {
                    if (materials[k] == null) continue;
                    // By NAME for the hidden pane: each dressed building owns its own copy of that
                    // material, and the shell being measured may be dressed by any of them.
                    bool glass = materials[k].name.IndexOf("SeamlessInteriors_HiddenPane", System.StringComparison.Ordinal) >= 0
                              || IsShellGlassMaterial(materials[k]);
                    if (!glass) continue;
                    if (!filter.sharedMesh.isReadable) { unreadable++; continue; }

                    int count = filter.sharedMesh.GetSubMesh(k).indexCount / 3;
                    if (count > bestTriangles) { bestTriangles = count; mesh = filter.sharedMesh; meshT = filter.transform; submesh = k; }
                }
            }

            if (mesh == null)
            {
                MelonLogger.Warning($"[PENCERE-OLCUM] {shellName}: okunabilir cam alt-mesh yok (okunamayan: {unreadable}) - bu binaya pencere verilmedi.",
                                    $"[WINDOW-MEASURE] {shellName}: no readable glass in the building's mesh (unreadable: {unreadable}) - this building gets no window views.");
                return new WindowSpot[0];
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.GetTriangles(submesh);

            // Into the shell ROOT's space, and the shell's middle, which tells an outside face from an
            // inner one where the glass has both.
            var local = new Vector3[vertices.Length];
            Vector3 middle = Vector3.zero;
            for (int i = 0; i < vertices.Length; i++) { local[i] = shellT.InverseTransformPoint(meshT.TransformPoint(vertices[i])); middle += local[i]; }
            if (vertices.Length > 0) middle /= vertices.Length;

            var clusters = new List<ShellWindowCluster>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p0 = local[triangles[t]], p1 = local[triangles[t + 1]], p2 = local[triangles[t + 2]];
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude < 1e-10f) continue;
                n.Normalize();
                if (Mathf.Abs(n.y) > 0.3f) continue;        // a skylight is not a window to look into

                // Which way the pane faces. A window in a square wall is snapped to the shell's own axis,
                // so leaning glass (Safe House A's leans a degree) and float noise move nothing. A pane
                // in a SLANTED wall keeps its own facing: the Pleasant Valley farmhouse's bay has two
                // side panes 39 degrees off their wall, and snapped to the wall they came out 0.54 m
                // wide instead of 0.70, each split in two because a slanted pane's corners do not share
                // a depth along the wall's axis, with the view drawn on a plane the glass is not on
                // (owner, 2026-09-19: "Oh boy. The farmhouse has angled windows.").
                Vector3 axis = Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector3(Mathf.Sign(n.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(n.z));
                Vector3 level = new Vector3(n.x, 0f, n.z).normalized;
                if (Vector3.Dot(level, axis) < SHELL_WINDOW_SQUARE_TO_WALL_COSINE) axis = level;
                Vector3 center = (p0 + p1 + p2) / 3f;
                Vector3 fromMiddle = center - middle; fromMiddle.y = 0f;
                if (Vector3.Dot(fromMiddle, axis) < 0f) continue;       // an inner face

                Vector3 right = Vector3.Cross(Vector3.up, axis);
                float plane = Vector3.Dot(center, axis);
                float uLo = float.MaxValue, uHi = float.MinValue, vLo = float.MaxValue, vHi = float.MinValue;
                for (int c = 0; c < 3; c++)
                {
                    Vector3 p = c == 0 ? p0 : c == 1 ? p1 : p2;
                    float u = Vector3.Dot(p, right);
                    if (u < uLo) uLo = u; if (u > uHi) uHi = u;
                    if (p.y < vLo) vLo = p.y; if (p.y > vHi) vHi = p.y;
                }

                ShellWindowCluster hit = null;
                for (int w = 0; w < clusters.Count && hit == null; w++)
                    if (ShellWindowTouches(clusters[w], axis, plane, uLo, uHi, vLo, vHi)) hit = clusters[w];

                if (hit == null)
                {
                    hit = new ShellWindowCluster { Normal = axis, Plane = plane, ULo = uLo, UHi = uHi, VLo = vLo, VHi = vHi };
                    clusters.Add(hit);
                }
                else
                {
                    hit.Plane = (hit.Plane * hit.Triangles + plane) / (hit.Triangles + 1);
                    hit.ULo = Mathf.Min(hit.ULo, uLo); hit.UHi = Mathf.Max(hit.UHi, uHi);
                    hit.VLo = Mathf.Min(hit.VLo, vLo); hit.VHi = Mathf.Max(hit.VHi, vHi);
                }
                hit.Triangles++;
                for (int c = 0; c < 3; c++)
                {
                    Vector3 p = c == 0 ? p0 : c == 1 ? p1 : p2;
                    hit.Corners.Add(new Vector2(Vector3.Dot(p, right), p.y));
                }
            }

            // Clusters that grew into each other.
            for (int i = 0; i < clusters.Count; i++)
                for (int j = clusters.Count - 1; j > i; j--)
                {
                    var a = clusters[i]; var b = clusters[j];
                    if (!ShellWindowTouches(a, b.Normal, b.Plane, b.ULo, b.UHi, b.VLo, b.VHi)) continue;
                    a.ULo = Mathf.Min(a.ULo, b.ULo); a.UHi = Mathf.Max(a.UHi, b.UHi);
                    a.VLo = Mathf.Min(a.VLo, b.VLo); a.VHi = Mathf.Max(a.VHi, b.VHi);
                    a.Triangles += b.Triangles;
                    a.Corners.AddRange(b.Corners);
                    clusters.RemoveAt(j);
                }

            var result = new WindowSpot[clusters.Count];
            var notRectangular = new HashSet<int>();
            var outlines = new Dictionary<int, Vector3[]>();
            string odd = "";
            for (int w = 0; w < clusters.Count; w++)
            {
                var c = clusters[w];
                float box = (c.UHi - c.ULo) * (c.VHi - c.VLo);
                var hull = OutlineOf(c.Corners);
                float outline = AreaOf(hull);
                if (box > 1e-4f && outline / box < SHELL_WINDOW_RECTANGLE_SHARE)
                {
                    notRectangular.Add(w);
                    Vector3 hullRight = Vector3.Cross(Vector3.up, c.Normal);
                    var corners = new Vector3[hull.Count];
                    for (int h = 0; h < hull.Count; h++) corners[h] = c.Normal * c.Plane + hullRight * hull[h].x + Vector3.up * hull[h].y;
                    outlines[w] = corners;
                    odd += (odd.Length > 0 ? ", " : "") + w + " (" + (outline / box * 100f).ToString("F0") + "%)";
                }
                Vector3 right = Vector3.Cross(Vector3.up, c.Normal);
                Vector3 center = c.Normal * c.Plane + right * ((c.ULo + c.UHi) * 0.5f) + Vector3.up * ((c.VLo + c.VHi) * 0.5f);
                result[w] = new WindowSpot(center.x, center.y, center.z, c.Normal.x, c.Normal.z, c.UHi - c.ULo, c.VHi - c.VLo);
            }

            // THE SELF-CHECK, once per shell. Compare with the room's windows, found independently.
            s_MeasuredShellWindows[shellName] = result;       // see MeasureShellWindowsByObject
            s_NotRectangularShellWindows[shellName] = notRectangular;
            s_ShellWindowOutlines[shellName] = outlines;
            if (odd.Length > 0)
                MelonLogger.Msg($"[PENCERE-OLCUM] {shellName}: dikdortgen olmayan pencereler, goruntu camin kendi seklinde: {odd}.",
                                $"[WINDOW-MEASURE] {shellName}: windows that are not rectangles, their views cut to the glass's own outline (share of their rectangle the outline covers): {odd}.");
            int roomWindows = -1;
            try { roomWindows = FindInteriorWindowOpenings(instance).Count; } catch { }
            string verdict = result.Length == 0 ? "HIC PENCERE BULUNAMADI"
                           : roomWindows < 0 ? "oda pencereleri sayilamadi"
                           : roomWindows == result.Length ? "oda ile ayni sayida"
                           : $"ODA ILE FARKLI (oda {roomWindows})";
            string line = $"[PENCERE-OLCUM] {shellName}: kabuk meshinden {result.Length} pencere olculdu ({triangles.Length / 3} cam ucgeni, '{meshT.gameObject.name}'), {verdict}.";
            if (SeamlessInteriorsLog.English)
            {
                verdict = result.Length == 0 ? "NO WINDOWS FOUND"
                        : roomWindows < 0 ? "the room's windows could not be counted"
                        : roomWindows == result.Length ? "same count as the room"
                        : $"DIFFERENT FROM THE ROOM (the room has {roomWindows})";
                line = $"[WINDOW-MEASURE] {shellName}: measured {result.Length} windows from the building's mesh ({triangles.Length / 3} glass triangles, '{meshT.gameObject.name}'), {verdict}.";
            }
            if (result.Length == 0 || (roomWindows >= 0 && roomWindows != result.Length)) MelonLogger.Warning(line);
            else MelonLogger.Msg(line);

            return result;
        }

        // ─── A BUILDING WHOSE GLASS IS SPREAD OVER MANY OBJECTS ───
        //
        // The measure above reads ONE mesh, the one with the most glass, which is the whole building
        // at all nineteen buildings looked at before the Airfield hangar. The hangar's glass is on 76
        // renderers. Here the table names the objects whose glass is a window, and each such object is
        // one window: all of its outward glass in one rectangle, whatever bars divide it (a hangar
        // door panel holds three panes stacked, and the owner, 2026-09-20: "I think we can probably
        // just make each one a single camera portal"). Of an object's levels of detail the one with
        // the most glass triangles is read.
        //
        // Two things differ from the one-mesh measure, both because the glass no longer surrounds the
        // building: the middle that tells an outside face from an inner one is the middle of the whole
        // SHELL, not of the glass (the hangar's listed glass is all at one end, and its own middle
        // stands in that end wall); and nothing is joined across objects.
        private static WindowSpot[] MeasureShellWindowsByObject(SeamlessInteriorInstance instance, string shellName, string[] objectNames)
        {
            Transform shellT = instance.ExteriorShell.transform;
            var renderers = instance.ExteriorShell.GetComponentsInChildren<MeshRenderer>(true);

            bool any = false; Bounds whole = new Bounds();
            var owners = new List<Transform>(); var ownerMesh = new List<Mesh>(); var ownerMeshT = new List<Transform>();
            var ownerSubmesh = new List<int>(); var ownerTriangles = new List<int>();
            int unreadable = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null) continue;

                // Of a believable size only: one oversize piece (a ground plane) drags the middle far
                // to one side, and then both side walls of a building read as facing one way
                // (2026-09-21, the hangar).
                Bounds rb = renderer.bounds;
                if (rb.size.x > BAND_ROOM_PIECE_LARGEST || rb.size.y > BAND_ROOM_PIECE_LARGEST || rb.size.z > BAND_ROOM_PIECE_LARGEST) { }
                else if (!any) { whole = rb; any = true; } else whole.Encapsulate(rb);

                // The listed object this renderer belongs to: itself or an ancestor, inside the shell.
                Transform owner = null;
                for (Transform t = renderer.transform; t != null && owner == null; t = t.parent)
                {
                    for (int n = 0; n < objectNames.Length && owner == null; n++)
                        if (t.name.StartsWith(objectNames[n], System.StringComparison.Ordinal)) owner = t;
                    if (t == shellT) break;
                }
                if (owner == null) continue;

                Material[] materials = renderer.sharedMaterials;
                for (int k = 0; k < materials.Length && k < filter.sharedMesh.subMeshCount; k++)
                {
                    if (materials[k] == null) continue;
                    bool glass = materials[k].name.IndexOf("SeamlessInteriors_HiddenPane", System.StringComparison.Ordinal) >= 0
                              || IsShellGlassMaterial(materials[k]);
                    if (!glass) continue;
                    if (!filter.sharedMesh.isReadable) { unreadable++; continue; }

                    int count = filter.sharedMesh.GetSubMesh(k).indexCount / 3;
                    int at = owners.IndexOf(owner);
                    if (at < 0) { owners.Add(owner); ownerMesh.Add(filter.sharedMesh); ownerMeshT.Add(filter.transform); ownerSubmesh.Add(k); ownerTriangles.Add(count); }
                    else if (count > ownerTriangles[at]) { ownerMesh[at] = filter.sharedMesh; ownerMeshT[at] = filter.transform; ownerSubmesh[at] = k; ownerTriangles[at] = count; }
                }
            }

            Vector3 middle = any ? shellT.InverseTransformPoint(whole.center) : Vector3.zero;

            BuildingWindowOverride byHand;
            float splitWithin = TryGetBuildingWindowOverride(shellName, out byHand) && !float.IsNaN(byHand.GlassSlotJoinWithinCm)
                ? GlassSlotJoinWithin(byHand) : -1f;

            var spots = new List<WindowSpot>();
            int glassTriangles = 0;
            for (int o = 0; o < owners.Count; o++)
            {
                Vector3[] vertices = ownerMesh[o].vertices;
                int[] triangles = ownerMesh[o].GetTriangles(ownerSubmesh[o]);
                Transform meshT = ownerMeshT[o];

                // The row asks for the object's panes one by one: cut as the room's slot glass is.
                var pieces = new List<List<Vector3>>();
                if (splitWithin > 0f) pieces.AddRange(SplitSlotGlassIntoWindows(vertices, triangles, meshT, splitWithin, true));
                else
                {
                    var allGlass = new List<Vector3>();
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                        for (int c = 0; c < 3; c++) allGlass.Add(meshT.TransformPoint(vertices[triangles[t + c]]));
                    pieces.Add(allGlass);
                }

                foreach (var piece in pieces)
                {
                    WindowSpot spot; int used;
                    if (!MeasureShellPiece(piece, shellT, middle, out spot, out used)) continue;
                    spots.Add(spot);
                    glassTriangles += used;
                }
            }

            // Cached BEFORE the self-check: a room whose windows are carried from the building's asks
            // for these spots from inside FindInteriorWindowOpenings, and without the cache that is
            // this measure calling itself for ever.
            s_MeasuredShellWindows[shellName] = spots.ToArray();
            int roomWindows = -1;
            try { roomWindows = FindInteriorWindowOpenings(instance).Count; } catch { }
            string tr = $"[PENCERE-OLCUM] {shellName}: cami cok nesneye yayilmis bina, tabloda adi gecen {owners.Count} nesneden {spots.Count} pencere olculdu ({glassTriangles} cam ucgeni, okunamayan {unreadable}); oda {roomWindows}.";
            string en = $"[WINDOW-MEASURE] {shellName}: a building with its glass on many objects, measured {spots.Count} windows from the {owners.Count} objects the table names ({glassTriangles} glass triangles, unreadable {unreadable}); the room has {roomWindows}.";
            if (spots.Count == 0 || (roomWindows >= 0 && roomWindows != spots.Count)) MelonLogger.Warning(tr, en);
            else MelonLogger.Msg(tr, en);

            return spots.ToArray();
        }

        // One window from world-space glass triangles (three corners each), measured in the shell's frame.
        private static bool MeasureShellPiece(List<Vector3> corners, Transform shellT, Vector3 middle, out WindowSpot spot, out int used)
        {
            spot = default(WindowSpot); used = 0;
            {
                bool found = false; Vector3 facing = Vector3.zero;
                float planeSum = 0f; int planes = 0;
                float uLo = float.MaxValue, uHi = float.MinValue, vLo = float.MaxValue, vHi = float.MinValue;
                for (int t = 0; t + 2 < corners.Count; t += 3)
                {
                    Vector3 p0 = shellT.InverseTransformPoint(corners[t]);
                    Vector3 p1 = shellT.InverseTransformPoint(corners[t + 1]);
                    Vector3 p2 = shellT.InverseTransformPoint(corners[t + 2]);
                    Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                    if (n.sqrMagnitude < 1e-10f) continue;
                    n.Normalize();
                    if (Mathf.Abs(n.y) > 0.3f) continue;        // a skylight is not a window to look into

                    // The facing, snapped as in the one-mesh measure above.
                    Vector3 axis = Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector3(Mathf.Sign(n.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(n.z));
                    Vector3 level = new Vector3(n.x, 0f, n.z).normalized;
                    if (Vector3.Dot(level, axis) < SHELL_WINDOW_SQUARE_TO_WALL_COSINE) axis = level;
                    Vector3 center = (p0 + p1 + p2) / 3f;
                    Vector3 fromMiddle = center - middle; fromMiddle.y = 0f;
                    if (Vector3.Dot(fromMiddle, axis) < 0f) continue;       // an inner face

                    // One object is one window facing one way: the first outward triangle says which.
                    if (!found) { facing = axis; found = true; }
                    else if (Vector3.Dot(facing, axis) < 0.9f) continue;

                    Vector3 right = Vector3.Cross(Vector3.up, facing);
                    planeSum += Vector3.Dot(center, facing); planes++;
                    for (int c = 0; c < 3; c++)
                    {
                        Vector3 p = c == 0 ? p0 : c == 1 ? p1 : p2;
                        float u = Vector3.Dot(p, right);
                        if (u < uLo) uLo = u; if (u > uHi) uHi = u;
                        if (p.y < vLo) vLo = p.y; if (p.y > vHi) vHi = p.y;
                    }
                    used++;
                }
                if (!found) return false;

                Vector3 across = Vector3.Cross(Vector3.up, facing);
                Vector3 middleOfPane = facing * (planeSum / planes) + across * ((uLo + uHi) * 0.5f) + Vector3.up * ((vLo + vHi) * 0.5f);
                spot = new WindowSpot(middleOfPane.x, middleOfPane.y, middleOfPane.z, facing.x, facing.z, uHi - uLo, vHi - vLo);
                return true;
            }
        }

        private static bool ShellWindowTouches(ShellWindowCluster c, Vector3 normal, float plane, float uLo, float uHi, float vLo, float vHi)
        {
            if (Vector3.Dot(c.Normal, normal) < 0.9f) return false;
            if (Mathf.Abs(c.Plane - plane) > SHELL_WINDOW_PLANE_TOLERANCE) return false;
            if (uLo > c.UHi + SHELL_WINDOW_JOIN_GAP || uHi < c.ULo - SHELL_WINDOW_JOIN_GAP) return false;
            if (vLo > c.VHi + SHELL_WINDOW_JOIN_GAP || vHi < c.VLo - SHELL_WINDOW_JOIN_GAP) return false;
            return true;
        }
    }
}
