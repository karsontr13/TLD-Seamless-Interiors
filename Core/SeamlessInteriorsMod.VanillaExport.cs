using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SeamlessInteriors
{
    // ─────────────────────────────────────────────────────────────────────
    // PREPARE A SAVE FOR UNINSTALL
    //
    // The reverse of LegacyImport.cs. Every building the mod holds data for, in any region, is
    // written into the game's save under the key the base game reads for that interior scene,
    // as a SceneSaveGameFormat, so loading the save without the mod finds the player's things in
    // the real interiors.
    //
    // Built from the mod's own files (the player's state), the building's facts
    // (VanillaIndex.cs: the scene's ids and the clone's place) and the region's game save
    // (fires), so the building's region does not have to be loaded.
    //
    // Only on the player's request (settings page). The mod never reads these keys again for a
    // building it owns (its _legacy.json marker says so), so a save that goes on being played
    // with the mod is not changed by them; it only holds an older copy.
    //
    // Clone-local space IS interior-scene space (see LegacyImport.cs), so positions are only
    // copied, except where the mod's own records hold world positions.
    // ─────────────────────────────────────────────────────────────────────
    public partial class SeamlessInteriorsMod
    {
        private static bool s_VanillaExportArmed;

        // Settings page: save once with the export on.
        public static void RequestVanillaExport()
        {
            if (string.IsNullOrEmpty(SaveGameSystem.m_CurrentSaveName))
            {
                ShowExportMessage("Seamless Interiors: load your save first.");
                return;
            }
            MelonCoroutines.Start(VanillaExportRoutine());
        }

        private static IEnumerator VanillaExportRoutine()
        {
            yield return null;
            s_VanillaExportArmed = true;
            ShowExportMessage("Seamless Interiors: saving your buildings for the base game...");
            try { GameManager.TriggerSurvivalSaveAndDisplayHUDMessage(); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KALDIRMA] Kayit baslatilamadi: {ex.Message}", $"[UNINSTALL] Could not start the save: {ex.Message}");
            }
        }

        private static string GetExportFilePath(string saveName, string id, string suffix)
        {
            return Path.Combine(ModPaths.DataDir(), saveName + "_" + id + suffix);
        }

        // SaveSceneData postfix (last), after the mod's own files and SC+'s records were written for this save.
        public static void WriteVanillaExportIfArmed(SlotData slot)
        {
            UpdateAllVanillaFacts();

            if (!s_VanillaExportArmed || slot == null) return;
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName) || !SlotBelongsToSave(slot, saveName)) return;
            s_VanillaExportArmed = false;

            int written = 0, failed = 0;
            var missing = new Dictionary<string, int>();
            var usedKeys = new HashSet<string>();
            var exported = new List<VanillaRoomFacts>();
            var fireCache = new Dictionary<string, Dictionary<string, string>>();
            var regionCache = new Dictionary<string, RegionSave>();

            foreach (var cfg in SupportedInteriors)
            {
                string id = cfg.ResolvedInstanceId;

                // A building the mod owns has its gear file, so ProbeLegacySave never imports it again.
                string gearPath = GetExportFilePath(saveName, id, "_inactive_scene_gear.json");
                if (!SaveFiles.Exists(gearPath)) continue;

                try
                {
                    VanillaRoomFacts facts = null;
                    SeamlessInteriorInstance live;
                    if (ActiveInteriors.TryGetValue(id, out live) && live != null && live.VanillaFacts != null && live.VanillaFacts.HasTransform)
                        facts = live.VanillaFacts;
                    if (facts == null) facts = FindVanillaFacts(id);
                    BorrowSceneFacts(facts);
                    if (facts == null || !facts.HasTransform || string.IsNullOrEmpty(facts.Key))
                    {
                        int n;
                        missing.TryGetValue(cfg.ExteriorSceneName, out n);
                        missing[cfg.ExteriorSceneName] = n + 1;
                        MelonLogger.Warning($"[KALDIRMA] {id}: bu bina icin dizin yok, atlandi.", $"[UNINSTALL] {id}: no index for this building, skipped.");
                        continue;
                    }
                    // The door of the town layout this save has (Milton's random houses).
                    bool absent, guessed;
                    var regionSave = RegionSaveOf(saveName, facts.Region, regionCache);
                    string key = facts.KeyFor(regionSave.Choices, regionSave.Doors, out absent, out guessed);
                    if (absent)
                    {
                        MelonLogger.Msg($"[KALDIRMA] {id}: bu kayittaki yerlesimde bu ev yok, atlandi.", $"[UNINSTALL] {id}: not part of this save's town layout, skipped.");
                        continue;
                    }
                    if (guessed && key != null && key != facts.Scene)
                        MelonLogger.Warning($"[KALDIRMA] {id}: kapinin bu kayittaki kimligi bulunamadi, dizindeki kullanildi ({key}).",
                                            $"[UNINSTALL] {id}: the door's id in this save was not found, the index's is used ({key}).");
                    if (string.IsNullOrEmpty(key) || !usedKeys.Add(key))
                    {
                        MelonLogger.Warning($"[KALDIRMA] {id}: '{key}' anahtari baska binada, atlandi.", $"[UNINSTALL] {id}: key '{key}' already used, skipped.");
                        failed++;
                        continue;
                    }
                    facts.ExportKey = key;

                    string summary;
                    string json = BuildVanillaSceneJson(saveName, id, facts, FireRecordsOf(saveName, facts.Region, fireCache), out summary);
                    var fmt = Utils.DeserializeObject<SceneSaveGameFormat>(json);
                    if (fmt == null || string.IsNullOrEmpty(fmt.m_GearManagerSerialized))
                    {
                        MelonLogger.Warning($"[KALDIRMA] {id}: sahne kaydi kurulamadi, atlandi.", $"[UNINSTALL] {id}: scene record could not be built, skipped.");
                        failed++;
                        continue;
                    }

                    BackUpVanillaKey(saveName, id, key);
                    if (!SaveGameSlots.SaveDataToSlot(slot, key, fmt))
                    {
                        MelonLogger.Warning($"[KALDIRMA] {id}: oyun '{key}' kaydini kabul etmedi.", $"[UNINSTALL] {id}: the game refused the record '{key}'.");
                        failed++;
                        continue;
                    }

                    try { File.WriteAllText(GetExportFilePath(saveName, id, "_vanilla_export.json"), json); } catch { }
                    MelonLogger.Msg($"[KALDIRMA] {id} -> '{key}': {summary}", $"[UNINSTALL] {id} -> '{key}': {summary}");
                    exported.Add(facts);
                    written++;
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[KALDIRMA] {id}: hata - {ex.Message}", $"[UNINSTALL] {id}: error - {ex.Message}");
                    failed++;
                }
            }

            // The region's own records of the clones' containers would land in a lost-and-found box (SI destroys those boxes).
            StripCloneContainersFromRegions(slot, saveName, exported);

            // Objects moved with Safehouse Customization+ go to the interior's own SC+ records.
            ModExceptions.ExportSafehouseCustomizationPlus(exported, saveName);

            // Which buildings the game now holds, for a reinstall after playing without the mod (VanillaReturn.cs).
            RecordVanillaExport(saveName, exported);

            var msg = new StringBuilder($"Seamless Interiors: {written} buildings are ready for the base game.");
            if (failed > 0) msg.Append($" {failed} could not be written (see the log).");
            if (missing.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in missing) parts.Add($"{kv.Key} ({kv.Value})");
                msg.Append(" Not prepared, enter these regions once and repeat: ").Append(string.Join(", ", parts)).Append('.');
            }
            MelonLogger.Msg($"[KALDIRMA] {written} bina yazildi, {failed} hata, {missing.Count} bolgede dizin yok.", "[UNINSTALL] " + msg);
            ShowExportMessage(msg.ToString());
        }

        // A region's container record belongs to a clone when its id is an interior scene's (or carries "_CLONE"),
        // or it lies inside an SI room and has the name of one of that room's containers.
        private static void StripCloneContainersFromRegions(SlotData slot, string saveName, List<VanillaRoomFacts> exported)
        {
            // Every region the mod has buildings in; one the player never visited has no record and is skipped.
            var regions = new HashSet<string>();
            foreach (var cfg in SupportedInteriors) regions.Add(cfg.ExteriorSceneName);

            foreach (string region in regions)
            {
                try
                {
                    SceneSaveGameFormat fmt = null;
                    string raw;
                    if (!SaveGameSlots.TryLoadDataFromSlot<SceneSaveGameFormat>(saveName, region, out fmt) || fmt == null)
                    {
                        fmt = null;
                        if (TryLoadSlotString(saveName, region, false, out raw)) fmt = Utils.DeserializeObject<SceneSaveGameFormat>(raw);
                    }
                    if (fmt == null || string.IsNullOrEmpty(fmt.m_ContainerManagerSerialized)) continue;

                    var list = Utils.DeserializeObject<ContainerSaveList>(fmt.m_ContainerManagerSerialized);
                    if (list == null || list.m_SerializedContainers == null) continue;

                    // Every SI building of the region, exported or not: their clones all leave records.
                    var rooms = new List<VanillaRoomFacts>();
                    var interiorIds = new HashSet<string>();
                    foreach (var cfg in SupportedInteriors)
                    {
                        if (cfg.ExteriorSceneName != region) continue;
                        SeamlessInteriorInstance live;
                        VanillaRoomFacts f = ActiveInteriors.TryGetValue(cfg.ResolvedInstanceId, out live) && live != null ? live.VanillaFacts : null;
                        if (f == null) f = FindVanillaFacts(cfg.ResolvedInstanceId);
                        if (f == null) continue;
                        BorrowSceneFacts(f);
                        rooms.Add(f);
                        foreach (var fc in f.Containers) interiorIds.Add(fc.Guid);
                    }

                    var removedNames = new List<string>();
                    for (int i = list.m_SerializedContainers.Count - 1; i >= 0; i--)
                    {
                        var c = list.m_SerializedContainers[i];
                        if (c == null || !IsCloneContainerRecord(c, region, rooms, interiorIds)) continue;
                        removedNames.Add(c.m_PrefabName);
                        list.m_SerializedContainers.RemoveAt(i);
                    }
                    if (removedNames.Count == 0) continue;

                    string backup = GetExportFilePath(saveName, region, "_region_before_export.json");
                    try { if (!File.Exists(backup)) File.WriteAllText(backup, Utils.SerializeObject(fmt)); } catch { }

                    fmt.m_ContainerManagerSerialized = Utils.SerializeObject(list);
                    if (SaveGameSlots.SaveDataToSlot(slot, region, fmt))
                        MelonLogger.Msg($"[KALDIRMA] {region}: bolge kaydindan {removedNames.Count} klon konteyneri cikarildi ({string.Join(", ", removedNames)}).",
                                        $"[UNINSTALL] {region}: {removedNames.Count} clone containers taken out of the region's record ({string.Join(", ", removedNames)}).");
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[KALDIRMA] {region}: bolge kaydi temizlenemedi - {ex.Message}", $"[UNINSTALL] {region}: region record not cleaned - {ex.Message}");
                }
            }
        }

        private static bool IsCloneContainerRecord(ContainerSaveData c, string region, List<VanillaRoomFacts> rooms, HashSet<string> interiorIds)
        {
            if (!string.IsNullOrEmpty(c.m_Guid) && (c.m_Guid.EndsWith("_CLONE") || interiorIds.Contains(c.m_Guid))) return true;
            string name = CleanGearName(c.m_PrefabName ?? "");
            foreach (var f in rooms)
            {
                if (f.Region != region || !f.HasTransform || !f.ContainsLocal(f.ToLocal(c.m_Position), 0.5f)) continue;
                foreach (var fc in f.Containers)
                    if (fc.Name == name) return true;
            }
            return false;
        }

        private static void ShowExportMessage(string text)
        {
            try { HUDMessage.AddMessage(text, 10f, true, true); } catch { }
        }

        // Before a key is replaced, its current value is kept once in the mod's data folder.
        private static void BackUpVanillaKey(string saveName, string id, string key)
        {
            string path = GetExportFilePath(saveName, id, "_vanilla_before_export.json");
            if (File.Exists(path)) return;
            string raw;
            if (TryLoadSlotString(saveName, key, false, out raw) && !string.IsNullOrEmpty(raw))
            {
                try { File.WriteAllText(path, raw); } catch { }
            }
        }

        // ─── The key the base game reads ───
        // An instanced scene is saved as "<scene>_<door guid>", any other as "<scene>".
        private static string ResolveVanillaSceneKey(SeamlessInteriorInstance instance)
        {
            string scene = instance.Config.InteriorSceneBaseName;
            if (string.IsNullOrEmpty(scene)) return null;

            string over = instance.Config.LegacySceneKeyOverride;
            if (!string.IsNullOrEmpty(over)) return over;

            bool shared = CountInstancesSharingScene(instance) > 1;
            LoadScene door = FindVanillaEntryDoor(instance, shared);
            if (door != null) return door.m_SceneCanBeInstanced ? scene + "_" + door.m_GUID : scene;

            // The key a pre-mod import read is the right one too.
            if (instance.LegacySource == LegacySourceKind.Name && !string.IsNullOrEmpty(instance.LegacySourceId))
                return instance.LegacySourceId;

            return shared ? null : scene;
        }

        private static LoadScene FindVanillaEntryDoor(SeamlessInteriorInstance instance, bool shared)
        {
            string wanted = instance.Config.InteriorSceneBaseName;
            Il2CppSystem.Collections.Generic.List<LoadScene> doors = null;
            try { doors = LoadScene.m_LoadScenesList; } catch { }
            if (doors == null) return null;

            // A door of a town layout the save did not pick is switched off; the live one wins.
            LoadScene best = null, bestOff = null, anyInClone = null;
            float bestDist = 60f, bestOffDist = 60f;
            for (int i = 0; i < doors.Count; i++)
            {
                LoadScene d = doors[i];
                if (d == null || d.gameObject == null || d.m_SceneToLoad != wanted || string.IsNullOrEmpty(d.m_GUID)) continue;

                // A cellar door sits inside the parent building's clone.
                if (IsUnderAnyMasterInterior(d.transform))
                {
                    if (anyInClone == null) anyInClone = d;
                    continue;
                }
                float dist = Vector3.Distance(d.transform.position, instance.Config.FallbackPosition);
                if (d.gameObject.activeInHierarchy) { if (dist < bestDist) { bestDist = dist; best = d; } }
                else if (dist < bestOffDist) { bestOffDist = dist; bestOff = d; }
            }
            if (best != null) return best;
            if (bestOff != null) return bestOff;
            return shared ? null : anyInClone;
        }

        // ─── The scene record ───
        private static string BuildVanillaSceneJson(string saveName, string id, VanillaRoomFacts facts,
                                                    Dictionary<string, string> regionFires, out string summary)
        {
            int nGear, nCont, nPlace, nRemoved, nState, nFire;

            var placeRecords = ReadPlaceableRecords(GetExportFilePath(saveName, id, "_placeables.json"));
            var spawnRecords = ReadSpawnRecords(GetExportFilePath(saveName, id, "_spawn_placeables.json"));

            string gear = BuildExportGear(GetExportFilePath(saveName, id, "_inactive_scene_gear.json"), out nGear);
            string containers = BuildExportContainers(GetExportFilePath(saveName, id, "_containers.json"), facts, placeRecords, spawnRecords, out nCont);
            string curing, photos;
            string placements = BuildExportPlacements(facts, placeRecords, spawnRecords, out nPlace, out nRemoved, out curing, out photos);
            string breakDowns, harvestables, openClosers, brokenDecor;
            nState = BuildExportStates(GetExportFilePath(saveName, id, "_interactive_v2.json"), facts,
                                       out breakDowns, out harvestables, out openClosers, out brokenDecor);
            string fires = BuildExportFires(facts, regionFires, out nFire);
            bool junkCleared = ReadJunkCleared(GetExportFilePath(saveName, id, "_junk.json"));

            float hours = 0f;
            try { hours = GameManager.GetTimeOfDayComponent().GetHoursPlayedNotPaused(); } catch { }
            int version = 4;
            try { if (SaveGameSystem.m_SceneSaveGameFormatVersion > 0) version = SaveGameSystem.m_SceneSaveGameFormatVersion; } catch { }

            var sb = new StringBuilder(4096);
            sb.Append("{\"m_Version\":").Append(version);
            Str(sb, "m_ContainerManagerSerialized", containers);
            Str(sb, "m_GearManagerSerialized", gear);
            Str(sb, "m_GearSpawnInOldSavesGUIDs", BuildSpawnedInOldSavesList(facts));
            Str(sb, "m_FireManagerSerialized", fires);
            Str(sb, "m_BreakDownObjectsSerialized", breakDowns);
            if (harvestables != null) Str(sb, "m_HarvestablesSerialized", harvestables);
            if (openClosers != null) Str(sb, "m_OpenClosersSerialized", openClosers);

            // Everything else as the game writes it for a room it has nothing to say about.
            Str(sb, "m_ArrowManagerSerialized", "{\"m_SerializedItems\":[]}");
            Str(sb, "m_FlareGunRoundManagerSerialized", "{\"m_SerializedItems\":[]}");
            Str(sb, "m_MusicTriggersSerialized", "{\"strings\":[]}");
            Str(sb, "m_LoadSceneTriggersSerialized", "{\"strings\":[]}");
            Str(sb, "m_RandomSpawnObjectManagerSerialized", "{\"m_SaveDataList\":[]}");
            Str(sb, "m_WaterSourceManagerSerialized", "{\"m_SerializedWaterSources\":[]}");
            Str(sb, "m_BodyHarvestManagerSerialized", "{\"m_SerializedBodyHarvests\":[]}");
            Str(sb, "m_DynamicDecalsManagerSerialized", "{\"m_SerializedDecalProjectors\":[]}");
            Str(sb, "m_FootstepsSerialized", "{\"m_Trails\":[]}");
            Str(sb, "m_SnowShelterManagerSerialized", "{\"m_SerializedSnowShelters\":[]}");
            Str(sb, "m_RockCacheSerialized", "{\"m_SerializedRockCacheList\":[]}");
            Str(sb, "m_BaseAiManagerSerialized", "{\"m_SerializedBaseAI\":[]}");
            Str(sb, "m_ActiveSwitchesSerialized", "[]");
            Str(sb, "m_TLDBehaviourTreeManagerSerialized", "{\"m_TLDBehaviourTreeOwnerProxyList\":[]}");
            Str(sb, "m_AuroraFieldsSerialized", "{\"m_SerializedFields\":[]}");
            Str(sb, "m_QuarteringSiteCarrionSerialized", "{\"m_ProxyDataList\":[]}");
            Str(sb, "m_MapDetailManagerSerialized", "{\"m_MapDetailUnlockedStates\":{},\"m_NewMapDetailStates\":{}}");
            Str(sb, "m_DialogueModeRigFPSerialized", "[]");
            Str(sb, "m_ScriptLightSerialized", "[]");
            Str(sb, "m_PersistentControllerStateSerialized", "[]");
            Str(sb, "m_PackManagerSerialized", "{\"m_SerializedDataProxyList\":[],\"m_RemoveOnLoadGuids\":[],\"m_SerializedTime\":" + F(hours) + "}");
            Str(sb, "m_AuroraElectrolizerControllerSerialized", "[]");
            Str(sb, "m_PrefabSpawnSerialized", "");
            Str(sb, "m_SpawnWildlife_TriggerSerialized", "[]");
            Str(sb, "m_NPCSerialized", "[]");
            Str(sb, "m_BedSerialized", "{\"strings\":[]}");
            Str(sb, "m_SteamPipeValveSerialized", "{\"m_SerializedSteamPipeValves\":[],\"m_SerializedSteamPipeValveSockets\":[]}");
            Str(sb, "m_SteamPipeSerialized", "{\"m_SerializedSteamPipes\":[]}");
            Str(sb, "m_ElevatorSerialized", "{\"strings\":[]}");
            Str(sb, "m_SpawnGearVariantsSerialized", "[]");
            Str(sb, "m_CustomSpawnedItemManagerSerialized", "[]");
            Str(sb, "m_BeachcombingSpawnersSerialized", "[]");

            sb.Append(",\"m_BrokenDecorationSaveData\":").Append(brokenDecor);
            sb.Append(",\"m_PlacementListSerialized\":").Append(placements);
            sb.Append(",\"m_DecorationItemData\":{\"m_ParentData\":[],\"m_CraftedItemPlacementPendingData\":[]}");
            if (curing != null) sb.Append(",\"m_CuringStorageSerialized\":").Append(curing);
            if (photos != null) sb.Append(",\"m_FramedPhotoSerialized\":").Append(photos);
            sb.Append(",\"m_JunkManagedSerialized\":{\"m_ClearedJunk\":").Append(junkCleared ? "true" : "false").Append('}');
            sb.Append('}');

            summary = $"{nGear} gear ({facts.SceneIds.Count} scene ids), {nCont} containers, {nPlace} placed/moved, " +
                      $"{nRemoved} removed, {nState} states, {nFire} fires";
            return sb.ToString();
        }

        // Gear and loot pickers marked "spawn in old saves" are only deleted / left alone on load when their
        // id is in m_GearSpawnInOldSavesGUIDs; without it the scene's own copy stays next to the saved one.
        private static string BuildSpawnedInOldSavesList(VanillaRoomFacts facts)
        {
            var items = new List<string>(facts.SceneIds.Count);
            foreach (var g in facts.SceneIds) items.Add(JsonString(g));
            return "[" + string.Join(",", items) + "]";
        }

        // ─── The mod's files ───
        private class SpawnRecord { public string Guid, Data, Element; public Vector3 Position; public bool HasPosition; }

        private static Dictionary<string, PlaceableEntry> ReadPlaceableRecords(string path)
        {
            var records = new Dictionary<string, PlaceableEntry>();
            if (!SaveFiles.Exists(path)) return records;
            foreach (var element in SplitJsonArray(SaveFiles.Read(path)))
            {
                var e = ParseEntry(element);
                if (e == null || string.IsNullOrEmpty(e.guid) || e.guid == StableGuidMarker) continue;
                records[e.guid] = e;
            }
            return records;
        }

        private static List<SpawnRecord> ReadSpawnRecords(string path)
        {
            var result = new List<SpawnRecord>();
            if (!SaveFiles.Exists(path)) return result;
            foreach (var element in SplitJsonArray(SaveFiles.Read(path)))
            {
                var r = new SpawnRecord { Guid = ExtractString(element, "\"g\":\"", "\""), Data = ExtractEscapedField(element, "d"), Element = element };
                if (string.IsNullOrEmpty(r.Guid) || string.IsNullOrEmpty(r.Data)) continue;
                r.HasPosition = TryReadTopVector(r.Data, "m_Position", out r.Position);
                result.Add(r);
            }
            return result;
        }

        private static bool ReadJunkCleared(string path)
        {
            try { return SaveFiles.Exists(path) && SaveFiles.Read(path).Replace(" ", "").Contains("\"cleared\":true"); }
            catch { return false; }
        }

        // Gear: the payload's world position replaced by the clone-local one the file records.
        private static string BuildExportGear(string path, out int count)
        {
            var items = new List<string>();
            if (SaveFiles.Exists(path))
            {
                foreach (var e in ParseGearFile(SaveFiles.Read(path)))
                {
                    if (e == null || string.IsNullOrEmpty(e.name) || string.IsNullOrEmpty(e.serialized)) continue;
                    string s = SetTopVector(e.serialized, "m_Position", Vec(e.position));
                    s = SetTopVector(s, "m_Rotation", Quat(e.rotation));
                    items.Add($"{{\"m_PrefabName\":\"{EscapeJson(CleanGearPrefabName(e.name))}\",\"m_SearializedGear\":\"{EscapeJson(s)}\"}}");
                }
            }
            count = items.Count;
            return "{\"m_SerializedItems\":[" + string.Join(",", items) + "],\"m_SpawnedGearInOldSavesGUIDs\":[]}";
        }

        // Containers are matched by id on load: the scene's container where it now stands (after the
        // furniture it sits on was moved), else furniture the player placed at that spot.
        private static string BuildExportContainers(string path, VanillaRoomFacts facts, Dictionary<string, PlaceableEntry> placeRecords,
                                                    List<SpawnRecord> spawnRecords, out int count)
        {
            var where = new List<Vector3>(facts.Containers.Count);
            foreach (var c in facts.Containers)
            {
                PlaceableEntry moved;
                if (c.PlaceableGuid != null && placeRecords.TryGetValue(c.PlaceableGuid, out moved))
                {
                    Vector3 scale = moved.scale == Vector3.zero ? Vector3.one : moved.scale;
                    where.Add(moved.position + moved.rotation * Vector3.Scale(scale, c.RelativePosition));
                }
                else where.Add(c.Position);
            }
            var claimed = new bool[facts.Containers.Count];
            float radiusSqr = CONTAINER_RESTORE_RADIUS * CONTAINER_RESTORE_RADIUS;

            var items = new List<string>();
            if (SaveFiles.Exists(path))
            {
                foreach (var rec in ParseContainerFile(SaveFiles.Read(path)))
                {
                    if (!rec.hasPosition || string.IsNullOrEmpty(rec.data)) continue;

                    int best = -1;
                    float bestSqr = radiusSqr;
                    for (int i = 0; i < where.Count; i++)
                    {
                        if (claimed[i]) continue;
                        float sqr = (where[i] - rec.position).sqrMagnitude;
                        if (sqr <= bestSqr) { bestSqr = sqr; best = i; }
                    }

                    string guid = null, name = rec.name;
                    if (best >= 0)
                    {
                        claimed[best] = true;
                        guid = facts.Containers[best].Guid;
                        if (string.IsNullOrEmpty(name)) name = facts.Containers[best].Name;
                    }
                    else
                    {
                        // A crafted container is re-created with its furniture's id.
                        foreach (var s in spawnRecords)
                            if (s.HasPosition && (s.Position - rec.position).sqrMagnitude <= radiusSqr) { guid = s.Guid; break; }
                    }

                    string data = SetTopVector(rec.data, "m_Position", Vec(rec.position));
                    Quaternion worldRot;
                    if (TryReadTopQuat(data, "m_Rotation", out worldRot)) data = SetTopVector(data, "m_Rotation", Quat(facts.ToLocal(worldRot)));

                    var sb = new StringBuilder();
                    sb.Append("{\"m_Position\":").Append(Vec(rec.position));
                    sb.Append(",\"m_PrefabName\":\"").Append(EscapeJson(name ?? "")).Append('"');
                    sb.Append(",\"m_SearializedContainer\":\"").Append(EscapeJson(data)).Append('"');
                    if (!string.IsNullOrEmpty(guid)) sb.Append(",\"m_Guid\":\"").Append(EscapeJson(guid)).Append('"');
                    sb.Append('}');
                    items.Add(sb.ToString());
                }
            }
            count = items.Count;
            return "{\"m_SerializedContainers\":[" + string.Join(",", items) + "]}";
        }

        // Placeables: player-placed ones from the spawn file, the scene's own furniture from the position
        // file; scene furniture that is gone becomes a "Removed" record.
        private static string BuildExportPlacements(VanillaRoomFacts facts, Dictionary<string, PlaceableEntry> placeRecords, List<SpawnRecord> spawnRecords,
                                                    out int placed, out int removed, out string curing, out string photos)
        {
            placed = 0; removed = 0; curing = null; photos = null;
            var items = new List<string>();
            var done = new HashSet<string>();
            var curingStates = new List<string>();
            var photoStates = new List<string>();

            // 1. Player-placed furniture: the record is already a clone-local PlaceableSaveData.
            foreach (var s in spawnRecords)
            {
                if (!done.Add(s.Guid)) continue;
                items.Add($"{{\"m_Guid\":\"{EscapeJson(s.Guid)}\",\"m_Serialized\":{s.Data}}}");
                placed++;

                // Curing box / photo state, keyed by the id the game recreates the object with.
                string cs = ReadEscapedField(s.Element, "\"cs\":\"");
                if (!string.IsNullOrEmpty(cs)) curingStates.Add(SetTopString(cs, "m_GUID", s.Guid));
                string fp = ReadEscapedField(s.Element, "\"fp\":\"");
                if (!string.IsNullOrEmpty(fp)) photoStates.Add(SetTopString(fp, "m_GUID", s.Guid));
            }

            // 2. The scene's furniture with the transform the mod keeps for it; without a record it was taken.
            if (placeRecords.Count > 0)
            {
                foreach (var p in facts.Placeables.Values)
                {
                    if (!done.Add(p.Guid)) continue;
                    PlaceableEntry e;
                    if (!placeRecords.TryGetValue(p.Guid, out e) || !e.active)
                    {
                        items.Add($"{{\"m_Guid\":\"{EscapeJson(p.Guid)}\",\"m_State\":\"Removed\"}}");
                        removed++;
                        continue;
                    }
                    Vector3 scale = e.scale == Vector3.zero ? Vector3.one : e.scale;
                    items.Add($"{{\"m_Guid\":\"{EscapeJson(p.Guid)}\",\"m_Serialized\":{{\"m_ActiveSelf\":true," +
                              $"\"m_Name\":{JsonString(p.Name)},\"m_Addressable\":{JsonString(p.Addressable)}," +
                              $"\"m_Position\":{Vec(e.position)},\"m_Rotation\":{Quat(e.rotation)},\"m_Scale\":{Vec(scale)}}}}}");
                    placed++;
                }
            }

            if (curingStates.Count > 0) curing = "[" + string.Join(",", curingStates) + "]";
            if (photoStates.Count > 0) photos = "[" + string.Join(",", photoStates) + "]";
            return "[" + string.Join(",", items) + "]";
        }

        // Broken-down, harvested, opened objects and repaired cabinet doors, from the mod's file.
        // Smashables and locks have no list of their own in a scene save.
        private static int BuildExportStates(string path, VanillaRoomFacts facts,
                                             out string breakDowns, out string harvestables, out string openClosers, out string brokenDecor)
        {
            var bd = new List<string>();
            var hv = new List<string>();
            var oc = new List<string>();
            var br = new List<string>();

            if (SaveFiles.Exists(path))
            {
                foreach (var kv in ParseStateFile(SaveFiles.Read(path)))
                {
                    int bar = kv.Key.IndexOf('|');
                    string tag = bar > 0 ? kv.Key.Substring(0, bar) : kv.Key;
                    string d = kv.Value;
                    if (string.IsNullOrEmpty(d)) continue;

                    // The mod's records were taken from the parked clone, in world space.
                    Vector3 world;
                    if (TryReadTopVector(d, "m_Position", out world))
                        d = SetTopVector(d, "m_Position", Vec(facts.ToLocal(world)));

                    switch (tag)
                    {
                        case TAG_BREAKDOWN: bd.Add(JsonString(d)); break;
                        case TAG_HARVESTABLE: hv.Add(JsonString(d)); break;
                        case TAG_OPENCLOSE: oc.Add(JsonString(d)); break;
                        case TAG_BROKENDECO:
                            {
                                string guid = ExtractGuidFromSerialized(d);
                                if (string.IsNullOrEmpty(guid)) break;
                                string state = d.Contains("\"Repaired\"") ? "Repaired" : "Broken";
                                br.Add($"{{\"m_State\":\"{state}\",\"m_ItemId\":\"{EscapeJson(guid)}\"}}");
                                break;
                            }
                    }
                }
            }

            breakDowns = "{\"strings\":[" + string.Join(",", bd) + "]}";
            harvestables = hv.Count > 0 ? "{\"strings\":[" + string.Join(",", hv) + "]}" : null;
            openClosers = oc.Count > 0 ? "{\"strings\":[" + string.Join(",", oc) + "]}" : null;
            brokenDecor = "{\"m_DataList\":[" + string.Join(",", br) + "]}";
            return bd.Count + hv.Count + oc.Count + br.Count;
        }

        // What the region's own entry in the save says: its RandomSpawnObject choices (town layouts) and its
        // doors' ids, which differ from save to save. Both null when the region has no entry.
        private class RegionSave
        {
            public Dictionary<string, HashSet<string>> Choices;
            public List<KeyValuePair<Vector3, string>> Doors;
        }

        private static RegionSave RegionSaveOf(string saveName, string region, Dictionary<string, RegionSave> cache)
        {
            RegionSave result;
            if (cache.TryGetValue(region, out result)) return result;
            result = new RegionSave();
            cache[region] = result;
            try
            {
                string raw;
                if (!TryLoadSlotString(saveName, region, false, out raw)) return result;
                result.Choices = ReadVariantChoices(ExtractNestedJsonField(raw, "m_RandomSpawnObjectManagerSerialized"));

                // {"strings":["{\"m_Position\":[..],\"m_GUID\":\"..\",..}", ..]}
                result.Doors = new List<KeyValuePair<Vector3, string>>();
                string triggers = ExtractNestedJsonField(raw, "m_LoadSceneTriggersSerialized");
                if (!string.IsNullOrEmpty(triggers))
                    foreach (var element in SplitJsonArray(ExtractJsonArrayOf(triggers, "strings")))
                    {
                        string door = UnwrapJsonStringElement(element);
                        Vector3 pos;
                        string guid = ExtractString(door ?? "", "\"m_GUID\":\"", "\"");
                        if (!string.IsNullOrEmpty(guid) && TryReadTopVector(door, "m_Position", out pos))
                            result.Doors.Add(new KeyValuePair<Vector3, string>(pos, guid));
                    }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KALDIRMA] {region}: bolge kaydi okunamadi - {ex.Message}", $"[UNINSTALL] {region}: region record could not be read - {ex.Message}");
            }
            return result;
        }

        // The [..] value of a top-level array field, brackets included.
        private static string ExtractJsonArrayOf(string json, string field)
        {
            int start = FindTopField(json, field);
            if (start < 0 || start >= json.Length || json[start] != '[') return "[]";
            int depth = 0;
            bool inString = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (inString) { if (c == '\\') i++; else if (c == '"') inString = false; continue; }
                if (c == '"') inString = true;
                else if (c == '[') depth++;
                else if (c == ']' && --depth == 0) return json.Substring(start, i - start + 1);
            }
            return "[]";
        }

        // Fires of a clone are saved with the region (SI registers them with the game's FireManager).
        // Id -> the fire's own record, from the region's entry in the save.
        private static Dictionary<string, string> FireRecordsOf(string saveName, string region, Dictionary<string, Dictionary<string, string>> cache)
        {
            Dictionary<string, string> result;
            if (cache.TryGetValue(region, out result)) return result;
            result = new Dictionary<string, string>();
            cache[region] = result;
            try
            {
                string raw;
                if (!TryLoadSlotString(saveName, region, false, out raw)) return result;
                string blob = ExtractNestedJsonField(raw, "m_FireManagerSerialized");
                if (string.IsNullOrEmpty(blob)) return result;
                var list = Utils.DeserializeObject<FireSaveList>(blob);
                if (list == null || list.m_SerializedFires == null) return result;
                foreach (var fsd in list.m_SerializedFires)
                    if (fsd != null && !string.IsNullOrEmpty(fsd.m_Guid) && !string.IsNullOrEmpty(fsd.m_SearializedFire))
                        result[fsd.m_Guid] = fsd.m_SearializedFire;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KALDIRMA] {region}: ates kayitlari okunamadi - {ex.Message}", $"[UNINSTALL] {region}: fire records could not be read - {ex.Message}");
            }
            return result;
        }

        private static string BuildExportFires(VanillaRoomFacts facts, Dictionary<string, string> regionFires, out int count)
        {
            var items = new List<string>();
            foreach (var fire in facts.Fires)
            {
                string s;
                if (!regionFires.TryGetValue(fire.Guid, out s)) continue;
                s = SetTopVector(s, "m_Position", Vec(fire.FirePosition));
                s = SetTopVector(s, "m_Rotation", Quat(fire.FireRotation));
                items.Add("{\"m_Position\":" + Vec(fire.StovePosition) + ",\"m_InWoodStove\":true,\"m_SearializedFire\":" + JsonString(s) +
                          ",\"m_Guid\":" + JsonString(fire.Guid) + "}");
            }
            count = items.Count;
            return "{\"m_SerializedFires\":[" + string.Join(",", items) + "],\"m_DestroyedFires\":[]}";
        }

        // ─── JSON helpers ───
        private static void Str(StringBuilder sb, string field, string value)
        {
            sb.Append(",\"").Append(field).Append("\":").Append(JsonString(value));
        }

        private static string JsonString(string value)
        {
            return "\"" + EscapeJson(value ?? "") + "\"";
        }

        private static string F(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Vec(Vector3 v)
        {
            return "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
        }

        private static string Quat(Quaternion q)
        {
            return "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
        }

        // Index of `"field":` at the object's own level. Nested payloads are escaped
        // strings, so their keys read \"field\" and never match.
        private static int FindTopField(string json, string field)
        {
            string marker = "\"" + field + "\":";
            int i = json.IndexOf(marker, System.StringComparison.Ordinal);
            while (i > 0 && json[i - 1] == '\\')
                i = json.IndexOf(marker, i + marker.Length, System.StringComparison.Ordinal);
            return i < 0 ? -1 : i + marker.Length;
        }

        // Replaces the [..] value of a top-level field; a missing field is left alone.
        private static string SetTopVector(string json, string field, string array)
        {
            if (string.IsNullOrEmpty(json)) return json;
            int start = FindTopField(json, field);
            if (start < 0 || start >= json.Length || json[start] != '[') return json;
            int end = json.IndexOf(']', start);
            if (end < 0) return json;
            return json.Substring(0, start) + array + json.Substring(end + 1);
        }

        private static float[] ReadTopNumbers(string json, string field)
        {
            int start = FindTopField(json, field);
            if (start < 0 || start >= json.Length || json[start] != '[') return null;
            int end = json.IndexOf(']', start);
            if (end < 0) return null;
            var parts = json.Substring(start + 1, end - start - 1).Split(',');
            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i])) return null;
            return result;
        }

        private static bool TryReadTopVector(string json, string field, out Vector3 v)
        {
            var n = ReadTopNumbers(json, field);
            v = n != null && n.Length == 3 ? new Vector3(n[0], n[1], n[2]) : Vector3.zero;
            return n != null && n.Length == 3;
        }

        private static bool TryReadTopQuat(string json, string field, out Quaternion q)
        {
            var n = ReadTopNumbers(json, field);
            q = n != null && n.Length == 4 ? new Quaternion(n[0], n[1], n[2], n[3]) : Quaternion.identity;
            return n != null && n.Length == 4;
        }

        // Sets a top-level string field, adding it when missing.
        private static string SetTopString(string json, string field, string value)
        {
            int start = FindTopField(json, field);
            if (start >= 0 && start < json.Length && json[start] == '"')
            {
                int end = FindClosingQuote(json, start + 1);
                if (end > 0) return json.Substring(0, start) + JsonString(value) + json.Substring(end + 1);
            }
            int brace = json.IndexOf('{');
            if (brace < 0) return json;
            string sep = json.Substring(brace + 1).TrimStart().StartsWith("}") ? "" : ",";
            return json.Substring(0, brace + 1) + "\"" + field + "\":" + JsonString(value) + sep + json.Substring(brace + 1);
        }
    }
}
