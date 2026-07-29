using Il2Cpp;
using Il2CppTLD.Audio;
using MelonLoader;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Toggles heavy audio occlusion on/off depending on whether the player
        // is inside the cloned interior. This is what makes outdoor wind/ambience
        // sound muffled while indoors, matching the game's normal indoor audio feel.
        public static void SetAudioOcclusion(bool occlude)
        {
            if (GameAudioManager.Instance == null) return;

            if (occlude && !s_IsAudioOccluded)
            {
                GameAudioManager.Instance.EnterOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                s_IsAudioOccluded = true;
                if (s_DebugBounds) MelonLogger.Msg("[AUDIO] Audio Occlusion ENABLED.");
            }
            else if (!occlude && s_IsAudioOccluded)
            {
                GameAudioManager.Instance.ExitOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                s_IsAudioOccluded = false;
                if (s_DebugBounds) MelonLogger.Msg("[AUDIO] Audio Occlusion DISABLED.");
            }
        }
    }
}