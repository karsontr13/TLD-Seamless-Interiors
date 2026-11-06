using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── CHALLENGES (issue #11) ───
        // Nomad and Whiteout read the real scene, which is the region in an SI building; they get the interior here.

        private const float WHITEOUT_COUNT_INTERVAL = 1f;
        private static float s_NextWhiteoutCount;
        private static MethodInfo s_SerializeObject;
        private static bool s_SerializeLookupDone;
        private static bool s_ReportedChallenge;

        // The building the player is in, null outdoors and in a vanilla interior.
        private static SeamlessInteriorInstance ChallengeBuilding()
        {
            if (!IsAliveDirect(s_EnteredSpace) || s_EnteredSpaceId == null) return null;
            SeamlessInteriorInstance instance;
            return ActiveInteriors.TryGetValue(s_EnteredSpaceId, out instance) && instance != null && instance.MasterInterior != null ? instance : null;
        }

        // Nomad's key as the vanilla game builds it in that interior: "Scene", "Scene:Region" or the Dam pair.
        internal static void NomadSceneKey(Action_NomadRequirements action, ref string key)
        {
            SeamlessInteriorInstance instance = ChallengeBuilding();
            if (instance == null || action == null) return;
            string scene = instance.Config.InteriorSceneBaseName;
            if (string.IsNullOrEmpty(scene)) return;

            var required = action.scenesRequired;
            string found = "";
            if (required != null)
            {
                string withRegion = scene + ":" + instance.Config.ExteriorSceneName;
                const string dam = "Dam;DamTransitionZone";
                if ((scene == "Dam" || scene == "DamTransitionZone") && required.Contains(dam)) found = dam;
                else if (required.Contains(scene)) found = scene;
                else if (required.Contains(withRegion)) found = withRegion;
            }
            ReportChallengeOnce("Nomad", scene, found);
            key = found;
        }

        // Counts the stockpile in the SI building, as the game counts it only while the scene is active.
        internal static void WhiteoutStockpile(Action_WhiteoutGearRequirements action)
        {
            if (action == null || Time.time < s_NextWhiteoutCount) return;
            SeamlessInteriorInstance instance = ChallengeBuilding();
            if (instance == null || instance.Config.InteriorSceneBaseName != action.sceneToStockpileItems) return;
            s_NextWhiteoutCount = Time.time + WHITEOUT_COUNT_INTERVAL;

            MethodInfo serialize = SerializeObjectMethod();
            var parameter = action.stockpiledGearSerialized;
            if (serialize == null || parameter == null) return;

            var stored = Utils.GetGearItemsStoredInSceneByPlayer(true);
            var kept = new List<GearItem>();
            HashSet<int> inBuilding = BuildingContainerGear(instance);
            for (int i = 0; stored != null && i < stored.Length; i++)
            {
                GearItem gear = stored[i];
                if (gear != null && IsStockpiledIn(instance, gear, inBuilding)) kept.Add(gear);
            }

            var array = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GearItem>(kept.ToArray());
            action.m_StockpiledGearDict = new Il2CppSystem.Collections.Generic.Dictionary<string, float>();
            action.AddGearItemsToDict(array);
            parameter.value = (string)serialize.Invoke(null, new object[] { action.m_StockpiledGearDict });
            ReportChallengeOnce("Whiteout", instance.Config.InteriorSceneBaseName, kept.Count + " esya");
        }

        // Container contents and water supplies of the building's own containers.
        private static HashSet<int> BuildingContainerGear(SeamlessInteriorInstance instance)
        {
            var ids = new HashSet<int>();
            var list = new Il2CppSystem.Collections.Generic.List<GearItem>();
            foreach (Container container in instance.MasterInterior.GetComponentsInChildren<Container>(true))
            {
                if (container == null) continue;
                list.Clear();
                container.GetItems(list);
                for (int i = 0; i < list.Count; i++) if (list[i] != null) ids.Add(list[i].GetInstanceID());
                WaterSupply water = container.GetWaterSupply(default(LiquidQuality));
                GearItem waterGear = water != null ? water.GetComponent<GearItem>() : null;
                if (waterGear != null) ids.Add(waterGear.GetInstanceID());
            }
            return ids;
        }

        private static bool IsStockpiledIn(SeamlessInteriorInstance instance, GearItem gear, HashSet<int> inBuilding)
        {
            if (inBuilding.Contains(gear.GetInstanceID())) return true;
            if (gear.m_InsideContainer) return false;
            SeamlessInteriorInstance owner = FindInstanceOwning(gear.transform);
            if (owner != null) return owner == instance;
            return IsPositionInsideFull(instance, gear.transform.position);
        }

        private static MethodInfo SerializeObjectMethod()
        {
            if (s_SerializeLookupDone) return s_SerializeObject;
            s_SerializeLookupDone = true;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("Il2CppTLD.Serialization.SerializationUtils", false);
                if (type == null) continue;
                s_SerializeObject = type.GetMethod("SerializeObject", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Il2CppSystem.Object) }, null);
                break;
            }
            if (s_SerializeObject == null) MelonLogger.Warning("[CHALLENGE] SerializationUtils.SerializeObject bulunamadi; Whiteout sayimi yapilamiyor.");
            return s_SerializeObject;
        }

        private static void ReportChallengeOnce(string challenge, string scene, string result)
        {
            if (s_ReportedChallenge && !s_DebugBounds) return;
            s_ReportedChallenge = true;
            MelonLogger.Msg($"[CHALLENGE] {challenge}: SI binasi '{scene}' -> {(string.IsNullOrEmpty(result) ? "(listede yok)" : result)}");
        }
    }

    [HarmonyPatch(typeof(Action_NomadRequirements), nameof(Action_NomadRequirements.GetCurrentSceneKey))]
    internal static class NomadSceneKeyPatch
    {
        private static void Postfix(Action_NomadRequirements __instance, ref string __result)
        {
            try { SeamlessInteriorsMod.NomadSceneKey(__instance, ref __result); }
            catch (Exception ex) { MelonLogger.Warning("[CHALLENGE] Nomad: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Action_WhiteoutGearRequirements), nameof(Action_WhiteoutGearRequirements.OnUpdate))]
    internal static class WhiteoutStockpilePatch
    {
        private static void Prefix(Action_WhiteoutGearRequirements __instance)
        {
            try { SeamlessInteriorsMod.WhiteoutStockpile(__instance); }
            catch (Exception ex) { MelonLogger.Warning("[CHALLENGE] Whiteout: " + ex.Message); }
        }
    }
}
