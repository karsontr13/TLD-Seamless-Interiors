using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System.Linq;

namespace SeamlessInteriors
{
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.FireManager), nameof(Il2Cpp.FireManager.Deserialize))]
    public class FireManagerStealerPatch
    {
        public static string s_StolenFireData = "";

        public static void Prefix(string text)
        {
            // Eğer aktif binalardan herhangi birinde klonlama rutini sürüyorsa bekle.
            bool isAnyCloningActive = SeamlessInteriorsMod.ActiveInteriors.Values.Any(i => i.IsCloningRoutineActive);

            if (!string.IsNullOrEmpty(text) && !isAnyCloningActive)
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
            if (s_ProtectInterior && obj != null)
            {
                GameObject go = obj.TryCast<GameObject>();
                if (go == null)
                {
                    Component comp = obj.TryCast<Component>();
                    if (comp != null) go = comp.gameObject;
                }

                if (go != null)
                {
                    foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
                    {
                        if (instance.MasterInterior != null && go.transform.IsChildOf(instance.MasterInterior.transform))
                        {
                            return false; // Silinmesini engelle
                        }
                    }
                }
            }
            return true;
        }
    }
}