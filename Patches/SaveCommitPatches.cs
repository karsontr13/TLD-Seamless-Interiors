using Il2Cpp;
using MelonLoader;

namespace SeamlessInteriors
{
    // The game's save for this slot reached the disk: SI's staged files go with it (see SaveFiles).
    // Region changes save to the in-memory "autosave" slot only and never land here.
    [HarmonyLib.HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.OnSaveCompleted))]
    public class CommitSiFilesOnDiskSavePatch
    {
        public static void Postfix(string name)
        {
            try
            {
                if (!string.IsNullOrEmpty(name) && name == SaveGameSystem.m_CurrentSaveName)
                    SaveFiles.Commit(name);
            }
            catch (System.Exception ex) { MelonLogger.Warning($"[SAVE-COMMIT] Commit hatasi: {ex.Message}"); }
        }
    }

    // A slot loaded from disk: whatever SI staged after that slot's last disk save never happened.
    // The in-memory "autosave" restore of a region change has no files under its name.
    [HarmonyLib.HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.RestoreGame))]
    public class DiscardSiFilesOnDiskLoadPatch
    {
        public static void Prefix(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return;
                MelonLogger.Msg($"[SAVE-COMMIT] RestoreGame '{name}'.");
                SaveFiles.Discard(name);
            }
            catch (System.Exception ex) { MelonLogger.Warning($"[SAVE-COMMIT] Discard hatasi: {ex.Message}"); }
        }
    }
}
