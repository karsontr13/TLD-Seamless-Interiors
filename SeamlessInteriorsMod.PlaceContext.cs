using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Il2Cpp;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── PLACE CONTEXT ───
        // Components other mods put on world objects (Frozen Food's, for one) run as in the vanilla game: where the
        // player is not, SI runs them once a second with that place's indoor state, scene and temperature.

        private const string REGION_PLACE = "";
        private const float PLACE_TICK_SECONDS = 1f;
        private const int PLACE_VISITS_PER_FRAME = 24;
        private const float PLACE_SAMPLE_SECONDS = 0.5f;
        private const float PLACE_SETTLE_SECONDS = 1f;
        private const int PLACE_TYPE_ERROR_LIMIT = 5;

        private sealed class ModComponentKind
        {
            internal string Name;
            internal Action<object> Update;
            internal int Errors;
            internal bool Failed;
        }

        private sealed class ModComponentEntry
        {
            internal MonoBehaviour Behaviour;
            internal ModComponentKind Kind;
            internal bool Checked;
            internal bool WorldObject;
            internal bool Started;
            internal bool DisabledBySI;
            internal float LastTick;
        }

        private sealed class PlaceSnapshot
        {
            internal string Building;
            internal bool Indoor;
            internal string Scene;
            // The loaded region whose name reads as Scene.
            internal string Region;
            internal float Temperature;
        }

        private static bool s_PlaceContextInstalled;
        private static object s_IndoorEnvironmentHook;
        private static object s_TemperatureHook;
        private static object s_TemperatureNoHeatHook;
        private static object s_DeltaTimeHook;
        private static readonly Dictionary<Type, ModComponentKind> s_ModComponentKinds = new Dictionary<Type, ModComponentKind>();
        private static readonly List<object> s_NewModComponents = new List<object>();
        private static readonly HashSet<object> s_KnownModComponents = new HashSet<object>();
        private static readonly List<ModComponentEntry> s_ModComponents = new List<ModComponentEntry>();
        private static readonly Dictionary<string, float> s_PlaceTemperatures = new Dictionary<string, float>();
        private static readonly Dictionary<string, PlaceSnapshot> s_PlaceSnapshots = new Dictionary<string, PlaceSnapshot>();
        private static readonly Dictionary<int, bool> s_RegionSceneHandles = new Dictionary<int, bool>();
        private static PlaceSnapshot s_TickPlace;
        private static float s_TickDeltaTime;
        private static string s_ContextPlace;
        private static string s_ContextRegion;
        private static int s_PlaceVisitIndex;
        private static int s_PlaceSnapshotsFrame = -1;
        private static float s_PlaceChangedAt;
        private static float s_NextPlaceSample;

        // Other mods' MonoBehaviours with an Update are found here; each instance is noted as the game creates it.
        internal static void InstallPlaceContext(HarmonyLib.Harmony harmony)
        {
            try
            {
                var created = new HarmonyLib.HarmonyMethod(typeof(SeamlessInteriorsMod).GetMethod(nameof(ModComponentCreated), BindingFlags.NonPublic | BindingFlags.Static));
                var names = new List<string>();
                int ownershipOnly = 0;
                foreach (MelonBase melon in MelonBase.RegisteredMelons)
                {
                    Assembly assembly = melon.GetType().Assembly;
                    if (assembly == typeof(SeamlessInteriorsMod).Assembly) continue;

                    foreach (Type type in LoadableTypes(assembly))
                    {
                        if (type == null || type.IsAbstract || type.ContainsGenericParameters || !typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                        if (!ClassInjector.IsTypeRegisteredInIl2Cpp(type)) continue;

                        // Components without an Update are only noted: they mark their object as the mod's.
                        MethodInfo update = FindModUpdate(type);
                        ConstructorInfo ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(IntPtr) }, null);
                        if (ctor == null) continue;

                        try
                        {
                            var kind = new ModComponentKind { Name = type.FullName, Update = update != null ? CompileModUpdate(type, update) : null };
                            harmony.Patch(ctor, postfix: created);
                            s_ModComponentKinds[type] = kind;
                            if (update != null) names.Add(type.FullName);
                            else ownershipOnly++;
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"[BAGLAM] {type.FullName} atlandi: {(ex.InnerException ?? ex).Message}");
                        }
                    }
                }

                if (names.Count == 0)
                if (s_ModComponentKinds.Count == 0)
                {
                    MelonLogger.Msg("[BAGLAM] Nesnelere bilesen ekleyen mod yok; kurulmadi.");
                    return;
                }

                if (names.Count > 0)
                {
                    ConstructorInfo hookCtor = GetHookConstructor();
                    if (hookCtor == null) throw new InvalidOperationException("MonoMod bulunamadi.");
                    EnsureIndoorEnvironmentHook();
                    s_TemperatureHook = HookWith(hookCtor, typeof(Weather).GetMethod("GetCurrentTemperature", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null), nameof(TemperatureInPlace));
                    s_TemperatureNoHeatHook = HookWith(hookCtor, typeof(Weather).GetMethod("GetCurrentTemperatureWithoutHeatSources", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null), nameof(TemperatureInPlace));
                    s_DeltaTimeHook = HookWith(hookCtor, PropertyAccessor(typeof(Time), "deltaTime", BindingFlags.Static, false), nameof(DeltaTimeInPlace));
                }

                s_PlaceContextInstalled = true;
                MelonLogger.Msg($"[BAGLAM] Kuruldu: {(names.Count > 0 ? string.Join(", ", names) : "hicbiri")} oyuncunun olmadigi yerde o yerin baglamiyla calisiyor; " +
                                $"{ownershipOnly} tur daha yalnizca nesnenin moda ait oldugunu isaretliyor.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[BAGLAM] Kurulamadi: " + (ex.InnerException ?? ex).Message);
            }
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types; }
        }

        // The mod's own Update, possibly on one of its base classes, never the game's.
        private static MethodInfo FindModUpdate(Type type)
        {
            for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                MethodInfo m = t.GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m != null) return m;
            }
            return null;
        }

        private static Action<object> CompileModUpdate(Type type, MethodInfo update)
        {
            ParameterExpression component = Expression.Parameter(typeof(object), "component");
            return Expression.Lambda<Action<object>>(Expression.Call(Expression.Convert(component, type), update), component).Compile();
        }

        // Runs while the game builds the component; it is looked at from the next frame on.
        private static void ModComponentCreated(object __instance)
        {
            if (__instance != null) s_NewModComponents.Add(__instance);
        }

        // ─── Context seen by mod code SI runs ───

        // The building mod code is shown: the one SI is running a component for, otherwise the player's.
        private static string ViewBuilding()
        {
            if (s_TickPlace != null) return s_TickPlace.Building;
            return s_EnteredSpace != null ? s_EnteredSpaceId : null;
        }

        private static bool InPlaceTick { get { return s_TickPlace != null && !s_BypassSceneView; } }

        // Weather Overhaul's sky view (see Compat/ModExceptions.cs): real scene names and, in a building, open air.
        internal static int s_WeatherRegionView;
        internal static int s_WeatherOutdoorView;

        // Shared by the place context and the Weather Overhaul exception.
        internal static void EnsureIndoorEnvironmentHook()
        {
            if (s_IndoorEnvironmentHook != null) return;
            ConstructorInfo hookCtor = GetHookConstructor();
            if (hookCtor == null) throw new InvalidOperationException("MonoMod bulunamadi.");
            s_IndoorEnvironmentHook = HookWith(hookCtor, typeof(Weather).GetMethod("IsIndoorEnvironment", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null), nameof(IndoorEnvironmentInPlace));
        }

        private static bool IndoorEnvironmentInPlace(Func<Weather, bool> orig, Weather self)
        {
            if (s_WeatherOutdoorView > 0 && s_EnteredSpace != null) return false;
            return InPlaceTick ? s_TickPlace.Indoor : orig(self);
        }

        private static float TemperatureInPlace(Func<Weather, float> orig, Weather self)
        {
            return InPlaceTick ? s_TickPlace.Temperature : orig(self);
        }

        private static float DeltaTimeInPlace(Func<float> orig)
        {
            return InPlaceTick ? s_TickDeltaTime : orig();
        }

        // ─── Per frame ───

        internal static void TickPlaceContext()
        {
            if (!s_PlaceContextInstalled) return;
            FlushNewModComponents();

            string region = ReadReal(() => GameManager.m_ActiveScene);
            if (ActiveInteriors.Count == 0 || !IsRegionWithBuildings(region))
            {
                ReleaseModComponents();
                return;
            }

            if (region != s_ContextRegion)
            {
                s_ContextRegion = region;
                s_RegionSceneHandles.Clear();
                s_PlaceTemperatures.Clear();
            }

            string place = s_EnteredSpace != null ? s_EnteredSpaceId : REGION_PLACE;
            SamplePlaceTemperature(place);

            if (place != s_ContextPlace)
            {
                bool first = s_ContextPlace == null;
                s_ContextPlace = place;
                s_PlaceChangedAt = Time.time;
                for (int i = 0; i < s_ModComponents.Count; i++) VisitModComponent(s_ModComponents[i], place, false);
                if (!first && s_DebugBounds) MelonLogger.Msg($"[BAGLAM] Oyuncunun yeri: {(place == REGION_PLACE ? region : place)}; {s_ModComponents.Count} mod bileseni yeniden yerlestirildi.");
            }

            int count = s_ModComponents.Count;
            for (int n = 0; n < PLACE_VISITS_PER_FRAME && n < count; n++)
            {
                if (s_PlaceVisitIndex >= s_ModComponents.Count) s_PlaceVisitIndex = 0;
                ModComponentEntry entry = s_ModComponents[s_PlaceVisitIndex];
                if (!VisitModComponent(entry, place, true))
                {
                    s_KnownModComponents.Remove(entry.Behaviour);
                    s_ModComponents.RemoveAt(s_PlaceVisitIndex);
                    continue;
                }
                s_PlaceVisitIndex++;
            }
        }

        private static void FlushNewModComponents()
        {
            if (s_NewModComponents.Count == 0) return;

            float now = Time.time;
            foreach (object created in s_NewModComponents)
            {
                ModComponentKind kind;
                MonoBehaviour behaviour = created as MonoBehaviour;
                if (behaviour == null || !s_KnownModComponents.Add(created) || !FindModComponentKind(created.GetType(), out kind)) continue;
                s_ModComponents.Add(new ModComponentEntry { Behaviour = behaviour, Kind = kind, LastTick = now });
            }
            s_NewModComponents.Clear();
        }

        // ─── Objects that belong to mods ───
        // A world root carrying another mod's component is that mod's content (an Architect wall), never scenery.
        // Gear and placeables keep SI's own handling; clones, the player and DontDestroyOnLoad are not the world's.

        private static HashSet<int> s_ModOwnedRootCache = new HashSet<int>();
        private static float s_ModOwnedRootCacheTime = -1f;

        internal static List<Transform> ModOwnedRoots()
        {
            FlushNewModComponents();
            var roots = new List<Transform>();
            var seen = new HashSet<int>();
            foreach (ModComponentEntry entry in s_ModComponents)
            {
                MonoBehaviour behaviour = entry.Behaviour;
                if (behaviour == null) continue;

                Transform root = behaviour.transform.root;
                if (!seen.Add(root.GetInstanceID())) continue;
                if (BuildingOf(root) != null || PlayerRefs.IsPlayerRoot(root)) continue;
                if (root.GetComponent<GearItem>() != null || root.GetComponent<Il2CppTLD.Placement.Placeable>() != null) continue;
                if (RealSceneName(root.gameObject.scene) == "DontDestroyOnLoad") continue;
                roots.Add(root);
            }

            var ids = new HashSet<int>();
            foreach (Transform root in roots) ids.Add(root.GetInstanceID());
            s_ModOwnedRootCache = ids;
            s_ModOwnedRootCacheTime = Time.realtimeSinceStartup;
            return roots;
        }

        internal static HashSet<int> ModOwnedRootIds()
        {
            ModOwnedRoots();
            return s_ModOwnedRootCache;
        }

        // For the inside rays, which ask many times a frame: the list is at most a second old.
        internal static bool IsModOwnedRoot(Transform root)
        {
            if (root == null || s_ModComponents.Count == 0 && s_NewModComponents.Count == 0) return false;
            if (s_ModOwnedRootCacheTime < 0f || Time.realtimeSinceStartup - s_ModOwnedRootCacheTime > 1f) ModOwnedRootIds();
            return s_ModOwnedRootCache.Contains(root.GetInstanceID());
        }

        private static bool FindModComponentKind(Type type, out ModComponentKind kind)
        {
            for (Type t = type; t != null; t = t.BaseType)
                if (s_ModComponentKinds.TryGetValue(t, out kind)) return true;
            kind = null;
            return false;
        }

        private static bool IsRegionWithBuildings(string region)
        {
            if (string.IsNullOrEmpty(region)) return false;
            foreach (var instance in ActiveInteriors.Values)
                if (instance != null && instance.Config != null && instance.Config.ExteriorSceneName == region) return true;
            return false;
        }

        // A component the game was running where the player is not is switched off and run here instead; one the
        // game never started is left for when the player gets there, as its scene would load then. False once destroyed.
        private static bool VisitModComponent(ModComponentEntry entry, string place, bool tick)
        {
            MonoBehaviour behaviour = entry.Behaviour;
            if (behaviour == null) return false;
            if (entry.Kind.Update == null || entry.Kind.Failed) return true;

            if (!entry.Checked)
            {
                entry.Checked = true;
                GameObject go = behaviour.gameObject;
                entry.WorldObject = go.GetComponent<GearItem>() != null || go.GetComponent<ObjectGuid>() != null;
            }
            if (!entry.WorldObject) return true;

            string where = PlaceOf(behaviour);
            if (where == null || where == place)
            {
                if (entry.DisabledBySI) { behaviour.enabled = true; entry.DisabledBySI = false; }
                if (!entry.Started && behaviour.isActiveAndEnabled) entry.Started = true;
                entry.LastTick = Time.time;
                return true;
            }

            if (behaviour.isActiveAndEnabled)
            {
                entry.Started = true;
                behaviour.enabled = false;
                entry.DisabledBySI = true;
                entry.LastTick = Time.time;
                return true;
            }

            if (!tick || !entry.Started) return true;
            float dt = Time.time - entry.LastTick;
            if (dt < PLACE_TICK_SECONDS) return true;

            // Only what the game would run there: not something switched off or packed away in a container.
            if (!entry.DisabledBySI && !behaviour.enabled) return true;
            if (!IsActiveInPlace(behaviour.transform, where)) { entry.LastTick = Time.time; return true; }

            RunInPlace(entry, where, dt);
            return true;
        }

        // Active up to the place's own root: a closed building's clone is inactive as a whole.
        private static bool IsActiveInPlace(Transform t, string where)
        {
            Transform stop = null;
            SeamlessInteriorInstance instance;
            if (where != REGION_PLACE && ActiveInteriors.TryGetValue(where, out instance) && instance != null && instance.MasterInterior != null)
                stop = instance.MasterInterior.transform;

            for (; t != null && t != stop; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        private static void RunInPlace(ModComponentEntry entry, string where, float dt)
        {
            entry.LastTick = Time.time;
            s_TickPlace = SnapshotOf(where);
            s_TickDeltaTime = dt;
            try
            {
                entry.Kind.Update(entry.Behaviour);
                entry.Kind.Errors = 0;
            }
            catch (Exception ex)
            {
                if (++entry.Kind.Errors >= PLACE_TYPE_ERROR_LIMIT) ReleaseModComponentKind(entry.Kind);
                if (entry.Kind.Errors == 1 || entry.Kind.Failed)
                    MelonLogger.Warning($"[BAGLAM] {entry.Kind.Name} calistirilirken hata{(entry.Kind.Failed ? " (tur birakildi, oyun calistiracak)" : "")}: {(ex.InnerException ?? ex).Message}");
            }
            finally
            {
                s_TickPlace = null;
            }
        }

        // A building's clone, the region (and items dropped in the player's building, not yet under its clone),
        // or null for anything else: the player's inventory, other scenes.
        private static string PlaceOf(MonoBehaviour behaviour)
        {
            Transform root = behaviour.transform.root;
            string building = BuildingOf(root);
            if (building != null) return building;

            UnityEngine.SceneManagement.Scene scene = behaviour.gameObject.scene;
            if (!scene.IsValid() || !IsRegionScene(scene)) return null;

            SeamlessInteriorInstance inside;
            if (s_EnteredSpace != null && ActiveInteriors.TryGetValue(s_EnteredSpaceId, out inside) && inside != null
                && inside.IsPositionInVolume(root.position))
                return s_EnteredSpaceId;
            return REGION_PLACE;
        }

        private static bool IsRegionScene(UnityEngine.SceneManagement.Scene scene)
        {
            bool isRegion;
            if (s_RegionSceneHandles.TryGetValue(scene.handle, out isRegion)) return isRegion;

            string name = RealSceneName(scene);
            isRegion = name != null && (name == s_ContextRegion || name.StartsWith(s_ContextRegion + "_"));
            s_RegionSceneHandles[scene.handle] = isRegion;
            return isRegion;
        }

        // The temperature a place had when the player was last there; a place not seen this session uses the game's
        // indoor or outdoor base temperature.
        private static PlaceSnapshot SnapshotOf(string where)
        {
            int frame = Time.frameCount;
            if (frame != s_PlaceSnapshotsFrame)
            {
                s_PlaceSnapshotsFrame = frame;
                s_PlaceSnapshots.Clear();
            }

            PlaceSnapshot snapshot;
            if (s_PlaceSnapshots.TryGetValue(where, out snapshot)) return snapshot;

            snapshot = new PlaceSnapshot { Building = where == REGION_PLACE ? null : where, Indoor = where != REGION_PLACE, Scene = s_ContextRegion, Region = s_ContextRegion };
            SeamlessInteriorInstance instance;
            if (snapshot.Building != null && ActiveInteriors.TryGetValue(where, out instance) && instance != null
                && !string.IsNullOrEmpty(instance.Config.InteriorSceneBaseName))
                snapshot.Scene = instance.Config.InteriorSceneBaseName;

            float recorded;
            if (s_PlaceTemperatures.TryGetValue(where, out recorded)) snapshot.Temperature = recorded;
            else snapshot.Temperature = snapshot.Indoor ? IndoorBaseTemperature() : OutdoorBaseTemperature();

            s_PlaceSnapshots[where] = snapshot;
            return snapshot;
        }

        private static float IndoorBaseTemperature()
        {
            Weather weather = GameManager.GetWeatherComponent();
            return weather != null ? weather.m_IndoorTemperatureCelsius : 0f;
        }

        private static float OutdoorBaseTemperature()
        {
            Weather weather = GameManager.GetWeatherComponent();
            TimeOfDay tod = GameManager.GetTimeOfDayComponent();
            ExperienceModeManager emm = GameManager.GetExperienceModeManagerComponent();
            if (weather == null || tod == null || emm == null) return 0f;
            return weather.m_BaseTemperature - emm.GetOutdoorTempDropCelcius(tod.GetDayNumber());
        }

        // The player's temperature, noted for the place they are in; not right after a door, while it settles.
        private static void SamplePlaceTemperature(string place)
        {
            float now = Time.time;
            if (place != s_ContextPlace || now - s_PlaceChangedAt < PLACE_SETTLE_SECONDS || now < s_NextPlaceSample) return;
            s_NextPlaceSample = now + PLACE_SAMPLE_SECONDS;

            Weather weather = GameManager.GetWeatherComponent();
            if (weather != null) s_PlaceTemperatures[place] = weather.GetCurrentTemperature();
        }

        // ─── Giving components back to the game ───

        private static void ReleaseModComponentKind(ModComponentKind kind)
        {
            kind.Failed = true;
            foreach (ModComponentEntry entry in s_ModComponents)
                if (entry.Kind == kind) Release(entry);
        }

        private static void ReleaseModComponents()
        {
            if (s_ContextPlace == null) return;
            s_ContextPlace = null;
            foreach (ModComponentEntry entry in s_ModComponents) Release(entry);
        }

        private static void Release(ModComponentEntry entry)
        {
            if (!entry.DisabledBySI) return;
            entry.DisabledBySI = false;
            try { if (entry.Behaviour != null) entry.Behaviour.enabled = true; }
            catch { }
        }
    }
}
