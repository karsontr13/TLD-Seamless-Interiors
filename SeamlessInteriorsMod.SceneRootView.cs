using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── SCENE ROOT VIEW ───
        // Mods walking the loaded scenes find what a vanilla game has loaded: inside a building only its interior,
        // outdoors the region without buildings, and never the interior scenes SI emptied into its clones.

        private static readonly List<object> s_RootViewHooks = new List<object>();
        private static bool s_RootViewFailed;
        private static bool s_ReportedRootView;

        private static int s_VisibleScenesFrame = -1;
        private static string s_VisibleScenesBuilding;
        private static readonly List<Scene> s_VisibleScenes = new List<Scene>();

        private static int s_RootsCacheFrame = -1;
        private static string s_RootsCacheBuilding;
        private static readonly Dictionary<int, GameObject[]> s_RootsCache = new Dictionary<int, GameObject[]>();
        private static readonly GameObject[] s_NoRoots = new GameObject[0];

        private delegate Il2CppReferenceArray<GameObject> RootsOrig(ref Scene self);
        private delegate void RootsListOrig(ref Scene self, Il2CppSystem.Collections.Generic.List<GameObject> rootGameObjects);
        private delegate int RootCountOrig(ref Scene self);

        // All or nothing: a scene count that disagreed with GetSceneAt would be worse than no view.
        private static void InstallSceneRootView(ConstructorInfo hookCtor)
        {
            try
            {
                const BindingFlags statics = BindingFlags.Public | BindingFlags.Static;
                const BindingFlags instance = BindingFlags.Public | BindingFlags.Instance;

                s_RootViewHooks.Add(HookWith(hookCtor, PropertyAccessor(typeof(SceneManager), "sceneCount", BindingFlags.Static, false), nameof(SceneCountView)));
                s_RootViewHooks.Add(HookWith(hookCtor, typeof(SceneManager).GetMethod("GetSceneAt", statics, null, new[] { typeof(int) }, null), nameof(SceneAtView)));
                s_RootViewHooks.Add(HookWith(hookCtor, typeof(SceneManager).GetMethod("GetSceneByName", statics, null, new[] { typeof(string) }, null), nameof(SceneByNameView)));
                s_RootViewHooks.Add(HookWith(hookCtor, typeof(Scene).GetMethod("GetRootGameObjects", instance, null, Type.EmptyTypes, null), nameof(RootsView)));
                s_RootViewHooks.Add(HookWith(hookCtor, typeof(Scene).GetMethod("GetRootGameObjects", instance, null,
                    new[] { typeof(Il2CppSystem.Collections.Generic.List<GameObject>) }, null), nameof(RootsListView)));
                s_RootViewHooks.Add(HookWith(hookCtor, PropertyAccessor(typeof(Scene), "rootCount", BindingFlags.Instance, false), nameof(RootCountView)));

                MethodInfo loaded = PropertyAccessor(typeof(SceneManager), "loadedSceneCount", BindingFlags.Static, false);
                if (loaded != null) s_RootViewHooks.Add(HookWith(hookCtor, loaded, nameof(LoadedSceneCountView)));

                MelonLogger.Msg("[KOK-GORUNUMU] Kuruldu: modlar yuklu sahneleri ve kok nesnelerini vanilla oyundaki gibi goruyor.");
            }
            catch (Exception ex)
            {
                foreach (object hook in s_RootViewHooks)
                {
                    try { (hook as IDisposable)?.Dispose(); }
                    catch { }
                }
                s_RootViewHooks.Clear();
                s_RootViewFailed = true;
                MelonLogger.Warning("[KOK-GORUNUMU] Kurulamadi: " + (ex.InnerException ?? ex).Message);
            }
        }

        // ─── SI's own reads, past the view ───

        internal static int RealSceneCount() { return ReadReal(() => SceneManager.sceneCount); }
        internal static Scene RealSceneAt(int index) { return ReadReal(() => SceneManager.GetSceneAt(index)); }
        internal static Scene RealSceneByName(string name) { return ReadReal(() => SceneManager.GetSceneByName(name)); }

        internal static GameObject[] RealRootObjects(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return s_NoRoots;
            Il2CppReferenceArray<GameObject> roots = ReadReal(() => scene.GetRootGameObjects());
            return roots != null ? (GameObject[])roots : s_NoRoots;
        }

        // ─── Hooks ───

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int SceneCountView(Func<int> orig)
        {
            int real = orig();
            return RootViewApplies(nameof(SceneCountView)) ? VisibleScenes().Count : real;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int LoadedSceneCountView(Func<int> orig)
        {
            int real = orig();
            if (!RootViewApplies(nameof(LoadedSceneCountView))) return real;

            int loaded = 0;
            foreach (Scene scene in VisibleScenes())
                if (scene.isLoaded) loaded++;
            return loaded;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Scene SceneAtView(Func<int, Scene> orig, int index)
        {
            if (!RootViewApplies(nameof(SceneAtView))) return orig(index);

            List<Scene> visible = VisibleScenes();
            return index >= 0 && index < visible.Count ? visible[index] : orig(index);
        }

        // The building's interior name finds the region scene, which already reads as that interior.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Scene SceneByNameView(Func<string, Scene> orig, string name)
        {
            Scene real = orig(name);
            if (!RootViewApplies(nameof(SceneByNameView))) return real;

            SeamlessInteriorInstance inside = RootViewInstance();
            if (inside != null && name == inside.Config.InteriorSceneBaseName) return inside.MasterInterior.scene;

            foreach (Scene scene in VisibleScenes())
                if (scene.handle == real.handle) return real;
            return default(Scene);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Il2CppReferenceArray<GameObject> RootsView(RootsOrig orig, ref Scene self)
        {
            Il2CppReferenceArray<GameObject> real = orig(ref self);
            if (real == null || !RootViewApplies(nameof(RootsView))) return real;

            GameObject[] view = RootsFor(self, real);
            return view != null ? new Il2CppReferenceArray<GameObject>(view) : real;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RootsListView(RootsListOrig orig, ref Scene self, Il2CppSystem.Collections.Generic.List<GameObject> rootGameObjects)
        {
            orig(ref self, rootGameObjects);
            if (rootGameObjects == null || !RootViewApplies(nameof(RootsListView))) return;

            var real = new GameObject[rootGameObjects.Count];
            for (int i = 0; i < real.Length; i++) real[i] = rootGameObjects[i];

            GameObject[] view = RootsFor(self, real);
            if (view == null) return;

            rootGameObjects.Clear();
            foreach (GameObject root in view) rootGameObjects.Add(root);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RootCountView(RootCountOrig orig, ref Scene self)
        {
            int real = orig(ref self);
            if (!RootViewApplies(nameof(RootCountView))) return real;

            GameObject[] view = RootsFor(self, RealRootObjects(self));
            return view != null ? view.Length : real;
        }

        // ─── What the mod is shown ───

        // Only another mod's call is answered from the view; a failure switches the view off for the session.
        private static bool RootViewApplies(string hook)
        {
            if (s_BypassSceneView || s_RootViewFailed || ActiveInteriors.Count == 0) return false;

            try
            {
                long perf = PerfProbe.Begin();
                string caller = ModCaller(hook, true);
                PerfProbe.Scanned(PerfProbe.Scan.ViewCaller, 0, perf);
                if (caller == null) return false;

                if (!s_ReportedRootView)
                {
                    s_ReportedRootView = true;
                    MelonLogger.Msg($"[KOK-GORUNUMU] {caller} yuklu sahneleri taradi; {(RootViewBuilding() ?? "bolge")} goruntusu verildi.");
                }
                return true;
            }
            catch (Exception ex)
            {
                s_RootViewFailed = true;
                MelonLogger.Warning("[KOK-GORUNUMU] Kapatildi, modlar gercek sahneleri goruyor: " + ex.Message);
                return false;
            }
        }

        // The building whose interior mods see: the one SI runs mod code for, else the player's while the view is on.
        private static string RootViewBuilding()
        {
            if (InPlaceTick) return s_TickPlace.Building;
            return IsSceneViewActive() ? s_EnteredSpaceId : null;
        }

        private static SeamlessInteriorInstance RootViewInstance()
        {
            string id = RootViewBuilding();
            SeamlessInteriorInstance instance;
            if (id == null || !ActiveInteriors.TryGetValue(id, out instance) || instance == null || instance.MasterInterior == null) return null;
            return instance;
        }

        internal static void InvalidateRootViewCache()
        {
            s_VisibleScenesFrame = -1;
            s_RootsCacheFrame = -1;
        }

        // Inside, the region's sub-scenes are not loaded in the vanilla game either; other scenes stay.
        private static List<Scene> VisibleScenes()
        {
            string building = RootViewBuilding();
            int frame = Time.frameCount;
            if (frame == s_VisibleScenesFrame && building == s_VisibleScenesBuilding) return s_VisibleScenes;

            s_VisibleScenesFrame = frame;
            s_VisibleScenesBuilding = building;
            s_VisibleScenes.Clear();

            SeamlessInteriorInstance inside = RootViewInstance();
            string subScenes = inside != null ? inside.Config.ExteriorSceneName + "_" : null;

            bool previous = s_BypassSceneView;
            s_BypassSceneView = true;
            try
            {
                int count = SceneManager.sceneCount;
                for (int i = 0; i < count; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    string name = scene.name;
                    if (name != null && IsInteriorLoadedAsCloneTemplate(name)) continue;
                    if (subScenes != null && name != null && name.StartsWith(subScenes, StringComparison.Ordinal)) continue;
                    s_VisibleScenes.Add(scene);
                }
            }
            finally
            {
                s_BypassSceneView = previous;
            }
            return s_VisibleScenes;
        }

        // Null when the mod may see the scene's roots as they are. Worked out once a frame per scene.
        private static GameObject[] RootsFor(Scene scene, GameObject[] real)
        {
            string building = RootViewBuilding();
            int frame = Time.frameCount;
            if (frame != s_RootsCacheFrame || building != s_RootsCacheBuilding)
            {
                s_RootsCache.Clear();
                s_RootsCacheFrame = frame;
                s_RootsCacheBuilding = building;
            }

            GameObject[] view;
            if (s_RootsCache.TryGetValue(scene.handle, out view)) return view;

            // Asked before the bypass: with it on, the scene view reads as off and every building as outdoors.
            SeamlessInteriorInstance inside = RootViewInstance();
            long perf = PerfProbe.Begin();
            bool previous = s_BypassSceneView;
            s_BypassSceneView = true;
            try
            {
                view = BuildRootsView(scene, real, inside);
            }
            finally
            {
                s_BypassSceneView = previous;
            }
            PerfProbe.Scanned(PerfProbe.Scan.RootView, real.Length, perf);

            s_RootsCache[scene.handle] = view;
            return view;
        }

        // Inside: the clone's content, the game's SCRIPT_/Skill_ roots and loose roots in the building.
        // Outdoors: everything but the clones. SI's emptied template scenes hold nothing.
        private static GameObject[] BuildRootsView(Scene scene, GameObject[] real, SeamlessInteriorInstance inside)
        {
            string name = scene.name;
            if (name != null && IsInteriorLoadedAsCloneTemplate(name)) return real.Length == 0 ? null : s_NoRoots;

            RefreshObjectViewRoots(inside);
            var roots = new List<GameObject>(real.Length);

            if (inside == null)
            {
                bool removed = false;
                foreach (GameObject root in real)
                {
                    if (root != null && s_ViewCloneRoots.ContainsKey(root.transform.GetInstanceID())) { removed = true; continue; }
                    roots.Add(root);
                }
                return removed ? roots.ToArray() : null;
            }

            string region = inside.Config.ExteriorSceneName;
            if (name != region)
                return name != null && name.StartsWith(region + "_", StringComparison.Ordinal) ? s_NoRoots : null;

            Transform master = inside.MasterInterior.transform;
            for (int i = 0; i < master.childCount; i++) roots.Add(master.GetChild(i).gameObject);

            foreach (GameObject root in real)
            {
                if (root == null) continue;
                int id = root.transform.GetInstanceID();
                if (s_ViewCloneRoots.ContainsKey(id)) continue;
                if (s_ViewSystemRoots.Contains(id) || inside.IsPositionInVolume(root.transform.position)) roots.Add(root);
            }
            return roots.ToArray();
        }
    }
}
