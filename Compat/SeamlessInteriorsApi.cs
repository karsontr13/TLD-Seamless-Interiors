using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    // ─── PUBLIC SURFACE FOR OTHER MODS ───
    //
    // Seamless Interiors puts a building's interior INTO the region scene instead of loading it
    // as a scene of its own. Everything a mod would normally ask to find out whether the player
    // is indoors - the active scene's name, Weather.IsIndoorScene, a scene load - keeps
    // answering "outdoors, in the region" the whole time the player is inside.
    //
    // This class is the replacement. It is static, never throws, and is safe to call from the
    // main menu. A mod that does not want a hard reference can reach it by reflection:
    //   Type.GetType("SeamlessInteriors.SeamlessInteriorsApi, SeamlessInteriors")
    //
    // An "interior id" is a building's ResolvedInstanceId: "CampOffice", "LakeCabinA_2".
    // Several buildings can share one interior scene, so a scene name is not an id;
    // GetInteriorSceneName maps an id back to its vanilla scene ("LakeCabinA").
    public static class SeamlessInteriorsApi
    {
        public const int ApiVersion = 1;

        // Raised after the player has entered a building (its clone is already open) and after
        // the player has left one. Moving straight from one building into another - a farmhouse
        // into its basement - raises both, the exit first.
        public static event Action<string> PlayerEnteredInterior;
        public static event Action<string> PlayerExitedInterior;

        public static bool IsPlayerInsideInterior
        {
            get { return SeamlessInteriorsMod.PlayerInteriorId != null; }
        }

        public static string GetPlayerInteriorId()
        {
            return SeamlessInteriorsMod.PlayerInteriorId;
        }

        public static string GetPlayerInteriorSceneName()
        {
            return GetInteriorSceneName(SeamlessInteriorsMod.PlayerInteriorId);
        }

        // True for an interior scene this mod is loading additively only to copy it into a
        // clone. Such a scene still raises OnSceneWasInitialized in every mod - and while it
        // loads, Weather.IsIndoorScene() can answer true - yet the player is not in it. A mod
        // that reacts to scene loads should ignore these.
        public static bool IsCloneTemplateScene(string sceneName)
        {
            try { return SeamlessInteriorsMod.IsInteriorLoadedAsCloneTemplate(sceneName); }
            catch { return false; }
        }

        // The vanilla interior scene behind an id, or null when the id is not one of this mod's
        // buildings.
        public static string GetInteriorSceneName(string interiorId)
        {
            InteriorConfig config = FindConfig(interiorId);
            return config != null ? config.InteriorSceneBaseName : null;
        }

        // The region scene the building stands in ("LakeRegion"), or null for an unknown id.
        public static string GetInteriorRegionSceneName(string interiorId)
        {
            InteriorConfig config = FindConfig(interiorId);
            return config != null ? config.ExteriorSceneName : null;
        }

        // Every building this version of the mod clones, as interior ids.
        public static string[] GetInteriorIds()
        {
            try
            {
                var ids = new List<string>();
                foreach (var config in SeamlessInteriorsMod.SupportedInteriors)
                    if (config != null && !ids.Contains(config.ResolvedInstanceId)) ids.Add(config.ResolvedInstanceId);
                return ids.ToArray();
            }
            catch { return new string[0]; }
        }

        // False while the mod is still building the interiors of the region that just loaded
        // (its loading screen is up). A mod that scans the scene once after a load should wait
        // for this, or the buildings' contents are not there yet.
        public static bool AreInteriorsReady
        {
            get
            {
                try { return !SeamlessInteriorsMod.IsAnyCloningActive() && !SeamlessInteriorsMod.s_ScreenHeldBlack; }
                catch { return true; }
            }
        }

        public static bool IsInteriorOpen(string interiorId)
        {
            try
            {
                SeamlessInteriorInstance instance;
                return TryGetInstance(interiorId, out instance)
                       && instance.MasterInterior != null
                       && instance.MasterInterior.activeInHierarchy;
            }
            catch { return false; }
        }

        // The root every object of the building hangs under, or null before it is built.
        public static GameObject GetInteriorRoot(string interiorId)
        {
            try
            {
                SeamlessInteriorInstance instance;
                return TryGetInstance(interiorId, out instance) ? instance.MasterInterior : null;
            }
            catch { return null; }
        }

        // The building whose volume holds this world position, or null. Where volumes overlap
        // (a farmhouse above its basement) an open building wins over a closed one.
        public static string GetInteriorIdAt(Vector3 worldPosition)
        {
            try
            {
                string closedMatch = null;
                foreach (var instance in SeamlessInteriorsMod.ActiveInteriors.Values)
                {
                    // Cheapest test first: the loaded-region check is a scene lookup, so it only
                    // runs for a building whose volume actually holds the position.
                    if (!IsBuiltAndNotParked(instance)) continue;
                    if (!instance.IsPositionInVolume(worldPosition)) continue;
                    if (!SeamlessInteriorsMod.IsExteriorSceneLoaded(instance)) continue;

                    if (instance.MasterInterior.activeSelf) return instance.Config.ResolvedInstanceId;
                    if (closedMatch == null) closedMatch = instance.Config.ResolvedInstanceId;
                }
                return closedMatch;
            }
            catch { return null; }
        }

        // The building an object belongs to: the clone it is part of or, for something standing
        // in the world (a campfire the player built indoors), the building it stands in.
        // Null outdoors.
        public static string GetInteriorIdOf(GameObject go)
        {
            if (go == null) return null;
            try
            {
                SeamlessInteriorInstance owner = SeamlessInteriorsMod.FindInstanceOwning(go.transform);
                if (owner != null) return owner.Config.ResolvedInstanceId;

                return GetInteriorIdAt(go.transform.position);
            }
            catch { return null; }
        }

        // Part of a building whose clone is closed right now: its Update does not run and it is
        // not in the visible world, even though managers such as FireManager may still list it.
        public static bool IsInClosedInterior(GameObject go)
        {
            if (go == null) return false;
            try
            {
                SeamlessInteriorInstance owner = SeamlessInteriorsMod.FindInstanceOwning(go.transform);
                return owner != null && owner.MasterInterior != null && !owner.MasterInterior.activeInHierarchy;
            }
            catch { return false; }
        }

        // ─── For this assembly ───

        internal static bool TryGetInstance(string interiorId, out SeamlessInteriorInstance instance)
        {
            instance = null;
            if (string.IsNullOrEmpty(interiorId)) return false;
            return SeamlessInteriorsMod.ActiveInteriors.TryGetValue(interiorId, out instance) && instance != null;
        }

        // Built, and not parked in DontDestroyOnLoad for another region.
        private static bool IsBuiltAndNotParked(SeamlessInteriorInstance instance)
        {
            return instance != null && instance.RunCompleted && !instance.InteriorPersisted
                   && instance.MasterInterior != null && instance.InteriorTrigger != null;
        }

        internal static bool IsInteriorInLoadedRegion(string interiorId)
        {
            SeamlessInteriorInstance instance;
            return TryGetInstance(interiorId, out instance)
                   && !instance.InteriorPersisted
                   && SeamlessInteriorsMod.IsExteriorSceneLoaded(instance);
        }

        internal static void RaisePlayerEnteredInterior(string interiorId)
        {
            Raise(PlayerEnteredInterior, interiorId, "giris");
        }

        internal static void RaisePlayerExitedInterior(string interiorId)
        {
            Raise(PlayerExitedInterior, interiorId, "cikis");
        }

        private static void Raise(Action<string> handlers, string interiorId, string what)
        {
            if (handlers == null) return;

            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<string>)d)(interiorId);
                }
                catch (Exception ex)
                {
                    string who = d.Method.DeclaringType != null ? d.Method.DeclaringType.Name + "." + d.Method.Name : d.Method.Name;
                    MelonLogger.Warning($"[API] {interiorId} {what} olayinin dinleyicisi hata verdi ({who}): {ex}");
                }
            }
        }

        // SupportedInteriors never changes after startup, so the id lookup is built once.
        private static Dictionary<string, InteriorConfig> s_ConfigsById;

        private static InteriorConfig FindConfig(string interiorId)
        {
            if (string.IsNullOrEmpty(interiorId)) return null;

            if (s_ConfigsById == null)
            {
                var map = new Dictionary<string, InteriorConfig>(StringComparer.Ordinal);
                foreach (var config in SeamlessInteriorsMod.SupportedInteriors)
                {
                    if (config == null || map.ContainsKey(config.ResolvedInstanceId)) continue;
                    map.Add(config.ResolvedInstanceId, config);
                }
                s_ConfigsById = map;
            }

            InteriorConfig found;
            return s_ConfigsById.TryGetValue(interiorId, out found) ? found : null;
        }
    }
}
