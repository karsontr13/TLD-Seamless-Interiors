using Il2Cpp;
using MelonLoader;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        private void RestoreSceneSaveData()
        {
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            if (!string.IsNullOrEmpty(currentSaveName))
            {
                if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData))
                {
                    MelonCoroutines.Start(DelayedFireRestore());
                }

                SaveGameSystem.LoadSceneDataAdditive(currentSaveName, EXTERIOR);
            }
        }

        public static IEnumerator DelayedFireRestore()
        {
            yield return null;
            yield return null;
            yield return null;

            if (!string.IsNullOrEmpty(FireManagerStealerPatch.s_StolenFireData) && s_MasterInterior != null)
            {
                var allFires = s_MasterInterior.GetComponentsInChildren<Il2Cpp.Fire>(true);
                foreach (var f in allFires) if (f != null && !Il2Cpp.FireManager.m_Fires.Contains(f)) Il2Cpp.FireManager.AddFire(f);

                var allWoodStoves = s_MasterInterior.GetComponentsInChildren<Il2Cpp.WoodStove>(true);
                foreach (var ws in allWoodStoves) if (ws != null && !Il2Cpp.FireManager.m_WoodStoves.Contains(ws)) Il2Cpp.FireManager.AddWoodStove(ws);

                var allCampfires = s_MasterInterior.GetComponentsInChildren<Il2Cpp.Campfire>(true);
                foreach (var cf in allCampfires) if (cf != null && !Il2Cpp.FireManager.m_Campfires.Contains(cf)) Il2Cpp.FireManager.AddCampfire(cf);

                while (s_MasterInterior != null && !s_MasterInterior.activeInHierarchy) yield return null;
                if (s_MasterInterior == null) yield break;

                yield return null;
                yield return null;

                PreventFireDestructionPatch.s_ProtectInterior = true;
                Il2Cpp.FireManager.Deserialize(FireManagerStealerPatch.s_StolenFireData);
                PreventFireDestructionPatch.s_ProtectInterior = false;

                FireManagerStealerPatch.s_StolenFireData = "";
            }
        }

        private static void DisableInteriorContainerSerialization(GameObject interiorRoot)
        {
            if (interiorRoot == null) return;
            var containers = interiorRoot.GetComponentsInChildren<Il2Cpp.Container>(true);
            foreach (var c in containers)
            {
                if (c != null) c.m_DisableSerialization = true;
            }
        }
    }
}