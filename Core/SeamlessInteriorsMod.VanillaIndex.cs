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
        public string ExportKey;   // the key this save's export wrote it under
        public Matrix4x4 WorldToLocal = Matrix4x4.identity;
        public Quaternion InverseRotation = Quaternion.identity;
        public Vector3 BoundsMin, BoundsMax;
        public bool HasTransform;
        public readonly HashSet<string> SceneIds = new HashSet<string>();
        public readonly List<ContainerFact> Containers = new List<ContainerFact>();
        public readonly Dictionary<string, PlaceableFact> Placeables = new Dictionary<string, PlaceableFact>();
        public readonly List<FireFact> Fires = new List<FireFact>();
        public readonly List<DoorFact> Doors = new List<DoorFact>();

        // A door into this building. Towns with random layouts (Milton) hold one per layout at the same spot;
        // the layout is a RandomSpawnObject choice the region's save keeps (VariantPdid picked VariantName).
        public class DoorFact
        {
            public string Guid, VariantPdid, VariantName;
            public bool Instanced, HasPosition;
            public Vector3 Position;
        }

        // The key for the layout this save has; null with absent = true when the building is not in it.
        // A door's id differs from save to save, so it is read from the save's own door records by position.
        public string KeyFor(Dictionary<string, HashSet<string>> chosen, List<KeyValuePair<Vector3, string>> savedDoors,
                             out bool absent, out bool guessed)
        {
            absent = false; guessed = false;
            if (Doors.Count == 0) { guessed = true; return Key; }
            bool unknown = false;
            foreach (var d in Doors)
            {
                HashSet<string> names;
                if (!string.IsNullOrEmpty(d.VariantPdid))
                {
                    if (chosen == null || !chosen.TryGetValue(d.VariantPdid, out names)) { unknown = true; continue; }
                    if (!names.Contains(d.VariantName)) continue;
                }
                return DoorKey(d, savedDoors, out guessed);
            }
            if (unknown) { guessed = true; return Key; }
            absent = true;
            return null;
        }

        private string DoorKey(DoorFact d, List<KeyValuePair<Vector3, string>> savedDoors, out bool guessed)
        {
            guessed = false;
            if (!d.Instanced) return Scene;
            if (d.HasPosition && savedDoors != null)
                foreach (var s in savedDoors)
                    if ((s.Key - d.Position).sqrMagnitude < 0.25f) return Scene + "_" + s.Value;
            guessed = true;
            return Scene + "_" + d.Guid;
        }

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
            if (f.Doors.Count == 0 && string.IsNullOrEmpty(instance.Config.LegacySceneKeyOverride)) CaptureDoorFacts(instance, f);
            BorrowSceneFacts(f);

            // Some rooms (TrailerE) get their contents after Run; taken again once, while nothing has moved yet.
            if (f.Containers.Count == 0 && f.Placeables.Count == 0 && !instance.VanillaFactsRetaken && !instance.ContentHydrated)
            {
                instance.VanillaFactsRetaken = true;
                CaptureVanillaFacts(instance);
                if (instance.VanillaFacts == null || instance.VanillaFacts == f) return;
                instance.VanillaFacts.WorldToLocal = f.WorldToLocal;
                instance.VanillaFacts.InverseRotation = f.InverseRotation;
                instance.VanillaFacts.HasTransform = true;
                instance.VanillaFacts.Key = f.Key;
                f = instance.VanillaFacts;
                if (f.Containers.Count == 0 && f.Placeables.Count == 0) return;
            }

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

        // Every door of this building's scene at the building's spot, active or not: a random layout the save
        // did not pick still has its door there, under another RandomSpawnObject candidate.
        private const float DOOR_SPOT_RADIUS = 15f;

        private static void CaptureDoorFacts(SeamlessInteriorInstance instance, VanillaRoomFacts f)
        {
            Il2CppSystem.Collections.Generic.List<LoadScene> doors = null;
            try { doors = LoadScene.m_LoadScenesList; } catch { }
            if (doors == null) return;

            var mine = new List<LoadScene>();
            LoadScene nearest = null;
            float nearestDist = 60f;
            for (int i = 0; i < doors.Count; i++)
            {
                LoadScene d = doors[i];
                if (d == null || d.gameObject == null || d.m_SceneToLoad != f.Scene || string.IsNullOrEmpty(d.m_GUID)) continue;
                if (IsUnderAnyMasterInterior(d.transform)) continue;
                mine.Add(d);
                float dist = Vector3.Distance(d.transform.position, instance.Config.FallbackPosition);
                if (dist < nearestDist) { nearestDist = dist; nearest = d; }
            }
            if (nearest == null) return;

            var seen = new HashSet<string>();
            foreach (var d in mine)
            {
                if (Vector3.Distance(d.transform.position, nearest.transform.position) > DOOR_SPOT_RADIUS) continue;
                string pdid, name;
                VariantOf(d.transform, out pdid, out name);
                if (!seen.Add(d.m_GUID + "|" + pdid + "|" + name)) continue;
                f.Doors.Add(new VanillaRoomFacts.DoorFact
                {
                    Guid = d.m_GUID, Instanced = d.m_SceneCanBeInstanced, VariantPdid = pdid, VariantName = name,
                    Position = d.transform.position, HasPosition = true
                });
            }
        }

        // The RandomSpawnObject candidate a door sits under, if any.
        private static Dictionary<int, KeyValuePair<string, string>> s_VariantCandidates;
        private static int s_VariantCandidatesGeneration = -1;

        private static void VariantOf(Transform t, out string pdid, out string name)
        {
            pdid = null; name = null;
            if (s_VariantCandidates == null || s_VariantCandidatesGeneration != s_SceneGeneration)
            {
                s_VariantCandidates = new Dictionary<int, KeyValuePair<string, string>>();
                s_VariantCandidatesGeneration = s_SceneGeneration;
                foreach (var rso in Object.FindObjectsOfType<RandomSpawnObject>(true))
                {
                    if (rso == null || rso.m_ObjectList == null || IsUnderAnyMasterInterior(rso.transform)) continue;
                    string id = SceneGuidOf(rso.GetComponent<ObjectGuid>());
                    if (id == null) continue;
                    foreach (var o in rso.m_ObjectList)
                        if (o != null) s_VariantCandidates[o.GetInstanceID()] = new KeyValuePair<string, string>(id, o.name);
                }
            }
            for (; t != null; t = t.parent)
            {
                KeyValuePair<string, string> v;
                if (s_VariantCandidates.TryGetValue(t.gameObject.GetInstanceID(), out v)) { pdid = v.Key; name = v.Value; return; }
            }
        }

        // Which candidates the region's own save picked: RandomSpawnObject id -> picked object names.
        public static Dictionary<string, HashSet<string>> ReadVariantChoices(string rsoBlob)
        {
            var result = new Dictionary<string, HashSet<string>>();
            if (string.IsNullOrEmpty(rsoBlob)) return result;
            var list = Utils.DeserializeObject<RandomSpawnObjectSaveList>(rsoBlob);
            if (list == null || list.m_SaveDataList == null) return result;
            foreach (var sd in list.m_SaveDataList)
            {
                if (sd == null || string.IsNullOrEmpty(sd.m_PDID) || string.IsNullOrEmpty(sd.m_ObjectName)) continue;
                HashSet<string> names;
                if (!result.TryGetValue(sd.m_PDID, out names)) result[sd.m_PDID] = names = new HashSet<string>();
                names.Add(sd.m_ObjectName);
            }
            return result;
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
        //   D|door guid (of the save it was taken from)|instanced 1/0|layout RandomSpawnObject id|layout candidate|door pos
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
            foreach (var d in f.Doors)
                sb.Append("D|").Append(d.Guid).Append('|').Append(d.Instanced ? "1" : "0").Append('|').Append(d.VariantPdid ?? "").Append('|').Append(d.VariantName ?? "")
                  .Append('|').Append(d.HasPosition ? V(d.Position) : "").Append('\n');
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
                        case 'D':
                            {
                                var door = new VanillaRoomFacts.DoorFact
                                {
                                    Guid = a[1], Instanced = a[2] == "1",
                                    VariantPdid = a[3].Length > 0 ? a[3] : null, VariantName = a[4].Length > 0 ? a[4] : null
                                };
                                if (a.Length > 5 && a[5].Length > 0) { door.Position = PV(a[5]); door.HasPosition = true; }
                                f.Doors.Add(door);
                                break;
                            }
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
