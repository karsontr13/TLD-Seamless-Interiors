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
            // in a doorway are not swept in (the existing -0.7 behaviour is preserved).
            if (instance.IsPositionInVolume(pos, -0.7f)) return StrayVerdict.Inside;

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

        // Digs or fills the terrain hole under a building (cached, reversible).
        public static void SetTerrainHoleState(SeamlessInteriorInstance instance, bool makeHole)
        {
            if (instance == null) return;
            string key = instance.Config.ResolvedInstanceId;

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

            var heightsObj = td.GetHeights(xBase, yBase, width, height);
            if (heightsObj != null)
            {
                // The IL2CPP 2D float array is written through a raw pointer: the
                // managed wrapper would allocate and copy on every element access.
                // The +32 offset skips the IL2CPP array header.
                unsafe
                {
                    float* data = (float*)((byte*)heightsObj.Pointer + 32);
                    int totalElements = width * height;

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

                td.SetHeightsDelayLOD(xBase, yBase, heightsObj);
                terrain.ApplyDelayedHeightmapModification();
                IsTerrainHoleActive[key] = makeHole;
            }
        }
    }
}
