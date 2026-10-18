using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SeamlessInteriors
{
    // ─────────────────────────────────────────────────────────────────────
    // REINSTALLING AFTER "PREPARE THE SAVE FOR UNINSTALL"
    //
    // After the export the player may play on without the mod; the base game then keeps the
    // buildings in its own interior records while SI's files stay as they were at the export.
    // Read back unchanged, those files would hide everything done meanwhile and bring back
    // items the player has since carried away.
    //
    // So SI notes the slot's timestamp at each of its own disk saves. A save that was written
    // since without SI has another timestamp: then every exported building's files are put
    // aside (never deleted) and the building is imported again from the base game's record,
    // through the pre-mod import (LegacyImport.cs), when it is next filled.
    //
    // Checked when the slot is restored, before any scene loads, so Safehouse Customization+
    // finds its records back under the region when it reads them.
    // ─────────────────────────────────────────────────────────────────────
    public partial class SeamlessInteriorsMod
    {
        // Per save: "seen|<slot ticks at SI's last disk save>", "X|<id>|<key>" exported,
        // "R|<id>|<key>" to import again from the base game's record.
        private static string GetVanillaStatePath(string saveName)
        {
            return Path.Combine(ModPaths.DataDir(), saveName + "_vanilla_state.txt");
        }

        private class VanillaState
        {
            public long Seen;
            public readonly Dictionary<string, string> Exported = new Dictionary<string, string>();
            public readonly Dictionary<string, string> Reimport = new Dictionary<string, string>();
        }

        private static VanillaState ReadVanillaState(string saveName)
        {
            string path = GetVanillaStatePath(saveName);
            if (!File.Exists(path)) return null;
            var s = new VanillaState();
            foreach (string raw in File.ReadAllLines(path))
            {
                string[] a = raw.Split('|');
                if (a[0] == "seen" && a.Length > 1) long.TryParse(a[1], out s.Seen);
                else if (a[0] == "X" && a.Length > 2) s.Exported[a[1]] = a[2];
                else if (a[0] == "R" && a.Length > 2) s.Reimport[a[1]] = a[2];
            }
            return s;
        }

        // Written straight to disk: it describes files that are already on disk.
        private static void WriteVanillaState(string saveName, VanillaState s)
        {
            var sb = new StringBuilder();
            sb.Append("seen|").Append(s.Seen).Append('\n');
            foreach (var kv in s.Exported) sb.Append("X|").Append(kv.Key).Append('|').Append(kv.Value).Append('\n');
            foreach (var kv in s.Reimport) sb.Append("R|").Append(kv.Key).Append('|').Append(kv.Value).Append('\n');
            try { File.WriteAllText(GetVanillaStatePath(saveName), sb.ToString()); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KALDIRMA] Durum dosyasi yazilamadi: {ex.Message}", $"[UNINSTALL] State file not written: {ex.Message}");
            }
        }

        private static long SlotTicks(string saveName)
        {
            try { return SaveGameSlots.GetTimestampForSlot(saveName).Ticks; }
            catch { return 0; }
        }

        // Export: which buildings the base game now holds, and under which key.
        private static void RecordVanillaExport(string saveName, List<VanillaRoomFacts> exported)
        {
            var s = ReadVanillaState(saveName) ?? new VanillaState();
            s.Exported.Clear();
            foreach (var f in exported) s.Exported[f.Id] = f.ExportKey;
            WriteVanillaState(saveName, s);
        }

        // SI's own disk save (OnSaveCompleted): the slot as SI left it.
        public static void RecordSiDiskSave(string saveName)
        {
            if (string.IsNullOrEmpty(saveName)) return;
            var s = ReadVanillaState(saveName);
            if (s == null) return;
            long ticks = SlotTicks(saveName);
            if (ticks == 0 || ticks == s.Seen) return;
            s.Seen = ticks;
            WriteVanillaState(saveName, s);
        }

        // RestoreGame: was this save written without SI since the export?
        public static void CheckPlayedWithoutSi(string saveName)
        {
            if (string.IsNullOrEmpty(saveName)) return;
            var s = ReadVanillaState(saveName);
            if (s == null || s.Exported.Count == 0 || s.Seen == 0) return;
            long ticks = SlotTicks(saveName);
            if (ticks == 0 || ticks == s.Seen) return;

            MelonLogger.Msg($"[KALDIRMA] '{saveName}' moddan sonra modsuz oynanmis (kayit zamani degismis): {s.Exported.Count} bina oyunun kaydindan geri alinacak.",
                            $"[UNINSTALL] '{saveName}' was played without the mod since its export: {s.Exported.Count} buildings will be read back from the game's records.");

            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var returned = new List<KeyValuePair<string, string>>();
            foreach (var kv in s.Exported)
            {
                SetAsideSiFiles(saveName, kv.Key, stamp);
                s.Reimport[kv.Key] = kv.Value;
                returned.Add(kv);
            }
            s.Exported.Clear();
            s.Seen = ticks;
            WriteVanillaState(saveName, s);

            // Objects moved with Safehouse Customization+ go back under the region, where SI keeps them.
            ModExceptions.ReturnSafehouseCustomizationPlus(saveName, returned);
        }

        // The building's content files and import marker, renamed so the next fill imports it again.
        private static readonly string[] s_ReturnSuffixes =
        {
            "_inactive_scene_gear.json", "_containers.json", "_placeables.json", "_spawn_placeables.json",
            "_interactive_v2.json", "_junk.json", "_legacy.json", "_legacy_backup.json"
        };

        private static void SetAsideSiFiles(string saveName, string id, string stamp)
        {
            foreach (string suffix in s_ReturnSuffixes)
            {
                string path = GetExportFilePath(saveName, id, suffix);
                foreach (string p in new[] { path, SaveFiles.PendingPath(path) })
                {
                    try
                    {
                        if (!File.Exists(p)) continue;
                        File.Move(p, p + ".before_reinstall_" + stamp);
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"[KALDIRMA] {id}: dosya kenara alinamadi ({Path.GetFileName(p)}) - {ex.Message}",
                                            $"[UNINSTALL] {id}: file not set aside ({Path.GetFileName(p)}) - {ex.Message}");
                    }
                }
                JsonWriteCache.Forget(path);
            }
        }

        // ProbeLegacySave: the base game's record for a building played without SI.
        public static bool TryTakeReimportKey(SeamlessInteriorInstance instance, out string key)
        {
            key = null;
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return false;
            var s = ReadVanillaState(saveName);
            return s != null && s.Reimport.TryGetValue(instance.Config.ResolvedInstanceId, out key) && !string.IsNullOrEmpty(key);
        }
    }
}
