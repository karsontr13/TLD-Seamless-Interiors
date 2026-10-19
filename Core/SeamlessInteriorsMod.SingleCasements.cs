using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── SINGLE-PANE CASEMENTS ───
        // Owner, 2026-10-19 (Coastal House E): a square window of one pane opens as one casement, hinged on its left
        // jamb, on the window's own hold. It stands in the frame's reveal; the frame is the room's mesh around it.

        private static readonly string[] SINGLE_CASEMENT_MODELS = { "STR_GlassPaneSquare_Single" };
        private const float SINGLE_CASEMENT_BAR = 0.04f;
        // The frame is looked for this far round the glass, and this deep.
        private const float SINGLE_FRAME_REACH = 0.25f, SINGLE_FRAME_DEPTH = 0.12f;

        private static bool IsSingleCasementWindow(InteriorWindowOpening opening)
        {
            return opening.PaneRoot != null && IsWindowModel(opening, SINGLE_CASEMENT_MODELS);
        }

        private static List<WindowSash> BuildSingleCasement(InteriorWindowOpening opening, int index, out string why)
        {
            why = null;
            var glassMf = opening.PaneRoot.GetComponent<MeshFilter>();
            if (glassMf == null || glassMf.sharedMesh == null || !glassMf.sharedMesh.isReadable) { why = "no readable glass"; return null; }
            Vector4 reveal;
            var frameMf = FindRoomFrame(opening, glassMf, out reveal);
            if (frameMf == null) { why = "no frame found round the glass"; return null; }
            // The pane's mesh is the model's, worn by every such window: this one gets its own.
            if (!glassMf.sharedMesh.name.EndsWith("_SI"))
            {
                var copy = Object.Instantiate(glassMf.sharedMesh);
                copy.name = glassMf.sharedMesh.name + "_SI";
                glassMf.sharedMesh = copy;
            }
            // Textured from the frame's casing beside the left jamb.
            var sash = BuildHingedSash(opening, index, index, new List<Vector4> { reveal }, true, false, glassMf, frameMf,
                                       reveal.x - 0.06f, reveal.y, reveal.z, SINGLE_CASEMENT_BAR);
            if (sash == null) { why = "no sash could be built"; return null; }
            return new List<WindowSash> { sash };
        }

        // ─── RADAR STATION ───
        // Owner, 2026-10-19: the tall narrow windows' upper pane swings in from its left jamb; the small one under the
        // transom stays. The room's glass and frames are not readable: copies are read back from the GPU.

        private static readonly string[] RADAR_GLASS_MODELS = { "STR_RadarStation_InteriorWindowGlass" };
        private const string RADAR_FRAMES = "STR_RadarStation_InteriorMetalFrames";
        // The casing's front runs the window's height: the bars take it whole from near its bottom, without repeating.
        private const float RADAR_BAR_SPAN = 1.2f;

        // The tall narrow ones only, by their glass's proportions.
        private static bool IsRadarUpperCasement(InteriorWindowOpening opening)
        {
            return opening.PaneRoot != null && IsWindowModel(opening, RADAR_GLASS_MODELS) && opening.GlassHeight > opening.GlassWidth * 1.8f;
        }

        private static List<WindowSash> BuildRadarCasement(InteriorWindowOpening opening, int index, out string why)
        {
            why = null;
            var glassMf = opening.PaneRoot.GetComponent<MeshFilter>();
            MeshFilter frameMf = null;
            foreach (var mf in opening.PaneRoot.root.GetComponentsInChildren<MeshFilter>(true)) if (mf.name == RADAR_FRAMES) { frameMf = mf; break; }
            if (glassMf == null || frameMf == null || !MakeReadable(glassMf) || !MakeReadable(frameMf)) { why = "glass or frame could not be read"; return null; }

            // The glass's two panes, stacked: the upper one, inside the frame's reveal and above the transom.
            Vector3 c = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;
            float halfW = opening.GlassWidth * 0.5f, halfH = opening.GlassHeight * 0.5f;
            var glassWorld = WorldVerts(glassMf);
            int[] glassTris = SharedGlassBaseTriangles(OriginalGlassMesh(glassMf.sharedMesh));
            float seam = float.MaxValue, gx0 = float.MaxValue, gx1 = float.MinValue, gy1 = float.MinValue;
            for (int t = 0; t + 2 < glassTris.Length; t += 3)
            {
                float u0 = float.MaxValue, u1 = float.MinValue, r0 = float.MaxValue, r1 = float.MinValue;
                bool own = true;
                for (int k = 0; k < 3 && own; k++)
                {
                    Vector3 p = glassWorld[glassTris[t + k]] - c;
                    float r = Vector3.Dot(p, R), u = Vector3.Dot(p, U);
                    own = Mathf.Abs(Vector3.Dot(p, O)) < 0.03f && Mathf.Abs(r) < halfW + 0.05f && Mathf.Abs(u) < halfH + 0.05f;
                    r0 = Mathf.Min(r0, r); r1 = Mathf.Max(r1, r); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                }
                if (!own) continue;
                gx0 = Mathf.Min(gx0, r0); gx1 = Mathf.Max(gx1, r1); gy1 = Mathf.Max(gy1, u1);
                // The upper pane's triangles reach the top; their lowest point is the seam.
                if (u1 > halfH * 0.5f) seam = Mathf.Min(seam, u0);
            }
            if (seam == float.MaxValue || seam < -halfH * 0.9f) { why = "its panes could not be read"; return null; }

            Vector4 reveal;
            if (FindRoomFrame(opening, glassMf, out reveal) != frameMf) reveal = new Vector4(gx0, gx1, seam, gy1);
            // The transom's top: the frame's highest point just round the seam.
            float transomTop = seam;
            foreach (var w in WorldVerts(frameMf))
            {
                Vector3 p = w - c;
                float r = Vector3.Dot(p, R), u = Vector3.Dot(p, U), o = Vector3.Dot(p, O);
                if (Mathf.Abs(r) < halfW * 0.8f && Mathf.Abs(u - seam) < 0.06f && o > -0.06f && o < 0.03f) transomTop = Mathf.Max(transomTop, u);
            }
            var pane = new Vector4(Mathf.Max(gx0, reveal.x), Mathf.Min(gx1, reveal.y), transomTop, Mathf.Min(gy1, reveal.w));
            // Textured from the casing's flat front 9 cm out from the jamb: nearer, its chamfer streaked the bars.
            var sash = BuildHingedSash(opening, index, index, new List<Vector4> { pane }, true, false, glassMf, frameMf,
                                       pane.x - 0.11f, pane.y, -halfH, SINGLE_CASEMENT_BAR, RADAR_BAR_SPAN);
            if (sash == null) { why = "no sash could be built"; return null; }
            // The bars wear the frame's own material round this window, not its first one.
            var material = FrameMaterialNear(frameMf, c, R, U, O, halfW, halfH);
            if (material != null) SetSashMaterial(sash, material);
            return new List<WindowSash> { sash };
        }

        // A mesh read back from the GPU in place of one the game keeps unreadable (the room's own, shared by its windows).
        private static bool MakeReadable(MeshFilter mf)
        {
            if (mf.sharedMesh == null) return false;
            if (mf.sharedMesh.isReadable) return true;
            var renderer = mf.GetComponent<MeshRenderer>();
            var copy = ReadableCopyFromGpu(mf.sharedMesh, renderer != null ? renderer.sharedMaterials.Length : 1);
            if (copy == null) return false;
            mf.sharedMesh = copy;
            return true;
        }

        // The material of the submesh with the most triangles round the window.
        private static Material FrameMaterialNear(MeshFilter frameMf, Vector3 c, Vector3 R, Vector3 U, Vector3 O, float halfW, float halfH)
        {
            var renderer = frameMf.GetComponent<MeshRenderer>();
            if (renderer == null) return null;
            var materials = renderer.sharedMaterials;
            var world = WorldVerts(frameMf);
            int best = -1, bestCount = 0;
            for (int s = 0; s < frameMf.sharedMesh.subMeshCount && s < materials.Length; s++)
            {
                int count = 0;
                foreach (int i in frameMf.sharedMesh.GetTriangles(s))
                {
                    Vector3 p = world[i] - c;
                    if (Mathf.Abs(Vector3.Dot(p, R)) < halfW + 0.15f && Mathf.Abs(Vector3.Dot(p, U)) < halfH + 0.15f && Mathf.Abs(Vector3.Dot(p, O)) < 0.15f) count++;
                }
                if (count > bestCount) { bestCount = count; best = s; }
            }
            return best >= 0 ? materials[best] : null;
        }

        // The room's mesh with the most points round the glass, and its reveal: (left, right, bottom, top) from the
        // glass's centre, the innermost frame edges past the glass's middle on each side.
        private static MeshFilter FindRoomFrame(InteriorWindowOpening opening, MeshFilter glassMf, out Vector4 reveal)
        {
            reveal = Vector4.zero;
            Vector3 c = opening.GlassCenter, R = opening.Right, U = opening.Up, O = opening.Outward;
            float halfW = opening.GlassWidth * 0.5f, halfH = opening.GlassHeight * 0.5f;
            MeshFilter best = null;
            int bestCount = 0;
            Vector4 bestReveal = Vector4.zero;
            foreach (var mf in opening.PaneRoot.root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == glassMf || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var renderer = mf.GetComponent<MeshRenderer>();
                if (renderer == null || !mf.gameObject.activeInHierarchy) continue;
                string n = mf.name;
                if (n.Contains("Glass") || n.Contains("Frost") || n.Contains("Snow") || n.Contains("Glow") || n.Contains("Shadow")) continue;
                if (!renderer.bounds.Intersects(new Bounds(c, new Vector3(halfW + halfH, halfW + halfH, halfW + halfH) * 2f))) continue;
                var world = WorldVerts(mf);
                int count = 0;
                float left = float.MinValue, right = float.MaxValue, bottom = float.MinValue, top = float.MaxValue;
                foreach (var w in world)
                {
                    Vector3 p = w - c;
                    float r = Vector3.Dot(p, R), u = Vector3.Dot(p, U), o = Vector3.Dot(p, O);
                    if (Mathf.Abs(r) > halfW + SINGLE_FRAME_REACH || Mathf.Abs(u) > halfH + SINGLE_FRAME_REACH || o < -SINGLE_FRAME_DEPTH || o > 0.02f) continue;
                    count++;
                    // The reveal: the innermost edges, beyond the middle half of the glass on each side.
                    if (Mathf.Abs(u) < halfH)
                    {
                        if (r < -halfW * 0.5f) left = Mathf.Max(left, r);
                        if (r > halfW * 0.5f) right = Mathf.Min(right, r);
                    }
                    if (Mathf.Abs(r) < halfW)
                    {
                        if (u < -halfH * 0.5f) bottom = Mathf.Max(bottom, u);
                        if (u > halfH * 0.5f) top = Mathf.Min(top, u);
                    }
                }
                if (count <= bestCount || left == float.MinValue || right == float.MaxValue || bottom == float.MinValue || top == float.MaxValue) continue;
                best = mf; bestCount = count; bestReveal = new Vector4(left, right, bottom, top);
            }
            reveal = bestReveal;
            return best;
        }
    }
}
