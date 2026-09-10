using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────
        // CLONE SCENE INTERACTION STATE (BreakDown / Harvestable / Smashable / OpenClose / Lock)
        //
        // PROBLEM: all of these components are written into the game's GLOBAL save data
        // and matched back on load via FindByPosition / FindByGuid. Neither works for a
        // clone scene:
        //   - Position: the clone was moved by AlignWithExteriorShell, so it is not at
        //     the original scene coordinates.
        //   - Guid: guids are regenerated for the clone.
        // Result: a broken chair/crate, a harvested plant or an opened door all revert
        // after a save/load.
        //
        // FIX: same pattern as the container data - a separate JSON per instance,
        // written through the game's own Serialize()/Deserialize() methods.
        // ─────────────────────────────────────────────────────────────────

        private static string GetInteractiveStateSavePath(SeamlessInteriorInstance instance)
        {
            // v2 = guid based key scheme. v1 files used order-dependent "#N" suffixes
            // and could apply a state to the wrong object; the file name was changed so
            // they are never read (old files are silently ignored).
            return GetInstanceSavePath(instance, "_interactive_v2.json");
        }

        private static string BuildStateKey(string typeTag, Transform interiorT, Component c, string serialized)
        {
            string guid = ExtractGuidFromSerialized(serialized);
            if (!string.IsNullOrEmpty(guid))
                return $"{typeTag}|g:{guid}";

            Vector3 lp = interiorT.InverseTransformPoint(c.transform.position);
            return $"{typeTag}|p:{c.gameObject.name}|{lp.x:F2},{lp.y:F2},{lp.z:F2}";
        }

        private static string ExtractGuidFromSerialized(string serialized)
        {
            if (string.IsNullOrEmpty(serialized)) return null;

            const string marker = "\"m_Guid\"";
            int i = serialized.IndexOf(marker);
            if (i < 0) return null;

            i = serialized.IndexOf(':', i + marker.Length);
            if (i < 0) return null;

            int q1 = serialized.IndexOf('"', i);
            if (q1 < 0) return null;
            int q2 = serialized.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;

            string guid = serialized.Substring(q1 + 1, q2 - q1 - 1);
            return string.IsNullOrEmpty(guid) ? null : guid;
        }

        // Collects one component type and appends it to the entries list.
        private static int CollectStates<T>(SeamlessInteriorInstance instance, string typeTag,
                                            List<string> entries, HashSet<string> usedKeys)
            where T : Component
        {
            Transform interiorT = instance.MasterInterior.transform;
            var comps = instance.MasterInterior.GetComponentsInChildren<T>(true);
            int count = 0;

            foreach (var c in comps)
            {
                if (c == null || c.gameObject == null) continue;

                string serialized;
                try
                {
                    // In Il2Cpp, Serialize() is not on a shared interface but declared
                    // separately on each type, hence the per-type wrapper below rather
                    // than a dynamic call.
                    serialized = SerializeComponent(c, typeTag);
                }
                catch (System.Exception ex)
                {
                    if (s_DebugBounds)
                        MelonLogger.Warning($"[STATE-SAVE] Serialize hatasi: {typeTag} '{c.gameObject.name}' - {ex.Message}");
                    continue;
                }

                if (string.IsNullOrEmpty(serialized)) continue;

                string uniqueKey = BuildStateKey(typeTag, interiorT, c, serialized);

                // On a clash (same name + position on the guid-less fallback path) SKIP
                // the entry. The old "#N" suffix depended on ordering and could apply a
                // state to the wrong object. Not writing an ambiguous entry is better
                // than writing a wrong one.
                if (!usedKeys.Add(uniqueKey))
                {
                    if (s_DebugBounds)
                        MelonLogger.Warning($"[STATE-SAVE] ATLANDI (belirsiz kimlik): {uniqueKey}");
                    continue;
                }

                string escKey = EscapeJson(uniqueKey);
                string escData = EscapeJson(serialized);
                entries.Add($"{{\"k\":\"{escKey}\",\"d\":\"{escData}\"}}");
                count++;
            }

            return count;
        }

        // Type tags. CollectStates/ApplyStates already know which type they scan;
        // passing the tag down lets the cast happen exactly ONCE.
        private const string TAG_BREAKDOWN = "BD";
        private const string TAG_HARVESTABLE = "HV";
        private const string TAG_SMASHABLE = "SM";
        private const string TAG_OPENCLOSE = "OC";
        private const string TAG_LOCK = "LK";

        private static string SerializeComponent(Component c, string typeTag)
        {
            switch (typeTag)
            {
                case TAG_BREAKDOWN:
                    {
                        var x = c.TryCast<Il2Cpp.BreakDown>();
                        return (x != null) ? x.Serialize() : null;
                    }
                case TAG_HARVESTABLE:
                    {
                        var x = c.TryCast<Il2Cpp.Harvestable>();
                        return (x != null) ? x.Serialize() : null;
                    }
                case TAG_SMASHABLE:
                    {
                        var x = c.TryCast<Il2Cpp.SmashableItem>();
                        return (x != null) ? x.Serialize() : null;
                    }
                case TAG_OPENCLOSE:
                    {
                        var x = c.TryCast<Il2Cpp.OpenClose>();
                        return (x != null) ? x.Serialize() : null;
                    }
                case TAG_LOCK:
                    {
                        var x = c.TryCast<Il2Cpp.Lock>();
                        return (x != null) ? x.Serialize() : null;
                    }
            }
            return null;
        }

        // Mirror of SerializeComponent, used on the restore path.
        private static void DeserializeComponent(Component c, string typeTag, string data)
        {
            switch (typeTag)
            {
                case TAG_BREAKDOWN:
                    {
                        var x = c.TryCast<Il2Cpp.BreakDown>();
                        if (x != null) x.Deserialize(data);
                        return;
                    }
                case TAG_HARVESTABLE:
                    {
                        var x = c.TryCast<Il2Cpp.Harvestable>();
                        if (x != null) x.Deserialize(data);
                        return;
                    }
                case TAG_SMASHABLE:
                    {
                        var x = c.TryCast<Il2Cpp.SmashableItem>();
                        if (x != null) x.Deserialize(data);
                        return;
                    }
                case TAG_OPENCLOSE:
                    {
                        var x = c.TryCast<Il2Cpp.OpenClose>();
                        if (x != null) x.Deserialize(data);
                        return;
                    }
                case TAG_LOCK:
                    {
                        var x = c.TryCast<Il2Cpp.Lock>();
                        if (x != null) x.Deserialize(data);
                        return;
                    }
            }
        }

        // The serialized payloads are stored as JSON strings, so they have to be escaped.
        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(ch);
                    continue;
                }

                char next = s[++i];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    // EscapeJson produces no other escapes; anything unexpected is kept
                    // verbatim so no data is lost.
                    default: sb.Append('\\').Append(next); break;
                }
            }
            return sb.ToString();
        }

        public static void SaveAllInteractiveStates()
        {
            foreach (var instance in ActiveInteriors.Values)
                SaveInteractiveState(instance);
        }

        public static void SaveInteractiveState(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null || !instance.RunCompleted) return;

            string path = GetInteractiveStateSavePath(instance);
            if (path == null) return;

            var entries = new List<string>();
            // Shared across all five passes so a key can never be claimed twice.
            var usedKeys = new HashSet<string>();

            int nBreak = CollectStates<Il2Cpp.BreakDown>(instance, TAG_BREAKDOWN, entries, usedKeys);
            int nHarv = CollectStates<Il2Cpp.Harvestable>(instance, TAG_HARVESTABLE, entries, usedKeys);
            int nSmash = CollectStates<Il2Cpp.SmashableItem>(instance, TAG_SMASHABLE, entries, usedKeys);
            int nOpen = CollectStates<Il2Cpp.OpenClose>(instance, TAG_OPENCLOSE, entries, usedKeys);
            int nLock = CollectStates<Il2Cpp.Lock>(instance, TAG_LOCK, entries, usedKeys);

            string json = "[\n" + string.Join(",\n", entries) + "\n]";
            JsonWriteCache.Write(path, json);

            if (s_DebugBounds)
                MelonLogger.Msg($"[STATE-SAVE] {instance.Config.ResolvedInstanceId}: BreakDown={nBreak} Harvestable={nHarv} Smashable={nSmash} OpenClose={nOpen} Lock={nLock} (toplam {entries.Count})");
        }

        public static void RestoreInteractiveState(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null) return;

            string path = GetInteractiveStateSavePath(instance);
            if (path == null || !File.Exists(path))
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[STATE-LOAD] {instance.Config.ResolvedInstanceId} durum dosyasi yok, atlaniyor.");
                return;
            }

            var keyToData = ParseStateFile(File.ReadAllText(path));
            if (keyToData.Count == 0)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[STATE-LOAD] {instance.Config.ResolvedInstanceId}: dosyada durum verisi yok.");
                return;
            }

            int restored = 0, missed = 0;
            var usedKeys = new HashSet<string>();

            restored += ApplyStates<Il2Cpp.BreakDown>(instance, TAG_BREAKDOWN, keyToData, usedKeys, ref missed);
            restored += ApplyStates<Il2Cpp.Harvestable>(instance, TAG_HARVESTABLE, keyToData, usedKeys, ref missed);
            restored += ApplyStates<Il2Cpp.SmashableItem>(instance, TAG_SMASHABLE, keyToData, usedKeys, ref missed);
            restored += ApplyStates<Il2Cpp.OpenClose>(instance, TAG_OPENCLOSE, keyToData, usedKeys, ref missed);
            restored += ApplyStates<Il2Cpp.Lock>(instance, TAG_LOCK, keyToData, usedKeys, ref missed);

            MelonLogger.Msg($"[STATE-LOAD] {instance.Config.ResolvedInstanceId}: {restored}/{keyToData.Count} durum geri yuklendi ({missed} eslesmedi).");
        }

        private static int ApplyStates<T>(SeamlessInteriorInstance instance, string typeTag,
                                          Dictionary<string, string> keyToData, HashSet<string> usedKeys,
                                          ref int missed)
            where T : Component
        {
            Transform interiorT = instance.MasterInterior.transform;
            var comps = instance.MasterInterior.GetComponentsInChildren<T>(true);
            int restored = 0;

            foreach (var c in comps)
            {
                if (c == null || c.gameObject == null) continue;

                // Building the key requires serializing the current state (the guid is
                // read from it). The value is used for identity only.
                string currentSerialized;
                try { currentSerialized = SerializeComponent(c, typeTag); }
                catch { continue; }

                string uniqueKey = BuildStateKey(typeTag, interiorT, c, currentSerialized);

                // The same key twice means the identity is ambiguous: leave it alone.
                if (!usedKeys.Add(uniqueKey))
                {
                    if (s_DebugBounds)
                        MelonLogger.Warning($"[STATE-LOAD] ATLANDI (belirsiz kimlik): {uniqueKey}");
                    continue;
                }

                if (!keyToData.TryGetValue(uniqueKey, out string data))
                {
                    missed++;
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[STATE-LOAD] ESLESME YOK: {uniqueKey}");
                    continue;
                }

                try
                {
                    DeserializeComponent(c, typeTag, data);
                    restored++;
                }
                catch (System.Exception ex)
                {
                    if (s_DebugBounds)
                        MelonLogger.Warning($"[STATE-LOAD] Deserialize hatasi: {uniqueKey} - {ex.Message}");
                }
            }

            return restored;
        }

        // Minimal reader for the {"k":...,"d":...} entry list. A real JSON parser is not
        // worth pulling in for a file this mod writes itself.
        private static Dictionary<string, string> ParseStateFile(string json)
        {
            var result = new Dictionary<string, string>();
            int idx = 0;

            while (idx < json.Length)
            {
                int kIdx = json.IndexOf("\"k\":\"", idx);
                if (kIdx < 0) break;
                int kStart = kIdx + 5;
                int kEnd = FindClosingQuote(json, kStart);
                if (kEnd < 0) break;
                string key = UnescapeJson(json.Substring(kStart, kEnd - kStart));

                int dIdx = json.IndexOf("\"d\":\"", kEnd);
                if (dIdx < 0) break;
                int dStart = dIdx + 5;
                int dEnd = FindClosingQuote(json, dStart);
                if (dEnd < 0) break;
                string data = UnescapeJson(json.Substring(dStart, dEnd - dStart));

                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(data))
                    result[key] = data;

                idx = dEnd + 1;
            }

            return result;
        }

        // Finds the closing quote, skipping escaped quotes (an odd number of preceding
        // backslashes means the quote itself is escaped).
        private static int FindClosingQuote(string s, int start)
        {
            int i = start;
            while (i < s.Length)
            {
                i = s.IndexOf('"', i);
                if (i < 0) return -1;

                int backslashes = 0;
                int p = i - 1;
                while (p >= start && s[p] == '\\') { backslashes++; p--; }

                if (backslashes % 2 == 0) return i;
                i++;
            }
            return -1;
        }
    }
}
