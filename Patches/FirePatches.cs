using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    // FireManager.Deserialize wipes and rebuilds every fire from the save string.
    // We keep a copy of that string so fires can be restored after cloning.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.FireManager), nameof(Il2Cpp.FireManager.Deserialize))]
    public class FireManagerStealerPatch
    {
        public static string s_StolenFireData = "";

        public static void Prefix(string text)
        {
            // While a clone routine is running the data is incomplete, so skip it.
            bool isAnyCloningActive = SeamlessInteriorsMod.IsAnyCloningActive();

            if (!string.IsNullOrEmpty(text) && !isAnyCloningActive)
            {
                s_StolenFireData = text;

                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg($"[FIRE-STEAL] Ates datasi basariyla kopyalandi ({text.Length} karakter).");
            }
        }
    }

    // Guard used while FireManager rebuilds fires: it must not take cloned
    // interior fires with it. Enabled only for the duration of that call.
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

                // Protect fire objects ONLY. Protecting everything under MasterInterior
                // used to leave player-broken objects (the ones without BreakDown) alive
                // and stuck at activeSelf=true in the hierarchy.
                //
                // Order matters: cheap root test first, expensive GetComponentInParent
                // chain second, because this prefix sees EVERY Object.Destroy in the game.
                if (go != null
                    && SeamlessInteriorsMod.FindInstanceOwning(go.transform) != null
                    && IsFireRelated(go))
                {
                    return false; // Block the destruction
                }
            }
            return true;
        }

        // FireManager.Deserialize only destroys Fire / WoodStove / Campfire objects,
        // so the protection is limited to those.
        private static bool IsFireRelated(GameObject go)
        {
            return go.GetComponentInParent<Il2Cpp.Fire>() != null
                || go.GetComponentInParent<Il2Cpp.WoodStove>() != null
                || go.GetComponentInParent<Il2Cpp.Campfire>() != null;
        }
    }
}
