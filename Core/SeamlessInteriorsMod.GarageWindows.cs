using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── QUONSET GARAGE WINDOWS ───
        // Owner, 2026-10-19: the square two-by-two windows open as a pair of casements from their outer jambs, on the
        // window's own hold. The mullion stays; each casement carries its half of the transom, so shut they look as before.

        private static readonly string[] GARAGE_GLASS_MODELS = { "STR_GarageWindowsGlass" };
        private const string GARAGE_FRAMES = "STR_GarageWindowsDoors_Prefab";
        // The casements' own bars are thinner than the hall's: the glass runs to the jambs here.
        private const float GARAGE_SASH_BAR = 0.03f;
        // The mullion's face is clear this far up from where the bars take their texture.
        private const float GARAGE_BAR_SPAN = 0.4f;
        // The transom lies within this of the glass's row seam, on the room side.
        private const float GARAGE_TRANSOM_REACH = 0.07f, GARAGE_TRANSOM_DEPTH = 0.05f;

        private static readonly Dictionary<string, bool> s_GarageWindowKnown = new Dictionary<string, bool>();

        private static bool IsGarageWindow(InteriorWindowOpening opening)
        {
            if (opening.PaneRoot == null || !IsWindowModel(opening, GARAGE_GLASS_MODELS)) return false;
            // Asked every frame from outside; the panes are measured once a window.
            string key = opening.PaneRoot.GetInstanceID() + ":" + opening.GlassCenter.ToString("F2");
            bool known;
            if (s_GarageWindowKnown.TryGetValue(key, out known)) return known;
            MeshFilter glassMf;
            Vector4 left, right;
            float seam;
            known = GaragePanes(opening, out glassMf, out left, out right, out seam);
            s_GarageWindowKnown[key] = known;
            return known;
        }

        // The hall's and the garage's windows: hinged sashes on shared glass.
        private static bool IsHingedWindow(InteriorWindowOpening opening) { return IsHallWindow(opening) || IsGarageWindow(opening) || IsSingleCasementWindow(opening) || IsRadarUpperCasement(opening); }
        // The hinged windows that open all their glass at once.
        private static bool OpensWhole(InteriorWindowOpening opening) { return IsGarageWindow(opening) || IsSingleCasementWindow(opening); }

        // The window's two columns of glass, (left, right, bottom, top) from its centre, and the height where each
        // column's two panes meet. Only a window of four panes, two to a column, counts.
        private static bool GaragePanes(InteriorWindowOpening opening, out MeshFilter glassMf, out Vector4 left, out Vector4 right, out float seam)
        {
            left = right = Vector4.zero; seam = 0f;
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
                    if (Mathf.Abs(b.x - box.x) < 0.05f && Mathf.Abs(b.y - box.y) < 0.05f && Mathf.Abs(b.z - box.z) < 0.05f && Mathf.Abs(b.w - box.w) < 0.05f)
                    { rects[i] = new Vector4(Mathf.Min(b.x, box.x), Mathf.Max(b.y, box.y), Mathf.Min(b.z, box.z), Mathf.Max(b.w, box.w)); merged = true; break; }
                }
                if (!merged) rects.Add(box);
            }
            if (rects.Count != 4) return false;
            var l = new List<Vector4>(); var r2 = new List<Vector4>();
            foreach (var b in rects) ((b.x + b.y) * 0.5f < 0f ? l : r2).Add(b);
            if (l.Count != 2 || r2.Count != 2) return false;
            left = new Vector4(Mathf.Min(l[0].x, l[1].x), Mathf.Max(l[0].y, l[1].y), Mathf.Min(l[0].z, l[1].z), Mathf.Max(l[0].w, l[1].w));
            right = new Vector4(Mathf.Min(r2[0].x, r2[1].x), Mathf.Max(r2[0].y, r2[1].y), Mathf.Min(r2[0].z, r2[1].z), Mathf.Max(r2[0].w, r2[1].w));
            if (left.y >= right.x) return false;
            // Each column's panes meet at one height: the lower one's top.
            seam = (Mathf.Min(l[0].w, l[1].w) + Mathf.Min(r2[0].w, r2[1].w)) * 0.5f;
            return true;
        }

        private static List<WindowSash> BuildGarageSashes(InteriorWindowOpening opening, int index, out string why)
        {
            why = null;
            MeshFilter glassMf;
            Vector4 left, right;
            float seam;
            if (!GaragePanes(opening, out glassMf, out left, out right, out seam)) { why = "its four panes could not be read"; return null; }
            MeshFilter frameMf = null;
            foreach (var mf in opening.PaneRoot.root.GetComponentsInChildren<MeshFilter>(true)) if (mf.name == GARAGE_FRAMES) { frameMf = mf; break; }
            if (frameMf == null || frameMf.sharedMesh == null || !frameMf.sharedMesh.isReadable) { why = "no readable frame"; return null; }
            // A copy of the building's frame, so the transom can be taken out of this room only.
            if (!frameMf.sharedMesh.name.EndsWith("_SI"))
            {
                var copy = Object.Instantiate(frameMf.sharedMesh);
                copy.name = frameMf.sharedMesh.name + "_SI";
                frameMf.sharedMesh = copy;
            }

            var sashes = new List<WindowSash>();
            foreach (bool onRight in new[] { false, true })
            {
                var column = onRight ? right : left;
                // Textured from the mullion's room face, low down.
                var sash = BuildHingedSash(opening, index, index, new List<Vector4> { column }, true, onRight, glassMf, frameMf,
                                           left.y, right.x, left.z, GARAGE_SASH_BAR);
                if (sash == null) continue;
                CarryTransomHalf(sash, opening, frameMf, column, seam);
                sashes.Add(sash);
            }
            if (sashes.Count == 0) why = "no sash could be built";
            return sashes;
        }

        // The frame's transom over this column moves onto the sash: a piece of its own on the sash, its triangles
        // left out of the frame while the sash stands.
        private static void CarryTransomHalf(WindowSash sash, InteriorWindowOpening opening, MeshFilter frameMf, Vector4 column, float seam)
        {
            var mesh = frameMf.sharedMesh;
            var world = WorldVerts(frameMf);
            Vector3 c = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;
            Vector2[] uv = mesh.uv;
            Vector2[] uv2 = mesh.uv2;
            Transform pr = sash.PaneRoot;
            var renderer = frameMf.GetComponent<MeshRenderer>();
            var hidden = new Dictionary<int, HashSet<int>>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] tris = FrameBaseTriangles(mesh, s);
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var uvs2 = new List<Vector2>();
                var own = new HashSet<int>();
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    bool inside = true;
                    for (int k = 0; k < 3 && inside; k++)
                    {
                        Vector3 p = world[tris[t + k]] - c;
                        float r = Vector3.Dot(p, R), u = Vector3.Dot(p, U), o = Vector3.Dot(p, O);
                        inside = r > column.x - 0.01f && r < column.y + 0.01f && Mathf.Abs(u - seam) < GARAGE_TRANSOM_REACH
                              && o < 0.005f && o > -GARAGE_TRANSOM_DEPTH;
                    }
                    if (!inside) continue;
                    own.Add(t);
                    for (int k = 0; k < 3; k++)
                    {
                        int i = tris[t + k];
                        verts.Add(pr.InverseTransformPoint(world[i]));
                        uvs.Add(i < uv.Length ? uv[i] : Vector2.zero);
                        uvs2.Add(i < uv2.Length ? uv2[i] : Vector2.zero);
                    }
                }
                if (own.Count == 0) continue;
                hidden[s] = own;
                var piece = new Mesh { name = "SI_SashTransom" };
                piece.vertices = verts.ToArray();
                piece.uv = uvs.ToArray();
                if (uv2.Length > 0) piece.uv2 = uvs2.ToArray();
                var order = new int[verts.Count];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                piece.triangles = order;
                piece.RecalculateNormals();
                piece.RecalculateBounds();
                var go = new GameObject("SI_SashTransom");
                go.layer = sash.Go.layer;
                go.transform.SetParent(sash.Go.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = piece;
                var pieceRenderer = go.AddComponent<MeshRenderer>();
                var materials = renderer != null ? renderer.sharedMaterials : null;
                if (materials != null && s < materials.Length) pieceRenderer.sharedMaterial = materials[s];
                if (renderer != null)
                {
                    pieceRenderer.lightProbeUsage = renderer.lightProbeUsage;
                    pieceRenderer.lightmapIndex = renderer.lightmapIndex;
                    pieceRenderer.lightmapScaleOffset = renderer.lightmapScaleOffset;
                    // The sash's bars wear the transom's material: the frame's first one may be another part's.
                    if (materials != null && s < materials.Length) SetSashMaterial(sash, materials[s]);
                }
            }
            if (hidden.Count > 0) HideFrameTriangles(mesh, sash, hidden);
        }

        // ─── THE TRANSOM OUTSIDE ───
        // The building's own transom stood across the opened pair (owner, 2026-10-19): it becomes a piece of its own,
        // shown while the casements are shut, as the hall's upper mullion is.
        private const float GARAGE_SHELL_TRANSOM_BAND = 0.08f, GARAGE_SHELL_TRANSOM_OUT = 0.1f;

        private static void SplitGarageShellTransoms(SeamlessInteriorInstance instance, List<ShellAperture> apertures, List<int> roomWindows, List<InteriorWindowOpening> roomOpenings)
        {
            try
            {
                if (instance == null || instance.ExteriorShell == null) return;
                int split = 0;
                for (int a = 0; a < apertures.Count && a < roomWindows.Count; a++)
                {
                    int room = roomWindows[a];
                    if (room < 0 || room >= roomOpenings.Count || !IsGarageWindow(roomOpenings[room])) continue;
                    var ap = apertures[a];
                    foreach (var mf in instance.ExteriorShell.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var renderer = mf.GetComponent<MeshRenderer>();
                        if (mf.sharedMesh == null || !mf.sharedMesh.isReadable || renderer == null) continue;
                        if (!renderer.bounds.Contains(ap.Center)) continue;
                        var made = mf.transform.Find(MULLION_TOP + a);
                        if (made != null) { s_HallMullionTops[MullionTopKey(instance, a)] = made.gameObject; split++; break; }
                        if (SplitShellTransom(mf, renderer, ap, a, MullionTopKey(instance, a))) { split++; break; }
                    }
                }
                if (split > 0 && s_DebugBounds)
                    MelonLoader.MelonLogger.Msg($"[GARAJ-PENCERE] Dis kabukta {split} pencerenin yatay kaydi ayrildi.", $"[GARAGE-WINDOW] The transom of {split} windows split off on the shell.");
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[GARAJ-PENCERE] Dis kabuk: {ex.Message}", $"[GARAGE-WINDOW] Shell: {ex.Message}");
            }
        }

        private static bool SplitShellTransom(MeshFilter mf, MeshRenderer renderer, ShellAperture ap, int aperture, string key)
        {
            var mesh = mf.sharedMesh;
            var world = WorldVerts(mf);
            float halfW = ap.Width * 0.5f + 0.02f;
            var taken = new Dictionary<int, List<int>>();
            var kept = new Dictionary<int, List<int>>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                var mine = new List<int>();
                var rest = new List<int>(tris.Length);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    bool inside = true;
                    for (int k = 0; k < 3 && inside; k++)
                    {
                        Vector3 p = world[tris[t + k]] - ap.Center;
                        float r = Vector3.Dot(p, ap.Right), u = Vector3.Dot(p, ap.Up), o = Vector3.Dot(p, ap.Outward);
                        inside = Mathf.Abs(r) < halfW && Mathf.Abs(u) < GARAGE_SHELL_TRANSOM_BAND && o > -0.01f && o < GARAGE_SHELL_TRANSOM_OUT;
                    }
                    var to = inside ? mine : rest;
                    to.Add(tris[t]); to.Add(tris[t + 1]); to.Add(tris[t + 2]);
                }
                if (mine.Count > 0) taken[s] = mine;
                kept[s] = rest;
            }
            if (taken.Count == 0) return false;

            // On a copy: the scene's mesh stays the game's.
            if (!mesh.name.EndsWith("_SI"))
            {
                var copy = Object.Instantiate(mesh);
                copy.name = mesh.name + "_SI";
                mf.sharedMesh = copy;
                mesh = copy;
            }
            foreach (var pair in kept) mesh.SetTriangles(pair.Value.ToArray(), pair.Key);

            Vector3[] local = mesh.vertices;
            Vector2[] uv = mesh.uv;
            Vector2[] uv2 = mesh.uv2;
            var holder = new GameObject(MULLION_TOP + aperture);
            holder.layer = mf.gameObject.layer;
            holder.transform.SetParent(mf.transform, false);
            var materials = renderer.sharedMaterials;
            foreach (var pair in taken)
            {
                var verts = new Vector3[pair.Value.Count];
                var uvs = new Vector2[verts.Length];
                var uvs2 = new Vector2[verts.Length];
                var order = new int[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    int v = pair.Value[i];
                    verts[i] = local[v];
                    if (v < uv.Length) uvs[i] = uv[v];
                    if (v < uv2.Length) uvs2[i] = uv2[v];
                    order[i] = i;
                }
                var piece = new Mesh { name = "SI_ShellTransom" };
                piece.vertices = verts;
                piece.uv = uvs;
                if (uv2.Length > 0) piece.uv2 = uvs2;
                piece.triangles = order;
                piece.RecalculateNormals();
                piece.RecalculateBounds();
                var go = new GameObject("SI_ShellTransom_" + pair.Key);
                go.layer = mf.gameObject.layer;
                go.transform.SetParent(holder.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = piece;
                var pieceRenderer = go.AddComponent<MeshRenderer>();
                if (pair.Key < materials.Length) pieceRenderer.sharedMaterial = materials[pair.Key];
                pieceRenderer.lightProbeUsage = renderer.lightProbeUsage;
                pieceRenderer.lightmapIndex = renderer.lightmapIndex;
                pieceRenderer.lightmapScaleOffset = renderer.lightmapScaleOffset;
            }
            s_HallMullionTops[key] = holder;
            return true;
        }

        // ─── FRAME TRIANGLES TAKEN BY SASHES ───
        // As the shared glass, but per submesh: the garage frame holds two.

        private sealed class FrameCutRecord
        {
            public int[][] Base;
            public readonly Dictionary<WindowSash, Dictionary<int, HashSet<int>>> Hidden = new Dictionary<WindowSash, Dictionary<int, HashSet<int>>>();
        }
        private static readonly Dictionary<Mesh, FrameCutRecord> s_FrameCuts = new Dictionary<Mesh, FrameCutRecord>();

        private static int[] FrameBaseTriangles(Mesh mesh, int submesh)
        {
            FrameCutRecord cut;
            if (s_FrameCuts.TryGetValue(mesh, out cut) && submesh < cut.Base.Length) return cut.Base[submesh];
            return mesh.GetTriangles(submesh);
        }

        private static void HideFrameTriangles(Mesh mesh, WindowSash sash, Dictionary<int, HashSet<int>> triangles)
        {
            FrameCutRecord cut;
            if (!s_FrameCuts.TryGetValue(mesh, out cut))
            {
                cut = new FrameCutRecord { Base = new int[mesh.subMeshCount][] };
                for (int s = 0; s < mesh.subMeshCount; s++) cut.Base[s] = mesh.GetTriangles(s);
                s_FrameCuts[mesh] = cut;
            }
            cut.Hidden[sash] = triangles;
            ApplyFrameCut(mesh, cut);
        }

        private static void ShowFrameTriangles(WindowSash sash)
        {
            Mesh done = null;
            foreach (var pair in s_FrameCuts)
            {
                if (!pair.Value.Hidden.Remove(sash)) continue;
                if (pair.Key != null) ApplyFrameCut(pair.Key, pair.Value);
                if (pair.Value.Hidden.Count == 0) done = pair.Key;
                break;
            }
            if (done != null) s_FrameCuts.Remove(done);
        }

        private static void ApplyFrameCut(Mesh mesh, FrameCutRecord cut)
        {
            for (int s = 0; s < cut.Base.Length; s++)
            {
                var kept = new List<int>(cut.Base[s].Length);
                for (int t = 0; t + 2 < cut.Base[s].Length; t += 3)
                {
                    bool hidden = false;
                    foreach (var sets in cut.Hidden.Values)
                    {
                        HashSet<int> set;
                        if (sets.TryGetValue(s, out set) && set.Contains(t)) { hidden = true; break; }
                    }
                    if (!hidden) { kept.Add(cut.Base[s][t]); kept.Add(cut.Base[s][t + 1]); kept.Add(cut.Base[s][t + 2]); }
                }
                mesh.SetTriangles(kept.ToArray(), s);
            }
        }
    }
}
