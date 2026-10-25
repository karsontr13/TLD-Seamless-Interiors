using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── THE PARKED ROOM'S OWN SHADOW, KEPT OUT OF THE VIEWS FROM ITS WINDOWS ───
        //
        // A parked room stands 40 m over its building, in the same world and under the same sun, so
        // it casts a shadow on the ground some 30 m away, and the view out of a window looks at
        // that ground. The owner, from inside the Camp Office on 2026-09-18: "it seems I can see the
        // shadow of the interior building on the ground", and again at Safe House A on 2026-09-19
        // when the hour made it plain. Proved both ways that day: the room's casters switched off,
        // the shadow went; switched back, it returned.
        //
        // The room cannot simply stop casting, because its own roof and walls shade it inside. And
        // no layer mask separates its casters from the world's: they sit on nine layers the world
        // shares. But Unity gathers shadow casters PER CAMERA at cull time, so they are silenced
        // in Camera.onPreCull for the window cameras only and put back in onPostRender of the same
        // camera. The player's own view never sees the switch.
        //
        // TWO KINDS OF CASTER, AND THE SECOND BITES. An ordinary object is set to cast nothing. A
        // SHADOWS-ONLY stand-in (Safe House A has 21, 'Shadow_Caster' and the chairs' own) must
        // NOT be: ShadowCastingMode.Off on one of those does not silence it, it makes the rough
        // black mesh VISIBLE. The first test did that and the owner saw "2 chairs ... clipping into
        // themselves with a black copy". Those are switched off whole instead.
        //
        // Two IL2CPP traps, both paid for before (see _dev/portal-diagnostics-console.cs): a lambda
        // will not convert to Camera.CameraCallback, and the Camera handed to the callback is a
        // different wrapper round the same native object, so cameras are compared by instance id.
        private static readonly List<Renderer> s_RoomShadowCasters = new List<Renderer>();
        private static readonly List<UnityEngine.Rendering.ShadowCastingMode> s_RoomShadowCasterModes = new List<UnityEngine.Rendering.ShadowCastingMode>();
        private static readonly List<Renderer> s_RoomShadowStandIns = new List<Renderer>();
        private static Camera.CameraCallback s_RoomShadowPreCull, s_RoomShadowPostRender;
        private static bool s_RoomShadowHooked, s_RoomShadowSilenced, s_RoomShadowFaulted;

        private static void BeginHidingParkedRoomShadow(SeamlessInteriorInstance instance)
        {
            EndHidingParkedRoomShadow();
            if (s_RoomShadowFaulted || instance == null || instance.MasterInterior == null || instance.Config == null || !instance.Config.IsParked) return;

            var renderers = instance.MasterInterior.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                var mode = renderer.shadowCastingMode;
                if (mode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                if (mode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) { if (renderer.enabled) s_RoomShadowStandIns.Add(renderer); continue; }
                s_RoomShadowCasters.Add(renderer);
                s_RoomShadowCasterModes.Add(mode);
            }
            if (s_RoomShadowCasters.Count == 0 && s_RoomShadowStandIns.Count == 0) return;

            if (s_RoomShadowPreCull == null)
            {
                s_RoomShadowPreCull = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnRoomShadowPreCull));
                s_RoomShadowPostRender = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnRoomShadowPostRender));
            }
            Camera.onPreCull += s_RoomShadowPreCull;
            Camera.onPostRender += s_RoomShadowPostRender;
            s_RoomShadowHooked = true;

            if (s_DebugBounds)
                MelonLogger.Msg($"[PARK-GOLGE] {instance.Config.ResolvedInstanceId}: odanin {s_RoomShadowCasters.Count} golge dusuren nesnesi ve {s_RoomShadowStandIns.Count} golge vekili pencere kameralari cizerken susturulacak.",
                                $"[ROOM-SHADOW] {instance.Config.ResolvedInstanceId}: the room's {s_RoomShadowCasters.Count} shadow-casting objects and {s_RoomShadowStandIns.Count} shadow-only stand-ins are silenced while a window camera draws, so the parked room's shadow does not show on the ground outside.");
        }

        private static void EndHidingParkedRoomShadow()
        {
            if (s_RoomShadowHooked)
            {
                Camera.onPreCull -= s_RoomShadowPreCull;
                Camera.onPostRender -= s_RoomShadowPostRender;
                s_RoomShadowHooked = false;
            }
            RestoreParkedRoomShadow();
            s_RoomShadowCasters.Clear();
            s_RoomShadowCasterModes.Clear();
            s_RoomShadowStandIns.Clear();
        }

        private static bool IsWindowPortalCamera(Camera camera)
        {
            // By native object rather than instance id: the callback's Camera is another wrapper round the
            // same native object, and both "!= null" and GetInstanceID are calls that leave garbage.
            if (!IsAliveDirect(camera) || s_WindowPortalCams == null) return false;
            System.IntPtr native = camera.Pointer;
            for (int i = 0; i < s_WindowPortalCams.Length; i++)
                if ((object)s_WindowPortalCams[i] != null && s_WindowPortalCams[i].Pointer == native) return true;
            return false;
        }

        private static void OnRoomShadowPreCull(Camera camera)
        {
            try
            {
                if (s_RoomShadowSilenced || !IsWindowPortalCamera(camera)) return;
                s_RoomShadowSilenced = true;
                for (int i = 0; i < s_RoomShadowCasters.Count; i++)
                    if (IsAliveDirect(s_RoomShadowCasters[i])) SetShadowCastingDirect(s_RoomShadowCasters[i], UnityEngine.Rendering.ShadowCastingMode.Off);
                for (int i = 0; i < s_RoomShadowStandIns.Count; i++)
                    if (IsAliveDirect(s_RoomShadowStandIns[i])) SetRendererEnabledDirect(s_RoomShadowStandIns[i], false);
            }
            catch (System.Exception e) { FaultParkedRoomShadow(e); }
        }

        private static void OnRoomShadowPostRender(Camera camera)
        {
            try
            {
                if (!s_RoomShadowSilenced || !IsWindowPortalCamera(camera)) return;
                RestoreParkedRoomShadow();
            }
            catch (System.Exception e) { FaultParkedRoomShadow(e); }
        }

        // Also called at the top of every portal tick: a camera switched off between its two
        // callbacks must not leave the room without shadows in the player's own view.
        private static void RestoreParkedRoomShadow()
        {
            if (!s_RoomShadowSilenced) return;
            s_RoomShadowSilenced = false;
            for (int i = 0; i < s_RoomShadowCasters.Count; i++)
                if (IsAliveDirect(s_RoomShadowCasters[i])) SetShadowCastingDirect(s_RoomShadowCasters[i], s_RoomShadowCasterModes[i]);
            for (int i = 0; i < s_RoomShadowStandIns.Count; i++)
                if (IsAliveDirect(s_RoomShadowStandIns[i])) SetRendererEnabledDirect(s_RoomShadowStandIns[i], true);
        }

        // THE SWITCHES ARE CALLED STRAIGHT INTO THE ENGINE. Through the usual property setters each switch
        // left about 32 bytes of the game's garbage, and the room's 1,320 casters and 102 stand-ins at the
        // hangar are switched off and back every frame a window view draws: about 80 KB a frame, a garbage
        // collection every 5 s and a skipped frame with each, seen as a jump (owner, 2026-10-24). The
        // engine's own functions, resolved once, leave none (measured: 0 KB for the same 50 rounds).
        // If either cannot be resolved, the setters are used as before.
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void RendererIntSetter(System.IntPtr renderer, int value);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void RendererBoolSetter(System.IntPtr renderer, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.U1)] bool value);
        private static RendererIntSetter s_SetShadowCastingNative;
        private static RendererBoolSetter s_SetRendererEnabledNative;
        private static bool s_RendererSettersResolved;

        private static void ResolveRendererSetters()
        {
            if (s_RendererSettersResolved) return;
            s_RendererSettersResolved = true;
            try { s_SetShadowCastingNative = Il2CppInterop.Runtime.IL2CPP.ResolveICall<RendererIntSetter>("UnityEngine.Renderer::set_shadowCastingMode"); } catch { s_SetShadowCastingNative = null; }
            try { s_SetRendererEnabledNative = Il2CppInterop.Runtime.IL2CPP.ResolveICall<RendererBoolSetter>("UnityEngine.Renderer::set_enabled"); } catch { s_SetRendererEnabledNative = null; }
            if (s_DebugBounds)
                MelonLogger.Msg($"[ROOM-SHADOW] Direct engine switches: shadow casting {(s_SetShadowCastingNative != null ? "found" : "not found, setter used")}, renderer on/off {(s_SetRendererEnabledNative != null ? "found" : "not found, setter used")}.");
        }

        // A Unity object's "!= null" is a call into the game too, and left about 30 bytes of garbage each:
        // the same again as the switches it guards (2026-10-24). The game's own test is whether the object
        // still holds its native object (m_CachedPtr), which is read here straight from memory. Checked
        // against Unity's answer on all 1,320 casters, on a new object and on a destroyed one: all agree.
        private static int s_CachedPtrOffset = -2;

        private static bool IsAliveDirect(Object obj)
        {
            if ((object)obj == null) return false;
            if (s_CachedPtrOffset == -2)
            {
                s_CachedPtrOffset = -1;
                try
                {
                    var field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(Il2CppInterop.Runtime.Il2CppClassPointerStore<Object>.NativeClassPtr, "m_CachedPtr");
                    if (field != System.IntPtr.Zero) s_CachedPtrOffset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
                }
                catch { s_CachedPtrOffset = -1; }
            }
            if (s_CachedPtrOffset <= 0) return obj != null;
            return System.Runtime.InteropServices.Marshal.ReadIntPtr(obj.Pointer, s_CachedPtrOffset) != System.IntPtr.Zero;
        }

        private static void SetShadowCastingDirect(Renderer renderer, UnityEngine.Rendering.ShadowCastingMode mode)
        {
            ResolveRendererSetters();
            if (s_SetShadowCastingNative != null) s_SetShadowCastingNative(renderer.Pointer, (int)mode);
            else renderer.shadowCastingMode = mode;
        }

        private static void SetRendererEnabledDirect(Renderer renderer, bool on)
        {
            ResolveRendererSetters();
            if (s_SetRendererEnabledNative != null) s_SetRendererEnabledNative(renderer.Pointer, on);
            else renderer.enabled = on;
        }

        // Off first, said once, and never tried again this session: a callback that throws does
        // so for every camera of every frame.
        private static void FaultParkedRoomShadow(System.Exception e)
        {
            if (s_RoomShadowFaulted) return;
            s_RoomShadowFaulted = true;
            try { EndHidingParkedRoomShadow(); } catch { }
            MelonLogger.Warning("[ROOM-SHADOW] Hiding the parked room's shadow failed and is switched off until the game is restarted: " + e.Message);
        }
    }
}
