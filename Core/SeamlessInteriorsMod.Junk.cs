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
        // SAFEHOUSE "CLEAR JUNK" (R) STATE
        //
        // PROBLEM: the game's JunkManager keeps a SINGLE bool per scene
        // (JunkManager.m_ClearedJunk -> SceneSaveGameFormat.m_JunkManagedSerialized).
        // When the player presses R inside a clone:
        //   1. The game clears every JunkTag object that is currently ACTIVE
        //      (clone ones included - the clone lives inside the region scene).
        //   2. m_ClearedJunk = true is written to the REGION scene's save data.
        //
        // After a load:
        //   - The game restores the region scene's junk clearing itself, but the
        //     clone scene is rebuilt FROM SCRATCH by the mod -> its junk is back.
        //   - m_ClearedJunk is still true, so CanClearJunk() returns false -> the R
        //     key does nothing and the HUD hint never appears.
        //
        // FIX (same pattern as the other clone data):
        //   - "Has this building's junk been cleared" is stored per instance in its
        //     own JSON and re-applied once the clone is ready.
        //   - The CanClearJunk patch re-enables R, despite the game's global flag,
        //     whenever the player is inside a clone whose junk is NOT yet cleared.
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

        private static bool HasClearableJunk(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return false;

            // Cache: the answer can only change when junk is cleared or restored, and
            // both paths bump s_JunkScanGeneration. The scan is expensive (all
            // JunkTags plus a parent-chain walk for each) and the HUD asks constantly.
            //
            // Safety: in case some unknown path destroys a JunkTag, the cache lives at
            // most JUNK_SCAN_MAX_AGE seconds.
            if (instance.JunkScanStamp == s_JunkScanGeneration
                && Time.time - instance.JunkScanTime < JUNK_SCAN_MAX_AGE)
            {
                return instance.JunkScanResult;
            }

            bool result = false;
            var tags = instance.MasterInterior.GetComponentsInChildren<Il2Cpp.JunkTag>(true);
            foreach (var tag in tags)
            {
                if (!IsPlainJunk(tag)) continue;
                if (tag.gameObject.activeSelf) { result = true; break; }
            }

            instance.JunkScanStamp = s_JunkScanGeneration;
            instance.JunkScanTime = Time.time;
            instance.JunkScanResult = result;
            return result;
        }

        // Maximum age of the junk-scan cache, in seconds.
        private const float JUNK_SCAN_MAX_AGE = 5f;

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

        private static bool PlayerIsAtInstance(SeamlessInteriorInstance instance, Vector3 playerPos)
        {
            return instance.IsPositionInside(playerPos) || instance.IsPositionInVolume(playerPos, 0.5f);
        }

        public static void OnGameClearedJunk()
        {
            // During a load the game may also call ClearJunk to re-apply the saved
            // "junk cleared" flag. That call is NOT a player action and must not mark
            // the clone - the clone's own state comes from RestoreJunkState.
            try
            {
                if (SaveGameSystem.IsRestoreInProgress() || SaveGameSystem.IsSceneRestoreInProgress())
                    return;
            }
            catch { }

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) return;
            Vector3 pos = playerT.position;

            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance.MasterInterior == null) continue;
                if (!instance.MasterInterior.activeSelf) continue;
                if (!PlayerIsAtInstance(instance, pos)) continue;

                int closed = ClearJunkInInstance(instance);
                InvalidateJunkPromptCache();

                if (s_DebugBounds)
                    MelonLogger.Msg($"[JUNK] {instance.Config.ResolvedInstanceId}: copler temizlendi (mod ek olarak {closed} obje kapatti).");
            }
        }

        // CanClearJunk is queried by the HUD every frame. The answer needs a raycast
        // plus GetComponentsInChildren, so it is cached briefly.
        private const float JUNK_PROMPT_CACHE_SECONDS = 0.25f;
        private static float s_JunkPromptCacheTime = -1f;
        private static bool s_JunkPromptCacheValue = false;

        // Bumped on every invalidation; also invalidates all per-instance
        // HasClearableJunk caches at once (see SeamlessInteriorInstance.JunkScanStamp).
        private static int s_JunkScanGeneration = 0;

        private static void InvalidateJunkPromptCache()
        {
            s_JunkPromptCacheTime = -1f;
            s_JunkScanGeneration++;
        }

        public static bool PlayerIsInInstanceWithClearableJunk()
        {
            if (s_JunkPromptCacheTime >= 0f && Time.time - s_JunkPromptCacheTime < JUNK_PROMPT_CACHE_SECONDS)
                return s_JunkPromptCacheValue;

            s_JunkPromptCacheTime = Time.time;
            s_JunkPromptCacheValue = ComputePlayerIsInInstanceWithClearableJunk();
            return s_JunkPromptCacheValue;
        }

        private static bool ComputePlayerIsInInstanceWithClearableJunk()
        {
            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) return false;
            Vector3 pos = playerT.position;

            foreach (var instance in ActiveInteriors.Values)
            {
                if (!instance.RunCompleted || instance.MasterInterior == null) continue;
                if (!instance.MasterInterior.activeSelf) continue;
                if (instance.JunkCleared) continue;

                // Cheap geometric test first, expensive raycast second.
                // (Without an InteriorTrigger the volume test is impossible, so we fall
                // straight through to the raycast - see IsPositionInVolume.)
                if (instance.InteriorTrigger != null && !instance.IsPositionInVolume(pos, 0.5f)) continue;
                if (!PlayerIsAtInstance(instance, pos)) continue;

                if (HasClearableJunk(instance)) return true;
            }
            return false;
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

            InvalidateJunkPromptCache();

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
        }
    }
}
