using System.Collections.Generic;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // TLD 2.58 removed ObjectGuid.m_Guid. The mod used it as its own id slot (empty in scenes),
        // so the value now lives here, keyed by the component's instance id.
        private static readonly Dictionary<int, string> s_ModGuids = new Dictionary<int, string>();

        // What ObjectGuid.m_Guid held on 2.55: "" until the mod set it.
        internal static string ModGuid(Il2Cpp.ObjectGuid og)
        {
            if (og == null) return "";
            string g;
            return s_ModGuids.TryGetValue(og.GetInstanceID(), out g) ? g : "";
        }

        internal static void SetModGuid(Il2Cpp.ObjectGuid og, string guid)
        {
            if (og == null) return;
            if (string.IsNullOrEmpty(guid)) s_ModGuids.Remove(og.GetInstanceID());
            else s_ModGuids[og.GetInstanceID()] = guid;
        }
    }
}
