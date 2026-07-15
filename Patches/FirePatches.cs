using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
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