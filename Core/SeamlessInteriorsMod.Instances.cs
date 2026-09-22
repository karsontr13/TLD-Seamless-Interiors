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

        // OPTIONAL: the key this building's data is stored under in the game's own
        // (pre-mod) save slot. Normally resolved automatically from the door's GUID or
        // from InteriorSceneBaseName - see SeamlessInteriorsMod.LegacyImport.cs.
        // Only set this by hand when automatic resolution picks the wrong entry
        // (F6 in game lists the keys the current save really contains).
        public string LegacySceneKeyOverride;

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

        // Renderers the interior scene ships switched off (AFHangar's stair collision ramps).
        // ShowCloneRenderers leaves them as they are.
        public List<Renderer> SceneDisabledRenderers = new List<Renderer>();

        public List<WeatherParticleManager.ParticleKillerInstance> CustomKillers = new List<WeatherParticleManager.ParticleKillerInstance>();
        public WeatherParticleManager.ParticleKillerInstance ParticleKiller = null;

        public bool WatchdogStarted = false;

        // Which generation of this instance the running watchdog belongs to.
        //
        // WHY: MelonLoader coroutines are not bound to a scene, and the watchdog loops on
        // "while (RunCompleted)" - which the persist flow deliberately leaves true. So the
        // old loop kept running after the region had been torn down, and the reattach
        // started a SECOND one: one more watchdog per region round trip, for ever.
        //
        // That is not only wasted work. A parked clone sits in DontDestroyOnLoad at its
        // region coordinates, and the stale loop keeps measuring the distance from the
        // player - who is now standing in a completely different scene - against those
        // coordinates. Close enough and it runs the stray sweep, which ADOPTS whatever it
        // finds inside the clone's volume into the building, i.e. takes objects out of the
        // scene the player is actually in.
        //
        // Bumping this makes the old loop exit on its next tick.
        public int WatchdogGeneration = 0;

        // ─── LAZY CONTENT HYDRATION ───
        //
        // The expensive part of building a clone is not the geometry, it is the
        // CONTENT: every loose GearItem is destroyed and respawned from JSON, and
        // every container's contents are deserialized. On a 1000 day save that can be
        // hundreds of items PER BUILDING, and a region has up to 15 buildings - so a
        // region load used to pay for thousands of item spawns nobody can see.
        //
        // Content is now restored only when the player actually comes near
        // (see SeamlessInteriorsMod.Hydration.cs). Until then the clone keeps the raw
        // scene template content, which is invisible: the clone is SetActive(false)
        // while the player is outside.
        //
        // CRITICAL INVARIANT: while ContentHydrated is false the mod must NEVER write
        // this instance's content save files. The clone does not hold the player's
        // items yet, so saving would overwrite the JSON with the template contents.
        // Every Save* function checks this flag first.
        public bool ContentHydrated = false;
        public bool HydrationInProgress = false;

        // Guids of the placeables the MOD created in this building - the player's own
        // furniture, decorations and looted containers, which do not exist in the scene
        // template at all.
        //
        // Their identity has to be tracked explicitly rather than guessed from the
        // object's name: the name is what the "(PLACED)" test reads, and an object the
        // game re-creates does not reliably keep it. Getting that wrong is expensive in
        // both directions - RestorePlaceablePositions switches off anything it does not
        // recognise, and SaveSpawnedPlaceables drops anything it does not recognise.
        public readonly System.Collections.Generic.HashSet<string> SpawnedPlaceableGuids =
            new System.Collections.Generic.HashSet<string>();

        // Guids the pre-mod import switched off because the game's own save listed them
        // as Removed. Kept so the diagnostics can say WHY an object in the building is
        // dark - "the import decided the player had taken this away" is a very different
        // answer from "something lost track of it".
        public readonly System.Collections.Generic.HashSet<string> LegacyRemovedGuids =
            new System.Collections.Generic.HashSet<string>();

        // Of the player's own objects, the ones the save file says should be STANDING
        // here. Anything in this set that turns up switched off has been switched off by
        // something outside the mod, and is repaired (see RepairSpawnedPlaceables).
        //
        // Kept separate from SpawnedPlaceableGuids because the player is allowed to
        // switch their own decorations off, and that has to survive.
        public readonly System.Collections.Generic.HashSet<string> SpawnedShouldBeActive =
            new System.Collections.Generic.HashSet<string>();

        // The hydration coroutine while it is running, so a player who reaches the door
        // before the background restore finishes can take it over and drain it to
        // completion on the spot instead of walking into a half-filled building.
        public System.Collections.IEnumerator HydrationRoutine = null;

        // ─── LEGACY (PRE-MOD) SAVE IMPORT ───
        //
        // Set while probing whether this building has data in the game's OWN save from
        // before the mod was installed. See SeamlessInteriorsMod.LegacyImport.cs.
        public LegacyImportState LegacyImport = LegacyImportState.Unknown;

        // How the vanilla scene save data for this building is addressed in the slot.
        public LegacySourceKind LegacySource = LegacySourceKind.None;
        public string LegacySourceId = null;

        // The blob the probe already pulled out of the slot, held until the transcode
        // consumes it. Reading it is what proves there is anything to import, so keeping
        // it saves reading and parsing the same few hundred KB a second time. Released
        // the moment the import finishes.
        public string LegacyRawBlob = null;

        // ─── TIME WHILE THE CLONE IS CLOSED ───
        //
        // Unity stops updating everything under MasterInterior once the clone closes, which
        // freezes the cooking pots and fires inside it. SeamlessInteriorsMod.FrozenTime.cs
        // keeps them running by hand; these fields are its bookkeeping.
        //
        // The component lists are collected the moment the clone closes. Nothing new can be
        // put into a building the player is not in, so they stay valid until it opens again.
        public readonly List<Il2Cpp.Fire> ClosedFires = new List<Il2Cpp.Fire>();
        public readonly List<Il2Cpp.CookingPotItem> ClosedCookingPots = new List<Il2Cpp.CookingPotItem>();

        // Burning light sources: their fuel and burn time run down while the building is shut,
        // as the vanilla save's catch-up does when an interior scene comes back.
        public readonly List<Il2CppTLD.Gear.KeroseneLampItem> ClosedLamps = new List<Il2CppTLD.Gear.KeroseneLampItem>();
        public readonly List<Il2Cpp.TorchItem> ClosedTorches = new List<Il2Cpp.TorchItem>();
        public readonly List<Il2Cpp.FlareItem> ClosedFlares = new List<Il2Cpp.FlareItem>();

        // ─── HEAT WHILE THE CLONE IS PARKED FOR ANOTHER REGION ───
        //
        // A parked clone's fires keep heating the building, as the vanilla mod bookkeeping does
        // once the interior scene is gone. Each fire is given the life it had left when it was
        // parked and heats until that runs out; the fire's own timers are left to the game's
        // catch-up on the way back. The list is dropped when the clone is reattached.
        public readonly List<Il2Cpp.Fire> ParkedFires = new List<Il2Cpp.Fire>();
        public readonly List<float> ParkedFireSecondsLeft = new List<float>();
        public bool ParkedHeatCacheBuilt = false;

        // Was the clone open the last time the tick looked? The open -> closed edge is what
        // triggers the rescan, and it is detected here rather than in the many places that
        // call SetActive(false), so no path can be forgotten.
        public bool ClosedTimeWasOpen = true;

        // Forces a rescan even without that edge (a clone that was already closed the first
        // time the tick saw it, or a save/load that put a new Fire into a closed clone).
        public bool ClosedTimeCacheDirty = true;
        public float ClosedTimeNextRescan = 0f;

        // ─── EXTERIOR FIRE EFFECTS ───
        //
        // The chimneys and window glass on this building's shell, and which fire smokes through
        // which chimney. Built for one shell and rebuilt whenever ExteriorShell is replaced;
        // see SeamlessInteriorsMod.ExteriorFireEffects.cs.
        public ExteriorFireFx ExteriorFx = null;

        // Has the safehouse "clear junk" (R) action been used on this building?
        // The game's own flag is per scene and cannot cover clone scenes;
        // see SeamlessInteriorsMod.Junk.cs
        public bool JunkCleared = false;

        // Junk objects switched off by clearing this building - by the mod, or by the
        // game's own ClearJunk while the player was customizing here.
        // Only these may be re-enabled: touching objects the scene template itself
        // left disabled would break the look of the interior.
        public List<GameObject> JunkClearedObjects = new List<GameObject>();

        // Objects standing inside this building that are NOT children of its clone -
        // items dropped on the porch side of a doorway, furniture the adoption pass could
        // not claim. Deactivating the clone cannot hide those, so the hide pass switches
        // their renderers and colliders off by hand.
        //
        // Written down so the SHOW pass does not have to search for them again. Finding
        // them costs two whole-scene searches (FindObjectsOfType over ~5000 gear and
        // ~1300 placeables, 43-176 ms each); giving them back costs a walk of this list,
        // which normally holds a handful of objects.
        //
        // OutsidersRecorded says whether this list can be trusted yet. Straight after a
        // load nothing has been hidden, so there is nothing written down, and a building
        // entered for the first time still has to search once - something outside the
        // mod may have left an object dark in there.
        public readonly List<GameObject> HiddenOutsiders = new List<GameObject>();
        public bool OutsidersRecorded = false;

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

        // Is there a piece of THIS building directly overhead?
        //
        // The volume test is an axis-aligned box around a building that is not axis
        // aligned, so it reaches past the walls - a crate on the porch, a bed leaning
        // against the outside wall and a body dropped by the door all land inside it.
        // Asking for a roof separates them from anything genuinely indoors, and unlike
        // the full inside test it does not also demand a floor, so an item resting on a
        // table or a high shelf still answers yes.
        //
        // Needs the clone to be OPEN - with it switched off there are no colliders to hit.
        public bool IsUnderInteriorRoof(Vector3 pos, float originLift = ITEM_RAY_ORIGIN_LIFT)
        {
            if (MasterInterior == null || !MasterInterior.activeSelf) return false;

            if (InteriorTrigger != null && !IsPositionInVolume(pos, RAY_EARLY_REJECT_PADDING))
                return false;

            return CheckDirectionForInterior(pos + (Vector3.up * originLift), Vector3.up, 30f);
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

                // 4. And through what mods built in the world (an Architect wall): not the building's own geometry.
                if (SeamlessInteriorsMod.IsModOwnedRoot(ct.root))
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
