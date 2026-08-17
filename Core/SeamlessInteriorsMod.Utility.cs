using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        public static Vector3 EnsureAboveGround(Vector3 pos, SeamlessInteriorInstance instance)
        {
            if (instance.MasterInterior == null || !instance.MasterInterior.activeSelf)
                return pos;

            // Pozisyonun çok üstünden başlayarak aşağı raycast at
            Vector3 rayOrigin = new Vector3(pos.x, pos.y + 5.0f, pos.z);
            float maxDist = 10.0f;

            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, maxDist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float bestFloorY = float.MinValue;

            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (hit.collider.transform.root.name.Contains("CHARACTER_FPSPlayer")) continue;

                // Sadece klon sahnenin zeminini kabul et
                if (hit.collider.transform.IsChildOf(instance.MasterInterior.transform))
                {
                    if (hit.point.y > bestFloorY && hit.point.y <= pos.y + 3.0f)
                    {
                        bestFloorY = hit.point.y;
                    }
                }
            }

            if (bestFloorY > float.MinValue)
            {
                // Zemin bulundu — oyuncuyu zeminin biraz üstüne koy
                float safeY = bestFloorY + 0.15f;
                if (pos.y < safeY)
                    return new Vector3(pos.x, safeY, pos.z);
            }

            return pos;
        }
    }
}
