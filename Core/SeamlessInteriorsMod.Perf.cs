using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    // ─────────────────────────────────────────────────────────────────────────
    // PERFORMANCE INFRASTRUCTURE
    //
    // The mod manages 15+ cloned interiors at once. In an earlier version each
    // instance scanned the ENTIRE scene independently while doing its own work:
    // FindObjectsOfType 15 times, the GameObject list 15 times, and raycasts sorted
    // through LINQ, allocating fresh arrays every frame.
    //
    // The helpers here reduce those repetitions to one pass:
    //   SceneScan        — shared FindObjectsOfType results (scope based)
    //   SceneObjectIndex — name -> GameObject index (for the shell lookup)
    //   PlayerRefs       — "is this transform the player's" without strings
    //   ModPaths         — computes the save folder path once
    //   JsonWriteCache   — skips rewriting JSON files whose content did not change
    //   RayScan          — LINQ-free, allocation-free raycast scanner
    // ─────────────────────────────────────────────────────────────────────────

    internal static class SceneScan
    {
        private static int s_BatchDepth;
        private static bool s_LoadPhase;

        private static Il2Cpp.GearItem[] s_Gear;
        private static Il2CppTLD.Placement.Placeable[] s_Placeables;
        private static Il2Cpp.ObjectGuid[] s_Guids;
        private static Il2Cpp.Container[] s_Containers;

        private static Renderer[] s_Renderers;

        /// <summary>Starts a mutation-free read block (e.g. the save flow).</summary>
        public static void Begin()
        {
            s_BatchDepth++;
        }

        public static void End()
        {
            if (s_BatchDepth > 0) s_BatchDepth--;
            if (s_BatchDepth == 0) InvalidateVolatile();
        }

        public static void SetLoadPhase(bool active)
        {
            if (s_LoadPhase == active) return;
            s_LoadPhase = active;

            // Drop the stale snapshot both when the window opens and when it closes.
            s_Renderers = null;

            // The name index is only needed during loading and holds tens of thousands
            // of entries; give the memory back once the window closes.
            if (!active) SceneObjectIndex.Invalidate();
        }

        public static bool InLoadPhase { get { return s_LoadPhase; } }

        public static void InvalidateVolatile()
        {
            s_Gear = null;
            s_Placeables = null;
            s_Guids = null;
            s_Containers = null;
        }

        public static void InvalidateAll()
        {
            InvalidateVolatile();
            s_Renderers = null;
            s_BatchDepth = 0;
            s_LoadPhase = false;
            SceneObjectIndex.Invalidate();
        }

        // The four scans below share a single shape: reuse the cached array inside a
        // batch scope, otherwise scan fresh.
        public static Il2Cpp.GearItem[] GearAll()
        {
            if (s_BatchDepth > 0 && s_Gear != null) return s_Gear;
            var r = UnityEngine.Object.FindObjectsOfType<Il2Cpp.GearItem>(true);
            if (s_BatchDepth > 0) s_Gear = r;
            return r;
        }

        public static Il2CppTLD.Placement.Placeable[] PlaceablesAll()
        {
            if (s_BatchDepth > 0 && s_Placeables != null) return s_Placeables;
            var r = UnityEngine.Object.FindObjectsOfType<Il2CppTLD.Placement.Placeable>(true);
            if (s_BatchDepth > 0) s_Placeables = r;
            return r;
        }

        public static Il2Cpp.ObjectGuid[] GuidsAll()
        {
            if (s_BatchDepth > 0 && s_Guids != null) return s_Guids;
            var r = UnityEngine.Object.FindObjectsOfType<Il2Cpp.ObjectGuid>(true);
            if (s_BatchDepth > 0) s_Guids = r;
            return r;
        }

        public static Il2Cpp.Container[] ContainersAll()
        {
            if (s_BatchDepth > 0 && s_Containers != null) return s_Containers;
            var r = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Container>(true);
            if (s_BatchDepth > 0) s_Containers = r;
            return r;
        }

        /// <summary>Active Renderers (also shared during the load window).</summary>
        public static Renderer[] RenderersActive()
        {
            bool shared = s_LoadPhase || s_BatchDepth > 0;
            if (shared && s_Renderers != null) return s_Renderers;

            var r = UnityEngine.Object.FindObjectsOfType<Renderer>();
            if (shared) s_Renderers = r;
            return r;
        }
    }

    internal static class SceneObjectIndex
    {
        private static Dictionary<string, List<GameObject>> s_ByName;

        public static void Invalidate()
        {
            s_ByName = null;
        }

        private static Dictionary<string, List<GameObject>> Index()
        {
            if (s_ByName != null) return s_ByName;

            var map = new Dictionary<string, List<GameObject>>(4096);
            var all = UnityEngine.Object.FindObjectsOfType<GameObject>();

            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null) continue;

                string n = go.name;
                List<GameObject> list;
                if (!map.TryGetValue(n, out list))
                {
                    list = new List<GameObject>(1);
                    map[n] = list;
                }
                list.Add(go);
            }

            s_ByName = map;
            return map;
        }

        public static void CollectByPrefix(string prefix, List<GameObject> results)
        {
            if (string.IsNullOrEmpty(prefix) || results == null) return;

            var map = Index();

            // Exact match first: the common case, a single dictionary lookup.
            List<GameObject> exact;
            if (map.TryGetValue(prefix, out exact))
            {
                for (int i = 0; i < exact.Count; i++)
                    if (exact[i] != null) results.Add(exact[i]);
            }

            // Then a prefix sweep for suffixes like "(Clone)" - over the UNIQUE names
            // only, not over every object.
            foreach (var kvp in map)
            {
                if (kvp.Key.Length <= prefix.Length) continue;   // exact match handled above
                if (!kvp.Key.StartsWith(prefix)) continue;

                var list = kvp.Value;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null) results.Add(list[i]);
            }
        }

        public static void CollectByContainsBoth(string a, string b, List<GameObject> results)
        {
            if (string.IsNullOrEmpty(a) || results == null) return;

            var map = Index();
            foreach (var kvp in map)
            {
                if (kvp.Key.IndexOf(a, System.StringComparison.Ordinal) < 0) continue;
                if (!string.IsNullOrEmpty(b) && kvp.Key.IndexOf(b, System.StringComparison.Ordinal) < 0) continue;

                var list = kvp.Value;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null) results.Add(list[i]);
            }
        }
    }

    internal static class PlayerRefs
    {
        public const string PLAYER_ROOT_NAME = "CHARACTER_FPSPlayer";

        private static int s_PlayerRootId;
        private static float s_ResolvedAt = -999f;

        // Resolve interval. THE THROTTLE APPLIES IN BOTH CASES: without it, while there
        // is no player yet (id == 0) this method would make an interop call to
        // GameManager on every invocation - and it is called from loops over thousands
        // of objects. During that window the name test is used; the result is still correct.
        private const float RESOLVE_INTERVAL = 1f;

        public static void Reset()
        {
            s_PlayerRootId = 0;
            s_ResolvedAt = -999f;
        }

        private static void Resolve()
        {
            if (Time.time - s_ResolvedAt < RESOLVE_INTERVAL) return;
            s_ResolvedAt = Time.time;

            Transform playerT = Il2Cpp.GameManager.GetPlayerTransform();
            if (playerT == null) { s_PlayerRootId = 0; return; }

            Transform root = playerT.root;
            s_PlayerRootId = (root != null) ? root.GetInstanceID() : 0;
        }

        public static bool IsPlayerRoot(Transform root)
        {
            if (root == null) return false;

            Resolve();
            if (s_PlayerRootId != 0)
                return root.GetInstanceID() == s_PlayerRootId;

            // Cache could not be built (no player yet) - fall back to the name test.
            return root.name.IndexOf(PLAYER_ROOT_NAME, System.StringComparison.Ordinal) >= 0;
        }

        public static bool IsUnderPlayer(Transform t)
        {
            if (t == null) return false;
            return IsPlayerRoot(t.root);
        }
    }

    internal static class ModPaths
    {
        private static string s_DataDir;

        public static string DataDir()
        {
            if (s_DataDir != null) return s_DataDir;

            string baseDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            string dir = System.IO.Path.Combine(baseDir, "SeamlessInteriorsData");

            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            s_DataDir = dir;
            return dir;
        }
    }

    internal static class JsonWriteCache
    {
        private static readonly Dictionary<string, int> s_Hashes = new Dictionary<string, int>();

        public static void Reset()
        {
            s_Hashes.Clear();
        }

        public static void Forget(string path)
        {
            if (!string.IsNullOrEmpty(path)) s_Hashes.Remove(path);
        }

        public static bool Write(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (content == null) content = string.Empty;

            int hash = ComputeHash(content);

            // File.Exists is also checked, so an externally deleted file is rewritten.
            int previous;
            if (s_Hashes.TryGetValue(path, out previous) && previous == hash
                && System.IO.File.Exists(path))
            {
                return false;
            }

            System.IO.File.WriteAllText(path, content);
            s_Hashes[path] = hash;
            return true;
        }

        // Length + FNV-1a. The collision chance is practically nil, and the file
        // existence check above adds another guard.
        private static int ComputeHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= 16777619u;
                }
                hash ^= (uint)s.Length;
                return (int)hash;
            }
        }
    }

    internal static class RayScan
    {
        private const int INITIAL_CAPACITY = 64;
        private const int MAX_CAPACITY = 1024;

        private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<RaycastHit> s_Buffer;
        private static bool[] s_Consumed = new bool[INITIAL_CAPACITY];
        private static int s_Count;

        private static void EnsureCapacity(int capacity)
        {
            if (s_Buffer != null && s_Buffer.Length >= capacity) return;

            s_Buffer = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<RaycastHit>(capacity);
            if (s_Consumed.Length < capacity) s_Consumed = new bool[capacity];
        }

        public static int Cast(Vector3 origin, Vector3 direction, float maxDistance,
                               int layerMask, QueryTriggerInteraction triggerInteraction)
        {
            EnsureCapacity(INITIAL_CAPACITY);

            while (true)
            {
                int count = Physics.RaycastNonAlloc(origin, direction, s_Buffer, maxDistance, layerMask, triggerInteraction);

                if (count < s_Buffer.Length || s_Buffer.Length >= MAX_CAPACITY)
                {
                    if (count > s_Buffer.Length) count = s_Buffer.Length;
                    s_Count = count;

                    for (int i = 0; i < count; i++) s_Consumed[i] = false;
                    return count;
                }

                EnsureCapacity(s_Buffer.Length * 2);
            }
        }

        public static RaycastHit Get(int index)
        {
            return s_Buffer[index];
        }

        public static int NextClosest()
        {
            int best = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < s_Count; i++)
            {
                if (s_Consumed[i]) continue;

                float d = s_Buffer[i].distance;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            if (best >= 0) s_Consumed[best] = true;
            return best;
        }
    }
}
