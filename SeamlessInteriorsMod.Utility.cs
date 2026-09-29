using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────
        // CLONE VOLUME PRE-FILTERING (shared helpers)
        //
        // Every scene-wide flow (saving, visibility, adoption) asks the same two
        // questions: "does this object fall inside this clone's volume" and "does
        // answering that need an expensive ray test". The same code had been copied
        // by hand into four places; it lives here now.
        // ─────────────────────────────────────────────────────────────────

        public enum StrayVerdict
        {
            Outside = 0,
            Inside = 1,
            NeedsRaycast = 2,
        }

        public static SeamlessInteriorInstance FindInstanceOwning(Transform t)
        {
            if (t == null) return null;

            Transform root = t.root;
            if (root == null) return null;

            int rootId = root.gameObject.GetInstanceID();

            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null) continue;

                GameObject master = instance.MasterInterior;
                if (master == null) continue;

                if (master.GetInstanceID() == rootId) return instance;
            }

            return null;
        }

        public static bool IsUnderAnyMasterInteriorPublic(Transform t)
        {
            if (t == null) return false;

            foreach (var inst in ActiveInteriors.Values)
            {
                if (inst == null || inst.MasterInterior == null) continue;
                if (t.IsChildOf(inst.MasterInterior.transform)) return true;
            }
            return false;
        }

        // Which interior this scene belongs to: "CampOffice_SANDBOX" -> "CampOffice".
        // Null when the scene is not one of the mod's interiors at all.
        //
        // The mod pulls each interior in as three additive scenes - the base name plus
        // the "_SANDBOX" and "_DLC01" variants - and empties them into the clone.
        //
        // THERE IS DELIBERATELY NO "IsInteriorSceneName" ANY MORE. A bare yes/no on the
        // name reads as "this belongs to the mod", and it does not: the original room
        // the player walks into through a loading screen carries the very same name.
        // One caller made that assumption and it cost the player every piece of
        // furniture they moved indoors. Ask IsInteriorLoadedAsCloneTemplate instead.
        public static string GetInteriorBaseName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;

            foreach (var cfg in SupportedInteriors)
            {
                string baseName = cfg.InteriorSceneBaseName;
                if (string.IsNullOrEmpty(baseName)) continue;

                if (sceneName.Length < baseName.Length) continue;
                if (!sceneName.StartsWith(baseName, System.StringComparison.Ordinal)) continue;

                // Exact match, or one of the variant suffixes - never a different scene
                // that merely starts with the same letters.
                if (sceneName.Length == baseName.Length) return baseName;
                if (sceneName[baseName.Length] == '_') return baseName;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        // TEMPLATE, OR THE ROOM THE PLAYER IS STANDING IN?
        //
        // An interior scene reaches the game in two completely different ways and the
        // mod has to treat them as opposites:
        //
        //   TEMPLATE - the mod loads it additively, next to the region, purely to copy
        //              its contents into a clone. Nothing in it belongs to the world and
        //              nothing in it may reach the game's own save.
        //
        //   THE REAL ROOM - the game itself takes the player inside through a loading
        //              screen, exactly as it would with no mod installed. Everything in
        //              it is the player's real surroundings and the game's save is the
        //              only thing recording it.
        //
        // Telling them apart by scene NAME alone is impossible, and treating the second
        // as the first is not a cosmetic mistake: it silently threw away every piece of
        // furniture the player moved while standing in an original interior.
        //
        // Two things separate them, and either one is enough to prove "real room":
        //
        //   1. The mod only ever pulls a template in while the REGION is loaded around
        //      it. Walking into an original interior unloads the region first, so with
        //      no supported exterior loaded there is no cloning going on at all.
        //
        //   2. During the changeover both can briefly be loaded at once - and in that
        //      moment the game is restoring the interior's own save. The ACTIVE scene is
        //      what settles it: the game makes the room the player is entering active,
        //      while a template is only ever an additive passenger.
        public static bool IsInteriorLoadedAsCloneTemplate(string sceneName)
        {
            string interiorBase = GetInteriorBaseName(sceneName);
            if (interiorBase == null) return false;

            if (!IsSupportedExteriorSceneLoaded()) return false;

            string activeBase = GetInteriorBaseName(
                RealSceneName(UnityEngine.SceneManagement.SceneManager.GetActiveScene()));

            return activeBase != interiorBase;
        }

        // Is a region the mod clones into currently loaded?
        //
        // Cached for the frame: every placeable in a template scene asks this as it
        // wakes up, and that is hundreds of calls inside one loading frame.
        private static int s_ExteriorLoadedFrame = -1;
        private static bool s_ExteriorLoadedAnswer;

        public static bool IsSupportedExteriorSceneLoaded()
        {
            if (s_ExteriorLoadedFrame == Time.frameCount) return s_ExteriorLoadedAnswer;
            s_ExteriorLoadedFrame = Time.frameCount;
            s_ExteriorLoadedAnswer = false;

            try
            {
                int count = RealSceneCount();
                for (int i = 0; i < count; i++)
                {
                    string name = RealSceneName(RealSceneAt(i));
                    if (string.IsNullOrEmpty(name)) continue;

                    foreach (var cfg in SupportedInteriors)
                    {
                        if (name == cfg.ExteriorSceneName) { s_ExteriorLoadedAnswer = true; return true; }
                    }
                }
            }
            catch { }

            return s_ExteriorLoadedAnswer;
        }

        public static bool TryGetWorldFilterBounds(SeamlessInteriorInstance instance, out Bounds bounds, float margin = 1.35f)
        {
            bounds = default(Bounds);
            if (instance == null || instance.InteriorTrigger == null) return false;

            Transform t = instance.InteriorTrigger.transform;
            Vector3 wCenter = t.TransformPoint(instance.InteriorTrigger.center);

            // Apply the transform's scale by hand: the collider size is local.
            Vector3 lossyScale = t.lossyScale;
            Vector3 size = instance.InteriorTrigger.size;
            Vector3 wSize = new Vector3(
                size.x * Mathf.Abs(lossyScale.x),
                size.y * Mathf.Abs(lossyScale.y),
                size.z * Mathf.Abs(lossyScale.z)) * margin;

            bounds = new Bounds(wCenter, wSize);
            return true;
        }

        public static StrayVerdict ClassifyStray(SeamlessInteriorInstance instance, bool hasFilter, Bounds filter, Vector3 pos)
        {
            if (hasFilter && !filter.Contains(pos)) return StrayVerdict.Outside;

            // Purely geometric volume test - needs no colliders, works while the clone
            // is switched off. The volume is shrunk slightly so outdoor items standing
            // in a doorway are not swept in.
            if (!instance.IsPositionInVolume(pos, -0.7f)) return StrayVerdict.Outside;

            // PASSING THE VOLUME TEST NO LONGER MEANS "INSIDE".
            //
            // The volume is an axis-aligned box around a building that is not axis
            // aligned, so it reaches past the walls and swallows whatever the player
            // left by the door. Claiming those into the building's save file made them
            // disappear from the world on the next load.
            //
            // Everything the box accepts is now verified with a ray instead. The callers
            // already batch that: they collect the pending candidates, open the clone
            // ONCE, and test them together - so this costs no extra scene toggling.
            return StrayVerdict.NeedsRaycast;
        }

        public static Vector3 EnsureAboveGround(Vector3 pos, SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null || !instance.MasterInterior.activeSelf)
                return pos;

            // Start well above the position and cast downwards.
            Vector3 rayOrigin = new Vector3(pos.x, pos.y + 5.0f, pos.z);
            float maxDist = 10.0f;

            int hitCount = RayScan.Cast(rayOrigin, Vector3.down, maxDist,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            Transform masterT = instance.MasterInterior.transform;
            float bestFloorY = float.MinValue;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = RayScan.Get(i);
                Collider col = hit.collider;
                if (col == null || col.isTrigger) continue;
                if (PlayerRefs.IsPlayerRoot(col.transform.root)) continue;

                // Only the clone scene's own floor counts.
                if (col.transform.IsChildOf(masterT))
                {
                    // Highest surface that is not unreasonably far above the position.
                    if (hit.point.y > bestFloorY && hit.point.y <= pos.y + 3.0f)
                    {
                        bestFloorY = hit.point.y;
                    }
                }
            }

            if (bestFloorY > float.MinValue)
            {
                // Floor found - place the player slightly above it.
                float safeY = bestFloorY + 0.15f;
                if (pos.y < safeY)
                    return new Vector3(pos.x, safeY, pos.z);
            }

            return pos;
        }

        // Name of the root the game parks every placed object under ("DesignPlaceables").
        // Read once: under IL2CPP each read of a static string property allocates.
        private static string s_PlacementRootName;

        private static string PlacementRootName()
        {
            if (s_PlacementRootName != null) return s_PlacementRootName;

            string n = null;
            try { n = Il2CppTLD.Placement.PlaceableManager.CATEGORY_NAME; }
            catch { }

            s_PlacementRootName = string.IsNullOrEmpty(n) ? "DesignPlaceables" : n;
            return s_PlacementRootName;
        }

        // Is this object parked under the game's own placement root?
        //
        // Everything the player sets down in the world lives there, and the object's own
        // name says nothing about it - only its parent does. Anything under that root is
        // the player's property and the mod must never destroy it.
        public static bool IsUnderPlacementRoot(Transform t)
        {
            if (t == null) return false;

            string rootName = PlacementRootName();

            Transform cur = t;
            while (cur != null)
            {
                if (cur.name.IndexOf(rootName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                cur = cur.parent;
            }
            return false;
        }

        public static bool IsPlayerOrInventory(Transform t)
        {
            if (t == null) return false;

            Transform root = t.root;
            if (root != null)
            {
                // Player root: by cached InstanceID, without reading strings.
                // (This method is called for thousands of objects during save/restore,
                //  and under IL2CPP reading .name allocates a new managed string each time.)
                if (PlayerRefs.IsPlayerRoot(root)) return true;

                if (root.name.IndexOf("WorldView", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            string name = t.name;
            if (name.IndexOf("WorldView", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("DesignPlaceable", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Objects moved to DontDestroyOnLoad belong to the player/UI, not the scene.
            if (t.gameObject.scene.name == "DontDestroyOnLoad")
                return true;

            var p = t.GetComponent<Il2CppTLD.Placement.Placeable>();
            if (p != null)
            {
                try
                {
                    var state = Il2CppTLD.Placement.PlaceableManager.GetPlacementState(p);
                    if (state == Il2CppTLD.Placement.PlacementState.OnPlayer)
                        return true;
                }
                catch { }
            }

            return false;
        }

        // Backup of the terrain heights a hole overwrote, so it can be filled back in.
        public class TerrainHoleData
        {
            public int xBase;
            public int yBase;
            public int width;
            public int height;
            public float[] originalHeights;
        }

        public static System.Collections.Generic.Dictionary<string, TerrainHoleData> TerrainHoleCache = new System.Collections.Generic.Dictionary<string, TerrainHoleData>();
        public static System.Collections.Generic.Dictionary<string, bool> IsTerrainHoleActive = new System.Collections.Generic.Dictionary<string, bool>();

        // Is a terrain hole WANTED for this instance (i.e. explicitly configured)?
        // Computed once per building and cached here, to avoid running
        // transform.Find("TerrainHoleBox") every frame.
        public static System.Collections.Generic.Dictionary<string, bool> TerrainHoleEnabled = new System.Collections.Generic.Dictionary<string, bool>();

        public static bool IsTerrainHoleWanted(SeamlessInteriorInstance instance)
        {
            if (instance == null) return false;

            string key = instance.Config.ResolvedInstanceId;
            bool wanted;
            if (TerrainHoleEnabled.TryGetValue(key, out wanted))
                return wanted;

            // Option 1: explicit size in the config.
            if (instance.Config.TerrainHoleSize != Vector3.zero)
            {
                TerrainHoleEnabled[key] = true;
                return true;
            }

            // Option 2: a dedicated "TerrainHoleBox" object inside the prefab.
            // If MasterInterior is not ready yet we do NOT decide and do not cache,
            // so the question can be revisited once the scene is built.
            if (instance.MasterInterior == null) return false;

            bool hasBox = instance.MasterInterior.transform.Find("TerrainHoleBox") != null;
            TerrainHoleEnabled[key] = hasBox;
            return hasBox;
        }

        public static bool IsTerrainHoleOpen(SeamlessInteriorInstance instance)
        {
            if (instance == null) return false;
            bool open;
            return IsTerrainHoleActive.TryGetValue(instance.Config.ResolvedInstanceId, out open) && open;
        }

        private static readonly System.Collections.Generic.Dictionary<string, float> s_TerrainHoleNextProbe =
            new System.Collections.Generic.Dictionary<string, float>();
        private static readonly System.Collections.Generic.Dictionary<string, bool> s_TerrainHoleLastProbe =
            new System.Collections.Generic.Dictionary<string, bool>();

        // Door state first; otherwise the ray-based inside test, reused for a quarter second.
        public static bool IsPlayerInsideForTerrainHole(SeamlessInteriorInstance instance, Vector3 pos)
        {
            string key = instance.Config.ResolvedInstanceId;
            if (PlayerInteriorId == key) return true;

            float now = Time.time;
            float next;
            bool last;
            if (s_TerrainHoleNextProbe.TryGetValue(key, out next) && now < next
                && s_TerrainHoleLastProbe.TryGetValue(key, out last))
                return last;

            last = instance.IsPositionInside(pos);
            s_TerrainHoleNextProbe[key] = now + 0.25f;
            s_TerrainHoleLastProbe[key] = last;
            return last;
        }

        // Buildings whose terrain hole could not be read or written this session. The state is
        // only recorded after a successful write, so a failure used to repeat on every frame the
        // player stood inside: a log line per frame, and the rest of OnUpdate aborted with it.
        private static readonly System.Collections.Generic.HashSet<string> s_TerrainHoleFailed =
            new System.Collections.Generic.HashSet<string>();

        // Digs or fills the terrain hole under a building (cached, reversible).
        public static void SetTerrainHoleState(SeamlessInteriorInstance instance, bool makeHole)
        {
            if (instance == null) return;
            string key = instance.Config.ResolvedInstanceId;
            if (s_TerrainHoleFailed.Contains(key)) return;

            try
            {
                SetTerrainHoleStateCore(instance, key, makeHole);
            }
            catch (System.Exception ex)
            {
                DisableTerrainHole(key, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void DisableTerrainHole(string key, string reason)
        {
            s_TerrainHoleFailed.Add(key);
            MelonLoader.MelonLogger.Warning($"[ARAZI-DELIGI] {key}: arazi yuksekligi okunamadi/yazilamadi ({reason}). " +
                                            "Bu bina icin arazi deligi bu oturumda kapatildi.");
        }

        private static void SetTerrainHoleStateCore(SeamlessInteriorInstance instance, string key, bool makeHole)
        {
            bool currentState;
            if (IsTerrainHoleActive.TryGetValue(key, out currentState) && currentState == makeHole)
                return;

            // Never dug means there is nothing to fill back in.
            // IMPORTANT: this check must come BEFORE Terrain.activeTerrain - otherwise
            // every never-visited building paid for a pointless IL2CPP property read
            // every frame (15 buildings x 60 fps = 900 calls per second).
            TerrainHoleData cache;
            bool hasCache = TerrainHoleCache.TryGetValue(key, out cache);
            if (!hasCache && !makeHole) return;

            // OPT-IN GATE: never dig for buildings with no hole configured.
            // (The fill path stays outside this gate so an already dug hole can always
            //  be filled back in.)
            if (makeHole && !IsTerrainHoleWanted(instance)) return;

            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return;
            TerrainData td = terrain.terrainData;

            int xBase = 0, yBase = 0, width = 0, height = 0;

            // If a backup exists (the hole was dug before) reuse exactly the same
            // heightmap rect, even if the source object is now inactive.
            if (hasCache)
            {
                xBase = cache.xBase;
                yBase = cache.yBase;
                width = cache.width;
                height = cache.height;
            }
            else
            {
                Vector3 terrainPos = terrain.transform.position;
                Bounds targetBounds;

                // For an exact hole shape: look for a hidden "TerrainHoleBox" object
                // inside the prefab.
                Transform customHoleT = null;
                if (instance.MasterInterior != null)
                     customHoleT = instance.MasterInterior.transform.Find("TerrainHoleBox");

                if (instance.Config.TerrainHoleSize != Vector3.zero)
                {
                    // Option 1: the config gives world-space values directly.
                    // TerrainHoleOffset is used as the world-space centre here.
                    targetBounds = new Bounds(instance.Config.TerrainHoleOffset, instance.Config.TerrainHoleSize);
                }
                else if (customHoleT != null)
                {
                    BoxCollider box = customHoleT.GetComponent<BoxCollider>();
                    if (box != null) targetBounds = box.bounds;
                    else targetBounds = new Bounds(customHoleT.position, customHoleT.localScale);
                }
                else return; // unreachable: IsTerrainHoleWanted already filtered this out

                // World bounds -> normalized terrain coords -> heightmap indices.
                float normalizedXMin = (targetBounds.min.x - terrainPos.x) / td.size.x;
                float normalizedZMin = (targetBounds.min.z - terrainPos.z) / td.size.z;
                float normalizedXMax = (targetBounds.max.x - terrainPos.x) / td.size.x;
                float normalizedZMax = (targetBounds.max.z - terrainPos.z) / td.size.z;

                xBase = Mathf.RoundToInt(normalizedXMin * (td.heightmapResolution - 1));
                yBase = Mathf.RoundToInt(normalizedZMin * (td.heightmapResolution - 1));

                width = Mathf.RoundToInt((normalizedXMax - normalizedXMin) * (td.heightmapResolution - 1));
                height = Mathf.RoundToInt((normalizedZMax - normalizedZMin) * (td.heightmapResolution - 1));

                // Keep the rect inside the heightmap.
                xBase = Mathf.Clamp(xBase, 0, td.heightmapResolution - 1);
                yBase = Mathf.Clamp(yBase, 0, td.heightmapResolution - 1);
                width = Mathf.Clamp(width, 1, td.heightmapResolution - xBase);
                height = Mathf.Clamp(height, 1, td.heightmapResolution - yBase);
            }

            int totalElements = width * height;
            System.IntPtr heights = GetHeightsNative(td, xBase, yBase, width, height);
            if (heights == System.IntPtr.Zero || Il2CppInterop.Runtime.IL2CPP.il2cpp_array_length(heights) < (uint)totalElements)
                throw new System.InvalidOperationException("TerrainData.GetHeights beklenen boyutta dizi dondurmedi");

            // The IL2CPP 2D float array is written through a raw pointer: the
            // managed wrapper would allocate and copy on every element access.
            // The +32 offset skips the IL2CPP array header.
            unsafe
            {
                float* data = (float*)((byte*)heights + 32);

                if (makeHole)
                {
                    // Back up the original heights once, so the hole can be undone.
                    if (!hasCache)
                    {
                        float[] backup = new float[totalElements];
                        for (int i = 0; i < totalElements; i++)
                            backup[i] = data[i];

                        var newCache = new TerrainHoleData
                        {
                            xBase = xBase, yBase = yBase, width = width, height = height, originalHeights = backup
                        };
                        TerrainHoleCache[key] = newCache;
                    }

                    // Sink by 10m rather than a very deep -30f.
                    float targetNormalizedY = Mathf.Clamp01(((terrain.transform.position.y - 10f) - terrain.transform.position.y) / td.size.y);

                    // Only lower - never raise ground that is already below the target.
                    for (int i = 0; i < totalElements; i++)
                    {
                        if (data[i] > targetNormalizedY) data[i] = targetNormalizedY;
                    }
                }
                else
                {
                    if (hasCache)
                    {
                        float[] backup = cache.originalHeights;
                        for (int i = 0; i < totalElements; i++)
                        {
                            data[i] = backup[i];
                        }
                    }
                }
            }

            SetHeightsDelayLODNative(td, xBase, yBase, heights);
            terrain.ApplyDelayedHeightmapModification();
            IsTerrainHoleActive[key] = makeHole;
        }

        // TerrainData.GetHeights / SetHeightsDelayLOD, invoked on the native methods. The heightmap
        // travels as a float[,], and not every Il2CppInterop version can wrap a two-dimensional
        // IL2CPP array: the managed GetHeights threw a NullReferenceException inside
        // Il2CppObjectPool after the native call had already returned the array. Here the array is
        // only ever handled as a pointer.
        private static System.IntPtr s_TerrainGetHeights = System.IntPtr.Zero;
        private static System.IntPtr s_TerrainSetHeightsDelayLOD = System.IntPtr.Zero;

        private static System.IntPtr ResolveTerrainDataMethod(ref System.IntPtr cached, string name, int argCount)
        {
            if (cached == System.IntPtr.Zero)
            {
                cached = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    Il2CppInterop.Runtime.Il2CppClassPointerStore<TerrainData>.NativeClassPtr, name, argCount);
                if (cached == System.IntPtr.Zero)
                    throw new System.InvalidOperationException("TerrainData." + name + " bulunamadi");
            }
            return cached;
        }

        private static unsafe System.IntPtr GetHeightsNative(TerrainData td, int xBase, int yBase, int width, int height)
        {
            System.IntPtr method = ResolveTerrainDataMethod(ref s_TerrainGetHeights, "GetHeights", 4);
            System.IntPtr* args = stackalloc System.IntPtr[4];
            args[0] = (System.IntPtr)(&xBase);
            args[1] = (System.IntPtr)(&yBase);
            args[2] = (System.IntPtr)(&width);
            args[3] = (System.IntPtr)(&height);

            System.IntPtr exc = System.IntPtr.Zero;
            System.IntPtr result = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, td.Pointer, (void**)args, ref exc);
            Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc);
            return result;
        }

        private static unsafe void SetHeightsDelayLODNative(TerrainData td, int xBase, int yBase, System.IntPtr heights)
        {
            System.IntPtr method = ResolveTerrainDataMethod(ref s_TerrainSetHeightsDelayLOD, "SetHeightsDelayLOD", 3);
            System.IntPtr* args = stackalloc System.IntPtr[3];
            args[0] = (System.IntPtr)(&xBase);
            args[1] = (System.IntPtr)(&yBase);
            args[2] = heights;

            System.IntPtr exc = System.IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, td.Pointer, (void**)args, ref exc);
            Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc);
        }
    }
}
