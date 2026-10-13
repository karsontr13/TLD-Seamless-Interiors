using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // The frost and snow drawn on the glass of the room the player is in (owner's look, 2026-10-13,
        // Tools/WindowFrostPrototype.md). Replaces the game's painted frost; the cover comes from WindowCover.cs.
        public static bool WindowCoverLayers = true;

        private const int COVER_FROST_SIZE = 512;
        // How far the cover must move before the frost texture or the snow bank is made again.
        private const float COVER_FROST_STEP = 1f / 256f;
        private const float COVER_SNOW_STEP = 1f / 256f;

        // The snow bank outside each pane (owner's reference photo, 2026-10-13): metres and shape factors.
        private const int BANK_COLUMNS = 48, BANK_ROWS = 8;
        private const float BANK_DEPTH = 0.10f, BANK_SLUMP = 0.45f, BANK_UV = 2f;
        private const float BANK_SIDE_WIDTH = 0.15f, BANK_SIDE_RISE = 0.35f, BANK_TOP_GAPS = 0.08f, BANK_DRIFT = 2.5f;
        // The pane bottom tucks this far under its bar, and a pane under another one hides this much of its top behind the bar.
        private const float BANK_TUCK = 0.03f, BANK_UNDER_BAR = 0.10f;

        private class WindowCoverLayer
        {
            public int Opening;
            public GameObject FrostGo, BankGo;
            public Material FrostMat;
            public Texture2D FrostTex;
            public Mesh BankMesh;
            public Color32[] FrostPixels;
            // Per pixel, worked out once for the window's own shape.
            public float[] VeinTime, Vein, Glow, HazeTime, Grain;
            // Panes in metres from Center along Right and Up: left, right, bottom, top, 1 when a bar sits above.
            public List<float[]> Panes;
            public Vector3 Center, Right, Up, Outward;
            public float FrostShown = -1f, SnowShown = -1f;
            // Set by the worker thread once the per-pixel tables are done.
            public volatile bool Ready;
            public bool Shown;
            public string Error;
            public long PrepareMs;
        }

        private static readonly List<WindowCoverLayer> s_CoverLayers = new List<WindowCoverLayer>();
        private static string s_CoverLayersInstance;
        private static int s_CoverLayersBuilt;
        private static float s_CoverNextTick;
        private static bool s_CoverDirty;
        private static bool s_CoverFailed;

        // Plain managed math: Mathf and Color calls cross into the game per call and are far too slow per pixel.
        private static float C01(float x) { return x < 0f ? 0f : (x > 1f ? 1f : x); }
        private static float CLerp(float a, float b, float t) { return a + (b - a) * t; }
        private static int CFloor(float x) { int i = (int)x; return x < i ? i - 1 : i; }

        private static float CoverHash(int x, int y)
        {
            unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; }
        }

        private static float CoverNoise(float x, float y)
        {
            int xi = CFloor(x), yi = CFloor(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return CLerp(CLerp(CoverHash(xi, yi), CoverHash(xi + 1, yi), fx), CLerp(CoverHash(xi, yi + 1), CoverHash(xi + 1, yi + 1), fx), fy);
        }

        private static float CoverFbm(float x, float y, int octaves)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += CoverNoise(x, y) * amp; norm += amp; x = x * 2.03f + 17.1f; y = y * 2.03f + 9.7f; amp *= 0.5f; }
            return sum / norm;
        }

        // The glass's own rectangle: its renderers' bounds laid on the opening's right and up.
        private static bool CoverRect(InteriorWindowOpening opening, out Vector3 center, out float width, out float height)
        {
            center = opening.GlassCenter; width = 0f; height = 0f;
            if (opening.Glass == null || opening.Glass.Count == 0) return false;
            float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
            bool any = false;
            for (int g = 0; g < opening.Glass.Count; g++)
            {
                var renderer = opening.Glass[g];
                if (renderer == null) continue;
                var b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z) - opening.GlassCenter;
                    float r = Vector3.Dot(p, opening.Right), u = Vector3.Dot(p, opening.Up);
                    minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r); minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                    any = true;
                }
            }
            if (!any) return false;
            // Some glass meshes run on below the frame behind the wall (Camp Office LakeCabinGlassB);
            // the game's own snow slab sits only in the frame, so its bottom is the pane's bottom.
            if (opening.Frost != null)
            {
                float slabMinU = float.MaxValue;
                for (int s = 0; s < opening.Frost.Count; s++)
                {
                    var slab = opening.Frost[s];
                    if (slab == null) continue;
                    var b = slab.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var p = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z) - opening.GlassCenter;
                        slabMinU = Mathf.Min(slabMinU, Vector3.Dot(p, opening.Up));
                    }
                }
                if (slabMinU > minU + 0.05f && slabMinU < maxU - 0.2f) minU = slabMinU;
            }
            width = maxR - minR; height = maxU - minU;
            center = opening.GlassCenter + opening.Right * ((minR + maxR) * 0.5f) + opening.Up * ((minU + maxU) * 0.5f);
            return width > 0.05f && height > 0.05f;
        }

        private static GameObject CoverQuad(string name, Material material, Vector3 center, Vector3 right, Vector3 up, Vector3 inward, float width, float height)
        {
            var go = new GameObject(name);
            var mesh = new Mesh();
            Vector3 hr = right * (width * 0.5f), hu = up * (height * 0.5f);
            mesh.vertices = new[] { center - hr - hu, center + hr - hu, center - hr + hu, center + hr + hu };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 };
            mesh.normals = new[] { inward, inward, inward, inward };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static WindowCoverLayer BuildCoverLayer(int index)
        {
            var opening = s_WindowPortalOpenings[index];
            Vector3 center; float width, height;
            if (!CoverRect(opening, out center, out width, out height)) return null;

            var layer = new WindowCoverLayer { Opening = index };
            // Both lit by the day outside, as frost and snow on a window are (dark mode showed the room-lit frost dark).
            Shader unlit = Shader.Find("Sprites/Default");
            if (unlit == null) return null;

            layer.FrostTex = new Texture2D(COVER_FROST_SIZE, COVER_FROST_SIZE, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "SeamlessInteriors_WindowFrost" };
            layer.FrostMat = new Material(unlit) { mainTexture = layer.FrostTex, renderQueue = WINDOW_VIEW_OUT_FROST_QUEUE + 2, name = "SeamlessInteriors_WindowFrost" };

            Vector3 inward = -opening.Outward;
            layer.FrostGo = CoverQuad("SeamlessInteriors_WindowFrost_" + index, layer.FrostMat, center + inward * 0.008f, opening.Right, opening.Up, inward, width, height);
            layer.FrostGo.SetActive(false);
            layer.FrostPixels = new Color32[COVER_FROST_SIZE * COVER_FROST_SIZE];

            // Snow is a real bank outside the glass in the game's own window snow; drawn flat on the glass
            // it looked like a cut-out from the side (owner, 2026-10-13).
            layer.Center = center; layer.Right = opening.Right; layer.Up = opening.Up; layer.Outward = opening.Outward;
            layer.Panes = CoverPanes(opening, center, height);
            var bankMaterial = CoverBankMaterial(opening);
            if (bankMaterial != null && layer.Panes.Count > 0)
            {
                layer.BankGo = new GameObject("SeamlessInteriors_WindowSnow_" + index);
                layer.BankMesh = new Mesh { name = "SeamlessInteriors_WindowSnow" };
                layer.BankGo.AddComponent<MeshFilter>().sharedMesh = layer.BankMesh;
                var renderer = layer.BankGo.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = bankMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                layer.BankGo.SetActive(false);
            }

            // The tables take a tenth of a second or more a window; worked out on the main thread they stalled the entry.
            float aspect = width / height;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try { PrepareCoverPixels(layer, aspect, index); }
                catch (System.Exception ex) { layer.Error = ex.Message; }
                layer.PrepareMs = watch.ElapsedMilliseconds;
                layer.Ready = true;
            });
            return layer;
        }

        // Frost ferns: stems grow in from every edge with side branches, each pixel noting when growth reaches it.
        // Runs off the main thread, so plain managed code only.
        private class FrostSketch
        {
            public int N;
            public float Aspect;
            public System.Random Rng;
            public float[] Time, Vein;

            private float Rf() { return (float)Rng.NextDouble(); }

            private void Splat(float x, float y, float r, float strength, float t)
            {
                float rx = r / Aspect;
                int x0 = CFloor(x - rx - 1), x1 = CFloor(x + rx + 1), y0 = CFloor(y - r - 1), y1 = CFloor(y + r + 1);
                for (int py = y0; py <= y1; py++)
                {
                    if (py < 0 || py >= N) continue;
                    for (int px = x0; px <= x1; px++)
                    {
                        if (px < 0 || px >= N) continue;
                        float dx = (px + 0.5f - x) * Aspect, dy = py + 0.5f - y;
                        float k = 1f - (float)System.Math.Sqrt(dx * dx + dy * dy) / r;
                        if (k <= 0f) continue;
                        int i = py * N + px;
                        if (k * strength > Vein[i]) Vein[i] = k * strength;
                        if (t < Time[i]) Time[i] = t;
                    }
                }
            }

            public void Branch(float x, float y, float angle, float length, float t, float timePerStep, int depth, float width, float strength)
            {
                int side = Rng.Next(2) == 0 ? 1 : -1;
                float next = 2f + Rf() * 4f;
                for (float s = 0; s < length; s += 1f)
                {
                    angle += (Rf() - 0.5f) * 0.10f;
                    x += (float)System.Math.Cos(angle) / Aspect; y += (float)System.Math.Sin(angle);
                    if (x < 0 || y < 0 || x >= N || y >= N) break;
                    float at = t + s * timePerStep;
                    Splat(x, y, width * (1f - 0.55f * s / length), strength, at);
                    if (depth <= 0 || s < next) continue;
                    float subLength = (length - s) * (0.18f + Rf() * 0.22f);
                    if (subLength > 3f) Branch(x, y, angle + side * (0.95f + Rf() * 0.25f), subLength, at, timePerStep * 1.15f, depth - 1, width * 0.62f, strength * 0.85f);
                    if (Rf() < 0.45f) Branch(x, y, angle - side * (0.95f + Rf() * 0.25f), subLength * (0.6f + Rf() * 0.5f), at, timePerStep * 1.15f, depth - 1, width * 0.62f, strength * 0.85f);
                    else side = -side;
                    next = s + (depth == 2 ? 3f : 2f) + Rf() * (depth == 2 ? 6f : 4f);
                }
            }

            // Every edge gets stems in proportion to its length (the owner found the bottom-heavy first try uneven).
            public void Grow()
            {
                const float pi = 3.14159f;
                float step = 1f / N;
                int across = 3 + (int)(6 * Aspect), down = 3 + (int)(6 / Aspect);
                for (int k = 0; k < across; k++) Branch(Rf() * N, 1f, pi / 2 + (Rf() - 0.5f) * 1.2f, N * (0.3f + Rf() * 0.4f), Rf() * 0.2f, step, 2, 1.6f, 1f);
                for (int k = 0; k < across; k++) Branch(Rf() * N, N - 1.5f, -pi / 2 + (Rf() - 0.5f) * 1.2f, N * (0.3f + Rf() * 0.4f), Rf() * 0.2f, step, 2, 1.6f, 1f);
                for (int k = 0; k < down; k++) Branch(1f, Rf() * N, (Rf() - 0.5f) * 1.2f, N * (0.3f + Rf() * 0.4f) * Aspect, Rf() * 0.2f, step, 2, 1.6f, 1f);
                for (int k = 0; k < down; k++) Branch(N - 1.5f, Rf() * N, pi + (Rf() - 0.5f) * 1.2f, N * (0.3f + Rf() * 0.4f) * Aspect, Rf() * 0.2f, step, 2, 1.6f, 1f);
                // A few stars that start later out on the glass.
                for (int k = 0; k < 6; k++)
                {
                    float cx = (0.15f + Rf() * 0.7f) * N, cy = (0.15f + Rf() * 0.7f) * N, t0 = 0.35f + Rf() * 0.4f, a0 = Rf() * 6.28f;
                    int arms = 3 + Rng.Next(4);
                    for (int a = 0; a < arms; a++) Branch(cx, cy, a0 + a * 6.283f / arms + (Rf() - 0.5f) * 0.4f, N * (0.06f + Rf() * 0.12f), t0, step * 1.3f, 1, 1.1f, 0.85f);
                }
            }
        }

        // Separable box blur, for the soft glow around the ferns.
        private static float[] CoverBlur(float[] src, int n, int r)
        {
            var tmp = new float[n * n]; var dst = new float[n * n];
            float norm = 1f / (2 * r + 1);
            for (int y = 0; y < n; y++)
            {
                float s = 0;
                for (int x = -r; x < n + r; x++)
                {
                    int xa = x + r, xr = x - r - 1;
                    if (xa >= 0 && xa < n) s += src[y * n + xa];
                    if (xr >= 0 && xr < n) s -= src[y * n + xr];
                    if (x >= 0 && x < n) tmp[y * n + x] = s * norm;
                }
            }
            for (int x = 0; x < n; x++)
            {
                float s = 0;
                for (int y = -r; y < n + r; y++)
                {
                    int ya = y + r, yr = y - r - 1;
                    if (ya >= 0 && ya < n) s += tmp[ya * n + x];
                    if (yr >= 0 && yr < n) s -= tmp[yr * n + x];
                    if (y >= 0 && y < n) dst[y * n + x] = s * norm;
                }
            }
            return dst;
        }

        // Everything that does not change with the cover, so a fill is a few sums a pixel.
        private static void PrepareCoverPixels(WindowCoverLayer layer, float aspect, int seed)
        {

            int n = COVER_FROST_SIZE;
            var sketch = new FrostSketch { N = n, Aspect = aspect, Rng = new System.Random(seed * 7919 + 13), Time = new float[n * n], Vein = new float[n * n] };
            for (int i = 0; i < n * n; i++) sketch.Time[i] = 9f;
            sketch.Grow();
            layer.VeinTime = sketch.Time; layer.Vein = sketch.Vein;
            layer.Glow = CoverBlur(sketch.Vein, n, 4);
            layer.HazeTime = new float[n * n]; layer.Grain = new float[n * n];
            float edgeScale = aspect < 1f ? aspect : 1f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, ua = u * aspect;
                    float eu = (u < 1f - u ? u : 1f - u) * aspect, ev = v < 1f - v ? v : 1f - v;
                    float edge = C01((eu < ev ? eu : ev) * 2f / edgeScale);
                    int i = y * n + x;
                    // The haze creeps in from the edges and gathers around the ferns first.
                    layer.HazeTime[i] = edge * 0.85f + (CoverFbm(ua * 3f + seed * 3.1f, v * 3f + seed * 1.7f, 4) - 0.5f) * 0.4f + 0.02f - layer.Glow[i] * 0.5f;
                    layer.Grain[i] = 0.5f + (CoverHash(x + seed * 977, y) - 0.5f) * 0.45f + (CoverFbm(ua * 28f + seed, v * 28f, 2) - 0.5f) * 0.7f;
                }
        }

        private static Color32 CoverPixel(float r, float g, float b, float a)
        {
            // Field by field: a struct constructor is a call into the game here.
            var c = default(Color32);
            c.r = (byte)(C01(r) * 255f); c.g = (byte)(C01(g) * 255f); c.b = (byte)(C01(b) * 255f); c.a = (byte)(C01(a) * 255f);
            return c;
        }

        private static void FillCoverFrost(WindowCoverLayer layer, float frost)
        {
            int count = layer.Vein.Length;
            float reach = frost * 1.15f, haze0 = frost * 0.06f;
            var veinTime = layer.VeinTime; var vein = layer.Vein; var hazeTime = layer.HazeTime; var grain = layer.Grain; var glow = layer.Glow; var pixels = layer.FrostPixels;
            for (int i = 0; i < count; i++)
            {
                float v = vein[i] * C01((reach - veinTime[i]) * 12f);
                float haze = C01((reach - hazeTime[i]) * 2.5f);
                float a = haze0 + haze * (0.36f + 0.22f * grain[i]) + v * 0.7f + glow[i] * haze * 0.3f;
                float w = C01(0.80f + 0.20f * v + 0.05f * grain[i]);
                pixels[i] = CoverPixel(w * 0.90f, w * 0.95f, w, a);
            }
            layer.FrostTex.SetPixels32(pixels);
            layer.FrostTex.Apply(true);
            layer.FrostShown = frost;
        }

        // The panes are the glass mesh's own rectangles (a sash window has one per sash).
        private static List<float[]> CoverPanes(InteriorWindowOpening opening, Vector3 center, float height)
        {
            var panes = new List<float[]>();
            float bottom = -height * 0.5f;
            for (int g = 0; g < opening.Glass.Count; g++)
            {
                var renderer = opening.Glass[g];
                var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                var t = renderer.transform;
                for (int k = 0; k + 2 < triangles.Length; k += 3)
                {
                    float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue;
                    for (int j = 0; j < 3; j++)
                    {
                        var p = t.TransformPoint(vertices[triangles[k + j]]) - center;
                        float r = Vector3.Dot(p, opening.Right), u = Vector3.Dot(p, opening.Up);
                        r0 = Mathf.Min(r0, r); r1 = Mathf.Max(r1, r); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                    }
                    // Glass that runs on behind the wall below the frame (see CoverRect) is cut off.
                    if (u0 < bottom - 0.02f) u0 = bottom;
                    if (u1 - u0 < 0.05f || r1 - r0 < 0.05f) continue;
                    bool seen = false;
                    foreach (var pane in panes)
                        if (Mathf.Abs(pane[0] - r0) < 0.01f && Mathf.Abs(pane[1] - r1) < 0.01f && Mathf.Abs(pane[2] - u0) < 0.01f && Mathf.Abs(pane[3] - u1) < 0.01f) { seen = true; break; }
                    if (!seen) panes.Add(new[] { r0, r1, u0, u1, 0f });
                }
            }
            foreach (var pane in panes)
                foreach (var other in panes)
                    if (other != pane && other[2] >= pane[3] - 0.01f && other[2] - pane[3] < 0.2f && other[0] < pane[1] && other[1] > pane[0]) pane[4] = 1f;
            return panes;
        }

        // The game's own window snow (TRN_GroundSnowPaintyA on the frame's slab), lit like the rest of the scene.
        private static Material s_CoverBankMaterial;

        private static Material CoverBankMaterial(InteriorWindowOpening opening)
        {
            if (opening.Frost != null)
                for (int i = 0; i < opening.Frost.Count; i++)
                    if (opening.Frost[i] != null && opening.Frost[i].sharedMaterial != null) return s_CoverBankMaterial = opening.Frost[i].sharedMaterial;
            if (s_CoverBankMaterial != null) return s_CoverBankMaterial;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.name.StartsWith("TRN_GroundSnowPaintyA")) return s_CoverBankMaterial = m;
            return null;
        }

        private static float CoverSmooth(float x) { x = C01(x); return x * x * (3f - 2f * x); }

        private static Vector3 CoverPoint(WindowCoverLayer layer, float right, float up, float outward)
        {
            // Field by field, as for Color32: Vector3 sums are calls into the game here.
            Vector3 c = layer.Center, r = layer.Right, u = layer.Up, o = layer.Outward;
            var v = default(Vector3);
            v.x = c.x + r.x * right + u.x * up + o.x * outward;
            v.y = c.y + r.y * right + u.y * up + o.y * outward;
            v.z = c.z + r.z * right + u.z * up + o.z * outward;
            return v;
        }

        // Each pane's bank: a face against the glass, a top that slumps away outside, and closed ends.
        private static void BuildCoverBank(WindowCoverLayer layer, float snow)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            const int nx = BANK_COLUMNS, nz = BANK_ROWS;
            // The dunes roll while snow builds and settle into a full pane at the end.
            float roll = snow * (1f - snow * snow);
            var tops = new float[nx + 1];
            for (int p = 0; p < layer.Panes.Count && snow > 0f; p++)
            {
                var pane = layer.Panes[p];
                int seed = layer.Opening * 13 + p * 7;
                float x0 = pane[0], pw = pane[1] - pane[0], hb = pane[2] - BANK_TUCK, ph = pane[3] - hb;
                float fullTop = pane[4] > 0f ? 1f - BANK_UNDER_BAR / ph : 1f;
                float sideLeft = CoverHash(seed, 1) > 0.45f ? 1f : -0.6f, sideRight = CoverHash(seed, 2) > 0.45f ? 1f : -0.6f;
                for (int i = 0; i <= nx; i++)
                {
                    float xl = (float)i / nx, wx = xl * pw / ph;
                    float el = CoverSmooth(1f - xl * pw / BANK_SIDE_WIDTH), er = CoverSmooth(1f - (1f - xl) * pw / BANK_SIDE_WIDTH);
                    // Sampled along the snow amount too, so the bank rises unevenly instead of just scaling up.
                    float shape = (el * sideLeft + er * sideRight) * BANK_SIDE_RISE
                                + (CoverFbm(wx * 1.4f + seed, snow * BANK_DRIFT, 3) - 0.5f) * 1.8f
                                + (CoverFbm(wx * 9f + seed, 3.5f + snow * BANK_DRIFT * 2f, 2) - 0.5f) * 0.15f;
                    // Even a full pane keeps small gaps along its top (owner, 2026-10-13).
                    float gap = C01((CoverFbm(wx * 3f + seed * 1.7f, 7.5f, 3) - 0.45f) * 4f) * BANK_TOP_GAPS;
                    float level = snow * (fullTop + 0.03f - gap) + shape * roll;
                    tops[i] = hb + C01(Mathf.Min(level, fullTop - gap * snow)) * ph;
                }

                int front = verts.Count;
                for (int i = 0; i <= nx; i++)
                {
                    float xr = x0 + pw * i / nx;
                    verts.Add(CoverPoint(layer, xr, hb, 0.004f)); uvs.Add(new Vector2(xr * BANK_UV, hb * BANK_UV));
                    verts.Add(CoverPoint(layer, xr, tops[i], 0.004f)); uvs.Add(new Vector2(xr * BANK_UV, tops[i] * BANK_UV));
                }
                for (int i = 0; i < nx; i++)
                {
                    int a = front + i * 2;
                    tris.Add(a); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 3); tris.Add(a + 2);
                }

                int top = verts.Count;
                for (int j = 0; j <= nz; j++)
                    for (int i = 0; i <= nx; i++)
                    {
                        float xr = x0 + pw * i / nx, oz = 0.004f + BANK_DEPTH * j / nz;
                        verts.Add(CoverPoint(layer, xr, BankHeight(tops[i], hb, xr, j, seed), oz)); uvs.Add(new Vector2(xr * BANK_UV, oz * BANK_UV + 3f));
                    }
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int a = top + j * (nx + 1) + i, b = a + nx + 1;
                        tris.Add(a); tris.Add(b); tris.Add(a + 1); tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                    }

                // End caps, both windings so they show from either side.
                for (int e = 0; e < 2; e++)
                {
                    int i = e == 0 ? 0 : nx;
                    float xr = x0 + pw * i / nx;
                    int cap = verts.Count;
                    verts.Add(CoverPoint(layer, xr, hb, 0.004f)); uvs.Add(new Vector2(0f, hb * BANK_UV));
                    for (int j = 0; j <= nz; j++)
                    {
                        float oz = 0.004f + BANK_DEPTH * j / nz, h = BankHeight(tops[i], hb, xr, j, seed);
                        verts.Add(CoverPoint(layer, xr, h, oz)); uvs.Add(new Vector2(oz * BANK_UV, h * BANK_UV));
                    }
                    verts.Add(CoverPoint(layer, xr, hb, 0.004f + BANK_DEPTH)); uvs.Add(new Vector2(BANK_DEPTH * BANK_UV, hb * BANK_UV));
                    for (int j = 1; j <= nz + 1; j++)
                    {
                        tris.Add(cap); tris.Add(cap + j + 1); tris.Add(cap + j);
                        tris.Add(cap); tris.Add(cap + j); tris.Add(cap + j + 1);
                    }
                }
            }

            var mesh = layer.BankMesh;
            mesh.Clear();
            mesh.vertices = verts.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            layer.SnowShown = snow;
        }

        // The bank's top, lower the further it lies out from the glass.
        private static float BankHeight(float top, float bottom, float right, int row, int seed)
        {
            float zl = (float)row / BANK_ROWS;
            return bottom + (top - bottom) * (1f - BANK_SLUMP * zl * zl + (CoverFbm(right * 6f + seed, zl * 3f, 2) - 0.5f) * 0.2f * zl);
        }

        // Frost is lit by the day outside, not by the room: lit by the room it read as a white wall.
        private static Color CoverSnowLight()
        {
            Color fog = RenderSettings.fogColor;
            Color ambient = RenderSettings.ambientLight;
            float level = (ambient.r + ambient.g + ambient.b) / 3f;
            Color c = Color.Lerp(fog, Color.white, 0.55f) * Mathf.Clamp(level * 1.35f, 0.05f, 1.1f);
            c.a = 1f;
            return c;
        }

        // Anything that changed a pane's record asks for a look on the next frame.
        private static void MarkWindowCoverDirty() { s_CoverDirty = true; }

        // Every frame from OnUpdate: builds one window a frame after entering, then follows the cover once a second.
        private static void TickWindowCoverLayers()
        {
            if (s_CoverFailed) return;
            try
            {
                string id = WindowCoverLayers ? s_WindowPortalInstanceId : null;
                if (id != s_CoverLayersInstance || (id != null && s_CoverLayers.Count != s_WindowPortalOpenings.Count))
                {
                    TeardownWindowCoverLayers();
                    if (id == null || s_WindowPortalOpenings.Count == 0) return;
                    s_CoverLayersInstance = id;
                    for (int i = 0; i < s_WindowPortalOpenings.Count; i++) s_CoverLayers.Add(null);
                    s_CoverLayersBuilt = 0;
                }
                if (s_CoverLayersInstance == null) return;

                if (s_CoverLayersBuilt < s_CoverLayers.Count)
                {
                    int i = s_CoverLayersBuilt++;
                    s_CoverLayers[i] = BuildCoverLayer(i);
                    UpdateCoverLayer(i, true);
                    return;
                }

                // One finished window a frame takes over its glass.
                for (int i = 0; i < s_CoverLayers.Count; i++)
                {
                    var layer = s_CoverLayers[i];
                    if (layer == null || layer.Shown || !layer.Ready) continue;
                    if (layer.Error != null) throw new System.Exception(layer.Error);
                    layer.Shown = true;
                    UpdateCoverLayer(i, true);
                    MelonLogger.Msg($"[PENCERE-ORTU] Pencere {i} hazir: {layer.PrepareMs} ms (arka planda)", $"[WINDOW-COVER] Window {i} prepared in {layer.PrepareMs} ms (background)");
                    return;
                }

                float real = Time.realtimeSinceStartup;
                if (!s_CoverDirty && real < s_CoverNextTick) return;
                s_CoverDirty = false;
                s_CoverNextTick = real + 1f;

                Color light = CoverSnowLight();
                int fills = 0;
                for (int i = 0; i < s_CoverLayers.Count; i++)
                {
                    var layer = s_CoverLayers[i];
                    if (layer != null && layer.FrostMat.color != light) layer.FrostMat.color = light;
                    // At most two fills a tick; the rest catch up on the next.
                    if (fills < 2 && UpdateCoverLayer(i, false)) fills++;
                }
            }
            catch (System.Exception ex)
            {
                s_CoverFailed = true;
                try { TeardownWindowCoverLayers(); } catch { }
                MelonLogger.Warning($"[PENCERE-ORTU] Kapatildi: {ex.Message}", $"[WINDOW-COVER] Turned off after an error: {ex.Message}");
            }
        }

        // True when a texture was filled.
        // True when the frost texture or the snow bank was made again.
        private static bool UpdateCoverLayer(int index, bool force)
        {
            var opening = s_WindowPortalOpenings[index];
            float frost, snow;
            GetRoomPaneCover(index, out frost, out snow);
            var layer = s_CoverLayers[index];
            // Until its tables are ready the window keeps the game's own frost.
            if (layer != null && !layer.Shown) { SetGlassShown(opening.Glass, true); return false; }

            // The game's painted frost and the frame's snow slab stay off; the bank stands in for the slab.
            SetGlassShown(opening.Glass, false);
            bool bank = layer != null && layer.BankGo != null;
            SetRenderersEnabled(opening.Frost, !bank && snow > 0f && InsideOutShowSnowSlab);
            if (layer == null) return false;

            bool filled = false;
            if (force || Mathf.Abs(frost - layer.FrostShown) >= COVER_FROST_STEP || (frost == 0f) != (layer.FrostShown == 0f))
            {
                FillCoverFrost(layer, frost);
                filled = true;
            }
            // The "snow in window frames" setting still turns the snow off.
            float bankSnow = InsideOutShowSnowSlab ? snow : 0f;
            if (bank && (force || Mathf.Abs(bankSnow - layer.SnowShown) >= COVER_SNOW_STEP || (bankSnow == 0f) != (layer.SnowShown == 0f)))
            {
                BuildCoverBank(layer, bankSnow);
                filled = true;
            }
            layer.FrostGo.SetActive(frost > 0f);
            if (bank) layer.BankGo.SetActive(bankSnow > 0f);
            if (force) layer.FrostMat.color = CoverSnowLight();
            return filled;
        }

        private static void TeardownWindowCoverLayers()
        {
            for (int i = 0; i < s_CoverLayers.Count; i++)
            {
                var layer = s_CoverLayers[i];
                if (layer == null) continue;
                if (layer.FrostGo != null) Object.Destroy(layer.FrostGo);
                if (layer.BankGo != null) Object.Destroy(layer.BankGo);
                if (layer.BankMesh != null) Object.Destroy(layer.BankMesh);
                if (layer.FrostMat != null) Object.Destroy(layer.FrostMat);
                if (layer.FrostTex != null) Object.Destroy(layer.FrostTex);
            }
            s_CoverLayers.Clear();
            s_CoverLayersInstance = null;
            s_CoverLayersBuilt = 0;
        }
    }
}
