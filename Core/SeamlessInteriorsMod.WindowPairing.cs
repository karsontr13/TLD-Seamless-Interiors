using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    // WHICH ROOM WINDOW IS WHICH WINDOW OF THE BUILDING, DECIDED FOR THE WHOLE BUILDING AT ONCE.
    //
    // Both window systems used to pair one window at a time: the nearest window facing the same way.
    // That is right only while the room stands where the building does. Rooms are larger than their
    // shells and their origins differ, and at Camp Trailer E the room's windows sit about 4 m along
    // from the building's, which is further than the windows are apart: "nearest" took the neighbor.
    // With 6 windows outside and 4 in the room, three were paired, two of them to the wrong window,
    // and three panes showed the dark backing (owner, 2026-09-18: "a few of them were black ... My
    // guess is that you have the interior and exterior windows not correlated properly").
    //
    // A room is a rigid thing, so its windows are slid as ONE pattern: every way of giving each room
    // window a building window that faces the same way is tried, and the one that leaves the least
    // disagreement after a single common slide wins. Measured in the running game before this was
    // written: Trailers E and B, 48 cm of disagreement for the right layout against 94 cm for the
    // room turned half a circle, and 55 to 57 cm on Trailers A, C and D, whose counts agree and
    // whose pairing was never in doubt. (The same fit is what showed Trailer E's 35 degree turn,
    // until then resting on a tie-break, to be the right way round.)
    public partial class SeamlessInteriorsMod
    {
        private const float WINDOW_PATTERN_SAME_FACING = 0.85f;
        // Two layouts whose plan errors differ by less than this are a tie, and height decides.
        private const float WINDOW_PATTERN_PLAN_TIE = 0.005f;
        private const int WINDOW_PATTERN_SEARCH_BUDGET = 200000;

        // roomCenters must already be in the BUILDING's frame (park offset taken off).
        // roomToShell[i] is the building window for room window i, or -1. meanSlide is the common
        // slide, building window to room window, of the pairs that were made.
        // THE SAME FIT IS NOT WORKED OUT TWICE. The view into a room and the views out of it pair the
        // same windows, 36 ms at the hangar each time (evidence 0923-13). They hand the room's windows
        // over from different places (the room parked, or carried onto its building), but the fit
        // tries the pairs themselves as slides, so moving every room window by one amount gives the
        // same pairing. So the last answer is kept with the windows it was for, measured from their
        // first one, and reused when the next call's windows agree to a millimeter; the slide is then
        // worked out afresh from the caller's own positions. EXCEPT after the step-by-step fallback,
        // which starts from the nearest windows and so does depend on where the room stands: that
        // answer is reused only for windows in exactly the same places.
        private static List<Vector3> s_LastFitRoom, s_LastFitRoomOutward, s_LastFitShell, s_LastFitShellOutward;
        private static int[] s_LastFitRoomToShell;
        private static bool s_LastFitResult, s_LastFitByNearness, s_FitByNearness;
        private const float WINDOW_PATTERN_SAME_INPUT = 0.001f;

        private static bool SameShape(List<Vector3> a, List<Vector3> b, bool relative)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            if (a.Count == 0) return true;
            Vector3 a0 = relative ? a[0] : Vector3.zero, b0 = relative ? b[0] : Vector3.zero;
            for (int i = 0; i < a.Count; i++)
            {
                Vector3 d = (a[i] - a0) - (b[i] - b0);
                if (Mathf.Abs(d.x) > WINDOW_PATTERN_SAME_INPUT || Mathf.Abs(d.y) > WINDOW_PATTERN_SAME_INPUT || Mathf.Abs(d.z) > WINDOW_PATTERN_SAME_INPUT) return false;
            }
            return true;
        }

        internal static bool FitWindowPattern(List<Vector3> roomCenters, List<Vector3> roomOutward,
                                              List<Vector3> shellCenters, List<Vector3> shellOutward,
                                              out int[] roomToShell, out Vector3 meanSlide)
        {
            if (s_LastFitRoomToShell != null
                && SameShape(roomCenters, s_LastFitRoom, !s_LastFitByNearness) && SameShape(roomOutward, s_LastFitRoomOutward, false)
                && SameShape(shellCenters, s_LastFitShell, false) && SameShape(shellOutward, s_LastFitShellOutward, false))
            {
                roomToShell = (int[])s_LastFitRoomToShell.Clone();
                meanSlide = Vector3.zero;
                if (!s_LastFitResult) return false;
                return MeanSlideOf(roomCenters, shellCenters, roomToShell, out meanSlide);
            }

            s_FitByNearness = false;
            bool fitted = FitWindowPatternFresh(roomCenters, roomOutward, shellCenters, shellOutward, out roomToShell, out meanSlide);
            s_LastFitByNearness = s_FitByNearness;
            s_LastFitRoom = new List<Vector3>(roomCenters); s_LastFitRoomOutward = new List<Vector3>(roomOutward);
            s_LastFitShell = new List<Vector3>(shellCenters); s_LastFitShellOutward = new List<Vector3>(shellOutward);
            s_LastFitRoomToShell = (int[])roomToShell.Clone();
            s_LastFitResult = fitted;
            return fitted;
        }

        private static bool FitWindowPatternFresh(List<Vector3> roomCenters, List<Vector3> roomOutward,
                                                  List<Vector3> shellCenters, List<Vector3> shellOutward,
                                                  out int[] roomToShell, out Vector3 meanSlide)
        {
            roomToShell = new int[roomCenters.Count];
            for (int i = 0; i < roomToShell.Length; i++) roomToShell[i] = -1;
            meanSlide = Vector3.zero;
            if (roomCenters.Count == 0 || shellCenters.Count == 0) return false;

            // A BIG BUILDING IS NOT SEARCHED EXHAUSTIVELY. The search below is exact and fine for the
            // 3 to 8 windows it was written for. At the Pleasant Valley farmhouse (18 room windows,
            // 27 panes) it burned its whole budget, 450 ms in one frame every time the building was
            // set up, and then fell back to the step-by-step fit anyway (measured 2026-09-19).
            if (roomCenters.Count * shellCenters.Count > WINDOW_PATTERN_EXHAUSTIVE_UP_TO)
            {
                if (!FitWindowPatternByGuesses(roomCenters, roomOutward, shellCenters, shellOutward, roomToShell)) return false;
                return MeanSlideOf(roomCenters, shellCenters, roomToShell, out meanSlide);
            }

            var search = new PatternSearch
            {
                RoomCenters = roomCenters, RoomOutward = roomOutward, ShellCenters = shellCenters, ShellOutward = shellOutward,
                Current = new int[roomCenters.Count], Best = new int[roomCenters.Count], Used = new bool[shellCenters.Count],
                BestPairs = -1, BestError = float.MaxValue, Budget = WINDOW_PATTERN_SEARCH_BUDGET
            };
            search.Step(0, 0);

            if (search.Budget <= 0 || search.BestPairs <= 0)
            {
                // Too many same-facing windows to try every layout (a hangar), or nothing faced the
                // same way. Nearest first, then re-paired after the slide those pairs imply, until
                // it stops changing: the same idea, without the guarantee.
                s_FitByNearness = true;
                if (!FitWindowPatternBySteps(roomCenters, roomOutward, shellCenters, shellOutward, roomToShell)) return false;
            }
            else
            {
                for (int i = 0; i < roomToShell.Length; i++) roomToShell[i] = search.Best[i];
            }

            int pairs = 0;
            for (int i = 0; i < roomToShell.Length; i++)
            {
                if (roomToShell[i] < 0) continue;
                meanSlide += roomCenters[i] - shellCenters[roomToShell[i]];
                pairs++;
            }
            if (pairs == 0) return false;
            meanSlide /= pairs;
            return true;
        }

        private const int WINDOW_PATTERN_EXHAUSTIVE_UP_TO = 100;     // room windows x panes; the Camp Office is 8 x 8

        // A row's PairInPlaceWithinCm: a room already standing on its building, each window paired with the
        // nearest same-facing one, nearest pairs first. The mine's 86 windows defeated the pattern search.
        internal static bool PairWindowsInPlace(List<Vector3> roomCenters, List<Vector3> roomOutward,
                                                List<Vector3> shellCenters, List<Vector3> shellOutward, float within,
                                                out int[] roomToShell, out Vector3 meanSlide)
        {
            int rooms = roomCenters.Count, shells = shellCenters.Count;
            roomToShell = new int[rooms];
            for (int i = 0; i < rooms; i++) roomToShell[i] = -1;
            meanSlide = Vector3.zero;

            // Plain floats: Vector3 arithmetic calls into native code in this interop build (see below).
            var rx = new float[rooms]; var ry = new float[rooms]; var rz = new float[rooms];
            for (int i = 0; i < rooms; i++) { Vector3 c = roomCenters[i]; rx[i] = c.x; ry[i] = c.y; rz[i] = c.z; }
            var sx = new float[shells]; var sy = new float[shells]; var sz = new float[shells];
            for (int s = 0; s < shells; s++) { Vector3 c = shellCenters[s]; sx[s] = c.x; sy[s] = c.y; sz[s] = c.z; }

            var pairs = new List<KeyValuePair<float, int>>();
            float limit = within * within;
            for (int i = 0; i < rooms; i++)
                for (int s = 0; s < shells; s++)
                {
                    if (Vector3.Dot(shellOutward[s], roomOutward[i]) < WINDOW_PATTERN_SAME_FACING) continue;
                    float dx = rx[i] - sx[s], dy = ry[i] - sy[s], dz = rz[i] - sz[s];
                    float d = dx * dx + dy * dy + dz * dz;
                    if (d <= limit) pairs.Add(new KeyValuePair<float, int>(d, i * shells + s));
                }
            pairs.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));

            var shellUsed = new bool[shells];
            foreach (var pair in pairs)
            {
                int i = pair.Value / shells, s = pair.Value % shells;
                if (roomToShell[i] >= 0 || shellUsed[s]) continue;
                roomToShell[i] = s; shellUsed[s] = true;
            }
            return MeanSlideOf(roomCenters, shellCenters, roomToShell, out meanSlide);
        }

        private static bool MeanSlideOf(List<Vector3> roomCenters, List<Vector3> shellCenters, int[] roomToShell, out Vector3 meanSlide)
        {
            meanSlide = Vector3.zero; int pairs = 0;
            for (int i = 0; i < roomToShell.Length; i++)
            {
                if (roomToShell[i] < 0) continue;
                meanSlide += roomCenters[i] - shellCenters[roomToShell[i]];
                pairs++;
            }
            if (pairs == 0) return false;
            meanSlide /= pairs;
            return true;
        }

        // EVERY "THIS ROOM WINDOW IS THAT PANE" IS A GUESS AT THE SLIDE. Under each guess the windows are
        // paired nearest first, the slide is taken again from those pairs and the pairing repeated
        // until it holds, and the result is scored exactly as the exhaustive search scores: most
        // pairs, then least spread on the plan, then least spread in height. At the farmhouse: the
        // same 17 pairs as before, in milliseconds.
        //
        // IT TOOK 5.8 SECONDS AT THE HANGAR, 42 WINDOWS AGAINST 42, measured 2026-09-22 with lap timers
        // through the outside-in setup, and it froze the game on the way up to the building and again
        // on the way in. Three things fixed it, each proven in the console against the old version on
        // the hangar's real windows and on 60 random layouts, half of them grids full of exact ties,
        // with every pairing identical:
        //   - THE DISTANCES ARE PLAIN FLOATS. Vector3 arithmetic in this interop build calls into the
        //     game's native code, and the old loop made hundreds of millions of those calls. This
        //     alone took the hangar from 3207 ms to 31 ms on the same inputs; the two changes below
        //     had taken it only to 220.
        //   - NEAREST FIRST IS ONE SORT, not a fresh scan of every remaining pair for every pick.
        //     Equal distances are put back in pair order, which is the order the old scan met them
        //     in and kept the first of, so a tie goes the same way it always did.
        //   - A GUESS WHOSE FIRST PAIRING HAS ALREADY BEEN SEEN IS SKIPPED. Everything after the first
        //     pairing follows from it alone, so a repeat can only reach a layout already scored, and
        //     an equal score never replaces the best. Most guesses at a big building are repeats.
        //
        // The scoring still uses Vector3: it runs once per distinct layout, not once per distance.
        private static bool FitWindowPatternByGuesses(List<Vector3> roomCenters, List<Vector3> roomOutward,
                                                      List<Vector3> shellCenters, List<Vector3> shellOutward, int[] roomToShell)
        {
            int rooms = roomCenters.Count, shells = shellCenters.Count;

            // Every allowed pair, room window major, which is the order the old nested scan used.
            var pairRoomList = new List<int>(); var pairShellList = new List<int>();
            for (int i = 0; i < rooms; i++)
                for (int s = 0; s < shells; s++)
                    if (Vector3.Dot(shellOutward[s], roomOutward[i]) >= WINDOW_PATTERN_SAME_FACING) { pairRoomList.Add(i); pairShellList.Add(s); }
            int pairCount = pairRoomList.Count;
            int[] pairRoom = pairRoomList.ToArray(), pairShell = pairShellList.ToArray();

            Vector3[] room = roomCenters.ToArray(), shell = shellCenters.ToArray();
            var rx = new float[rooms]; var ry = new float[rooms]; var rz = new float[rooms];
            for (int i = 0; i < rooms; i++) { rx[i] = room[i].x; ry[i] = room[i].y; rz[i] = room[i].z; }
            var sx = new float[shells]; var sy = new float[shells]; var sz = new float[shells];
            for (int s = 0; s < shells; s++) { sx[s] = shell[s].x; sy[s] = shell[s].y; sz[s] = shell[s].z; }

            var order = new int[pairCount]; var distance = new float[pairCount];
            var map = new int[rooms]; var next = new int[rooms]; var roomUsed = new bool[rooms]; var used = new bool[shells];
            var seenFirstPairings = new HashSet<string>(); var key = new System.Text.StringBuilder();
            int bestPairs = -1; float bestError = float.MaxValue, bestErrorUp = float.MaxValue;

            for (int g = 0; g < pairCount; g++)
            {
                Vector3 slide = room[pairRoom[g]] - shell[pairShell[g]];
                bool have = false, repeat = false;

                for (int pass = 0; pass < 4; pass++)
                {
                    float lx = slide.x, ly = slide.y, lz = slide.z;
                    for (int k = 0; k < pairCount; k++)
                    {
                        int i = pairRoom[k], s = pairShell[k];
                        order[k] = k;
                        float dx = (rx[i] - lx) - sx[s], dy = (ry[i] - ly) - sy[s], dz = (rz[i] - lz) - sz[s];
                        distance[k] = dx * dx + dy * dy + dz * dz;
                    }
                    System.Array.Sort(distance, order);
                    for (int a = 0; a < pairCount; )
                    {
                        int b = a + 1;
                        while (b < pairCount && distance[b] == distance[a]) b++;
                        if (b - a > 1) System.Array.Sort(order, a, b - a);
                        a = b;
                    }

                    for (int i = 0; i < rooms; i++) { next[i] = -1; roomUsed[i] = false; }
                    for (int s = 0; s < shells; s++) used[s] = false;
                    for (int k = 0; k < pairCount; k++)
                    {
                        int p = order[k], i = pairRoom[p], s = pairShell[p];
                        if (roomUsed[i] || used[s]) continue;
                        next[i] = s; roomUsed[i] = true; used[s] = true;
                    }

                    if (pass == 0)
                    {
                        key.Clear();
                        for (int i = 0; i < rooms; i++) key.Append(next[i]).Append(',');
                        if (!seenFirstPairings.Add(key.ToString())) { repeat = true; break; }
                    }

                    bool same = have;
                    for (int i = 0; i < rooms; i++) { if (map[i] != next[i]) same = false; map[i] = next[i]; }
                    have = true;
                    if (same) break;

                    Vector3 sum = Vector3.zero; int counted = 0;
                    for (int i = 0; i < rooms; i++) if (map[i] >= 0) { sum += room[i] - shell[map[i]]; counted++; }
                    if (counted == 0) break;
                    slide = sum / counted;
                }
                if (repeat) continue;

                int pairs = 0; Vector3 mean = Vector3.zero;
                for (int i = 0; i < rooms; i++) if (map[i] >= 0) { mean += room[i] - shell[map[i]]; pairs++; }
                if (pairs == 0) continue;
                mean /= pairs;
                float error = 0f, errorUp = 0f;
                for (int i = 0; i < rooms; i++)
                {
                    if (map[i] < 0) continue;
                    Vector3 off = room[i] - shell[map[i]] - mean;
                    errorUp += Mathf.Abs(off.y);
                    off.y = 0f; error += off.magnitude;
                }
                error /= pairs; errorUp /= pairs;

                bool better = pairs > bestPairs
                           || (pairs == bestPairs && error < bestError - WINDOW_PATTERN_PLAN_TIE)
                           || (pairs == bestPairs && Mathf.Abs(error - bestError) <= WINDOW_PATTERN_PLAN_TIE && errorUp < bestErrorUp);
                if (!better) continue;
                bestPairs = pairs; bestError = error; bestErrorUp = errorUp;
                for (int i = 0; i < rooms; i++) roomToShell[i] = map[i];
            }

            return bestPairs > 0;
        }

        private sealed class PatternSearch
        {
            public List<Vector3> RoomCenters, RoomOutward, ShellCenters, ShellOutward;
            public int[] Current, Best;
            public bool[] Used;
            public int BestPairs, Budget;
            public float BestError;
            public float BestErrorUp = float.MaxValue;

            public void Step(int room, int pairsSoFar)
            {
                if (Budget-- <= 0) return;

                // Cannot beat the best count of pairs any more.
                if (pairsSoFar + (RoomCenters.Count - room) < BestPairs) return;

                if (room == RoomCenters.Count)
                {
                    if (pairsSoFar == 0) return;
                    Vector3 mean = Vector3.zero;
                    for (int i = 0; i < Current.Length; i++) if (Current[i] >= 0) mean += Flat(RoomCenters[i] - ShellCenters[Current[i]]);
                    mean /= pairsSoFar;
                    float error = 0f;
                    for (int i = 0; i < Current.Length; i++) if (Current[i] >= 0) error += (Flat(RoomCenters[i] - ShellCenters[Current[i]]) - mean).magnitude;
                    error /= pairsSoFar;

                    // HEIGHT BREAKS A TIE ON THE PLAN, AND ONLY A TIE. The fit is judged flat because a
                    // room's windows and its building's do not agree in height from one building to
                    // the next (rooms are scaled, small windows sit at other sill heights), and that
                    // must not outvote the plan. But Safe House A has two windows ONE OVER THE OTHER
                    // (building windows 2 and 6: 4 cm apart on the plan, 276 cm apart in height), and
                    // flat, the two ways of pairing them are the same layout: whichever the search
                    // met first stood. It met the wrong one. Owner, 2026-09-18: "The top window is
                    // looking through the bottom and the bottom window is looking through the top."
                    // Measured: the lower pane had been given the room window 395 cm up and the upper
                    // pane the one 138 cm up. Between layouts that agree on the plan to within half a
                    // centimeter, the one whose heights agree better now wins. It is the only building
                    // of LakeRegion's 15 with stacked windows, so no other pairing there can change.
                    float meanUp = 0f;
                    for (int i = 0; i < Current.Length; i++) if (Current[i] >= 0) meanUp += RoomCenters[i].y - ShellCenters[Current[i]].y;
                    meanUp /= pairsSoFar;
                    float errorUp = 0f;
                    for (int i = 0; i < Current.Length; i++) if (Current[i] >= 0) errorUp += Mathf.Abs(RoomCenters[i].y - ShellCenters[Current[i]].y - meanUp);
                    errorUp /= pairsSoFar;

                    // More pairs first; between equal counts, the layout that agrees best.
                    bool better = pairsSoFar > BestPairs
                               || (pairsSoFar == BestPairs && error < BestError - WINDOW_PATTERN_PLAN_TIE)
                               || (pairsSoFar == BestPairs && Mathf.Abs(error - BestError) <= WINDOW_PATTERN_PLAN_TIE && errorUp < BestErrorUp);
                    if (better)
                    {
                        BestPairs = pairsSoFar; BestError = error; BestErrorUp = errorUp;
                        for (int i = 0; i < Current.Length; i++) Best[i] = Current[i];
                    }
                    return;
                }

                for (int s = 0; s < ShellCenters.Count; s++)
                {
                    if (Used[s]) continue;
                    if (Vector3.Dot(ShellOutward[s], RoomOutward[room]) < WINDOW_PATTERN_SAME_FACING) continue;
                    Used[s] = true; Current[room] = s;
                    Step(room + 1, pairsSoFar + 1);
                    Used[s] = false;
                }

                // A room window with no window on the building (Lake Cabin D: 4 in the room, 3 outside).
                Current[room] = -1;
                Step(room + 1, pairsSoFar);
            }

            private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        }

        private static bool FitWindowPatternBySteps(List<Vector3> roomCenters, List<Vector3> roomOutward,
                                                    List<Vector3> shellCenters, List<Vector3> shellOutward, int[] roomToShell)
        {
            Vector3 slide = Vector3.zero;
            bool any = false;
            for (int pass = 0; pass < 6; pass++)
            {
                var used = new bool[shellCenters.Count];
                var next = new int[roomCenters.Count];
                Vector3 sum = Vector3.zero; int pairs = 0;
                for (int i = 0; i < roomCenters.Count; i++)
                {
                    next[i] = -1; float bestDistance = float.MaxValue;
                    for (int s = 0; s < shellCenters.Count; s++)
                    {
                        if (used[s] || Vector3.Dot(shellOutward[s], roomOutward[i]) < WINDOW_PATTERN_SAME_FACING) continue;
                        float distance = (roomCenters[i] - slide - shellCenters[s]).sqrMagnitude;
                        if (distance < bestDistance) { bestDistance = distance; next[i] = s; }
                    }
                    if (next[i] < 0) continue;
                    used[next[i]] = true; sum += roomCenters[i] - shellCenters[next[i]]; pairs++;
                }
                if (pairs == 0) return any;

                bool same = any;
                for (int i = 0; i < next.Length; i++) { if (roomToShell[i] != next[i]) same = false; roomToShell[i] = next[i]; }
                any = true;
                if (same) break;
                slide = sum / pairs;
            }
            return any;
        }
    }
}
