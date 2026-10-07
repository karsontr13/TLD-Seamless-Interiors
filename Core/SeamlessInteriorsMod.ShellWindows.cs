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
            s_MeasuredShellWindows[shellName] = spots;      // a failure is remembered too
            return spots != null && spots.Length > 0;
        }

        private class ShellWindowCluster
        {
            public Vector3 Normal;
            public float Plane, ULo, UHi, VLo, VHi;
            public int Triangles;
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
                              || materials[k].name.IndexOf("WindowGlassMix", System.StringComparison.Ordinal) >= 0;
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
                    clusters.RemoveAt(j);
                }

            var result = new WindowSpot[clusters.Count];
            for (int w = 0; w < clusters.Count; w++)
            {
                var c = clusters[w];
                Vector3 right = Vector3.Cross(Vector3.up, c.Normal);
                Vector3 center = c.Normal * c.Plane + right * ((c.ULo + c.UHi) * 0.5f) + Vector3.up * ((c.VLo + c.VHi) * 0.5f);
                result[w] = new WindowSpot(center.x, center.y, center.z, c.Normal.x, c.Normal.z, c.UHi - c.ULo, c.VHi - c.VLo);
            }

            // THE SELF-CHECK, once per shell. Compare with the room's windows, found independently.
            s_MeasuredShellWindows[shellName] = result;       // see MeasureShellWindowsByObject
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
                              || materials[k].name.IndexOf("WindowGlassMix", System.StringComparison.Ordinal) >= 0;
                    if (!glass) continue;
                    if (!filter.sharedMesh.isReadable) { unreadable++; continue; }

                    int count = filter.sharedMesh.GetSubMesh(k).indexCount / 3;
                    int at = owners.IndexOf(owner);
                    if (at < 0) { owners.Add(owner); ownerMesh.Add(filter.sharedMesh); ownerMeshT.Add(filter.transform); ownerSubmesh.Add(k); ownerTriangles.Add(count); }
                    else if (count > ownerTriangles[at]) { ownerMesh[at] = filter.sharedMesh; ownerMeshT[at] = filter.transform; ownerSubmesh[at] = k; ownerTriangles[at] = count; }
                }
            }

            Vector3 middle = any ? shellT.InverseTransformPoint(whole.center) : Vector3.zero;

            var spots = new List<WindowSpot>();
            int glassTriangles = 0;
            for (int o = 0; o < owners.Count; o++)
            {
                Vector3[] vertices = ownerMesh[o].vertices;
                int[] triangles = ownerMesh[o].GetTriangles(ownerSubmesh[o]);
                Transform meshT = ownerMeshT[o];

                bool found = false; Vector3 facing = Vector3.zero;
                float planeSum = 0f; int planes = 0;
                float uLo = float.MaxValue, uHi = float.MinValue, vLo = float.MaxValue, vHi = float.MinValue;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    Vector3 p0 = shellT.InverseTransformPoint(meshT.TransformPoint(vertices[triangles[t]]));
                    Vector3 p1 = shellT.InverseTransformPoint(meshT.TransformPoint(vertices[triangles[t + 1]]));
                    Vector3 p2 = shellT.InverseTransformPoint(meshT.TransformPoint(vertices[triangles[t + 2]]));
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
                    glassTriangles++;
                }
                if (!found) continue;

                Vector3 across = Vector3.Cross(Vector3.up, facing);
                Vector3 middleOfPane = facing * (planeSum / planes) + across * ((uLo + uHi) * 0.5f) + Vector3.up * ((vLo + vHi) * 0.5f);
                spots.Add(new WindowSpot(middleOfPane.x, middleOfPane.y, middleOfPane.z, facing.x, facing.z, uHi - uLo, vHi - vLo));
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
