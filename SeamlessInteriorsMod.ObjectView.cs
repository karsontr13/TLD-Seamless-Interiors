using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── OBJECT VIEW ───
        // Mods searching with FindObjectsOfType/FindObjectOfType find what a vanilla scene holds: inside a
        // building its clone and the game's systems, outdoors none of the buildings. SI and the game see all.

        private const string FIND_ICALL = "UnityEngine.Object::FindObjectsOfType(System.Type,System.Boolean)";
        private const int ARRAY_DATA_OFFSET = 0x20;
        private const int OBJECT_VIEW_LOG_LIMIT = 30;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr FindObjectsIcall(IntPtr type, byte includeInactive);

        private static FindObjectsIcall s_FindOriginal;
        private static FindObjectsIcall s_FindView;
        private static bool s_ObjectViewFailed;
        private static bool s_ReportedObjectView;
        private static int s_ObjectViewLogs;
        private static Il2CppReferenceArray<UnityEngine.Object> s_LastObjectView;
        private static readonly Dictionary<Assembly, bool> s_PlumbingAssemblies = new Dictionary<Assembly, bool>();

        private static int s_ViewRootsFrame = -1;
        private static readonly Dictionary<int, string> s_ViewCloneRoots = new Dictionary<int, string>();
        private static readonly HashSet<int> s_ViewRegionScenes = new HashSet<int>();
        private static readonly HashSet<int> s_ViewSystemRoots = new HashSet<int>();
        private static string s_ViewSystemRootsRegion;
        private static int s_ViewSystemRootsScene;

        // Every FindObjectsOfType and FindObjectOfType call, generic or not, reads the engine function from one
        // static slot; pointing it here needs no code patch and leaves FindObjectsByType, which the game uses, alone.
        internal static unsafe void InstallObjectView()
        {
            try
            {
                IntPtr slot = FindIcallSlot();
                if (slot == IntPtr.Zero) throw new InvalidOperationException("FindObjectsOfType'in motor adresi bulunamadi (oyun surumu degismis olabilir).");

                IntPtr original = *(IntPtr*)slot;
                if (original == IntPtr.Zero) original = IL2CPP.il2cpp_resolve_icall(FIND_ICALL);
                if (original == IntPtr.Zero) throw new InvalidOperationException(FIND_ICALL + " cozulemedi.");

                s_FindOriginal = Marshal.GetDelegateForFunctionPointer<FindObjectsIcall>(original);
                s_FindView = FindObjectsView;
                *(IntPtr*)slot = Marshal.GetFunctionPointerForDelegate(s_FindView);
                MelonLogger.Msg("[NESNE-GORUNUMU] Kuruldu: modlar FindObjectsOfType ile yalnizca bulunduklari yerin nesnelerini buluyor.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[NESNE-GORUNUMU] Kurulamadi: " + (ex.InnerException ?? ex).Message);
            }
        }

        // The non-generic wrapper loads the slot (mov rax,[rip+slot]) and names the icall it resolves (lea rcx,[rip+name]).
        private static unsafe IntPtr FindIcallSlot()
        {
            FieldInfo field = null;
            foreach (FieldInfo f in typeof(UnityEngine.Object).GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (f.Name.StartsWith("NativeMethodInfoPtr_FindObjectsOfType_") && f.Name.EndsWith("_Type_Boolean_0")) { field = f; break; }
            }
            if (field == null) return IntPtr.Zero;

            IntPtr methodInfo = (IntPtr)field.GetValue(null);
            byte* code = methodInfo == IntPtr.Zero ? null : *(byte**)methodInfo;
            if (code == null) return IntPtr.Zero;

            IntPtr slot = IntPtr.Zero;
            bool named = false;
            for (int i = 0; i < 0x60; i++)
            {
                if (code[i] != 0x48) continue;
                IntPtr target = (IntPtr)(code + i + 7 + *(int*)(code + i + 3));
                if (code[i + 1] == 0x8B && code[i + 2] == 0x05 && slot == IntPtr.Zero) slot = target;
                else if (code[i + 1] == 0x8D && code[i + 2] == 0x0D && !named) named = Marshal.PtrToStringAnsi(target) == FIND_ICALL;
            }
            return named ? slot : IntPtr.Zero;
        }

        // Called by native code: nothing may throw out of here.
        private static IntPtr FindObjectsView(IntPtr type, byte includeInactive)
        {
            IntPtr found = s_FindOriginal(type, includeInactive);
            if (found == IntPtr.Zero || s_ObjectViewFailed || s_BypassSceneView || ActiveInteriors.Count == 0) return found;

            bool previous = s_BypassSceneView;
            s_BypassSceneView = true;
            try
            {
                return ObjectViewOf(type, found);
            }
            catch (Exception ex)
            {
                s_ObjectViewFailed = true;
                MelonLogger.Warning("[NESNE-GORUNUMU] Kapatildi, modlar tum nesneleri buluyor: " + ex.Message);
                return found;
            }
            finally
            {
                s_BypassSceneView = previous;
            }
        }

        private static unsafe IntPtr ObjectViewOf(IntPtr type, IntPtr found)
        {
            int count = (int)IL2CPP.il2cpp_array_length(found);
            if (count == 0) return found;

            // Only scene objects have a place; other results (assets, settings) are the same everywhere.
            IntPtr klass = IL2CPP.il2cpp_class_from_system_type(type);
            bool components = klass != IntPtr.Zero && IL2CPP.il2cpp_class_is_assignable_from(Il2CppClassPointerStore<Component>.NativeClassPtr, klass);
            bool gameObjects = !components && klass == Il2CppClassPointerStore<GameObject>.NativeClassPtr;
            if (!components && !gameObjects) return found;

            // Every search from anywhere pays this stack walk, including the mod's own.
            // Measured apart from the filtering below: the two have very different fixes.
            long perf = PerfProbe.Begin();
            string caller = ModCaller();
            PerfProbe.Scanned(PerfProbe.Scan.ViewCaller, count, perf);
            if (caller == null) return found;

            string building = ViewBuilding();
            SeamlessInteriorInstance inside = null;
            if (building != null && (!ActiveInteriors.TryGetValue(building, out inside) || inside == null)) return found;

            perf = PerfProbe.Begin();
            RefreshObjectViewRoots(inside);

            IntPtr* items = (IntPtr*)((byte*)found + ARRAY_DATA_OFFSET);
            var kept = new List<IntPtr>(count);
            for (int i = 0; i < count; i++)
            {
                IntPtr item = items[i];
                if (item == IntPtr.Zero) { kept.Add(item); continue; }
                Transform t = components ? new Component(item).transform : new GameObject(item).transform;
                if (t == null || IsVisibleToMods(t, building, inside)) kept.Add(item);
            }
            PerfProbe.Scanned(PerfProbe.Scan.ViewFilter, count, perf);
            if (kept.Count == count) return found;

            if (!s_ReportedObjectView || (s_DebugBounds && s_ObjectViewLogs < OBJECT_VIEW_LOG_LIMIT))
            {
                s_ReportedObjectView = true;
                s_ObjectViewLogs++;
                string where = building != null ? building + " icinde" : "disarida";
                string typeName = new Il2CppSystem.Type(type).Name;
                MelonLogger.Msg($"[NESNE-GORUNUMU] {caller} {where} {typeName} aradi: {count} nesneden {kept.Count} tanesi gosterildi.");
            }

            var view = new Il2CppReferenceArray<UnityEngine.Object>(kept.Count);
            IntPtr* viewItems = (IntPtr*)((byte*)view.Pointer + ARRAY_DATA_OFFSET);
            for (int i = 0; i < kept.Count; i++) viewItems[i] = kept[i];

            // Held until the next search so the array outlives the caller's copy.
            s_LastObjectView = view;
            return view.Pointer;
        }

        // Inside: the clone, the game's systems, anything outside the region's scenes, and items dropped in
        // the building but not yet moved under its clone. Outdoors: everything but the clones.
        private static bool IsVisibleToMods(Transform t, string building, SeamlessInteriorInstance inside)
        {
            Transform root = t.root;
            int rootId = root.GetInstanceID();

            string owner;
            if (s_ViewCloneRoots.TryGetValue(rootId, out owner)) return owner == building;
            if (inside == null) return true;
            if (s_ViewSystemRoots.Contains(rootId)) return true;
            if (!s_ViewRegionScenes.Contains(t.gameObject.scene.handle)) return true;
            return inside.IsPositionInVolume(root.position);
        }

        private static void RefreshObjectViewRoots(SeamlessInteriorInstance inside)
        {
            int frame = Time.frameCount;
            if (frame != s_ViewRootsFrame)
            {
                s_ViewRootsFrame = frame;
                s_ViewCloneRoots.Clear();
                foreach (var pair in ActiveInteriors)
                    if (pair.Value != null && pair.Value.MasterInterior != null)
                        s_ViewCloneRoots[pair.Value.MasterInterior.transform.GetInstanceID()] = pair.Key;
            }

            if (inside == null) return;

            // The region and its sub-scenes (_SANDBOX, _WILDLIFE, _DLC01...); names are read past the scene view.
            string region = inside.Config.ExteriorSceneName;
            s_ViewRegionScenes.Clear();
            UnityEngine.SceneManagement.Scene regionScene = default(UnityEngine.SceneManagement.Scene);
            int sceneCount = RealSceneCount();
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = RealSceneAt(i);
                string name = RealSceneName(scene);
                if (name != region && (name == null || !name.StartsWith(region + "_"))) continue;
                s_ViewRegionScenes.Add(scene.handle);
                if (name == region) regionScene = scene;
            }

            // Every vanilla scene carries its own SCRIPT_ and Skill_ roots, so an interior has them as well.
            if (!regionScene.IsValid() || (region == s_ViewSystemRootsRegion && regionScene.handle == s_ViewSystemRootsScene)) return;
            s_ViewSystemRootsRegion = region;
            s_ViewSystemRootsScene = regionScene.handle;
            s_ViewSystemRoots.Clear();
            foreach (GameObject root in RealRootObjects(regionScene))
            {
                string name = root.name;
                if (name.StartsWith("SCRIPT_") || name.StartsWith("Skill_")) s_ViewSystemRoots.Add(root.transform.GetInstanceID());
            }
        }

        // The mod whose code made the search: the first frame past this hook outside Unity, the interop layer
        // and the runtime. Null for SI and for native callers, which get everything.
        private static string ModCaller()
        {
            return ModCaller(nameof(FindObjectsView), false);
        }

        // Same walk past any of SI's view hooks; with toolsSeeAll, debugging tools get the real world too.
        private static string ModCaller(string hook, bool toolsSeeAll)
        {
            var trace = new StackTrace(false);
            bool pastHook = false;
            for (int i = 0; i < trace.FrameCount; i++)
            {
                MethodBase method = trace.GetFrame(i).GetMethod();
                Type declaring = method != null ? method.DeclaringType : null;
                if (!pastHook)
                {
                    pastHook = declaring == typeof(SeamlessInteriorsMod) && method.Name == hook;
                    continue;
                }
                if (declaring == null) continue;

                Assembly assembly = declaring.Assembly;
                if (assembly == typeof(SeamlessInteriorsMod).Assembly || ModExceptions.SeesWholeRegion(assembly)) return null;
                if (toolsSeeAll && ModExceptions.IsDebugTool(assembly)) return null;
                if (!IsPlumbing(assembly)) return assembly.GetName().Name;
            }
            return null;
        }

        private static bool IsPlumbing(Assembly assembly)
        {
            bool plumbing;
            if (s_PlumbingAssemblies.TryGetValue(assembly, out plumbing)) return plumbing;

            string name = assembly.GetName().Name;
            plumbing = name.StartsWith("UnityEngine") || name.StartsWith("Unity.") || name.StartsWith("Il2Cpp")
                || name == "Assembly-CSharp" || name == "Assembly-CSharp-firstpass"
                || name.StartsWith("System") || name == "mscorlib" || name == "netstandard"
                || name.StartsWith("MonoMod") || name == "0Harmony" || name == "MelonLoader";
            s_PlumbingAssemblies[assembly] = plumbing;
            return plumbing;
        }
    }
}
