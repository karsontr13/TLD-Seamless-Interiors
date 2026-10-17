using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── ONE CLEANED-OR-FROSTED STATE PER WINDOW, SHARED BY BOTH DIRECTIONS ───
        //
        // The owner's requirement, 2026-09-18: a window's frosted state must match from inside and
        // from outside, and an interaction will eventually let the player clean one window, which
        // must then be clean on both sides.
        //
        // Inside-out and outside-in are never live together, so they cannot tell each other
        // anything. They share this instead. A window is identified by its index in the author's
        // own s_ShellWindows table for the building's shell: the outside-in apertures are built in
        // that order, and the inside-out pairing finds that index for each interior opening, so
        // both sides already agree on it without any new bookkeeping.
        //
        // The default for a window nobody has touched is FROSTED, which is how the base game draws
        // it. The "Windows Start Cleaned" preference turns that round for anyone who wants it. Its
        // predecessor governed the inside only and defaulted to clear, which is what made the two
        // sides disagree out of the box: wiped clear from inside, frosted from outside.
        //
        // PERSISTED THE WAY THE MOD ALREADY PERSISTS A BUILDING'S JUNK FLAG (see Junk.cs): one small
        // hand-written file per save per building, <saveName>_<instanceId>_windows.json in
        // ModPaths.DataDir(), written from the same SaveSceneData patch as everything else. Following
        // that convention is what buys the protections the author already built: the playthrough
        // identity check in SaveIdentity.cs and the clean-up in SaveDeletion.cs both work on
        // "<saveName>_*", so this file is covered by both without either knowing it exists.
        //
        //   WRITTEN ONLY WHEN THE GAME SAVES, never at the moment a window changes. Otherwise
        //   cleaning a window and quitting without saving would leave the file ahead of the save.
        //
        //   LOADED ON FIRST USE, not at hydration. Outside-in shows a building's windows from the
        //   yard, before its contents have been hydrated, and needs the recorded state then.
        //
        //   FORGOTTEN AT THE MAIN MENU, which is the author's own rule for anything remembered
        //   about a save (see OnSceneWasInitialized) and which every route to a different save
        //   passes through. This state is keyed by building, not by save, so without that it would
        //   carry one save's windows into the next, including back into the SAME save reloaded
        //   after quitting without saving. NOT at hydration: that also happens mid-session, and
        //   would throw away a window changed from the yard and not yet saved.
        //
        // Only windows something has actually set are recorded. A building nobody has touched gets
        // no file, and a save from before this feature loads exactly as it did.
        private static readonly Dictionary<string, Dictionary<int, bool>> s_WindowCleaned
            = new Dictionary<string, Dictionary<int, bool>>();

        // ROOM WINDOWS WITH NO WINDOW OUTSIDE (owner, 2026-10-01). Barn A's room has 13 windows and
        // its building 10; room windows 0, 11 and 12 pair with nothing, so they had no record above
        // and fell back to frosted at every entry while the ten paired ones came back clean: "Some
        // windows start with them cleared and some start with them not cleared." There is no
        // outside to match, so they are recorded by their own room window number (the index into
        // s_WindowPortalOpenings, the number the room tags and the hand pairing use), in the same
        // file under their own keys. The clean-the-window hover trigger goes through
        // SetWindowPaneCleared, which routes here for these, so it is remembered by the mod itself.
        private static readonly Dictionary<string, Dictionary<int, bool>> s_RoomWindowCleaned
            = new Dictionary<string, Dictionary<int, bool>>();

        // ─── A CLEANED WINDOW FROSTS OVER AGAIN ───
        //
        // Owner, 2026-10-10: a cleaned window should not stay clean for ever. The game has no frost
        // of its own to follow (it is painted art), so the mod keeps the game hour each window was
        // cleaned at and calls it frosted again once "Windows Frost Over Again After" hours have
        // passed. Game hours played, so sleeping and passing time count and a reload does not.
        //
        // DECIDED WHEN THE WINDOW IS ASKED ABOUT, not on a timer: a building nobody is near costs
        // nothing, and one visited a week later is already frosted when it is first drawn.
        //
        // The whole window comes back at once, so it waits until the window is no longer being drawn
        // (owner, 2026-10-10: "defer changing it until the windows become inactive"). A window that
        // is due stays clean while it is on the player's screen from inside its room, and from
        // outside while its own view into the room is showing. It is frosted as soon as neither is.
        // See IsWindowBeingDrawn.
        //
        // Per window inside as well. The rule first held every window of the room the player was
        // in, and he sat in the Camp Office through a blizzard with a window 204 hours clean
        // against 48 and it never frosted (read live, 2026-10-10).
        //
        // Per window, not per building, from outside. The first rule held every window of a
        // building while any of its views showed, and the owner walked round and round the Camp
        // Office with three windows 61 hours clean and none frosting: the building's level never
        // left 1 (read live, 2026-10-10).
        //
        // A window cleaned before this existed has no hour on record. Its hours start counting the
        // first time it is asked about, so an old save's clean windows do not all frost at load.
        private static readonly Dictionary<string, Dictionary<int, float>> s_WindowCleanedHour
            = new Dictionary<string, Dictionary<int, float>>();
        private static readonly Dictionary<string, Dictionary<int, float>> s_RoomWindowCleanedHour
            = new Dictionary<string, Dictionary<int, float>>();

        private static MelonPreferences_Entry<float> s_PrefWindowFrostReturnHours;
        public static float WindowFrostReturnHours
        {
            get { return s_PrefWindowFrostReturnHours != null ? s_PrefWindowFrostReturnHours.Value : 48f; }
            set { if (s_PrefWindowFrostReturnHours != null) s_PrefWindowFrostReturnHours.Value = value; }
        }

        // Asked for every window every frame from outside, so read from the game once a frame.
        private static int s_WindowHoursFrame = -1;
        private static float s_WindowGameHoursNow;
        private static float WindowGameHoursNow()
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame != s_WindowHoursFrame)
            {
                s_WindowHoursFrame = frame;
                s_WindowGameHoursNow = GetHoursPlayedSafe();
            }
            return s_WindowGameHoursNow;
        }

        // The hour every window is timed against: game hours played, plus the hours heavy weather
        // has added (see HEAVY WEATHER below).
        private static float WindowHoursNow()
        {
            EnsureWindowFrostBonusLoaded();
            return WindowGameHoursNow() + s_WindowFrostBonusHours;
        }

        // ─── HEAVY WEATHER BRINGS THE FROST BACK SOONER ───
        //
        // Owner, 2026-10-10. In heavy snow the frost returns twice as fast and in a blizzard four
        // times (both are preferences). Kept as ONE number for the save, the bonus hours: once a
        // second, each game hour that passed in heavy weather adds rate - 1 hours to it, and every
        // window is timed against game hours plus the bonus. So the hours on record per window mean
        // what they always did, nothing is tracked per building, and every region's windows age
        // alike under the weather the player is in, which is the only weather the game runs.
        //
        // Saved as <saveName>_windowfrost.json with the window files, at the game's own save, and
        // read on first use for a save. A save without the file has no bonus.
        private static MelonPreferences_Entry<float> s_PrefWindowFrostHeavySnowRate;
        private static MelonPreferences_Entry<float> s_PrefWindowFrostBlizzardRate;
        public static float WindowFrostHeavySnowRate
        {
            get { return s_PrefWindowFrostHeavySnowRate != null ? s_PrefWindowFrostHeavySnowRate.Value : 2f; }
            set { if (s_PrefWindowFrostHeavySnowRate != null) s_PrefWindowFrostHeavySnowRate.Value = value; }
        }
        public static float WindowFrostBlizzardRate
        {
            get { return s_PrefWindowFrostBlizzardRate != null ? s_PrefWindowFrostBlizzardRate.Value : 4f; }
            set { if (s_PrefWindowFrostBlizzardRate != null) s_PrefWindowFrostBlizzardRate.Value = value; }
        }

        private static float s_WindowFrostBonusHours;
        private static string s_WindowFrostBonusSaveName;
        private static bool s_WindowFrostBonusOnDisk;
        private static float s_WindowFrostWeatherTickAt;
        private static float s_WindowFrostWeatherLastHour = -1f;
        private static float s_WindowFrostWeatherRate = 1f;
        private static bool s_WindowFrostWeatherFailed;

        // Longer than this between two looks is a load or another save, not time that passed in
        // this weather. Sleeping and passing time are seen an hour or so at a time.
        private const float WINDOW_FROST_WEATHER_MAX_STEP_HOURS = 6f;

        private static string GetWindowFrostBonusSavePath()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return null;
            return Path.Combine(ModPaths.DataDir(), saveName + "_windowfrost.json");
        }

        private static void EnsureWindowFrostBonusLoaded()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (saveName == s_WindowFrostBonusSaveName) return;
            s_WindowFrostBonusSaveName = saveName;
            s_WindowFrostBonusHours = 0f;
            s_WindowFrostBonusOnDisk = false;
            s_WindowFrostWeatherLastHour = -1f;
            s_WindowFrostWeatherRate = 1f;
            s_WindowSnowHours = 0f;
            s_WindowSnowWeight = 0f;

            try
            {
                string path = GetWindowFrostBonusSavePath();
                if (path == null || !SaveFiles.Exists(path)) return;
                s_WindowFrostBonusOnDisk = true;

                string json = SaveFiles.Read(path);
                int at = json.IndexOf("\"bonusHours\"", System.StringComparison.Ordinal);
                int colon = at < 0 ? -1 : json.IndexOf(':', at);
                int end = colon < 0 ? -1 : json.IndexOfAny(new[] { ',', '}' }, colon);
                float hours;
                if (end > colon && float.TryParse(json.Substring(colon + 1, end - colon - 1).Trim(), System.Globalization.NumberStyles.Float,
                                                  System.Globalization.CultureInfo.InvariantCulture, out hours) && hours > 0f)
                    s_WindowFrostBonusHours = hours;
                s_WindowSnowHours = Mathf.Max(0f, ReadJsonFloat(json, "\"snowHours\"", 0f));
            }
            catch (System.Exception ex)
            {
                // Unreadable is treated as absent: no bonus, as before this existed.
                MelonLogger.Warning($"[PENCERE-BUZ] Hava dosyasi okunamadi: {ex.Message}",
                                    $"[WINDOW-FROST] The heavy weather file could not be read: {ex.Message}");
            }
        }

        private static void SaveWindowFrostBonus()
        {
            if (SaveGameSystem.m_CurrentSaveName != s_WindowFrostBonusSaveName) return;
            // No heavy weather yet and nothing on disk: leave it that way.
            if (s_WindowFrostBonusHours <= 0f && s_WindowSnowHours <= 0f && !s_WindowFrostBonusOnDisk) return;
            string path = GetWindowFrostBonusSavePath();
            if (path == null) return;
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            JsonWriteCache.Write(path, "{\"v\":1,\"bonusHours\":" + s_WindowFrostBonusHours.ToString("0.###", invariant)
                                     + ",\"snowHours\":" + s_WindowSnowHours.ToString("0.###", invariant) + "}");
            s_WindowFrostBonusOnDisk = true;
        }

        private static void TickWindowFrostWeather()
        {
            if (s_WindowFrostWeatherFailed) return;
            float real = UnityEngine.Time.realtimeSinceStartup;
            if (real < s_WindowFrostWeatherTickAt) return;
            s_WindowFrostWeatherTickAt = real + 1f;

            try
            {
                EnsureWindowFrostBonusLoaded();

                // A load: whatever the clock does across it is not time spent in this weather.
                float now = WindowGameHoursNow();
                if (s_ScreenHeldBlack || IsAnyCloningActive() || now < 0.05f) { s_WindowFrostWeatherLastHour = -1f; return; }

                float last = s_WindowFrostWeatherLastHour;
                s_WindowFrostWeatherLastHour = now;

                Weather weather = GameManager.GetWeatherComponent();
                if (weather == null) return;
                WeatherStage stage = weather.GetWeatherStage();
                float rate = stage == WeatherStage.Blizzard ? WindowFrostBlizzardRate
                           : stage == WeatherStage.HeavySnow ? WindowFrostHeavySnowRate : 1f;
                if (rate < 1f) rate = 1f;

                // The hours since the last look are counted at the rate that held over them.
                float passed = now - last;
                if (last >= 0f && passed > 0f && passed <= WINDOW_FROST_WEATHER_MAX_STEP_HOURS && s_WindowFrostWeatherRate > 1f)
                    s_WindowFrostBonusHours += passed * (s_WindowFrostWeatherRate - 1f);
                // Snow on the windows, counted the same way at the weight that held over those hours.
                if (last >= 0f && passed > 0f && passed <= WINDOW_FROST_WEATHER_MAX_STEP_HOURS && s_WindowSnowWeight > 0f)
                    s_WindowSnowHours += passed * s_WindowSnowWeight;
                s_WindowSnowWeight = WindowSnowWeightOf(stage);

                if (rate != s_WindowFrostWeatherRate)
                {
                    s_WindowFrostWeatherRate = rate;
                    MelonLogger.Msg($"[PENCERE-BUZ] Hava: {stage}. Buz {rate:0.#} kat hizla donuyor (eklenen toplam {s_WindowFrostBonusHours:F1} saat).",
                                    $"[WINDOW-FROST] Weather: {stage}. Frost returns at {rate:0.#}x ({s_WindowFrostBonusHours:F1} hours added by heavy weather so far).");
                }
            }
            catch (System.Exception ex)
            {
                // Retire on the first failure rather than throw every second; the windows keep working.
                s_WindowFrostWeatherFailed = true;
                MelonLogger.Warning($"[PENCERE-BUZ] Hava kontrolu kapatildi: {ex.Message}",
                                    $"[WINDOW-FROST] The weather check turned off after an error: {ex.Message}");
            }
        }

        private static void StampWindowCleanedHour(Dictionary<string, Dictionary<int, float>> hours, string instanceId, int window, bool cleaned)
        {
            Dictionary<int, float> at;
            if (!hours.TryGetValue(instanceId, out at))
            {
                if (!cleaned) return;
                at = new Dictionary<int, float>();
                hours[instanceId] = at;
            }
            if (cleaned) at[window] = WindowHoursNow();
            else at.Remove(window);
        }

        // True when a window on record as cleaned has been clean long enough, and the record now
        // says frosted. Changes the record only; whoever draws the window follows it.
        private static bool HasWindowFrostedOverAgain(Dictionary<string, Dictionary<int, float>> hours, Dictionary<int, bool> building,
                                                      string instanceId, int window, bool roomWindow)
        {
            // The game's clock reads 0 until a load has restored it.
            if (WindowGameHoursNow() < 0.05f) return false;
            float now = WindowHoursNow();

            Dictionary<int, float> at;
            if (!hours.TryGetValue(instanceId, out at))
            {
                at = new Dictionary<int, float>();
                hours[instanceId] = at;
            }
            float hour;
            if (!at.TryGetValue(window, out hour))
            {
                at[window] = now;
                return false;
            }

            float after = WindowFrostReturnHours;
            if (after <= 0f || now - hour < after) return false;
            // The cover layers grow the frost in sight, so the record can turn while seen; the old
            // all-at-once frost waits until the window is out of sight.
            if ((!WindowCoverLayers || !WindowCoverBuildUp) && IsWindowBeingDrawn(instanceId, window, roomWindow)) return false;

            building[window] = false;
            at.Remove(window);
            MelonLogger.Msg($"[PENCERE-BUZ] {instanceId}: {(roomWindow ? "oda" : "bina")} penceresi {window} yeniden buzlandi ({now - hour:F1} saat temiz kaldi).",
                            $"[WINDOW-FROST] {instanceId}: {(roomWindow ? "room" : "building")} window {window} frosted over again after {now - hour:F1} hours clean.");
            return true;
        }

        // From inside its room, a window any part of whose opening is inside the player's view (a
        // wall in the way does not count as looking away). From outside, a window whose own view
        // into the room is showing or fading. A window out of sight is changed unseen, and shows
        // what the record then says when it comes into sight.
        private static bool IsWindowBeingDrawn(string instanceId, int window, bool roomWindow)
        {
            if (s_WindowPortalInstanceId == instanceId)
            {
                for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
                {
                    if (roomWindow ? i != window : (i >= s_WindowPortalSpot.Count || s_WindowPortalSpot[i] != window)) continue;
                    var opening = s_WindowPortalOpenings[i];
                    Vector3 corner = opening.PortalCenter - opening.Right * (opening.Width * 0.5f) - opening.Up * (opening.Height * 0.5f);
                    if (IsWindowOpeningOnScreen(opening, corner)) return true;
                }
                return false;
            }
            if (roomWindow) return false;
            for (int h = 0; h < s_OutsideInBuildings.Count; h++)
            {
                var b = s_OutsideInBuildings[h];
                if (b.InstanceId != instanceId || b.Level <= 0f) continue;
                if (OutsideInPaneLevel(b, window) > 0f) return true;
                // The second pane of a double window is drawn by its leader's view.
                int leader = window < b.Apertures.Count ? b.Apertures[window].ViewFollows : -1;
                if (leader >= 0 && OutsideInPaneLevel(b, leader) > 0f) return true;
            }
            return false;
        }

        // ─── A FIRE HOLDS THE FROST OFF ───
        //
        // Owner, 2026-10-10: "if the window's been cleaned, and there's a fire, it stays at zero."
        // While a fire burns in a building, the hours of every cleaned window of that building are
        // kept at now, so the count starts when the fire goes out. It cleans nothing: a frosted
        // window stays frosted beside a fire.
        //
        // Once a second, and only for buildings that have a cleaned window on record. The fires
        // are the Fire components in the building's room, found again every minute, the way the
        // chimney smoke finds them (ExteriorFireEffects.cs, whose own list is not built when smoke
        // and glow are switched off). A building in a region that is not loaded has no room to
        // look in, so its hours run on; a fire left there burns out unseen anyway.
        private class WindowFrostFires
        {
            public UnityEngine.GameObject Room;
            public readonly List<Il2Cpp.Fire> Fires = new List<Il2Cpp.Fire>();
            public float NextScan;
            public bool Burning;
        }

        private static readonly Dictionary<string, WindowFrostFires> s_WindowFrostFires = new Dictionary<string, WindowFrostFires>();
        private static readonly List<string> s_WindowFrostFireIds = new List<string>();
        private static readonly List<int> s_WindowFrostFireWindows = new List<int>();
        private static float s_WindowFrostFireTickAt;
        private static bool s_WindowFrostFireFailed;

        private static void TickWindowFrostFires()
        {
            if (s_WindowFrostFireFailed) return;
            float real = UnityEngine.Time.realtimeSinceStartup;
            if (real < s_WindowFrostFireTickAt) return;
            s_WindowFrostFireTickAt = real + 1f;

            try
            {
                // A load is switching the rooms on and off behind a black screen.
                if (s_ScreenHeldBlack || IsAnyCloningActive()) return;
                if (WindowGameHoursNow() < 0.05f) return;
                float now = WindowHoursNow();

                s_WindowFrostFireIds.Clear();
                foreach (var pair in s_WindowCleanedHour) if (pair.Value.Count > 0) s_WindowFrostFireIds.Add(pair.Key);
                foreach (var pair in s_RoomWindowCleanedHour)
                    if (pair.Value.Count > 0 && !s_WindowFrostFireIds.Contains(pair.Key)) s_WindowFrostFireIds.Add(pair.Key);

                for (int i = 0; i < s_WindowFrostFireIds.Count; i++)
                {
                    string id = s_WindowFrostFireIds[i];
                    if (!IsFireBurningIn(id, real)) continue;
                    HoldWindowHoursAt(s_WindowCleanedHour, id, now);
                    HoldWindowHoursAt(s_RoomWindowCleanedHour, id, now);
                }
            }
            catch (System.Exception ex)
            {
                // Retire on the first failure rather than throw every second; the windows keep working.
                s_WindowFrostFireFailed = true;
                MelonLogger.Warning($"[PENCERE-BUZ] Ates kontrolu kapatildi: {ex.Message}",
                                    $"[WINDOW-FROST] The fire check turned off after an error: {ex.Message}");
            }
        }

        private static void HoldWindowHoursAt(Dictionary<string, Dictionary<int, float>> hours, string instanceId, float now)
        {
            Dictionary<int, float> at;
            if (!hours.TryGetValue(instanceId, out at) || at.Count == 0) return;
            s_WindowFrostFireWindows.Clear();
            foreach (var pair in at) s_WindowFrostFireWindows.Add(pair.Key);
            for (int i = 0; i < s_WindowFrostFireWindows.Count; i++) at[s_WindowFrostFireWindows[i]] = now;
        }

        private static bool IsFireBurningIn(string instanceId, float real)
        {
            SeamlessInteriorInstance instance;
            if (!ActiveInteriors.TryGetValue(instanceId, out instance) || instance == null) return false;
            if (!instance.RunCompleted || instance.InteriorPersisted || instance.MasterInterior == null) return false;

            WindowFrostFires fires;
            if (!s_WindowFrostFires.TryGetValue(instanceId, out fires))
            {
                fires = new WindowFrostFires();
                s_WindowFrostFires[instanceId] = fires;
            }
            if (!ReferenceEquals(fires.Room, instance.MasterInterior) || real >= fires.NextScan)
            {
                fires.Room = instance.MasterInterior;
                fires.NextScan = real + EXTERIOR_FX_FIRE_RESCAN_INTERVAL;
                fires.Fires.Clear();
                foreach (var fire in instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Fire>(true))
                    if (fire != null) fires.Fires.Add(fire);
            }

            bool burning = false;
            // A fire that has caught, not one being lit: the game calls an attempt burning while it
            // lasts, and a failed one held the windows for its nine seconds (owner, 2026-10-10). A
            // lit stove reads FullBurn (read live at the Camp Office the same night).
            for (int i = 0; i < fires.Fires.Count && !burning; i++)
                burning = IsFireStillAlive(fires.Fires[i]) && fires.Fires[i].m_FireState == FireState.FullBurn;

            if (burning != fires.Burning)
            {
                fires.Burning = burning;
                MelonLogger.Msg($"[PENCERE-BUZ] {instanceId}: " + (burning ? "ates yaniyor, temiz pencerelerin suresi sifirda tutuluyor." : "ates sondu, temiz pencerelerin suresi isliyor."),
                                $"[WINDOW-FROST] {instanceId}: " + (burning ? "a fire is burning, cleaned windows are held at zero hours." : "the fire is out, cleaned windows are counting again."));
            }
            return burning;
        }

        // A room the player is standing in holds each pane's state itself (s_WindowPaneCleared), so
        // a pane the record now calls frosted is brought in line here. Once a second.
        private static float s_WindowFrostReturnCheckAt;
        private static bool s_WindowFrostReturnFailed;

        private static void TickWindowFrostReturn()
        {
            if (s_WindowFrostReturnFailed || s_WindowPortalInstanceId == null || WindowFrostReturnHours <= 0f) return;
            float real = UnityEngine.Time.realtimeSinceStartup;
            if (real < s_WindowFrostReturnCheckAt) return;
            s_WindowFrostReturnCheckAt = real + 1f;
            try
            {
                for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
                    if (IsWindowPaneCleared(i) && !IsRoomPaneCleaned(s_WindowPortalInstanceId, i)) ApplyWindowPane(i, false);
            }
            catch (System.Exception ex)
            {
                // Retire on the first failure rather than throw every second; the windows keep working.
                s_WindowFrostReturnFailed = true;
                MelonLogger.Warning($"[PENCERE-BUZ] Oda kontrolu kapatildi: {ex.Message}",
                                    $"[WINDOW-FROST] The check for the room the player is in turned off after an error: {ex.Message}");
            }
        }

        private static bool WindowCleanedDefault()
        {
            return s_PrefClearWindowPanes != null && s_PrefClearWindowPanes.Value;
        }

        // Buildings whose file has been read (or found absent) for the save named below.
        private static readonly HashSet<string> s_WindowStateLoaded = new HashSet<string>();
        private static string s_WindowStateSaveName;

        // A pretend half drawn by its neighbour's view shares that window's record, so inside and outside
        // agree: the room window behind both reads only the neighbour (FarmHouse 17/18, 2026-10-14).
        private static int SharedWindowRecord(string instanceId, int window)
        {
            for (int b = 0; b < s_OutsideInBuildings.Count; b++)
            {
                var building = s_OutsideInBuildings[b];
                if (building == null || building.InstanceId != instanceId) continue;
                if (window >= building.Apertures.Count) return window;
                var pane = building.Apertures[window];
                return pane.Pretend && pane.ViewFollows >= 0 ? pane.ViewFollows : window;
            }
            return window;
        }

        public static bool IsWindowCleaned(string instanceId, int window)
        {
            if (instanceId == null || window < 0) return WindowCleanedDefault();
            window = SharedWindowRecord(instanceId, window);

            EnsureWindowStateLoaded(instanceId);

            Dictionary<int, bool> building;
            bool cleaned;
            if (s_WindowCleaned.TryGetValue(instanceId, out building) && building.TryGetValue(window, out cleaned))
                return cleaned && !HasWindowFrostedOverAgain(s_WindowCleanedHour, building, instanceId, window, false);
            return WindowCleanedDefault();
        }

        // The call a clean-the-window interaction makes. Takes effect on whichever direction is
        // live: outside-in reads this every frame, and inside-out is told directly.
        public static void SetWindowCleaned(string instanceId, int window, bool cleaned)
        {
            if (instanceId == null || window < 0) return;
            window = SharedWindowRecord(instanceId, window);

            // Load first, or the write below would be the only thing on record for this building
            // and the next save would overwrite the file with it.
            EnsureWindowStateLoaded(instanceId);

            Dictionary<int, bool> building;
            if (!s_WindowCleaned.TryGetValue(instanceId, out building))
            {
                building = new Dictionary<int, bool>();
                s_WindowCleaned[instanceId] = building;
            }
            building[window] = cleaned;
            StampWindowCleanedHour(s_WindowCleanedHour, instanceId, window, cleaned);

            if (s_WindowPortalInstanceId == instanceId)
                for (int i = 0; i < s_WindowPortalSpot.Count; i++)
                    if (s_WindowPortalSpot[i] == window) ApplyWindowPane(i, cleaned);
        }

        // The same two calls for a room window that pairs with no window outside. Only the inside
        // ever shows it, so only the inside is told.
        public static bool IsRoomWindowCleaned(string instanceId, int roomWindow)
        {
            if (instanceId == null || roomWindow < 0) return WindowCleanedDefault();

            EnsureWindowStateLoaded(instanceId);

            Dictionary<int, bool> building;
            bool cleaned;
            if (s_RoomWindowCleaned.TryGetValue(instanceId, out building) && building.TryGetValue(roomWindow, out cleaned))
                return cleaned && !HasWindowFrostedOverAgain(s_RoomWindowCleanedHour, building, instanceId, roomWindow, true);
            return WindowCleanedDefault();
        }

        public static void SetRoomWindowCleaned(string instanceId, int roomWindow, bool cleaned)
        {
            if (instanceId == null || roomWindow < 0) return;

            EnsureWindowStateLoaded(instanceId);

            Dictionary<int, bool> building;
            if (!s_RoomWindowCleaned.TryGetValue(instanceId, out building))
            {
                building = new Dictionary<int, bool>();
                s_RoomWindowCleaned[instanceId] = building;
            }
            building[roomWindow] = cleaned;
            StampWindowCleanedHour(s_RoomWindowCleanedHour, instanceId, roomWindow, cleaned);

            if (s_WindowPortalInstanceId == instanceId) ApplyWindowPane(roomWindow, cleaned);
        }

        // Which record a room window follows: its window outside when it has one, its own otherwise.
        private static bool IsRoomPaneCleaned(string instanceId, int roomWindow)
        {
            int window = roomWindow >= 0 && roomWindow < s_WindowPortalSpot.Count ? s_WindowPortalSpot[roomWindow] : -1;
            return window >= 0 ? IsWindowCleaned(instanceId, window) : IsRoomWindowCleaned(instanceId, roomWindow);
        }

        // Every window of the building the player is at, from either side of its door.
        public static void SetAllWindowsCleaned(bool cleaned)
        {
            string instanceId = s_WindowPortalInstanceId ?? s_OutsideInInstanceId;
            if (instanceId == null) return;

            int windows = System.Math.Max(s_OutsideInApertures.Count, 0);
            for (int i = 0; i < s_WindowPortalSpot.Count; i++)
                if (s_WindowPortalSpot[i] + 1 > windows) windows = s_WindowPortalSpot[i] + 1;

            for (int window = 0; window < windows; window++)
            {
                SetWindowCleaned(instanceId, window, cleaned);
                SetWindowSnowCleared(instanceId, window, false, cleaned);
            }

            // Interior openings that paired with no shell window keep their own record.
            if (s_WindowPortalInstanceId == instanceId)
                for (int i = 0; i < s_WindowPortalSpot.Count; i++)
                    if (s_WindowPortalSpot[i] < 0)
                    {
                        SetRoomWindowCleaned(instanceId, i, cleaned);
                        SetWindowSnowCleared(instanceId, i, true, cleaned);
                    }
        }

        public static bool AreAllWindowsCleaned()
        {
            string instanceId = s_WindowPortalInstanceId ?? s_OutsideInInstanceId;
            if (instanceId == null) return WindowCleanedDefault();

            int windows = s_OutsideInApertures.Count;
            for (int i = 0; i < s_WindowPortalSpot.Count; i++)
                if (s_WindowPortalSpot[i] + 1 > windows) windows = s_WindowPortalSpot[i] + 1;
            if (windows == 0) return WindowCleanedDefault();

            for (int window = 0; window < windows; window++)
                if (!IsWindowCleaned(instanceId, window)) return false;
            if (s_WindowPortalInstanceId == instanceId)
                for (int i = 0; i < s_WindowPortalSpot.Count; i++)
                    if (s_WindowPortalSpot[i] < 0 && !IsRoomWindowCleaned(instanceId, i)) return false;
            return true;
        }

        // ─── Persistence ───

        private static string GetWindowStateSavePath(string instanceId)
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName) || string.IsNullOrEmpty(instanceId)) return null;
            return Path.Combine(ModPaths.DataDir(), saveName + "_" + instanceId + "_windows.json");
        }

        public static void ForgetAllWindowStates()
        {
            s_WindowCleaned.Clear();
            s_RoomWindowCleaned.Clear();
            s_WindowCleanedHour.Clear();
            s_RoomWindowCleanedHour.Clear();
            s_WindowSnowFrom.Clear();
            s_RoomWindowSnowFrom.Clear();
            s_WindowFrostFires.Clear();
            s_WindowStateLoaded.Clear();
            s_WindowStateSaveName = null;
            // Read again from the file at the next use, like the windows.
            s_WindowFrostBonusSaveName = null;
            s_WindowFrostBonusHours = 0f;
            s_WindowFrostBonusOnDisk = false;
            s_WindowFrostWeatherLastHour = -1f;
            s_WindowFrostWeatherRate = 1f;
        }

        private static void EnsureWindowStateLoaded(string instanceId)
        {
            // A different save name means everything held here belongs to another playthrough.
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (saveName != s_WindowStateSaveName)
            {
                s_WindowStateSaveName = saveName;
                s_WindowCleaned.Clear();
                s_RoomWindowCleaned.Clear();
                s_WindowCleanedHour.Clear();
                s_RoomWindowCleanedHour.Clear();
                s_WindowSnowFrom.Clear();
                s_RoomWindowSnowFrom.Clear();
                s_WindowFrostFires.Clear();
                s_WindowStateLoaded.Clear();
            }

            if (!s_WindowStateLoaded.Add(instanceId)) return;

            string path = GetWindowStateSavePath(instanceId);
            if (path == null || !SaveFiles.Exists(path)) return;

            try
            {
                string json = SaveFiles.Read(path);
                var building = new Dictionary<int, bool>();
                ReadWindowList(json, "\"cleaned\"", true, building);
                ReadWindowList(json, "\"frosted\"", false, building);
                if (building.Count > 0) s_WindowCleaned[instanceId] = building;

                // Absent from files written before 2026-10-01, which then load as they always did.
                var room = new Dictionary<int, bool>();
                ReadWindowList(json, "\"roomCleaned\"", true, room);
                ReadWindowList(json, "\"roomFrosted\"", false, room);
                if (room.Count > 0) s_RoomWindowCleaned[instanceId] = room;

                // Absent from files written before 2026-10-10: those windows' hours start counting
                // when they are first asked about (HasWindowFrostedOverAgain).
                var hours = new Dictionary<int, float>();
                ReadWindowHours(json, "\"cleanedAt\"", building, hours);
                if (hours.Count > 0) s_WindowCleanedHour[instanceId] = hours;
                var roomHours = new Dictionary<int, float>();
                ReadWindowHours(json, "\"roomCleanedAt\"", room, roomHours);
                if (roomHours.Count > 0) s_RoomWindowCleanedHour[instanceId] = roomHours;

                // Absent from files written before 2026-10-13: those windows count as fully snowed over.
                var snowFrom = new Dictionary<int, float>();
                ReadWindowValues(json, "\"snowFrom\"", snowFrom);
                if (snowFrom.Count > 0) s_WindowSnowFrom[instanceId] = snowFrom;
                var roomSnowFrom = new Dictionary<int, float>();
                ReadWindowValues(json, "\"roomSnowFrom\"", roomSnowFrom);
                if (roomSnowFrom.Count > 0) s_RoomWindowSnowFrom[instanceId] = roomSnowFrom;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[PENCERE-LOAD] {instanceId}: {building.Count} pencerenin, {room.Count} yalniz oda penceresinin durumu okundu.",
                                    $"[WINDOW-LOAD] {instanceId}: state of {building.Count} windows and {room.Count} room-only windows read from the save.");
            }
            catch (System.Exception ex)
            {
                // Unreadable is treated as absent: every window falls back to the default, which is
                // how the building looked before this feature existed.
                MelonLogger.Warning($"[PENCERE-LOAD] {instanceId} durum dosyasi okunamadi: {ex.Message}",
                                    $"[WINDOW-LOAD] {instanceId}: window state file could not be read: {ex.Message}");
            }
        }

        // Reads the integers of  "name":[1,2,3]  . Written by hand to match, so parsed by hand too,
        // the way Junk.cs reads its own flag.
        private static void ReadWindowList(string json, string name, bool cleaned, Dictionary<int, bool> into)
        {
            int at = json.IndexOf(name, System.StringComparison.Ordinal);
            if (at < 0) return;
            int open = json.IndexOf('[', at);
            int close = open < 0 ? -1 : json.IndexOf(']', open);
            if (open < 0 || close < 0) return;

            foreach (string part in json.Substring(open + 1, close - open - 1).Split(','))
            {
                int window;
                if (int.TryParse(part.Trim(), out window) && window >= 0) into[window] = cleaned;
            }
        }

        // Reads  "name":[window,hour,window,hour]  , the game hour each cleaned window was cleaned at.
        // Kept only for windows the lists above call cleaned. Numbers are written and read with a
        // point whatever the machine's language, because a comma also separates the entries.
        private static void ReadWindowHours(string json, string name, Dictionary<int, bool> cleaned, Dictionary<int, float> into)
        {
            int at = json.IndexOf(name, System.StringComparison.Ordinal);
            if (at < 0) return;
            int open = json.IndexOf('[', at);
            int close = open < 0 ? -1 : json.IndexOf(']', open);
            if (open < 0 || close < 0) return;

            string[] parts = json.Substring(open + 1, close - open - 1).Split(',');
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                int window;
                float hour;
                bool isCleaned;
                if (!int.TryParse(parts[i].Trim(), out window) || window < 0) continue;
                if (!float.TryParse(parts[i + 1].Trim(), System.Globalization.NumberStyles.Float, invariant, out hour)) continue;
                if (cleaned.TryGetValue(window, out isCleaned) && isCleaned) into[window] = hour;
            }
        }

        private static string WriteWindowHours(List<int> cleanedList, Dictionary<int, float> hours)
        {
            var parts = new List<string>();
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            if (hours != null)
                foreach (int window in cleanedList)
                {
                    float hour;
                    if (!hours.TryGetValue(window, out hour)) continue;
                    parts.Add(window.ToString(invariant));
                    parts.Add(hour.ToString("0.###", invariant));
                }
            return string.Join(",", parts);
        }

        public static void SaveAllWindowStates()
        {
            // Only buildings whose file has been read this session. One that was never loaded has
            // nothing in memory, and writing that would erase what is on disk.
            try { SaveWindowFrostBonus(); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[PENCERE-SAVE] Hava dosyasi yazilamadi: {ex.Message}",
                                    $"[WINDOW-SAVE] The heavy weather file could not be written: {ex.Message}");
            }

            foreach (string instanceId in s_WindowStateLoaded)
            {
                try { SaveWindowState(instanceId); }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[PENCERE-SAVE] {instanceId} yazilamadi: {ex.Message}",
                                        $"[WINDOW-SAVE] {instanceId}: window state could not be written: {ex.Message}");
                }
            }
        }

        private static void SaveWindowState(string instanceId)
        {
            if (SaveGameSystem.m_CurrentSaveName != s_WindowStateSaveName) return;

            Dictionary<int, bool> building, room;
            bool any = s_WindowCleaned.TryGetValue(instanceId, out building) && building.Count > 0;
            bool anyRoom = s_RoomWindowCleaned.TryGetValue(instanceId, out room) && room.Count > 0;
            Dictionary<int, float> snowFrom, roomSnowFrom;
            bool anySnow = s_WindowSnowFrom.TryGetValue(instanceId, out snowFrom) && snowFrom.Count > 0;
            bool anyRoomSnow = s_RoomWindowSnowFrom.TryGetValue(instanceId, out roomSnowFrom) && roomSnowFrom.Count > 0;

            string path = GetWindowStateSavePath(instanceId);
            if (path == null) return;

            // Nothing recorded and nothing on disk: leave it that way. An untouched building gets
            // no file.
            if (!any && !anyRoom && !anySnow && !anyRoomSnow && !SaveFiles.Exists(path)) return;

            var cleanedList = new List<int>();
            var frostedList = new List<int>();
            if (any)
                foreach (var pair in building)
                    (pair.Value ? cleanedList : frostedList).Add(pair.Key);
            cleanedList.Sort();
            frostedList.Sort();

            var roomCleanedList = new List<int>();
            var roomFrostedList = new List<int>();
            if (anyRoom)
                foreach (var pair in room)
                    (pair.Value ? roomCleanedList : roomFrostedList).Add(pair.Key);
            roomCleanedList.Sort();
            roomFrostedList.Sort();

            Dictionary<int, float> hours, roomHours;
            s_WindowCleanedHour.TryGetValue(instanceId, out hours);
            s_RoomWindowCleanedHour.TryGetValue(instanceId, out roomHours);

            // Version stays 1: the room keys and the cleaned-at hours are additive, and a build
            // without them reads the building lists exactly as before and ignores the rest.
            string json = "{\"v\":1,\"cleaned\":[" + string.Join(",", cleanedList) + "],\"frosted\":[" + string.Join(",", frostedList) + "]"
                        + ",\"roomCleaned\":[" + string.Join(",", roomCleanedList) + "],\"roomFrosted\":[" + string.Join(",", roomFrostedList) + "]"
                        + ",\"cleanedAt\":[" + WriteWindowHours(cleanedList, hours) + "],\"roomCleanedAt\":[" + WriteWindowHours(roomCleanedList, roomHours) + "]"
                        + ",\"snowFrom\":[" + WriteWindowValues(snowFrom) + "],\"roomSnowFrom\":[" + WriteWindowValues(roomSnowFrom) + "]}";
            JsonWriteCache.Write(path, json);

            if (s_DebugBounds)
                MelonLogger.Msg($"[PENCERE-SAVE] {instanceId}: temiz={cleanedList.Count} buzlu={frostedList.Count} yalniz oda temiz={roomCleanedList.Count} buzlu={roomFrostedList.Count}",
                                $"[WINDOW-SAVE] {instanceId}: cleaned={cleanedList.Count} frosted={frostedList.Count} room-only cleaned={roomCleanedList.Count} frosted={roomFrostedList.Count}");
        }
    }
}
