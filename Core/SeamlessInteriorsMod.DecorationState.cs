using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    // Curing box contents and framed photos live in the scene save's own lists, keyed by guid,
    // and are handed out at scene load. A clone's box is rebuilt later, so it came back empty.
    public partial class SeamlessInteriorsMod
    {
        // Extra JSON fields for one spawned-placeable record ("" when the object has no state).
        private static string SerializeDecorationState(GameObject go)
        {
            string extra = "";
            try
            {
                var curing = go.GetComponent<CuringStorage>();
                if (curing != null)
                {
                    var state = curing.SerializeCuringStorage();
                    string s = state != null ? Utils.SerializeObject(state) : null;
                    if (!string.IsNullOrEmpty(s)) extra += $",\"cs\":\"{EscapeJson(s)}\"";
                }

                var photo = go.GetComponent<FramedPhoto>();
                if (photo != null)
                {
                    var state = photo.Serialize();
                    string s = state != null ? Utils.SerializeObject(state) : null;
                    if (!string.IsNullOrEmpty(s)) extra += $",\"fp\":\"{EscapeJson(s)}\"";
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[DECOR-STATE] '{go.name}' durumu kaydedilemedi: {ex.Message}");
            }
            return extra;
        }

        // Applies the "cs"/"fp" fields of a spawned-placeable record to the re-created object.
        private static void RestoreDecorationState(GameObject go, string element)
        {
            if (go == null) return;
            ApplyCuringState(go, ReadEscapedField(element, "\"cs\":\""));
            ApplyPhotoState(go, ReadEscapedField(element, "\"fp\":\""));
        }

        private static string ReadEscapedField(string element, string key)
        {
            int idx = element.IndexOf(key, System.StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = idx + key.Length;
            int end = FindClosingQuote(element, start);
            if (end < 0) return null;
            return UnescapeJson(element.Substring(start, end - start));
        }

        private static bool ApplyCuringState(GameObject go, string serialized)
        {
            if (string.IsNullOrEmpty(serialized)) return false;
            var curing = go.GetComponent<CuringStorage>();
            if (curing == null) return false;
            try
            {
                var state = Utils.DeserializeObject<CuringStorageState>(serialized);
                if (state == null) return false;

                // The record may carry an older guid; the live box's own guid wins.
                var current = curing.m_State;
                if (current != null && !string.IsNullOrEmpty(current.m_GUID)) state.m_GUID = current.m_GUID;

                curing.DeserializeCuringStorage(state);
                int count = state.m_CuringItems != null ? state.m_CuringItems.Count : 0;
                MelonLogger.Msg($"[DECOR-STATE] '{go.name}' kur kutusu geri yuklendi ({count} esya).");
                return true;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[DECOR-STATE] '{go.name}' kur kutusu geri yuklenemedi: {ex.Message}");
                return false;
            }
        }

        private static bool ApplyPhotoState(GameObject go, string serialized)
        {
            if (string.IsNullOrEmpty(serialized)) return false;
            var photo = go.GetComponent<FramedPhoto>();
            if (photo == null) return false;
            try
            {
                var state = Utils.DeserializeObject<FramedPhotoState>(serialized);
                if (state == null) return false;

                var current = photo.m_State;
                if (current != null && !string.IsNullOrEmpty(current.m_GUID)) state.m_GUID = current.m_GUID;

                photo.Deserialize(state);
                return true;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[DECOR-STATE] '{go.name}' fotograf geri yuklenemedi: {ex.Message}");
                return false;
            }
        }

        // Pre-mod saves: the interior scene's own curing/photo lists, matched by guid to the
        // spawned objects RestoreSpawnedPlaceables just created.
        private static int TranscodeDecorationStates(SeamlessInteriorInstance instance, SceneSaveGameFormat fmt)
        {
            if (instance?.MasterInterior == null || fmt == null) return 0;

            var byGuid = new System.Collections.Generic.Dictionary<string, GameObject>();
            foreach (var p in InteriorScan.Placeables(instance.MasterInterior))
            {
                if (p == null || p.gameObject == null) continue;
                if (!string.IsNullOrEmpty(p.m_Guid)) byGuid[p.m_Guid] = p.gameObject;
                var og = p.GetComponent<ObjectGuid>();
                string ogGuid = ModGuid(og);
                if (!string.IsNullOrEmpty(ogGuid)) byGuid[ogGuid] = p.gameObject;
            }

            int applied = 0;
            if (fmt.m_CuringStorageSerialized != null)
            {
                foreach (var st in fmt.m_CuringStorageSerialized)
                {
                    if (st == null || string.IsNullOrEmpty(st.m_GUID)) continue;
                    if (!byGuid.TryGetValue(st.m_GUID, out var go)) continue;
                    if (ApplyCuringState(go, Utils.SerializeObject(st))) applied++;
                }
            }
            if (fmt.m_FramedPhotoSerialized != null)
            {
                foreach (var st in fmt.m_FramedPhotoSerialized)
                {
                    if (st == null || string.IsNullOrEmpty(st.m_GUID)) continue;
                    if (!byGuid.TryGetValue(st.m_GUID, out var go)) continue;
                    if (ApplyPhotoState(go, Utils.SerializeObject(st))) applied++;
                }
            }
            return applied;
        }
    }
}
