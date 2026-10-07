using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.IO;

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

        private static bool WindowCleanedDefault()
        {
            return s_PrefClearWindowPanes != null && s_PrefClearWindowPanes.Value;
        }

        // Buildings whose file has been read (or found absent) for the save named below.
        private static readonly HashSet<string> s_WindowStateLoaded = new HashSet<string>();
        private static string s_WindowStateSaveName;

        public static bool IsWindowCleaned(string instanceId, int window)
        {
            if (instanceId == null || window < 0) return WindowCleanedDefault();

            EnsureWindowStateLoaded(instanceId);

            Dictionary<int, bool> building;
            bool cleaned;
            if (s_WindowCleaned.TryGetValue(instanceId, out building) && building.TryGetValue(window, out cleaned))
                return cleaned;
            return WindowCleanedDefault();
        }

        // The call a clean-the-window interaction makes. Takes effect on whichever direction is
        // live: outside-in reads this every frame, and inside-out is told directly.
        public static void SetWindowCleaned(string instanceId, int window, bool cleaned)
        {
            if (instanceId == null || window < 0) return;

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

            if (s_WindowPortalInstanceId == instanceId)
                for (int i = 0; i < s_WindowPortalSpot.Count; i++)
                    if (s_WindowPortalSpot[i] == window) ApplyWindowPane(i, cleaned);
        }

        // Every window of the building the player is at, from either side of its door.
        public static void SetAllWindowsCleaned(bool cleaned)
        {
            string instanceId = s_WindowPortalInstanceId ?? s_OutsideInInstanceId;
            if (instanceId == null) return;

            int windows = System.Math.Max(s_OutsideInApertures.Count, 0);
            for (int i = 0; i < s_WindowPortalSpot.Count; i++)
                if (s_WindowPortalSpot[i] + 1 > windows) windows = s_WindowPortalSpot[i] + 1;

            for (int window = 0; window < windows; window++) SetWindowCleaned(instanceId, window, cleaned);

            // Interior openings that paired with no shell window have no shared state to follow.
            if (s_WindowPortalInstanceId == instanceId)
                for (int i = 0; i < s_WindowPortalSpot.Count; i++)
                    if (s_WindowPortalSpot[i] < 0) ApplyWindowPane(i, cleaned);
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
            s_WindowStateLoaded.Clear();
            s_WindowStateSaveName = null;
        }

        private static void EnsureWindowStateLoaded(string instanceId)
        {
            // A different save name means everything held here belongs to another playthrough.
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (saveName != s_WindowStateSaveName)
            {
                s_WindowStateSaveName = saveName;
                s_WindowCleaned.Clear();
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

                if (s_DebugBounds)
                    MelonLogger.Msg($"[PENCERE-LOAD] {instanceId}: {building.Count} pencerenin durumu okundu.",
                                    $"[WINDOW-LOAD] {instanceId}: state of {building.Count} windows read from the save.");
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

        public static void SaveAllWindowStates()
        {
            // Only buildings whose file has been read this session. One that was never loaded has
            // nothing in memory, and writing that would erase what is on disk.
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

            Dictionary<int, bool> building;
            bool any = s_WindowCleaned.TryGetValue(instanceId, out building) && building.Count > 0;

            string path = GetWindowStateSavePath(instanceId);
            if (path == null) return;

            // Nothing recorded and nothing on disk: leave it that way. An untouched building gets
            // no file.
            if (!any && !SaveFiles.Exists(path)) return;

            var cleanedList = new List<int>();
            var frostedList = new List<int>();
            if (any)
                foreach (var pair in building)
                    (pair.Value ? cleanedList : frostedList).Add(pair.Key);
            cleanedList.Sort();
            frostedList.Sort();

            string json = "{\"v\":1,\"cleaned\":[" + string.Join(",", cleanedList) + "],\"frosted\":[" + string.Join(",", frostedList) + "]}";
            JsonWriteCache.Write(path, json);

            if (s_DebugBounds)
                MelonLogger.Msg($"[PENCERE-SAVE] {instanceId}: temiz={cleanedList.Count} buzlu={frostedList.Count}",
                                $"[WINDOW-SAVE] {instanceId}: cleaned={cleanedList.Count} frosted={frostedList.Count}");
        }
    }
}
