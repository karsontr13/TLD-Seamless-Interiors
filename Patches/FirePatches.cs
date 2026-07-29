using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    // Intercepts the base game's fire-state deserialization so we can grab a copy of the
    // raw save data before the game applies it. We only steal it outside of our own cloning
    // routine (to avoid capturing/interfering with our own restore pass), and stash it for
    // DelayedFireRestore (see SaveData.cs) to apply once the cloned fires/stoves/campfires
    // actually exist and are registered with FireManager.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.FireManager), nameof(Il2Cpp.FireManager.Deserialize))]
    public class FireManagerStealerPatch
    {
        public static string s_StolenFireData = "";

        public static void Prefix(string text)
        {
            if (!string.IsNullOrEmpty(text) && !SeamlessInteriorsMod.s_IsCloningRoutineActive)
            {
                s_StolenFireData = text;

                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg($"[FIRE-STEAL] Ates datasi basariyla kopyalandi ({text.Length} karakter).");
            }
        }
    }

    // While s_ProtectInterior is set (during the fire-data restore window), blocks any
    // UnityEngine.Object.Destroy() call targeting an object inside our cloned interior.
    // This guards against other systems trying to tear down/replace fire-related objects
    // in the clone while we're in the middle of re-registering them with FireManager.
    [HarmonyLib.HarmonyPatch(typeof(UnityEngine.Object), nameof(UnityEngine.Object.Destroy), new System.Type[] { typeof(UnityEngine.Object) })]
    public class PreventFireDestructionPatch
    {
        public static bool s_ProtectInterior = false;

        public static bool Prefix(UnityEngine.Object obj)
        {
            if (s_ProtectInterior && obj != null && SeamlessInteriorsMod.s_MasterInterior != null)
            {
                GameObject go = obj.TryCast<GameObject>();
                if (go == null)
                {
                    Component comp = obj.TryCast<Component>();
                    if (comp != null) go = comp.gameObject;
                }

                if (go != null && go.transform.IsChildOf(SeamlessInteriorsMod.s_MasterInterior.transform))
                {
                    return false;
                }
            }
            return true;
        }
    }
}