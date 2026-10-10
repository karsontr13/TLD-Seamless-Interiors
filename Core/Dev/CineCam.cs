using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SeamlessInteriors
{
    // ─── CINECAM: SHOWCASE VIDEO CAMERA ───
    //
    // NOT SHIPPED: compiled only with -p:SIDev=true. Driven from the UnityExplorer REPL, e.g.
    //   CineCam.Clear(); CineCam.Key(px,py,pz, yaw,pitch, 50); ... CineCam.Play(8);
    // Takes over the main camera after the player's eye has placed it and before the
    // portal postfixes read it, so window views follow the cinematic camera.
    public static class CineCam
    {
        public struct Shot
        {
            public Vector3 Pos;
            public float Yaw, Pitch, Fov;
        }

        public static readonly List<Shot> Keys = new List<Shot>();
        public static bool Active { get; private set; }
        public static bool Playing { get; private set; }
        public static float Duration = 8f;
        // 0 = constant speed, 1 = full ease in and out.
        public static float Ease = 1f;
        public static bool HideHud = true;
        public static bool HideArms = true;
        public static bool Loop;

        private static float s_Elapsed;
        private static Shot s_Hold;
        private static float[] s_KeyT;
        private static Camera s_WeaponCam;
        private static object s_SavedHudMode;
        private static Breath s_Breath;
        private static bool s_BreathWasSuppressed;
        private static Panel_Subtitles s_Subtitles;

        // ─── OFFLINE RECORDING: fixed-step frames to JPG, so a slow machine still gives smooth video ───
        public static bool Recording { get; private set; }
        public static string LastRecordDir;
        public static int RecordedFrames;
        private static int s_RecFps, s_RecTotal, s_RecWarmup, s_RecIndex, s_RecQuality;
        private static string s_RecDir;
        private static RenderTexture s_RecRT;
        private static Texture2D s_RecTex;
        private static Camera s_RecCam;

        private static string ShotDir =>
            Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "SICineCam");

        // ─── KEYS ───

        public static void Clear() { Keys.Clear(); }

        public static void Key(float x, float y, float z, float yaw, float pitch, float fov = 50f)
        {
            Keys.Add(new Shot { Pos = new Vector3(x, y, z), Yaw = yaw, Pitch = pitch, Fov = fov });
        }

        // Adds a key at the camera's current pose: the player's eye, or the held shot.
        public static string KeyHere(float fov = 0f)
        {
            Shot s = Current();
            if (fov > 0f) s.Fov = fov;
            Keys.Add(s);
            return Describe(s);
        }

        // A key placed at distance from a target and aimed at it (orbit and reveal shots).
        public static void KeyLookAt(float x, float y, float z, float tx, float ty, float tz, float fov = 50f)
        {
            Vector3 d = new Vector3(tx - x, ty - y, tz - z);
            Quaternion q = Quaternion.LookRotation(d.sqrMagnitude > 1e-6f ? d : Vector3.forward);
            Vector3 e = q.eulerAngles;
            Keys.Add(new Shot { Pos = new Vector3(x, y, z), Yaw = e.y, Pitch = WrapPitch(e.x), Fov = fov });
        }

        // Keys on an arc around a point: from/to angles in degrees, compass yaw convention.
        public static void Orbit(float cx, float cy, float cz, float radius, float height,
                                 float fromDeg, float toDeg, int steps = 6, float fov = 45f)
        {
            Clear();
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                float x = cx + Mathf.Sin(a) * radius, z = cz + Mathf.Cos(a) * radius;
                KeyLookAt(x, cy + height, z, cx, cy, cz, fov);
            }
        }

        // ─── PLAYBACK ───

        public static void Hold(float x, float y, float z, float yaw, float pitch, float fov = 50f)
        {
            s_Hold = new Shot { Pos = new Vector3(x, y, z), Yaw = yaw, Pitch = pitch, Fov = fov };
            Playing = false;
            Enter();
        }

        // Holds on key i, for framing a shot before it plays.
        public static void HoldKey(int i)
        {
            if (i < 0 || i >= Keys.Count) return;
            s_Hold = Keys[i];
            Playing = false;
            Enter();
        }

        public static string Play(float duration = 0f)
        {
            if (Keys.Count == 0) return "no keys";
            if (duration > 0f) Duration = duration;
            BuildTimes();
            s_Elapsed = 0f;
            s_Hold = Keys[0];
            Playing = true;
            Enter();
            return "playing " + Keys.Count + " keys over " + Duration + " s";
        }

        // Plays the keys once and writes every frame to UserData/SICineCam/frames/<name>/.
        // Warmup frames hold key 0 unsaved so exposure and TAA settle first.
        public static string Record(string name, float duration = 0f, int fps = 60, int width = 1920, int height = 1080,
                                    int quality = 92, int warmup = 45)
        {
            if (Keys.Count == 0) return "no keys";
            if (Recording) return "already recording";
            if (duration > 0f) Duration = duration;
            s_RecDir = Path.Combine(ShotDir, "frames", name);
            if (Directory.Exists(s_RecDir) && Directory.GetFiles(s_RecDir, "*.jpg").Length > 0)
                return "frames already exist: " + s_RecDir;
            Directory.CreateDirectory(s_RecDir);

            s_RecFps = fps;
            s_RecQuality = quality;
            s_RecWarmup = warmup;
            s_RecTotal = Mathf.CeilToInt(Duration * fps) + 1;
            s_RecIndex = -warmup;
            RecordedFrames = 0;
            s_RecRT = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            s_RecRT.Create();
            s_RecTex = new Texture2D(width, height, TextureFormat.RGB24, false);
            Time.captureFramerate = fps;

            BuildTimes();
            s_Elapsed = 0f;
            s_Hold = Keys[0];
            Playing = false;
            Recording = true;
            Enter();
            return "recording " + s_RecTotal + " frames (+" + warmup + " warmup) to " + s_RecDir;
        }

        public static string RecordStatus()
        {
            return Recording ? ("recording " + RecordedFrames + "/" + s_RecTotal) : ("idle, last " + RecordedFrames + " frames in " + LastRecordDir);
        }

        // Called before this frame's render: saves the previous frame, then poses this one.
        // TLD draws the world through CameraGlobalRT (main camera stays disabled), so capture its output: post effects, no UI.
        private static bool RecordStep(Camera mainCam, ref Shot s)
        {
            if (s_RecCam == null)
            {
                CameraGlobalRT global = UnityEngine.Object.FindObjectOfType<CameraGlobalRT>();
                s_RecCam = global != null ? global.GetComponent<Camera>() : mainCam;
            }
            Camera cam = s_RecCam;
            if (s_RecIndex > 0) SaveFrame(s_RecIndex - 1);
            if (s_RecIndex >= s_RecTotal)
            {
                EndRecording();
                return false;
            }

            float u = s_RecIndex <= 0 ? 0f : Mathf.Clamp01(s_RecIndex / (float)(s_RecTotal - 1));
            s = Sample(EaseT(u));
            cam.targetTexture = s_RecRT;
            s_RecIndex++;
            return true;
        }

        private static void SaveFrame(int index)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = s_RecRT;
            s_RecTex.ReadPixels(new Rect(0, 0, s_RecRT.width, s_RecRT.height), 0, 0, false);
            RenderTexture.active = prev;
            byte[] jpg = ImageConversion.EncodeToJPG(s_RecTex, s_RecQuality);
            string path = Path.Combine(s_RecDir, index.ToString("D5") + ".jpg");
            System.Threading.Tasks.Task.Run(() => File.WriteAllBytes(path, jpg));
            RecordedFrames++;
        }

        private static void EndRecording()
        {
            Recording = false;
            Time.captureFramerate = 0;
            if (s_RecCam != null) s_RecCam.targetTexture = null;
            s_RecCam = null;
            if (s_RecRT != null) { s_RecRT.Release(); UnityEngine.Object.Destroy(s_RecRT); }
            if (s_RecTex != null) UnityEngine.Object.Destroy(s_RecTex);
            s_RecRT = null;
            s_RecTex = null;
            LastRecordDir = s_RecDir;
            s_Hold = Keys[Keys.Count - 1];
            MelonLogger.Msg("[CINECAM] Recorded " + RecordedFrames + " frames to " + s_RecDir);
        }

        public static void Stop()
        {
            if (Recording) EndRecording();
            if (!Active) return;
            Active = false;
            Playing = false;
            if (s_WeaponCam != null) s_WeaponCam.enabled = true;
            s_WeaponCam = null;
            if (s_SavedHudMode != null) SetHudMode(s_SavedHudMode);
            s_SavedHudMode = null;
            if (s_Breath != null) s_Breath.m_SuppressEffects = s_BreathWasSuppressed;
            s_Breath = null;
            if (s_Subtitles != null) s_Subtitles.Enable(true);
            s_Subtitles = null;
        }

        private static void Enter()
        {
            if (Active) return;
            Active = true;
            if (HideArms)
            {
                s_WeaponCam = WeaponCamera();
                if (s_WeaponCam != null) s_WeaponCam.enabled = false;
            }
            if (HideHud) s_SavedHudMode = SetHudModeByName("Off", "None", "Hidden");
            try
            {
                s_Breath = UnityEngine.Object.FindObjectOfType<Breath>();
                if (s_Breath != null)
                {
                    s_BreathWasSuppressed = s_Breath.m_SuppressEffects;
                    s_Breath.m_SuppressEffects = true;
                    s_Breath.StopBreathEffectImmediate();
                }
                s_Subtitles = UnityEngine.Object.FindObjectOfType<Panel_Subtitles>();
                if (s_Subtitles != null)
                {
                    s_Subtitles.HideSubtitlesImmediate();
                    s_Subtitles.Enable(false);
                }
            }
            catch (Exception e) { MelonLogger.Warning("[CINECAM] Breath/subtitle hide failed: " + e.Message); }
        }

        private static int s_PoseFrame = -1;
        private static Shot s_Pose;

        // Called before SI's OnLateUpdate (portal renders) and after vp_FPSCamera.LateUpdate.
        // vp pulls the camera back to the player in FixedUpdate, so both need the pose; it advances once per frame.
        internal static void Apply()
        {
            if (!Active) return;
            Camera cam = GameManager.GetMainCamera();
            if (cam == null) return;

            if (s_PoseFrame != Time.frameCount)
            {
                s_PoseFrame = Time.frameCount;
                s_Pose = Advance(cam);
            }
            Shot p = s_Pose;
            cam.transform.SetPositionAndRotation(p.Pos, Quaternion.Euler(p.Pitch, p.Yaw, 0f));
            cam.fieldOfView = p.Fov;
            if (s_WeaponCam != null && s_WeaponCam.enabled) s_WeaponCam.enabled = false;
        }

        private static Shot Advance(Camera cam)
        {
            Shot s = s_Hold;
            if (Recording)
            {
                if (!RecordStep(cam, ref s)) s = s_Hold;
            }
            else if (Playing)
            {
                s_Elapsed += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(s_Elapsed / Mathf.Max(0.01f, Duration));
                s = Sample(EaseT(u));
                if (u >= 1f)
                {
                    if (Loop) s_Elapsed = 0f;
                    else { Playing = false; s_Hold = s; }
                }
            }
            return s;
        }

        private static float EaseT(float u)
        {
            float smooth = u * u * u * (u * (u * 6f - 15f) + 10f);
            return Mathf.Lerp(u, smooth, Ease);
        }

        // Key times by path length, so speed stays even across unequal gaps.
        private static void BuildTimes()
        {
            int n = Keys.Count;
            s_KeyT = new float[n];
            float total = 0f;
            for (int i = 1; i < n; i++)
            {
                float d = Vector3.Distance(Keys[i - 1].Pos, Keys[i].Pos);
                float a = Mathf.Abs(Mathf.DeltaAngle(Keys[i - 1].Yaw, Keys[i].Yaw)) * 0.05f;
                total += Mathf.Max(0.05f, d + a);
                s_KeyT[i] = total;
            }
            for (int i = 1; i < n; i++) s_KeyT[i] = total > 0f ? s_KeyT[i] / total : i / (float)(n - 1);
        }

        private static Shot Sample(float t)
        {
            int n = Keys.Count;
            if (n == 1) return Keys[0];
            int seg = 0;
            while (seg < n - 2 && t > s_KeyT[seg + 1]) seg++;
            float span = s_KeyT[seg + 1] - s_KeyT[seg];
            float f = span > 0f ? Mathf.Clamp01((t - s_KeyT[seg]) / span) : 1f;

            Shot p0 = Keys[Math.Max(0, seg - 1)], p1 = Keys[seg], p2 = Keys[seg + 1], p3 = Keys[Math.Min(n - 1, seg + 2)];
            // Unwrap yaw so the spline never spins the long way round.
            float y1 = p1.Yaw;
            float y0 = y1 + Mathf.DeltaAngle(y1, p0.Yaw);
            float y2 = y1 + Mathf.DeltaAngle(y1, p2.Yaw);
            float y3 = y2 + Mathf.DeltaAngle(p2.Yaw, p3.Yaw);

            return new Shot
            {
                Pos = new Vector3(CR(p0.Pos.x, p1.Pos.x, p2.Pos.x, p3.Pos.x, f),
                                  CR(p0.Pos.y, p1.Pos.y, p2.Pos.y, p3.Pos.y, f),
                                  CR(p0.Pos.z, p1.Pos.z, p2.Pos.z, p3.Pos.z, f)),
                Yaw = CR(y0, y1, y2, y3, f),
                Pitch = CR(p0.Pitch, p1.Pitch, p2.Pitch, p3.Pitch, f),
                Fov = CR(p0.Fov, p1.Fov, p2.Fov, p3.Fov, f),
            };
        }

        private static float CR(float a, float b, float c, float d, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3);
        }

        // ─── POSES ───

        public static Shot Current()
        {
            if (Active) return s_Hold;
            Camera cam = GameManager.GetMainCamera();
            if (cam == null) return default(Shot);
            Vector3 e = cam.transform.eulerAngles;
            return new Shot { Pos = cam.transform.position, Yaw = e.y, Pitch = WrapPitch(e.x), Fov = cam.fieldOfView };
        }

        public static string Where() { return Describe(Current()); }

        private static float WrapPitch(float x) { return x > 180f ? x - 360f : x; }

        private static string Describe(Shot s)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F2}f, {1:F2}f, {2:F2}f, {3:F1}f, {4:F1}f, {5:F0}f",
                s.Pos.x, s.Pos.y, s.Pos.z, s.Yaw, s.Pitch, s.Fov);
        }

        public static string List()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Keys.Count; i++) sb.Append(i).Append(": ").AppendLine(Describe(Keys[i]));
            return sb.ToString();
        }

        // ─── SAVED SHOTS: UserData/SICineCam/<name>.txt, one key per line ───

        public static string Save(string name)
        {
            Directory.CreateDirectory(ShotDir);
            var sb = new StringBuilder();
            sb.AppendLine("duration " + Duration.ToString(CultureInfo.InvariantCulture));
            foreach (Shot s in Keys) sb.AppendLine(Describe(s).Replace("f", ""));
            string path = Path.Combine(ShotDir, name + ".txt");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        public static string Load(string name)
        {
            string path = Path.Combine(ShotDir, name + ".txt");
            if (!File.Exists(path)) return "missing " + path;
            Clear();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("duration "))
                {
                    Duration = float.Parse(line.Substring(9), CultureInfo.InvariantCulture);
                    continue;
                }
                string[] p = line.Split(',');
                if (p.Length < 6) continue;
                Func<int, float> f = i => float.Parse(p[i].Trim(), CultureInfo.InvariantCulture);
                Key(f(0), f(1), f(2), f(3), f(4), f(5));
            }
            return Keys.Count + " keys, " + Duration + " s";
        }

        // ─── GAME HOOKS, resolved by reflection so a renamed member only disables that part ───

        private static Camera WeaponCamera()
        {
            try
            {
                var m = typeof(GameManager).GetMethod("GetWeaponCamera", Type.EmptyTypes);
                return m != null ? m.Invoke(null, null) as Camera : null;
            }
            catch { return null; }
        }

        private static System.Reflection.MethodInfo s_SetHud;
        private static System.Reflection.PropertyInfo s_GetHud;

        private static bool ResolveHud()
        {
            if (s_SetHud != null) return true;
            foreach (Type t in typeof(GameManager).Assembly.GetTypes())
            {
                var m = t.GetMethod("SetHUDDisplayMode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m == null || m.GetParameters().Length != 1) continue;
                s_SetHud = m;
                s_GetHud = t.GetProperty("HudDisplayMode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return true;
            }
            return false;
        }

        // Returns the mode in force before, to restore on Stop.
        private static object SetHudModeByName(params string[] names)
        {
            try
            {
                if (!ResolveHud()) return null;
                Type enumType = s_SetHud.GetParameters()[0].ParameterType;
                object before = s_GetHud != null ? s_GetHud.GetValue(null) : null;
                foreach (string n in names)
                {
                    if (!Enum.IsDefined(enumType, n)) continue;
                    s_SetHud.Invoke(null, new[] { Enum.Parse(enumType, n) });
                    return before;
                }
                MelonLogger.Warning("[CINECAM] HUD modes: " + string.Join(", ", Enum.GetNames(enumType)));
            }
            catch (Exception e) { MelonLogger.Warning("[CINECAM] HUD hide failed: " + e.Message); }
            return null;
        }

        private static void SetHudMode(object mode)
        {
            try { if (ResolveHud()) s_SetHud.Invoke(null, new[] { mode }); }
            catch (Exception e) { MelonLogger.Warning("[CINECAM] HUD restore failed: " + e.Message); }
        }
    }

    // SI renders the window portals in OnLateUpdate; give them the cinematic pose first.
    [HarmonyLib.HarmonyPatch(typeof(SeamlessInteriorsMod), nameof(SeamlessInteriorsMod.OnLateUpdate))]
    internal static class CineCamBeforePortals
    {
        private static bool s_Failed;

        private static void Prefix()
        {
            if (s_Failed) return;
            try { CineCam.Apply(); }
            catch (Exception e)
            {
                s_Failed = true;
                MelonLogger.Error("[CINECAM] Pre-portal pose threw and stopped for this session: " + e);
            }
        }
    }

    // SI moves the snow box out of a parked room only if the camera matches the snow's eye; pose first.
    [HarmonyLib.HarmonyPatch]
    internal static class CineCamBeforeSnowJobs
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var t = typeof(Il2CppTLD.WeatherParticle.WeatherParticleManager);
            yield return HarmonyLib.AccessTools.Method(t, "ScheduleFallingSnowParticleJobs");
            yield return HarmonyLib.AccessTools.Method(t, "ScheduleBlowingSnowParticleJobs");
        }

        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.First)]
        private static void Prefix()
        {
            try { CineCam.Apply(); }
            catch (Exception e) { MelonLogger.Error("[CINECAM] Pre-snow pose threw: " + e.Message); }
        }
    }

    // ParkedCullEye (the inside-out window eye) reads the camera in OnUpdate.
    [HarmonyLib.HarmonyPatch(typeof(SeamlessInteriorsMod), nameof(SeamlessInteriorsMod.OnUpdate))]
    internal static class CineCamBeforeUpdate
    {
        private static void Prefix()
        {
            try { CineCam.Apply(); }
            catch (Exception e) { MelonLogger.Error("[CINECAM] Pre-update pose threw: " + e.Message); }
        }
    }

    // Runs before PortalFrustumCapturePatch so portal and wall cameras see the cinematic pose.
    [HarmonyLib.HarmonyPatch(typeof(vp_FPSCamera), nameof(vp_FPSCamera.LateUpdate))]
    [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.First)]
    internal static class CineCamTick
    {
        private static bool s_Failed;

        private static void Postfix()
        {
            if (s_Failed) return;
            try { CineCam.Apply(); }
            catch (Exception e)
            {
                s_Failed = true;
                MelonLogger.Error("[CINECAM] Threw and stopped for this session: " + e);
            }
        }
    }
}
