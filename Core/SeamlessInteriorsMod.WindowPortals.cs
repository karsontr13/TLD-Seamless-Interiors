using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── SEEING THE REAL WORLD THROUGH AN INTERIOR WINDOW ───
        //
        // A clone is parked away from its shell, so its windows look out at whatever happens to be
        // where it was parked - open sky, usually. This draws the view from the SHELL's position
        // onto a quad sitting in each window opening, so the building looks out on the place it
        // actually stands.
        //
        // "Portal" here means a window, not the mod's door transition. The door transition is also
        // called a portal throughout this codebase (PORTAL_SUPPRESS_WINDOW, NotifyPortalUsed,
        // Patches/PortalPatches.cs) and the two are unrelated. Everything here says WindowPortal.
        //
        // WHERE THE QUAD GOES is decided by FindInteriorWindowOpenings in
        // SeamlessInteriorsMod.WindowOpenings.cs: between the frost mesh's outer face and the
        // frame's outer face. That ordering is what makes the frost work with no shader and no
        // blending - the snow is opaque and nearer the player, so it occludes the view exactly
        // where it has geometry.
        //
        // ─── ONE CAMERA PER VISIBLE OPENING, AND UNITY DRIVES ALL OF THEM ───
        //
        // The correctness of a portal view comes from the PROJECTION MATRIX rather than from the
        // camera object, so in principle one camera could serve every window by being re-aimed and
        // re-rendered per opening. THAT WAS TRIED FIRST AND IT DOES NOT WORK.
        //
        // Re-rendering one camera means calling Camera.Render() by hand, and a hand-driven render
        // draws a STALE WORLD: the portal shows the lake, the ground and the distant mountains but
        // NO TREES AND NO ROCK OUTCROPS, a hill with nothing standing on it. TLD settles its
        // terrain-object and vegetation activation very late in the frame.
        //
        // On 2026-09-14 this was diagnosed as a coroutine resuming too early - after Update, before
        // LateUpdate - which made OnLateUpdate look like the cure. **It is not. Measured from
        // OnLateUpdate on the same day, in the DLL, the trees and rocks were still missing.** The
        // problem is not which callback calls Render(); it is that ANY mod-driven render runs
        // before the game has finished deciding what exists this frame.
        //
        // So Unity drives the cameras. A camera with enabled = true and a negative depth is
        // rendered by the engine at the proper point in its own pipeline, after the game has
        // settled, and the trees come back. A Unity-driven camera renders exactly once per frame,
        // which is the whole reason there is a POOL of them: one per tile, one tile per visible
        // opening. The pool is what "all the windows" costs in camera objects.
        //
        // WHAT THIS COSTS LATER: the per-view fog swap wants RenderSettings changed immediately
        // before a render and restored immediately after, which is trivial when we own the call
        // and is not available here. The replacement is Camera.onPreRender / onPostRender, which
        // fire around each camera's engine-driven render. That is a known, solved pattern, and it
        // is a smaller price than a portal with no forest in it.
        //
        // ─── A FULL-RESOLUTION TARGET PER SLOT, ALLOCATED ONLY WHEN ONE IS NEEDED ───
        //
        // A portal is only convincing at the pixel density of the screen it is drawn on. Standing
        // at a window, the opening covers a large part of the view, so anything less than the
        // game's own resolution is magnified and reads as a blurry panel rather than a hole.
        //
        // THIS WAS GOT WRONG ONCE, and the way it was wrong is worth keeping. The first version
        // packed every opening into TILES of one shared atlas, to avoid allocating a texture per
        // window. At a 2048 atlas with four tiles that gives each portal 1024 x 1024, against the
        // 3840 x 2160 the prototype had used - about eight times fewer pixels, stretched across
        // most of the screen. It looked like a compression artifact, and the owner said so
        // immediately.
        //
        // The trade was not worth making, because it was not even saving anything: four tiles of a
        // 4096 atlas cost exactly as much memory as four separate 2048 targets. Tiling divides
        // resolution; it does not divide cost.
        //
        // So each slot owns a full-resolution target, and slots are created ON DEMAND. Most of the
        // time one window is visible and exactly one target exists. The cap is what bounds the
        // worst case, in cost and in memory together, which is the honest place to bound it.
        //
        // RELATED TRAP, PAID FOR THE SAME EVENING: raising a preference's DEFAULT does not change
        // a value already written to the preferences file. The tile size default went from 1024 to
        // 2048 and every running game kept 1024, because MelonPreferences only uses a default when
        // the key is absent. Any setting whose meaning or scale changes needs a NEW KEY, not a new
        // default - which is why the resolution setting below is a scale under a new name rather
        // than the old pixel count.
        //
        // ─── WHAT BOUNDS THE COST ───
        //
        // One VISIBLE opening is one full scene render per frame. Not one existing opening: the
        // openings behind the player, facing away, or off screen cost a few dot products each. A
        // room with eight windows typically has one or two in view.
        //
        // Both knobs that matter are preferences, so the cost can be measured against them rather
        // than guessed at: MaxWindowPortals caps how many render at once, and WindowPortalTileSize
        // trades sharpness for fill rate.

        // Quads live on a layer the portal camera does not draw, so a window can never render
        // another window. Layer 7 is unused by the game and the main camera already draws it.
        // NOTE: only objects this mod creates are ever moved onto it. The clone's own renderers
        // are deliberately left on their real layers - TLD uses layers for interaction raycasts
        // and collision, so relayering 592 interior objects to hide them from the portal camera
        // would risk breaking item pickup and containers to fix a rendering detail.
        private const int WINDOW_PORTAL_QUAD_LAYER = 7;

        // Minimum eye-to-opening distance before a portal is drawn. Below this the off-axis
        // frustum degenerates and the near plane swallows the view.
        private const float WINDOW_PORTAL_MIN_EYE_DISTANCE = 0.05f;

        // HOW FAR OUTSIDE THE SHELL'S GLASS THE VIEW OUT STARTS. With the shell kept on for a parked
        // building (see KeepParkedShellOn), a view starting AT the shell's glass takes in the back
        // of the shell's own window frame and sash bar. Those are modeled only to be seen from the
        // yard, so from indoors they are a hollow outline sitting a little off the interior's real
        // frame; the owner saw it at once: "a window alignment problem similar to the one we had
        // with the exterior." Settled by the owner with a live slider on 2026-09-18, all eight Camp
        // Office windows, at 4 cm and nothing else moved. That is the shell frame's own outer face
        // (4.0 cm proud of the glass; the bar is 3.3), so the view starts where the shell's window
        // ends and keeps the porch, the posts and the roof. The room already has a whole frame of
        // its own, so nothing is lost.
        //
        // THAT 4 cm WAS A STATEMENT ABOUT THE CAMP OFFICE'S FRAME, and it is now MEASURED PER WINDOW;
        // this constant is only the fallback for a window whose shell cannot be measured. Owner, in
        // Lake Cabin A_2, 2026-09-18: "once again we can see part of the outer frame through the
        // window ... This will be the third time we've had to fix this." It was the third value
        // tuned by eye at the Camp Office that turned out to describe that building's window and
        // not windows (the drift's depth and the drift's width were the first two). Lake Cabin A's
        // outer casing stands 6.7 cm proud of its glass, so a view starting at 4 cm took in the
        // inner side of 2.7 cm of casing. See MeasureShellFrameRelief.
        private const float WINDOW_PORTAL_VIEW_STARTS_OUTSIDE_SHELL_GLASS = 0.04f;

        // Started this far BEFORE the frame's own outer face (negative = before), so that face is
        // never in the plane. It was 1 mm AFTER, until a closed shutter: Lake Cabin A's casing stands
        // 66.7 mm proud of the glass and a shutter closed flush over the window has its inward face
        // at 67.8, so a view starting at 67.7 had that face 0.1 mm in front of it. At X 1674 a float
        // cannot hold 0.1 mm, the face fell either side of the cut from frame to frame, and the
        // owner saw the shutter "blinking in and out of existence"; the open shutter's hinge corner,
        // at 67.3, was cut away outright. There is 1.1 mm between a casing and what is hung on it,
        // which is no room for a plane, so the plane goes on the near side of the casing's face
        // instead. What that lets into the view is half a millimeter of the casing's side faces,
        // seen edge on: under a pixel. Tried live at A_3 first, 2026-09-18; owner: "That fixed it."
        private const float WINDOW_PORTAL_VIEW_CLEARS_FRAME_BY = -0.0005f;

        // ...BUT PAST THE CASING'S OWN FACE WHERE NOTHING IS HUNG ON IT. Half a millimeter before the
        // casing's face lets in that half millimeter of its side faces, which was reckoned "under a
        // pixel" and is not: the owner, inside the Camp Office on 2026-09-19, "I DID notice a
        // hairline frame showing through... I think we need a mm adjustment." With a live slider on
        // all eight windows 0.5 mm further out (the plane ON the face) cleared it, and he set 1.0
        // "to be safe", which is this half millimeter PAST the face. The casing's face looks
        // outward, so the view never draws it, and its sides are then wholly behind the plane.
        //
        // The shutter that put the plane before the face in the first place still rules where there
        // is one: if anything of the building stands within WINDOW_PORTAL_VIEW_LOOKS_BEYOND_FRAME of
        // the face, beyond it (Lake Cabin A: an open shutter's hinge corner at 0.6 mm, a closed
        // shutter's inward face at 1.1), the plane goes HALFWAY between the face and that, so it
        // is past the casing and still short of what hangs on it. A cover nearer than the casing's
        // face (Lake Cabin C's closed shutter) is handled as before: the view starts before it.
        private const float WINDOW_PORTAL_VIEW_PAST_FRAME_BY = 0.0005f;
        private const float WINDOW_PORTAL_VIEW_LOOKS_BEYOND_FRAME = 0.005f;
        private const float WINDOW_PORTAL_VIEW_LOOKS_BESIDE_PANE = 0.6f;

        // The nearest corner of anything of the shell that stands just beyond the casing's face,
        // in front of the window or beside it; -1 when nothing does.
        private static float MeasureShellNextBeyondFrame(Vector3[] world, int[] triangles, Vector3 center, Vector3 outward, float width, float height, float relief)
        {
            if (world == null || triangles == null) return -1f;

            Vector3 right = Vector3.Cross(Vector3.up, -outward).normalized;
            float halfWidth = width * 0.5f + WINDOW_PORTAL_VIEW_LOOKS_BESIDE_PANE, halfHeight = height * 0.5f + 0.2f;
            float from = relief + 0.0001f, to = relief + WINDOW_PORTAL_VIEW_LOOKS_BEYOND_FRAME;
            float nearest = float.MaxValue;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 p0 = world[triangles[i]] - center, p1 = world[triangles[i + 1]] - center, p2 = world[triangles[i + 2]] - center;
                Vector3 middle = (p0 + p1 + p2) / 3f;
                if (Mathf.Abs(Vector3.Dot(middle, right)) > halfWidth || Mathf.Abs(Vector3.Dot(middle, Vector3.up)) > halfHeight) continue;
                for (int c = 0; c < 3; c++)
                {
                    float proud = Vector3.Dot(c == 0 ? p0 : c == 1 ? p1 : p2, outward);
                    if (proud > from && proud <= to && proud < nearest) nearest = proud;
                }
            }
            return nearest < float.MaxValue ? nearest : -1f;
        }

        // How far outside the shell's glass each opening's view out starts, by opening.
        private static readonly List<float> s_WindowPortalViewStart = new List<float>();

        // ─── HOW FAR A SHELL WINDOW'S FRAME STANDS PROUD OF ITS GLASS ───
        //
        // The proudest outward-facing face of the shell's mesh within 20 cm of the window's glass
        // and no more than 15 cm out from it: the frame boards and the casing, not the porch.
        //
        // THE CONTROL, run read-only in the console against the live game before this was written:
        // all eight Camp Office windows measured 3.9 to 4.0 cm, which is the 4 cm the owner had
        // settled by eye with a slider. Lake Cabins A_2, E and F measured 6.7 cm on every large
        // window and 5.6 on the small ones.
        //
        // A FACE ON ITS OWN IS NOT A FRAME. One of A_2's windows also had three stray faces at 9 and
        // 14.6 cm, something standing near the window that is no part of it. A frame is many faces
        // at one depth, so a depth counts only if at least four faces share its centimeter.
        private const float SHELL_FRAME_SEARCH_SIDE = 0.20f;
        private const float SHELL_FRAME_SEARCH_DEPTH = 0.15f;
        private const int SHELL_FRAME_MIN_FACES = 4;

        private static float MeasureShellFrameRelief(Vector3[] world, int[] triangles, Vector3 center, Vector3 outward, float width, float height)
        {
            if (world == null || triangles == null) return -1f;

            Vector3 right = Vector3.Cross(Vector3.up, -outward).normalized;
            float halfWidth = width * 0.5f + SHELL_FRAME_SEARCH_SIDE;
            float halfHeight = height * 0.5f + SHELL_FRAME_SEARCH_SIDE;

            var facesAtCm = new int[16];
            var proudestAtCm = new float[16];

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 p0 = world[triangles[i]], p1 = world[triangles[i + 1]], p2 = world[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                if (normal.sqrMagnitude < 1e-10f) continue;
                if (Vector3.Dot(normal.normalized, outward) < 0.7f) continue;      // faces looking out of the building

                Vector3 fromGlass = (p0 + p1 + p2) / 3f - center;
                if (Mathf.Abs(Vector3.Dot(fromGlass, right)) > halfWidth) continue;
                if (Mathf.Abs(Vector3.Dot(fromGlass, Vector3.up)) > halfHeight) continue;

                float proud = Vector3.Dot(fromGlass, outward);
                if (proud < 0.003f || proud > SHELL_FRAME_SEARCH_DEPTH) continue;

                int cm = Mathf.Min((int)(proud * 100f), facesAtCm.Length - 1);
                facesAtCm[cm]++;
                if (proud > proudestAtCm[cm]) proudestAtCm[cm] = proud;
            }

            for (int cm = facesAtCm.Length - 1; cm >= 0; cm--)
                if (facesAtCm[cm] >= SHELL_FRAME_MIN_FACES) return proudestAtCm[cm];
            return -1f;
        }

        // ─── WHAT IS CLOSED OVER THE PANE MAY STAND NEARER THAN THE FRAME'S FACE ───
        //
        // Owner, in Lake Cabin C, 2026-09-18: "Missing the flush shutter like the other lake cabin."
        // On Lake Cabin A's kind a shutter closed over the window has its inward face about 0.6 mm PAST
        // the casing's face (67.2 to 67.3 against 66.6 to 66.8), so a view starting just before the casing
        // takes it in. Lake Cabin C's closed leaf sits 3.6 mm INSIDE its casing (63.1 against 66.7),
        // the view started within the leaf's own thickness, and a slab seen from inside itself is
        // not drawn at all; its other leaf hangs ajar, further out, and showed.
        //
        // So the view starts before whichever comes first: the frame's face, or the nearest face
        // looking INWARD over the pane. Looked for over the middle nine tenths of the pane only
        // (the casing's own back stands round the pane, not over it) and no nearer than 3.5 cm,
        // which clears the sash bar (3.3 at the Camp Office); the shell's bars are modeled without
        // backs in any case. Dry-run read-only over all 67 LakeRegion windows before it was built:
        // it found the five closed shutters and nothing else, and only Lake Cabin C's was nearer
        // than its frame. Tried live there first by hand; owner: "It looks right."
        //
        // Returns -1 when nothing is closed over the pane.
        private const float SHELL_COVER_SEARCH_FROM = 0.035f;
        private const float SHELL_COVER_PANE_SHARE = 0.45f;

        private static float MeasureShellCoverInwardFace(Vector3[] world, int[] triangles, Vector3 center, Vector3 outward, float width, float height, float noFurtherThan)
        {
            if (world == null || triangles == null) return -1f;

            Vector3 right = Vector3.Cross(Vector3.up, -outward).normalized;
            float halfWidth = width * SHELL_COVER_PANE_SHARE;
            float halfHeight = height * SHELL_COVER_PANE_SHARE;
            float nearest = float.MaxValue;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 p0 = world[triangles[i]], p1 = world[triangles[i + 1]], p2 = world[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                if (normal.sqrMagnitude < 1e-10f) continue;
                if (Vector3.Dot(normal.normalized, outward) > -0.7f) continue;     // faces looking INTO the building

                Vector3 fromGlass = (p0 + p1 + p2) / 3f - center;
                if (Mathf.Abs(Vector3.Dot(fromGlass, right)) > halfWidth) continue;
                if (Mathf.Abs(Vector3.Dot(fromGlass, Vector3.up)) > halfHeight) continue;

                float proud = Mathf.Min(Vector3.Dot(p0 - center, outward),
                              Mathf.Min(Vector3.Dot(p1 - center, outward), Vector3.Dot(p2 - center, outward)));
                if (proud < SHELL_COVER_SEARCH_FROM || proud > noFurtherThan) continue;
                if (proud < nearest) nearest = proud;
            }

            return nearest < float.MaxValue ? nearest : -1f;
        }

        // THE FAR PLANE IS THE PLAYER'S, READ EVERY FRAME. Set to a fixed 15000 m on 2026-09-14 to
        // match the main camera, because a shorter one clipped distant terrain; but the game shortens
        // the player's far plane in bad weather (445 m in a blizzard, 2026-09-28), and a fixed 15000
        // drew what the player never sees there: distant trees and rocks drawn by the game's instanced
        // renderer came out BLACK through the fog in a window view (Camp Office window 7), because
        // they are made for the player's deferred camera, whose fog pass never reaches them past its
        // far plane. With the player's far plane in the console, owner: "That fixed it." The wall
        // cameras already took main.farClipPlane (WallCameras.cs).

        // The quad must draw AFTER TLD's alpha-tested foliage (2450) and BEFORE transparent
        // (3000). Unlit/Texture defaults to Geometry (2000), and on a deferred camera an unlit
        // forward shader does not reliably occlude what is drawn later - so every snow-laden tree
        // painted itself over the portal, producing a faint second copy of the world offset by the
        // park height. 2600 also leaves the window glass (3000) and the frost drawing in front,
        // which is exactly what the slot design needs.
        private const int WINDOW_PORTAL_RENDER_QUEUE = 2600;

        // THE VIEWS OUT OF A ROOM DRAW AFTER THE LIGHT ABSORBERS. `_LongDark/TLD_LightAbsorber`
        // volumes (80 in AirfieldRegion, queue 3000) drawn after a view at 2600 darken it with the
        // trees of the PLAYER's world, 40 m up: faint swaying "ghost trees" over the snow in every
        // window (2026-09-22). Proven by moving the quad (3600 and 3001 clean, 2601 ghost) and by
        // hiding all 80 absorbers (gone) and showing them again (back); written up in TLD-Knowledge,
        // Light-Absorber-Volumes-Darken-Portal-Views.md. So these views draw at 3001, and the mod's
        // frost after them at 3002 (at 3001 the frost vanished behind the view, as predicted).
        // THE COST, accepted by the owner: anything transparent standing in the room in front of a
        // window, the game's own glass included, now draws BEHIND the view. The views INTO a room
        // (OutsideInPortals.cs) keep 2600: this was only tested on the views out.
        private const int WINDOW_VIEW_OUT_RENDER_QUEUE = 3001;
        private const int WINDOW_VIEW_OUT_FROST_QUEUE = 3002;

        // ─── THE VIEWS' RENDER TARGETS OUTLIVE A VISIT BY TWO MINUTES ───
        //
        // Measured 2026-09-22 with a per-frame probe at the hangar: the first frame with the window
        // cameras on took about 350 ms at 10 cameras and about 40 ms at 1, about 35 ms a camera, and
        // every entry paid it again, because leaving released every target and the GPU allocates a
        // target only when it is first drawn into (creating 10 of them costs 0 ms on the CPU). That
        // slow frame held the first frame inside on screen: the owner's "split second" before the
        // views appear. So leaving now keeps the targets and the next visit takes them back; they are
        // released for real after WINDOW_TARGETS_KEPT_SECONDS away. The first visit of a session
        // still pays; creating them during the approach is the fuller fix and belongs with the wall
        // cameras (see the 2026-09-22 handoff). NOT YET MEASURED IN THE BUILD.
        private const float WINDOW_TARGETS_KEPT_SECONDS = 120f;
        private static RenderTexture[] s_KeptWindowTargets;
        private static float s_KeptWindowTargetsSince;

        internal static void TickKeptWindowTargets()
        {
            if (s_KeptWindowTargets == null) return;
            if (Time.realtimeSinceStartup - s_KeptWindowTargetsSince < WINDOW_TARGETS_KEPT_SECONDS) return;
            ReleaseKeptWindowTargets();
        }

        private static void ReleaseKeptWindowTargets()
        {
            if (s_KeptWindowTargets == null) return;
            foreach (var target in s_KeptWindowTargets)
            {
                if (target == null) continue;
                target.Release();
                Object.Destroy(target);
            }
            s_KeptWindowTargets = null;
        }

        // Smallest render target edge. Below this a portal costs nothing worth saving and starts
        // to shimmer as it resizes.
        private const int WINDOW_PORTAL_MIN_TARGET = 128;

        // How far past the visible edge of an opening to render, as a fraction of its size.
        private const float WINDOW_PORTAL_SUBRECT_MARGIN = 0.03f;
        // One centimetre. Absolute on purpose: a fraction of the opening is what went wrong.
        private const float WINDOW_PORTAL_SUBRECT_MARGIN_FLOOR = 0.01f;

        // Largest interior-to-exterior pane separation still treated as a pairing. Measured
        // mismatches on the Camp Office run to 2.28 m; beyond a few metres it is not the same
        // window and aiming at it would be worse than falling back to the building offset.
        private const float WINDOW_PORTAL_PAIR_MAX_DISTANCE = 4f;

        // HOW MUCH A CHALLENGER MUST BEAT THE INCUMBENT BY TO TAKE ITS SLOT.
        //
        // Priority is solid angle - area over eye distance squared - which is a fine ranking and a
        // terrible tie-break. Two identical panes side by side on the same wall sit at very nearly
        // the same distance, so their priorities agree to four decimal places, and which one wins
        // the last slot is then decided by nothing but list order. Measured live in the Camp
        // Office on 2026-09-15: openings 4 and 5 both at 0.0116, the loser falling back to the
        // painted plate. Shifting weight by a few centimetres flipped which was marginally nearer
        // and the pair swapped the slot back and forth - a window visibly popping between the real
        // view and a static image while the player stood still.
        //
        // Raising the limit hides this whenever there are slots to spare; it does not fix it. In
        // any building where the cap actually binds the tie comes straight back. So an opening
        // already drawing keeps its slot unless a rival beats it by this margin, which makes the
        // assignment stable under the sub-centimetre jitter of simply standing there.
        private const float WINDOW_PORTAL_HYSTERESIS = 1.25f;

        // A SLOT is one camera, its own full-resolution target, and the material that reads it.
        // Slots are created on demand and reused; a building where one window is ever visible
        // allocates exactly one. Which OPENING occupies which slot changes from frame to frame,
        // which is why the material is per slot and gets handed to whichever quad is using it.
        //
        // Separate targets rather than one shared atlas, and that is not a preference. Cameras
        // sharing a render texture CLEAR THE WHOLE THING, not their own viewport rect, at least
        // under deferred rendering. Measured 2026-09-14 with three windows on screen: three quads
        // drawing, three cameras enabled, and only the last one to render had any content - the
        // other two panes were flat skybox blue, because its clear had wiped them.
        private static Camera[] s_WindowPortalCams;
        private static RenderTexture[] s_WindowPortalSlotTargets;
        private static Material[] s_WindowPortalSlotMaterials;
        private static int s_WindowPortalSlotCount;

        private static string s_WindowPortalInstanceId = null;
        // The row's RoomGlassKeepsOwnMaterial for the building set up now: its glass is not frosted.
        private static bool s_WindowPortalKeepsOwnGlass;
        private static List<InteriorWindowOpening> s_WindowPortalOpenings = new List<InteriorWindowOpening>();
        private static List<GameObject> s_WindowPortalQuads = new List<GameObject>();
        private static List<int> s_WindowPortalQuadTile = new List<int>();

        // Backdrops this mod switched off, with what they were, so they go back exactly as found.
        private static List<Renderer> s_WindowPortalHiddenBackdrops = new List<Renderer>();

        // And, in a parked room, the backdrops this mod switched ON: renderers it enabled and objects
        // it activated, each put back exactly as found at teardown. See ShowPaintedPanesInParkedRoom.
        private static readonly List<Renderer> s_WindowPortalShownBackdrops = new List<Renderer>();
        private static readonly List<GameObject> s_WindowPortalActivatedBackdrops = new List<GameObject>();

        // Which panes have been cleared of glass and frost, by opening index. PER WINDOW on
        // purpose: the player-facing version of this is a per-pane action, so the state it drives
        // is per-pane from the start rather than a building-wide flag retrofitted later.
        private static List<bool> s_WindowPaneCleared = new List<bool>();

        // For each interior opening, the index of the shell window it paired with, or -1. That index
        // is the window's identity in the shared cleaned state (see WindowFrostState.cs).
        private static readonly List<int> s_WindowPortalSpot = new List<int>();

        // ─── FROST ON THE GLASS, SEEN FROM INSIDE ───
        //
        // A pane that is NOT cleared shows its glass and, if the lever says so, its snow slab. The
        // glass is given a material of the mod's own, because the one it ships with draws nothing:
        // Shader Forge/TLD_StandardTransparent was measured on 2026-09-18 contributing no frost and
        // no tint on the shell's panes, where the same texture on Legacy Shaders/Transparent/Diffuse
        // drew the game's painted frost at once (see OutsideInPortals.cs). Whether the game's shader
        // also draws nothing from INSIDE has not been measured; this does not depend on the answer.
        //
        // Per pane on purpose. SetWindowPaneCleared is the call a future clean-the-window
        // interaction will make, and the owner's requirement is that a cleaned window is clean from
        // both sides, so the outside-in frost has to come to read this same per-window state.
        //
        // FULL STRENGTH, WHERE THE OUTSIDE PANE WANTS HALF. Set by eye by the owner on 2026-09-18:
        // 0.5 looked right from the porch and 1.0 was needed from inside, with the same texture
        // and the same shader on both. Unmeasured, but the likely reason is contrast rather than
        // the material: from outside the frost is sunlit against a dark room, from inside it is
        // pale against a bright snowfield. Keep the two dials separate.
        // Backed by a preference so a value dialed in from the tuning panel survives the session.
        // A plain static field went back to this default at every launch.
        private static MelonPreferences_Entry<float> s_PrefInsideOutFrostStrength;
        public static float InsideOutFrostStrength
        {
            get { return s_PrefInsideOutFrostStrength != null ? s_PrefInsideOutFrostStrength.Value : 1f; }
            set { if (s_PrefInsideOutFrostStrength != null) s_PrefInsideOutFrostStrength.Value = value; }
        }

        // The 3D snow slab sitting in the outer frame. Opaque, so it hides the view where it has
        // geometry. On is how the base game looks.
        // Backed by a preference so a value dialed in from the tuning panel survives the session.
        // A plain static field went back to this default at every launch.
        private static MelonPreferences_Entry<bool> s_PrefInsideOutShowSnowSlab;
        public static bool InsideOutShowSnowSlab
        {
            get { return s_PrefInsideOutShowSnowSlab != null ? s_PrefInsideOutShowSnowSlab.Value : true; }
            set { if (s_PrefInsideOutShowSnowSlab != null) s_PrefInsideOutShowSnowSlab.Value = value; }
        }

        private static Material s_WindowPortalFrostMaterial;
        private static bool s_WindowPortalSnowSlabApplied = true;
        private static readonly List<Renderer> s_WindowPortalFrostedGlass = new List<Renderer>();
        private static readonly List<Material[]> s_WindowPortalFrostedGlassSaved = new List<Material[]>();

        // ─── THE INTERIOR'S WINDOWS ARE NOT WHERE THE SHELL'S WINDOWS ARE ───
        //
        // This, not parking, is the reason a window needs a portal at all. The interior prefab is
        // physically bigger than the shell it stands in - the Camp Office interior reaches 0.8 m
        // past it on X-, 1.2 m on X+ and 1.7 m on Z- - and the config scales it by a non-uniform
        // (1.05, 0.98, 1.05) on top of that. So an interior pane and the exterior pane it
        // represents are in different places, and no amount of un-parking would line them up.
        //
        // Measured by the previous session: one upstairs pane sits at interior local (7.15, 5.71,
        // 0.40) against exterior local (4.871, 6.339, 0.396). Z agrees to 4 mm, X is out by
        // 2.28 m. The other upstairs pane is out by 1.21 m. A single building-wide translation
        // cannot serve both, which is why this is per opening.
        //
        // The first working portal used one rigid building delta and was called "plausible". It is
        // not correct: it renders every window from a viewpoint up to two metres from where that
        // window actually is, which is a parallax error that grows as the player moves.
        //
        // The exterior half is the author's own s_ShellWindows table in ExteriorFireEffects.cs,
        // measured offline from each shell mesh's STR_WindowGlassMix submesh because shell meshes
        // are not readable at runtime. 56 panes across the supported shells. It is reused here
        // rather than re-derived.
        private static List<Vector3> s_WindowPortalDelta = new List<Vector3>();
        private static int s_WindowPortalPairedCount;

        // Scratch, reused every frame so the per-frame path allocates nothing.
        private static readonly List<int> s_WindowPortalVisible = new List<int>();
        private static readonly List<float> s_WindowPortalPriority = new List<float>();

        // Which openings held a slot last frame, so an incumbent can be given its margin. One
        // entry per opening, not per slot: an opening keeps its claim across a change of slot.
        private static readonly List<bool> s_WindowPortalWasDrawn = new List<bool>();
        private static readonly Vector4[] s_WindowPortalPlanes = new Vector4[5];
        private static readonly List<Vector3> s_WindowPortalPoly = new List<Vector3>(16);
        private static readonly List<Vector3> s_WindowPortalPolyB = new List<Vector3>(16);

        // ─── THE PER-FRAME ENTRY POINT ───

        public static void TickWindowPortals()
        {
            if (s_PrefEnableWindowPortals == null || !s_PrefEnableWindowPortals.Value)
            {
                if (s_WindowPortalInstanceId != null) TeardownWindowPortals();
                return;
            }

            SeamlessInteriorInstance instance = s_IsPlayerInsideClone ? ResolveOccupiedInstance() : null;

            if (instance == null || instance.MasterInterior == null || instance.ExteriorShell == null)
            {
                if (s_WindowPortalInstanceId != null) TeardownWindowPortals();
                return;
            }

            string id = instance.Config.ResolvedInstanceId;
            // WindowWallCameras switched while inside: rebuilt next frame the other way, so the two
            // can be compared standing still (see WallCameras.cs).
            if (s_WindowPortalInstanceId == id && s_WindowPortalCams != null && s_WallCamerasActive != WallCamerasWanted(instance))
            {
                TeardownWindowPortals();
                return;
            }
            if (s_WindowPortalInstanceId != id)
            {
                TeardownWindowPortals();
                if (!SetupWindowPortals(instance, id)) return;
            }

            // A ROOM WITH NO WINDOWS THE MOD CAN READ HAS NOTHING TO DRAW. SetupWindowPortals remembers
            // such a room by its id so it is not scanned again every frame, which also meant every
            // later frame came straight here with no cameras made, and RenderWindowPortals threw on
            // the list that was never built: one exception a frame, 674 in the 11 seconds the owner
            // stood in the Coastal Highway gas station on 2026-09-19, whose glass is a material the
            // room finder does not know.
            if (s_WindowPortalCams == null || s_WindowPortalOpenings.Count == 0) return;

            Camera main = GameManager.GetMainCamera();
            // NOT UnityEngine.Camera.main. TLD's FPSCamera carries the MainCamera tag but its
            // Camera component is DISABLED - the game renders it by hand - and Camera.main only
            // ever returns enabled cameras. Its transform and matrices are valid while disabled.
            if (main == null) return;

            // NOTE: everything positional here - the openings, the quads' world-space vertices,
            // and each opening's paired delta - is captured at setup and assumes the clone does
            // not move afterwards. That has always been true of the quads; it is written down here
            // because a clone re-placed at runtime would strand all three together.
            KeepParkedShellOn(instance);
            FollowWindowFrostLevers();
            TickGlassSnow();   // one window a frame, see WindowSnow.BuildingToRoom.cs
            // Wall cameras are placed from the postfix on vp_FPSCamera.LateUpdate, after the zoom.
            if (s_WallCamerasActive) return;
            RenderWindowPortals(main);
        }

        // ─── THE SHELL STAYS ON WHILE THE PLAYER IS INSIDE A PARKED BUILDING ───
        //
        // The author switches a building's whole exterior shell off the moment the player steps in,
        // and in their design that is right: the interior stands INSIDE its shell, which would wrap
        // round the room. It also removes the porch, the posts, the roof snow, the icicles and the
        // building's own shadow from every camera, the window cameras included, so looking out of a
        // window showed the yard with no building in it (owner, 2026-09-18: "Can we make it so we
        // can see the porch from the inside-out windows?", and, of the cause, "that's probably
        // because we weren't using parking before").
        //
        // A PARKED interior stands ParkOffset away from its shell, so the two cannot touch and the
        // shell can stay. The window cameras start their view just past the shell's window, so they
        // never see its walls, only what stands beyond them.
        //
        // Re-asserted every frame against the object's real state, for the reason the snow slab is:
        // the mod switches the shell off from half a dozen places. Leaving is the mod's own
        // business; every path out switches the shell on.
        private static void KeepParkedShellOn(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.ExteriorShell == null || instance.Config == null) return;
            if (instance.Config.ParkOffset == Vector3.zero) return;
            if (!instance.ExteriorShell.activeSelf) instance.ExteriorShell.SetActive(true);
        }

        // ─── CHOOSING WHICH OPENINGS RENDER, AND DRAWING THEM ───

        private static void RenderWindowPortals(Camera main)
        {
            RestoreParkedRoomShadow();      // the backstop; see ParkedRoomShadow.cs
            RestoreWindowViewAmbient();
            Vector3 eye = main.transform.position;

            // Only when the late capture is not running: the tick sees the camera BEFORE the zoom
            // is applied, so building here would overwrite a correct frustum with a wrong one.
            if (!s_FrustumCapturedLate) BuildWindowPortalFrustum(main);

            s_WindowPortalVisible.Clear();
            s_WindowPortalPriority.Clear();

            for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
            {
                var opening = s_WindowPortalOpenings[i];

                Vector3 corner = opening.PortalCenter
                               - opening.Right * (opening.Width * 0.5f)
                               - opening.Up * (opening.Height * 0.5f);

                // Is the eye on the inside of this window? Outward points out of the building, so
                // a positive distance from eye to the opening plane means we are looking out of
                // it. This also rejects the windows behind the player, which is most of them.
                float eyeDistance = Vector3.Dot(corner - eye, opening.Outward);
                if (eyeDistance <= WINDOW_PORTAL_MIN_EYE_DISTANCE) continue;

                if (!IsWindowOpeningOnScreen(opening, corner)) continue;

                // The pane's apparent size: its area, foreshortened by the angle it is seen at, over
                // the square of the straight-line distance to it. Only used to rank openings when
                // more are visible than there are slots. It was area over the distance to the wall's
                // PLANE, which is the same number for every window on one wall: at the hangar's side,
                // 3 m from the wall, a 5.3 x 1.8 m window 33 m away outranked the 1.65 x 0.9 m one
                // 4.8 m away that the owner stood beside (2026-09-21, "nearest windows should get
                // priority rendering").
                float straight = Mathf.Max((opening.PortalCenter - eye).magnitude, 0.1f);
                float priority = (opening.Width * opening.Height) * (eyeDistance / straight) / (straight * straight);

                // An opening already drawing defends its slot. Without this, two panes of equal
                // size on one wall tie to four decimal places and swap the last slot on every
                // sub-centimetre wobble.
                if (i < s_WindowPortalWasDrawn.Count && s_WindowPortalWasDrawn[i])
                    priority *= WINDOW_PORTAL_HYSTERESIS;

                s_WindowPortalVisible.Add(i);
                s_WindowPortalPriority.Add(priority);
            }

            int drawCount = Mathf.Min(s_WindowPortalVisible.Count, s_WindowPortalSlotCount);

            // Partial selection sort: only the first drawCount entries need to be in order, and
            // the list is at most the number of windows in one building.
            for (int a = 0; a < drawCount; a++)
            {
                int best = a;
                for (int b = a + 1; b < s_WindowPortalVisible.Count; b++)
                    if (s_WindowPortalPriority[b] > s_WindowPortalPriority[best]) best = b;
                if (best == a) continue;

                int swapIndex = s_WindowPortalVisible[a];
                s_WindowPortalVisible[a] = s_WindowPortalVisible[best];
                s_WindowPortalVisible[best] = swapIndex;

                float swapPriority = s_WindowPortalPriority[a];
                s_WindowPortalPriority[a] = s_WindowPortalPriority[best];
                s_WindowPortalPriority[best] = swapPriority;
            }

            // Everything not chosen is switched off first, so an opening that loses its tile this
            // frame cannot keep drawing another window's image.
            for (int i = 0; i < s_WindowPortalQuads.Count; i++)
            {
                bool chosen = false;
                for (int k = 0; k < drawCount; k++)
                    if (s_WindowPortalVisible[k] == i) { chosen = true; break; }

                if (!chosen && s_WindowPortalQuads[i] != null)
                {
                    var renderer = s_WindowPortalQuads[i].GetComponent<MeshRenderer>();
                    if (renderer != null) renderer.enabled = false;
                }
            }

            // Cameras with no opening this frame must stop rendering, or they keep drawing a full
            // scene into their tile at whatever projection they were last left with - the cost of
            // a portal that is not on screen, plus a stale image waiting to be shown.
            for (int k = drawCount; k < s_WindowPortalCams.Length; k++)
                if (s_WindowPortalCams[k] != null) s_WindowPortalCams[k].enabled = false;

            for (int k = 0; k < drawCount; k++)
                DrawOneWindowPortal(s_WindowPortalVisible[k], k, main, eye);

            // Record incumbency for next frame's tie-break. Written from the CHOSEN set rather
            // than from each quad's renderer, because DrawOneWindowPortal can return early - an
            // opening clipped to nothing, or a slot whose target could not be built - and such an
            // opening won its slot fairly and should keep defending it rather than silently
            // surrendering its claim to whichever rival happens to be a rounding error ahead.
            for (int i = 0; i < s_WindowPortalWasDrawn.Count; i++) s_WindowPortalWasDrawn[i] = false;
            for (int k = 0; k < drawCount; k++)
            {
                int index = s_WindowPortalVisible[k];
                if (index >= 0 && index < s_WindowPortalWasDrawn.Count) s_WindowPortalWasDrawn[index] = true;
            }
        }

        private static void DrawOneWindowPortal(int openingIndex, int slot, Camera main, Vector3 eye)
        {
            var opening = s_WindowPortalOpenings[openingIndex];
            GameObject quad = s_WindowPortalQuads[openingIndex];
            if (quad == null) return;

            // The quads used to be switched off on a load made inside. The cause was this mod's own
            // scenery-hiding list taking them for outdoor objects; it is fixed where the list is
            // built (see IsOwnPortalObject in Environment.cs), so nothing is re-asserted here.

            Vector3 right = opening.Right;
            Vector3 up = opening.Up;
            Vector3 outward = opening.Outward;

            // ONLY THE PART OF THE OPENING THAT IS ON SCREEN.
            //
            // The frustum through a window is already the narrowest it can be in ANGLE - it is the
            // cone through the aperture and nothing else. What was still being wasted is the part
            // of that cone hanging off the edge of the screen, which gets fully shaded and then
            // thrown away. Half a window off screen is half a scene render burnt.
            //
            // This was implemented in the prototype and then switched off, because the clipped
            // rectangle under-covered by about 19% and left a strip of the real elevated view
            // showing through. That was blamed on the main camera's matrices being stale when read
            // mid-frame. THAT DIAGNOSIS WAS WRONG: measured 2026-09-14, FPSCamera reports an
            // aspect of 1.778 against a 3840x2160 screen and a sane FOV, and the clip's verdict
            // matches what is actually on screen opening for opening. The matrices are fine.
            //
            // A margin is still kept, because being slightly too generous costs a few pixels of
            // shading while being slightly too tight is a visible seam of the wrong world.
            float u0, u1, v0, v1;
            if (!TryComputeVisibleSubRect(opening, eye, out u0, out u1, out v0, out v1)) return;

            Vector3 pa = opening.PortalCenter
                       - right * (opening.Width * 0.5f)
                       - up * (opening.Height * 0.5f);

            Vector3 qa = pa + right * u0 + up * v0;
            Vector3 qb = pa + right * u1 + up * v0;
            Vector3 qc = pa + right * u0 + up * v1;
            Vector3 qd = pa + right * u1 + up * v1;

            Vector3 va = qa - eye;
            Vector3 vb = qb - eye;
            Vector3 vc = qc - eye;

            float near = Vector3.Dot(va, outward);
            if (near <= WINDOW_PORTAL_MIN_EYE_DISTANCE) return;

            // Kooima off-axis frustum. The near plane IS the opening plane, so nothing between the
            // player and the window is drawn and the view lines up with the hole exactly.
            float left = Vector3.Dot(right, va);
            float rightExtent = Vector3.Dot(right, vb);
            float bottom = Vector3.Dot(up, va);
            float top = Vector3.Dot(up, vc);

            int targetWidth, targetHeight;
            ComputeWindowPortalPixelSize(qa, qb, qc, main, out targetWidth, out targetHeight);
            if (!EnsureWindowPortalSlot(slot, main, targetWidth, targetHeight)) return;
            Camera cam = s_WindowPortalCams[slot];
            UseWindowViewFogPass(cam, slot, main);   // see WindowViewFog.cs
            SharePlayerBuffersWith(cam, slot, main);   // see WindowViewFog.cs

            // The quad covers the sub-rectangle, and the target covers exactly the same cone, so
            // the UVs stay 0..1 and only the corners move.
            var filter = quad.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                // A window that is not a rectangle shows the picture only inside its glass's outline.
                WriteViewSurface(filter.sharedMesh, pa, right, up, u0, u1, v0, v1, opening.Outline);

            // The virtual eye is placed so that the player stands in the same relation to the
            // EXTERIOR pane as they do to the interior one they are looking through. That is what
            // the per-opening delta carries, and it is why this is not one building-wide offset:
            // the interior is wider than its shell, so the two panes are metres apart on some
            // windows and a single translation would render most of them from the wrong place.
            //
            // The window travels with the eye, so the eye-to-corner vectors above are unchanged.
            float viewStart = openingIndex < s_WindowPortalViewStart.Count
                            ? s_WindowPortalViewStart[openingIndex]
                            : WINDOW_PORTAL_VIEW_STARTS_OUTSIDE_SHELL_GLASS;
            cam.transform.position = eye + s_WindowPortalDelta[openingIndex] + outward * viewStart;
            cam.transform.rotation = Quaternion.LookRotation(outward, up);
            cam.nearClipPlane = near;
            cam.farClipPlane = main.farClipPlane;

            // FIELD OF VIEW MUST AGREE WITH THE PROJECTION, AND IT IS NOT DECORATIVE.
            //
            // Unity computes LOD selection and LODGroup culling from Camera.fieldOfView, NOT from
            // a custom projectionMatrix. Left at the default 60 while the real frustum through a
            // window is a few degrees, the camera believes everything in front of it is roughly
            // ten times smaller than it is. Every object drops several LOD levels, and anything
            // past its last LOD is culled outright.
            //
            // Found 2026-09-14 from a wolf seen through a window with a zoom mod: visible all the
            // way up, gone at maximum magnification. Zooming narrows the portal frustum further,
            // so the mismatch grows until the wolf falls off the end of its LOD chain. Measured at
            // that moment: fieldOfView 60 against frustums of 27.9, 14.6, 12.0 and 5.8 degrees.
            //
            // It was degrading every portal view long before anything vanished, by rendering
            // distant terrain and vegetation at a lower detail level than the same view deserves.
            //
            // THE PLAYER'S FIELD OF VIEW, NOT THE WINDOW'S. The fix above first used the angle the
            // window spans, which is right only by accident: close to a window it is far WIDER than
            // the player's (111 degrees measured against 67.7 on 2026-09-23), so everything judged
            // about twice too small, and the angle changes as the player moves. A stump through a
            // hangar door lost its parts as he moved or zoomed and vanished zoomed out, while it held
            // from outside. What the player sees through the glass is drawn at the player's own
            // scale, so the player's field of view gives the same LOD the game would pick outdoors.
            // It must be the value captured after the zoom (CaptureMainCameraFrustumLate): read here,
            // it is the unzoomed one on 5 frames in 6, and every LOD in the view flickers.
            //
            // ORDER MATTERS. Assigning fieldOfView or aspect RESETS an explicit projection matrix,
            // so these have to be set first and the matrix last, or the off-axis frustum is thrown
            // away and the portal renders an ordinary centred view.
            float frustumHeight = top - bottom;
            float frustumWidth = rightExtent - left;
            if (frustumHeight > 0.0001f && frustumWidth > 0.0001f)
            {
                cam.fieldOfView = s_FrustumCapturedLate ? s_LateFieldOfView : main.fieldOfView;
                cam.aspect = frustumWidth / frustumHeight;
            }

            cam.projectionMatrix = Matrix4x4.Frustum(left, rightExtent, bottom, top,
                                                     near, main.farClipPlane);

            // NO Render() CALL. Unity draws this camera itself, later in the frame, once the game
            // has finished activating its terrain objects and vegetation. Driving it from here
            // instead is what produces a portal with no trees and no rock outcrops.
            // The eye was carried to the camera by a plain shift, so the way back is the same shift undone.
            TellTaaHowViewMoves(cam, main, Matrix4x4.Translate(eye - cam.transform.position));   // see ViewMotionVectors.cs
            cam.enabled = true;

            var quadRenderer = quad.GetComponent<MeshRenderer>();
            if (quadRenderer == null) return;
            TellTaaWhichSheet(cam, quadRenderer);

            // Point the quad at the slot it was given this frame. Openings change slots as the
            // player turns and the priority order shifts, so a quad left on last frame's material
            // would show another window's view.
            if (s_WindowPortalQuadTile[openingIndex] != slot)
            {
                quadRenderer.sharedMaterial = s_WindowPortalSlotMaterials[slot];
                s_WindowPortalQuadTile[openingIndex] = slot;
            }

            quadRenderer.enabled = true;
        }

        // ─── IS ANY OF THIS OPENING ON SCREEN? ───
        //
        // Clipped against the main camera's frustum rather than estimated. An estimate that is
        // slightly too generous costs a wasted scene render; one slightly too tight pops a window
        // off at the edge of the screen, which is far more noticeable.
        private static bool IsWindowOpeningOnScreen(InteriorWindowOpening opening, Vector3 corner)
        {
            s_WindowPortalPoly.Clear();
            s_WindowPortalPoly.Add(corner);
            s_WindowPortalPoly.Add(corner + opening.Right * opening.Width);
            s_WindowPortalPoly.Add(corner + opening.Right * opening.Width + opening.Up * opening.Height);
            s_WindowPortalPoly.Add(corner + opening.Up * opening.Height);

            for (int p = 0; p < s_WindowPortalPlanes.Length; p++)
            {
                ClipWindowPortalPolygon(s_WindowPortalPoly, s_WindowPortalPolyB, s_WindowPortalPlanes[p]);

                s_WindowPortalPoly.Clear();
                for (int i = 0; i < s_WindowPortalPolyB.Count; i++) s_WindowPortalPoly.Add(s_WindowPortalPolyB[i]);

                if (s_WindowPortalPoly.Count == 0) return false;
            }

            return s_WindowPortalPoly.Count >= 3;
        }

        // Left, right, bottom, top and near, extracted from the view-projection matrix
        // (Gribb/Hartmann). Far is deliberately not included: a window is never far enough away
        // to be clipped by it, and the plane is the least numerically stable of the six.
        // Set once the planes have been captured after the zoom is applied, which stops the ticks
        // overwriting them with the pre-zoom frustum. See Patches/PortalFrustumPatch.cs.
        private static bool s_FrustumCapturedLate;
        private static Matrix4x4 s_LateViewProjection = Matrix4x4.identity;
        private static float s_LateFieldOfView = 60f;

        // Where a world point lands on screen, according to the frustum that is actually rendered.
        // Camera.WorldToViewportPoint answers for the camera as it stands RIGHT NOW, which in the
        // mod's tick is before the zoom has been applied; this answers for the captured one.
        private static Vector3 LateViewportPoint(Camera main, Vector3 world)
        {
            if (!s_FrustumCapturedLate) return main.WorldToViewportPoint(world);

            Vector4 clip = s_LateViewProjection * new Vector4(world.x, world.y, world.z, 1f);
            if (Mathf.Abs(clip.w) < 1e-6f) return new Vector3(0f, 0f, -1f);
            return new Vector3(clip.x / clip.w * 0.5f + 0.5f,
                               clip.y / clip.w * 0.5f + 0.5f,
                               clip.w);                       // w > 0 means in front, as viewport.z is used
        }

        // Called from a Harmony postfix on vp_FPSCamera.LateUpdate, which is where the player's
        // zoom lands. Recorded live 2026-09-22 over 120 consecutive frames with the player zoomed:
        // the planes described a 68 degree frustum on 100 of them and the correct 17 on 20, while
        // the camera rendered at 17 throughout. One frame in six was sharp and the rest were not,
        // which is exactly what the owner reported seeing.
        internal static void CaptureMainCameraFrustumLate()
        {
            var main = GameManager.GetMainCamera();
            if (main == null) return;
            BuildWindowPortalFrustum(main);
            // The SAME matrix the planes came from. Everything that asks "where is this on screen"
            // has to use it, or it asks the pre-zoom camera and gets a different answer. The size
            // of the render target was the second consumer to be caught by that.
            s_LateViewProjection = main.projectionMatrix * main.worldToCameraMatrix;
            // The window cameras pick LOD from this. Read in the mod's own tick instead, it was the
            // unzoomed value on 5 frames in 6 and everything with a LOD flickered (2026-09-23).
            s_LateFieldOfView = main.fieldOfView;
            s_FrustumCapturedLate = true;
        }

        private static void BuildWindowPortalFrustum(Camera main)
        {
            Matrix4x4 viewProjection = main.projectionMatrix * main.worldToCameraMatrix;
            Vector4 r0 = viewProjection.GetRow(0);
            Vector4 r1 = viewProjection.GetRow(1);
            Vector4 r2 = viewProjection.GetRow(2);
            Vector4 r3 = viewProjection.GetRow(3);

            s_WindowPortalPlanes[0] = r3 + r0;
            s_WindowPortalPlanes[1] = r3 - r0;
            s_WindowPortalPlanes[2] = r3 + r1;
            s_WindowPortalPlanes[3] = r3 - r1;
            s_WindowPortalPlanes[4] = r3 + r2;
        }

        // Sutherland-Hodgman against one plane. Writes into `result` rather than allocating, so
        // this can run for every window of every building every frame.
        private static void ClipWindowPortalPolygon(List<Vector3> polygon, List<Vector3> result, Vector4 plane)
        {
            result.Clear();
            int count = polygon.Count;
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                Vector3 a = polygon[i];
                Vector3 b = polygon[(i + 1) % count];

                float da = plane.x * a.x + plane.y * a.y + plane.z * a.z + plane.w;
                float db = plane.x * b.x + plane.y * b.y + plane.z * b.z + plane.w;

                if (da >= 0f) result.Add(a);
                if ((da >= 0f) != (db >= 0f))
                {
                    float t = da / (da - db);
                    result.Add(a + (b - a) * t);
                }
            }
        }

        // Pairs each interior opening with the exterior pane it represents, and stores the
        // translation from one to the other. Falls back to the building-wide delta for any opening
        // that cannot be paired, which is still better than not drawing it.
        // A room window's view out moved by hand from the spot the pairs agree on (BuildingWindowOverride.ViewOutOffsets),
        // paired or not.
        private static Vector3 ViewOutOffset(string shellName, int roomWindow, Vector3 outward)
        {
            BuildingWindowOverride row;
            if (!TryGetBuildingWindowOverride(shellName, out row) || row.ViewOutOffsets == null) return Vector3.zero;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-6f) return Vector3.zero;
            outward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;   // the right hand of someone inside looking out
            foreach (var v in row.ViewOutOffsets)
                if (v != null && v.RoomWindow == roomWindow)
                    return right * (v.RightCm / 100f) + Vector3.up * (v.UpCm / 100f) + outward * (v.OutCm / 100f);
            return Vector3.zero;
        }

        private static void PairWindowOpeningsToShell(SeamlessInteriorInstance instance, Vector3 buildingDelta)
        {
            s_WindowPortalDelta.Clear();
            s_WindowPortalPairedCount = 0;

            for (int i = 0; i < s_WindowPortalOpenings.Count; i++) s_WindowPortalDelta.Add(buildingDelta);

            s_WindowPortalSpot.Clear();
            for (int i = 0; i < s_WindowPortalOpenings.Count; i++) s_WindowPortalSpot.Add(-1);

            s_WindowPortalViewStart.Clear();
            for (int i = 0; i < s_WindowPortalOpenings.Count; i++) s_WindowPortalViewStart.Add(WINDOW_PORTAL_VIEW_STARTS_OUTSIDE_SHELL_GLASS);

            WindowSpot[] spots;
            string shellName = instance.Config.ExteriorShellPrefabName;
            if (string.IsNullOrEmpty(shellName) || !TryGetShellWindows(instance, out spots)) return;
            if (instance.ExteriorShell == null) return;

            Transform shell = instance.ExteriorShell.transform;
            var taken = new bool[spots.Length];

            // The shell's mesh, once, for measuring each paired window's frame below.
            Vector3[] shellWorld; int[] shellTriangles;
            bool shellReadable = TryGetShellWorldMesh(instance, out shellWorld, out shellTriangles);
            var viewStartReport = new System.Text.StringBuilder();

            // THE WHOLE BUILDING'S WINDOWS ARE PAIRED AT ONCE, the same fit the view from outside
            // uses (WindowPairing.cs), so a window shows the same twin from both sides. It used to be
            // the nearest window facing the same way, one at a time, which took the neighbor at Camp
            // Trailer E, where the room sits 4 m along from its building.
            var fitShellCenters = new List<Vector3>(spots.Length); var fitShellOutward = new List<Vector3>(spots.Length);
            for (int s = 0; s < spots.Length; s++)
            {
                fitShellCenters.Add(shell.TransformPoint(new Vector3(spots[s].X, spots[s].Y, spots[s].Z)));
                fitShellOutward.Add(shell.TransformDirection(new Vector3(spots[s].NX, 0f, spots[s].NZ)).normalized);
            }
            var fitRoomCenters = new List<Vector3>(s_WindowPortalOpenings.Count); var fitRoomOutward = new List<Vector3>(s_WindowPortalOpenings.Count);
            for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
            {
                // Where this interior pane would be if the building were standing on its shell.
                fitRoomCenters.Add(s_WindowPortalOpenings[i].PortalCenter + buildingDelta);
                fitRoomOutward.Add(s_WindowPortalOpenings[i].Outward);
            }
            // Views out only (InteriorWindowOpening.ViewOutOnly) are left out of the fit, so they cannot take a
            // building window from a real one; unpaired, they look out from where the pairs agree the room stands.
            var fitRooms = new List<int>(s_WindowPortalOpenings.Count);
            var fitOnlyCenters = new List<Vector3>(s_WindowPortalOpenings.Count); var fitOnlyOutward = new List<Vector3>(s_WindowPortalOpenings.Count);
            for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
            {
                if (s_WindowPortalOpenings[i].ViewOutOnly) continue;
                fitRooms.Add(i); fitOnlyCenters.Add(fitRoomCenters[i]); fitOnlyOutward.Add(fitRoomOutward[i]);
            }
            int[] fitOnlyToShell; Vector3 fitSlide;
            BuildingWindowOverride pairRow;
            bool fitted = TryGetBuildingWindowOverride(shellName, out pairRow) && !float.IsNaN(pairRow.PairInPlaceWithinCm)
                ? PairWindowsInPlace(fitOnlyCenters, fitOnlyOutward, fitShellCenters, fitShellOutward, pairRow.PairInPlaceWithinCm / 100f, out fitOnlyToShell, out fitSlide)
                : FitWindowPattern(fitOnlyCenters, fitOnlyOutward, fitShellCenters, fitShellOutward, out fitOnlyToShell, out fitSlide);
            int[] roomToShell = new int[s_WindowPortalOpenings.Count];
            for (int i = 0; i < roomToShell.Length; i++) roomToShell[i] = -1;
            if (fitted && fitOnlyToShell != null) for (int k = 0; k < fitRooms.Count && k < fitOnlyToShell.Length; k++) roomToShell[fitRooms[k]] = fitOnlyToShell[k];

            // The building's hand pairing, when it has one, so the views out pair as the views in do
            // (BuildingWindowOverride.PairedRoomWindows). Barn A's reached only the views in at first.
            var handShellToRoom = new int[spots.Length];
            for (int s = 0; s < handShellToRoom.Length; s++) handShellToRoom[s] = -1;
            if (fitted) for (int i = 0; i < roomToShell.Length; i++) if (roomToShell[i] >= 0) handShellToRoom[roomToShell[i]] = i;
            if (ApplyHandPairs(shellName, instance.Config.ResolvedInstanceId, handShellToRoom, s_WindowPortalOpenings.Count))
            {
                if (roomToShell == null || roomToShell.Length != s_WindowPortalOpenings.Count) roomToShell = new int[s_WindowPortalOpenings.Count];
                for (int i = 0; i < roomToShell.Length; i++) roomToShell[i] = -1;
                for (int s = 0; s < handShellToRoom.Length; s++) if (handShellToRoom[s] >= 0) roomToShell[handShellToRoom[s]] = s;
                Vector3 handSlide;
                if (MeanSlideOf(fitRoomCenters, fitShellCenters, roomToShell, out handSlide)) { fitSlide = handSlide; fitted = true; }
            }

            for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
            {
                var opening = s_WindowPortalOpenings[i];

                int best = fitted ? roomToShell[i] : -1;
                if (best < 0)
                {
                    // A ROOM WINDOW WITH NO PANE ON THE BUILDING (the gas station's high strip of three).
                    // It looked out from where it would stand if the room stood exactly on its
                    // building as configured, which at the gas station is a meter and a half from
                    // where the room's other windows agree it stands. Owner, 2026-09-20: "as long as
                    // the camera position is close enough to where the exterior window would be,
                    // we're good." So it takes the slide the paired windows agree on, which is what a
                    // building pane with no room window already does in the view from outside.
                    // fitSlide runs from the building's window to the room's, hence the minus.
                    if (fitted) s_WindowPortalDelta[i] = buildingDelta - fitSlide + ViewOutOffset(shellName, i, opening.Outward);
                    continue;
                }

                taken[best] = true;
                Vector3 pairedWorld = shell.TransformPoint(new Vector3(spots[best].X, spots[best].Y, spots[best].Z));

                // A DOUBLE WINDOW: one wide window in the room, two panes and a post outside (the
                // Pleasant Valley farmhouse has five). The room's window was carried onto ONE pane's
                // middle, so the whole view out stood half a pane-spacing to one side. It is carried
                // onto the middle of the two panes instead. Same test as AssignOutsideInViews.
                Vector3 viewWorld = pairedWorld;
                if (opening.Width > spots[best].Width * DOUBLE_WINDOW_ROOM_WIDER_BY)
                {
                    Vector3 outwardHere = fitShellOutward[best];
                    Vector3 rightHere = Vector3.Cross(Vector3.up, -outwardHere).normalized;
                    int partner = -1; float bestMiss = DOUBLE_WINDOW_WIDTH_AGREES_WITHIN;
                    for (int s = 0; s < spots.Length; s++)
                    {
                        if (s == best) continue;
                        bool pairedElsewhere = false;
                        for (int r = 0; r < roomToShell.Length && !pairedElsewhere; r++) pairedElsewhere = roomToShell[r] == s;
                        if (pairedElsewhere) continue;
                        if (Vector3.Dot(fitShellOutward[s], outwardHere) < 0.999f) continue;
                        Vector3 between = fitShellCenters[s] - pairedWorld;
                        if (Mathf.Abs(Vector3.Dot(between, outwardHere)) > DOUBLE_WINDOW_SAME_PLANE || Mathf.Abs(between.y) > DOUBLE_WINDOW_SAME_PLANE) continue;
                        float miss = Mathf.Abs(Mathf.Abs(Vector3.Dot(between, rightHere)) + (spots[best].Width + spots[s].Width) * 0.5f - opening.Width);
                        if (miss >= bestMiss) continue;
                        bestMiss = miss; partner = s;
                    }
                    if (partner >= 0)
                    {
                        float across = Vector3.Dot(fitShellCenters[partner] - pairedWorld, rightHere);
                        float left = Mathf.Min(-spots[best].Width * 0.5f, across - spots[partner].Width * 0.5f);
                        float right = Mathf.Max(spots[best].Width * 0.5f, across + spots[partner].Width * 0.5f);
                        viewWorld = pairedWorld + rightHere * ((left + right) * 0.5f);
                        taken[partner] = true;
                    }
                }
                // A hand move applies to a paired window as well (2026-10-02); no row moved one before.
                s_WindowPortalDelta[i] = viewWorld - opening.PortalCenter + ViewOutOffset(shellName, i, opening.Outward);
                s_WindowPortalSpot[i] = best;
                s_WindowPortalPairedCount++;

                if (shellReadable)
                {
                    Vector3 pairedOutward = shell.TransformDirection(new Vector3(spots[best].NX, 0f, spots[best].NZ)).normalized;
                    float relief = MeasureShellFrameRelief(shellWorld, shellTriangles, pairedWorld, pairedOutward, spots[best].Width, spots[best].Height);
                    float cover = relief > 0f
                                ? MeasureShellCoverInwardFace(shellWorld, shellTriangles, pairedWorld, pairedOutward, spots[best].Width, spots[best].Height, relief)
                                : -1f;
                    if (relief > 0f && cover > 0f) s_WindowPortalViewStart[i] = cover + WINDOW_PORTAL_VIEW_CLEARS_FRAME_BY;
                    else if (relief > 0f)
                    {
                        float start = relief + WINDOW_PORTAL_VIEW_PAST_FRAME_BY;
                        float next = MeasureShellNextBeyondFrame(shellWorld, shellTriangles, pairedWorld, pairedOutward, spots[best].Width, spots[best].Height, relief);
                        if (next > 0f && next - start < WINDOW_PORTAL_VIEW_PAST_FRAME_BY) start = relief + (next - relief) * 0.5f;
                        s_WindowPortalViewStart[i] = start;
                    }
                    viewStartReport.Append(relief > 0f ? (cover > 0f ? $"{relief * 100f:F1}(shutter {cover * 100f:F1}) " : $"{relief * 100f:F1} ") : "? ");
                }
            }

            if (s_DebugBounds)
                MelonLogger.Msg($"[WINPORTAL] {shellName}: pencere cercevelerinin camdan disari cikintisi, cm: {viewStartReport}(? = olculemedi, 4.0 kullanildi; parantez icinde: cerceveden daha yakin duran kapali kepenk).",
                                $"[WINPORTAL] {shellName}: how far each window's frame stands out from its glass, in cm: {viewStartReport}(? = could not be measured, 4.0 used). The view out of each window starts half a millimeter PAST that (halfway to anything hung just beyond it), or half a millimeter short of a shutter closed nearer than the frame, shown in brackets.");

            if (s_DebugBounds)
                MelonLogger.Msg($"[WINPORTAL] {shellName}: {s_WindowPortalPairedCount}/{s_WindowPortalOpenings.Count} " +
                                $"pencere dis kabuktaki esiyle eslestirildi.",
                                $"[WINPORTAL] {shellName}: {s_WindowPortalPairedCount} of {s_WindowPortalOpenings.Count} " +
                                $"room windows matched to a window on the outside of the building.");

            // THE WINDOWS OF ONE WALL GROUP LOOK OUT PAST THE OUTERMOST OF THEIR BUILDING PANES. The
            // hangar's sliding doors stand staggered, 48 cm from the innermost to the outermost, and
            // overlap sideways: a view starting 4 cm past a middle door's own glass had the neighboring
            // doors' frames in it. So every member's view starts where the group's outermost pane
            // does, own pane to outermost plus its own measured start (2026-09-21, his pass).
            for (int g = 1; ; g++)
            {
                float outermost = float.MinValue; int members = 0;
                for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
                {
                    if (s_WindowPortalOpenings[i].WallGroup != g || s_WindowPortalSpot[i] < 0) continue;
                    var sp = spots[s_WindowPortalSpot[i]];
                    float depth = sp.X * sp.NX + sp.Z * sp.NZ;
                    if (depth > outermost) outermost = depth;
                    members++;
                }
                if (members == 0) break;
                for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
                {
                    if (s_WindowPortalOpenings[i].WallGroup != g || s_WindowPortalSpot[i] < 0) continue;
                    var sp = spots[s_WindowPortalSpot[i]];
                    s_WindowPortalViewStart[i] += outermost - (sp.X * sp.NX + sp.Z * sp.NZ);
                }
            }

            // (see SeamlessInteriorsMod.WindowOpenings.cs)
            LogWallGroupOffsets(shellName, s_WindowPortalOpenings, s_WindowPortalDelta);
        }

        // ─── CLEARING A PANE: GLASS AND FROST OFF ONE WINDOW ───
        //
        // This is the mechanism behind the player-facing "clear the windows" action, and it is
        // deliberately PER PANE. A hover trigger on one window will call
        // SetWindowPaneCleared(index, true) for that window and nothing else.
        //
        // Glass and frost are the two things between the player and the portal. The frost is
        // opaque and nearer than the quad, so it occludes the view wherever it has geometry; the
        // glass is transparent and tints what is behind it. Taking both out is what a wiped pane
        // looks like.
        //
        // Renderers are switched off rather than the objects deactivated, because the objects
        // carry the pane's colliders and the game's own interaction lives on them. A cleared
        // window must still be a window you can walk into.

        public static int WindowPaneCount
        {
            get { return s_WindowPortalOpenings.Count; }
        }

        public static bool IsWindowPaneCleared(int openingIndex)
        {
            if (openingIndex < 0 || openingIndex >= s_WindowPaneCleared.Count) return false;
            return s_WindowPaneCleared[openingIndex];
        }

        public static void SetWindowPaneCleared(int openingIndex, bool cleared)
        {
            if (openingIndex < 0 || openingIndex >= s_WindowPortalOpenings.Count) return;
            if (openingIndex >= s_WindowPaneCleared.Count) return;
            // With the cover layers a cleaned pane frosts over again, and wiping it again restarts its clock.
            if (s_WindowPaneCleared[openingIndex] == cleared && !(cleared && WindowCoverLayers)) return;

            // Through the shared state when this opening has a shell window, so the outside of the
            // same window follows. SetWindowCleaned comes back here through ApplyWindowPane.
            int window = openingIndex < s_WindowPortalSpot.Count ? s_WindowPortalSpot[openingIndex] : -1;
            // Without one, through the room window's own record, so it is still there next entry.
            if (window >= 0 && s_WindowPortalInstanceId != null) SetWindowCleaned(s_WindowPortalInstanceId, window, cleared);
            else if (s_WindowPortalInstanceId != null) SetRoomWindowCleaned(s_WindowPortalInstanceId, openingIndex, cleared);
            else ApplyWindowPane(openingIndex, cleared);

            if (s_DebugBounds)
                MelonLogger.Msg($"[WINPORTAL] pencere {openingIndex}: cam ve kar " +
                                (cleared ? "kaldirildi." : "geri kondu."),
                                $"[WINPORTAL] window {openingIndex}: glass and snow " +
                                (cleared ? "removed." : "put back."));
        }

        // Does the work, unconditionally and without touching the shared state. Setup uses this to
        // bring a pane to the state it already has on record.
        private static void ApplyWindowPane(int openingIndex, bool cleared)
        {
            if (openingIndex < 0 || openingIndex >= s_WindowPortalOpenings.Count) return;
            if (openingIndex >= s_WindowPaneCleared.Count) return;

            var opening = s_WindowPortalOpenings[openingIndex];
            // The cover layers draw the window instead of the painted frost (WindowCoverLayers.cs).
            if (WindowCoverLayers)
            {
                SetGlassShown(opening.Glass, false);
                s_WindowPaneCleared[openingIndex] = cleared;
                MarkWindowCoverDirty();
                return;
            }
            SetGlassShown(opening.Glass, !cleared);
            SetRenderersEnabled(opening.Frost, !cleared && InsideOutShowSnowSlab);
            if (!cleared && !s_WindowPortalKeepsOwnGlass) GiveGlassTheFrostMaterial(opening.Glass);
            s_WindowPaneCleared[openingIndex] = cleared;
        }

        // A renderer holding frame and glass (the post office door) stays on when cleared: only its
        // glass slots go clear, so the frame stays. Glass-only renderers are switched off as before.
        private static Material s_WindowPortalClearGlassMaterial;

        private static void SetGlassShown(List<Renderer> glass, bool shown)
        {
            if (glass == null) return;
            for (int i = 0; i < glass.Count; i++)
            {
                var renderer = glass[i];
                if (renderer == null) continue;
                Material[] current = renderer.sharedMaterials;
                if (!IsGlassSharingItsObject(current)) { renderer.enabled = shown; continue; }
                renderer.enabled = true;
                if (shown) continue;            // GiveGlassTheFrostMaterial frosts the glass slots again
                if (s_WindowPortalClearGlassMaterial == null)
                {
                    Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
                    if (shader == null) { renderer.enabled = false; continue; }
                    s_WindowPortalClearGlassMaterial = new Material(shader);
                    s_WindowPortalClearGlassMaterial.name = "SeamlessInteriors_ClearGlass";
                    s_WindowPortalClearGlassMaterial.color = new Color(1f, 1f, 1f, 0f);
                }
                for (int k = 0; k < current.Length; k++) if (IsGlassSlot(current[k])) current[k] = s_WindowPortalClearGlassMaterial;
                renderer.sharedMaterials = current;
            }
        }

        private static bool IsGlassSlot(Material material)
        {
            if (material == null) return false;
            if (material == s_WindowPortalFrostMaterial || material == s_WindowPortalClearGlassMaterial) return true;
            return material.name.StartsWith(GLASS_MATERIAL_PREFIX) || material.name.StartsWith(MANY_PANE_GLASS_MATERIAL_PREFIX) || IsWindowGlassMaterial(material);
        }

        private static bool HasFrameSlot(Material[] slots)
        {
            for (int k = 0; k < slots.Length; k++) if (slots[k] != null && !IsGlassSlot(slots[k])) return true;
            return false;
        }

        private static void SetRenderersEnabled(List<Renderer> renderers, bool enabled)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Count; i++)
            {
                // Unity's overloaded == is what detects a destroyed native object here, which a
                // renderer belonging to a clone being torn down may well be.
                if (renderers[i] == null) continue;
                // Glass that shares its object with something else is never switched off: that would take
                // the door with it. Such a pane stays as it is when its window is cleaned.
                if (!enabled)
                {
                    Material[] asMade = renderers[i].sharedMaterials;
                    int frostedAt = s_WindowPortalFrostedGlass.IndexOf(renderers[i]);
                    if (frostedAt >= 0 && frostedAt < s_WindowPortalFrostedGlassSaved.Count) asMade = s_WindowPortalFrostedGlassSaved[frostedAt];
                    if (IsGlassSharingItsObject(asMade)) continue;
                }
                renderers[i].enabled = enabled;
            }
        }

        // Every pane, which is what a panel row or a preference wants. The per-pane call above is
        // what an interaction wants.
        public static void SetAllWindowPanesCleared(bool cleared)
        {
            SetAllWindowsCleaned(cleared);
        }

        public static bool AreAllWindowPanesCleared()
        {
            if (s_WindowPaneCleared.Count == 0) return false;
            for (int i = 0; i < s_WindowPaneCleared.Count; i++) if (!s_WindowPaneCleared[i]) return false;
            return true;
        }

        // ─── A PANE OF GLASS FOR AN OPENING THAT HAS NONE ───
        //
        // The opening over Grey Mother's front door is a bare hole in the wall. Read as a window it
        // shows the outdoors, and the owner, the same evening: "It is missing some frost on the
        // interior view. Could we manufacture some?" and "Can you just flip one sideways?" So: one
        // flat pane, in the opening, wearing the material of a real pane of the same house, with
        // that pane's place on the frost sheet laid over it WHOLE, turned on its side when the
        // opening is wide and the pane tall (or the other way round), and squashed to fit, which he
        // looked at and passed ("Squishing is fine"): frost along all four edges, as a pane has it.
        // A strip cut from one edge of the pane, tried first to avoid the squash, puts the pane's
        // edge frost along the bottom and its clear middle along the top.
        //
        // Set by his eye on a console prototype, 2026-09-20: 0.10 cm down, and 2.5 cm past the
        // measured hole all round, because the hole's rim is stepped (the wall has corners 1.5 and
        // 2.5 cm below the tightest rectangle and 1 cm above it) and a pane cut to that rectangle
        // leaves the step bare. The wall hides what reaches past. The lighting normal is the real
        // pane's, in or out of the room as that pane has it: two-sided triangles sharing corners
        // with recalculated normals cancel to nothing and the pane renders dark, which the first
        // prototype did ("it's dark instead of light").
        private static readonly List<GameObject> s_WindowPortalMadeGlass = new List<GameObject>();
        private const float MADE_GLASS_PAST_THE_RIM = 0.025f;
        private const float MADE_GLASS_DOWN = 0.001f;
        private const float MADE_GLASS_INTO_THE_ROOM = 0.006f;      // in front of the picture of the outdoors, which stands on the glass plane

        private static Renderer MakeGlassForAnOpeningWithNone(InteriorWindowOpening opening, List<InteriorWindowOpening> all)
        {
            // The LARGEST plain four-corner pane of this room. At Grey Mother's house that is a lower
            // pane of a tall ground-floor window, 46 x 120 cm, which is the one the owner looked at
            // and passed; the narrower upstairs panes would squash less and were not what he saw.
            MeshRenderer pane = null; Vector3 paneOutward = Vector3.zero; float largest = 0f;
            Vector3[] paneWorld = null; Vector2[] paneUv = null; Vector3 paneNormal = Vector3.zero;
            for (int o = 0; o < all.Count; o++)
            {
                if (all[o].Glass == null) continue;
                for (int g = 0; g < all[o].Glass.Count; g++)
                {
                    var candidate = all[o].Glass[g] as MeshRenderer;
                    var filter = candidate == null ? null : candidate.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable || filter.sharedMesh.vertexCount != 4) continue;
                    Vector3[] local = filter.sharedMesh.vertices; Vector2[] uv = filter.sharedMesh.uv;
                    if (uv == null || uv.Length != 4) continue;
                    var world = new Vector3[4]; float low = float.MaxValue, high = float.MinValue;
                    for (int v = 0; v < 4; v++) { world[v] = candidate.transform.TransformPoint(local[v]); low = Mathf.Min(low, world[v].y); high = Mathf.Max(high, world[v].y); }
                    float wide = 0f;
                    for (int v = 1; v < 4; v++) { Vector3 d = world[v] - world[0]; d.y = 0f; wide = Mathf.Max(wide, d.magnitude); }
                    if (wide < 0.05f || high - low < 0.05f) continue;
                    float area = (high - low) * wide;
                    if (area <= largest + 0.0001f) continue;                                           // the first of equals, so the choice does not wander
                    largest = area; pane = candidate; paneOutward = all[o].Outward; paneWorld = world; paneUv = uv;
                    Vector3[] normals = filter.sharedMesh.normals;
                    paneNormal = normals != null && normals.Length > 0 ? candidate.transform.TransformDirection(normals[0]).normalized : -all[o].Outward;
                }
            }
            if (pane == null) return null;

            // The pane's place on the frost sheet, by its corners as they stand in the world.
            Vector3 paneMiddle = (paneWorld[0] + paneWorld[1] + paneWorld[2] + paneWorld[3]) * 0.25f;
            Vector3 paneAlong = Vector3.Cross(Vector3.up, new Vector3(paneOutward.x, 0f, paneOutward.z).normalized).normalized;
            Vector2 bottomLeft = paneUv[0], bottomRight = paneUv[0], topLeft = paneUv[0];
            float paneWidth = 0f, paneLow = float.MaxValue, paneHigh = float.MinValue;
            for (int v = 0; v < 4; v++)
            {
                bool low = paneWorld[v].y < paneMiddle.y;
                bool left = Vector3.Dot(paneWorld[v] - paneMiddle, paneAlong) < 0f;
                if (low && left) bottomLeft = paneUv[v];
                if (low && !left) bottomRight = paneUv[v];
                if (!low && left) topLeft = paneUv[v];
                paneWidth = Mathf.Max(paneWidth, Mathf.Abs(Vector3.Dot(paneWorld[v] - paneMiddle, paneAlong)) * 2f);
                paneLow = Mathf.Min(paneLow, paneWorld[v].y); paneHigh = Mathf.Max(paneHigh, paneWorld[v].y);
            }
            float paneHeight = paneHigh - paneLow;

            float holeWidth = opening.GlassWidth > 0.01f ? opening.GlassWidth : opening.Width;
            float holeHeight = opening.GlassHeight > 0.01f ? opening.GlassHeight : opening.Height;
            float width = holeWidth + 2f * MADE_GLASS_PAST_THE_RIM, height = holeHeight + 2f * MADE_GLASS_PAST_THE_RIM;
            Vector3 middle = opening.GlassCenter - opening.Outward * MADE_GLASS_INTO_THE_ROOM - opening.Up * MADE_GLASS_DOWN;

            // On its side when the opening lies the other way from the pane: the pane's UP then runs
            // along the opening's width, its bottom at the opening's left.
            bool onItsSide = (holeWidth > holeHeight) != (paneWidth > paneHeight);
            Vector2 acrossStep = bottomRight - bottomLeft, upStep = topLeft - bottomLeft;
            Vector2 uvBottomLeft = bottomLeft;
            Vector2 uvBottomRight = onItsSide ? bottomLeft + upStep : bottomLeft + acrossStep;
            Vector2 uvTopLeft = onItsSide ? bottomLeft + acrossStep : bottomLeft + upStep;
            Vector2 uvTopRight = bottomLeft + acrossStep + upStep;

            Vector3 lit = opening.Outward * (Vector3.Dot(paneNormal, paneOutward) >= 0f ? 1f : -1f);
            var mesh = new Mesh();
            mesh.name = "SeamlessInteriors_MadeGlassMesh";
            mesh.vertices = new Vector3[]
            {
                middle - opening.Right * (width * 0.5f) - opening.Up * (height * 0.5f),
                middle + opening.Right * (width * 0.5f) - opening.Up * (height * 0.5f),
                middle - opening.Right * (width * 0.5f) + opening.Up * (height * 0.5f),
                middle + opening.Right * (width * 0.5f) + opening.Up * (height * 0.5f),
            };
            mesh.uv = new Vector2[] { uvBottomLeft, uvBottomRight, uvTopLeft, uvTopRight };
            mesh.normals = new Vector3[] { lit, lit, lit, lit };
            mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 };      // seen from both sides, lit by the normals above
            mesh.RecalculateBounds();

            // World-space corners on an identity transform, under the pane's own parent so it goes
            // where the room goes. The name keeps it off the author's list of scenery to hide.
            var made = new GameObject("SeamlessInteriors_MadeGlass");
            made.layer = pane.gameObject.layer;
            made.transform.SetParent(pane.transform.parent, true);
            made.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = made.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = pane.sharedMaterials;
            renderer.shadowCastingMode = pane.shadowCastingMode;
            renderer.receiveShadows = pane.receiveShadows;
            renderer.lightProbeUsage = pane.lightProbeUsage;
            renderer.reflectionProbeUsage = pane.reflectionProbeUsage;

            if (s_DebugBounds)
                MelonLogger.Msg($"[WINPORTAL] Camsiz aciklik icin bir cam yapildi: {holeWidth * 100f:F0} x {holeHeight * 100f:F0} cm, '{pane.gameObject.name}' caminin buzu {(onItsSide ? "yan cevrilerek" : "dik")} kondu.",
                                $"[WINPORTAL] A pane of glass made for an opening with none: {holeWidth * 100f:F0} x {holeHeight * 100f:F0} cm, wearing the frost of '{pane.gameObject.name}' ({paneWidth * 100f:F0} x {paneHeight * 100f:F0} cm) {(onItsSide ? "turned on its side" : "upright")}.");
            return renderer;
        }

        private static bool IsWindowGlassMaterial(Material material)
        {
            return material != null && material.name.IndexOf("WindowGlass", System.StringComparison.Ordinal) >= 0;
        }

        // Window glass in some slots and something else in others: the glass is part of a larger object.
        private static bool IsGlassSharingItsObject(Material[] materials)
        {
            if (materials == null) return false;
            bool glass = false, other = false;
            for (int k = 0; k < materials.Length; k++) { if (IsGlassSlot(materials[k])) glass = true; else if (materials[k] != null) other = true; }
            return glass && other;
        }

        private static void GiveGlassTheFrostMaterial(List<Renderer> glass)
        {
            if (glass == null) return;

            for (int i = 0; i < glass.Count; i++)
            {
                var renderer = glass[i];
                if (renderer == null) continue;
                // GLASS THAT SHARES ITS OBJECT WITH SOMETHING ELSE. Only the glass slots are given the frost.
                // Thompson's Store's door glass is one of three materials on the room's doors, and every slot
                // used to be replaced: both doors turned to frosted glass from top to bottom, with the world
                // 82 m below showing through them (owner, 2026-10-09: "Inside is all messed up"). An object
                // that is glass in every slot, or in none by name, is treated as it always was.
                bool mixed = IsGlassSharingItsObject(renderer.sharedMaterials);
                if (s_WindowPortalFrostedGlass.Contains(renderer))
                {
                    // Back from cleared: its glass slots wear the frost again.
                    if (!mixed || s_WindowPortalFrostMaterial == null) continue;
                    Material[] again = renderer.sharedMaterials;
                    for (int k = 0; k < again.Length; k++) if (IsGlassSlot(again[k])) again[k] = s_WindowPortalFrostMaterial;
                    renderer.sharedMaterials = again;
                    continue;
                }

                Material[] current = renderer.sharedMaterials;
                if (current == null || current.Length == 0 || current[0] == null) continue;
                int source = 0;
                if (mixed) for (int k = 0; k < current.Length; k++) if (IsGlassSlot(current[k])) { source = k; break; }

                if (s_WindowPortalFrostMaterial == null)
                {
                    Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
                    if (shader == null || current[source].mainTexture == null) return;

                    s_WindowPortalFrostMaterial = new Material(shader);
                    s_WindowPortalFrostMaterial.name = "SeamlessInteriors_FrostedGlassInside";
                    s_WindowPortalFrostMaterial.mainTexture = current[source].mainTexture;
                    s_WindowPortalFrostMaterial.mainTextureScale = current[source].mainTextureScale;
                    s_WindowPortalFrostMaterial.mainTextureOffset = current[source].mainTextureOffset;
                    s_WindowPortalFrostMaterial.renderQueue = WINDOW_VIEW_OUT_FROST_QUEUE;   // after the view
                    s_WindowPortalFrostMaterial.color = new Color(1f, 1f, 1f, Mathf.Clamp01(InsideOutFrostStrength));
                }

                // Frame and glass in one renderer: only the glass slots wear the frost.
                var frosted = new Material[current.Length];
                for (int k = 0; k < frosted.Length; k++) frosted[k] = !mixed || IsGlassSlot(current[k]) ? s_WindowPortalFrostMaterial : current[k];

                s_WindowPortalFrostedGlass.Add(renderer);
                s_WindowPortalFrostedGlassSaved.Add(current);
                renderer.sharedMaterials = frosted;
            }
        }

        // The levers are public fields a panel can write at any moment, so they are followed
        // rather than read once.
        private static void FollowWindowFrostLevers()
        {
            if (s_WindowPortalFrostMaterial != null)
            {
                float strength = Mathf.Clamp01(InsideOutFrostStrength);
                if (s_WindowPortalFrostMaterial.color.a != strength)
                    s_WindowPortalFrostMaterial.color = new Color(1f, 1f, 1f, strength);
            }

            if (s_WindowPortalSnowSlabApplied == InsideOutShowSnowSlab) return;
            s_WindowPortalSnowSlabApplied = InsideOutShowSnowSlab;
            if (WindowCoverLayers) { MarkWindowCoverDirty(); return; }
            for (int i = 0; i < s_WindowPaneCleared.Count && i < s_WindowPortalOpenings.Count; i++)
                if (!s_WindowPaneCleared[i]) SetRenderersEnabled(s_WindowPortalOpenings[i].Frost, InsideOutShowSnowSlab);
        }

        // ─── SETUP AND TEARDOWN ───

        private static bool SetupWindowPortals(SeamlessInteriorInstance instance, string id)
        {
            var openings = FindInteriorWindowOpenings(instance);
            if (openings.Count == 0)
            {
                // Remembered anyway, so a building with no readable windows is not re-scanned
                // every single frame the player stands in it.
                s_WindowPortalInstanceId = id;
                return false;
            }

            s_WallCamerasActive = WallCamerasWanted(instance);
            s_WindowPortalSlotCount = Mathf.Max(1, s_PrefWindowPortalLimit.Value);
            s_WindowPortalCams = new Camera[s_WindowPortalSlotCount];
            s_WindowPortalSlotTargets = new RenderTexture[s_WindowPortalSlotCount];
            if (s_KeptWindowTargets != null)
            {
                // Taken back from the last visit; a wrong size is replaced by EnsureWindowPortalSlot.
                for (int i = 0; i < s_KeptWindowTargets.Length; i++)
                {
                    var kept = s_KeptWindowTargets[i];
                    if (kept == null) continue;
                    if (i < s_WindowPortalSlotCount) s_WindowPortalSlotTargets[i] = kept;
                    else { kept.Release(); Object.Destroy(kept); }
                }
                s_KeptWindowTargets = null;
            }
            s_WindowPortalSlotMaterials = new Material[s_WindowPortalSlotCount];

            s_WindowPortalOpenings.Clear();
            s_WindowPortalQuads.Clear();
            s_WindowPortalQuadTile.Clear();
            s_WindowPortalHiddenBackdrops.Clear();
            s_WindowPaneCleared.Clear();
            s_WindowPortalWasDrawn.Clear();

            for (int i = 0; i < openings.Count; i++)
            {
                var opening = openings[i];
                // An opening with no glass of its own gets a pane made for it, so it wears frost like
                // its neighbors and can be wiped like them. Into the opening's own Glass list, before
                // anything below reads it. See MakeGlassForAnOpeningWithNone.
                if (opening.Glass != null && opening.Glass.Count == 0)
                {
                    Renderer made = MakeGlassForAnOpeningWithNone(opening, openings);
                    if (made != null) { opening.Glass.Add(made); s_WindowPortalMadeGlass.Add(made.gameObject); }
                }
                s_WindowPortalOpenings.Add(opening);
                s_WindowPortalQuads.Add(s_WallCamerasActive ? CreateWallQuad(opening) : CreateWindowPortalQuad(opening));
                s_WindowPortalQuadTile.Add(-1);
                s_WindowPaneCleared.Add(false);
                s_WindowPortalWasDrawn.Add(false);

                // A room standing where its building does keeps the old rule: the painted fake
                // exterior comes off, because the hole behind it shows the real outside from the
                // right place. A PARKED room does the opposite; see ShowPaintedPanesInParkedRoom.
                //
                // Note this checks activeInHierarchy as well as enabled. A MeshRenderer on an
                // INACTIVE GameObject still reports enabled = true, which is what made an earlier
                // probe call all eight backdrops visible when none of them were.
                var backdrop = opening.Backdrop;
                if (instance.Config.ParkOffset == Vector3.zero
                    && backdrop != null && backdrop.gameObject.activeInHierarchy && backdrop.enabled)
                {
                    backdrop.enabled = false;
                    s_WindowPortalHiddenBackdrops.Add(backdrop);
                }
            }
            if (instance.Config.ParkOffset != Vector3.zero) ShowPaintedPanesInParkedRoom(instance, openings);
            HideRoomObjectsByRow(instance);

            // Pair each opening with the exterior pane it stands for, so every window renders
            // from where that window actually is rather than from one offset for the whole
            // building. Needs the openings list populated, so it runs after the loop above.
            PairWindowOpeningsToShell(instance,
                ShellPosition(instance) - instance.MasterInterior.transform.position);
            if (s_WallCamerasActive) BuildWallGroups(id);

            // Until the player-facing action exists, this preference is what decides whether a
            // building starts with its panes wiped. It goes through the same per-pane call the
            // hover trigger will use, so the two can never drift apart.
            // Each pane starts in the state its window has on record, which is what makes it match
            // the outside. A window nobody has touched falls back to the preference. One with no
            // window outside starts in its own recorded state (WindowFrostState.cs).
            s_WindowPortalSnowSlabApplied = InsideOutShowSnowSlab;
            BuildingWindowOverride glassRow;
            s_WindowPortalKeepsOwnGlass = TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out glassRow) && glassRow.RoomGlassKeepsOwnMaterial;
            for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
                ApplyWindowPane(i, IsRoomPaneCleaned(id, i));

            s_WindowPortalInstanceId = id;
            BeginHidingParkedRoomShadow(instance);
            BeginWindowViewAmbient();       // see WindowViewAmbient.cs
            QueueGlassSnow(instance, id);   // see WindowSnow.BuildingToRoom.cs

            if (s_DebugBounds)
                MelonLogger.Msg($"[WINPORTAL] {id}: {openings.Count} pencere portali hazir, " +
                                $"en fazla {s_WindowPortalSlotCount} es zamanli.",
                                $"[WINPORTAL] {id}: {openings.Count} window views out of the room ready, " +
                                $"at most {s_WindowPortalSlotCount} drawn at once.");

            return true;
        }

        // ─── HOW BIG DOES THIS PORTAL ACTUALLY NEED TO BE? ───
        //
        // What makes a portal convincing is matching the TEXEL DENSITY of the view beside it, and
        // that is a property of how much SCREEN the opening covers, not of the display resolution.
        // Standing at a window it fills most of the view and wants nearly every pixel the screen
        // has. Seen across the room it covers a fifth of the width, and rendering it at full
        // resolution throws away about 96% of the work for an image that cannot show it.
        //
        // Measured 2026-09-14: one full-resolution portal held 60 fps (vsync-capped), three at
        // once hammered the GPU. Since cost is per visible opening, the multi-window case is
        // exactly where the waste concentrates, and it is also where the openings are furthest
        // away and smallest on screen. Sizing to the footprint takes the worst case apart.
        //
        // Sizes are quantized to powers of two so walking toward a window does not reallocate a
        // render texture every frame. A power-of-two ladder is its own hysteresis: leaving a
        // bucket takes a factor of two of movement, not a pixel of jitter.
        // The visible part of an opening, in its own (u, v) coordinates: u along Right from 0 to
        // Width, v along Up from 0 to Height. Returns false when nothing of it is on screen.
        private static bool TryComputeVisibleSubRect(InteriorWindowOpening opening, Vector3 eye,
                                                     out float u0, out float u1, out float v0, out float v1)
        {
            u0 = 0f; u1 = opening.Width; v0 = 0f; v1 = opening.Height;

            Vector3 corner = opening.PortalCenter
                           - opening.Right * (opening.Width * 0.5f)
                           - opening.Up * (opening.Height * 0.5f);

            s_WindowPortalPoly.Clear();
            s_WindowPortalPoly.Add(corner);
            s_WindowPortalPoly.Add(corner + opening.Right * opening.Width);
            s_WindowPortalPoly.Add(corner + opening.Right * opening.Width + opening.Up * opening.Height);
            s_WindowPortalPoly.Add(corner + opening.Up * opening.Height);

            for (int p = 0; p < s_WindowPortalPlanes.Length; p++)
            {
                ClipWindowPortalPolygon(s_WindowPortalPoly, s_WindowPortalPolyB, s_WindowPortalPlanes[p]);
                s_WindowPortalPoly.Clear();
                for (int i = 0; i < s_WindowPortalPolyB.Count; i++) s_WindowPortalPoly.Add(s_WindowPortalPolyB[i]);
                if (s_WindowPortalPoly.Count == 0) return false;
            }
            if (s_WindowPortalPoly.Count < 3) return false;

            float minU = float.MaxValue, maxU = float.MinValue;
            float minV = float.MaxValue, maxV = float.MinValue;
            for (int i = 0; i < s_WindowPortalPoly.Count; i++)
            {
                Vector3 d = s_WindowPortalPoly[i] - corner;
                float u = Vector3.Dot(d, opening.Right);
                float v = Vector3.Dot(d, opening.Up);
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            // Erring outward by a few percent costs a little shading; erring inward shows a seam
            // of the real, elevated world through the gap. The asymmetry decides the margin.
            //
            // A FRACTION OF THE VISIBLE SLICE, NOT OF THE WHOLE OPENING. It used to be
            // opening.Height * 0.03, which on the hangar's 5.38 m doors is 16.1 cm a side against
            // a slice that can be 19 cm tall - the margin was bigger than the thing it was padding,
            // and the render target's pixels went mostly outside the view. Measured 2026-09-22 at a
            // door, zoomed: 0.327 x 0.188 m visible, 1.185 x 1.013 m rendered, so 1058 texels
            // across and 402 up landed on a 3840 x 2160 screen. Owner: "This is a major defect.
            // Players WILL stand at the window and zoom in."
            //
            // The floor keeps a sliver from being padded by nothing at all, and it is absolute so
            // it cannot scale with a tall window again.
            float marginU = Mathf.Max((maxU - minU) * WINDOW_PORTAL_SUBRECT_MARGIN, WINDOW_PORTAL_SUBRECT_MARGIN_FLOOR);
            float marginV = Mathf.Max((maxV - minV) * WINDOW_PORTAL_SUBRECT_MARGIN, WINDOW_PORTAL_SUBRECT_MARGIN_FLOOR);

            u0 = Mathf.Clamp(minU - marginU, 0f, opening.Width);
            u1 = Mathf.Clamp(maxU + marginU, 0f, opening.Width);
            v0 = Mathf.Clamp(minV - marginV, 0f, opening.Height);
            v1 = Mathf.Clamp(maxV + marginV, 0f, opening.Height);

            return (u1 - u0) > 0.005f && (v1 - v0) > 0.005f;
        }

        private static void ComputeWindowPortalPixelSize(Vector3 qa, Vector3 qb, Vector3 qc, Camera main,
                                                         out int width, out int height)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            Vector3 qd = qb + (qc - qa);
            for (int i = 0; i < 4; i++)
            {
                Vector3 point = (i == 0) ? qa : (i == 1) ? qb : (i == 2) ? qc : qd;
                Vector3 viewport = LateViewportPoint(main, point);

                // A corner behind the eye projects to a mirrored, meaningless point. The opening is
                // already known to be partly on screen, so treating a behind-corner as covering the
                // whole axis is the safe reading: it can only ask for more resolution, never less.
                if (viewport.z <= 0f) { minX = 0f; maxX = 1f; minY = 0f; maxY = 1f; break; }

                if (viewport.x < minX) minX = viewport.x;
                if (viewport.x > maxX) maxX = viewport.x;
                if (viewport.y < minY) minY = viewport.y;
                if (viewport.y > maxY) maxY = viewport.y;
            }

            // Only the part on screen needs pixels.
            minX = Mathf.Clamp01(minX); maxX = Mathf.Clamp01(maxX);
            minY = Mathf.Clamp01(minY); maxY = Mathf.Clamp01(maxY);

            float scale = Mathf.Clamp(s_PrefWindowPortalResolutionScale.Value, 0.1f, 1f);
            width = RoundUpToPowerOfTwo(Mathf.RoundToInt((maxX - minX) * Screen.width * scale));
            height = RoundUpToPowerOfTwo(Mathf.RoundToInt((maxY - minY) * Screen.height * scale));

            // Never larger than the screen: past that there are no display pixels to carry it.
            width = Mathf.Clamp(width, WINDOW_PORTAL_MIN_TARGET, Mathf.Max(WINDOW_PORTAL_MIN_TARGET, Screen.width));
            height = Mathf.Clamp(height, WINDOW_PORTAL_MIN_TARGET, Mathf.Max(WINDOW_PORTAL_MIN_TARGET, Screen.height));
        }

        private static int RoundUpToPowerOfTwo(int value)
        {
            int result = WINDOW_PORTAL_MIN_TARGET;
            while (result < value) result <<= 1;
            return result;
        }

        // Builds slot `slot` the first time something needs it, and rebuilds its target when the
        // size it needs changes. Returns false if the slot cannot be made, which simply means that
        // opening goes undrawn this frame rather than the system falling over.
        private static bool EnsureWindowPortalSlot(int slot, Camera main, int width, int height)
        {
            if (slot < 0 || slot >= s_WindowPortalSlotCount) return false;

            var target = s_WindowPortalSlotTargets[slot];
            if (target != null && (target.width != width || target.height != height))
            {
                // The player changed resolution or windowed mode. Drop the stale target rather
                // than keeping a portal at the old pixel count for the rest of the session.
                if (s_WindowPortalCams[slot] != null) s_WindowPortalCams[slot].targetTexture = null;
                target.Release();
                Object.Destroy(target);
                s_WindowPortalSlotTargets[slot] = null;
                target = null;
            }

            if (target == null)
            {
                // HDR is required: an LDR target double-tonemaps and blows the sky out. The alpha
                // channel is not, because the quad is opaque, so prefer the packed float format
                // and spend half the memory of ARGBHalf for the same result.
                RenderTextureFormat format =
                    SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float)
                        ? RenderTextureFormat.RGB111110Float
                        : RenderTextureFormat.ARGBHalf;

                target = new RenderTexture(width, height, 24, format);
                target.name = "SI_WindowPortalTarget" + slot;
                if (!target.Create())
                {
                    Object.Destroy(target);
                    return false;
                }
                // Kept across a visit (see WINDOW_TARGETS_KEPT_SECONDS), so a region load must not
                // unload it; the mod releases it itself.
                target.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                s_WindowPortalSlotTargets[slot] = target;
            }

            // Outside the block above: a target taken back from the last visit needs its material
            // too, and the material is made per visit.
            if (s_WindowPortalSlotMaterials[slot] == null)
            {
                var material = new Material(Shader.Find("Unlit/Texture"));
                material.renderQueue = WINDOW_VIEW_OUT_RENDER_QUEUE;
                s_WindowPortalSlotMaterials[slot] = material;
            }
            if (s_WindowPortalSlotMaterials[slot].mainTexture != target) s_WindowPortalSlotMaterials[slot].mainTexture = target;

            var cam = s_WindowPortalCams[slot];
            if (cam == null)
            {
                var camObject = new GameObject("SI_WindowPortalCamera" + slot);
                cam = camObject.AddComponent<Camera>();

                // A COPY OF THE PLAYER'S CAMERA, then only what the view needs to differ. The owner's
                // rule (2026-09-28): "as close to the same as a player camera as possible." Built
                // setting by setting before, it drifted (forward rendering, the MSAA, event mask and
                // render-texture flags); copied in the console at the hangar, every camera setting read
                // the same as FPSCamera's except the ones below, and "Nothing seems different".
                // Rendering path, fog and the player's world-drawing buffers: WindowViewFog.cs.
                if (main != null)
                {
                    cam.CopyFrom(main);
                    cam.targetTexture = null;
                    cam.ResetWorldToCameraMatrix();
                    cam.ResetProjectionMatrix();
                    cam.ResetCullingMatrix();
                    cam.cullingMask = main.cullingMask & ~(1 << WINDOW_PORTAL_QUAD_LAYER);   // not the window sheets themselves
                }
                else
                {
                    cam.clearFlags = CameraClearFlags.Skybox;
                    cam.allowHDR = true;              // LDR here double-tonemaps and blows out
                }

                // ENABLED, and rendered by Unity rather than by us. A negative depth puts it
                // before the player's view. Each slot gets its own depth so the order is defined.
                cam.enabled = false;
                cam.depth = -50f - slot;
                cam.useOcclusionCulling = false;  // baked for the player's viewpoint, wrong from here

                s_WindowPortalCams[slot] = cam;
            }

            if (cam.targetTexture != target) cam.targetTexture = target;
            return true;
        }

        // Room objects the building's row switches off for as long as the room is set up
        // (BuildingWindowOverride.RoomObjectsHidden), on the same list as the backdrops hidden above,
        // so the teardown puts them back. The Small Barn's painted outside lit the gaps round a
        // shuttered window and the door crack like daylight at midnight (owner, 2026-10-01).
        // Something later in the room's set-up switches them on again (seen 2026-10-01: hidden by the
        // set-up, on the backdrop list, and back on when the owner looked). Rather than chase it, they
        // are held off every frame by KeepRowHiddenRoomObjectsOff; a handful of renderers at most.
        private static readonly List<Renderer> s_RowHiddenRoomObjects = new List<Renderer>();

        internal static void KeepRowHiddenRoomObjectsOff()
        {
            for (int i = s_RowHiddenRoomObjects.Count - 1; i >= 0; i--)
            {
                var renderer = s_RowHiddenRoomObjects[i];
                if (renderer == null) { s_RowHiddenRoomObjects.RemoveAt(i); continue; }
                if (renderer.enabled) renderer.enabled = false;
            }
        }

        private static void HideRoomObjectsByRow(SeamlessInteriorInstance instance)
        {
            s_RowHiddenRoomObjects.Clear();
            BuildingWindowOverride row;
            if (instance == null || instance.Config == null || instance.MasterInterior == null
                || !TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out row) || row.RoomObjectsHidden == null) return;
            int hidden = 0;
            foreach (var renderer in instance.MasterInterior.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || System.Array.IndexOf(row.RoomObjectsHidden, renderer.gameObject.name) < 0) continue;
                renderer.enabled = false;
                s_WindowPortalHiddenBackdrops.Add(renderer);
                s_RowHiddenRoomObjects.Add(renderer);
                hidden++;
            }
            if (s_DebugBounds)
                MelonLogger.Msg($"[WINPORTAL] {instance.Config.InstanceId}: binanin satiriyla {hidden} oda nesnesi gizlendi.",
                                $"[WINPORTAL] {instance.Config.InstanceId}: {hidden} room objects hidden by the building's row.");
        }

        // ─── A WINDOW WITH NO VIEW SHOWS THE GAME'S PAINTED PANE ───
        //
        // A parked room stands ParkOffset above its building, so a window that has no camera this
        // frame, with its painted backdrop off, is a hole onto the real world seen from 40 m up:
        // wrong parallax, and a view over things it should not see over. Measured at the hangar on
        // 2026-09-22: 42 windows, at most 10 drawn, and all three of its painted backdrops off, two
        // by this mod and one by the building's own config. Owner: "should we be flipping between
        // the portal view and the texture the vanilla game has on the inside of that window with no
        // cutout?" Yes, and it needs no flipping.
        //
        // EVERY BACKDROP STAYS ON, AND A DRAWN VIEW COVERS ITS OWN. The view's quad stands a couple
        // of millimeters inside the backdrop (PORTAL_PLANE_CLEARANCE; MANY_PANE_VIEW_OUTSIDE_GLASS on
        // the many-pane path) and writes depth, so wherever a window is drawn the backdrop behind it
        // fails the depth test, and wherever it is not, the painted pane shows. This matters because
        // switching per window is impossible where it counts: the hangar's two side backdrops are
        // one renderer for 17 windows each. Tried in the console first, owner standing inside with
        // 10 of 20 visible windows drawn: undrawn windows showed the painted pane, and the drawn
        // views stayed clean with nothing of the pane showing through them.
        //
        // Only objects that are the backdrop itself or named in the building's own disable list are
        // switched on. Anything else inactive above a backdrop was switched off for another reason
        // and is left alone, which leaves that backdrop off too.
        private static void ShowPaintedPanesInParkedRoom(SeamlessInteriorInstance instance, List<InteriorWindowOpening> openings)
        {
            HashSet<string> disableSet = instance.Config.DisableSet;
            var seen = new HashSet<int>();
            int shown = 0, left = 0;
            for (int i = 0; i < openings.Count; i++)
            {
                var backdrop = openings[i].Backdrop;
                if (backdrop == null || !seen.Add(backdrop.GetInstanceID())) continue;

                var toActivate = new List<GameObject>();
                bool blocked = false;
                for (Transform t = backdrop.transform; t != null; t = t.parent)
                {
                    if (t.gameObject.activeSelf) continue;
                    if (t != backdrop.transform && !disableSet.Contains(t.name)) { blocked = true; break; }
                    toActivate.Add(t.gameObject);
                }
                if (blocked) { left++; continue; }

                // Outermost first, so each object comes on under a parent that is already on.
                for (int k = toActivate.Count - 1; k >= 0; k--)
                {
                    toActivate[k].SetActive(true);
                    s_WindowPortalActivatedBackdrops.Add(toActivate[k]);
                }
                if (!backdrop.enabled)
                {
                    backdrop.enabled = true;
                    s_WindowPortalShownBackdrops.Add(backdrop);
                }
                shown++;
            }

            if (s_DebugBounds || left > 0)
                MelonLogger.Msg($"[WINPORTAL] {instance.Config.ResolvedInstanceId}: gorunumu olmayan pencereler oyunun boyali camini gosteriyor: {shown} boyali arka plan acik" +
                                (left > 0 ? $", {left} tanesi baska bir sebeple kapatilmis bir nesnenin altinda oldugu icin kapali birakildi." : "."),
                                $"[WINPORTAL] {instance.Config.ResolvedInstanceId}: windows with no view show the game's painted pane: {shown} painted backdrops on" +
                                (left > 0 ? $", {left} left off because they sit under an object switched off for some other reason." : "."));
        }

        private static GameObject CreateWindowPortalQuad(InteriorWindowOpening opening)
        {
            var quad = new GameObject("SI_WindowPortalQuad");
            quad.layer = WINDOW_PORTAL_QUAD_LAYER;

            // Vertices are world space with the object left at the origin, so the quad needs no
            // transform maintenance and cannot drift from the opening it belongs to.
            Vector3 pa = opening.PortalCenter
                       - opening.Right * (opening.Width * 0.5f)
                       - opening.Up * (opening.Height * 0.5f);
            Vector3 pb = pa + opening.Right * opening.Width;
            Vector3 pc = pa + opening.Up * opening.Height;
            Vector3 pd = pb + (pc - pa);

            var mesh = new Mesh();
            mesh.vertices = new Vector3[] { pa, pc, pd, pb };
            // The whole target belongs to this quad while it holds a slot, so the UVs are the full
            // range and never change. They were per-tile while the targets were shared.
            mesh.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var filter = quad.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = quad.AddComponent<MeshRenderer>();
            // No material yet: it gets the material of whichever slot it is given, per frame.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;

            return quad;
        }

        // A ROOM IS COPIED FROM ANOTHER ROOM, AND THE COPY TAKES WHATEVER THE FIRST ONE IS WEARING.
        // Buildings that share an interior (Lake Cabins A_1, A_2, A_3) are Instantiate copies of the
        // first one loaded. On a save made INSIDE that first one, the views out of its windows are
        // set up before the copies are taken, so the copies' glass wore this file's frost material.
        // The window finder knows glass by the game's own material, so it found 0 windows in them:
        // no room turn, no windows from outside. When the player then walked out the frost material
        // was destroyed and the copies' glass held no material at all, for the rest of the session.
        // Seen 2026-09-18 in A_1 and A_3 on a save made in A_2. Taking the views down first hands
        // the copy the room as the game made it; TickWindowPortals sets them up again next frame.
        public static void ReleaseRoomBeforeItIsCopied(GameObject room)
        {
            if (room == null || s_WindowPortalInstanceId == null) return;

            SeamlessInteriorInstance wearer;
            if (ActiveInteriors.TryGetValue(s_WindowPortalInstanceId, out wearer)
                && wearer != null && wearer.MasterInterior != room) return;

            if (s_DebugBounds)
                MelonLogger.Msg($"[WINPORTAL] {s_WindowPortalInstanceId}: oda kopyalanmadan once pencere gorunumleri kaldirildi, kopya odanin kendi camini alsin diye.",
                                $"[WINPORTAL] {s_WindowPortalInstanceId}: the views out of the windows were taken down before the room is copied, so the copy gets the room's own glass.");
            TeardownWindowPortals();
        }

        public static void TeardownWindowPortals()
        {
            // Each step stands alone. One shared try/catch would abandon the rest of the cleanup
            // at the first failure, and a half-removed portal system - backdrops still hidden, say,
            // with the camera already gone - is a worse state than either end of the operation.

            try { EndHidingParkedRoomShadow(); } catch { }
            try { EndWindowViewAmbient(); } catch { }
            try { TeardownWallCameras(); } catch { }
            try { TeardownViewMotion(); } catch { }
            try { TeardownGlassSnow(); } catch { }
            try { TeardownWindowCoverLayers(); } catch { }

            s_RowHiddenRoomObjects.Clear();   // before the list below switches them back on
            for (int i = 0; i < s_WindowPortalHiddenBackdrops.Count; i++)
            {
                var backdrop = s_WindowPortalHiddenBackdrops[i];
                // Unity's overloaded == is what detects a destroyed native object, so this is
                // deliberately not a null-reference check: the clone may have been torn down with
                // its backdrops still on our list.
                if (backdrop != null) backdrop.enabled = true;
            }
            s_WindowPortalHiddenBackdrops.Clear();

            for (int i = 0; i < s_WindowPortalShownBackdrops.Count; i++)
                if (s_WindowPortalShownBackdrops[i] != null) s_WindowPortalShownBackdrops[i].enabled = false;
            s_WindowPortalShownBackdrops.Clear();
            // Innermost first, the reverse of the order they were switched on in.
            for (int i = s_WindowPortalActivatedBackdrops.Count - 1; i >= 0; i--)
                if (s_WindowPortalActivatedBackdrops[i] != null) s_WindowPortalActivatedBackdrops[i].SetActive(false);
            s_WindowPortalActivatedBackdrops.Clear();
            s_WindowPortalWasDrawn.Clear();

            // The glass gets its own material back first. The clone outlives the portal system, so a
            // pane left wearing ours would keep a destroyed material for the rest of the save.
            for (int i = 0; i < s_WindowPortalFrostedGlass.Count && i < s_WindowPortalFrostedGlassSaved.Count; i++)
            {
                var glassRenderer = s_WindowPortalFrostedGlass[i];
                if (glassRenderer != null) glassRenderer.sharedMaterials = s_WindowPortalFrostedGlassSaved[i];
            }
            s_WindowPortalFrostedGlass.Clear();
            s_WindowPortalFrostedGlassSaved.Clear();
            if (s_WindowPortalFrostMaterial != null) Object.Destroy(s_WindowPortalFrostMaterial);
            s_WindowPortalFrostMaterial = null;

            // The panes made for openings with no glass are ours, and go with the views.
            for (int i = 0; i < s_WindowPortalMadeGlass.Count; i++)
            {
                var made = s_WindowPortalMadeGlass[i];
                if (made == null) continue;
                var madeFilter = made.GetComponent<MeshFilter>();
                if (madeFilter != null && madeFilter.sharedMesh != null) Object.Destroy(madeFilter.sharedMesh);
                Object.Destroy(made);
            }
            s_WindowPortalMadeGlass.Clear();

            // Put the glass and frost back on every pane this cleared. The clone outlives the
            // portal system - it is only closed, not destroyed - so a pane left cleared here is a
            // window that stays wiped for the rest of the save with nothing tracking it.
            //
            // EVERY pane, not only the cleared ones: the snow slab lever can hide the slab on a pane
            // that was never cleared, and that pane has to get it back too.
            for (int i = 0; i < s_WindowPortalOpenings.Count; i++)
            {
                SetRenderersEnabled(s_WindowPortalOpenings[i].Glass, true);
                SetRenderersEnabled(s_WindowPortalOpenings[i].Frost, true);
            }
            s_WindowPortalSnowSlabApplied = true;
            s_WindowPaneCleared.Clear();
            s_WindowPortalSpot.Clear();
            s_WindowPortalViewStart.Clear();
            s_WindowPortalDelta.Clear();
            s_WindowPortalPairedCount = 0;

            for (int i = 0; i < s_WindowPortalQuads.Count; i++)
            {
                var quad = s_WindowPortalQuads[i];
                if (quad == null) continue;

                var filter = quad.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
                Object.Destroy(quad);
            }
            s_WindowPortalQuads.Clear();
            s_WindowPortalQuadTile.Clear();
            s_WindowPortalOpenings.Clear();

            if (s_WindowPortalCams != null)
            {
                for (int i = 0; i < s_WindowPortalCams.Length; i++)
                {
                    var cam = s_WindowPortalCams[i];
                    if (cam == null) continue;
                    // Cleared before destroying the camera: a camera still pointing at a render
                    // texture that is about to be released is the ordering that produces a
                    // "RenderTexture in use" warning and, on some drivers, a frame of garbage.
                    cam.targetTexture = null;
                    Object.Destroy(cam.gameObject);
                }
                s_WindowPortalCams = null;
            }

            if (s_WindowPortalSlotTargets != null)
            {
                // Kept for the next visit, not released: see WINDOW_TARGETS_KEPT_SECONDS. The
                // cameras above were already pointed away from them.
                ReleaseKeptWindowTargets();
                s_KeptWindowTargets = s_WindowPortalSlotTargets;
                s_KeptWindowTargetsSince = Time.realtimeSinceStartup;
                s_WindowPortalSlotTargets = null;
            }

            if (s_WindowPortalSlotMaterials != null)
            {
                for (int i = 0; i < s_WindowPortalSlotMaterials.Length; i++)
                    if (s_WindowPortalSlotMaterials[i] != null) Object.Destroy(s_WindowPortalSlotMaterials[i]);
                s_WindowPortalSlotMaterials = null;
            }

            s_WindowPortalSlotCount = 0;
            s_WindowPortalInstanceId = null;
            s_WindowPortalKeepsOwnGlass = false;
        }
    }
}
