using Il2Cpp;
using MelonLoader;
using System.Collections;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // Margin added to the real interior bounds when building the ParticleKiller
        // trigger. 3m extra on Y (stops snow leaking through sloped/curved roofs),
        // 1m on XZ.
        // NOTE: SeamlessInteriorInstance.TRIGGER_EXPANSION is the inverse of this
        // value - the two must be changed together.
        private static readonly Vector3 TRIGGER_PADDING = new Vector3(1.0f, 3.0f, 1.0f);

        // Padding for the WEATHER KILL VOLUMES ONLY, deliberately separate from
        // TRIGGER_PADDING above. The trigger box is also the safehouse collider and the
        // volume IsPositionInVolume tests against (it subtracts TRIGGER_EXPANSION back
        // out to recover the true room), so widening the kill volumes through the shared
        // constant would push the safehouse 4.7m out past every exterior wall.
        //
        // The values come from what the base game gives its own indoor spaces, measured
        // in the Mindful Cabin (Forsaken Airfield) against its 12.0 x 10.2 x 11.7m
        // IndoorSpace trigger during a blizzard:
        //
        //   FallingSnowParticleKiller  14.49 x 10.00 x 14.25  -> 1.25m past the room on XZ
        //   BlowingSnowParticleKiller  21.39 x 11.00 x 21.12  -> 4.70m past the room on XZ
        //
        // Blowing snow travels sideways and needs the far bigger catch volume; falling
        // snow does not, and an oversized falling volume would carve a visible snow-free
        // bubble around the building that the player can see through the windows.
        // Bounds.Expand adds to the TOTAL size, so these are twice the per-side figure.
        //
        // Y keeps the author's original 3m rather than vanilla's, which is barely any:
        // vanilla's falling volume is actually SHORTER than its room on Y. 3m is the
        // margin that stops snow leaking through sloped and curved roofs.
        private static readonly Vector3 FALLING_KILLER_PADDING = new Vector3(2.5f, 3.0f, 2.5f);
        private static readonly Vector3 BLOWING_KILLER_PADDING = new Vector3(9.4f, 3.0f, 9.4f);

        // Below this much rotation the clone is axis-aligned with the world.
        private const float KILLER_ROTATION_EPSILON_DEGREES = 0.5f;

        // Builds this clone's weather kill volumes: the dimensions of the prefab we brought
        // into the world, plus padding on every side.
        //
        // The bounds are measured HERE rather than taken from the caller. Every caller used
        // to pass the ComputeLocalInteriorBounds result, which is a floor-plan approximation
        // that drops sparse and thin rectangles and so comes back short on a long building
        // (see TryComputePrefabLocalBounds for what it discards and why). A kill volume that
        // is short is a hole snow falls through, so it takes the prefab's real extent and
        // the approximation is left to the callers that actually want a room shape.
        private static void BuildParticleKillerSlices(SeamlessInteriorInstance instance,
                                                      GameObject particleKillerObj,
                                                      Bounds fallbackLocalBounds)
        {
            var uniStorm = GetCachedUniStorm();

            // Out of the game's list BEFORE the set is rebuilt. Clearing only the mod's copy left a
            // set that was in force orphaned in the game's list for good: at the hangar on
            // 2026-09-22 the list held 144 killers at room height, two full sets of 72.
            if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
            {
                var inForce = uniStorm.m_WeatherParticleManager.m_AllParticleKillers;
                foreach (var pk in instance.CustomKillers) inForce.Remove(pk);
            }
            instance.CustomKillers.Clear();

            if (uniStorm == null || uniStorm.m_WeatherParticleManager == null) return;

            if (instance.Config != null && instance.Config.IsParked)
            {
                return;   // no killer for a parked room: see "A PARKED ROOM GETS NO KILLER" below
            }

            Bounds localBounds;
            int meshesUsed, meshesSkipped;
            if (TryComputePrefabLocalBounds(instance.MasterInterior, out localBounds,
                                            out meshesUsed, out meshesSkipped))
            {
                if (s_DebugBounds)
                    MelonLogger.Msg($"[KILL-BOUNDS] {instance.Config.ResolvedInstanceId}: prefab extent " +
                                    $"{localBounds.size.x:F1} x {localBounds.size.y:F1} x {localBounds.size.z:F1} " +
                                    $"from {meshesUsed} meshes ({meshesSkipped} skipped); " +
                                    $"floor-plan approximation was " +
                                    $"{fallbackLocalBounds.size.x:F1} x {fallbackLocalBounds.size.y:F1} x {fallbackLocalBounds.size.z:F1}");
            }
            else
            {
                // No meshes at all is not a case worth inventing a number for; fall back to
                // what the caller measured rather than leaving the building uncovered.
                localBounds = fallbackLocalBounds;
                MelonLogger.Warning($"[KILL-BOUNDS] {instance.Config.ResolvedInstanceId}: no usable meshes, " +
                                    "falling back to the floor-plan bounds for the kill volumes.");
            }

            // Two families with different reach, as the base game does it. Previously one
            // set carried both flags at TRIGGER_PADDING, which is 0.5m per side on XZ -
            // roughly a ninth of the room the game gives itself for blowing snow.
            AddKillerFamily(instance, particleKillerObj, localBounds, FALLING_KILLER_PADDING,
                            killsFalling: true, killsBlowing: false);
            AddKillerFamily(instance, particleKillerObj, localBounds, BLOWING_KILLER_PADDING,
                            killsFalling: false, killsBlowing: true);
        }

        private static void AddKillerFamily(SeamlessInteriorInstance instance,
                                            GameObject particleKillerObj,
                                            Bounds localBounds,
                                            Vector3 padding,
                                            bool killsFalling,
                                            bool killsBlowing)
        {
            Bounds expandedBounds = localBounds;
            expandedBounds.Expand(padding);

            Transform killerT = particleKillerObj.transform;

            // WHY SLICE AT ALL: a killer takes a WORLD-SPACE AABB, so a rotated clone's
            // volume has to be approximated by an axis-aligned one. Cutting it into a grid
            // first and enveloping each piece separately tracks the rotated volume far more
            // tightly than one envelope around the whole thing, which for a 45 degree
            // building overshoots by about 40 percent on each horizontal axis.
            //
            // With NO rotation that reasoning disappears entirely: the union of a grid
            // partition of a box is exactly the box, so the slices were only ever costing
            // per-particle tests for identical coverage. Most configs are unrotated, so
            // emit one killer for those instead of up to 36.
            bool rotated = Quaternion.Angle(killerT.rotation, Quaternion.identity) > KILLER_ROTATION_EPSILON_DEGREES;

            int sliceCountX = 1;
            int sliceCountZ = 1;

            if (rotated)
            {
                // Slice on both Z and X to form a 2D grid. A single axis is not enough
                // for wide or irregular buildings (e.g. the hangar).
                //
                // AT MOST 2 x 2 PER FAMILY, not the 6 x 6 it was. The game runs at most 32 killers at
                // once (WeatherParticleManager.MAX_ACTIVE_PARTICLE_KILLERS, from the dump), and one
                // rotated building held up to 72, so part of its cover could never be in force. The
                // owner, 2026-09-22, after seeing the hangar's 72 drawn live: "It needs to be at most a
                // few Rectangular prisms." Vanilla covers its L-shaped control tower with two boxes.
                // A 2 x 2 grid of a room turned 45 degrees overshoots each side by about a fifth of the
                // room's width, against none at 6 x 6 and nearly half with one box.
                sliceCountZ = 2;
                sliceCountX = Mathf.Max(1, Mathf.CeilToInt(expandedBounds.size.x / expandedBounds.size.z * sliceCountZ));
                if (sliceCountX > 2) sliceCountX = 2;   // cap the slice count
            }

            float sliceZ = expandedBounds.size.z / sliceCountZ;
            float sliceX = expandedBounds.size.x / sliceCountX;
            float startZ = expandedBounds.center.z - (expandedBounds.size.z / 2f) + (sliceZ / 2f);
            float startX = expandedBounds.center.x - (expandedBounds.size.x / 2f) + (sliceX / 2f);

            for (int ix = 0; ix < sliceCountX; ix++)
            {
                for (int iz = 0; iz < sliceCountZ; iz++)
                {
                    var pki = new Il2CppTLD.WeatherParticle.WeatherParticleManager.ParticleKillerInstance();
                    pki.m_OwnerGameObject = particleKillerObj;
                    pki.m_KillsFallingSnow = killsFalling;
                    pki.m_KillsBlowingSnow = killsBlowing;

                    Vector3 c = new Vector3(startX + (ix * sliceX), expandedBounds.center.y, startZ + (iz * sliceZ));
                    Vector3 e = new Vector3(sliceX / 2f, expandedBounds.size.y / 2f, sliceZ / 2f);

                    // Killers take world-space AABBs, so transform the eight corners
                    // and take their axis-aligned envelope.
                    Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 corner = c + new Vector3(
                            ((k & 1) == 0) ? e.x : -e.x,
                            ((k & 2) == 0) ? e.y : -e.y,
                            ((k & 4) == 0) ? e.z : -e.z);

                        Vector3 wp = killerT.TransformPoint(corner);
                        min = Vector3.Min(min, wp);
                        max = Vector3.Max(max, wp);
                    }

                    Bounds sliceAABB = new Bounds();
                    sliceAABB.SetMinMax(min, max);
                    sliceAABB.Expand(0.5f);   // small overlap so no gap forms between slices

                    pki.m_Bounds = sliceAABB;
                    instance.CustomKillers.Add(pki);
                }
            }
        }

        // ─── A PARKED ROOM GETS NO KILLER ───
        //
        // From 2026-09-22 a parked room had one killer, the world box round the room plus 5 m (the
        // hangar's set by the owner's eye), replacing slices that spread over 135 x 135 m at the
        // hangar. It turned out to do nothing. While the player is in a parked room the snow is born
        // at the ground (ParkedWeather.cs) and kept in a box moved down to the window cameras
        // (Patches/ParkedSnowBoxPatches.cs), so no flake exists at the room's height to kill.
        // Tested 2026-09-28 in a Mystery Lake blizzard with no killer registered for any room: the
        // Camp Office with its killer removed, then Lake Cabin A, which never had one; owner, both
        // times: no snow inside. The game also holds at most 32 killers in force and the region's
        // own filled all 32. Rooms left in place still get the slices above.

        // Rebuilds only the particle killers for an instance whose trigger object
        // already exists (used after a reload).
        private void SetupWeatherParticleKillersOnly(SeamlessInteriorInstance instance, Bounds localBounds)
        {
            if (instance.MasterInterior == null) return;

            var particleKillerT = instance.MasterInterior.transform.Find("ParticleKiller");
            if (particleKillerT == null) return;

            // The kill volumes apply their own padding now, so the raw bounds go in.
            BuildParticleKillerSlices(instance, particleKillerT.gameObject, localBounds);
        }

        // First-time setup: creates the ParticleKiller object, its trigger volume,
        // the particle killer slices and the building's vanilla indoor space.
        private void SetupWeatherAndParticles(SeamlessInteriorInstance instance, Bounds localBounds)
        {
            Bounds expandedBounds = localBounds;
            expandedBounds.Expand(TRIGGER_PADDING);

            GameObject particleKillerObj = new GameObject("ParticleKiller");
            particleKillerObj.transform.SetParent(instance.MasterInterior.transform, false);
            particleKillerObj.transform.localPosition = Vector3.zero;
            particleKillerObj.transform.localRotation = Quaternion.identity;
            particleKillerObj.layer = LayerMask.NameToLayer("TriggerIgnoreRaycast");

            // Trigger volume covering the whole interior; also used as the safehouse
            // collider and for inside/outside tests.
            BoxCollider triggerBox = particleKillerObj.AddComponent<BoxCollider>();
            triggerBox.isTrigger = true;
            triggerBox.center = expandedBounds.center;
            triggerBox.size = expandedBounds.size;
            instance.InteriorTrigger = triggerBox;

            // NOT expandedBounds: the kill volumes reach much further than the trigger box
            // and apply their own padding, so they take the raw interior bounds.
            BuildParticleKillerSlices(instance, particleKillerObj, localBounds);

            // The box stays a plain volume: the doors, not physics, move the player in and out.
            CreateVanillaIndoorSpace(instance);
        }

        // Unregisters an instance's particle killer from the weather manager.
        public static void ResetWeatherParticles(SeamlessInteriorInstance instance)
        {
            if (instance.ParticleKiller != null)
            {
                var uniStorm = GetCachedUniStorm();
                if (uniStorm != null && uniStorm.m_WeatherParticleManager != null)
                {
                    uniStorm.m_WeatherParticleManager.m_AllParticleKillers.Remove(instance.ParticleKiller);
                }
                instance.ParticleKiller = null;
            }
        }

        // ─── Wind audio loop restart ───

        public static void ForceRestartWindAudio(Il2Cpp.Wind wind, string tag)
        {
            if (wind == null) return;

            uint before = wind.m_WindLoopAudioInstance;

            // Drop the stale / dead instance.
            wind.ForceStopAudioLoop();

            // ForceStopAudioLoop may have set the flag - clear it again.
            wind.m_WindAudioForceStopped = false;
            wind.m_WindLoopAudioInstance = 0;

            // Restart and STORE THE RETURNED ID (the step the old code skipped).
            wind.m_WindLoopAudioInstance = wind.PlayProceduralWindAudio();

            MelonLogger.Msg($"[WIND-RESTART:{tag}] loop id {before} -> {wind.m_WindLoopAudioInstance}, " +
                            $"forceStopped={wind.m_WindAudioForceStopped}, occl=({DescribeAudioOcclusion()})");
        }

        public static IEnumerator RestoreWindAudioAfterLoad()
        {
            // Let Wwise and the scene settle.
            yield return new WaitForSeconds(1.5f);

            var winds = UnityEngine.Object.FindObjectsOfType<Il2Cpp.Wind>();
            if (winds == null || winds.Length == 0)
            {
                MelonLogger.Msg("[WIND-RESTORE] Sahnede Wind bulunamadi.");
                yield break;
            }

            MelonLogger.Msg($"[WIND-RESTORE] {winds.Length} Wind bulundu, insideClone={s_IsPlayerInsideClone}, occl=({DescribeAudioOcclusion()})");

            // ONLY RESTART A LOOP THAT IS ACTUALLY DEAD.
            //
            // This used to stop and repost every Wind on every load, OUTDOORS INCLUDED, a few
            // lines after the mod had logged "the player is outside, the wind was left alone".
            // Measured 2026-09-22: the two region loads where this ran (Forsaken Airfield, twice)
            // came up with a long silence and then an abrupt loud gust, while the two loads where
            // it did not run (Timberwolf Mountain, Sundered Pass) gave the game's normal low
            // continuous bed. Throwing away a healthy loop to post a replacement is not free.
            foreach (var w in winds)
            {
                if (w == null) continue;

                if (w.m_WindLoopAudioInstance == 0)
                {
                    ForceRestartWindAudio(w, "after-load");
                    continue;
                }

                MelonLogger.Msg($"[WIND-RESTORE] Calisan dongu korundu, id={w.m_WindLoopAudioInstance}.",
                                $"[WIND-RESTORE] The running loop was left alone, id={w.m_WindLoopAudioInstance}.");
            }

            // Then put the loop where the player's current inside/outside state says it belongs.
            ApplyOutdoorWindForInsideState(s_IsPlayerInsideClone, "after-load");

            // Diagnostics: watch for the state degrading over the next 10 seconds.
            // Verbose logging only - doing 5 FindObjectOfType + log passes after
            // every load has no value in normal play.
            if (!s_DebugBounds) yield break;

            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForSeconds(2f);
                var w0 = UnityEngine.Object.FindObjectOfType<Il2Cpp.Wind>();
                if (w0 == null) continue;
                MelonLogger.Msg($"[WIND-WATCH t+{(i + 1) * 2}s] id={w0.m_WindLoopAudioInstance} " +
                                $"forceStopped={w0.m_WindAudioForceStopped} occluded={w0.m_PlayerOccluded} " +
                                $"mph={w0.GetSpeedMPH():F1} occl=({DescribeAudioOcclusion()})");
            }
        }

        // ─── Wind fix-up (after Run) ───

        private IEnumerator FixWindAfterRun(SeamlessInteriorInstance instance)
        {
            // Give the wind system a few frames to stabilise.
            yield return new WaitForSeconds(2f);

            var wind = UnityEngine.Object.FindObjectOfType<Il2Cpp.Wind>();
            if (wind == null) yield break;

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) yield break;

            bool playerInside = s_IsPlayerInsideClone;

            // Fallback: if the flag was wrongly left false, trust the saved state - but only
            // once the saved state is corroborated by where the player actually IS.
            //
            // Trusting the saved id on its own was a way for the flag to get stuck on: the
            // PlayerPrefs entry is addressed by save name, the game reuses slot names, and a
            // leftover entry from another save turned a player standing in the open into a
            // permanently wind-sheltered one at indoor temperature. The three tests below
            // are what tells a real "I saved indoors" from a leftover entry.
            if (!playerInside)
            {
                string savedId = GetSavedPlayerInsideInstanceId();
                SeamlessInteriorInstance savedInstance = null;
                if (!string.IsNullOrEmpty(savedId))
                    ActiveInteriors.TryGetValue(savedId, out savedInstance);

                bool reallyInside = savedInstance != null
                                    && savedInstance.MasterInterior != null
                                    && savedInstance.MasterInterior.activeInHierarchy
                                    && savedInstance.IsPositionInside(playerT.position);

                if (reallyInside)
                {
                    playerInside = true;
                    MarkPlayerInside(savedInstance, "ruzgar duzeltmesi");
                    SetAudioOcclusion(true);
                    if (s_DebugBounds) MelonLogger.Msg($"[WIND-POST-RUN] Flag false ama oyuncu gercekten {savedId} icinde, duzeltildi.");
                }
                else if (!string.IsNullOrEmpty(savedId) && s_DebugBounds)
                {
                    MelonLogger.Msg($"[WIND-POST-RUN] Kayit '{savedId}' diyor ama oyuncu orada degil - " +
                                    $"bayat kayit, flag'e dokunulmadi.");
                }
            }

            // Outdoors: DO NOT touch wind, the game's own system is in charge.
            if (!playerInside)
            {
                if (s_DebugBounds) MelonLogger.Msg("[WIND-POST-RUN] Oyuncu disarida, wind'e dokunulmadi.");
                yield break;
            }

            // Indoors: make sure the loop is running. It is never stopped; the occlusion filter
            // lowers it (see ApplyOutdoorWindForInsideState in Audio.cs).
            ApplyOutdoorWindForInsideState(true, "post-run");

            if (s_DebugBounds) MelonLogger.Msg($"[WIND-POST-RUN] Oyuncu icerde, dis ruzgar sesi ic mekan kuralina gore ayarlandi, id={wind.m_WindLoopAudioInstance}",
                                               $"[WIND-POST-RUN] The player is inside; the outdoor wind loop was set by the indoor rule, id={wind.m_WindLoopAudioInstance}");
        }
    }
}
