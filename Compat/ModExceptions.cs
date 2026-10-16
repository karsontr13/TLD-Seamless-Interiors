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
        // SI hides template scenes from mods and empties them first, so SI runs that pass itself before the copy.
        private static MethodInfo s_SCPlusMakeDecoration;
        private static MethodInfo s_SCPlusCarryableRoot;

        private static void InstallSafehouseCustomizationPlus()
        {
            Assembly assembly = FindMelonAssembly("SafehouseCustomizationPlus");
            if (assembly == null) return;

            const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            s_SCPlusMakeDecoration = assembly.GetType("SCPlus.SCPMain")?.GetMethod("MakeIntoDecoration", statics, null, new[] { typeof(GameObject) }, null);
            s_SCPlusCarryableRoot = assembly.GetType("SCPlus.Utility")?.GetMethod("TryGetCarryableRoot", statics);
            if (s_SCPlusMakeDecoration == null || s_SCPlusCarryableRoot == null)
            {
                s_SCPlusMakeDecoration = null;
                MelonLogger.Warning($"{TAG} Safehouse Customization+ istisnasi kurulamadi: beklenen uyeler bulunamadi (surum degismis olabilir).");
                return;
            }
            MelonLogger.Msg($"{TAG} Safehouse Customization+: bina sobalari ve buyuk esyalar tasinabilir yapilacak.");
        }

        // The same passes SC+ runs in OnSceneWasInitialized, per scene suffix, on the template's active objects.
        internal static void ApplySafehouseCustomizationPlus(UnityEngine.SceneManagement.Scene scene, GameObject[] roots)
        {
            if (s_SCPlusMakeDecoration == null || roots == null) return;

            string name = SeamlessInteriorsMod.RealSceneName(scene) ?? "";
            var done = new HashSet<int>();
            int made = 0;
            try
            {
                foreach (GameObject root in roots)
                {
                    if (root == null) continue;
                    if (name.EndsWith("_SANDBOX"))
                    {
                        made += MakeDecorations<WoodStove>(root, done);
                        made += MakeDecorations<MillingMachine>(root, done);
                        made += MakeDecorations<AmmoWorkBench>(root, done);
                        made += MakeDecorations<ObjectAnim>(root, done);
                    }
                    else if (name.EndsWith("_DLC01")) made += MakeDecorations<Il2CppTLD.Trader.TraderRadio>(root, done);
                    else if (!name.Contains("_")) made += MakeDecorations<WoodStove>(root, done);
                }
            }
            catch (Exception ex)
            {
                ReportOnce("Safehouse Customization+", ex);
            }

            if (made > 0)
                MelonLogger.Msg($"{TAG} Safehouse Customization+: '{name}' icinde {made} esya tasinabilir yapildi.");
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
            if (s_SCPlusMakeDecoration == null || instance == null || instance.MasterInterior == null) return;
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

        // SC+ keeps a moved object under the region, so in a parked room it would hang in the air seen from outside.
        // Only its look and collision follow the room: SC+ saves an inactive object as removed, losing its contents.
        private static IList s_SCPlusCarryables;
        private static float s_SCPlusNextTick;
        private static readonly Dictionary<int, bool> s_SCPlusShown = new Dictionary<int, bool>();

        internal static void TickSafehouseCustomizationPlus()
        {
            if (s_SCPlusMakeDecoration == null || Time.unscaledTime < s_SCPlusNextTick) return;
            s_SCPlusNextTick = Time.unscaledTime + 0.5f;
            try
            {
                if (s_SCPlusCarryables == null)
                    s_SCPlusCarryables = s_SCPlusMakeDecoration.DeclaringType.Assembly.GetType("SCPlus.CarryableManager")?
                        .GetField("carryables", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IList;
                if (s_SCPlusCarryables == null) return;

                foreach (object item in s_SCPlusCarryables)
                {
                    var carryable = item as Component;
                    if (carryable == null || carryable.gameObject == null || !carryable.gameObject.activeInHierarchy) continue;
                    GameObject go = carryable.gameObject;
                    if (SeamlessInteriorsMod.IsUnderAnyMasterInteriorPublic(go.transform)) continue;

                    bool visible = true;
                    foreach (SeamlessInteriorInstance instance in SeamlessInteriorsMod.ActiveInteriors.Values)
                    {
                        if (instance == null || instance.MasterInterior == null || !instance.Config.IsParked) continue;
                        if (!instance.IsPositionInVolume(go.transform.position)) continue;
                        // The open room is drawn on its own layer for window views; this object is not.
                        visible = instance.MasterInterior.activeSelf && SeamlessInteriorsMod.PlayerInteriorId == instance.Config.ResolvedInstanceId;
                        break;
                    }

                    bool shown;
                    int id = go.GetInstanceID();
                    if (!s_SCPlusShown.TryGetValue(id, out shown)) shown = true;
                    if (shown == visible) continue;
                    SeamlessInteriorsMod.SetObjectVisualState(go, visible);
                    s_SCPlusShown[id] = visible;
                }
            }
            catch (Exception ex)
            {
                ReportOnce("Safehouse Customization+ gorunum", ex);
            }
        }

        private static bool ResolveSCPlusData()
        {
            if (s_SCPlusLoad != null) return true;

            Assembly assembly = s_SCPlusMakeDecoration.DeclaringType.Assembly;
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

        private static int MakeDecorations<T>(GameObject root, HashSet<int> done) where T : Component
        {
            int made = 0;
            foreach (T component in root.GetComponentsInChildren<T>(false))
            {
                var args = new object[] { component.transform, null };
                if (!(bool)s_SCPlusCarryableRoot.Invoke(null, args)) continue;

                var go = args[1] as GameObject;
                if (go == null || !done.Add(go.GetInstanceID())) continue;
                if (go.GetComponentInChildren<DecorationItem>() != null) continue;

                s_SCPlusMakeDecoration.Invoke(null, new object[] { go });
                made++;
            }
            return made;
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
