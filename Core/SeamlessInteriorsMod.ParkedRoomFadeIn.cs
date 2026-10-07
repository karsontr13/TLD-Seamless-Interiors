using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── A PARKED ROOM APPEARS AT ONCE, NOT FADED IN ───
        //
        // Most of a room's LODGroups cross-fade over time (Lake Cabin A: 39 of 66, fadeMode
        // CrossFade, animateCrossFading on, 0.5 s) and cull their last LOD when small on screen. From
        // outside, a parked room stands ParkOffset above the player's eye, so its small objects are
        // culled; the frame the door puts the player inside, they fade in instead of being there.
        // Owner, 2026-09-28, after the room went from 40 to 80 m: "some objects were missing then
        // faded in over a frame or two". With the timed fade switched off on that room in the console
        // he judged it "better".
        //
        // Switched off only for the moment of arrival and put back after, so the game's own fading
        // still runs while the player walks about inside a large room.
        private const float PARKED_FADE_IN_HOLD_SECONDS = 1f;

        internal static void ArriveWithoutCrossFade(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null || instance.Config == null || !instance.Config.IsParked) return;

            var changed = new List<LODGroup>();
            foreach (var g in instance.MasterInterior.GetComponentsInChildren<LODGroup>(true))
            {
                if (g == null || !g.animateCrossFading) continue;
                g.animateCrossFading = false;
                changed.Add(g);
            }
            if (changed.Count > 0) MelonCoroutines.Start(RestoreCrossFadeLater(changed));
        }

        private static IEnumerator RestoreCrossFadeLater(List<LODGroup> changed)
        {
            yield return new WaitForSeconds(PARKED_FADE_IN_HOLD_SECONDS);
            foreach (var g in changed)
                if (g != null) g.animateCrossFading = true;
        }
    }
}
