using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppTLD.ModularElectrolizer;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── BUILDING LOAD EVENTS ───
        // For other mods, walking into a building is its interior scene loading and walking out is it unloading,
        // as with TLD's additive sub-scenes. Their patches on the vanilla load hooks run then; the game's code does not.

        private sealed class ReplayTarget
        {
            internal readonly string Label;
            internal readonly MethodInfo Method;

            internal ReplayTarget(string label, MethodInfo method)
            {
                Label = label;
                Method = method;
            }
        }

        // What mods did while a building loaded for them, undone when it unloads.
        private sealed class ModLoadRecord
        {
            internal readonly string BuildingId;
            internal readonly List<GameObject> Created = new List<GameObject>();
            internal readonly List<KeyValuePair<GameObject, bool>> ActiveBefore = new List<KeyValuePair<GameObject, bool>>();
            internal readonly HashSet<int> Seen = new HashSet<int>();

            internal ModLoadRecord(string buildingId) { BuildingId = buildingId; }
        }

        private const string LOAD_TAG = "[BINA-YUKLEME]";
        private const string MOD_VISIT_CONTENT = "SI_ModVisitContent";
        private const string MOD_FIRST_CONTENT = "SI_ModFirstContent";
        private const float ADOPT_INSIDE_PADDING = 2f;
        private static readonly object[] s_NoArgs = new object[0];

        private static bool s_ReplayTargetsResolved;
        private static ReplayTarget s_PlayerObjectHook;
        private static ReplayTarget s_QualitySettingsHook;
        private static ReplayTarget s_ElectrolizerInitHook;
        private static ReplayTarget s_RegisterElectrolizerHook;
        private static ReplayTarget s_RegisterLightSimpleHook;
        private static ReplayTarget s_RegisterToggleHook;
        private static ReplayTarget s_RegisterFieldHook;

        private static ModLoadRecord s_LoadedForMods;
        private static ModLoadRecord s_ReplayRecord;
        private static string s_PendingLoadId;
        private static bool s_ActiveRecorderTried;
        private static readonly List<object> s_ActiveRecorderHooks = new List<object>();
        private static readonly HashSet<MethodInfo> s_ReportedPatchProblems = new HashSet<MethodInfo>();
        private static bool s_ReportedFirstLoad;
        private static bool s_ReportedFirstContent;

        private static void ResolveReplayTargets()
        {
            if (s_ReplayTargetsResolved) return;
            s_ReplayTargetsResolved = true;

            s_PlayerObjectHook = Target("GameManager.InstantiatePlayerObject", typeof(GameManager), "InstantiatePlayerObject", Type.EmptyTypes);
            s_QualitySettingsHook = Target("QualitySettingsManager.ApplyCurrentQualitySettings", typeof(QualitySettingsManager), "ApplyCurrentQualitySettings", Type.EmptyTypes);
            s_ElectrolizerInitHook = Target("AuroraModularElectrolizer.Initialize", typeof(AuroraModularElectrolizer), "Initialize", Type.EmptyTypes);
            s_RegisterElectrolizerHook = Target("AuroraManager.RegisterAuroraElectrolizer", typeof(AuroraManager), "RegisterAuroraElectrolizer", new[] { typeof(AuroraModularElectrolizer) });
            s_RegisterLightSimpleHook = Target("AuroraManager.RegisterAuroraLightSimple", typeof(AuroraManager), "RegisterAuroraLightSimple", new[] { typeof(AuroraLightingSimple) });
            s_RegisterToggleHook = Target("AuroraManager.RegisterAuroraActivatedToggle", typeof(AuroraManager), "RegisterAuroraActivatedToggle", new[] { typeof(AuroraActivatedToggle) });
            s_RegisterFieldHook = Target("AuroraManager.RegisterAuroraField", typeof(AuroraManager), "RegisterAuroraField", new[] { typeof(AuroraField) });
        }

        private static ReplayTarget Target(string label, Type type, string name, Type[] parameters)
        {
            MethodInfo method = AccessTools.Method(type, name, parameters);
            if (method == null) MelonLogger.Warning($"{LOAD_TAG} {label} bulunamadi; bu kanca modlara tekrarlanmayacak.");
            return method != null ? new ReplayTarget(label, method) : null;
        }

        // One line at start-up: which mods' hooks a building's load will run.
        internal static void ReportBuildingLoadHooks()
        {
            ResolveReplayTargets();
            var entry = new List<string>();
            var content = new List<string>();
            foreach (ReplayTarget target in new[] { s_PlayerObjectHook, s_ElectrolizerInitHook, s_RegisterElectrolizerHook,
                                                    s_RegisterLightSimpleHook, s_RegisterToggleHook, s_RegisterFieldHook })
                DescribeForeignPatches(target, entry);
            DescribeForeignPatches(s_QualitySettingsHook, content);

            if (entry.Count == 0 && content.Count == 0)
            {
                MelonLogger.Msg($"{LOAD_TAG} Kuruldu: sahne yukleme kancalarina yama yapan baska mod yok.");
                return;
            }
            MelonLogger.Msg($"{LOAD_TAG} Kuruldu: bina girisinde {(entry.Count > 0 ? string.Join(", ", entry) : "hicbiri")}; " +
                            $"bina ilk kez kurulurken {(content.Count > 0 ? string.Join(", ", content) : "hicbiri")} calisacak.");
        }

        private static void DescribeForeignPatches(ReplayTarget target, List<string> into)
        {
            foreach (HarmonyLib.Patch patch in ForeignPatches(target, null))
                into.Add($"{patch.PatchMethod.DeclaringType.Assembly.GetName().Name} ({target.Label})");
        }

        // ─── Entering and leaving ───

        // The building's interior loads for other mods. Mods must see it as the loaded scene, so without the view
        // (a scene change still under way) it waits for the next frame that has it.
        internal static void LoadBuildingForMods(SeamlessInteriorInstance instance)
        {
            UnloadBuildingForMods();
            s_PendingLoadId = null;
            if (instance == null || instance.MasterInterior == null) return;

            if (!IsSceneViewActive())
            {
                s_PendingLoadId = instance.Config.ResolvedInstanceId;
                return;
            }

            ResolveReplayTargets();
            bool sceneHook = HasForeignPatches(s_PlayerObjectHook);
            bool lights = HasAuroraRegistrationPatches();
            if (!sceneHook && !lights) return;

            long perf = PerfProbe.Begin();
            var record = new ModLoadRecord(instance.Config.ResolvedInstanceId);
            s_LoadedForMods = record;
            EnsureActiveRecorder();

            int ran, created, sceneLocal;
            RunBuildingLoad(instance, record, sceneHook ? s_PlayerObjectHook : null, GameManager.m_Instance, lights,
                            out ran, out created, out sceneLocal);
            PerfProbe.End(PerfProbe.Section.ModLoad, perf);

            string line = $"{LOAD_TAG} {record.BuildingId}: ic sahne modlar icin yuklendi - {ran} yama calisti, {created} nesne eklendi " +
                          $"({sceneLocal} tanesi sahne koordinatinda), {record.ActiveBefore.Count} nesnenin gorunurlugu degisti.";
            if (!s_ReportedFirstLoad) { s_ReportedFirstLoad = true; MelonLogger.Msg(line); }
            else if (s_DebugBounds) MelonLogger.Msg(line);
        }

        // Every frame: a load that waited for the view runs once the player is still in that building and the view is on.
        internal static void TickPendingBuildingLoad()
        {
            string id = s_PendingLoadId;
            if (id == null) return;
            if (id != s_EnteredSpaceId) { s_PendingLoadId = null; return; }
            if (!IsSceneViewActive()) return;

            SeamlessInteriorInstance instance;
            if (ActiveInteriors.TryGetValue(id, out instance)) LoadBuildingForMods(instance);
            else s_PendingLoadId = null;
        }

        // The interior unloads for mods: what their load made goes, what it switched off or on is put back.
        internal static void UnloadBuildingForMods()
        {
            ModLoadRecord record = s_LoadedForMods;
            if (record == null) return;
            s_LoadedForMods = null;

            foreach (var pair in record.ActiveBefore)
            {
                try { if (pair.Key != null && pair.Key.activeSelf != pair.Value) pair.Key.SetActive(pair.Value); }
                catch { }
            }
            foreach (GameObject go in record.Created)
            {
                try { if (go != null) UnityEngine.Object.Destroy(go); }
                catch { }
            }

            SeamlessInteriorInstance instance;
            if (ActiveInteriors.TryGetValue(record.BuildingId, out instance) && instance != null && instance.MasterInterior != null)
            {
                Transform visit = instance.MasterInterior.transform.Find(MOD_VISIT_CONTENT);
                if (visit != null) UnityEngine.Object.Destroy(visit.gameObject);
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"{LOAD_TAG} {record.BuildingId}: ic sahne modlar icin kaldirildi - {record.Created.Count} nesne silindi, " +
                                $"{record.ActiveBefore.Count} nesne eski haline dondu.");
        }

        // A scene change destroyed whatever the record points at.
        internal static void ForgetBuildingLoads()
        {
            s_LoadedForMods = null;
            s_ReplayRecord = null;
            s_PendingLoadId = null;
        }

        // Buildings sharing an interior copy the first one's clone. What mods did to that clone is its own load's
        // doing, so the copy starts without it and has its own load later.
        internal static void CleanTemplateCopy(GameObject template, GameObject copy)
        {
            if (template == null || copy == null) return;

            ModLoadRecord record = s_LoadedForMods;
            if (record != null)
            {
                foreach (var pair in record.ActiveBefore)
                {
                    GameObject original = pair.Key;
                    if (original == null) continue;
                    Transform counterpart = Counterpart(original.transform, template.transform, copy.transform);
                    if (counterpart != null && counterpart.gameObject.activeSelf != pair.Value) counterpart.gameObject.SetActive(pair.Value);
                }
            }

            foreach (string name in new[] { MOD_VISIT_CONTENT, MOD_FIRST_CONTENT })
            {
                Transform content = copy.transform.Find(name);
                if (content != null) UnityEngine.Object.DestroyImmediate(content.gameObject);
            }
        }

        // The object at the same sibling path under another root; null when original is not under fromRoot.
        private static Transform Counterpart(Transform original, Transform fromRoot, Transform toRoot)
        {
            var path = new List<int>();
            Transform t = original;
            for (; t != null && t != fromRoot; t = t.parent) path.Add(t.GetSiblingIndex());
            if (t == null) return null;

            Transform c = toRoot;
            for (int i = path.Count - 1; i >= 0 && c != null; i--)
                c = path[i] < c.childCount ? c.GetChild(path[i]) : null;
            return c;
        }

        // ─── First build ───

        // A building filled for the first time in this save loads for mods once, as its scene would on a first visit.
        // What they spawn becomes building content and is saved with it; later loads bring it back from the save.
        internal static void LoadBuildingContentForMods(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;
            ResolveReplayTargets();
            if (!HasForeignPatches(s_QualitySettingsHook)) return;

            long perf = PerfProbe.Begin();
            PlaceSnapshot previousPlace = s_TickPlace;
            float previousDelta = s_TickDeltaTime;
            float delta = Time.deltaTime;
            s_TickDeltaTime = delta;
            s_TickPlace = new PlaceSnapshot
            {
                Building = instance.Config.ResolvedInstanceId,
                Indoor = true,
                Scene = instance.Config.InteriorSceneBaseName,
                Region = instance.Config.ExteriorSceneName,
                Temperature = IndoorBaseTemperature()
            };

            int ran = 0, created = 0, sceneLocal = 0;
            try
            {
                RunBuildingLoad(instance, null, s_QualitySettingsHook, GameManager.GetQualitySettingsManager(), false,
                                out ran, out created, out sceneLocal);
            }
            finally
            {
                s_TickPlace = previousPlace;
                s_TickDeltaTime = previousDelta;
            }

            // New items get the same kind of fixed id as the template's, so a reload rolls into the same save keys.
            if (created > 0) GenerateDeterministicPDIDs(instance.MasterInterior, instance.Config.ResolvedInstanceId);
            PerfProbe.End(PerfProbe.Section.ModFirstBuild, perf);

            string line = $"{LOAD_TAG} {instance.Config.ResolvedInstanceId}: ilk kurulumda modlar icin yuklendi - {ran} yama calisti, " +
                          $"{created} nesne binaya eklendi ({sceneLocal} tanesi sahne koordinatinda).";
            if (!s_ReportedFirstContent) { s_ReportedFirstContent = true; MelonLogger.Msg(line); }
            else if (s_DebugBounds) MelonLogger.Msg(line);
        }

        // ─── The load itself ───

        private static void RunBuildingLoad(SeamlessInteriorInstance instance, ModLoadRecord record, ReplayTarget sceneHook,
                                            object sceneInstance, bool lights, out int ran, out int created, out int sceneLocal)
        {
            ran = 0;
            created = 0;
            sceneLocal = 0;

            // Objects made without a parent go to the active scene, the region.
            Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var before = new HashSet<int>();
            foreach (GameObject root in RealRootObjects(scene))
                if (root != null) before.Add(root.GetInstanceID());

            s_ReplayRecord = record;
            InvalidateRootViewCache();
            try
            {
                // Vanilla order: the scene's own hook, then each object's Awake.
                if (sceneHook != null) ran += InvokeForeignPatches(sceneHook, sceneInstance, s_NoArgs);
                if (lights) ran += ReplayAuroraRegistrations(instance);
            }
            finally
            {
                s_ReplayRecord = null;
                InvalidateRootViewCache();
            }

            AdoptCreatedRoots(instance, scene, before, record, out created, out sceneLocal);
        }

        // Roots a mod made during the load are the building's. Already inside it they keep their place; anywhere else
        // they were given interior scene coordinates, which are the clone's local ones.
        private static void AdoptCreatedRoots(SeamlessInteriorInstance instance, Scene scene, HashSet<int> before,
                                              ModLoadRecord record, out int created, out int sceneLocal)
        {
            created = 0;
            sceneLocal = 0;
            Transform container = null;

            foreach (GameObject root in RealRootObjects(scene))
            {
                if (root == null || before.Contains(root.GetInstanceID()) || root == instance.MasterInterior) continue;

                if (container == null) container = ModContentContainer(instance, record != null ? MOD_VISIT_CONTENT : MOD_FIRST_CONTENT);
                // Padded: a switch on a wall sits at the volume's edge; interior coordinates land far from any building.
                bool inside = instance.IsPositionInVolume(root.transform.position, ADOPT_INSIDE_PADDING);
                root.transform.SetParent(container, inside);
                if (!inside) sceneLocal++;
                created++;
                if (record != null) record.Created.Add(root);
            }
        }

        // Sits at the clone's origin, so its local frame is still the interior scene's.
        private static Transform ModContentContainer(SeamlessInteriorInstance instance, string name)
        {
            Transform master = instance.MasterInterior.transform;
            Transform container = master.Find(name);
            if (container != null) return container;

            var go = new GameObject(name);
            go.transform.SetParent(master, false);
            return go.transform;
        }

        // Each aurora light in the building registers again, as its Awake would in a freshly loaded scene.
        private static int ReplayAuroraRegistrations(SeamlessInteriorInstance instance)
        {
            int ran = 0;
            GameObject root = instance.MasterInterior;

            bool init = HasForeignPatches(s_ElectrolizerInitHook);
            bool register = HasForeignPatches(s_RegisterElectrolizerHook);
            if (init || register)
            {
                foreach (AuroraModularElectrolizer electrolizer in InteriorScan.Components<AuroraModularElectrolizer>(root))
                {
                    if (electrolizer == null || !electrolizer.gameObject.activeInHierarchy || !electrolizer.m_IsInitialized) continue;
                    if (init) ran += InvokeForeignPatches(s_ElectrolizerInitHook, electrolizer, s_NoArgs);
                    if (register) ran += InvokeForeignPatches(s_RegisterElectrolizerHook, null, new object[] { electrolizer });
                }
            }

            if (HasForeignPatches(s_RegisterLightSimpleHook))
                foreach (AuroraLightingSimple light in InteriorScan.Components<AuroraLightingSimple>(root))
                    if (light != null && light.gameObject.activeInHierarchy)
                        ran += InvokeForeignPatches(s_RegisterLightSimpleHook, null, new object[] { light });

            if (HasForeignPatches(s_RegisterToggleHook))
                foreach (AuroraActivatedToggle toggle in InteriorScan.Components<AuroraActivatedToggle>(root))
                    if (toggle != null && toggle.gameObject.activeInHierarchy)
                        ran += InvokeForeignPatches(s_RegisterToggleHook, null, new object[] { toggle });

            if (HasForeignPatches(s_RegisterFieldHook))
                foreach (AuroraField field in InteriorScan.Components<AuroraField>(root))
                    if (field != null && field.gameObject.activeInHierarchy)
                        ran += InvokeForeignPatches(s_RegisterFieldHook, null, new object[] { field });

            return ran;
        }

        private static bool HasAuroraRegistrationPatches()
        {
            return HasForeignPatches(s_ElectrolizerInitHook) || HasForeignPatches(s_RegisterElectrolizerHook)
                || HasForeignPatches(s_RegisterLightSimpleHook) || HasForeignPatches(s_RegisterToggleHook)
                || HasForeignPatches(s_RegisterFieldHook);
        }

        // ─── Running another mod's patches ───

        private static bool HasForeignPatches(ReplayTarget target)
        {
            return ForeignPatches(target, null).Count > 0;
        }

        // Other mods' patches of one kind (all kinds when kind is null), in the order Harmony runs them.
        private static List<HarmonyLib.Patch> ForeignPatches(ReplayTarget target, string kind)
        {
            var list = new List<HarmonyLib.Patch>();
            if (target == null) return list;

            HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(target.Method);
            if (info == null) return list;

            if (kind == null || kind == "prefix") AddForeign(info.Prefixes, list);
            if (kind == null || kind == "postfix") AddForeign(info.Postfixes, list);
            if (kind == null || kind == "finalizer") AddForeign(info.Finalizers, list);
            list.Sort((a, b) => a.priority != b.priority ? b.priority.CompareTo(a.priority) : a.index.CompareTo(b.index));
            return list;
        }

        private static void AddForeign(IEnumerable<HarmonyLib.Patch> patches, List<HarmonyLib.Patch> into)
        {
            if (patches == null) return;
            foreach (HarmonyLib.Patch patch in patches)
            {
                MethodInfo method = patch != null ? patch.PatchMethod : null;
                if (method == null || method.DeclaringType == null) continue;
                if (method.DeclaringType.Assembly == typeof(SeamlessInteriorsMod).Assembly) continue;
                into.Add(patch);
            }
        }

        // Prefixes, postfixes and finalizers of target, as if the game had just called it; never the game's body.
        private static int InvokeForeignPatches(ReplayTarget target, object instance, object[] args)
        {
            if (target == null) return 0;

            var states = new Dictionary<Type, object>();
            bool runOriginal = true;
            int ran = 0;
            ran += RunPatches(ForeignPatches(target, "prefix"), target, instance, args, states, ref runOriginal, true);
            ran += RunPatches(ForeignPatches(target, "postfix"), target, instance, args, states, ref runOriginal, false);
            ran += RunPatches(ForeignPatches(target, "finalizer"), target, instance, args, states, ref runOriginal, false);
            return ran;
        }

        private static int RunPatches(List<HarmonyLib.Patch> patches, ReplayTarget target, object instance, object[] args,
                                      Dictionary<Type, object> states, ref bool runOriginal, bool prefixes)
        {
            int ran = 0;
            foreach (HarmonyLib.Patch patch in patches)
            {
                MethodInfo method = patch.PatchMethod;
                ParameterInfo[] parameters = method.GetParameters();
                var values = new object[parameters.Length];
                if (!BindPatchArguments(target, method, parameters, values, instance, args, states, runOriginal)) continue;

                try
                {
                    object result = method.Invoke(null, values);
                    ran++;
                    if (prefixes && result is bool && !(bool)result) runOriginal = false;
                }
                catch (Exception ex)
                {
                    ReportPatchProblem(method, target, "calisirken hata: " + (ex.InnerException ?? ex).Message);
                }
                ReadBackPatchArguments(method, parameters, values, states, ref runOriginal);
            }
            return ran;
        }

        // Harmony's injected names; a patch asking for anything else is skipped and reported once.
        private static bool BindPatchArguments(ReplayTarget target, MethodInfo method, ParameterInfo[] parameters, object[] values,
                                               object instance, object[] args, Dictionary<Type, object> states, bool runOriginal)
        {
            ParameterInfo[] original = target.Method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                string name = parameters[i].Name;
                Type type = parameters[i].ParameterType.IsByRef ? parameters[i].ParameterType.GetElementType() : parameters[i].ParameterType;

                if (name == "__instance")
                {
                    if (instance != null && !type.IsInstanceOfType(instance)) return ReportPatchProblem(method, target, "__instance turu uymuyor");
                    values[i] = instance;
                }
                else if (name == "__originalMethod") values[i] = target.Method;
                else if (name == "__runOriginal") values[i] = runOriginal;
                else if (name == "__args") values[i] = args;
                else if (name == "__exception") values[i] = null;
                else if (name == "__state")
                {
                    object state;
                    values[i] = states.TryGetValue(method.DeclaringType, out state) ? state : DefaultOf(type);
                }
                else if (name == "__result")
                {
                    if (target.Method.ReturnType == typeof(void)) return ReportPatchProblem(method, target, "__result istiyor");
                    values[i] = DefaultOf(type);
                }
                else
                {
                    int index = OriginalArgumentIndex(name, original);
                    if (index < 0 || index >= args.Length) return ReportPatchProblem(method, target, $"desteklenmeyen parametre '{name}'");
                    values[i] = args[index];
                }
            }
            return true;
        }

        private static void ReadBackPatchArguments(MethodInfo method, ParameterInfo[] parameters, object[] values,
                                                   Dictionary<Type, object> states, ref bool runOriginal)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!parameters[i].ParameterType.IsByRef) continue;
                if (parameters[i].Name == "__state") states[method.DeclaringType] = values[i];
                else if (parameters[i].Name == "__runOriginal" && values[i] is bool) runOriginal = (bool)values[i];
            }
        }

        // "__0" style or the original parameter's own name.
        private static int OriginalArgumentIndex(string name, ParameterInfo[] original)
        {
            int index;
            if (name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(name.Substring(2), out index)) return index;
            for (int i = 0; i < original.Length; i++)
                if (original[i].Name == name) return i;
            return -1;
        }

        private static object DefaultOf(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static bool ReportPatchProblem(MethodInfo method, ReplayTarget target, string what)
        {
            if (s_ReportedPatchProblems.Add(method))
            {
                string owner = method.DeclaringType != null ? method.DeclaringType.Assembly.GetName().Name + " " + method.DeclaringType.FullName : "?";
                MelonLogger.Warning($"{LOAD_TAG} {owner}.{method.Name} ({target.Label}) {what}; bu yama tekrarlarda atlanabilir.");
            }
            return false;
        }

        // ─── What mods switch off or on during a load ───

        // Hooked only on the managed wrappers, and only once a building load has something to replay.
        private static void EnsureActiveRecorder()
        {
            if (s_ActiveRecorderTried) return;
            s_ActiveRecorderTried = true;

            ConstructorInfo hookCtor = GetHookConstructor();
            if (hookCtor == null) return;

            try
            {
                s_ActiveRecorderHooks.Add(HookWith(hookCtor, typeof(GameObject).GetMethod("SetActive", BindingFlags.Public | BindingFlags.Instance,
                                                                                         null, new[] { typeof(bool) }, null), nameof(SetActiveRecorder)));
                MethodInfo setter = PropertyAccessor(typeof(GameObject), "active", BindingFlags.Instance, true);
                if (setter != null) s_ActiveRecorderHooks.Add(HookWith(hookCtor, setter, nameof(ActiveSetterRecorder)));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{LOAD_TAG} Gorunurluk kaydi kurulamadi; modlarin kapattigi nesneler cikista geri acilmayacak: " +
                                    (ex.InnerException ?? ex).Message);
            }
        }

        private static void SetActiveRecorder(Action<GameObject, bool> orig, GameObject self, bool value)
        {
            if (s_ReplayRecord != null) NoteActiveChange(self, value);
            orig(self, value);
        }

        private static void ActiveSetterRecorder(Action<GameObject, bool> orig, GameObject self, bool value)
        {
            if (s_ReplayRecord != null) NoteActiveChange(self, value);
            orig(self, value);
        }

        private static void NoteActiveChange(GameObject go, bool value)
        {
            try
            {
                ModLoadRecord record = s_ReplayRecord;
                if (record == null || go == null || go.activeSelf == value) return;
                if (record.Seen.Add(go.GetInstanceID())) record.ActiveBefore.Add(new KeyValuePair<GameObject, bool>(go, !value));
            }
            catch { }
        }
    }
}
