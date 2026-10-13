using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // The same frost and snow seen from outside the building (owner, 2026-10-13): the room's frost
        // pattern on the building's glass, read from the other side, and the same bank outside, both from
        // the window's own record. They stand in for the building's painted frost overlay and the copied
        // room slabs (OutsideInPortals.cs), which only knew cleaned or not.
        private const int COVER_OUTSIDE_FROST_SIZE = 256;

        // The bank stays this far behind the frame's outer face, so the frame and bars cover its ends.
        private const float OUTSIDE_BANK_BEHIND_FRAME = 0.004f;
        // The bars in front of the glass hide about this much of a pane's top.
        private const float OUTSIDE_BANK_UNDER_BAR = 0.04f;

        // Built with the building's dressing, after its glass has been measured.
        private static void BuildOutsideCovers(SeamlessInteriorInstance instance)
        {
            DestroyOutsideCovers();
            if (!WindowCoverLayers || instance == null) return;
            Vector3[] shellWorld; int[] shellTriangles;
            TryGetShellWorldMesh(instance, out shellWorld, out shellTriangles);
            for (int a = 0; a < s_OutsideInApertures.Count; a++)
            {
                s_OI.Covers.Add(null);
                var pane = s_OutsideInApertures[a];
                if (!pane.Paired || IsLeftAloneAt(instance.Config.ExteriorShellPrefabName, a)) continue;
                Vector4 glass = a < s_OutsideInGlassRects.Count ? s_OutsideInGlassRects[a]
                              : new Vector4(-pane.Width * 0.5f, pane.Width * 0.5f, -pane.Height * 0.5f, pane.Height * 0.5f);
                float width = glass.y - glass.x, height = glass.w - glass.z;
                if (width < 0.05f || height < 0.05f) continue;

                // Across runs the room's way (the building's right is the room's left), so the room's
                // frost pattern lands where it is seen from inside.
                var layer = new WindowCoverLayer { Opening = a, Seed = a, FrostSize = COVER_OUTSIDE_FROST_SIZE };
                Vector3 across = -pane.Right;
                Vector3 frostCenter = pane.Center + pane.Right * ((glass.x + glass.y) * 0.5f) + pane.Up * ((glass.z + glass.w) * 0.5f)
                                    + pane.Outward * (pane.Toward + OUTSIDE_IN_FROST_PROUD_OF_QUAD);
                if (!MakeCoverFrost(layer, "SeamlessInteriors_OutsideFrost_" + a, frostCenter, across, pane.Up, pane.Outward,
                                    width, height, WINDOW_VIEW_FADE_RENDER_QUEUE + 10)) continue;

                // In front of the picture of the room, and behind the frame's outer face: the frame then
                // covers the bank's ends from any side. Further out, it stood past the frame and over the
                // middle bar (owner, 2026-10-13; Camp Office frames stand 3.1 cm out from the glass).
                float relief;
                if (shellWorld == null || !TryMeasureWindowRelief(pane, glass, shellWorld, shellTriangles, out relief)) relief = 0.03f;
                float room = Mathf.Max(relief - pane.Toward - 0.004f - OUTSIDE_BANK_BEHIND_FRAME, 0.006f);
                layer.BankDepth = room * 0.55f; layer.BankReach = room * 0.45f;
                layer.BankTuck = 0f; layer.BankUnderBar = OUTSIDE_BANK_UNDER_BAR;
                layer.Center = pane.Center + pane.Outward * pane.Toward; layer.Right = across; layer.Up = pane.Up; layer.Outward = pane.Outward;
                layer.Panes = ShellPanes(a, pane, glass);
                var opening = a < s_OI.ApertureOpenings.Count ? s_OI.ApertureOpenings[a] : default(InteriorWindowOpening);
                MakeCoverBank(layer, CoverBankMaterial(opening), "SeamlessInteriors_OutsideSnow_" + a);
                FollowOutsideBank(layer, a);
                StartCoverTables(layer, width / height);
                s_OI.Covers[a] = layer;
            }
        }

        // The building's own panes: the rectangles of its glass triangles (the frost overlay was cut from
        // them), in the cover's across. The room's panes laid onto the building put the middle bar on the
        // wrong side on half the Camp Office's windows.
        private static List<float[]> ShellPanes(int aperture, ShellAperture pane, Vector4 glass)
        {
            var panes = new List<float[]>();
            var overlay = aperture < s_OutsideInFrostOverlays.Count ? s_OutsideInFrostOverlays[aperture] : null;
            var filter = overlay != null ? overlay.GetComponent<MeshFilter>() : null;
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null)
            {
                var vertices = mesh.vertices;
                for (int k = 0; k + 2 < vertices.Length; k += 3)
                {
                    float r0 = float.MaxValue, r1 = float.MinValue, u0 = float.MaxValue, u1 = float.MinValue;
                    for (int j = 0; j < 3; j++)
                    {
                        var p = vertices[k + j] - pane.Center;
                        float r = -Vector3.Dot(p, pane.Right), u = Vector3.Dot(p, pane.Up);
                        r0 = Mathf.Min(r0, r); r1 = Mathf.Max(r1, r); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u);
                    }
                    if (u1 - u0 < 0.05f || r1 - r0 < 0.05f) continue;
                    bool seen = false;
                    foreach (var q in panes)
                        if (Mathf.Abs(q[0] - r0) < 0.01f && Mathf.Abs(q[1] - r1) < 0.01f && Mathf.Abs(q[2] - u0) < 0.01f && Mathf.Abs(q[3] - u1) < 0.01f) { seen = true; break; }
                    if (!seen) panes.Add(new[] { r0, r1, u0, u1, 0f });
                }
            }
            if (panes.Count == 0) panes.Add(new[] { -glass.y, -glass.x, glass.z, glass.w, 0f });
            foreach (var p in panes)
                foreach (var other in panes)
                    if (other != p && other[2] >= p[3] - 0.01f && other[2] - p[3] < 0.2f && other[0] < p[1] && other[1] > p[0]) p[4] = 1f;
            return panes;
        }

        // Whether this window's frost is drawn by the cover rather than the old overlay.
        private static bool OutsideCoverShown(int aperture)
        {
            return aperture < s_OI.Covers.Count && s_OI.Covers[aperture] != null && s_OI.Covers[aperture].Shown;
        }

        // Whether the building's old frost overlay (frost true) or copied drift shows. The bank is made
        // with the dressing and needs no tables, so its window never shows the old drift. The frost waits
        // for its tables; until then the old overlay shows by the record, not by "cleaned": a window
        // cleaned from inside keeps its snow and may be frosting again (owner, 2026-10-13).
        private static bool OutsideOldDressingShows(int aperture, bool frost)
        {
            // Asked every time: asking is what lets a cleaned window frost over again (WindowFrostState.cs).
            bool cleaned = IsWindowCleaned(s_OutsideInInstanceId, aperture);
            var layer = WindowCoverLayers && aperture < s_OI.Covers.Count ? s_OI.Covers[aperture] : null;
            if (layer == null) return !cleaned;
            if (!frost) return layer.BankGo == null && !cleaned;
            if (layer.Shown) return false;
            float frostCover, snowCover;
            GetWindowCover(s_OutsideInInstanceId, aperture, false, out frostCover, out snowCover);
            return frostCover >= 0.5f;
        }

        // The bank follows the record right away. A bank is a few milliseconds; held to two updates a
        // second with the frost, the last window's snow came eight seconds after the building was dressed.
        private static void FollowOutsideBank(WindowCoverLayer layer, int aperture)
        {
            if (layer.BankGo == null) return;
            float frost, snow;
            GetWindowCover(s_OutsideInInstanceId, aperture, false, out frost, out snow);
            float bankSnow = InsideOutShowSnowSlab ? snow : 0f;
            if (layer.SnowShown < 0f || Mathf.Abs(bankSnow - layer.SnowShown) >= COVER_SNOW_STEP || (bankSnow == 0f) != (layer.SnowShown == 0f))
                BuildCoverBank(layer, bankSnow);
            bool show = layer.SnowShown > 0f;
            if (layer.BankGo.activeSelf != show) layer.BankGo.SetActive(show);
        }

        // From UpdateOutsideInDressing, for the building bound now. The frost shows where the old overlay
        // did (with the view drawn behind it); the bank is outside geometry and shows whenever it has snow.
        private static void TickOutsideCovers(bool live)
        {
            var covers = s_OI.Covers;
            if (covers.Count == 0) return;
            bool force = false;
            for (int a = 0; a < covers.Count; a++)
            {
                var layer = covers[a];
                if (layer == null || layer.Shown || !layer.Ready) continue;
                if (layer.Error != null)
                {
                    MelonLogger.Warning($"[DIS-ORTU] {s_OutsideInInstanceId} pencere {a}: {layer.Error}", $"[OUTSIDE-COVER] {s_OutsideInInstanceId} window {a}: {layer.Error}");
                    DestroyCoverLayer(layer);
                    covers[a] = null;
                    continue;
                }
                layer.Shown = true;
                force = true;
            }

            float real = Time.realtimeSinceStartup;
            if (!force && !s_CoverDirty && real < s_OI.CoversNextTick && live == s_OI.CoversLive) return;
            s_OI.CoversNextTick = real + 1f;
            s_OI.CoversLive = live;

            Color light = CoverSnowLight();
            int fills = 0;
            for (int a = 0; a < covers.Count; a++)
            {
                var layer = covers[a];
                if (layer == null) continue;
                FollowOutsideBank(layer, a);
                if (!layer.Shown) continue;
                float frost, snow;
                GetWindowCover(s_OutsideInInstanceId, a, false, out frost, out snow);
                // A first fill always; a change at most two a tick, the rest on the next.
                bool fresh = layer.FrostShown < 0f;
                if (fresh || (fills < 2 && (Mathf.Abs(frost - layer.FrostShown) >= COVER_FROST_STEP || (frost == 0f) != (layer.FrostShown == 0f))))
                {
                    FillCoverFrost(layer, frost);
                    if (!fresh) fills++;
                }
                if (layer.FrostMat.color != light) layer.FrostMat.color = light;
                bool showFrost = live && OutsideInFrostedPane && layer.FrostShown >= 0f;
                if (layer.FrostGo.activeSelf != showFrost) layer.FrostGo.SetActive(showFrost);
            }
        }

        private static void DestroyOutsideCovers()
        {
            foreach (var layer in s_OI.Covers) DestroyCoverLayer(layer);
            s_OI.Covers.Clear();
        }
    }
}
