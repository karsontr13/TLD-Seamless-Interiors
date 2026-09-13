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
        // SAFEHOUSE "CLEAR JUNK" (R)
        //
        // WHAT THE GAME DOES - measured from GameAssembly.dll (the method xref cache plus
        // disassembly, 2026-09-12), not guessed from the names:
        //
        //   CanClearJunk()   customizing && not placing a mesh or decal && !m_ClearedJunk.
        //                    Asked by StartCustomizing and the HUD indicator - it is what
        //                    puts the "R" prompt up.
        //   MaybeClearJunk() if (m_ClearedJunk) return false; ClearJunk(); return true.
        //                    The player's action. No code in the game calls it directly.
        //   ClearJunk()      SetActive(false) on EVERY JunkTag in every loaded scene,
        //                    inactive ones included, then m_ClearedJunk = true. Called by
        //                    MaybeClearJunk, the console, and LoadSceneData when a scene's
        //                    save says its junk was cleared.
        //
        // m_ClearedJunk is a single bool on a JunkManager that lives as long as the game,
        // and it is written into the scene's save. A clone lives inside the region scene,
        // so:
        //
        //   1. Clearing ONE clone set the REGION's flag. From then on every gate above
        //      refused in every other clone - and the flag went into the region's save, so
        //      a load did not help either. The prompt could be forced back (the old
        //      CanClearJunk patch did), but R still did nothing.
        //   2. The same press hid the junk of EVERY clone in the region, closed ones
        //      included, because ClearJunk searches inactive objects too.
        //
        // The old patches on CanClearJunk and MaybeClearJunk are gone. Nothing in the game
        // calls MaybeClearJunk directly and its prefix never logged in play, so nothing here
        // relies on seeing that call.
        //
        // THE FIX
        //   * While the player customizes inside a clone, m_ClearedJunk holds THAT clone's
        //     state. The region's own value goes back the moment they stop, and around every
        //     scene save. Every gate reads this one flag, so the game's own R path works as
        //     it is, whatever calls it.
        //   * Around ClearJunk, junk the call had no business touching is switched back on:
        //     only the clone the player is customizing gets cleared.
        //   * Whether a building's junk has been cleared is stored per instance in its own
        //     JSON and re-applied once the clone is ready.
        // ─────────────────────────────────────────────────────────────────

        private static string GetJunkStateSavePath(SeamlessInteriorInstance instance)
        {
            return GetInstanceSavePath(instance, "_junk.json");
        }

        private static bool IsPlainJunk(Il2Cpp.JunkTag tag)
        {
            if (tag == null || tag.gameObject == null) return false;

            Transform t = tag.transform;
            while (t != null)
            {
                if (t.GetComponent<Il2Cpp.GearItem>() != null) return false;
                if (t.GetComponent<Il2CppTLD.Placement.Placeable>() != null) return false;
                t = t.parent;
            }
            return true;
        }

        public static int ClearJunkInInstance(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return 0;

            int count = 0;
            var tags = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.JunkTag>(true);
            foreach (var tag in tags)
            {
                if (!IsPlainJunk(tag)) continue;
                if (!tag.gameObject.activeSelf) continue;

                tag.gameObject.SetActive(false);
                instance.JunkClearedObjects.Add(tag.gameObject);
                count++;
            }

            instance.JunkCleared = true;
            return count;
        }

        private static int RestoreJunkInInstance(SeamlessInteriorInstance instance)
        {
            if (instance == null) return 0;

            int count = 0;
            foreach (var go in instance.JunkClearedObjects)
            {
                if (go == null) continue;
                if (go.activeSelf) continue;

                go.SetActive(true);
                count++;
            }

            instance.JunkClearedObjects.Clear();
            instance.JunkCleared = false;
            return count;
        }

        // ─── The flag, as seen from inside a clone ───

        // The clone whose state m_ClearedJunk holds right now. Null means the flag holds
        // the region's own value, which is the normal state.
        private static SeamlessInteriorInstance s_JunkFlagOwner;

        // The region's own value, put back when the player stops customizing.
        private static bool s_RegionJunkCleared;

        // Which clone the player stands in needs raycasts. Customizing can go on for
        // minutes, and the answer cannot change faster than the player walks.
        private const float JUNK_FLAG_RECHECK_SECONDS = 0.5f;
        private static float s_JunkFlagNextRecheck;

        private static Il2Cpp.JunkManager TryGetJunkManager()
        {
            try { return GameManager.GetJunkManager(); }
            catch { return null; }
        }

        private static bool IsCustomizingSafehouse()
        {
            try
            {
                var sm = GameManager.GetSafehouseManager();
                return sm != null && sm.IsCustomizing();
            }
            catch { return false; }
        }

        // Every frame, from OnUpdate. Outside customizing mode - nearly always - this is
        // two IL2CPP calls.
        public static void TickJunkFlagView()
        {
            if (!IsCustomizingSafehouse())
            {
                if (s_JunkFlagOwner != null) EndJunkFlagView();
                return;
            }

            if (Time.realtimeSinceStartup < s_JunkFlagNextRecheck) return;
            s_JunkFlagNextRecheck = Time.realtimeSinceStartup + JUNK_FLAG_RECHECK_SECONDS;

            SeamlessInteriorInstance inside = GetInstancePlayerIsIn();
            if (ReferenceEquals(inside, s_JunkFlagOwner)) return;

            EndJunkFlagView();
            if (inside != null) BeginJunkFlagView(inside);
        }

        // StartCustomizing asks CanClearJunk for the "R" prompt inside the same call, so
        // the clone's state has to be in the flag before it runs - the tick would only get
        // there a frame later, with the prompt already decided.
        public static void OnStartCustomizing()
        {
            SeamlessInteriorInstance inside = GetInstancePlayerIsIn();
            if (inside == null) return;

            BeginJunkFlagView(inside);
            s_JunkFlagNextRecheck = Time.realtimeSinceStartup + JUNK_FLAG_RECHECK_SECONDS;
        }

        private static void BeginJunkFlagView(SeamlessInteriorInstance instance)
        {
            if (ReferenceEquals(instance, s_JunkFlagOwner)) return;
            EndJunkFlagView();

            Il2Cpp.JunkManager jm = TryGetJunkManager();
            if (jm == null) return;

            s_RegionJunkCleared = jm.m_ClearedJunk;
            s_JunkFlagOwner = instance;
            jm.m_ClearedJunk = instance.JunkCleared;

            if (s_DebugBounds)
                MelonLogger.Msg($"[JUNK] {instance.Config.ResolvedInstanceId}: duzenleme modu, cop bayragi klonun durumuna cekildi " +
                                $"(klon={instance.JunkCleared}, bolge={s_RegionJunkCleared}).");
        }

        // Also called on a region change: the next scene's restore has to find the
        // region's own value in the flag.
        public static void EndJunkFlagView()
        {
            if (s_JunkFlagOwner == null) return;

            Il2Cpp.JunkManager jm = TryGetJunkManager();
            if (jm != null) jm.m_ClearedJunk = s_RegionJunkCleared;

            s_JunkFlagOwner = null;
        }

        // A scene save writes the flag into the REGION's save, so it has to see the
        // region's own value, never the clone's. StopCustomizing triggers a save itself,
        // a frame before the tick notices that customizing has ended.
        public static void BeforeSceneSave()
        {
            if (s_JunkFlagOwner == null) return;

            Il2Cpp.JunkManager jm = TryGetJunkManager();
            if (jm != null) jm.m_ClearedJunk = s_RegionJunkCleared;
        }

        public static void AfterSceneSave()
        {
            if (s_JunkFlagOwner == null) return;

            Il2Cpp.JunkManager jm = TryGetJunkManager();
            if (jm != null) jm.m_ClearedJunk = s_JunkFlagOwner.JunkCleared;
        }

        // ─── Keeping ClearJunk inside the clone being customized ───

        private struct JunkBeforeClear
        {
            public GameObject Go;
            public SeamlessInteriorInstance Instance;   // null: not part of any clone
        }

        // Junk that was switched on right before the game's ClearJunk ran.
        private static readonly List<JunkBeforeClear> s_JunkBeforeClear = new List<JunkBeforeClear>();

        public static void BeforeGameClearJunk()
        {
            s_JunkBeforeClear.Clear();
            if (ActiveInteriors.Count == 0) return;

            // The same search ClearJunk makes, inactive objects included, so whatever it
            // is about to switch off is on this list first.
            foreach (var tag in UnityEngine.Object.FindObjectsOfType<Il2Cpp.JunkTag>(true))
            {
                if (tag == null) continue;

                GameObject go = tag.gameObject;
                if (go == null || !go.activeSelf) continue;

                JunkBeforeClear entry;
                entry.Go = go;
                entry.Instance = FindInstanceOwning(tag.transform);
                s_JunkBeforeClear.Add(entry);
            }
        }

        public static void AfterGameClearJunk()
        {
            // A clear the player asked for happens in customizing mode, inside the clone
            // that holds the flag. Anything else - LoadSceneData re-applying a region's saved
            // flag, the console - clears the scene, and no clone at all.
            SeamlessInteriorInstance target = s_JunkFlagOwner;
            try
            {
                if (SaveGameSystem.IsRestoreInProgress() || SaveGameSystem.IsSceneRestoreInProgress())
                    target = null;
            }
            catch { }

            int clearedHere = 0;
            int switchedBack = 0;

            foreach (JunkBeforeClear entry in s_JunkBeforeClear)
            {
                if (entry.Go == null || entry.Go.activeSelf) continue;

                if (entry.Instance == null)
                {
                    // Junk of the scene itself: the region's own clear takes it, a clone's
                    // clear does not.
                    if (target == null) continue;
                }
                else if (ReferenceEquals(entry.Instance, target))
                {
                    // Written down, so a load of an older save can switch it back on
                    // (RestoreJunkInInstance only re-enables what is on this list).
                    target.JunkClearedObjects.Add(entry.Go);
                    clearedHere++;
                    continue;
                }

                entry.Go.SetActive(true);
                switchedBack++;
            }

            s_JunkBeforeClear.Clear();

            if (target != null)
            {
                // Marks the building, and hides anything the game's pass did not reach.
                clearedHere += ClearJunkInInstance(target);

                MelonLogger.Msg($"[JUNK] {target.Config.ResolvedInstanceId}: copler temizlendi ({clearedHere} obje). " +
                                $"Oyunun temizligi baska yerlerden {switchedBack} copu da kapatmisti, geri acildi.");
            }
            else if (switchedBack > 0)
            {
                MelonLogger.Msg($"[JUNK] Oyunun cop temizligi klonlardaki {switchedBack} copu da kapatmisti, geri acildi " +
                                $"(klonun copu klonun kendi kaydina bagli).");
            }
        }

        // ─── Persistence ───

        public static void SaveAllJunkStates()
        {
            foreach (var instance in ActiveInteriors.Values)
                SaveJunkState(instance);
        }

        public static void SaveJunkState(SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null || !instance.RunCompleted) return;
            if (!CanPersistContent(instance)) return;

            string path = GetJunkStateSavePath(instance);
            if (path == null) return;

            // A single flag is all this needs, so the JSON is written by hand.
            JsonWriteCache.Write(path, $"{{\"cleared\":{(instance.JunkCleared ? "true" : "false")}}}");

            if (s_DebugBounds)
                MelonLogger.Msg($"[JUNK-SAVE] {instance.Config.ResolvedInstanceId}: cleared={instance.JunkCleared}");
        }

        public static void RestoreJunkState(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;

            string path = GetJunkStateSavePath(instance);
            bool cleared = false;

            if (path != null && File.Exists(path))
            {
                try
                {
                    cleared = File.ReadAllText(path).Contains("true");
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[JUNK-LOAD] {instance.Config.ResolvedInstanceId} durum dosyasi okunamadi: {ex.Message}");
                    return;
                }
            }

            if (cleared)
            {
                int closed = ClearJunkInInstance(instance);
                if (s_DebugBounds)
                    MelonLogger.Msg($"[JUNK-LOAD] {instance.Config.ResolvedInstanceId}: kayit 'temizlendi' diyor, {closed} cop kapatildi.");
            }
            else
            {
                int opened = RestoreJunkInInstance(instance);
                if (s_DebugBounds && opened > 0)
                    MelonLogger.Msg($"[JUNK-LOAD] {instance.Config.ResolvedInstanceId}: kayit 'temizlenmedi' diyor, {opened} cop geri acildi.");
            }

            // The player may be customizing in this very building right now (a persisted
            // clone put back, a load on the doorstep): the flag has to follow.
            if (ReferenceEquals(instance, s_JunkFlagOwner))
            {
                Il2Cpp.JunkManager jm = TryGetJunkManager();
                if (jm != null) jm.m_ClearedJunk = instance.JunkCleared;
            }
        }
    }
}
