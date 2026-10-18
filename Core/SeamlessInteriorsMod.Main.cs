using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[assembly: MelonInfo(typeof(SeamlessInteriors.SeamlessInteriorsMod), "SeamlessInteriors", "2.3.1", "Hamsi Buglama")]
[assembly: MelonGame("Hinterland", "TheLongDark")]
// The in-game settings page needs ModSettings; without it the mod runs on its preferences file alone.
[assembly: MelonOptionalDependencies("ModSettings")]

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod : MelonMod
    {
        // ─── INTERIOR LIGHTING MODE OPTION ───
        // 0 = Outdoor (lit by the outside world - the current default)
        // 1 = Dark Atmosphere (the original dark interior atmosphere)
        private static MelonPreferences_Category s_PrefCategory;
        private static MelonPreferences_Entry<int> s_PrefInteriorLightingMode;

        // Should the clone scene's initial loot roll (RandomSpawnObject) be performed?
        // Can be turned off for troubleshooting: off, the mod falls back to the old
        // behaviour of never rolling (every candidate item in the scene stays enabled).
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] EnableInitialLootRoll
        private static MelonPreferences_Entry<bool> s_PrefEnableInitialLootRoll;

        public static bool IsInitialLootRollEnabled => s_PrefEnableInitialLootRoll?.Value ?? true;

        // Gear save filter: should container contents, looted items and eliminated loot
        // candidates be kept out of the gear JSON? Can be turned off for troubleshooting.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] EnableGearSaveFilter
        private static MelonPreferences_Entry<bool> s_PrefEnableGearSaveFilter;

        public static bool IsGearSaveFilterEnabled => s_PrefEnableGearSaveFilter?.Value ?? true;

        // Should a save made BEFORE the mod was installed have its interior history
        // imported? See SeamlessInteriorsMod.LegacyImport.cs. Runs at most once per
        // building per save; the untouched original is kept as a backup either way.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] EnableLegacySaveImport
        private static MelonPreferences_Entry<bool> s_PrefEnableLegacySaveImport;

        public static bool IsLegacySaveImportEnabled => s_PrefEnableLegacySaveImport?.Value ?? true;

        // Should the mod check that the data on disk belongs to the playthrough that is
        // actually loaded? The game reuses the slot names of deleted saves, so without
        // this a new game inherits the previous one's interiors.
        // See SeamlessInteriorsMod.SaveIdentity.cs.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] EnableSaveIdentityGuard
        private static MelonPreferences_Entry<bool> s_PrefEnableSaveIdentityGuard;

        public static bool IsSaveIdentityGuardEnabled => s_PrefEnableSaveIdentityGuard?.Value ?? true;

        // Should the mod's own files for a save be removed when that save is deleted?
        // The guard above cleans up AFTER the fact, once the next game has already been
        // handed the deleted save's slot name; this stops the leftovers from existing in
        // the first place. See SeamlessInteriorsMod.SaveDeletion.cs.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] DeleteDataWithSave
        private static MelonPreferences_Entry<bool> s_PrefDeleteDataWithSave;

        public static bool IsDeleteDataWithSaveEnabled => s_PrefDeleteDataWithSave?.Value ?? true;

        // Should a building's contents be restored only when the player comes near,
        // instead of for every building during the region load?
        // See SeamlessInteriorsMod.Hydration.cs.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] EnableLazyInteriorContent
        private static MelonPreferences_Entry<bool> s_PrefEnableLazyInteriorContent;

        public static bool IsLazyContentEnabled => s_PrefEnableLazyInteriorContent?.Value ?? true;

        // Should the import switch off scene furniture the game's own save lists as
        // "Removed"? On, an interior the player stripped comes back stripped. Off, every
        // piece of the original furniture stays - which is the safer direction if the
        // import ever reads that flag wrong, because a wrongly hidden object looks exactly
        // like the player's belongings having vanished.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] ApplyLegacyRemovedObjects
        private static MelonPreferences_Entry<bool> s_PrefApplyLegacyRemovedObjects;

        public static bool IsLegacyRemovedHandlingEnabled => s_PrefApplyLegacyRemovedObjects?.Value ?? true;

        // Exterior fire effects: smoke from the building's chimney and, between dusk and dawn, a
        // warm glow in its windows and on the snow in front of them while a fire burns inside.
        // See SeamlessInteriorsMod.ExteriorFireEffects.cs.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] EnableChimneySmoke, EnableWindowGlow,
        // WindowGlowFlicker, WindowGlowStrength, EnableWindowGroundGlow, WindowGroundGlowStrength
        private static MelonPreferences_Entry<bool> s_PrefEnableChimneySmoke;
        private static MelonPreferences_Entry<bool> s_PrefEnableWindowGlow;
        private static MelonPreferences_Entry<bool> s_PrefWindowGlowFlicker;
        private static MelonPreferences_Entry<float> s_PrefWindowGlowStrength;
        private static MelonPreferences_Entry<bool> s_PrefEnableWindowGroundGlow;
        private static MelonPreferences_Entry<float> s_PrefWindowGroundGlowStrength;

        private static MelonPreferences_Entry<bool> s_PrefEnableWindowPortals;
        private static MelonPreferences_Entry<bool> s_PrefWindowFrostSnowBuildUp;
        private static MelonPreferences_Entry<int> s_PrefWindowPortalLimit;
        private static MelonPreferences_Entry<float> s_PrefWindowPortalResolutionScale;
        private static MelonPreferences_Entry<bool> s_PrefClearWindowPanes;
        private static MelonPreferences_Entry<bool> s_PrefCleanWindowsByHand;
        private static MelonPreferences_Entry<KeyCode> s_PrefDoorWindowCleanKey;

        public static bool IsChimneySmokeEnabled => s_PrefEnableChimneySmoke?.Value ?? true;
        public static bool IsWindowGlowEnabled => s_PrefEnableWindowGlow?.Value ?? true;
        public static bool IsWindowGlowFlickerEnabled => s_PrefWindowGlowFlicker?.Value ?? true;
        public static float WindowGlowStrength => s_PrefWindowGlowStrength?.Value ?? 1f;
        public static bool IsWindowGroundGlowEnabled => s_PrefEnableWindowGroundGlow?.Value ?? true;
        public static float WindowGroundGlowStrength => s_PrefWindowGroundGlowStrength?.Value ?? 1f;

        // VERBOSE LOGGING.
        //
        // PERFORMANCE: with this flag on, the mod writes a line per object on every
        // save/load step (e.g. [GEAR-TEST] for EVERY item in a building). Because
        // MelonLogger writes SYNCHRONOUSLY to the console and to file, and each line
        // allocates an interpolated string, on a 15-building map this alone accounted for
        // a large part of the stall while saving.
        //
        // It therefore defaults to OFF. While troubleshooting:
        //   UserData\MelonPreferences.cfg -> [SeamlessInteriors] VerboseLogging = true
        // or F7 in game.
        private static MelonPreferences_Entry<bool> s_PrefVerboseLogging;

        // Should the door skip switching the renderers and colliders of the building's own
        // contents off and on again? Everything inside a clone is a child of MasterInterior,
        // and MasterInterior is deactivated the moment the player steps out - which hides
        // the whole subtree by itself. Doing it a second time, object by object, is what
        // made a door transition cost most of a second on a full base.
        // See SetInteriorItemsVisibleCore.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] FastInteriorToggle
        private static MelonPreferences_Entry<bool> s_PrefFastInteriorToggle;

        public static bool IsFastInteriorToggleEnabled => s_PrefFastInteriorToggle?.Value ?? true;
        private static MelonPreferences_Entry<bool> s_PrefDoorFade;
        public static bool IsDoorFadeEnabled => s_PrefDoorFade?.Value ?? true;

        // PERFORMANCE REPORT. Writes a summary to the log every 5 seconds: frame time, what
        // the mod spends it on, and which object searches cost what against how full each
        // building is. Cheap enough to leave on (one branch per measured call while off),
        // but it does write to the log, so it defaults to OFF.
        //
        // This is the switch to ask a player to turn on when they report frame drops.
        // Shift+F6 toggles the same thing in game and writes the choice back here.
        // UserData\MelonPreferences.cfg -> [SeamlessInteriors] EnablePerformanceReport
        private static MelonPreferences_Entry<bool> s_PrefEnablePerfProbe;

        public static int InteriorLightingMode => s_PrefInteriorLightingMode?.Value ?? 0;

        public static bool IsDarkAtmosphereMode => InteriorLightingMode == 1;

        public override void OnInitializeMelon()
        {
            s_PrefCategory = MelonPreferences.CreateCategory("SeamlessInteriors", "Seamless Interiors Settings");
            s_PrefInteriorLightingMode = s_PrefCategory.CreateEntry<int>(
                "InteriorLightingMode",
                0,
                "Interior Lighting Mode",
                "0 = Outdoor Lighting (bright, exterior-synced) | 1 = Dark Atmosphere (original indoor ambiance)"
            );

            s_PrefEnableInitialLootRoll = s_PrefCategory.CreateEntry<bool>(
                "EnableInitialLootRoll",
                true,
                "Enable Initial Loot Roll",
                "Runs the game's own RandomSpawnObject elimination once per save so cloned interiors get the correct amount of loot. Turn off only for troubleshooting."
            );

            s_PrefEnableGearSaveFilter = s_PrefCategory.CreateEntry<bool>(
                "EnableGearSaveFilter",
                true,
                "Enable Gear Save Filter",
                "Keeps container contents, looted items and eliminated loot candidates out of the cloned-interior gear save file. Turn off only for troubleshooting."
            );

            s_PrefEnableLegacySaveImport = s_PrefCategory.CreateEntry<bool>(
                "EnableLegacySaveImport",
                true,
                "Import Pre-Mod Save Data",
                "Carries what the player did in the ORIGINAL interior scenes (items, containers, harvested objects, placed furniture, cleared junk) into the cloned interior the first time an existing save is loaded with the mod. Runs once per building per save."
            );

            s_PrefEnableSaveIdentityGuard = s_PrefCategory.CreateEntry<bool>(
                "EnableSaveIdentityGuard",
                true,
                "Guard Against Another Game's Interior Data",
                "Stamps every save with an id of its own and refuses interior data carrying a different one. The game reuses the slot names of deleted saves, so without this a new game can inherit the previous one's furniture, containers and harvested objects. Mismatched data is moved to Mods\\SeamlessInteriorsData\\BayatVeri, never deleted."
            );

            s_PrefDeleteDataWithSave = s_PrefCategory.CreateEntry<bool>(
                "DeleteDataWithSave",
                true,
                "Delete Interior Data With The Save",
                "Removes this mod's files and loot flags for a save the moment that save is deleted, and clears out data left behind by saves that no longer exist. The game reuses the slot names of deleted saves, so leftovers would otherwise be handed to the next new game. Turn off to keep every file forever - the Save Identity Guard above then quarantines the leftovers instead."
            );

            s_PrefEnableLazyInteriorContent = s_PrefCategory.CreateEntry<bool>(
                "EnableLazyInteriorContent",
                true,
                "Lazy Interior Content",
                "Restores a building's items and container contents only when the player comes near it, instead of filling every building in the region during the loading screen. Turn off to go back to the old, eager behaviour."
            );

            s_PrefApplyLegacyRemovedObjects = s_PrefCategory.CreateEntry<bool>(
                "ApplyLegacyRemovedObjects",
                true,
                "Apply Removed Objects From Pre-Mod Save",
                "Switches off scene furniture the pre-mod save records as removed, so a stripped interior is imported stripped. Turn off if furniture that should be there is coming in dark."
            );

            s_PrefEnableChimneySmoke = s_PrefCategory.CreateEntry<bool>(
                "EnableChimneySmoke",
                true,
                "Chimney Smoke",
                "Smoke rises from a building's chimney for as long as a stove or fireplace burns inside it - the game's own chimney effect, which the seamless doors would otherwise never trigger."
            );

            s_PrefEnableWindowGlow = s_PrefCategory.CreateEntry<bool>(
                "EnableWindowGlow",
                true,
                "Window Glow At Night",
                "While a fire burns inside, the building's windows glow warm orange from dusk to dawn, the way Grey Mother's house looks in Wintermute. Never in daylight."
            );

            s_PrefWindowGlowFlicker = s_PrefCategory.CreateEntry<bool>(
                "WindowGlowFlicker",
                true,
                "Window Glow Flicker",
                "Adds a very slight firelight flicker to the window glow. Turn off for a steady glow."
            );

            s_PrefWindowGlowStrength = s_PrefCategory.CreateEntry<float>(
                "WindowGlowStrength",
                1f,
                "Window Glow Strength",
                "Brightness multiplier for the window glow. 1 = the value Wintermute uses."
            );

            s_PrefEnableWindowGroundGlow = s_PrefCategory.CreateEntry<bool>(
                "EnableWindowGroundGlow",
                true,
                "Window Glow On The Ground",
                "Lit windows also throw a soft orange light onto the snow in front of the ground floor. Uses a few real-time lights per building, only while you are nearby."
            );

            s_PrefWindowGroundGlowStrength = s_PrefCategory.CreateEntry<float>(
                "WindowGroundGlowStrength",
                1f,
                "Window Ground Glow Strength",
                "Brightness multiplier for the light the windows throw onto the ground."
            );

            s_PrefEnableWindowPortals = s_PrefCategory.CreateEntry<bool>(
                "EnableWindowPortals",
                true,
                "See Through Windows",
                "Windows show the real view from where the building stands, instead of a painted backdrop. Costs one extra render of the world for each window you can actually see at that moment."
            );

            // Owner, 2026-10-17: off brings back the game's own frosted panes; a cleaned pane still gets the clean glass.
            s_PrefWindowFrostSnowBuildUp = s_PrefCategory.CreateEntry<bool>(
                "WindowFrostSnowBuildUp",
                true,
                "Frost And Snow Build Up On Windows",
                "On: frost grows back over a cleaned window and snow piles up against it while it snows. Off: windows wear the game's own painted frost until cleaned, and a cleaned window frosts over again all at once."
            );

            // NEW KEY on purpose, replacing "MaxWindowPortals". That entry's 4 was never a cost
            // budget: under the original shared-atlas design the value WAS the atlas grid, via
            // tilesPerSide = CeilToInt(Sqrt(value)), so 4 meant a 2x2 atlas and raising it split
            // one texture into more, smaller tiles and made every window blurrier. The atlas was
            // replaced by per-slot targets sized to each window's on-screen footprint, which
            // deleted that justification entirely - cost now scales with the windows actually
            // visible, and five simultaneous portals measure about 4 MB of target - but the 4 was
            // carried across unexamined. It is far too low: the Camp Office alone has 8 windows
            // and routinely has 5 visible, so one was always denied and fell back to the painted
            // plate, visibly popping as the player turned.
            //
            // Renaming rather than raising the default is the whole point. MelonPreferences only
            // applies a default when the key is ABSENT, so every existing preferences file would
            // have kept its 4 and nobody would have seen the fix. This is the same mistake that
            // WindowPortalTileSize made below; a setting whose meaning or scale changes needs a
            // new name, not a new default.
            s_PrefWindowPortalLimit = s_PrefCategory.CreateEntry<int>(
                "WindowPortalLimit",
                10,     // 8 until 2026-09-19: the Pleasant Valley farmhouse shows more than 8 at once. An existing file keeps its own number.
                "Windows Drawn At Once",
                "Upper limit on how many windows may show the real view in the same frame. Only the windows you can actually see are ever drawn, so this is a safety cap rather than a cost you pay. Lower it to reclaim frame rate in a building with many windows."
            );

            // On by default: the wall cameras replace the per-window cameras in parked rooms (owner,
            // 2026-09-23). Off brings the per-window cameras back at once, for the frame-time
            // comparison; see WallCameras.cs.
            s_PrefWindowWallCameras = s_PrefCategory.CreateEntry<bool>(
                "WindowWallCameras",
                true,
                "One Camera Per Wall",
                "In buildings whose room is kept away from its shell, draw all the windows on one wall with one camera instead of one camera per window. Off uses one camera per window."
            );

            // The same for the views INTO a room from outside (owner, 2026-09-26); see
            // OutsideInWallCameras.cs. Off brings the per-pane cameras back at once.
            s_PrefOutsideInWallCameras = s_PrefCategory.CreateEntry<bool>(
                "OutsideInWallCameras",
                true,
                "One Camera Per Wall From Outside",
                "Looking into a building from outside, draw all the windows on one wall with one camera instead of one camera per window, so more of them show the room at once. Off uses one camera per window."
            );

            // NEW KEY on purpose. This replaced a "WindowPortalTileSize" pixel count, and raising
            // that entry's default would have changed nothing for anyone who had already run the
            // mod: MelonPreferences only applies a default when the key is absent, so every
            // existing preferences file kept the old value. A setting whose meaning or scale
            // changes needs a new name, not a new default.
            s_PrefWindowPortalResolutionScale = s_PrefCategory.CreateEntry<float>(
                "WindowPortalResolutionScale",
                1f,
                "Window View Sharpness",
                "Resolution of the view through a window, as a fraction of the game's own. 1 matches the screen exactly, so a window looks as sharp as standing in the doorway next to it. Lower it to reclaim frame rate."
            );

            // FROSTED BY DEFAULT, under a NEW key. "ClearWindowPanes" defaulted to true, which was a
            // working convenience while the portal rendering behind the glass was being fixed, not
            // a decision about how the mod should look (owner, 2026-09-18). MelonPreferences applies
            // a default only where the key is absent, so every file that ever ran the old build
            // holds "ClearWindowPanes = true" and would have kept it for ever. The old key is simply
            // no longer read. A window is clear once something has cleaned it, and that is recorded
            // per window per save (see WindowFrostState.cs), not here.
            s_PrefClearWindowPanes = s_PrefCategory.CreateEntry<bool>(
                "WindowsStartCleaned",
                false,
                "Windows Start Cleaned",
                "Off, the default: windows are frosted over and carry a snow drift, as in the base game, until they are cleaned. On: every window nobody has touched starts clean, from both sides."
            );

            // The player's own way to clean one window (WindowClearing.cs). New key, 2026-10-01.
            s_PrefCleanWindowsByHand = s_PrefCategory.CreateEntry<bool>(
                "CleanWindowsByHand",
                true,
                "Clean Windows By Hand",
                "Look at a frosted window up close and hold the interact button to scrape it clear. A cleaned window is clean from inside and outside, is kept with the save, and frosts over again after a while (see Windows Frost Over Again After)."
            );

            // A door's own collider covers its window (WindowClearing.cs). New key, 2026-10-18.
            s_PrefDoorWindowCleanKey = s_PrefCategory.CreateEntry<KeyCode>(
                "DoorWindowCleanKey",
                KeyCode.Z,
                "Door Window Cleaning Key",
                "A door's window cannot be cleaned while the door takes the interact button. This key switches the doors with a window to cleaning; press it again to use the doors."
            );

            // How long a cleaned window stays clean (WindowFrostState.cs). New key, 2026-10-10.
            s_PrefWindowFrostReturnHours = s_PrefCategory.CreateEntry<float>(
                "WindowFrostReturnHours",
                48f,
                "Windows Frost Over Again After",
                "Game hours until a cleaned window frosts over again. A fire burning in the building holds the count at zero. Heavy snow and blizzards shorten it. 0: a cleaned window stays clean for ever."
            );

            // How much faster the frost returns in heavy weather. New keys, 2026-10-10. Not on the
            // settings page: tuned by the owner, like the frost strengths.
            s_PrefWindowFrostHeavySnowRate = s_PrefCategory.CreateEntry<float>(
                "WindowFrostHeavySnowRate",
                2f,
                "Window Frost Rate In Heavy Snow",
                "How many times faster a cleaned window frosts over again while it is snowing heavily. 1: no faster."
            );
            s_PrefWindowFrostBlizzardRate = s_PrefCategory.CreateEntry<float>(
                "WindowFrostBlizzardRate",
                4f,
                "Window Frost Rate In A Blizzard",
                "How many times faster a cleaned window frosts over again during a blizzard. 1: no faster."
            );

            // ─── Window tuning. These started life as live dials and are kept as preferences so the
            // values chosen by eye are not lost at exit. New keys, so no existing file holds a stale
            // value for any of them (MelonPreferences applies a default only when the key is absent).
            //
            // THE DEFAULTS ARE THE OWNER'S OWN SETTINGS, read out of the running game on 2026-09-18
            // at the Camp Office once the interior's snow slab was reliably hidden from the view in:
            // set back 0, drift offset -6.25 cm, overlap 0, frost 50% outside and 100% inside,
            // room visible within 50 m. The defaults were changed in place rather than under new
            // keys because these keys have never shipped, so the only file holding them is the
            // owner's, which already holds these values. After a release that stops being true.
            //
            // FOUR OF THEM ENDED AT ZERO AND ARE NO LONGER PREFERENCES (owner, 2026-09-18): the window set
            // back, the drift's overlap into the frame, and two view dials that lived for a day. The
            // keys WindowRecess, WindowDriftOverlap, WindowViewOffsetUp and WindowViewStartDepth may
            // linger in the owner's file, where nothing reads them.
            //
            // THE DRIFT OFFSET AND THE SASH BAR. Windows 1 to 7 have a horizontal sash bar standing
            // 3.2 cm proud of the glass, inside a frame standing 4.0 cm proud. At -6.25 the drift's
            // face is about 2.9 cm proud, behind the bar, so the wood shows, and that is the owner's
            // choice ("it looks okay"). A -4.75 that was briefly made the default the same day was an
            // accident: the dial had been moved while the drifts were hidden by cleaned windows, so
            // nobody was looking at what it did. The frame face at 4.0 cm is the hard limit.
            s_PrefOutsideInFrostedPane = s_PrefCategory.CreateEntry<bool>(
                "WindowFrostOutside", true, "Painted Frost Outside",
                "Draw the game's painted window frost over the view into the room.");
            s_PrefOutsideInFrostStrength = s_PrefCategory.CreateEntry<float>(
                // NEW KEY on purpose, for the reason given at WindowPortalLimit: this was
                // "WindowFrostStrengthOutside" at 0.5, for the room's sheet drawn as painted. The frost
                // is now the building's own sheet, darkened and with its contrast raised, and was tuned
                // by eye at full strength (owner, 2026-10-10); a file holding the old 0.5 would show
                // half of that.
                "WindowFrostStrengthFromOutside", 1f, "Frost Strength Outside",
                "0 to 1. How solid the frosted pane is drawn over the view into a room. 1 is the building's own pane as tuned.");
            // NEW KEY on purpose, for the reason given above: raised from 50 to 100 m (owner, 2026-09-26,
            // at the hangar, where the views switched off while he could still see into it), and an
            // existing file would have kept 50 under the old name "WindowInteriorRange".
            s_PrefOutsideInPortalRange = s_PrefCategory.CreateEntry<float>(
                "WindowInteriorRangeMeters", 100f, "Interior Visible Through Windows Within",
                "From outside, how near a building must be, in meters, before its windows show the real room. Past this, and 5 m more once showing, a window keeps its shape, frost and drift and shows dark glass.");
            SeamlessInteriorsLog.s_PrefLogLanguage = s_PrefCategory.CreateEntry<string>(
                "LogLanguage", "auto", "Log Language",
                "The language of this mod's lines in the MelonLoader log: auto, tr or en. auto is Turkish on a computer set to Turkish and English everywhere else.");
            s_PrefOutsideInMaxHeldRooms = s_PrefCategory.CreateEntry<int>(
                "WindowInteriorRoomsHeld", 3, "Rooms Kept Ready For Windows",
                "From outside, how many nearby buildings keep their rooms switched on at once so their windows can show them, 1 to 6. Buildings standing close together stop trading places as you walk between them. Lower it on a slow machine; 1 is one building at a time.");
            s_PrefInsideOutFrostStrength = s_PrefCategory.CreateEntry<float>(
                "WindowFrostStrengthInside", 1f, "Frost Strength Inside",
                "0 to 1. Looking out at a bright snowfield, the same frost needs full strength to read.");
            s_PrefInsideOutShowSnowSlab = s_PrefCategory.CreateEntry<bool>(
                "WindowSnowDrift", true, "Snow Drift In Window Frames",
                "Show the drift of snow sitting in a frosted window's frame, from both sides.");

            s_PrefVerboseLogging = s_PrefCategory.CreateEntry<bool>(
                "VerboseLogging",
                false,
                "Verbose Logging",
                "Writes a log line per object during save/load. Costs noticeable frame time on maps with many interiors - turn on only for troubleshooting (F7 toggles it in-game)."
            );
            s_DebugBounds = s_PrefVerboseLogging.Value;

            s_PrefShowSnowKillers = s_PrefCategory.CreateEntry<bool>(
                "ShowSnowKillers",
                false,
                "Show Snow Killers",
                "Developer display: draws every snow killer the game holds in the region as a see-through box, live: green in force, yellow listed but off, red held out by the mod. Off for play."
            );

            s_PrefFastInteriorToggle = s_PrefCategory.CreateEntry<bool>(
                "FastInteriorToggle",
                true,
                "Fast Door Transitions",
                "Lets deactivating the building hide its contents, instead of switching every item's renderers and colliders off and on again one by one. On a base with thousands of items this is the difference between a door taking most of a second and taking a few milliseconds. Turn off only if items go missing or become impossible to pick up after walking through a door."
            );

            s_PrefDoorFade = s_PrefCategory.CreateEntry<bool>(
                "DoorFade",
                true,
                "Fade Doors Through Black",
                "A building's door fades the screen to black and back (a quarter of a second each way, a quarter held black) instead of cutting straight to the other side, so the last things to switch on at the door are not seen appearing. Turn off for an instant cut."
            );

            s_PrefEnablePerfProbe = s_PrefCategory.CreateEntry<bool>(
                "EnablePerformanceReport",
                false,
                "Performance Report",
                "Writes a performance summary to the log every 5 seconds: frame time, what the mod spends it on, and how much each object search costs against how full every building is. Turn this on if you are reporting frame drops. Shift+F6 toggles it in game."
            );

            // The scene stats the probe writes on start need a loaded region, so the state is
            // applied without them here; the first report follows five seconds into the game.
            PerfProbe.On = s_PrefEnablePerfProbe.Value;

            // The in-game settings page, when ModSettings is installed (ModSettingsPage.cs). It has to be
            // added before the game builds its custom-mode menu, which is why it is here and not later.
            TryRegisterSettingsPage();

            // The timestamped fire dumps earlier versions left behind, one per save and one
            // per load, go in one pass here.
            CleanUpOldFireDumps();

            // Flush the settings to disk right away: otherwise newly added keys only reach
            // MelonPreferences.cfg when the game shuts down CLEANLY - after a crash they
            // never appear in the file at all.
            MelonPreferences.Save();

            MelonLogger.Msg($"[SETTINGS] Log dili: {(SeamlessInteriorsLog.English ? "English" : "Turkce")} (LogLanguage tercihi: auto, tr, en). Ceviri tablosu: {SeamlessInteriorsLog.RuleCount} kural.",
                            $"[SETTINGS] Log language: {(SeamlessInteriorsLog.English ? "English" : "Turkish")} (preference LogLanguage: auto, tr, en). Translation table: {SeamlessInteriorsLog.RuleCount} rules.");
            MelonLogger.Msg($"[SETTINGS] Interior Lighting Mode: {(IsDarkAtmosphereMode ? "Dark Atmosphere" : "Outdoor Lighting")}");
            MelonLogger.Msg($"[SETTINGS] Initial Loot Roll: {(IsInitialLootRollEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Gear Save Filter: {(IsGearSaveFilterEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Legacy Save Import: {(IsLegacySaveImportEnabled ? "ON" : "OFF")} (F6 teshis)");
            MelonLogger.Msg($"[SETTINGS] Save Identity Guard: {(IsSaveIdentityGuardEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Delete Data With Save: {(IsDeleteDataWithSaveEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Lazy Interior Content: {(IsLazyContentEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Apply Removed Objects: {(IsLegacyRemovedHandlingEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Chimney Smoke: {(IsChimneySmokeEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Window Glow: {(IsWindowGlowEnabled ? "ON" : "OFF")} " +
                            $"(flicker {(IsWindowGlowFlickerEnabled ? "ON" : "OFF")}, strength {WindowGlowStrength:F2}) (Shift+F9 onizleme)");
            MelonLogger.Msg($"[SETTINGS] Window Ground Glow: {(IsWindowGroundGlowEnabled ? "ON" : "OFF")} (strength {WindowGroundGlowStrength:F2})");
            MelonLogger.Msg($"[SETTINGS] Verbose Logging: {(s_DebugBounds ? "ON" : "OFF")} (F7)");
            MelonLogger.Msg($"[SETTINGS] Fast Door Transitions: {(IsFastInteriorToggleEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Fade Doors Through Black: {(IsDoorFadeEnabled ? "ON" : "OFF")}");
            MelonLogger.Msg($"[SETTINGS] Performance Report: {(PerfProbe.On ? "ON" : "OFF")} (Shift+F6)");
        }

        // Runs once every mod has been initialised, before any gameplay code of those mods has
        // run: early enough that the views are in place before other mods' gameplay code asks.
        public override void OnLateInitializeMelon()
        {
            // Template scene loads stay between this mod and MelonLoader; mods see the interior scene
            // and only its fires, and cannot save under its name (see SeamlessInteriorsMod.VanillaView.cs).
            InstallSceneEventShield();
            InstallSceneView();

            // FindObjectsOfType searches from mods find only the player's place (SeamlessInteriorsMod.ObjectView.cs),
            // and their components elsewhere keep running in their own place (SeamlessInteriorsMod.PlaceContext.cs).
            InstallObjectView();
            InstallPlaceContext(HarmonyInstance);

            // The two cases no vanilla answer can cover (see Compat/ModExceptions.cs).
            ModExceptions.Install(HarmonyInstance);

            // Which mods' scene load hooks a building's load will run (SeamlessInteriorsMod.SceneLoadReplay.cs).
            ReportBuildingLoadHooks();
        }

        public override void OnUpdate()
        {
            PerfProbe.Frame();

            // Determine the owner of the global lighting (the clone the player is inside)
            // every frame. The light patches consult that result to decide who may write
            // the global ambient.
            long perf = PerfProbe.Begin();
            ClonedInteriorLightingGuard.Tick();
            PerfProbe.End(PerfProbe.Section.LightGuard, perf);

            perf = PerfProbe.Begin();

            // A region gear restore the mod turned away must always be replayed.
            TickDeferredGearRestore();

            // Interior data belonging to saves that no longer exist. Costs one call every
            // few seconds and does nothing at all outside the main menu.
            TickDeletedSaveSweep();

            // A decoration in the backpack must not also be standing in the world.
            // (see SeamlessInteriorsMod.CarriedDecorations.cs)
            TickCarriedDecorationGuard();

            // Safehouse Customization+ objects standing in a parked room show and hide with it.
            ModExceptions.TickSafehouseCustomizationPlus();

            // A shell left in a RandomBurnedHouses group the game switched off after the load.
            TickShellVariantGuard();

            // While the player customizes inside a clone, the game's single "junk cleared"
            // flag has to answer for that clone. (see SeamlessInteriorsMod.Junk.cs)
            TickJunkFlagView();

            // The player cleaning one window by hand. (see SeamlessInteriorsMod.WindowClearing.cs)
            TickWindowClearing();

            // Heavy weather brings the frost back sooner, and a fire in a building holds it off its
            // cleaned windows. (see SeamlessInteriorsMod.WindowFrostState.cs)
            TickWindowFrostWeather();
            TickWindowFrostFires();
            TickWindowFrostReturn();
            TickOutsideOpenWindows();  // open windows seen from outside, taken down first on the way in
            TickWindowCoverLayers();   // the frost and snow drawn on the room's glass
            TickWindowSashes();        // windows that open (see SeamlessInteriorsMod.WindowSashes.cs)

            // Room objects a building's row hides stay hidden. (see HideRoomObjectsByRow)
            KeepRowHiddenRoomObjectsOff();

            PerfProbe.End(PerfProbe.Section.FrameTicks, perf);

            // Every built clone follows the lighting mode the moment it changes (F8 or settings).
            TickLightingModeSwitch();
            TickInteriorProbes();

            // ─── F8: SWITCH INTERIOR LIGHTING MODE ───
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8))
            {
                int newMode = IsDarkAtmosphereMode ? 0 : 1;
                s_PrefInteriorLightingMode.Value = newMode;
                MelonPreferences.Save();

                string modeName = newMode == 1 ? "Dark Atmosphere" : "Outdoor Lighting";
                s_LightingModeMessage = $"Interior Lighting: {modeName}";
                s_LightingModeMessageTimer = 4f;

                MelonLogger.Msg($"[SETTINGS] Interior Lighting Mode changed to: {modeName}");
                RefreshSettingsPage();
            }

            // ─── F7: TOGGLE VERBOSE LOGGING ───
            // Verbose logging writes a line per object during save/load and costs visible
            // frame time, which is why it defaults to off.
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
            {
                s_DebugBounds = !s_DebugBounds;
                if (s_PrefVerboseLogging != null)
                {
                    s_PrefVerboseLogging.Value = s_DebugBounds;
                    MelonPreferences.Save();
                }

                s_LightingModeMessage = $"Verbose Logging: {(s_DebugBounds ? "ON" : "OFF")}";
                s_LightingModeMessageTimer = 3f;
                MelonLogger.Msg($"[SETTINGS] Verbose Logging: {(s_DebugBounds ? "ON" : "OFF")}");
                RefreshSettingsPage();
            }

            // ─── F6: PRE-MOD SAVE DIAGNOSTICS ───
            // Lists the scene keys the current save really contains and what the mod
            // resolved for every building. SHIFT+F6 toggles the performance probe instead.
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
            {
                if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift))
                {
                    PerfProbe.Toggle();
                }
                else
                {
                    try { DumpLegacySaveDiagnostics(); }
                    catch (System.Exception ex) { MelonLogger.Warning($"[LEGACY-TESHIS] Hata: {ex}"); }

                    s_LightingModeMessage = "Eski kayit teshisi log'a yazildi";
                    s_LightingModeMessageTimer = 3f;
                }
            }

            // ─── F9: INTERACTION DIAGNOSTICS ───
            // Dumps the colliders in front of the crosshair and the active/collider/layer
            // state of nearby items to the MelonLoader log, to find the cause of
            // "I can see the item but cannot pick it up".
            // SHIFT+F9 previews the exterior fire effects on the nearest building instead
            // (see SeamlessInteriorsMod.ExteriorFireEffects.cs).
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                bool shift = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift)
                          || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
                if (shift)
                {
                    try { ToggleExteriorFxPreview(); }
                    catch (System.Exception ex) { MelonLogger.Warning($"[DIS-EFEKT] Onizleme hatasi: {ex}"); }
                }
                else
                {
                    try { DiagnoseInteractivity(); }
                    catch (System.Exception ex) { MelonLogger.Warning($"[TESHIS] Hata: {ex}"); }
                }
            }

            // ─── F10: INTERACTION REPAIR (manual) ───
            // Re-enables item colliders left disabled in the clone the player is inside.
            // SHIFT+F10 shows the indoor temperatures instead (see SeamlessInteriorsMod.VanillaView.cs).
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
            {
                bool shiftHeld = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift)
                              || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
                try
                {
                    if (shiftHeld)
                    {
                        string screenText;
                        string logLine = DescribeIndoorState(out screenText);
                        if (logLine != null)
                        {
                            MelonLogger.Msg($"[ISI] (Shift+F10) {logLine}");
                            s_LightingModeMessage = screenText;
                        }
                        else
                        {
                            s_LightingModeMessage = "Sicaklik bilgisi henuz hazir degil";
                        }
                        s_LightingModeMessageTimer = 8f;
                    }
                    else
                    {
                        int repaired = RepairInteractivityForPlayerInstance(true);
                        s_LightingModeMessage = $"Etkilesim onarimi: {repaired} collider geri acildi";
                        s_LightingModeMessageTimer = 4f;
                    }
                }
                catch (System.Exception ex) { MelonLogger.Warning($"[F10] Hata: {ex}"); }
            }

            // Tick down the on-screen notification timer.
            if (s_LightingModeMessageTimer > 0f)
                s_LightingModeMessageTimer -= UnityEngine.Time.deltaTime;

            Transform playerT = Il2Cpp.GameManager.GetPlayerTransform();
            if (playerT == null) { PerfProbe.FrameEnd(); return; }
            Vector3 pos = playerT.position;

            // Keep the cooking pots and fires in CLOSED clones running. Unity stops calling
            // their Update the moment MasterInterior goes inactive, which used to freeze a
            // boil timer for as long as the player stayed outside.
            // (see SeamlessInteriorsMod.FrozenTime.cs)
            perf = PerfProbe.Begin();
            TickClosedInteriorTime();
            PerfProbe.End(PerfProbe.Section.ClosedTime, perf);

            // Chimney smoke and window glow on the shells of buildings with a fire burning inside.
            // Runs after the closed-clone time step, so a fire that just ran out is seen as out.
            // (see SeamlessInteriorsMod.ExteriorFireEffects.cs)
            perf = PerfProbe.Begin();
            TickExteriorFireEffects(pos);
            PerfProbe.End(PerfProbe.Section.ExteriorFx, perf);

            // A player something moved out of a building without a door (sleepwalking, a
            // teleport) is taken out of its inside state here.
            // (see SeamlessInteriorsMod.PlayerLocation.cs)
            perf = PerfProbe.Begin();
            TickDoorlessExitDetection(pos);
            PerfProbe.End(PerfProbe.Section.DoorlessExit, perf);

            // Objects the game put back where a closed building stands are pulled under its
            // clone. One pass for the whole region - it used to be one per building, each
            // with its own scene scan. (see SeamlessInteriorsMod.Visibility.cs)
            TickStraySweep(pos);

            // The game's own indoor space follows the building the player is recorded in; a load may build it late.
            SyncVanillaIndoorSpace();
            TickIndoorReadout();

            // Inside a building the player gets the vanilla indoor capsule (SeamlessInteriorsMod.IndoorMovement.cs).
            TickIndoorPlayerCapsule();

            // Other mods' components where the player is not run in their own place's context.
            // (see SeamlessInteriorsMod.PlaceContext.cs)
            perf = PerfProbe.Begin();
            TickPlaceContext();
            PerfProbe.End(PerfProbe.Section.PlaceContext, perf);

            // Window light shafts follow the time of day, as the interior's lighting manager makes them do.
            // (see SeamlessInteriorsMod.LightShafts.cs)
            TickWindowLightShafts();

            // Safety net for "the player is sheltered from wind and stuck at indoor
            // temperature while standing in the open".
            // (see SeamlessInteriorsMod.InsideFlag.cs)
            perf = PerfProbe.Begin();
            TickPlayerInsideCloneFlagGuard();
            PerfProbe.End(PerfProbe.Section.FlagGuard, perf);

            // Tell the game the player is in an indoor space, which nothing else does: the
            // clone's IndoorSpaceTrigger never fires OnTriggerEnter because the player is
            // teleported in. Runs AFTER the guard above so a flag the guard just cleared
            // releases the registration on the same tick rather than the next one.
            // (see SeamlessInteriorsMod.IndoorState.cs)
            TickIndoorSpaceRegistration();

            // Keep the sound engine's scene and interior states at the region's own values, and
            // play the room's own sounds while the player is in it. Once a second.
            // (see SeamlessInteriorsMod.RoomSound.cs)
            TickRoomSound();

            // In a parked room the player still has the OUTDOOR body, 100 cm wide with a 45 degree
            // slope limit, in a room the game built for its indoor one, 60 cm and 60 degrees. Give
            // it the indoor body while the flag says inside, and hand the outdoor one back. After
            // the guard for the same reason as the registration above.
            // (see SeamlessInteriorsMod.ParkedRoomBody.cs)
            TickParkedRoomBody();

            // Inside a PARKED clone the game still culls its instanced trees and rocks for the
            // player's camera, which is ParkOffset away from the eye the window portals render
            // from. Give that cull a stand-in eye on the ground. Runs after the guard for the
            // same reason as the registration above: a flag the guard just cleared must release
            // the stand-in on this tick, because left in force outside it is underground.
            // (see SeamlessInteriorsMod.ParkedCullEye.cs)
            TickParkedCullEye();

            // The room's own fog pass goes off with the stand-in eye and comes back with it.
            // (see SeamlessInteriorsMod.ParkedWeather.cs)
            TickParkedRoomFog(ParkedCullEyeInForce ? GameManager.GetMainCamera() : null);

            // Keep each building's terrain hole in sync with whether the player is inside it.
            perf = PerfProbe.Begin();
            foreach (var instance in ActiveInteriors.Values)
            {
                if (!instance.RunCompleted || instance.MasterInterior == null) continue;

                // PRE-FILTER: with the clone scene closed the player definitely is not
                // inside it. The terrain hole will not be open either, so there is nothing
                // to do - no need to enter IsPositionInside/SetTerrainHoleState at all.
                // (On a 15-building map this line removes 14 pointless calls per frame.)
                if (!instance.MasterInterior.activeSelf)
                {
                    if (IsTerrainHoleOpen(instance))
                        SetTerrainHoleState(instance, false);
                    continue;
                }

                // Only buildings with a hole; the door state answers first, rays at most four times a second.
                if (!IsTerrainHoleWanted(instance) && !IsTerrainHoleOpen(instance)) continue;
                SetTerrainHoleState(instance, IsPlayerInsideForTerrainHole(instance, pos));
            }
            PerfProbe.End(PerfProbe.Section.TerrainHole, perf);
            PerfProbe.FrameEnd();
        }

        public override void OnLateUpdate()
        {
            long perf = PerfProbe.Begin();
            ClonedInteriorLightingGuard.EnforceOwnerAmbient();

            // A snowed-up window's light shaft stays off every frame. (see SeamlessInteriorsMod.LightShafts.cs)
            HoldSnowedUpShaftsDark();

            // In a parked room the game's snow is born at the ground, below the room.
            // (see SeamlessInteriorsMod.ParkedWeather.cs)
            TickParkedSnowEmitters();

            // In a parked room the game's snow planes are off; they stood in the room.
            // (see SeamlessInteriorsMod.ParkedWeather.cs)
            TickParkedSnowPlanes();

            // Developer display of the game's snow killers, off unless ShowSnowKillers is set.
            // (see SeamlessInteriorsMod.KillerDisplay.cs)
            TickSnowKillerDisplay();

            // The window views' targets kept after a visit are released after two minutes away.
            // (see SeamlessInteriorsMod.WindowPortals.cs)
            TickKeptWindowTargets();

            // Draw the real outside view into the interior's windows.
            //
            // HERE, not in OnUpdate, and this is load-bearing. The portal cameras are rendered by
            // hand, and a hand-driven render that happens too early in the frame draws a stale
            // world: TLD settles its terrain-object and vegetation activation late, so an early
            // render produced a view with the lake and the mountains but no trees and no rock
            // outcrops. OnLateUpdate is after that settles and still before rendering.
            // (see SeamlessInteriorsMod.WindowPortals.cs)
            // INSIDE-OUT PORTALS ARE RETIRED (owner's decision, 2026-09-15). Windows showing the
            // real outside view from inside the building depended on a parked interior, and the
            // parking went with them: on the ground the interior is co-located with its shell and
            // physically larger than it, so the portal camera stands inside the very geometry it
            // is trying to see past and draws the cabin's own walls. The renderer, its reasoning
            // and its measurements are preserved on branch feature-window-portals.
            //
            // What replaced it looks the other way through the same opening.
            //
            // TEST RUN 2026-09-18: inside-out is switched back on beside it, to test whether the
            // trees that dropped out of its view were removed by the game's own instanced renderer
            // (RenderObjectInstance), which culls every tree and rock for FPSCamera alone. The
            // retirement above stands until that is measured. The two never run together: each
            // keys on s_IsPlayerInsideClone and tears itself down on the other's side of the door.
            //
            // ORDER IS LOAD-BEARING, AND IT DEPENDS ON WHICH SIDE OF THE DOOR THE PLAYER IS ON. Each
            // system tears itself down when the player is on the other side, and the one being LEFT
            // has to finish tearing down before the one being ENTERED sets up, because each borrows
            // state from the clone that the other reads:
            //
            //   stepping IN,  outside-in must first give back the clone's layers, colliders and the
            //                 main camera's mask, or inside-out sets up against a relayered clone.
            //   stepping OUT, inside-out must first give the glass its own material back, or
            //                 outside-in, which finds the interior's windows BY that material, finds
            //                 none. Measured 2026-09-18: 8 shell apertures, 0 paired, no portals, and
            //                 the shell's windows went back to opaque for the rest of the visit.
            if (s_IsPlayerInsideClone)
            {
                TickOutsideInPortals();
                TickWindowPortals();
            }
            else
            {
                TickWindowPortals();
                TickOutsideInPortals();
            }
            PerfProbe.End(PerfProbe.Section.LateUpdate, perf);
        }

        // ─── SAFETY NET FOR THE DEFERRED REGION GEAR RESTORE ───
        //
        // GearManager.Deserialize is how the game puts the region's loose gear back:
        // the carcasses, the meat and everything the player dropped outdoors. The mod
        // turns that call away while an interior scene is being pulled in, and replays it
        // afterwards (see PreventGearManagerDuplicationPatch).
        //
        // That replay used to hang off ONE call site - the end of Run()'s scene-load
        // section. Run() is not called at all when a region is re-entered from a vanilla
        // interior: the clones come back out of the persist cache instead. A payload
        // deferred in that window was therefore never replayed, and the items the player
        // had left on the ground outside were simply gone.
        //
        // So the replay is now driven from here as well: the moment nothing is loading
        // any more, whatever is queued runs. The check is a single bool read per frame
        // while the queue is empty, which it is for the entire game except during a load.
        private static float s_LastDeferredGearReplayCheck = -1f;

        private static void TickDeferredGearRestore()
        {
            if (!PreventGearManagerDuplicationPatch.HasDeferred) return;
            if (IsLoadingInteriorScene) return;

            // Do not hammer it: a replay can itself take a while.
            if (s_LastDeferredGearReplayCheck >= 0f
                && Time.realtimeSinceStartup - s_LastDeferredGearReplayCheck < 0.5f) return;

            s_LastDeferredGearReplayCheck = Time.realtimeSinceStartup;
            PreventGearManagerDuplicationPatch.ReplayIfDeferred();
        }

        // Every building handled by the mod, keyed by InstanceId.
        public static Dictionary<string, SeamlessInteriorInstance> ActiveInteriors = new Dictionary<string, SeamlessInteriorInstance>();

        // LOADING SCREEN EXTENSION: holds the screen black (CameraFade) until the mod has
        // loaded every instance. While this flag is true the screen is blacked out; it is
        // released once all instances are done.
        public static bool s_ScreenHeldBlack = false;

        // LOADING OVERLAY: draws a "Loading..." label on top of the black screen (OnGUI).
        private static string s_LoadingDots = "Loading...";
        private static float s_LoadingDotsTimer = 0f;
        private static int s_LoadingDotsIndex = 0;

        // LIGHTING MODE NOTIFICATION (shown on screen when toggled with F8).
        private static string s_LightingModeMessage = "";
        private static float s_LightingModeMessageTimer = 0f;

        // The SupportedInteriors config list lives in SeamlessInteriorsMod.Configs.cs.

        // GENERAL SETTINGS (shared by all interiors).
        // After a door transition the watchdog stays out of the way for this long.
        public const float PORTAL_SUPPRESS_WINDOW = 2f;
        public static float s_LastPortalUseTime = -10f;

        public static bool s_DebugBounds = false;

        // ─── Authority over "is the player inside" after a load ───
        //
        // PROBLEM: the bounds fallback in IsPositionInside can give a false positive right
        // in front of a door (the InteriorTrigger volume is expanded by (1,3,1) and only
        // shrunk by 0.5). A player who saved outside, in front of the door, got the
        // interior shown as visible during loading.
        //
        // FIX: in the first seconds after a load, the saved "player is inside" information
        // is MORE RELIABLE than the geometric test. The save file knows exactly where the
        // player saved. That authority holds until the player uses a door (or the window
        // expires).
        private const float LOAD_STATE_AUTHORITY_WINDOW = 20f;
        private static bool s_LoadStateAuthorityActive = false;
        private static float s_LoadStateAuthorityExpireTime = 0f;

        public static void NotifyPortalUsed()
        {
            s_LastPortalUseTime = Time.time;
            s_LoadStateAuthorityActive = false;

            // MAKE THE CLONE SOLID AGAIN BEFORE THE TRANSITION MEASURES IT.
            //
            // Reported 2026-09-15: "entering the cabin the player is NOT on the floor, it's up in
            // the air." Every door transition computes its landing spot through SnapToGround, which
            // raycasts down about 3 m for the nearest solid surface - and while the player is
            // outside, the outside-in portal system deliberately holds all 427 of the clone's
            // colliders disabled so nobody can walk on an interior floor from the yard. The floor
            // the snap is looking for is therefore switched off at the moment it looks, so the
            // snap either finds the terrain below the building or nothing at all and returns the
            // raw position unchanged. The floors are correctly ALIGNED; the query was asking a
            // building that was not there.
            //
            // Teardown restores that collision, but it runs from OnLateUpdate, which is after the
            // transition has already teleported the player. This is the transition's own starting
            // gun, and it fires before all four SnapToGround call sites, so it is the one place
            // that covers entry, exit and both sub-interior links at once.
            //
            // Safe to over-call: if no transition actually follows, the per-frame sweep in
            // ReassertOutsideInHold disables the colliders again on the very next frame.
            RestoreOutsideInCollisionNow();
        }

        private static bool IsLoadStateAuthoritative()
        {
            if (!s_LoadStateAuthorityActive) return false;

            if (Time.time > s_LoadStateAuthorityExpireTime)
            {
                s_LoadStateAuthorityActive = false;
                return false;
            }

            return HasSavedPlayerInsideState();
        }

        // THE DOOR IS THE TOGGLE; POSITION IS THE BACKSTOP (owner, 2026-09-18: "We should primarily
        // be using the door entry as the toggle. The inside-the-building check should be extra,
        // not the primary."). At a load there is no door, and the author's stand-in for it is the
        // saved record, so that answers first, exactly as before. Position only overrides it in
        // the one direction it has been caught wrong: the record says "outside" while the player
        // is standing in a parked room. That is said out loud, because it means something upstream
        // of here is broken and this is only catching it.
        public static bool ResolvePlayerInside(SeamlessInteriorInstance instance, Vector3 pos)
        {
            if (PlayerIsInAnotherParkedBuilding(instance)) return false;
            bool recorded = ResolvePlayerInsideAsRecorded(instance, pos);
            if (recorded) return true;

            if (instance != null && instance.IsPositionInParkedRoom(pos, 0f))
            {
                MelonLogger.Warning($"[LOAD-STATE] {instance.Config.ResolvedInstanceId}: kayit 'disarida' diyor ama oyuncu park edilmis odanin icinde duruyor - konuma uyuluyor.");
                return true;
            }
            return false;
        }

        private static bool ResolvePlayerInsideAsRecorded(SeamlessInteriorInstance instance, Vector3 pos)
        {
            bool geometric = instance.IsPositionInside(pos);

            // Already inside this building and within its volume: a missed ray (the conveyor pipe) must not close it,
            // nor a save made elsewhere (the mine) before arriving here.
            if (PlayerInteriorId == instance.Config.ResolvedInstanceId && (geometric || instance.IsPositionInVolume(pos, 2.0f)))
                return true;

            if (!IsLoadStateAuthoritative()) return geometric;

            if (GetSavedPlayerInsideInstanceId() == instance.Config.ResolvedInstanceId)
            {
                // The save says the player was inside THIS building. Count them as inside
                // even if geometry misses - but they do have to be near the building, so a
                // stale save after a region change cannot open a distant house.
                return geometric || instance.IsPositionInVolume(pos, 2.0f);
            }

            // The save says the player was NOT inside this building. Do not trust the
            // doorway bounds false positive - the save file is right.
            if (geometric && s_DebugBounds)
                MelonLogger.Msg($"[LOAD-STATE] {instance.Config.ResolvedInstanceId}: geometri 'iceride' dedi ama kayit 'disarida' diyor, kayda uyuluyor.");

            return false;
        }

        // Optimization: cache UniStorm instead of calling FindObjectOfType every frame.
        private static Il2Cpp.UniStormWeatherSystem s_CachedUniStorm = null;
        public static Il2Cpp.UniStormWeatherSystem GetCachedUniStorm()
        {
            if (s_CachedUniStorm == null)
                s_CachedUniStorm = UnityEngine.Object.FindObjectOfType<Il2Cpp.UniStormWeatherSystem>();
            return s_CachedUniStorm;
        }

        // Lock that stops several scene-loading coroutines from running at once.
        // Instances sharing an InteriorSceneBaseName load one after another: the first
        // loads the scene and stores it as a template, the rest copy it.
        private static bool s_SceneLoadLock = false;
        private static float s_SceneLoadLockTakenAt = 0f;

        // A single Addressables scene load is a matter of seconds. Anything still
        // holding the lock after this has been interrupted and is never going to
        // release it.
        private const float SCENE_LOAD_LOCK_MAX_SECONDS = 45f;

        private static void TakeSceneLoadLock()
        {
            s_SceneLoadLock = true;
            s_SceneLoadLockTakenAt = Time.realtimeSinceStartup;
        }

        // Read by the patches: is an interior scene being pulled in RIGHT NOW?
        //
        // This is a far narrower window than "is the mod cloning". Cloning a whole
        // region takes the better part of a minute; a single scene load is a moment.
        // Patches that need to hold the game off should use this, never the full
        // cloning flag - see PreventGearManagerDuplicationPatch.
        //
        // SELF-HEALING. While this lock is held, the game is not allowed to restore the
        // region's loose gear, and no further clone may load. A Run() coroutine killed
        // between taking and releasing it - an interrupted load, an exception inside
        // LoadInteriorScenes, an Addressables handle that never reports IsDone - used to
        // leave it set for the rest of the session, and from that moment on nothing the
        // player dropped outdoors ever came back and no building was ever cloned again.
        // Nothing announced it either. A load that overruns the cap is therefore treated
        // as over.
        public static bool IsLoadingInteriorScene
        {
            get
            {
                if (!s_SceneLoadLock) return false;

                if (Time.realtimeSinceStartup - s_SceneLoadLockTakenAt <= SCENE_LOAD_LOCK_MAX_SECONDS)
                    return true;

                s_SceneLoadLock = false;
                MelonLogger.Warning($"[SAHNE-KILIDI] Ic mekan sahne yukleme kilidi {SCENE_LOAD_LOCK_MAX_SECONDS:F0} " +
                                    $"saniyeden uzun suredir acik kalmis (yarida kesilmis bir yukleme), zorla birakildi.");
                return false;
            }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // A new scene is up, so whatever was being torn down is gone. See
            // SuspendParkedCullEyeForSceneExit.
            ResumeParkedCullEye();

            // No save is loaded at the main menu, so anything the mod remembers ABOUT a
            // save has to be dropped here. The game hands the slot name of a deleted save
            // straight to the next new game, and a memo keyed on that name would answer
            // for the wrong playthrough (see ForgetSaveIdentity).
            if (!string.IsNullOrEmpty(sceneName)
                && sceneName.StartsWith("MainMenu", System.StringComparison.Ordinal))
            {
                ForgetSaveIdentity("ana menuye donuldu");

                // Which windows are frosted is remembered per building, not per save, so it goes
                // the same way. Every route to a different save passes through here.
                ForgetAllWindowStates();

                // The main menu is also where saves get deleted, and where a save deleted
                // in an earlier session (or with the mod uninstalled) first becomes visible
                // as data without an owner. This is the earliest the sweep can be tried;
                // the save list is usually still loading here, in which case it does
                // nothing and the ticker in OnUpdate picks it up a few seconds later.
                // See SeamlessInteriorsMod.SaveDeletion.cs.
                SweepDeletedSaveData("ana menu", true);
            }

            // Is this a region scene the mod cares about?
            bool isSupportedExteriorScene = false;
            foreach (var cfg in SupportedInteriors)
            {
                if (sceneName == cfg.ExteriorSceneName) { isSupportedExteriorScene = true; break; }
            }

            // Reset the caches - BUT only on a real region change, or while the load window
            // is closed.
            //
            // CRITICAL: this callback also fires for ADDITIVE scene loads, including the
            // mod's OWN interior scenes (CampOffice, CampOffice_SANDBOX, CampOffice_DLC01
            // ... more than 30 per region). Resetting unconditionally would close the
            // shared scan window in the middle of loading, and the scene-wide cleanups
            // would again repeat per building.
            if (isSupportedExteriorScene || !SceneScan.InLoadPhase)
            {
                SceneScan.InvalidateAll();
                PlayerRefs.Reset();
                ResetSceneWideCleanupState();
            }

            // New-game check: clear the "player is inside" data left over from the previous
            // save. Without this the player spawns at the wrong spot inside a clone scene.
            //
            // CRITICAL: this check must only run in a real region scene and while no restore
            // is in progress. In the intermediate scenes that appear while a save loads,
            // TimeOfDay has not been restored yet and GetHoursPlayedNotPaused() returns 0;
            // that is why the old code mistook a save load for a "new game" and DELETED the
            // PlayerInside data. With it gone, savedInsideId stays empty, EARLY-FLAG does
            // not run and Wind.Start starts as if the player were outside -> the wind sound
            // disappears.
            bool restoreInProgress = false;
            try
            {
                restoreInProgress = SaveGameSystem.IsRestoreInProgress() || SaveGameSystem.IsSceneRestoreInProgress();
            }
            catch { }

            var tod = GameManager.GetTimeOfDayComponent();
            bool looksLikeNewGame = isSupportedExteriorScene && !restoreInProgress
                                    && tod != null && tod.GetHoursPlayedNotPaused() < 0.05f;

            if (!looksLikeNewGame && s_DebugBounds && tod != null && tod.GetHoursPlayedNotPaused() < 0.05f)
            {
                MelonLogger.Msg($"[NEW GAME] Temizleme ATLANDI (scene={sceneName}, supported={isSupportedExteriorScene}, restore={restoreInProgress}).");
            }

            if (looksLikeNewGame)
            {
                // The saved local position has to go too; clearing only insideKey would
                // leave the old clone-scene point in PlayerPrefs and teleport the player there.
                ClearSavedPlayerInsideState();
                if (s_DebugBounds)
                    MelonLogger.Msg($"[NEW GAME] Onceki save'den kalan PlayerInside bilgisi temizlendi.");
            }

            // Find out which clone scene the player saved in (for the early activation).
            string savedInsideId = GetSavedPlayerInsideInstanceId();

            // A mod loaded this scene while the player was inside a building. Like a vanilla door,
            // it puts the player somewhere itself, so the saved state must not pull them back in.
            if (TakeModSceneLoad(sceneName) && !string.IsNullOrEmpty(savedInsideId))
            {
                MelonLogger.Msg($"[MOD-GECIS] {savedInsideId} icin kayitli 'oyuncu iceride' bilgisi kullanilmadi: " +
                                $"oyuncuyu sahneyi yukleten mod yerlestiriyor.");
                ClearSavedPlayerInsideState();
                savedInsideId = "";
            }

            // Coming back from a scene SI does not clone (the concentrator's mine): the player
            // arrives inside the building, as if they had saved there.
            if (isSupportedExteriorScene && !string.IsNullOrEmpty(s_PendingArrivalInstance))
            {
                if (SceneOwnsInstanceId(sceneName, s_PendingArrivalInstance)) savedInsideId = s_PendingArrivalInstance;
                else { s_PendingArrivalInstance = null; s_PendingArrivalPoint = null; }
            }

            // STALE ENTRY FROM ANOTHER SAVE.
            //
            // The key is addressed by save NAME and the game reuses slot names, so a new
            // game can inherit "the player was inside building X" from whatever used to
            // live in that slot. The cleanup above cannot always catch it: it needs
            // SaveGameSystem.m_CurrentSaveName, which is not always set yet at this point.
            //
            // Here the entry can be judged on its own merits: if no building in THIS region
            // answers to that id, it cannot describe where the player is standing now.
            // Only tested in a real region scene - the additive interior scenes the mod
            // loads itself, and the intermediate scenes a save load passes through, own no
            // buildings at all and would fail the test for the wrong reason.
            if (isSupportedExteriorScene && !string.IsNullOrEmpty(savedInsideId)
                && !SceneOwnsInstanceId(sceneName, savedInsideId))
            {
                MelonLogger.Msg($"[BAYAT-KAYIT] Kayitli 'oyuncu iceride' bilgisi ({savedInsideId}) bu bolgeye " +
                                $"({sceneName}) ait degil - baska bir save'den kalmis, temizlendi.");
                ClearSavedPlayerInsideState();
                savedInsideId = "";
            }

            // The save file outranks the record: a save that put the player in a parked room
            // means "inside that building", whatever the record says (see SaveData.cs).
            if (isSupportedExteriorScene && !looksLikeNewGame)
                savedInsideId = RepairInsideRecordFromSavePosition(sceneName, savedInsideId);

            // Inside the post-load window the saved state outranks the geometric test.
            // (Stops the interior from starting visible when the player saved outside,
            //  right in front of the door.)
            s_LoadStateAuthorityActive = true;
            s_LoadStateAuthorityExpireTime = Time.time + LOAD_STATE_AUTHORITY_WINDOW;

            // CRITICAL: if the player saved inside a clone scene, set the flags IMMEDIATELY.
            // Wind.Start and other patches read this flag - too late and the wind/audio are
            // started as if the player were outside.
            //
            // Restricted to the region scene that actually owns the saved building. The
            // flag used to be set in EVERY scene this callback fires for - including
            // regions the mod does not support at all, where a leftover entry from another
            // save left the player permanently wind-sheltered and stuck at indoor
            // temperature in the open. Wind.Start only matters in the region scene, so
            // nothing is lost by being this specific.
            if (isSupportedExteriorScene && SceneOwnsInstanceId(sceneName, savedInsideId))
            {
                MarkPlayerInside(savedInsideId, "erken bayrak: icerde kaydedilmis");
                SetAudioOcclusion(true);
                if (s_DebugBounds)
                    MelonLogger.Msg($"[EARLY-FLAG] Oyuncu icerde kaydetti ({savedInsideId}), flag'ler erken set edildi.");
            }

            // Count how many instances this scene will load - needed for the screen blackout.
            int expectedInstanceCount = 0;
            foreach (var config in SupportedInteriors)
            {
                if (sceneName == config.ExteriorSceneName)
                    expectedInstanceCount++;
            }

            // At least one instance to load: hold the screen black until the mod is done.
            if (expectedInstanceCount > 0)
            {
                // Open the load window: for its duration the expensive scene scans (active
                // Renderer list, name index) are SHARED by all buildings - one scan instead
                // of 15 buildings scanning 15 times.
                SceneScan.SetLoadPhase(true);

                s_ScreenHeldBlack = true;
                CameraFade.FadeOut(0f, 0f, null);
                ShowLoadingOverlay();
                if (s_DebugBounds)
                    MelonLogger.Msg($"[SCREEN-HOLD] Ekran karartildi, {expectedInstanceCount} instance yuklenecek.");

                // Safety: force the screen open if the mod is not done within 60 seconds.
                MelonCoroutines.Start(ScreenHoldFailsafe());
            }

            // Separate the priority instance (the one the player is inside) from the rest.
            SeamlessInteriorInstance priorityInstance = null;
            var deferredConfigs = new List<InteriorConfig>();

            foreach (var config in SupportedInteriors)
            {
                if (sceneName != config.ExteriorSceneName) continue;

                string key = config.ResolvedInstanceId;
                if (!ActiveInteriors.ContainsKey(key))
                    ActiveInteriors.Add(key, new SeamlessInteriorInstance(config));

                var instance = ActiveInteriors[key];
                bool playerSavedInside = !string.IsNullOrEmpty(savedInsideId) && savedInsideId == key;

                if (playerSavedInside)
                {
                    // This instance has priority - start it immediately.
                    if (instance.InteriorPersisted && instance.MasterInterior != null)
                    {
                        instance.MasterInterior.SetActive(true);
                        if (s_DebugBounds)
                            MelonLogger.Msg($"[EARLY-ACTIVATE] Persist akisi: {key} erken aktif edildi (oyuncu icerde kaydetti).");
                        MelonCoroutines.Start(ReattachPersistedInterior(instance, true));
                    }
                    else if (!instance.RunCompleted)
                    {
                        priorityInstance = instance;
                        MelonCoroutines.Start(FadeScreenForInteriorLoad(instance));
                        MelonCoroutines.Start(WaitForPlayerThenRun(instance, true));
                    }
                }
                else
                {
                    // This instance is deferred.
                    deferredConfigs.Add(config);

                    // Persist flows are never deferred.
                    if (instance.InteriorPersisted && instance.MasterInterior != null)
                    {
                        MelonCoroutines.Start(ReattachPersistedInterior(instance, false));
                        deferredConfigs.Remove(config);
                    }
                }
            }

            // Start the non-priority instances with a delay.
            if (deferredConfigs.Count > 0)
            {
                MelonCoroutines.Start(StartDeferredInstances(deferredConfigs, priorityInstance));
            }
        }

        private IEnumerator StartDeferredInstances(List<InteriorConfig> configs, SeamlessInteriorInstance priorityInstance)
        {
            // Wait for the priority instance to finish, if there is one.
            if (priorityInstance != null)
            {
                float maxWait = 30f;
                float waited = 0f;
                while (!priorityInstance.RunCompleted && waited < maxWait)
                {
                    yield return new WaitForSeconds(0.5f);
                    waited += 0.5f;
                }
            }

            foreach (var config in configs)
            {
                string key = config.ResolvedInstanceId;
                if (!ActiveInteriors.ContainsKey(key)) continue;
                var instance = ActiveInteriors[key];
                if (!instance.RunCompleted && !instance.InteriorPersisted)
                {
                    MelonCoroutines.Start(WaitForPlayerThenRun(instance, false));
                }
            }
        }

        // Closes what OnSceneWasInitialized opened, for the persist flow - see the call
        // site in ReattachPersistedInterior.
        // One is enough, however many buildings the region reattached
        // (same pattern as s_BatchEnvironmentPending).
        private static bool s_PersistReleasePending = false;

        private IEnumerator ReleaseLoadingScreenAfterPersist(string regionSceneName)
        {
            if (s_PersistReleasePending) yield break;
            s_PersistReleasePending = true;

            // Wait for every building in THIS region to be back on its feet. Mixed regions
            // are normal: some clones come out of the persist cache, others have to be
            // rebuilt by Run() because they had not finished when the region was left.
            float timeout = 30f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                bool allDone = true;
                foreach (var inst in ActiveInteriors.Values)
                {
                    if (inst.Config.ExteriorSceneName != regionSceneName) continue;

                    if (inst.IsCloningRoutineActive || inst.InteriorPersisted || !inst.RunCompleted)
                    {
                        allDone = false;
                        break;
                    }
                }
                if (allDone) break;

                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }

            s_PersistReleasePending = false;

            // A Run() in flight owns the release: it calls TryBatchUpdateEnvironment when
            // it finishes, and that does the full job including the environment fix.
            if (IsAnyCloningActive()) yield break;

            SceneScan.SetLoadPhase(false);

            if (s_ScreenHeldBlack)
            {
                s_ScreenHeldBlack = false;
                HideLoadingOverlay();
                CameraFade.FadeIn(0.5f, 0f, null);
                MelonCoroutines.Start(FinishPendingArrival());

                if (s_DebugBounds)
                    MelonLogger.Msg("[SCREEN-RELEASE] Persist akisi: klonlar geri baglandi, ekran aciliyor.");
            }
        }

        private IEnumerator ScreenHoldFailsafe()
        {
            float maxWait = 60f;
            float waited = 0f;
            while (s_ScreenHeldBlack && waited < maxWait)
            {
                yield return new WaitForSeconds(1f);
                waited += 1f;
            }

            if (s_ScreenHeldBlack)
            {
                MelonLogger.Warning($"[SCREEN-FAILSAFE] 60 saniye doldu, ekran zorla aciliyor!");
                s_ScreenHeldBlack = false;
                HideLoadingOverlay();
                CameraFade.FadeIn(0.5f, 0f, null);
            }

            // The load window has to be closed here too: if TryBatchUpdateEnvironment never
            // completes, stale scan results must not be used for the rest of the session.
            SceneScan.SetLoadPhase(false);
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            // Scene change: clear the screen blackout flag (it may have been left set).
            s_ScreenHeldBlack = false;
            HideLoadingOverlay();

            // The scene is being torn down: the object references in the caches are dead.
            //
            // The load window is closed ONLY when a real region scene unloads. The mod's own
            // additive scenes (the interior scenes left empty after the clone is made) also
            // trigger this callback, and closing the window for them would waste the shared
            // scans.
            bool unloadedSupportedExterior = false;
            foreach (var cfg in SupportedInteriors)
            {
                if (sceneName == cfg.ExteriorSceneName) { unloadedSupportedExterior = true; break; }
            }

            if (unloadedSupportedExterior || !SceneScan.InLoadPhase)
            {
                SceneScan.InvalidateAll();
                PlayerRefs.Reset();
                PruneStrippedMaterialCache();
            }
            else
            {
                SceneScan.InvalidateVolatile();
            }

            // A gear restore still sitting in the queue describes the region that is being
            // torn down right now; replaying it into the next one would spawn the wrong
            // region's items. (Normally the queue is already empty - OnUpdate drains it
            // within half a second of the load window closing.)
            if (unloadedSupportedExterior)
                PreventGearManagerDuplicationPatch.DiscardDeferred();

            // The game's "junk cleared" flag goes back to the region's own value before the
            // next scene's restore reads it. (see SeamlessInteriorsMod.Junk.cs)
            if (unloadedSupportedExterior)
                EndJunkFlagView();

            // The chimneys and shell renderers the exterior fire effects were driving go with the
            // region. (see SeamlessInteriorsMod.ExteriorFireEffects.cs)
            if (unloadedSupportedExterior)
                ForgetExteriorFireEffects();

            // Reset the light ownership state: nobody owns it in the new scene, the outside
            // world writes.
            ClonedInteriorLightingGuard.Reset();

            // Pending hydration requests point at clones that are about to be torn down
            // or parked; anything still needed is requested again by the new scene's
            // watchdog.
            ResetHydrationQueue();

            // The stray sweep's item counts and its backoff describe the region being torn
            // down; the next one has never been swept. (see SeamlessInteriorsMod.Visibility.cs)
            ResetStraySweep();

            // On a scene change, reset the mod's occlusion bool AND GameAudioManager's real
            // occlusion counters. Resetting only the bool was not enough: since
            // GameAudioManager survives scene changes, an unbalanced Enter/Exit counter
            // leaves the audio permanently muffled or inaudible.
            ResetAudioOcclusionCounters("unload");

            // What mods made while a building of this region was loaded for them goes with the region.
            if (unloadedSupportedExterior)
                ForgetBuildingLoads();

            // A mod-triggered load takes the player out of the building, whatever the save says.
            string savedIdOnUnload = IsModSceneLoadPending() ? "" : GetSavedPlayerInsideInstanceId();
            if (string.IsNullOrEmpty(savedIdOnUnload))
            {
                MarkPlayerOutside("sahne kaldirildi: " + sceneName);
            }
            else if (s_DebugBounds)
            {
                MelonLogger.Msg($"[UNLOAD] Oyuncu icerde kaydetti ({savedIdOnUnload}), s_IsPlayerInsideClone korundu.");
            }

            s_CachedUniStorm = null;

            // Handle only the buildings that belong to this scene.
            foreach (var instance in ActiveInteriors.Values)
            {
                if (sceneName == instance.Config.ExteriorSceneName)
                {
                    if (instance.RunCompleted && instance.MasterInterior != null)
                    {
                        // PERSIST: keep the finished clone alive across the scene change so
                        // it does not have to be rebuilt from scratch on the way back.
                        UnityEngine.Object.DontDestroyOnLoad(instance.MasterInterior);
                        instance.MasterInterior.SetActive(false);
                        instance.InteriorPersisted = true;

                        if (s_DebugBounds)
                            MelonLogger.Msg($"[PERSIST] {instance.Config.InteriorSceneBaseName} DontDestroyOnLoad'a tasindi.");

                        // Scene-bound references are dead; only the clone itself survives.
                        instance.ExteriorShell = null;
                        instance.ExteriorFx = null;
                        instance.WatchdogStarted = false;

                        // Those objects belonged to the region being torn down. The clone
                        // comes back, they do not, so the next show pass has to look again
                        // rather than trust a list of dead references.
                        instance.HiddenOutsiders.Clear();
                        instance.OutsidersRecorded = false;

                        // Retire the running watchdog. RunCompleted stays true here, so its
                        // loop would otherwise keep ticking - in the next scene, against
                        // this region's coordinates - and the reattach would start a second
                        // one next to it. (see SeamlessInteriorInstance.WatchdogGeneration)
                        instance.WatchdogGeneration++;

                        if (instance.CustomKillers != null) instance.CustomKillers.Clear();

                        // NOTE: ResetWeatherParticles will take an instance parameter later.
                        // ResetWeatherParticles(instance);
                        continue;
                    }

                    instance.RunCompleted = false;
                    instance.ExteriorShell = null;
                    instance.ExteriorFx = null;
                    instance.MasterInterior = null;
                    instance.InteriorTrigger = null;
                    instance.SceneDisabledRenderers.Clear();
                    instance.WatchdogStarted = false;
                    instance.WatchdogGeneration++;
                    instance.InteriorPersisted = false;

                    // The clone is gone, so its contents are gone with it. The next Run()
                    // rebuilds from the template and has to fill it again.
                    // (The PERSIST path above deliberately does NOT reset this: that clone
                    //  keeps every object it was holding.)
                    instance.ContentHydrated = false;
                    instance.HydrationInProgress = false;
                    instance.SpawnedPlaceableGuids.Clear();
                    instance.LegacyRemovedGuids.Clear();
                    instance.SpawnedShouldBeActive.Clear();
                    instance.ClearOrphans();
                    instance.ClearLightingModeParts();
                    instance.LegacyImport = LegacyImportState.Unknown;
                    instance.LegacySource = LegacySourceKind.None;
                    instance.LegacySourceId = null;
                    instance.LegacyRawBlob = null;

                    // The clone was destroyed, so the references to the hidden junk objects
                    // are dead. The new clone is built from scratch and its state comes from
                    // the save file.
                    instance.JunkCleared = false;
                    instance.JunkClearedObjects.Clear();

                    // See the persist path above: the objects this list names went with the
                    // old scene.
                    instance.HiddenOutsiders.Clear();
                    instance.OutsidersRecorded = false;

                    if (instance.CustomKillers != null) instance.CustomKillers.Clear();
                    // ResetWeatherParticles(instance);
                }
            }

            // Clear the template snapshots: a load cut short by this unload never reached its own cleanup.
            DestroyPristineTemplates();
        }


        // Cloning needs a valid player position, so it waits for the player to be placed
        // in the world before Run() starts.
        private IEnumerator WaitForPlayerThenRun(SeamlessInteriorInstance instance, bool playerSavedInside = false)
        {
            // Shorter wait when the player is inside - start loading straight away.
            float timeout = playerSavedInside ? 2f : 10f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null && playerT.position.sqrMagnitude > 1f) break;

                yield return null;
                elapsed += 0.5f;
            }

            // A parked clone is not where the game has just put the player. The save holds
            // the world position they had INSIDE it, so between the restore and the clone
            // being rebuilt there they are standing in empty space with nothing under them.
            // Hold them at the shell, which is real ground, until the early activation in
            // Run moves them inside. Inert for an unparked building.
            if (playerSavedInside && instance.Config.IsParked)
            {
                var playerManager = GameManager.GetPlayerManagerComponent();
                Transform playerT = GameManager.GetPlayerTransform();

                if (playerManager != null && playerT != null)
                {
                    playerManager.TeleportPlayer(instance.Config.FallbackPosition, playerT.rotation);
                    MelonLogger.Msg($"[PARK] {instance.Config.ResolvedInstanceId}: player held at the shell until the parked clone is built.");
                }
            }

            MelonCoroutines.Start(Run(instance, playerSavedInside));
        }

        private IEnumerator FadeScreenForInteriorLoad(SeamlessInteriorInstance instance)
        {
            // Black out immediately (0 seconds = instant).
            // NOTE: s_ScreenHeldBlack may already have blacked the screen out with
            // BurnInBlack. Calling FadeOut anyway is harmless - they simply stack.
            Il2Cpp.CameraFade.FadeOut(0f, 0f, null);

            if (s_DebugBounds)
                MelonLogger.Msg($"[FADE] {instance.Config.ResolvedInstanceId}: Ekran karartildi, klon sahne yukleniyor...");

            // Wait for Run() to complete.
            float maxWait = 30f;
            float waited = 0f;
            while (!instance.RunCompleted && waited < maxWait)
            {
                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;
            }

            // A couple more frames, so the renderers come up.
            yield return null;
            yield return null;

            // Fade the screen back in - BUT only if s_ScreenHeldBlack is false.
            // If it is still true, TryBatchUpdateEnvironment will do it.
            if (!s_ScreenHeldBlack)
            {
                Il2Cpp.CameraFade.FadeIn(0.5f, 0f, null);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[FADE] {instance.Config.ResolvedInstanceId}: Klon sahne hazir, ekran aciliyor.");
            }
            else if (s_DebugBounds)
            {
                MelonLogger.Msg($"[FADE] {instance.Config.ResolvedInstanceId}: Klon sahne hazir ama diger instance'lar bekleniyor, ekran acilmadi.");
            }
        }

        // Brings a clone that survived the scene change (see PERSIST above) back into the
        // new region scene, instead of rebuilding it from scratch.
        private IEnumerator ReattachPersistedInterior(SeamlessInteriorInstance instance, bool playerSavedInside = false)
        {
            // Keep the wait very short when the player is inside.
            float timeout = playerSavedInside ? 2f : 10f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null && playerT.position.sqrMagnitude > 1f) break;
                yield return null;
                elapsed += 0.5f;
            }

            var exteriorScene = RealSceneByName(instance.Config.ExteriorSceneName);
            if (exteriorScene.isLoaded && instance.MasterInterior != null)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance.MasterInterior, exteriorScene);
            }

            AlignWithExteriorShell(instance);

            // Player inside: activate immediately and close the shell.
            if (playerSavedInside && instance.MasterInterior != null)
            {
                instance.MasterInterior.SetActive(true);
                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(false);

                // Back from another scene through one of the building's doors.
                if (TryTakePendingArrival(instance, out Vector3 arrival, false))
                {
                    yield return null;   // colliders up first
                    GameManager.GetPlayerManagerComponent().TeleportPlayer(EnsureAboveGround(arrival, instance), GameManager.GetPlayerTransform().rotation);
                }
            }

            // The trigger reference was lost with the old scene; recover it from the clone.
            if (instance.InteriorTrigger == null && instance.MasterInterior != null)
            {
                var killer = instance.MasterInterior.transform.Find("ParticleKiller");
                if (killer != null)
                    instance.InteriorTrigger = killer.GetComponent<BoxCollider>();
            }

            Bounds interiorBounds = ComputeLocalInteriorBounds(instance.MasterInterior, instance.Config.BoundsRadius);

            // Refresh the InteriorTrigger bounds with the padded version too
            // (used by the IsPositionInside fallback).
            if (instance.InteriorTrigger != null)
            {
                Bounds expandedBounds = interiorBounds;
                expandedBounds.Expand(TRIGGER_PADDING);
                instance.InteriorTrigger.center = expandedBounds.center;
                instance.InteriorTrigger.size = expandedBounds.size;
            }

            SetupWeatherParticleKillersOnly(instance, interiorBounds);

            ApplySafehouseCustomizationFix(instance);

            instance.InteriorPersisted = false;

            // PUT THIS CLONE'S FIRES BACK IN FRONT OF FireManager.
            //
            // This path does not go through Run(), so nothing here ever registered them -
            // and FireManager.Serialize only ever walks its own lists. A clone fire that is
            // not in them is left out of the NEXT save entirely, which means a stove still
            // burning when the player leaves the region is gone one save later, even though
            // it looks fine the moment they come back.
            //
            // The clone GameObject itself survived the region change, so its Fire components
            // still carry their own state; all that is missing is the registration.
            RegisterCloneFiresWithManager();

            // In the persist flow the clone GameObject stays alive. Since a DIFFERENT or
            // OLDER save may have been loaded in the same session, the junk state is
            // re-synchronised from the save file.
            RestoreJunkState(instance);

            // A persisted clone normally still holds everything it was filled with, so
            // this is a no-op. It only bites when the player left the region without ever
            // going near this building and is now being put straight back inside it.
            if (playerSavedInside)
                EnsureHydratedNow(instance, "persist + oyuncu icerde kaydetmis");

            InitializeVisibilityAndWatchdog(instance);

            // RELEASE THE LOADING SCREEN AND THE SHARED SCAN WINDOW.
            //
            // OnSceneWasInitialized blacks the screen out and opens the scan window for
            // every region that owns buildings, but only Run() ever started the coroutine
            // that closes them again. Re-entering a region from a vanilla interior takes
            // the persist path instead of Run(), so nothing did: the "Loading..." overlay
            // and the held black screen sat there until the 60 second failsafe fired, and
            // the load window - which also keeps the name index of the whole scene alive -
            // stayed open for just as long.
            //
            // Deliberately NOT TryBatchUpdateEnvironment: that one also forces the
            // scene-wide orphan cleanup, which DESTROYS placed objects it finds outside
            // any scene. On this path the game's own scene restore is still running, and
            // an object it has instantiated but not yet put into a scene looks exactly
            // like such an orphan. Only the release is wanted here.
            MelonCoroutines.Start(ReleaseLoadingScreenAfterPersist(instance.Config.ExteriorSceneName));

            // After a persist the player may be below the floor - correct with the saved position.
            Transform playerTFix = GameManager.GetPlayerTransform();
            if (playerTFix != null && instance.MasterInterior != null && instance.MasterInterior.activeSelf)
            {
                if (instance.IsPositionInside(playerTFix.position))
                {
                    // PRIORITY 1: use the saved local-space position
                    // (only when the save belongs to this instance - see GetSavedPlayerLocalPosition)
                    string instanceId = instance.Config.ResolvedInstanceId;
                    Vector3? savedLocalPos = GetSavedPlayerLocalPosition(instanceId);
                    Quaternion? savedLocalRot = GetSavedPlayerLocalRotation(instanceId);

                    if (savedLocalPos.HasValue)
                    {
                        Vector3 restoredWorldPos = instance.MasterInterior.transform.TransformPoint(savedLocalPos.Value);
                        Quaternion restoredWorldRot = savedLocalRot.HasValue
                            ? instance.MasterInterior.transform.rotation * savedLocalRot.Value
                            : playerTFix.rotation;

                        GameManager.GetPlayerManagerComponent().TeleportPlayer(restoredWorldPos, restoredWorldRot);
                        if (s_DebugBounds)
                            MelonLogger.Msg($"[PERSIST-FIX] Kaydedilmis local pozisyon ile duzeltildi: {restoredWorldPos}");
                    }
                    else
                    {
                        // Fallback: EnsureAboveGround
                        Vector3 correctedPos = EnsureAboveGround(playerTFix.position, instance);
                        if (correctedPos.y - playerTFix.position.y > 0.1f)
                        {
                            playerTFix.position = correctedPos;
                            if (s_DebugBounds)
                                MelonLogger.Msg($"[PERSIST-FIX] Oyuncu Y duzeltildi (fallback): {correctedPos}");
                        }
                    }
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[PERSIST] Klon interior {instance.Config.ExteriorSceneName}'a geri baglandi: {instance.Config.InteriorSceneBaseName}");
        }

        // The full clone pipeline for one building: load the interior scene, clone it,
        // align it with the exterior shell, roll the loot, restore the saved data, and
        // finally hand it over to the visibility watchdog.
        private IEnumerator Run(SeamlessInteriorInstance instance, bool playerSavedInside = false)
        {
            instance.IsCloningRoutineActive = true;
            var trace = PerfProbe.Trace.Start();
            trace?.Begin();

            // FIRST OF ALL: does the data on disk actually belong to THIS playthrough?
            // The game reuses the slot names of deleted saves, so "sandbox29" can be a
            // brand new game sitting on top of a previous one's interior data. Runs once
            // per save; see SeamlessInteriorsMod.SaveIdentity.cs.
            EnsureSaveIdentity();

            CheckNewGameLootLock(instance);
            trace?.End("kimlik");

            // Wait for the scene load lock: if another coroutine is loading the same scene,
            // let it finish and store the template first.
            //
            // The wait goes through IsLoadingInteriorScene rather than reading the field,
            // so a lock left behind by an interrupted load cannot stop every remaining
            // building in the region from ever being cloned.
            while (IsLoadingInteriorScene)
                yield return null;

            float sceneWait = Time.realtimeSinceStartup;
            TakeSceneLoadLock();
            yield return LoadInteriorScenes(instance);
            s_SceneLoadLock = false;
            if (trace != null) trace.Add("sahne-yukleme(duvar)", (Time.realtimeSinceStartup - sceneWait) * 1000.0);
            trace?.Begin();

            // If the game tried to restore the region's loose gear while that scene was
            // coming in, it was turned away - run it now that the window has closed.
            PreventGearManagerDuplicationPatch.ReplayIfDeferred();

            PrepareMasterInterior(instance);
            AlignWithExteriorShell(instance);
            trace?.End("hazirlama");

            // EARLY ACTIVATION: if the player saved inside this scene, activate
            // MasterInterior right away and move the player to the spawn position, so they
            // do not appear to be outside until Run() completes.
            if (playerSavedInside && instance.MasterInterior != null)
            {
                instance.MasterInterior.SetActive(true);
                if (instance.ExteriorShell != null) instance.ExteriorShell.SetActive(false);

                // Move the player to the position they saved at (and keep them off the floor).
                Transform playerT = GameManager.GetPlayerTransform();
                if (playerT != null)
                {
                    // One frame, so the colliders become active.
                    yield return null;

                    Vector3 safePos;
                    Quaternion safeRot = playerT.rotation;

                    // PRIORITY 1: the saved local-space position (most reliable)
                    // (only when the save belongs to this instance - see GetSavedPlayerLocalPosition)
                    string instanceId = instance.Config.ResolvedInstanceId;
                    Vector3? savedLocalPos = GetSavedPlayerLocalPosition(instanceId);
                    Quaternion? savedLocalRot = GetSavedPlayerLocalRotation(instanceId);

                    // PRIORITY 0: back from another scene through one of the building's doors.
                    if (TryTakePendingArrival(instance, out Vector3 arrival, false))
                    {
                        safePos = EnsureAboveGround(arrival, instance);
                    }
                    else if (savedLocalPos.HasValue && instance.MasterInterior != null)
                    {
                        // Convert the local position into the current clone's world space.
                        safePos = instance.MasterInterior.transform.TransformPoint(savedLocalPos.Value);
                        if (savedLocalRot.HasValue)
                            safeRot = instance.MasterInterior.transform.rotation * savedLocalRot.Value;

                        if (s_DebugBounds)
                            MelonLogger.Msg($"[EARLY-ACTIVATE] Kaydedilmis local pozisyon kullanildi: local={savedLocalPos.Value} -> world={safePos}");
                    }
                    // PRIORITY 1b: no record of where in the room, but the save's own position stands
                    // in this parked room (see RepairInsideRecordFromSavePosition). Parking is
                    // deterministic, so that spot is where the player was. Asked of the real box
                    // now that the shell is bound; the early test had only the config to go on.
                    else if (s_SavePositionInParkedRoom.HasValue && s_SavePositionInParkedRoomId == instanceId
                             && instance.IsPositionInParkedRoom(s_SavePositionInParkedRoom.Value, 0f))
                    {
                        safePos = EnsureAboveGround(s_SavePositionInParkedRoom.Value, instance);
                        var pmSaved = GameManager.GetPlayerManagerComponent();
                        if (pmSaved != null) safeRot = pmSaved.m_SaveGameRotation;

                        MelonLogger.Msg($"[EARLY-ACTIVATE] {instanceId}: oyuncu kayit dosyasinin konumuna geri kondu (X {safePos.x:F1}, Z {safePos.z:F1}).",
                                        $"[EARLY-ACTIVATE] {instanceId}: player put back at the save file's own position in the room (X {safePos.x:F1}, Z {safePos.z:F1}).");
                    }
                    // PRIORITY 2: the EntrySpawnPosition fallback
                    else if (instance.Config.EntrySpawnPosition != Vector3.zero)
                    {
                        safePos = EnsureAboveGround(ParkedInteriorPoint(instance, instance.Config.EntrySpawnPosition), instance);
                    }
                    // PRIORITY 3: lift the current position onto the floor
                    else
                    {
                        safePos = EnsureAboveGround(playerT.position, instance);
                    }

                    GameManager.GetPlayerManagerComponent().TeleportPlayer(safePos, safeRot);
                }

                if (s_DebugBounds)
                    MelonLogger.Msg($"[EARLY-ACTIVATE] Run akisi: {instance.Config.ResolvedInstanceId} erken aktif edildi, oyuncu iceri spawn edildi.");
            }

            trace?.Begin();
            HandleInitialPlaceables(instance);
            trace?.End("ilk-mobilya");
            if (!playerSavedInside)
                yield return new WaitForSeconds(0.5f);
            else
                yield return null;

            trace?.Begin();
            // PRE-MOD SAVE CHECK - must happen BEFORE ProcessSpawnsAndDeduplication.
            //
            // If the player already visited this building in the original interior scene,
            // its loot was decided long ago and lives in the game's own save. Rolling
            // fresh loot now would refill a house they emptied hundreds of days ago, so
            // the building is treated exactly as one the mod itself has already
            // generated. (The actual data is imported later, during hydration.)
            ProbeLegacySave(instance);
            if (IsLegacyImportPending(instance))
            {
                string legacySaveKey = instance.Config.SaveKeyPrefix + SaveGameSystem.m_CurrentSaveName;
                UnityEngine.PlayerPrefs.SetInt(legacySaveKey, 1);
                UnityEngine.PlayerPrefs.Save();
            }

            trace?.End("eski-kayit");

            // The template's own ids, while it still has them (VanillaIndex.cs).
            CaptureVanillaFacts(instance);
            s_LoadTrace = trace;
            trace?.Begin();
            ProcessSpawnsAndDeduplication(instance);

            s_LoadTrace = null;
            trace?.Begin();
            DisableInteriorContainerSerialization(instance.MasterInterior);
            RestoreSceneSaveData(instance);
            trace?.End("kayit-geri");
            yield return null;

            // Nothing saved for this building yet: its interior loads for mods as on a first visit, before the
            // spawn chance roll below so what they spawn rolls with the rest (SeamlessInteriorsMod.SceneLoadReplay.cs).
            trace?.Begin();
            if (!HasPersistedLootRecord(instance))
                LoadBuildingContentForMods(instance);
            trace?.End("modlara-icerik");
            trace?.Begin();

            // Activate the scene invisibly (so container Awake fires) with the renderers
            // switched off, so the player does not notice.
            if (instance.MasterInterior != null && !instance.MasterInterior.activeSelf)
            {
                // LOOT ROLL - BEFORE SetActive, while the clone scene is still inactive.
                //
                // It only toggles candidate GameObjects (see PerformInitialLootRoll); it
                // touches no internal game state and does NOT change the flow from here on.
                // Once the roll is done the "loot generated" flag is written, so when
                // RandomSpawnObject.Start() runs during the SetActive below, the existing
                // RandomSpawnBlockerPatch meets it as usual.
                PerformInitialLootRoll(instance);

                // isAlreadyGenerated cannot be used here: ProcessSpawnsAndDeduplication may
                // already have set saveKey to 1. The existence of the gear JSON file is
                // checked instead - if the file is there, this was saved before.
                //
                // CAREFUL: this block must stay CONDITIONAL. Making it unconditional (i.e.
                // destroying the component here on the first generation too) lets the RSO
                // GameObjects survive - in the original flow those objects are deleted
                // entirely by RandomSpawnBlockerPatch at Start(). That difference crashed
                // the game natively on the first autosave.
                //
                // A pending pre-mod import counts as "saved before" too: the gear JSON
                // does not exist yet, but the game's own save holds this building's real
                // contents and they are about to replace whatever the template provides.
                //
                // SAME TEST ProcessSpawnsAndDeduplication USES. The two have to agree - a
                // building whose loot it decided to roll afresh must still have its random
                // spawn filters removed the ordinary way - so they share one helper.
                bool hasExistingSave = HasPersistedLootRecord(instance);

                // LAST MOMENT THE RANDOM SPAWN FILTERS STILL EXIST.
                //
                // For a pre-mod building the roll above was suppressed, which leaves every
                // candidate object enabled. Replay what the player's own save chose before
                // the filters are destroyed below - afterwards there is no way to tell
                // which objects were candidates at all.
                ApplyLegacyRandomSpawns(instance);

                if (hasExistingSave)
                {
                    var allRSO = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.RandomSpawnObject>(true);
                    int rsoCount = 0;
                    foreach (var rso in allRSO)
                    {
                        if (rso != null)
                        {
                            UnityEngine.Object.DestroyImmediate(rso);
                            rsoCount++;
                        }
                    }
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[RSO-CLEANUP] {instance.Config.InteriorSceneBaseName}: {rsoCount} RSO component SetActive oncesi yok edildi.");
                }

                foreach (var r in InteriorScan.Renderers(instance.MasterInterior))
                    if (r != null) r.enabled = false;

                instance.MasterInterior.SetActive(true);

                if (s_DebugBounds)
                    MelonLogger.Msg($"[GEAR-INIT] {instance.Config.InteriorSceneBaseName} gorunmez olarak aktif edildi (container Awake tetikleme).");
            }
            // Template loot with live physics would fall once the room's colliders go off.
            FreezeGearPhysicsIn(instance.MasterInterior);
            trace?.End("ganimet-aktiflestirme");

            // Give container Awake time to fire.
            // Minimal wait when the player is inside; the normal delay for other scenes.
            if (!playerSavedInside)
                yield return new WaitForSeconds(3f);
            else
                yield return new WaitForSeconds(0.5f);

            // NOTE: do NOT enable the renderers yet - lightmap stripping and the
            // environment fix come first. With the renderers on, StripBakedLightmaps would
            // let the player see the broken lighting.

            trace?.Begin();
            Bounds interiorBounds = ComputeLocalInteriorBounds(instance.MasterInterior, instance.Config.BoundsRadius);

            SetupWeatherAndParticles(instance, interiorBounds);

            AutoResolveOverlappingExternalObjects(instance);

            SetupSolidPerimeter(instance, interiorBounds);

            // Lightmap stripping - the environment fix itself is now done in one batch once
            // all instances are finished.
            StripBakedLightmaps(instance);

            // Player is in this scene: bring the renderers and the environment up
            // immediately, no waiting.
            if (playerSavedInside && instance.MasterInterior != null)
            {
                UpdateGlobalEnvironment(instance);
                ShowCloneRenderers(instance);

                // Renderers were enabled unconditionally, so the colliders must be too,
                // otherwise items are visible but non-interactive
                // (see SeamlessInteriorsMod.Interactivity.cs).
                RestoreInteriorItemColliders(instance);
            }
            trace?.End("isik-hava-cevre");
            trace?.Begin();

            // Otherwise the renderers stay off - they come up in the batched
            // UpdateGlobalEnvironment call.
            instance.IsCloningRoutineActive = false;
            instance.RunCompleted = true;
            UpdateVanillaFacts(instance);

            // Check whether all instances are ready; if so, run the batched environment fix.
            MelonCoroutines.Start(TryBatchUpdateEnvironment());

            // Scene-wide cleanups: these run once per scene, not once per building
            // (see ResetSceneWideCleanupState).
            CleanupOrphanPlaceables();
            CleanupLostAndFoundBoxes();

            ApplySafehouseCustomizationFix(instance);
            trace?.End("temizlik");
            trace?.Begin();

            // ─── CONTENT ───
            //
            // Placed furniture, loose items, container contents, the state of broken /
            // harvested / opened objects and the cleared-junk flag. None of it can come
            // from the game's own save: it cannot match a clone scene by position or
            // guid, so the mod keeps its own JSON per building.
            //
            // The work itself lives in SeamlessInteriorsMod.Hydration.cs. Filling every
            // building in the region here, behind the loading screen, meant thousands of
            // item spawns for buildings the player may never walk into - so it now
            // happens when they actually come near.
            //
            // The building the player saved inside is the one exception: they are about
            // to be looking at it, so it is filled immediately, before the screen opens.
            if (IsLazyContentEnabled && !playerSavedInside)
            {
                // Covers the case where the player loaded in already standing next to the
                // building; otherwise the watchdog's distance check picks it up later.
                Transform hydratePlayerT = GameManager.GetPlayerTransform();
                if (hydratePlayerT != null)
                {
                    float d = Vector3.Distance(hydratePlayerT.position, instance.Config.FallbackPosition);
                    MaybeRequestHydrationByDistance(instance, d);
                }
            }
            else
            {
                EnsureHydratedNow(instance, playerSavedInside ? "oyuncu icerde kaydetmis" : "tembel yukleme kapali");
            }

            trace?.End("doldurma-istegi");
            trace?.Begin();
            InitializeVisibilityAndWatchdog(instance);

            // Fix the wind audio once Run() finishes in a new save.
            MelonCoroutines.Start(FixWindAfterRun(instance));

            if (trace != null)
            {
                trace.End("gorunurluk-bekci");
                trace.Log("PERF-YUKLEME", instance.Config.ResolvedInstanceId,
                    $"esya={InteriorScan.Gear(instance.MasterInterior).Length} mobilya={InteriorScan.Placeables(instance.MasterInterior).Length} icerde={playerSavedInside}");
            }
        }

        // FixWindAfterRun moved to SeamlessInteriorsMod.Weather.cs.
        // EnsureAboveGround moved to SeamlessInteriorsMod.Utility.cs.

        // ─── LOADING OVERLAY (OnGUI based) ───

        // OnGUI runs every frame (twice, in fact: Layout + Repaint). Creating GUIStyle
        // objects there would mean several allocations per frame, so they are built once
        // and refreshed when the screen size changes.
        private static GUIStyle s_NotifyStyle;
        private static GUIStyle s_LoadingStyle;
        private static int s_GuiStyleScreenHeight = -1;

        private static void EnsureGuiStyles()
        {
            if (s_NotifyStyle != null && s_GuiStyleScreenHeight == Screen.height) return;
            s_GuiStyleScreenHeight = Screen.height;

            s_NotifyStyle = new GUIStyle(GUI.skin.label);
            s_NotifyStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.022f);
            s_NotifyStyle.alignment = TextAnchor.UpperCenter;
            s_NotifyStyle.fontStyle = FontStyle.Bold;

            s_LoadingStyle = new GUIStyle(GUI.skin.label);
            s_LoadingStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.026f); // ~28px @ 1080p
            s_LoadingStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 0.85f);
            s_LoadingStyle.alignment = TextAnchor.LowerRight;
            s_LoadingStyle.padding = new RectOffset(0, 30, 0, 20);
        }

        public override void OnGUI()
        {
            // Nothing to draw: bail out in a single line - the common case.
            bool showNotify = s_LightingModeMessageTimer > 0f && !string.IsNullOrEmpty(s_LightingModeMessage);
            if (!showNotify && !s_ScreenHeldBlack) return;

            EnsureGuiStyles();

            // ─── LIGHTING MODE NOTIFICATION ───
            if (showNotify)
            {
                float alpha = Mathf.Clamp01(s_LightingModeMessageTimer); // fades out over the last second
                GUIStyle notifyStyle = s_NotifyStyle;
                notifyStyle.normal.textColor = new Color(1f, 0.85f, 0.4f, alpha);

                float boxW = Screen.width * 0.4f;
                float boxH = Screen.height * 0.08f;
                float boxX = (Screen.width - boxW) / 2f;
                float boxY = Screen.height * 0.15f;

                // Semi-transparent background.
                GUI.color = new Color(0f, 0f, 0f, 0.6f * alpha);
                GUI.DrawTexture(new Rect(boxX, boxY, boxW, boxH), Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUI.Label(new Rect(boxX, boxY + 5, boxW, boxH), s_LightingModeMessage, notifyStyle);
            }

            // ─── LOADING OVERLAY ───
            if (!s_ScreenHeldBlack) return;

            // Advance the dot animation (every 0.5 seconds).
            s_LoadingDotsTimer += Time.deltaTime;
            if (s_LoadingDotsTimer >= 0.5f)
            {
                s_LoadingDotsTimer = 0f;
                s_LoadingDotsIndex = (s_LoadingDotsIndex + 1) % 3;
                switch (s_LoadingDotsIndex)
                {
                    case 0: s_LoadingDots = "Loading."; break;
                    case 1: s_LoadingDots = "Loading.."; break;
                    case 2: s_LoadingDots = "Loading..."; break;
                }
            }

            // Full-screen black background.
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            // Text area - the whole screen, aligned to the bottom right corner.
            GUI.color = Color.white;
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), s_LoadingDots, s_LoadingStyle);
        }

        private static void ShowLoadingOverlay()
        {
            s_LoadingDots = "Loading...";
            s_LoadingDotsTimer = 0f;
            s_LoadingDotsIndex = 0;
        }

        private static void HideLoadingOverlay()
        {
            s_LoadingDotsTimer = 0f;
            s_LoadingDotsIndex = 0;
        }
    }
}
