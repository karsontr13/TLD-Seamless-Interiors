using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    // Intercepts every door/portal interaction in the game (LoadScene.PerformInteraction).
    // If the door doesn't belong to the CampOffice building at all, we let the game handle it
    // normally (real scene load), but we first make sure the interior is visible and its
    // placeable positions are saved so nothing is lost if the player is about to enter the
    // "real" (non-cloned) version of the interior through a different door/trigger.
    // If the door DOES belong to CampOffice, we take over completely: instead of loading a
    // separate scene, we just toggle the clone/shell visibility and audio occlusion and
    // teleport the player to the matching spawn point - this is what makes the transition
    // instant and seamless instead of showing a loading screen.
    [HarmonyLib.HarmonyPatch(typeof(LoadScene), nameof(LoadScene.PerformInteraction))]
    public class PortalMagicPatch
    {
        public static bool Prefix(LoadScene __instance)
        {
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null || !SeamlessInteriorsMod.s_RunCompleted) return true;

            bool belongsToCampOffice =
                (SeamlessInteriorsMod.s_ExteriorShell != null && __instance.transform.IsChildOf(SeamlessInteriorsMod.s_ExteriorShell.transform)) ||
                (SeamlessInteriorsMod.s_MasterInterior != null && __instance.transform.IsChildOf(SeamlessInteriorsMod.s_MasterInterior.transform));

            if (SeamlessInteriorsMod.s_DebugBounds)
            {
                MelonLogger.Msg($"[DEBUG-PORTAL] Door: {__instance.gameObject.name} | Root: {__instance.transform.root.name} | belongsToCampOffice={belongsToCampOffice} | targetScene={__instance.m_SceneToLoad}");
            }

            if (!belongsToCampOffice)
            {
                if (SeamlessInteriorsMod.s_MasterInterior != null && !SeamlessInteriorsMod.s_MasterInterior.activeSelf)
                {
                    SeamlessInteriorsMod.s_MasterInterior.SetActive(true);

                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg("[SAVE-FIX] Orijinal ic mekana giriliyor, save kaybini onlemek icin interior gecici olarak aktif edildi.");
                }

                try
                {
                    SeamlessInteriorsMod.SavePlaceablePositions();
                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg("[PORTAL-SAVE] Orijinal ic mekana gecis oncesi Placeable pozisyonlari kaydedildi.");
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[PORTAL-SAVE] Hata: {ex.Message}");
                }

                return true;
            }

            string targetScene = __instance.m_SceneToLoad;
            SeamlessInteriorsMod.s_LastPortalUseTime = Time.time;

            if (targetScene == SeamlessInteriorsMod.INTERIOR)
            {
                if (SeamlessInteriorsMod.s_MasterInterior != null) SeamlessInteriorsMod.s_MasterInterior.SetActive(true);
                if (SeamlessInteriorsMod.s_ExteriorShell != null) SeamlessInteriorsMod.s_ExteriorShell.SetActive(false);

                LightingManager.m_LevelLoadComplete = true;
                LightingManager.OnLevelLoadComplete();
                LightingManager.SetLightingStrengthDefault();
                UnityEngine.DynamicGI.UpdateEnvironment();

                SeamlessInteriorsMod.SetInteriorItemsVisible(true);

                SeamlessInteriorsMod.SetAudioOcclusion(true);

                Vector3 spawnPos = __instance.transform.Find("SpawnPoint") != null ? __instance.transform.Find("SpawnPoint").position : __instance.transform.position;
                GameManager.GetPlayerManagerComponent().TeleportPlayer(spawnPos, GameManager.GetPlayerTransform().rotation);
                GameManager.GetPlayerManagerComponent().StickPlayerToGround();

                return false;
            }

            if (targetScene == SeamlessInteriorsMod.EXTERIOR)
            {
                if (SeamlessInteriorsMod.s_MasterInterior != null) SeamlessInteriorsMod.s_MasterInterior.SetActive(false);
                if (SeamlessInteriorsMod.s_ExteriorShell != null) SeamlessInteriorsMod.s_ExteriorShell.SetActive(true);

                LightingManager.m_LevelLoadComplete = true;
                LightingManager.OnLevelLoadComplete();
                LightingManager.SetLightingStrengthDefault();
                UnityEngine.DynamicGI.UpdateEnvironment();

                SeamlessInteriorsMod.SetInteriorItemsVisible(false);

                SeamlessInteriorsMod.SetAudioOcclusion(false);

                return false;
            }

            return true;
        }
    }
}
