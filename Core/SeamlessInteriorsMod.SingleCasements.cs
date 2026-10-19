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
