using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── EVERY VIEW FADES IN AND OUT ───
        //
        // Owner, 2026-09-26: "We need a smooth fade-in fade-out for all windows at the transitions.
        // Let's start with 2 seconds." Two transitions are faded for the views into a room:
        //
        // - THE CAMERA RANGE. Crossing it inward, the building keeps its own glass while the views
        //   fade in over it, and only then is the glass hidden. Crossing it outward, the glass comes
        //   back at once and the views, still drawing, fade out over it. See UpdateOutsideInDressing.
        // - A PANE GAINING OR LOSING ITS CAMERA. His own design of 2026-09-22: a pane that loses its
        //   place keeps its camera and fades out, and the pane that outranks it waits for the slot and
        //   fades in, so a handover is sequential and needs no extra cameras. A pane that goes off
        //   screen has nothing to fade and stops at once.
        //
        // A view part way through a fade is drawn with Sprites/Default, which blends by its color's
        // alpha, after the transparent queue so the building's glass is under it and not over it; a
        // view fully in keeps the opaque material it always had.
        internal static float OutsideInFadeSeconds = 2f;
        private const int WINDOW_VIEW_FADE_RENDER_QUEUE = 3050;

        private static Material[] s_OutsideInFadeMaterials;
        private static MaterialPropertyBlock s_ViewFadeBlock;
        private static int s_ViewFadeColorId = -1;
        private static readonly List<bool> s_OutsideInCandidateWanted = new List<bool>();
        // Every slot was taken last frame. A pane that starts drawing then took a slot another gave up:
        // a handover, and it fades in. Otherwise it is simply coming into view and shows at once.
        private static bool s_OutsideInPoolFullLastFrame;

        private static float OutsideInFadeStep()
        {
            return Time.unscaledDeltaTime / Mathf.Max(OutsideInFadeSeconds, 0.01f);
        }

        private static float OutsideInPaneLevel(OutsideInBuilding b, int index)
        {
            return index >= 0 && index < b.PaneLevel.Count ? b.PaneLevel[index] : 0f;
        }

        // The view's material for this much of it showing. Fully in: the slot's own opaque material.
        private static void ApplyViewFade(Renderer renderer, int slot, float alpha)
        {
            if (renderer == null || s_OutsideInMaterials == null || slot < 0 || slot >= s_OutsideInMaterials.Length) return;
            if (s_ViewFadeBlock == null) { s_ViewFadeBlock = new MaterialPropertyBlock(); s_ViewFadeColorId = Shader.PropertyToID("_Color"); }
            if (alpha >= 0.999f)
            {
                if (renderer.sharedMaterial != s_OutsideInMaterials[slot]) renderer.sharedMaterial = s_OutsideInMaterials[slot];
                return;
            }
            if (s_OutsideInFadeMaterials == null || s_OutsideInFadeMaterials.Length != s_OutsideInMaterials.Length)
                s_OutsideInFadeMaterials = new Material[s_OutsideInMaterials.Length];
            var fade = s_OutsideInFadeMaterials[slot];
            if (fade == null)
            {
                fade = new Material(Shader.Find("Sprites/Default"));
                fade.name = "SeamlessInteriors_ViewFade" + slot;
                fade.renderQueue = WINDOW_VIEW_FADE_RENDER_QUEUE;
                s_OutsideInFadeMaterials[slot] = fade;
            }
            var target = s_OutsideInTargets != null ? s_OutsideInTargets[slot] : null;
            if (fade.mainTexture != target) fade.mainTexture = target;
            if (renderer.sharedMaterial != fade) renderer.sharedMaterial = fade;
            s_ViewFadeBlock.SetColor(s_ViewFadeColorId, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            renderer.SetPropertyBlock(s_ViewFadeBlock);
        }

        private static void DestroyOutsideInFadeMaterials()
        {
            if (s_OutsideInFadeMaterials == null) return;
            foreach (var m in s_OutsideInFadeMaterials) if (m != null) Object.Destroy(m);
            s_OutsideInFadeMaterials = null;
        }

        // ─── THE TICK'S PART: WHO IS WANTED, WHO KEEPS A SLOT WHILE FADING ───

        // After the candidates are sorted by priority: which of them would get a slot on priority
        // alone (a wall group counted once), and only while the building's cameras are on. Then the
        // panes already showing are moved to the front, in priority order, so a pane fading out keeps
        // its slot until it is gone.
        private static void OrderOutsideInCandidatesForFade(bool wallCameras)
        {
            s_OutsideInCandidateWanted.Clear();
            int users = 0;
            var groupsSeen = new HashSet<long>();
            for (int a = 0; a < s_OutsideInCandidates.Count; a++)
            {
                var b = s_OutsideInCandidateBuilding[a];
                int index = s_OutsideInCandidates[a];
                bool wanted = false;
                if (b.CamerasOn && users < s_OutsideInSlotCount)
                {
                    long key = -1;
                    if (wallCameras)
                    {
                        BindOutsideIn(b);
                        EnsureOutsideInWallGroups(b);
                        int g = b.ApertureWallGroup[index];
                        if (g >= 0 && b.WallGroups[g].Members.Count > 1) key = ((long)b.GetHashCode() << 20) | (long)g;
                    }
                    if (key < 0 || groupsSeen.Add(key)) users++;
                    wanted = true;
                }
                s_OutsideInCandidateWanted.Add(wanted);
            }

            // Stable: showing panes first, each half in priority order.
            var order = new List<int>(s_OutsideInCandidates.Count);
            for (int a = 0; a < s_OutsideInCandidates.Count; a++) if (OutsideInPaneLevel(s_OutsideInCandidateBuilding[a], s_OutsideInCandidates[a]) > 0f) order.Add(a);
            for (int a = 0; a < s_OutsideInCandidates.Count; a++) if (OutsideInPaneLevel(s_OutsideInCandidateBuilding[a], s_OutsideInCandidates[a]) <= 0f) order.Add(a);
            var c = new List<int>(order.Count); var bl = new List<OutsideInBuilding>(order.Count); var p = new List<float>(order.Count); var w = new List<bool>(order.Count);
            foreach (int a in order) { c.Add(s_OutsideInCandidates[a]); bl.Add(s_OutsideInCandidateBuilding[a]); p.Add(s_OutsideInPriority[a]); w.Add(s_OutsideInCandidateWanted[a]); }
            s_OutsideInCandidates.Clear(); s_OutsideInCandidates.AddRange(c);
            s_OutsideInCandidateBuilding.Clear(); s_OutsideInCandidateBuilding.AddRange(bl);
            s_OutsideInPriority.Clear(); s_OutsideInPriority.AddRange(p);
            s_OutsideInCandidateWanted.Clear(); s_OutsideInCandidateWanted.AddRange(w);
        }

        private static void MarkOutsideInPaneDrawn(OutsideInBuilding b, int index, bool wanted)
        {
            while (b.PaneDrawnFrame.Count < b.Apertures.Count) b.PaneDrawnFrame.Add(-1);
            while (b.PaneWanted.Count < b.Apertures.Count) b.PaneWanted.Add(false);
            while (b.PaneDrawnLastFrame.Count < b.Apertures.Count) b.PaneDrawnLastFrame.Add(false);
            if (index < 0 || index >= b.PaneDrawnFrame.Count) return;
            if (b.PaneDrawnFrame[index] != Time.frameCount) b.PaneDrawnLastFrame[index] = b.PaneDrawnFrame[index] == Time.frameCount - 1;
            b.PaneDrawnFrame[index] = Time.frameCount;
            b.PaneWanted[index] = wanted;
        }

        // After the draw loop. A pane drawn this frame moves toward in if wanted and toward out if not;
        // a pane not drawn has nothing on screen to fade and is at zero. A pane that STARTS drawing
        // shows at once at the building's level, unless every slot was taken the frame before (a
        // handover), when it fades in. Without that, every pane turned into view faded in over the
        // whole fade, close to the building with half the slots free (owner, 2026-09-26: "windows
        // are fading in/out .. I'm close enough to where that shouldn't be happening"; read: 8 panes
        // on screen, 8 wanted, 4 of 10 slots in use, nothing part way faded).
        private static void UpdateOutsideInPaneLevels(bool poolFull)
        {
            bool handover = s_OutsideInPoolFullLastFrame;
            s_OutsideInPoolFullLastFrame = poolFull;
            float step = OutsideInFadeStep();
            int frame = Time.frameCount;
            for (int h = 0; h < s_OutsideInBuildings.Count; h++)
            {
                var b = s_OutsideInBuildings[h];
                while (b.PaneLevel.Count < b.Apertures.Count) b.PaneLevel.Add(0f);
                for (int i = 0; i < b.PaneLevel.Count; i++)
                {
                    bool drawn = i < b.PaneDrawnFrame.Count && b.PaneDrawnFrame[i] == frame;
                    if (!drawn) { b.PaneLevel[i] = 0f; continue; }
                    bool wanted = i < b.PaneWanted.Count && b.PaneWanted[i];
                    bool starting = !(i < b.PaneDrawnLastFrame.Count && b.PaneDrawnLastFrame[i]);
                    if (starting && wanted && !handover) b.PaneLevel[i] = Mathf.Max(b.PaneLevel[i], b.Level);
                    b.PaneLevel[i] = Mathf.MoveTowards(b.PaneLevel[i], wanted ? 1f : 0f, step);
                }
            }
        }
    }
}
