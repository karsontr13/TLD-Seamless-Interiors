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

    // DIAGNOSTIC (verbose only): what the game actually WROTE for this save.
    //
    // Read-only - it takes the string the game just produced rather than asking for
    // another one, so nothing about the save changes. Together with the load-side dump
    // this answers the one question the log cannot: is the clone's stove fire in the
    // save at all? (see SeamlessInteriorsMod.FireDiagnostics.cs)
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.FireManager), nameof(Il2Cpp.FireManager.Serialize))]
    public class FireSerializeDiagPatch
    {
        public static void Postfix(string __result)
        {
            if (!SeamlessInteriorsMod.s_DebugBounds) return;

            SeamlessInteriorsMod.DumpFireState("kayit-ani");
            SeamlessInteriorsMod.DumpFireBlob(__result, "kayit");
        }
    }

    // REMOVED: PreventFireDestructionPatch.
    //
    // It was a prefix on UnityEngine.Object.Destroy that blocked the destruction of fire
    // objects inside a clone, and it was switched on for exactly one thing: the mod's own
    // replay of FireManager.Deserialize. That replay is gone (see DelayedFireRestore in
    // SeamlessInteriorsMod.SaveData.cs - it was measured not to reach a closed building's
    // fires at all, which is the only reason it existed), so the guard could never be
    // switched on again.
    //
    // A prefix that can never fire is not free: this one sat in front of EVERY
    // Object.Destroy in the game, for the whole session. The fires it used to protect are
    // now restored one by one, by guid, without anything being destroyed or rebuilt.
}
