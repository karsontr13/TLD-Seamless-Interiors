using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Fixes a specific save/load edge case: if the player saves and reloads while
        // standing inside the cloned interior, the clone/shell active-state and the
        // player's Y position can end up out of sync with where they actually are
        // (e.g. the exterior shell shows instead of the interior, or the player is
        // clipped slightly below the floor). This runs shortly after a load to detect
        // and correct both problems.
        public static IEnumerator DelayedSaveLoadVisibilityFix()
        {
            yield return new WaitForSeconds(0.1f);
            if (!s_RunCompleted) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) yield break;

            bool isInside = IsPositionInsideCabin(playerT.position);

            if (s_DebugBounds)
                MelonLogger.Msg($"[SAVE-LOAD-FIX] isInside={isInside} | pos={playerT.position}");

            bool shellActiveWrong = s_ExteriorShell != null && s_ExteriorShell.activeSelf == isInside;
            bool interiorActiveWrong = s_MasterInterior != null && s_MasterInterior.activeSelf != isInside;
            if (shellActiveWrong || interiorActiveWrong)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg("[SAVE-LOAD-FIX] Yanlis gorunum tespit edildi, duzeltiliyor.");
                ApplyInitialSyncState(playerT.position);
            }

            if (isInside && s_InteriorTrigger != null && s_MasterInterior != null)
            {
                Vector3 localPos = s_MasterInterior.transform.InverseTransformPoint(playerT.position);
                float boundsMinY = s_InteriorTrigger.center.y - s_InteriorTrigger.size.y / 2f;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[SAVE-LOAD-FIX] localPos.y={localPos.y:F2} boundsMinY={boundsMinY:F2}");

                if (localPos.y < boundsMinY + 0.5f)
                {
                    Vector3 fixedLocal = new Vector3(localPos.x, boundsMinY + 1.5f, localPos.z);
                    playerT.position = s_MasterInterior.transform.TransformPoint(fixedLocal);
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[SAVE-LOAD-FIX] Oyuncu Y duzeltildi: {playerT.position}");
                }
            }
        }
    }
}