using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── TURNING A PARKED ROOM TO ITS SHELL ───
        //
        // In the author's design a room and its shell are never seen together: the shell is
        // switched off the moment the room is switched on. So a hand-typed RotationOffset was close
        // enough, and nobody could tell. Window portals show the two TOGETHER, the room through the
        // shell's glass and the world through the room's, and then the error is the first thing seen.
        // Measured live on 2026-09-18, angle between a room's window normals and its shell's,
        // identical on every window of a building: Camp Office 0.0 (which is why everything tuned
        // there lined up), Lake Cabin A_3 -1.3, E -2.7, Trailer D -4.2, A_1 7.8, F 9.5, Trailer B
        // -11.6, Trailer C -15.7, C -16.2, SafeHouseA 26.4, Trailer E 35.0, Trailer A 43.0, A_2 54.2.
        // A snow drift 75 cm wide on a room 9.5 degrees off its wall stands 6 cm proud at one end;
        // past about 32 degrees the windows cannot pair at all and the building gets no portals.
        //
        // PARKING IS WHAT MAKES IT FIXABLE. A room standing on its shell could not be turned without
        // moving its walls through the shell's. A room 40 m up can be turned freely, about its own
        // origin, so it does not move.
        //
        // THE ANGLE COMES FROM THE WINDOWS, not from the two transforms. For every building but one
        // the correction equals shell yaw minus room yaw, but SafeHouseA breaks that rule (its
        // transforms say -90, its windows 26.4), and the windows are the thing that has to line up.
        // Every (room window, shell window) pair proposes the yaw that would bring their normals
        // together; a proposal is scored by how many room windows then land near a shell window
        // facing the same way. Positions are what tell a building's true yaw from the same yaw plus
        // a quarter or half turn, which the normals of a rectangular building cannot.
        //
        // "NEAR" IS LOOSE, AND ACROSS THE GROUND ONLY, and both were learned from a dry run of this
        // rule against all fifteen LakeRegion buildings on 2026-09-18, before it was allowed to turn
        // anything. A room is not a copy of its shell: TLD's interiors are larger than the buildings
        // they are in, so after the right turn a room's windows still stand 0.8 to 2.9 m from their
        // shell twins, and no single sideways shift brings them together (tried: it matched fewer,
        // not more). And a room stands about 1.5 m higher than its shell on top of ParkOffset, so
        // height is left out. With a 6 m reach the right yaw matched every window on every building
        // and the half turn matched fewer on all but the two trailers whose shells have six windows
        // to their rooms' four. There the transforms break the tie, which is all they are trusted
        // for.
        //
        // IT FAILS SHUT. Fewer matches than the rule below and the room is left exactly as the
        // author placed it, with one warning in the log. A building with no measurable shell
        // windows (a burnt ruin, an unparked building, a sub-interior) is never turned.
        //
        // WHAT A SAVE HOLDS IS UNAFFECTED: items, containers and the player's own position inside a
        // room are all saved in the room's local space and come back through its transform.

        // A room window and a shell window further apart than this across the ground, after the
        // turn, are not the same window. See "NEAR IS LOOSE" above.
        private const float PARKED_YAW_MATCH_DISTANCE = 6f;

        // A proposal this close to shell yaw minus room yaw is the one the transforms agree with.
        private const float PARKED_YAW_TRANSFORM_AGREEMENT = 1f;

        // Normals within this of each other, after the turn, face the same way (about 8 degrees).
        private const float PARKED_YAW_MATCH_DOT = 0.99f;

        // Computes and applies the turn. Called at the end of AlignWithExteriorShell, which has just
        // set the author's rotation, so calling it again starts from the same place every time.
        private static void TurnParkedRoomToShell(SeamlessInteriorInstance instance)
        {
            if (instance == null) return;
            instance.ParkedYawCorrection = 0f;

            if (instance.Config == null || !instance.Config.IsParked) return;
            if (instance.MasterInterior == null || instance.ExteriorShell == null) return;

            // A turn set by hand for this building wins over the rule. See BuildingWindowOverride.RoomTurnDegrees.
            BuildingWindowOverride byHand;
            if (TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out byHand) && !float.IsNaN(byHand.RoomTurnDegrees))
            {
                instance.MasterInterior.transform.rotation = Quaternion.AngleAxis(byHand.RoomTurnDegrees, Vector3.up) * instance.MasterInterior.transform.rotation;
                instance.ParkedYawCorrection = byHand.RoomTurnDegrees;
                if (s_DebugBounds)
                    MelonLogger.Msg($"[ODA-DONUS] {instance.Config.ResolvedInstanceId}: oda kabuga elle verilen {byHand.RoomTurnDegrees:F1} derece donduruldu.",
                                    $"[ROOM-TURN] {instance.Config.ResolvedInstanceId}: room turned {byHand.RoomTurnDegrees:F1} degrees to match its building, as set by hand for this building.");
                return;
            }

            WindowSpot[] spots;
            if (!TryGetShellWindows(instance, out spots)) return;

            var openings = FindInteriorWindowOpenings(instance);
            if (openings.Count == 0) return;

            string id = instance.Config.ResolvedInstanceId;
            Transform shell = instance.ExteriorShell.transform;
            Transform room = instance.MasterInterior.transform;
            Vector3 pivot = room.position;

            var shellCenters = new Vector3[spots.Length];
            var shellNormals = new Vector3[spots.Length];
            for (int s = 0; s < spots.Length; s++)
            {
                shellCenters[s] = shell.TransformPoint(new Vector3(spots[s].X, spots[s].Y, spots[s].Z));
                shellNormals[s] = Flat(shell.TransformDirection(new Vector3(spots[s].NX, 0f, spots[s].NZ)));
            }

            var roomNormals = new Vector3[openings.Count];
            for (int i = 0; i < openings.Count; i++) roomNormals[i] = Flat(openings[i].Outward);

            float transformYaw = Mathf.DeltaAngle(room.eulerAngles.y, shell.eulerAngles.y);

            int bestMatched = 0;
            bool bestAgrees = false;
            float bestMeanSqr = float.MaxValue;
            float bestYaw = 0f;
            var tried = new List<float>();

            for (int i = 0; i < openings.Count; i++)
            {
                for (int s = 0; s < spots.Length; s++)
                {
                    float yaw = Vector3.SignedAngle(roomNormals[i], shellNormals[s], Vector3.up);

                    bool seen = false;
                    for (int t = 0; t < tried.Count; t++)
                        if (Mathf.Abs(Mathf.DeltaAngle(tried[t], yaw)) < 0.25f) { seen = true; break; }
                    if (seen) continue;
                    tried.Add(yaw);

                    int matched;
                    float cost, refine;
                    ScoreParkedYaw(yaw, pivot, openings, roomNormals, shellCenters, shellNormals, out matched, out cost, out refine);

                    if (matched == 0) continue;

                    // More windows landing wins. Between equals, the one the transforms agree with
                    // wins, and only then the closer landing.
                    bool agrees = Mathf.Abs(Mathf.DeltaAngle(yaw, transformYaw)) <= PARKED_YAW_TRANSFORM_AGREEMENT;
                    bool better = matched > bestMatched;
                    if (!better && matched == bestMatched)
                    {
                        if (agrees != bestAgrees) better = agrees;
                        else better = cost < bestMeanSqr;
                    }
                    if (better)
                    {
                        bestMatched = matched;
                        bestAgrees = agrees;
                        bestMeanSqr = cost;
                        bestYaw = yaw + refine;
                    }
                }
            }

            // All but one of the windows both sides have, and never fewer than two: one window fixes
            // a yaw but cannot tell it from a wrong one.
            int needed = Mathf.Max(2, Mathf.Min(openings.Count, spots.Length) - 1);
            if (bestMatched < needed)
            {
                MelonLogger.Warning($"[ODA-DONUS] {id}: oda kabuga DONDURULMEDI. En iyi aci {bestYaw:F1} derece yalnizca " +
                                    $"{bestMatched} pencere eslestirdi (gereken {needed}; oda {openings.Count}, kabuk {spots.Length}).",
                                    $"[ROOM-TURN] {id}: room NOT turned to match its building. The best angle, {bestYaw:F1} degrees, lined up only " +
                                    $"{bestMatched} windows (needed {needed}; the room has {openings.Count}, the building {spots.Length}).");
                return;
            }

            room.rotation = Quaternion.AngleAxis(bestYaw, Vector3.up) * room.rotation;
            instance.ParkedYawCorrection = bestYaw;

            // The refusal above is always logged. A turn that went through is routine, and is said
            // only with Verbose Logging on (F7).
            if (s_DebugBounds)
                MelonLogger.Msg($"[ODA-DONUS] {id}: oda kabuga {bestYaw:F1} derece donduruldu, {bestMatched} pencere eslesti " +
                                $"(oda {openings.Count}, kabuk {spots.Length}), ortalama sapma {Mathf.Sqrt(bestMeanSqr):F2} m, transformlar {transformYaw:F1} diyor.",
                                $"[ROOM-TURN] {id}: room turned {bestYaw:F1} degrees to match its building, {bestMatched} windows lined up " +
                                $"(the room has {openings.Count}, the building {spots.Length}), on average {Mathf.Sqrt(bestMeanSqr):F2} m from their twins; comparing the two objects' rotations alone would give {transformYaw:F1} degrees.");
        }

        // How well a proposed yaw lands the room's windows on the shell's. cost is the mean squared
        // distance of the matched windows; refine is the mean angle still left between matched
        // normals, which takes out the noise of whichever single pair proposed the yaw.
        private static void ScoreParkedYaw(float yaw, Vector3 pivot,
                                           List<InteriorWindowOpening> openings, Vector3[] roomNormals,
                                           Vector3[] shellCenters, Vector3[] shellNormals,
                                           out int matched, out float cost, out float refine)
        {
            matched = 0;
            cost = float.MaxValue;
            refine = 0f;

            Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
            var taken = new bool[shellCenters.Length];
            float sumSqr = 0f;
            float sumAngle = 0f;

            for (int i = 0; i < openings.Count; i++)
            {
                // Turned about the room's origin. Compared across the ground only, so neither the
                // park nor the room's own height above its shell comes into it.
                Vector3 center = pivot + turn * (openings[i].GlassCenter - pivot);
                Vector3 normal = turn * roomNormals[i];

                int best = -1;
                float bestSqr = PARKED_YAW_MATCH_DISTANCE * PARKED_YAW_MATCH_DISTANCE;
                for (int s = 0; s < shellCenters.Length; s++)
                {
                    if (taken[s]) continue;
                    if (Vector3.Dot(normal, shellNormals[s]) < PARKED_YAW_MATCH_DOT) continue;
                    float dx = center.x - shellCenters[s].x;
                    float dz = center.z - shellCenters[s].z;
                    float sqr = dx * dx + dz * dz;
                    if (sqr < bestSqr) { bestSqr = sqr; best = s; }
                }
                if (best < 0) continue;

                taken[best] = true;
                matched++;
                sumSqr += bestSqr;
                sumAngle += Vector3.SignedAngle(normal, shellNormals[best], Vector3.up);
            }

            if (matched == 0) return;
            cost = sumSqr / matched;
            refine = sumAngle / matched;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        // WHERE A HAND-TYPED INTERIOR POSITION IS NOW. The config's entry points are world positions
        // authored against a room standing on its shell at the author's rotation. A parked room has
        // moved out from under them by ParkOffset, and a turned one has also turned about its own
        // origin, so the point is carried the same way. With no turn this is exactly the old
        // "+ ParkOffset". Positions read from objects INSIDE the room have already moved with it and
        // must not come through here.
        internal static Vector3 ParkedInteriorPoint(SeamlessInteriorInstance instance, Vector3 authored)
        {
            if (instance == null || instance.Config == null) return authored;

            Vector3 park = instance.Config.ParkOffset;
            if (instance.MasterInterior == null) return authored + park;

            Vector3 pivot = instance.MasterInterior.transform.position - park;
            Vector3 fromPivot = authored - pivot;

            // ...AND IT WAS AUTHORED AGAINST A STRETCHED ROOM. A parked room loads at its own size
            // (see AlignWithExteriorShell), so a point typed for a room 15% wider stands up to a
            // meter from the same spot of the real one: at Grey Mother's house or the farmhouse that
            // is the difference between the hall and the inside of a wall, 40 m up. The stretch was
            // about the room's own origin and along the room's own axes as the author had them, so
            // it is taken out the same way: into the room's frame before any turn of ours, divided
            // by the author's scale, and back.
            Vector3 stretch = instance.Config.ScaleAdjustment;
            if (instance.Config.IsParked && stretch != Vector3.one && stretch.x > 0.01f && stretch.y > 0.01f && stretch.z > 0.01f)
            {
                Quaternion asAuthored = Quaternion.AngleAxis(-instance.ParkedYawCorrection, Vector3.up) * instance.MasterInterior.transform.rotation;
                Vector3 inRoom = Quaternion.Inverse(asAuthored) * fromPivot;
                inRoom = new Vector3(inRoom.x / stretch.x, inRoom.y / stretch.y, inRoom.z / stretch.z);
                fromPivot = asAuthored * inRoom;
            }

            if (instance.ParkedYawCorrection == 0f) return pivot + fromPivot + park;
            return pivot + Quaternion.AngleAxis(instance.ParkedYawCorrection, Vector3.up) * fromPivot + park;
        }

        // WHAT THE GAME PUTS BACK WHERE A PARKED ROOM USED TO STAND BELONGS IN THE ROOM.
        //
        // The author adopts loose objects found inside a room's volume into the room; the game's own
        // save restores a building's loose loot at the building, and adoption is how it becomes the
        // room's. A parked room is not at the building any more, so that loot lay on the ground in
        // and around the shell and the room stayed bare. Seen 2026-09-18 at Lake Cabin A_3, never
        // entered in that save: an open book and a paper stack lying outside, a room owning 0 items;
        // carried the way the room was carried, both stand under its roof at EXACTLY (0.00 m) the
        // room-local spots of the same two items in its sister cabin A_1. Moved there by hand in the
        // running game first, and the owner confirmed them inside.
        //
        // So an object outside the room's bounds is judged as if carried (up by ParkOffset, and
        // round with the room if the room was turned). Three guards. Only a FILLED room: before the
        // fill the room's own items are not there to be compared with. Never an object that has a
        // twin in the room at the carried spot: that is a leaked COPY, and the purge's to delete,
        // or the room ends up with two of everything (the same dry run counted 17 such copies round
        // the 15 LakeRegion buildings). And the caller still demands a roof overhead at the carried
        // spot, the author's own test, so something left on a porch is not dragged indoors.
        internal static bool TryCarryStrayToParkedRoom(SeamlessInteriorInstance instance, Bounds filter, ref Vector3 pos)
        {
            if (instance == null || instance.Config == null || !instance.Config.IsParked) return false;
            if (instance.MasterInterior == null || !instance.ContentHydrated) return false;

            Vector3 carried = ParkedInteriorPoint(instance, pos);
            if (!filter.Contains(carried)) return false;

            pos = carried;
            return true;
        }

        private static bool RoomOwnsTwinAt(SeamlessInteriorInstance instance, string cleanName, Vector3 at)
        {
            float sqrRadius = LEAKED_GEAR_MATCH_RADIUS * LEAKED_GEAR_MATCH_RADIUS;
            foreach (var owned in instance.MasterInterior.GetComponentsInChildren<Il2Cpp.GearItem>(true))
            {
                if (owned == null || owned.gameObject == null) continue;
                if ((owned.transform.position - at).sqrMagnitude > sqrRadius) continue;
                if (CleanGearName(owned.gameObject.name) == cleanName) return true;
            }
            return false;
        }

        // Places an adopted object where the room is now. Returns false, and moves nothing, when
        // the room already owns its twin there. The caller reparents it afterwards as it always did.
        internal static bool CarryStrayIntoParkedRoom(SeamlessInteriorInstance instance, Transform stray, Vector3 at)
        {
            if (RoomOwnsTwinAt(instance, CleanGearName(stray.gameObject.name), at)) return false;

            stray.rotation = Quaternion.AngleAxis(instance.ParkedYawCorrection, Vector3.up) * stray.rotation;
            stray.position = at;
            MelonLogger.Msg($"[ADOPT] {instance.Config.ResolvedInstanceId}: '{stray.gameObject.name}' binanin oldugu yerde kalmisti, park edilmis odaya tasindi.",
                            $"[ADOPT] {instance.Config.ResolvedInstanceId}: '{stray.gameObject.name}' was lying where the room used to stand, at the building; carried up into the room.");
            return true;
        }
    }
}
