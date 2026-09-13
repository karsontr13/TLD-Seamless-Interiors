using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace SeamlessInteriors
{
    // ─────────────────────────────────────────────────────────────────────────
    // DID THIS OBJECT'S SETUP EVER RUN?
    //
    // WHY THIS EXISTS. A combination safe in a clone cannot be clicked: no icon, no
    // hover line, nothing. Every value that can be read off it says it is healthy -
    // the collider is reachable on the right layer, Container reports open, and
    // ContainerInteraction reports IsEnabled and CanInteract true through BOTH its own
    // override and the base class, and produces a hover line ("Kasayi arastir").
    // Calling PerformInteraction on it by hand returns true and opens the door. And the
    // game's own pick for the crosshair is still NULL, while an identical locker one
    // metre away is picked normally.
    //
    // Every theory about the object itself has now been measured and killed: distance,
    // the XP-mode gate (this safe is only disabled on Interloper/Misery, and the game
    // is Stalker), the hover-icon list (identical to the locker's), the extra
    // BoxCollider, and a suspected base-vs-override mix-up in the readings.
    //
    // WHAT IS LEFT is the one thing no property can report: whether the component was
    // ever INITIALISED. Two independent components on that same GameObject look like
    // their setup never ran - SafeCracking has m_RollCombination true but an EMPTY
    // combination and no dial object, both of which its setup creates. A component
    // whose Start never ran still answers every getter correctly and still performs
    // when called directly; what it does not do is end up in whatever list the game
    // scans for the crosshair.
    //
    // So the four setup entry points are watched and say, in the log, whether they ran
    // for the safe and for the locker. Verbose only, a handful of lines per building.
    // ─────────────────────────────────────────────────────────────────────────
    internal static class InteractionSetupLog
    {
        // NAME FILTER, NOT A LINE BUDGET.
        //
        // The first attempt just capped the output at 120 lines, and a region load spent
        // every one of them on ContainerInteraction.Awake for crates, corpses and cupboard
        // doors - the budget was gone before the player had even opened the building the
        // question was about. Only two objects matter here, so only two objects are
        // logged, and the cap is left as a runaway guard rather than as the filter.
        private const int LIMIT = 400;
        private static int s_Lines;

        private static bool Interesting(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            return name.IndexOf("Safe", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Locker", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // alwaysLog is for the safe's own components: there are only a handful of them in
        // a region, and every one of them is evidence.
        public static void Say(string what, UnityEngine.Component c, bool alwaysLog = false)
        {
            if (!SeamlessInteriorsMod.s_DebugBounds) return;
            if (s_Lines >= LIMIT) return;

            string name = "?";
            try { name = c != null && c.gameObject != null ? c.gameObject.name : "(yok)"; }
            catch { }

            if (!alwaysLog && !Interesting(name)) return;

            s_Lines++;
            MelonLogger.Msg($"[KURULUM] {what} '{name}'");
        }

        public static void Reset() { s_Lines = 0; }
    }

    // NOT PATCHED: BaseInteraction.Start.
    //
    // It was, for one build, to separate "Start never ran" from "Start ran and the
    // wiring step did not". It took the game down before the main menu finished
    // loading - no managed exception, the log simply stops - and it was the only patch
    // that build added. BaseInteraction is the abstract base every interaction in the
    // game inherits Start from, and hooking a Unity message at that level under IL2CPP
    // reaches every one of them through a wrapper the runtime does not owe us.
    //
    // Nothing is lost that matters. InitializeInteraction below is what Start CALLS, so
    // its absence answers the same question, and SafeCracking.Start is hooked on the
    // concrete class where the entry/exit pair is safe.

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.ContainerInteraction), nameof(Il2Cpp.ContainerInteraction.Awake))]
    public class ContainerInteractionAwakeLogPatch
    {
        public static void Postfix(Il2Cpp.ContainerInteraction __instance)
        {
            InteractionSetupLog.Say("ContainerInteraction.Awake", __instance);
        }
    }

    // THE ONE THAT MATTERS. BaseInteraction.Start calls InitializeInteraction, and
    // ContainerInteraction overrides it - so this fires exactly when an interaction
    // finishes wiring itself up. An object the game never offers, whose name never
    // appears on this line, has its answer.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.ContainerInteraction), nameof(Il2Cpp.ContainerInteraction.InitializeInteraction))]
    public class ContainerInteractionInitLogPatch
    {
        public static void Postfix(Il2Cpp.ContainerInteraction __instance)
        {
            InteractionSetupLog.Say("ContainerInteraction.InitializeInteraction", __instance);
        }
    }

    // The safe's own setup, for the same reason: an empty combination on a safe that is
    // supposed to roll one is either a step that never ran or a step that threw.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SafeCracking), nameof(Il2Cpp.SafeCracking.Awake))]
    public class SafeCrackingAwakeLogPatch
    {
        public static void Postfix(Il2Cpp.SafeCracking __instance)
        {
            InteractionSetupLog.Say("SafeCracking.Awake", __instance, true);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.SafeCracking), nameof(Il2Cpp.SafeCracking.Start))]
    public class SafeCrackingStartLogPatch
    {
        public static void Prefix(Il2Cpp.SafeCracking __instance)
        {
            InteractionSetupLog.Say("SafeCracking.Start GIRIS", __instance, true);
        }

        // Entry and exit are logged separately on purpose. A Start that is entered and
        // never leaves is a Start that threw halfway - which would leave exactly the
        // half-built state the safe is showing, and which no getter can report.
        public static void Postfix(Il2Cpp.SafeCracking __instance)
        {
            InteractionSetupLog.Say("SafeCracking.Start CIKIS", __instance, true);
        }
    }
}
