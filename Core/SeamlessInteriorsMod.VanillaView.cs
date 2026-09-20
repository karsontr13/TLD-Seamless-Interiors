using System;
using System.Collections;
using System.Reflection;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── VANILLA VIEW ───
        // The player's building is a real game indoor space, entered and left at the doors like any other.

        private const string VANILLA_SPACE_OBJECT = "SI_IndoorSpace";

        private static IndoorSpaceTrigger s_EnteredSpace;
        private static string s_EnteredSpaceId;
        private static bool s_ReportedFirstSpace;
        private static bool s_WarnedSpaceRefused;

        // A collider-less trigger: physics never enters it, only SyncVanillaIndoorSpace does.
        // The values give exactly what the game already did for a clone: interior temperature and lighting.
        internal static void CreateVanillaIndoorSpace(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;
            if (instance.MasterInterior.transform.Find(VANILLA_SPACE_OBJECT) != null) return;

            var go = new GameObject(VANILLA_SPACE_OBJECT);
            go.transform.SetParent(instance.MasterInterior.transform, false);

            // Mods name a space after its PDID: the interior scene's name, as they would see it in the vanilla game.
            // PDID reads a runtime cache the game's PDID table fills, so it is set here without registering.
            string spaceName = string.IsNullOrEmpty(instance.Config.InteriorSceneBaseName)
                ? instance.Config.ResolvedInstanceId
                : instance.Config.InteriorSceneBaseName;
            var guid = go.AddComponent<ObjectGuid>();
            guid.m_Guid = spaceName;
            guid.SetRuntimeCachedPdid(spaceName);

            var space = go.AddComponent<IndoorSpaceTrigger>();
            space.m_DontCountAsInterior = false;
            space.m_UseOutdoorTemperature = false;
            space.m_TemperatureDeltaCelsius = 0f;
            space.m_UseOutdoorLighting = false;
            space.m_AllowCampfires = false;
            space.m_AllowDropTravois = false;
            space.m_CantSurvey = true;
            space.m_ValidSafehouse = false;
            space.m_TriggerID = "SI_" + instance.Config.ResolvedInstanceId;
        }

        // Brings the game's indoor space in line with PlayerInteriorId; returns at once when nothing changed.
        internal static void SyncVanillaIndoorSpace()
        {
            string wanted = PlayerInteriorId;
            bool enteredAlive = s_EnteredSpace != null;
            if (wanted == s_EnteredSpaceId && (wanted == null || enteredAlive)) return;

            if (s_EnteredSpaceId != null)
            {
                // Mods see the region again from the moment the player leaves.
                s_ViewInteriorScene = null;
                s_ViewRegionScene = null;

                if (enteredAlive)
                {
                    Collider leaving = GetPlayerCollider();
                    if (leaving != null)
                    {
                        s_SyncingSpace = true;
                        try { s_EnteredSpace.OnTriggerExit(leaving); }
                        finally { s_SyncingSpace = false; }
                    }
                }

                ForgetSpaceOnPlayer(s_EnteredSpace);
                if (s_DebugBounds) MelonLogger.Msg($"[VANILLA-ALAN] {s_EnteredSpaceId}: ic mekan alanindan cikildi.");
                s_EnteredSpace = null;
                s_EnteredSpaceId = null;
            }

            if (wanted == null) return;

            SeamlessInteriorInstance instance;
            if (!ActiveInteriors.TryGetValue(wanted, out instance) || instance == null || instance.MasterInterior == null) return;

            // Not built yet during a load; OnUpdate asks again next frame.
            Transform spaceT = instance.MasterInterior.transform.Find(VANILLA_SPACE_OBJECT);
            IndoorSpaceTrigger space = spaceT != null ? spaceT.GetComponent<IndoorSpaceTrigger>() : null;
            Collider player = GetPlayerCollider();
            if (space == null || player == null) return;

            s_EnteredSpace = space;
            s_EnteredSpaceId = wanted;
            SetSceneView(instance);

            s_SyncingSpace = true;
            try { space.OnTriggerEnter(player); }
            finally { s_SyncingSpace = false; }
            MakeSureSpaceTaken(space, wanted);
        }

        // ─── INDOOR READOUT ───
        // The temperatures the game uses indoors, to check what a temperature mod adds to the base.

        private const float READOUT_INTERVAL_HOURS = 0.25f;
        private const int FOREIGN_TRIGGER_LOG_LIMIT = 20;
        private static float s_NextReadoutHours = -1f;
        private static int s_ForeignTriggerLogs;
        private static bool s_SyncingSpace;

        // A log line plus a short screen text; null while the game is not ready.
        internal static string DescribeIndoorState(out string screenText)
        {
            screenText = null;
            Weather weather = GameManager.GetWeatherComponent();
            TimeOfDay tod = GameManager.GetTimeOfDayComponent();
            ExperienceModeManager emm = GameManager.GetExperienceModeManagerComponent();
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (weather == null || tod == null || emm == null || pm == null) return null;

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float baseTemp = weather.m_BaseTemperature - emm.GetOutdoorTempDropCelcius(tod.GetDayNumber());
            float indoor = weather.m_IndoorTemperatureCelsius;
            string extra = (indoor - baseTemp).ToString("+0.0;-0.0;0.0", inv);
            string withoutFires = weather.m_CurrentTemperatureWithoutHeatSources.ToString("F1", inv);
            string felt = weather.GetCurrentTemperature().ToString("F1", inv);
            bool indoorEnvironment = weather.IsIndoorEnvironment();
            string modScene = "icSahne=" + weather.IsIndoorScene() + " sahne=" + (GameManager.m_ActiveScene ?? "?")
                              + " aktifSahne=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            // Fires as mods see them, then the full list.
            var realFires = RealFires();
            modScene += " ates=" + (FireManager.m_Fires?.Count ?? -1) + "/" + (realFires?.Count ?? -1);

            IndoorSpaceTrigger current = pm.m_IndoorSpaceTrigger;
            string space = current != null ? current.gameObject.name : "yok";
            ObjectGuid spaceGuid = current != null ? current.GetComponent<ObjectGuid>() : null;
            if (spaceGuid != null) space += "[" + (spaceGuid.PDID ?? "pdid yok") + "]";
            int spaces = pm.m_IndoorSpaceTriggers != null ? pm.m_IndoorSpaceTriggers.Count : 0;

            screenText = $"taban {baseTemp.ToString("F1", inv)} C | ic deger {indoor.ToString("F1", inv)} C (taban {extra})\n" +
                         $"ates haric {withoutFires} C | anlik {felt} C\n" +
                         $"alan {space} | icOrtam {indoorEnvironment} | {modScene}";
            return $"bina={PlayerInteriorId ?? "disarida"} alan={space}({spaces}) icOrtam={indoorEnvironment} {modScene} | " +
                   $"taban={baseTemp.ToString("F1", inv)} ic deger={indoor.ToString("F1", inv)} (tabana ek {extra}) " +
                   $"ates haric={withoutFires} anlik={felt} | " +
                   $"kar={weather.m_CurrentBlizzardDegreesDrop.ToString("F1", inv)} ruzgar={weather.m_CurrentWindChill.ToString("F1", inv)}";
        }

        // With verbose logging on, a line every 15 game minutes inside a building, so a time skip leaves a curve.
        internal static void TickIndoorReadout()
        {
            if (!s_DebugBounds || s_EnteredSpaceId == null)
            {
                s_NextReadoutHours = -1f;
                return;
            }

            TimeOfDay tod = GameManager.GetTimeOfDayComponent();
            if (tod == null) return;

            float now = tod.GetHoursPlayedNotPaused();
            if (s_NextReadoutHours >= 0f && now < s_NextReadoutHours) return;
            s_NextReadoutHours = now + READOUT_INTERVAL_HOURS;

            string screenText;
            string line = DescribeIndoorState(out screenText);
            if (line != null) MelonLogger.Msg("[ISI] " + line);
        }

        // Trigger events SI did not send while the player is in one of its buildings.
        // A mod that ignores the collider takes them for the player's own.
        internal static void NoteForeignTriggerEvent(IndoorSpaceTrigger trigger, Collider other, bool entering)
        {
            if (s_SyncingSpace || s_EnteredSpaceId == null || s_ForeignTriggerLogs >= FOREIGN_TRIGGER_LOG_LIMIT) return;
            s_ForeignTriggerLogs++;

            string triggerName = trigger != null ? trigger.gameObject.name : "?";
            string otherName = other != null ? other.gameObject.name : "?";
            string otherTag = other != null ? other.gameObject.tag : "?";
            string last = s_ForeignTriggerLogs == FOREIGN_TRIGGER_LOG_LIMIT ? "; sonrakiler yazilmayacak" : "";
            MelonLogger.Msg($"[VANILLA-ALAN] {s_EnteredSpaceId} icindeyken baska tetikleyici {(entering ? "girisi" : "cikisi")}: " +
                            $"'{triggerName}' <- '{otherName}' (etiket {otherTag}){last}");
        }

        private static Collider GetPlayerCollider()
        {
            GameObject player = GameManager.GetPlayerObject();
            return player != null ? player.GetComponent<Collider>() : null;
        }

        // The game only accepts its player tag; if it refused, the state is set by hand so indoor rules still apply.
        private static void MakeSureSpaceTaken(IndoorSpaceTrigger space, string id)
        {
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null) return;

            if (IsSameObject(pm.m_IndoorSpaceTrigger, space))
            {
                if (!s_ReportedFirstSpace)
                {
                    s_ReportedFirstSpace = true;
                    MelonLogger.Msg($"[VANILLA-ALAN] {id}: oyuncu oyunun kendi ic mekan alanina girdi.");
                }
                else if (s_DebugBounds)
                {
                    MelonLogger.Msg($"[VANILLA-ALAN] {id}: ic mekan alanina girildi.");
                }
                return;
            }

            if (!s_WarnedSpaceRefused)
            {
                s_WarnedSpaceRefused = true;
                GameObject player = GameManager.GetPlayerObject();
                MelonLogger.Warning($"[VANILLA-ALAN] {id}: oyun alani kabul etmedi (oyuncu etiketi '{(player != null ? player.tag : "?")}'), elle ayarlandi.");
            }

            var list = pm.m_IndoorSpaceTriggers;
            if (list != null && !list.Contains(space)) list.Add(space);
            pm.m_IndoorSpaceTrigger = space;
        }

        // Leaves no reference to this space, or to any destroyed one, on the player.
        private static void ForgetSpaceOnPlayer(IndoorSpaceTrigger space)
        {
            PlayerManager pm = GameManager.GetPlayerManagerComponent();
            if (pm == null) return;

            var list = pm.m_IndoorSpaceTriggers;
            if (list != null)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    IndoorSpaceTrigger entry = list[i];
                    if (entry == null || IsSameObject(entry, space)) list.RemoveAt(i);
                }
            }

            IndoorSpaceTrigger current = pm.m_IndoorSpaceTrigger;
            if (current == null || IsSameObject(current, space))
                pm.m_IndoorSpaceTrigger = list != null && list.Count > 0 ? list[list.Count - 1] : null;
        }

        // Pointer identity: still true for an object Unity has already destroyed.
        private static bool IsSameObject(Il2CppObjectBase a, Il2CppObjectBase b)
        {
            return (object)a != null && (object)b != null && a.Pointer == b.Pointer;
        }

        // ─── SCENE EVENT SHIELD ───
        // Interior scenes loaded only to be copied never reach other mods' scene callbacks.
        // MelonLoader drops Harmony patches on itself, so each subscriber is wrapped in its event list.

        private const BindingFlags SHIELD_FIELD_FLAGS = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo s_EventActions =
            typeof(MelonEventBase<LemonAction<int, string>>).GetField("actions", SHIELD_FIELD_FLAGS);
        private static readonly FieldInfo s_EventSnapshot =
            typeof(MelonEventBase<LemonAction<int, string>>).GetField("cachedActionsArray", SHIELD_FIELD_FLAGS);
        // Reached by reflection: its MethodInfo overload cannot be bound from a net472 project.
        private static readonly MethodInfo s_EventUnsubscribe =
            typeof(MelonEventBase<LemonAction<int, string>>).GetMethod("Unsubscribe", new[] { typeof(LemonAction<int, string>) });
        private static FieldInfo s_ActionDelegate;
        private static FieldInfo s_ActionOneShot;

        private static SeamlessInteriorsMod s_Instance;
        private static readonly object[] s_ShieldedSnapshots = new object[3];
        private static bool s_ShieldFailed;
        private static bool s_ReportedShield;
        private static string s_LastShieldedEvent;
        private static string s_TemplateCheckScene;
        private static int s_TemplateCheckFrame = -1;
        private static bool s_TemplateCheckAnswer;

        // Exceptions pass through, so MelonLoader still reports them under the mod they came from.
        private sealed class SceneEventGate
        {
            private readonly LemonAction<int, string> m_Inner;
            private readonly string m_EventName;
            private readonly object m_OneShotEvent;

            internal SceneEventGate(LemonAction<int, string> inner, string eventName, object oneShotEvent)
            {
                m_Inner = inner;
                m_EventName = eventName;
                m_OneShotEvent = oneShotEvent;
            }

            internal void Forward(int buildIndex, string sceneName)
            {
                if (IsShieldedTemplate(sceneName, m_EventName)) return;

                // While a mod handles a scene event it sees the real scene, not the building's interior.
                s_DispatchingSceneEvents++;
                try
                {
                    if (m_OneShotEvent == null)
                    {
                        m_Inner(buildIndex, sceneName);
                        return;
                    }

                    // A one-shot subscriber leaves only after a scene it was allowed to see.
                    try { m_Inner(buildIndex, sceneName); }
                    finally { s_EventUnsubscribe.Invoke(m_OneShotEvent, new object[] { new LemonAction<int, string>(Forward) }); }
                }
                finally
                {
                    s_DispatchingSceneEvents--;
                }
            }
        }

        internal void InstallSceneEventShield()
        {
            s_Instance = this;
            int wrapped = RefreshSceneEventShield();
            if (!s_ShieldFailed)
                MelonLogger.Msg($"[SAHNE-KALKANI] Kuruldu: {wrapped} sahne olayi aboneligi sablon sahnelerden korunuyor.");
        }

        // Also catches subscribers added after start-up; only a reference compare when nothing changed.
        internal static int RefreshSceneEventShield()
        {
            if (s_ShieldFailed || s_Instance == null) return 0;
            try
            {
                return WrapSceneSubscribers(MelonEvents.OnSceneWasLoaded, 0, "yuklendi")
                     + WrapSceneSubscribers(MelonEvents.OnSceneWasInitialized, 1, "hazir")
                     + WrapSceneSubscribers(MelonEvents.OnSceneWasUnloaded, 2, "kaldirildi");
            }
            catch (Exception ex)
            {
                s_ShieldFailed = true;
                MelonLogger.Warning("[SAHNE-KALKANI] Kurulamadi, sablon sahneler diger modlara duyurulacak: " + ex.Message);
                return 0;
            }
        }

        // The entry keeps its priority, order and owner; only the delegate it calls changes.
        private static int WrapSceneSubscribers(MelonEvent<int, string> sceneEvent, int slot, string eventName)
        {
            if (s_EventActions == null || s_EventSnapshot == null)
                throw new MissingFieldException("MelonEventBase", "actions");

            object snapshot = s_EventSnapshot.GetValue(sceneEvent);
            if (ReferenceEquals(snapshot, s_ShieldedSnapshots[slot])) return 0;

            var actions = (IList)s_EventActions.GetValue(sceneEvent);
            int wrapped = 0;
            lock (actions)
            {
                foreach (object action in actions)
                {
                    if (action == null) continue;
                    if (s_ActionDelegate == null)
                    {
                        s_ActionDelegate = action.GetType().GetField("del", SHIELD_FIELD_FLAGS)
                                           ?? throw new MissingFieldException("MelonAction", "del");
                        s_ActionOneShot = action.GetType().GetField("unsubscribeOnFirstInvocation", SHIELD_FIELD_FLAGS);
                    }

                    // Only callbacks bound to a mod instance: a free subscription stays removable by its owner.
                    var inner = s_ActionDelegate.GetValue(action) as LemonAction<int, string>;
                    if (inner == null || !(inner.Target is MelonBase) || IsOwnSceneSubscriber(inner)) continue;

                    // MelonLoader drops a one-shot entry even after a hidden call, so the gate does it instead.
                    bool oneShot = s_ActionOneShot != null && s_EventUnsubscribe != null && (bool)s_ActionOneShot.GetValue(action);
                    var gate = new SceneEventGate(inner, eventName, oneShot ? sceneEvent : null);
                    s_ActionDelegate.SetValue(action, new LemonAction<int, string>(gate.Forward));
                    if (oneShot) s_ActionOneShot.SetValue(action, false);
                    wrapped++;
                }
                s_ShieldedSnapshots[slot] = s_EventSnapshot.GetValue(sceneEvent);
            }
            return wrapped;
        }

        // This mod loads the templates, so it keeps hearing about them.
        private static bool IsOwnSceneSubscriber(Delegate subscriber)
        {
            if (ReferenceEquals(subscriber.Target, s_Instance)) return true;
            Type owner = subscriber.Method != null ? subscriber.Method.DeclaringType : null;
            return owner != null && owner.Assembly == typeof(SeamlessInteriorsMod).Assembly;
        }

        private static bool IsShieldedTemplate(string sceneName, string eventName)
        {
            if (!IsTemplateSceneCached(sceneName)) return false;

            // One line per scene event, however many mods listen to it.
            string key = eventName + ":" + sceneName;
            if (key == s_LastShieldedEvent) return true;
            s_LastShieldedEvent = key;

            if (!s_ReportedShield)
            {
                s_ReportedShield = true;
                MelonLogger.Msg($"[SAHNE-KALKANI] '{sceneName}' sablon sahnesi diger modlara duyurulmadi ({eventName}); sonrakiler sessiz.");
            }
            else if (s_DebugBounds)
            {
                MelonLogger.Msg($"[SAHNE-KALKANI] '{sceneName}' {eventName}: yalnizca SI'ya iletildi.");
            }
            return true;
        }

        // Every listening mod asks in the same frame; the answer is worked out once.
        private static bool IsTemplateSceneCached(string sceneName)
        {
            int frame = Time.frameCount;
            if (frame == s_TemplateCheckFrame && sceneName == s_TemplateCheckScene) return s_TemplateCheckAnswer;

            bool answer;
            try { answer = IsInteriorLoadedAsCloneTemplate(sceneName); }
            catch { answer = false; }

            s_TemplateCheckFrame = frame;
            s_TemplateCheckScene = sceneName;
            s_TemplateCheckAnswer = answer;
            return answer;
        }

        // ─── SCENE VIEW ───
        // Inside a building, mod code asking for the scene gets the interior, as in the vanilla game.
        // Hooks sit on the managed wrappers only, so the game and MelonLoader keep reading the region.

        private static bool s_SceneViewInstalled;
        private static bool s_ReportedSceneView;
        private static bool s_BypassSceneView;
        private static int s_DispatchingSceneEvents;
        private static string s_ViewInteriorScene;
        private static string s_ViewRegionScene;
        private static int s_RealSceneFrame = -1;
        private static bool s_RealSceneIsRegion;
        private static object s_IndoorSceneHook;
        private static object s_ActiveSceneHook;
        private static object s_SceneNameHook;
        private static int s_InUnitySceneCallbacks;

        // Also installs the world view and the save name guard, which use the same MonoMod hooks.
        internal static void InstallSceneView()
        {
            ConstructorInfo hookCtor = GetHookConstructor();
            if (hookCtor == null)
            {
                MelonLogger.Warning("[SAHNE-GORUNUMU] Kurulamadi: MonoMod bulunamadi.");
                return;
            }

            try
            {
                MethodInfo indoorScene = typeof(Weather).GetMethod("IsIndoorScene", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                MethodInfo activeScene = PropertyAccessor(typeof(GameManager), "m_ActiveScene", BindingFlags.Static, false);
                if (indoorScene == null || activeScene == null) throw new MissingMethodException("Weather.IsIndoorScene ya da GameManager.m_ActiveScene bulunamadi.");

                s_IndoorSceneHook = HookWith(hookCtor, indoorScene, nameof(IsIndoorSceneView));
                s_ActiveSceneHook = HookWith(hookCtor, activeScene, nameof(ActiveSceneView));
                s_SceneViewInstalled = true;

                string sceneNameNote = "";
                try
                {
                    MethodInfo sceneName = PropertyAccessor(typeof(UnityEngine.SceneManagement.Scene), "name", BindingFlags.Instance, false);
                    s_SceneNameHook = HookWith(hookCtor, sceneName, nameof(SceneNameView));
                }
                catch (Exception ex)
                {
                    sceneNameNote = " Scene.name kancasi kurulamadi: " + (ex.InnerException ?? ex).Message;
                }

                string covered = s_SceneNameHook != null ? "IsIndoorScene, m_ActiveScene ve Scene.name" : "IsIndoorScene ve m_ActiveScene";
                MelonLogger.Msg($"[SAHNE-GORUNUMU] Kuruldu: bina icinde modlar {covered} icin ic sahneyi goruyor.{sceneNameNote}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[SAHNE-GORUNUMU] Kurulamadi: " + (ex.InnerException ?? ex).Message);
            }

            InstallWorldView(hookCtor);
            InstallSaveNameGuard(hookCtor);
        }

        // MonoMod is reached by reflection: its constructors cannot be bound from a net472 project.
        private static ConstructorInfo GetHookConstructor()
        {
            Type hookType = Type.GetType("MonoMod.RuntimeDetour.Hook, MonoMod.RuntimeDetour");
            return hookType != null ? hookType.GetConstructor(new[] { typeof(MethodBase), typeof(MethodInfo) }) : null;
        }

        // Points managed calls of target at one of this class's methods; throws when either is missing.
        private static object HookWith(ConstructorInfo hookCtor, MethodBase target, string replacement)
        {
            MethodInfo method = typeof(SeamlessInteriorsMod).GetMethod(replacement, BindingFlags.NonPublic | BindingFlags.Static);
            if (target == null || method == null) throw new MissingMethodException(replacement + " icin hedef bulunamadi.");
            return hookCtor.Invoke(new object[] { target, method });
        }

        private static MethodInfo PropertyAccessor(Type type, string name, BindingFlags scope, bool setter)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | scope);
            if (property == null) return null;
            return setter ? property.GetSetMethod() : property.GetGetMethod();
        }

        private static void SetSceneView(SeamlessInteriorInstance instance)
        {
            string interior = instance.Config.InteriorSceneBaseName;
            s_ViewInteriorScene = string.IsNullOrEmpty(interior) ? null : interior;
            s_ViewRegionScene = instance.Config.ExteriorSceneName;
            s_RealSceneFrame = -1;

            if (!s_SceneViewInstalled || s_ViewInteriorScene == null) return;
            if (!s_ReportedSceneView)
            {
                s_ReportedSceneView = true;
                MelonLogger.Msg($"[SAHNE-GORUNUMU] {instance.Config.ResolvedInstanceId}: modlar ic sahne '{s_ViewInteriorScene}' goruyor.");
            }
            else if (s_DebugBounds)
            {
                MelonLogger.Msg($"[SAHNE-GORUNUMU] {instance.Config.ResolvedInstanceId}: ic sahne '{s_ViewInteriorScene}'.");
            }
        }

        // MonoMod's hook shape: the original as a delegate, then the method's own arguments.
        // While SI runs a component for another place (see SeamlessInteriorsMod.PlaceContext.cs), that place answers.
        private static bool IsIndoorSceneView(Func<Weather, bool> orig, Weather self)
        {
            if (s_WeatherRegionView > 0) return orig(self);
            if (InPlaceTick) return s_TickPlace.Indoor;
            return orig(self) || IsSceneViewActive();
        }

        private static string ActiveSceneView(Func<string> orig)
        {
            string real = orig();
            if (s_WeatherRegionView > 0) return real;
            if (InPlaceTick) return real != null && real == s_ContextRegion ? s_TickPlace.Scene : real;
            return real != null && real == s_ViewRegionScene && IsSceneViewActive() ? s_ViewInteriorScene : real;
        }

        private delegate string SceneNameOrig(ref UnityEngine.SceneManagement.Scene self);

        // The region's scene reads as the interior while the view is on; other scenes keep their names.
        private static string SceneNameView(SceneNameOrig orig, ref UnityEngine.SceneManagement.Scene self)
        {
            string real = orig(ref self);
            if (s_WeatherRegionView > 0) return real;
            if (InPlaceTick) return real != null && real == s_ContextRegion ? s_TickPlace.Scene : real;
            return real != null && real == s_ViewRegionScene && IsSceneViewActive() ? s_ViewInteriorScene : real;
        }

        // SI's own code reads real scene names through this, never the interior the mods are shown.
        internal static string RealSceneName(UnityEngine.SceneManagement.Scene scene)
        {
            bool previous = s_BypassSceneView;
            s_BypassSceneView = true;
            try { return scene.name; }
            finally { s_BypassSceneView = previous; }
        }

        internal static void EnterUnitySceneCallback()
        {
            s_InUnitySceneCallbacks++;
            s_RealSceneFrame = -1;
        }

        internal static void ExitUnitySceneCallback()
        {
            if (s_InUnitySceneCallbacks > 0) s_InUnitySceneCallbacks--;
            s_RealSceneFrame = -1;
        }

        // Off during scene events and Unity's scene callbacks, and whenever the region is not the active scene.
        private static bool IsSceneViewActive()
        {
            if (s_ViewInteriorScene == null || s_DispatchingSceneEvents > 0 || s_InUnitySceneCallbacks > 0 || s_BypassSceneView) return false;

            int frame = Time.frameCount;
            if (frame != s_RealSceneFrame)
            {
                s_RealSceneFrame = frame;
                s_BypassSceneView = true;
                try { s_RealSceneIsRegion = s_EnteredSpace != null && GameManager.m_ActiveScene == s_ViewRegionScene; }
                catch { s_RealSceneIsRegion = false; }
                finally { s_BypassSceneView = false; }
            }
            return s_RealSceneIsRegion;
        }

        // SI's own reads of the real game state, past every view.
        private static T ReadReal<T>(Func<T> read)
        {
            bool previous = s_BypassSceneView;
            s_BypassSceneView = true;
            try { return read(); }
            finally { s_BypassSceneView = previous; }
        }

        // ─── WORLD VIEW ───
        // Mod code gets FireManager's lists as in the vanilla game: inside a building only that building's fires,
        // outdoors none of the buildings'. The game and SI keep the full lists.

        private static object s_FiresHook;
        private static object s_WoodStovesHook;
        private static object s_CampfiresHook;
        private static bool s_WorldViewFailed;
        private static readonly WorldList<Fire> s_FiresView = new WorldList<Fire>();
        private static readonly WorldList<WoodStove> s_WoodStovesView = new WorldList<WoodStove>();
        private static readonly WorldList<Campfire> s_CampfiresView = new WorldList<Campfire>();
        private static readonly System.Collections.Generic.Dictionary<int, string> s_BuildingRoots = new System.Collections.Generic.Dictionary<int, string>();
        private static int s_BuildingRootsFrame = -1;

        private static void InstallWorldView(ConstructorInfo hookCtor)
        {
            try
            {
                s_FiresHook = HookWith(hookCtor, PropertyAccessor(typeof(FireManager), "m_Fires", BindingFlags.Static, false), nameof(FiresView));
                s_WoodStovesHook = HookWith(hookCtor, PropertyAccessor(typeof(FireManager), "m_WoodStoves", BindingFlags.Static, false), nameof(WoodStovesView));
                s_CampfiresHook = HookWith(hookCtor, PropertyAccessor(typeof(FireManager), "m_Campfires", BindingFlags.Static, false), nameof(CampfiresView));
                MelonLogger.Msg("[DUNYA-GORUNUMU] Kuruldu: modlar FireManager listelerinde yalnizca bulunduklari yerin ateslerini goruyor.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[DUNYA-GORUNUMU] Kurulamadi: " + (ex.InnerException ?? ex).Message);
            }
        }

        private static Il2CppSystem.Collections.Generic.List<Fire> FiresView(Func<Il2CppSystem.Collections.Generic.List<Fire>> orig)
        {
            return s_FiresView.Filter(orig());
        }

        private static Il2CppSystem.Collections.Generic.List<WoodStove> WoodStovesView(Func<Il2CppSystem.Collections.Generic.List<WoodStove>> orig)
        {
            return s_WoodStovesView.Filter(orig());
        }

        private static Il2CppSystem.Collections.Generic.List<Campfire> CampfiresView(Func<Il2CppSystem.Collections.Generic.List<Campfire>> orig)
        {
            return s_CampfiresView.Filter(orig());
        }

        internal static Il2CppSystem.Collections.Generic.List<Fire> RealFires() { return ReadReal(() => FireManager.m_Fires); }
        internal static Il2CppSystem.Collections.Generic.List<WoodStove> RealWoodStoves() { return ReadReal(() => FireManager.m_WoodStoves); }
        internal static Il2CppSystem.Collections.Generic.List<Campfire> RealCampfires() { return ReadReal(() => FireManager.m_Campfires); }

        // The building whose clone holds this transform, null outdoors. Building roots are listed once a frame.
        private static string BuildingOf(Transform t)
        {
            int frame = Time.frameCount;
            if (frame != s_BuildingRootsFrame)
            {
                s_BuildingRootsFrame = frame;
                s_BuildingRoots.Clear();
                foreach (var pair in ActiveInteriors)
                    if (pair.Value != null && pair.Value.MasterInterior != null)
                        s_BuildingRoots[pair.Value.MasterInterior.GetInstanceID()] = pair.Key;
            }

            string id;
            return s_BuildingRoots.TryGetValue(t.root.gameObject.GetInstanceID(), out id) ? id : null;
        }

        // One filtered copy per list, refilled at most once a frame; mods always get the same object.
        private sealed class WorldList<T> where T : Component
        {
            private Il2CppSystem.Collections.Generic.List<T> m_View;
            private int m_Frame = -1;
            private string m_Building;

            internal Il2CppSystem.Collections.Generic.List<T> Filter(Il2CppSystem.Collections.Generic.List<T> real)
            {
                if (real == null || s_BypassSceneView || s_WorldViewFailed || ActiveInteriors.Count == 0) return real;

                string building = ViewBuilding();
                int frame = Time.frameCount;
                if (m_View != null && m_Frame == frame && m_Building == building) return m_View;

                try
                {
                    if (m_View == null) m_View = new Il2CppSystem.Collections.Generic.List<T>();
                    m_View.Clear();
                    int count = real.Count;
                    for (int i = 0; i < count; i++)
                    {
                        T item = real[i];
                        Component component = item;
                        if (component != null && BuildingOf(component.transform) == building) m_View.Add(item);
                    }
                    m_Frame = frame;
                    m_Building = building;
                    return m_View;
                }
                catch (Exception ex)
                {
                    s_WorldViewFailed = true;
                    MelonLogger.Warning("[DUNYA-GORUNUMU] Kapatildi, modlar tam listeyi goruyor: " + ex.Message);
                    return real;
                }
            }
        }

        // ─── SAVE NAME GUARD ───
        // A mod handing a building's scene name back to the game as the loaded scene would save the region
        // under the interior's name. The guard writes the loaded region's name instead.

        private const int SAVE_NAME_LOG_LIMIT = 20;
        private static object s_SaveGameHook;
        private static object s_LoadSceneHook;
        private static object s_SaveFilenameHook;
        private static int s_SaveNameLogs;

        private static void InstallSaveNameGuard(ConstructorInfo hookCtor)
        {
            try
            {
                Type[] twoStrings = { typeof(string), typeof(string) };
                MethodInfo saveGame = typeof(SaveGameSystem).GetMethod("SaveGame", BindingFlags.Public | BindingFlags.Static, null, twoStrings, null);
                MethodInfo loadScene = typeof(GameManager).GetMethod("LoadScene", BindingFlags.Public | BindingFlags.Static, null, twoStrings, null);
                MethodInfo setFilename = PropertyAccessor(typeof(SceneTransitionData), "m_SceneSaveFilenameCurrent", BindingFlags.Instance, true);

                s_SaveGameHook = HookWith(hookCtor, saveGame, nameof(SaveGameGuard));
                s_LoadSceneHook = HookWith(hookCtor, loadScene, nameof(LoadSceneGuard));
                s_SaveFilenameHook = HookWith(hookCtor, setFilename, nameof(SaveFilenameGuard));
                MelonLogger.Msg("[KAYIT-ADI] Kuruldu: modlarin kayda verdigi ic sahne adlari yuklu bolgenin adina cevriliyor.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[KAYIT-ADI] Kurulamadi: " + (ex.InnerException ?? ex).Message);
            }
        }

        private static void SaveGameGuard(Action<string, string> orig, string name, string sceneSaveName)
        {
            orig(name, LoadedSceneFor(sceneSaveName, "SaveGame"));
        }

        private static void LoadSceneGuard(Action<string, string> orig, string sceneName, string sceneSaveFilenameCurrent)
        {
            NoteModSceneLoad(sceneName);
            orig(sceneName, LoadedSceneFor(sceneSaveFilenameCurrent, "LoadScene"));
        }

        private static void SaveFilenameGuard(Action<SceneTransitionData, string> orig, SceneTransitionData self, string value)
        {
            orig(self, LoadedSceneFor(value, "m_SceneSaveFilenameCurrent"));
        }

        // ─── MOD SCENE LOADS ───
        // A mod loading a scene while the player is in a building is a transition, as a door is:
        // the mod decides where the player comes out, so the saved "inside" state must not pull them back.

        private const float MOD_LOAD_WINDOW_SECONDS = 180f;
        private static string s_ModLoadScene;
        private static float s_ModLoadTime;

        private static void NoteModSceneLoad(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || s_EnteredSpaceId == null) return;

            s_ModLoadScene = sceneName;
            s_ModLoadTime = Time.realtimeSinceStartup;
            MelonLogger.Msg($"[MOD-GECIS] Bir mod {s_EnteredSpaceId} icindeyken '{sceneName}' sahnesini yukletti.");
        }

        // A load a mod asked for while the player was inside, still on its way.
        internal static bool IsModSceneLoadPending()
        {
            return s_ModLoadScene != null && Time.realtimeSinceStartup - s_ModLoadTime <= MOD_LOAD_WINDOW_SECONDS;
        }

        // True once, for the scene that load was aimed at.
        internal static bool TakeModSceneLoad(string sceneName)
        {
            if (!IsModSceneLoadPending() || s_ModLoadScene != sceneName) return false;

            s_ModLoadScene = null;
            return true;
        }

        // A building's interior scene name while its region is the loaded scene stands for the region.
        private static string LoadedSceneFor(string name, string where)
        {
            if (string.IsNullOrEmpty(name)) return name;

            try
            {
                string loaded = ReadReal(() => GameManager.m_ActiveScene);
                if (string.IsNullOrEmpty(loaded) || loaded == name) return name;

                foreach (var instance in ActiveInteriors.Values)
                {
                    if (instance == null || instance.Config == null) continue;
                    if (instance.Config.InteriorSceneBaseName != name || instance.Config.ExteriorSceneName != loaded) continue;

                    if (s_SaveNameLogs < SAVE_NAME_LOG_LIMIT)
                    {
                        s_SaveNameLogs++;
                        MelonLogger.Msg($"[KAYIT-ADI] {where}: bir mod '{name}' verdi, yuklu sahne '{loaded}' yazildi.");
                    }
                    return loaded;
                }
            }
            catch { }

            return name;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(IndoorSpaceTrigger), nameof(IndoorSpaceTrigger.OnTriggerEnter))]
    internal static class ForeignTriggerEnterPatch
    {
        private static void Postfix(IndoorSpaceTrigger __instance, Collider __0)
        {
            try { SeamlessInteriorsMod.NoteForeignTriggerEvent(__instance, __0, true); }
            catch { }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(IndoorSpaceTrigger), nameof(IndoorSpaceTrigger.OnTriggerExit))]
    internal static class ForeignTriggerExitPatch
    {
        private static void Postfix(IndoorSpaceTrigger __instance, Collider __0)
        {
            try { SeamlessInteriorsMod.NoteForeignTriggerEvent(__instance, __0, false); }
            catch { }
        }
    }

    // Unity announces scene loads and unloads here, and MelonLoader reads the scene's name inside.
    // Running first, this keeps the scene view off until the callback is over.
    [HarmonyLib.HarmonyPatch(typeof(UnityEngine.SceneManagement.SceneManager), "Internal_SceneLoaded")]
    internal static class SceneLoadedViewGuardPatch
    {
        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.First)]
        private static void Prefix() { SeamlessInteriorsMod.EnterUnitySceneCallback(); }

        private static void Finalizer() { SeamlessInteriorsMod.ExitUnitySceneCallback(); }
    }

    [HarmonyLib.HarmonyPatch(typeof(UnityEngine.SceneManagement.SceneManager), "Internal_SceneUnloaded")]
    internal static class SceneUnloadedViewGuardPatch
    {
        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.First)]
        private static void Prefix() { SeamlessInteriorsMod.EnterUnitySceneCallback(); }

        private static void Finalizer() { SeamlessInteriorsMod.ExitUnitySceneCallback(); }
    }
}
