using Il2Cpp;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SeamlessInteriors
{
    // ─── MOD EXCEPTIONS ───
    // The places where no vanilla answer can fit, patched by name only while that mod is loaded.
    // Everything else other mods see comes from the vanilla view (SeamlessInteriorsMod.VanillaView.cs).
    internal static class ModExceptions
    {
        private const string TAG = "[ISTISNA]";

        // Major Miseries shelters the hangar's lower level from the aurora by the player's height in
        // the AFHangar scene; inside the clone that scene's coordinates are the clone's local ones.
        private const string HANGAR_SCENE = "AFHangar";
        private const float HANGAR_SHELTER_MAX_Y = 6f;

        private static bool s_ReportedHangar;
        private static bool s_ReportedWeather;
        private static readonly HashSet<string> s_ReportedErrors = new HashSet<string>();
        private static Assembly s_WeatherOverhaul;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            InstallHangarShelter(harmony);
            InstallWeatherOverhaulVisuals(harmony);
            InstallSafehouseCustomizationPlus();
        }

        // Safehouse Customization+ makes stoves and other big objects movable when an interior scene initializes.
        // SI hides template scenes from mods and empties them first, so SI hands SC+ that event itself before the copy.
        private static MelonMod s_SCPlusMelon;
        private static Assembly s_SCPlusAssembly;

        private static void InstallSafehouseCustomizationPlus()
        {
            foreach (MelonBase melon in MelonBase.RegisteredMelons)
                if (melon != null && melon.Info != null && melon.Info.Name == "SafehouseCustomizationPlus")
                    s_SCPlusMelon = melon as MelonMod;
            if (s_SCPlusMelon == null) return;

            s_SCPlusAssembly = s_SCPlusMelon.GetType().Assembly;
            MelonLogger.Msg($"{TAG} Safehouse Customization+ {s_SCPlusMelon.Info.Version}: bina sobalari ve buyuk esyalar tasinabilir yapilacak.");
        }

        // SC+'s own scene pass, through its public MelonMod callback, so it keeps working when SC+ renames its internals.
        internal static void ApplySafehouseCustomizationPlus(UnityEngine.SceneManagement.Scene scene, GameObject[] roots)
        {
            if (s_SCPlusMelon == null || roots == null || roots.Length == 0) return;

            string name = SeamlessInteriorsMod.RealSceneName(scene) ?? "";
            int before = CountDecorations(roots);
            try
            {
                SeamlessInteriorsMod.WithRealSceneView(() => s_SCPlusMelon.OnSceneWasInitialized(scene.buildIndex, name));
            }
            catch (Exception ex)
            {
                ReportOnce("Safehouse Customization+", ex);
            }

            int made = CountDecorations(roots) - before;
            if (made > 0)
                MelonLogger.Msg($"{TAG} Safehouse Customization+: '{name}' icinde {made} esya tasinabilir yapildi.");
        }

        private static int CountDecorations(GameObject[] roots)
        {
            int n = 0;
            foreach (GameObject root in roots)
                if (root != null) n += root.GetComponentsInChildren<DecorationItem>(true).Length;
            return n;
        }

        // SC+ switches off the original of a moved object when it loads the save, before SI has built the room,
        // so the original came back next to the moved copy. SI does that step itself once the room is built.
        private static object s_SCPlusData;
        private static MethodInfo s_SCPlusLoad;
        private static MethodInfo s_SCPlusSanitize;
        private static IDictionary s_SCPlusDefinitions;
        private static string s_SCPlusTag;

        internal static void ApplySafehouseCustomizationPlusMoves(SeamlessInteriorInstance instance)
        {
            if (s_SCPlusAssembly == null || instance == null || instance.MasterInterior == null) return;
            try
            {
                if (!ResolveSCPlusData()) return;

                string scene = instance.Config.InteriorSceneBaseName;
                var records = new List<string>();
                foreach (string key in new[] { instance.Config.ExteriorSceneName, "carried" })
                    records.AddRange(SplitSCPlusRecords(s_SCPlusLoad.Invoke(s_SCPlusData, new object[] { s_SCPlusTag + "_" + key }) as string));

                int hidden = 0, moved = 0;
                foreach (string record in records)
                {
                    if (JsonString(record, "nativeScene") != scene) continue;
                    string name = JsonString(record, "name");
                    Vector3 original = JsonVector(record, "originalPos");
                    if (string.IsNullOrEmpty(name) || original == Vector3.zero) continue;

                    GameObject go = FindSCPlusOriginal(instance.MasterInterior, name, original);
                    if (go == null || !go.activeSelf) continue;

                    if (IsSCPlusPickupable(name)) { go.SetActive(false); hidden++; }
                    else
                    {
                        Vector3 rot = JsonVector(record, "currentRot");
                        go.transform.SetPositionAndRotation(JsonVector(record, "currentPos"),
                            new Quaternion(rot.x, rot.y, rot.z, JsonFloat(JsonObject(record, "currentRot"), "w", 1f)));
                        moved++;
                    }
                }

                if (hidden + moved > 0)
                    MelonLogger.Msg($"{TAG} Safehouse Customization+: {instance.Config.ResolvedInstanceId} icinde tasinmis {hidden} esyanin asli kapatildi, {moved} esya yerine konuldu.");
            }
            catch (Exception ex)
            {
                ReportOnce("Safehouse Customization+ tasima", ex);
            }
        }

        // "Prepare for uninstall": a room's SC+ records move from the region's list to the interior's own key, in
        // interior coordinates, as SC+ keeps them for the real interior. Runs after SC+ wrote this save's lists.
        private static MethodInfo s_SCPlusSave;

        internal static void ExportSafehouseCustomizationPlus(List<VanillaRoomFacts> rooms, string saveName)
        {
            if (s_SCPlusAssembly == null || rooms == null || rooms.Count == 0) return;
            try
            {
                if (!ResolveSCPlusData()) return;
                if (s_SCPlusSave == null) s_SCPlusSave = s_SCPlusData.GetType().GetMethod("Save", new[] { typeof(string), typeof(string) });
                if (s_SCPlusSave == null) throw new MissingMemberException("Safehouse Customization+ kayit yazma uyesi bulunamadi.");

                var regions = new HashSet<string>();
                foreach (var room in rooms) regions.Add(room.Region);

                foreach (string region in regions)
                {
                    string regionKey = s_SCPlusTag + "_" + region;
                    var kept = new List<string>();
                    var perKey = new Dictionary<string, List<string>>();

                    foreach (string raw in SplitSCPlusRecords(s_SCPlusLoad.Invoke(s_SCPlusData, new object[] { regionKey }) as string))
                    {
                        string record = TrimSCPlusRecord(raw);
                        VanillaRoomFacts owner = JsonString(record, "currentScene") == region ? FindSCPlusRoom(rooms, region, record) : null;
                        if (owner != null)
                        {
                            List<string> list;
                            if (!perKey.TryGetValue(owner.ExportKey, out list)) perKey[owner.ExportKey] = list = new List<string>();
                            list.Add(ToInteriorRecord(record, owner));
                        }
                        else kept.Add(record);
                    }
                    if (perKey.Count == 0) continue;

                    foreach (var kv in perKey)
                    {
                        string key = s_SCPlusTag + "_" + kv.Key;
                        BackUpSCPlusKey(saveName, key);
                        s_SCPlusSave.Invoke(s_SCPlusData, new object[] { "[" + string.Join(",", kv.Value) + "]", key });
                        MelonLogger.Msg($"[KALDIRMA] Safehouse Customization+: {kv.Value.Count} tasinmis obje '{kv.Key}' sahnesine aktarildi.",
                                        $"[UNINSTALL] Safehouse Customization+: {kv.Value.Count} moved objects written for '{kv.Key}'.");
                    }

                    // Left in the region's list, SC+ would make a copy of each one in the region.
                    BackUpSCPlusKey(saveName, regionKey);
                    s_SCPlusSave.Invoke(s_SCPlusData, new object[] { "[" + string.Join(",", kept) + "]", regionKey });
                }
            }
            catch (Exception ex)
            {
                ReportOnce("Safehouse Customization+ aktarimi", ex);
            }
        }

        // SplitSCPlusRecords cuts between record starts, so a piece can end with "," or the list's "]".
        private static string TrimSCPlusRecord(string raw)
        {
            string r = raw.TrimEnd();
            if (r.EndsWith("]")) r = r.Substring(0, r.Length - 1).TrimEnd();
            if (r.EndsWith(",")) r = r.Substring(0, r.Length - 1).TrimEnd();
            return r;
        }

        // The room a record's scene object belongs to: its original spot lies inside that room. Rooms of one
        // scene in one region are told apart only by that.
        private const float SCPLUS_ROOM_MARGIN = 2f;

        private static VanillaRoomFacts FindSCPlusRoom(List<VanillaRoomFacts> rooms, string region, string record)
        {
            string native = JsonString(record, "nativeScene");
            if (string.IsNullOrEmpty(native) || string.IsNullOrEmpty(JsonString(record, "name"))) return null;
            Vector3 original = JsonVector(record, "originalPos");

            VanillaRoomFacts only = null, inside = null;
            int sameScene = 0;
            foreach (var room in rooms)
            {
                if (room.Scene != native || room.Region != region) continue;
                sameScene++;
                only = room;
                if (room.ContainsLocal(room.ToLocal(original), SCPLUS_ROOM_MARGIN)) inside = room;
            }
            return inside ?? (sameScene == 1 ? only : null);
        }

        private static string ToInteriorRecord(string record, VanillaRoomFacts room)
        {
            Vector3 rot = JsonVector(record, "currentRot");
            Quaternion world = new Quaternion(rot.x, rot.y, rot.z, JsonFloat(JsonObject(record, "currentRot"), "w", 1f));
            Quaternion local = room.ToLocal(world);

            // SC+ matches the native scene with Contains(save key), so an instanced interior needs its full key.
            record = SetJsonString(record, "nativeScene", room.ExportKey);
            record = SetJsonString(record, "currentScene", room.Scene);
            record = SetJsonObject(record, "originalPos", Vector3Json(room.ToLocal(JsonVector(record, "originalPos"))));
            record = SetJsonObject(record, "currentPos", Vector3Json(room.ToLocal(JsonVector(record, "currentPos"))));
            record = SetJsonObject(record, "currentRot",
                "{\"@type\":\"UnityEngine.Quaternion\",\"x\":" + F(local.x) + ",\"y\":" + F(local.y) + ",\"z\":" + F(local.z) + ",\"w\":" + F(local.w) + "}");
            return record;
        }

        private static void BackUpSCPlusKey(string saveName, string key)
        {
            try
            {
                string path = System.IO.Path.Combine(ModPaths.DataDir(), saveName + "_" + key + "_before_export.json");
                if (System.IO.File.Exists(path)) return;
                string old = s_SCPlusLoad.Invoke(s_SCPlusData, new object[] { key }) as string;
                if (!string.IsNullOrEmpty(old)) System.IO.File.WriteAllText(path, old);
            }
            catch { }
        }

        private static string Vector3Json(Vector3 v)
        {
            return "{\"@type\":\"UnityEngine.Vector3\",\"x\":" + F(v.x) + ",\"y\":" + F(v.y) + ",\"z\":" + F(v.z) + "}";
        }

        private static string F(float v)
        {
            return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string SetJsonString(string json, string field, string value)
        {
            string key = "\"" + field + "\":\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return json;
            int start = i + key.Length;
            int end = json.IndexOf('"', start);
            return end < 0 ? json : json.Substring(0, start) + value + json.Substring(end);
        }

        // TinyJSON writes these objects flat, so the first "}" closes them.
        private static string SetJsonObject(string json, string field, string value)
        {
            string key = "\"" + field + "\":{";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return json;
            int start = i + key.Length - 1;
            int end = json.IndexOf('}', start);
            return end < 0 ? json : json.Substring(0, start) + value + json.Substring(end + 1);
        }

        // SC+ keeps a moved object under the region, so in a parked room it would hang in the air seen from outside.
        // Only its look and collision follow the room: SC+ saves an inactive object as removed, losing its contents.
        // While the room is held for the views in from outside, the object is drawn on the room's layer, like the room.
        private const int SCPLUS_HIDDEN = 0, SCPLUS_SHOWN = 1, SCPLUS_ROOM_LAYER = 2;
        private static IList s_SCPlusCarryables;
        private static float s_SCPlusNextTick;
        private static readonly Dictionary<int, int> s_SCPlusShown = new Dictionary<int, int>();
        // The layers an object's renderers had before it went onto a room layer, by object.
        private static readonly Dictionary<int, List<KeyValuePair<GameObject, int>>> s_SCPlusSavedLayers = new Dictionary<int, List<KeyValuePair<GameObject, int>>>();

        internal static void TickSafehouseCustomizationPlus()
        {
            if (s_SCPlusAssembly == null || Time.unscaledTime < s_SCPlusNextTick) return;
            s_SCPlusNextTick = Time.unscaledTime + 0.5f;
            try
            {
                if (s_SCPlusCarryables == null)
                    s_SCPlusCarryables = s_SCPlusAssembly.GetType("SCPlus.CarryableManager")?
                        .GetField("carryables", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IList;
                if (s_SCPlusCarryables == null) return;

                foreach (object item in s_SCPlusCarryables)
                {
                    var carryable = item as Component;
                    if (carryable == null || carryable.gameObject == null || !carryable.gameObject.activeInHierarchy) continue;
                    GameObject go = carryable.gameObject;
                    if (SeamlessInteriorsMod.IsUnderAnyMasterInteriorPublic(go.transform)) continue;

                    int state = SCPLUS_SHOWN, roomLayer = -1;
                    foreach (SeamlessInteriorInstance instance in SeamlessInteriorsMod.ActiveInteriors.Values)
                    {
                        if (instance == null || instance.MasterInterior == null || !instance.Config.IsParked) continue;
                        if (!instance.IsPositionInVolume(go.transform.position)) continue;
                        string id = instance.Config.ResolvedInstanceId;
                        roomLayer = SeamlessInteriorsMod.OutsideInHeldRoomLayer(id);
                        if (instance.MasterInterior.activeSelf && SeamlessInteriorsMod.PlayerInteriorId == id) state = SCPLUS_SHOWN;
                        else state = roomLayer >= 0 ? SCPLUS_ROOM_LAYER : SCPLUS_HIDDEN;
                        break;
                    }

                    int shown;
                    int key = go.GetInstanceID();
                    if (!s_SCPlusShown.TryGetValue(key, out shown)) shown = SCPLUS_SHOWN;
                    if (shown == state && (state != SCPLUS_ROOM_LAYER || OnLayer(go, roomLayer))) continue;
                    if (shown == SCPLUS_ROOM_LAYER) RestoreSCPlusLayers(key);
                    // Collision only where the player is: a held room is seen, not walked in.
                    SeamlessInteriorsMod.SetObjectVisualState(go, state == SCPLUS_SHOWN);
                    if (state == SCPLUS_ROOM_LAYER) PutSCPlusOnRoomLayer(go, key, roomLayer);
                    s_SCPlusShown[key] = state;
                }
            }
            catch (Exception ex)
            {
                ReportOnce("Safehouse Customization+ gorunum", ex);
            }
        }

        // Renderers on, each moved to the room's layer; the main camera does not draw that layer.
        private static void PutSCPlusOnRoomLayer(GameObject go, int key, int layer)
        {
            var saved = new List<KeyValuePair<GameObject, int>>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                r.enabled = true;
                if (r.gameObject.layer == layer) continue;
                saved.Add(new KeyValuePair<GameObject, int>(r.gameObject, r.gameObject.layer));
                r.gameObject.layer = layer;
            }
            s_SCPlusSavedLayers[key] = saved;
        }

        private static void RestoreSCPlusLayers(int key)
        {
            List<KeyValuePair<GameObject, int>> saved;
            if (!s_SCPlusSavedLayers.TryGetValue(key, out saved)) return;
            foreach (var entry in saved) if (entry.Key != null) entry.Key.layer = entry.Value;
            s_SCPlusSavedLayers.Remove(key);
        }

        // Another held room can take the layer over; the object follows it.
        private static bool OnLayer(GameObject go, int layer)
        {
            var r = go.GetComponentInChildren<Renderer>(true);
            return r == null || r.gameObject.layer == layer;
        }

        private static bool ResolveSCPlusData()
        {
            if (s_SCPlusLoad != null) return true;

            Assembly assembly = s_SCPlusAssembly;
            const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            Type utility = assembly.GetType("SCPlus.Utility");
            s_SCPlusData = utility?.GetField("dataManager", statics)?.GetValue(null);
            s_SCPlusTag = utility?.GetField("movablesSaveDataTag", statics)?.GetValue(null) as string;
            s_SCPlusSanitize = utility?.GetMethod("SanitizeObjectName", statics);
            s_SCPlusDefinitions = assembly.GetType("SCPlus.CarryableData")?.GetField("carryablePrefabDefinition", statics)?.GetValue(null) as IDictionary;
            MethodInfo load = s_SCPlusData?.GetType().GetMethod("Load", new[] { typeof(string) });
            if (load == null || s_SCPlusTag == null || s_SCPlusSanitize == null || s_SCPlusDefinitions == null)
                throw new MissingMemberException("Safehouse Customization+ kayit uyeleri bulunamadi (surum degismis olabilir).");
            s_SCPlusLoad = load;
            return true;
        }

        private static GameObject FindSCPlusOriginal(GameObject master, string name, Vector3 original)
        {
            foreach (DecorationItem di in master.GetComponentsInChildren<DecorationItem>(true))
            {
                if (di == null) continue;
                GameObject go = di.gameObject;
                if ((go.transform.position - original).sqrMagnitude > 1f) continue;
                if (string.Equals(s_SCPlusSanitize.Invoke(null, new object[] { go.name }) as string, name, StringComparison.OrdinalIgnoreCase))
                    return go;
            }
            return null;
        }

        private static bool IsSCPlusPickupable(string name)
        {
            object definition = s_SCPlusDefinitions.Contains(name) ? s_SCPlusDefinitions[name] : null;
            FieldInfo field = definition?.GetType().GetField("pickupable");
            return field == null || (bool)field.GetValue(definition);
        }

        // SC+ writes its records with TinyJSON; only a few flat fields are read here.
        private static IEnumerable<string> SplitSCPlusRecords(string json)
        {
            if (string.IsNullOrEmpty(json)) yield break;
            const string start = "{\"@type\":\"SCPlus.CarryableSaveDataProxy\"";
            int at = json.IndexOf(start, StringComparison.Ordinal);
            while (at >= 0)
            {
                int next = json.IndexOf(start, at + start.Length, StringComparison.Ordinal);
                yield return next < 0 ? json.Substring(at) : json.Substring(at, next - at);
                at = next;
            }
        }

        private static string JsonString(string json, string field)
        {
            string key = "\"" + field + "\":\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length;
            int j = json.IndexOf('"', i);
            return j < 0 ? null : json.Substring(i, j - i);
        }

        private static string JsonObject(string json, string field)
        {
            string key = "\"" + field + "\":{";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "";
            int j = json.IndexOf('}', i);
            return j < 0 ? "" : json.Substring(i, j - i);
        }

        // TinyJSON skips default values, so a missing component is zero.
        private static Vector3 JsonVector(string json, string field)
        {
            string obj = JsonObject(json, field);
            return new Vector3(JsonFloat(obj, "x", 0f), JsonFloat(obj, "y", 0f), JsonFloat(obj, "z", 0f));
        }

        private static float JsonFloat(string obj, string field, float fallback)
        {
            string key = "\"" + field + "\":";
            int i = obj.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return fallback;
            i += key.Length;
            int j = i;
            while (j < obj.Length && "0123456789.-+eE".IndexOf(obj[j]) >= 0) j++;
            float value;
            return float.TryParse(obj.Substring(i, j - i), System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static void InstallHangarShelter(HarmonyLib.Harmony harmony)
        {
            Assembly assembly = FindMelonAssembly("MajorMiseries");
            if (assembly == null) return;

            try
            {
                Type aurora = assembly.GetType("MajorMiseries.Managers.AuroraInfluenceManager");
                MethodInfo zone = aurora?.GetMethod("IsPlayerInAFHangarShelterZone", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (zone == null) throw new MissingMethodException("AuroraInfluenceManager.IsPlayerInAFHangarShelterZone bulunamadi.");

                harmony.Patch(zone, prefix: OwnMethod(nameof(HangarShelterPrefix)));
                MelonLogger.Msg($"{TAG} Major Miseries: hangarin alt kati bina koordinatiyla olculuyor.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{TAG} Major Miseries hangar istisnasi kurulamadi: {(ex.InnerException ?? ex).Message}");
            }
        }

        private static void InstallWeatherOverhaulVisuals(HarmonyLib.Harmony harmony)
        {
            Assembly assembly = FindMelonAssembly("WeatherOverhaul");
            if (assembly == null) return;

            try
            {
                MethodInfo capture = StaticMethod(assembly, "WeatherOverhaul.Weather.WeatherSnapshot", "Capture");
                MethodInfo gameplayScene = StaticMethod(assembly, "WeatherOverhaul.Weather.CustomWeatherStageRuntime", "IsGameplayWeatherSceneActive");
                MethodInfo auroraAuthority = StaticMethod(assembly, "WeatherOverhaul.Weather.VanillaAuroraAuthority", "HasLoadedRegionAuthority");
                if (capture == null || gameplayScene == null || auroraAuthority == null)
                    throw new MissingMemberException("Weather Overhaul'un beklenen uyeleri bulunamadi (surum degismis olabilir).");

                SeamlessInteriorsMod.EnsureIndoorEnvironmentHook();
                harmony.Patch(capture, prefix: OwnMethod(nameof(SkyViewEnter)), finalizer: OwnMethod(nameof(SkyViewExit)));
                harmony.Patch(gameplayScene, prefix: OwnMethod(nameof(RegionViewEnter)), finalizer: OwnMethod(nameof(RegionViewExit)));
                harmony.Patch(auroraAuthority, prefix: OwnMethod(nameof(RegionViewEnter)), finalizer: OwnMethod(nameof(RegionViewExit)));
                s_WeatherOverhaul = assembly;
                MelonLogger.Msg($"{TAG} Weather Overhaul: bina icinde de bolgenin acik havasini goruyor; islanma ve sicaklik icerideki gibi.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{TAG} Weather Overhaul istisnasi kurulamadi: {(ex.InnerException ?? ex).Message}");
            }
        }

        // ─── Patches ───

        private static bool HangarShelterPrefix(ref bool __result)
        {
            try
            {
                SeamlessInteriorInstance instance;
                string id = SeamlessInteriorsMod.PlayerInteriorId;
                if (id == null || !SeamlessInteriorsMod.ActiveInteriors.TryGetValue(id, out instance) || instance == null) return true;
                if (instance.MasterInterior == null || instance.Config.InteriorSceneBaseName != HANGAR_SCENE) return true;

                Transform player = GameManager.GetPlayerTransform();
                if (player == null) return true;

                __result = instance.MasterInterior.transform.InverseTransformPoint(player.position).y <= HANGAR_SHELTER_MAX_Y;
                if (!s_ReportedHangar)
                {
                    s_ReportedHangar = true;
                    MelonLogger.Msg($"{TAG} Hangar siginagi bina koordinatiyla olculdu: {(__result ? "alt kat, korunuyor" : "ust kat")} (bir kez yazilir).");
                }
                return false;
            }
            catch (Exception ex)
            {
                ReportOnce("hangar", ex);
                return true;
            }
        }

        // An indoor snapshot or a new scene name makes Weather Overhaul drop its sky; SI's windows show it, so the
        // snapshot sees the region's open air. Wetness and temperature still read the real indoor state.
        private static void SkyViewEnter()
        {
            SeamlessInteriorsMod.s_WeatherRegionView++;
            SeamlessInteriorsMod.s_WeatherOutdoorView++;
        }

        private static void SkyViewExit()
        {
            SeamlessInteriorsMod.s_WeatherRegionView--;
            SeamlessInteriorsMod.s_WeatherOutdoorView--;
            if (!s_ReportedWeather && SeamlessInteriorsMod.PlayerInteriorId != null)
            {
                s_ReportedWeather = true;
                MelonLogger.Msg($"{TAG} Weather Overhaul bina icinde bolgenin acik havasini goruyor (bir kez yazilir).");
            }
        }

        // Checks that compare the active scene with the snapshot's must read the same region.
        private static void RegionViewEnter()
        {
            SeamlessInteriorsMod.s_WeatherRegionView++;
        }

        private static void RegionViewExit()
        {
            SeamlessInteriorsMod.s_WeatherRegionView--;
        }

        // Its blood moon keeps acting on the region's animals, which SI keeps alive while the player is inside.
        internal static bool SeesWholeRegion(Assembly assembly)
        {
            return assembly != null && assembly == s_WeatherOverhaul;
        }

        private static readonly Dictionary<Assembly, bool> s_DebugTools = new Dictionary<Assembly, bool>();

        // UnityExplorer and its UniverseLib walk scenes for the person debugging; the scene root view leaves them alone.
        // Code typed into its console compiles into its own assembly and still counts as a mod.
        internal static bool IsDebugTool(Assembly assembly)
        {
            if (assembly == null) return false;

            bool tool;
            if (s_DebugTools.TryGetValue(assembly, out tool)) return tool;

            string name = assembly.GetName().Name ?? "";
            tool = name.StartsWith("UnityExplorer", StringComparison.Ordinal) || name.StartsWith("UniverseLib", StringComparison.Ordinal);
            s_DebugTools[assembly] = tool;
            return tool;
        }

        // ─── Helpers ───

        private static MethodInfo StaticMethod(Assembly assembly, string typeName, string methodName)
        {
            Type type = assembly.GetType(typeName);
            return type?.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        }

        private static Assembly FindMelonAssembly(string melonName)
        {
            foreach (MelonBase melon in MelonBase.RegisteredMelons)
                if (melon != null && melon.Info != null && melon.Info.Name == melonName)
                    return melon.GetType().Assembly;
            return null;
        }

        private static HarmonyLib.HarmonyMethod OwnMethod(string name)
        {
            return new HarmonyLib.HarmonyMethod(typeof(ModExceptions).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));
        }

        private static void ReportOnce(string where, Exception ex)
        {
            if (!s_ReportedErrors.Add(where)) return;
            MelonLogger.Warning($"{TAG} {where} istisnasi hata verdi (bir kez yazilir): {ex.Message}");
        }
    }
}
