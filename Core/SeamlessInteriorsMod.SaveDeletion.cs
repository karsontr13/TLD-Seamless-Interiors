using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.IO;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────────
        // THE SAVE IS GONE - THE MOD'S DATA GOES WITH IT
        //
        // THE PROBLEM
        // Deleting a save removes the game's own slot and nothing else. Everything the
        // mod wrote for that playthrough - "sandbox29_CampOffice_containers.json" and the
        // twenty files beside it - stays in Mods\SeamlessInteriorsData forever, because
        // the mod is never told the save is gone.
        //
        // That would be merely untidy if slot names were unique. They are not: the game
        // hands out the LOWEST FREE id to every new game (SaveGameSlots.GetUnusedGameId),
        // so the next game started after deleting "sandbox29" is called "sandbox29" too -
        // and it finds a full set of interior files waiting for it under its own name.
        //
        // SaveIdentity.cs already catches that AFTER the fact: the new save carries a
        // different stamp, so the leftovers are quarantined on the next load. That is the
        // safety net, and it stays. This file removes the reason to ever need it, which is
        // also what the player expects when they delete a save.
        //
        // TWO WAYS IN, BECAUSE ONE IS NEVER ENOUGH
        //
        //   1. THE GAME TELLS US (OnGameDeletedSave). SaveGameSystem.DeleteSaveFiles and
        //      SaveGameSlotHelper.DeleteSaveSlotInfo both name the save they are erasing,
        //      so there is nothing to guess: those files go in the same breath.
        //
        //   2. NOBODY TOLD US (SweepDeletedSaveData). Data can outlive its save without
        //      the mod ever seeing the deletion - the save was deleted while the mod was
        //      not installed, or through a path that reaches neither of the two calls
        //      above. So whenever the game refreshes its save list, every save name the
        //      data directory still carries is put back to the game: "does this save
        //      exist?" The ones that do not are cleaned out.
        //
        // WHAT IS NEVER TOUCHED
        //
        //   - The save that is currently BEING PLAYED. m_CurrentSaveName is left behind
        //     when the player quits to the main menu, so the name matching on its own
        //     proves nothing; a game has to actually be on screen for the guard to hold
        //     (IsSaveCurrentlyBeingPlayed).
        //   - Anything, while the game is in the middle of a save or a restore.
        //   - Anything, when the game's slot list cannot be read. "The save does not
        //     exist" and "I cannot see the save list" are different statements, and only
        //     the first one is evidence (see SlotListReadable - the same reasoning as the
        //     canary in SaveIdentity.cs).
        //   - Mods\SeamlessInteriorsData\BayatVeri. That folder IS the recovery net; it
        //     is the one place data is kept precisely because something went wrong.
        // ─────────────────────────────────────────────────────────────────────

        // The list sweep runs off the game's own save-list refresh, which fires several
        // times while the player walks around the menus. One pass per interval is plenty.
        private const float ORPHAN_SWEEP_MIN_INTERVAL = 2f;
        private static float s_LastOrphanSweepAt = -999f;

        // TWO SWEEPS HAVE TO AGREE BEFORE THE SWEEP DELETES ANYTHING.
        //
        // The explicit hooks act on a statement - "I am deleting this save". The sweep acts
        // on an absence, and an absence can be a lie: the save list is refilled from disk,
        // and a list caught halfway through that refill is missing saves that are really
        // there. Acting on the first such reading would delete a live playthrough's
        // interiors, which is not a mistake anybody can undo.
        //
        // So the first sweep only remembers the name, and the second one - a different
        // refresh, at least ORPHAN_SWEEP_MIN_INTERVAL later - is what acts on it. A name
        // that comes back (the list had simply not finished loading) drops off the list.
        private static readonly HashSet<string> s_MissingOnce = new HashSet<string>();

        // THE SWEEP NEEDS A SECOND LOOK, AND THE MENU DOES NOT ALWAYS OFFER ONE.
        //
        // SaveGameSlotHelper.RefreshSaveSlots is what drives the sweep, and the player can
        // easily open the save list once and start a game - one refresh, one reading, and
        // the two-reading rule above means nothing is ever concluded. So while the main
        // menu is up the sweep is also given a turn of its own every few seconds, which is
        // where a leftover from a save deleted in an earlier session actually gets cleared.
        private const float MENU_SWEEP_INTERVAL = 3f;
        private static float s_NextMenuSweepAt;

        public static void TickDeletedSaveSweep()
        {
            if (!IsDeleteDataWithSaveEnabled) return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < s_NextMenuSweepAt) return;
            s_NextMenuSweepAt = now + MENU_SWEEP_INTERVAL;

            // Only at the main menu: no playthrough is loaded there, so nothing can be
            // half-written, and it is the one screen where saves are deleted.
            try { if (!GameManager.IsMainMenuActive()) return; }
            catch { return; }

            SweepDeletedSaveData("ana menu", false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 1. THE GAME NAMED THE SAVE IT IS DELETING
        // ─────────────────────────────────────────────────────────────────────
        public static void OnGameDeletedSave(string saveName, string why)
        {
            if (!IsDeleteDataWithSaveEnabled) return;
            if (string.IsNullOrEmpty(saveName)) return;

            if (IsSaveCurrentlyBeingPlayed(saveName))
            {
                // The game is rewriting the slot it is playing (a migration, a rename).
                // Deleting this save's files here would throw away the interiors of the
                // playthrough that is running.
                MelonLogger.Warning($"[KAYIT-SILME] '{saveName}' su anda oynanan kayit - " +
                                    $"mod verisine DOKUNULMADI ({why}).");
                return;
            }

            DeleteModDataFor(saveName, why);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 2. NOBODY TOLD US - ASK THE GAME ABOUT EVERY SAVE WE STILL HOLD DATA FOR
        // ─────────────────────────────────────────────────────────────────────
        public static void SweepDeletedSaveData(string why, bool force)
        {
            if (!IsDeleteDataWithSaveEnabled) return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!force && now - s_LastOrphanSweepAt < ORPHAN_SWEEP_MIN_INTERVAL) return;
            s_LastOrphanSweepAt = now;

            // Mid-save and mid-load the slot list is a moving target, and a file the mod
            // is writing this very moment must not be pulled out from under it.
            try
            {
                if (SaveGameSystem.IsRestoreInProgress() || SaveGameSystem.IsSceneRestoreInProgress())
                    return;
            }
            catch { }

            List<string> names = CollectSaveNamesWithData();
            if (names.Count == 0) return;

            if (!SlotListReadable())
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[KAYIT-SILME] Oyunun kayit listesi okunamiyor ({why}) - " +
                                    $"{names.Count} kayit adi kontrol edilmedi.");
                return;
            }

            foreach (string name in names)
            {
                if (IsSaveCurrentlyBeingPlayed(name)) { s_MissingOnce.Remove(name); continue; }

                if (SaveStillExists(name))
                {
                    // It was there all along - the earlier reading was a list still being
                    // filled, not a deleted save.
                    s_MissingOnce.Remove(name);
                    continue;
                }

                if (s_MissingOnce.Add(name))
                {
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[KAYIT-SILME] '{name}' kayit listesinde gorunmuyor - " +
                                        $"ikinci bir kontrol bekleniyor ({why}).");
                    continue;
                }

                s_MissingOnce.Remove(name);
                DeleteModDataFor(name, why + " - kayit artik yok");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // THE ACTUAL REMOVAL
        // ─────────────────────────────────────────────────────────────────────
        private static int DeleteModDataFor(string saveName, string why)
        {
            string dataDir;
            try { dataDir = ModPaths.DataDir(); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KAYIT-SILME] Veri klasorune ulasilamadi: {ex.Message}");
                return 0;
            }

            // "sandbox1_" cannot match "sandbox13_" - the underscore is what separates the
            // save name from the building id, so it belongs in the prefix.
            string prefix = saveName + "_";

            string[] paths;
            try { paths = Directory.GetFiles(dataDir); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KAYIT-SILME] Veri klasoru listelenemedi: {ex.Message}");
                return 0;
            }

            int deleted = 0;
            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                if (!name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    File.Delete(path);
                    deleted++;
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[KAYIT-SILME] '{name}' silinemedi: {ex.Message}");
                    continue;
                }
                finally
                {
                    // The write cache remembers what is already on disk under this path and
                    // skips writing identical content again. Left alone, the next game to
                    // be handed this slot name would write the same bytes, be told "already
                    // there", and end up with no file at all.
                    JsonWriteCache.Forget(path);
                }
            }

            bool prefsCleared = ClearSaveScopedPrefsIfAny(saveName);

            // A memo decided for a save that no longer exists must not answer for the next
            // game handed the same name.
            if (s_SaveIdentityCheckedFor == saveName) ForgetSaveIdentity("kayit silindi");

            if (deleted > 0 || prefsCleared)
            {
                MelonLogger.Msg($"[KAYIT-SILME] '{saveName}' icin mod verisi kaldirildi ({why}): " +
                                $"{deleted} dosya{(prefsCleared ? " + loot bayraklari" : "")}.");
            }
            else if (s_DebugBounds)
            {
                MelonLogger.Msg($"[KAYIT-SILME] '{saveName}' icin kaldirilacak mod verisi yoktu ({why}).");
            }

            return deleted;
        }

        // The "loot was generated" flags and the "player was inside" record live in
        // PlayerPrefs and are addressed by save name, so they are exactly as stale as the
        // files - and left behind they are worse, because the new game would read them as
        // "this building has already been rolled" and never roll its loot at all.
        //
        // Nothing is written unless something is actually there: this runs on every slot
        // the game deletes, autosave rotation included, and PlayerPrefs.Save() is a
        // registry write.
        private static bool ClearSaveScopedPrefsIfAny(string saveName)
        {
            bool any = false;
            try
            {
                foreach (var cfg in SupportedInteriors)
                {
                    if (cfg == null) continue;
                    if (UnityEngine.PlayerPrefs.HasKey(cfg.SaveKeyPrefix + saveName)) { any = true; break; }
                }

                if (!any) any = UnityEngine.PlayerPrefs.HasKey(PLAYER_INSIDE_KEY_PREFIX + saveName);
                if (!any) any = UnityEngine.PlayerPrefs.HasKey(PLAYER_LOCALPOS_KEY_PREFIX + saveName);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KAYIT-SILME] PlayerPrefs sorgulanamadi: {ex.Message}");
                return false;
            }

            if (!any) return false;

            ClearSaveScopedPrefs(saveName);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // WHAT THE GAME CAN TELL US
        // ─────────────────────────────────────────────────────────────────────
        //
        // "This save is the one being played" is only true while a game is actually on
        // screen. m_CurrentSaveName survives the trip back to the main menu, and that is
        // precisely the moment the player deletes saves - so the name on its own would
        // protect the one save the player just asked to be rid of.
        private static bool IsSaveCurrentlyBeingPlayed(string saveName)
        {
            string current = null;
            try { current = SaveGameSystem.m_CurrentSaveName; } catch { }

            if (string.IsNullOrEmpty(current)) return false;
            if (!string.Equals(current, saveName, System.StringComparison.Ordinal)) return false;

            try { if (GameManager.IsMainMenuActive()) return false; } catch { }

            // No player object means no playthrough on screen. If even this cannot be
            // answered the name match is left standing, because keeping data that should
            // have gone is the recoverable mistake.
            try { if (GameManager.GetPlayerTransform() == null) return false; } catch { }

            return true;
        }

        // Does the game still have this save? Two different questions are asked, and only
        // both of them answering "no" counts - anything throwing is read as "yes, keep the
        // data", never as permission to delete it.
        private static bool SaveStillExists(string saveName)
        {
            try { if (SaveGameSlots.HasSaveSlot(saveName)) return true; }
            catch (System.Exception ex)
            {
                if (s_DebugBounds) MelonLogger.Msg($"[KAYIT-SILME] HasSaveSlot('{saveName}') sorulamadi: {ex.Message}");
                return true;
            }

            try { if (SaveGameSlots.SaveExists(saveName)) return true; }
            catch (System.Exception ex)
            {
                if (s_DebugBounds) MelonLogger.Msg($"[KAYIT-SILME] SaveExists('{saveName}') sorulamadi: {ex.Message}");
                return true;
            }

            return false;
        }

        // THE CANARY. An empty save list is the same answer whether the player really has
        // no saves or the list simply is not loaded yet, and acting on the second one
        // would wipe every playthrough's interiors at once. A list that is still being
        // read, or that cannot show a single save it definitely has, is treated as
        // unreadable - and so is any of these three questions refusing to answer.
        private static readonly SaveSlotType[] SLOT_TYPES =
        {
            SaveSlotType.SANDBOX, SaveSlotType.STORY, SaveSlotType.CHALLENGE,
            SaveSlotType.CHECKPOINT, SaveSlotType.AUTOSAVE
        };

        private static bool SlotListReadable()
        {
            try
            {
                if (!SaveGameSlots.HasLoadedAllSavedGameFiles) return false;

                foreach (SaveSlotType type in SLOT_TYPES)
                    if (SaveGameSlots.SlotsAreLoading(type)) return false;

                return SaveGameSlots.GetNumSaveSlotsInUse() > 0;
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds) MelonLogger.Msg($"[KAYIT-SILME] Kayit listesi sorulamadi: {ex.Message}");
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // WHICH SAVES DOES THE DATA DIRECTORY STILL SPEAK FOR?
        // ─────────────────────────────────────────────────────────────────────
        private static List<string> CollectSaveNamesWithData()
        {
            var names = new List<string>();

            string dataDir;
            try { dataDir = ModPaths.DataDir(); } catch { return names; }

            string[] paths;
            try { paths = Directory.GetFiles(dataDir); } catch { return names; }

            foreach (string path in paths)
            {
                string file = Path.GetFileName(path);

                // Everything the mod writes is "<save>_<building><suffix>", so the save
                // name is what stands before the first underscore. The diagnostic dumps
                // start with one and are skipped by the same rule.
                int cut = file.IndexOf('_');
                if (cut <= 0) continue;

                string name = file.Substring(0, cut);
                if (!LooksLikeSlotName(name)) continue;
                if (!names.Contains(name)) names.Add(name);
            }

            return names;
        }

        // A slot name is what SaveGameSlots.BuildSlotName produces: a type prefix followed
        // by the game id, "sandbox29" / "story4" / "challenge6". Nothing else is ever
        // offered to the game as a save name, so nothing else is deleted on the strength
        // of the game answering "I have no such save".
        private static bool LooksLikeSlotName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 32) return false;

            int i = 0;
            while (i < name.Length && char.IsLetter(name[i])) i++;

            if (i == 0) return false;            // no prefix
            if (i == name.Length) return false;  // no id

            for (int j = i; j < name.Length; j++)
                if (!char.IsDigit(name[j])) return false;

            return true;
        }
    }
}
