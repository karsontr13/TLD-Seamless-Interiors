using Il2CppTLD.WeatherParticle;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    // Link definition for nested scene transitions (e.g. FarmHouse -> Basement):
    // doors that lead from one interior scene into another.
    public class SubInteriorLink
    {
        // InstanceId of the target instance (the key in the ActiveInteriors dictionary).
        public string TargetInstanceId;

        // World-space position of the door inside the parent scene (used for matching).
        public Vector3 ParentDoorPosition;

        // Where the player spawns when entering the child (in the child clone's space).
        public Vector3 ChildSpawnPosition;

        // Where the player spawns when coming back (in the parent clone's space).
        public Vector3 ParentSpawnPosition;
    }

    // Per-door spawn coordinates, for buildings with more than one entrance.
    public class DoorSpawnPoint
    {
        public string DoorName;        // Name of the door object (or a substring of it)
        public Vector3 DoorTransformPosition;
        public Vector3 EntryPosition;  // interior coordinate to spawn at when entering here
        public Vector3 ExitPosition;   // exterior coordinate to spawn at when leaving here
    }

    // Static configuration of one interior.
    public class InteriorConfig
    {
        // UNIQUE ID: several buildings can share the same scene/shell, so every
        // config needs its own id.
        // Left empty, InteriorSceneBaseName is used instead (backwards compatibility).
        public string InstanceId;

        // Returns InstanceId when set, otherwise InteriorSceneBaseName.
        public string ResolvedInstanceId => string.IsNullOrEmpty(InstanceId) ? InteriorSceneBaseName : InstanceId;

        // Names or prefab names of outdoor objects that stick into the interior and
        // therefore have to be hidden.
        public List<string> ExternalObjectsToHide = new List<string>();
        public string ExteriorSceneName;
        public string InteriorSceneBaseName;
        public string ExteriorShellPrefabName;
        public float YOffset;
        public Vector3 ScaleAdjustment;
        public Vector3 FallbackPosition;

        // Objects inside the cloned scene to remove or switch off after cloning.
        public List<string> ObjectsToDestroy = new List<string>();
        public List<string> ObjectsToDisable = new List<string>();
        public Vector3 EntrySpawnPosition;  // fallback for single-door buildings
        public Vector3 ExitSpawnPosition;   // fallback for single-door buildings

        // Per-door spawn points, for buildings with more than one entrance.
        public List<DoorSpawnPoint> DoorSpawnPoints = new List<DoorSpawnPoint>();

        // Rotation applied to the clone so it lines up with the exterior shell.
        public Vector3 RotationOffset;

        // Pin the clone to FallbackPosition instead of the shell's own position.
        public bool ForceExactPosition;

        // Terrain hole carved under the building (zero = no hole).
        public Vector3 TerrainHoleSize = Vector3.zero;
        public Vector3 TerrainHoleOffset = Vector3.zero;

        // Nested scene transitions (e.g. the basement door inside FarmHouse).
        public List<SubInteriorLink> SubInteriorLinks = new List<SubInteriorLink>();

        // SaveKeyPrefix is InstanceId based, so buildings sharing a scene never clash.
        public string SaveKeyPrefix => $"{ResolvedInstanceId}Gen_";

        // ─── Caches for name lookups ───
        //
        // PrepareMasterInterior searches these two lists for EVERY transform in the
        // cloned scene. List.Contains means a linear scan with string comparisons:
        // 5,000 objects x 20 entries x 2 lists = 200,000 string comparisons per
        // building, times 15 buildings. A HashSet makes it constant time.
        //
        // The lists never change after the config is built, so the sets are created
        // lazily on first use.
        private HashSet<string> _destroySet;
        private HashSet<string> _disableSet;

        public HashSet<string> DestroySet
        {
            get
            {
                if (_destroySet == null)
                    _destroySet = (ObjectsToDestroy != null)
                        ? new HashSet<string>(ObjectsToDestroy)
                        : new HashSet<string>();
                return _destroySet;
            }
        }

        public HashSet<string> DisableSet
        {
            get
            {
                if (_disableSet == null)
                    _disableSet = (ObjectsToDisable != null)
                        ? new HashSet<string>(ObjectsToDisable)
                        : new HashSet<string>();
                return _disableSet;
            }
        }
    }

    // Runtime state of one building in the running game.
    public class SeamlessInteriorInstance
    {
        // Outdoor objects that were found in the world and matched to this instance.
        public List<GameObject> ResolvedExternalHiddenObjects = new List<GameObject>();
        public InteriorConfig Config { get; private set; }

        public bool RunCompleted = false;
        public bool IsCloningRoutineActive = false;
        public bool InteriorPersisted = false;

        // This building's loot has NOT been generated for this save yet; the
        // RandomSpawnObject selection (DisableAll + ActivateRandomObject) is still due.
        //
        // WHY THIS EXISTS: the "loot generated" flag (PlayerPrefs SaveKeyPrefix) used to
        // be written at the same time as the GUID assignment - i.e. BEFORE the clone
        // scene was activated. Once MasterInterior went active, RandomSpawnObject.Start()
        // ran, RandomSpawnBlockerPatch saw the flag as 1 and DESTROYED the spawner.
        // RandomSpawnObject is not a "spawner" but a "filter": the scene file contains
        // ALL candidate items and it leaves only a few of m_ObjectList enabled according
        // to the difficulty mode. With the filter never running, EVERY candidate in the
        // scene stayed enabled -> far too much loot, and two different items in the same
        // spot (e.g. an axe plus a knife). See PerformInitialLootRoll.
        public bool PendingInitialLootRoll = false;

        public GameObject ExteriorShell = null;
        public GameObject MasterInterior = null;
        public BoxCollider InteriorTrigger = null;

        public List<WeatherParticleManager.ParticleKillerInstance> CustomKillers = new List<WeatherParticleManager.ParticleKillerInstance>();
        public WeatherParticleManager.ParticleKillerInstance ParticleKiller = null;

        public bool WatchdogStarted = false;

        // Has the safehouse "clear junk" (R) action been used on this building?
        // The game's own flag is per scene and cannot cover clone scenes;
        // see SeamlessInteriorsMod.Junk.cs
        public bool JunkCleared = false;

        // Objects the MOD disabled while clearing junk.
        // Only these may be re-enabled: touching objects the scene template itself
        // left disabled would break the look of the interior.
        public List<GameObject> JunkClearedObjects = new List<GameObject>();

        // Cache for "does this building still have clearable junk".
        //
        // WHY: the game's HUD asks CanClearJunk every frame. Answering requires
        // scanning EVERY JunkTag in the clone scene and walking the parent chain of
        // each. The answer only changes when junk is cleared or restored from a save,
        // and both of those invalidate the cache.
        // (see SeamlessInteriorsMod.Junk.cs -> InvalidateJunkPromptCache)
        public int JunkScanStamp = -1;
        public float JunkScanTime = -999f;
        public bool JunkScanResult;

        // When the ParticleKiller trigger is built, the real interior bounds are
        // expanded by (1, 3, 1) (SetupWeatherAndParticles / ReattachPersistedInterior).
        // Shrinking by the same amount recovers the true volume.
        private static readonly Vector3 TRIGGER_EXPANSION = new Vector3(1.0f, 3.0f, 1.0f);

        public SeamlessInteriorInstance(InteriorConfig config)
        {
            Config = config;
        }

        public bool IsPositionInVolume(Vector3 worldPos, float padding = 0f)
        {
            if (MasterInterior == null || InteriorTrigger == null) return false;

            Vector3 localPos = InteriorTrigger.transform.InverseTransformPoint(worldPos);

            // Undo the padding the trigger was built with to get the true volume.
            Vector3 size = InteriorTrigger.size - TRIGGER_EXPANSION;
            size = new Vector3(
                Mathf.Max(0.1f, size.x),
                Mathf.Max(0.1f, size.y),
                Mathf.Max(0.1f, size.z));

            Bounds volume = new Bounds(InteriorTrigger.center, size);
            if (padding != 0f) volume.Expand(padding);

            return volume.Contains(localPos);
        }

        private const float RAY_EARLY_REJECT_PADDING = 12f;

        // Full inside test: ray based, with a bounds fallback for awkward geometry.
        public bool IsPositionInside(Vector3 pos)
        {
            if (MasterInterior == null) return false;

            // With MasterInterior inactive the colliders are gone and a raycast would
            // lie. An inactive MasterInterior means the player is outside.
            if (!MasterInterior.activeSelf) return false;

            // Cheap geometric pre-rejection: no rays for points well outside the volume.
            // (AutoResolveOverlappingExternalObjects calls this for thousands of
            //  renderer corners; this line removes most of those rays.)
            if (InteriorTrigger != null && !IsPositionInVolume(pos, RAY_EARLY_REJECT_PADDING))
                return false;

            // During a load the game can snap the player into the terrain, so the ray
            // origin is lifted 2.5m to keep the rays from missing the building's floor.
            Vector3 rayOrigin = pos + (Vector3.up * 2.5f);

            // 1. CEILING CHECK (ray upwards)
            bool roofHit = CheckDirectionForInterior(rayOrigin, Vector3.up, 30f);

            // 2. FLOOR CHECK (ray downwards)
            bool floorHit = CheckDirectionForInterior(rayOrigin, Vector3.down, 30f);

            // A roof of this building above us and a floor of it below us means we are
            // definitely inside.
            if (roofHit && floorHit) return true;

            // FALLBACK: raycasts can fail (sloped roofs, thin colliders, large hangar
            // structures...). Inside the InteriorTrigger bounds still counts as inside,
            // which compensates for those misses.
            if (InteriorTrigger != null)
            {
                Vector3 localPos = InteriorTrigger.transform.InverseTransformPoint(pos);
                Bounds localBounds = new Bounds(InteriorTrigger.center, InteriorTrigger.size);
                // Shrink slightly so doorways do not produce false positives.
                localBounds.Expand(-0.5f);
                if (localBounds.Contains(localPos))
                    return true;
            }

            return false;
        }

        public bool IsPositionInsideRaycastOnly(Vector3 pos, float originLift = 2.5f)
        {
            if (MasterInterior == null) return false;
            if (!MasterInterior.activeSelf) return false;

            // Generous pre-rejection (see RAY_EARLY_REJECT_PADDING). This is a REJECT
            // test, not an ACCEPT test - it only skips rays for distant points, so it
            // does not break the "no bounds fallback" rule.
            if (InteriorTrigger != null && !IsPositionInVolume(pos, RAY_EARLY_REJECT_PADDING))
                return false;

            Vector3 rayOrigin = pos + (Vector3.up * originLift);
            bool roofHit = CheckDirectionForInterior(rayOrigin, Vector3.up, 30f);
            bool floorHit = CheckDirectionForInterior(rayOrigin, Vector3.down, 30f);
            return roofHit && floorHit;
        }

        // Ray lift to use for items (GearItem / Placeable). Only 0.15m: enough that the
        // downward ray does not start inside the collider of an item resting on the
        // floor, small enough not to clear the ceiling for an item on a table or shelf.
        public const float ITEM_RAY_ORIGIN_LIFT = 0.15f;

        private bool CheckDirectionForInterior(Vector3 origin, Vector3 direction, float maxDistance)
        {
            // QueryTriggerInteraction.Ignore so invisible triggers are not hit.
            int hitCount = RayScan.Cast(origin, direction, maxDistance,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (hitCount <= 0) return false;   // nothing hit at all -> outside

            Transform masterT = MasterInterior.transform;
            Transform shellT = (ExteriorShell != null) ? ExteriorShell.transform : null;

            for (int step = 0; step < hitCount; step++)
            {
                int idx = RayScan.NextClosest();
                if (idx < 0) break;

                Collider col = RayScan.Get(idx).collider;
                if (col == null) continue;

                Transform ct = col.transform;

                // 1. Ignore the player's own body (capsule collider).
                if (PlayerRefs.IsPlayerRoot(ct.root))
                    continue;

                // 2. Pass through the exterior shell.
                if (shellT != null && ct.IsChildOf(shellT))
                    continue;

                // 3. Pass through dropped GearItems and Placeables (furniture).
                if (col.GetComponentInParent<Il2Cpp.GearItem>() != null || col.GetComponentInParent<Il2CppTLD.Placement.Placeable>() != null)
                    continue;

                // Is the FIRST valid object hit part of MasterInterior?
                // If not we are outside it - either way this is the answer.
                return ct.IsChildOf(masterT);
            }

            // Only pass-through hits: treat as outside.
            return false;
        }
    }
}
