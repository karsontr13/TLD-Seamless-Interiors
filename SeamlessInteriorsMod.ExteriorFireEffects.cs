using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    // What one building's shell offers for the exterior fire effects, and which fire feeds which
    // chimney. Everything in here points into the region scene the shell lives in, so the whole
    // object is thrown away together with the shell.
    public class ExteriorFireFx
    {
        public GameObject Shell;

        public readonly List<Il2Cpp.Chimney> Chimneys = new List<Il2Cpp.Chimney>();

        // Remaining burn time of the longest-burning fire behind each chimney, by chimney index.
        public float[] ChimneyMinutes = new float[0];

        // Every fire inside the clone, and the index into Chimneys it smokes through
        // (-1: the building has no chimney). Kept in step by index.
        public readonly List<Il2Cpp.Fire> Fires = new List<Il2Cpp.Fire>();
        public readonly List<int> FireChimney = new List<int>();

        // Chimneys Chimney.FindByGuid has found, by guid.
        public readonly Dictionary<string, int> ChimneyByGuid = new Dictionary<string, int>();

        public readonly List<WindowGlassSlot> Windows = new List<WindowGlassSlot>();

        // Spot lights in front of the ground-floor windows that throw the glow onto the snow, and how
        // much of it each one gets (a bay of windows throws more than a single narrow pane).
        public readonly List<Light> GroundLights = new List<Light>();
        public readonly List<float> GroundLightWeights = new List<float>();
        public bool GroundLightsOn;

        public float NextFireRescan;
        public bool SummaryLogged;

        public float GlowTarget;
        public float GlowLevel;
        public float AppliedLevel;
        public bool GlowApplied;
        public float LastGlowUpdate = -1f;
        public float NextGlowApply;
        public float FlickerSeed;

        public bool Failed;
    }

    // One window glass material slot on one shell renderer.
    public class WindowGlassSlot
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public string MaterialName;
        public Color BaseColor;
        public float BaseEmissive;

        // Only for glass whose material ships without an emissive texture (see CollectWindowGlassSlots).
        public Texture EmissiveTexture;

        public MaterialPropertyBlock Block;
    }

    // One window pane of a shell: centre and outward direction in the shell root's local space, pane
    // size in metres. Plain floats on purpose - this is static data, built before Unity is up.
    public struct WindowSpot
    {
        public float X, Y, Z, NX, NZ, Width, Height;

        public WindowSpot(float x, float y, float z, float nx, float nz, float width, float height)
        {
            X = x; Y = y; Z = z; NX = nx; NZ = nz; Width = width; Height = height;
        }
    }

    public partial class SeamlessInteriorsMod
    {
        // ─── EXTERIOR FIRE EFFECTS: CHIMNEY SMOKE, WINDOW GLOW, GLOW ON THE GROUND ───
        //
        // CHIMNEY SMOKE. The building shells carry Tech/SmokeControl: a Chimney with an
        // FX_SmokeChimneyA SmokeTrail under it. Chimney.Update runs the smoke on its own - it counts
        // m_LifetimeGameMinutes down in game time, keeps the trail on while that is above zero and
        // fades it out over the last half hour. The interior stove's Fire names the chimney it
        // belongs to (m_ChimneyGuid), and vanilla hands the fire's remaining burn time to that chimney
        // when the player walks out through the loading screen (FireManager.SerializeChimneyData /
        // DeserializeChimneyData). That hand-over is skipped whenever the scene did not change, and
        // this mod never changes the scene, so the chimneys stayed cold. The same number is written
        // here directly, once a second, for as long as a fire burns.
        //
        // WINDOW GLOW. Wintermute lights Grey Mother's windows with TodMaterial: from dusk the glass
        // slot's _Color goes from grey to warm orange and its emissive strength comes up, and at dawn
        // both go back. The component is still on the sandbox shells of the Trapper's Cabin and Grey
        // Mother's house, switched off by m_StoryOnly - and switching it on would light the windows
        // every night whether anybody is home or not. So its colour and strength are applied here
        // instead, only between dusk and dawn and only while a fire burns inside, through a
        // MaterialPropertyBlock on the glass slot: no material is copied or replaced, and an empty
        // block puts the original look back exactly.
        //
        // GLOW ON THE GROUND. This one is not from the game: Wintermute's Grey Mother's house has no
        // light near it at all (the story scene was checked - the only things around the house are the
        // TodMaterial, the chimney and set dressing). A spot light sits just outside each ground-floor
        // window, tilted down onto the snow, and follows the window glow - same night curve, same fire,
        // same flicker. The window positions cannot be read at runtime (the shell meshes are not
        // readable), so they were taken offline from each shell mesh's window glass submesh and live
        // in s_ShellWindows. Neighbouring panes share one light, and the lights only switch on while
        // the player is close enough to see them.
        //
        // None of the effects saves anything. They follow the fires, whose state the mod already saves
        // and restores - including in closed clones, where FrozenTime.cs keeps their burn time running.

        private const float EXTERIOR_FX_TICK_INTERVAL = 1f;
        private const float EXTERIOR_FX_FIRE_RESCAN_INTERVAL = 60f;

        // TodMaterial's night colour on the Wintermute house, and the emissive strength it derives
        // from that colour (the average of the three channels).
        private const float GLOW_NIGHT_R = 1f;
        private const float GLOW_NIGHT_G = 0.5306f;
        private const float GLOW_NIGHT_B = 0.1176f;
        private const float GLOW_NIGHT_EMISSIVE = (GLOW_NIGHT_R + GLOW_NIGHT_G + GLOW_NIGHT_B) / 3f;

        // A dying fire dims the windows over the same last half hour in which SmokeTrail fades the
        // chimney smoke out (its m_SourceFireFadeStart).
        private const float GLOW_FIRE_FADE_MINUTES = 30f;

        private const float GLOW_FADE_SECONDS = 2.5f;
        private const float GLOW_APPLY_INTERVAL = 0.05f;

        // Very slight: firelight behind the glass, not a lamp shorting out.
        private const float GLOW_FLICKER_AMPLITUDE = 0.07f;
        private const float GLOW_FLICKER_DISTANCE = 400f;

        private const string WINDOW_GLASS_PREFIX = "STR_WindowGlassMix";

        // Ground glow. Windows higher than this above the shell root are upstairs: the stoves are all
        // on the ground floor, and a light from up there would land far out on the snow.
        private const float GROUND_GLOW_MAX_WINDOW_HEIGHT = 4.5f;
        private const float GROUND_GLOW_MERGE_DISTANCE = 1.8f;
        private const float GROUND_GLOW_OUTSET = 0.35f;
        private const float GROUND_GLOW_DROP = 0.2f;
        private const float GROUND_GLOW_TILT_DEGREES = 40f;
        private const float GROUND_GLOW_SPOT_ANGLE = 110f;
        private const float GROUND_GLOW_INNER_ANGLE = 45f;
        private const float GROUND_GLOW_RANGE = 8f;
        private const float GROUND_GLOW_INTENSITY = 1.4f;
        private const float GROUND_GLOW_R = 1f;
        private const float GROUND_GLOW_G = 0.6f;
        private const float GROUND_GLOW_B = 0.28f;
        private const float GROUND_GLOW_DISTANCE = 90f;
        private const string GROUND_GLOW_OBJECT_NAME = "SeamlessInteriors_WindowGroundGlow";

        private const float EXTERIOR_FX_PREVIEW_RADIUS = 250f;
        private const float EXTERIOR_FX_PREVIEW_SMOKE_MINUTES = 60f;

        private static float s_NextExteriorFxTick = 0f;
        private static float s_ExteriorFxNight = 0f;
        private static bool s_ExteriorFxGlowActive = false;
        private static SeamlessInteriorInstance s_ExteriorFxPreview = null;

        private static int s_GlowColorId = -1;
        private static int s_GlowEmissiveStrengthId;
        private static int s_GlowEmissiveTextureId;
        private static int s_GroundGlowMask = 0;

        // Window panes of the supported shells, keyed by ExteriorShellPrefabName: centre and outward
        // direction in the shell root's local space, pane width and height. Read out of each shell
        // mesh's STR_WindowGlassMix submesh (triangles clustered into panes, the winding giving the
        // outside) from the game's asset bundles.
        private static readonly Dictionary<string, WindowSpot[]> s_ShellWindows = new Dictionary<string, WindowSpot[]>
        {
            // Trapper's Cabin
            {
                "STRSPAWN_CabinAExterior_Prefab", new[]
                {
                    new WindowSpot(4.017f, 2.808f, 0.014f, 0.445f, 0.896f, 0.68f, 1.24f),
                    new WindowSpot(-5.775f, 2.702f, 3.641f, -0.895f, 0.445f, 0.47f, 0.87f),
                    new WindowSpot(5.467f, 2.842f, -3.174f, 0.895f, -0.445f, 0.64f, 1.24f),
                    new WindowSpot(2.513f, 2.773f, -4.15f, -0.445f, -0.896f, 0.64f, 1.24f),
                    new WindowSpot(-0.134f, 2.711f, 2.154f, 0.445f, 0.896f, 0.64f, 1.24f),
                    new WindowSpot(-4.737f, 2.603f, 0.892f, -0.895f, 0.445f, 0.64f, 1.24f),
                    new WindowSpot(5.431f, 5.598f, -3.201f, 0.895f, -0.445f, 0.47f, 0.87f),
                }
            },
            // Camp Office
            {
                "STRSPAWN_CampOffice_Prefab", new[]
                {
                    new WindowSpot(0.073f, 2.539f, -3.293f, 1f, 0f, 0.98f, 0.55f),
                    new WindowSpot(3.467f, 2.738f, -2.276f, 0f, -1f, 0.97f, 1.04f),
                    new WindowSpot(4.871f, 6.339f, 0.396f, 1f, 0f, 0.97f, 1.04f),
                    new WindowSpot(-4.899f, 6.339f, 0.335f, -1f, 0f, 0.97f, 1.04f),
                    new WindowSpot(0.636f, 2.722f, 3.105f, 0f, 1f, 0.97f, 1.04f),
                    new WindowSpot(4.871f, 2.738f, -0.731f, 1f, 0f, 0.97f, 1.04f),
                    new WindowSpot(-3.666f, 2.72f, 3.103f, 0f, 1f, 0.97f, 1.04f),
                    new WindowSpot(-4.909f, 2.738f, 1.449f, -1f, 0f, 0.97f, 1.04f),
                }
            },
            // Pleasant Valley farmhouse
            {
                "STRSPAWN_FarmHouseA_Prefab", new[]
                {
                    new WindowSpot(-4.348f, 6.82f, 8.352f, -1f, 0f, 0.77f, 1.68f),
                    new WindowSpot(-4.346f, 6.793f, 3.009f, -1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(-4.339f, 2.63f, 5.532f, -1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(-4.339f, 2.63f, 8.471f, -1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(-8.897f, 6.812f, -0.729f, -1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(-8.889f, 2.649f, -0.733f, -1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(-9.82f, 2.63f, -3.165f, -1f, 0f, 0.54f, 1.62f),
                    new WindowSpot(-9.811f, 2.63f, -5.385f, -1f, 0f, 0.54f, 1.62f),
                    new WindowSpot(-10.196f, 2.635f, -4.263f, -1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(-5.357f, 6.793f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(-4.369f, 6.793f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(-0.506f, 6.793f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(0.481f, 6.793f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(4.38f, 6.793f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(5.367f, 6.793f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(-5.358f, 2.641f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(-4.37f, 2.641f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(4.377f, 2.666f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(5.365f, 2.666f, -7.167f, 0f, -1f, 0.7f, 1.62f),
                    new WindowSpot(4.339f, 6.793f, 3.009f, 1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(4.339f, 6.793f, 8.371f, 1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(8.905f, 2.649f, -0.733f, 1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(8.908f, 6.812f, -0.729f, 1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(4.339f, 2.63f, 2.636f, 1f, 0f, 0.7f, 1.62f),
                    new WindowSpot(8.905f, 2.63f, -5.459f, 1f, 0f, 0.71f, 1.62f),
                    new WindowSpot(0.514f, 2.641f, 10.243f, 0f, 1f, 0.7f, 1.62f),
                    new WindowSpot(-0.473f, 2.641f, 10.243f, 0f, 1f, 0.7f, 1.62f),
                }
            },
            // Grey Mother's house
            {
                "STR_GreyMothersHouse_Prefab", new[]
                {
                    new WindowSpot(7.621f, 3.034f, -0.299f, 1f, 0f, 0.97f, 2.2f),
                    new WindowSpot(0.05f, 4.125f, -2.206f, 0f, -1f, 1.33f, 0.53f),
                    new WindowSpot(-6.701f, 7.552f, -0.3f, -1f, 0f, 0.75f, 1.43f),
                    new WindowSpot(6.711f, 7.58f, 3.43f, 1f, 0f, 0.75f, 1.43f),
                    new WindowSpot(-4.128f, 3.03f, -2.182f, 0f, -1f, 0.98f, 2.2f),
                    new WindowSpot(-3.786f, 3.03f, 5.344f, 0f, 1f, 0.98f, 2.2f),
                    new WindowSpot(4.23f, 3.034f, -2.182f, 0f, -1f, 0.97f, 2.2f),
                    new WindowSpot(3.855f, 3.034f, 5.344f, 0f, 1f, 0.97f, 2.2f),
                    new WindowSpot(-7.524f, 3.034f, 3.446f, -1f, 0f, 0.97f, 2.2f),
                    new WindowSpot(-0.003f, 7.616f, -4.442f, 0f, -1f, 0.81f, 1.43f),
                    new WindowSpot(-4.225f, 7.552f, -2.176f, 0f, -1f, 0.75f, 1.43f),
                    new WindowSpot(-3.234f, 7.552f, 5.147f, 0f, 1f, 0.75f, 1.43f),
                    new WindowSpot(4.2f, 7.552f, -2.176f, 0f, -1f, 0.75f, 1.43f),
                    new WindowSpot(3.407f, 7.552f, 5.147f, 0f, 1f, 0.75f, 1.43f),
                }
            },
        };

        // Called from OnUpdate every frame.
        public static void TickExteriorFireEffects(Vector3 playerPos)
        {
            bool smoke = IsChimneySmokeEnabled;
            bool glow = IsWindowGlowEnabled;
            bool ground = IsWindowGroundGlowEnabled;

            if (!smoke && !glow && !ground && s_ExteriorFxPreview == null)
            {
                // All switched off: take down whatever glow is still on, once.
                if (s_ExteriorFxGlowActive) ClearAllWindowGlow();
                return;
            }

            // A load is switching the clones on and off behind a black screen.
            if (s_ScreenHeldBlack || IsAnyCloningActive()) return;

            float now = Time.realtimeSinceStartup;
            bool slowTick = now >= s_NextExteriorFxTick;
            if (slowTick)
            {
                s_NextExteriorFxTick = now + EXTERIOR_FX_TICK_INTERVAL;
                s_ExteriorFxNight = ComputeNightFactor();
            }

            // Building a cache scans the shell and the whole clone. Right after a load every building
            // needs one, so they are spread over consecutive frames instead of all landing in the first.
            bool builtThisFrame = false;

            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || !instance.RunCompleted || instance.InteriorPersisted) continue;
                if (instance.MasterInterior == null || instance.ExteriorShell == null) continue;

                ExteriorFireFx fx = instance.ExteriorFx;
                if (fx == null || !ReferenceEquals(fx.Shell, instance.ExteriorShell))
                {
                    if (builtThisFrame) continue;
                    builtThisFrame = true;
                    fx = GetExteriorFireFx(instance);
                }
                if (fx.Failed) continue;

                try
                {
                    if (slowTick) EvaluateExteriorFires(instance, fx, now, smoke, glow || ground);
                    UpdateWindowGlow(instance, fx, playerPos, now);
                }
                catch (System.Exception ex)
                {
                    // Every frame from here on would throw the same way: stop this building only.
                    fx.Failed = true;
                    MelonLogger.Warning($"[DIS-EFEKT] {instance.Config.ResolvedInstanceId}: dis efektler durduruldu: {ex.Message}");
                    try { ClearWindowGlow(fx); } catch { }
                }
            }
        }

        // The region is going away, and with it every chimney, shell renderer and light the effects touched.
        public static void ForgetExteriorFireEffects()
        {
            s_ExteriorFxPreview = null;
            s_ExteriorFxGlowActive = false;
        }

        private static ExteriorFireFx GetExteriorFireFx(SeamlessInteriorInstance instance)
        {
            ExteriorFireFx fx = instance.ExteriorFx;
            if (fx != null && ReferenceEquals(fx.Shell, instance.ExteriorShell)) return fx;

            fx = BuildExteriorFireFx(instance);
            instance.ExteriorFx = fx;
            return fx;
        }

        private static ExteriorFireFx BuildExteriorFireFx(SeamlessInteriorInstance instance)
        {
            string id = instance.Config.ResolvedInstanceId;
            var fx = new ExteriorFireFx
            {
                Shell = instance.ExteriorShell,
                // Neighbouring buildings must not flicker in step.
                FlickerSeed = (id.GetHashCode() & 0xFFFF) * 0.001f,
            };

            try
            {
                foreach (var chimney in fx.Shell.GetComponentsInChildren<Il2Cpp.Chimney>(true))
                    if (chimney != null) fx.Chimneys.Add(chimney);
                fx.ChimneyMinutes = new float[fx.Chimneys.Count];

                CollectWindowGlassSlots(fx, id);
                CreateWindowGroundLights(instance, fx);
                RescanExteriorFxFires(instance, fx, Time.realtimeSinceStartup);
            }
            catch (System.Exception ex)
            {
                fx.Failed = true;
                MelonLogger.Warning($"[DIS-EFEKT] {id}: baca ve pencere taramasi basarisiz: {ex.Message}");
            }

            return fx;
        }

        private static void RescanExteriorFxFires(SeamlessInteriorInstance instance, ExteriorFireFx fx, float now)
        {
            fx.NextFireRescan = now + EXTERIOR_FX_FIRE_RESCAN_INTERVAL;
            fx.Fires.Clear();
            fx.FireChimney.Clear();

            string id = instance.Config.ResolvedInstanceId;
            int viaGuid = 0, viaDistance = 0;

            foreach (var fire in instance.MasterInterior.GetComponentsInChildren<Il2Cpp.Fire>(true))
            {
                if (fire == null) continue;

                bool linked;
                int chimney = ResolveFireChimney(fx, fire, id, out linked);
                fx.Fires.Add(fire);
                fx.FireChimney.Add(chimney);

                if (chimney < 0) continue;
                if (linked) viaGuid++; else viaDistance++;
            }

            // One line per building per load, and only for buildings with a fire at all - the lake
            // cabins and the trailers have none.
            if (fx.Fires.Count > 0 && !fx.SummaryLogged)
            {
                fx.SummaryLogged = true;
                MelonLogger.Msg($"[DIS-EFEKT] {id}: {fx.Fires.Count} ates, {fx.Chimneys.Count} baca " +
                                $"({viaGuid} ates kendi bacasina, {viaDistance} ates en yakin bacaya bagli), " +
                                $"{fx.Windows.Count} pencere cami slotu, {fx.GroundLights.Count} zemin isigi.");
            }
        }

        private static int ResolveFireChimney(ExteriorFireFx fx, Il2Cpp.Fire fire, string id, out bool linked)
        {
            linked = false;
            if (fx.Chimneys.Count == 0) return -1;

            // The chimney this stove was built for. Chimney.FindByGuid is the lookup the game's own
            // chimney restore makes with this very guid.
            string guid = fire.m_ChimneyGuid;
            if (!string.IsNullOrEmpty(guid))
            {
                int cached;
                if (!fx.ChimneyByGuid.TryGetValue(guid, out cached))
                {
                    cached = IndexOfChimney(fx, Il2Cpp.Chimney.FindByGuid(guid));

                    // Only a hit is remembered. A miss is asked again at the next rescan, in case the
                    // lookup ran before the region had registered its chimneys.
                    if (cached >= 0) fx.ChimneyByGuid[guid] = cached;
                    else if (s_DebugBounds)
                        MelonLogger.Msg($"[BACA] {id}: '{guid}' bacasi bu binanin kabugunda yok, en yakin baca kullaniliyor.");
                }

                if (cached >= 0)
                {
                    linked = true;
                    return cached;
                }
            }

            // Every fire smokes, not only the linked ones: a metal stove without a chimney of its own
            // uses the building's nearest one.
            return NearestChimney(fx, fire.transform.position);
        }

        private static int IndexOfChimney(ExteriorFireFx fx, Il2Cpp.Chimney chimney)
        {
            if (chimney == null) return -1;

            int wanted = chimney.GetInstanceID();
            for (int c = 0; c < fx.Chimneys.Count; c++)
                if (fx.Chimneys[c] != null && fx.Chimneys[c].GetInstanceID() == wanted) return c;

            return -1;
        }

        private static int NearestChimney(ExteriorFireFx fx, Vector3 pos)
        {
            int best = -1;
            float bestDist = float.MaxValue;

            for (int c = 0; c < fx.Chimneys.Count; c++)
            {
                if (fx.Chimneys[c] == null) continue;

                Vector3 p = ChimneySmokePosition(fx.Chimneys[c]);
                float dx = p.x - pos.x, dz = p.z - pos.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }

            return best;
        }

        // Two chimneys of one building can share the building's origin (the camp office's
        // SmokeControlUp and SmokeControlDown both sit there); where the smoke leaves the roof is
        // the trail's own position.
        private static Vector3 ChimneySmokePosition(Il2Cpp.Chimney chimney)
        {
            Il2Cpp.SmokeTrail trail = chimney.m_SmokeTrail;
            return trail != null ? trail.transform.position : chimney.transform.position;
        }

        private static void EvaluateExteriorFires(SeamlessInteriorInstance instance, ExteriorFireFx fx, float now, bool smoke, bool glow)
        {
            if (now >= fx.NextFireRescan) RescanExteriorFxFires(instance, fx, now);

            bool preview = ReferenceEquals(s_ExteriorFxPreview, instance);

            for (int c = 0; c < fx.ChimneyMinutes.Length; c++) fx.ChimneyMinutes[c] = 0f;

            // The fire with the most life left decides all the effects.
            float strongest = 0f;
            for (int i = 0; i < fx.Fires.Count; i++)
            {
                Il2Cpp.Fire fire = fx.Fires[i];
                if (fire == null || !IsFireStillAlive(fire)) continue;

                float minutes = fire.GetRemainingLifeTimeSeconds() / 60f;
                float strength = FxClamp01(minutes / GLOW_FIRE_FADE_MINUTES);
                if (strength > strongest) strongest = strength;

                int c = fx.FireChimney[i];
                if (c >= 0 && minutes > fx.ChimneyMinutes[c]) fx.ChimneyMinutes[c] = minutes;
            }

            if (smoke || preview)
            {
                for (int c = 0; c < fx.Chimneys.Count; c++)
                {
                    Il2Cpp.Chimney chimney = fx.Chimneys[c];
                    if (chimney == null) continue;

                    float minutes = fx.ChimneyMinutes[c];
                    if (preview && minutes < EXTERIOR_FX_PREVIEW_SMOKE_MINUTES) minutes = EXTERIOR_FX_PREVIEW_SMOKE_MINUTES;

                    // Nothing burns behind this chimney: it is left to wind its own smoke down.
                    if (minutes > 0f) chimney.m_LifetimeGameMinutes = minutes;
                }
            }

            fx.GlowTarget = preview ? 1f : (glow ? s_ExteriorFxNight * strongest : 0f);
        }

        // 0 in daylight, 1 at night: TodMaterial's curve - in over dusk, out over dawn.
        private static float ComputeNightFactor()
        {
            TimeOfDay tod = GameManager.GetTimeOfDayComponent();
            if (tod == null) return 0f;

            if (tod.IsDusk()) return FxClamp01(tod.GetProgressDusk());
            if (tod.IsDawn()) return 1f - FxClamp01(tod.GetProgressDawn());
            return tod.IsNight() ? 1f : 0f;
        }

        private static void CollectWindowGlassSlots(ExteriorFireFx fx, string id)
        {
            EnsureGlowPropertyIds();

            foreach (var renderer in fx.Shell.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null) continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null) continue;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null) continue;

                    string name = material.name;
                    if (name == null || !name.StartsWith(WINDOW_GLASS_PREFIX, System.StringComparison.Ordinal)) continue;

                    // The see-through "Alpha" panes (the farmhouse porch, burned glass) use a transparent
                    // shader and are not the building's windows.
                    if (name.IndexOf("Alpha", System.StringComparison.Ordinal) >= 0) continue;

                    if (!material.HasProperty(s_GlowColorId) || !material.HasProperty(s_GlowEmissiveStrengthId))
                    {
                        if (s_DebugBounds)
                            MelonLogger.Msg($"[PENCERE] {id}: '{name}' camda renk/emisyon ozelligi yok, atlandi.");
                        continue;
                    }

                    var slot = new WindowGlassSlot
                    {
                        Renderer = renderer,
                        MaterialIndex = i,
                        MaterialName = name,
                        BaseColor = material.GetColor(s_GlowColorId),
                        BaseEmissive = material.GetFloat(s_GlowEmissiveStrengthId),
                        Block = new MaterialPropertyBlock(),
                    };

                    // The Trapper's Cabin and Grey Mother's glass carry an emissive texture; the camp
                    // office and farmhouse glass leave it empty, which gives the strength nothing to
                    // light. They get their own glass texture - the one the other two use there.
                    if (material.HasProperty(s_GlowEmissiveTextureId) && material.GetTexture(s_GlowEmissiveTextureId) == null)
                        slot.EmissiveTexture = material.mainTexture;

                    fx.Windows.Add(slot);
                }
            }
        }

        private static void EnsureGlowPropertyIds()
        {
            if (s_GlowColorId != -1) return;

            s_GlowColorId = Shader.PropertyToID("_Color");
            s_GlowEmissiveStrengthId = Shader.PropertyToID("_EmissiveStrength");
            s_GlowEmissiveTextureId = Shader.PropertyToID("_Emissive");
        }

        private class WindowGroup
        {
            public float X, Y, Z, NX, NZ, Area;
            public int Count;
        }

        private static void CreateWindowGroundLights(SeamlessInteriorInstance instance, ExteriorFireFx fx)
        {
            WindowSpot[] spots;
            string shellName = instance.Config.ExteriorShellPrefabName;
            if (string.IsNullOrEmpty(shellName) || !s_ShellWindows.TryGetValue(shellName, out spots)) return;

            Transform shellT = fx.Shell.transform;

            // Lights left on this shell by an earlier build of its cache.
            for (int i = shellT.childCount - 1; i >= 0; i--)
            {
                Transform child = shellT.GetChild(i);
                if (child != null && child.name == GROUND_GLOW_OBJECT_NAME) UnityEngine.Object.Destroy(child.gameObject);
            }

            int mask = GroundGlowCullingMask();
            double tilt = GROUND_GLOW_TILT_DEGREES * System.Math.PI / 180.0;
            float cos = (float)System.Math.Cos(tilt), sin = (float)System.Math.Sin(tilt);

            foreach (WindowGroup group in GroupGroundFloorWindows(spots))
            {
                // The shell hides itself while the player is inside, which takes these lights with it.
                var go = new GameObject(GROUND_GLOW_OBJECT_NAME);
                go.transform.SetParent(shellT, false);
                go.transform.localPosition = new Vector3(
                    group.X + group.NX * GROUND_GLOW_OUTSET,
                    group.Y - GROUND_GLOW_DROP,
                    group.Z + group.NZ * GROUND_GLOW_OUTSET);
                go.transform.localRotation = Quaternion.LookRotation(new Vector3(group.NX * cos, -sin, group.NZ * cos), Vector3.up);

                Light light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = GROUND_GLOW_SPOT_ANGLE;
                light.innerSpotAngle = GROUND_GLOW_INNER_ANGLE;
                light.range = GROUND_GLOW_RANGE;
                light.color = new Color(GROUND_GLOW_R, GROUND_GLOW_G, GROUND_GLOW_B, 1f);
                light.intensity = 0f;
                light.bounceIntensity = 0f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.Auto;
                light.cullingMask = mask;
                light.enabled = false;

                fx.GroundLights.Add(light);
                fx.GroundLightWeights.Add(FxClamp(group.Area, 0.7f, 1.5f));
            }
        }

        // Ground-floor panes only, and neighbouring panes on the same wall - a double window, a bay -
        // share one light.
        private static List<WindowGroup> GroupGroundFloorWindows(WindowSpot[] spots)
        {
            var groups = new List<WindowGroup>();

            foreach (WindowSpot s in spots)
            {
                if (s.Y > GROUND_GLOW_MAX_WINDOW_HEIGHT) continue;

                WindowGroup into = null;
                foreach (WindowGroup g in groups)
                {
                    if (g.NX * s.NX + g.NZ * s.NZ < 0.95f || System.Math.Abs(g.Y - s.Y) > 1f) continue;

                    float dx = g.X - s.X, dz = g.Z - s.Z;
                    if (dx * dx + dz * dz > GROUND_GLOW_MERGE_DISTANCE * GROUND_GLOW_MERGE_DISTANCE) continue;

                    into = g;
                    break;
                }

                float area = s.Width * s.Height;
                if (into == null)
                {
                    groups.Add(new WindowGroup { X = s.X, Y = s.Y, Z = s.Z, NX = s.NX, NZ = s.NZ, Area = area, Count = 1 });
                    continue;
                }

                into.X = (into.X * into.Count + s.X) / (into.Count + 1);
                into.Y = (into.Y * into.Count + s.Y) / (into.Count + 1);
                into.Z = (into.Z * into.Count + s.Z) / (into.Count + 1);
                into.Area += area;
                into.Count++;
            }

            return groups;
        }

        // Everything the window light can fall on - snow, terrain, buildings, props, animals - but not
        // the interface or the first-person weapon view.
        private static int GroundGlowCullingMask()
        {
            if (s_GroundGlowMask != 0) return s_GroundGlowMask;

            int mask = ~0;
            foreach (string layer in new[] { "UI", "Weapon", "InspectGear" })
            {
                int index = LayerMask.NameToLayer(layer);
                if (index >= 0) mask &= ~(1 << index);
            }

            s_GroundGlowMask = mask;
            return mask;
        }

        private static void UpdateWindowGlow(SeamlessInteriorInstance instance, ExteriorFireFx fx, Vector3 playerPos, float now)
        {
            if (fx.Windows.Count == 0 && fx.GroundLights.Count == 0) return;

            float dt = fx.LastGlowUpdate < 0f ? 0f : now - fx.LastGlowUpdate;
            fx.LastGlowUpdate = now;
            if (dt > 0.25f) dt = 0.25f;

            fx.GlowLevel = FxMoveTowards(fx.GlowLevel, fx.GlowTarget, dt / GLOW_FADE_SECONDS);

            if (fx.GlowLevel <= 0f)
            {
                if (fx.GlowApplied) ClearWindowGlow(fx);
                return;
            }

            // The player is inside this very building, which switches the shell off: nothing to see.
            if (!fx.Shell.activeInHierarchy) return;

            if (now < fx.NextGlowApply) return;
            fx.NextGlowApply = now + GLOW_APPLY_INTERVAL;

            bool preview = ReferenceEquals(s_ExteriorFxPreview, instance);

            Vector3 anchor = instance.Config.FallbackPosition;
            float px = playerPos.x - anchor.x, py = playerPos.y - anchor.y, pz = playerPos.z - anchor.z;
            float distSq = px * px + py * py + pz * pz;

            bool windows = fx.Windows.Count > 0 && (IsWindowGlowEnabled || preview);
            bool groundLights = fx.GroundLights.Count > 0 && (IsWindowGroundGlowEnabled || preview)
                                && distSq <= GROUND_GLOW_DISTANCE * GROUND_GLOW_DISTANCE;
            bool flicker = IsWindowGlowFlickerEnabled && distSq <= GLOW_FLICKER_DISTANCE * GLOW_FLICKER_DISTANCE;

            // Too far away to see a flicker: only follow real changes in brightness.
            if (!flicker && fx.GlowApplied && groundLights == fx.GroundLightsOn
                && System.Math.Abs(fx.AppliedLevel - fx.GlowLevel) < 0.01f) return;

            float k = fx.GlowLevel;
            float flickerScale = flicker ? 1f + GLOW_FLICKER_AMPLITUDE * FlickerWave(now, fx.FlickerSeed) : 1f;

            if (windows)
            {
                float emissive = GLOW_NIGHT_EMISSIVE * WindowGlowStrength * k * flickerScale;

                for (int i = 0; i < fx.Windows.Count; i++)
                {
                    WindowGlassSlot slot = fx.Windows[i];
                    if (slot.Renderer == null) continue;

                    MaterialPropertyBlock block = slot.Block;
                    block.Clear();
                    block.SetColor(s_GlowColorId, new Color(
                        FxLerp(slot.BaseColor.r, GLOW_NIGHT_R, k),
                        FxLerp(slot.BaseColor.g, GLOW_NIGHT_G, k),
                        FxLerp(slot.BaseColor.b, GLOW_NIGHT_B, k),
                        slot.BaseColor.a));
                    block.SetFloat(s_GlowEmissiveStrengthId, slot.BaseEmissive + emissive);
                    if (slot.EmissiveTexture != null) block.SetTexture(s_GlowEmissiveTextureId, slot.EmissiveTexture);

                    slot.Renderer.SetPropertyBlock(block, slot.MaterialIndex);
                }
            }

            if (groundLights)
            {
                float intensity = GROUND_GLOW_INTENSITY * WindowGroundGlowStrength * k * flickerScale;
                for (int i = 0; i < fx.GroundLights.Count; i++)
                {
                    Light light = fx.GroundLights[i];
                    if (light != null) light.intensity = intensity * fx.GroundLightWeights[i];
                }
            }
            SetGroundLights(fx, groundLights);

            fx.GlowApplied = true;
            fx.AppliedLevel = k;
            s_ExteriorFxGlowActive = true;
        }

        private static void SetGroundLights(ExteriorFireFx fx, bool on)
        {
            if (fx.GroundLightsOn == on) return;

            for (int i = 0; i < fx.GroundLights.Count; i++)
                if (fx.GroundLights[i] != null) fx.GroundLights[i].enabled = on;

            fx.GroundLightsOn = on;
        }

        private static void ClearWindowGlow(ExteriorFireFx fx)
        {
            for (int i = 0; i < fx.Windows.Count; i++)
            {
                WindowGlassSlot slot = fx.Windows[i];
                if (slot.Renderer == null) continue;

                // An empty block: no overrides left, the glass draws from its own material again.
                slot.Block.Clear();
                slot.Renderer.SetPropertyBlock(slot.Block, slot.MaterialIndex);
            }

            SetGroundLights(fx, false);

            fx.GlowApplied = false;
            fx.AppliedLevel = 0f;
        }

        private static void ClearAllWindowGlow()
        {
            foreach (var instance in ActiveInteriors.Values)
            {
                ExteriorFireFx fx = instance != null ? instance.ExteriorFx : null;
                if (fx == null) continue;

                fx.GlowLevel = 0f;
                fx.GlowTarget = 0f;
                if (!fx.GlowApplied) continue;

                try { ClearWindowGlow(fx); } catch { }
            }

            s_ExteriorFxGlowActive = false;
        }

        // Three sines at unrelated rates: smooth, never visibly repeating, always within [-1, 1].
        private static float FlickerWave(float t, float seed)
        {
            double v = 0.55 * System.Math.Sin(t * 2.3 + seed)
                     + 0.30 * System.Math.Sin(t * 5.3 + seed * 1.7)
                     + 0.15 * System.Math.Sin(t * 11.9 + seed * 2.9);
            return (float)v;
        }

        // Plain managed maths: this runs every frame for every glowing window.
        private static float FxClamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        private static float FxClamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }

        private static float FxLerp(float a, float b, float t) { return a + (b - a) * t; }

        private static float FxMoveTowards(float current, float target, float maxDelta)
        {
            if (current < target) return current + maxDelta >= target ? target : current + maxDelta;
            return current - maxDelta <= target ? target : current - maxDelta;
        }

        // ─── SHIFT+F9: PREVIEW ───
        //
        // Lights the nearest building's windows, ground and chimney regardless of fire and time of day
        // and writes what the shell offers to the log - so the look can be checked without lighting a
        // stove and waiting for dusk. A second press turns it off.
        public static void ToggleExteriorFxPreview()
        {
            if (s_ExteriorFxPreview != null)
            {
                SeamlessInteriorInstance previous = s_ExteriorFxPreview;
                s_ExteriorFxPreview = null;

                // The preview smoke has no fire behind it: stop it now rather than let the hour run out.
                ExteriorFireFx pfx = previous.ExteriorFx;
                if (pfx != null && !pfx.Failed)
                {
                    for (int c = 0; c < pfx.Chimneys.Count; c++)
                        if (pfx.Chimneys[c] != null && pfx.ChimneyMinutes[c] <= 0f)
                            pfx.Chimneys[c].m_LifetimeGameMinutes = 0f;
                }

                s_LightingModeMessage = "Dis efekt onizlemesi kapandi";
                s_LightingModeMessageTimer = 3f;
                MelonLogger.Msg($"[DIS-EFEKT] Onizleme kapatildi ({previous.Config.ResolvedInstanceId}).");
                return;
            }

            Transform playerT = GameManager.GetPlayerTransform();
            if (playerT == null) return;
            Vector3 pos = playerT.position;

            SeamlessInteriorInstance nearest = null;
            float nearestDist = EXTERIOR_FX_PREVIEW_RADIUS * EXTERIOR_FX_PREVIEW_RADIUS;
            foreach (var instance in ActiveInteriors.Values)
            {
                if (instance == null || !instance.RunCompleted || instance.InteriorPersisted) continue;
                if (instance.MasterInterior == null || instance.ExteriorShell == null) continue;

                Vector3 a = instance.Config.FallbackPosition;
                float dx = pos.x - a.x, dy = pos.y - a.y, dz = pos.z - a.z;
                float d = dx * dx + dy * dy + dz * dz;
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearest = instance;
                }
            }

            if (nearest == null)
            {
                s_LightingModeMessage = "Yakinda dis kabugu olan bir bina yok";
                s_LightingModeMessageTimer = 3f;
                return;
            }

            ExteriorFireFx fx = GetExteriorFireFx(nearest);
            DumpExteriorFireFx(nearest, fx);

            s_ExteriorFxPreview = nearest;
            s_NextExteriorFxTick = 0f; // apply on the next frame, not up to a second later

            s_LightingModeMessage = $"Dis efekt onizlemesi: {nearest.Config.ResolvedInstanceId}\n(baca, pencere ve zemin isigi zorla acik, loga bak)";
            s_LightingModeMessageTimer = 4f;
        }

        private static void DumpExteriorFireFx(SeamlessInteriorInstance instance, ExteriorFireFx fx)
        {
            string id = instance.Config.ResolvedInstanceId;

            if (fx.Failed || fx.Shell == null)
            {
                MelonLogger.Msg($"[DIS-EFEKT] {id} teshis: tarama basarisiz, bu binada dis efektler kapali.");
                return;
            }

            MelonLogger.Msg($"[DIS-EFEKT] {id} teshis: kabuk='{fx.Shell.name}' acik={fx.Shell.activeInHierarchy} " +
                            $"gece={ComputeNightFactor():F2} duman={IsChimneySmokeEnabled} parilti={IsWindowGlowEnabled} " +
                            $"zemin={IsWindowGroundGlowEnabled} titreme={IsWindowGlowFlickerEnabled} " +
                            $"guc={WindowGlowStrength:F2}/{WindowGroundGlowStrength:F2}");

            for (int c = 0; c < fx.Chimneys.Count; c++)
            {
                Il2Cpp.Chimney chimney = fx.Chimneys[c];
                if (chimney == null)
                {
                    MelonLogger.Msg($"[DIS-EFEKT]   baca {c}: artik yok");
                    continue;
                }

                Vector3 p = ChimneySmokePosition(chimney);
                MelonLogger.Msg($"[DIS-EFEKT]   baca {c}: '{chimney.name}' duman=({p.x:F1}, {p.y:F1}, {p.z:F1}) " +
                                $"kalan={chimney.m_LifetimeGameMinutes:F1} dk");
            }
            if (fx.Chimneys.Count == 0) MelonLogger.Msg("[DIS-EFEKT]   bu kabukta baca yok");

            for (int i = 0; i < fx.Fires.Count; i++)
            {
                Il2Cpp.Fire fire = fx.Fires[i];
                if (fire == null) continue;

                int c = fx.FireChimney[i];
                MelonLogger.Msg($"[DIS-EFEKT]   ates {i}: '{fire.transform.parent?.name}' guid='{fire.m_ChimneyGuid}' " +
                                $"yaniyor={IsFireStillAlive(fire)} kalan={fire.GetRemainingLifeTimeSeconds() / 60f:F1} dk " +
                                $"-> {(c >= 0 ? "baca " + c : "baca yok")}");
            }
            if (fx.Fires.Count == 0) MelonLogger.Msg("[DIS-EFEKT]   klonda ates yok");

            foreach (WindowGlassSlot slot in fx.Windows)
            {
                string owner = slot.Renderer != null ? slot.Renderer.name : "(yok)";
                Color b = slot.BaseColor;
                MelonLogger.Msg($"[DIS-EFEKT]   pencere: '{owner}' slot {slot.MaterialIndex} '{slot.MaterialName}' " +
                                $"renk=({b.r:F2}, {b.g:F2}, {b.b:F2}) emisyon={slot.BaseEmissive:F2} " +
                                $"emisyonDokusu={(slot.EmissiveTexture != null ? "eklendi" : "materyalde var")}");
            }
            if (fx.Windows.Count == 0) MelonLogger.Msg("[DIS-EFEKT]   pencere cami slotu bulunamadi");

            for (int i = 0; i < fx.GroundLights.Count; i++)
            {
                Light light = fx.GroundLights[i];
                if (light == null) continue;

                Vector3 p = light.transform.position;
                MelonLogger.Msg($"[DIS-EFEKT]   zemin isigi {i}: konum=({p.x:F1}, {p.y:F1}, {p.z:F1}) agirlik={fx.GroundLightWeights[i]:F2}");
            }
            if (fx.GroundLights.Count == 0) MelonLogger.Msg("[DIS-EFEKT]   bu kabuk icin pencere konumu yok, zemin isigi eklenmedi");
        }
    }
}
