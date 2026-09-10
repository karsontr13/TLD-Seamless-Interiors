using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    internal static class ClonedInteriorLightingGuard
    {

        public static readonly bool BlockOwnerLightmapTint = false;

        public static readonly bool EnforceOwnerAmbientEveryFrame = true;

        // ─── Ownership state ───

        private static SeamlessInteriorInstance s_Owner;

        // The values the owner wrote to the global ambient MOST RECENTLY (indoor look).
        private static bool s_HasOwnerAmbient;
        private static AmbientState s_OwnerAmbient;

        // The values from just before the player entered the clone (outdoor look).
        // Re-applied once on the way out, so the screen does not stay on the indoor
        // ambient until the outdoor system writes its own value again.
        private static bool s_HasExteriorAmbient;
        private static AmbientState s_ExteriorAmbient;

        // The owner's "write window": open while a lighting manager's ambient call is in
        // progress. The Utils.SetAmbientLight* patch inspects this window to tell
        // whether a call came from the owner or from some other writer.
        private static int s_OwnerWriteDepth;
        private static int s_OwnerWriteFrame = -1;

        // The ambient state from just before the window opened. If the value is unchanged
        // when the window closes, the manager did not actually write anything.
        //
        // WHY NOT JUST ambientLight: in a scene using Trilight/Gradient ambient mode,
        // Utils.SetAmbientLight writes the sky/equator/ground fields and ambientLight
        // never changes. The old test concluded "did not write" in those scenes and never
        // established ownership; with the outdoor writer unblocked, the interior stayed on
        // outdoor lighting and only the local spot lights worked.
        private static AmbientState s_AmbientBeforeOwnerWrite;

        // Does the owning clone have NO TodAmbientLight of its own? If so its manager
        // inevitably writes the outside world's ambient, and those writes cannot be trusted.
        private static bool s_OwnerAmbientOrphan;

        // The interior ambient each instance established MOST RECENTLY.
        //
        // WHY: when the player leaves an interior and comes back, ownership is
        // established from scratch and the environment stays on the outdoor value until
        // the interior's lighting manager writes its ambient again (in some scenes it
        // never does). The last known value is therefore applied the moment ownership is
        // taken back.
        private static readonly System.Collections.Generic.Dictionary<string, AmbientState> s_AmbientByInstance =
            new System.Collections.Generic.Dictionary<string, AmbientState>();

        public static SeamlessInteriorInstance Owner { get { return s_Owner; } }

        public static bool IsOwner(SeamlessInteriorInstance instance)
        {
            return instance != null && ReferenceEquals(instance, s_Owner);
        }

        public static bool OwnerAmbientEstablished
        {
            get { return s_Owner != null && s_HasOwnerAmbient; }
        }

        public static bool OwnerAmbientOrphan
        {
            get { return s_Owner != null && s_OwnerAmbientOrphan; }
        }

        // ─── Per-frame ownership check (called from SeamlessInteriorsMod.OnUpdate) ───

        public static void Tick()
        {
            SeamlessInteriorInstance next = ResolveOwner();
            if (ReferenceEquals(next, s_Owner))
            {
                MaybeApplyLateFallback();
                return;
            }

            SeamlessInteriorInstance prev = s_Owner;

            s_Owner = next;
            s_HasOwnerAmbient = false;
            s_OwnerWriteDepth = 0;
            s_OwnerSince = Time.time;
            s_LateFallbackLogged = false;

            if (prev == null && next != null)
            {
                // Outside -> inside: back up the outdoor ambient (restored on the way out).
                s_ExteriorAmbient = AmbientState.Capture();
                s_HasExteriorAmbient = true;

                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg($"[LIGHT-OWNER] {next.Config.ResolvedInstanceId} global aydinlatmanin sahibi oldu.");
            }
            else if (next == null)
            {
                // Inside -> outside: put the outdoor values back immediately.
                if (s_HasExteriorAmbient) s_ExteriorAmbient.Apply();

                if (SeamlessInteriorsMod.s_DebugBounds)
                    MelonLogger.Msg($"[LIGHT-OWNER] Sahiplik birakildi ({(prev != null ? prev.Config.ResolvedInstanceId : "?")}), " +
                                    $"dis dunya ambient'i geri yuklendi.");
            }
            else if (SeamlessInteriorsMod.s_DebugBounds)
            {
                // Interior to interior (e.g. the basement door). The outdoor backup is kept.
                MelonLogger.Msg($"[LIGHT-OWNER] Sahiplik devredildi: " +
                                $"{(prev != null ? prev.Config.ResolvedInstanceId : "?")} -> {next.Config.ResolvedInstanceId}");
            }

            // Ownership taken: bind the managers to the clone's OWN ambient object.
            // (The clone has just been activated, so DarkLightingManager.Start() has run
            //  by now; binding at clone time alone is therefore not enough.)
            s_OwnerAmbientOrphan = false;
            if (next != null)
            {
                s_OwnerAmbientOrphan = !SeamlessInteriorsMod.BindLightingManagersToOwnAmbient(next);
            }

            // If this interior established an ambient before, re-apply it right away.
            //
            // WHY: waiting for the manager to write again is not reliable - in some scenes
            // the write is never detected and until then (sometimes permanently) the
            // outdoor ambient stays in effect, giving the "dark mode on but only spot
            // lights working" look. If the value is stale, the manager's first write
            // updates it anyway.
            if (next != null)
            {
                AmbientState cached;
                if (s_AmbientByInstance.TryGetValue(next.Config.ResolvedInstanceId, out cached))
                {
                    cached.Apply();
                    s_OwnerAmbient = cached;
                    s_HasOwnerAmbient = true;

                    if (SeamlessInteriorsMod.s_DebugBounds)
                        MelonLogger.Msg($"[LIGHT-OWNER] {next.Config.ResolvedInstanceId}: " +
                                        $"onceki ziyaretten kalan ic mekan ambient'i geri uygulandi.");
                }
            }

            // LAST RESORT: with no TodAmbientLight of its own, the manager inevitably
            // writes the outside world's ambient; that value already matches what is on
            // screen, so ownership is never established and the interior stays bright.
            // In that case the outdoor ambient is darkened and applied by us.
            if (next != null && s_OwnerAmbientOrphan && !s_HasOwnerAmbient)
                ApplyDarkFallbackAmbient(next, "klon sahnede TodAmbientLight yok");
        }

        private static void MaybeApplyLateFallback()
        {
            if (s_Owner == null || s_HasOwnerAmbient) return;
            if (Time.time - s_OwnerSince < LATE_FALLBACK_DELAY) return;

            ApplyDarkFallbackAmbient(s_Owner, "yonetici ambient yazmadi");
        }

        private static void ApplyDarkFallbackAmbient(SeamlessInteriorInstance instance, string reason)
        {
            AmbientState fallback = AmbientState.Capture(false);
            fallback.Light = ScaleColor(fallback.Light, FALLBACK_DARK_FACTOR);
            fallback.Sky = ScaleColor(fallback.Sky, FALLBACK_DARK_FACTOR);
            fallback.Equator = ScaleColor(fallback.Equator, FALLBACK_DARK_FACTOR);
            fallback.Ground = ScaleColor(fallback.Ground, FALLBACK_DARK_FACTOR);
            fallback.Intensity *= FALLBACK_DARK_FACTOR;
            fallback.Apply();

            s_OwnerAmbient = fallback;
            s_HasOwnerAmbient = true;

            // Deliberately NOT cached: this value was derived from the ambient of the
            // moment. Cached, a player returning at night would get the darkened noon
            // value. Recomputing on every entry is more correct.

            if (!s_LateFallbackLogged)
            {
                s_LateFallbackLogged = true;
                MelonLogger.Warning($"[LIGHT-OWNER] {instance.Config.ResolvedInstanceId}: {reason}, " +
                                    $"dis ambient x{FALLBACK_DARK_FACTOR} ile karartildi (son care).");
            }
        }

        private static readonly float LATE_FALLBACK_DELAY = 1.5f;

        private static readonly float FALLBACK_DARK_FACTOR = 0.30f;

        // When ownership was taken (for the delayed safety net).
        private static float s_OwnerSince;
        private static bool s_LateFallbackLogged;

        private static Color ScaleColor(Color c, float f)
        {
            return new Color(c.r * f, c.g * f, c.b * f, c.a);
        }

        private const float OWNER_FALLBACK_MAX_DISTANCE = 40f;

        private static SeamlessInteriorInstance ResolveOwner()
        {
            // In Outdoor mode interiors deliberately use the outdoor light - no owner.
            if (!SeamlessInteriorsMod.IsDarkAtmosphereMode) return null;

            Vector3 pos = Vector3.zero;
            bool hasPos = false;
            Transform playerT = Il2Cpp.GameManager.GetPlayerTransform();
            if (playerT != null)
            {
                pos = playerT.position;
                hasPos = true;
            }

            SeamlessInteriorInstance nearestActive = null;
            float nearestDist = float.MaxValue;

            foreach (var inst in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                if (inst == null || inst.MasterInterior == null) continue;
                if (!inst.MasterInterior.activeSelf) continue;

                if (hasPos && inst.IsPositionInVolume(pos, 1.0f)) return inst;

                // Backup candidate in case the volume test misses.
                float d = hasPos
                    ? Vector3.SqrMagnitude(inst.MasterInterior.transform.position - pos)
                    : 0f;
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearestActive = inst;
                }
            }

            // The volume test missed but the mod says "the player is inside a clone": the
            // owner is the nearest OPEN clone.
            //
            // WHY the "only one clone open" condition was dropped: in nested buildings
            // (farmhouse + basement), or during a transition, more than one clone can be
            // open briefly; the old condition then failed to pick an owner at all and the
            // outdoor lighting kept overriding the interior. It was also a permanent
            // source of trouble: after a load (for persisted clones) the InteriorTrigger
            // volume is computed from an inactive hierarchy, so the volume test can miss.
            //
            // SAFETY: only if the player is actually near that clone, so a
            // s_IsPlayerInsideClone flag wrongly left true (it is restored from the save)
            // cannot lock out the outside world's lighting.
            if (SeamlessInteriorsMod.s_IsPlayerInsideClone
                && nearestActive != null
                && nearestDist <= OWNER_FALLBACK_MAX_DISTANCE * OWNER_FALLBACK_MAX_DISTANCE)
            {
                return nearestActive;
            }

            return null;
        }

        // ─── The owner's write window ───

        public static void BeginOwnerWrite()
        {
            // Frame stamp: if the original method throws, the Postfix never runs and the
            // counter would leak. Resetting on each new frame makes it self-healing.
            int frame = Time.frameCount;
            if (s_OwnerWriteFrame != frame)
            {
                s_OwnerWriteFrame = frame;
                s_OwnerWriteDepth = 0;
            }
            s_OwnerWriteDepth++;
            // Snapshot without reading the probe (expensive): for comparison only.
            s_AmbientBeforeOwnerWrite = AmbientState.Capture(false);
        }

        public static void EndOwnerWrite()
        {
            if (s_OwnerWriteDepth > 0) s_OwnerWriteDepth--;

            if (s_Owner == null) return;

            // NO ambient-related field changed: the manager really did not write.
            if (s_AmbientBeforeOwnerWrite.MatchesCurrent()) return;

            bool firstWrite = !s_HasOwnerAmbient;

            s_OwnerAmbient = AmbientState.Capture();
            s_HasOwnerAmbient = true;
            s_AmbientByInstance[s_Owner.Config.ResolvedInstanceId] = s_OwnerAmbient;

            // Diagnostics (F7): the moment the owner's ambient is first established. If
            // this line never appears, the interior's lighting manager is not writing ambient.
            if (firstWrite && SeamlessInteriorsMod.s_DebugBounds)
                MelonLogger.Msg($"[LIGHT-OWNER] {s_Owner.Config.ResolvedInstanceId}: ic mekan ambient'i kuruldu " +
                                $"(mode={s_OwnerAmbient.Mode}, light={s_OwnerAmbient.Light}) — dis dunya yazari artik engelleniyor.");
        }

        public static bool IsOwnerWriting
        {
            get { return s_OwnerWriteDepth > 0 && s_OwnerWriteFrame == Time.frameCount; }
        }

        public static void EnforceOwnerAmbient()
        {
            if (!EnforceOwnerAmbientEveryFrame) return;
            if (s_Owner == null || !s_HasOwnerAmbient) return;
            if (s_OwnerAmbient.MatchesCurrent()) return;

            s_OwnerAmbient.Apply();
        }

        public static void Reset()
        {
            s_Owner = null;
            s_HasOwnerAmbient = false;
            s_HasExteriorAmbient = false;
            s_OwnerWriteDepth = 0;
            s_LoggedInstanceIds.Clear();

            // The time of day may have changed when a save is loaded, so the old ambient
            // values are no longer correct. The managers will establish them again.
            s_AmbientByInstance.Clear();
        }

        public static SeamlessInteriorInstance FindOwningInstance(Transform t)
        {
            return SeamlessInteriorsMod.FindInstanceOwning(t);
        }

        // Avoids log spam: write once per component.
        private static readonly System.Collections.Generic.HashSet<int> s_LoggedInstanceIds =
            new System.Collections.Generic.HashSet<int>();

        public static void LogOnce(SeamlessInteriorInstance instance, Object component, string what)
        {
            if (!SeamlessInteriorsMod.s_DebugBounds) return;
            if (component == null) return;
            if (!s_LoggedInstanceIds.Add(component.GetInstanceID())) return;

            MelonLogger.Msg($"[LIGHT-BLOCK] {instance.Config.ResolvedInstanceId}: " +
                            $"'{component.name}' {what} engellendi (sahip degil).");
        }

        private struct AmbientState
        {
            public UnityEngine.Rendering.AmbientMode Mode;
            public Color Light;
            public Color Sky;
            public Color Equator;
            public Color Ground;
            public float Intensity;
            public UnityEngine.Rendering.SphericalHarmonicsL2 Probe;
            public bool HasProbe;

            public static AmbientState Capture(bool includeProbe = true)
            {
                AmbientState s = new AmbientState();
                s.Mode = RenderSettings.ambientMode;
                s.Light = RenderSettings.ambientLight;
                s.Sky = RenderSettings.ambientSkyColor;
                s.Equator = RenderSettings.ambientEquatorColor;
                s.Ground = RenderSettings.ambientGroundColor;
                s.Intensity = RenderSettings.ambientIntensity;

                if (!includeProbe)
                {
                    s.HasProbe = false;
                    return s;
                }

                try
                {
                    s.Probe = RenderSettings.ambientProbe;
                    s.HasProbe = true;
                }
                catch
                {
                    s.HasProbe = false;
                }

                return s;
            }

            public void Apply()
            {
                RenderSettings.ambientMode = Mode;
                RenderSettings.ambientSkyColor = Sky;
                RenderSettings.ambientEquatorColor = Equator;
                RenderSettings.ambientGroundColor = Ground;
                RenderSettings.ambientIntensity = Intensity;
                RenderSettings.ambientLight = Light;

                if (HasProbe)
                {
                    try { RenderSettings.ambientProbe = Probe; }
                    catch { }
                }
            }

            public bool MatchesCurrent()
            {
                return RenderSettings.ambientMode == Mode
                    && RenderSettings.ambientLight == Light
                    && RenderSettings.ambientSkyColor == Sky
                    && RenderSettings.ambientEquatorColor == Equator
                    && RenderSettings.ambientGroundColor == Ground
                    && Mathf.Approximately(RenderSettings.ambientIntensity, Intensity);
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.TodAmbientLight), nameof(Il2Cpp.TodAmbientLight.SetAmbientLightValue))]
    public class BlockClonedInteriorAmbientPatch
    {
        public static bool Prefix(Il2Cpp.TodAmbientLight __instance, out bool __state)
        {
            __state = false;
            if (__instance == null) return true;

            var instance = ClonedInteriorLightingGuard.FindOwningInstance(__instance.transform);

            if (instance == null)
            {
                // Not a clone: the outside world's TodAmbientLight component (or one the
                // owner picked up by mistake through FindAmbientLighting).
                //
                // If the owner's write window is open the call really does come from the
                // interior - let it through. Otherwise, once the interior ambient is
                // established, cut the outdoor writer off HERE.
                //
                // WHY the Utils patch is not enough: Utils.SetAmbientLight is a small
                // static wrapper and IL2CPP AOT compilation may inline it into its caller,
                // in which case the Harmony prefix never runs and the outside world
                // overwrites the interior ambient every frame. That overwrite races the
                // LateUpdate safety net, and since the winner depends on Update order the
                // result differed from scene to scene and after loading: some clones went
                // dark properly while others stayed on outdoor lighting in dark mode.
                //
                // If the owner is "orphan" (the clone has no TodAmbientLight of its own)
                // the call is not let through even with the window open: it carries the
                // outside world's ambient, not the clone's, and would overwrite our
                // last-resort darkening.
                if (ClonedInteriorLightingGuard.IsOwnerWriting
                    && !ClonedInteriorLightingGuard.OwnerAmbientOrphan) return true;

                return !ClonedInteriorLightingGuard.OwnerAmbientEstablished;
            }

            if (ClonedInteriorLightingGuard.IsOwner(instance))
            {
                // The owner: allow the write and open the window for the Utils patch.
                ClonedInteriorLightingGuard.BeginOwnerWrite();
                __state = true;
                return true;
            }

            ClonedInteriorLightingGuard.LogOnce(instance, __instance, "global ambient yazmasi");
            return false; // do not run the original
        }

        public static void Postfix(bool __state)
        {
            if (!__state) return;
            ClonedInteriorLightingGuard.EndOwnerWrite();
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.DarkLightingManager), nameof(Il2Cpp.DarkLightingManager.UpdateAmbient))]
    public class BlockClonedDarkLightingAmbientPatch
    {
        public static bool Prefix(Il2Cpp.DarkLightingManager __instance, out bool __state)
        {
            __state = false;
            if (__instance == null) return true;

            var instance = ClonedInteriorLightingGuard.FindOwningInstance(__instance.transform);
            if (instance == null) return true;

            if (ClonedInteriorLightingGuard.IsOwner(instance))
            {
                ClonedInteriorLightingGuard.BeginOwnerWrite();
                __state = true;
                return true;
            }

            ClonedInteriorLightingGuard.LogOnce(instance, __instance, "UpdateAmbient (global ambient)");
            return false;
        }

        public static void Postfix(bool __state)
        {
            if (!__state) return;
            ClonedInteriorLightingGuard.EndOwnerWrite();
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.DarkLightingManager), nameof(Il2Cpp.DarkLightingManager.UpdateLightmaps))]
    public class BlockClonedDarkLightingLightmapPatch
    {
        public static bool Prefix(Il2Cpp.DarkLightingManager __instance)
        {
            if (__instance == null) return true;

            var instance = ClonedInteriorLightingGuard.FindOwningInstance(__instance.transform);
            if (instance == null) return true;

            if (ClonedInteriorLightingGuard.IsOwner(instance))
                return !ClonedInteriorLightingGuard.BlockOwnerLightmapTint;

            ClonedInteriorLightingGuard.LogOnce(instance, __instance, "UpdateLightmaps (global lightmap tint)");
            return false;
        }
    }


    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Utils), nameof(Il2Cpp.Utils.SetAmbientLight))]
    public class BlockExteriorAmbientWhileInsidePatch
    {
        public static bool Prefix()
        {
            if (ClonedInteriorLightingGuard.IsOwnerWriting) return true;
            return !ClonedInteriorLightingGuard.OwnerAmbientEstablished;
        }
    }


    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Utils), nameof(Il2Cpp.Utils.SetAmbientLightScaled))]
    public class BlockExteriorAmbientScaledWhileInsidePatch
    {
        public static bool Prefix()
        {
            if (ClonedInteriorLightingGuard.IsOwnerWriting) return true;
            return !ClonedInteriorLightingGuard.OwnerAmbientEstablished;
        }
    }
}
