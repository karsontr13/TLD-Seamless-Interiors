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
    // The reverse of LegacyImport.cs. Each building of the loaded region is written into the
    // game's save under the key the base game reads for that interior scene, as a
    // SceneSaveGameFormat, so loading the save without the mod finds the player's things in
    // the real interiors.
    //
    // Only on the player's request (settings page). The mod itself never reads these keys
    // again for a building it owns (its _legacy.json marker says so), so a save that goes on
    // being played with the mod is not changed by them; it only holds an older copy.
    //
    // Clone-local space IS interior-scene space (see LegacyImport.cs), so positions are only
    // copied, never converted, except where the mod's own records hold world positions.
    // ─────────────────────────────────────────────────────────────────────
    public partial class SeamlessInteriorsMod
    {
        private static bool s_VanillaExportArmed;
        private static bool s_VanillaExportRunning;

        // Settings page: hydrate every building of this region, then save once with the export on.
        public static void RequestVanillaExport()
        {
            if (s_VanillaExportRunning) return;
            if (string.IsNullOrEmpty(SaveGameSystem.m_CurrentSaveName) || ActiveInteriors.Count == 0)
            {
                ShowExportMessage("Seamless Interiors: there is no building of this mod in this region.");
                return;
            }
            MelonCoroutines.Start(VanillaExportRoutine());
        }

        private static IEnumerator VanillaExportRoutine()
        {
            s_VanillaExportRunning = true;
            yield return null;

            int filled = 0;
            foreach (var instance in new List<SeamlessInteriorInstance>(ActiveInteriors.Values))
            {
                if (!IsExportable(instance) || instance.ContentHydrated) continue;
                try { EnsureHydratedNow(instance, "vanilla-export"); filled++; }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[KALDIRMA] {instance.Config.ResolvedInstanceId}: doldurulamadi - {ex.Message}",
                                        $"[UNINSTALL] {instance.Config.ResolvedInstanceId}: could not be filled - {ex.Message}");
                }
                yield return null;
            }

            s_VanillaExportArmed = true;
            s_VanillaExportRunning = false;
            MelonLogger.Msg($"[KALDIRMA] {filled} bina dolduruldu, kayit baslatiliyor.",
                            $"[UNINSTALL] {filled} buildings filled, saving the game.");
            ShowExportMessage("Seamless Interiors: saving this region for the base game...");

            try { GameManager.TriggerSurvivalSaveAndDisplayHUDMessage(); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KALDIRMA] Kayit baslatilamadi: {ex.Message}",
                                    $"[UNINSTALL] Could not start the save: {ex.Message}");
            }
        }

        // A building the mod owns: it has its own gear file, so ProbeLegacySave never imports it again.
        private static bool IsExportable(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null || !instance.RunCompleted) return false;
            string gearPath = GetInactiveSceneGearSavePath(instance);
            return gearPath != null && SaveFiles.Exists(gearPath);
        }

        // SaveSceneData postfix, after the mod's own files were written for this save.
        public static void WriteVanillaExportIfArmed(SlotData slot)
        {
            if (!s_VanillaExportArmed || slot == null) return;
            string saveName = SaveGameSystem.m_CurrentSaveName;
            if (string.IsNullOrEmpty(saveName) || !SlotBelongsToSave(slot, saveName)) return;
            s_VanillaExportArmed = false;

            int written = 0, skipped = 0;
            var usedKeys = new HashSet<string>();
            var exported = new List<KeyValuePair<SeamlessInteriorInstance, string>>();
            foreach (var instance in ActiveInteriors.Values)
            {
                if (!IsExportable(instance)) continue;
                string id = instance.Config.ResolvedInstanceId;
                try
                {
                    if (!instance.ContentHydrated && IsLazyContentEnabled)
                    {
                        MelonLogger.Warning($"[KALDIRMA] {id}: bina dolu degil, atlandi.", $"[UNINSTALL] {id}: building not filled, skipped.");
                        skipped++;
                        continue;
                    }

                    string key = ResolveVanillaSceneKey(instance);
                    if (key == null || !usedKeys.Add(key))
                    {
                        MelonLogger.Warning($"[KALDIRMA] {id}: oyunun kayit anahtari bulunamadi, atlandi.",
                                            $"[UNINSTALL] {id}: the game's save key could not be found, skipped.");
                        skipped++;
                        continue;
                    }

                    string summary;
                    string json = BuildVanillaSceneJson(instance, out summary);
                    var fmt = Utils.DeserializeObject<SceneSaveGameFormat>(json);
                    if (fmt == null || string.IsNullOrEmpty(fmt.m_GearManagerSerialized))
                    {
                        MelonLogger.Warning($"[KALDIRMA] {id}: sahne kaydi kurulamadi, atlandi.", $"[UNINSTALL] {id}: scene record could not be built, skipped.");
                        skipped++;
                        continue;
                    }

                    BackUpVanillaKey(instance, saveName, key);
                    if (!SaveGameSlots.SaveDataToSlot(slot, key, fmt))
                    {
                        MelonLogger.Warning($"[KALDIRMA] {id}: oyun '{key}' kaydini kabul etmedi.", $"[UNINSTALL] {id}: the game refused the record '{key}'.");
                        skipped++;
                        continue;
                    }

                    try { File.WriteAllText(GetInstanceSavePath(instance, "_vanilla_export.json"), json); } catch { }
                    MelonLogger.Msg($"[KALDIRMA] {id} -> '{key}': {summary}", $"[UNINSTALL] {id} -> '{key}': {summary}");
                    exported.Add(new KeyValuePair<SeamlessInteriorInstance, string>(instance, key));
                    written++;
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[KALDIRMA] {id}: hata - {ex.Message}", $"[UNINSTALL] {id}: error - {ex.Message}");
                    skipped++;
                }
            }

            // Objects moved with Safehouse Customization+ go to the interior's own SC+ records.
            ModExceptions.ExportSafehouseCustomizationPlus(exported, saveName);

            string msg = skipped == 0
                ? $"Seamless Interiors: {written} buildings of this region are ready for the base game. Repeat in every region you use, then quit."
                : $"Seamless Interiors: {written} buildings ready, {skipped} could not be written (see the log).";
            ShowExportMessage(msg);
        }

        private static void ShowExportMessage(string text)
        {
            try { HUDMessage.AddMessage(text, 8f, true, true); } catch { }
        }

        // Before a key is replaced, its current value is kept once in the mod's data folder.
        private static void BackUpVanillaKey(SeamlessInteriorInstance instance, string saveName, string key)
        {
            string path = GetInstanceSavePath(instance, "_vanilla_before_export.json");
            if (path == null || File.Exists(path)) return;
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

            LoadScene best = null, anyInClone = null;
            float bestDist = 60f;
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
                if (dist < bestDist) { bestDist = dist; best = d; }
            }
            if (best != null) return best;
            return shared ? null : anyInClone;
        }

        // ─── The scene record ───
        private static string BuildVanillaSceneJson(SeamlessInteriorInstance instance, out string summary)
        {
            Transform interiorT = instance.MasterInterior.transform;
            int nGear, nCont, nPlace, nRemoved, nState, nFire;

            string gear = BuildExportGear(instance, out nGear);
            string containers = BuildExportContainers(instance, interiorT, out nCont);
            string curing, photos;
            string placements = BuildExportPlacements(instance, interiorT, out nPlace, out nRemoved, out curing, out photos);
            string breakDowns, harvestables, openClosers, brokenDecor;
            nState = BuildExportStates(instance, interiorT, out breakDowns, out harvestables, out openClosers, out brokenDecor);
            string fires = BuildExportFires(instance, interiorT, out nFire);

            float hours = 0f;
            try { hours = GameManager.GetTimeOfDayComponent().GetHoursPlayedNotPaused(); } catch { }
            int version = 4;
            try { if (SaveGameSystem.m_SceneSaveGameFormatVersion > 0) version = SaveGameSystem.m_SceneSaveGameFormatVersion; } catch { }

            var sb = new StringBuilder(4096);
            sb.Append("{\"m_Version\":").Append(version);
            Str(sb, "m_ContainerManagerSerialized", containers);
            Str(sb, "m_GearManagerSerialized", gear);
            Str(sb, "m_GearSpawnInOldSavesGUIDs", BuildSpawnedInOldSavesList(instance));
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
            sb.Append(",\"m_JunkManagedSerialized\":{\"m_ClearedJunk\":").Append(instance.JunkCleared ? "true" : "false").Append('}');
            sb.Append('}');

            summary = $"{nGear} gear ({instance.VanillaSceneGuids.Count} scene ids), {nCont} containers, {nPlace} placed/moved, {nRemoved} removed, {nState} states, {nFire} fires";
            return sb.ToString();
        }

        // Gear: the mod's own file (it also holds strays and orphans), the payload's
        // world position replaced by the clone-local one the file records.
        private static string BuildExportGear(SeamlessInteriorInstance instance, out int count)
        {
            var items = new List<string>();
            string path = GetInactiveSceneGearSavePath(instance);
            if (path != null && SaveFiles.Exists(path))
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

        // Containers: the mod's file for the contents, the live clone for guid and rotation.
        private static string BuildExportContainers(SeamlessInteriorInstance instance, Transform interiorT, out int count)
        {
            var live = new List<Container>(instance.MasterInterior.GetComponentsInChildren<Container>(true));
            foreach (var moved in instance.MovedContainers)
                if (moved != null && !live.Contains(moved)) live.Add(moved);
            var claimed = new bool[live.Count];

            var items = new List<string>();
            string path = GetContainerSavePath(instance);
            if (path != null && SaveFiles.Exists(path))
            {
                foreach (var rec in ParseContainerFile(SaveFiles.Read(path)))
                {
                    if (!rec.hasPosition || string.IsNullOrEmpty(rec.data)) continue;

                    int best = -1;
                    float bestSqr = CONTAINER_RESTORE_RADIUS * CONTAINER_RESTORE_RADIUS;
                    for (int i = 0; i < live.Count; i++)
                    {
                        if (claimed[i] || live[i] == null) continue;
                        float sqr = (interiorT.InverseTransformPoint(live[i].transform.position) - rec.position).sqrMagnitude;
                        if (sqr <= bestSqr) { bestSqr = sqr; best = i; }
                    }

                    string data = SetTopVector(rec.data, "m_Position", Vec(rec.position));
                    string guid = null, name = rec.name;
                    bool notActive = false;
                    if (best >= 0)
                    {
                        claimed[best] = true;
                        var c = live[best];
                        data = SetTopVector(data, "m_Rotation", Quat(Quaternion.Inverse(interiorT.rotation) * c.transform.rotation));
                        guid = SceneGuidOf(c.GetComponent<ObjectGuid>());
                        if (string.IsNullOrEmpty(name)) name = CleanGearName(c.gameObject.name);
                        notActive = !c.gameObject.activeSelf;
                    }

                    var sb = new StringBuilder();
                    sb.Append("{\"m_Position\":").Append(Vec(rec.position));
                    sb.Append(",\"m_PrefabName\":\"").Append(EscapeJson(name ?? "")).Append('"');
                    sb.Append(",\"m_SearializedContainer\":\"").Append(EscapeJson(data)).Append('"');
                    if (!string.IsNullOrEmpty(guid)) sb.Append(",\"m_Guid\":\"").Append(EscapeJson(guid)).Append('"');
                    if (notActive) sb.Append(",\"m_NotActive\":true");
                    sb.Append('}');
                    items.Add(sb.ToString());
                }
            }
            count = items.Count;
            return "{\"m_SerializedContainers\":[" + string.Join(",", items) + "]}";
        }

        // Placeables: player-placed ones from the spawn file, the scene's own furniture
        // from the position file; scene furniture that is gone becomes a "Removed" record.
        private static string BuildExportPlacements(SeamlessInteriorInstance instance, Transform interiorT,
                                                    out int placed, out int removed, out string curing, out string photos)
        {
            placed = 0; removed = 0; curing = null; photos = null;
            var items = new List<string>();
            var done = new HashSet<string>();
            var curingStates = new List<string>();
            var photoStates = new List<string>();

            var liveByGuid = BestPlaceablePerGuid(InteriorScan.Placeables(instance.MasterInterior));

            // 1. Player-placed furniture: the record is already a clone-local PlaceableSaveData.
            string spawnPath = GetSpawnedPlaceableSavePath(instance);
            if (spawnPath != null && SaveFiles.Exists(spawnPath))
            {
                foreach (var element in SplitJsonArray(SaveFiles.Read(spawnPath)))
                {
                    string guid = ExtractString(element, "\"g\":\"", "\"");
                    string data = ExtractEscapedField(element, "d");
                    if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(data) || !done.Add(guid)) continue;
                    items.Add($"{{\"m_Guid\":\"{EscapeJson(guid)}\",\"m_Serialized\":{data}}}");
                    placed++;

                    // Curing box / photo state, keyed by the guid the game recreates the object with.
                    string cs = ReadEscapedField(element, "\"cs\":\"");
                    if (!string.IsNullOrEmpty(cs)) curingStates.Add(SetTopString(cs, "m_GUID", guid));
                    string fp = ReadEscapedField(element, "\"fp\":\"");
                    if (!string.IsNullOrEmpty(fp)) photoStates.Add(SetTopString(fp, "m_GUID", guid));
                }
            }

            // 2. The scene's furniture with the transform the mod keeps for it.
            var records = new Dictionary<string, PlaceableEntry>();
            string placePath = GetPlaceableSavePath(instance);
            if (placePath != null && SaveFiles.Exists(placePath))
            {
                foreach (var element in SplitJsonArray(SaveFiles.Read(placePath)))
                {
                    var e = ParseEntry(element);
                    if (e == null || string.IsNullOrEmpty(e.guid)) continue;
                    if (e.guid == StableGuidMarker) continue;
                    records[e.guid] = e;
                }
            }

            Dictionary<string, Il2CppTLD.Placement.Placeable> sceneByGuid = null;
            foreach (var kv in records)
            {
                if (done.Contains(kv.Key)) continue;
                Il2CppTLD.Placement.Placeable p;
                if (!liveByGuid.TryGetValue(kv.Key, out p) || p == null)
                {
                    // Furniture moved with the game's placement leaves the clone for the placement root.
                    if (sceneByGuid == null) sceneByGuid = BestPlaceablePerGuid(SceneScan.PlaceablesAll());
                    if (!sceneByGuid.TryGetValue(kv.Key, out p) || p == null) continue;
                }
                if (HasStableGuid(p)) continue;
                if (IsPlayerOrInventory(p.transform)) continue;
                done.Add(kv.Key);

                if (!kv.Value.active) { items.Add(RemovedRecord(kv.Key)); removed++; continue; }

                var data = p.Serialize();
                if (data == null) continue;
                data.m_Position = kv.Value.position;
                data.m_Rotation = kv.Value.rotation;
                if (kv.Value.scale != Vector3.zero) data.m_Scale = kv.Value.scale;
                data.m_ActiveSelf = true;
                string s = Utils.SerializeObject(data);
                if (string.IsNullOrEmpty(s)) continue;
                items.Add($"{{\"m_Guid\":\"{EscapeJson(kv.Key)}\",\"m_Serialized\":{s}}}");
                placed++;
            }

            // 3. Scene furniture the mod has no record for was taken by the player.
            if (records.Count > 0)
            {
                foreach (var kv in liveByGuid)
                {
                    if (done.Contains(kv.Key) || kv.Value == null) continue;
                    if (IsSpawnedPlaceable(instance, kv.Value) || HasStableGuid(kv.Value)) continue;
                    if (IsPlayerOrInventory(kv.Value.transform)) continue;
                    done.Add(kv.Key);
                    items.Add(RemovedRecord(kv.Key));
                    removed++;
                }
            }

            if (curingStates.Count > 0) curing = "[" + string.Join(",", curingStates) + "]";
            if (photoStates.Count > 0) photos = "[" + string.Join(",", photoStates) + "]";
            return "[" + string.Join(",", items) + "]";
        }

        private static string RemovedRecord(string guid)
        {
            return $"{{\"m_Guid\":\"{EscapeJson(guid)}\",\"m_State\":\"Removed\"}}";
        }

        // Broken-down, harvested, opened objects and repaired cabinet doors, from the mod's file.
        // Smashables and locks have no list of their own in a scene save.
        private static int BuildExportStates(SeamlessInteriorInstance instance, Transform interiorT,
                                             out string breakDowns, out string harvestables, out string openClosers, out string brokenDecor)
        {
            var bd = new List<string>();
            var hv = new List<string>();
            var oc = new List<string>();
            var br = new List<string>();

            string path = GetInteractiveStateSavePath(instance);
            if (path != null && SaveFiles.Exists(path))
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
                        d = SetTopVector(d, "m_Position", Vec(interiorT.InverseTransformPoint(world)));

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

        // Stove and fireplace fires, from the live clone.
        private static string BuildExportFires(SeamlessInteriorInstance instance, Transform interiorT, out int count)
        {
            var items = new List<string>();
            foreach (var f in instance.MasterInterior.GetComponentsInChildren<Fire>(true))
            {
                if (f == null) continue;
                var stove = f.GetComponentInParent<WoodStove>();
                if (stove == null) continue;

                string s = null;
                try { s = f.Serialize(); } catch { }
                if (string.IsNullOrEmpty(s)) continue;

                s = SetTopVector(s, "m_Position", Vec(interiorT.InverseTransformPoint(f.transform.position)));
                s = SetTopVector(s, "m_Rotation", Quat(Quaternion.Inverse(interiorT.rotation) * f.transform.rotation));
                string guid = GetFireKey(f);

                var sb = new StringBuilder();
                sb.Append("{\"m_Position\":").Append(Vec(interiorT.InverseTransformPoint(stove.transform.position)));
                sb.Append(",\"m_InWoodStove\":true");
                sb.Append(",\"m_SearializedFire\":\"").Append(EscapeJson(s)).Append('"');
                if (!string.IsNullOrEmpty(guid)) sb.Append(",\"m_Guid\":\"").Append(EscapeJson(guid)).Append('"');
                sb.Append('}');
                items.Add(sb.ToString());
            }
            count = items.Count;
            return "{\"m_SerializedFires\":[" + string.Join(",", items) + "],\"m_DestroyedFires\":[]}";
        }

        // The scene's own id of an object: on a clone it often lives in PDID, and containers carry "_CLONE".
        private static string SceneGuidOf(ObjectGuid og)
        {
            if (og == null) return null;
            string g = null;
            try { g = !string.IsNullOrEmpty(og.m_Guid) ? og.m_Guid : og.PDID; } catch { }
            if (!string.IsNullOrEmpty(g) && g.EndsWith("_CLONE")) g = g.Substring(0, g.Length - "_CLONE".Length);
            return string.IsNullOrEmpty(g) ? null : g;
        }

        // ─── Scene gear the base game must treat as already spawned ───
        // Gear and loot pickers marked "spawn in old saves" are only deleted / left alone on load when
        // their id is in m_GearSpawnInOldSavesGUIDs; without it the scene's own copy stays next to the saved one.
        // Taken in Run, before the loot roll and hydration replace the template's gear.
        public static void CaptureVanillaSceneGuids(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;
            instance.VanillaSceneGuids.Clear();
            try
            {
                foreach (var g in instance.MasterInterior.GetComponentsInChildren<GearItem>(true))
                    if (g != null) AddSceneGuid(instance, g.GetComponent<ObjectGuid>());
                foreach (var r in instance.MasterInterior.GetComponentsInChildren<RandomSpawnObject>(true))
                    if (r != null) AddSceneGuid(instance, r.GetComponent<ObjectGuid>());
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KALDIRMA] {instance.Config.ResolvedInstanceId}: sahne kimlikleri okunamadi - {ex.Message}",
                                    $"[UNINSTALL] {instance.Config.ResolvedInstanceId}: scene ids could not be read - {ex.Message}");
            }
        }

        private static void AddSceneGuid(SeamlessInteriorInstance instance, ObjectGuid og)
        {
            string g = SceneGuidOf(og);
            if (g != null) instance.VanillaSceneGuids.Add(g);
        }

        private static string BuildSpawnedInOldSavesList(SeamlessInteriorInstance instance)
        {
            var items = new List<string>(instance.VanillaSceneGuids.Count);
            foreach (var g in instance.VanillaSceneGuids) items.Add(JsonString(g));
            return "[" + string.Join(",", items) + "]";
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

        private static bool TryReadTopVector(string json, string field, out Vector3 v)
        {
            v = Vector3.zero;
            int start = FindTopField(json, field);
            if (start < 0 || start >= json.Length || json[start] != '[') return false;
            int end = json.IndexOf(']', start);
            if (end < 0) return false;
            var parts = json.Substring(start + 1, end - start - 1).Split(',');
            if (parts.Length != 3) return false;
            float x, y, z;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
            v = new Vector3(x, y, z);
            return true;
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
