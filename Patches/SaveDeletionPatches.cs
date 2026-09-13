using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace SeamlessInteriors
{
    // ─────────────────────────────────────────────────────────────────────────
    // "THE PLAYER DELETED A SAVE" - THE THREE PLACES THE GAME SAYS IT
    //
    // The work itself lives in SeamlessInteriorsMod.SaveDeletion.cs; these only carry
    // the news. All three are POSTFIXES: the mod's files go after the game's own
    // deletion has run, never before it, so a deletion that fails halfway leaves the
    // interiors where they are.
    //
    // They overlap on purpose. A single delete may reach all three - the removal is
    // idempotent, and the second and third pass find nothing left to do - but no single
    // one of them can be relied on to fire for every delete the game performs.
    // ─────────────────────────────────────────────────────────────────────────

    // THE FILES THEMSELVES. Whatever route the player took through the menus, this is
    // where the game erases a slot, and it names the slot it is erasing.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SaveGameSystem), nameof(Il2Cpp.SaveGameSystem.DeleteSaveFiles))]
    public class DeleteModDataWithSaveFilesPatch
    {
        public static void Postfix(string __0)
        {
            try { SeamlessInteriorsMod.OnGameDeletedSave(__0, "oyun kayit dosyalarini sildi"); }
            catch (System.Exception ex) { MelonLogger.Warning($"[KAYIT-SILME] Hata: {ex.Message}"); }
        }
    }

    // THE MENU ENTRY. Panel_ChooseSandbox / Panel_SaveStory / Panel_ChooseChallenge all
    // delete through this one helper, and the SaveSlotInfo it is handed carries the slot
    // name the player pressed delete on.
    //
    // The name is read in the PREFIX: the info object belongs to the list the game is
    // about to rebuild, and what it still holds afterwards is not something to bet a
    // playthrough's data on.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SaveGameSlotHelper), nameof(Il2Cpp.SaveGameSlotHelper.DeleteSaveSlotInfo))]
    public class DeleteModDataWithSaveSlotPatch
    {
        private static string s_DeletedSaveName;

        public static void Prefix(Il2Cpp.SaveSlotInfo __0)
        {
            s_DeletedSaveName = null;
            try { if (__0 != null) s_DeletedSaveName = __0.m_SaveSlotName; }
            catch (System.Exception ex)
            {
                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg($"[KAYIT-SILME] Silinen slotun adi okunamadi: {ex.Message}");
            }
        }

        public static void Postfix()
        {
            string name = s_DeletedSaveName;
            s_DeletedSaveName = null;

            try { SeamlessInteriorsMod.OnGameDeletedSave(name, "oyuncu kayit slotunu sildi"); }
            catch (System.Exception ex) { MelonLogger.Warning($"[KAYIT-SILME] Hata: {ex.Message}"); }
        }
    }

    // THE BACKSTOP. Nothing above fires for a save that was deleted while the mod was not
    // installed, or through a route neither of them sits on. The save list is rebuilt
    // whenever the player opens or returns to a save screen - and right after a deletion -
    // which is exactly the moment to ask the game which of the saves the data directory
    // still speaks for actually exist.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SaveGameSlotHelper), nameof(Il2Cpp.SaveGameSlotHelper.RefreshSaveSlots))]
    public class SweepDeletedSaveDataPatch
    {
        public static void Postfix()
        {
            try { SeamlessInteriorsMod.SweepDeletedSaveData("kayit listesi yenilendi", false); }
            catch (System.Exception ex) { MelonLogger.Warning($"[KAYIT-SILME] Hata: {ex.Message}"); }
        }
    }
}
