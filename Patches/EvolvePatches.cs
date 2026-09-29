using Il2Cpp;

namespace SeamlessInteriors
{
    // Curing (saplings, hides, guts) needs an indoor trigger around the item; clones have none,
    // so nothing cured in an SI building. An item inside a building's volume counts as indoors.
    [HarmonyLib.HarmonyPatch(typeof(EvolveItem), nameof(EvolveItem.ObjectInIndoorTrigger))]
    public class EvolveItemIndoorPatch
    {
        public static void Postfix(EvolveItem __instance, ref bool __result)
        {
            if (__result || __instance == null) return;
            __result = SeamlessInteriorsMod.IsInsideAnyInteriorVolume(__instance.transform.position);
        }
    }
}
