using MelonLoader;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── THE IN-GAME SETTINGS PAGE (ModSettings) ───
        //
        // Approved by the mod's author (2026-10-01). Before this, every option was a MelonPreferences
        // entry a player could only change by editing UserData/MelonPreferences.cfg.
        //
        // MELONPREFERENCES STAYS THE ONLY STORE. The page derives from ModSettingsBase, not
        // JsonModSettings, so ModSettings writes no file of its own: the fields below are copied in
        // from the entries before the page is shown and copied back out on Confirm. Two stores would
        // disagree the first time F7 or F8 changed an entry behind the page's back, and a JSON file's
        // saved key always beats a new code default (TLD-Knowledge, ModSettings traps), which would
        // have frozen every default at whatever the first build wrote.
        //
        // ONLY WHAT A PLAYER SHOULD TOUCH IS ON THE PAGE. The author's "turn off only for
        // troubleshooting" save switches (loot roll, gear filter, legacy import, save identity,
        // deletion, lazy content, removed objects, fast toggle), the per-wall camera switches, the
        // frost strengths and range tuned by eye, the log language and the snow-killer display stay in
        // the preferences file, where they always were.
        //
        // OPTIONAL. Without ModSettings.dll installed the mod runs exactly as before; nothing outside
        // this file names a ModSettings type, and the call that does is kept out of line so the JIT
        // never has to resolve it unless the library is loaded.

        public static bool IsModSettingsLoaded()
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name == "ModSettings") return true;
            return false;
        }

        private static void TryRegisterSettingsPage()
        {
            if (!IsModSettingsLoaded())
            {
                MelonLogger.Msg("[AYARLAR] ModSettings yuklu degil; ayarlar MelonPreferences.cfg dosyasinda.",
                                "[SETTINGS] ModSettings is not installed; the settings stay in MelonPreferences.cfg.");
                return;
            }
            try { RegisterSettingsPage(); }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[AYARLAR] Ayar sayfasi kurulamadi: {ex.Message}",
                                    $"[SETTINGS] The settings page could not be added: {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterSettingsPage()
        {
            var page = new SettingsPage();
            page.CopyFromPreferences();
            page.AddToModSettings("Seamless Interiors");
            s_SettingsPage = page;
            MelonLogger.Msg("[AYARLAR] Ayar sayfasi eklendi (Secenekler > Mod Ayarlari).",
                            "[SETTINGS] Settings page added (Options > Mod Settings).");
        }

        private static object s_SettingsPage;

        // Called where an entry may have changed behind the page (F7, F8, a scene load), so the page
        // shows the live value the next time it opens. Cheap: a handful of field copies.
        private static void RefreshSettingsPage()
        {
            if (s_SettingsPage == null) return;
            try { RefreshSettingsPageNow(); } catch { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RefreshSettingsPageNow()
        {
            ((SettingsPage)s_SettingsPage).CopyFromPreferences();
        }

        // Choice labels match an enum by position, so the order here is the order in the file.
        // The values are the integers InteriorLightingMode already stores.
        internal enum LightingChoice { OutdoorLighting = 0, DarkAtmosphere = 1 }

        internal class SettingsPage : ModSettings.ModSettingsBase
        {
            [ModSettings.Section("Doors")]
            [ModSettings.Name("Fade doors through black")]
            [ModSettings.Description("A door fades the screen to black and back, about three quarters of a second in all, instead of cutting straight to the other side.")]
            public bool DoorFade = true;

            [ModSettings.Name("Interior lighting")]
            [ModSettings.Description("Outdoor Lighting: rooms are lit to match the world outside. Dark Atmosphere: the game's original indoor mood. Takes effect at the next region load (F8 also switches it).")]
            [ModSettings.Choice(new string[] { "Outdoor Lighting", "Dark Atmosphere" })]
            public LightingChoice InteriorLighting = LightingChoice.OutdoorLighting;

            [ModSettings.Section("Windows")]
            [ModSettings.Name("See through windows")]
            [ModSettings.Description("Windows show the real world outside, and the real room from outside, instead of painted backdrops. Off is the cheapest setting on a slow machine.")]
            public bool SeeThroughWindows = true;

            [ModSettings.Name("Clean windows by hand")]
            [ModSettings.Description("Look at a frosted window up close and hold the interact button to scrape it clear. A cleaned window is clean from inside and outside, and is kept with your save.")]
            public bool CleanWindowsByHand = true;

            [ModSettings.Name("Windows frost over again after (hours)")]
            [ModSettings.Description("Game hours until a cleaned window frosts over again. A fire burning in the building holds the count at zero. Heavy snow and blizzards shorten it. 0: a cleaned window stays clean for ever.")]
            [ModSettings.Slider(0f, 240f, 21, NumberFormat = "{0:F0}")]
            public float WindowFrostReturnsAfter = 48f;

            [ModSettings.Name("Windows start clean")]
            [ModSettings.Description("Off, as in the base game: windows start frosted with snow in their frames. On: every window you have not touched starts clean.")]
            public bool WindowsStartClean = false;

            [ModSettings.Name("Snow in window frames")]
            [ModSettings.Description("Show the drift of snow sitting in a frosted window's frame, from both sides.")]
            public bool SnowInWindowFrames = true;

            [ModSettings.Section("Window performance")]
            [ModSettings.Name("Window view sharpness")]
            [ModSettings.Description("Resolution of the view through a window, as a fraction of the screen's. Lower it to win back frame rate.")]
            [ModSettings.Slider(0.25f, 1f, 16, NumberFormat = "{0:F2}")]
            public float WindowViewSharpness = 1f;

            [ModSettings.Name("Windows drawn at once")]
            [ModSettings.Description("The most windows that may show a real view in the same frame, nearest and largest first. Only windows you can see are drawn. A building with more windows in sight than this fades them in and out as you move. Fewer is cheaper.")]
            [ModSettings.Slider(1f, 16f)]
            public int WindowsDrawnAtOnce = 10;

            [ModSettings.Name("Rooms seen from outside within (m)")]
            [ModSettings.Description("How near a building must be before its windows show the real room. Past this a window keeps its frost and dark glass.")]
            [ModSettings.Slider(20f, 200f, 37, NumberFormat = "{0:F0}")]
            public float RoomsSeenWithin = 100f;

            [ModSettings.Name("Rooms kept ready")]
            [ModSettings.Description("How many nearby buildings keep their rooms ready to be seen through their windows at once. Fewer is cheaper.")]
            [ModSettings.Slider(1f, 6f)]
            public int RoomsKeptReady = 3;

            [ModSettings.Section("Window glow")]
            [ModSettings.Name("Window glow at night")]
            [ModSettings.Description("While a fire burns inside, the windows glow warm orange from dusk to dawn.")]
            public bool WindowGlow = true;

            [ModSettings.Name("Glow flicker")]
            [ModSettings.Description("A very slight firelight flicker in the glow.")]
            public bool GlowFlicker = true;

            [ModSettings.Name("Glow strength")]
            [ModSettings.Slider(0f, 2f, 21, NumberFormat = "{0:F1}")]
            public float GlowStrength = 1f;

            [ModSettings.Name("Glow on the ground")]
            [ModSettings.Description("Lit windows also throw a soft orange light onto the snow in front of them.")]
            public bool GroundGlow = true;

            [ModSettings.Name("Ground glow strength")]
            [ModSettings.Slider(0f, 2f, 21, NumberFormat = "{0:F1}")]
            public float GroundGlowStrength = 1f;

            [ModSettings.Section("Atmosphere")]
            [ModSettings.Name("Chimney smoke")]
            [ModSettings.Description("Smoke rises from a building's chimney while a stove or fireplace burns inside it.")]
            public bool ChimneySmoke = true;

            [ModSettings.Section("Troubleshooting")]
            [ModSettings.Name("Verbose logging")]
            [ModSettings.Description("Writes a log line per object during save and load. Costs frame time; on only when reporting a problem (F7).")]
            public bool VerboseLogging = false;

            [ModSettings.Name("Performance report")]
            [ModSettings.Description("Writes a performance summary to the log every 5 seconds. Turn on when reporting frame drops (Shift+F6).")]
            public bool PerformanceReport = false;

            // Slider ranges are checked against the field's value when the page is added, so a value
            // set by hand in the file outside the page's range is shown clamped, and only written
            // back clamped if the player confirms the page.
            internal void CopyFromPreferences()
            {
                DoorFade = s_PrefDoorFade?.Value ?? true;
                InteriorLighting = (s_PrefInteriorLightingMode?.Value ?? 0) == 1 ? LightingChoice.DarkAtmosphere : LightingChoice.OutdoorLighting;
                SeeThroughWindows = s_PrefEnableWindowPortals?.Value ?? true;
                CleanWindowsByHand = s_PrefCleanWindowsByHand?.Value ?? true;
                WindowFrostReturnsAfter = Mathf.Clamp(s_PrefWindowFrostReturnHours?.Value ?? 48f, 0f, 240f);
                WindowsStartClean = s_PrefClearWindowPanes?.Value ?? false;
                SnowInWindowFrames = s_PrefInsideOutShowSnowSlab?.Value ?? true;
                WindowViewSharpness = Mathf.Clamp(s_PrefWindowPortalResolutionScale?.Value ?? 1f, 0.25f, 1f);
                WindowsDrawnAtOnce = Mathf.Clamp(s_PrefWindowPortalLimit?.Value ?? 10, 1, 16);
                RoomsSeenWithin = Mathf.Clamp(s_PrefOutsideInPortalRange?.Value ?? 100f, 20f, 200f);
                RoomsKeptReady = Mathf.Clamp(s_PrefOutsideInMaxHeldRooms?.Value ?? 3, 1, 6);
                WindowGlow = s_PrefEnableWindowGlow?.Value ?? true;
                GlowFlicker = s_PrefWindowGlowFlicker?.Value ?? true;
                GlowStrength = Mathf.Clamp(s_PrefWindowGlowStrength?.Value ?? 1f, 0f, 2f);
                GroundGlow = s_PrefEnableWindowGroundGlow?.Value ?? true;
                GroundGlowStrength = Mathf.Clamp(s_PrefWindowGroundGlowStrength?.Value ?? 1f, 0f, 2f);
                ChimneySmoke = s_PrefEnableChimneySmoke?.Value ?? true;
                VerboseLogging = s_DebugBounds;
                PerformanceReport = PerfProbe.On;
                RefreshGUI();
            }

            protected override void OnConfirm()
            {
                Set(s_PrefDoorFade, DoorFade);
                Set(s_PrefInteriorLightingMode, InteriorLighting == LightingChoice.DarkAtmosphere ? 1 : 0);
                Set(s_PrefEnableWindowPortals, SeeThroughWindows);
                Set(s_PrefCleanWindowsByHand, CleanWindowsByHand);
                Set(s_PrefWindowFrostReturnHours, WindowFrostReturnsAfter);
                Set(s_PrefClearWindowPanes, WindowsStartClean);
                Set(s_PrefInsideOutShowSnowSlab, SnowInWindowFrames);
                Set(s_PrefWindowPortalResolutionScale, WindowViewSharpness);
                // The cameras for the views into rooms are made once at this size, so a new number
                // lets them go here and they are made again on the next frame that needs them. The
                // views out of a room take the new number the next time a room is entered.
                bool limitChanged = s_PrefWindowPortalLimit != null && s_PrefWindowPortalLimit.Value != WindowsDrawnAtOnce;
                Set(s_PrefWindowPortalLimit, WindowsDrawnAtOnce);
                if (limitChanged) { DestroyOutsideInSlots(); DestroyOutsideInFadeMaterials(); }
                Set(s_PrefOutsideInPortalRange, RoomsSeenWithin);
                Set(s_PrefOutsideInMaxHeldRooms, RoomsKeptReady);
                Set(s_PrefEnableWindowGlow, WindowGlow);
                Set(s_PrefWindowGlowFlicker, GlowFlicker);
                Set(s_PrefWindowGlowStrength, GlowStrength);
                Set(s_PrefEnableWindowGroundGlow, GroundGlow);
                Set(s_PrefWindowGroundGlowStrength, GroundGlowStrength);
                Set(s_PrefEnableChimneySmoke, ChimneySmoke);

                // These two are held outside their entries while the game runs (F7, Shift+F6).
                s_DebugBounds = VerboseLogging;
                Set(s_PrefVerboseLogging, VerboseLogging);
                if (PerfProbe.On != PerformanceReport) PerfProbe.SetEnabled(PerformanceReport, false);
                Set(s_PrefEnablePerfProbe, PerformanceReport);

                MelonPreferences.Save();
                MelonLogger.Msg("[AYARLAR] Ayar sayfasindan kaydedildi.", "[SETTINGS] Saved from the settings page.");
            }

            private static void Set<T>(MelonPreferences_Entry<T> entry, T value)
            {
                if (entry != null && !Equals(entry.Value, value)) entry.Value = value;
            }
        }
    }
}
