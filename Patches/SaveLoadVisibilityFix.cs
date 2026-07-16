using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        public static IEnumerator DelayedSaveLoadVisibilityFix()
        {
            yield return new WaitForSeconds(0.1f);
            if (!s_RunCompleted) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) yield break;

            bool isInside = IsPositionInsideCabin(playerT.position);

            if (s_DebugBounds)
                MelonLogger.Msg($"[SAVE-LOAD-FIX] isInside={isInside} | pos={playerT.position}");

            // Görünürlük yanlışsa düzelt
            bool shellActiveWrong = s_ExteriorShell != null && s_ExteriorShell.activeSelf == isInside;
            bool interiorActiveWrong = s_MasterInterior != null && s_MasterInterior.activeSelf != isInside;
            if (shellActiveWrong || interiorActiveWrong)
            {
                if (s_DebugBounds)
                    MelonLogger.Msg("[SAVE-LOAD-FIX] Yanlis gorunum tespit edildi, duzeltiliyor.");
                ApplyInitialSyncState(playerT.position);
            }

            // Y düzeltmesi — görünürlükten bağımsız, her zaman kontrol et
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
