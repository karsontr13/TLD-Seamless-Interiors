using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── VIEWS CUT TO THE GLASS'S OWN OUTLINE ───
        //
        // Every view is drawn on a rectangle: a camera renders the rectangle of the window that is on
        // screen, and a four-cornered surface shows that picture with picture coordinates 0 to 1. A
        // window that is not a rectangle (the Radio Control Hut's triangular end-wall windows,
        // 2026-10-01) got that rectangle over its slanted edges. The owner: "I really wish there was a
        // quick win for triangular windows", and of the inside-out side, "we're likely going to do the
        // same thing".
        //
        // The camera and its rectangle stay exactly as they are. Only the surface changes: the glass's
        // outline, clipped to the on-screen rectangle, with each point's picture coordinates taken
        // from where it stands in that rectangle. The picture inside the outline is the same picture
        // the rectangle showed, trimmed (proven in the console that day on the hut's front window).
        // A rectangular window passes no outline and gets the four corners as before.
        //
        // The surfaces are shared between windows from frame to frame, so one that last showed an
        // outline is put back to four corners before a rectangle uses it.
        private static readonly HashSet<int> s_ShapedViewSurfaces = new HashSet<int>();
        private static readonly List<Vector2> s_ShapedPoly = new List<Vector2>();
        private static readonly List<Vector2> s_ShapedPolyB = new List<Vector2>();

        private static void WriteViewSurface(Mesh mesh, Vector3 pa, Vector3 right, Vector3 up,
                                             float u0, float u1, float v0, float v1, Vector3[] outline)
        {
            Vector3 qa = pa + right * u0 + up * v0, qb = pa + right * u1 + up * v0;
            Vector3 qc = pa + right * u0 + up * v1, qd = pa + right * u1 + up * v1;
            int id = mesh.GetInstanceID();

            if (outline == null || outline.Length < 3)
            {
                if (s_ShapedViewSurfaces.Remove(id))
                {
                    mesh.Clear();
                    mesh.vertices = new Vector3[] { qa, qc, qd, qb };
                    mesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
                    mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
                }
                else mesh.vertices = new Vector3[] { qa, qc, qd, qb };
                mesh.RecalculateBounds();
                return;
            }

            // The outline in the window's plane, across and up from the rectangle's corner, clipped to
            // the part on screen.
            s_ShapedPoly.Clear();
            for (int i = 0; i < outline.Length; i++)
            {
                Vector3 d = outline[i] - pa;
                s_ShapedPoly.Add(new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)));
            }
            ClipShaped(0, u0, true); ClipShaped(0, u1, false); ClipShaped(1, v0, true); ClipShaped(1, v1, false);

            s_ShapedViewSurfaces.Add(id);
            mesh.Clear();
            int n = s_ShapedPoly.Count;
            if (n < 3 || u1 - u0 < 1e-5f || v1 - v0 < 1e-5f) { mesh.RecalculateBounds(); return; }

            // Clockwise across and up, as the four-cornered surface winds (bottom left, top left, top
            // right), so it faces the same way; a surface is drawn from its front only. The order is
            // read from the shape itself: the building's outlines are measured with "right" the other
            // way from the viewer's, the room's with the viewer's, so assuming one order hid the
            // building's triangles from outside while the room's showed (2026-10-01).
            float signedArea = 0f;
            for (int i = 0; i < n; i++) { Vector2 a = s_ShapedPoly[i], b = s_ShapedPoly[(i + 1) % n]; signedArea += a.x * b.y - b.x * a.y; }
            bool reverse = signedArea > 0f;      // counter-clockwise
            var vertices = new Vector3[n];
            var uvs = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 p = s_ShapedPoly[reverse ? n - 1 - i : i];
                vertices[i] = pa + right * p.x + up * p.y;
                uvs[i] = new Vector2((p.x - u0) / (u1 - u0), (p.y - v0) / (v1 - v0));
            }
            var triangles = new int[(n - 2) * 3];
            for (int i = 0; i < n - 2; i++) { triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = i + 2; }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        // One side of the rectangle: keep the part of s_ShapedPoly with coordinate axis at least (keepAbove)
        // or at most (!keepAbove) the given value.
        private static void ClipShaped(int axis, float value, bool keepAbove)
        {
            s_ShapedPolyB.Clear();
            int n = s_ShapedPoly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = s_ShapedPoly[i], b = s_ShapedPoly[(i + 1) % n];
                float da = (keepAbove ? 1f : -1f) * ((axis == 0 ? a.x : a.y) - value);
                float db = (keepAbove ? 1f : -1f) * ((axis == 0 ? b.x : b.y) - value);
                if (da >= 0f) s_ShapedPolyB.Add(a);
                if ((da >= 0f) != (db >= 0f)) s_ShapedPolyB.Add(Vector2.Lerp(a, b, da / (da - db)));
            }
            s_ShapedPoly.Clear();
            s_ShapedPoly.AddRange(s_ShapedPolyB);
        }

        // A sheet in the outline's shape, both windings, for the dark backing behind a view.
        private static void BuildOutlineSheet(Mesh mesh, Vector3[] outline, Vector3 center, Vector3 right, Vector3 up)
        {
            int n = outline.Length;
            var vertices = new Vector3[n];
            var uvs = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                vertices[i] = outline[i];
                Vector3 d = outline[i] - center;
                uvs[i] = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up));
            }
            var triangles = new List<int>();
            for (int i = 0; i < n - 2; i++)
            {
                triangles.Add(0); triangles.Add(i + 1); triangles.Add(i + 2);
                triangles.Add(0); triangles.Add(i + 2); triangles.Add(i + 1);
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
        }
    }
}
