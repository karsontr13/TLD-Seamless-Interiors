using Il2Cpp;
using Il2CppTLD.Audio;
using MelonLoader;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        public static void SetAudioOcclusion(SeamlessInteriorInstance instance, bool occlude)
        {
            if (GameAudioManager.Instance == null) return;

            if (occlude && !instance.IsAudioOccluded)
            {
                GameAudioManager.Instance.EnterOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                instance.IsAudioOccluded = true;
                if (s_DebugBounds) MelonLogger.Msg($"[AUDIO] {instance.Config.InteriorSceneBaseName} Audio Occlusion ENABLED.");
            }
            else if (!occlude && instance.IsAudioOccluded)
            {
                GameAudioManager.Instance.ExitOcclusionTrigger(Il2Cpp.AudioOcclusionLevel.HeavyOcclusion);
                instance.IsAudioOccluded = false;
                if (s_DebugBounds) MelonLogger.Msg($"[AUDIO] {instance.Config.InteriorSceneBaseName} Audio Occlusion DISABLED.");
            }
        }
    }
}