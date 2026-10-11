using Il2CppTLD.WeatherParticle;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    // Link definition for nested scene transitions (e.g. FarmHouse -> Basement):
    // doors that lead from one interior scene into another.
    public class TerrainHoleRect
    {
        public Vector3 Offset;   // world centre; y = height the terrain is sunk to
        public Vector3 Size;     // x/z footprint in the rectangle's own axes
        public float Yaw;

        public TerrainHoleRect(Vector3 offset, Vector3 size, float yaw) { Offset = offset; Size = size; Yaw = yaw; }
    }

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

        // Moves the clone away from its shell instead of standing it on top of it.
        // Zero keeps the author's original behavior: clone on the shell's footprint.
        //
        // The two halves never have to overlap, because the player is teleported between
        // them and only ever stands in one of them. Separating them removes the whole class
        // of problems caused by two buildings occupying one volume (z-fighting, poke-through,
        // outdoor props inside the room, the terrain hole, the scale fudge factors).
        //
        // THE DEFAULT IS PARKED, STRAIGHT UP, since 2026-09-18, by the owner's decision. Why up is
        // written out in the Camp Office's config, where it was learned. 40 m until 2026-09-28, then
        // 80 m: at 40 m a tree top beside Lake Cabin A swayed in the wind into the parked room's
        // corner (owner). Tree crowns have no collider, so no ray check sees this; 80 m clears any
        // tree in the game. Checked by downward rays on a 1 m grid for all 15 Mystery Lake rooms:
        // no terrain or rock inside any of them at 80 m. Other regions are unchecked at 80 m. By that
        // day parking was carrying: the real view out of the windows and the cull eye that makes
        // it correct, the porch seen from inside, an exact test for "is the player in this room",
        // no outdoor-scenery scan, and it would have prevented both of that day's 40 m falls.
        //
        // IT HAS ONLY BEEN JUDGED ON THE CAMP OFFICE. Every other building is unchecked. Known
        // things to look for when checking one: neighbors close enough that two parked rooms, each
        // about 5% larger than its shell, overlap; terrain or cliffs standing 80 m above the
        // building; sub-interiors (the farmhouse basement has no shell and a fixed position; the
        // links between it and the farmhouse already add the target's offset). Vector3.zero on a
        // config restores the author's original placement for that building.
        //
        // The author has not agreed to this. It is how this fork is tested, and the case for it.
        public Vector3 ParkOffset = new Vector3(0f, 80f, 0f);

        public bool IsParked => ParkOffset != Vector3.zero;

        // Pin the clone to FallbackPosition instead of the shell's own position.
        public bool ForceExactPosition;

        // How far from the clone root its parts may reach and still count as the building's volume.
        // Raise it for a sprawling interior (the mine concentrator's wings lie 130 m out).
        public float BoundsRadius = 30f;

        // Terrain hole carved under the building (zero = no hole).
        public Vector3 TerrainHoleSize = Vector3.zero;
        public Vector3 TerrainHoleOffset = Vector3.zero;
        // Set: the hole is a rectangle turned by this yaw, sunk only to TerrainHoleOffset.y
        // (under a basement floor), a shallow pit beside the walls. Unset: world-axis box, sunk deep.
        public float? TerrainHoleYaw = null;
        // More turned rectangles for a building spread over several parts (world centre, size, yaw);
        // each is sunk to its own Offset.y.
        public List<TerrainHoleRect> TerrainHoles = null;

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

        // Degrees about the vertical that a parked room was turned, on top of the author's
        // rotation, to face the way its shell does. Zero when it was not turned. See
        // TurnParkedRoomToShell.
        public float ParkedYawCorrection = 0f;
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

        // Saved records the restore could not apply (missing prefab, unmatched container, failed
        // re-create). Written back verbatim on every save, so a failed load never erases them.
        public readonly System.Collections.Generic.List<string> OrphanGearEntries = new System.Collections.Generic.List<string>();
        public readonly System.Collections.Generic.List<string> OrphanContainerEntries = new System.Collections.Generic.List<string>();
        public readonly System.Collections.Generic.List<string> OrphanSpawnEntries = new System.Collections.Generic.List<string>();

        // Lighting parts each mode needs, kept in both so F8 can switch live (see LightingModeSwitch.cs).
        public readonly System.Collections.Generic.List<UnityEngine.GameObject> DarkOnlyObjects = new System.Collections.Generic.List<UnityEngine.GameObject>();
        public readonly System.Collections.Generic.List<UnityEngine.ReflectionProbe> DarkOnlyProbes = new System.Collections.Generic.List<UnityEngine.ReflectionProbe>();
        public readonly System.Collections.Generic.List<UnityEngine.Light> BigPointLights = new System.Collections.Generic.List<UnityEngine.Light>();
        public readonly System.Collections.Generic.List<UnityEngine.GameObject> DarkActivatedObjects = new System.Collections.Generic.List<UnityEngine.GameObject>();
        public readonly System.Collections.Generic.List<UnityEngine.Behaviour> DarkEnabledManagers = new System.Collections.Generic.List<UnityEngine.Behaviour>();
        public readonly System.Collections.Generic.List<bool> DarkOnlyWasActive = new System.Collections.Generic.List<bool>();
        public readonly System.Collections.Generic.List<bool> BigPointLightWasEnabled = new System.Collections.Generic.List<bool>();
        public bool? AppliedDarkMode = null;

        // Renderers lit from the interior's own probes in dark mode (see InteriorProbes.cs), by instance id.
        public readonly System.Collections.Generic.Dictionary<int, ProbedRenderer> ProbedRenderers =
            new System.Collections.Generic.Dictionary<int, ProbedRenderer>();

        // Lightmapped renderers with their baked and stripped looks (see StripBakedLightmaps).
        public readonly System.Collections.Generic.List<LightmappedRenderer> LightmappedRenderers =
            new System.Collections.Generic.List<LightmappedRenderer>();

        public void ClearLightingModeParts()
        {
            ProbedRenderers.Clear();
            LightmappedRenderers.Clear();
            DarkOnlyObjects.Clear(); DarkOnlyWasActive.Clear(); DarkOnlyProbes.Clear();
            BigPointLights.Clear(); BigPointLightWasEnabled.Clear();
            DarkActivatedObjects.Clear(); DarkEnabledManagers.Clear();
            AppliedDarkMode = null;
        }

        // The file existed but could not be read: never overwrite it from this clone.
        public bool GearSaveBlocked = false;
        public bool ContainerSaveBlocked = false;
        public bool PlaceableSaveBlocked = false;

        public void ClearOrphans()
        {
            OrphanGearEntries.Clear();
            OrphanContainerEntries.Clear();
            OrphanSpawnEntries.Clear();
            GearSaveBlocked = false;
            ContainerSaveBlocked = false;
            PlaceableSaveBlocked = false;
        }

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

        // Curing items (saplings, hides, guts): their evolve time runs on as well.
        public readonly List<Il2Cpp.EvolveItem> ClosedEvolveItems = new List<Il2Cpp.EvolveItem>();

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

        // Distance from a world position to this building, measured to whichever of its
        // two halves is nearer.
        //
        // With the clone standing on its shell both halves are the same place and this is
        // simply the distance to the building. With Config.ParkOffset set they are not: a
        // player INSIDE the clone is then arbitrarily far from FallbackPosition, and every
        // proximity test that reads that number alone concludes they have left the area.
        public float DistanceFrom(Vector3 worldPos)
        {
            float distance = Vector3.Distance(worldPos, Config.FallbackPosition);

            if (MasterInterior != null)
                distance = Mathf.Min(distance, Vector3.Distance(worldPos, MasterInterior.transform.position));

            return distance;
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

        // The room's true volume as a world box (its trigger without the padding), for telling whether
        // two parked rooms overlap. Works with the room switched off: only transforms are read.
        public bool TryGetRoomWorldBox(out Bounds box)
        {
            box = default(Bounds);
            if (MasterInterior == null || InteriorTrigger == null) return false;
            Vector3 size = InteriorTrigger.size - TRIGGER_EXPANSION;
            Vector3 half = new Vector3(Mathf.Max(0.05f, size.x), Mathf.Max(0.05f, size.y), Mathf.Max(0.05f, size.z)) * 0.5f;
            Transform t = InteriorTrigger.transform;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = InteriorTrigger.center + new Vector3((k & 1) == 0 ? half.x : -half.x, (k & 2) == 0 ? half.y : -half.y, (k & 4) == 0 ? half.z : -half.z);
                Vector3 world = t.TransformPoint(corner);
                min = Vector3.Min(min, world); max = Vector3.Max(max, world);
            }
            box.SetMinMax(min, max);
            return true;
        }

        private const float RAY_EARLY_REJECT_PADDING = 12f;

        // ─── THE ONE QUESTION, FOR A PARKED BUILDING: IS THIS POSITION IN THE ROOM? ───
        //
        // Owner, 2026-09-18, after two 40 m falls: "We need a function that determines if you're
        // inside of the spawned interior prefab. Use that for everything."
        //
        // Until then "inside" was something that HAPPENED to the player: a door transition set a
        // flag, a saved record re-armed it after a load, a guard cleared it when it went stale, and
        // four different geometric tests answered for everything else, two of them by casting rays
        // at colliders that another system switches off. Any path that skipped the event or undid
        // it left the mod holding the wrong answer, and that day it did, twice.
        //
        // A PARKED room makes the question exact. Its box stands ParkOffset away from anything the
        // player can reach outdoors and is entered only by teleport, so there is no doorway to
        // flicker in and no wall for the box to reach past. It needs no colliders, no active clone
        // and no memory. The three tests below and ResolvePlayerInside all defer to it for a parked
        // building, so every existing caller asks the same question and gets the same answer.
        //
        // A co-located room cannot be settled this way: its axis-aligned box reaches out past the
        // walls to where a player can stand. There the author's tests still answer. Park every
        // building and this is the only test left.
        //
        // WHERE THE ROOM WILL BE IS KNOWN WITHOUT THE ROOM (owner, 2026-09-18: "the key is where
        // will the prefab be spawned. We know it's going to be 40 meters above the exterior, and I
        // think we have zero offset with the other two coords, so that should be deterministic").
        // Measured the same day: the interior's origin 42.0 m above the shell's, 0.00 m to the
        // side. So the first answer comes from the SHELL, which never moves and exists from the
        // moment the region loads: its mesh bounds, carried up by ParkOffset, with room for the
        // floor alignment and for an interior built 5% wider than its shell. The built room's own
        // box is the second answer. Asking only the built room is what failed on the second fall:
        // at that instant the clone had not yet been moved up to its parked place, so it said "no"
        // about a player standing exactly where it was about to be.
        private const float PARKED_ROOM_SIDE_ALLOWANCE = 1.5f;
        private const float PARKED_ROOM_HEIGHT_ALLOWANCE = 3f;
        private bool _shellBoundsCached;
        private bool _shellBoundsValid;
        private Bounds _shellBounds;

        public bool IsPositionInParkedRoom(Vector3 pos, float padding = 1f)
        {
            if (Config == null || !Config.IsParked) return false;

            Bounds shell;
            if (TryGetShellMeshBounds(out shell))
            {
                var expected = new Bounds(shell.center + Config.ParkOffset, shell.size);
                expected.Expand(new Vector3(2f * (PARKED_ROOM_SIDE_ALLOWANCE + padding),
                                            2f * (PARKED_ROOM_HEIGHT_ALLOWANCE + padding),
                                            2f * (PARKED_ROOM_SIDE_ALLOWANCE + padding)));
                if (expected.Contains(pos)) return true;
            }

            return IsPositionInVolume(pos, padding);
        }

        // From the meshes through their transforms, not Renderer.bounds, which reports a box at the
        // world origin for an inactive object, and the shell is inactive exactly when this matters.
        // Cached: a shell does not move.
        private bool TryGetShellMeshBounds(out Bounds bounds)
        {
            bounds = _shellBounds;
            if (_shellBoundsCached) return _shellBoundsValid;
            if (ExteriorShell == null) return false;     // not cached: the shell may not be bound yet

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;
            var filters = ExteriorShell.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                if (filter == null || filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = mesh.center + new Vector3(
                        ((k & 1) == 0) ? mesh.extents.x : -mesh.extents.x,
                        ((k & 2) == 0) ? mesh.extents.y : -mesh.extents.y,
                        ((k & 4) == 0) ? mesh.extents.z : -mesh.extents.z);
                    Vector3 world = toWorld.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, world);
                    max = Vector3.Max(max, world);
                }
                any = true;
            }

            _shellBoundsCached = true;
            _shellBoundsValid = any;
            if (any) { _shellBounds = new Bounds(); _shellBounds.SetMinMax(min, max); }
            bounds = _shellBounds;
            return any;
        }

        // Full inside test: ray based, with a bounds fallback for awkward geometry.
        public bool IsPositionInside(Vector3 pos)
        {
            if (MasterInterior == null) return false;

            // For a parked room this GEOMETRIC question is answered by the box, active or not. That
            // replaces the rays below, not the door transitions: those still set the inside flag.
            if (Config != null && Config.IsParked) return IsPositionInParkedRoom(pos);

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
