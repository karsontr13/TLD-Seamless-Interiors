using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── THE DOOR'S OWN FRAME IS LIT FOR THE SIDE THE PLAYER IS GOING TO ───
        //
        // The game sets RenderSettings.ambientLight and ambientProbe for indoors or outdoors, and does
        // it for the door's frame from where the player stood when the frame began, rewriting both
        // later in that frame; the right side's values arrive the frame after. With Interior Lighting
        // Mode on Outdoor Lighting the two sides are close and nothing shows. On Dark Atmosphere the
        // indoor ambient is five times darker (Lake Cabin A at 17:00: 0.078 against 0.38 outside, probe
        // 0.005 against 0.075), so the first frame outside had black grass and the first frame inside
        // was more than twice as bright as the next (owner, 2026-09-28, and frame captures, LOG
        // 0928-07). Set at the door, the values were overwritten before the frame was drawn; set in
        // Camera.onPreCull of that frame they held, and the first frame matched the ones after it.
        //
        // The values are the game's own, remembered at the door from the side being left, where they
        // are right: outdoors as it was on the way in, each room as it was on the way out. Outdoors
        // changes slowly (0.383 on the way in, 0.384 on the way out a minute later) and the game puts
        // the live value back the next frame. A room not yet left this session has no remembered
        // probe; its entry sets the room's authored ambient only.
        private struct DoorLighting
        {
            public Color Ambient;
            public SphericalHarmonicsL2 Probe;
            public bool HasProbe;
        }

        private static DoorLighting s_OutdoorLighting;
        private static bool s_HaveOutdoorLighting;
        private static readonly Dictionary<string, DoorLighting> s_RoomLighting = new Dictionary<string, DoorLighting>();

        private static DoorLighting s_DoorLightingWanted;
        private static int s_DoorLightingFrame = -1;
        private static Camera.CameraCallback s_DoorLightingPreCull;

        // Before the door does anything: the lighting in force belongs to the side being left.
        internal static void RememberLightingBeforeDoor()
        {
            var here = new DoorLighting { Ambient = RenderSettings.ambientLight, Probe = RenderSettings.ambientProbe, HasProbe = true };
            string room = PlayerInteriorId;
            if (room == null) { s_OutdoorLighting = here; s_HaveOutdoorLighting = true; }
            else s_RoomLighting[room] = here;
        }

        // After the door: this frame is drawn with the destination's lighting.
        internal static void LightDoorFrameForArrival(SeamlessInteriorInstance instance, bool entering)
        {
            if (instance == null || instance.Config == null || !instance.Config.IsParked) return;

            DoorLighting wanted;
            if (!entering)
            {
                if (!s_HaveOutdoorLighting) return;
                wanted = s_OutdoorLighting;
            }
            else if (!s_RoomLighting.TryGetValue(instance.Config.ResolvedInstanceId ?? "", out wanted))
            {
                var tod = FindRoomAmbient(instance);
                var time = GameManager.GetTimeOfDayComponent();
                if (tod == null || time == null) return;
                wanted = new DoorLighting { Ambient = time.IsDay() ? tod.m_AmbientIndoorsDay : tod.m_AmbientIndoorsNight, HasProbe = false };
            }

            s_DoorLightingWanted = wanted;
            s_DoorLightingFrame = Time.frameCount;
            if (s_DoorLightingPreCull == null)
            {
                s_DoorLightingPreCull = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnDoorLightingPreCull));
                Camera.onPreCull += s_DoorLightingPreCull;
            }
        }

        private static void OnDoorLightingPreCull(Camera cam)
        {
            if (s_DoorLightingFrame != Time.frameCount) return;
            RenderSettings.ambientLight = s_DoorLightingWanted.Ambient;
            if (s_DoorLightingWanted.HasProbe) RenderSettings.ambientProbe = s_DoorLightingWanted.Probe;
        }
    }
}
