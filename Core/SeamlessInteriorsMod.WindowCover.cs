using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // How much of a window is covered, 0 to 1, in two layers (owner, 2026-10-13). Frost: the hours
        // since it was cleaned over WindowFrostReturnHours. Snow: weighted snow hours since cleaned from outside.

        // Weighted snow hours for the whole save; a window keeps the value it was cleaned at.
        private static float s_WindowSnowHours;
        private static float s_WindowSnowWeight;
        public static float WindowSnowFullHours = 12f;
        public static float WindowSnowLightWeight = 0.5f;
        public static float WindowSnowHeavyWeight = 1f;
        public static float WindowSnowBlizzardWeight = 2f;

        private static readonly Dictionary<string, Dictionary<int, float>> s_WindowSnowFrom = new Dictionary<string, Dictionary<int, float>>();
        private static readonly Dictionary<string, Dictionary<int, float>> s_RoomWindowSnowFrom = new Dictionary<string, Dictionary<int, float>>();

        private static float WindowSnowWeightOf(Il2Cpp.WeatherStage stage)
        {
            if (stage == Il2Cpp.WeatherStage.Blizzard) return WindowSnowBlizzardWeight;
            if (stage == Il2Cpp.WeatherStage.HeavySnow) return WindowSnowHeavyWeight;
            if (stage == Il2Cpp.WeatherStage.LightSnow) return WindowSnowLightWeight;
            return 0f;
        }

        private static float WindowSnowHoursNow()
        {
            EnsureWindowFrostBonusLoaded();
            return s_WindowSnowHours;
        }

        // Cleaning from outside wipes the snow too; from inside it does not (owner, 2026-10-13).
        public static void ClearWindowSnow(string instanceId, int window, bool roomWindow)
        {
            if (instanceId == null || window < 0) return;
            if (!roomWindow) window = SharedWindowRecord(instanceId, window);
            EnsureWindowStateLoaded(instanceId);
            var all = roomWindow ? s_RoomWindowSnowFrom : s_WindowSnowFrom;
            Dictionary<int, float> from;
            if (!all.TryGetValue(instanceId, out from)) { from = new Dictionary<int, float>(); all[instanceId] = from; }
            from[window] = WindowSnowHoursNow();
        }

        // The setting's clean-all and frost-all: cleared snow, or the record dropped so it reads full again.
        private static void SetWindowSnowCleared(string instanceId, int window, bool roomWindow, bool cleared)
        {
            if (cleared) { ClearWindowSnow(instanceId, window, roomWindow); return; }
            if (!roomWindow) window = SharedWindowRecord(instanceId, window);
            Dictionary<int, float> from;
            if ((roomWindow ? s_RoomWindowSnowFrom : s_WindowSnowFrom).TryGetValue(instanceId, out from)) from.Remove(window);
        }

        // Frost and snow cover of a building window (roomWindow false) or a room-only window.
        public static void GetWindowCover(string instanceId, int window, bool roomWindow, out float frost, out float snow)
        {
            frost = 1f;
            snow = WindowCleanedDefault() ? 0f : 1f;
            if (instanceId == null || window < 0) return;
            if (!roomWindow) window = SharedWindowRecord(instanceId, window);

            bool cleaned = roomWindow ? IsRoomWindowCleaned(instanceId, window) : IsWindowCleaned(instanceId, window);
            if (cleaned)
            {
                Dictionary<int, float> at;
                float hour;
                var hours = roomWindow ? s_RoomWindowCleanedHour : s_WindowCleanedHour;
                float after = WindowFrostReturnHours;
                if (hours.TryGetValue(instanceId, out at) && at.TryGetValue(window, out hour) && after > 0f)
                    frost = Mathf.Clamp01((WindowHoursNow() - hour) / after);
                else
                    frost = 0f;
            }

            Dictionary<int, float> from;
            float start;
            var snows = roomWindow ? s_RoomWindowSnowFrom : s_WindowSnowFrom;
            if (snows.TryGetValue(instanceId, out from) && from.TryGetValue(window, out start))
                snow = Mathf.Clamp01((WindowSnowHoursNow() - start) / Mathf.Max(WindowSnowFullHours, 0.01f));
        }

        // The pane of the room the player is in: its window outside when it has one, its own record otherwise.
        public static void GetRoomPaneCover(int openingIndex, out float frost, out float snow)
        {
            int window = openingIndex >= 0 && openingIndex < s_WindowPortalSpot.Count ? s_WindowPortalSpot[openingIndex] : -1;
            if (window >= 0) GetWindowCover(s_WindowPortalInstanceId, window, false, out frost, out snow);
            else GetWindowCover(s_WindowPortalInstanceId, openingIndex, true, out frost, out snow);
        }

        // "name":[window,value,...] for every window with a value.
        private static string WriteWindowValues(Dictionary<int, float> values)
        {
            if (values == null || values.Count == 0) return "";
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            var keys = new List<int>(values.Keys);
            keys.Sort();
            var parts = new List<string>();
            foreach (int window in keys) { parts.Add(window.ToString(invariant)); parts.Add(values[window].ToString("0.###", invariant)); }
            return string.Join(",", parts);
        }

        private static void ReadWindowValues(string json, string name, Dictionary<int, float> into)
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
                float value;
                if (!int.TryParse(parts[i].Trim(), out window) || window < 0) continue;
                if (float.TryParse(parts[i + 1].Trim(), System.Globalization.NumberStyles.Float, invariant, out value)) into[window] = value;
            }
        }

        private static float ReadJsonFloat(string json, string name, float fallback)
        {
            int at = json.IndexOf(name, System.StringComparison.Ordinal);
            int colon = at < 0 ? -1 : json.IndexOf(':', at);
            int end = colon < 0 ? -1 : json.IndexOfAny(new[] { ',', '}' }, colon);
            float value;
            if (end > colon && float.TryParse(json.Substring(colon + 1, end - colon - 1).Trim(), System.Globalization.NumberStyles.Float,
                                              System.Globalization.CultureInfo.InvariantCulture, out value)) return value;
            return fallback;
        }
    }
}
