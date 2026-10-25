using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── AN OPEN WINDOW LETS THE OUTDOORS IN ───
        //
        // Owner, 2026-10-18. While windows stand open the room's air goes toward the outdoor air, fast:
        // with one window open it is half way there in about ten game minutes, and each further window
        // halves that time again. Shut, it comes back the same way. It is the room's own indoor space
        // that carries it (m_TemperatureDeltaCelsius, which the game adds to the indoor temperature), so
        // every system that asks the game for the temperature sees it, mods included.
        //
        // And near an open window the wind outside is heard as outside: the game's outdoor wind loop
        // plays on a sound object at the nearest open window, loud beside it and gone a few metres in.

        private const float OPEN_AIR_HALF_MINUTES = 10f;
        // Wide and eased: 0.8 to 4 m in a straight line and a 0.4 s stop were heard as abrupt (owner, 2026-10-18).
        private const float OPEN_AIR_SOUND_FULL_WITHIN = 0.5f;   // metres from the glass
        private const float OPEN_AIR_SOUND_GONE_AT = 6f;
        private const float OPEN_AIR_SOUND_FADE_MS = 1500f;
        // The level follows its target with this time constant, so steps and the first start glide.
        private const float OPEN_AIR_SOUND_GLIDE_SECONDS = 0.35f;

        private static IndoorSpaceTrigger s_OpenAirSpace;
        private static float s_OpenAirDelta;
        private static bool s_OpenAirFailed;

        private static GameObject s_OpenAirEmitter;
        private static uint s_OpenAirWindId;
        private static float s_OpenAirGain = -1f;

        // Every frame from OnUpdate, after the sashes.
        private static void TickOpenWindowAir()
        {
            if (s_OpenAirFailed) return;
            try
            {
                int open = 0;
                Vector3 nearest = Vector3.zero;
                float nearestDist = float.MaxValue;
                Camera main = GameManager.GetMainCamera();
                Vector3 eye = main != null ? main.transform.position : Vector3.zero;
                foreach (var sash in s_WindowSashes)
                {
                    if (!IsRoomWindowOpen(s_WindowPortalInstanceId, StateOf(sash))) continue;
                    open++;
                    var opening = s_WindowPortalOpenings[sash.Opening];
                    float d = Vector3.Distance(eye, opening.GlassCenter);
                    if (d < nearestDist) { nearestDist = d; nearest = opening.GlassCenter; }
                }

                TickOpenWindowCooling(open);
                TickOpenWindowSound(open > 0 && main != null, nearest, nearestDist);
                TickWindowEar(main);
            }
            catch (System.Exception ex)
            {
                s_OpenAirFailed = true;
                try { ResetOpenWindowAir(); } catch { }
                MelonLogger.Warning($"[ACIK-PENCERE] Kapatildi: {ex.Message}", $"[OPEN-WINDOW] Turned off after an error: {ex.Message}");
            }
        }

        private static void TickOpenWindowCooling(int open)
        {
            // Follows the space the player is in; another space starts again from no change.
            IndoorSpaceTrigger space = s_EnteredSpaceId != null && s_EnteredSpaceId == s_WindowPortalInstanceId ? s_EnteredSpace : null;
            if (space != s_OpenAirSpace)
            {
                if (s_OpenAirSpace != null) s_OpenAirSpace.m_TemperatureDeltaCelsius = 0f;
                s_OpenAirSpace = space;
                s_OpenAirDelta = 0f;
            }
            if (space == null) return;

            var weather = GameManager.GetWeatherComponent();
            var tod = GameManager.GetTimeOfDayComponent();
            if (weather == null || tod == null) return;

            // The room as it would be with every window shut, and the air outside it.
            float room = weather.m_CurrentTemperatureWithoutHeatSources - s_OpenAirDelta;
            float outdoor = weather.m_BaseTemperature - weather.m_CurrentBlizzardDegreesDrop;
            float target = open > 0 ? outdoor - room : 0f;

            // Game minutes, so passing time and sleeping count; each open window halves the half-life.
            float minutes = tod.GetTODMinutes(Time.deltaTime);
            if (minutes <= 0f) return;
            float half = OPEN_AIR_HALF_MINUTES / Mathf.Max(open, 1);
            s_OpenAirDelta = target + (s_OpenAirDelta - target) * Mathf.Pow(0.5f, minutes / half);
            if (open == 0 && Mathf.Abs(s_OpenAirDelta) < 0.01f) s_OpenAirDelta = 0f;
            space.m_TemperatureDeltaCelsius = s_OpenAirDelta;
        }

        private static void TickOpenWindowSound(bool wanted, Vector3 window, float distance)
        {
            if (!wanted) { StopOpenWindowSound(); return; }

            if (s_OpenAirEmitter == null)
            {
                s_OpenAirEmitter = new GameObject("SI_OpenWindowWind");
                Object.DontDestroyOnLoad(s_OpenAirEmitter);
                AkSoundEngine.RegisterGameObj(s_OpenAirEmitter, s_OpenAirEmitter.name);
                s_OpenAirGain = -1f;
            }
            s_OpenAirEmitter.transform.position = window;

            // Heard only by the listener, at a level set by the distance to the glass, eased at both ends.
            float far = Mathf.Clamp01((distance - OPEN_AIR_SOUND_FULL_WITHIN) / (OPEN_AIR_SOUND_GONE_AT - OPEN_AIR_SOUND_FULL_WITHIN));
            float target = 1f - far * far * (3f - 2f * far);

            bool starting = s_OpenAirWindId == 0 || AkSoundEngine.GetEventIDFromPlayingID(s_OpenAirWindId) != OutdoorWindEvent;
            if (starting) s_OpenAirGain = 0f;   // a new loop rises from silence
            float gain = s_OpenAirGain < 0f ? target : Mathf.Lerp(s_OpenAirGain, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / OPEN_AIR_SOUND_GLIDE_SECONDS));
            Camera main = GameManager.GetMainCamera();
            var listener = main != null ? main.GetComponent<AkAudioListener>() : null;
            if (listener != null && (starting || Mathf.Abs(gain - s_OpenAirGain) > 0.002f))
                AkSoundEngine.SetGameObjectOutputBusVolume(s_OpenAirEmitter, listener.gameObject, gain);
            s_OpenAirGain = gain;

            if (starting) s_OpenAirWindId = AkSoundEngine.PostEvent(OutdoorWindEvent, s_OpenAirEmitter);
        }

        private static void StopOpenWindowSound()
        {
            if (s_OpenAirWindId != 0)
            {
                AkSoundEngine.StopPlayingID(s_OpenAirWindId, (int)OPEN_AIR_SOUND_FADE_MS, AkCurveInterpolation.AkCurveInterpolation_Linear);
                s_OpenAirWindId = 0;
            }
        }

        // ─── ANIMALS OUTSIDE ARE HEARD AT THE WINDOW ───
        //
        // The listener is on the player's camera, in the parked room 80 m over the building, so wolves
        // howling beside the window were all but silent (owner, 2026-10-18). A second listener stands
        // where the nearest window's view out is taken from, and the animals' sound objects are heard
        // by it too: fully beside an open window, much less through shut glass (owner), none far in.
        private const float WINDOW_EAR_SHUT_GAIN = 0.25f;
        private const float WINDOW_EAR_SCAN_SECONDS = 1f;
        private static GameObject s_WindowEar;
        private static readonly System.Collections.Generic.List<GameObject> s_WindowEarEmitters = new System.Collections.Generic.List<GameObject>();
        private static float s_WindowEarGain = -1f;
        private static float s_WindowEarNextScan;

        private static void TickWindowEar(Camera main)
        {
            if (!s_IsPlayerInsideClone || main == null || s_WindowPortalOpenings.Count == 0) { DropWindowEar(); return; }

            Vector3 eye = main.transform.position;
            int nearest = -1; float nearestDist = float.MaxValue;
            for (int i = 0; i < s_WindowPortalOpenings.Count && i < s_WindowPortalDelta.Count; i++)
            {
                float d = Vector3.Distance(eye, s_WindowPortalOpenings[i].GlassCenter);
                if (d < nearestDist) { nearestDist = d; nearest = i; }
            }
            if (nearest < 0) { DropWindowEar(); return; }

            if (s_WindowEar == null)
            {
                s_WindowEar = new GameObject("SI_WindowEar");
                Object.DontDestroyOnLoad(s_WindowEar);
                AkSoundEngine.RegisterGameObj(s_WindowEar, s_WindowEar.name);
                s_WindowEarGain = -1f;
                s_WindowEarNextScan = 0f;
            }
            AkSoundEngine.SetObjectPosition(s_WindowEar, eye + s_WindowPortalDelta[nearest], main.transform.forward, main.transform.up);

            // How open that window is, eased with its sash, and how near the player stands to it.
            var sash = SashOf(nearest);
            float open = sash != null ? Mathf.Clamp01(sash.Shown) : 0f;
            float far = Mathf.Clamp01((nearestDist - OPEN_AIR_SOUND_FULL_WITHIN) / (OPEN_AIR_SOUND_GONE_AT - OPEN_AIR_SOUND_FULL_WITHIN));
            float target = (1f - far * far * (3f - 2f * far)) * Mathf.Lerp(WINDOW_EAR_SHUT_GAIN, 1f, open);
            float gain = s_WindowEarGain < 0f ? target : Mathf.Lerp(s_WindowEarGain, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / OPEN_AIR_SOUND_GLIDE_SECONDS));

            bool added = false;
            if (Time.unscaledTime >= s_WindowEarNextScan)
            {
                s_WindowEarNextScan = Time.unscaledTime + WINDOW_EAR_SCAN_SECONDS;
                s_WindowEarEmitters.RemoveAll(go => go == null);
                added = ListenToAnimalsAtWindow();
            }
            if (added || s_WindowEarGain < 0f || Mathf.Abs(gain - s_WindowEarGain) > 0.002f)
                foreach (var go in s_WindowEarEmitters)
                    if (go != null) AkSoundEngine.SetGameObjectOutputBusVolume(go, s_WindowEar, gain);
            s_WindowEarGain = gain;
        }

        // Every animal's sound objects not yet heard at the window are added to it; true when one was.
        // From the game's own list of animals: a search of every object in the scene once a second was a
        // hitch a second inside any room with windows (owner, 2026-10-24, the hangar).
        private static bool ListenToAnimalsAtWindow()
        {
            bool added = false;
            var animals = BaseAiManager.m_BaseAis;
            for (int a = 0; animals != null && a < animals.Count; a++)
            {
                var ai = animals[a];
                if (ai == null) continue;
                // An animal's calls play on its sound child (Audio_WildlifeWolf), not on itself.
                foreach (var ak in ai.GetComponentsInChildren<AkGameObj>(true))
                {
                    var go = ak.gameObject;
                    if (s_WindowEarEmitters.Contains(go)) continue;
                    AkSoundEngine.AddListener(go, s_WindowEar);
                    s_WindowEarEmitters.Add(go);
                    added = true;
                }
            }
            return added;
        }

        private static void DropWindowEar()
        {
            if (s_WindowEar == null) return;
            foreach (var go in s_WindowEarEmitters)
                if (go != null) AkSoundEngine.RemoveListener(go, s_WindowEar);
            s_WindowEarEmitters.Clear();
            AkSoundEngine.UnregisterGameObj(s_WindowEar);
            Object.Destroy(s_WindowEar);
            s_WindowEar = null;
            s_WindowEarGain = -1f;
        }

        private static void ResetOpenWindowAir()
        {
            DropWindowEar();
            StopOpenWindowSound();
            if (s_OpenAirSpace != null) s_OpenAirSpace.m_TemperatureDeltaCelsius = 0f;
            s_OpenAirSpace = null;
            s_OpenAirDelta = 0f;
        }
    }
}
