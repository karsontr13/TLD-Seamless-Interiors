using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.IO;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─────────────────────────────────────────────────────────────────────
        // WHICH PLAYTHROUGH DOES THIS DATA BELONG TO?
        //
        // THE PROBLEM
        // Every file the mod writes is named after SaveGameSystem.m_CurrentSaveName -
        // "sandbox29_CampOffice_containers.json" and so on. That name is NOT unique to a
        // playthrough: it is "sandbox" + the slot's game id, and the game hands out the
        // LOWEST FREE id to every new game (SaveGameSlots.GetUnusedGameId). Delete the
        // save that was using sandbox29 and the next new game is called sandbox29 too.
        //
        // The mod's files are not deleted with the save, so the new game inherits the old
        // one's interiors: furniture back where the previous character left it, containers
        // holding the previous character's things, objects that were harvested 300 days
        // ago in a game that no longer exists still missing.
        //
        // It is worse than it first looks, because those leftovers also SILENCE the
        // pre-mod import: ProbeLegacySave sees an "_inactive_scene_gear.json" for this
        // save name and concludes "this save has already been played with the mod", so
        // what the player really did in the original interior scenes is never read
        // (result "mod-data-exists" in the _legacy.json marker).
        //
        // The only guard used to be CheckNewGameLootLock's "less than 0.05 hours played"
        // test. That catches a save created WITH the mod already installed and loaded
        // immediately. It cannot catch the ordinary case: a few minutes - or a few days -
        // played before the mod goes in. By then the clock is past the threshold and the
        // stale files are treated as this save's own history.
        //
        // THE FIX
        // Give every playthrough an identity of its own and refuse data that does not
        // carry it:
        //
        //   1. A random tag is written INTO THE GAME'S OWN SAVE SLOT, under the mod's own
        //      key, every time the game saves (StampSaveIdentity, called from the
        //      SaveSceneData patch that already writes the rest of the mod's data).
        //   2. The same tag is written next to the mod's JSON files, as
        //      "<save>_save_id.json".
        //
        // Both are written in the same save operation, so they can only disagree when the
        // files and the save belong to two different games. At load time the two are
        // compared (EnsureSaveIdentity, first thing in Run()):
        //
        //   tags match          -> this really is the same playthrough, carry on
        //   slot has no tag     -> the save was started or continued without the mod
        //   tags differ         -> a different game is using this slot name now
        //
        // In the last two cases the files are NOT deleted. They are MOVED to
        // "BayatVeri\<save>_<date>\", the loot locks for that save name are cleared and
        // the building starts from scratch - which also lets the pre-mod import run
        // properly, because there is no longer a mod file pretending this save has
        // history. Moving rather than deleting matters: if this check is ever wrong, the
        // player's interiors are one folder move away from coming back.
        //
        // A save the mod has never stamped has no "<save>_save_id.json" yet, so there is
        // nothing to compare and nothing is thrown away - the first save writes the tag
        // and the protection starts from the load after that. Such a save is only warned
        // about, in case its interiors are already wrong from before this check existed
        // (Tools\ResetInteriorLoot.ps1 clears one by hand).
        // ─────────────────────────────────────────────────────────────────────

        // The key the tag lives under inside the game's own save slot. It is not a scene
        // name, so nothing in the game or in the legacy-import key matching can mistake
        // it for one.
        private const string SAVE_TAG_SLOT_KEY = "SeamlessInteriors_SaveTag";

        // Where quarantined data goes, under the mod's data directory.
        private const string STALE_DATA_DIR_NAME = "BayatVeri";

        // The tag belonging to the save that is currently loaded. Decided once per save
        // by EnsureSaveIdentity and stamped into the slot on every save after that.
        private static string s_CurrentSaveTag;

        // The save name EnsureSaveIdentity has already run for, so it runs once per save
        // and not once per building.
        private static string s_SaveIdentityCheckedFor;

        private static string GetSaveIdentityPath(string saveName)
        {
            if (string.IsNullOrEmpty(saveName)) return null;
            return Path.Combine(ModPaths.DataDir(), saveName + "_save_id.json");
        }

        // ─────────────────────────────────────────────────────────────────────
        // BACK TO THE MAIN MENU - FORGET WHAT WAS DECIDED FOR THE LAST SAVE
        //
        // The memo above exists so the check costs one slot query per save instead of
        // one per building. It was keyed on the save NAME alone and never cleared, which
        // quietly disabled the entire guard for the case it was written for:
        //
        //   play "sandbox30" -> main menu -> delete it -> start a new game
        //
        // The new game is handed the same name, so the memo still matched, the check
        // returned immediately, and the PREVIOUS playthrough's tag was then stamped into
        // the new save. Both sides agreed forever after, and the stale files - including
        // the pre-mod import markers - were never quarantined.
        //
        // The main menu is the one place where no save is loaded, so it is the honest
        // moment to forget. s_CurrentSaveTag goes with it: a tag belongs to the save it
        // was decided for and must never be carried into the next one.
        public static void ForgetSaveIdentity(string why)
        {
            if (s_SaveIdentityCheckedFor == null && s_CurrentSaveTag == null) return;

            if (s_DebugBounds)
                MelonLogger.Msg($"[KAYIT-KIMLIGI] Kimlik hafizasi sifirlandi ({why}) - " +
                                $"onceki kayit '{s_SaveIdentityCheckedFor}'.");

            s_SaveIdentityCheckedFor = null;
            s_CurrentSaveTag = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // THE CHECK - called at the top of Run(), before anything is restored
        // ─────────────────────────────────────────────────────────────────────
        public static void EnsureSaveIdentity()
        {
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return;

            // Once per save, not once per building.
            if (s_SaveIdentityCheckedFor == saveName) return;
            s_SaveIdentityCheckedFor = saveName;

            if (!IsSaveIdentityGuardEnabled)
            {
                s_CurrentSaveTag = null;
                MelonLogger.Msg("[KAYIT-KIMLIGI] Kapali (EnableSaveIdentityGuard=false) - bayat veri kontrolu yapilmiyor.");
                return;
            }

            string slotTag;
            SlotTagProbe probe = ProbeSlotTag(saveName, out slotTag);

            string fileTag;
            float fileHours;
            bool haveFile = ReadSaveIdentityFile(saveName, out fileTag, out fileHours);
            float hoursNow = GetHoursPlayedSafe();

            // What the slot actually told us, every time. This is the one line that says
            // why the decision below went the way it did.
            MelonLogger.Msg($"[KAYIT-KIMLIGI] '{saveName}' slot sorgusu: {probe} " +
                            $"(kayit={Shorten(slotTag)}, dosyalar={(haveFile ? Shorten(fileTag) : "(yok)")}).");

            // ── Never stamped before ──────────────────────────────────────────
            // Either a genuinely new save, or one played with a mod version from before
            // this check existed. Nothing can be proven, so nothing is touched; the tag
            // is only settled so the next save can write it down.
            if (!haveFile)
            {
                s_CurrentSaveTag = string.IsNullOrEmpty(slotTag) ? NewSaveTag() : slotTag;

                if (HasAnyModDataFor(saveName))
                {
                    MelonLogger.Warning($"[KAYIT-KIMLIGI] '{saveName}' icin mod verisi var ama kimlik damgasi yok " +
                                        $"(bu kontrolden onceki bir surumden kalmis). Bu yukleme dogrulanamiyor; " +
                                        $"ic mekanlar bu kayda ait gorunmuyorsa Tools\\ResetInteriorLoot.ps1 ile " +
                                        $"'{saveName}' verisini sifirlayin. Bir sonraki kayittan itibaren otomatik korunacak.");
                }
                else if (s_DebugBounds)
                {
                    MelonLogger.Msg($"[KAYIT-KIMLIGI] '{saveName}' ilk kez damgalanacak (tag={s_CurrentSaveTag}).");
                }
                return;
            }

            // ── Stamped before: the two must agree ────────────────────────────
            //
            // ONLY A PROVEN MISMATCH MAY THROW ANYTHING AWAY.
            //
            // This used to read "no tag came back from the slot" as "the save carries no
            // tag", and those are not the same statement. The slot's own storage is not
            // always readable from inside a region load, and when it was not, a perfectly
            // healthy playthrough was declared stale: every interior the player had was
            // quarantined on EVERY game restart, silently, mid-load. The stamp was on both
            // sides of the disk the whole time - it simply could not be read back.
            //
            // ProbeSlotTag now separates the two with a canary (see there), and anything
            // short of a proven mismatch leaves the player's data exactly where it is.
            string reason = null;

            if (probe == SlotTagProbe.Unstamped)
            {
                // The slot's storage IS readable and our key is genuinely not in it. The
                // save was replaced by one made without the mod - which is exactly what
                // happens when the slot name is recycled by a new game.
                reason = "oyunun kayit dosyasinda mod damgasi yok";
            }
            else if (probe == SlotTagProbe.Tagged && slotTag != fileTag)
            {
                // Both sides are stamped and they disagree: two different playthroughs.
                reason = $"damgalar farkli (kayit={Shorten(slotTag)}, dosyalar={Shorten(fileTag)})";
            }
            else if (probe == SlotTagProbe.Stamped)
            {
                // The key is in the slot but its value could not be decoded. The save is
                // one the mod has stamped, so there is nothing to act on.
                MelonLogger.Msg($"[KAYIT-KIMLIGI] '{saveName}': kayit dosyasinda damga var ama degeri okunamadi - " +
                                $"dogrulanmis sayiliyor (dosyalar={Shorten(fileTag)}).");
            }
            else if (probe == SlotTagProbe.Unreadable)
            {
                // We cannot see the slot's storage at all right now, so nothing can be
                // proven either way. Leaving the data alone is the only safe answer.
                MelonLogger.Warning($"[KAYIT-KIMLIGI] '{saveName}': oyunun kayit slotu bu anda okunamiyor, " +
                                    $"kimlik dogrulanamadi - veriye DOKUNULMUYOR (dosyalar={Shorten(fileTag)}). " +
                                    $"Bu yuklemede bayat veri korumasi devre disi; ic mekanlar bu kayda ait " +
                                    $"gorunmuyorsa Tools\\ResetInteriorLoot.ps1 ile '{saveName}' verisini sifirlayin.");
            }

            if (reason == null)
            {
                s_CurrentSaveTag = fileTag;

                // Not a reason to throw anything away - loading an earlier save of the
                // SAME game legitimately puts the clock back - but worth seeing in the
                // log when interiors look ahead of the world.
                if (hoursNow > 0f && fileHours > 0f && hoursNow + 0.5f < fileHours)
                {
                    MelonLogger.Warning($"[KAYIT-KIMLIGI] '{saveName}': oynanan sure geriye gitti " +
                                        $"({fileHours:F2}h -> {hoursNow:F2}h). Ayni oyunun daha eski bir kaydi " +
                                        $"yuklenmis olabilir; ic mekanlar dunyadan ileride olabilir.");
                }
                else if (s_DebugBounds)
                {
                    MelonLogger.Msg($"[KAYIT-KIMLIGI] '{saveName}' dogrulandi (tag={Shorten(fileTag)}).");
                }
                return;
            }

            // ── Mismatch: the files belong to another game ───────────────────
            MelonLogger.Warning($"[KAYIT-KIMLIGI] '{saveName}' icin BASKA BIR OYUNA ait mod verisi bulundu - {reason}. " +
                                $"Oyun silinen kayitlarin slot adini yeniden kullanir, bu yuzden eski ic mekan " +
                                $"duzeni yeni oyuna karisabiliyordu. Veriler karantinaya aliniyor.");

            QuarantineSaveData(saveName, reason);

            // The new game gets its own identity from here on. If the slot already had a
            // tag (another game that WAS played with the mod), that tag is the correct
            // one to keep - the files were the stale side, not the save.
            s_CurrentSaveTag = string.IsNullOrEmpty(slotTag) ? NewSaveTag() : slotTag;
        }

        // ─────────────────────────────────────────────────────────────────────
        // THE STAMP - called from the SaveSceneData patch, with the slot the game
        // is about to write to disk.
        //
        // The tag goes into the game's slot first and the matching file is written
        // straight after, in the same save operation, so the pair cannot drift apart
        // through anything short of the save itself failing.
        // ─────────────────────────────────────────────────────────────────────
        public static void StampSaveIdentity(SlotData slot)
        {
            if (!IsSaveIdentityGuardEnabled) return;
            if (slot == null) return;

            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName)) return;

            // THE TAG AND THE FILES MUST END UP IN THE SAME PLACE.
            //
            // Everything the mod writes is named after m_CurrentSaveName. If the game is
            // writing to some OTHER slot here (a checkpoint, an autosave, a migration),
            // stamping that slot would leave the file claiming a tag the save the player
            // actually reloads does not carry - and the next load would read that as
            // another game's data and quarantine perfectly good interiors.
            //
            // So a slot that is not this save is left alone. That costs nothing but the
            // protection for that one write, which is the right way round to be wrong.
            if (!SlotBelongsToSave(slot, saveName))
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[KAYIT-KIMLIGI] Damga atlandi: yazilan slot '{DescribeSlot(slot)}' " +
                                    $"guncel kayit '{saveName}' degil.");
                return;
            }

            // Run() has not happened yet (the player saved before any building was built,
            // or in a region the mod does not touch): settle the identity now so the save
            // is stamped anyway.
            if (s_SaveIdentityCheckedFor != saveName) EnsureSaveIdentity();
            if (string.IsNullOrEmpty(s_CurrentSaveTag)) return;

            string payload = "{\"v\":1,\"tag\":\"" + s_CurrentSaveTag + "\"}";

            bool stamped;
            try
            {
                stamped = SaveGameSlots.SaveDataToSlot(slot, SAVE_TAG_SLOT_KEY, (Il2CppSystem.String)payload);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KAYIT-KIMLIGI] Damga kayit dosyasina yazilamadi: {ex.Message}");
                return;
            }

            // The companion file is only written when the slot really took the tag.
            // Writing it regardless would leave a file claiming a tag the save does not
            // have, and the next load would read that as "another game's data".
            if (!stamped)
            {
                MelonLogger.Warning("[KAYIT-KIMLIGI] Oyun damgayi kabul etmedi, kimlik dosyasi yazilmiyor.");
                return;
            }

            WriteSaveIdentityFile(saveName, s_CurrentSaveTag, GetHoursPlayedSafe());
        }

        // ─────────────────────────────────────────────────────────────────────
        // QUARANTINE - move, never delete
        // ─────────────────────────────────────────────────────────────────────
        private static void QuarantineSaveData(string saveName, string reason)
        {
            string dataDir = ModPaths.DataDir();
            string prefix = saveName + "_";   // "sandbox1_" cannot match "sandbox13_"

            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string targetDir = Path.Combine(Path.Combine(dataDir, STALE_DATA_DIR_NAME), saveName + "_" + stamp);

            int moved = 0;
            try
            {
                Directory.CreateDirectory(targetDir);

                foreach (string path in Directory.GetFiles(dataDir, prefix + "*"))
                {
                    string name = Path.GetFileName(path);
                    try
                    {
                        File.Move(path, Path.Combine(targetDir, name));
                        moved++;
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"[KAYIT-KIMLIGI] '{name}' tasinamadi: {ex.Message}");
                    }
                    JsonWriteCache.Forget(path);
                }

                File.WriteAllText(Path.Combine(targetDir, "_NEDEN.txt"),
                    $"Kayit: {saveName}\r\nTarih: {System.DateTime.Now}\r\nSebep: {reason}\r\n\r\n" +
                    "Bu dosyalar bu slot adini daha once kullanan BASKA bir oyuna ait gorunuyordu.\r\n" +
                    "Yanlis bir tespit oldugunu dusunuyorsaniz dosyalari SeamlessInteriorsData klasorune geri tasiyin.\r\n");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[KAYIT-KIMLIGI] Karantina klasoru hazirlanamadi: {ex.Message}");
            }

            ClearSaveScopedPrefs(saveName);
            ResetInstanceStateAfterQuarantine();

            MelonLogger.Msg($"[KAYIT-KIMLIGI] {moved} dosya karantinaya alindi: {targetDir}");
        }

        // The loot locks and the "player was inside" record live in PlayerPrefs and are
        // addressed by save name too, so they are just as stale as the files.
        private static void ClearSaveScopedPrefs(string saveName)
        {
            foreach (var cfg in SupportedInteriors)
            {
                if (cfg == null) continue;
                UnityEngine.PlayerPrefs.DeleteKey(cfg.SaveKeyPrefix + saveName);
            }

            UnityEngine.PlayerPrefs.DeleteKey(PLAYER_INSIDE_KEY_PREFIX + saveName);
            UnityEngine.PlayerPrefs.DeleteKey(PLAYER_LOCALPOS_KEY_PREFIX + saveName);
            UnityEngine.PlayerPrefs.DeleteKey(PLAYER_LOCALROT_KEY_PREFIX + saveName);
            UnityEngine.PlayerPrefs.Save();
        }

        // A building probed before the quarantine (there is none in the normal flow, but
        // Run() order is not something to rely on) must not keep the answer it got.
        private static void ResetInstanceStateAfterQuarantine()
        {
            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null) continue;
                instance.LegacyImport = LegacyImportState.Unknown;
                instance.LegacySource = LegacySourceKind.None;
                instance.LegacySourceId = null;
                instance.LegacyRawBlob = null;
                instance.JunkCleared = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // READ / WRITE
        // ─────────────────────────────────────────────────────────────────────
        // WHAT THE GAME'S SAVE SLOT CAN TELL US ABOUT OUR STAMP.
        //
        // The distinction that matters is between "this save has no stamp" and "I cannot
        // read this save's storage". Only the first one is evidence of anything, and
        // treating the second as if it were the first quarantined healthy playthroughs on
        // every single game restart.
        private enum SlotTagProbe
        {
            Unreadable,  // the slot's storage cannot be read from here - prove nothing
            Unstamped,   // storage readable, our key is genuinely absent
            Stamped,     // our key is there, its value could not be decoded
            Tagged       // our key is there and the tag was read
        }

        private static SlotTagProbe ProbeSlotTag(string saveName, out string tag)
        {
            tag = ReadSaveTagFromSlot(saveName);
            if (!string.IsNullOrEmpty(tag)) return SlotTagProbe.Tagged;

            // The value did not come back. Does the key exist at all? This asks the slot
            // a plain yes/no question and never has to decode anything.
            if (SlotHasKey(saveName, SAVE_TAG_SLOT_KEY)) return SlotTagProbe.Stamped;

            // THE CANARY.
            //
            // "Our key is not there" is only meaningful if the slot would have told us
            // about a key that IS there. Every real save carries the game's own entries,
            // so asking for one of those with the SAME call separates "the slot has no
            // stamp" from "this call cannot see the slot".
            return SlotStorageReadable(saveName) ? SlotTagProbe.Unstamped : SlotTagProbe.Unreadable;
        }

        // Keys the game writes into every save of its own accord.
        private static string[] CanaryKeys()
        {
            var keys = new List<string>(3);
            try { if (!string.IsNullOrEmpty(SlotData.GLOBAL_KEY)) keys.Add(SlotData.GLOBAL_KEY); } catch { }
            try { if (!string.IsNullOrEmpty(SlotData.INFO_KEY)) keys.Add(SlotData.INFO_KEY); } catch { }
            try { if (!string.IsNullOrEmpty(SlotData.BOOT_KEY)) keys.Add(SlotData.BOOT_KEY); } catch { }
            return keys.ToArray();
        }

        private static bool SlotStorageReadable(string saveName)
        {
            foreach (string key in CanaryKeys())
                if (SlotHasKey(saveName, key)) return true;

            return false;
        }

        private static bool SlotHasKey(string saveName, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            try { if (SaveGameSlots.HasFilenameInSlot(saveName, key)) return true; }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[KAYIT-KIMLIGI] Slot anahtari sorgulanamadi ({key}): {ex.Message}");
            }

            return SlotKeyListContains(saveName, key);
        }

        // SECOND WAY TO ASK THE SAME QUESTION.
        //
        // HasFilenameInSlot answers "no" for every key on this build - including the
        // game's own boot/global/info - so the canary above never fired and the probe
        // returned Unreadable on every single load. That is the "prove nothing, touch
        // nothing" branch, which meant the stale-data guard had quietly been doing
        // nothing at all since it was written.
        //
        // GetSceneKeysForCurrentSaveSlot does work: the F6 dump lists the real key set,
        // the mod's own SeamlessInteriors_SaveTag among them. It only speaks for the
        // CURRENTLY LOADED slot, which is the only slot this check ever asks about.
        //
        // An empty or missing list still means "cannot see the slot", never "the key is
        // absent" - the canary in ProbeSlotTag is what turns absence into evidence, and
        // it goes through this same call.
        private static bool SlotKeyListContains(string saveName, string key)
        {
            try
            {
                if (saveName != SaveGameSystem.m_CurrentSaveName) return false;

                var keys = SaveGameSlots.GetSceneKeysForCurrentSaveSlot();
                if (keys == null) return false;

                for (int i = 0; i < keys.Count; i++)
                    if (keys[i] == key) return true;
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[KAYIT-KIMLIGI] Slot anahtar listesi okunamadi ({key}): {ex.Message}");
            }

            return false;
        }

        // The generic argument decides how the game hands the value back, and which
        // instantiation works is not something to bet a player's save on - so both are
        // tried. The first one that yields a tag wins; neither one failing is an answer
        // in itself (that is what the canary above is for).
        private static string ReadSaveTagFromSlot(string saveName)
        {
            try
            {
                Il2CppSystem.String il2cppRaw;
                if (SaveGameSlots.TryLoadDataFromSlot<Il2CppSystem.String>(saveName, SAVE_TAG_SLOT_KEY, out il2cppRaw))
                {
                    string tag = ExtractTag(il2cppRaw != null ? il2cppRaw.ToString() : null);
                    if (!string.IsNullOrEmpty(tag)) return tag;
                }
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[KAYIT-KIMLIGI] Slot damgasi okunamadi (Il2CppSystem.String): {ex.Message}");
            }

            try
            {
                string raw;
                if (SaveGameSlots.TryLoadDataFromSlot<string>(saveName, SAVE_TAG_SLOT_KEY, out raw))
                    return ExtractTag(raw);
            }
            catch (System.Exception ex)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[KAYIT-KIMLIGI] Slot damgasi okunamadi (string): {ex.Message}");
            }

            return null;
        }

        private static bool ReadSaveIdentityFile(string saveName, out string tag, out float hours)
        {
            tag = null;
            hours = 0f;

            string path = GetSaveIdentityPath(saveName);
            if (path == null || !File.Exists(path)) return false;

            try
            {
                string text = File.ReadAllText(path);
                tag = ExtractTag(text);
                hours = ExtractFloat(text, "\"hours\":");
                return !string.IsNullOrEmpty(tag);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KAYIT-KIMLIGI] Kimlik dosyasi okunamadi: {ex.Message}");
                return false;
            }
        }

        private static void WriteSaveIdentityFile(string saveName, string tag, float hours)
        {
            string path = GetSaveIdentityPath(saveName);
            if (path == null) return;

            string json = "{\"v\":1,\"tag\":\"" + tag + "\"," +
                          "\"hours\":" + hours.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," +
                          "\"saved\":\"" + System.DateTime.Now.ToString("s") + "\"}";

            try { File.WriteAllText(path, json); }
            catch (System.Exception ex) { MelonLogger.Warning($"[KAYIT-KIMLIGI] Kimlik dosyasi yazilamadi: {ex.Message}"); }

            JsonWriteCache.Forget(path);
        }

        // Both sides store {"v":1,"tag":"..."}, but the game is free to wrap what it is
        // handed (a bare string comes back quoted on some versions), so the value is
        // pulled out by name and a bare tag is still accepted.
        private static string ExtractTag(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            string tag = ExtractString(raw, "\"tag\":\"", "\"");
            if (!string.IsNullOrEmpty(tag)) return tag;

            // Escaped once by the game's own serializer: {\"tag\":\"...\"}
            tag = ExtractString(raw, "\\\"tag\\\":\\\"", "\\\"");
            if (!string.IsNullOrEmpty(tag)) return tag;

            string trimmed = raw.Trim().Trim('"');
            return IsPlainTag(trimmed) ? trimmed : null;
        }

        // A tag is the "N" form of a Guid - 32 hex characters, nothing else - so anything
        // else that happens to come back from the slot is not mistaken for one.
        private static bool IsPlainTag(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length != 32) return false;
            foreach (char c in text)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        // The slot carries a couple of names depending on how it was created. Any of them
        // matching is enough; a slot that names itself nothing at all cannot be judged, so
        // it is accepted rather than silently skipped forever.
        private static bool SlotBelongsToSave(SlotData slot, string saveName)
        {
            string internalName = null;
            string fileName = null;

            try { internalName = slot.m_InternalName; } catch { }
            try { fileName = slot.m_Filename; } catch { }

            if (string.IsNullOrEmpty(internalName) && string.IsNullOrEmpty(fileName)) return true;

            return internalName == saveName || fileName == saveName;
        }

        private static string DescribeSlot(SlotData slot)
        {
            try { return string.IsNullOrEmpty(slot.m_InternalName) ? slot.m_Filename : slot.m_InternalName; }
            catch { return "?"; }
        }

        private static string NewSaveTag()
        {
            return System.Guid.NewGuid().ToString("N");
        }

        private static string Shorten(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "(yok)";
            return tag.Length <= 8 ? tag : tag.Substring(0, 8);
        }

        private static bool HasAnyModDataFor(string saveName)
        {
            try
            {
                // The identity file itself does not count as history.
                foreach (string path in Directory.GetFiles(ModPaths.DataDir(), saveName + "_*"))
                {
                    if (!path.EndsWith("_save_id.json", System.StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        private static float GetHoursPlayedSafe()
        {
            try
            {
                var tod = GameManager.GetTimeOfDayComponent();
                return tod != null ? tod.GetHoursPlayedNotPaused() : 0f;
            }
            catch { return 0f; }
        }
    }
}
