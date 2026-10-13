using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── SEVERAL BUILDINGS AT ONCE ───
        //
        // The outside-in portals were written for one building, the nearest, and everything they
        // own was a static. That was enough at the Camp Office, which stands alone. At the lake
        // cabins it was not (owner, 2026-09-18, between Lake Cabins E and F, 16.4 and 16.6 m away):
        // "when buildings are close together like this, it flips between them often and closely",
        // and "it causes a significant frame hitch whenever it happens." Each flip tore down one
        // building's dressing, switched its room off, dressed the other and switched that room on.
        // Timed the same day over six flips: teardown 43 to 51 ms, dressing 17 to 26, switching the
        // room on 48 to 76, so 110 to 147 ms in ONE frame, as often as every 0.8 s. And the building
        // that lost had nothing behind its glass while the player was looking straight at it.
        //
        // So (owner): "keep them all loaded and enable just the outside-in camera portals for the
        // one the player is looking at the most."
        //
        //   - Every supported shell within OUTSIDE_IN_DRESSING_RANGE stays DRESSED. The dressing is
        //     static geometry; it costs nothing to keep and 20 ms to make.
        //   - The rooms of the nearest few within portal range are HELD OPEN, all at once. How many
        //     is a preference, because a held room is a switched-on room and not everyone has the
        //     owner's machine. One reproduces the old behavior.
        //   - THE CAMERAS ARE NOT PER BUILDING AND NEVER WERE. The pool is per window, and every
        //     held room's windows compete for it by apparent size (RenderOutsideInPortals). Two
        //     cabins side by side cost no more cameras than one.
        //
        // HOW THE STATICS BECAME PER BUILDING WITHOUT REWRITING 2,000 LINES THAT WORK. Each one is
        // now a property forwarding to s_OI, the building currently BOUND, and every function in
        // OutsideInPortals.cs works on the bound building exactly as it worked on the only one.
        // The tick binds each building in turn. Code outside this system that asks about "the"
        // building (cleaning a window) finds the nearest one bound, which is what it always meant.

        // A blank building is bound when there is none, so nothing here is ever null.
        private static readonly OutsideInBuilding s_OutsideInNone = new OutsideInBuilding();
        private static OutsideInBuilding s_OI = s_OutsideInNone;
        private static readonly List<OutsideInBuilding> s_OutsideInBuildings = new List<OutsideInBuilding>();

        // Buildings whose setup failed, so they are not set up again every frame. Cleared with
        // everything else.
        private static readonly HashSet<string> s_OutsideInUnsupported = new HashSet<string>();

        // A dressed building is kept this much past the range it was dressed at, for the reason a
        // held room is: a player pacing along the line. It was 10 m, and the line found the player
        // anyway: walking among Lake Cabins E, F and A_2 is walking 150 to 175 m from Lake Cabin A_1,
        // which was dressed and undressed again and again (owner, 2026-09-18: "a few quick hitches
        // when walking between the three buildings"; the log had A_1 dressed twice in 30 s, 9.8 ms
        // each, and a 70.9 ms frame in this system's tick in the period it was dropped, which the
        // teardown did not log at the time and now does). Dressing costs nothing to keep.
        private const float OUTSIDE_IN_DRESSING_RELEASE_MARGIN = 100f;

        // A ROOM IS HELD MUCH FURTHER OUT THAN ITS WINDOWS ARE DRAWN. The portal range exists to stop
        // paying for cameras too small to read, and it used to switch the room off with them. But a
        // held room nobody is looking into is nearly free - the whole tick was 0.7 ms with three
        // held and one camera drawing, and the main camera never draws their layer - while switching
        // one on or off is about 50 ms. With three cabins side by side the player crossed the 50 m
        // line for all of them together (owner, 2026-09-18, "still a few frame hitches": the log had
        // three rooms returned within 0.2 s at 55 m, 44 to 48 ms each, and three opened again at
        // 50 m). So the cameras keep the portal range and the room keeps this one: the switch then
        // happens far from the building, seldom, and with nothing on screen to show it.
        private const float OUTSIDE_IN_ROOM_HOLD_RANGE = 100f;
        private const float OUTSIDE_IN_ROOM_RELEASE_MARGIN = 15f;

        // A held room defends its place against a nearer newcomer by this factor of distance, so
        // that with more buildings in range than rooms allowed, the last place does not change
        // hands on every step.
        private const float OUTSIDE_IN_HELD_ROOM_ADVANTAGE = 0.85f;

        private static MelonPreferences_Entry<int> s_PrefOutsideInMaxHeldRooms;
        public static int OutsideInMaxHeldRooms
        {
            get { return s_PrefOutsideInMaxHeldRooms != null ? Mathf.Clamp(s_PrefOutsideInMaxHeldRooms.Value, 1, 6) : 3; }
            set { if (s_PrefOutsideInMaxHeldRooms != null) s_PrefOutsideInMaxHeldRooms.Value = value; }
        }

        private sealed class OutsideInBuilding
        {
            // Decided each frame by ChooseOutsideInHeldRooms, acted on by the tick.
            public bool WantHeld;
            public bool CamerasOn;
            public float ShellDistance = float.MaxValue;
            // This room's layer while held (PickOutsideInRoomLayer); its cameras draw only this.
            public int Layer = 3;

            // Everything below was a static of the same name, s_OutsideIn + the name, when this
            // system served one building, and is documented where that static is still declared.
            public Material FrostMaterial;
            public bool FrostShadowed;
            public Texture FrostSheetSource;
            public float FrostContrastApplied = 1f;
            public List<GameObject> FrostOverlays = new List<GameObject>();
            public List<List<Renderer>> ApertureDrifts = new List<List<Renderer>>();
            public float AppliedDriftOffset = float.NaN;
            public float AppliedDriftOverlap = float.NaN;
            public float AppliedRecess = -1f;
            public bool ShellPanesHidden;
            public Material HiddenPaneMaterial;
            public Material BackingMaterial;
            public List<Renderer> PaneRenderers = new List<Renderer>();
            public List<Material[]> PaneSavedMaterials = new List<Material[]>();
            public List<Mesh> PaneSavedMeshes = new List<Mesh>();      // by PaneRenderers; null where the mesh was not split
            public List<Mesh> PaneSplitMeshes = new List<Mesh>();      // the clones, to destroy at restore
            public List<GameObject> Backings = new List<GameObject>();
            public List<InteriorWindowOpening> ApertureOpenings = new List<InteriorWindowOpening>();
            public List<GameObject> BarCaps = new List<GameObject>();
            public List<float> BarFront = new List<float>();
            public float BarCapsApplied = float.NaN;
            public List<MeshFilter> ResizedFrames = new List<MeshFilter>();
            public List<Mesh> ResizedFrameOriginals = new List<Mesh>();
            public List<List<GameObject>> LedgeDrifts = new List<List<GameObject>>();
            // The frost and snow seen from outside, by aperture (WindowCoverOutside.cs).
            public List<WindowCoverLayer> Covers = new List<WindowCoverLayer>();
            public float CoversNextTick;
            public bool CoversLive;
            public int PairedCount;
            public float RetryAt;
            public List<ShellAperture> Apertures = new List<ShellAperture>();
            public string InstanceId;
            public SeamlessInteriorInstance Instance;
            public List<bool> WasDrawn = new List<bool>();
            public bool Borrowed;
            public bool ActivatedClone;
            public List<Renderer> Relayered = new List<Renderer>();
            public List<int> SavedLayers = new List<int>();
            public List<Collider> DisabledColliders = new List<Collider>();
            public HashSet<Collider> DisabledSet = new HashSet<Collider>();
            public Color AmbientDay;
            public Color AmbientNight;
            public bool AmbientValid;
            public float NextRelayerSweep;
            public List<Vector4> GlassRects = new List<Vector4>();
            public List<Vector4> FrostRects = new List<Vector4>();   // what each window's frost covers, from the pane's measured center
            public Texture2D BackingTexture;
            public List<Vector3> ApertureGlass = new List<Vector3>();
            public List<Vector2> ApertureInteriorSize = new List<Vector2>();
            public List<List<Vector3>> LedgeDriftBase = new List<List<Vector3>>();
            // The room's own window objects that may be hidden while seen from behind, by aperture
            // (null = never touched), and whether each is drawn now. See UpdateOutsideInRoomWindowSides.
            public List<List<Renderer>> RoomWindowRenderers = new List<List<Renderer>>();
            public List<bool> RoomWindowShown = new List<bool>();
            // The wall cameras for the views in, built on first need. See OutsideInWallCameras.cs.
            public List<OutsideInWallGroup> WallGroups;
            public int[] ApertureWallGroup;
            public OutsideInWallGrid[] WallGrids;
            public float WallBuiltFit = float.NaN, WallBuiltToward = float.NaN;
            public int WallBuiltPaired = -1, WallBuiltCount = -1;
            public float WallBuiltAcross = float.NaN, WallBuiltAlong = float.NaN, WallBuiltPlane = float.NaN;
            public bool WallBuiltSorted, WallBuiltHand;
            // The room's painted outside behind its windows, switched off while a view in draws.
            // Built on first need, per borrowing. See OutsideInBackdrops.
            public List<Renderer> Backdrops;
            // The building's window models held at their detailed version while its cameras are on.
            // See PinOutsideInWindowModels.
            public List<LODGroup> PinnedWindowModels;
            // The building's own glass given back while its cameras are off. See ShowShellGlassPastRange.
            public bool GlassShownPastRange;
            // The fades (ViewFade.cs): the building's across the camera range, and each pane's.
            public float Level;
            public List<float> PaneLevel = new List<float>();
            public List<int> PaneDrawnFrame = new List<int>();
            public List<bool> PaneWanted = new List<bool>();
            public List<bool> PaneDrawnLastFrame = new List<bool>();
        }

        private static void BindOutsideIn(OutsideInBuilding building)
        {
            s_OI = building ?? s_OutsideInNone;
        }

        private static int CountOutsideInHeldRooms()
        {
            int held = 0;
            for (int h = 0; h < s_OutsideInBuildings.Count; h++) if (s_OutsideInBuildings[h].Borrowed) held++;
            return held;
        }

        private static OutsideInBuilding NearestOutsideInBuilding()
        {
            OutsideInBuilding nearest = null;
            for (int h = 0; h < s_OutsideInBuildings.Count; h++)
                if (nearest == null || s_OutsideInBuildings[h].ShellDistance < nearest.ShellDistance) nearest = s_OutsideInBuildings[h];
            return nearest;
        }

        // Everything, for when the system has nothing to say at all: switched off, the player
        // inside a building, or no camera.
        private static void TeardownAllOutsideIn()
        {
            for (int h = s_OutsideInBuildings.Count - 1; h >= 0; h--)
            {
                BindOutsideIn(s_OutsideInBuildings[h]);
                TeardownOutsideInPortals();
            }
            BindOutsideIn(null);
            s_OutsideInUnsupported.Clear();
        }

        // Drops buildings that have gone out of range or out of existence, and dresses AT MOST ONE
        // new one per frame, nearest first. Arriving in a region with ten buildings in range is ten
        // frames of 20 ms, not one of 200.
        private static void UpdateOutsideInBuildingSet(Vector3 eye)
        {
            float keepRange = OUTSIDE_IN_DRESSING_RANGE + OUTSIDE_IN_DRESSING_RELEASE_MARGIN;

            for (int h = s_OutsideInBuildings.Count - 1; h >= 0; h--)
            {
                var building = s_OutsideInBuildings[h];
                var instance = building.Instance;

                // Unity's overloaded == is what notices a shell or a room destroyed with its scene,
                // and a region reloaded under the same id is a different instance object.
                SeamlessInteriorInstance current;
                bool alive = instance != null && instance.ExteriorShell != null && instance.MasterInterior != null
                             && building.InstanceId != null
                             && ActiveInteriors.TryGetValue(building.InstanceId, out current) && current == instance;

                if (alive) building.ShellDistance = ShellDistanceFrom(instance, eye);

                if (!alive || building.ShellDistance > keepRange)
                {
                    string droppedId = building.InstanceId;
                    float droppedAt = building.ShellDistance;
                    var dropTimer = System.Diagnostics.Stopwatch.StartNew();

                    BindOutsideIn(building);
                    TeardownOutsideInPortals();
                    if (droppedId != null) s_OutsideInRetriesLeft.Remove(droppedId);

                    if (s_DebugBounds)
                        MelonLogger.Msg($"[DIS-ICE] {droppedId}: giydirme kaldirildi ({(alive ? $"{droppedAt:F0} m" : "bina artik yok")}, {dropTimer.Elapsed.TotalMilliseconds:F1} ms). " +
                                        $"Giydirilmis bina: {s_OutsideInBuildings.Count}.",
                                        $"[OUTSIDE-IN] {droppedId}: window frost and snow taken down ({(alive ? $"building is {droppedAt:F0} m away" : "the building no longer exists")}, took {dropTimer.Elapsed.TotalMilliseconds:F1} ms). " +
                                        $"Buildings still set up: {s_OutsideInBuildings.Count}.");
                }
            }

            SeamlessInteriorInstance next = null;
            string nextId = null;
            float nextDistance = OUTSIDE_IN_DRESSING_RANGE * OUTSIDE_IN_DRESSING_RANGE;

            foreach (var pair in ActiveInteriors)
            {
                var instance = pair.Value;
                if (instance == null || instance.ExteriorShell == null || instance.MasterInterior == null) continue;
                if (instance.Config == null || string.IsNullOrEmpty(instance.Config.ExteriorShellPrefabName)) continue;

                string id = instance.Config.ResolvedInstanceId;
                if (s_OutsideInUnsupported.Contains(id) || IsOutsideInBuilding(id)) continue;

                float distance = ShellDistanceFrom(instance, eye); distance *= distance;
                if (distance >= nextDistance) continue;

                // Measured from the shell's own mesh where the author's table has no entry, and only
                // for a shell that IS the configured one: in a save where a cabin is burnt the mod
                // binds the ruin, and the intact cabin's windows hung in the air round it (found
                // 2026-09-18 at burnt Lake Cabin B). See ShellWindows.cs.
                WindowSpot[] unused;
                if (!TryGetShellWindows(instance, out unused)) continue;

                nextDistance = distance;
                next = instance;
                nextId = id;
            }

            if (next == null) return;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var fresh = new OutsideInBuilding();
            fresh.ShellDistance = Mathf.Sqrt(nextDistance);
            BindOutsideIn(fresh);
            if (SetupOutsideInPortals(next, nextId))
            {
                s_OutsideInBuildings.Add(fresh);
                if (s_DebugBounds)
                    MelonLogger.Msg($"[DIS-ICE] {nextId}: giydirildi ({fresh.ShellDistance:F0} m, {timer.Elapsed.TotalMilliseconds:F1} ms). " +
                                    $"Giydirilmis bina: {s_OutsideInBuildings.Count}.",
                                    $"[OUTSIDE-IN] {nextId}: window frost and snow set up (building is {fresh.ShellDistance:F0} m away, took {timer.Elapsed.TotalMilliseconds:F1} ms). " +
                                    $"Buildings set up so far: {s_OutsideInBuildings.Count}.");
            }
            else
            {
                s_OutsideInUnsupported.Add(nextId);
                BindOutsideIn(null);
            }
        }

        private static bool IsOutsideInBuilding(string id)
        {
            for (int h = 0; h < s_OutsideInBuildings.Count; h++)
                if (s_OutsideInBuildings[h].InstanceId == id) return true;
            return false;
        }

        // Which dressed buildings have their rooms held open this frame: within the hold range (and
        // 15 m more once held), never with the player possibly inside, nearest first, up to the
        // preference. And which of them are near enough for their windows to be drawn: the portal
        // range, and 5 m more once drawing. Distances were refreshed by UpdateOutsideInBuildingSet.
        private static readonly List<OutsideInBuilding> s_OutsideInHeldOrder = new List<OutsideInBuilding>();

        private static void ChooseOutsideInHeldRooms(Vector3 eye)
        {
            s_OutsideInHeldOrder.Clear();

            for (int h = 0; h < s_OutsideInBuildings.Count; h++)
            {
                var building = s_OutsideInBuildings[h];
                building.WantHeld = false;

                building.CamerasOn = building.ShellDistance
                                     <= OutsideInPortalRange + (building.CamerasOn ? OUTSIDE_IN_RELEASE_MARGIN : 0f);
                PinOutsideInWindowModels(building, building.CamerasOn || building.Level > 0f);

                // Never nearer than the cameras need it, whatever the preference says.
                float reach = Mathf.Max(OUTSIDE_IN_ROOM_HOLD_RANGE, OutsideInPortalRange)
                            + (building.Borrowed ? OUTSIDE_IN_ROOM_RELEASE_MARGIN : 0f);
                if (building.ShellDistance > reach) continue;

                // NEVER WITH THE PLAYER IN THE ROOM. Borrowing switches off every collider in the
                // interior, the floor included. See PlayerMayBeInsideForOutsideIn.
                if (PlayerMayBeInsideForOutsideIn(building.Instance, eye)) continue;

                s_OutsideInHeldOrder.Add(building);
            }

            int allowed = OutsideInMaxHeldRooms;
            s_OutsideInChosen.Clear();
            for (int taken = 0; taken < allowed && s_OutsideInHeldOrder.Count > 0; taken++)
            {
                int best = 0;
                for (int i = 1; i < s_OutsideInHeldOrder.Count; i++)
                    if (HeldRoomRank(s_OutsideInHeldOrder[i]) < HeldRoomRank(s_OutsideInHeldOrder[best])) best = i;

                var pick = s_OutsideInHeldOrder[best];
                s_OutsideInHeldOrder.RemoveAt(best);
                // ROOMS THAT OVERLAP AT PARK HEIGHT ARE NEVER HELD TOGETHER: each camera would draw the
                // other's walls, and the open one's colliders stand in the other's rooms (Milton H2/F3,
                // 2026-10-09). The better-ranked one is held, the other keeps its dressing.
                if (OverlapsChosenRoom(pick)) { taken--; continue; }
                pick.WantHeld = true;
                s_OutsideInChosen.Add(pick);
            }
        }

        private static readonly List<OutsideInBuilding> s_OutsideInChosen = new List<OutsideInBuilding>();
        // Rooms closer than this to touching still count as apart (neighbors share no space).
        private const float OUTSIDE_IN_ROOM_OVERLAP_SLACK = 0.3f;

        private static bool OverlapsChosenRoom(OutsideInBuilding candidate)
        {
            Bounds mine;
            if (candidate.Instance == null || !candidate.Instance.TryGetRoomWorldBox(out mine)) return false;
            mine.Expand(-2f * OUTSIDE_IN_ROOM_OVERLAP_SLACK);
            for (int i = 0; i < s_OutsideInChosen.Count; i++)
            {
                Bounds other;
                if (s_OutsideInChosen[i].Instance != null && s_OutsideInChosen[i].Instance.TryGetRoomWorldBox(out other) && mine.Intersects(other))
                    return true;
            }
            return false;
        }

        // ─── WHAT IT COSTS, IN THE LOG ───
        //
        // Holding several rooms open was chosen without knowing its price per frame, and the owner's
        // standing requirement is a frame-time pass before release: "Not everyone has a fast computer
        // and GPU like I do." This is the CPU half of that: this system's own tick, averaged, with
        // what it was serving, one line every 15 seconds while any room is held. It cannot see the
        // GPU, or what a switched-on room costs the rest of the game. Only with Verbose Logging on
        // (F7), like every other routine line of this system: with it off the tick is not timed at
        // all (owner, 2026-09-18).
        private const float OUTSIDE_IN_COST_REPORT_SECONDS = 15f;
        private static double s_OutsideInCostSum, s_OutsideInCostWorst;
        private static int s_OutsideInCostFrames, s_OutsideInCostCameras;

        // THE WHOLE FRAME, not only this system's part of it, because the tick has been under 3 ms
        // through every hitch the owner has felt since the rooms stopped trading places. A frame
        // over 25 ms is a dropped frame at 60; over 50 is one anybody notices. Counted alongside:
        // frames during which the author's content fill was running, the one other heavy thing
        // known to happen while the player is simply walking about.
        private static int s_OutsideInCostSlowFrames, s_OutsideInCostVerySlowFrames, s_OutsideInCostFillFrames;
        private static float s_OutsideInCostLongestFrame;
        private static float s_OutsideInCostReportAt;

        private static void ReportOutsideInCost(double tickMs)
        {
            int held = CountOutsideInHeldRooms();
            if (held == 0 && s_OutsideInCostFrames == 0) return;

            int cameras = 0;
            for (int slot = 0; slot < s_OutsideInSlotAperture.Count; slot++) if (s_OutsideInSlotAperture[slot] >= 0) cameras++;

            s_OutsideInCostSum += tickMs;
            if (tickMs > s_OutsideInCostWorst) s_OutsideInCostWorst = tickMs;
            s_OutsideInCostCameras += cameras;
            s_OutsideInCostFrames++;

            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (frameMs > 25f) s_OutsideInCostSlowFrames++;
            if (frameMs > 50f) s_OutsideInCostVerySlowFrames++;
            if (frameMs > s_OutsideInCostLongestFrame) s_OutsideInCostLongestFrame = frameMs;
            if (s_HydrationPumpRunning) s_OutsideInCostFillFrames++;

            if (Time.realtimeSinceStartup < s_OutsideInCostReportAt) return;
            s_OutsideInCostReportAt = Time.realtimeSinceStartup + OUTSIDE_IN_COST_REPORT_SECONDS;

            float fps = 1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f);
            float meanCameras = (float)s_OutsideInCostCameras / s_OutsideInCostFrames;
            MelonLogger.Msg($"[DIS-ICE] maliyet: {s_OutsideInCostFrames} karede ortalama {s_OutsideInCostSum / s_OutsideInCostFrames:F2} ms, " +
                            $"en kotu {s_OutsideInCostWorst:F1} ms; giydirilmis {s_OutsideInBuildings.Count}, acik oda {held}, " +
                            $"ortalama kamera {meanCameras:F1}, {fps:F0} fps. Tum oyun: 25 ms ustu {s_OutsideInCostSlowFrames} kare, 50 ms ustu {s_OutsideInCostVerySlowFrames}, " +
                            $"en uzun {s_OutsideInCostLongestFrame:F0} ms; icerik dolumu surerken {s_OutsideInCostFillFrames} kare.",
                            $"[OUTSIDE-IN] cost: window portals took {s_OutsideInCostSum / s_OutsideInCostFrames:F2} ms per frame on average over {s_OutsideInCostFrames} frames, " +
                            $"worst single frame {s_OutsideInCostWorst:F1} ms; buildings with window frost and snow set up: {s_OutsideInBuildings.Count}, rooms switched on: {held}, " +
                            $"window cameras drawing: {meanCameras:F1} on average; game running at {fps:F0} fps. " +
                            $"WHOLE GAME in the same period: {s_OutsideInCostSlowFrames} frames over 25 ms, {s_OutsideInCostVerySlowFrames} over 50 ms, longest {s_OutsideInCostLongestFrame:F0} ms; " +
                            $"room contents were being filled during {s_OutsideInCostFillFrames} of the frames.");

            s_OutsideInCostSum = 0; s_OutsideInCostWorst = 0; s_OutsideInCostFrames = 0; s_OutsideInCostCameras = 0;
            s_OutsideInCostSlowFrames = 0; s_OutsideInCostVerySlowFrames = 0; s_OutsideInCostFillFrames = 0; s_OutsideInCostLongestFrame = 0f;
        }

        private static float HeldRoomRank(OutsideInBuilding building)
        {
            return building.ShellDistance * (building.Borrowed ? OUTSIDE_IN_HELD_ROOM_ADVANTAGE : 1f);
        }
    }
}
