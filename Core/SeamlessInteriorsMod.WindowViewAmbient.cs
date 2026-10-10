using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── THE VIEW OUT OF A WINDOW IS LIT BY THE OUTDOORS, IN EITHER INTERIOR LIGHTING MODE ───
        //
        // Dark Atmosphere puts the room's own ambient light in force for the whole game while the
        // player is inside (ClonedInteriorLightingGuard, LightingPatches.cs), because ambient light
        // is one global value. The cameras that look out of the room's windows render the outdoors
        // in the same frame, so they drew it under the room's dark ambient. Owner, in Thompson's
        // Store on 2026-10-10 with Dark Atmosphere on: "the outside world doesn't seem as bright as
        // it should be either even with the windows cleaned", and the rule: "The lighting switch is
        // for the INDOOR SPACE. It should not be affecting the inside-out cameras at all".
        //
        // So for a window camera only, the outdoor ambient is put in force in onPreCull and the
        // room's is put back in onPostRender of the same camera: the pattern of ParkedRoomShadow.cs,
        // which also explains the two IL2CPP traps. The guard knows what the outdoor ambient is and
        // does nothing in Outdoor Lighting, where nothing was swapped to begin with.

        private static Camera.CameraCallback s_ViewAmbientPreCull;
        private static Camera.CameraCallback s_ViewAmbientPostRender;
        private static bool s_ViewAmbientHooked;
        private static bool s_ViewAmbientFaulted;

        private static void BeginWindowViewAmbient()
        {
            EndWindowViewAmbient();
            if (s_ViewAmbientFaulted) return;

            if (s_ViewAmbientPreCull == null)
            {
                s_ViewAmbientPreCull = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnViewAmbientPreCull));
                s_ViewAmbientPostRender = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnViewAmbientPostRender));
            }
            Camera.onPreCull += s_ViewAmbientPreCull;
            Camera.onPostRender += s_ViewAmbientPostRender;
            s_ViewAmbientHooked = true;
        }

        private static void EndWindowViewAmbient()
        {
            if (s_ViewAmbientHooked)
            {
                Camera.onPreCull -= s_ViewAmbientPreCull;
                Camera.onPostRender -= s_ViewAmbientPostRender;
                s_ViewAmbientHooked = false;
            }
            RestoreWindowViewAmbient();
        }

        private static void OnViewAmbientPreCull(Camera camera)
        {
            try
            {
                if (!IsWindowPortalCamera(camera)) return;
                ClonedInteriorLightingGuard.BeginExteriorAmbientForViewOut();
            }
            catch (System.Exception e) { FaultWindowViewAmbient(e); }
        }

        private static void OnViewAmbientPostRender(Camera camera)
        {
            try
            {
                if (!IsWindowPortalCamera(camera)) return;
                ClonedInteriorLightingGuard.EndExteriorAmbientForViewOut();
            }
            catch (System.Exception e) { FaultWindowViewAmbient(e); }
        }

        // Also called at the top of every portal tick: a camera switched off between its two
        // callbacks must not leave the room on the outdoor ambient in the player's own view.
        private static void RestoreWindowViewAmbient()
        {
            ClonedInteriorLightingGuard.EndExteriorAmbientForViewOut();
        }

        // Off first, said once, and never tried again this session: a callback that throws does
        // so for every camera of every frame.
        private static void FaultWindowViewAmbient(System.Exception e)
        {
            if (s_ViewAmbientFaulted) return;
            s_ViewAmbientFaulted = true;
            try { EndWindowViewAmbient(); } catch { }
            MelonLogger.Warning("[PENCERE-ISIK] Pencere gorunumlerine dis ortam isigi verilemedi; oyun yeniden baslatilana kadar kapali: " + e.Message,
                                "[WINDOW-AMBIENT] Giving the window views the outdoor ambient light failed and is switched off until the game is restarted: " + e.Message);
        }
    }
}
