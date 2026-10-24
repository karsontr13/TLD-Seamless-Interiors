using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // The frost and snow drawn on the glass of the room the player is in (owner's look, 2026-10-13,
        // Tools/WindowFrostPrototype.md). Replaces the game's painted frost; the cover comes from WindowCover.cs.
        public static bool WindowCoverLayers = true;

        // The player's setting. Off: no growth and no banks; only a cleaned pane is drawn by the layers.
        internal static bool WindowCoverBuildUp => s_PrefWindowFrostSnowBuildUp == null || s_PrefWindowFrostSnowBuildUp.Value;

        private const int COVER_FROST_SIZE = 512;
        // How far the cover must move before the frost texture or the snow bank is made again.
        private const float COVER_FROST_STEP = 1f / 256f;
        private const float COVER_SNOW_STEP = 1f / 256f;

        // The snow bank outside each pane (owner's reference photo, 2026-10-13): metres and shape factors.
        private const int BANK_COLUMNS = 48, BANK_ROWS = 8, BANK_ROUND_ROWS = 5;
        // How far at most the rounded outer face bulges past the top's edge.
        private const float BANK_ROUND_REACH = 0.016f;
        private const float BANK_DEPTH = 0.018f, BANK_SLUMP = 0.45f;
        // Seen from the room, a 1.8 cm bank read as a thin cut-out: a 10 cm mound with a gentle top (owner's "C", 2026-10-20).
        private const float BANK_ROOM_VIEW_DEPTH = 0.075f, BANK_ROOM_VIEW_REACH = 0.025f, BANK_ROOM_VIEW_SLUMP = 0.12f;
        // Texture per metre, as the buildings' own snow is laid (0.09; at 2 the blots of the texture showed).
        private const float BANK_UV = 0.09f;
        private const float BANK_SIDE_WIDTH = 0.15f, BANK_SIDE_RISE = 0.35f, BANK_TOP_GAPS = 0.08f, BANK_DRIFT = 2.5f;
        // The pane bottom tucks this far under its bar, and a pane under another one hides this much of its top behind the bar.
        private const float BANK_TUCK = 0.03f, BANK_UNDER_BAR = 0.10f;
        // The view must stand at least this far out for the bank to fit in front of it.
        private const float COVER_BANK_OUTSIDE_NEEDS = 0.004f + BANK_DEPTH + BANK_ROUND_REACH;
        // A bank on the room side: its depth, all of it behind the frost moved this far into the room.
        private const float COVER_ROOM_SIDE_FROST_IN = 0.014f, COVER_ROOM_SIDE_STAND_OFF = 0.0005f;
        private const float COVER_ROOM_SIDE_DEPTH = 0.007f, COVER_ROOM_SIDE_REACH = 0.005f;

        private class WindowCoverLayer
        {
            public int Opening, Seed;
            public int FrostSize = COVER_FROST_SIZE;
            public GameObject FrostGo, BankGo;
            public Material FrostMat;
            public Texture2D FrostTex;
            public Mesh BankMesh;
            // The texture's RGBA32 bytes, mips included.
            public byte[] FrostBytes;
            // Per pixel, worked out once for the window's own shape.
            public CoverTables Tables;
            // Zero where a bar barely proud of the glass would be drawn over; null for none.
            public float[] FrostMask;
            public List<Vector2> Outline;   // a shaped pane's glass, triangles in across and up about Center
            // Panes in metres from Center along Right and Up: left, right, bottom, top, 1 when a bar sits above.
            public List<float[]> Panes;
            public Vector3 Center, Right, Up, Outward;
            public float FrostShown = -1f, SnowShown = -1f;
            // A fill under way: the cover it is for, and the next pixel to make (-1: none).
            public float FillTarget = -1f;
            public int FillNext = -1;
            // The bank's depth out from the glass, its rounded face, and how it meets the bars.
            public float BankDepth = BANK_DEPTH, BankReach = BANK_ROUND_REACH, BankTuck = BANK_TUCK, BankUnderBar = BANK_UNDER_BAR;
            public float BankSlump = BANK_SLUMP;
            // How far from the glass the bank's flat face stands.
            public float BankStandOff = 0.004f;
            // The bank's mesh arrays and the noise its shape takes, kept between rebuilds.
            public float[] BankNoise;
            public Vector3[] BankVerts;
            public Vector2[] BankUvs;
            public bool BankTrisSet;
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
        // The room the layers failed in; another room or save tries again.
        private static string s_CoverFailedIn;

        // Plain managed math: Mathf and Color calls cross into the game per call and are far too slow per pixel.
        // MethodImplOptions.AggressiveOptimization (512): this project builds against a framework that does not
        // name it, while the game's .NET 6 runtime honours it.
        internal const MethodImplOptions CoverOptimised = (MethodImplOptions)512;

        private static float C01(float x) { return x < 0f ? 0f : (x > 1f ? 1f : x); }
        private static float CLerp(float a, float b, float t) { return a + (b - a) * t; }
        private static int CFloor(float x) { int i = (int)x; return x < i ? i - 1 : i; }

        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]

        private static float CoverHash(int x, int y)
        {
            unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; }
        }

        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]

        private static float CoverNoise(float x, float y)
        {
            int xi = CFloor(x), yi = CFloor(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return CLerp(CLerp(CoverHash(xi, yi), CoverHash(xi + 1, yi), fx), CLerp(CoverHash(xi, yi + 1), CoverHash(xi + 1, yi + 1), fx), fy);
        }

        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]

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
            // One glass mesh for the whole building (Community Hall's STR_CommunityHallA_GlassWin): its bounds
            // are the building's, 23 m across. The opening's own measure is the window then.
            float ownWidth = opening.GlassWidth > 0f ? opening.GlassWidth : opening.Width;
            float ownHeight = opening.GlassHeight > 0f ? opening.GlassHeight : opening.Height;
            if (ownWidth > 0.05f && ownHeight > 0.05f && (width > ownWidth * 1.5f + 0.1f || height > ownHeight * 1.5f + 0.1f))
            {
                width = ownWidth; height = ownHeight; center = opening.GlassCenter;
            }
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
            long perf = PerfProbe.Begin();
            try { return BuildCoverLayerTimed(index); } finally { PerfProbe.End(PerfProbe.Section.CoverBuild, perf); }
        }

        private static WindowCoverLayer BuildCoverLayerTimed(int index)
        {
            var opening = s_WindowPortalOpenings[index];
            Vector3 center; float width, height;
            if (!CoverRect(opening, out center, out width, out height)) return null;
            center += opening.Outward * opening.CoverOut;

            // Seeded by the building's window where there is one, so the outside shows the same frost and snow.
            int window = index < s_WindowPortalSpot.Count ? s_WindowPortalSpot[index] : -1;
            var layer = new WindowCoverLayer { Opening = index, Seed = window >= 0 ? window : 100 + index };

            // Where the view of the outdoors stands in front of the bank's place (Community Hall's at 2.5 mm,
            // 2026-10-13), the bank would stand behind the picture. It moves to the room side, behind the
            // frost, turned to face the room (owner's choice "A").
            Vector3 portal = opening.PortalCenter != Vector3.zero ? opening.PortalCenter : opening.GlassCenter + opening.Outward * opening.PortalOffsetLocal;
            bool roomSide = Vector3.Dot(portal - center, opening.Outward) < COVER_BANK_OUTSIDE_NEEDS;
            float frostIn = roomSide ? COVER_ROOM_SIDE_FROST_IN : 0.008f;
            if (!MakeCoverFrost(layer, "SeamlessInteriors_WindowFrost_" + index, center - opening.Outward * frostIn, opening.Right, opening.Up, -opening.Outward,
                                width, height, WINDOW_VIEW_OUT_FROST_QUEUE + 2)) return null;

            // Snow is a real bank outside the glass in the game's own window snow; drawn flat on the glass
            // it looked like a cut-out from the side (owner, 2026-10-13).
            layer.Center = center; layer.Right = opening.Right; layer.Up = opening.Up; layer.Outward = opening.Outward;
            layer.Panes = CoverPanes(opening, center, width, height);
            // A shaped pane's frost stops at its glass (Coastal House E's gable window, owner 2026-10-19).
            Vector3 offset = center - opening.GlassCenter;
            layer.FrostMask = ShapedGlassFrostMask(opening, layer.FrostSize, Vector3.Dot(offset, opening.Right), Vector3.Dot(offset, opening.Up), width, height, false);
            if (roomSide)
            {
                // Turned half round on its upright: across and out both reverse, so the panes are read mirrored.
                layer.Right = -opening.Right; layer.Outward = -opening.Outward;
                foreach (var p in layer.Panes) { float left = p[0]; p[0] = -p[1]; p[1] = -left; }
                layer.BankStandOff = COVER_ROOM_SIDE_STAND_OFF;
                layer.BankDepth = COVER_ROOM_SIDE_DEPTH; layer.BankReach = COVER_ROOM_SIDE_REACH;
            }
            else
            {
                layer.BankDepth = BANK_ROOM_VIEW_DEPTH; layer.BankReach = BANK_ROOM_VIEW_REACH; layer.BankSlump = BANK_ROOM_VIEW_SLUMP;
            }
            // A shaped pane's bank stays under its glass's outline, in the layer's own across and up.
            var outline = ShapedGlassOutline(opening);
            if (outline != null)
            {
                layer.Outline = new List<Vector2>(outline.Count);
                foreach (var p in outline)
                {
                    Vector3 at = opening.GlassCenter + opening.Right * p.x + opening.Up * p.y - layer.Center;
                    layer.Outline.Add(new Vector2(Vector3.Dot(at, layer.Right), Vector3.Dot(at, layer.Up)));
                }
            }
            MakeCoverBank(layer, CoverBankMaterial(opening), "SeamlessInteriors_WindowSnow_" + index);
            StartCoverTables(layer, width / height);
            return layer;
        }

        // Looked up once: a search by name each window, and the first one in a session took 387 ms (2026-10-14).
        private static Shader s_CoverFrostShader;

        internal static Shader CoverFrostShader()
        {
            if (s_CoverFrostShader == null) s_CoverFrostShader = Shader.Find("Sprites/Default");
            return s_CoverFrostShader;
        }

        // Both lit by the day outside, as frost and snow on a window are (dark mode showed the room-lit frost dark).
        private static bool MakeCoverFrost(WindowCoverLayer layer, string name, Vector3 center, Vector3 right, Vector3 up, Vector3 facing, float width, float height, int queue)
        {
            Shader unlit = CoverFrostShader();
            if (unlit == null) return false;
            int n = layer.FrostSize;
            // Taken from the pool when a building before left some: making them was 2 ms a window.
            layer.FrostTex = TakePooled(s_FrostTexPool, n);
            if (layer.FrostTex == null)
            {
                layer.FrostTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "SeamlessInteriors_WindowFrost" };
                layer.FrostTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            layer.FrostMat = TakePooled(s_FrostMatPool, 0);
            if (layer.FrostMat == null)
            {
                layer.FrostMat = new Material(unlit) { name = "SeamlessInteriors_WindowFrost" };
                layer.FrostMat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            layer.FrostMat.mainTexture = layer.FrostTex;
            layer.FrostMat.renderQueue = queue;
            layer.FrostGo = CoverQuad(name, layer.FrostMat, center, right, up, facing, width, height);
            layer.FrostGo.SetActive(false);
            layer.FrostBytes = TakePooled(s_FrostBytesPool, n) ?? new byte[CoverMipBytes(n)];
            return true;
        }

        private static void MakeCoverBank(WindowCoverLayer layer, Material material, string name)
        {
            if (material == null || layer.Panes == null || layer.Panes.Count == 0) return;
            layer.BankGo = new GameObject(name);
            layer.BankMesh = new Mesh { name = "SeamlessInteriors_WindowSnow" };
            layer.BankGo.AddComponent<MeshFilter>().sharedMesh = layer.BankMesh;
            var renderer = layer.BankGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            layer.BankGo.SetActive(false);
        }

        // A window's frost tables, packed: 11 bytes a pixel. Read only once made,
        // so windows of the same size, seed and shape share one set.
        private sealed class CoverTables
        {
            public byte[] Vein, Grain, HazeGain, GlassAlpha, GlassR, GlassG, GlassB;
            public ushort[] VeinTime, HazeTime;

            // How the stored numbers read back (stored = (value - base) / scale).
            public const float UnitScale = 1f / 255f;
            public const float TimeScale = 2f / 65535f;                              // vein time 0..2; later is "never"
            public const float HazeBase = -1f, HazeScale = 3f / 65535f;              // haze time -1..2
            public const float GrainBase = -0.2f, GrainScale = 1.4f / 255f;          // grain -0.2..1.2
            public const float GainScale = 1.2f / 255f;                              // haze gain 0..1.2

            // The glass's colour comes in already as bytes (PrepareCoverGlass).
            public static CoverTables Encode(int n, float[] veinTime, float[] vein, float[] glow, float[] hazeTime, float[] grain,
                                             float[] glassAlpha, byte[] glassR, byte[] glassG, byte[] glassB)
            {
                int count = n * n;
                var t = new CoverTables
                {
                    Vein = new byte[count], Grain = new byte[count], HazeGain = new byte[count], GlassAlpha = new byte[count],
                    GlassR = glassR, GlassG = glassG, GlassB = glassB, VeinTime = new ushort[count], HazeTime = new ushort[count]
                };
                for (int i = 0; i < count; i++)
                {
                    t.Vein[i] = (byte)(C01(vein[i]) * 255f + 0.5f);
                    t.VeinTime[i] = (ushort)(C01(veinTime[i] / 2f) * 65535f + 0.5f);
                    t.HazeTime[i] = (ushort)(C01((hazeTime[i] - HazeBase) / 3f) * 65535f + 0.5f);
                    t.Grain[i] = (byte)(C01((grain[i] - GrainBase) / 1.4f) * 255f + 0.5f);
                    // The haze's own strength, its grain and the glow round the ferns, folded into one.
                    t.HazeGain[i] = (byte)(C01((0.36f + 0.22f * grain[i] + 0.3f * glow[i]) / 1.2f) * 255f + 0.5f);
                    t.GlassAlpha[i] = (byte)(C01(glassAlpha[i]) * 255f + 0.5f);
                }
                return t;
            }
        }

        // Made once a session per size, seed and shape: the same room's windows came back with every entry and
        // were worked out again, 0.7 to 1 s each in the background and 8 MB of arrays a window (2026-10-14).
        private const int COVER_TABLES_KEEP = 48;
        private static readonly Dictionary<string, CoverTables> s_CoverTablesMade = new Dictionary<string, CoverTables>();
        private static readonly Queue<string> s_CoverTablesOrder = new Queue<string>();
        private static readonly object s_CoverTablesLock = new object();

        private static string CoverTablesKey(int size, int seed, float aspect)
        {
            return size + ":" + seed + ":" + Mathf.RoundToInt(aspect * 1000f);
        }

        // The tables take a tenth of a second or more a window; worked out on the main thread they stalled the entry.
        private static void StartCoverTables(WindowCoverLayer layer, float aspect)
        {
            string key = CoverTablesKey(layer.FrostSize, layer.Seed, aspect);
            lock (s_CoverTablesLock)
            {
                CoverTables made;
                if (s_CoverTablesMade.TryGetValue(key, out made)) { layer.Tables = made; layer.Ready = true; return; }
            }
            int size = layer.FrostSize, seed = layer.Seed;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var tables = PrepareCoverPixels(size, aspect, seed);
                    lock (s_CoverTablesLock)
                    {
                        if (!s_CoverTablesMade.ContainsKey(key))
                        {
                            s_CoverTablesMade[key] = tables;
                            s_CoverTablesOrder.Enqueue(key);
                            while (s_CoverTablesOrder.Count > COVER_TABLES_KEEP) s_CoverTablesMade.Remove(s_CoverTablesOrder.Dequeue());
                        }
                    }
                    layer.Tables = tables;
                }
                catch (System.Exception ex) { layer.Error = ex.Message; }
                layer.PrepareMs = watch.ElapsedMilliseconds;
                layer.Ready = true;
            });
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
        private static CoverTables PrepareCoverPixels(int n, float aspect, int seed)
        {
            var sketch = new FrostSketch { N = n, Aspect = aspect, Rng = new System.Random(seed * 7919 + 13), Time = new float[n * n], Vein = new float[n * n] };
            for (int i = 0; i < n * n; i++) sketch.Time[i] = 9f;
            sketch.Grow();
            float[] veinTime = sketch.Time, vein = sketch.Vein;
            float[] glow = CoverBlur(sketch.Vein, n, 4);
            float[] hazeTime = new float[n * n], grain = new float[n * n];
            float edgeScale = aspect < 1f ? aspect : 1f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, ua = u * aspect;
                    float eu = (u < 1f - u ? u : 1f - u) * aspect, ev = v < 1f - v ? v : 1f - v;
                    float edge = C01((eu < ev ? eu : ev) * 2f / edgeScale);
                    int i = y * n + x;
                    // The haze creeps in from the edges and gathers around the ferns first.
                    hazeTime[i] = edge * 0.85f + (CoverFbm(ua * 3f + seed * 3.1f, v * 3f + seed * 1.7f, 4) - 0.5f) * 0.4f + 0.02f - glow[i] * 0.5f;
                    grain[i] = 0.5f + (CoverHash(x + seed * 977, y) - 0.5f) * 0.45f + (CoverFbm(ua * 28f + seed, v * 28f, 2) - 0.5f) * 0.7f;
                }
            float[] glassAlpha = new float[n * n];
            byte[] glassR = new byte[n * n], glassG = new byte[n * n], glassB = new byte[n * n];
            PrepareCoverGlass(n, aspect, seed, glassAlpha, glassR, glassG, glassB);
            return CoverTables.Encode(n, veinTime, vein, glow, hazeTime, grain, glassAlpha, glassR, glassG, glassB);
        }

        // The glass under the frost, painted like the game's own panes but see-through (owner and Lycanthor,
        // 2026-10-17: the old shine band and wipe arcs did not fit). Navy blotches and strokes, mist left at
        // the sides and corners, a few pale specks and chips; colours measured on STR_WindowGlassMixExtApha_A.
        // Plain floats, not Color: this runs off the main thread, where Color's methods call into the game.
        private static readonly float[] s_GlassDark = { 0.19f, 0.24f, 0.28f }, s_GlassLight = { 0.34f, 0.43f, 0.50f };
        private static readonly float[] s_GlassSpeck = { 0.59f, 0.66f, 0.72f }, s_GlassMist = { 0.62f, 0.70f, 0.75f };
        private static readonly float[] s_GlassChip = { 0.49f, 0.57f, 0.63f };   // speck toward light, 0.4
        private const float GLASS_DOT_CELL = 0.035f;

        private static void PrepareCoverGlass(int n, float aspect, int seed, float[] glassAlpha, byte[] glassR, byte[] glassG, byte[] glassB)
        {
            var rng = new System.Random(seed * 31 + 7);
            float sd = (seed % 1000) * 13.7f;
            int chips = 1 + rng.Next(2);
            var cx = new float[chips]; var cy = new float[chips]; var cs = new float[chips]; var cc = new float[chips]; var sn = new float[chips];
            for (int k = 0; k < chips; k++)
            {
                cx[k] = (0.1f + (float)rng.NextDouble() * 0.8f) * aspect; cy[k] = 0.1f + (float)rng.NextDouble() * 0.8f;
                cs[k] = 0.012f + (float)rng.NextDouble() * 0.012f;
                float turn = (float)rng.NextDouble() * 6.28f; cc[k] = (float)System.Math.Cos(turn); sn[k] = (float)System.Math.Sin(turn);
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n, ua = u * aspect;
                    float blot = CoverFbm(ua * 1.8f + sd, v * 1.8f, 5);
                    float warp = CoverFbm(ua * 1.5f + sd + 40f, v * 1.5f, 2);
                    float stroke = CoverFbm(ua * 1.2f + warp * 1.2f + sd, v * 6f, 4);
                    float shade = C01((blot - 0.40f) * 2.6f) * 0.55f + C01((stroke - 0.48f) * 3f) * 0.45f;
                    shade = CLerp(shade, CFloor(shade * 4f) / 4f, 0.5f);   // flat brush steps
                    float wisp = C01((CoverFbm(ua * 3.5f + sd + 90f, v * 3.5f, 5) - 0.58f) * 5f);
                    float tone = C01(shade * 0.8f + wisp * 0.5f);
                    float cr = CLerp(s_GlassDark[0], s_GlassLight[0], tone), cg = CLerp(s_GlassDark[1], s_GlassLight[1], tone), cb = CLerp(s_GlassDark[2], s_GlassLight[2], tone);
                    float a = 0.12f + shade * 0.09f + wisp * 0.10f;

                    // Mist at the sides, thicker in the corners, with a ragged painted edge.
                    float ex = (u < 1f - u ? u : 1f - u) * aspect, ey = v < 1f - v ? v : 1f - v;
                    float rag = (CoverFbm(ua * 7f + sd + 200f, v * 7f, 4) - 0.5f) * 0.05f;
                    float side = C01(1f - ((ex < ey ? ex : ey) + rag) / 0.035f) * 0.75f;
                    float nook = C01(1f - ((float)System.Math.Sqrt(ex * ey) + rag * 1.5f) / 0.075f);
                    float fog = side > nook ? side : nook;
                    fog = C01(fog * fog * (3f - 2f * fog) * (0.75f + CoverFbm(ua * 12f + sd, v * 12f, 3) * 0.5f));
                    cr = CLerp(cr, s_GlassMist[0], fog); cg = CLerp(cg, s_GlassMist[1], fog); cb = CLerp(cb, s_GlassMist[2], fog);
                    a = CLerp(a, 0.78f, fog);

                    int gx = CFloor(ua / GLASS_DOT_CELL), gy = CFloor(v / GLASS_DOT_CELL);
                    if (CoverHash(gx + seed * 101, gy) > 0.975f)
                    {
                        float px = (gx + 0.2f + CoverHash(gx, gy + 7) * 0.6f) * GLASS_DOT_CELL, py = (gy + 0.2f + CoverHash(gx + 3, gy) * 0.6f) * GLASS_DOT_CELL;
                        float r = 0.0025f + CoverHash(gx + 9, gy + 9) * 0.003f;
                        if ((ua - px) * (ua - px) + (v - py) * (v - py) < r * r)
                        { cr = s_GlassSpeck[0]; cg = s_GlassSpeck[1]; cb = s_GlassSpeck[2]; a = a > 0.6f ? a : 0.6f; }
                    }
                    for (int k = 0; k < chips; k++)
                    {
                        float dx = ua - cx[k], dy = v - cy[k], lx = dx * cc[k] - dy * sn[k], ly = dx * sn[k] + dy * cc[k];
                        float d = System.Math.Max(System.Math.Abs(lx) * 1.3f, System.Math.Abs(ly)) + System.Math.Abs(lx + ly) * 0.35f;
                        if (d < cs[k]) { cr = s_GlassChip[0]; cg = s_GlassChip[1]; cb = s_GlassChip[2]; a = a > 0.5f ? a : 0.5f; }
                    }
                    int i = y * n + x;
                    glassAlpha[i] = C01(a);
                    glassR[i] = (byte)(C01(cr) * 255f + 0.5f); glassG[i] = (byte)(C01(cg) * 255f + 0.5f); glassB[i] = (byte)(C01(cb) * 255f + 0.5f);
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
            // Asked for here, made a few rows a frame by PumpCoverFills: a room window's 512 x 512 in one go was
            // 23 ms (2026-10-14). The texture keeps the old frost until the new one is whole.
            if (layer == null || layer.Tables == null) return;
            if (layer.FillNext >= 0 ? layer.FillTarget == frost : layer.FrostShown == frost) return;
            // A fill under way is not restarted for a smaller step than shows: it would never finish.
            if (layer.FillNext >= 0 && Mathf.Abs(frost - layer.FillTarget) < COVER_FROST_STEP && (frost == 0f) == (layer.FillTarget == 0f)) return;
            layer.FillTarget = frost;
            layer.FillNext = 0;
        }

        // Pixels made a frame, across all the windows waiting.
        private const int COVER_FILL_PIXELS_PER_FRAME = 32768;

        private static void PumpCoverFills(List<WindowCoverLayer> layers)
        {
            int budget = COVER_FILL_PIXELS_PER_FRAME;
            for (int l = 0; l < layers.Count && budget > 0; l++)
            {
                var layer = layers[l];
                if (layer == null || layer.FillNext < 0 || layer.Tables == null || layer.FrostTex == null) continue;
                long perf = PerfProbe.Begin();
                try
                {
                    int count = layer.Tables.Vein.Length;
                    if (layer.FillNext < count)
                    {
                        int end = Mathf.Min(count, layer.FillNext + budget);
                        FillCoverRange(layer, layer.FillTarget, layer.FillNext, end);
                        budget -= end - layer.FillNext;
                        layer.FillNext = end;
                        // The mips and the upload on a frame of their own: with the last rows they made the worst frame.
                        continue;
                    }
                    budget -= count / 4;

                    CoverMips(layer.FrostBytes, layer.FrostSize);
                    var pin = GCHandle.Alloc(layer.FrostBytes, GCHandleType.Pinned);
                    try { layer.FrostTex.LoadRawTextureData(pin.AddrOfPinnedObject(), layer.FrostBytes.Length); }
                    finally { pin.Free(); }
                    layer.FrostTex.Apply(false);
                    layer.FrostShown = layer.FillTarget;
                    layer.FillNext = -1;
                    PerfProbe.Count(PerfProbe.Hit.CoverFills);
                    // Shown on the next frame, not the next once-a-second look.
                    s_CoverDirty = true;
                }
                finally { PerfProbe.End(PerfProbe.Section.CoverFill, perf); }
            }
        }

        // Optimised from the first call: .NET runs a method's first calls unoptimised, and a fill comes seldom
        // enough to stay that way (16 ms first, 5 ms from the tenth on, 2026-10-14). The pixels go straight into
        // the texture's own bytes, mips made here: SetPixels32 copied a whole array into the game each fill.
        [MethodImpl(CoverOptimised)]
        private static void FillCoverRange(WindowCoverLayer layer, float frost, int from, int to)
        {
            var tb = layer.Tables;
            float reach = frost * 1.15f, haze0 = frost * 0.06f;
            var veinTime = tb.VeinTime; var vein = tb.Vein; var hazeTime = tb.HazeTime; var grain = tb.Grain; var hazeGain = tb.HazeGain;
            var glassAlpha = tb.GlassAlpha; var glassR = tb.GlassR; var glassG = tb.GlassG; var glassB = tb.GlassB; var mask = layer.FrostMask;
            var bytes = layer.FrostBytes;
            for (int i = from; i < to; i++)
            {
                float g = grain[i] * CoverTables.GrainScale + CoverTables.GrainBase;
                float v = vein[i] * CoverTables.UnitScale * C01((reach - veinTime[i] * CoverTables.TimeScale) * 12f);
                float haze = C01((reach - (hazeTime[i] * CoverTables.HazeScale + CoverTables.HazeBase)) * 2.5f);
                float a = C01(haze0 + haze * hazeGain[i] * CoverTables.GainScale + v * 0.7f);
                float w = C01(0.80f + 0.20f * v + 0.05f * g);
                // The frost laid over the glass.
                float ga = glassAlpha[i] * CoverTables.UnitScale * (1f - a), all = a + ga, gs = ga * CoverTables.UnitScale;
                float k = all > 0.0001f ? 1f / all : 0f;
                int o = i * 4;
                bytes[o] = (byte)(C01((w * 0.90f * a + glassR[i] * gs) * k) * 255f);
                bytes[o + 1] = (byte)(C01((w * 0.95f * a + glassG[i] * gs) * k) * 255f);
                bytes[o + 2] = (byte)(C01((w * a + glassB[i] * gs) * k) * 255f);
                bytes[o + 3] = (byte)(C01(mask != null ? all * mask[i] : all) * 255f);
            }
        }

        // The RGBA32 mip chain after the first level, each a 2x2 average of the one before.
        private static int CoverMipBytes(int size)
        {
            int total = 0;
            for (int s = size; ; s >>= 1) { total += s * s * 4; if (s == 1) break; }
            return total;
        }

        [MethodImpl(CoverOptimised)]
        private static void CoverMips(byte[] bytes, int size)
        {
            int src = 0;
            for (int s = size; s > 1; s >>= 1)
            {
                int half = s >> 1, dst = src + s * s * 4, row = s * 4;
                for (int y = 0; y < half; y++)
                    for (int x = 0; x < half; x++)
                    {
                        int a = src + (y * 2) * row + x * 8, d = dst + (y * half + x) * 4;
                        for (int c = 0; c < 4; c++)
                            bytes[d + c] = (byte)((bytes[a + c] + bytes[a + 4 + c] + bytes[a + row + c] + bytes[a + row + 4 + c] + 2) >> 2);
                    }
                src = dst;
            }
        }

        // The panes are the glass mesh's own rectangles (a sash window has one per sash).
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static List<float[]> CoverPanes(InteriorWindowOpening opening, Vector3 center, float width, float height)
        {
            var panes = new List<float[]>();
            float bottom = -height * 0.5f;
            for (int g = 0; g < opening.Glass.Count; g++)
            {
                var renderer = opening.Glass[g];
                var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                // Managed copies, once: indexing the game's arrays and TransformPoint were a call per vertex.
                int[] triangles = mesh.triangles;
                Vector3[] world = CoverToWorld(renderer.transform, mesh.vertices);
                for (int k = 0; k + 2 < triangles.Length; k += 3)
                {
                    float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue;
                    Vector3 a0 = Vector3.zero, a1 = Vector3.zero, a2 = Vector3.zero;
                    for (int j = 0; j < 3; j++)
                    {
                        var p = world[triangles[k + j]] - center;
                        if (j == 0) a0 = p; else if (j == 1) a1 = p; else a2 = p;
                        float r = Vector3.Dot(p, opening.Right), u = Vector3.Dot(p, opening.Up);
                        r0 = Mathf.Min(r0, r); r1 = Mathf.Max(r1, r); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                    }
                    // A glass mesh shared by the building's windows: only this window's own glass counts.
                    Vector3 mid = (a0 + a1 + a2) / 3f, normal = Vector3.Cross(a1 - a0, a2 - a0);
                    if (Mathf.Abs(Vector3.Dot(mid, opening.Outward)) > 0.15f) continue;
                    if (normal.sqrMagnitude > 1e-12f && Mathf.Abs(Vector3.Dot(normal.normalized, opening.Outward)) < 0.7f) continue;
                    float midR = Vector3.Dot(mid, opening.Right), midU = Vector3.Dot(mid, opening.Up);
                    if (Mathf.Abs(midR) > width * 0.5f + 0.05f || Mathf.Abs(midU) > height * 0.5f + 0.05f) continue;
                    r0 = Mathf.Max(r0, -width * 0.5f); r1 = Mathf.Min(r1, width * 0.5f);
                    u0 = Mathf.Max(u0, -height * 0.5f - 0.05f); u1 = Mathf.Min(u1, height * 0.5f);
                    // Glass that runs on behind the wall below the frame (see CoverRect) is cut off.
                    if (u0 < bottom - 0.02f) u0 = bottom;
                    if (u1 - u0 < 0.05f || r1 - r0 < 0.05f) continue;
                    bool seen = false;
                    foreach (var pane in panes)
                        if (Mathf.Abs(pane[0] - r0) < 0.01f && Mathf.Abs(pane[1] - r1) < 0.01f && Mathf.Abs(pane[2] - u0) < 0.01f && Mathf.Abs(pane[3] - u1) < 0.01f) { seen = true; break; }
                    if (!seen) panes.Add(new[] { r0, r1, u0, u1, 0f });
                }
            }
            MergeTouchingPanes(panes);
            float barLow, barHigh;
            if (TryMeasureRoomBar(opening, center, width, height, out barLow, out barHigh)) SplitPanesAtBar(panes, barLow, barHigh);
            SplitPanesAtPosts(panes, MeasureRoomPosts(opening, center, width, height));
            MarkBarsAbove(panes);
            return panes;
        }

        // One sheet of glass modelled as a grid of quads (Trapper's) is one pane: rectangles that share
        // a whole edge are joined. Panes split by a bar leave a gap and stay apart.
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static void MergeTouchingPanes(List<float[]> panes)
        {
            const float tol = 0.01f;
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < panes.Count && !merged; i++)
                    for (int j = i + 1; j < panes.Count && !merged; j++)
                    {
                        float[] a = panes[i], b = panes[j];
                        bool sameRows = Mathf.Abs(a[2] - b[2]) < tol && Mathf.Abs(a[3] - b[3]) < tol;
                        bool sameColumns = Mathf.Abs(a[0] - b[0]) < tol && Mathf.Abs(a[1] - b[1]) < tol;
                        bool sideBySide = sameRows && (Mathf.Abs(a[1] - b[0]) < tol || Mathf.Abs(b[1] - a[0]) < tol);
                        bool stacked = sameColumns && (Mathf.Abs(a[3] - b[2]) < tol || Mathf.Abs(b[3] - a[2]) < tol);
                        // The two triangles of one slightly skewed pane give two rectangles over each other
                        // (Barn's: 0.06 and 0.10 to 0.38); two banks then stood inside each other and flickered.
                        float overlapX = Mathf.Min(a[1], b[1]) - Mathf.Max(a[0], b[0]), overlapY = Mathf.Min(a[3], b[3]) - Mathf.Max(a[2], b[2]);
                        float smaller = Mathf.Min((a[1] - a[0]) * (a[3] - a[2]), (b[1] - b[0]) * (b[3] - b[2]));
                        bool overlapping = overlapX > 0f && overlapY > 0f && overlapX * overlapY > smaller * 0.5f;
                        if (!sideBySide && !stacked && !overlapping) continue;
                        a[0] = Mathf.Min(a[0], b[0]); a[1] = Mathf.Max(a[1], b[1]);
                        a[2] = Mathf.Min(a[2], b[2]); a[3] = Mathf.Max(a[3], b[3]);
                        panes.RemoveAt(j);
                        merged = true;
                    }
            }
        }

        // Glass running on behind a bar is two panes, one under it and one over it.
        private static void SplitPanesAtBar(List<float[]> panes, float barLow, float barHigh)
        {
            for (int i = panes.Count - 1; i >= 0; i--)
            {
                var p = panes[i];
                if (p[2] > barLow - 0.02f || p[3] < barHigh + 0.02f) continue;
                panes.Add(new[] { p[0], p[1], barHigh, p[3], 0f });
                p[3] = barLow;
            }
        }

        // A mesh's vertices in world space, from the transform's matrix read once.
        [MethodImpl(CoverOptimised)]
        private static Vector3[] CoverToWorld(Transform t, Vector3[] local)
        {
            Matrix4x4 m = t.localToWorldMatrix;
            var world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++)
            {
                Vector3 p = local[i];
                var w = default(Vector3);
                w.x = m.m00 * p.x + m.m01 * p.y + m.m02 * p.z + m.m03;
                w.y = m.m10 * p.x + m.m11 * p.y + m.m12 * p.z + m.m13;
                w.z = m.m20 * p.x + m.m21 * p.y + m.m22 * p.z + m.m23;
                world[i] = w;
            }
            return world;
        }
        // UPRIGHT POSTS. The side faces of a frame that run most of the glass's height, inside its width, as
        // places along `right` from `center`. A sheet of glass behind a post is two panes (Thompson's Store, 2026-10-13).
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static void CollectPostSides(Vector3[] world, int[] triangles, Vector3 center, Vector3 right, Vector3 up, Vector3 outward,
                                             float left, float rightEdge, float bottom, float top, List<float> sides)
        {
            float height = top - bottom, midUp = (top + bottom) * 0.5f;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p0 = world[triangles[t]] - center, p1 = world[triangles[t + 1]] - center, p2 = world[triangles[t + 2]] - center;
                Vector3 middle = (p0 + p1 + p2) / 3f;
                if (Mathf.Abs(Vector3.Dot(middle, outward)) > 0.1f) continue;
                float across = Vector3.Dot(middle, right);
                if (across < left + 0.03f || across > rightEdge - 0.03f) continue;
                if (Mathf.Abs(Vector3.Dot(middle, up) - midUp) > height * 0.5f) continue;
                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                if (normal.sqrMagnitude < 1e-10f || Mathf.Abs(Vector3.Dot(normal.normalized, right)) < 0.95f) continue;
                float longest = Mathf.Max((p0 - p1).magnitude, Mathf.Max((p1 - p2).magnitude, (p0 - p2).magnitude));
                if (longest < height * 0.5f) continue;
                sides.Add(across);
            }
        }

        // Side faces close together are one post: its two sides, or two chamfers of one side.
        private static List<Vector2> ClusterPosts(List<float> sides)
        {
            sides.Sort();
            var posts = new List<Vector2>();
            for (int i = 0; i < sides.Count; )
            {
                int j = i;
                while (j + 1 < sides.Count && sides[j + 1] - sides[i] <= 0.2f) j++;
                if (j > i && sides[j] - sides[i] >= 0.01f) posts.Add(new Vector2(sides[i], sides[j]));
                i = j + 1;
            }
            return posts;
        }

        // A pane running on behind a post is two panes, meeting behind its middle: cut at its sides, the
        // gap behind the post showed dark from the side (owner, 2026-10-13).
        private static void SplitPanesAtPosts(List<float[]> panes, List<Vector2> posts)
        {
            foreach (var post in posts)
            {
                float middle = (post.x + post.y) * 0.5f;
                for (int i = panes.Count - 1; i >= 0; i--)
                {
                    var p = panes[i];
                    if (p[0] > post.x - 0.02f || p[1] < post.y + 0.02f) continue;
                    panes.Add(new[] { middle + 0.001f, p[1], p[2], p[3], p[4] });
                    p[1] = middle - 0.001f;
                }
            }
        }

        // How far the posts' front faces stand out from the glass (the nearest of them), or 0 when none is found.
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static float PostFront(Vector3[] world, int[] triangles, Vector3 center, Vector3 right, Vector3 outward, List<Vector2> posts)
        {
            float front = float.MaxValue;
            foreach (var post in posts)
            {
                float best = 0f;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    Vector3 p0 = world[triangles[t]] - center, p1 = world[triangles[t + 1]] - center, p2 = world[triangles[t + 2]] - center;
                    Vector3 middle = (p0 + p1 + p2) / 3f;
                    float across = Vector3.Dot(middle, right);
                    if (across < post.x || across > post.y) continue;
                    Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                    if (normal.sqrMagnitude < 1e-10f || Vector3.Dot(normal.normalized, outward) < 0.7f) continue;
                    float proud = Vector3.Dot(middle, outward);
                    if (proud > best && proud < 0.15f) best = proud;
                }
                if (best > 0f && best < front) front = best;
            }
            return front < float.MaxValue ? front : 0f;
        }

        // The room frame's posts, from the frame meshes under the pane (glass and slabs left out).
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static List<Vector2> MeasureRoomPosts(InteriorWindowOpening opening, Vector3 center, float width, float height)
        {
            var sides = new List<float>();
            if (opening.PaneRoot != null)
                foreach (var filter in opening.PaneRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null || !mesh.isReadable) continue;
                    var renderer = filter.GetComponent<Renderer>();
                    if (renderer != null && (opening.Glass.Contains(renderer) || (opening.Frost != null && opening.Frost.Contains(renderer)))) continue;
                    var local = mesh.vertices;
                    var world = CoverToWorld(filter.transform, local);
                    CollectPostSides(world, (int[])mesh.triangles, center, opening.Right, opening.Up, opening.Outward,
                                     -width * 0.5f, width * 0.5f, -height * 0.5f, height * 0.5f, sides);
                }
            return ClusterPosts(sides);
        }

        // The room frame's horizontal bar across the glass, as heights from center: the frame's flat
        // top and underside faces that run most of the glass's width (Trapper's frame, 2026-10-13).
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static bool TryMeasureRoomBar(InteriorWindowOpening opening, Vector3 center, float width, float height, out float low, out float high)
        {
            low = float.MaxValue; high = float.MinValue;
            if (opening.PaneRoot == null) return false;
            int faces = 0;
            foreach (var filter in opening.PaneRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                var renderer = filter.GetComponent<Renderer>();
                if (renderer != null && (opening.Glass.Contains(renderer) || (opening.Frost != null && opening.Frost.Contains(renderer)))) continue;
                int[] triangles = mesh.triangles;
                Vector3[] world = CoverToWorld(filter.transform, mesh.vertices);
                for (int k = 0; k + 2 < triangles.Length; k += 3)
                {
                    Vector3 p0 = world[triangles[k]] - center;
                    Vector3 p1 = world[triangles[k + 1]] - center;
                    Vector3 p2 = world[triangles[k + 2]] - center;
                    Vector3 middle = (p0 + p1 + p2) / 3f;
                    if (Mathf.Abs(Vector3.Dot(middle, opening.Outward)) > 0.1f) continue;
                    if (Mathf.Abs(Vector3.Dot(middle, opening.Right)) > width * 0.5f) continue;
                    float h = Vector3.Dot(middle, opening.Up);
                    if (Mathf.Abs(h) > height * 0.5f - 0.05f) continue;
                    Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                    if (normal.sqrMagnitude < 1e-10f || Mathf.Abs(Vector3.Dot(normal.normalized, opening.Up)) < 0.95f) continue;
                    float longest = Mathf.Max((p0 - p1).magnitude, Mathf.Max((p1 - p2).magnitude, (p0 - p2).magnitude));
                    if (longest < width * 0.5f) continue;
                    faces++;
                    if (h < low) low = h;
                    if (h > high) high = h;
                }
            }
            return faces >= 2 && high - low >= 0.01f && high - low <= 0.2f;
        }

        private static Material s_FrameSnowHiddenMaterial;

        // Snow modelled into the frame itself (Trapper's Obj_WindowCabinSnowA, a second material on the frame):
        // the bank stands in for it, and a slot of a renderer can only be hidden by a see-through material.
        //
        // Only snow assets, never a building surface. The game's wall, roof and concrete materials are GLB_ and
        // about 80 of them carry "Snow" in the name (GLB_ConcreteRust_A01_FlatSnow, GLB_WoodWallBlue_B01_Snow_Blend):
        // the surface itself, with snow on its top faces. Where a pane root is a whole building part, as the hangar's
        // front is (STR_AF_Hangar_Interior_Main_Split_Prefab), hiding those made all eight front doors invisible
        // from inside and took a slot each off the roof, stairs and wall (2.3.1 to 2.4.0, read live 2026-10-16).
        private static bool IsBuildingSurfaceSnow(Material m)
        {
            return m.name.StartsWith("GLB_", System.StringComparison.OrdinalIgnoreCase);
        }

        private static void HideFrameSnowSlots(Transform paneRoot)
        {
            if (!WindowCoverLayers || paneRoot == null) return;
            foreach (var renderer in paneRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                if (materials.Length < 2) continue;
                bool changed = false;
                for (int k = 0; k < materials.Length; k++)
                {
                    var m = materials[k];
                    if (m == null || m.name.IndexOf("Snow", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (IsBuildingSurfaceSnow(m))
                    {
                        MelonLogger.Msg($"[WINDOW] {renderer.gameObject.name}: '{m.name}' bina yuzeyi, cerceve kari degil; gizlenmedi.",
                                        $"[WINDOW] {renderer.gameObject.name}: '{m.name}' is a building surface, not frame snow; left drawn.");
                        continue;
                    }
                    MelonLogger.Msg($"[WINDOW] {renderer.gameObject.name}: '{m.name}' cerceve kari olarak gizlendi.",
                                    $"[WINDOW] {renderer.gameObject.name}: '{m.name}' hidden as snow modelled into the frame.");
                    if (s_FrameSnowHiddenMaterial == null)
                    {
                        Shader shader = Shader.Find(OUTSIDE_IN_FROST_SHADER);
                        if (shader == null) return;
                        s_FrameSnowHiddenMaterial = new Material(shader) { name = "SeamlessInteriors_HiddenFrameSnow", color = new Color(0f, 0f, 0f, 0f) };
                        s_FrameSnowHiddenMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }
                    materials[k] = s_FrameSnowHiddenMaterial;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        // A pane with another pane just above it has a bar over its top.
        private static void MarkBarsAbove(List<float[]> panes)
        {
            foreach (var pane in panes)
            {
                pane[4] = 0f;
                foreach (var other in panes)
                    if (other != pane && other[2] >= pane[3] - 0.01f && other[2] - pane[3] < 0.2f && other[0] < pane[1] && other[1] > pane[0]) pane[4] = 1f;
            }
        }

        // The game's own window snow (TRN_GroundSnowPaintyA on the frame's slab), lit like the rest of the scene.
        private static Material s_CoverBankMaterial;

        // The look the owner picked (2026-10-13, Community Hall): the snow modelled on the buildings themselves,
        // sills, chimneys and stairs alike, where the painted ground snow showed big blots. Kept as a copy of our
        // own once seen, so a region without it still has it.
        private const string COVER_BANK_PREFERRED = "TRN_Snow_A07";
        private static Material s_CoverBankPreferred;
        private static float s_CoverBankSearchAt;

        private static Material CoverBankMaterial(InteriorWindowOpening opening)
        {
            // Looked for at most every half minute: the search walks every material loaded.
            if (s_CoverBankPreferred == null && Time.realtimeSinceStartup >= s_CoverBankSearchAt)
            {
                s_CoverBankSearchAt = Time.realtimeSinceStartup + 30f;
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null && m.name == COVER_BANK_PREFERRED)
                    {
                        s_CoverBankPreferred = new Material(m) { name = COVER_BANK_PREFERRED + "_SeamlessInteriors" };
                        s_CoverBankPreferred.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        break;
                    }
            }
            if (s_CoverBankPreferred != null) return s_CoverBankPreferred;

            if (opening.Frost != null)
                for (int i = 0; i < opening.Frost.Count; i++)
                    if (opening.Frost[i] != null && opening.Frost[i].sharedMaterial != null) return s_CoverBankMaterial = opening.Frost[i].sharedMaterial;
            if (s_CoverBankMaterial != null) return s_CoverBankMaterial;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.name.StartsWith("TRN_GroundSnowPaintyA")) return s_CoverBankMaterial = m;
            return null;
        }

        private static float CoverSmooth(float x) { x = C01(x); return x * x * (3f - 2f * x); }

        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]

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
            long perf = PerfProbe.Begin();
            PerfProbe.Count(PerfProbe.Hit.CoverBanks);
            try { BuildCoverBankTimed(layer, snow); } finally { PerfProbe.End(PerfProbe.Section.CoverBank, perf); }
        }

        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]

        private static void BuildCoverBankTimed(WindowCoverLayer layer, float snow)
        {
            const int nx = BANK_COLUMNS, rows = BANK_ROWS + BANK_ROUND_ROWS, cols = nx + 1;
            // Per pane: the face on the glass, the top and rounded face as a grid, two end caps.
            const int paneVerts = 2 * cols + (rows + 1) * cols + 2 * (rows + 2);
            const int paneTris = (nx + rows * nx + 2 * rows) * 6;
            if (layer.BankNoise == null) PrepareBankNoise(layer);
            int panes = layer.Panes.Count;
            if (snow <= 0f || panes == 0) { layer.SnowShown = snow; return; }

            int vertexCount = panes * paneVerts;
            if (layer.BankVerts == null || layer.BankVerts.Length != vertexCount)
            {
                layer.BankVerts = new Vector3[vertexCount];
                layer.BankUvs = new Vector2[vertexCount];
                layer.BankTrisSet = false;
            }
            var verts = layer.BankVerts; var uvs = layer.BankUvs;
            int[] tris = layer.BankTrisSet ? null : new int[panes * paneTris];
            int v = 0, t = 0;

            // The dunes roll while snow builds and settle into a full pane at the end.
            float roll = snow * (1f - snow * snow);
            var tops = s_BankTops;
            for (int p = 0; p < panes; p++)
            {
                var pane = layer.Panes[p];
                int seed = layer.Seed * 13 + p * 7;
                float x0 = pane[0], pw = pane[1] - pane[0], hb = pane[2] - layer.BankTuck, ph = pane[3] - hb;
                // A pane too narrow to hold snow still keeps its place in the mesh, folded flat.
                bool flat = pw < 0.05f;
                float fullTop = pane[4] > 0f ? 1f - layer.BankUnderBar / ph : 1f;
                float sideLeft = CoverHash(seed, 1) > 0.45f ? 1f : -0.6f, sideRight = CoverHash(seed, 2) > 0.45f ? 1f : -0.6f;
                float uOff = CoverHash(seed, 11) * 7f, vOff = CoverHash(seed, 12) * 7f;
                int noiseBase = p * (cols * (rows + 2));
                for (int i = 0; i <= nx; i++)
                {
                    float xl = (float)i / nx, wx = xl * pw / ph;
                    float el = CoverSmooth(1f - xl * pw / BANK_SIDE_WIDTH), er = CoverSmooth(1f - (1f - xl) * pw / BANK_SIDE_WIDTH);
                    // Sampled along the snow amount too, so the bank rises unevenly instead of just scaling up.
                    float shape = (el * sideLeft + er * sideRight) * BANK_SIDE_RISE
                                + (CoverFbm(wx * 1.4f + seed, snow * BANK_DRIFT, 3) - 0.5f) * 1.8f
                                + (CoverFbm(wx * 9f + seed, 3.5f + snow * BANK_DRIFT * 2f, 2) - 0.5f) * 0.15f;
                    // Even a full pane keeps small gaps along its top (owner, 2026-10-13).
                    float gap = layer.BankNoise[noiseBase + (rows + 1) * cols + i];
                    float level = snow * (fullTop + 0.03f - gap) + shape * roll;
                    float cap = fullTop - gap * snow;
                    tops[i] = flat ? hb : hb + C01(level < cap ? level : cap) * ph;
                    if (layer.Outline != null && !flat) tops[i] = Mathf.Max(hb, Mathf.Min(tops[i], OutlineTop(layer.Outline, x0 + pw * i / nx, tops[i])));
                }

                int front = v;
                for (int i = 0; i <= nx; i++)
                {
                    float xr = x0 + pw * i / nx;
                    verts[v] = CoverPoint(layer, xr, hb, layer.BankStandOff); uvs[v++] = BankUv(uOff, vOff, xr, hb);
                    verts[v] = CoverPoint(layer, xr, tops[i], layer.BankStandOff); uvs[v++] = BankUv(uOff, vOff, xr, tops[i]);
                }
                if (tris != null)
                    for (int i = 0; i < nx; i++)
                    {
                        int a = front + i * 2;
                        tris[t++] = a; tris[t++] = a + 1; tris[t++] = a + 2; tris[t++] = a + 1; tris[t++] = a + 3; tris[t++] = a + 2;
                    }

                // The top slumps away from the glass, then rolls over in a rounded face down to the sill:
                // the face seen from outside (owner, 2026-10-13).
                int top = v;
                for (int j = 0; j <= rows; j++)
                    for (int i = 0; i <= nx; i++)
                    {
                        float xr = x0 + pw * i / nx, h, oz;
                        BankProfile(layer, tops[i], hb, j, layer.BankNoise[noiseBase + j * cols + i], out h, out oz);
                        verts[v] = CoverPoint(layer, xr, h, oz); uvs[v++] = BankUv(uOff, vOff, xr, oz - h + 1.5f);
                    }
                if (tris != null)
                    for (int j = 0; j < rows; j++)
                        for (int i = 0; i < nx; i++)
                        {
                            int a = top + j * cols + i, b = a + cols;
                            tris[t++] = a; tris[t++] = b; tris[t++] = a + 1; tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1;
                        }

                // End caps, both windings so they show from either side.
                for (int e = 0; e < 2; e++)
                {
                    int i = e == 0 ? 0 : nx;
                    float xr = x0 + pw * i / nx;
                    int capStart = v;
                    verts[v] = CoverPoint(layer, xr, hb, layer.BankStandOff); uvs[v++] = BankUv(uOff, vOff, 0f, hb);
                    for (int j = 0; j <= rows; j++)
                    {
                        float h, oz;
                        BankProfile(layer, tops[i], hb, j, layer.BankNoise[noiseBase + j * cols + i], out h, out oz);
                        verts[v] = CoverPoint(layer, xr, h, oz); uvs[v++] = BankUv(uOff, vOff, oz, h);
                    }
                    if (tris != null)
                        for (int j = 1; j <= rows; j++)
                        {
                            tris[t++] = capStart; tris[t++] = capStart + j + 1; tris[t++] = capStart + j;
                            tris[t++] = capStart; tris[t++] = capStart + j; tris[t++] = capStart + j + 1;
                        }
                }

                // Folded to one point: nothing to draw.
                if (flat)
                    for (int k = front + 1; k < v; k++) { verts[k] = verts[front]; uvs[k] = uvs[front]; }
            }

            // The mesh keeps its triangles: only the points move as the snow builds.
            var mesh = layer.BankMesh;
            mesh.vertices = verts;
            mesh.uv = uvs;
            if (tris != null) { mesh.triangles = tris; layer.BankTrisSet = true; }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            layer.SnowShown = snow;
        }

        private static readonly float[] s_BankTops = new float[BANK_COLUMNS + 1];

        // The parts of the bank's shape that do not change with the snow, once a window: the wobble of its top
        // across each row and the gaps along a full pane's top. Worked out again on every rebuild, they made a
        // wide window's bank 20 to 34 ms (2026-10-14).
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static void PrepareBankNoise(WindowCoverLayer layer)
        {
            const int nx = BANK_COLUMNS, rows = BANK_ROWS + BANK_ROUND_ROWS, cols = nx + 1;
            var noise = new float[layer.Panes.Count * cols * (rows + 2)];
            for (int p = 0; p < layer.Panes.Count; p++)
            {
                var pane = layer.Panes[p];
                int seed = layer.Seed * 13 + p * 7, b = p * cols * (rows + 2);
                float x0 = pane[0], pw = pane[1] - pane[0], ph = pane[3] - (pane[2] - layer.BankTuck);
                for (int i = 0; i <= nx; i++)
                {
                    float xr = x0 + pw * i / nx, wx = (float)i / nx * pw / ph;
                    for (int j = 0; j <= rows; j++)
                    {
                        int edgeRow = j < BANK_ROWS ? j : BANK_ROWS;
                        float zl = (float)edgeRow / BANK_ROWS;
                        noise[b + j * cols + i] = (CoverFbm(xr * 6f + seed, zl * 3f, 2) - 0.5f) * 0.2f * zl;
                    }
                    noise[b + (rows + 1) * cols + i] = C01((CoverFbm(wx * 3f + seed * 1.7f, 7.5f, 3) - 0.45f) * 4f) * BANK_TOP_GAPS;
                }
            }
            layer.BankNoise = noise;
        }

        // Where the snow texture is read for a point of the bank. Laid straight on, it tiled twice a metre and
        // a long window showed the same blots five times over (owner, Thompson's Store, 2026-10-13): each pane
        // starts somewhere of its own.
        private static Vector2 BankUv(float uOff, float vOff, float across, float up)
        {
            var uv = default(Vector2);
            uv.x = across * BANK_UV + uOff; uv.y = up * BANK_UV + vOff;
            return uv;
        }

        // One point of the bank's cross-section: the top, lower the further it lies out from the glass,
        // then a quarter ellipse from its outer edge down to the sill.
        private static void BankProfile(WindowCoverLayer layer, float top, float bottom, int row, float wobble, out float height, out float outward)
        {
            int edgeRow = row < BANK_ROWS ? row : BANK_ROWS;
            float zl = (float)edgeRow / BANK_ROWS;
            height = bottom + (top - bottom) * (1f - layer.BankSlump * zl * zl + wobble);
            outward = layer.BankStandOff + layer.BankDepth * zl;
            if (row <= BANK_ROWS) return;
            float rise = height - bottom;
            float angle = 1.5708f * (row - BANK_ROWS) / BANK_ROUND_ROWS;
            float reach = rise * 0.6f + 0.005f;
            outward += (layer.BankReach < reach ? layer.BankReach : reach) * (float)System.Math.Sin(angle);
            height = bottom + rise * (float)System.Math.Cos(angle);
        }
        // Frost is lit by the day outside, not by the room: lit by the room it read as a white wall.
        private static Color CoverSnowLight()
        {
            Color fog = RenderSettings.fogColor;
            Color ambient = RenderSettings.ambientLight;
            float level = (ambient.r + ambient.g + ambient.b) / 3f;
            // Under a skybox ambient the flat colour is not the light (Trapper's read 0.04 at 15:00, 2026-10-13):
            // the day's level is taken from the fog then.
            if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Skybox)
                level = Mathf.Max(level, (fog.r + fog.g + fog.b) / 3f * 0.8f);
            Color c = Color.Lerp(fog, Color.white, 0.55f) * Mathf.Clamp(level * 1.35f, 0.05f, 1.1f);
            c.a = 1f;
            return c;
        }

        // Inside, the frost and the bank take the outdoor ambient the window views are drawn with (owner,
        // 2026-10-13). Read off the views themselves first, it jumped with what each view happened to show:
        // 0.17 to 0.7 within a tenth of a second at Community Hall, the frost flickering with every step.
        // The bank as bright as the frost: three times over-lit it white with blue blots, unlike the same bank seen
        // from outside (owner, Community Hall at night, 2026-10-19).
        // The thick mound's room-facing side read dark grey at 1x; 2.2x with a little more blue (owner's "C", 2026-10-20).
        private const float COVER_OUTDOOR_FROST = 0.85f, COVER_OUTDOOR_BANK = 2.2f, COVER_OUTDOOR_BANK_BLUE = 2.4f;
        private const float COVER_BANK_TINT_MAX = 1.2f, COVER_BANK_TINT_MAX_BLUE = 1.3f;
        // The last light, for layers made between two reads.
        private static Color s_CoverLight = new Color(0.3f, 0.3f, 0.3f, 1f), s_CoverBankLight = Color.white;
        private static readonly MaterialPropertyBlock s_CoverBankTint = new MaterialPropertyBlock();

        // The frost's colour and the bank's tint for this second's light.
        private static void CoverRoomLight(out Color frost, out Color bank)
        {
            long perf = PerfProbe.Begin();
            try { CoverRoomLightTimed(out frost, out bank); } finally { PerfProbe.End(PerfProbe.Section.CoverLight, perf); }
        }

        private static void CoverRoomLightTimed(out Color frost, out Color bank)
        {
            Color outdoor;
            if (!ClonedInteriorLightingGuard.TryGetExteriorLight(out outdoor)) { frost = CoverSnowLight(); bank = Color.white; return; }
            float level = (outdoor.r + outdoor.g + outdoor.b) / 3f;
            float k = Mathf.Clamp(level * COVER_OUTDOOR_FROST, 0.02f, 1.1f) / Mathf.Max(level, 0.0001f);
            frost = new Color(outdoor.r * k, outdoor.g * k, outdoor.b * k, 1f);
            bank = new Color(Mathf.Min(frost.r * COVER_OUTDOOR_BANK, COVER_BANK_TINT_MAX), Mathf.Min(frost.g * COVER_OUTDOOR_BANK, COVER_BANK_TINT_MAX),
                             Mathf.Min(frost.b * COVER_OUTDOOR_BANK_BLUE, COVER_BANK_TINT_MAX_BLUE), 1f);
        }

        // The rock-snow material has no _Color: its snow colour is what is darkened.
        private static void TintCoverBank(WindowCoverLayer layer, Color tint)
        {
            if (layer == null || layer.BankGo == null) return;
            var renderer = layer.BankGo.GetComponent<MeshRenderer>();
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null) return;
            s_CoverBankTint.Clear();
            if (material.HasProperty("_SnowColor")) s_CoverBankTint.SetColor("_SnowColor", material.GetColor("_SnowColor") * tint);
            else s_CoverBankTint.SetColor("_Color", tint);
            renderer.SetPropertyBlock(s_CoverBankTint);
        }

        // Anything that changed a pane's record asks for a look on the next frame.
        private static void MarkWindowCoverDirty() { s_CoverDirty = true; }

        // Every frame from OnUpdate: builds one window a frame after entering, then follows the cover once a second.
        private static void TickWindowCoverLayers()
        {
            long perf = PerfProbe.Begin();
            try { TickWindowCoverLayersTimed(); } finally { PerfProbe.End(PerfProbe.Section.CoverInside, perf); }
        }

        private static void TickWindowCoverLayersTimed()
        {
            string failedCheck = WindowCoverLayers ? s_WindowPortalInstanceId : null;
            if (s_CoverFailedIn != null && s_CoverFailedIn == failedCheck) return;
            s_CoverFailedIn = null;
            try
            {
                string id = WindowCoverLayers ? s_WindowPortalInstanceId : null;
                // The raised sash made for the view from outside holds the glass cut in half until it is
                // taken down; measured then, the lower pane got no frost (owner, 2026-10-18).
                if (id != null && id != s_CoverLayersInstance && HasOutsideSashes(id)) return;
                if (id != s_CoverLayersInstance || (id != null && s_CoverLayers.Count != s_WindowPortalOpenings.Count))
                {
                    TeardownWindowCoverLayers();
                    if (id == null || s_WindowPortalOpenings.Count == 0) return;
                    s_CoverLayersInstance = id;
                    // New layers start in this room's light, not the last room's.
                    CoverRoomLight(out s_CoverLight, out s_CoverBankLight);
                    for (int i = 0; i < s_WindowPortalOpenings.Count; i++) s_CoverLayers.Add(null);
                    s_CoverLayersBuilt = 0;
                }
                if (s_CoverLayersInstance == null) return;
                // Gone with their scene (a save loaded while they stood): built again next frame.
                foreach (var built in s_CoverLayers)
                    if (built != null && (built.FrostGo == null || built.FrostMat == null)) { TeardownWindowCoverLayers(); return; }
                PumpCoverFills(s_CoverLayers);

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

                // Taken as read, not eased in: the slow follow made the frost brighten over half a minute (owner, 2026-10-18).
                Color light, bankTint;
                CoverRoomLight(out light, out bankTint);
                s_CoverLight = light; s_CoverBankLight = bankTint;
                TickSnowedUpWindowShafts();
                int fills = 0;
                for (int i = 0; i < s_CoverLayers.Count; i++)
                {
                    var layer = s_CoverLayers[i];
                    if (layer != null && layer.FrostMat.color != light) layer.FrostMat.color = light;
                    TintCoverBank(layer, bankTint);
                    // At most two fills a tick; the rest catch up on the next.
                    if (fills < 2 && UpdateCoverLayer(i, false)) fills++;
                }
            }
            catch (System.Exception ex)
            {
                s_CoverFailedIn = s_WindowPortalInstanceId ?? "";
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
            // Build-up off: an uncleaned pane is the game's frost as before the layers; a cleaned one falls through.
            if (!WindowCoverBuildUp && (frost > 0f || layer == null || !layer.Shown))
            {
                bool cleared = frost <= 0f;
                SetGlassShown(opening.Glass, !cleared);
                SetRenderersEnabled(opening.Frost, !cleared && InsideOutShowSnowSlab);
                if (!cleared && !s_WindowPortalKeepsOwnGlass) GiveGlassTheFrostMaterial(opening.Glass);
                if (layer != null && layer.FrostGo != null) layer.FrostGo.SetActive(false);
                if (layer != null && layer.BankGo != null) layer.BankGo.SetActive(false);
                return false;
            }
            // Until its tables are ready the window keeps the game's own frost.
            if (layer != null && !layer.Shown) { SetGlassShown(opening.Glass, true); return false; }

            // The game's painted frost and the frame's snow slab stay off; the bank stands in for the slab.
            // The game's glass stays until our frost has been filled once, so the pane never shows bare.
            SetGlassShown(opening.Glass, layer != null && layer.FrostShown < 0f);
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
            // Always once filled: the clean glass is drawn by it too. Before, it would show what the pooled texture held.
            layer.FrostGo.SetActive(layer.FrostShown >= 0f);
            if (bank) layer.BankGo.SetActive(bankSnow > 0f);
            if (force) { layer.FrostMat.color = s_CoverLight; TintCoverBank(layer, s_CoverBankLight); }
            return filled;
        }

        private static void TeardownWindowCoverLayers()
        {
            for (int i = 0; i < s_CoverLayers.Count; i++) DestroyCoverLayer(s_CoverLayers[i]);
            s_CoverLayers.Clear();
            s_CoverLayersInstance = null;
            s_CoverLayersBuilt = 0;
        }

        // The bank's material is the game's own and is left alone.
        private static void DestroyCoverLayer(WindowCoverLayer layer)
        {
            if (layer == null) return;
            if (layer.FrostGo != null) Object.Destroy(layer.FrostGo);
            if (layer.BankGo != null) Object.Destroy(layer.BankGo);
            if (layer.BankMesh != null) Object.Destroy(layer.BankMesh);
            // The texture, material and bytes go back to the pool for the next building.
            if (layer.FrostMat != null) GiveBack(s_FrostMatPool, 0, layer.FrostMat);
            if (layer.FrostTex != null) GiveBack(s_FrostTexPool, layer.FrostSize, layer.FrostTex);
            if (layer.FrostBytes != null) GiveBack(s_FrostBytesPool, layer.FrostSize, layer.FrostBytes);
            layer.FrostMat = null; layer.FrostTex = null; layer.FrostBytes = null;
        }

        private const int COVER_POOL_KEEP = 32;
        private static readonly Dictionary<int, Stack<Texture2D>> s_FrostTexPool = new Dictionary<int, Stack<Texture2D>>();
        private static readonly Dictionary<int, Stack<Material>> s_FrostMatPool = new Dictionary<int, Stack<Material>>();
        private static readonly Dictionary<int, Stack<byte[]>> s_FrostBytesPool = new Dictionary<int, Stack<byte[]>>();

        private static T TakePooled<T>(Dictionary<int, Stack<T>> pool, int size) where T : class
        {
            Stack<T> stack;
            if (!pool.TryGetValue(size, out stack)) return null;
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                // A Unity object can still have been destroyed in between (Unity's == says so; plain null does not).
                var unity = item as Object;
                bool alive = ReferenceEquals(unity, null) ? item != null : unity != null;
                if (alive) return item;
            }
            return null;
        }

        private static void GiveBack<T>(Dictionary<int, Stack<T>> pool, int size, T item) where T : class
        {
            Stack<T> stack;
            if (!pool.TryGetValue(size, out stack)) { stack = new Stack<T>(); pool[size] = stack; }
            if (stack.Count < COVER_POOL_KEEP) { stack.Push(item); return; }
            var unity = item as Object;
            if (unity != null) Object.Destroy(unity);
        }

        // Once a session, while a region loads: the first frost a session makes took 387 ms (2026-10-14), so a
        // layer of each size is made and handed back to the pool behind the loading screen.
        private static bool s_CoverPrewarmed;

        internal static void PrewarmWindowCovers()
        {
            if (s_CoverPrewarmed || !WindowCoverLayers) return;
            s_CoverPrewarmed = true;
            try
            {
                foreach (int size in new[] { COVER_FROST_SIZE, COVER_OUTSIDE_FROST_SIZE })
                {
                    var layer = new WindowCoverLayer { FrostSize = size };
                    if (MakeCoverFrost(layer, "SeamlessInteriors_CoverPrewarm", Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, 0.1f, 0.1f, 3000))
                        DestroyCoverLayer(layer);
                }
                CoverBankMaterial(default(InteriorWindowOpening));
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[WINDOW-COVER] prewarm: {ex.Message}");
            }
        }
    }
}
