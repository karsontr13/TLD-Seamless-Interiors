using Il2Cpp;
using MelonLoader;
using System.IO;
using System.Text;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────────
        // FIRE DIAGNOSTICS - temporary, verbose-only
        //
        // WHY THIS EXISTS: "the stove in the camp office is out after a save/load" has
        // survived one fix already. The timing defect that fix addressed was real - the
        // restore used to consume the fire blob three frames into a ten second region
        // clone, so every building but the first got nothing - but the symptom outlived
        // it, and the log proved why the fix could not have been the whole story:
        //
        //   [FIRE-RESTORE] 6.00 sn beklendi, 0 klon ates objesi FireManager'a kaydedildi
        //
        // Zero. The clone fires register themselves when the clone is activated, so the
        // restore was already running with every fire present, against a full 17 KB blob,
        // and the fire still came back out.
        //
        // That leaves three possibilities, and no amount of reading the code from the
        // outside separates them:
        //   1. the fire is not in the blob    -> the SAVE side drops it;
        //   2. it is in the blob but Deserialize cannot match it to the clone's object;
        //   3. it is restored correctly and something afterwards puts it out.
        //
        // So this dumps the fire state on both sides of the restore, and the blob itself
        // to a file, which tells all three apart in a single run.
        //
        // All of it is behind s_DebugBounds (Verbose Logging / F7) and writes nothing the
        // mod reads back. It is meant to be deleted once the cause is known.
        // ─────────────────────────────────────────────────────────────────────

        public static void DumpFireState(string phase)
        {
            if (!s_DebugBounds) return;

            try
            {
                var sb = new StringBuilder();
                sb.Append($"[FIRE-DIAG:{phase}] ");

                int managerTotal = 0, managerDead = 0;
                try
                {
                    var all = Il2Cpp.FireManager.m_Fires;
                    if (all != null)
                    {
                        managerTotal = all.Count;
                        for (int i = 0; i < all.Count; i++)
                            if (all[i] == null) managerDead++;
                    }
                }
                catch (System.Exception ex) { sb.Append($"(m_Fires okunamadi: {ex.Message}) "); }

                sb.Append($"FireManager.m_Fires={managerTotal} (olu={managerDead})");
                MelonLogger.Msg(sb.ToString());

                foreach (var instance in ActiveInteriors.Values)
                {
                    if (instance == null || instance.MasterInterior == null) continue;

                    var fires = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Fire>(true);
                    foreach (var fire in fires)
                    {
                        if (fire == null || fire.gameObject == null) continue;
                        MelonLogger.Msg("[FIRE-DIAG:" + phase + "]   " + DescribeFire(instance, fire));
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[FIRE-DIAG:{phase}] Dokum basarisiz: {ex.Message}");
            }
        }

        private static string DescribeFire(SeamlessInteriorInstance instance, Il2Cpp.Fire fire)
        {
            string id = instance.Config.ResolvedInstanceId;
            string name = "?";
            string guid = "(yok)";
            string burning = "?", life = "?", elapsed = "?", state = "?", registered = "?";
            string activeSelf = "?", parent = "?";

            try { name = fire.gameObject.name; } catch { }

            try
            {
                var g = fire.GetComponent<Il2Cpp.ObjectGuid>();
                if (g == null) g = fire.GetComponentInParent<Il2Cpp.ObjectGuid>();
                if (g != null && !string.IsNullOrEmpty(g.m_Guid)) guid = g.m_Guid;
                else if (g != null && !string.IsNullOrEmpty(g.PDID)) guid = "PDID:" + g.PDID;
            }
            catch { }

            try { burning = fire.IsBurning().ToString(); } catch { }
            try { life = fire.GetRemainingLifeTimeSeconds().ToString("F1"); } catch { }
            try { elapsed = fire.m_ElapsedOnTODSeconds.ToString("F1"); } catch { }
            try { state = fire.m_FireState.ToString(); } catch { }
            try { registered = Il2Cpp.FireManager.m_Fires.Contains(fire).ToString(); } catch { }
            try { activeSelf = fire.gameObject.activeSelf.ToString(); } catch { }
            try { parent = fire.transform.parent != null ? fire.transform.parent.name : "ROOT"; } catch { }

            Vector3 pos = Vector3.zero;
            try { pos = fire.transform.position; } catch { }

            return $"{id} '{name}' parent={parent} pos=({pos.x:F2},{pos.y:F2},{pos.z:F2}) " +
                   $"guid={guid} yaniyor={burning} kalanOmur={life}sn elapsed={elapsed} " +
                   $"durum={state} kayitli={registered} acik={activeSelf}";
        }

        // The blob itself, so the records can be read against the dump above: does a record
        // for this stove exist at all, and if it does, what guid / position does it carry?
        //
        // ONE FILE PER SIDE, OVERWRITTEN.
        //
        // The name used to carry the time of day, which meant a new file on every single
        // save and every single load - hundreds of them piling up in the data directory
        // over a playthrough, all but the last two of no use to anybody. The question this
        // answers ("what did the game write for the fire THIS time?") is only ever asked
        // of the most recent one, so the previous answer is simply replaced.
        public static void DumpFireBlob(string blob, string tag)
        {
            if (!s_DebugBounds) return;
            if (string.IsNullOrEmpty(blob)) return;

            try
            {
                string path = Path.Combine(ModPaths.DataDir(), FIRE_DUMP_PREFIX + tag + ".txt");

                File.WriteAllText(path, blob);
                MelonLogger.Msg($"[FIRE-DIAG] Ates datasi dosyaya yazildi ({blob.Length} karakter): {path}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[FIRE-DIAG] Ates datasi yazilamadi: {ex.Message}");
            }
        }

        private const string FIRE_DUMP_PREFIX = "_ates_dokumu_";

        // The timestamped dumps written by earlier versions, swept once at startup so the
        // data directory cleans itself up instead of needing the player to do it by hand.
        // Only the old shape is touched: "_ates_dokumu_<tag>_<HHmmss>.txt" ends in a digit,
        // the two files written now ("..._kayit.txt", "..._yukleme.txt") never do.
        public static void CleanUpOldFireDumps()
        {
            int removed = 0;

            try
            {
                foreach (string path in Directory.GetFiles(ModPaths.DataDir(), FIRE_DUMP_PREFIX + "*.txt"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!name.StartsWith(FIRE_DUMP_PREFIX, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (!char.IsDigit(name[name.Length - 1])) continue;

                    try { File.Delete(path); removed++; }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"[FIRE-DIAG] Eski dokum '{Path.GetFileName(path)}' silinemedi: {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[FIRE-DIAG] Eski dokumler temizlenemedi: {ex.Message}");
                return;
            }

            if (removed > 0)
                MelonLogger.Msg($"[FIRE-DIAG] {removed} eski ates dokumu temizlendi.");
        }
    }
}
