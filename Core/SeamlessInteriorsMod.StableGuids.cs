using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // A template placeable whose guid was handed out at runtime (SC+ makes fire barrels decorations)
        // gets a new random guid every load; the save then no longer knows it and switches it off.
        // Such guids are replaced by one built from the building and the object's template spot.
        public const string StableGuidMarker = "#stable-guids-1";

        private static readonly HashSet<int> s_StableGuidChecked = new HashSet<int>();
        private static readonly HashSet<string> s_StableGuids = new HashSet<string>();

        // Run before anything is moved: at clone build and again at hydration, for late additions.
        public static int StabilizeRuntimeGuids(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return 0;

            Transform interiorT = instance.MasterInterior.transform;
            var used = new HashSet<string>();
            int changed = 0;

            foreach (var p in InteriorScan.Placeables(instance.MasterInterior))
            {
                if (p == null || p.gameObject == null) continue;
                if (!s_StableGuidChecked.Add(p.GetInstanceID())) continue;
                if (string.IsNullOrEmpty(p.m_Guid) || IsSpawnedPlaceable(instance, p)) continue;

                var og = p.GetComponent<Il2Cpp.ObjectGuid>();
                if (og == null || !og.RuntimeRegistered || og.GetPDID() != p.m_Guid) continue;

                Vector3 lp = interiorT.InverseTransformPoint(p.transform.position);
                string key = $"{instance.Config.ResolvedInstanceId}|{CleanGearName(p.gameObject.name)}|{lp.x:F2}|{lp.y:F2}|{lp.z:F2}";
                string guid = GuidFromKey(key);
                for (int n = 1; !used.Add(guid); n++) guid = GuidFromKey(key + "#" + n);

                string old = p.m_Guid;
                try { Il2CppTLD.PDID.PdidTable.RuntimeAddOrReplace(og, guid); }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"[STABLE-GUID] {instance.Config.ResolvedInstanceId}: '{p.gameObject.name}' kimligi degistirilemedi: {ex.Message}");
                    continue;
                }
                p.m_Guid = guid;
                s_StableGuids.Add(guid);
                changed++;

                if (s_DebugBounds)
                    MelonLogger.Msg($"[STABLE-GUID] {instance.Config.ResolvedInstanceId}: '{p.gameObject.name}' {old} -> {guid}");
            }

            if (changed > 0)
                MelonLogger.Msg($"[STABLE-GUID] {instance.Config.ResolvedInstanceId}: calisirken verilmis {changed} kimlik sabit kimlikle degistirildi.");
            return changed;
        }

        public static bool HasStableGuid(Il2CppTLD.Placement.Placeable p)
        {
            return p != null && !string.IsNullOrEmpty(p.m_Guid) && s_StableGuids.Contains(p.m_Guid);
        }

        private static string GuidFromKey(string key)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return new System.Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).ToString();
        }
    }
}
