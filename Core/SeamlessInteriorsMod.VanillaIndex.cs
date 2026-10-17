using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SeamlessInteriors
{
    // What "Prepare for uninstall" needs about a building that the player's files do not hold: the scene's
    // own ids and the clone's place in the region. Fixed facts of the game's scenes, the same for every save,
    // so a building can be written without its region being loaded.
    public class VanillaRoomFacts
    {
        public string Id, Scene, Region, Key;
        public Matrix4x4 WorldToLocal = Matrix4x4.identity;
        public Quaternion InverseRotation = Quaternion.identity;
        public Vector3 BoundsMin, BoundsMax;
        public bool HasTransform;
        public readonly HashSet<string> SceneIds = new HashSet<string>();
        public readonly List<ContainerFact> Containers = new List<ContainerFact>();
        public readonly Dictionary<string, PlaceableFact> Placeables = new Dictionary<string, PlaceableFact>();
        public readonly List<FireFact> Fires = new List<FireFact>();

        public class ContainerFact
        {
            public string Guid, Name, PlaceableGuid;
            public Vector3 Position, RelativePosition;
            public Quaternion Rotation = Quaternion.identity, RelativeRotation = Quaternion.identity;
        }

        public class PlaceableFact { public string Guid, Name, Addressable; }

        public class FireFact
        {
            public string Guid;
            public Vector3 StovePosition, FirePosition;
            public Quaternion FireRotation = Quaternion.identity;
        }

        public Vector3 ToLocal(Vector3 world) { return WorldToLocal.MultiplyPoint3x4(world); }
        public Quaternion ToLocal(Quaternion world) { return InverseRotation * world; }

        public bool ContainsLocal(Vector3 p, float margin)
        {
            return p.x >= BoundsMin.x - margin && p.y >= BoundsMin.y - margin && p.z >= BoundsMin.z - margin
                && p.x <= BoundsMax.x + margin && p.y <= BoundsMax.y + margin && p.z <= BoundsMax.z + margin;
        }
    }

    public partial class SeamlessInteriorsMod
    {
        // ─── Taken in Run, while the clone still holds the untouched scene template ───
        public static void CaptureVanillaFacts(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;
            var f = new VanillaRoomFacts
            {
                Id = instance.Config.ResolvedInstanceId,
                Scene = instance.Config.InteriorSceneBaseName,
                Region = instance.Config.ExteriorSceneName
            };
            Transform root = instance.MasterInterior.transform;
            try
            {
                // Gear and loot pickers the game must count as spawned (see BuildSpawnedInOldSavesList).
                foreach (var g in instance.MasterInterior.GetComponentsInChildren<GearItem>(true))
                    if (g != null) AddId(f.SceneIds, g.GetComponent<ObjectGuid>());
                foreach (var r in instance.MasterInterior.GetComponentsInChildren<RandomSpawnObject>(true))
                    if (r != null) AddId(f.SceneIds, r.GetComponent<ObjectGuid>());

                foreach (var c in instance.MasterInterior.GetComponentsInChildren<Container>(true))
                {
                    if (c == null) continue;
                    var cf = new VanillaRoomFacts.ContainerFact
                    {
                        Guid = SceneGuidOf(c.GetComponent<ObjectGuid>()),
                        Name = CleanGearName(c.gameObject.name),
                        Position = root.InverseTransformPoint(c.transform.position),
                        Rotation = Quaternion.Inverse(root.rotation) * c.transform.rotation
                    };
                    // A container on movable furniture follows the furniture's saved transform.
                    var p = PlaceableAbove(c.transform, root);
                    if (p != null && !string.IsNullOrEmpty(p.m_Guid))
                    {
                        cf.PlaceableGuid = p.m_Guid;
                        cf.RelativePosition = p.transform.InverseTransformPoint(c.transform.position);
                        cf.RelativeRotation = Quaternion.Inverse(p.transform.rotation) * c.transform.rotation;
                    }
                    if (cf.Guid != null) f.Containers.Add(cf);
                }

                foreach (var p in InteriorScan.Placeables(instance.MasterInterior))
                {
                    if (p == null || string.IsNullOrEmpty(p.m_Guid) || f.Placeables.ContainsKey(p.m_Guid)) continue;
                    if (HasStableGuid(p) || IsSpawnedPlaceable(instance, p)) continue;
                    var data = p.Serialize();
                    string s = data != null ? Utils.SerializeObject(data) : null;
                    if (string.IsNullOrEmpty(s)) continue;
                    f.Placeables[p.m_Guid] = new VanillaRoomFacts.PlaceableFact
                    {
                        Guid = p.m_Guid,
                        Name = ExtractString(s, "\"m_Name\":\"", "\""),
                        Addressable = ExtractString(s, "\"m_Addressable\":\"", "\"")
                    };
                }

                foreach (var fire in instance.MasterInterior.GetComponentsInChildren<Fire>(true))
                {
                    if (fire == null) continue;
                    var stove = StoveAbove(fire.transform);
                    string guid = GetFireKey(fire);
                    if (stove == null || string.IsNullOrEmpty(guid)) continue;
                    f.Fires.Add(new VanillaRoomFacts.FireFact
                    {
                        Guid = guid,
                        StovePosition = root.InverseTransformPoint(stove.transform.position),
                        FirePosition = root.InverseTransformPoint(fire.transform.position),
                        FireRotation = Quaternion.Inverse(root.rotation) * fire.transform.rotation
                    });
                }

                // The room's extent, from its meshes' positions; tells which building a world position belongs to.
                bool any = false;
                foreach (var mr in instance.MasterInterior.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr == null) continue;
                    Vector3 lp = root.InverseTransformPoint(mr.transform.position);
                    if (!any) { f.BoundsMin = f.BoundsMax = lp; any = true; }
                    else { f.BoundsMin = Vector3.Min(f.BoundsMin, lp); f.BoundsMax = Vector3.Max(f.BoundsMax, lp); }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KALDIRMA] {f.Id}: sahne bilgileri okunamadi - {ex.Message}",
                                    $"[UNINSTALL] {f.Id}: scene facts could not be read - {ex.Message}");
                return;
            }
            instance.VanillaFacts = f;
        }

        // At the end of Run and at every save: the clone's place and the vanilla key, then the file.
        public static void UpdateVanillaFacts(SeamlessInteriorInstance instance)
        {
            var f = instance?.VanillaFacts;
            if (f == null || instance.MasterInterior == null || !instance.RunCompleted) return;
            Transform root = instance.MasterInterior.transform;
            f.WorldToLocal = root.worldToLocalMatrix;
            f.InverseRotation = Quaternion.Inverse(root.rotation);
            f.HasTransform = true;
            f.Key = ResolveVanillaSceneKey(instance);
            if (string.IsNullOrEmpty(f.Key)) return;
            BorrowSceneFacts(f);

            string text = WriteFacts(f);
            string known;
            if (s_WrittenFacts.TryGetValue(f.Id, out known) && known == text) return;
            string path = GetLocalFactsPath(f.Id);
            try
            {
                if (!File.Exists(path) || File.ReadAllText(path) != text)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, text);
                }
                s_WrittenFacts[f.Id] = text;
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KALDIRMA] {f.Id}: dizin yazilamadi - {ex.Message}", $"[UNINSTALL] {f.Id}: index not written - {ex.Message}");
            }
        }

        private static readonly Dictionary<string, string> s_WrittenFacts = new Dictionary<string, string>();

        // A building copied from another one of the same scene has no PDIDs of its own; the scene's ids are
        // the same in every copy, so they come from a building that has them. Key and place stay its own.
        public static void BorrowSceneFacts(VanillaRoomFacts f)
        {
            if (f == null || f.SceneIds.Count > 0 || f.Containers.Count > 0 || f.Placeables.Count > 0) return;
            foreach (var cfg in SupportedInteriors)
            {
                if (cfg.InteriorSceneBaseName != f.Scene || cfg.ResolvedInstanceId == f.Id) continue;
                SeamlessInteriorInstance other;
                VanillaRoomFacts source = ActiveInteriors.TryGetValue(cfg.ResolvedInstanceId, out other) && other != null ? other.VanillaFacts : null;
                if (source == null || source.Placeables.Count == 0) source = FindVanillaFacts(cfg.ResolvedInstanceId);
                if (source == null || (source.Placeables.Count == 0 && source.Containers.Count == 0)) continue;

                foreach (string id in source.SceneIds) f.SceneIds.Add(id);
                f.Containers.AddRange(source.Containers);
                foreach (var kv in source.Placeables) f.Placeables[kv.Key] = kv.Value;
                f.Fires.AddRange(source.Fires);
                return;
            }
        }

        public static void UpdateAllVanillaFacts()
        {
            foreach (var instance in ActiveInteriors.Values) UpdateVanillaFacts(instance);
        }

        // This machine's index first (it comes from the game as it is now), then the one shipped with the mod.
        public static VanillaRoomFacts FindVanillaFacts(string id)
        {
            try
            {
                string path = GetLocalFactsPath(id);
                if (File.Exists(path))
                {
                    var local = ReadFacts(File.ReadAllText(path));
                    if (local.Count > 0) return local[0];
                }
            }
            catch { }
            VanillaRoomFacts shipped;
            return ShippedFacts().TryGetValue(id, out shipped) ? shipped : null;
        }

        private static string GetLocalFactsPath(string id)
        {
            return Path.Combine(Path.Combine(ModPaths.DataDir(), "VanillaIndex"), id + ".txt");
        }

        private static Dictionary<string, VanillaRoomFacts> s_ShippedFacts;

        private static Dictionary<string, VanillaRoomFacts> ShippedFacts()
        {
            if (s_ShippedFacts != null) return s_ShippedFacts;
            s_ShippedFacts = new Dictionary<string, VanillaRoomFacts>();
            try
            {
                var assembly = typeof(SeamlessInteriorsMod).Assembly;
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (!name.EndsWith("VanillaIndex.txt", System.StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = assembly.GetManifestResourceStream(name))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        foreach (var f in ReadFacts(reader.ReadToEnd())) s_ShippedFacts[f.Id] = f;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[KALDIRMA] Moddaki dizin okunamadi: {ex.Message}", $"[UNINSTALL] The index shipped with the mod could not be read: {ex.Message}");
            }
            return s_ShippedFacts;
        }

        // ─── Helpers ───
        private static void AddId(HashSet<string> set, ObjectGuid og)
        {
            string g = SceneGuidOf(og);
            if (g != null) set.Add(g);
        }

        // The scene's own id of an object: on a clone it often lives in PDID, and containers carry "_CLONE".
        private static string SceneGuidOf(ObjectGuid og)
        {
            if (og == null) return null;
            string g = null;
            try { g = !string.IsNullOrEmpty(og.m_Guid) ? og.m_Guid : og.PDID; } catch { }
            if (!string.IsNullOrEmpty(g) && g.EndsWith("_CLONE")) g = g.Substring(0, g.Length - "_CLONE".Length);
            return string.IsNullOrEmpty(g) ? null : g;
        }

        // GetComponentInParent skips inactive objects, and the clone is inactive in Run.
        private static Il2CppTLD.Placement.Placeable PlaceableAbove(Transform t, Transform stop)
        {
            for (; t != null && t != stop; t = t.parent)
            {
                var p = t.GetComponent<Il2CppTLD.Placement.Placeable>();
                if (p != null) return p;
            }
            return null;
        }

        private static WoodStove StoveAbove(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                var s = t.GetComponent<WoodStove>();
                if (s != null) return s;
            }
            return null;
        }

        // ─── Text form: one record per line, fields split by '|' ───
        //   B|id|scene|region|key   M|16 floats (world to local)   Q|inverse rotation   X|bounds min|bounds max
        //   I|scene gear or loot picker id   C|guid|name|pos|rot|placeable guid|rel pos|rel rot
        //   P|guid|name|addressable   F|guid|stove pos|fire pos|fire rot
        private static string WriteFacts(VanillaRoomFacts f)
        {
            var sb = new StringBuilder();
            sb.Append("B|").Append(f.Id).Append('|').Append(f.Scene).Append('|').Append(f.Region).Append('|').Append(f.Key).Append('\n');
            sb.Append("M");
            for (int i = 0; i < 16; i++) sb.Append(i == 0 ? "|" : " ").Append(Fl(f.WorldToLocal[i]));
            sb.Append('\n');
            sb.Append("Q|").Append(Q(f.InverseRotation)).Append('\n');
            sb.Append("X|").Append(V(f.BoundsMin)).Append('|').Append(V(f.BoundsMax)).Append('\n');
            var ids = new List<string>(f.SceneIds);
            ids.Sort(System.StringComparer.Ordinal);
            foreach (string id in ids) sb.Append("I|").Append(id).Append('\n');
            foreach (var c in f.Containers)
                sb.Append("C|").Append(c.Guid).Append('|').Append(c.Name).Append('|').Append(V(c.Position)).Append('|').Append(Q(c.Rotation))
                  .Append('|').Append(c.PlaceableGuid ?? "").Append('|').Append(V(c.RelativePosition)).Append('|').Append(Q(c.RelativeRotation)).Append('\n');
            var guids = new List<string>(f.Placeables.Keys);
            guids.Sort(System.StringComparer.Ordinal);
            foreach (string g in guids)
            {
                var p = f.Placeables[g];
                sb.Append("P|").Append(p.Guid).Append('|').Append(p.Name ?? "").Append('|').Append(p.Addressable ?? "").Append('\n');
            }
            foreach (var fire in f.Fires)
                sb.Append("F|").Append(fire.Guid).Append('|').Append(V(fire.StovePosition)).Append('|').Append(V(fire.FirePosition)).Append('|').Append(Q(fire.FireRotation)).Append('\n');
            return sb.ToString();
        }

        private static List<VanillaRoomFacts> ReadFacts(string text)
        {
            var result = new List<VanillaRoomFacts>();
            VanillaRoomFacts f = null;
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length < 2 || line[1] != '|') continue;
                string[] a = line.Split('|');
                try
                {
                    switch (line[0])
                    {
                        case 'B':
                            f = new VanillaRoomFacts { Id = a[1], Scene = a[2], Region = a[3], Key = a[4] };
                            result.Add(f);
                            break;
                        case 'M':
                            {
                                string[] n = a[1].Split(' ');
                                var m = new Matrix4x4();
                                for (int i = 0; i < 16; i++) m[i] = PF(n[i]);
                                f.WorldToLocal = m;
                                f.HasTransform = true;
                                break;
                            }
                        case 'Q': f.InverseRotation = PQ(a[1]); break;
                        case 'X': f.BoundsMin = PV(a[1]); f.BoundsMax = PV(a[2]); break;
                        case 'I': f.SceneIds.Add(a[1]); break;
                        case 'C':
                            f.Containers.Add(new VanillaRoomFacts.ContainerFact
                            {
                                Guid = a[1], Name = a[2], Position = PV(a[3]), Rotation = PQ(a[4]),
                                PlaceableGuid = a[5].Length > 0 ? a[5] : null, RelativePosition = PV(a[6]), RelativeRotation = PQ(a[7])
                            });
                            break;
                        case 'P': f.Placeables[a[1]] = new VanillaRoomFacts.PlaceableFact { Guid = a[1], Name = a[2], Addressable = a[3] }; break;
                        case 'F':
                            f.Fires.Add(new VanillaRoomFacts.FireFact { Guid = a[1], StovePosition = PV(a[2]), FirePosition = PV(a[3]), FireRotation = PQ(a[4]) });
                            break;
                    }
                }
                catch { /* a damaged line is skipped, the rest of the building still reads */ }
            }
            return result;
        }

        private static string Fl(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        private static string V(Vector3 v) { return Fl(v.x) + " " + Fl(v.y) + " " + Fl(v.z); }
        private static string Q(Quaternion q) { return Fl(q.x) + " " + Fl(q.y) + " " + Fl(q.z) + " " + Fl(q.w); }
        private static float PF(string s) { return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }

        private static Vector3 PV(string s)
        {
            string[] n = s.Split(' ');
            return new Vector3(PF(n[0]), PF(n[1]), PF(n[2]));
        }

        private static Quaternion PQ(string s)
        {
            string[] n = s.Split(' ');
            return new Quaternion(PF(n[0]), PF(n[1]), PF(n[2]), PF(n[3]));
        }
    }
}
