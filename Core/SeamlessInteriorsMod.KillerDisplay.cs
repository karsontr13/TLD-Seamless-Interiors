using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── DEVELOPER DISPLAY: THE SNOW KILLERS, DRAWN LIVE ───
        //
        // A snow killer is invisible, so whether one covers a room could only be argued about. With
        // ShowSnowKillers on, every killer in the region is drawn as a see-through box colored by its state (below), from anywhere: those in the game's own list
        // (WeatherParticleManager.m_AllParticleKillers) and the mod's room killers held out of it, every frame. Nothing is built by hand, and a
        // killer added, moved or removed by anyone shows at once.
        //
        // Drawing the 72 slices of the hangar this way on 2026-09-22 is what showed them spread over
        // about 135 x 135 m round an 86 x 89 m room.
        //
        // THE COLOR SAYS WHETHER IT IS IN FORCE:
        //   green   the game is killing snow in it this frame (its own ShouldBeActive test, against
        //           the camera position, is true)
        //   yellow  in the game's list, but that test is false at the moment
        //   red     a room killer the mod is holding OUT of the list, the player being outside
        //
        // Off by default. The boxes have no collider, cast no shadow, and are destroyed as soon as
        // the setting is switched off.
        private static readonly Color KILLER_COLOR_ACTIVE = new Color(0f, 1f, 0f, 0.25f);
        private static readonly Color KILLER_COLOR_IDLE = new Color(1f, 1f, 0f, 0.25f);
        private static readonly Color KILLER_COLOR_HELD_OUT = new Color(1f, 0f, 0f, 0.25f);

        private static MelonPreferences_Entry<bool> s_PrefShowSnowKillers;
        private static readonly List<GameObject> s_KillerDisplayBoxes = new List<GameObject>();
        private static Material s_KillerMatActive, s_KillerMatIdle, s_KillerMatHeldOut;

        internal static void TickSnowKillerDisplay()
        {
            bool wanted = s_PrefShowSnowKillers != null && s_PrefShowSnowKillers.Value;
            if (!wanted)
            {
                if (s_KillerDisplayBoxes.Count > 0) ClearSnowKillerDisplay();
                return;
            }

            try
            {
                var uniStorm = GetCachedUniStorm();
                Camera main = GameManager.GetMainCamera();
                if (uniStorm == null || uniStorm.m_WeatherParticleManager == null || main == null) { HideKillerBoxesFrom(0); return; }

                // Each checked: a region load unloads assets nothing in the scene uses, and on 2026-09-22 it
                // destroyed the yellow and red materials while green survived, so boxes in those states
                // drew Unity's magenta "missing material" color. They are now also kept from unloading.
                if (s_KillerMatActive == null || s_KillerMatIdle == null || s_KillerMatHeldOut == null)
                {
                    s_KillerMatActive = MakeKillerMaterial(KILLER_COLOR_ACTIVE);
                    s_KillerMatIdle = MakeKillerMaterial(KILLER_COLOR_IDLE);
                    s_KillerMatHeldOut = MakeKillerMaterial(KILLER_COLOR_HELD_OUT);
                }
                Vector3 camPos = main.transform.position;

                var killers = uniStorm.m_WeatherParticleManager.m_AllParticleKillers;
                int shown = 0;
                for (int i = 0; i < killers.Count; i++)
                {
                    var k = killers[i];
                    PlaceKillerBox(ref shown, k.m_Bounds, k.ShouldBeActive(camPos) ? s_KillerMatActive : s_KillerMatIdle);
                }

                // The mod takes a room's killers OUT of the game's list while the player is outside
                // it (ApplyParticleKillers, Visibility.cs). Draw those too, so every killer that
                // exists shows from anywhere, not only the ones in force at this moment.
                foreach (var instance in ActiveInteriors.Values)
                {
                    if (instance == null || instance.CustomKillers == null || instance.CustomKillers.Count == 0) continue;
                    if (killers.Contains(instance.CustomKillers[0])) continue;
                    foreach (var pk in instance.CustomKillers) PlaceKillerBox(ref shown, pk.m_Bounds, s_KillerMatHeldOut);
                }
                HideKillerBoxesFrom(shown);
            }
            catch (System.Exception e)
            {
                // A developer display must never take the frame down with it: switch it off.
                MelonLogger.Warning($"[KILLER-DISPLAY] Oldurucu gosterimi kapatildi: {e.Message}",
                                    $"[KILLER-DISPLAY] Snow killer display switched off after an error: {e.Message}");
                s_PrefShowSnowKillers.Value = false;
                ClearSnowKillerDisplay();
            }
        }

        private static Material MakeKillerMaterial(Color color)
        {
            var m = new Material(Shader.Find("Sprites/Default"));
            m.color = color;
            m.renderQueue = 3500;
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return m;
        }

        private static void PlaceKillerBox(ref int shown, Bounds b, Material material)
        {
            GameObject box = shown < s_KillerDisplayBoxes.Count ? s_KillerDisplayBoxes[shown] : null;
            if (box == null)
            {
                box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "SI_SnowKillerDisplay";
                Object.Destroy(box.GetComponent<Collider>());
                Object.DontDestroyOnLoad(box);
                var r = box.GetComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (shown < s_KillerDisplayBoxes.Count) s_KillerDisplayBoxes[shown] = box;
                else s_KillerDisplayBoxes.Add(box);
            }
            var renderer = box.GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
            if (!box.activeSelf) box.SetActive(true);
            box.transform.SetPositionAndRotation(b.center, Quaternion.identity);
            box.transform.localScale = b.size;
            shown++;
        }

        private static void HideKillerBoxesFrom(int first)
        {
            for (int i = first; i < s_KillerDisplayBoxes.Count; i++)
            {
                var box = s_KillerDisplayBoxes[i];
                if (box != null && box.activeSelf) box.SetActive(false);
            }
        }

        private static void ClearSnowKillerDisplay()
        {
            foreach (var box in s_KillerDisplayBoxes)
                if (box != null) Object.Destroy(box);
            s_KillerDisplayBoxes.Clear();
        }
    }
}
