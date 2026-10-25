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

        // The outdoor ambient light, for what outdoor light falls on inside a room (the window frost).
        public static bool TryGetExteriorLight(out Color light)
        {
            light = s_HasExteriorAmbient ? s_ExteriorAmbient.Light : Color.black;
            return s_HasExteriorAmbient;
        }

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
                KeepExteriorProbeInForce();
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
                // The room's manager left its tint in _LightingStrength; vanilla resets it on the region load.
                Il2Cpp.LightingManager.SetLightingStrengthDefault();

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

            // On the fields and without Unity's null tests: those leave garbage every frame (NoGarbageMath.cs).
            Vector3 pos = new Vector3(0f, 0f, 0f);
            bool hasPos = false;
            Transform playerT = Il2Cpp.GameManager.GetPlayerTransform();
            if (NoGarbageMath.IsAlive(playerT))
            {
                pos = playerT.position;
                hasPos = true;
            }

            SeamlessInteriorInstance nearestActive = null;
            float nearestDist = float.MaxValue;

            foreach (var inst in SeamlessInteriorsMod.ActiveInteriors.Values)
            {
                if (inst == null || !NoGarbageMath.IsAlive(inst.MasterInterior)) continue;
                if (!inst.MasterInterior.activeSelf) continue;

                if (hasPos && inst.IsPositionInVolume(pos, 1.0f)) return inst;

                // Backup candidate in case the volume test misses.
                float d = hasPos
                    ? NoGarbageMath.SqrLength(NoGarbageMath.Sub(inst.MasterInterior.transform.position, pos))
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

        // ─── The outdoor ambient, for the cameras that look out of the room's windows ───
        //
        // The lighting mode is for the indoor space and must not reach the views out of it
        // (owner, 2026-10-10; see SeamlessInteriorsMod.WindowViewAmbient.cs). Two parts.
        //
        // THE SWAP: around one window camera's render the outdoor ambient is put in force and
        // what was there is put back. Without the probe on the way in (reading it is expensive
        // and this runs per camera per frame); the owner's probe is set again on the way out.
        //
        // THE SAMPLE: the outdoor ambient was captured once, on the way in, so after an hour
        // indoors the windows would still show the hour the player walked in. The outdoor
        // writer keeps asking to write and is refused (Utils.SetAmbientLight, see the patch on it at the
        // end of this file; TodAmbientLight where a scene has one); once a second it is let through,
        // what it wrote is kept as the outdoor ambient, and the room's is put straight back in
        // the same call, before anything renders. The frame stamp is the same self-healing as
        // the owner's write window, and EnforceOwnerAmbient is the backstop behind it.
        //
        // A switch, so the two can be compared live from inside a room.
        public static bool ExteriorAmbientForViewsOut = true;

        private const float EXTERIOR_SAMPLE_INTERVAL = 1f;
        private static bool s_ViewOutSwapped;
        private static AmbientState s_AmbientBeforeViewOut;
        private static UnityEngine.Rendering.SphericalHarmonicsL2 s_ViewOutRoomProbe;
        private static bool s_ViewOutRoomProbeSaved;
        private static bool s_ExteriorSampling;
        private static int s_ExteriorSampleFrame;
        private static float s_ExteriorSampledAt = -1000f;
        private static AmbientState s_AmbientBeforeExteriorSample;

        // ─── THE OUTDOOR PROBE STAYS IN FORCE FOR THE WHOLE FRAME ───
        //
        // The swap round a window camera reaches the flat ambient values and not the light an object takes
        // from the ambient probe. That light followed the probe in force OUTSIDE the window camera's own
        // render, which was the room's. Through the Airfield hangar's windows in Dark Atmosphere the
        // helicopter, the far hangar, the tower and the shaded ground stood at 41 to 62 percent of their
        // Outdoor Lighting brightness, nearly black on a dark entry, while every value the window camera
        // was handed read the same in both modes (owner's matched pairs, 2026-10-12). Shown in the console:
        // a camera drawn first with the same swap changed nothing (99 to 100 percent); the outdoor probe
        // set once a frame did (98 to 104 percent of Outdoor Lighting; owner: "Pressed F8. Outdoor view
        // stayed the same"), and the room stayed as dark as before. Inferred from frames compared cell by
        // cell; the light one object got cannot be read. When in the frame Unity takes the probe is not
        // known: before the first camera fits, and so does the frame before.
        //
        // The room stayed dark, presumably because its own objects do not use that probe: TickInteriorProbes
        // hands each one the room's baked light by hand. So this is done only for a room that has such
        // probes; another room keeps its own probe as before and its views out may still darken (not
        // looked at). Per-window cameras and wall cameras alike: the probe is held for the frame, not
        // swapped for a camera.
        //
        // A switch, so the two can be compared live from inside a room.
        public static bool ExteriorProbeAllFrame = true;

        private static bool KeepsExteriorProbe
        {
            get
            {
                return ExteriorProbeAllFrame && ExteriorAmbientForViewsOut && s_Owner != null && s_HasOwnerAmbient
                       && s_HasExteriorAmbient && s_ExteriorAmbient.HasProbe && SeamlessInteriorsMod.RoomHasItsOwnProbes(s_Owner);
            }
        }

        // Every frame, from Tick and from the LateUpdate backstop, and after each window camera.
        private static void KeepExteriorProbeInForce()
        {
            if (s_ViewOutSwapped || !KeepsExteriorProbe) return;
            try { AmbientState.SetProbe(s_ExteriorAmbient.Probe); } catch { }
        }

        public static void BeginExteriorAmbientForViewOut()
        {
            if (s_ViewOutSwapped || !ExteriorAmbientForViewsOut) return;
            if (s_Owner == null || !s_HasOwnerAmbient || !s_HasExteriorAmbient) return;
            s_AmbientBeforeViewOut = AmbientState.Capture(false);
            // A room with no probe of its own on record (the dark fallback) has it read here, to be put back.
            s_ViewOutRoomProbeSaved = s_ExteriorAmbient.HasProbe && !s_OwnerAmbient.HasProbe && !KeepsExteriorProbe;
            if (s_ViewOutRoomProbeSaved) s_ViewOutRoomProbe = RenderSettings.ambientProbe;
            s_ExteriorAmbient.Apply();
            s_ViewOutSwapped = true;
            for (int i = 0; i < 4; i++) s_RoomAmbientForShaders[i] = Shader.GetGlobalVector(s_AmbientShaderIds[i]);
            s_HasRoomAmbientForShaders = true;
            if (s_HasExteriorAmbientForShaders)
                for (int i = 0; i < 4; i++) Shader.SetGlobalVector(s_AmbientShaderIds[i], s_ExteriorAmbientForShaders[i]);
            // The room's _LightingStrength (black at night in the dark mode) dimmed the ground outside the
            // windows while the trees stayed lit; the outdoors gets the game's default (2026-10-11).
            s_ViewOutRoomLightingStrength = Shader.GetGlobalVector(s_ViewOutLightingStrengthId);
            Il2Cpp.LightingManager.SetLightingStrengthDefault();
        }

        private static readonly int s_ViewOutLightingStrengthId = Shader.PropertyToID("_LightingStrength");
        private static Vector4 s_ViewOutRoomLightingStrength;

        // THE SHADERS ARE HANDED THE AMBIENT TOO, BECAUSE UNITY HANDS IT OVER TOO LATE. Unity copies the
        // ambient into its shader values partway through a camera's drawing, so a camera starts with
        // what the camera before it left. Read per camera in the game on 2026-10-10: the first window
        // camera of a frame started on the room's 0.047 and the player's own view on the outdoor 0.565.
        // Trees take that value and nothing else on their shaded side, so through a lone window they
        // stood black, and lit as soon as a second window came into view (owner: "it's flipping back
        // and forth depending on my view angle and position"). So the four values go in by hand on both
        // sides of the swap. Each side's are read back from what Unity itself made of them the frame
        // before: the room's on the way in, the outdoor ones on the way out. Four reads and four writes
        // per window camera on each side. Prototyped in the console; owner: "Trees are lit at every
        // angle now. Room looks fine".
        private static readonly int[] s_AmbientShaderIds =
        {
            Shader.PropertyToID("unity_AmbientSky"), Shader.PropertyToID("unity_AmbientEquator"),
            Shader.PropertyToID("unity_AmbientGround"), Shader.PropertyToID("glstate_lightmodel_ambient"),
        };
        private static readonly Vector4[] s_ExteriorAmbientForShaders = new Vector4[4];
        private static readonly Vector4[] s_RoomAmbientForShaders = new Vector4[4];
        private static bool s_HasExteriorAmbientForShaders;
        private static bool s_HasRoomAmbientForShaders;

        public static void EndExteriorAmbientForViewOut()
        {
            if (!s_ViewOutSwapped) return;
            s_ViewOutSwapped = false;
            Shader.SetGlobalVector(s_ViewOutLightingStrengthId, s_ViewOutRoomLightingStrength);
            for (int i = 0; i < 4; i++) s_ExteriorAmbientForShaders[i] = Shader.GetGlobalVector(s_AmbientShaderIds[i]);
            s_HasExteriorAmbientForShaders = true;
            if (s_HasRoomAmbientForShaders)
                for (int i = 0; i < 4; i++) Shader.SetGlobalVector(s_AmbientShaderIds[i], s_RoomAmbientForShaders[i]);
            s_AmbientBeforeViewOut.Apply();
            if (KeepsExteriorProbe)
            {
                // Set again under the room's intensity, which Apply has just put back.
                try { AmbientState.SetProbe(s_ExteriorAmbient.Probe); } catch { }
            }
            else if (s_ExteriorAmbient.HasProbe && s_HasOwnerAmbient && s_OwnerAmbient.HasProbe)
            {
                try { AmbientState.SetProbe(s_OwnerAmbient.Probe); } catch { }
            }
            else if (s_ViewOutRoomProbeSaved)
            {
                try { AmbientState.SetProbe(s_ViewOutRoomProbe); } catch { }
            }
            s_ViewOutRoomProbeSaved = false;
        }

        public static bool IsExteriorSampling
        {
            get { return s_ExteriorSampling && s_ExteriorSampleFrame == Time.frameCount; }
        }

        // True when the refused outdoor write should be let through this once.
        public static bool BeginExteriorSample()
        {
            if (!ExteriorAmbientForViewsOut || s_Owner == null || !s_HasOwnerAmbient || s_ViewOutSwapped) return false;
            float now = Time.unscaledTime;
            if (now - s_ExteriorSampledAt < EXTERIOR_SAMPLE_INTERVAL) return false;
            s_ExteriorSampledAt = now;
            s_AmbientBeforeExteriorSample = AmbientState.Capture();
            s_ExteriorSampling = true;
            s_ExteriorSampleFrame = Time.frameCount;
            return true;
        }

        public static void EndExteriorSample()
        {
            if (!s_ExteriorSampling) return;
            bool sameFrame = s_ExteriorSampleFrame == Time.frameCount;
            s_ExteriorSampling = false;
            if (!sameFrame) return;
            // The outdoor writer wrote nothing: keep the outdoor ambient already held.
            // The writer sets colours and probe only: mode and intensity stay as captured on the way in (taking the
            // room's darkened the outdoors on exit, 2026-10-10); its probe is rescaled from the room's intensity.
            if (!s_AmbientBeforeExteriorSample.MatchesCurrent())
            {
                AmbientState sampled = AmbientState.Capture();
                if (s_HasExteriorAmbient)
                {
                    float room = AmbientState.LinearIntensity(sampled.Mode, sampled.Intensity);
                    sampled.Mode = s_ExteriorAmbient.Mode;
                    sampled.Intensity = s_ExteriorAmbient.Intensity;
                    float outdoor = AmbientState.LinearIntensity(sampled.Mode, sampled.Intensity);
                    if (sampled.HasProbe && room > 1e-6f) sampled.Probe = sampled.Probe * (outdoor / room);
                }
                s_ExteriorAmbient = sampled;
                s_HasExteriorAmbient = true;
            }
            s_AmbientBeforeExteriorSample.Apply();
        }

        public static bool IsOwnerWriting
        {
            get { return s_OwnerWriteDepth > 0 && s_OwnerWriteFrame == Time.frameCount; }
        }

        public static void EnforceOwnerAmbient()
        {
            KeepExteriorProbeInForce();
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
            s_HasExteriorAmbientForShaders = false;
            s_HasRoomAmbientForShaders = false;
            s_OwnerWriteDepth = 0;
            s_LoggedInstanceIds.Clear();

            // The time of day may have changed when a save is loaded, so the old ambient
            // values are no longer correct. The managers will establish them again.
            s_AmbientByInstance.Clear();
            s_RoomLightingStrength.Clear();
        }

        public static SeamlessInteriorInstance FindOwningInstance(Transform t)
        {
            SeamlessInteriorsMod.PerfProbe.Count(SeamlessInteriorsMod.PerfProbe.Hit.LightOwnerLookup);
            return SeamlessInteriorsMod.FindInstanceOwning(t);
        }

        // What each room's own lighting manager last wrote to _LightingStrength, for the views in from outside.
        private static readonly System.Collections.Generic.Dictionary<SeamlessInteriorInstance, Vector4> s_RoomLightingStrength =
            new System.Collections.Generic.Dictionary<SeamlessInteriorInstance, Vector4>();

        public static void RecordRoomLightingStrength(SeamlessInteriorInstance instance, Vector4 strength)
        {
            if (instance != null) s_RoomLightingStrength[instance] = strength;
        }

        public static bool TryGetRoomLightingStrength(SeamlessInteriorInstance instance, out Vector4 strength)
        {
            strength = default(Vector4);
            return instance != null && s_RoomLightingStrength.TryGetValue(instance, out strength);
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
                    try { SetProbe(Probe); }
                    catch { }
                }
            }

            // Unity scales a probe it is handed by the linear ambient intensity (Skybox mode) and reads it
            // back scaled, so each read-and-write darkened it (at 0.3, x0.073 a time: the room blinked, 2026-10-10).
            public static void SetProbe(UnityEngine.Rendering.SphericalHarmonicsL2 probe)
            {
                float k = LinearIntensity(RenderSettings.ambientMode, RenderSettings.ambientIntensity);
                RenderSettings.ambientProbe = k > 1e-6f ? probe * (1f / k) : probe;
            }

            public static float LinearIntensity(UnityEngine.Rendering.AmbientMode mode, float intensity)
            {
                return mode == UnityEngine.Rendering.AmbientMode.Skybox ? Mathf.GammaToLinearSpace(intensity) : 1f;
            }

            // Every frame from EnforceOwnerAmbient: compared on the fields, as Unity's Color == and
            // Mathf.Approximately leave garbage (NoGarbageMath.cs).
            public bool MatchesCurrent()
            {
                return RenderSettings.ambientMode == Mode
                    && SameColor(RenderSettings.ambientLight, Light)
                    && SameColor(RenderSettings.ambientSkyColor, Sky)
                    && SameColor(RenderSettings.ambientEquatorColor, Equator)
                    && SameColor(RenderSettings.ambientGroundColor, Ground)
                    && NoGarbageMath.Approximately(RenderSettings.ambientIntensity, Intensity);
            }

            // Unity's Color ==: the squared length of the difference, as a Vector4, below 9.99999944E-11.
            private static bool SameColor(Color a, Color b)
            {
                float r = a.r - b.r, g = a.g - b.g, bl = a.b - b.b, al = a.a - b.a;
                return r * r + g * g + bl * bl + al * al < 9.99999944E-11f;
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

                if (!ClonedInteriorLightingGuard.OwnerAmbientEstablished) return true;
                return ClonedInteriorLightingGuard.BeginExteriorSample();
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
            ClonedInteriorLightingGuard.EndExteriorSample();
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
        private static readonly int s_LightingStrengthId = Shader.PropertyToID("_LightingStrength");
        private static Vector4 s_Saved;
        private static SeamlessInteriorInstance s_Running;

        // A non-owner room still runs, so its tint is known for the views in; the global is put back after.
        public static bool Prefix(Il2Cpp.DarkLightingManager __instance)
        {
            s_Running = null;
            if (__instance == null) return true;

            var instance = ClonedInteriorLightingGuard.FindOwningInstance(__instance.transform);
            if (instance == null) return true;

            if (ClonedInteriorLightingGuard.IsOwner(instance))
            {
                if (ClonedInteriorLightingGuard.BlockOwnerLightmapTint) return false;
                s_Running = instance;
                return true;
            }

            s_Saved = Shader.GetGlobalVector(s_LightingStrengthId);
            s_Running = instance;
            return true;
        }

        public static System.Exception Finalizer(Il2Cpp.DarkLightingManager __instance)
        {
            var instance = s_Running;
            s_Running = null;
            if (instance == null) return null;
            ClonedInteriorLightingGuard.RecordRoomLightingStrength(instance, Shader.GetGlobalVector(s_LightingStrengthId));
            if (!ClonedInteriorLightingGuard.IsOwner(instance)) Shader.SetGlobalVector(s_LightingStrengthId, s_Saved);
            return null;
        }
    }


    // A running clone's InteriorLightingManager writes the global _LightingStrength (its room tint,
    // black at night), which zeroed the ambient of the whole outdoors while outside-in portals held rooms open.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.InteriorLightingManager), nameof(Il2Cpp.InteriorLightingManager.Update))]
    public class KeepClonedLightingStrengthInRoomPatch
    {
        private static readonly int s_LightingStrengthId = Shader.PropertyToID("_LightingStrength");
        private static readonly System.Collections.Generic.Dictionary<int, SeamlessInteriorInstance> s_CloneOf =
            new System.Collections.Generic.Dictionary<int, SeamlessInteriorInstance>();
        private static Vector4 s_Saved;

        public static void Prefix(Il2Cpp.InteriorLightingManager __instance, out bool __state)
        {
            __state = false;
            if (__instance == null) return;

            int id = __instance.GetInstanceID();
            SeamlessInteriorInstance clone;
            // Only a found clone is cached: a scene-loaded room runs before it is registered as one.
            if (!s_CloneOf.TryGetValue(id, out clone))
            {
                clone = ClonedInteriorLightingGuard.FindOwningInstance(__instance.transform);
                if (clone != null) s_CloneOf[id] = clone;
            }
            if (clone == null || ClonedInteriorLightingGuard.IsOwner(clone)) return;

            __state = true;
            s_Clone = clone;
            s_Before = Shader.GetGlobalVector(s_LightingStrengthId);
            if (ClonedInteriorLightingGuard.Owner != null) s_Saved = s_Before;
        }

        private static SeamlessInteriorInstance s_Clone;
        private static Vector4 s_Before;

        // A finalizer, so the restore also runs when Update throws (a template room's half-built light group
        // throws for a few frames while loading); the throw is swallowed, as it is unpatched.
        public static System.Exception Finalizer(bool __state)
        {
            if (!__state) return null;
            Vector4 written = Shader.GetGlobalVector(s_LightingStrengthId);
            if (written != s_Before) ClonedInteriorLightingGuard.RecordRoomLightingStrength(s_Clone, written);
            if (ClonedInteriorLightingGuard.Owner == null) Il2Cpp.LightingManager.SetLightingStrengthDefault();
            else Shader.SetGlobalVector(s_LightingStrengthId, s_Saved);
            return null;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Utils), nameof(Il2Cpp.Utils.SetAmbientLight))]
    public class BlockExteriorAmbientWhileInsidePatch
    {
        // THE OUTDOOR AMBIENT IS SAMPLED HERE. In a region nothing outdoors calls TodAmbientLight: the
        // outdoors asks through this method, every frame (counted in the game on 2026-10-10: 1500 calls
        // in 1500 frames, none through TodAmbientLight). So the once-a-second sample that was hung on
        // TodAmbientLight never ran, and the views out kept the ambient of the moment the player walked
        // in. Loaded with the player already inside, that moment comes before the outdoors has its
        // light: the mod held 0.030 against a room at 0.047, and the views out stayed dark until the
        // lighting mode was flipped. Prototyped in the console; owner, flipping the mode with it on:
        // "Looks identical when switching."
        public static bool Prefix(out bool __state)
        {
            __state = false;
            if (ClonedInteriorLightingGuard.IsOwnerWriting || ClonedInteriorLightingGuard.IsExteriorSampling) return true;
            if (!ClonedInteriorLightingGuard.OwnerAmbientEstablished) return true;
            __state = ClonedInteriorLightingGuard.BeginExteriorSample();
            return __state;
        }

        public static void Postfix(bool __state)
        {
            if (__state) ClonedInteriorLightingGuard.EndExteriorSample();
        }
    }


    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Utils), nameof(Il2Cpp.Utils.SetAmbientLightScaled))]
    public class BlockExteriorAmbientScaledWhileInsidePatch
    {
        public static bool Prefix()
        {
            if (ClonedInteriorLightingGuard.IsOwnerWriting || ClonedInteriorLightingGuard.IsExteriorSampling) return true;
            return !ClonedInteriorLightingGuard.OwnerAmbientEstablished;
        }
    }
}
