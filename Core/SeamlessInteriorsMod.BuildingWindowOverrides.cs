using System.Collections.Generic;

namespace SeamlessInteriors
{
    // Everything about one building's windows that was set BY HAND, in one row. A field left at its
    // default means "measure it", which is what happens for every building with no row at all.
    internal sealed class BuildingWindowOverride
    {
        // Where a view from OUTSIDE into the room starts, in centimeters from the ROOM's own glass:
        // + toward outdoors, - into the room. NaN = the mod decides (RoomPointBehindPane). The views
        // OUT of the room are not moved by this.
        public float ViewFromOutsideStartsCm = float.NaN;

        // The object in the room that holds its window frames, where those frames are NOT to be seen
        // in a view from outside. Only the pieces of its mesh round each paired window are left out,
        // only while the room is being shown from outside, and the game's own mesh is given back
        // on the way in. null = the room's frames are drawn from outside, as everywhere else.
        public string RoomFramesHiddenFromOutside;

        // Windows left alone completely, by the mod's window index for the shell: no view, no dark
        // sheet, no frost of the mod's; the building's OWN pane, as the game made it.
        public int[] WindowsLeftAlone;

        // Windows fitted by hand, in centimeters. See WindowFit.
        public WindowFit[] Fits;

        // The room has openings with NO GLASS in them, only a piece of the game's painted outside
        // behind a hole in the wall, and they are to be windows like the rest. Off everywhere else:
        // a room is read by its glass, and this is asked for building by building so that no
        // building already looked at gains a window nobody has seen. See TryAddOpeningsWithNoGlass.
        public bool OpeningsWithNoGlass;

        // The BUILDING's glass is spread over many objects, and these are the ones whose glass is a
        // window: each object whose name starts with one of these is ONE window, the whole of its
        // outward glass together. null = the building's glass is one mesh, measured and joined into
        // panes as everywhere else. The Airfield hangar carries glass on 76 renderers, the measure's
        // one best mesh found 6 triangles on one door panel and no window (2026-09-20); named here,
        // the glass nobody wants a view through (the high side windows) is simply not listed. Asked
        // for building by building for the reason OpeningsWithNoGlass is. See MeasureShellWindowsByObject.
        public string[] ShellGlassObjects;

        // Bands of glass in the ROOM that are several windows each. See RoomWindowBand.
        public RoomWindowBand[] RoomBands;

        // Glass objects in the ROOM whose windows are TAKEN FROM THE BUILDING'S: every window measured
        // on the building that, carried into the room, lands inside one of these objects' boxes is a
        // room window there, at that place and size, facing that way. For glass that cannot be read
        // and is not one flat band either: the hangar's two side sheets each hold 17 windows of five
        // sizes on two tiers. The building's windows are read (ShellGlassObjects), and the front
        // matched the room within 0.6 m and the doors within 2 cm (2026-09-21), so the building says
        // where the room's windows are. See TryAddWindowsCarriedFromBuilding.
        public string[] RoomWindowsFromBuilding;

        // Glass objects in the ROOM to be read as MANY windows from their own geometry, whether or
        // not the mesh is readable - unreadable ones are read back from the GPU (see TryReadMesh).
        // Each connected piece of glass is a pane, panes side by side or stacked join into one
        // window, and the windows are sorted into a wall group per plane they stand on.
        //
        // Asked for building by building, like OpeningsWithNoGlass and ShellGlassObjects, because
        // these sheets wear the ORDINARY glass material: without a row they take the single-window
        // path, and no building already looked at should gain or lose a window. The hangar's two
        // side sheets are the case this was written for - 104 vertices, 26 quads, 17 windows a side
        // on two tiers, measured from the GPU on 2026-09-22 against the building's own 34.
        public string[] RoomManyPaneObjects;

        // Pieces of a many-pane sheet standing side by side on one wall joined into ONE window when the
        // gap between them is at most this, in centimeters, and they share at least half the shorter
        // one's height. NaN = the ordinary joining (a bar's width, edges level). Joining renumbers the
        // room windows after the joined ones. The Community Hall's front door (three panes, gaps of
        // 16 and 46 cm) and its back window (two halves), 2026-10-02; the next nearest panes there are
        // more than 2 m apart.
        public float RoomManyPaneJoinWithinCm = float.NaN;

        // BUILDING glass objects, by name, measured one window each and ADDED after the windows the
        // building's own mesh gives, so those keep their numbers. ShellGlassObjects replaces the
        // measure; this adds to it. The Community Hall's front door glass is its own object beside the
        // one mesh that holds the seven windows, 2026-10-02.
        public string[] ExtraShellGlassObjects;

        // Room panes, by object name, that are windows although the game put no painted outside behind
        // them; without a row such glass is left out (WindowOpenings.cs, "WINDOW GLASS WITH NO
        // BACKDROP"). They are added after the windows found by the rule, so those keep their numbers,
        // and each takes its outside from the nearest window in the same wall. The Small Barn, 2026-10-01.
        public string[] RoomWindowsWithoutBackdrop;

        // Room glass, by object name, that gets a view OUT and nothing else: its panes are never paired with
        // a building window, so they take no pairing from the real windows and no view in is made for them.
        // The object must also be read as glass (RoomManyPaneObjects, and RoomWindowsWithoutBackdrop where
        // nothing is painted behind it). Each looks out from where the paired windows agree the room stands.
        // Thompson's Store, 2026-10-09: the panes at the top of the room's two doors showed the world from
        // the parked room's height, 82 m up; named as plain windows they took the low window's pairing.
        // Owner: "We just need to make an inside-out camera only for each. Making the outside-in isn't worth
        // it since they're too high up and don't have mesh parts on the exterior shell door."
        public string[] RoomGlassViewOutOnly;

        // Room window objects (by name prefix) the rule misses: glass in a slot beside the frame, or not
        // STR_WindowGlassMix. Read from the glass submesh, facing away from the room's middle (2026-10-10).
        public string[] RoomGlassSlotObjects;

        // Panes of one glass object further apart than this are separate windows, in the room's slot glass
        // and the building's ExtraShellGlassObjects alike (Blackrock power plant door: two panes). NaN: 1 m in
        // the room, one window per object outside.
        public float GlassSlotJoinWithinCm = float.NaN;

        // The room already stands on its building: each building window is paired with the nearest
        // same-facing room window within this distance, instead of the whole-pattern fit (the mine).
        public float PairInPlaceWithinCm = float.NaN;

        // Seen from inside, the room's glass keeps the game's own material: SI's frost layer draws the
        // glass texture's dirt past the frame where its UVs run along a strip (2026-10-10).
        public bool RoomGlassKeepsOwnMaterial;
        // Room glass, by object name, joined into ONE window per set however far apart the pieces stand
        // (posts wider than the joining allows). A set takes its outside from the nearest window in the
        // same wall, not from a backdrop, and is added after the windows found by the rule, like
        // RoomWindowsWithoutBackdrop. The Small Barn's loft row, 2026-10-01.
        public string[][] RoomWindowJoins;

        // The room behind the glass drawn this much of its fitted size, seen from outside, in percent
        // (97 draws it about 3 percent larger). NaN = 100. The farmhouse's 97 was tuned by eye on that
        // building to pull its room's frames onto its own and line them up (2026-09-19); it was then
        // applied to every building by mistake, and it stops a wall camera serving several panes,
        // because each pane grew its room about its own middle (owner, 2026-09-26: "that was just a
        // single building tune to pull the frames together and line them up").
        public float ViewRoomSizePercent = float.NaN;

        // Panes forced onto ONE camera for the views in, by hand, where the rules would split them.
        // Each group is drawn by the screen-aligned wall camera from the MEAN of its panes' eyes, with
        // no per-pane correction, and the named room objects switched off while it renders. Written
        // for the hangar's front: eight door panes on four planes 48 cm apart whose room windows are
        // spaced unlike the building's, so the rules (one plane, one eye) gave them seven cameras.
        // Its room's eight door objects and its glass mesh stand wholly in front of the room glass
        // planes (2.5 to 38 cm and 15 to 24 cm, read from the GPU), so the per-pane cameras never
        // drew them and hiding them loses nothing; with them hidden one camera from the eye shows
        // exactly what eight did. Passed by the owner's eye on 2026-09-26: "It all looks good!"
        // (LOG-2026-09-26 0926-28 to 0926-34). A per-pane texture shift that made the picture exact
        // at the glass was tried and dropped the same day: it warps deep content as the eye moves
        // (0926-33). Panes not paired, or following another pane, are left out of the group.
        public OutsideInHandGroup[] ViewInGroups;

        // The same for the views OUT: room windows forced onto one wall camera, by the room's window
        // numbers as [WALL-CAMERA] prints them. The camera is the ordinary wall camera from the mean
        // of the members' offsets; the building's pieces between the lens and the glass are hidden
        // as they are for every wall group (FindWallShellPieces), so no objects are named. The
        // hangar's front looking out: the plane rule gave its eight doors four cameras (the mirror
        // pairs); one camera was compared against them live, switching every three seconds ten
        // times, and the owner saw no difference at all (2026-09-26, LOG 0926-38). The furthest
        // window stands 5.7 cm from the shared eye.
        public int[][] ViewOutGroups;

        // Each ViewOutGroups group's camera slid along its wall from the members' mean, in centimeters,
        // + to the right as seen from inside looking out. Same order as ViewOutGroups; missing = 0.
        // Set by the owner's eye with markers outside each window where one camera serves panes whose
        // room spacing differs from the building's (the Community Hall, 2026-10-02).
        public float[] ViewOutGroupSidewaysCm;

        // Where a view from outside is drawn, cm in front of the building's glass (- behind it), for the
        // whole building and, by building window number, for single windows (NaN = the building's value).
        // Unset, the global OutsideInViewTowardViewer (1 cm). The picture, the room point behind it, the
        // backing and the frost all move with it. Asked for on 2026-10-02 at the Community Hall, whose
        // window bars run from 6.7 cm in front of the glass to 2.4 cm behind it: a picture drawn 1 cm in
        // front left the bars' back ends behind it, and from an angle the room showed where a bar should
        // go on. Owner: "make SURE I can adjust that value ... per window, per building".
        public float PicturePlaneCm = float.NaN;
        public float[] PicturePlaneCmByWindow;

        // Outdoor snow touching a window drawn on the glass for the views out (WindowSnow.BuildingToRoom.cs).
        // Off unless asked for: only the Quonset gas station has the ground piled against a window, and the
        // search costs a scan of the snow round the building on every entry (owner, 2026-10-02).
        public bool GlassSnow;

        // Windows whose snow copied from the ROOM keeps only its sill piece: the room's window has a
        // bar whose snow the copy carries, and the building's window has no bar under it. The Camp
        // Office's short window 0 (owner, 2026-09-30: "an extra snowdrift in the center bar where
        // there shouldn't be one because there's no center bar"). See CropLedgeDriftToSill.
        public int[] DriftSillOnly;

        // Snow the game placed over one window of the BUILDING, copied onto another that has none.
        // See CopiedShellDrift.
        public CopiedShellDrift[] CopiedDrifts;

        // The turn that brings the parked room square to its building, in degrees about up, where the
        // automatic rule refuses. NaN = the rule (TurnParkedRoomToShell). Barn A's room is far larger
        // than its barn and has 13 windows to its 10, so at the right turn only 8 room windows land
        // within the rule's 6 m and it wants 9: it found -109.1 and refused by one. -109.1 is the
        // turn, and it is also exactly the barn's own rotation (room 325.0 turned to 215.9): every
        // wall's windows then match the barn's, ground and upper floors, middle-high rows included.
        // Read live 2026-09-30, after a wrong first try at -19.1 (the smallest turn that makes the
        // windows FACE along the walls, which cannot tell a quarter turn apart; _dev LOG-2026-09-30).
        public float RoomTurnDegrees = float.NaN;

        // Which room window stands behind each building window, by hand: index = the building's window,
        // value = the room's (FindInteriorWindowOpenings order), -1 = none. Replaces the automatic
        // pairing for every window it lists. Barn A: after the turn the automatic pairing matched nine
        // windows exactly and left window 9, by the side door, with nothing, while room window 1
        // stands 30 cm along from it on the same wall and floor. The owner had found it from inside:
        // window 9 "sits to the left of the entry door from outside" (2026-09-30).
        public int[] PairedRoomWindows;

        // Where a ROOM window with no window on the building looks out from, moved by hand. Such a
        // window looks out from where the paired windows agree the room stands (WindowPortals.cs, the
        // gas station's rule), which for a room far larger than its building is nowhere near the spot
        // its own wall would be. See RoomWindowViewOut.
        public RoomWindowViewOut[] ViewOutOffsets;

        // World objects a room window's view out does not draw: named by prefix, within WithinMeters of
        // the glass, hidden from that window's camera only (everyone else still sees them). For things
        // the game hangs on the building's outside that the view out cannot show sensibly. The Small
        // Barn's end-wall windows, 2026-10-01: their camera has to hide the whole barn (one mesh, porch
        // included), which left the porch's icicles hanging from nothing.
        public RoomWindowViewOutHide[] ViewOutHides;

        // Room objects, by exact name, switched off while the room is set up and back on at teardown.
        // Trade: a parked room keeps its painted outsides on so a window whose view is not drawn in a
        // frame shows the painting, not the parked room's surroundings (ShowPaintedPanesInParkedRoom);
        // hiding one gives that up for every window it stood behind. The Small Barn, 2026-10-01.
        public string[] RoomObjectsHidden;

        // Room openings with no glass at all, placed by hand beside a found room window. See RoomHandOpening.
        public RoomHandOpening[] RoomOpeningsByHand;
    }

    // A hole in the room's wall with no glass (boards only), on LikeRoomWindow's wall, width and facing;
    // its bottom and top in cm from that window's glass middle. Numbered after every found window.
    internal sealed class RoomHandOpening
    {
        public int LikeRoomWindow;
        public float BottomCm, TopCm;
        // Its middle slid along the wall (+ = the room window's Right), and its width; NaN = as LikeRoomWindow's.
        public float AcrossCm, WidthCm = float.NaN;
        // The picture's plane, cm out from the glass plane (past boards nailed outside it); NaN = as LikeRoomWindow's.
        public float PictureOutCm = float.NaN;
    }

    // One room window's view out moved from the agreed spot: right as seen from inside looking out,
    // up, and out through the glass, in centimeters. By the room's window numbers (the cyan tags).
    internal sealed class RoomWindowViewOut
    {
        public int RoomWindow;
        public float RightCm, UpCm, OutCm;
    }

    internal sealed class RoomWindowViewOutHide
    {
        public int[] RoomWindows;
        public string[] NamePrefixes;
        public float WithinMeters = 8f;
    }

    // A copy of one of the game's own snow objects on the building, set down over another window at
    // the same place in that window's own frame. Made once per scene, when the building's windows are
    // first set up, and left on the building like the original: it is scenery, not a window dressing,
    // so it does not come and go with the dressing range. See BuildCopiedShellDrifts.
    internal sealed class CopiedShellDrift
    {
        public string SourceObject;     // the game's snow object on the shell, by name ("TRN_SnowA_Prefab")
        public int FromWindow;          // the window it stands over; the nearest such object to that window's top is copied
        public int ToWindow;
        public float AcrossCm, UpCm, OutCm;  // where it goes, from ToWindow's measured center along its right, up and outward
    }

    // One hand-made camera group for the views in. See BuildingWindowOverride.ViewInGroups.
    internal sealed class OutsideInHandGroup
    {
        public int[] Panes;                 // the mod's pane indices for the shell (the dev tags' numbers)
        public string[] HideWhileRendering; // room objects switched off for this camera: a name, or a name and its "(n)" clones

        // For a room piece that comes in several copies (the hangar's two side walls are one object
        // placed twice, the second mirrored): the copy nearest this group's room glass is the one the
        // camera looks through. Only in THAT copy are the children below switched off and the
        // materials below drawn fully transparent, and only while this camera renders, so the far
        // copy seen across the room keeps its frames and the room seen from inside is untouched.
        // Materials, not objects, because the frames share their object with the walls, floor and
        // stairs; a material used by nothing but the frames can be cleared alone. Name or name "(n)".
        public string NearestCopyOf;
        public string[] NearestCopyHideChildren;
        public string[] NearestCopyClearMaterials;
    }

    // One object in the room that glazes a whole wall, to be read as several equal windows. For glass
    // whose mesh cannot be read, where only its box is known: the box is cut into Windows strips of
    // WindowWidthCm, the first and the last flush with its ends. The numbers come from the BUILDING's
    // own panes, which can be read. See TryAddBandOfWindows.
    internal sealed class RoomWindowBand
    {
        public string Object;           // the window root's name in the room starts with this
        public int Windows;
        public float WindowWidthCm;
    }

    public partial class SeamlessInteriorsMod
    {
        // ─── EVERY EXCEPTION THE WINDOWS MAKE FOR A BUILDING, IN ONE PLACE ───
        //
        // Everything about a window is measured at load: where it is, how large, which room window
        // stands behind it, what frost it wears, where its views start. That is right for most
        // buildings and wrong for a few, and no instrument settles those: at Safe House A the
        // opening the owner sees is 72 x 136 cm where the glass measures 68 x 124, its window 0 has
        // doubled glass, and rays cannot see the frame at all because the collider is a flat wall
        // (2026-09-18). His eye settled it in minutes, and then the numbers had nowhere to live and
        // were gone at the next launch. This is where they live. Owner, 2026-09-19: "We should only
        // have to author this once per funky building. Painful, but tractable."
        //
        // Keyed by ExteriorShellPrefabName like s_ShellWindows, so one row serves every building that
        // uses the shell. A building with no row, and a field left unset, is measured. Each value
        // says who judged it and when, because none of them can be derived again from the game.
        private static readonly Dictionary<string, BuildingWindowOverride> s_BuildingWindowOverrides = new Dictionary<string, BuildingWindowOverride>
        {
            // Safe House A (Trapper's Cabin). Window 0's glass is doubled and measures 4 cm too wide;
            // set by the owner's eye on 2026-09-19, last with the shutters in place: the measured
            // width and center stand, the picture is 10 cm taller, the frost half a centimeter past
            // it all round. He looked at the other six as measured and left them. Rows are printed
            // ready to paste by WindowFitDev.Print() after the sliders have been set in the game.
            {
                "STRSPAWN_CabinAExterior_Prefab", new BuildingWindowOverride
                {
                    Fits = new[]
                    {
                        new WindowFit(0, 0f, 0f, 68f, 134f, 0f, 0f, 69f, 135f, 3),
                    },
                }
            },
            // Barn A (Pleasant Valley, the author's, added upstream 2026-09-29). The room is turned by hand
            // and its windows paired by hand; see RoomTurnDegrees and PairedRoomWindows. Pairs read live
            // on the turned room, 2026-09-30: the front's ground floor 0, 1, 2 to room 2, 3, 4; the two
            // upper side rows 3, 4, 5 to 10, 8, 9 and 6, 7, 8 to 5, 6, 7, middle windows high on both;
            // window 9 by the side door to room 1.
            {
                "STRSPAWN_BarnHouseA_Prefab", new BuildingWindowOverride
                {
                    RoomTurnDegrees = -109.1f,
                    PairedRoomWindows = new[] { 2, 3, 4, 10, 8, 9, 5, 6, 7, 1 },
                    // Room windows 11 and 12, ground floor beside the side wall's upper row, have no barn
                    // window. Set by his eye from inside, 2026-09-30, on one shared camera, so each was judged
                    // on its own: first both at 275 right ("11 and 12 are good"), then, with a snow pile
                    // showing in 11 from far off, moved to window 1's own offset and set apart: "320 is the
                    // correct ... setting for window 12. And 0 is the correct setting for Window 11." Window 11
                    // then raised 17 cm, to 112, to look over a snow bank against the barn wall at its spot
                    // (terrain 43 cm under the window's bottom edge half a meter out, against 102 at window 1);
                    // "the distant terrain seemed to only move a degree or so". Window 12 not rechecked at 112.
                    ViewOutOffsets = new[]
                    {
                        new RoomWindowViewOut { RoomWindow = 11, RightCm = 0f, UpCm = 112f, OutCm = -150f },
                        new RoomWindowViewOut { RoomWindow = 12, RightCm = 320f, UpCm = 94.8f, OutCm = -150f },
                    },
                }
            },

            // The Small Barn (Pleasant Valley, the author's; the game's scene BarnHouseB). Read live 2026-10-01
            // (_dev LOG-2026-10-01, 1001-08 and 1001-09).
            // - The ground-floor window beside Mesh007, on the same wall, had no view: its glass Mesh012 has no
            //   painted outside behind it (the barn's one backdrop object, STR_Barn_B_GlassGlow, stops short
            //   of it), so the rule left it out. Named here.
            // - The loft row is six pieces of glass in pairs with posts between: the rule made two windows of
            //   it, both facing INTO the room (the nearest backdrop corners stand on the room side), and left
            //   the third pair (Mesh003, Mesh004, no backdrop) to the 80 m view of the parked room. Owner: "we
            //   need to build the entire three-window set as one single wall camera". One window, its outside
            //   taken from the ground-floor windows of the same wall.
            // - Barn window 4 is the high window in the end wall; the rule had paired it with the low side
            //   window Mesh010, which set that view 4 m too high and, through the average, every view of a
            //   room window with no barn window 1 m too high. Unpaired, then paired with its own window, Mesh013 (below).
            // Room windows after this row: 0 to 4 = Mesh007, 008, 009, 010, 011 as the rule finds them (the
            // owner passed them as 2 to 6 before the row), 5 = the loft row, 6 = Mesh012, 7 = Mesh013. Barn 0
            // to 3 keep the same room windows as before. The room is not turned (the rule's -5.3 was refused,
            // and the passed windows were judged unturned).
            {
                "STR_BarnSmallA_Exterior_Prefab", new BuildingWindowOverride
                {
                    // Mesh013, the high window in the end wall above room windows 3 and 4, made a window
                    // too (owner, 2026-10-01: "the window directly above 4 and 3 is the one we need to also
                    // turn into a portal window"): room window 7, paired with barn window 4, its own window.
                    RoomWindowsWithoutBackdrop = new[] { "Mesh012", "Mesh013" },
                    RoomWindowJoins = new[] { new[] { "Mesh001", "Mesh002", "Mesh003", "Mesh004", "Mesh005", "Mesh006" } },
                    PairedRoomWindows = new[] { 6, 0, 1, 2, 7 },
                    // Room windows 3 and 4, low in the end wall, have no barn window, so they look out from
                    // where the paired windows agree the room stands, which put their shared camera's glass
                    // 3.80 m outside the barn's end wall, out in the hay stacked against it: the bales slid
                    // like a magnifier and against the strafe. Moved in onto the wall plane (measured from barn
                    // window 4's glass in the same wall), live on their wall camera; owner: "That was it. It
                    // looks perfect now." (2026-10-01, _dev LOG 1001-10). That look was luckier than it seemed:
                    // the wall camera lists the barn pieces to hide on its FIRST draw (FindWallShellPieces: from
                    // 5 cm outside its glass inward), and its first draw had been with the glass 3.8 m out, so
                    // the whole end wall was hidden. On the next build, pairing Mesh013 moved the windows' start
                    // by 3 m (it joined the average), the glass began 0.75 m inside the wall, the wall was never
                    // listed, and its outer boards showed and slid past (1001-11). The offset is now measured from
                    // that build's start and puts the glass 30 cm OUTSIDE the wall line, so the boards are behind
                    // it and listed whatever the first draw sees. Nothing stands that close outside.
                    ViewOutOffsets = new[]
                    {
                        new RoomWindowViewOut { RoomWindow = 3, RightCm = 0f, UpCm = 0f, OutCm = -275f },
                        new RoomWindowViewOut { RoomWindow = 4, RightCm = 0f, UpCm = 0f, OutCm = -275f },
                    },
                    // Their camera hides the whole barn, porch included (one mesh); seeing the porch was
                    // tried live with the barn mesh cut around these windows, and the porch "basically made
                    // the windows useless for looking outside" (owner, 2026-10-01). So no porch roof, and its
                    // icicles (TRN_IcicleA/B, separate region art) hidden too, live-passed the same day. So is
                    // the loose plank leaning on that wall outside (OBJ_SchoolHousePlank5): with the wall gone
                    // from these views it stood against the glass and filled both windows from the side of the
                    // room. Owner, after all three were hidden: "I can't find a place where it looks bad."
                    ViewOutHides = new[]
                    {
                        new RoomWindowViewOutHide { RoomWindows = new[] { 3, 4 }, NamePrefixes = new[] { "TRN_Icicle", "OBJ_SchoolHousePlank" }, WithinMeters = 8f },
                    },
                    // The barn's one painted outside (GLB_GlowExt_A01, not self-lit but lit by the room's
                    // ambient at double strength) shone through the gaps round the shuttered window and the
                    // door crack as bright as day at midnight in heavy snow (screenshot 1001-15). Hidden
                    // live: "That worked. It seems like the door crack was also fixed." Its windows all have
                    // views, so it only ever showed through those gaps.
                    RoomObjectsHidden = new[] { "STR_Barn_B_GlassGlow" },
                    // Views in from outside: each starts just behind its room window's frame, so the room's
                    // frame is not in the picture and only the barn's shows ("the interior frames shown
                    // through the outside-in portal do not line up with the exterior frames"; the room
                    // stands about 5 degrees off the barn, so its frames lay twisted in the holes). The cut
                    // mode's own measurement found nothing here, because these panes carry no frame of their
                    // own: the frames are in the room's wall mesh, STR_Barn_B_interior_Prefab, read back from
                    // the card live at 6.9, 3.8, 5.2 cm into the room behind barn windows 0 to 3 (room
                    // windows 6, 0, 1, 2); window 4's (room window 7) read was spoiled by roof beams and set
                    // at 9. Each plus 0.3 cm. Owner, live: "Whatever you did now looks great." (2026-10-01.)
                    Fits = new[]
                    {
                        new WindowFit(0, 0f, 0f, 75.95f, 130.33f, -0.01f, -0f, 75.95f, 130.33f, -1, -7.2f),
                        new WindowFit(1, 0f, 0f, 75.95f, 130.33f, -0.01f, -0f, 75.95f, 130.33f, -1, -7.2f),
                        new WindowFit(2, 0f, 0f, 75.94f, 130.33f, 0f, 0f, 75.94f, 130.33f, -1, -4.1f),
                        new WindowFit(3, 0f, 0f, 75.94f, 130.33f, 0f, 0f, 75.94f, 130.33f, -1, -5.5f),
                        new WindowFit(4, 0f, 0f, 109.25f, 163.5f, 0f, 0f, 109.25f, 163.5f, -1, -9f),
                    },
                }
            },

            // The Camp Office, Mystery Lake. The two front windows' views from outside showed a sliver
            // down one side and along the bottom, only noticeable with the windows cleaned (owner,
            // 2026-09-28: "The camp office windows are slightly misaligned"): the room's frame does not
            // sit flush behind the building's. Set by his eye the same day with the view-in panel: the
            // picture a little larger than measured, and window 6's room slid 1.6 cm left and 0.4 up.
            // No position of the room alone closed it; the size did.
            // Window 0 is a different shape from the room's window behind it: the building's is 98 x 55 cm,
            // the room's 105 x 113, so the plain fit squeezed the whole room window into half the height
            // (2.0, the limit) and no size of the hole fitted both frames. Its hole stays as measured; the
            // room behind it is stretched 79% taller, slid 25.6 cm up and 2.3 left, the view starting
            // 2.5 cm out. Set by his eye, 2026-09-28 night. The same night window 6 was narrowed to 96.9 cm and its room made 2% wider. Window 4 likewise: 96.9 x 107 cm, 1 cm left, view from 9.5 cm, room 2% wider. Windows 1 and 5: holes as measured, room 1% wider, window 5's room 1.6 cm left. Every view in starts 9.5 cm out (his, the same night), except window 0's, which keeps its own 2.5. On 2026-09-30, after the upstream merge, window 1's room was made 3% taller by his eye.
            {
                "STRSPAWN_CampOffice_Prefab", new BuildingWindowOverride
                {
                    ViewFromOutsideStartsCm = 9.5f,
                    // Window 0 is short and has no bar; its room window has one. Owner, 2026-09-30.
                    DriftSillOnly = new[] { 0 },
                    // The game put a drift over window 5 and none over window 7, the same size on the
                    // opposite wall. Measured on window 5 and passed as is: "Looks perfect" (2026-09-30).
                    CopiedDrifts = new[]
                    {
                        new CopiedShellDrift { SourceObject = "TRN_SnowA_Prefab", FromWindow = 5, ToWindow = 7, AcrossCm = -3.9f, UpCm = 62.3f, OutCm = -0.9f },
                    },
                    Fits = new[]
                    {
                        new WindowFit(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, 2.5f, -2.3f, 25.6f, roomTallerPercent: 79f),
                        new WindowFit(1, 0f, 0f, 97f, 104f, -0.01f, -0.01f, 96.84f, 103.72f, -1, roomTallerPercent: 3f, roomWiderPercent: 1f),
                        new WindowFit(4, -1f, 0f, 96.9f, 107f, -0.04f, 0.05f, 96.84f, 103.72f, -1, roomWiderPercent: 2f),
                        new WindowFit(5, 0f, 0f, 97f, 104f, -0.04f, -0.01f, 96.84f, 103.72f, -1, float.NaN, -1.6f, 0f, roomWiderPercent: 1f),
                        new WindowFit(6, 0f, 0f, 96.9f, 104.5f, 0f, -0.03f, 96.83f, 103.72f, -1, float.NaN, -1.6f, 0.4f, roomWiderPercent: 2f),
                        new WindowFit(7, 0f, 0f, 98.4f, 105.1f, -0.01f, -0.01f, 96.84f, 103.72f, -1),
                    },
                }
            },

            // The Pleasant Valley farmhouse. Window 3 stands behind the kitchen cabinets and window
            // 21 has no counterpart in the room either, so a pretend view there can only be wrong.
            // Owner, 2026-09-19: "needs to be left alone ... Just use what's already there.", and of
            // the copy of its glass the mod first pasted back, "I was hoping the mod could just skip
            // these locations completely": it does, see SplitLeftAloneGlass.
            {
                "STRSPAWN_FarmHouseA_Prefab", new BuildingWindowOverride
                {
                    WindowsLeftAlone = new[] { 3, 21 },
                    ViewRoomSizePercent = 97f,
                }
            },

            // The Airfield hangar. Its glass is not one mesh on either side. In the BUILDING it is on
            // 76 renderers, and the measure's one best mesh found six triangles on one door panel
            // and no window (2026-09-20); named here, each of the eight STR_AF_Hangar_DoorBig_Prefab
            // is one window, 121 x 538 cm of glass in three panes stacked, 324 cm from one to the
            // next. In the ROOM the end wall's glass is ONE object whose mesh cannot be read, a box
            // 23.88 m x 538 cm that the size limit rightly refused; cut into 8 of 121 cm its pitch is
            // 323.9 cm, and 7 x 324 + 121 = 2389 against 2388, heights and bottoms equal to the
            // centimeter. Owner, 2026-09-20: "There are eight gigantic windows on the front that are
            // composed of three windows each ... I think we can probably just make each one a single
            // camera portal"; 2026-09-21, eight windows as the first stage: "Yes." NOT listed: the two
            // small STR_AF_Hangar_Window0_Prefab by the entry doors, until the room is looked at
            // behind them, and the two leaning side bands, which stay as the game has them. The
            // painted piece behind the side bands is switched back on in the hangar's config.
            //
            // THE SIDES CAME IN THE SAME EVENING ("I changed my mind about the higher windows, they
            // all have interior room windows counterparts"). Read from the building: the side walls
            // are stepped, not curved, and their 34 windows all stand upright, 17 a side in five
            // sizes on two tiers; the room's side sheet holds 26 quads a side, which is those 17. So
            // the building's other five window kinds were named too, and the room's side sheets took
            // their windows from the building (RoomWindowsFromBuilding). THAT WAS WRONG BY TWO
            // MEASURED FAULTS the same night (evidence 0921-27): the carry maps the shell's origin
            // 1.9 m below the room's, an offset the pairing absorbs and a carried picture does not,
            // and the measure gave both side walls one facing. And it is no longer needed: the
            // room's sheets CAN be read, from the GPU (Mesh.GetVertexBuffer + AsyncGPUReadback,
            // proven live the same night). The sides are to go through the many-pane path on
            // GPU-read vertices; until then the row names the doors only and the sides stay vanilla.
            {
                "STR_AF_Hangar_Prefab", new BuildingWindowOverride
                {
                    // THE SIDES ARE BACK ON, 2026-09-22, this time from the room's own glass rather
                    // than carried from the building. The building's five other window kinds and the
                    // room's two side sheets go in TOGETHER: a building pane with no room window
                    // behind it becomes a pretend view through the back of the room's wall.
                    ShellGlassObjects = new[]
                    {
                        "STR_AF_Hangar_DoorBig_Prefab",
                        "STR_AF_Hangar_Window0_Prefab",
                        "STR_AF_Hangar_Window1_Prefab",
                        "STR_AF_Hangar_Window2_Prefab",
                        "STR_AF_Hangar_Window3_Prefab",
                        "STR_AF_Hangar_Window4_Prefab",
                    },
                    RoomBands = new[]
                    {
                        new RoomWindowBand { Object = "STR_AF_Hangar_Interior_Main_Split_Prefab", Windows = 8, WindowWidthCm = 121f },
                    },
                    // Each side sheet is 104 vertices = 26 quads = 17 windows, the same 17 the
                    // building carries on that side, in five sizes on two tiers. Read from the GPU
                    // and cut by its own geometry, so nothing is carried and no offset is guessed.
                    RoomManyPaneObjects = new[] { "STR_AF_Hangar_Interior_Side_Windows_Prefab" },
                    // THE WHOLE FRONT ON ONE CAMERA, 2026-09-26, his rule: "The goal is to use a wall
                    // camera for the entire front face." Mirror pairs 35+0, 37+36, 38+40, 39+41.
                    ViewInGroups = new[]
                    {
                        new OutsideInHandGroup
                        {
                            Panes = new[] { 0, 35, 36, 37, 38, 39, 40, 41 },
                            HideWhileRendering = new[] { "STR_AF_Hangar_Interior_Main_Door_Prefab", "STR_AF_Hangar_Interior_Main_Windows_Prefab" },
                        },
                        // EACH SIDE ON ONE CAMERA, 2026-09-27: both rows, 13 panes, from the mean of their
                        // eyes (the furthest 3.3 cm from it on one side, 4.5 on the other, over standing
                        // spots 3 to 25 m out; the front passed at 5.7). The rules gave the sides 11 cameras
                        // and 6 panes alone, so with the front 20 were wanted for 10 slots and panes faded
                        // in and out up close (owner: "some windows still fade out/in when I'm pretty
                        // close"). Plane cameras per row were tried first and went blocky at a slant: one
                        // picture spread evenly along an 18 m row gave the pane nearest him 0.22 texture
                        // pixels per screen pixel (0.22 against 1.27 on its own camera), the 2026-09-22
                        // limit of flat pictures. This camera draws his screen, one pixel per pixel, but
                        // from his eye, so the side's own room frames would show; they are the side copy's
                        // material GLB_MetalRed_A01 and nothing else, so that material is drawn clear, and
                        // the copy's glass and glow sheets switched off, while the camera renders. Owner at
                        // pane 34: "Fixed." (LOG-2026-09-27 0927-02 to 0927-07). The eight squeezed panes
                        // (6 to 9, 14 to 17; room windows 501 and 508 wide behind 527) stay alone: their
                        // eyes stand about 27 cm apart.
                        new OutsideInHandGroup
                        {
                            Panes = new[] { 1, 2, 3, 4, 5, 10, 11, 12, 22, 23, 27, 28, 29 },
                            NearestCopyOf = "STR_AF_Hangar_Interior_Side_Prefab",
                            NearestCopyHideChildren = new[] { "STR_AF_Hangar_Interior_Side_Windows_Prefab", "STR_AF_Hangar_Interior_Side_Window_Glow_Prefab" },
                            NearestCopyClearMaterials = new[] { "GLB_MetalRed_A01" },
                        },
                        new OutsideInHandGroup
                        {
                            Panes = new[] { 13, 18, 19, 20, 21, 24, 25, 26, 30, 31, 32, 33, 34 },
                            NearestCopyOf = "STR_AF_Hangar_Interior_Side_Prefab",
                            NearestCopyHideChildren = new[] { "STR_AF_Hangar_Interior_Side_Windows_Prefab", "STR_AF_Hangar_Interior_Side_Window_Glow_Prefab" },
                            NearestCopyClearMaterials = new[] { "GLB_MetalRed_A01" },
                        },
                    },
                    // And looking out: the room's eight front door windows, its band's strips 0 to 7.
                    // Each side's 17 room windows on one camera too, 2026-09-27, in place of the rules' two
                    // per side: the furthest window 5.2 cm from the shared eye on one side, 3.0 on the
                    // other; the old cameras' eyes moved 1.4 to 3.3 cm. A/B by his eye at room window 20
                    // (the 3.3 cm group): "Almost imperceptible. Objects about 5m away move a pixel or two",
                    // then "Build B into the mod" (LOG-2026-09-27 0927-09). 3 cameras looking out, not 5.
                    ViewOutGroups = new[]
                    {
                        new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                        new[] { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 },
                        new[] { 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41 },
                    },
                    // THE 34 SIDE WINDOWS' VIEWS FROM OUTSIDE START 3.8 CM BEHIND THE ROOM'S GLASS, so the
                    // room's own side frames are never drawn and the building's framing is the framing, as
                    // it already is at the front (the doors are hidden there). Why: the room's side frames
                    // live in Side_Prefab, not under the glass sheet the mod measures, so the frame reach
                    // could not be read and every side view started 0.2 cm outside the glass, slicing the
                    // frame (its faces span -3.6 to +2.0 cm about the glass); and the room's frames are the
                    // building's MIRRORED, one side copy having scale (-1, 1, 1), so on the plain side they
                    // sat 1.1 to 5.4 cm off the building's (LOG-2026-09-26 0926-50 to 0926-56). A start
                    // 2.1 cm outside the frame and a per-window slide were tried first and he still saw
                    // the frames off ("not flush"); starting behind the frame was his own request: "can we
                    // just not show them at all and rely on the exterior framing", then "It looks good now."
                    // Judged at window 21 and along that side; the other side after the change, and other
                    // weather, not yet.
                    Fits = ViewStartsFor(1, 34, -3.8f),
                }
            },

            // Grey Mother's house in Mountain Town. Window 1 is the small wide pane, 133 x 53 cm, one of
            // the two on the building with no window in the room behind it, so all it could get was
            // a pretend view through the back of the room's wall. Owner, 2026-09-20, on first seeing
            // the house with its windows working: "Window 1 will need to be disabled." Window 5 is the
            // other one, a tall ground-floor pane on the back wall, where the room has cabinetry and
            // a trash can against a wall with no glass in it; he had not noticed it, and once told
            // what stands there: "I say blank it out like window 1." The farmhouse's window 3 again.
            //
            // WINDOW 1 CAME BACK THE SAME EVENING, as a real window. The room does have an opening
            // there, over the front door, 146 x 35 cm, with no glass in it: a hole in the wall with
            // a piece of the painted outside behind it, which the author's config switches off with
            // the rest, so from indoors it showed open sky from 40 m up with the window's number
            // floating in it. A copy of the painted piece kept drawing there looked like a lamp at
            // night (the material is self-lit) and so does the game's own; he: "Let's just turn it
            // back into a real portal. Seems easier." So the hole is read as a window, it pairs with
            // pane 1, and only window 5 is left alone.
            {
                "STR_GreyMothersHouse_Prefab", new BuildingWindowOverride
                {
                    OpeningsWithNoGlass = true,
                    WindowsLeftAlone = new[] { 5 },
                }
            },

            // THE RADIO CONTROL HUT (Pleasant Valley, 2026-10-01). Its end wall has three panes with no
            // painted outside behind them, all named here: two triangles (STR_RadioControlHutInteriorGlassA_04
            // and _06), whose views are cut to the glass's own outline (ShapedViews.cs; owner: "I really
            // wish there was a quick win for triangular windows"), and the rectangle between them, _05
            // (three panes, 3.78 x 0.47 m; owner: "There's still one rectangular window in this room").
            // A pane whose wall has no other window takes its outside from the middle of the room's glass.
            // The room's frames show in the views from outside as the default leaves them; he liked them.
            {
                "STRSPAWN_RadioControlHut_Prefab", new BuildingWindowOverride
                {
                    RoomWindowsWithoutBackdrop = new[] { "STR_RadioControlHutInteriorGlassA_05_Prefab", "STR_RadioControlHutInteriorGlassA_04_Prefab", "STR_RadioControlHutInteriorGlassA_06_Prefab" },
                    // Window 2: its outer frame lines up with the room's, but the room's center board does
                    // not match the building's divider, and the room's frames are one mesh, so no slide or
                    // stretch fixes one board (owner: "the outside frame is lined up perfectly. It's just
                    // the center vertical board"). By his frame rule, the view starts 3 cm into the room,
                    // past that board, which still leaves "a little bit of the outer frame" (his slider).
                    Fits = new[]
                    {
                        new WindowFit(2, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, -3f),
                    },
                }
            },
            // The Coastal Highway gas station, set by the owner's eye on 2026-09-20 at windows 3, 4
            // and 7 and then looked at on all eight. Its room's window is 114 x 106 cm behind a
            // building pane of 106 x 103, so the room's cross-frame cannot lie on the building's:
            // "the center vertical bar is not lined up right ... this entire interior frame has more
            // space in the middle than the exterior", and a sliver showed where the one rests on the
            // other at every start he tried. With the room's frames out of the view from outside and
            // the view starting 1.0 cm outside the room's glass: "All windows look good from outside
            // now." All of the room's window frames are one object, and the same mesh holds the
            // frames of the room's four doors (measured: what is left after the cut includes four
            // pieces 134 to 148 cm across and 252 tall), so it is named here and cut, not switched off. Hidden
            // indoors as well, the frames' casing no longer covers the rim of the hole in the wall
            // and the views out look far too small; he saw that on the prototype.
            // The Community Hall (karsontr13's, Pleasant Valley). The room's glass is ONE object, read
            // without a row as two windows ten meters wide, which hung a view panel in the middle of the
            // room and gave the building's windows the wrong views (2026-10-02). Read as panes, with the
            // front door's three panes and the back window's two halves joined, it is eight windows:
            // long wall 0, 1, 5; back 2, 6; door side 3, 4 and the door 7. The building's front door
            // glass is its own object and becomes building window 7. The room is about a third deeper
            // than the building, so one camera per wall sees from the wrong spot at the ends; the owner
            // set each wall's camera along the wall with markers outside every window: long wall
            // "as good as it's gonna get", door side "as good as it's going to get", back "lined up".
            // Those were set before the joins, which move the door side's and the back's mean.
            {
                "STR_Thompson_Store_Prefab", new BuildingWindowOverride
                {
                    // The room's window glass is ONE object, a sheet round the whole room, as at the Community
                    // Hall. Unnamed it is read as 2 windows and a view hangs in the middle of the room (owner,
                    // 2026-10-09: "Window 0 is in the center of the room again like Comm. Hall"). Named as
                    // sheet glass it gives 6 room windows, and the pane with no painted outside is the 7th.
                    RoomManyPaneObjects = new[] { "STR_RuralStoreAInteriorWindowsGlass_Prefab", "STR_RuralStoreAInteriorDoors_Prefab" },
                    RoomWindowsWithoutBackdrop = new[] { "STR_RuralStoreAInteriorWindowsGlass_Prefab", "STR_RuralStoreAInteriorDoors_Prefab" },
                    // The glass at the top of the room's two doors: a view out each, never paired, no view in.
                    RoomGlassViewOutOnly = new[] { "STR_RuralStoreAInteriorDoors_Prefab" },
                    // The three tall front windows look out through ONE camera (room windows 3, 4 and 5). Their glass
                    // is on one plane to within 0.0 cm, read live 2026-10-09, and each pairs with the building's
                    // tall window in front of it. The rules kept them apart only because the room's windows are
                    // 88 cm wide and 141 and 148 cm apart where the building's are 74 to 76. The front door's top
                    // glass is 5.0 cm further out than that plane, so it is not in the group yet: its number is
                    // only known once the door glass has been read at a launch.
                    ViewOutGroups = new[] { new[] { 3, 4, 5 } },
                    // THE TURN IS PINNED TO WHAT THE RULE GAVE FROM TWO WINDOWS, 13.7 degrees, the turn the owner
                    // judged. Naming the glass gives the rule seven windows and it then answers 0: he saw the
                    // angle go wrong at the next launch. Half a turn on, tried because the window shapes then
                    // match wall for wall, showed him sky. This room's windows do not match its building's.
                    RoomTurnDegrees = 13.748832f,
                    // The three tall front windows are NOT on a hand-made camera for the views in. They were, by
                    // the owner's choice, and the room's morning sun patch then slid over the floor as he
                    // strafed, window-frame shadows and all: a hand-made group is drawn by the screen camera,
                    // which places a spotlight's pattern wrongly. On their own cameras it holds still (live
                    // A/B, 2026-10-09: "That looks perfect"). Three cameras here until the screen camera is mended.
                    // The room's frames are NOT hidden. They slid against the building's frames as he strafed because
                    // the room is 2.6 m longer than its building along the line of sight through these windows
                    // (13.7 x 7.6 m against 11.1 x 7.6), so the room's wall does not stand at the building's. That is
                    // fitted with the view's start, not cut away (frames show by default). Hiding was tried on
                    // 2026-10-09 and removed: it cut nothing he could see.
                }
            },
            {
                "STR_CommunityHallA_Exterior_Prefab (1)", new BuildingWindowOverride
                {
                    // Every view from outside starts 10 cm into the room, past the room's frames (owner,
                    // 2026-10-02). Its window frames are one-sided, faces into the room only, so from
                    // outside their backs cannot be drawn and no fit closed the gap at the bars.
                    ViewFromOutsideStartsCm = -10f,
                    // The door, building window 7: its picture stands 1.5 cm BEHIND the glass. The building's door
                    // mesh runs from 12.5 cm in front of its glass to 3.4 cm behind (read live), and a picture at
                    // the default 1 cm in front covered the back of the door's frame. Set by the owner's eye with
                    // the rig, 2026-10-09.
                    PicturePlaneCmByWindow = new[] { float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, -1.5f },
                    // The door's glass is its own object, not on the building's frosted sheet, so it has no frost
                    // of its own: each of its four tall panes wears a piece of window 1's, one of the hall's four
                    // narrow windows (76 x 130 cm), the nearest in shape to a door pane. It first took window 0's,
                    // a wide one, laid over the whole door (owner, 2026-10-09: "Frost is wrong").
                    // Window 2: the camera in the room stands 6.5 cm higher (owner's eye, rig, 2026-10-09).
                    Fits = new[]
                    {
                        new WindowFit(2, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, float.NaN, 0f, 6.5f),
                        new WindowFit(7, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1),
                    },
                    RoomManyPaneObjects = new[] { "STR_CommunityHallA_GlassWin" },
                    RoomManyPaneJoinWithinCm = 50f,
                    ExtraShellGlassObjects = new[] { "STR_CommunityHallDoorsA_Glass" },
                    // The door (room window 7) is not in a group: sharing the door-side camera with windows 3
                    // and 4 took its plane from the two windows and sliced the frames on the door (owner,
                    // 2026-10-02 live, 2026-10-09). It looks out through its own camera.
                    ViewOutGroups = new[] { new[] { 3, 4 }, new[] { 2, 6 }, new[] { 0, 1, 5 } },
                    ViewOutGroupSidewaysCm = new[] { -5f, 31f, -12f },
                }
            },
            // Milton bank. Pane5 holds the doors and three transoms, so it is cut by its own pieces;
            // Pane4's two halves (34 cm post) are one window. Pane3 has no backdrop. 4 is a 1 cm sliver (2026-10-09).
            {
                "STR_BankA_Prefab (1)", new BuildingWindowOverride
                {
                    RoomManyPaneObjects = new[] { "STR_BankAGlassPane" },
                    RoomManyPaneJoinWithinCm = 40f,
                    RoomWindowsWithoutBackdrop = new[] { "STR_BankAGlassPane3_Prefab" },
                    WindowsLeftAlone = new[] { 4 },
                    // Under Pane3 (room window 5) the bay is boards with no glass; seen from inside the
                    // gaps showed the room's white painted glow. A view behind the boards (2-9 cm out), down to the doors' sill.
                    // 14 cm, not 10: the frost and snow stand just in front of the picture and must clear the boards.
                    RoomOpeningsByHand = new[] { new RoomHandOpening { LikeRoomWindow = 5, BottomCm = -212f, TopCm = -22f, PictureOutCm = 14f } },
                }
            },
            // Milton post office. Its room glass is two sheets (A: five windows); read whole, the room was
            // turned 90 degrees on two windows. Unturned, A pairs with the side row; the transom (1) stays out.
            {
                "STR_PostOfficeA_Prefab", new BuildingWindowOverride
                {
                    RoomManyPaneObjects = new[] { "STR_PostOfficeMainRoomWindowsAGlass_Prefab", "STR_PostOfficeMainRoomWindowsBGlass_Prefab" },
                    RoomTurnDegrees = 0f,
                    PairedRoomWindows = new[] { 6, -1, 3, 4, 1, 2, 0, 5, -1, -1, -1, -1, -1, -1 },
                    // No room behind these: their pretend views met void or the inside of a wall (2026-10-10).
                    WindowsLeftAlone = new[] { 1, 8, 9, 10, 11, 12, 13 },
                }
            },
            // FarmHouseB (Mountain Town). The rule found -46.9 degrees but counted 5 of 7 windows (it does not
            // allow for the room sitting 5 m off its building) and refused, so nothing paired. Turned, all
            // seven pair within 0.6 m and the doors meet (2026-10-10).
            {
                "STR_FarmHouseB_Prefab", new BuildingWindowOverride
                {
                    RoomTurnDegrees = -46.9f,
                }
            },
            // Orca gas station store. The room's window frames (-5 to +5 cm) showed off-line behind the
            // building's bars, so views start behind them; the door's frame reaches 19 cm in (2026-10-10).
            {
                "STR_ConvenienceStoreA_Prefab", new BuildingWindowOverride
                {
                    ViewFromOutsideStartsCm = -6f,
                    Fits = new[]
                    {
                        new WindowFit(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, -20f),
                    },
                    // Room window 0 (Pane6) glazes only 225 of its 325 cm strip; the bare third showed
                    // the white painted outside from inside. A view there, beside it.
                    // And the door's glass (room window 7) stops 1.5 cm under its frame: from below, the
                    // painted outside showed as a white strip over it. A view up behind the frame head.
                    RoomOpeningsByHand = new[]
                    {
                        new RoomHandOpening { LikeRoomWindow = 0, BottomCm = -22f, TopCm = 22f, AcrossCm = -161.5f, WidthCm = 107f, PictureOutCm = 1f },
                        // From the glass's top, 1 cm behind the door's own view, so the two never z-fight.
                        new RoomHandOpening { LikeRoomWindow = 7, BottomCm = 117f, TopCm = 130f, PictureOutCm = 1f },
                    },
                    // The door's view out starts 15 cm further out: the building's 95 cm door frame showed
                    // inside the room's 110 cm one on both sides, and the near plane now cuts it (2026-10-10).
                    ViewOutOffsets = new[] { new RoomWindowViewOut { RoomWindow = 7, OutCm = 15f } },
                }
            },
            // Milton house A. Window 7 has no room window; its pretend eye stood in the shower's wall, so
            // it is slid 40 cm right into the bathroom by the owner's eye (2026-10-10).
            {
                "STRSPAWN_HouseExteriorAMilton_Prefab", new BuildingWindowOverride
                {
                    Fits = new[]
                    {
                        // Right is counted as seen from inside looking out, as for a room window (PretendWindowHandMove).
                        new WindowFit(7, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, float.NaN, -40f, 0f),
                    },
                }
            },
            // Milton house F, all three variants. See MiltonHouseFRow.
            { "STRSPAWN_HouseExteriorFMilton_Prefab_1", MiltonHouseFRow() },
            { "STRSPAWN_HouseExteriorFMilton_Prefab_2", MiltonHouseFRow() },
            { "STRSPAWN_HouseExteriorFMilton_Prefab_3", MiltonHouseFRow() },
            // Milton house D. Windows 3 (tall) and 6 (wide, above it) took each other's room window; and
            // the room's 56 x 105 panes were squeezed 1.19x into the 57 x 88 holes. Owner passed both (2026-10-10).
            {
                "STRSPAWN_HouseExteriorDMilton_Prefab", new BuildingWindowOverride
                {
                    PairedRoomWindows = new[] { 3, 2, 1, 4, 5, 0, 6 },
                    Fits = new[]
                    {
                        new WindowFit(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 19f),
                        new WindowFit(1, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 19f),
                        new WindowFit(2, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 19f),
                        new WindowFit(3, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 19f),
                        new WindowFit(4, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 19f),
                        new WindowFit(5, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 19f),
                        new WindowFit(6, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomWiderPercent: 19f),
                    },
                }
            },
            // Milton house H. Room windows 66 x 137 behind 45 x 111; the room's frames (-5 to +6 cm)
            // showed off-line, so views from outside start behind them (owner, 2026-10-09).
            {
                "STRSPAWN_HouseExteriorHMilton_Prefab (3)", new BuildingWindowOverride
                {
                    ViewFromOutsideStartsCm = -6f,
                }
            },
            // Coastal house C. Its north-wall window has no glass object in the room (frame + glow only):
            // a hand opening 699 cm left of room window 4, paired with building window 3 (owner, 2026-10-10).
            {
                "STRSPAWN_HouseExteriorC_Prefab", new BuildingWindowOverride
                {
                    RoomOpeningsByHand = new[] { new RoomHandOpening { LikeRoomWindow = 4, BottomCm = -52.1f, TopCm = 62f, AcrossCm = -699f, WidthCm = 58f } },
                    PairedRoomWindows = new[] { 2, 1, 0, 6, 4, 3, 5 },
                }
            },
            // Coastal house D. The rule paired building window 2 with the upstairs room window and 6 with a
            // ground-floor one; swapped back by hand (owner, 2026-10-10).
            {
                "STRSPAWN_HouseExteriorD_Prefab", new BuildingWindowOverride
                {
                    PairedRoomWindows = new[] { 1, 0, 4, 3, 2, 5, 6 },
                }
            },
            // Lighthouse. Only the lantern glass was measured; the tower (6) and ground (2) windows are prefabs
            // of their own. With them the room turns +10.65 (the rule found -11.2 from the lantern), 2026-10-10.
            {
                "STRSPAWN_LighthouseExt_Prefab", new BuildingWindowOverride
                {
                    ExtraShellGlassObjects = new[] { "STR_LighthouseWindowExtA_Prefab", "STR_LighthouseWindowExtB_Prefab" },
                    RoomTurnDegrees = 10.65f,
                    // The two ground-floor room windows carry their glass in slot 1 beside the frame.
                    RoomGlassSlotObjects = new[] { "STR_LighthouseWindowInteriorA_Prefab" },
                }
            },
            // Whaling warehouse. Room glass wears STR_DamControlRoomGlass, so the rule found no window. Panes
            // 13 and 14 sit in the office's inner walls and are left out (owner, 2026-10-10).
            {
                "STR_WarehouseA_Prefab", new BuildingWindowOverride
                {
                    RoomGlassSlotObjects = new[]
                    {
                        "STR_WarehouseWindowPane1_Prefab", "STR_WarehouseWindowPane2_Prefab", "STR_WarehouseWindowPane3_Prefab",
                        "STR_WarehouseWindowPane4_Prefab", "STR_WarehouseWindowPane5_Prefab", "STR_WarehouseWindowPane6_Prefab",
                        "STR_WarehouseWindowPane7_Prefab", "STR_WarehouseWindowPane8_Prefab", "STR_WarehouseWindowPane9_Prefab",
                        "STR_WarehouseWindowPane10_Prefab", "STR_WarehouseWindowPane11_Prefab", "STR_WarehouseWindowPane12_Prefab",
                    },
                }
            },
            // Broken Railroad maintenance shed. The building's two windows are the garage doors' pane strips,
            // glass in the room's one big mesh beside its roof lights (2026-10-10).
            {
                "STR_MaintenanceShedA_Prefab", new BuildingWindowOverride
                {
                    RoomGlassSlotObjects = new[] { "STR_MaintenanceShedAMetal_Prefab" },
                    // Door 2's end pane has no glass in the room: a hole beside room window 1.
                    RoomOpeningsByHand = new[] { new RoomHandOpening { LikeRoomWindow = 1, BottomCm = -30f, TopCm = 30f, AcrossCm = -184f, WidthCm = 84f } },
                }
            },
            // Bleak Inlet maintenance shed B. The windows are in the room's one big mesh; its office glass
            // (STR_MaintenanceShedBInt_Glass) is an inner window and stays out (2026-10-10).
            {
                "STR_MaintenanceShedB_Prefab", new BuildingWindowOverride
                {
                    RoomGlassSlotObjects = new[] { "STR_MaintenanceShedBInt_Base_Prefab" },
                    RoomGlassKeepsOwnMaterial = true,
                }
            },
            // Blackrock power plant. Its panes all hang under one parent on three walls, which the rule reads as a
            // house and skips. The door's two panes are one glass object on both sides, 0.8 m apart (2026-10-10).
            {
                "STR_Powerplant_Ext_Prefab", new BuildingWindowOverride
                {
                    RoomManyPaneObjects = new[] { "STR_Powerplant_IntWindow00" },
                    RoomGlassSlotObjects = new[] { "STR_MetalDoorExt_C_Double_Window" },
                    ExtraShellGlassObjects = new[] { "STR_MetalDoorExt_C_Double_Window" },
                    GlassSlotJoinWithinCm = 40f,
                }
            },
            // Sundered Pass radar station: all room windows in one unreadable glass object, which the rule
            // never read (0 of the building's 7). InteriorWindowGlass_B is inner office glass (2026-10-10).
            {
                "STR_RadarStation_Prefab", new BuildingWindowOverride
                {
                    RoomManyPaneObjects = new[] { "STR_RadarStation_InteriorWindowGlass_A" },
                }
            },
            // Mountain Town church: both side walls' windows are one glass object, read as two 5.75 m windows
            // whose view stood in the middle of the nave (2026-10-12). Read pane by pane instead.
            {
                "STR_ChurchB_Prefab", new BuildingWindowOverride
                {
                    RoomManyPaneObjects = new[] { "STR_ChurchBGlass_Prefab" },
                }
            },
            // Zone of Contamination pump house: all room windows in one unreadable glass object the rule never
            // read (0 of the building's 5) (2026-10-10).
            {
                "STR_PumpHouse_Ext_Prefab", new BuildingWindowOverride
                {
                    RoomManyPaneObjects = new[] { "PumpHouse_InteriorWindowGlass" },
                }
            },
            // Zone of Contamination mine: the concentrator, conveyor building and headframe are one room, and
            // one shell group (Mine_Buildings, see IsShellGroup). Their glass is on six objects (2026-10-10).
            {
                "Mine_Buildings", new BuildingWindowOverride
                {
                    ShellGlassObjects = new[] { "OBJ_MineBuildingWindow_Ext_LOD0", "STR_ConveyorBuilding_Ext_Windows",
                        "STR_ConveyorBuilding_Ext_Connector_A_Windows", "STR_ConveyorBuilding_Ext_Connector_B_Windows",
                        "STR_ConveyorBuilding_Ext_Connector_C_Windows", "STR_Headframe_Ext_Windows" },
                    GlassSlotJoinWithinCm = 30f,
                    RoomManyPaneObjects = new[] { "STR_MineConcentratorWindow_Int_" },
                    RoomWindowsWithoutBackdrop = new[] { "STR_MineConcentratorWindow_Int_A_LOD0", "STR_MineConcentratorWindow_Int_E_LOD0" },
                    RoomGlassSlotObjects = new[] { "STR_ConveyorBuilding_Int_Connector_" },
                    RoomTurnDegrees = 0f,
                    PairInPlaceWithinCm = 550f,
                    // No room behind these: the garage door's strip and the concentrator's top floor are wall inside.
                    WindowsLeftAlone = new[] { 15, 17, 18, 19, 23, 24 },
                }
            },
            // Hunting Lodge: every room window is in one glass object, read as 2 windows of 16 m and the room
            // left unturned. Cut by its own geometry it gives the building's 16 (2026-10-10).
            {
                "STR_HuntingLodgeA_Prefab", new BuildingWindowOverride
                {
                    RoomManyPaneObjects = new[] { "STR_HuntingLodgeAIntGlass_Prefab" },
                }
            },
            {
                "STRSPAWN_QuonsetGasStation_Prefab", new BuildingWindowOverride
                {
                    ViewFromOutsideStartsCm = 1.0f,
                    RoomFramesHiddenFromOutside = "STR_GarageWindowsDoors_Prefab",
                    // The ground drift against its windows, the reason glass snow exists (2026-09-28).
                    GlassSnow = true,
                }
            },
        };

        // Milton house F: the room does not match its building. The rule turned it 180 degrees on two
        // windows, its door 12 m from the building's; unturned the doors meet (0.6 m). The two windows
        // beside the door then have no room window, and the rest were squeezed (owner, 2026-10-10).
        private static BuildingWindowOverride MiltonHouseFRow()
        {
            return new BuildingWindowOverride
            {
                RoomTurnDegrees = 0f,
                WindowsLeftAlone = new[] { 1, 2 },
                Fits = new[]
                {
                    new WindowFit(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 7f, roomWiderPercent: 12f),
                    new WindowFit(3, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 28f, roomWiderPercent: 21f),
                    new WindowFit(4, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 26f, roomWiderPercent: 2f),
                    new WindowFit(5, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 26f, roomWiderPercent: 2f),
                    new WindowFit(6, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, roomTallerPercent: 23f, roomWiderPercent: 13f),
                },
            };
        }

        // One neutral fit per window in [first, last] that only sets where its view from outside starts:
        // sizes 0 (as measured), frost 0 (as built), FrostSource -1 (the mod decides), no room slide.
        private static WindowFit[] ViewStartsFor(int first, int last, float viewStartCm)
        {
            var fits = new WindowFit[last - first + 1];
            for (int w = first; w <= last; w++) fits[w - first] = new WindowFit(w, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, viewStartCm);
            return fits;
        }

        internal static bool TryGetBuildingWindowOverride(string shellName, out BuildingWindowOverride row)
        {
            row = null;
            if (string.IsNullOrEmpty(shellName)) return false;
            return s_BuildingWindowOverrides.TryGetValue(shellName, out row) && row != null;
        }
    }
}
