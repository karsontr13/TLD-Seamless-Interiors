using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
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
                pm.transform.position = pm.transform.position + (GameManager.GetVpFPSCamera().transform.forward * 2f);
                SeamlessInteriorsMod.SetAudioOcclusion(true);

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
                pm.transform.position = pm.transform.position + (GameManager.GetVpFPSCamera().transform.forward * 2f);
                SeamlessInteriorsMod.SetAudioOcclusion(false);

                return false;
            }

            return true;
        }
    }
}