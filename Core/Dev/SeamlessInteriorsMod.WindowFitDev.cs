using MelonLoader;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── THE TUNING RIG: WINDOWS SET BY HAND IN THE RUNNING GAME ───
        //
        // NOT SHIPPED. This file is not in the csproj; the owner's build adds it through
        // _dev/tools/SeamlessInteriors.dev.targets, which _dev/tools/deploy.sh passes to MSBuild.
        // Nothing in the shipping code names it: it attaches itself to the mod's OnLateUpdate with
        // a Harmony postfix (WindowFitRigTick, at the bottom), and it works by writing into the
        // same s_BuildingWindowOverrides table the shipping fit reader (WindowFit.cs) already
        // reads. Delete the file and the mod does not notice. Split out on 2026-09-22 so the PR to
        // the author carries the fits and not the rig that set them.
        //
        // Fits set with the slider panels (WindowFitDev, below) are kept in s_WindowFitsLive, laid
        // over the table's own row for that building, and written to a file beside the game's
        // other user data so a tuning session survives a relaunch before its rows are pasted in.
        // The file does not exist on a player's machine.
        private static readonly Dictionary<string, Dictionary<int, WindowFit>> s_WindowFitsLive = new Dictionary<string, Dictionary<int, WindowFit>>();
        private static readonly Dictionary<string, HashSet<int>> s_WindowsWithNoViewLive = new Dictionary<string, HashSet<int>>();
        // The table's own rows as they stood before the rig first touched a building, so Forget and
        // a lever back at zero give the row back exactly.
        private static readonly Dictionary<string, WindowFit[]> s_TableFitsBeforeRig = new Dictionary<string, WindowFit[]>();
        private static readonly Dictionary<string, int[]> s_TableNoViewBeforeRig = new Dictionary<string, int[]>();
        private static bool s_WindowFitsLiveLoaded;
        private static string s_WindowFitRefitShell;
        private static float s_WindowFitRefitAt;
        private static bool s_WindowFitSavePending;

        // Read from the file, in force nowhere until Arm() is called. See LoadLiveWindowFits.
        private static readonly Dictionary<string, Dictionary<int, WindowFit>> s_WindowFitsPending = new Dictionary<string, Dictionary<int, WindowFit>>();
        // "shell|window" -> the window's MEASURED size in cm when the row was written. NaN for a
        // row written before the stamp existed, which can be armed but cannot be checked.
        private static readonly Dictionary<string, Vector2> s_WindowFitStamp = new Dictionary<string, Vector2>();
        private static bool s_WindowFitsArmed;

        private static string WindowFitLivePath =>
            System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "SeamlessInteriors_WindowFits.dev.txt");

        // The building's row in the table, made if it has none. A row with every field at its
        // default reads as no row at all, so making one changes nothing by itself.
        private static BuildingWindowOverride RigRow(string shellName)
        {
            BuildingWindowOverride row;
            if (!s_BuildingWindowOverrides.TryGetValue(shellName, out row) || row == null)
            {
                row = new BuildingWindowOverride();
                s_BuildingWindowOverrides[shellName] = row;
            }
            if (!s_TableFitsBeforeRig.ContainsKey(shellName)) s_TableFitsBeforeRig[shellName] = row.Fits;
            if (!s_TableNoViewBeforeRig.ContainsKey(shellName)) s_TableNoViewBeforeRig[shellName] = row.WindowsLeftAlone;
            return row;
        }

        // The table's row becomes the building's own fits with the hand-set ones laid over, and its
        // own windows left alone plus the ones set so by hand. The shipping reader sees only the table.
        private static void MirrorRigIntoTable(string shellName)
        {
            BuildingWindowOverride row = RigRow(shellName);

            var fits = new Dictionary<int, WindowFit>();
            WindowFit[] own = s_TableFitsBeforeRig[shellName];
            if (own != null) for (int i = 0; i < own.Length; i++) fits[own[i].Window] = own[i];
            Dictionary<int, WindowFit> live;
            if (s_WindowFitsLive.TryGetValue(shellName, out live)) foreach (var pair in live) fits[pair.Key] = pair.Value;
            row.Fits = fits.Count > 0 ? new List<WindowFit>(fits.Values).ToArray() : own;

            var noView = new HashSet<int>();
            int[] ownNoView = s_TableNoViewBeforeRig[shellName];
            if (ownNoView != null) for (int i = 0; i < ownNoView.Length; i++) noView.Add(ownNoView[i]);
            HashSet<int> liveNoView;
            if (s_WindowsWithNoViewLive.TryGetValue(shellName, out liveNoView)) noView.UnionWith(liveNoView);
            row.WindowsLeftAlone = noView.Count > 0 ? new List<int>(noView).ToArray() : ownNoView;
        }

        private static void RigStore(string shellName, WindowFit fit, bool refit = true)
        {
            Dictionary<int, WindowFit> live;
            if (!s_WindowFitsLive.TryGetValue(shellName, out live)) { live = new Dictionary<int, WindowFit>(); s_WindowFitsLive[shellName] = live; }
            live[fit.Window] = fit;
            MirrorRigIntoTable(shellName);
            if (refit) s_WindowFitRefitShell = shellName;
        }

        // Back to the table's own row for that window, whatever it says.
        private static void RigForget(string shellName, int window, bool refit = true)
        {
            Dictionary<int, WindowFit> live;
            if (!s_WindowFitsLive.TryGetValue(shellName, out live) || !live.Remove(window)) return;
            if (live.Count == 0) s_WindowFitsLive.Remove(shellName);
            MirrorRigIntoTable(shellName);
            if (refit) s_WindowFitRefitShell = shellName;
        }

        // Sets one building's windows up again from the fits as they stand now, without letting go
        // of its room. The order is the one the setup uses and it matters: the frost builder finds
        // the glass by its material, so the panes have to be showing while it runs.
        private static void RefitOutsideInWindows(OutsideInBuilding building)
        {
            if (building == null || building.Instance == null || building.Instance.Config == null || building.Instance.ExteriorShell == null) return;
            BindOutsideIn(building);
            ApplyWindowFitsToApertures(building.Instance.Config.ExteriorShellPrefabName);
            AssignOutsideInViews(building.InstanceId);
            RestoreShellPanes();
            BuildOutsideInFrostOverlays(building.Instance);
            BuildOutsideInRecessObjects(building.Instance);
            s_OutsideInAppliedRecess = -1f;
            // The views in bake each pane's fit into their cameras when built; build them again.
            DestroyOutsideInWall(building);
        }

        // EVERY WINDOW'S FROST of the building nearest the player, grown and slid together, in
        // centimeters. Levers while the owner judges them by eye ("I also think I need to tune the
        // frost extents. It seems to be a little too stretched.", 2026-09-19); they start at nothing
        // and mean nothing until he has settled numbers. WindowFitDev.FrostAll*.
        //
        // Before the split these were added inside the shipping frost layout. Now they are BAKED
        // into a hand fit per window, over each window's frost as it stood when a lever first moved,
        // so the shipping code carries no lever: what Print() then emits is the settled number per
        // window, which is what gets pasted. Rows the bake made go away again once all four levers
        // are back at zero; a window that already had a hand fit gets its frost numbers back.
        private static float s_FrostAllWiderCm, s_FrostAllTallerCm, s_FrostAllRightCm, s_FrostAllUpCm;
        private static bool s_FrostAllBakeAsked;
        private static readonly Dictionary<string, Dictionary<int, WindowFit>> s_FrostAllBase = new Dictionary<string, Dictionary<int, WindowFit>>();
        private static readonly Dictionary<string, HashSet<int>> s_FrostAllMadeRows = new Dictionary<string, HashSet<int>>();

        private static void BakeFrostAll()
        {
            var building = NearestOutsideInBuilding();
            if (building == null || building.Instance == null) return;
            string shellName = building.Instance.Config.ExteriorShellPrefabName;
            bool zero = s_FrostAllWiderCm == 0f && s_FrostAllTallerCm == 0f && s_FrostAllRightCm == 0f && s_FrostAllUpCm == 0f;

            Dictionary<int, WindowFit> bases;
            if (!s_FrostAllBase.TryGetValue(shellName, out bases)) { bases = new Dictionary<int, WindowFit>(); s_FrostAllBase[shellName] = bases; }
            HashSet<int> made;
            if (!s_FrostAllMadeRows.TryGetValue(shellName, out made)) { made = new HashSet<int>(); s_FrostAllMadeRows[shellName] = made; }

            for (int w = 0; w < building.Apertures.Count; w++)
            {
                WindowFit before;
                if (!bases.TryGetValue(w, out before))
                {
                    if (zero) continue;                                  // never touched, nothing to give back
                    Dictionary<int, WindowFit> live; WindowFit unused;
                    bool byHand = s_WindowFitsLive.TryGetValue(shellName, out live) && live.TryGetValue(w, out unused);
                    before = WindowFitDev.InForceAtNearest(w);
                    bases[w] = before;
                    if (!byHand) made.Add(w);
                }
                if (zero)
                {
                    if (made.Contains(w)) RigForget(shellName, w, false); else RigStore(shellName, before, false);
                    continue;
                }
                WindowFit baked = before;
                baked.FrostRight = before.FrostRight + s_FrostAllRightCm;
                baked.FrostUp = before.FrostUp + s_FrostAllUpCm;
                baked.FrostWidth = Mathf.Max(before.FrostWidth + s_FrostAllWiderCm, 1f);
                baked.FrostHeight = Mathf.Max(before.FrostHeight + s_FrostAllTallerCm, 1f);
                RigStore(shellName, baked, false);
            }
            if (zero) { bases.Clear(); made.Clear(); }
            s_WindowFitRefitShell = shellName;
        }

        // Runs after the mod's own OnLateUpdate, from WindowFitRigTick. The first call reads the
        // file. A slider being dragged asks many times a second; the windows are set up again at
        // most ten times a second, and the file is written once the hand has stopped.
        internal static void TickWindowFitRefit()
        {
            if (!s_WindowFitsLiveLoaded) LoadLiveWindowFits();
            if (s_FrostAllBakeAsked && Time.realtimeSinceStartup >= s_WindowFitRefitAt)
            {
                s_FrostAllBakeAsked = false;
                BakeFrostAll();
            }
            if (s_WindowFitRefitShell == null && !s_WindowFitSavePending) return;
            if (Time.realtimeSinceStartup < s_WindowFitRefitAt) return;
            if (s_WindowFitRefitShell != null)
            {
                string shellName = s_WindowFitRefitShell;
                s_WindowFitRefitShell = null;
                s_WindowFitRefitAt = Time.realtimeSinceStartup + 0.1f;
                for (int h = 0; h < s_OutsideInBuildings.Count; h++)
                {
                    var instance = s_OutsideInBuildings[h].Instance;
                    if (instance == null || instance.Config == null || instance.Config.ExteriorShellPrefabName != shellName) continue;
                    RefitOutsideInWindows(s_OutsideInBuildings[h]);
                }
                BindOutsideIn(null);
                s_WindowFitSavePending = true;
                s_WindowFitRefitAt = Time.realtimeSinceStartup + 0.1f;
                return;
            }
            s_WindowFitSavePending = false;
            SaveLiveWindowFits();
        }

        private static string WindowFitRow(WindowFit fit)
        {
            var c = CultureInfo.InvariantCulture;
            return "new WindowFit(" + fit.Window + ", "
                 + fit.PortalRight.ToString("0.##", c) + "f, " + fit.PortalUp.ToString("0.##", c) + "f, "
                 + fit.PortalWidth.ToString("0.##", c) + "f, " + fit.PortalHeight.ToString("0.##", c) + "f, "
                 + fit.FrostRight.ToString("0.##", c) + "f, " + fit.FrostUp.ToString("0.##", c) + "f, "
                 + fit.FrostWidth.ToString("0.##", c) + "f, " + fit.FrostHeight.ToString("0.##", c) + "f, "
                 + fit.FrostSource
                 + (fit.RoomRightCm != 0f || fit.RoomUpCm != 0f
                        ? ", " + (fit.HasViewStart ? fit.ViewStartCm.ToString("0.##", c) + "f" : "float.NaN") + ", " + fit.RoomRightCm.ToString("0.##", c) + "f, " + fit.RoomUpCm.ToString("0.##", c) + "f"
                        : fit.HasViewStart ? ", " + fit.ViewStartCm.ToString("0.##", c) + "f" : "")
                 + (fit.RoomTallerPercent != 0f ? ", roomTallerPercent: " + fit.RoomTallerPercent.ToString("0.##", c) + "f" : "")
                 + (fit.RoomWiderPercent != 0f ? ", roomWiderPercent: " + fit.RoomWiderPercent.ToString("0.##", c) + "f" : "")
                 + "),";
        }

        // One line per window: shell|window|eight lengths|source. Written whole every time.
        // The window's own measured size in cm, or NaN where the building is not set up right now.
        private static Vector2 MeasuredCm(string shellName, int window)
        {
            var building = NearestOutsideInBuilding();
            if (building == null || building.Instance == null) return new Vector2(float.NaN, float.NaN);
            if (building.Instance.Config.ExteriorShellPrefabName != shellName) return new Vector2(float.NaN, float.NaN);
            if (window < 0 || window >= building.Apertures.Count) return new Vector2(float.NaN, float.NaN);
            var pane = building.Apertures[window];
            return new Vector2(pane.BaseWidth * 100f, pane.BaseHeight * 100f);
        }

        // ARM: move the pending rows into force, refusing any whose window no longer measures what
        // the row was written against. Deliberate, per session, and it says exactly what it did.
        internal static string ArmLiveWindowFits(bool armed)
        {
            if (!s_WindowFitsLiveLoaded) LoadLiveWindowFits();

            if (!armed)
            {
                s_WindowFitsArmed = false;
                s_WindowFitsLive.Clear();
                s_OutsideInRebuildAsked = true;
                MelonLogger.Msg("[PENCERE-AYARI] Elle ayarlanmis satirlar yururlukten kaldirildi; her pencere yine DLL'in olctugu gibi.",
                                "[WINDOW-FIT] The hand-set rows were taken out of force; every window is as the DLL measured it again.");
                return "hand-set rows OFF: every window is as the DLL measured it";
            }

            int applied = 0, refused = 0, unchecked_ = 0;
            var report = new System.Text.StringBuilder();
            foreach (var shell in s_WindowFitsPending)
            {
                foreach (var pair in shell.Value)
                {
                    Vector2 stamp;
                    s_WindowFitStamp.TryGetValue(shell.Key + "|" + pair.Key, out stamp);
                    Vector2 now = MeasuredCm(shell.Key, pair.Key);

                    bool checkable = !float.IsNaN(stamp.x) && !float.IsNaN(now.x);
                    if (checkable && (Mathf.Abs(stamp.x - now.x) > WINDOW_FIT_STAMP_TOLERANCE_CM
                                   || Mathf.Abs(stamp.y - now.y) > WINDOW_FIT_STAMP_TOLERANCE_CM))
                    {
                        refused++;
                        report.Append("REFUSED ").Append(shell.Key).Append(" window ").Append(pair.Key)
                              .Append(": written for a window of ").Append(stamp.x.ToString("F0")).Append("x").Append(stamp.y.ToString("F0"))
                              .Append(" cm, that window now measures ").Append(now.x.ToString("F0")).Append("x").Append(now.y.ToString("F0"))
                              .Append(" cm. ");
                        continue;
                    }
                    if (!checkable) unchecked_++;

                    Dictionary<int, WindowFit> live;
                    if (!s_WindowFitsLive.TryGetValue(shell.Key, out live)) { live = new Dictionary<int, WindowFit>(); s_WindowFitsLive[shell.Key] = live; }
                    live[pair.Key] = pair.Value;
                    applied++;
                }
            }
            foreach (var shell in s_WindowFitsLive.Keys) MirrorRigIntoTable(shell);
            s_WindowFitsArmed = applied > 0;
            s_OutsideInRebuildAsked = true;

            string summary = applied + " hand-set rows in force, " + refused + " refused"
                           + (unchecked_ > 0 ? ", " + unchecked_ + " could not be checked (written before the stamp, or their building is not set up here)" : "");
            MelonLogger.Msg("[PENCERE-AYARI] " + summary + ". " + report,
                            "[WINDOW-FIT] " + summary + ". " + report);
            return summary;
        }

        // Two centimetres: wider than any measurement wobble, far narrower than the difference
        // between one window kind and another.
        private const float WINDOW_FIT_STAMP_TOLERANCE_CM = 2f;

        private static void SaveLiveWindowFits()
        {
            try
            {
                var c = CultureInfo.InvariantCulture;
                var text = new System.Text.StringBuilder();
                text.AppendLine("# Seamless Interiors: windows set by hand in the running game and not yet pasted into s_BuildingWindowOverrides.");
                text.AppendLine("# shell|window|portal right|portal up|portal width|portal height|frost right|frost up|frost width|frost height|frost source|view start, empty = not set|camera in the room right|camera in the room up|MEASURED width when written|MEASURED height when written   (cm)|room taller|room wider   (percent)");
                text.AppendLine("# The last two are a STAMP, not a setting: Arm() refuses a row whose window no longer measures that, because window numbers move when a building gains windows.");
                foreach (var shell in s_WindowFitsLive)
                    foreach (var pair in shell.Value)
                    {
                        var f = pair.Value;
                        text.AppendLine(string.Join("|", new[] { shell.Key, f.Window.ToString(c),
                            f.PortalRight.ToString("R", c), f.PortalUp.ToString("R", c), f.PortalWidth.ToString("R", c), f.PortalHeight.ToString("R", c),
                            f.FrostRight.ToString("R", c), f.FrostUp.ToString("R", c), f.FrostWidth.ToString("R", c), f.FrostHeight.ToString("R", c),
                            f.FrostSource.ToString(c), f.HasViewStart ? f.ViewStartCm.ToString("R", c) : "",
                            f.RoomRightCm.ToString("R", c), f.RoomUpCm.ToString("R", c),
                            MeasuredCm(shell.Key, f.Window).x.ToString("R", c), MeasuredCm(shell.Key, f.Window).y.ToString("R", c),
                            f.RoomTallerPercent.ToString("R", c), f.RoomWiderPercent.ToString("R", c) }));
                    }
                System.IO.File.WriteAllText(WindowFitLivePath, text.ToString());
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning("[WINDOW-FIT] The hand-set windows could not be written to their file: " + e.Message);
            }
        }

        // ─── A HAND-SET ROW IS NEVER IN FORCE UNTIL IT IS ARMED ───
        //
        // These rows used to load and apply themselves at the first tick of every session. On
        // 2026-09-22 that put seven rows written for the hangar's eight doors onto seven of its
        // NEW side windows, forcing each to the door's 1.21 x 5.38 m: tall rectangles standing up
        // through the roof. A row is addressed by its position in the building's window list, and
        // adding the 34 side windows renumbered every one of them. Nothing said so, in any log.
        //
        // So the file is still read - nothing tuned is ever lost, which is why the file exists at
        // all - but the rows sit in a PENDING table and reach nothing until WindowFitDev.Arm() is
        // called. A verdict taken without arming is therefore a verdict on the DLL alone, which is
        // the owner's standing standard, and it is true by default rather than by remembering.
        private static void LoadLiveWindowFits()
        {
            if (s_WindowFitsLiveLoaded) return;
            s_WindowFitsLiveLoaded = true;
            try
            {
                if (!System.IO.File.Exists(WindowFitLivePath)) return;
                var c = CultureInfo.InvariantCulture;
                int read = 0;
                foreach (string line in System.IO.File.ReadAllLines(WindowFitLivePath))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] p = line.Split('|');
                    if (p.Length != 11 && p.Length != 14 && p.Length != 16 && p.Length != 18) continue;  // 11: before the view's three numbers; 14: before the stamp; 16: before the stretch
                    float viewStart = p.Length >= 14 && p[11].Length > 0 ? float.Parse(p[11], c) : float.NaN;
                    float roomRight = p.Length >= 14 ? float.Parse(p[12], c) : 0f, roomUp = p.Length >= 14 ? float.Parse(p[13], c) : 0f;
                    float roomTaller = p.Length == 18 ? float.Parse(p[16], c) : 0f, roomWider = p.Length == 18 ? float.Parse(p[17], c) : 0f;
                    var fit = new WindowFit(int.Parse(p[1], c),
                        float.Parse(p[2], c), float.Parse(p[3], c), float.Parse(p[4], c), float.Parse(p[5], c),
                        float.Parse(p[6], c), float.Parse(p[7], c), float.Parse(p[8], c), float.Parse(p[9], c), int.Parse(p[10], c), viewStart, roomRight, roomUp, roomTaller, roomWider);
                    Dictionary<int, WindowFit> pending;
                    if (!s_WindowFitsPending.TryGetValue(p[0], out pending)) { pending = new Dictionary<int, WindowFit>(); s_WindowFitsPending[p[0]] = pending; }
                    pending[fit.Window] = fit;
                    float stampW = p.Length >= 16 && p[14].Length > 0 ? float.Parse(p[14], c) : float.NaN;
                    float stampH = p.Length >= 16 && p[15].Length > 0 ? float.Parse(p[15], c) : float.NaN;
                    s_WindowFitStamp[p[0] + "|" + fit.Window] = new Vector2(stampW, stampH);
                    read++;
                }
                if (read == 0) return;

                // Named, not just counted: a count is what was skipped past on 2026-09-22.
                var waiting = new System.Text.StringBuilder();
                foreach (var shell in s_WindowFitsPending)
                {
                    waiting.Append(shell.Key).Append(" windows ");
                    var indices = new List<int>(shell.Value.Keys);
                    indices.Sort();
                    for (int i = 0; i < indices.Count; i++) { if (i > 0) waiting.Append(","); waiting.Append(indices[i]); }
                    waiting.Append("; ");
                }
                MelonLogger.Msg($"[PENCERE-AYARI] Elle ayarlanmis {read} satir okundu ama HICBIRI YURURLUKTE DEGIL: {waiting}. Uygulamak icin WindowFitDev.Arm().",
                                $"[WINDOW-FIT] {read} hand-set rows were read and NONE OF THEM IS IN FORCE: {waiting}. Call WindowFitDev.Arm() to apply them; until then every window is as the DLL measured it.");
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning("[WINDOW-FIT] The hand-set windows file could not be read, so it is ignored: " + e.Message);
            }
        }

        // ─── THE HANDLES THE SLIDER PANELS HOLD ───
        //
        // Every call is about the building nearest the player that has its windows set up, and says
        // which one that is, so the same panels serve at every building. Lengths in centimeters.
        public static class WindowFitDev
        {
            // Hand-set rows are read at load but reach NOTHING until this is set true, once per
            // session, deliberately. See ArmLiveWindowFits.
            public static bool Armed
            {
                get { return s_WindowFitsArmed; }
                set { ArmLiveWindowFits(value); }
            }

            public static string ArmedDescribe()
            {
                if (!s_WindowFitsLiveLoaded) LoadLiveWindowFits();
                int pending = 0;
                foreach (var shell in s_WindowFitsPending) pending += shell.Value.Count;
                int live = 0;
                foreach (var shell in s_WindowFitsLive) live += shell.Value.Count;
                return s_WindowFitsArmed
                    ? live + " hand-set rows IN FORCE (" + pending + " read from the file)"
                    : pending + " hand-set rows read and NONE in force; every window is as the DLL measured it";
            }

            public static readonly string[] Fields = { "PortalRight", "PortalUp", "PortalWidth", "PortalHeight", "FrostRight", "FrostUp", "FrostWidth", "FrostHeight", "FrostSource", "ViewStartCm", "RoomRightCm", "RoomUpCm", "RoomTallerPercent", "RoomWiderPercent" };

            // How much of the view's fit to its room window is applied, percent: 0 is the pure slide,
            // 100 lays the room's opening exactly over the building's. Every building, at once.
            public static float ViewFitPercent
            {
                get { return OutsideInViewFit * 100f; }
                set { OutsideInViewFit = Mathf.Clamp01(value * 0.01f); }
            }

            // Where in the room a view from outside starts: 0 where the room-window finder put it, 1 at the
            // outer face of the room's own frame (the default), 2 behind the frame. Takes hold by
            // setting every building's windows up again.
            public static int CutMode
            {
                get { return OutsideInCutMode; }
                set
                {
                    int mode = Mathf.Clamp(value, 0, 2);
                    if (OutsideInCutMode == mode) return;
                    OutsideInCutMode = mode;
                    s_OutsideInRebuildAsked = true;
                }
            }

            // How far the picture reaches past the hole on every side, cm, and the fit's multiplier, percent
            // (under 100 draws the room larger). Both take hold by setting every building up again.
            public static float ViewPastHoleCm
            {
                get { return OutsideInViewPastHole * 100f; }
                set { float v = Mathf.Clamp(value, 0f, 30f) * 0.01f; if (Mathf.Approximately(v, OutsideInViewPastHole)) return; OutsideInViewPastHole = v; s_OutsideInRebuildAsked = true; }
            }
            public static float ViewZoomPercent
            {
                get { return OutsideInViewZoom * 100f; }
                set { float v = Mathf.Clamp(value, 50f, 150f) * 0.01f; if (Mathf.Approximately(v, OutsideInViewZoom)) return; OutsideInViewZoom = v; s_OutsideInRebuildAsked = true; }
            }

            // Every window's frost together, cm: wider, taller, slid right, slid up. Applied through the
            // mod's own frost builder at most ten times a second; kept for the session, through walking
            // indoors and out, and gone at a relaunch until the settled numbers are written in.
            public static float FrostAllWiderCm { get { return s_FrostAllWiderCm; } set { s_FrostAllWiderCm = Mathf.Clamp(value, -60f, 60f); s_FrostAllBakeAsked = true; } }
            public static float FrostAllTallerCm { get { return s_FrostAllTallerCm; } set { s_FrostAllTallerCm = Mathf.Clamp(value, -60f, 60f); s_FrostAllBakeAsked = true; } }
            public static float FrostAllRightCm { get { return s_FrostAllRightCm; } set { s_FrostAllRightCm = Mathf.Clamp(value, -30f, 30f); s_FrostAllBakeAsked = true; } }
            public static float FrostAllUpCm { get { return s_FrostAllUpCm; } set { s_FrostAllUpCm = Mathf.Clamp(value, -30f, 30f); s_FrostAllBakeAsked = true; } }
            public static string FrostAllDescribe()
            {
                var building = NearestOutsideInBuilding();
                string size = "";
                if (building != null && building.FrostRects.Count > 0)
                    for (int a = 0; a < building.FrostRects.Count; a++)
                    {
                        Vector4 r = building.FrostRects[a];
                        if (r.y - r.x < 0.01f) continue;
                        size = "; e.g. window " + a + "'s frost covers " + ((r.y - r.x) * 100f).ToString("F1") + " x " + ((r.w - r.z) * 100f).ToString("F1") + " cm on a pane of " + (building.Apertures[a].BaseWidth * 100f).ToString("F1") + " x " + (building.Apertures[a].BaseHeight * 100f).ToString("F1");
                        break;
                    }
                return "every window's frost: " + FrostAllWiderCm.ToString("F1") + " wider, " + FrostAllTallerCm.ToString("F1") + " taller, " + FrostAllRightCm.ToString("F1") + " right, " + FrostAllUpCm.ToString("F1") + " up (cm)" + size;
            }

            // Everything toward the viewer, cm. Read every frame by the views; the frost and the backing
            // follow when the building's windows are next set up.
            public static float ViewTowardViewerCm
            {
                get { return OutsideInViewTowardViewer * 100f; }
                set { float v = Mathf.Clamp(value, -10f, 15f) * 0.01f; if (Mathf.Approximately(v, OutsideInViewTowardViewer)) return; OutsideInViewTowardViewer = v; s_OutsideInRebuildAsked = true; }
            }

            // Switch one window's view off or on in the running game, nearest building. Not kept: a
            // window that should stay off goes into WindowsLeftAlone in s_BuildingWindowOverrides.
            public static string SetNoView(int window, bool noView)
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null) return "no building has its windows set up";
                string shellName = building.Instance.Config.ExteriorShellPrefabName;
                HashSet<int> live;
                if (!s_WindowsWithNoViewLive.TryGetValue(shellName, out live)) { live = new HashSet<int>(); s_WindowsWithNoViewLive[shellName] = live; }
                if (noView) live.Add(window); else live.Remove(window);
                MirrorRigIntoTable(shellName);
                s_OutsideInRebuildAsked = true;
                return building.InstanceId + " window " + window + (HasNoView(shellName, window) ? ": left alone (the building's own glass)" : ": view on");
            }

            public static string ViewFitDescribe()
            {
                var building = NearestOutsideInBuilding();
                if (building == null) return "no building has its windows set up";
                var text = new System.Text.StringBuilder(building.InstanceId + " at " + (OutsideInViewFit * 100f).ToString("F0") + "% fit; full fit per window, across x up: ");
                for (int a = 0; a < building.Apertures.Count; a++)
                {
                    var pane = building.Apertures[a];
                    if (!pane.Paired || pane.Pretend || pane.ViewFollows >= 0) continue;
                    text.Append(a + ": " + (pane.FitAcross * 100f).ToString("F0") + "x" + (pane.FitUp * 100f).ToString("F0") + "  ");
                }
                return text.ToString();
            }

            public static string Target()
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null) return "no building has its windows set up";
                return building.InstanceId + " (" + building.Instance.Config.ExteriorShellPrefabName + "), " + building.Apertures.Count + " windows, " + building.ShellDistance.ToString("F0") + " m away";
            }

            // ONE PANEL FOR THE WINDOW PICKED HERE, instead of a panel per window index. The panels of
            // 2026-09-18 held sliders with fixed absolute ranges (a height of 20 to 160 cm), and a
            // panel built at a window outside that range clamped the window's real size to the
            // slider's limit and wrote it back: the hangar's 538 cm window 1 went black on the panel
            // being opened (2026-09-21). Owner: "These controls suck. Retire these panels. Give me
            // ones that will be helpful." So every size lever below is RELATIVE to what the mod
            // measured, 0 = as measured, and a fresh panel reads zeros and can write nothing wrong.
            public static int Window
            {
                get { return s_DevWindow; }
                set { var building = NearestOutsideInBuilding(); int count = building != null ? building.Apertures.Count : 0; s_DevWindow = count > 0 ? Mathf.Clamp(value, 0, count - 1) : Mathf.Max(0, value); }
            }
            private static int s_DevWindow;

            // A field name of the relative kind, and the absolute one it stands for.
            private static bool Relative(string field, out string absolute)
            {
                switch (field)
                {
                    case "PortalWider": absolute = "PortalWidth"; return true;
                    case "PortalTaller": absolute = "PortalHeight"; return true;
                    case "FrostWider": absolute = "FrostWidth"; return true;
                    case "FrostTaller": absolute = "FrostHeight"; return true;
                }
                absolute = field; return false;
            }

            // What a relative field is measured from: the pane as measured for the picture, and the
            // picture itself for the frost, so "frost wider 1" means one centimeter past the picture.
            private static float RelativeBase(OutsideInBuilding building, int window, WindowFit fit, string absolute)
            {
                var pane = building.Apertures[window];
                switch (absolute)
                {
                    case "PortalWidth": return pane.BaseWidth * 100f;
                    case "PortalHeight": return pane.BaseHeight * 100f;
                    case "FrostWidth": return fit.PortalWidth;
                    case "FrostHeight": return fit.PortalHeight;
                }
                return 0f;
            }

            public static bool GetNoView(int window)
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null) return false;
                return HasNoView(building.Instance.Config.ExteriorShellPrefabName, window);
            }

            // The same value on every window of the nearest building: a row of identical windows
            // (the hangar's eight) is set once.
            public static void SetAll(string field, float value)
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null) return;
                for (int w = 0; w < building.Apertures.Count; w++) Set(w, field, value);
            }

            // The value in force: the hand-set one where there is one, otherwise what the mod built.
            public static float Get(int window, string field)
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null || window < 0 || window >= building.Apertures.Count) return 0f;
                string absolute; WindowFit fit = InForce(building, window);
                if (Relative(field, out absolute)) return Read(fit, absolute) - RelativeBase(building, window, fit, absolute);
                return Read(fit, field);
            }

            public static void Set(int window, string field, float value)
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null || window < 0 || window >= building.Apertures.Count) return;
                string shellName = building.Instance.Config.ExteriorShellPrefabName;

                WindowFit fit = InForce(building, window);
                string absolute;
                if (Relative(field, out absolute)) { value += RelativeBase(building, window, fit, absolute); field = absolute; }
                // A panel hands every slider its own value back when it is built. That is not a hand
                // on the window, and storing it would pin six untouched windows' frost in place.
                if (Mathf.Abs(Read(fit, field) - value) < 0.01f) return;
                switch (field)
                {
                    case "PortalRight": fit.PortalRight = value; break;
                    case "PortalUp": fit.PortalUp = value; break;
                    case "PortalWidth": fit.PortalWidth = Mathf.Max(value, 1f); break;
                    case "PortalHeight": fit.PortalHeight = Mathf.Max(value, 1f); break;
                    case "FrostRight": fit.FrostRight = value; break;
                    case "FrostUp": fit.FrostUp = value; break;
                    case "FrostWidth": fit.FrostWidth = Mathf.Max(value, 1f); break;
                    case "FrostHeight": fit.FrostHeight = Mathf.Max(value, 1f); break;
                    case "FrostSource": fit.FrostSource = Mathf.RoundToInt(value); break;
                    case "ViewStartCm": fit.ViewStartCm = Mathf.Clamp(value, -60f, 30f); fit.HasViewStart = true; break;
                    case "RoomRightCm": fit.RoomRightCm = Mathf.Clamp(value, -150f, 150f); break;
                    case "RoomUpCm": fit.RoomUpCm = Mathf.Clamp(value, -150f, 150f); break;
                    case "RoomTallerPercent": fit.RoomTallerPercent = Mathf.Clamp(value, -50f, 150f); break;
                    case "RoomWiderPercent": fit.RoomWiderPercent = Mathf.Clamp(value, -50f, 150f); break;
                    default: return;
                }
                Store(shellName, fit);
            }

            // The frost laid over the picture of the room, half a centimeter past it all round.
            public static void FrostToPortal(int window)
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null || window < 0 || window >= building.Apertures.Count) return;
                WindowFit fit = InForce(building, window);
                fit.FrostRight = fit.PortalRight; fit.FrostUp = fit.PortalUp;
                fit.FrostWidth = fit.PortalWidth + 1f; fit.FrostHeight = fit.PortalHeight + 1f;
                Store(building.Instance.Config.ExteriorShellPrefabName, fit);
            }

            // Back to what the table says, or to what the mod measures where the table says nothing.
            public static void Forget(int window)
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null) return;
                RigForget(building.Instance.Config.ExteriorShellPrefabName, window);
            }

            public static string Describe(int window)
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null) return "no building has its windows set up";
                if (window < 0 || window >= building.Apertures.Count) return building.InstanceId + " has no window " + window;
                var pane = building.Apertures[window];
                WindowFit fit = InForce(building, window);
                WindowFit unused;
                bool byHand = TryGetWindowFit(building.Instance.Config.ExteriorShellPrefabName, window, out unused);
                return building.InstanceId + " window " + window + (byHand ? " (set by hand)" : " (as measured)")
                     + ": picture " + fit.PortalWidth.ToString("F1") + " x " + fit.PortalHeight.ToString("F1") + " cm, measured " + (pane.BaseWidth * 100f).ToString("F1") + " x " + (pane.BaseHeight * 100f).ToString("F1")
                     + "; frost " + fit.FrostWidth.ToString("F1") + " x " + fit.FrostHeight.ToString("F1") + " cm, from window " + (fit.FrostSource >= 0 ? fit.FrostSource.ToString() : fit.FrostSource == -2 ? "its own" : "the mod's choice");
            }

            // Rows ready to paste into Fits in s_BuildingWindowOverrides, for the nearest building's shell.
            public static string Print()
            {
                var building = NearestOutsideInBuilding();
                if (building == null || building.Instance == null) return "no building has its windows set up";
                string shellName = building.Instance.Config.ExteriorShellPrefabName;

                var text = new System.Text.StringBuilder();
                text.AppendLine("            // " + building.InstanceId);
                text.AppendLine("            {");
                text.AppendLine("                \"" + shellName + "\", new[]");
                text.AppendLine("                {");
                int rows = 0;
                for (int a = 0; a < building.Apertures.Count; a++)
                {
                    WindowFit fit;
                    if (!TryGetWindowFit(shellName, a, out fit)) continue;
                    text.AppendLine("                    " + WindowFitRow(fit));
                    rows++;
                }
                text.AppendLine("                }");
                text.AppendLine("            },");
                if (rows == 0) return building.InstanceId + ": no window of this building is set by hand";
                MelonLogger.Msg("[WINDOW-FIT] Rows for Fits in s_BuildingWindowOverrides:\n" + text);
                return text.ToString();
            }

            // For the frost bake, which lives outside this class and cannot name OutsideInBuilding.
            internal static WindowFit InForceAtNearest(int window)
            {
                return InForce(NearestOutsideInBuilding(), window);
            }

            private static WindowFit InForce(OutsideInBuilding building, int window)
            {
                WindowFit fit;
                bool byHand = TryGetWindowFit(building.Instance.Config.ExteriorShellPrefabName, window, out fit);
                if (!byHand) fit = new WindowFit(window, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, -1);

                var pane = building.Apertures[window];
                if (fit.PortalWidth <= 0f) fit.PortalWidth = pane.BaseWidth * 100f;
                if (fit.PortalHeight <= 0f) fit.PortalHeight = pane.BaseHeight * 100f;
                if ((fit.FrostWidth <= 0f || fit.FrostHeight <= 0f) && window < building.FrostRects.Count)
                {
                    Vector4 built = building.FrostRects[window];
                    fit.FrostRight = (built.x + built.y) * 50f; fit.FrostUp = (built.z + built.w) * 50f;
                    fit.FrostWidth = (built.y - built.x) * 100f; fit.FrostHeight = (built.w - built.z) * 100f;
                }
                // The view start in force, so a new lever starts from what is on screen and a panel
                // handing the value back is not taken for a hand on the window.
                if (!fit.HasViewStart && window < building.ApertureOpenings.Count && pane.Paired && !pane.Pretend)
                {
                    var opening = building.ApertureOpenings[window];
                    fit.ViewStartCm = Vector3.Dot(RoomPointBehindPane(opening, building.Instance.Config.ExteriorShellPrefabName, window) - opening.GlassCenter, opening.Outward) * 100f;
                }
                return fit;
            }

            private static float Read(WindowFit fit, string field)
            {
                switch (field)
                {
                    case "PortalRight": return fit.PortalRight;
                    case "PortalUp": return fit.PortalUp;
                    case "PortalWidth": return fit.PortalWidth;
                    case "PortalHeight": return fit.PortalHeight;
                    case "FrostRight": return fit.FrostRight;
                    case "FrostUp": return fit.FrostUp;
                    case "FrostWidth": return fit.FrostWidth;
                    case "FrostHeight": return fit.FrostHeight;
                    case "FrostSource": return fit.FrostSource;
                    case "ViewStartCm": return fit.ViewStartCm;
                    case "RoomRightCm": return fit.RoomRightCm;
                    case "RoomUpCm": return fit.RoomUpCm;
                    case "RoomTallerPercent": return fit.RoomTallerPercent;
                    case "RoomWiderPercent": return fit.RoomWiderPercent;
                }
                return 0f;
            }

            private static void Store(string shellName, WindowFit fit)
            {
                RigStore(shellName, fit);
            }
        }
    }

    // The rig's only way in. MelonLoader applies every [HarmonyPatch] in the assembly when the mod
    // loads, so nothing in the mod calls this. A probe that throws retires itself, once.
    [HarmonyLib.HarmonyPatch(typeof(SeamlessInteriorsMod), nameof(SeamlessInteriorsMod.OnLateUpdate))]
    internal static class WindowFitRigTick
    {
        private static bool s_Failed;

        private static void Postfix()
        {
            if (s_Failed) return;
            try { SeamlessInteriorsMod.TickWindowFitRefit(); }
            catch (System.Exception e)
            {
                s_Failed = true;
                MelonLogger.Error("[WINDOW-FIT] The tuning rig threw and has stopped for this session: " + e);
            }
        }
    }
}
