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
            // 2.5 cm out. Set by his eye, 2026-09-28 night. The same night window 6 was narrowed to 96.9 cm and its room made 2% wider. Window 4 likewise: 96.9 x 107 cm, 1 cm left, view from 9.5 cm, room 2% wider. Windows 1 and 5: holes as measured, room 1% wider, window 5's room 1.6 cm left. Every view in starts 9.5 cm out (his, the same night), except window 0's, which keeps its own 2.5.
            {
                "STRSPAWN_CampOffice_Prefab", new BuildingWindowOverride
                {
                    ViewFromOutsideStartsCm = 9.5f,
                    Fits = new[]
                    {
                        new WindowFit(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1, 2.5f, -2.3f, 25.6f, roomTallerPercent: 79f),
                        new WindowFit(1, 0f, 0f, 97f, 104f, -0.01f, -0.01f, 96.84f, 103.72f, -1, roomWiderPercent: 1f),
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
            {
                "STRSPAWN_QuonsetGasStation_Prefab", new BuildingWindowOverride
                {
                    ViewFromOutsideStartsCm = 1.0f,
                    RoomFramesHiddenFromOutside = "STR_GarageWindowsDoors_Prefab",
                }
            },
        };

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
