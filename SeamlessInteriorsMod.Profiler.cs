using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // SI's own cost and hot patch call counts, logged every 5 seconds. Shift+F6 toggles.
        internal static class PerfProbe
        {
            // ORDER IS LOAD-BEARING. Everything before PortalHydrate is added up into the
            // per-frame total; everything from PortalHydrate on is a breakdown of a section
            // that has already been counted, and adding those again would inflate it.
            // PlaceContext used to sit at the end and was therefore left out of the total
            // although it is per-frame work of its own; it now sits with the rest.
            internal enum Section
            {
                LightGuard, FrameTicks, ClosedTime, ExteriorFx, DoorlessExit, FlagGuard, TerrainHole, LateUpdate,
                WatchdogVisibility, WatchdogRepair, WatchdogSweep, InitialSync, PlaceContext, Save, Portal,
                // ─── breakdowns, not counted again ───
                PortalHydrate, PortalActivate, PortalForceRenderers, PortalColliders, PortalHideSync,
                PortalItemsVisible, PortalMarkInside,
                SaveGear, SavePlaceables, SaveContainers, SaveState, SaveJunk,
                ModLoad, ModFirstBuild, Count
            }

            internal enum Hit
            {
                LightOwnerLookup, CustomizableSafehouse, DecorationInside, PlacementChecks, Count
            }

            // Object searches, by what they walk. These are what scales with how full a
            // base is, so they are counted apart from the sections that contain them:
            // a section says "the sweep cost 40 ms", a scan says which search spent it.
            internal enum Scan
            {
                SceneGear, ScenePlaceables, SceneGuids, SceneContainers, SceneRenderers,
                CloneGear, ClonePlaceables, CloneRenderers, CloneColliders, CloneComponents,
                ViewCaller, ViewFilter, RootView, Count
            }

            private static readonly string[] s_SectionNames =
            {
                "isik-sahibi", "diger-tick", "kapali-zaman", "dis-efekt", "kapisiz-cikis",
                "bayrak", "arazi-deligi", "late-update",
                "bekci-gorunurluk", "bekci-onarim", "bekci-supurme", "ilk-senkron", "baglam",
                "kayit", "kapi-gecisi",
                "giris-dolum", "giris-acma", "giris-renderer", "giris-collider", "giris-gizleme",
                "giris-esya", "giris-konum",
                "kayit-esya", "kayit-mobilya", "kayit-konteyner", "kayit-durum", "kayit-cop",
                "mod-yukleme", "mod-ilk-kurulum"
            };

            private static readonly string[] s_HitNames =
            {
                "isik-yamasi", "safehouse-hud", "dekor-icinde", "yerlestirme-kontrol"
            };

            private static readonly string[] s_ScanNames =
            {
                "sahne-esya", "sahne-mobilya", "sahne-guid", "sahne-konteyner", "sahne-renderer",
                "klon-esya", "klon-mobilya", "klon-renderer", "klon-collider", "klon-bilesen",
                "gorunum-yigin", "gorunum-filtre", "kok-gorunum"
            };

            private const float REPORT_SECONDS = 5f;

            // Single runs at least this long are reported with their maximum.
            private const double MAX_REPORT_MS = 5.0;

            // Shadow-casting lights listed by name when the probe is switched on.
            private const int LISTED_SHADOW_LIGHTS = 8;

            internal static bool On;

            private static readonly long[] s_Ticks = new long[(int)Section.Count];
            private static readonly long[] s_MaxTicks = new long[(int)Section.Count];
            private static readonly long[] s_Hits = new long[(int)Hit.Count];
            private static readonly long[] s_ScanCalls = new long[(int)Scan.Count];
            private static readonly long[] s_ScanTicks = new long[(int)Scan.Count];
            private static readonly long[] s_ScanMaxTicks = new long[(int)Scan.Count];
            private static readonly long[] s_ScanObjects = new long[(int)Scan.Count];
            private static int s_Frames;
            private static float s_FrameSeconds;
            private static float s_WorstFrame;
            private static float s_WindowStart;
            private static bool s_LoggedSceneStats;
            private static int s_Gc0, s_Gc1, s_Gc2;

            // ─── MANAGED ALLOCATION ───
            //
            // Measured on a full base: 1358 KB allocated PER FRAME, 17 gen0 / 9 gen1 / 1
            // gen2 collections every six seconds, and a frame rate that tracks the
            // collection count almost exactly - 9-21 fps inside against 24-47 outside,
            // with the worst outdoor window (41/16/3) also the slowest at 13 fps.
            //
            // So the cost of standing in a full base is garbage, not drawing: switching
            // off all 54 of the building's lights changed nothing (-0.5 fps) and cutting
            // the draw distance to 25 m changed nothing (-2.1 fps).
            //
            // These two counters say how much of that garbage is this mod's: everything
            // between the first and last line of OnUpdate is ours, everything else in the
            // frame is the game, the other mods and the engine. GC.GetTotalMemory(false)
            // costs 0.04 microseconds, so asking twice a frame does not disturb what it
            // measures.
            private static long s_MemAtUpdateStart;
            private static long s_MemAtUpdateEnd;
            private static long s_BytesSelf;
            private static long s_BytesOther;

            internal static long Begin()
            {
                return On ? Stopwatch.GetTimestamp() : 0L;
            }

            internal static void End(Section section, long start)
            {
                if (start == 0L) return;
                long elapsed = Stopwatch.GetTimestamp() - start;
                s_Ticks[(int)section] += elapsed;
                if (elapsed > s_MaxTicks[(int)section]) s_MaxTicks[(int)section] = elapsed;
            }

            internal static void Count(Hit hit)
            {
                if (On) s_Hits[(int)hit]++;
            }

            // Last line of OnUpdate: everything since Frame() was this mod's doing.
            internal static void FrameEnd()
            {
                if (!On) return;

                s_MemAtUpdateEnd = GC.GetTotalMemory(false);
                long self = s_MemAtUpdateEnd - s_MemAtUpdateStart;
                if (self > 0 && s_MemAtUpdateStart > 0) s_BytesSelf += self;
            }

            // One object search: how long it took and how many objects it walked.
            // `start` comes from Begin(), so a switched-off probe costs one branch.
            internal static void Scanned(Scan kind, int objects, long start)
            {
                if (start == 0L) return;

                long elapsed = Stopwatch.GetTimestamp() - start;
                int i = (int)kind;
                s_ScanCalls[i]++;
                s_ScanTicks[i] += elapsed;
                s_ScanObjects[i] += objects;
                if (elapsed > s_ScanMaxTicks[i]) s_ScanMaxTicks[i] = elapsed;
            }

            internal static void Toggle()
            {
                SetEnabled(!On, true);
            }

            // Also called from the settings at startup, where nothing may be drawn on
            // screen yet and the preference is the one doing the asking.
            internal static void SetEnabled(bool enabled, bool fromHotkey)
            {
                On = enabled;
                if (On)
                {
                    s_LoggedSceneStats = true;
                    LogSceneStats();
                }
                ResetWindow();

                if (fromHotkey)
                {
                    s_LightingModeMessage = On ? "Performans olcumu ACIK (5 sn'de bir loga yazar)" : "Performans olcumu KAPALI";
                    s_LightingModeMessageTimer = 3f;

                    if (s_PrefEnablePerfProbe != null && s_PrefEnablePerfProbe.Value != On)
                    {
                        s_PrefEnablePerfProbe.Value = On;
                        MelonPreferences.Save();
                    }
                }

                MelonLogger.Msg("[PERF] Olcum " + (On ? "ACIK" : "KAPALI") + " (Shift+F6)");
            }

            // First thing in OnUpdate, once per frame.
            internal static void Frame()
            {
                if (!On) return;

                // Switched on by the preference rather than the hotkey: no window has been
                // opened yet, and reporting against a zero start would print one garbage
                // line.
                if (s_WindowStart <= 0f)
                {
                    ResetWindow();
                    return;
                }

                // The scene stats need a region with buildings in it, which the main menu
                // this was switched on from does not have. Written on the first frame that
                // does, once per session.
                //
                // WAIT FOR THE LOAD TO FINISH. Counted while the region is still coming in,
                // every interior scene the mod pulls in is still resident and the numbers
                // are nonsense - measured 48177 renderers across 29 scenes mid-load against
                // 6646 once settled. A figure that wrong is worse than none.
                if (!s_LoggedSceneStats && ActiveInteriors.Count > 0
                    && !s_ScreenHeldBlack && !IsAnyCloningActive())
                {
                    s_LoggedSceneStats = true;
                    LogSceneStats();
                    ResetWindow();
                    return;
                }

                float dt = Time.unscaledDeltaTime;
                s_Frames++;
                s_FrameSeconds += dt;
                if (dt > s_WorstFrame) s_WorstFrame = dt;

                // Everything since this mod's last line of OnUpdate belongs to somebody
                // else. A collection in between makes the difference negative, and a
                // frame that collected has nothing to say about allocation, so it is
                // dropped rather than clamped.
                s_MemAtUpdateStart = GC.GetTotalMemory(false);
                long other = s_MemAtUpdateStart - s_MemAtUpdateEnd;
                if (other > 0 && s_MemAtUpdateEnd > 0) s_BytesOther += other;

                if (Time.realtimeSinceStartup - s_WindowStart >= REPORT_SECONDS) Report();
            }

            private static void Report()
            {
                if (s_Frames == 0 || s_FrameSeconds <= 0f)
                {
                    ResetWindow();
                    return;
                }

                double msPerTick = 1000.0 / Stopwatch.Frequency;
                double totalMs = 0.0;
                var sections = new StringBuilder();
                for (int i = 0; i < (int)Section.Count; i++)
                {
                    double ms = s_Ticks[i] * msPerTick / s_Frames;
                    double maxMs = s_MaxTicks[i] * msPerTick;

                    // The door steps are already inside kapi-gecisi; counting them again would inflate the total.
                    if (i < (int)Section.PortalHydrate) totalMs += ms;
                    if (ms < 0.005 && maxMs < MAX_REPORT_MS) continue;

                    sections.Append(' ').Append(s_SectionNames[i]).Append('=').Append(ms.ToString("F3", CultureInfo.InvariantCulture));
                    if (maxMs >= MAX_REPORT_MS) sections.Append("(max ").Append(maxMs.ToString("F0", CultureInfo.InvariantCulture)).Append(')');
                }

                var hits = new StringBuilder();
                for (int i = 0; i < (int)Hit.Count; i++)
                {
                    if (s_Hits[i] == 0) continue;
                    double perFrame = (double)s_Hits[i] / s_Frames;
                    hits.Append(' ').Append(s_HitNames[i]).Append('=').Append(perFrame.ToString("F1", CultureInfo.InvariantCulture));
                }

                float fps = s_Frames / s_FrameSeconds;
                string place = PlayerInteriorId ?? "disarida";
                int gc0 = GC.CollectionCount(0) - s_Gc0;
                int gc1 = GC.CollectionCount(1) - s_Gc1;
                int gc2 = GC.CollectionCount(2) - s_Gc2;

                // The collider debt: what the repair pass actually has to walk.
                int debt = s_CollidersWeDisabled.Count;

                double selfKb = s_BytesSelf / 1024.0 / s_Frames;
                double otherKb = s_BytesOther / 1024.0 / s_Frames;
                long heapMb = GC.GetTotalMemory(false) / 1048576;

                MelonLogger.Msg(FormattableString.Invariant(
                    $"[PERF] {place} | fps {fps:F1} (en kotu kare {s_WorstFrame * 1000f:F1} ms) | SI {totalMs:F3} ms/kare:{sections} | cagri/kare:{hits} | collider-borc {debt} | gc {gc0}/{gc1}/{gc2}"));

                MelonLogger.Msg(FormattableString.Invariant(
                    $"[PERF-BELLEK] kare basina {selfKb + otherKb:F0} KB ayriliyor: SI {selfKb:F0} KB, digerleri {otherKb:F0} KB | heap {heapMb} MB"));

                LogScans(msPerTick);

                s_LightingModeMessage = FormattableString.Invariant($"FPS {fps:F0} | SI {totalMs:F2} ms/kare | {place}");
                s_LightingModeMessageTimer = 3f;

                ResetWindow();
            }

            // Which object searches the window spent its time in. Read as
            // "name = total ms (calls x average object count, worst single call)":
            // the cost that grows with how full a base is lives here, not in the
            // per-frame sections.
            private static void LogScans(double msPerTick)
            {
                double windowSeconds = Time.realtimeSinceStartup - s_WindowStart;
                if (windowSeconds <= 0.0) windowSeconds = REPORT_SECONDS;

                double scanMs = 0.0;
                var line = new StringBuilder();
                for (int i = 0; i < (int)Scan.Count; i++)
                {
                    if (s_ScanCalls[i] == 0) continue;

                    double ms = s_ScanTicks[i] * msPerTick;
                    double maxMs = s_ScanMaxTicks[i] * msPerTick;
                    double avgObjects = (double)s_ScanObjects[i] / s_ScanCalls[i];
                    scanMs += ms;

                    line.Append(' ').Append(s_ScanNames[i]).Append('=')
                        .Append(ms.ToString("F1", CultureInfo.InvariantCulture)).Append("ms(")
                        .Append(s_ScanCalls[i].ToString(CultureInfo.InvariantCulture)).Append('x')
                        .Append(avgObjects.ToString("F0", CultureInfo.InvariantCulture)).Append(", en kotu ")
                        .Append(maxMs.ToString("F1", CultureInfo.InvariantCulture)).Append("ms)");
                }

                if (line.Length == 0) return;

                double msPerSecond = scanMs / windowSeconds;
                MelonLogger.Msg(FormattableString.Invariant(
                    $"[PERF-TARAMA] {windowSeconds:F1} sn icinde toplam {scanMs:F1} ms (saniyede {msPerSecond:F1} ms):{line}"));
            }

            private static void ResetWindow()
            {
                Array.Clear(s_Ticks, 0, s_Ticks.Length);
                Array.Clear(s_MaxTicks, 0, s_MaxTicks.Length);
                Array.Clear(s_Hits, 0, s_Hits.Length);
                Array.Clear(s_ScanCalls, 0, s_ScanCalls.Length);
                Array.Clear(s_ScanTicks, 0, s_ScanTicks.Length);
                Array.Clear(s_ScanMaxTicks, 0, s_ScanMaxTicks.Length);
                Array.Clear(s_ScanObjects, 0, s_ScanObjects.Length);
                s_BytesSelf = 0;
                s_BytesOther = 0;
                s_Frames = 0;
                s_FrameSeconds = 0f;
                s_WorstFrame = 0f;
                s_WindowStart = Time.realtimeSinceStartup;
                s_Gc0 = GC.CollectionCount(0);
                s_Gc1 = GC.CollectionCount(1);
                s_Gc2 = GC.CollectionCount(2);
            }

            // The rendering load, written when the probe is switched on. Only lights that are on count.
            private static void LogSceneStats()
            {
                try
                {
                    int renderers = UnityEngine.Object.FindObjectsOfType<Renderer>().Length;
                    int particles = UnityEngine.Object.FindObjectsOfType<ParticleSystem>().Length;

                    int litLights = 0, shadowLights = 0;
                    foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
                    {
                        if (!IsLit(light)) continue;
                        litLights++;
                        if (light.shadows != LightShadows.None) shadowLights++;
                    }

                    int openBuildings = 0, openRenderers = 0, openLit = 0;
                    var openShadow = new List<Light>();
                    foreach (var instance in ActiveInteriors.Values)
                    {
                        if (instance == null || instance.MasterInterior == null || !instance.MasterInterior.activeInHierarchy) continue;
                        openBuildings++;
                        openRenderers += instance.MasterInterior.GetComponentsInChildren<Renderer>(false).Length;

                        foreach (var light in instance.MasterInterior.GetComponentsInChildren<Light>(false))
                        {
                            if (!IsLit(light)) continue;
                            openLit++;
                            if (light.shadows != LightShadows.None) openShadow.Add(light);
                        }
                    }

                    Camera cam = Il2Cpp.GameManager.GetMainCamera();
                    float far = cam != null ? cam.farClipPlane : -1f;
                    Terrain terrain = Terrain.activeTerrain;
                    float trees = terrain != null ? terrain.treeDistance : -1f;
                    float detail = terrain != null ? terrain.detailObjectDistance : -1f;
                    float pixelError = terrain != null ? terrain.heightmapPixelError : -1f;

                    MelonLogger.Msg(FormattableString.Invariant(
                        $"[PERF-SAHNE] renderer={renderers} (acik {openBuildings} binada {openRenderers}) yanan isik={litLights} (golgeli {shadowLights}; binalarda yanan {openLit}, golgeli {openShadow.Count}) parcacik={particles} | kamera uzak={far:F0} | arazi agac={trees:F0} detay={detail:F0} pikselHata={pixelError:F1} | golge={QualitySettings.shadowDistance:F0}m lodBias={QualitySettings.lodBias:F2} pikselIsik={QualitySettings.pixelLightCount}"));

                    LogBuildingContents();

                    openShadow.Sort((a, b) => b.range.CompareTo(a.range));
                    for (int i = 0; i < openShadow.Count && i < LISTED_SHADOW_LIGHTS; i++)
                    {
                        Light light = openShadow[i];
                        Transform parent = light.transform.parent;
                        MelonLogger.Msg(FormattableString.Invariant(
                            $"[PERF-ISIK] '{light.gameObject.name}' (ust: {(parent != null ? parent.name : "-")}) tip={light.type} menzil={light.range:F1} parlaklik={light.intensity:F2} golge={light.shadows} mod={light.renderMode}"));
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[PERF-SAHNE] Sahne sayimi yapilamadi: " + ex.Message);
                }
            }

            // HOW FULL EACH BASE IS - the number every scan cost has to be read against.
            // A report saying "the sweep costs 40 ms" means nothing without knowing the
            // building holds 3000 items; this is the line that says so.
            //
            // Deliberately expensive: it runs the very scans this work is about to remove,
            // once, at the moment the probe is switched on.
            private static void LogBuildingContents()
            {
                int totalGear = 0, totalPlaceables = 0;

                foreach (var instance in ActiveInteriors.Values)
                {
                    if (instance == null || instance.MasterInterior == null) continue;

                    GameObject master = instance.MasterInterior;
                    int gear = master.GetComponentsInChildren<Il2Cpp.GearItem>(true).Length;
                    int placeables = master.GetComponentsInChildren<Il2CppTLD.Placement.Placeable>(true).Length;
                    int renderers = master.GetComponentsInChildren<Renderer>(true).Length;
                    int colliders = master.GetComponentsInChildren<Collider>(true).Length;

                    totalGear += gear;
                    totalPlaceables += placeables;

                    string state = master.activeInHierarchy ? "ACIK" : (instance.InteriorPersisted ? "park" : "kapali");
                    string content = instance.ContentHydrated ? "dolu" : "bos";
                    MelonLogger.Msg(FormattableString.Invariant(
                        $"[PERF-BINA] {instance.Config.ResolvedInstanceId} ({state}) esya={gear} mobilya={placeables} renderer={renderers} collider={colliders} icerik={content}"));
                }

                MelonLogger.Msg(FormattableString.Invariant(
                    $"[PERF-BINA] Toplam {ActiveInteriors.Count} bina: {totalGear} esya, {totalPlaceables} mobilya (bir tam klon taramasinin gezdigi nesne sayisi)."));
            }

            private static bool IsLit(Light light)
            {
                return light != null && light.enabled && light.intensity > 0f;
            }
        }
    }

    // Times a whole door interaction, including every other patch on it.
    [HarmonyPatch(typeof(Il2Cpp.LoadScene), nameof(Il2Cpp.LoadScene.PerformInteraction))]
    internal static class PortalTimingPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out long __state)
        {
            __state = SeamlessInteriorsMod.PerfProbe.Begin();
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(long __state)
        {
            SeamlessInteriorsMod.PerfProbe.End(SeamlessInteriorsMod.PerfProbe.Section.Portal, __state);
        }
    }
}
