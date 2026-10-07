using UnityEngine;

namespace SeamlessInteriors
{
    // One window's hand-set fit, for a building whose windows cannot be measured right. Every
    // length is in CENTIMETERS, because these rows are read off sliders and pasted here by hand.
    // "Right" and "up" are as seen from OUTSIDE, facing the window, from the center the mod
    // measured for that pane.
    public struct WindowFit
    {
        public int Window;                                  // the mod's own window index for the shell
        public float PortalRight, PortalUp;                 // how far the picture of the room is slid
        public float PortalWidth, PortalHeight;             // its size; 0 = as measured
        public float FrostRight, FrostUp;                   // where the frost's middle stands
        public float FrostWidth, FrostHeight;               // its size; 0 = as built from the glass
        public int FrostSource;                             // window whose frost layout this one wears; -1 = the mod decides, -2 = always its own
        // Where THIS window's view from outside starts, from the ROOM's own glass (or, where the room
        // has no glass there, from where the game's painted outside stood): + toward outdoors,
        // - into the room. Only where HasViewStart; otherwise the building's row or the mod decides.
        // The one lever that keeps a view's start out of the wall beside a deep opening.
        public float ViewStartCm;
        public bool HasViewStart;
        // The same point slid sideways and up IN THE ROOM: where the camera stands indoors, not where
        // the picture hangs on the building (PortalRight and PortalUp are that). 0 = as measured.
        public float RoomRightCm, RoomUpCm;
        // The room seen through the hole made taller or wider, in percent, without resizing the hole:
        // the view's computed fit (the room window's size over the building's) divided by 1 + this.
        // For a room window that is a different shape from the building's, where no fit of the two
        // frames can be right (the Camp Office's window 0). 0 = as computed.
        public float RoomTallerPercent, RoomWiderPercent;

        public WindowFit(int window, float portalRight, float portalUp, float portalWidth, float portalHeight,
                         float frostRight, float frostUp, float frostWidth, float frostHeight, int frostSource,
                         float viewStartCm = float.NaN, float roomRightCm = 0f, float roomUpCm = 0f,
                         float roomTallerPercent = 0f, float roomWiderPercent = 0f)
        {
            RoomRightCm = roomRightCm; RoomUpCm = roomUpCm;
            RoomTallerPercent = roomTallerPercent; RoomWiderPercent = roomWiderPercent;
            HasViewStart = !float.IsNaN(viewStartCm);
            ViewStartCm = HasViewStart ? viewStartCm : 0f;
            Window = window;
            PortalRight = portalRight; PortalUp = portalUp; PortalWidth = portalWidth; PortalHeight = portalHeight;
            FrostRight = frostRight; FrostUp = frostUp; FrostWidth = frostWidth; FrostHeight = frostHeight;
            FrostSource = frostSource;
        }
    }

    public partial class SeamlessInteriorsMod
    {
        // ─── WINDOWS SET BY HAND, FOR THE BUILDINGS THAT NEED IT ───
        //
        // The rows themselves, the hand fits and the windows left alone, live with every other
        // per-building exception in s_BuildingWindowOverrides (BuildingWindowOverrides.cs). What
        // is here reads them.
        internal static bool HasNoView(string shellName, int window)
        {
            BuildingWindowOverride row;
            if (string.IsNullOrEmpty(shellName) || !TryGetBuildingWindowOverride(shellName, out row) || row.WindowsLeftAlone == null) return false;
            int[] rows = row.WindowsLeftAlone;
            for (int i = 0; i < rows.Length; i++) if (rows[i] == window) return true;
            return false;
        }

        internal static bool TryGetWindowFit(string shellName, int window, out WindowFit fit)
        {
            fit = default(WindowFit);
            BuildingWindowOverride row;
            if (string.IsNullOrEmpty(shellName) || !TryGetBuildingWindowOverride(shellName, out row) || row.Fits == null) return false;
            WindowFit[] rows = row.Fits;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].Window == window) { fit = rows[i]; return true; }
            return false;
        }

        // The picture of the room, slid and sized. Delta is left as it was measured, so sliding the
        // picture slides the window along the wall: the rectangle looked through on the room's side
        // moves with it. That is how the owner's numbers were set, so that is what they mean.
        private static void ApplyWindowFitsToApertures(string shellName)
        {
            for (int a = 0; a < s_OutsideInApertures.Count; a++)
            {
                var pane = s_OutsideInApertures[a];
                pane.Center = pane.BaseCenter; pane.Width = pane.BaseWidth; pane.Height = pane.BaseHeight;

                WindowFit fit;
                if (TryGetWindowFit(shellName, a, out fit))
                {
                    pane.Center = pane.BaseCenter + pane.Right * (fit.PortalRight * 0.01f) + pane.Up * (fit.PortalUp * 0.01f);
                    if (fit.PortalWidth > 0f) pane.Width = fit.PortalWidth * 0.01f;
                    if (fit.PortalHeight > 0f) pane.Height = fit.PortalHeight * 0.01f;
                }

                // WHERE THE VIEW STARTS IS TAKEN AGAIN HERE, every time the windows are fitted. It used
                // to be taken once, when the windows were paired, so nothing set afterwards could move
                // it: a console slider on 2026-09-19 wrote its number where nothing read it again
                // ("I moved the slider but nothing changed"). The same expression as at the pairing.
                if (pane.Paired && !pane.Pretend && a < s_OutsideInApertureOpenings.Count && s_OutsideInApertureOpenings[a].PaneRoot != null)
                    pane.Delta = RoomPointBehindPane(s_OutsideInApertureOpenings[a], shellName, a) - pane.BaseCenter;
                s_OutsideInApertures[a] = pane;
            }
        }

        // The frost laid over a rectangle given by hand: the layout's own outline, as built or as
        // borrowed, is carried onto it. Returns the rectangle the frost covers either way, from the
        // pane's MEASURED center, in meters (x, y = left and right; z, w = bottom and top).
        private static Vector4 FitOutsideInFrostLayout(string shellName, int a, Vector3[] layout)
        {
            var pane = s_OutsideInApertures[a];
            float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            for (int i = 0; i < layout.Length; i++)
            {
                Vector3 fromCenter = layout[i] - pane.BaseCenter;
                float across = Vector3.Dot(fromCenter, pane.Right), upward = Vector3.Dot(fromCenter, pane.Up);
                left = Mathf.Min(left, across); right = Mathf.Max(right, across);
                bottom = Mathf.Min(bottom, upward); top = Mathf.Max(top, upward);
            }
            var built = new Vector4(left, right, bottom, top);

            if (right - left < 0.01f || top - bottom < 0.01f) return built;

            // The rectangle wanted: the window's own hand-set one where it has one, otherwise as built.
            var wanted = built;
            WindowFit fit;
            if (TryGetWindowFit(shellName, a, out fit) && fit.FrostWidth > 0f && fit.FrostHeight > 0f)
                wanted = new Vector4((fit.FrostRight - fit.FrostWidth * 0.5f) * 0.01f, (fit.FrostRight + fit.FrostWidth * 0.5f) * 0.01f,
                                     (fit.FrostUp - fit.FrostHeight * 0.5f) * 0.01f, (fit.FrostUp + fit.FrostHeight * 0.5f) * 0.01f);
            if (wanted.y - wanted.x < 0.01f || wanted.w - wanted.z < 0.01f) return built;
            if (wanted == built) return built;

            for (int i = 0; i < layout.Length; i++)
            {
                Vector3 fromCenter = layout[i] - pane.BaseCenter;
                float across = (Vector3.Dot(fromCenter, pane.Right) - left) / (right - left);
                float upward = (Vector3.Dot(fromCenter, pane.Up) - bottom) / (top - bottom);
                layout[i] = pane.BaseCenter + pane.Right * Mathf.Lerp(wanted.x, wanted.y, across) + pane.Up * Mathf.Lerp(wanted.z, wanted.w, upward);
            }
            return wanted;
        }
    }
}
