using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SeamlessInteriors
{
    // ─── MOD EXCEPTIONS ───
    // The two places where no vanilla answer can fit, patched by name only while that mod is loaded.
    // Everything else other mods see comes from the vanilla view (SeamlessInteriorsMod.VanillaView.cs).
    internal static class ModExceptions
    {
        private const string TAG = "[ISTISNA]";

        // Major Miseries shelters the hangar's lower level from the aurora by the player's height in
        // the AFHangar scene; inside the clone that scene's coordinates are the clone's local ones.
        private const string HANGAR_SCENE = "AFHangar";
        private const float HANGAR_SHELTER_MAX_Y = 6f;

        private static bool s_ReportedHangar;
        private static bool s_ReportedWeather;
        private static readonly HashSet<string> s_ReportedErrors = new HashSet<string>();
        private static Assembly s_WeatherOverhaul;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            InstallHangarShelter(harmony);
            InstallWeatherOverhaulVisuals(harmony);
        }

        private static void InstallHangarShelter(HarmonyLib.Harmony harmony)
        {
            Assembly assembly = FindMelonAssembly("MajorMiseries");
            if (assembly == null) return;

            try
            {
                Type aurora = assembly.GetType("MajorMiseries.Managers.AuroraInfluenceManager");
                MethodInfo zone = aurora?.GetMethod("IsPlayerInAFHangarShelterZone", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (zone == null) throw new MissingMethodException("AuroraInfluenceManager.IsPlayerInAFHangarShelterZone bulunamadi.");

                harmony.Patch(zone, prefix: OwnMethod(nameof(HangarShelterPrefix)));
                MelonLogger.Msg($"{TAG} Major Miseries: hangarin alt kati bina koordinatiyla olculuyor.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{TAG} Major Miseries hangar istisnasi kurulamadi: {(ex.InnerException ?? ex).Message}");
            }
        }

        private static void InstallWeatherOverhaulVisuals(HarmonyLib.Harmony harmony)
        {
            Assembly assembly = FindMelonAssembly("WeatherOverhaul");
            if (assembly == null) return;

            try
            {
                MethodInfo capture = StaticMethod(assembly, "WeatherOverhaul.Weather.WeatherSnapshot", "Capture");
                MethodInfo gameplayScene = StaticMethod(assembly, "WeatherOverhaul.Weather.CustomWeatherStageRuntime", "IsGameplayWeatherSceneActive");
                MethodInfo auroraAuthority = StaticMethod(assembly, "WeatherOverhaul.Weather.VanillaAuroraAuthority", "HasLoadedRegionAuthority");
                if (capture == null || gameplayScene == null || auroraAuthority == null)
                    throw new MissingMemberException("Weather Overhaul'un beklenen uyeleri bulunamadi (surum degismis olabilir).");

                SeamlessInteriorsMod.EnsureIndoorEnvironmentHook();
                harmony.Patch(capture, prefix: OwnMethod(nameof(SkyViewEnter)), finalizer: OwnMethod(nameof(SkyViewExit)));
                harmony.Patch(gameplayScene, prefix: OwnMethod(nameof(RegionViewEnter)), finalizer: OwnMethod(nameof(RegionViewExit)));
                harmony.Patch(auroraAuthority, prefix: OwnMethod(nameof(RegionViewEnter)), finalizer: OwnMethod(nameof(RegionViewExit)));
                s_WeatherOverhaul = assembly;
                MelonLogger.Msg($"{TAG} Weather Overhaul: bina icinde de bolgenin acik havasini goruyor; islanma ve sicaklik icerideki gibi.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{TAG} Weather Overhaul istisnasi kurulamadi: {(ex.InnerException ?? ex).Message}");
            }
        }

        // ─── Patches ───

        private static bool HangarShelterPrefix(ref bool __result)
        {
            try
            {
                SeamlessInteriorInstance instance;
                string id = SeamlessInteriorsMod.PlayerInteriorId;
                if (id == null || !SeamlessInteriorsMod.ActiveInteriors.TryGetValue(id, out instance) || instance == null) return true;
                if (instance.MasterInterior == null || instance.Config.InteriorSceneBaseName != HANGAR_SCENE) return true;

                Transform player = GameManager.GetPlayerTransform();
                if (player == null) return true;

                __result = instance.MasterInterior.transform.InverseTransformPoint(player.position).y <= HANGAR_SHELTER_MAX_Y;
                if (!s_ReportedHangar)
                {
                    s_ReportedHangar = true;
                    MelonLogger.Msg($"{TAG} Hangar siginagi bina koordinatiyla olculdu: {(__result ? "alt kat, korunuyor" : "ust kat")} (bir kez yazilir).");
                }
                return false;
            }
            catch (Exception ex)
            {
                ReportOnce("hangar", ex);
                return true;
            }
        }

        // An indoor snapshot or a new scene name makes Weather Overhaul drop its sky; SI's windows show it, so the
        // snapshot sees the region's open air. Wetness and temperature still read the real indoor state.
        private static void SkyViewEnter()
        {
            SeamlessInteriorsMod.s_WeatherRegionView++;
            SeamlessInteriorsMod.s_WeatherOutdoorView++;
        }

        private static void SkyViewExit()
        {
            SeamlessInteriorsMod.s_WeatherRegionView--;
            SeamlessInteriorsMod.s_WeatherOutdoorView--;
            if (!s_ReportedWeather && SeamlessInteriorsMod.PlayerInteriorId != null)
            {
                s_ReportedWeather = true;
                MelonLogger.Msg($"{TAG} Weather Overhaul bina icinde bolgenin acik havasini goruyor (bir kez yazilir).");
            }
        }

        // Checks that compare the active scene with the snapshot's must read the same region.
        private static void RegionViewEnter()
        {
            SeamlessInteriorsMod.s_WeatherRegionView++;
        }

        private static void RegionViewExit()
        {
            SeamlessInteriorsMod.s_WeatherRegionView--;
        }

        // Its blood moon keeps acting on the region's animals, which SI keeps alive while the player is inside.
        internal static bool SeesWholeRegion(Assembly assembly)
        {
            return assembly != null && assembly == s_WeatherOverhaul;
        }

        private static readonly Dictionary<Assembly, bool> s_DebugTools = new Dictionary<Assembly, bool>();

        // UnityExplorer and its UniverseLib walk scenes for the person debugging; the scene root view leaves them alone.
        // Code typed into its console compiles into its own assembly and still counts as a mod.
        internal static bool IsDebugTool(Assembly assembly)
        {
            if (assembly == null) return false;

            bool tool;
            if (s_DebugTools.TryGetValue(assembly, out tool)) return tool;

            string name = assembly.GetName().Name ?? "";
            tool = name.StartsWith("UnityExplorer", StringComparison.Ordinal) || name.StartsWith("UniverseLib", StringComparison.Ordinal);
            s_DebugTools[assembly] = tool;
            return tool;
        }

        // ─── Helpers ───

        private static MethodInfo StaticMethod(Assembly assembly, string typeName, string methodName)
        {
            Type type = assembly.GetType(typeName);
            return type?.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        }

        private static Assembly FindMelonAssembly(string melonName)
        {
            foreach (MelonBase melon in MelonBase.RegisteredMelons)
                if (melon != null && melon.Info != null && melon.Info.Name == melonName)
                    return melon.GetType().Assembly;
            return null;
        }

        private static HarmonyLib.HarmonyMethod OwnMethod(string name)
        {
            return new HarmonyLib.HarmonyMethod(typeof(ModExceptions).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));
        }

        private static void ReportOnce(string where, Exception ex)
        {
            if (!s_ReportedErrors.Add(where)) return;
            MelonLogger.Warning($"{TAG} {where} istisnasi hata verdi (bir kez yazilir): {ex.Message}");
        }
    }
}
