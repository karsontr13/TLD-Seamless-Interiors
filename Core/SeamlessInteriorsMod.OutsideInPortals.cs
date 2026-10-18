using Il2Cpp;
using MelonLoader;
using System.Collections.Generic;
using UnityEngine;

namespace SeamlessInteriors
{
    public partial class SeamlessInteriorsMod
    {
        // ─── SEEING THE REAL INTERIOR THROUGH A SHELL WINDOW, FROM OUTSIDE ───
        //
        // The player stands in the yard, looks at a window in the building's exterior shell, and
        // sees the actual interior behind it instead of a painted pane. This is the OPPOSITE
        // direction to the retired inside-out portals (kept on branch feature-window-portals) and
        // it is the direction that survives, because it needs no parked interior.
        //
        // WHY A WINDOW NEEDS NO MESH SURGERY. The quad is opaque and sits in the shell's window
        // opening, so it simply draws the interior over whatever the shell's own pane shows.
        // Nothing has to be cut out of the shell. The door is different and will need a real hole -
        // the 2026-09-13 proof removed the leaf, frame and handle triangles from
        // STR_CampOfficeBase_LOD0/_LOD1 and rebuilt the collider front wall as three rectangles -
        // because the player walks through a door. A window is only ever looked through.
        //
        // ─── THE THREE PIECES OF STATE THIS BORROWS, AND GIVES BACK ───
        //
        // Every one of these is someone else's, so each is captured on setup and restored exactly
        // on teardown. A half-removed portal system is worse than either end of the operation.
        //
        //   1. THE CLONE IS ALIVE WHILE THE PLAYER IS OUTSIDE. Normally HideInteriorCompletely
        //      switches MasterInterior off the moment the player leaves. There is nothing to
        //      render in that state, so this activates it. Note a plain SetActive(true) is NOT
        //      enough on its own: it leaves all of the clone's item renderers off, which is why
        //      the mod has SetInteriorItemsVisible at all.
        //
        //   2. THE INTERIOR MOVES TO ITS OWN LAYER. Interior and shell are co-located, so an
        //      active clone would interpenetrate its own shell in the main camera's view. The
        //      interior therefore goes to a layer the MAIN camera does not draw and the PORTAL
        //      camera draws exclusively. Layers 3 and 7 are the two the game leaves unused, 7 is
        //      already the quad layer, so the interior takes 3.
        //
        //      **The main camera draws layer 3 by default** - FPSCamera's mask measured 490708959
        //      on 2026-09-15, which has bit 3 set - so its culling mask has to be edited. That is
        //      state belonging to the game and it is restored on teardown.
        //
        //      A LAYER BELONGS TO THE GAMEOBJECT, NOT THE RENDERER. Unity has no per-renderer
        //      layer, so relayering for the camera necessarily moves that object's collider too.
        //      An earlier comment here claimed the opposite and the code could never have honored
        //      it. What makes it safe is not the relayering but the collision sweep below, which
        //      switches the clone's colliders off outright while the player is outside.
        //
        //   3. THE VISIBILITY WATCHDOG MUST NOT CLOSE THE CLONE while this holds it open.
        //
        //   4. THE CLONE'S COLLISION IS SUPPRESSED, EVERY FRAME. See s_OutsideInDisabledColliders.
        //
        // ─── WHAT IS DELIBERATELY NOT DONE YET ───
        //
        // Interior lighting is not restored. In the mod's default Outdoor Lighting mode the clone's
        // lightmaps and 98 of its 103 lights are destroyed at region load, so the room is lit only
        // by the per-view ambient swap around each render. Restoring the real lights wants a
        // Light.cullingMask on the interior's own layer, which the layer split above already makes
        // possible - with the caveat that TLD renders DeferredShading and Unity's deferred path
        // supports only a limited number of light culling masks. Untested.

        // The interior's private layer while a portal is showing it. Unused by the game.
        private const int OUTSIDE_IN_INTERIOR_LAYER = 3;

        // One layer per held room, so a camera draws only its own room: rooms of close neighbors
        // overlap at park height (Milton H2/F3, 2026-10-09). 4 holds no objects in the game; 30 only the
        // player's renderer-less trigger capsule. Not 28: Pleasant Valley's frozen river is on it.
        private static readonly int[] OUTSIDE_IN_ROOM_LAYERS = { OUTSIDE_IN_INTERIOR_LAYER, 4, 30 };
        private static readonly int OUTSIDE_IN_ROOM_LAYERS_MASK = (1 << 3) | (1 << 4) | (1 << 30);

        // A pool layer no other held room uses; the shared layer 3 when all are taken.
        private static int PickOutsideInRoomLayer(OutsideInBuilding building)
        {
            for (int l = 0; l < OUTSIDE_IN_ROOM_LAYERS.Length; l++)
            {
                bool taken = false;
                for (int h = 0; h < s_OutsideInBuildings.Count && !taken; h++)
                {
                    var other = s_OutsideInBuildings[h];
                    taken = other != building && other.Borrowed && other.Layer == OUTSIDE_IN_ROOM_LAYERS[l];
                }
                if (!taken) return OUTSIDE_IN_ROOM_LAYERS[l];
            }
            return OUTSIDE_IN_INTERIOR_LAYER;
        }

        // ─── TWO LIFETIMES, BECAUSE THE PARTS DO NOT COST THE SAME ───
        //
        // THE PORTALS are expensive: an open clone, a relayered interior, an edited camera mask, a
        // collider sweep every frame and a scene render per visible pane. They are paid for only
        // while a window is big enough on screen to read.
        //
        // THE DRESSING is nearly free: the shell's painted panes hidden, a dark backing in every
        // window, the frost overlays and the drifts on the ledges. It is static geometry, and it
        // needs the clone only as something to copy from, never open.
        //
        // Both used to share the portals' range, then 25 m, and the owner saw the result on 2026-09-18: "when
        // you are a decent distance away from the building, the portals stop working and the snow
        // drifts disappear." Every window popped from recessed with a drift to flush and painted.
        // Past portal range a window now keeps its shape and shows dark glass under its frost,
        // which is what a window into an unlit room looks like from across a yard.

        // How near the player must be to a shell before its windows are worth rendering, in meters.
        // It was a constant 25 until the owner walked away from the Camp Office on 2026-09-18 with
        // the dressing fixed and said the room should stay visible "a little further out than 30
        // meters". Judged by eye, so it is a preference and a live dial rather than a rebuild.
        private static MelonPreferences_Entry<float> s_PrefOutsideInPortalRange;
        public static float OutsideInPortalRange
        {
            get { return s_PrefOutsideInPortalRange != null ? Mathf.Clamp(s_PrefOutsideInPortalRange.Value, 5f, OUTSIDE_IN_DRESSING_RANGE) : 100f; }
            set { if (s_PrefOutsideInPortalRange != null) s_PrefOutsideInPortalRange.Value = value; }
        }

        // Portals already drawing are kept this much further out, so a player pacing along the line
        // does not flick them on and off. (Crossing it used to close or reopen the whole clone as
        // well; the room now has its own, much longer range. See OUTSIDE_IN_ROOM_HOLD_RANGE.)
        private const float OUTSIDE_IN_RELEASE_MARGIN = 5f;

        // How near before the nearest supported building is dressed. NOT JUDGED BY EYE YET: chosen
        // so a meter-wide window is about a third of a degree across when its dressing changes.
        private const float OUTSIDE_IN_DRESSING_RANGE = 150f;

        // Minimum eye-to-aperture distance. Below this the off-axis frustum degenerates.
        private const float OUTSIDE_IN_MIN_EYE_DISTANCE = 0.05f;

        private const float OUTSIDE_IN_FAR_CLIP = 500f;      // an interior is small; this is generous

        // HOW FAR PROUD OF THE PANE THE QUAD SITS. POSITIVE IS TOWARD THE PLAYER.
        //
        // s_ShellWindows centres are read from the shell's STR_WindowGlassMix submesh, so a quad
        // placed exactly there is COPLANAR with the glass and the two z-fight - reported from the
        // yard on 2026-09-15 as "most portals are z-fighting with the glass pane". Depth buffer
        // precision cannot separate two surfaces at the same depth, and no render queue fixes it
        // because the problem is the depth test rather than the draw order.
        //
        // IT MUST GO IN FRONT, NOT BEHIND, AND THAT WAS MEASURED. Putting it behind the pane was
        // tried first because keeping the shell's own glass over the portal would preserve its
        // frost and reflections. It goes black: the shell's pane is OPAQUE. Measured 2026-09-15 -
        // STR_CampOfficeBase_LOD0 submesh 3 carries STR_WindowGlassMix_A01_Ext on
        // Shader Forge/TLD_StandardComplexProp at queue 2000. The INTERIOR's glass is the
        // transparent one (STR_WindowGlassMix_A01_ExtAlpha, TLD_StandardTransparent, queue 3000),
        // which is why the retired inside-out portal could sit behind its pane and this one cannot.
        //
        // Nor is there a plate to switch off. The interior's fake backdrop is a separate object
        // (OBJ_LakeCabinInteriorWindow) which the author simply disables; the shell's opaque pane
        // is a SUBMESH sharing one renderer with the walls, door and roof.
        //
        // The same 2 cm the 2026-09-13 door proof used. Small enough to stay inside the window
        // reveal so it does not float proud of the frame at a grazing angle.
        private const float OUTSIDE_IN_QUAD_OUTSET = 0.02f;

        // ─── KEEPING THE SHELL'S PAINTED FROST OVER THE VIEW, ONE WINDOW AT A TIME ───
        //
        // The snow and frost a player sees on a shell window is PAINTED: it is the pane's texture,
        // STR_WindowGlassMixExt_A, drawn opaque on a submesh of the wall mesh. The portal quad sits
        // 2 cm in front of that pane, so the painted snow goes with it. The game also ships a
        // see-through twin of the texture, STR_WindowGlassMixExtApha_A, worn by the interior's glass.
        //
        // Each paired window gets an OVERLAY: that pane's own triangles, lifted out of the shell's
        // glass submesh with their real UVs, drawn just in front of the portal quad with the twin
        // texture. Per window, because the owner's requirement is that a window is frosted or
        // cleaned as ONE thing, matching from inside and out (see WindowFrostState.cs), and the
        // shell's panes are one material on one submesh, which can only be changed all together.
        //
        // This replaces a first version that swapped that shared material and moved the quad behind
        // the pane. It worked, and the owner saw it draw, but it was all-or-nothing per building, it
        // turned any pane without a portal into a hole onto the hollow shell, and at an inset of
        // zero it z-fought. The opaque pane now never changes, so all three are gone.
        //
        // THE GAME'S OWN TRANSPARENT SHADER DOES NOT WORK HERE, AND THAT WAS MEASURED on 2026-09-18.
        // Lending the shell the interior glass's whole material, on
        // Shader Forge/TLD_StandardTransparent, drew nothing visible: not the frost, and not a solid
        // red _Color either. A plain red material on Legacy Shaders/Transparent/Diffuse on the same
        // submesh drew over the room at once, which cleared geometry, facing, depth and draw order
        // and left the shader. The twin texture's alpha runs 0.52 to 0.97, so it was never empty.
        // This also explains the 2026-09-15 result that a see-through pane "contributed nothing".
        //
        // Needs a readable shell mesh. Both Camp Office LODs measured readable, against a comment
        // in ExteriorFireEffects.cs that says shell meshes are not. A building whose mesh is not
        // readable gets no overlays, and its portals show the room unfrosted.
        // Backed by a preference so a value dialed in from the tuning panel survives the session.
        // A plain static field went back to this default at every launch.
        private static MelonPreferences_Entry<bool> s_PrefOutsideInFrostedPane;
        public static bool OutsideInFrostedPane
        {
            get { return s_PrefOutsideInFrostedPane != null ? s_PrefOutsideInFrostedPane.Value : true; }
            set { if (s_PrefOutsideInFrostedPane != null) s_PrefOutsideInFrostedPane.Value = value; }
        }

        // Overall strength of the painted frost, multiplied into the texture's own alpha.
        //
        // The texture is heavy on its own: measured 0.52 in its clearest areas and 0.97 at the
        // frosted edges. 0.5 was set by eye by the owner with a live dial on 2026-09-18, which puts
        // the middle of a pane near 26% and its edges near 49%. From INSIDE the same texture wanted
        // full strength (see WindowPortals.cs), so the two are separate on purpose.
        // Backed by a preference so a value dialed in from the tuning panel survives the session.
        // A plain static field went back to this default at every launch.
        private static MelonPreferences_Entry<float> s_PrefOutsideInFrostStrength;
        public static float OutsideInFrostStrength
        {
            get { return s_PrefOutsideInFrostStrength != null ? s_PrefOutsideInFrostStrength.Value : 0.5f; }
            set { if (s_PrefOutsideInFrostStrength != null) s_PrefOutsideInFrostStrength.Value = value; }
        }

        // How far in front of the portal quad an overlay sits, in meters. Far enough not to z-fight
        // with the quad across the range the portals draw at, near enough not to float off the wall.
        // Past that range it stands 7 mm in front of the backing, at sizes too small to show a fight.
        private const float OUTSIDE_IN_FROST_PROUD_OF_QUAD = 0.005f;

        // How far outside the room's box a pretend window's eye may still land (it stands in the wall).
        private const float PRETEND_EYE_IN_ROOM = 0.5f;

        // How far outside a pane's nominal rectangle a triangle's middle may lie and still belong
        // to it. Panes are split by glazing bars, so a window is several quads, not one.
        private const float OUTSIDE_IN_FROST_PANE_MARGIN = 0.12f;

        private const string OUTSIDE_IN_FROST_SHADER = "Legacy Shaders/Transparent/Diffuse";

        // ─── THE FROST TAKES SHADOWS, AND THE GLASS BEHIND IT IS TINTED ───
        //
        // A see-through layer is lit and never shadowed, so under a porch roof the frost glowed
        // white beside a window still wearing the building's own pane, which sat in the roof's
        // shade like the wall around it. Owner, 2026-10-10, of two such windows side by side: "The
        // frost on windows 2 is super white while the frost on 3 is subdued, and almost dark blue in
        // the middle, like glass", and before that: "the window frost is creating a bright layer in
        // front of the window cameras instead of darkening what comes through like the native one".
        //
        // So the frost is drawn with the game's own cut-out shader, which takes shadows and cannot
        // blend: a spot of the sheet is drawn solid or left out. The building's see-through sheet has
        // no clear part at all (measured: the glassy middle is 50 to 70% solid, the frost 80 to 100%),
        // so the cut is set between the two and only the frost is drawn. At 35% the whole pane was
        // drawn and the room was hidden ("Still no camera inside though").
        //
        // That leaves the middle perfectly clear ("Now back to too transparent"), so a dark tint
        // stands 2.5 mm behind the frost: it darkens the room's picture in the clear part of the
        // pane and the frost in front of it hides it everywhere else.
        //
        // The shader is not found by name (Shader.Find gives nothing for it) and is looked for once
        // among the loaded ones. Without it the frost is the blended layer it was before.
        private const string OUTSIDE_IN_FROST_SHADOWED_SHADER = "_LongDark/Transparent/Cutout/Diffuse";
        private const float OUTSIDE_IN_GLASS_TINT_BEHIND_FROST = 0.0025f;
        //
        // OFF. He judged it live and went back: cut out, the pane lost the building sheet's own
        // glassy middle, which is what he had liked that morning ("Opacity looks much more
        // realistic"), and the tint behind it did not get a verdict. What he asked for on the
        // blended frost, beside the same original pane in the same shade, was only "still a bit too
        // bright": OutsideInFrostBrightness, which darkens the whole sheet, frost and glass alike,
        // in either kind. It is one number for sun and shade both, which the cut-out kind would not
        // need; that is the trade, and the cut-out kind is kept behind this switch for it.
        internal static bool OutsideInFrostTakesShadows = false;
        internal static float OutsideInFrostCutoff = 0.75f;
        //
        // 36% brightness, with the sheet's contrast at 170% (below) and the strength at full, are his
        // own slider values at the Pleasant Valley farmhouse, 2026-10-10, beside a window still
        // wearing the building's pane: "TUNED! That worked beautifully". That was in the shade of its
        // porch at dawn. At Thompson's Store the same day, once the sheet there was no longer coarse,
        // he set them again: 76% brightness and 125% contrast, which is what stands.
        internal static float OutsideInFrostBrightness = 0.76f;
        internal static float OutsideInGlassTint = 0.5f;
        internal static Color OutsideInGlassTintColor = new Color(0.03f, 0.04f, 0.07f);
        private static Shader s_OutsideInFrostShadowedShader;
        private static bool s_OutsideInFrostShadowedShaderLooked;
        private static Material s_OutsideInGlassTintMaterial;

        // ─── THE FROST SHEET'S CONTRAST ───
        //
        // Owner, 2026-10-10, with the sheet at 60% brightness: "Any way to increase the contrast a
        // bit?" The frost's shader has no such dial, so the frost wears a COPY of its sheet with the
        // colors spread about the middle: frost lighter, glass darker. The see-through part of the
        // sheet is left as it is. A copy is made once for each sheet (104 ms for the building's
        // 1024 x 1024 one, measured) and again only when the number has changed and then stood still
        // for a moment, so a dragged slider costs one copy and not one for every step. 1 is the sheet
        // as painted, and no copy is made.
        //
        // THE GAME STREAMS THE SHEET, and a copy made while only a coarse level of it is loaded is
        // coarse for good. Owner, 2026-10-10, at Thompson's Store after tuning at the farmhouse: "The
        // frost is extemely low resolution"; read there: the sheet streaming, level 1 of 11 loaded,
        // level 2 wanted. So the full sheet is asked for first, and until it has come the frost wears
        // the sheet as painted and the copy is tried again a quarter of a second later. After three
        // seconds of waiting the copy is made from what there is.
        internal static float OutsideInFrostContrast = 1.25f;
        private const float OUTSIDE_IN_FROST_SHEET_WAIT_SECONDS = 3f;
        private static float s_OutsideInFrostSheetWaitingSince = -1f;
        private static readonly Dictionary<int, Texture2D> s_OutsideInFrostContrastSheets = new Dictionary<int, Texture2D>();
        private static readonly List<Texture2D> s_OutsideInFrostContrastRetired = new List<Texture2D>();
        private static float s_OutsideInFrostContrastSheetsAt = 1f;
        private static float s_OutsideInFrostContrastSeen = -1f, s_OutsideInFrostContrastSettlesAt;

        // `ready` is false while the full sheet is still coming: what is returned is then the sheet
        // as painted, and the caller asks again.
        private static Texture OutsideInFrostSheetWithContrast(Texture source, out bool ready)
        {
            ready = true;
            float contrast = Mathf.Clamp(OutsideInFrostContrast, 0f, 4f);
            if (source == null || Mathf.Abs(contrast - 1f) < 0.005f) return source;

            if (s_OutsideInFrostContrastSheetsAt != contrast)
            {
                // The copies before last go now: another building's frost may wear the last ones
                // until its own next frame.
                foreach (var old in s_OutsideInFrostContrastRetired) if (old != null) Object.Destroy(old);
                s_OutsideInFrostContrastRetired.Clear();
                s_OutsideInFrostContrastRetired.AddRange(s_OutsideInFrostContrastSheets.Values);
                s_OutsideInFrostContrastSheets.Clear();
                s_OutsideInFrostContrastSheetsAt = contrast;
            }

            int key = source.GetInstanceID();
            Texture2D made;
            if (s_OutsideInFrostContrastSheets.TryGetValue(key, out made) && made != null) return made;

            var streamed = source.TryCast<Texture2D>();
            if (streamed != null && streamed.streamingMipmaps && streamed.loadedMipmapLevel > 0)
            {
                if (streamed.requestedMipmapLevel != 0) streamed.requestedMipmapLevel = 0;
                if (s_OutsideInFrostSheetWaitingSince < 0f) s_OutsideInFrostSheetWaitingSince = Time.unscaledTime;
                if (Time.unscaledTime - s_OutsideInFrostSheetWaitingSince < OUTSIDE_IN_FROST_SHEET_WAIT_SECONDS) { ready = false; return source; }
            }
            s_OutsideInFrostSheetWaitingSince = -1f;

            try
            {
                int width = source.width, height = source.height;
                // The game's sheet cannot be read where it is, so it is drawn into a texture that can.
                var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, target);
                var before = RenderTexture.active;
                RenderTexture.active = target;
                made = new Texture2D(width, height, TextureFormat.RGBA32, true, false);
                made.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);

                var spread = new byte[256];
                for (int v = 0; v < 256; v++) spread[v] = (byte)Mathf.RoundToInt(Mathf.Clamp01((v / 255f - 0.5f) * contrast + 0.5f) * 255f);
                var pixels = made.GetPixels32(0);
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    pixel.r = spread[pixel.r]; pixel.g = spread[pixel.g]; pixel.b = spread[pixel.b];
                    pixels[i] = pixel;
                }
                made.SetPixels32(pixels, 0);
                made.Apply(true, false);
                made.name = "SeamlessInteriors_FrostSheet";
                made.wrapMode = source.wrapMode;
                made.filterMode = source.filterMode;
                made.anisoLevel = source.anisoLevel;
                s_OutsideInFrostContrastSheets[key] = made;
                // The game goes back to streaming its sheet as it sees fit; the copy is not streamed.
                if (streamed != null && streamed.streamingMipmaps) streamed.ClearRequestedMipmapLevel();
                return made;
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"[OUTSIDE-IN] The frost sheet could not be copied for its contrast and is used as painted: {e.Message}");
                return source;
            }
        }

        private static Shader FindOutsideInShadowedFrostShader()
        {
            if (s_OutsideInFrostShadowedShader != null || s_OutsideInFrostShadowedShaderLooked) return s_OutsideInFrostShadowedShader;
            s_OutsideInFrostShadowedShaderLooked = true;
            var loaded = Resources.FindObjectsOfTypeAll<Shader>();
            for (int i = 0; i < loaded.Length; i++)
                if (loaded[i] != null && loaded[i].name == OUTSIDE_IN_FROST_SHADOWED_SHADER) { s_OutsideInFrostShadowedShader = loaded[i]; break; }
            return s_OutsideInFrostShadowedShader;
        }
        private static Material s_OutsideInFrostMaterial { get => s_OI.FrostMaterial; set => s_OI.FrostMaterial = value; }

        // One per aperture, by aperture index, which is also the window's index in s_ShellWindows
        // and therefore its key in the shared cleaned state. Null where a window got no overlay.
        private static List<GameObject> s_OutsideInFrostOverlays => s_OI.FrostOverlays;

        // THE SNOW DRIFT IN THE FRAME, SEEN FROM OUTSIDE. It is a 3D object only the INTERIOR has
        // (OBJ_LakeCabinWindowSnow*); the shell has nothing like it, so from outside it can only be
        // seen through the portal. The owner noticed on 2026-09-18 that it showed looking out and
        // not looking in. It follows the window's shared state like everything else: a frosted
        // window shows its drift from both sides and a cleaned one from neither. One list per
        // aperture, of the paired interior opening's drift renderers; null where unpaired.
        //
        // This is why the look-in depth is only applied to a CLEANED window. It was set, by eye, to
        // start the view just behind this slab, when the slab read as a stray white strip in an
        // otherwise clear pane. On a frosted window the slab is wanted, so the view starts at the
        // opening itself.
        private static List<List<Renderer>> s_OutsideInApertureDrifts => s_OI.ApertureDrifts;

        // ─── THE WINDOW SET BACK INTO THE WALL, SO A DRIFT HAS A LEDGE TO SIT ON ───
        //
        // How far BEHIND the shell's own glass the window plane sits, in meters. ONE slider moves
        // one rigid stack: the portal at this depth, the frost glass a fixed 5 mm in front of it,
        // and the drift standing on the ledge in front of that. There is no second mode; zero is
        // simply the same stack with no ledge.
        //
        // The owner's summary, after an afternoon of this growing extra dials and special cases:
        // "we need to leave some exterior frame to put the effects on, so both the glass and the
        // portal need to move back. So the offset between the glass and the portal needs to be
        // constant, and then we can have a slider to move both of them at the same time."
        //
        // The owner's observation and the owner's fix, 2026-09-18. Snow drifts showed in the window
        // frames looking out and not looking in, and before any attempt had run: "we're probably
        // going to have to slide the glass back a little bit to leave room on the frame for the
        // drift to sit." Measured from the shell mesh the same hour: the frame stands 3.9 to 4.0 cm
        // outboard of the glass, where the interior's own window gives its 54 mm slab a 93 mm slot.
        // There is 6.5 to 11.5 cm of real frame geometry BEHIND every pane, so the glass can go
        // back that far and still be framed by geometry that already exists.
        //
        // A portal can only show what is behind its plane, so a drift seen THROUGH it reads as snow
        // inside the window with the frost drawn over it. With the window set back, the drift is
        // real geometry instead: a copy of the interior's slab standing on the exterior ledge, in
        // front of the frost, lit by the sun, following the window's shared state.
        //
        // The shell's own painted panes are hidden for as long as outside-in holds the building,
        // since they would sit in front of the window, and EVERY window gets a plain dark backing,
        // so one without a live portal shows dark glass rather than the hollow inside of the
        // shell. That is the hole the first frost attempt opened, closed for good. All of it is
        // handed back on teardown.
        //
        // NO LONGER A DIAL. It was a live preference through 2026-09-18, the owner settled it at
        // ZERO, and asked for every dial that ended at zero to be removed. Zero is also what the
        // geometry wants: the shell's sash bar reaches only 1.4 to 1.7 cm behind the glass, so any
        // set back past that opens a gap between it and the interior's bar. The machinery that
        // moves the stack is kept, because the planned per-building table lists a set back per
        // building and another shell may need one.
        internal static float OutsideInWindowRecess { get { return 0f; } }

        // HOW MUCH OF THE FIT IS APPLIED, 0 to 1. 0 is the pure slide every view used until 2026-09-19.
        // The owner, at the farmhouse's bay with the lever in his hand: "It needed to be set to 100."
        // Full fit is therefore the default. It stays a lever (WindowFitDev.ViewFitPercent) until Safe
        // House A, the hard case at 77% across, has been judged with it; not yet a preference.
        internal static float OutsideInViewFit = 1f;

        // WHERE THE DRIFT STANDS RELATIVE TO THE WINDOW, in meters along the window normal, positive
        // toward the player. The portal and the frost glass are one rigid pair moved by the recess.
        // The drift rides along with them, and this moves it on its own, because the two wanted
        // different things (owner, 2026-09-18): "when I make the snow drifts look good, the window
        // is no longer far enough back to show through the wall panel." The window's depth is
        // decided by the wall; the drift's by the ledge.
        //
        // NO LONGER A DIAL. The owner's by-eye value, -6.25 cm, put the Camp Office's drift 2.9 cm
        // proud behind a 3.2 cm bar, and buried Lake Cabin A's 2.15 cm bar. What it was really
        // saying is "3 mm behind the bar", which is now measured per window; see
        // OUTSIDE_IN_DRIFT_BEHIND_RELIEF. The old value survives only as the fallback for a window
        // whose frame could not be measured at all.
        internal static float OutsideInDriftOffset { get { return 0f; } }
        private const float OUTSIDE_IN_DRIFT_LEGACY_OFFSET = -0.0625f;
        private static float s_OutsideInAppliedDriftOffset { get => s_OI.AppliedDriftOffset; set => s_OI.AppliedDriftOffset = value; }

        // EACH DRIFT IS FITTED TO ITS OWN WINDOW, measured from the shell mesh: centered on that
        // window's glass, as wide as that glass plus this overlap a side, and seated on that glass's
        // bottom edge. A single sideways offset was tried first and could not work (owner,
        // 2026-09-18: "works for one window but not for the other"), because the error follows the
        // snow VARIANT, half a centimeter one way for the full-frame borders and 0.7 the other for
        // the half-height drifts. The same hour showed the frames are easy to read from the mesh:
        // each window's glass rectangle to the millimeter from its own triangles, and the frame's
        // outer face 3.9 to 4.0 cm proud of the glass on all eight Camp Office windows.
        //
        // The owner's tolerances, which the overlap is tuned against: the drift may run INTO the
        // frame at its sides, must not come out the far side of it, and must not stand further out
        // than the frame's own outer face. The frame boards measured about 5 cm wide on one side
        // and only 1.4 cm on the other, so the default stayed under that until it had been looked at. The owner looked, tried
        // 1.5 cm, and settled on none (2026-09-18).
        // NO LONGER A DIAL, for the same reason as the set back: the owner tried 1.0 and 1.5 cm and
        // settled on none. Kept as a value because the per-building table lists an overlap too.
        internal static float OutsideInDriftOverlap { get { return 0f; } }
        private static float s_OutsideInAppliedDriftOverlap { get => s_OI.AppliedDriftOverlap; set => s_OI.AppliedDriftOverlap = value; }

        // TWO DIALS LIVED HERE FOR A DAY AND ARE GONE: a vertical offset for the room behind the
        // window and a depth at which the view into it starts, both added on 2026-09-18 to line the
        // interior's window bar up with the shell's. Neither could: the offset was right from above
        // and wrong from below, and the depth only thinned a band. The bar was missing its front
        // face and stood 4 mm too tall; see the bar caps below. Both dials ended at zero and the
        // owner had them removed. They are in history at 1a76a90 and 866b283.

        // Each window's glass, measured: min and max along Right (x, y) and along Up (z, w), relative
        // to the pane center from the author's table. Filled while the frost overlays are built,
        // which is where that window's own triangles are already in hand.
        private static List<Vector4> s_OutsideInGlassRects => s_OI.GlassRects;

        private const float OUTSIDE_IN_BACKING_BEHIND_QUAD = 0.002f;
        private static float s_OutsideInAppliedRecess { get => s_OI.AppliedRecess; set => s_OI.AppliedRecess = value; }
        private static bool s_OutsideInShellPanesHidden { get => s_OI.ShellPanesHidden; set => s_OI.ShellPanesHidden = value; }
        private static Material s_OutsideInHiddenPaneMaterial { get => s_OI.HiddenPaneMaterial; set => s_OI.HiddenPaneMaterial = value; }
        private static Material s_OutsideInBackingMaterial { get => s_OI.BackingMaterial; set => s_OI.BackingMaterial = value; }
        private static Texture2D s_OutsideInBackingTexture { get => s_OI.BackingTexture; set => s_OI.BackingTexture = value; }
        private static List<Renderer> s_OutsideInPaneRenderers => s_OI.PaneRenderers;
        private static List<Material[]> s_OutsideInPaneSavedMaterials => s_OI.PaneSavedMaterials;
        private static List<GameObject> s_OutsideInBackings => s_OI.Backings;          // by aperture
        private static List<Vector3> s_OutsideInApertureGlass => s_OI.ApertureGlass;           // interior glass center, by aperture
        private static List<Vector2> s_OutsideInApertureInteriorSize => s_OI.ApertureInteriorSize;    // interior opening width, height
        private static List<InteriorWindowOpening> s_OutsideInApertureOpenings => s_OI.ApertureOpenings;   // the paired opening, whole

        // ─── THE FRONT FACE THE INTERIOR'S WINDOW BAR NEVER HAD ───
        //
        // The interior's window frame is only ever seen from the room, so its horizontal bar was
        // modeled with a top, an underside and a face toward the room, and NOTHING toward the
        // outside. Measured 2026-09-18 on OBJ_LakeCabinWindowB_LOD0: six triangles, none facing out.
        // Seen through a portal from the yard it is a hollow beam with an open end, and no offset
        // could make it sit right behind the shell's bar (the owner tried both dials and called it:
        // "we're not seeing the entire center bar through the interior camera ... Backface
        // culling?"). The owner likes the rest of the frame, which reads as a deep windowsill, so
        // the frame stays and the bar is finished instead, "on the interior scene".
        //
        // THE CAP TAKES ITS HEIGHT AND WIDTH FROM THE BAR'S OWN EDGES, as the owner asked, read from
        // the frame mesh. ITS DEPTH CANNOT BE THE BAR'S OWN FRONT EDGE whenever the view starts
        // further in than that: the bar's front is 10.3 to 10.6 cm outboard of the interior glass
        // and the view usually starts inside it, where a face at the true front is clipped and
        // never drawn. So the cap stands at the bar's front or just inside the view's start plane,
        // whichever is further in, and follows the set back that moves that plane.
        //
        // Made when the portals are borrowed and destroyed when they are returned, so it is never
        // there to be seen from inside the room.
        private static List<GameObject> s_OutsideInBarCaps => s_OI.BarCaps;   // by aperture, null where none
        private static List<float> s_OutsideInBarFront => s_OI.BarFront;            // the bar's own front, meters outboard of the interior glass
        private static float s_OutsideInBarCapsApplied { get => s_OI.BarCapsApplied; set => s_OI.BarCapsApplied = value; }

        // Frames wearing a private copy of their mesh with the bar resized, and what to give back.
        private static List<MeshFilter> s_OutsideInResizedFrames => s_OI.ResizedFrames;
        private static List<Mesh> s_OutsideInResizedFrameOriginals => s_OI.ResizedFrameOriginals;
        // The owner's value was 0. On the plane itself, whether the cap survives the near clip is a
        // matter of rounding, so it stands a millimeter inside, which nobody can see.
        private const float OUTSIDE_IN_BAR_CAP_INSIDE_VIEW = 0.001f;

        // THE INTERIOR BAR IS TUCKED INSIDE THE SHELL BAR'S HEIGHT, NOT MATCHED TO IT. Settled by the
        // owner by eye on 2026-09-18 with live sliders on a console prototype, on all seven two-pane
        // Camp Office windows, from above and below: top 0.95 cm under the shell bar's top, underside
        // 0.10 cm over its underside. The agent had predicted an exact match would look right and it
        // did not: a bar the same height still shows its long top or underside past the shell bar
        // as soon as the eye leaves level, and a slightly smaller one stays behind it. Kept as
        // insets from each window's own measured shell bar, so they carry to other buildings.
        private const float OUTSIDE_IN_BAR_TOP_UNDER_SHELL = 0.0095f;
        private const float OUTSIDE_IN_BAR_UNDERSIDE_OVER_SHELL = 0.0010f;
        private static List<List<GameObject>> s_OutsideInLedgeDrifts => s_OI.LedgeDrifts;   // by aperture
        private static List<List<Vector3>> s_OutsideInLedgeDriftBase => s_OI.LedgeDriftBase;      // their positions at zero recess

        // A setup that paired NOTHING is not remembered as final. It is retried about once a
        // second, because pairing depends on the state the clone happens to be in at that moment:
        // on 2026-09-18 one bad frame at the door left a supported building with no portals until
        // the player walked away and came back.
        //
        // BOUNDED, because a retry is not free: it tears down and sets up again, which closes and
        // reopens the clone, and a building whose windows genuinely cannot be paired would
        // otherwise have its whole interior switched off and on once a second for as long as the
        // player stood near it. A few attempts cover a bad frame; after that the answer is real.
        private const int OUTSIDE_IN_MAX_PAIRING_RETRIES = 3;
        private static int s_OutsideInPairedCount { get => s_OI.PairedCount; set => s_OI.PairedCount = value; }
        private static float s_OutsideInRetryAt { get => s_OI.RetryAt; set => s_OI.RetryAt = value; }
        //
        // The budget is kept by building id, outside the building's own state, because a retry
        // throws that state away. It is refilled when the building leaves dressing range.
        private static readonly Dictionary<string, int> s_OutsideInRetriesLeft = new Dictionary<string, int>();

        private static bool TakeOutsideInRetry(string id)
        {
            if (id == null) return false;
            int left;
            if (!s_OutsideInRetriesLeft.TryGetValue(id, out left)) left = OUTSIDE_IN_MAX_PAIRING_RETRIES;
            if (left <= 0) return false;
            s_OutsideInRetriesLeft[id] = left - 1;
            return true;
        }

        // How far outside the visible sub-rectangle to render. Erring outward costs a few pixels
        // of shading; erring inward shows a seam of the wrong world at the window edge.
        private const float OUTSIDE_IN_SUBRECT_MARGIN = 0.03f;
        // One centimetre, absolute, so it can never scale with a tall pane again.
        private const float OUTSIDE_IN_SUBRECT_MARGIN_FLOOR = 0.01f;

        // A pane already drawing defends its slot; a rival must beat it by this much to take it.
        // Same value, and the same reason, as the inside-out system: two equally sized panes on one
        // wall tie their priority to four decimal places, and without hysteresis list order decides
        // the winner, so a few centimetres of movement flips which one shows the real interior.
        private const float OUTSIDE_IN_HYSTERESIS = 1.25f;

        // An aperture in the exterior shell, in world space, with the translation that carries the
        // player's eye from this pane to the matching interior pane.
        private struct ShellAperture
        {
            public Vector3 Center;      // middle of the pane
            public Vector3 Outward;     // out of the building, away from the interior
            public Vector3 Right;       // in-plane, as seen by a viewer OUTSIDE looking in
            public Vector3 Up;          // in-plane
            public float Width, Height;
            public Vector3 Delta;       // from this pane's middle to the paired interior pane's
            // The ROOM's frame for the same window: level and upright, whatever the building does.
            // Equal to Outward and Right on a building that stands true. See TryGetShellTilt.
            public Vector3 RoomOutward;
            public Vector3 RoomRight;
            public bool Paired;
            public bool Pretend;        // paired to a place in the room, not to a window of it
            public float Toward;        // where this window's picture is drawn, m in front of the building glass (PicturePlaneFor)
            // Center, Width and Height as MEASURED. The three above are these with the window's
            // hand-set fit laid over them, where it has one. See WindowFit.cs.
            public Vector3 BaseCenter;
            public float BaseWidth, BaseHeight;
            // WHAT THE CAMERA DRAWS FOR THIS PANE. Its own rectangle, except for a double window: two
            // panes and a post outside, one wide window in the room. The leader's view spans both
            // panes and the follower (ViewFollows = the leader's index, otherwise -1) is drawn by it
            // and gets no camera of its own. See AssignOutsideInViews.
            public Vector3 ViewCenter, ViewDelta;
            public float ViewWidth, ViewHeight;
            public Vector3[] Outline;   // the glass's own outline, world points, for a window that is not a rectangle; null for a rectangle
            public int ViewFollows;
            // The room window's own size over the view's, across and up: what the view has to be
            // scaled by for the room's opening to fill the building's. 1 where there is no room
            // window to fit. See OutsideInViewFit.
            public float FitAcross, FitUp;
        }

        private static List<ShellAperture> s_OutsideInApertures => s_OI.Apertures;
        private static string s_OutsideInInstanceId { get => s_OI.InstanceId; set => s_OI.InstanceId = value; }
        private static SeamlessInteriorInstance s_OutsideInInstance { get => s_OI.Instance; set => s_OI.Instance = value; }

        // ── ONE SLOT PER SIMULTANEOUSLY VISIBLE PANE ──
        //
        // Milestone 1 rendered a single window, the one the player was most directly facing, which
        // is why the view jumped from pane to pane as they turned. A slot is a camera, a target and
        // a quad; panes compete for slots by priority with hysteresis.
        //
        // CAMERAS MUST NOT SHARE ONE TARGET. Measured on 2026-09-14 for the inside-out system:
        // under deferred rendering a camera clears the WHOLE target rather than its viewport rect,
        // so three cameras into one texture left only the last one's content and the other panes
        // came out flat skybox blue. One target per slot, sized to that pane's own footprint.
        private static int s_OutsideInSlotCount;
        private static Camera[] s_OutsideInCams;
        private static RenderTexture[] s_OutsideInTargets;
        private static Material[] s_OutsideInMaterials;
        private static GameObject[] s_OutsideInQuads;

        // Which aperture each slot is currently showing, and which apertures drew last frame. The
        // second is what hysteresis reads.
        private static readonly List<int> s_OutsideInSlotAperture = new List<int>();
        private static List<bool> s_OutsideInWasDrawn => s_OI.WasDrawn;
        private static readonly List<int> s_OutsideInCandidates = new List<int>();
        private static readonly List<float> s_OutsideInPriority = new List<float>();
        private static readonly List<OutsideInBuilding> s_OutsideInCandidateBuilding = new List<OutsideInBuilding>();   // whose pane each candidate is

        // ── borrowed state, restored exactly when the portals are given back ──
        //
        // True from BorrowInteriorForOutsideIn to ReturnInteriorFromOutsideIn. s_OutsideInInstanceId
        // says which building is DRESSED; this says its portals are live as well.
        private static bool s_OutsideInBorrowed { get => s_OI.Borrowed; set => s_OI.Borrowed = value; }
        private static bool s_OutsideInActivatedClone { get => s_OI.ActivatedClone; set => s_OI.ActivatedClone = value; }
        private static bool s_OutsideInMaskEdited;
        private static int s_OutsideInSavedMainMask;
        private static List<Renderer> s_OutsideInRelayered => s_OI.Relayered;
        private static List<int> s_OutsideInSavedLayers => s_OI.SavedLayers;

        // COLLIDERS THIS SWITCHED OFF, AND THE REASON IT MUST.
        //
        // Activating the clone to render it also activates its COLLISION. The interior is
        // co-located with its shell and its floor sits about 1.07 m above the porch, so the player
        // walking past the building stands on the interior's invisible floor and is lifted off the
        // ground. Reported from the yard on 2026-09-15 as "when I exit, I'm up too high".
        //
        // A portal needs the interior VISIBLE, never SOLID. Nothing outside the building should be
        // able to touch it, walk on it, or shoot it.
        //
        // ─── WHY THIS IS SWEPT EVERY FRAME AND NOT ONCE AT ACTIVATION ───
        //
        // Measured live on 2026-09-15, standing in the Camp Office yard on build 87eca1b: the clone
        // carried 427 colliders, 271 of them live and SOLID, while this list had recorded only 114.
        // Every one of the 114 was still correctly disabled - nothing had switched them back on -
        // and 42 curtain-point colliders had never been recorded at all.
        //
        // The cause is that a one-shot sweep at activation samples a hierarchy that is still being
        // built. The clone's own spawn logic runs in the frames AFTER SetActive(true), and the
        // objects it produces carry colliders the sweep never saw: OBJ_CurtainBarA2_Prefab (1)
        // through (6), whose "(N)" suffixes are Unity's own mark of a runtime Instantiate. Their
        // CurtainPlacementPoints sit exactly in the window openings - the one place an interior
        // collider is reachable from outside, because everywhere else the shell wall gets there
        // first. That is why the report was "I can't walk up to the door" and "it's only in front
        // of the windows": the same defect, seen at the two places the interior is exposed.
        //
        // So the sweep is re-asserted while the portal holds the clone open. It is cheap - a
        // GetComponentsInChildren and a bool test per collider, against a full scene render per
        // visible pane - and cheapness is not the point: a portal that is solid is a broken
        // building, and sampling a hierarchy once while it is still growing cannot be made correct.
        //
        // ─── AND WHY THE RECORD ACCUMULATES INSTEAD OF BEING REBUILT ───
        //
        // The previous version called Clear() at the top of every sweep. Re-run it and the record
        // of everything already disabled is destroyed, while those colliders are skipped by the
        // "already off" test and never re-added - so teardown restores nothing and the interior
        // stays permanently non-solid, which the player meets as a floor they fall through. The
        // 2026-09-13 proof got this right: SIPortalCol.off accumulates under a Contains guard and is
        // cleared only by Restore(). The HashSet is that guard, at O(1) instead of O(n).
        private static List<Collider> s_OutsideInDisabledColliders => s_OI.DisabledColliders;
        private static HashSet<Collider> s_OutsideInDisabledSet => s_OI.DisabledSet;

        // The clone's own ambient, captured at setup from its TodAmbientLight. Swapped in around
        // the portal render because the mod's Outdoor Lighting mode strips the interior's lightmaps
        // and destroys 98 of about 103 of its lights, leaving the room lit by outdoor ambient.
        private static Color s_OutsideInAmbientDay { get => s_OI.AmbientDay; set => s_OI.AmbientDay = value; }
        private static Color s_OutsideInAmbientNight { get => s_OI.AmbientNight; set => s_OI.AmbientNight = value; }
        private static bool s_OutsideInAmbientValid { get => s_OI.AmbientValid; set => s_OI.AmbientValid = value; }

        // ─── THE TICK ───

        // Where the last room's switch-on went, in ms, for the log line that follows it.
        private static string s_OutsideInLastBorrowLaps = "";
        private static string s_OutsideInLastReturnLaps = "";

        public static void TickOutsideInPortals()
        {
            if (s_PrefEnableWindowPortals == null || !s_PrefEnableWindowPortals.Value)
            {
                TeardownAllOutsideIn();
                return;
            }

            // Inside the building this system has nothing to say, and leaving it running would
            // fight the mod's own interior/shell swap.
            if (s_IsPlayerInsideClone)
            {
                TeardownAllOutsideIn();
                return;
            }

            Camera main = GameManager.GetMainCamera();
            // NOT UnityEngine.Camera.main. TLD's FPSCamera carries the MainCamera tag but its
            // Camera component is DISABLED - the game renders it by hand - and Camera.main only
            // ever returns enabled cameras. Its transform and matrices are valid while disabled.
            if (main == null)
            {
                TeardownAllOutsideIn();
                return;
            }

            Vector3 eye = main.transform.position;
            var tickTimer = s_DebugBounds ? System.Diagnostics.Stopwatch.StartNew() : null;

            // Which buildings are dressed, then which of those have their rooms held open. Both are
            // in OutsideInBuildings.cs, with the reasons.
            // A lever that changes what the set-up itself computes asks for everything to be set up again.
            if (s_OutsideInRebuildAsked) { s_OutsideInRebuildAsked = false; TeardownAllOutsideIn(); }

            UpdateOutsideInBuildingSet(eye);
            ChooseOutsideInHeldRooms(eye);

            // ONE ROOM SWITCHED ON PER FRAME, however many are wanted. Switching one on was timed at
            // 48 to 76 ms on 2026-09-18; three in one frame is a stall, three in three frames is not.
            bool borrowedThisFrame = false;

            for (int h = s_OutsideInBuildings.Count - 1; h >= 0; h--)
            {
                var building = s_OutsideInBuildings[h];
                BindOutsideIn(building);
                var instance = building.Instance;

                // A setup that paired nothing is retried a few times; see s_OutsideInRetriesLeft. The
                // building is dropped here and UpdateOutsideInBuildingSet sets it up afresh.
                if (s_OutsideInPairedCount == 0 && Time.realtimeSinceStartup >= s_OutsideInRetryAt
                    && TakeOutsideInRetry(building.InstanceId))
                {
                    TeardownOutsideInPortals();
                    continue;
                }

                // The portals come and go inside the dressing's lifetime. See OutsideInPortalRange.
                bool wantPortals = building.WantHeld;

                if (wantPortals && !s_OutsideInBorrowed)
                {
                    if (!borrowedThisFrame)
                    {
                        borrowedThisFrame = true;
                        var borrowTimer = System.Diagnostics.Stopwatch.StartNew();
                        CaptureOutsideInAmbient(instance);
                        double ambientMs = borrowTimer.Elapsed.TotalMilliseconds;
                        s_OutsideInBorrowed = BorrowInteriorForOutsideIn(instance);
                        double beforeCapsMs = borrowTimer.Elapsed.TotalMilliseconds;
                        if (s_OutsideInBorrowed) BuildOutsideInBarCaps();
                        double capsMs = borrowTimer.Elapsed.TotalMilliseconds - beforeCapsMs;
                        s_OutsideInLastBorrowLaps += SeamlessInteriorsLog.English
                            ? $", read room lighting {ambientMs:F1}, fit window bars {capsMs:F1}"
                            : $", ortam isigi {ambientMs:F1}, pencere citasi {capsMs:F1}";
                        if (s_OutsideInBorrowed && s_DebugBounds)
                            MelonLogger.Msg($"[DIS-ICE] {instance.Config.ResolvedInstanceId}: oda acildi ({building.ShellDistance:F0} m), " +
                                            $"{s_OutsideInRelayered.Count} ic mekan renderer katman {s_OI.Layer}'e tasindi. " +
                                            $"Sure: {borrowTimer.Elapsed.TotalMilliseconds:F1} ms ({s_OutsideInLastBorrowLaps}). Acik tutulan oda: {CountOutsideInHeldRooms()}. " +
                                            $"Goz X {eye.x:F0}, Z {eye.z:F0}.",
                                            $"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: room switched ON so it can be seen through the windows (building is {building.ShellDistance:F0} m away), " +
                                            $"{s_OutsideInRelayered.Count} objects moved to the hidden layer only the window cameras draw. " +
                                            $"Took {borrowTimer.Elapsed.TotalMilliseconds:F1} ms ({s_OutsideInLastBorrowLaps}). Rooms now switched on: {CountOutsideInHeldRooms()}. " +
                                            $"Player at X {eye.x:F0}, Z {eye.z:F0}.");
                    }
                }
                else if (!wantPortals && s_OutsideInBorrowed)
                {
                    var returnTimer = System.Diagnostics.Stopwatch.StartNew();
                    ReturnInteriorFromOutsideIn();
                    bool mayBeInside = s_DebugBounds && PlayerMayBeInsideForOutsideIn(instance, eye);
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[DIS-ICE] {instance.Config.ResolvedInstanceId}: oda geri verildi ({building.ShellDistance:F0} m), " +
                                        $"pencere giydirmesi yerinde. Sure: {returnTimer.Elapsed.TotalMilliseconds:F1} ms ({s_OutsideInLastReturnLaps}). Acik tutulan oda: {CountOutsideInHeldRooms()}. " +
                                        $"Goz X {eye.x:F0}, Z {eye.z:F0}; menzil {OutsideInPortalRange:F0} m, iceride olabilir: {mayBeInside}.",
                                        $"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: room switched OFF (building is {building.ShellDistance:F0} m away), " +
                                        $"its window frost and snow stay in place. Took {returnTimer.Elapsed.TotalMilliseconds:F1} ms ({s_OutsideInLastReturnLaps}). Rooms now switched on: {CountOutsideInHeldRooms()}. " +
                                        $"Player at X {eye.x:F0}, Z {eye.z:F0}; window cameras work out to {OutsideInPortalRange:F0} m; player could be inside this room: {mayBeInside}.");
                }

                // RE-ASSERT EVERY FRAME, because setup is not the only thing that touches this.
                //
                // The mod's visibility watchdog closes a clone whenever it decides the player is
                // outside, which is exactly when this system wants it open. Setup runs only when the
                // instance CHANGES, so without this the clone was closed under the portal and never
                // reopened: measured on 2026-09-15 with MasterInterior.activeSelf false and 0 of 595
                // relayered renderers drawing, while the camera cheerfully rendered an empty scene
                // into the quad. That is the "glitching in and out of reality" report.
                if (s_OutsideInBorrowed) ReassertOutsideInHold(instance, main);
                if (s_OutsideInBorrowed) ApplyOutsideInBarCaps();

                ApplyOutsideInRecess(instance);

                // Followed every frame rather than set once, so the dial works in the running game.
                if (s_OutsideInFrostMaterial != null)
                {
                    if (s_OI.FrostSheetSource != null)
                    {
                        float contrast = OutsideInFrostContrast;
                        if (contrast != s_OutsideInFrostContrastSeen) { s_OutsideInFrostContrastSeen = contrast; s_OutsideInFrostContrastSettlesAt = Time.unscaledTime + 0.3f; }
                        if (s_OI.FrostContrastApplied != contrast && Time.unscaledTime >= s_OutsideInFrostContrastSettlesAt)
                        {
                            bool ready;
                            s_OutsideInFrostMaterial.mainTexture = OutsideInFrostSheetWithContrast(s_OI.FrostSheetSource, out ready);
                            if (ready) s_OI.FrostContrastApplied = contrast;
                            else s_OutsideInFrostContrastSettlesAt = Time.unscaledTime + 0.25f;
                        }
                    }

                    if (s_OI.FrostShadowed)
                    {
                        // Cut out, not blended: the strength dial has nothing to act on here.
                        float bright = Mathf.Clamp01(OutsideInFrostBrightness);
                        if (s_OutsideInFrostMaterial.color.r != bright) s_OutsideInFrostMaterial.color = new Color(bright, bright, bright, 1f);
                        float cut = Mathf.Clamp01(OutsideInFrostCutoff);
                        if (s_OutsideInFrostMaterial.GetFloat("_Cutoff") != cut) s_OutsideInFrostMaterial.SetFloat("_Cutoff", cut);
                        if (s_OutsideInGlassTintMaterial != null)
                        {
                            var tint = new Color(OutsideInGlassTintColor.r, OutsideInGlassTintColor.g, OutsideInGlassTintColor.b, Mathf.Clamp01(OutsideInGlassTint));
                            if (s_OutsideInGlassTintMaterial.color != tint) s_OutsideInGlassTintMaterial.color = tint;
                        }
                    }
                    else
                    {
                        float strength = Mathf.Clamp01(OutsideInFrostStrength);
                        float bright = Mathf.Clamp01(OutsideInFrostBrightness);
                        Color now = s_OutsideInFrostMaterial.color;
                        if (now.a != strength || now.r != bright) s_OutsideInFrostMaterial.color = new Color(bright, bright, bright, strength);
                    }
                }

                UpdateOutsideInDressing();
            }

            if (CountOutsideInHeldRooms() > 0)
            {
                // Drawn after the game has finished moving the player's camera, when it can be. See
                // DrawOutsideInViewsLate.
                if (OutsideInViewsDrawLate()) s_OutsideInDrawLateFrame = Time.frameCount;
                else RenderOutsideInPortals(main);
            }

            // Left bound to the building the player is nearest, which is what the code outside this
            // system means by "the building the player is at" (cleaning a window, WindowFrostState).
            BindOutsideIn(NearestOutsideInBuilding());

            if (tickTimer != null) ReportOutsideInCost(tickTimer.Elapsed.TotalMilliseconds);
        }

        // ─── THE PLAYER FELL 40 METERS THROUGH THE FLOOR, ONCE ───
        //
        // Owner, 2026-09-18, loading a save made inside the parked Camp Office: "The player dropped
        // from 40 meters onto the roof of the exterior shell and was injured." The log had the
        // cause on one line, at the moment of load: "portal menzilinde (43 m)". For the first
        // frames after a load s_IsPlayerInsideClone is not yet set, so this system ran with the
        // player standing in the room, found the building 43 m away, inside a range that had been
        // raised from 25 m to 50 m that same day, borrowed the interior, and swept its colliders
        // off, the floor among them. At 25 m a parked interior 40 m up was always out of reach,
        // which is the only reason this had not happened before. The same race exists for an
        // interior that is NOT parked, at any range; it just drops the player a meter, not forty.
        //
        // The inside flag is the normal guard and it is late exactly when it matters, so two
        // signals that do not wait for it are added. NEITHER MAY USE THE MOD'S USUAL INSIDE TEST:
        // IsPositionInside casts rays at the interior's colliders and wants the clone active, and
        // the colliders are the very thing at stake.
        //
        //   - While a load is still authoritative, the save's own record of which building the
        //     player was in. If it names this building, the player is inside, whatever else says.
        //   - For a PARKED building, the interior's plain box, padded. Nothing in the yard is
        //     anywhere near a box ParkOffset away, so it costs the outside view nothing. It cannot
        //     be used for a co-located interior, whose box reaches out past the walls to where the
        //     player stands to look in.
        private static bool PlayerMayBeInsideForOutsideIn(SeamlessInteriorInstance instance, Vector3 eye)
        {
            if (instance == null || instance.Config == null) return false;

            if (IsLoadStateAuthoritative()
                && GetSavedPlayerInsideInstanceId() == instance.Config.ResolvedInstanceId) return true;

            if (instance.IsPositionInParkedRoom(eye, 2f)) return true;

            return false;
        }

        // Cheap per-frame guard: three comparisons in the common case where nothing has moved.
        private static void ReassertOutsideInHold(SeamlessInteriorInstance instance, Camera main)
        {
            var interior = instance.MasterInterior;
            if (interior == null) return;

            if (!interior.activeSelf)
            {
                interior.SetActive(true);
                s_OutsideInActivatedClone = true;
                // Re-show the loot too: SetInteriorItemsVisible is what the mod uses to hide items
                // when the player leaves, and a clone reopened without it renders a room stripped
                // of everything the player put down.
                ShowInteriorLootForOutsideIn(instance);
            }

            // EVERY FRAME, NOT ONLY ON ACTIVATION. The clone's spawn logic keeps producing collider
            // -carrying objects for several frames after SetActive(true), and the curtain placement
            // points it creates land squarely in the window openings. Measured: 271 live solid
            // colliders against 114 recorded, all 114 still correctly off.
            DisableInteriorCollisionForOutsideIn(interior);

            if (Time.realtimeSinceStartup >= s_OutsideInNextRelayerSweep)
            {
                s_OutsideInNextRelayerSweep = Time.realtimeSinceStartup + OUTSIDE_IN_RELAYER_SWEEP_SECONDS;
                RelayerInteriorForOutsideIn(interior);
            }

            if (main != null && s_OutsideInMaskEdited)
            {
                int wanted = s_OutsideInSavedMainMask & ~OUTSIDE_IN_ROOM_LAYERS_MASK;
                if (main.cullingMask != wanted) main.cullingMask = wanted;
            }
        }

        // True while the portal system is holding this instance's clone open for rendering. The
        // visibility watchdog asks before closing it, so the two do not fight frame by frame.
        internal static bool OutsideInHoldsCloneOpen(SeamlessInteriorInstance instance)
        {
            // A building that is only dressed has its clone closed, and the watchdog may keep it so.
            if (instance == null || instance.Config == null) return false;
            string id = instance.Config.ResolvedInstanceId;
            for (int h = 0; h < s_OutsideInBuildings.Count; h++)
                if (s_OutsideInBuildings[h].Borrowed && s_OutsideInBuildings[h].InstanceId == id) return true;
            return false;
        }

        // ─── SETUP: PAIR THE WINDOWS AND DRESS THE SHELL ───
        //
        // Nothing here opens the clone. Pairing reads it with includeInactive, the frost texture is
        // a material lookup, and a drift is a detached copy. Borrowing is BorrowInteriorForOutsideIn.

        private static bool SetupOutsideInPortals(SeamlessInteriorInstance instance, string id)
        {
            // LAP TIMES FROM THE TOP OF THE FUNCTION. The 5.7 s stall of 2026-09-22 sits between the
            // room openings being found and the step timer below, with nothing logged in between and
            // three guesses already wrong, so every block in that stretch gets its own lap.
            var lapTimer = System.Diagnostics.Stopwatch.StartNew();
            DestroyOutsideInWall(s_OI);
            s_OutsideInApertures.Clear();
            s_OutsideInApertureDrifts.Clear();
            s_OutsideInApertureGlass.Clear();
            s_OutsideInApertureInteriorSize.Clear();
            s_OutsideInApertureOpenings.Clear();
            s_OI.ApertureRoomWindow.Clear();
            s_OutsideInPairedCount = 0;
            s_OutsideInRetryAt = Time.realtimeSinceStartup + 1f;

            WindowSpot[] spots;
            string shellName = instance.Config.ExteriorShellPrefabName;
            if (!TryGetShellWindows(instance, out spots))
            {
                // Remembered anyway, so an unsupported shell is not rescanned every frame.
                s_OutsideInInstanceId = id;
                return false;
            }

            // The interior's own apertures, found by glass material. This reads the clone's mesh
            // renderers with includeInactive true, so it works before the clone is switched on.
            double lapShellWindows = lapTimer.Elapsed.TotalMilliseconds;
            var allRoomOpenings = FindInteriorWindowOpenings(instance);
            var interiorOpenings = WithoutViewOutOnly(allRoomOpenings);
            double lapRoomOpenings = lapTimer.Elapsed.TotalMilliseconds;

            Transform shell = instance.ExteriorShell.transform;

            // THE WHOLE BUILDING'S WINDOWS ARE PAIRED AT ONCE. See WindowPairing.cs: one window at
            // a time by nearness took the neighbor wherever a room sits further along from its
            // building than its windows are apart. Matched in the shell's frame: a parked room
            // stands ParkOffset away from its building. The Delta stored below stays the real
            // world-space one, park included, because that is where the camera must go.
            var shellCenters = new List<Vector3>(spots.Length); var shellOutwards = new List<Vector3>(spots.Length);
            for (int s = 0; s < spots.Length; s++)
            {
                shellCenters.Add(shell.TransformPoint(new Vector3(spots[s].X, spots[s].Y, spots[s].Z)));
                shellOutwards.Add(shell.TransformDirection(new Vector3(spots[s].NX, 0f, spots[s].NZ)).normalized);
            }
            var roomCenters = new List<Vector3>(interiorOpenings.Count); var roomOutwards = new List<Vector3>(interiorOpenings.Count);
            for (int i = 0; i < interiorOpenings.Count; i++)
            {
                roomCenters.Add(interiorOpenings[i].PortalCenter - instance.Config.ParkOffset);
                roomOutwards.Add(interiorOpenings[i].Outward);
            }
            int[] roomToShell; Vector3 meanSlide;
            double lapCenters = lapTimer.Elapsed.TotalMilliseconds;
            BuildingWindowOverride pairRow;
            bool fitted = TryGetBuildingWindowOverride(shellName, out pairRow) && !float.IsNaN(pairRow.PairInPlaceWithinCm)
                ? PairWindowsInPlace(roomCenters, roomOutwards, shellCenters, shellOutwards, pairRow.PairInPlaceWithinCm / 100f, out roomToShell, out meanSlide)
                : FitWindowPattern(roomCenters, roomOutwards, shellCenters, shellOutwards, out roomToShell, out meanSlide);
            double lapPairing = lapTimer.Elapsed.TotalMilliseconds;
            var shellToRoom = new int[spots.Length];
            for (int s = 0; s < shellToRoom.Length; s++) shellToRoom[s] = -1;
            if (fitted) for (int i = 0; i < roomToShell.Length; i++) if (roomToShell[i] >= 0) shellToRoom[roomToShell[i]] = i;
            if (ApplyHandPairs(shellName, id, shellToRoom, interiorOpenings.Count))
            {
                // The pretend windows' eye follows the slide the pairs agree on, so it is taken again
                // from the pairs as they now stand.
                var handRoomToShell = new int[interiorOpenings.Count];
                for (int i = 0; i < handRoomToShell.Length; i++) handRoomToShell[i] = -1;
                for (int s = 0; s < shellToRoom.Length; s++) if (shellToRoom[s] >= 0) handRoomToShell[shellToRoom[s]] = s;
                Vector3 handSlide;
                if (MeanSlideOf(roomCenters, shellCenters, handRoomToShell, out handSlide)) { meanSlide = handSlide; fitted = true; }
            }

            Quaternion shellTilt;
            float shellTiltDegrees;
            if (TryGetShellTilt(instance, out shellTilt, out shellTiltDegrees))
                MelonLogger.Msg($"[DIS-ICE] {id}: bina {shellTiltDegrees:F2} derece egik duruyor; pencereleri binanin kendi egimiyle kuruluyor, oda tarafi duz kaliyor.",
                                $"[OUTSIDE-IN] {id}: the building stands tipped {shellTiltDegrees:F2} degrees; its windows are set up in the building's own tilted frame and the room's side stays upright.");

            double lapTilt = lapTimer.Elapsed.TotalMilliseconds;
            var pairSizes = new System.Text.StringBuilder(); int pairSizesLarger = 0;
            for (int s = 0; s < spots.Length; s++)
            {
                var spot = spots[s];

                Vector3 center = shellCenters[s];
                Vector3 outward = shellOutwards[s];

                var aperture = new ShellAperture();
                aperture.Center = center;
                // Right-handed about the OUTSIDE viewer's forward, which is -outward. Getting this
                // backwards mirrors the interior left to right, which reads as subtly wrong rather
                // than obviously broken, so it is worth being explicit.
                Vector3 levelRight = Vector3.Cross(Vector3.up, -outward).normalized;
                aperture.RoomOutward = outward;
                aperture.RoomRight = levelRight;
                // The BUILDING's frame is the level one carried through the building's own tilt, so
                // the picture, the backing, the frost and the drift all stand square in a frame that
                // does not stand square in the world. Identity for a building that stands true.
                aperture.Outward = (shellTilt * outward).normalized;
                aperture.Up = (shellTilt * Vector3.up).normalized;
                aperture.Right = (shellTilt * levelRight).normalized;
                aperture.Width = spot.Width;
                aperture.Height = spot.Height;
                aperture.BaseCenter = center; aperture.BaseWidth = spot.Width; aperture.BaseHeight = spot.Height;
                Vector3[] localOutline = ShellWindowOutline(shellName, s);
                if (localOutline != null)
                {
                    aperture.Outline = new Vector3[localOutline.Length];
                    for (int o = 0; o < localOutline.Length; o++) aperture.Outline[o] = shell.TransformPoint(localOutline[o]);
                }
                aperture.Delta = Vector3.zero;
                aperture.Paired = false;
                aperture.Toward = PicturePlaneFor(shellName, s);

                int best = shellToRoom[s];
                if (best >= 0 && HasNoView(shellName, s)) best = -1;
                if (best >= 0)
                {
                    // Carry the eye from this shell pane to its interior twin. This is exactly the
                    // inside-out delta with the sign reversed.
                    aperture.Delta = RoomPointBehindPane(interiorOpenings[best], shellName, s) - center;
                    aperture.Paired = true;
                }
                // Only where the eye lands in the room: the lighthouse's lantern stands above its room, and its
                // 34 panes showed the empty space there (owner, 2026-10-14). Those keep the building's own glass.
                else if (fitted && !HasNoView(shellName, s)
                         && (instance.InteriorTrigger == null || instance.IsPositionInVolume(center + meanSlide + instance.Config.ParkOffset, PRETEND_EYE_IN_ROOM)))
                {
                    // A WINDOW ON THE BUILDING WITH NO WINDOW IN THE ROOM (Trailers B and E have six
                    // outside and four inside). It used to get the dark backing and nothing else, a
                    // black pane. Owner, 2026-09-18: "Why can't we just pretend there's a window on
                    // the inside and still look through it from the outside?" We can: the room is
                    // drawn from inside only, so an eye put where this window WOULD be, by the slide
                    // the real pairs agree on, looks through the back of the room's wall at the room.
                    // Tried live on Trailer E first; he: "It looks good; the windows are just missing
                    // the frosting." It has no room-side drift, bar or frame to fit, hence Pretend.
                    aperture.Delta = meanSlide + instance.Config.ParkOffset;
                    // A pretend eye moved by hand is added once, in AssignOutsideInViews (PretendWindowHandMove).
                    aperture.Paired = true;
                    aperture.Pretend = true;
                }

                s_OutsideInApertures.Add(aperture);
                bool real = best >= 0;
                s_OutsideInApertureDrifts.Add(real ? interiorOpenings[best].Frost : null);
                s_OutsideInApertureGlass.Add(real ? interiorOpenings[best].GlassCenter : Vector3.zero);
                s_OutsideInApertureInteriorSize.Add(real ? new Vector2(interiorOpenings[best].Width, interiorOpenings[best].Height) : Vector2.zero);
                s_OutsideInApertureOpenings.Add(real ? interiorOpenings[best] : default(InteriorWindowOpening));
                // The number the room knows it by, in the list the views out keep (view-out-only glass included).
                s_OI.ApertureRoomWindow.Add(real ? allRoomOpenings.FindIndex(o => o.PaneRoot == interiorOpenings[best].PaneRoot && o.GlassCenter == interiorOpenings[best].GlassCenter) : -1);

                // EACH PAIR'S TWO SIZES, SAID AT LOAD. Counting and pairing were both right at Safe House A
                // and the log had nothing to say, while the owner could see at once that "they just
                // don't line up ... the interior one seems smaller": its room openings are 0.49 x 1.11 m
                // against panes of 0.64 x 1.24. A pane LARGER than the room's opening shows past the
                // opening's edges; the other way round (every lake cabin) the pane only crops. Marked
                // so the first kind can be found in a region's log without walking it.
                if (real)
                {
                    var room = interiorOpenings[best];
                    bool paneLarger = spot.Width > room.Width * 1.05f || spot.Height > room.Height * 1.05f;
                    if (paneLarger) pairSizesLarger++;
                    pairSizes.Append($"{s}: {room.Width:F2}x{room.Height:F2}->{spot.Width:F2}x{spot.Height:F2}{(paneLarger ? "!" : "")}  ");
                }
            }

            double lapApertures = lapTimer.Elapsed.TotalMilliseconds;
            MelonLogger.Msg($"[DIS-ICE] {id}: kurulumun turlari, ms: kabuk pencereleri {lapShellWindows:F1}, oda aciklilari {(lapRoomOpenings - lapShellWindows):F1}, merkezler {(lapCenters - lapRoomOpenings):F1}, eslestirme {(lapPairing - lapCenters):F1}, egim {(lapTilt - lapPairing):F1}, pencere dongusu {(lapApertures - lapTilt):F1}; {spots.Length} bina penceresi, {interiorOpenings.Count} oda acikligi.",
                            $"[OUTSIDE-IN] {id}: setup laps, ms: building windows {lapShellWindows:F1}, room openings {(lapRoomOpenings - lapShellWindows):F1}, centers {(lapCenters - lapRoomOpenings):F1}, pairing {(lapPairing - lapCenters):F1}, tilt {(lapTilt - lapPairing):F1}, window loop {(lapApertures - lapTilt):F1}; {spots.Length} building windows, {interiorOpenings.Count} room openings.");

            if (s_DebugBounds && pairSizes.Length > 0)
                MelonLogger.Msg($"[DIS-ICE] {id}: her pencere cifti, oda acikligi -> bina cami, m (! = cam odanin acikligindan buyuk, {pairSizesLarger} adet): {pairSizes}",
                                $"[OUTSIDE-IN] {id}: each window pair, room opening -> building pane, width x height in m (! = the pane is LARGER than the room's opening, so it shows past the opening's edges; {pairSizesLarger} of them): {pairSizes}");

            s_OutsideInInstance = instance;
            s_OutsideInInstanceId = id;

            int paired = 0, pretend = 0;
            for (int i = 0; i < s_OutsideInApertures.Count; i++)
            {
                if (!s_OutsideInApertures[i].Paired) continue;
                if (s_OutsideInApertures[i].Pretend) pretend++; else paired++;
            }
            s_OutsideInPairedCount = paired;

            // WHERE THE SETUP TIME GOES, step by step, the way the "room switched ON" line already
            // reports its own. Measured 2026-09-22: this whole step cost 222 ms at the hangar with
            // 8 room windows and 5650 ms with 42, which is 5.25x the windows for 25x the time. Two
            // guesses at the cause were wrong (the upstream merge, then the GPU mesh read-backs,
            // which measure 0.3 ms each), so it is timed rather than reasoned about.
            var stepTimer = System.Diagnostics.Stopwatch.StartNew();
            ApplyWindowFitsToApertures(shellName);
            double msFits = stepTimer.Elapsed.TotalMilliseconds;
            AssignOutsideInViews(id);
            double msViews = stepTimer.Elapsed.TotalMilliseconds;
            BuildOutsideInFrostOverlays(instance);
            double msFrost = stepTimer.Elapsed.TotalMilliseconds;
            BuildOutsideInRecessObjects(instance);
            BuildCopiedShellDrifts(instance);
            BuildOutsideCovers(instance);
            double msRecess = stepTimer.Elapsed.TotalMilliseconds;

            int driftSlabs = 0;
            for (int i = 0; i < s_OutsideInApertureDrifts.Count; i++)
                if (s_OutsideInApertureDrifts[i] != null) driftSlabs += s_OutsideInApertureDrifts[i].Count;

            MelonLogger.Msg($"[DIS-ICE] {id}: kurulum adim adim, ms: pencere ayarlari {msFits:F1}, gorunumler {(msViews - msFits):F1}, kirag {(msFrost - msViews):F1}, niş {(msRecess - msFrost):F1}, fonksiyonun tamami {lapTimer.Elapsed.TotalMilliseconds:F1}; {s_OutsideInApertures.Count} pencere, {driftSlabs} kar dilimi kopyalandi.",
                            $"[OUTSIDE-IN] {id}: setup step by step, ms: window fits {msFits:F1}, views {(msViews - msFits):F1}, frost {(msFrost - msViews):F1}, recess {(msRecess - msFrost):F1}, whole function {lapTimer.Elapsed.TotalMilliseconds:F1}; {s_OutsideInApertures.Count} windows, {driftSlabs} snow slabs copied.");
            s_OutsideInAppliedRecess = -1f;     // force ApplyOutsideInRecess on the next tick
            // Always said when NOTHING paired, which is a building with no view through any window;
            // otherwise only with Verbose Logging on (F7).
            if (s_DebugBounds || paired == 0)
                MelonLogger.Msg($"[DIS-ICE] {id} ({shellName}): {s_OutsideInApertures.Count} kabuk penceresi, " +
                                $"{paired} tanesi ic mekan esiyle eslestirildi, {pretend} tanesi odada penceresi olmadan gosteriliyor, pencereler giydirildi.",
                                $"[OUTSIDE-IN] {id} ({shellName}): {s_OutsideInApertures.Count} windows on the outside of the building, " +
                                $"{paired} matched to a window of the room, {pretend} more shown with no window in the room behind them; frost and snow set up on them.");
            return true;
        }

        // Activates the clone, moves its renderers to the private layer, and takes that layer away
        // from the main camera. Everything here is recorded for exact restoration.
        // THE AUTHOR'S PURGE OF LEAKED LOOT ONLY RUNS FOR A CLOSED ROOM, and a room held for its
        // windows is open for as long as the player is within 100 m. So the copies of a room's own
        // loot that the game's save puts back at the building (GearLeak.cs) stayed lying round the
        // very buildings the player was looking at: seen 2026-09-18 at Lake Cabin A_1, an open book
        // and a paper stack outside the wall, each the twin of an item in the room 40 m above, while
        // the burnt cabin next door, never held, had its three copies purged in the same minute.
        // Run once per room object, the first time it is held; the room is filled by then, which
        // the purge requires. It costs one scan of the scene's gear, about 20 ms, once.
        private static readonly HashSet<int> s_OutsideInLeakPurgedRooms = new HashSet<int>();

        private static void PurgeLeakedCopiesOncePerRoom(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;
            if (!s_OutsideInLeakPurgedRooms.Add(instance.MasterInterior.GetInstanceID())) return;

            SceneScan.Begin();
            try
            {
                // Copies first, then what is left: an object at the building with no twin in the
                // room is the room's own, never carried up to it (TryCarryStrayToParkedRoom). The
                // room's collision is still on at this point, which the adoption's roof test needs.
                PurgeLeakedCloneGearDuplicates(instance);
                AdoptStrayInteriorObjects(instance);
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: oyunun kaydindan gelen kopya esyalar temizlenemedi: {e.Message}",
                                    $"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: could not clean up the copies of this room's items that the game's save left at the building: {e.Message}");
            }
            finally { SceneScan.End(); }
        }

        private static bool BorrowInteriorForOutsideIn(SeamlessInteriorInstance instance)
        {
            var interior = instance.MasterInterior;
            if (interior == null) return false;

            // WHAT IS SEEN THROUGH THE WINDOW MUST BE THE ROOM THE PLAYER WILL WALK INTO (owner,
            // 2026-09-18). It is the same object, so that holds by construction, with one gap: the
            // author fills a room's contents lazily, and forces the fill only on the way in through
            // the door. Seen in the log that day: a room borrowed at 15:00:20.9 and its contents
            // arriving at 15:00:22.5, so for a second and a half the window showed a room that was
            // not yet the real one. The fill is forced here too, before anything is drawn.
            // LAPS, because "switching a room on" is 48 to 76 ms and which part of it is not known.
            var lap = System.Diagnostics.Stopwatch.StartNew();
            EnsureHydratedNow(instance, "disaridan iceri bakis");
            PurgeLeakedCopiesOncePerRoom(instance);
            double hydrateMs = lap.Elapsed.TotalMilliseconds;

            s_OutsideInActivatedClone = !interior.activeSelf;
            if (s_OutsideInActivatedClone) interior.SetActive(true);
            double activateMs = lap.Elapsed.TotalMilliseconds - hydrateMs;

            // MAKE VISIBLE FIRST, STRIP COLLISION SECOND, and the order is not a preference. A bare
            // SetActive leaves the clone's item renderers off - measured at 167 of 595 drawing,
            // rising to 410 after this call - so without it the portal shows a room with no loot in
            // it, which is what the yard reported on 2026-09-15. The call also re-enables item
            // COLLIDERS, which Interactivity.cs says outright, so the sweep below has to come after
            // it or the loot is left solid and reachable through a wall.
            ShowInteriorLootForOutsideIn(instance);
            SwitchOnAllSectorsForOutsideIn(instance);
            double lootMs = lap.Elapsed.TotalMilliseconds - hydrateMs - activateMs;

            // The layer is the GameObject's, not the renderer's - Unity has no per-renderer layer -
            // so this necessarily moves each renderer's collider to layer 3 as well. That is
            // tolerable only because the collision sweep below switches those colliders off
            // entirely; nothing is left on layer 3 for TLD's interaction raycasts to find.
            s_OutsideInRelayered.Clear();
            s_OutsideInSavedLayers.Clear();
            s_OI.Layer = PickOutsideInRoomLayer(s_OI);
            for (int c = 0; c < s_OutsideInBarCaps.Count; c++)
                if (s_OutsideInBarCaps[c] != null) s_OutsideInBarCaps[c].layer = s_OI.Layer;
            RelayerInteriorForOutsideIn(interior);
            s_OutsideInNextRelayerSweep = Time.realtimeSinceStartup + OUTSIDE_IN_RELAYER_SWEEP_SECONDS;
            double relayerMs = lap.Elapsed.TotalMilliseconds - hydrateMs - activateMs - lootMs;

            DisableInteriorCollisionForOutsideIn(interior);
            double collisionMs = lap.Elapsed.TotalMilliseconds - hydrateMs - activateMs - lootMs - relayerMs;
            s_OutsideInLastBorrowLaps = SeamlessInteriorsLog.English
                ? $"ms per step: fill room contents {hydrateMs:F1}, switch on {activateMs:F1}, show items {lootMs:F1}, move to hidden layer {relayerMs:F1}, turn collision off {collisionMs:F1}"
                : $"dolum {hydrateMs:F1}, SetActive {activateMs:F1}, esyalar {lootMs:F1}, katman {relayerMs:F1}, carpisma {collisionMs:F1}";

            // The main camera's mask is edited once, by the first room held, and given back by the
            // last one returned: the saved value must be the game's, never an already edited one.
            Camera main = GameManager.GetMainCamera();
            if (main != null)
            {
                if (!s_OutsideInMaskEdited)
                {
                    s_OutsideInSavedMainMask = main.cullingMask;
                    s_OutsideInMaskEdited = true;
                }
                main.cullingMask = s_OutsideInSavedMainMask & ~OUTSIDE_IN_ROOM_LAYERS_MASK;
            }

            return true;
        }

        // ─── THE LAYER MOVE IS REPEATED, FOR THE REASON THE COLLIDER SWEEP IS ───
        //
        // Moving the interior to its private layer once, at the borrow, samples a hierarchy that is
        // still filling up. Found 2026-09-18 after a load: the room was borrowed at 13:14:38 and
        // "[GEAR-RESTORE] CampOffice: 55 gear" landed at 13:14:42, so everything the player owned in
        // that room arrived after the move and kept its ordinary layer. Measured live: 472
        // renderers on the private layer and 125 not, which the owner saw as "all of the objects
        // in the interior space floating in midair". In a co-located building those objects sit
        // inside the shell's walls and nobody sees them; parked, they hang in open sky.
        //
        // Swept twice a second, not every frame: a stray object showing for half a second costs
        // less than a GetComponentsInChildren over six hundred renderers on every frame. An object
        // already on the private layer is skipped, so the record never doubles up, and whatever
        // layer a late arrival had is what it is given back.
        private const float OUTSIDE_IN_RELAYER_SWEEP_SECONDS = 0.5f;
        private static float s_OutsideInNextRelayerSweep { get => s_OI.NextRelayerSweep; set => s_OI.NextRelayerSweep = value; }

        private static void RelayerInteriorForOutsideIn(GameObject interior)
        {
            var renderers = interior.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                var go = renderer.gameObject;
                if (go == null || go.layer == s_OI.Layer) continue;
                s_OutsideInRelayered.Add(renderer);
                s_OutsideInSavedLayers.Add(go.layer);
                go.layer = s_OI.Layer;
            }
        }

        // The clone carries its own TodAmbientLight with the values the room was authored to sit
        // under. Reading them beats inventing a color, and if the component is missing the ambient
        // swap simply does not happen rather than guessing.
        private static void CaptureOutsideInAmbient(SeamlessInteriorInstance instance)
        {
            s_OutsideInAmbientValid = false;
            if (instance == null || instance.MasterInterior == null) return;

            // Whatever the mode is now: the values are only used in Dark Atmosphere (ApplyOutsideInAmbient),
            // and the mode can be switched after this is read, so a TodAmbientLight in a dark-only part
            // counts. FindRoomAmbient leaves those out in Outdoor Lighting, which is right for its callers.
            TodAmbientLight ambient = null;
            foreach (var tod in instance.MasterInterior.GetComponentsInChildren<TodAmbientLight>(true))
                if (tod != null) { ambient = tod; break; }
            if (ambient == null) return;

            s_OutsideInAmbientDay = ambient.m_AmbientIndoorsDay;
            s_OutsideInAmbientNight = ambient.m_AmbientIndoorsNight;
            s_OutsideInAmbientValid = true;
        }

        // Hands the clone's collision back immediately, without tearing the rest of the portal
        // system down. A door transition needs the building solid BEFORE it snaps the player to a
        // floor; see the call in NotifyPortalUsed for why that is where this fires.
        //
        // This deliberately does NOT stop the portal system holding the clone open, because the
        // transition may still be cancelled. If the player stays outside, the next frame's sweep
        // simply disables everything again.
        //
        // EVERY HELD ROOM, not only the one behind the door. The caller does not say which building
        // the transition is into, a wrong guess is a player falling 40 m, and over-calling is safe
        // for the reason above.
        internal static void RestoreOutsideInCollisionNow()
        {
            for (int h = 0; h < s_OutsideInBuildings.Count; h++)
            {
                var building = s_OutsideInBuildings[h];
                if (building.DisabledColliders.Count == 0) continue;

                for (int i = 0; i < building.DisabledColliders.Count; i++)
                {
                    var collider = building.DisabledColliders[i];
                    if (collider != null) collider.enabled = true;
                }
                building.DisabledColliders.Clear();
                building.DisabledSet.Clear();
            }
        }

        // The mod's own call for putting a clone's items back on screen. Wrapped rather than
        // inlined so the one place that knows WHY the collision sweep must follow it is the one
        // place that calls it.
        private static void ShowInteriorLootForOutsideIn(SeamlessInteriorInstance instance)
        {
            if (instance == null) return;
            SetInteriorItemsVisible(instance, true);
        }

        // ─── A ROOM SEEN FROM OUTSIDE HAS LEFT ITS SECTORS ───
        //
        // Big interiors (the Pleasant Valley farmhouse: 19 sectors, Kitchen, WaterCloset,
        // Bathroom...) carry the game's BoxSectorManager, which switches off the furniture of every
        // room not visible from the one the player stands in. Leaving through the mod's door is a
        // teleport, so the game never sees the player leave: the sectors stay as the last room left
        // them, and the views in showed whatever that room could see. Found 2026-10-01 (1001-20):
        // out through the porch door from KitchenSmall the downstairs toilet was missing from
        // window 23, out from KitchenLeft it was there. The farmhouse is set to switch every sector
        // on when the player leaves (m_ActivateAllOnExit), so this makes that call for it. Inside,
        // the sector triggers take over again by themselves (read back the same day).
        private static void SwitchOnAllSectorsForOutsideIn(SeamlessInteriorInstance instance)
        {
            if (instance == null || instance.MasterInterior == null) return;
            foreach (var manager in instance.MasterInterior.GetComponentsInChildren<BoxSectorManager>(true))
            {
                if (manager == null) continue;
                try { manager.ForceAllActive(); }
                catch (System.Exception e)
                {
                    MelonLogger.Warning($"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: could not switch on the room's sectors: {e.Message}");
                }
            }
        }

        // Visible, never solid. See the comment on s_OutsideInDisabledColliders: an active clone
        // brings its collision with it, and the player ends up standing on the interior's floor
        // while walking past the building outside.
        private static void DisableInteriorCollisionForOutsideIn(GameObject interior)
        {
            // NO Clear() HERE. See the comment on s_OutsideInDisabledColliders: this runs every
            // frame, and rebuilding the record each time is what loses the restore information.
            // Solid while something thrown in through a window settles (ShootThroughWindow.cs).
            if (RoomHeldSolidForThrownIn(interior)) return;
            var colliders = interior.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || !collider.enabled) continue;
                collider.enabled = false;
                // Only record the flip the first time, so one collider cannot occupy two entries
                // and a collider the game legitimately disabled later is not re-enabled twice.
                if (s_OutsideInDisabledSet.Add(collider)) s_OutsideInDisabledColliders.Add(collider);
            }
        }

        // ─── DRAWING ONE WINDOW ───
        //
        // THIS FOLLOWS THE 2026-09-13 PROOF (handoffs/portal-proof-console.cs, SIPortal9) RATHER
        // THAN BEING REDERIVED, because the first attempt at it here was wrong in two measurable
        // ways and the proof was not.
        //
        // 1. ONE FIXED FULL-RESOLUTION TARGET, CREATED ONCE. The first version sized the target
        //    from the window's on-screen footprint each frame, rounded up to a power of two and
        //    clamped to the screen. Walking made it jump between 1024, 2048 and 3840, and every
        //    jump destroyed and recreated the RenderTexture.
        //
        // 2. RENDER ONLY THE VISIBLE PART OF THE WINDOW. This is the one that actually produced
        //    the pixelation, and it was measured: standing close, the aperture covered 3850 x 5076
        //    screen pixels while the target was clamped to 3840 x 2160, so the visible 2160 pixels
        //    of window height were fed by about 919 texels - 0.43 texels per pixel, magnified 2.3x.
        //    Most of the target was being spent on parts of the window hanging off screen.
        //
        //    Clipping the aperture against the main camera's frustum and rendering only the
        //    surviving sub-rectangle puts the whole target on the part that is actually visible, so
        //    the ratio stays at or above 1.0 texel per pixel at any distance. The quad is reshaped
        //    to that sub-rectangle and its UVs stay 0..1, so nothing else has to change.
        //
        //    The retired inside-out system already had this, as commit a6eb26c, "Render only the
        //    visible part of a window, at the size it appears". It simply was not carried across.
        //
        // A 3% margin is kept because being slightly too generous costs a few pixels of shading
        // while being slightly too tight shows a seam of the wrong world at the window edge.
        //
        // WHY Camera.Render() IS SAFE HERE AND WAS NOT FOR THE INSIDE-OUT PORTAL. A hand-driven
        // render draws whatever the game has finished activating at that moment, which is why the
        // inside-out portal came out with no trees and no rock outcrops: TLD settles terrain object
        // and vegetation activation late in the frame. An interior clone has no streaming and no
        // late activation - it is a static prefab this mod holds open itself - so there is nothing
        // to be early for. Rendering by hand is what lets the ambient swap below wrap the render.

        // THE VIEWS INTO A ROOM ARE DRAWN FROM THE PLAYER'S EYE AS THE GAME LEAVES IT, NOT AS THE MOD'S
        // TICK FINDS IT.
        //
        // The mod's OnLateUpdate runs before vp_FPSCamera.LateUpdate, where the game finishes
        // moving the player's camera for the frame. A single window's view was drawn in the tick,
        // so its room was drawn for one head position and the window round it for another. Owner,
        // 2026-10-10, at Camp Office window 7: the view "is bouncing up and down disproportionately
        // to the player camera as I approach the window", and backing up a slope, "there's a jump
        // in the window camera". Measured that day over 1,545 moving frames: the camera stood up to
        // 2.1 cm higher or lower, and 2.7 cm to the side, at the end of the frame than where the
        // tick had read it. With TAA on, that mismatch also smears, and no motion vector can
        // describe it. The wall views were already drawn from the postfix on
        // vp_FPSCamera.LateUpdate for the zoom's sake (PortalFrustumPatch.cs); this puts the single
        // windows there too. The tick still does everything else and only leaves the drawing.
        //
        // If that postfix did not run on the frame before (the game's camera component is off),
        // the tick draws as it always did.
        private static int s_OutsideInDrawLateFrame = -1;
        private static int s_OutsideInLateHookFrame = -10;
        private static bool s_OutsideInLateFaulted;

        private static bool OutsideInViewsDrawLate()
        {
            return !s_OutsideInLateFaulted && Time.frameCount - s_OutsideInLateHookFrame <= 1;
        }

        // Called from the postfix on vp_FPSCamera.LateUpdate, before the outside-in wall cameras.
        internal static void DrawOutsideInViewsLate()
        {
            s_OutsideInLateHookFrame = Time.frameCount;
            if (s_OutsideInLateFaulted || s_OutsideInDrawLateFrame != Time.frameCount) return;
            try
            {
                Camera main = GameManager.GetMainCamera();
                if (main == null) return;
                RenderOutsideInPortals(main);
                // The tick leaves the nearest building bound; drawing binds each building in turn.
                BindOutsideIn(NearestOutsideInBuilding());
            }
            catch (System.Exception e)
            {
                s_OutsideInLateFaulted = true;
                MelonLogger.Warning("[OUTSIDE-IN] Drawing the views after the player's camera has moved failed; they are drawn from the mod's tick again until the game is restarted: " + e);
            }
        }

        private static void RenderOutsideInPortals(Camera main)
        {
            if (!EnsureOutsideInSlotArrays()) return;

            Vector3 eye = main.transform.position;

            // Once per frame, not once per pane: the frustum depends only on the player's camera.
            // Same reason as the looking-out tick: the late capture owns these planes once it runs.
            if (!s_FrustumCapturedLate) BuildWindowPortalFrustum(main);

            s_OutsideInCandidates.Clear();
            s_OutsideInPriority.Clear();
            s_OutsideInCandidateBuilding.Clear();

            // EVERY HELD ROOM'S WINDOWS COMPETE FOR THE ONE POOL. The pool was always per window and
            // never per building, so two cabins side by side cost no more cameras than one: the
            // panes that look largest win, whichever building they belong to, which is also what
            // "the building the player is looking at" means without anyone having to decide it.
            for (int h = 0; h < s_OutsideInBuildings.Count; h++)
            {
                var held = s_OutsideInBuildings[h];
                // Held but too far for its windows to be worth a camera: its panes show the dark
                // backing, exactly as a building past the portal range always has.
                // Still fading out past the range counts: its views are drawn until they are gone.
                if (!held.Borrowed || !(held.CamerasOn || held.Level > 0f)) continue;
                BindOutsideIn(held);
                UpdateOutsideInRoomWindowSides(eye);

                // Per-aperture bookkeeping grows to match the building. Apertures only change when
                // the instance does, so this is a no-op in the steady state.
                while (s_OutsideInWasDrawn.Count < s_OutsideInApertures.Count) s_OutsideInWasDrawn.Add(false);

                for (int i = 0; i < s_OutsideInApertures.Count; i++)
                {
                    var candidate = s_OutsideInApertures[i];
                    if (!candidate.Paired || candidate.ViewFollows >= 0) continue;      // a follower is drawn by its leader

                    // The player must be OUTSIDE this pane. Outward points away from the building, so a
                    // positive distance from pane to eye means we are standing in the yard. This also
                    // rejects every pane on the far side of the building, which is most of them.
                    float eyeDistance = Vector3.Dot(eye - candidate.Center, candidate.Outward);
                    if (eyeDistance <= OUTSIDE_IN_MIN_EYE_DISTANCE) continue;

                    // The pane's apparent size, as for the views out (WindowPortals.cs): area,
                    // foreshortened, over the straight-line distance squared, not over the distance
                    // to the wall's plane, which every window on one wall shares.
                    float straight = Mathf.Max((eye - candidate.Center).magnitude, 0.1f);
                    float priority = (candidate.ViewWidth * candidate.ViewHeight) * (eyeDistance / straight) / (straight * straight);

                    // A pane already drawing defends its slot. Two equally sized panes on one wall tie
                    // this to four decimal places, and without hysteresis list order breaks the tie, so
                    // walking forward and back a few centimetres flips which one shows the interior.
                    if (i < s_OutsideInWasDrawn.Count && s_OutsideInWasDrawn[i]) priority *= OUTSIDE_IN_HYSTERESIS;

                    s_OutsideInCandidates.Add(i);
                    s_OutsideInCandidateBuilding.Add(held);
                    s_OutsideInPriority.Add(priority);
                }
            }

            // ALL of them in order, not only the first pool's worth. A candidate is only known to be
            // on screen once RenderOutsideInPane has clipped it, and one that is not gives its place
            // up. With one building there were never more candidates than slots, so stopping at the
            // pool size lost nothing; with several, the windows of a cabin behind the player would
            // fill the list and leave cameras idle while windows in plain view went dark.
            int drawCount = s_OutsideInCandidates.Count;

            // Selection sort: the list is at most the windows of the few buildings held.
            for (int a = 0; a < drawCount; a++)
            {
                int best = a;
                for (int b = a + 1; b < s_OutsideInCandidates.Count; b++)
                    if (s_OutsideInPriority[b] > s_OutsideInPriority[best]) best = b;
                if (best == a) continue;

                int swapIndex = s_OutsideInCandidates[a];
                s_OutsideInCandidates[a] = s_OutsideInCandidates[best];
                s_OutsideInCandidates[best] = swapIndex;

                float swapPriority = s_OutsideInPriority[a];
                s_OutsideInPriority[a] = s_OutsideInPriority[best];
                s_OutsideInPriority[best] = swapPriority;

                var swapBuilding = s_OutsideInCandidateBuilding[a];
                s_OutsideInCandidateBuilding[a] = s_OutsideInCandidateBuilding[best];
                s_OutsideInCandidateBuilding[best] = swapBuilding;
            }

            // Before the panes render, so a change shows in the frame it is made in.
            //
            // The interior's own snow slab is NEVER drawn while looking in. The exterior drift is
            // the copy standing on the ledge; the interior's slab seen through the portal is a
            // second one behind the frost, and it does not line up with the first, because the
            // interior's windows are not quite the shell's (owner, 2026-09-18).
            //
            // CHECKED AGAINST THE RENDERER ITSELF, EVERY FRAME. This used to hide each slab once and
            // remember having done it, and the memory was wrong within seconds: the mod re-enables
            // a clone's renderers in bulk, unconditionally, in three places (see the comment in
            // SetInteriorItemsVisible). Measured live on 2026-09-18 with the portals up: all eight
            // windows recorded as hidden, all eight slab renderers enabled and drawing. Eight bool
            // reads a frame is what it costs to be right.
            for (int h = 0; h < s_OutsideInBuildings.Count; h++)
            {
                var held = s_OutsideInBuildings[h];
                if (!held.Borrowed) continue;
                BindOutsideIn(held);

                for (int i = 0; i < s_OutsideInWasDrawn.Count; i++) s_OutsideInWasDrawn[i] = false;

                for (int i = 0; i < s_OutsideInApertureDrifts.Count; i++)
                {
                    var drifts = s_OutsideInApertureDrifts[i];
                    if (drifts == null) continue;
                    for (int k = 0; k < drifts.Count; k++)
                        if (drifts[k] != null && drifts[k].enabled) drifts[k].enabled = false;
                }
            }

            // Grouped panes are drawn by their wall's camera, in the postfix after the zoom; here their
            // group only takes a slot, at the place of its highest-ranked pane. See OutsideInWallCameras.cs.
            bool wallCameras = OutsideInWallCamerasWanted();
            BeginOutsideInPlaneFrame();
            if (wallCameras) BeginOutsideInWallFrame();
            // Panes already showing first, so one fading out keeps its slot. See ViewFade.cs.
            OrderOutsideInCandidatesForFade(wallCameras);

            int used = 0;
            for (int a = 0; a < drawCount && used < s_OutsideInSlotCount; a++)
            {
                int index = s_OutsideInCandidates[a];
                var candidateBuilding = s_OutsideInCandidateBuilding[a];
                bool wanted = s_OutsideInCandidateWanted[a];
                // Neither wanted nor still showing: nothing to draw, not even at zero.
                if (!wanted && OutsideInPaneLevel(candidateBuilding, index) <= 0f) continue;
                BindOutsideIn(candidateBuilding);
                if (wallCameras)
                {
                    int claim = ClaimOutsideInWallSlot(candidateBuilding, index, used, wanted);
                    if (claim > 0) { s_OutsideInSlotAperture[used] = index; used++; continue; }
                    if (claim == 0) continue;
                }
                MarkOutsideInPaneDrawn(candidateBuilding, index, wanted);
                if (!RenderOutsideInPane(index, used, main, eye)) { candidateBuilding.PaneDrawnFrame[index] = -1; continue; }
                if (index < s_OutsideInWasDrawn.Count) s_OutsideInWasDrawn[index] = true;
                s_OutsideInSlotAperture[used] = index;
                used++;
            }
            UpdateOutsideInPaneLevels(used >= s_OutsideInSlotCount);

            // A slot with nothing to draw stops drawing. Leaving it enabled would hang a frozen
            // image of wherever it last pointed in a window the player has since walked past.
            for (int slot = used; slot < s_OutsideInSlotCount; slot++) HideOutsideInSlot(slot);
        }

        // Dark glass, the way a window into an unlit room reads from outside. MARKED NEVER TO BE UNLOADED,
        // and put back if it is gone anyway: at the hangar on 2026-09-26 every backing's material read
        // an empty texture while all 42 backings stood on, and past the camera range the owner saw the
        // inside of the shell through every hole. A texture made at run time with no keep-alive flag
        // is the kind an unload of unused assets takes; that cause is not proven.
        private static void EnsureOutsideInBackingTexture()
        {
            if (s_OutsideInBackingMaterial == null) return;
            if (s_OutsideInBackingTexture != null && s_OutsideInBackingMaterial.mainTexture != null) return;
            s_OutsideInBackingTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            s_OutsideInBackingTexture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            s_OutsideInBackingTexture.SetPixel(0, 0, new Color(0.03f, 0.04f, 0.05f, 1f));
            s_OutsideInBackingTexture.Apply();
            s_OutsideInBackingMaterial.mainTexture = s_OutsideInBackingTexture;
        }

        // ─── PAST THE CAMERA RANGE THE BUILDING WEARS ITS OWN GLASS ───
        //
        // With the cameras off, a window showed the building's glass or the dark backing depending on
        // which model of it the game happened to draw: the far-away model's glass is never hidden and
        // covered the backing, the detailed model's is and did not. The game picks by size on screen,
        // so walking toward the hangar turned more windows into flat dark holes the nearer he came:
        // 1 at 119.9 m, 3 at 117.0, 5 at 114.0, 10 at 106.5, 14 at 102.1, then every window live at
        // 99.6 (owner, 2026-09-26, the backings dyed magenta to see them; log 0926-11 to 0926-16).
        // So while the cameras are off the hidden glass is given back and the backings and the mod's
        // frost overlays (the building's own glass carries its own frost) are put away, and the
        // building looks as the game made it until the views take over. The drifts on the ledges stay.
        private static void ShowShellGlassPastRange(bool show)
        {
            if (!s_OutsideInShellPanesHidden || s_OI.GlassShownPastRange == show) return;
            s_OI.GlassShownPastRange = show;
            for (int i = 0; i < s_OutsideInPaneRenderers.Count && i < s_OutsideInPaneSavedMaterials.Count; i++)
            {
                var renderer = s_OutsideInPaneRenderers[i];
                if (renderer == null) continue;
                Material[] saved = s_OutsideInPaneSavedMaterials[i];
                Material[] current = renderer.sharedMaterials;
                // Index for index: a split mesh's extra last submesh (a window left alone) is beyond
                // `saved` and keeps the building's glass either way.
                for (int k = 0; k < current.Length && k < saved.Length; k++)
                {
                    if (saved[k] == null || !IsShellGlassMaterial(saved[k])) continue;
                    current[k] = show ? saved[k] : s_OutsideInHiddenPaneMaterial;
                }
                renderer.sharedMaterials = current;
            }
        }

        // The parts of a window that do not need its portal, brought to the window's shared state.
        // Runs for as long as the building is dressed, portals or no portals.
        private static void UpdateOutsideInDressing()
        {
            EnsureOutsideInBackingTexture();

            // The building's own glass stays until its views have fully faded in, and comes back the
            // moment they start fading out. See ViewFade.cs.
            // Before the fade starts, so a window that is due is frosted when its view first shows and
            // not held clean by it. Asking is what frosts it (WindowFrostState.cs).
            if (s_OI.Level <= 0f)
                for (int i = 0; i < s_OutsideInApertures.Count; i++) IsWindowCleaned(s_OutsideInInstanceId, i);

            s_OI.Level = Mathf.MoveTowards(s_OI.Level, s_OI.CamerasOn ? 1f : 0f, OutsideInFadeStep());
            bool live = s_OI.CamerasOn && s_OI.Level >= 1f;
            ShowShellGlassPastRange(!live);
            for (int i = 0; i < s_OutsideInBackings.Count; i++)
            {
                var backing = s_OutsideInBackings[i];
                if (backing != null && backing.activeSelf != live) backing.SetActive(live);
            }

            // A frosted window wears its frost whether or not a portal is drawing behind it. The
            // shell's painted pane is hidden for as long as the building is dressed, so what stands
            // behind the overlay is either the room or the dark backing, never a second coat of
            // painted snow. It used to follow the portal, from when the painted pane came back
            // wherever no portal drew.
            for (int i = 0; i < s_OutsideInFrostOverlays.Count; i++)
            {
                var overlay = s_OutsideInFrostOverlays[i];
                if (overlay == null) continue;

                bool show = live && OutsideInFrostedPane && OutsideOldDressingShows(i, true);

                var overlayRenderer = overlay.GetComponent<MeshRenderer>();
                if (overlayRenderer != null && overlayRenderer.enabled != show) overlayRenderer.enabled = show;

                // The glass tint behind it, when the frost is the cut-out kind.
                if (overlay.transform.childCount > 0)
                {
                    var tintRenderer = overlay.transform.GetChild(0).GetComponent<MeshRenderer>();
                    bool showTint = show && OutsideInGlassTint > 0f;
                    if (tintRenderer != null && tintRenderer.enabled != showTint) tintRenderer.enabled = showTint;
                }
            }

            // The drift on the ledge does not depend on a portal drawing: it is real geometry on the
            // outside of the building, there whenever the window is frosted.
            for (int i = 0; i < s_OutsideInLedgeDrifts.Count; i++)
            {
                var drifts = s_OutsideInLedgeDrifts[i];
                if (drifts == null) continue;
                bool showDrift = InsideOutShowSnowSlab && OutsideOldDressingShows(i, false);
                for (int k = 0; k < drifts.Count; k++)
                    if (drifts[k] != null && drifts[k].activeSelf != showDrift) drifts[k].SetActive(showDrift);
            }

            TickOutsideCovers(live);
        }

        // Draws one pane into one slot. Returns false when the pane turns out not to be worth
        // drawing, which simply costs it its slot rather than taking the system down.
        private static bool RenderOutsideInPane(int index, int slot, Camera main, Vector3 eye)
        {
            // The pane's VIEW, which for the leader of a double window spans both panes.
            var pane = s_OutsideInApertures[index];
            pane.Center = pane.ViewCenter; pane.Delta = pane.ViewDelta;
            pane.Width = pane.ViewWidth; pane.Height = pane.ViewHeight;
            // Everything toward the viewer. See OutsideInViewTowardViewer.
            pane.Center += pane.Outward * pane.Toward;
            pane.Delta -= pane.Outward * pane.Toward + pane.RoomOutward * pane.Toward;

            Vector3 right = pane.Right;
            Vector3 up = pane.Up;
            Vector3 forward = -pane.Outward;       // looking INTO the building

            // The aperture plane, pushed clear of the shell's own pane so the two never z-fight.
            // The window plane: the shell's glass plane, set back into the wall by the one slider.
            // The frost glass rides a fixed 5 mm in front of it and the drift stands on the ledge in
            // front of that, so the three move as one rigid stack (owner, 2026-09-18).
            float recess = Mathf.Max(OutsideInWindowRecess, 0f);
            Vector3 planeCenter = pane.Center - pane.Outward * recess;
            Vector3 pa = planeCenter - right * (pane.Width * 0.5f) - up * (pane.Height * 0.5f);
            Vector3 pb = pa + right * pane.Width;
            Vector3 pc = pa + up * pane.Height;
            Vector3 pd = pb + up * pane.Height;

            // ── the visible part of the aperture, clipped against the main camera's frustum ──
            s_WindowPortalPoly.Clear();
            s_WindowPortalPoly.Add(pa);
            s_WindowPortalPoly.Add(pb);
            s_WindowPortalPoly.Add(pd);
            s_WindowPortalPoly.Add(pc);
            for (int i = 0; i < s_WindowPortalPlanes.Length; i++)
            {
                ClipWindowPortalPolygon(s_WindowPortalPoly, s_WindowPortalPolyB, s_WindowPortalPlanes[i]);
                s_WindowPortalPoly.Clear();
                for (int k = 0; k < s_WindowPortalPolyB.Count; k++) s_WindowPortalPoly.Add(s_WindowPortalPolyB[k]);
                if (s_WindowPortalPoly.Count == 0) return false;
            }
            if (s_WindowPortalPoly.Count < 3) return false;

            float u0 = pane.Width, u1 = 0f, v0 = pane.Height, v1 = 0f;
            for (int i = 0; i < s_WindowPortalPoly.Count; i++)
            {
                Vector3 d = s_WindowPortalPoly[i] - pa;
                float u = Vector3.Dot(d, right);
                float v = Vector3.Dot(d, up);
                if (u < u0) u0 = u;
                if (u > u1) u1 = u;
                if (v < v0) v0 = v;
                if (v > v1) v1 = v;
            }

            // A FRACTION OF THE VISIBLE SLICE, NOT OF THE WHOLE PANE. The same defect as the
            // looking-out path and fixed the same way: pane.Height * 0.03 is 16.1 cm a side on the
            // hangar's 5.38 m doors, which on a close or zoomed view is larger than the slice being
            // padded, so most of the render target's pixels land outside the view. The floor is
            // absolute on purpose - a fraction of the pane is exactly what went wrong.
            float marginU = Mathf.Max((u1 - u0) * OUTSIDE_IN_SUBRECT_MARGIN, OUTSIDE_IN_SUBRECT_MARGIN_FLOOR);
            float marginV = Mathf.Max((v1 - v0) * OUTSIDE_IN_SUBRECT_MARGIN, OUTSIDE_IN_SUBRECT_MARGIN_FLOOR);
            u0 = Mathf.Clamp(u0 - marginU, 0f, pane.Width);
            u1 = Mathf.Clamp(u1 + marginU, 0f, pane.Width);
            v0 = Mathf.Clamp(v0 - marginV, 0f, pane.Height);
            v1 = Mathf.Clamp(v1 + marginV, 0f, pane.Height);
            if (u1 - u0 < 0.005f || v1 - v0 < 0.005f) return false;

            Vector3 qa = pa + right * u0 + up * v0;
            Vector3 qb = pa + right * u1 + up * v0;
            Vector3 qc = pa + right * u0 + up * v1;
            Vector3 qd = pa + right * u1 + up * v1;

            Vector3 va = qa - eye, vb = qb - eye, vc = qc - eye;
            float near = Vector3.Dot(va, forward);
            if (near <= OUTSIDE_IN_MIN_EYE_DISTANCE) return false;

            // SIZED TO THE SUB-RECTANGLE'S OWN FOOTPRINT, NOT TO THE SCREEN.
            //
            // Milestone 1 gave every pane one fixed full-resolution target, which is right for one
            // window and ruinous for eight: 8 x 3840 x 2160 is a quarter of a gigabyte of render
            // texture for a building. Because the target now covers exactly the visible
            // sub-rectangle, sizing it to that sub-rectangle's on-screen footprint holds at or
            // above one texel per screen pixel while a distant pane costs a small texture. Sizes
            // are quantized to powers of two, which is its own hysteresis: leaving a bucket takes a
            // factor of two of movement rather than a pixel of jitter.
            int width, height;
            ComputeWindowPortalPixelSize(qa, qb, qc, main, out width, out height);
            if (!EnsureOutsideInSlot(slot, main, width, height)) return false;

            // The quad covers the sub-rectangle and the target covers exactly the same cone, so the
            // UVs stay 0..1 and only the corners move.
            var quad = s_OutsideInQuads[slot];
            var filter = quad.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                // A window that is not a rectangle shows the picture only inside its glass's outline.
                WriteViewSurface(filter.sharedMesh, pa, right, up, u0, u1, v0, v1, s_OutsideInApertures[index].Outline);

            // Kooima off-axis frustum whose near plane IS the aperture plane, so nothing between
            // the player and the window is drawn and the view lines up with the hole exactly.
            float left = Vector3.Dot(right, va);
            float rightExtent = Vector3.Dot(right, vb);
            float bottom = Vector3.Dot(up, va);
            float top = Vector3.Dot(up, vc);

            // THE FIT. A slide carries the pane's middle onto the room window's middle and nothing
            // more, so where the two differ in SIZE the room's frame stands inset from the building's
            // (the farmhouse's bay: 68.7 x 149 behind 70 x 162) or the pane shows past the room's
            // opening (Safe House A: 49 x 111 behind 64 x 124). Scaling the eye's place and the view's
            // four edges across and up by the two windows' ratio lays the room's opening exactly over
            // the building's. Depth is not scaled. The price is a view squashed unevenly by that
            // ratio, which is for the owner's eye to judge: see OutsideInViewFit.
            float fitAcross = Mathf.Lerp(1f, pane.FitAcross > 0f ? pane.FitAcross : 1f, OutsideInViewFit);
            float fitUp = Mathf.Lerp(1f, pane.FitUp > 0f ? pane.FitUp : 1f, OutsideInViewFit);
            left *= fitAcross; rightExtent *= fitAcross; bottom *= fitUp; top *= fitUp;

            Camera cam = s_OutsideInCams[slot];
            // Only a cleaned window starts its view behind the snow slab. See s_OutsideInApertureDrifts.
            // The camera's near plane IS the window plane, so the view starts as far into the room as
            // the window is set back, and there is nothing else to add. A separate look-in depth
            // existed for a few hours on 2026-09-18 and was removed as a second dial doing the same
            // job as the first.
            // The eye is CARRIED into the room, not slid: see CarryEyeIntoRoom. The camera looks
            // along the ROOM's axes; the frustum above was measured along the BUILDING's, and the
            // two sets of numbers are the same because the carry keeps them so.
            Vector3 fromPane = eye - pane.Center;
            cam.transform.position = pane.Center + pane.Delta
                                   + pane.RoomRight * (Vector3.Dot(fromPane, pane.Right) * fitAcross)
                                   + Vector3.up * (Vector3.Dot(fromPane, pane.Up) * fitUp)
                                   + pane.RoomOutward * Vector3.Dot(fromPane, pane.Outward);
            cam.transform.rotation = Quaternion.LookRotation(-pane.RoomOutward, Vector3.up);
            // The slot's camera may last have drawn a wall, with a view matrix of its own.
            cam.ResetWorldToCameraMatrix();
            cam.nearClipPlane = near;
            cam.farClipPlane = OUTSIDE_IN_FAR_CLIP;

            // Unity picks LOD from fieldOfView rather than from a custom projectionMatrix. It is the
            // PLAYER'S field of view, captured after the zoom, for the reason DrawOneWindowPortal
            // gives: the angle the window spans made LOD depend on how close the player stood, and
            // the value read here, before the zoom, is the unzoomed one on 5 frames in 6.
            cam.fieldOfView = s_FrustumCapturedLate ? s_LateFieldOfView : main.fieldOfView;
            cam.aspect = (rightExtent - left) / (top - bottom);
            // ORDER MATTERS: assigning fieldOfView or aspect resets an explicit projection matrix.
            cam.projectionMatrix = Matrix4x4.Frustum(left, rightExtent, bottom, top, near, OUTSIDE_IN_FAR_CLIP);
            // The same carry as the position above, as one matrix, undone. See ViewMotionVectors.cs.
            float carriedAcross, carriedUp;
            TellTaaHowViewMoves(cam, main, OutsideInBuildingToRoom(pane, out carriedAcross, out carriedUp).inverse);

            // The clone's own lighting is gone in Outdoor Lighting mode - lightmaps stripped and 98
            // of about 103 lights destroyed at region load - so the room is lit by the outdoor
            // ambient unless this is swapped for the interior's own TodAmbientLight values around
            // the render. Restored in a finally so an exception cannot leave the WORLD lit like a
            // cabin interior.
            var savedMode = RenderSettings.ambientMode;
            var savedLight = RenderSettings.ambientLight;
            // Nor is the room's painted outside, which showed white between the bars at steep angles
            // (see OutsideInBackdrops).
            try
            {
                SwitchOffForViewIn(OutsideInBackdrops(s_OI), s_OutsideInWallSwitchedOff);
                ApplyOutsideInAmbient(s_OI);
                cam.cullingMask = 1 << s_OI.Layer;
                cam.Render();
            }
            finally { RenderSettings.ambientMode = savedMode; RenderSettings.ambientLight = savedLight; RestoreOutsideInLightingStrength(); SwitchBackOn(s_OutsideInWallSwitchedOff); }

            var quadRenderer = quad.GetComponent<MeshRenderer>();
            if (quadRenderer != null)
            {
                ApplyViewFade(quadRenderer, slot, OutsideInPaneLevel(s_OI, index));
                quadRenderer.enabled = true;
                TellTaaWhichSheet(cam, quadRenderer);
            }
            return true;
        }

        private static void HideOutsideInSlot(int slot)
        {
            if (s_OutsideInQuads == null || slot < 0 || slot >= s_OutsideInQuads.Length) return;
            var quad = s_OutsideInQuads[slot];
            if (quad == null) return;
            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
            if (slot < s_OutsideInSlotAperture.Count) s_OutsideInSlotAperture[slot] = -1;
        }

        // THE ROOM THROUGH THE WINDOW IS LIT AS IT IS FOR SOMEONE STANDING IN IT, in whichever Interior
        // Lighting Mode is on, and follows the mode when it is switched from outside. Owner, 2026-10-09:
        // "The rooms should look the same through the window as it would if you were standing in it."
        //
        // Outdoor Lighting: a room is lit at the outdoor level, so nothing is swapped. Dark Atmosphere:
        // the room's own TodAmbientLight values, or for a room with none the outdoor level darkened by
        // the factor ClonedInteriorLightingGuard uses for the same room from inside (its last resort,
        // FALLBACK_DARK_FACTOR).
        //
        // Until this the room's own values were swapped in in BOTH modes (the 2026-09-13 proof, made
        // when the mode could not change in play), so a room that has them never showed the switch:
        // the Thompson houses stayed at 0.05 to 0.06 against 0.50 outdoors in either mode, read live,
        // and passed at Thompson House 4 with the swap off in Outdoor Lighting ("Seems to work, but
        // just not toggleable while outside").
        private const float OUTSIDE_IN_DARK_FACTOR_NO_OWN_AMBIENT = 0.30f;

        // The room's _LightingStrength (near black at night) dims its lightmaps; without it the view in was
        // a bright room at night while inside was dark (player's FarmHouse shots, 2.3.0). Render only.
        private static readonly int s_OutsideInLightingStrengthId = Shader.PropertyToID("_LightingStrength");
        private static bool s_OutsideInLightingStrengthSwapped;
        private static Vector4 s_OutsideInLightingStrengthSaved;

        private static void ApplyOutsideInAmbient(OutsideInBuilding b)
        {
            if (!IsDarkAtmosphereMode) return;
            Vector4 roomStrength;
            if (!s_OutsideInLightingStrengthSwapped && b != null
                && ClonedInteriorLightingGuard.TryGetRoomLightingStrength(b.Instance, out roomStrength))
            {
                s_OutsideInLightingStrengthSaved = Shader.GetGlobalVector(s_OutsideInLightingStrengthId);
                Shader.SetGlobalVector(s_OutsideInLightingStrengthId, roomStrength);
                s_OutsideInLightingStrengthSwapped = true;
            }
            if (s_OutsideInAmbientValid)
            {
                var tod = GameManager.GetTimeOfDayComponent();
                bool night = tod != null && tod.IsNight();
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = night ? s_OutsideInAmbientNight : s_OutsideInAmbientDay;
                return;
            }
            Color outdoors = RenderSettings.ambientLight;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(outdoors.r * OUTSIDE_IN_DARK_FACTOR_NO_OWN_AMBIENT, outdoors.g * OUTSIDE_IN_DARK_FACTOR_NO_OWN_AMBIENT,
                                                    outdoors.b * OUTSIDE_IN_DARK_FACTOR_NO_OWN_AMBIENT, outdoors.a);
        }

        private static void RestoreOutsideInLightingStrength()
        {
            if (!s_OutsideInLightingStrengthSwapped) return;
            s_OutsideInLightingStrengthSwapped = false;
            Shader.SetGlobalVector(s_OutsideInLightingStrengthId, s_OutsideInLightingStrengthSaved);
        }

        // The slot pool is sized from the preference once, and the preference is read through a NEW
        // key rather than a raised default: MelonPreferences only applies a default when the key is
        // absent, so every existing preferences file would otherwise keep the old value.
        private static bool EnsureOutsideInSlotArrays()
        {
            if (s_OutsideInCams != null && s_OutsideInSlotCount > 0) return true;

            int wanted = s_PrefWindowPortalLimit != null ? s_PrefWindowPortalLimit.Value : 10;
            s_OutsideInSlotCount = Mathf.Clamp(wanted, 1, 16);

            s_OutsideInCams = new Camera[s_OutsideInSlotCount];
            s_OutsideInTargets = new RenderTexture[s_OutsideInSlotCount];
            s_OutsideInMaterials = new Material[s_OutsideInSlotCount];
            s_OutsideInQuads = new GameObject[s_OutsideInSlotCount];

            s_OutsideInSlotAperture.Clear();
            for (int i = 0; i < s_OutsideInSlotCount; i++) s_OutsideInSlotAperture.Add(-1);
            return true;
        }

        // Builds slot `slot` the first time something needs it, and rebuilds its target when the
        // size it needs changes. Returns false if the slot cannot be made, which means that pane
        // goes undrawn this frame rather than the system falling over.
        private static bool EnsureOutsideInSlot(int slot, Camera main, int width, int height)
        {
            if (s_OutsideInCams == null || slot < 0 || slot >= s_OutsideInSlotCount) return false;

            var target = s_OutsideInTargets[slot];
            if (target != null && (target.width != width || target.height != height))
            {
                // The pane changed size on screen, or the player changed resolution. Drop the stale
                // target rather than keeping this portal at the old pixel count all session.
                if (s_OutsideInCams[slot] != null) s_OutsideInCams[slot].targetTexture = null;
                target.Release();
                Object.Destroy(target);
                s_OutsideInTargets[slot] = null;
                target = null;
            }

            if (target == null)
            {
                // HDR is required: an LDR target double-tonemaps and blows the sky out.
                // RGB111110Float where supported gives the same result as ARGBHalf at half the
                // memory, and the quad is opaque so the alpha channel is unused either way.
                RenderTextureFormat format =
                    SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float)
                        ? RenderTextureFormat.RGB111110Float
                        : RenderTextureFormat.ARGBHalf;

                target = new RenderTexture(width, height, 24, format);
                target.name = "SI_OutsideInPortalTarget" + slot;
                if (!target.Create()) { Object.Destroy(target); return false; }
                s_OutsideInTargets[slot] = target;

                if (s_OutsideInMaterials[slot] == null)
                {
                    // Queue 2600: after TLD's alpha-tested foliage at 2450 and before transparent at
                    // 3000. Unlit/Texture defaults to 2000, and on a deferred camera a forward unlit
                    // shader does not reliably occlude geometry drawn later in the frame, so every
                    // tree painted itself over the portal.
                    s_OutsideInMaterials[slot] = new Material(Shader.Find("Unlit/Texture"));
                    s_OutsideInMaterials[slot].renderQueue = WINDOW_PORTAL_RENDER_QUEUE;
                }
                s_OutsideInMaterials[slot].mainTexture = target;
            }

            if (s_OutsideInCams[slot] == null)
            {
                var camObject = new GameObject("SI_OutsideInPortalCamera" + slot);
                var cam = camObject.AddComponent<Camera>();

                // DISABLED, and rendered by hand. Unlike the inside-out portal there is nothing to
                // be early for: a clone does not stream and is not activated late in the frame.
                cam.enabled = false;
                cam.depth = -60f;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.allowHDR = true;                 // LDR here double-tonemaps and blows out
                cam.useOcclusionCulling = false;     // baked for the player's viewpoint
                cam.cullingMask = 1 << OUTSIDE_IN_INTERIOR_LAYER;   // ONLY the interior
                if (main != null)
                {
                    cam.allowMSAA = main.allowMSAA;
                    cam.renderingPath = main.renderingPath;
                    cam.depthTextureMode = main.depthTextureMode;
                }
                s_OutsideInCams[slot] = cam;
            }

            if (s_OutsideInCams[slot].targetTexture != s_OutsideInTargets[slot])
                s_OutsideInCams[slot].targetTexture = s_OutsideInTargets[slot];

            if (s_OutsideInQuads[slot] == null)
            {
                var quad = new GameObject("SI_OutsideInPortalQuad" + slot);
                quad.layer = WINDOW_PORTAL_QUAD_LAYER;   // main camera draws it, portal camera does not

                var mesh = new Mesh();
                mesh.vertices = new Vector3[4];
                mesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
                mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };

                var filter = quad.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = quad.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                // ─── MOTION VECTORS, AND WHY THIS IS BACK TO THE DEFAULT ───
                //
                // This was MotionVectorGenerationMode.Camera, reasoning that the quad's vertices
                // are rewritten in world space each frame while its transform never moves, so
                // Object motion vectors would describe a stationary object whose geometry
                // teleports. The reasoning is sound and the choice was still wrong.
                //
                // Camera mode means "do not render me into the motion vector buffer; derive my
                // motion from the DEPTH BUFFER instead". The depth at those pixels is the QUAD's -
                // the window plane - while the pixels show interior metres further back. So TAA is
                // handed the parallax of the glass for content that is nowhere near it, and
                // reprojects every portal pixel against the wrong history.
                //
                // That predicts smearing under translation and none under rotation, because
                // rotation-only motion vectors are depth-independent. Owner reported exactly that
                // on 2026-09-15: "moving only, not turning on the spot". It also explains why the
                // 2026-09-13 REPL proof was remembered as clean - it never set this property, so it
                // ran on Unity's default, and Camera mode arrived later with the DLL port.
                //
                // NOT A PROVEN FIX. Object mode still cannot describe the true per-pixel parallax
                // of a virtual scene behind a flat quad; nothing can, short of writing real depth.
                // What it does is stop actively feeding TAA a wrong answer derived from the glass.
                // If the smearing survives this, the remaining routes are a shader writing true
                // depth via SV_Depth (needs an asset bundle) or drawing the interior as real
                // geometry in the main camera's pass with CommandBuffer.DrawRenderer and a stencil.
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                renderer.enabled = false;

                s_OutsideInQuads[slot] = quad;
            }

            // Re-assigned every time because the material's texture is replaced whenever the slot's
            // target is rebuilt, and a quad still pointing at the released one renders nothing.
            var quadRenderer2 = s_OutsideInQuads[slot].GetComponent<MeshRenderer>();
            if (quadRenderer2 != null && quadRenderer2.sharedMaterial != s_OutsideInMaterials[slot])
                quadRenderer2.sharedMaterial = s_OutsideInMaterials[slot];

            return true;
        }

        private static void DestroyOutsideInSlots()
        {
            if (s_OutsideInCams == null) { s_OutsideInSlotCount = 0; return; }

            for (int slot = 0; slot < s_OutsideInSlotCount; slot++)
            {
                if (s_OutsideInQuads[slot] != null) { Object.Destroy(s_OutsideInQuads[slot]); s_OutsideInQuads[slot] = null; }
                if (s_OutsideInCams[slot] != null)
                {
                    s_OutsideInCams[slot].targetTexture = null;
                    Object.Destroy(s_OutsideInCams[slot].gameObject);
                    s_OutsideInCams[slot] = null;
                }
                if (s_OutsideInTargets[slot] != null)
                {
                    s_OutsideInTargets[slot].Release();
                    Object.Destroy(s_OutsideInTargets[slot]);
                    s_OutsideInTargets[slot] = null;
                }
                if (s_OutsideInMaterials[slot] != null) { Object.Destroy(s_OutsideInMaterials[slot]); s_OutsideInMaterials[slot] = null; }
            }

            s_OutsideInCams = null;
            s_OutsideInTargets = null;
            s_OutsideInMaterials = null;
            s_OutsideInQuads = null;
            s_OutsideInSlotCount = 0;
            s_OutsideInSlotAperture.Clear();
            s_OutsideInWasDrawn.Clear();
        }


        // ─── WHERE IN THE ROOM A VIEW FROM OUTSIDE STARTS: BEHIND THE ROOM'S OWN FRAME ───
        //
        // The building's glass is the camera's near plane, and the point of the room that is laid on
        // it decides what of the room survives the cut. It used to be the room window's PortalCenter,
        // a few centimeters OUTSIDE the room's glass, which keeps the room's whole frame in the
        // picture. Seen from the yard that frame is a tunnel 15 cm deep (the farmhouse's runs from
        // 5 cm outside its glass to 10 cm inside it), and in perspective a tunnel is a second,
        // smaller frame floating inside the building's, its bar at another height. No slide and no
        // scale removes a tunnel: the owner chased it with depth sliders for an hour on 2026-09-19
        // ("there's a dead band right where I need pull everything in to"; the dead band was the cut
        // passing THROUGH the frame, showing its hollow inside). What cleared it was the cut standing
        // BEHIND the frame, at the inner face of the room's wall, with the view fitted: one frame,
        // the building's, and the room's opening filling it edge to edge ("That fixed the bay
        // windows"). Nothing is switched off; from inside every frame is still there.
        //
        // How the room's frames should look from outside is DEFERRED by him to a polish phase
        // ("We'll defer interior frame visibility"), so this is a lever, not a settled design.
        //
        // Each window's own depth: how far its frame reaches into the room, read from the frame's
        // corners near the opening's rim, plus a little. The farmhouse's bay 9.9 cm, its doubles
        // 9.9, its single windows 11.1: one number for the building does not serve.
        // CORRECTED THE SAME DAY. What the owner had called perfect ("BINGO") was the state just BEFORE
        // the cut went behind the frame: the view fitted at 100% with the cut AT THE FRAME'S OUTER
        // FACE, the room's frame in the picture and lying on the building's. The agent then moved the
        // cut behind the frame on its own idea and made that the default; he, afterwards: "you
        // seemed to have fit the interior frame to the exterior frame which looked perfect, but then
        // shortly afterward, you just completely removed the interior frame from view." So:
        //
        //   0  where the room-window finder put it (PortalCenter), as before 2026-09-19;
        //   1  AT THE OUTER FACE of the room's own frame, measured window by window: THE DEFAULT. The
        //      farmhouse's bay stood there already, by his own 5 cm; its doubles and singles stood
        //      4 to 10 mm INSIDE their frames' faces (6.4 and 6.9 cm against 7.4 and 7.3), so a
        //      sliver of frame was sliced off and showed as a thin hollow line ("Still a gap");
        //   2  behind the frame, the frame not in the picture at all.
        internal static int OutsideInCutMode = 1;

        // How far the picture reaches past the hole on every side, in meters, and what the fit is
        // multiplied by (under 1 draws the room larger). THE OWNER'S FINAL NUMBERS, read from the
        // running game three times and confirmed by him on 2026-09-19: the picture 3.0 cm wider and
        // taller (1.5 cm a side) and the fit at 97%, the 97% at the farmhouse only (below). An earlier commit wrote 5 cm a side from a read
        // taken BEFORE he had finished; it never ran. Levers: WindowFitDev.ViewPastHoleCm and
        // ViewZoomPercent. See AssignOutsideInViews.
        internal static float OutsideInViewPastHole = 0.015f;
        // THE 97% IS THE FARMHOUSE'S ALONE: its row in BuildingWindowOverrides.cs carries it, and this
        // lever multiplies whatever the building's row says (1 = as the row says).
        internal static float OutsideInViewZoom = 1f;

        // "Everything toward you", his third number, 1.0 cm: the picture stands that far in FRONT of
        // the building's glass, inside the exterior frame's depth, and the room comes that far
        // toward the viewer with it. Written exactly as the slider that he set did it: the picture's
        // plane moves out by this much and the room point laid on it moves INTO the room by the same,
        // so the room as a whole reads 2 cm nearer. The frost and the dark backing go with the
        // picture; the snow on the sill stays on the sill. See RenderOutsideInPane.
        internal static float OutsideInViewTowardViewer = 0.01f;

        // The picture plane of one window: its own row value, else its building's, else the global above.
        // Meters. Worked out when the windows are fitted and kept on the window (ShellAperture.Toward).
        private static float PicturePlaneFor(string shellName, int window)
        {
            BuildingWindowOverride row;
            if (!string.IsNullOrEmpty(shellName) && TryGetBuildingWindowOverride(shellName, out row))
            {
                if (row.PicturePlaneCmByWindow != null && window >= 0 && window < row.PicturePlaneCmByWindow.Length && !float.IsNaN(row.PicturePlaneCmByWindow[window]))
                    return row.PicturePlaneCmByWindow[window] * 0.01f;
                if (!float.IsNaN(row.PicturePlaneCm)) return row.PicturePlaneCm * 0.01f;
            }
            return OutsideInViewTowardViewer;
        }
        internal static bool OutsideInCutBehindRoomFrame { get { return OutsideInCutMode == 2; } }
        private const float ROOM_FRAME_FACE_CLEARED_BY = 0.001f;
        private static bool s_OutsideInRebuildAsked;
        private const float ROOM_FRAME_RIM = 0.12f;
        private const float ROOM_FRAME_DEEPEST = 0.30f;
        private const float ROOM_FRAME_CLEARED_BY = 0.003f;

        // THE POINT IN THE ROOM A VIEW FROM OUTSIDE IS TAKEN FROM: where along the line through the wall
        // it starts (RoomPointAlongTheWall), and then, for one window set by hand, slid sideways and
        // up IN THE ROOM. That is not the picture's place on the building's wall (WindowFit.PortalRight
        // and PortalUp move that): it is where the camera stands indoors. Owner, 2026-09-20, of the
        // opening over Grey Mother's door: "I need to move the camera position inside the indoor
        // prefab, not the position of the portal on the exterior wall."
        private static Vector3 RoomPointBehindPane(InteriorWindowOpening opening, string shellName, int window)
        {
            Vector3 point = RoomPointAlongTheWall(opening, shellName, window);
            WindowFit thisWindow;
            if (window >= 0 && TryGetWindowFit(shellName, window, out thisWindow) && (thisWindow.RoomRightCm != 0f || thisWindow.RoomUpCm != 0f))
                point += opening.Right * (thisWindow.RoomRightCm * 0.01f) + opening.Up * (thisWindow.RoomUpCm * 0.01f);
            return point;
        }

        private static Vector3 RoomPointAlongTheWall(InteriorWindowOpening opening, string shellName, int window)
        {
            // A start set by hand wins over anything measured: it was judged by eye where measuring
            // had already given its answer. This one window's first (WindowFit.ViewStartCm), then the
            // building's (s_BuildingWindowOverrides).
            WindowFit thisWindow;
            if (window >= 0 && TryGetWindowFit(shellName, window, out thisWindow) && thisWindow.HasViewStart)
                return opening.GlassCenter + opening.Outward * (thisWindow.ViewStartCm * 0.01f);

            BuildingWindowOverride byHand;
            if (TryGetBuildingWindowOverride(shellName, out byHand) && !float.IsNaN(byHand.ViewFromOutsideStartsCm))
                return opening.GlassCenter + opening.Outward * (byHand.ViewFromOutsideStartsCm * 0.01f);

            // A band strip knows its frame's depth from the frame's box (TryAddBandOfWindows): the view
            // starts 15 cm past a frame that stands inside the glass, and at the glass where the frame
            // stands outside it. His pass at the hangar, 2026-09-21: "The interior frames are gone."
            if (opening.FrameDepthKnown)
                return opening.GlassCenter - opening.Outward * (opening.FrameDepthIntoRoom > -BAND_VIEW_PAST_INBOARD_FRAME ? opening.FrameDepthIntoRoom + BAND_VIEW_PAST_INBOARD_FRAME : 0f);

            float reach;
            if (OutsideInCutMode == 2 && TryMeasureRoomFrameReach(opening, true, out reach))
                return opening.GlassCenter - opening.Outward * (reach + ROOM_FRAME_CLEARED_BY);

            // A bent glass keeps the plane the owner set by eye for it (BENT_GLASS_VIEW_OUTSIDE_GLASS):
            // round a bend the corner posts reach further out than the panes' own frames do.
            if (OutsideInCutMode == 1 && opening.Companions == null && TryMeasureRoomFrameReach(opening, false, out reach))
            {
                Vector3 onGlass = opening.PortalCenter - opening.Outward * Vector3.Dot(opening.PortalCenter - opening.GlassCenter, opening.Outward);
                return onGlass + opening.Outward * (reach + ROOM_FRAME_FACE_CLEARED_BY);
            }
            return opening.PortalCenter;
        }

        // How far the window's own frame reaches from its glass, in meters: INTO the room, or OUT of it
        // toward the yard.
        private static bool TryMeasureRoomFrameReach(InteriorWindowOpening opening, bool intoRoom, out float inner)
        {
            inner = 0f;
            if (opening.PaneRoot == null) return false;

            var parts = new List<Renderer>(opening.PaneRoot.GetComponentsInChildren<Renderer>(true));
            if (opening.Companions != null) parts.AddRange(opening.Companions);

            float halfWidth = (opening.GlassWidth > 0.01f ? opening.GlassWidth : opening.Width) * 0.5f;
            float halfHeight = (opening.GlassHeight > 0.01f ? opening.GlassHeight : opening.Height) * 0.5f;
            bool any = false;

            for (int r = 0; r < parts.Count; r++)
            {
                var renderer = parts[r] as MeshRenderer;
                if (renderer == null) continue;
                string matName = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : string.Empty;
                string objName = renderer.gameObject.name;
                if (objName.StartsWith("SeamlessInteriors_", System.StringComparison.Ordinal)) continue;
                if (MatchesWindowRole(0, objName, matName) || MatchesWindowRole(1, objName, matName) || MatchesWindowRole(3, objName, matName)) continue;
                if (matName.IndexOf("FrostedGlass", System.StringComparison.Ordinal) >= 0) continue;
                if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                Vector3[] vertices = filter.sharedMesh.vertices;
                Transform t = renderer.transform;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = t.TransformPoint(vertices[i]) - opening.GlassCenter;
                    float across = Mathf.Abs(Vector3.Dot(p, opening.Right)), up = Mathf.Abs(Vector3.Dot(p, opening.Up));
                    // Round the opening's rim only: a sill board or a shelf further off is not the frame.
                    if (across > halfWidth + ROOM_FRAME_RIM || up > halfHeight + ROOM_FRAME_RIM) continue;
                    if (across < halfWidth - ROOM_FRAME_RIM && up < halfHeight - ROOM_FRAME_RIM) continue;
                    float into = intoRoom ? -Vector3.Dot(p, opening.Outward) : Vector3.Dot(p, opening.Outward);
                    if (into < 0f || into > ROOM_FRAME_DEEPEST) continue;
                    if (into > inner) inner = into;
                    any = true;
                }
            }
            return any && inner > 0.005f;
        }

        // ─── ONE VIEW FOR A DOUBLE WINDOW ───
        //
        // The Pleasant Valley farmhouse has five windows that are ONE wide window in the room
        // (1.69 m) and TWO panes with a post between them outside (0.70 m each). Pane by pane that
        // went wrong twice over: the room's window was paired to ONE of the two panes, center to
        // center, so that pane's view stood half a pane-spacing off to the side; and its neighbor,
        // with no room window left for it, became a pretend window with a camera of its own. Two
        // renders for one hole in the wall, not promised to agree at the post. The owner,
        // 2026-09-19: "We may have to adopt a double window approach as a single camera here."
        //
        // Two panes in one wall seen from one eye ARE one perspective view, so one camera whose
        // rectangle spans both is exactly right for both; the post is real geometry in front of
        // the picture and hides the strip behind it. Frost, backing and snow stay pane by pane.
        //
        // A double is: a real pair whose room window is much wider than its pane, and an unpaired
        // (or pretend) pane in the same wall, level with it, such that the two panes and the gap
        // between them come to the room window's width.
        private const float DOUBLE_WINDOW_ROOM_WIDER_BY = 1.4f;
        private const float DOUBLE_WINDOW_SAME_PLANE = 0.05f;
        private const float DOUBLE_WINDOW_WIDTH_AGREES_WITHIN = 0.30f;

        // A PRETEND WINDOW'S VIEW, MOVED BY HAND. A building window with no window in the room looks in
        // from where the real pairs say the room stands (see Pretend), and nothing a hand set on it
        // reached that point: the camera levers are read for a room window, and it has none. Thompson's
        // Store has seven windows outside and two in the room, so five of its views could not be moved
        // (owner, 2026-10-09: "'camera right' still does nothing"). The same three levers, in the same
        // directions a room window's own frame gives them: right as seen from inside looking out, up,
        // and the view's start along the wall's outward (+ toward outdoors), here from where the view
        // would otherwise start, since there is no room glass to measure it from.
        private static Vector3 PretendWindowHandMove(string shellName, int window, Vector3 roomOutward)
        {
            WindowFit fit;
            if (string.IsNullOrEmpty(shellName) || !TryGetWindowFit(shellName, window, out fit)) return Vector3.zero;
            Vector3 right = Vector3.Cross(Vector3.up, roomOutward).normalized;
            Vector3 move = right * (fit.RoomRightCm * 0.01f) + Vector3.up * (fit.RoomUpCm * 0.01f);
            if (fit.HasViewStart) move += roomOutward * (fit.ViewStartCm * 0.01f);
            return move;
        }

        private static void AssignOutsideInViews(string id)
        {
            string viewsShellName = s_OutsideInInstance != null && s_OutsideInInstance.Config != null ? s_OutsideInInstance.Config.ExteriorShellPrefabName : null;
            int count = s_OutsideInApertures.Count;
            for (int a = 0; a < count; a++)
            {
                var pane = s_OutsideInApertures[a];
                pane.ViewCenter = pane.Center; pane.ViewDelta = pane.Delta;
                if (pane.Paired && pane.Pretend) pane.ViewDelta += PretendWindowHandMove(viewsShellName, a, pane.RoomOutward);
                pane.ViewWidth = pane.Width; pane.ViewHeight = pane.Height;
                pane.ViewFollows = -1;
                s_OutsideInApertures[a] = pane;
            }

            string doubles = "";
            for (int a = 0; a < count; a++)
            {
                var pane = s_OutsideInApertures[a];
                if (!pane.Paired || pane.Pretend || pane.ViewFollows >= 0 || a >= s_OutsideInApertureInteriorSize.Count || a >= s_OutsideInApertureOpenings.Count) continue;
                float roomWidth = s_OutsideInApertureInteriorSize[a].x;
                if (roomWidth < pane.BaseWidth * DOUBLE_WINDOW_ROOM_WIDER_BY) continue;

                int partner = -1; float bestMiss = DOUBLE_WINDOW_WIDTH_AGREES_WITHIN;
                for (int b = 0; b < count; b++)
                {
                    if (b == a) continue;
                    var other = s_OutsideInApertures[b];
                    if ((other.Paired && !other.Pretend) || other.ViewFollows >= 0) continue;
                    if (Vector3.Dot(other.Outward, pane.Outward) < 0.999f) continue;
                    Vector3 between = other.BaseCenter - pane.BaseCenter;
                    if (Mathf.Abs(Vector3.Dot(between, pane.Outward)) > DOUBLE_WINDOW_SAME_PLANE) continue;
                    if (Mathf.Abs(Vector3.Dot(between, pane.Up)) > DOUBLE_WINDOW_SAME_PLANE) continue;
                    float spacing = Mathf.Abs(Vector3.Dot(between, pane.Right));
                    float miss = Mathf.Abs(spacing + (pane.BaseWidth + other.BaseWidth) * 0.5f - roomWidth);
                    if (miss >= bestMiss) continue;
                    bestMiss = miss; partner = b;
                }
                if (partner < 0) continue;

                var follower = s_OutsideInApertures[partner];

                // The rectangle over both panes, as they are DRAWN (a hand-set fit included)...
                float acrossOther = Vector3.Dot(follower.Center - pane.Center, pane.Right);
                float left = Mathf.Min(-pane.Width * 0.5f, acrossOther - follower.Width * 0.5f);
                float right = Mathf.Max(pane.Width * 0.5f, acrossOther + follower.Width * 0.5f);
                pane.ViewCenter = pane.Center + pane.Right * ((left + right) * 0.5f);
                pane.ViewWidth = right - left;
                pane.ViewHeight = Mathf.Max(pane.Height, follower.Height);

                // ...and the slide that carries the middle of the two panes AS MEASURED onto the middle
                // of the room's window, which is what a single pane's Delta is for a single window.
                float baseAcrossOther = Vector3.Dot(follower.BaseCenter - pane.BaseCenter, pane.Right);
                float baseLeft = Mathf.Min(-pane.BaseWidth * 0.5f, baseAcrossOther - follower.BaseWidth * 0.5f);
                float baseRight = Mathf.Max(pane.BaseWidth * 0.5f, baseAcrossOther + follower.BaseWidth * 0.5f);
                Vector3 measuredMiddle = pane.BaseCenter + pane.Right * ((baseLeft + baseRight) * 0.5f);
                pane.ViewDelta = RoomPointBehindPane(s_OutsideInApertureOpenings[a], viewsShellName, a) - measuredMiddle;

                follower.ViewFollows = a;
                s_OutsideInApertures[a] = pane;
                s_OutsideInApertures[partner] = follower;
                doubles += (doubles.Length > 0 ? ", " : "") + a + "+" + partner;
            }

            // The fit of each view to its room window. The room window's size is its GLASS as measured,
            // not a view widened to meet a neighbor at a bend.
            string fits = "";
            for (int a = 0; a < count; a++)
            {
                var pane = s_OutsideInApertures[a];
                pane.FitAcross = 1f; pane.FitUp = 1f;
                if (pane.Paired && !pane.Pretend && pane.ViewFollows < 0 && a < s_OutsideInApertureOpenings.Count)
                {
                    var opening = s_OutsideInApertureOpenings[a];
                    float roomWidth = opening.GlassWidth > 0.01f ? opening.GlassWidth : opening.Width;
                    float roomHeight = opening.GlassHeight > 0.01f ? opening.GlassHeight : opening.Height;
                    if (roomWidth > 0.01f && roomHeight > 0.01f && pane.ViewWidth > 0.01f && pane.ViewHeight > 0.01f)
                    {
                        pane.FitAcross = Mathf.Clamp(roomWidth / pane.ViewWidth, 0.5f, 2f);
                        pane.FitUp = Mathf.Clamp(roomHeight / pane.ViewHeight, 0.5f, 2f);
                        float startsAt = -Vector3.Dot(RoomPointBehindPane(opening, viewsShellName, a) - opening.GlassCenter, opening.Outward);
                        fits += $"{a}: {pane.FitAcross * 100f:F0}x{pane.FitUp * 100f:F0} @{startsAt * 100f:F1}  ";
                    }
                }
                // THE ROOM A LITTLE LARGER THAN THE PLAIN FIT, AND THE PICTURE LARGER THAN THE HOLE. Both are
                // the owner's, from sliders at the Pleasant Valley farmhouse on 2026-09-19, set on its bay
                // and on its 24 other windows separately and arriving at the same numbers both times.
                //
                // The picture past the hole is what two hours of depth sliders could not do. A
                // building's frame is only a few centimeters deep (the farmhouse's inner walls run 4.8 cm)
                // with nothing behind it, so from any slanting angle one looks PAST the hole's edge,
                // behind the back of the exterior frame, and the room's own frame belongs exactly
                // there. A picture that stops at the hole's edge leaves that place to the dark backing
                // and, further in, to the room seen through the back of its own wall ("the gap further
                // in from that where you can see the floor"). No depth, slide or cut puts a picture
                // where there is no picture. Seen square on, the extra is behind the building's frame.
                // The room's size is the farmhouse's own tune, not every building's (see ViewRoomSizePercent).
                float roomSize = OutsideInViewZoom;
                BuildingWindowOverride sizeRow;
                if (TryGetBuildingWindowOverride(viewsShellName, out sizeRow) && !float.IsNaN(sizeRow.ViewRoomSizePercent))
                    roomSize *= sizeRow.ViewRoomSizePercent / 100f;
                pane.FitAcross *= roomSize; pane.FitUp *= roomSize;
                // The room stretched inside the hole by hand, where its window is a different shape
                // from the building's. See WindowFit.RoomTallerPercent.
                WindowFit stretch;
                if (TryGetWindowFit(viewsShellName, a, out stretch))
                {
                    if (stretch.RoomWiderPercent > -90f) pane.FitAcross /= 1f + stretch.RoomWiderPercent / 100f;
                    if (stretch.RoomTallerPercent > -90f) pane.FitUp /= 1f + stretch.RoomTallerPercent / 100f;
                }
                if (pane.Paired && pane.ViewFollows < 0)
                {
                    pane.ViewWidth += 2f * OutsideInViewPastHole;
                    pane.ViewHeight += 2f * OutsideInViewPastHole;
                }
                s_OutsideInApertures[a] = pane;
            }
            if (fits.Length > 0 && s_DebugBounds)
                MelonLogger.Msg($"[DIS-ICE] {id}: her goruntunun odadaki pencereye oturmasi icin olcegi, yuzde, en x boy (su an yuzde {OutsideInViewFit * 100f:F0} uygulaniyor): {fits}",
                                $"[OUTSIDE-IN] {id}: what each view would be scaled by for the room's window to fill the building's, percent across x up (the lever stands at {OutsideInViewFit * 100f:F0}% of that), and @ how many cm INSIDE the room's glass the view starts (negative = outside it; cut mode {OutsideInCutMode}: 0 as found, 1 at the room frame's outer face, 2 behind the frame): {fits}");

            if (doubles.Length > 0 && s_DebugBounds)
                MelonLogger.Msg($"[DIS-ICE] {id}: cift pencereler tek bir goruntuyu paylasiyor (pencere+komsusu): {doubles}. Odada tek genis pencere, disarida iki cam ve aralarinda bir direk.",
                                $"[OUTSIDE-IN] {id}: double windows share one view (window+its neighbor): {doubles}. One wide window in the room, two panes and a post between them outside.");
        }

        // ─── THE FROST OVERLAYS ───

        private static void BuildOutsideInFrostOverlays(SeamlessInteriorInstance instance)
        {
            DestroyOutsideInFrostOverlays();
            s_OutsideInGlassRects.Clear();
            s_OI.FrostRects.Clear();
            for (int i = 0; i < s_OutsideInApertures.Count; i++)
            {
                s_OI.FrostRects.Add(Vector4.zero);
                s_OutsideInFrostOverlays.Add(null);
                // Until measured, the author's table is the best available answer.
                var tablePane = s_OutsideInApertures[i];
                s_OutsideInGlassRects.Add(new Vector4(-tablePane.Width * 0.5f, tablePane.Width * 0.5f, -tablePane.Height * 0.5f, tablePane.Height * 0.5f));
            }

            if (instance == null || instance.ExteriorShell == null || instance.MasterInterior == null) return;

            // THE FROST OUTSIDE IS PAINTED FROM THE BUILDING'S OWN SHEET (the see-through exterior one, found
            // below), in every building. It used to be the room's sheet when the room had frosted glass,
            // and the building's only as a fallback, and which one a building got depended on whether
            // the player had been inside it: a room's glass wears the mod's own material from then on
            // and is no longer found. The room's sheet is also thinner. Owner, 2026-10-10, with the
            // building's sheet put on four buildings live: "Opacity looks much more realistic", "frost
            // still lines up"; at Thompson's Store, whose room sheet does not agree with the building's
            // layout at all: "That fixed it!" Off, the room's sheet is looked for first, as before.
            Texture frostTexture = null;
            Vector2 frostScale = Vector2.one, frostOffset = Vector2.zero;
            bool fromBuildingSheet = OutsideInFrostFromBuildingSheet;
            var interiorRenderers = instance.MasterInterior.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < interiorRenderers.Length && frostTexture == null && !fromBuildingSheet; i++)
            {
                var material = interiorRenderers[i] == null ? null : interiorRenderers[i].sharedMaterial;
                if (material == null || material.mainTexture == null) continue;
                if (material.name.IndexOf("WindowGlassMix", System.StringComparison.Ordinal) < 0) continue;
                if (material.renderQueue < 2500) continue;      // the opaque variant carries no alpha
                frostTexture = material.mainTexture;
                frostScale = material.mainTextureScale;
                frostOffset = material.mainTextureOffset;
            }

            // A ROOM WITH NO FROSTED GLASS OF ITS OWN. The gas station's room has a plain pane
            // (GLB_GlassWindow) where every other room has a WindowGlassMix sheet, and its windows
            // came out clear while the building's own painted panes are frosted. Owner, 2026-09-20:
            // "We should probably borrow if it's easy. They have frost on the exterior shell
            // windows." The game keeps a see-through variant of the very sheet the building's panes
            // are painted from (STR_WindowGlassMix_A02_ExtAlpha, texture STR_WindowGlassMixExtApha_A),
            // and the overlays below are laid out by the building's own UVs, so it fits as it is.
            // Looked for among the loaded materials, once per set-up and only for such a room.
            // Found once and kept: the search reads the name of every loaded material (about 7,000), and
            // run for every building it took the frost step from 20 ms to 80 to 110 (2026-10-10).
            if (frostTexture == null && fromBuildingSheet && s_OutsideInBuildingFrostSheet != null)
            {
                frostTexture = s_OutsideInBuildingFrostSheet;
                frostScale = s_OutsideInBuildingFrostScale;
                frostOffset = s_OutsideInBuildingFrostOffset;
            }
            if (frostTexture == null)
            {
                Material best = null;
                var loaded = Resources.FindObjectsOfTypeAll<Material>();
                for (int i = 0; i < loaded.Length; i++)
                {
                    var material = loaded[i];
                    if (material == null || material.mainTexture == null || material.renderQueue < 2500) continue;
                    string name = material.name;
                    if (name.IndexOf("WindowGlassMix", System.StringComparison.Ordinal) < 0) continue;
                    if (name.StartsWith("SeamlessInteriors_", System.StringComparison.Ordinal)) continue;
                    bool exterior = name.IndexOf("Ext", System.StringComparison.Ordinal) >= 0;
                    if (best == null || (exterior && best.name.IndexOf("Ext", System.StringComparison.Ordinal) < 0)) best = material;
                }
                if (best != null)
                {
                    frostTexture = best.mainTexture;
                    frostScale = best.mainTextureScale;
                    frostOffset = best.mainTextureOffset;
                    if (best.name.IndexOf("Ext", System.StringComparison.Ordinal) >= 0)
                    {
                        s_OutsideInBuildingFrostSheet = frostTexture;
                        s_OutsideInBuildingFrostScale = frostScale;
                        s_OutsideInBuildingFrostOffset = frostOffset;
                    }
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[DIS-ICE] {instance.Config.ResolvedInstanceId}: odanin buzlu cami yok; buz, binanin kendi cam dokusunun saydam halinden alindi ('{best.name}', doku '{frostTexture.name}').",
                                        $"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: the room has no frosted glass of its own; the frost is taken from the see-through variant of the building's own pane sheet ('{best.name}', texture '{frostTexture.name}').");
                }
            }

            Shader frostShader = OutsideInFrostTakesShadows ? FindOutsideInShadowedFrostShader() : null;
            bool shadowed = frostShader != null;
            if (!shadowed) frostShader = Shader.Find(OUTSIDE_IN_FROST_SHADER);
            if (frostTexture == null || frostShader == null) return;
            s_OI.FrostShadowed = shadowed;

            // The shell's most detailed mesh that carries a glass submesh. The overlays are their
            // own objects, so they do not care which LOD the shell happens to be showing.
            Mesh shellMesh = null; Transform shellTransform = null; int glassSubmesh = -1;
            var shellRenderers = instance.ExteriorShell.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < shellRenderers.Length; i++)
            {
                var renderer = shellRenderers[i];
                if (renderer == null) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;

                Material[] materials = renderer.sharedMaterials;
                for (int k = 0; k < materials.Length && k < filter.sharedMesh.subMeshCount; k++)
                {
                    if (materials[k] == null) continue;
                    if (!IsShellGlassMaterial(materials[k])) continue;
                    if (shellMesh != null && filter.sharedMesh.vertexCount <= shellMesh.vertexCount) continue;
                    shellMesh = filter.sharedMesh; shellTransform = renderer.transform; glassSubmesh = k;
                }
            }
            if (shellMesh == null) return;

            Vector3[] vertices = shellMesh.vertices;
            Vector2[] uvs = shellMesh.uv;
            int[] triangles = shellMesh.GetTriangles(glassSubmesh);
            if (vertices == null || uvs == null || triangles == null || uvs.Length != vertices.Length) return;

            // To world space once, not once per window.
            var world = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) world[i] = shellTransform.TransformPoint(vertices[i]);

            s_OutsideInFrostMaterial = new Material(frostShader);
            s_OutsideInFrostMaterial.name = "SeamlessInteriors_FrostedPane";
            s_OI.FrostSheetSource = frostTexture;
            bool sheetReady;
            s_OutsideInFrostMaterial.mainTexture = OutsideInFrostSheetWithContrast(frostTexture, out sheetReady);
            // Not ready: the tick asks again until the full sheet has come.
            s_OI.FrostContrastApplied = sheetReady ? OutsideInFrostContrast : -1f;
            s_OutsideInFrostMaterial.mainTextureScale = frostScale;
            s_OutsideInFrostMaterial.mainTextureOffset = frostOffset;
            // After the portal quad at 2600 AND after a view part way through a fade, which is drawn
            // later still (ViewFade.cs). At 3000 the fading view covered the frost for the whole fade
            // and the frost came back the frame the fade ended. The farmhouse has more windows than
            // cameras, so its windows trade cameras all the way round. Owner, 2026-10-10, orbiting it:
            // "it was just the frost popping in/out"; with the frost moved after the fade, live:
            // "No more popping. Just the normal portals fading in to view".
            //
            // The cut-out frost keeps its shader's own place among the solid things, before the view:
            // it writes depth, so a view behind it can never cover it, fading or not.
            if (shadowed)
            {
                float bright = Mathf.Clamp01(OutsideInFrostBrightness);
                s_OutsideInFrostMaterial.color = new Color(bright, bright, bright, 1f);
                s_OutsideInFrostMaterial.SetFloat("_Cutoff", Mathf.Clamp01(OutsideInFrostCutoff));
                if (s_OutsideInGlassTintMaterial == null)
                {
                    var tintShader = Shader.Find("Sprites/Default");
                    if (tintShader != null)
                    {
                        s_OutsideInGlassTintMaterial = new Material(tintShader);
                        s_OutsideInGlassTintMaterial.name = "SeamlessInteriors_GlassTint";
                        // Over a view part way through a fade, which is drawn late (ViewFade.cs).
                        s_OutsideInGlassTintMaterial.renderQueue = WINDOW_VIEW_FADE_RENDER_QUEUE + 7;
                        s_OutsideInGlassTintMaterial.color = new Color(OutsideInGlassTintColor.r, OutsideInGlassTintColor.g, OutsideInGlassTintColor.b, Mathf.Clamp01(OutsideInGlassTint));
                    }
                }
            }
            else
            {
                float bright = Mathf.Clamp01(OutsideInFrostBrightness);
                s_OutsideInFrostMaterial.renderQueue = WINDOW_VIEW_FADE_RENDER_QUEUE + 10;
                s_OutsideInFrostMaterial.color = new Color(bright, bright, bright, Mathf.Clamp01(OutsideInFrostStrength));
            }

            var paneVertices = new List<Vector3>(64);
            var paneUvs = new List<Vector2>(64);
            int built = 0;

            // Collected for every window first and built afterwards, because a window may end up
            // wearing another window's layout. See BorrowOutsideInFrostLayouts.
            var layoutVertices = new Vector3[s_OutsideInApertures.Count][];
            var layoutUvs = new Vector2[s_OutsideInApertures.Count][];

            for (int a = 0; a < s_OutsideInApertures.Count; a++)
            {
                var pane = s_OutsideInApertures[a];
                // A window left alone gets nothing of the mod's: its glass is never hidden in the first
                // place. See SplitLeftAloneGlass.
                if (!pane.Paired || IsLeftAloneAt(instance.Config.ExteriorShellPrefabName, a)) continue;

                paneVertices.Clear();
                paneUvs.Clear();
                Vector4 glassRect = Vector4.zero;
                bool glassMeasured = false;

                float halfWidth = pane.Width * 0.5f + OUTSIDE_IN_FROST_PANE_MARGIN;
                float halfHeight = pane.Height * 0.5f + OUTSIDE_IN_FROST_PANE_MARGIN;
                Vector3 lift = Vector3.zero;   // on the glass plane; ApplyOutsideInRecess moves the object

                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    Vector3 v0 = world[triangles[t]], v1 = world[triangles[t + 1]], v2 = world[triangles[t + 2]];

                    // This window's, and facing out. The glass has an inner face too on some
                    // shells, and an overlay built from that would be drawn back to front.
                    Vector3 middle = (v0 + v1 + v2) / 3f - pane.Center;
                    if (Mathf.Abs(Vector3.Dot(middle, pane.Right)) > halfWidth) continue;
                    if (Mathf.Abs(Vector3.Dot(middle, pane.Up)) > halfHeight) continue;
                    if (Mathf.Abs(Vector3.Dot(middle, pane.Outward)) > OUTSIDE_IN_FROST_PANE_MARGIN) continue;
                    if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0).normalized, pane.Outward) < 0.5f) continue;

                    // FLAT ON THE PANE'S PLANE, NOT ON THE GLASS'S OWN. The building's glass is not always
                    // true to its wall: Safe House A's leans 0.6 to 1.2 degrees top to bottom, which
                    // across a 1.24 m pane is up to 1.3 cm either side of the plane. The overlay rides
                    // only 5 mm in front of the room's picture, which is flat, opaque and drawn first,
                    // so wherever a copied triangle dipped behind that plane the picture covered it:
                    // a straight-edged strip with no frost, along the top of a pane leaning one way and
                    // the bottom of one leaning the other. The owner saw it as "black up top on this
                    // window, and bottom on others. Makes me think there's something else there", and
                    // proved it by having one pane moved out 1 cm (2026-09-18). All seven of that
                    // building's panes had a part behind the plane. Flattened live first; every vertex
                    // is carried along the window's outward onto the plane, so the frost keeps its
                    // outline and its layout and cannot dip behind anything.
                    v0 -= pane.Outward * Vector3.Dot(v0 - pane.Center, pane.Outward);
                    v1 -= pane.Outward * Vector3.Dot(v1 - pane.Center, pane.Outward);
                    v2 -= pane.Outward * Vector3.Dot(v2 - pane.Center, pane.Outward);
                    paneVertices.Add(v0 + lift); paneVertices.Add(v1 + lift); paneVertices.Add(v2 + lift);

                    for (int corner = 0; corner < 3; corner++)
                    {
                        Vector3 fromCenter = (corner == 0 ? v0 : corner == 1 ? v1 : v2) - pane.Center;
                        float across = Vector3.Dot(fromCenter, pane.Right), upward = Vector3.Dot(fromCenter, pane.Up);
                        if (!glassMeasured) { glassRect = new Vector4(across, across, upward, upward); glassMeasured = true; }
                        glassRect.x = Mathf.Min(glassRect.x, across); glassRect.y = Mathf.Max(glassRect.y, across);
                        glassRect.z = Mathf.Min(glassRect.z, upward); glassRect.w = Mathf.Max(glassRect.w, upward);
                    }
                    paneUvs.Add(uvs[triangles[t]]); paneUvs.Add(uvs[triangles[t + 1]]); paneUvs.Add(uvs[triangles[t + 2]]);
                }

                if (paneVertices.Count == 0) continue;
                if (glassMeasured) s_OutsideInGlassRects[a] = glassRect;
                layoutVertices[a] = paneVertices.ToArray();
                layoutUvs[a] = paneUvs.ToArray();
            }

            BorrowOutsideInFrostLayouts(instance, layoutVertices, layoutUvs);

            for (int a = 0; a < s_OutsideInApertures.Count; a++)
            {
                if (layoutVertices[a] == null) continue;
                var pane = s_OutsideInApertures[a];
                s_OI.FrostRects[a] = FitOutsideInFrostLayout(instance.Config.ExteriorShellPrefabName, a, layoutVertices[a]);
                paneVertices.Clear(); paneVertices.AddRange(layoutVertices[a]);
                paneUvs.Clear(); paneUvs.AddRange(layoutUvs[a]);

                var overlayNormals = new Vector3[paneVertices.Count];
                var overlayTriangles = new int[paneVertices.Count];
                for (int i = 0; i < overlayTriangles.Length; i++) { overlayTriangles[i] = i; overlayNormals[i] = pane.Outward; }

                var mesh = new Mesh();
                mesh.name = "SeamlessInteriors_FrostOverlayMesh";
                mesh.vertices = paneVertices.ToArray();
                mesh.uv = paneUvs.ToArray();
                mesh.normals = overlayNormals;
                mesh.triangles = overlayTriangles;
                mesh.RecalculateBounds();

                // World-space vertices on an identity transform, the same way the portal quads are
                // built, so nothing here depends on the shell's transform after setup.
                var overlay = new GameObject("SeamlessInteriors_FrostOverlay_" + a);
                overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
                var overlayRenderer = overlay.AddComponent<MeshRenderer>();
                overlayRenderer.sharedMaterial = s_OutsideInFrostMaterial;
                overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                overlayRenderer.receiveShadows = shadowed;
                overlayRenderer.enabled = false;

                if (shadowed && s_OutsideInGlassTintMaterial != null)
                {
                    // A child, so it goes where the frost goes and is destroyed with it.
                    var tintObject = new GameObject("SeamlessInteriors_GlassTint_" + a);
                    tintObject.transform.SetParent(overlay.transform, false);
                    tintObject.transform.localPosition = -s_OutsideInApertures[a].Outward * OUTSIDE_IN_GLASS_TINT_BEHIND_FROST;
                    tintObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var tintRenderer = tintObject.AddComponent<MeshRenderer>();
                    tintRenderer.sharedMaterial = s_OutsideInGlassTintMaterial;
                    tintRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    tintRenderer.receiveShadows = false;
                    tintRenderer.enabled = false;
                }

                s_OutsideInFrostOverlays[a] = overlay;
                built++;
            }

            if (s_PrefVerboseLogging != null && s_PrefVerboseLogging.Value)
                MelonLogger.Msg($"[DIS-ICE] {built} pencereye buz kaplamasi kuruldu ({triangles.Length / 3} cam ucgeninden).",
                                $"[OUTSIDE-IN] Frost overlay built on {built} windows (from {triangles.Length / 3} glass triangles).");
        }

        // THE OUTSIDE OF A WINDOW WEARS THE FROST ITS OWN ROOM WINDOW WEARS INSIDE, where the building
        // has a pane that carries that picture. The owner's rule, 2026-09-18, at Safe House A: "We
        // need to use the frost from window 3 on window 0 because that's the same frost that's on
        // the inside of window 0", then "Use panel 3's frost on all the windows except the small
        // one." The building's glass reads a different part of the frost sheet on every pane, the
        // room's large windows all read ONE part (one mesh, one material), and exactly one pane of
        // the building reads that same part. So nobody has to choose the source: it is the pane of
        // like size whose UVs cover what the room window's glass covers. A window with no such pane
        // keeps its own layout, which is what Safe House A's two small panes do.
        //
        // It also mends a window whose own glass is odd. Safe House A's window 0 has TWO layers of
        // glass 7 cm apart sideways and copied as it stands reads as doubled frost.
        //
        // The layout is carried from the source's measured glass onto this window's, across for
        // across and up for up as each is seen from OUTSIDE, so the glazing bar stays where the
        // building's bar is and nothing is mirrored.
        internal static bool OutsideInFrostFromBuildingSheet = true;
        // Unity's own null test tells when a region change has unloaded it; it is then looked for again.
        private static Texture s_OutsideInBuildingFrostSheet;
        private static Vector2 s_OutsideInBuildingFrostScale = Vector2.one, s_OutsideInBuildingFrostOffset = Vector2.zero;

        private const float OUTSIDE_IN_FROST_BORROW_UV_TOLERANCE = 0.04f;
        private const float OUTSIDE_IN_FROST_BORROW_SIZE_TOLERANCE = 0.10f;

        private static void BorrowOutsideInFrostLayouts(SeamlessInteriorInstance instance, Vector3[][] layoutVertices, Vector2[][] layoutUvs)
        {
            int count = s_OutsideInApertures.Count;
            var layoutUvRects = new Vector4[count];
            for (int a = 0; a < count; a++)
                if (layoutUvs[a] != null) layoutUvRects[a] = UvRectOf(layoutUvs[a], null);

            var sources = new int[count];
            for (int a = 0; a < count; a++) sources[a] = -1;

            for (int a = 0; a < count; a++)
            {
                var pane = s_OutsideInApertures[a];
                if (!pane.Paired || pane.Pretend || a >= s_OutsideInApertureOpenings.Count) continue;

                // A source named by hand wins, and -2 by hand means this window keeps its own.
                //
                // A WINDOW WITH NO FROST OF ITS OWN CAN STILL WEAR ANOTHER'S, by hand only. Glass the
                // row adds from its own object (ExtraShellGlassObjects: the Community Hall's door) is
                // not on the building's frosted sheet, so nothing was laid out for it and the door
                // stood clear among seven frosted windows. Owner, 2026-10-09: "We also need to add
                // frost to it ... borrow one of the long window frost textures." Its glass rectangle
                // is then the window's own measured size, which is what the layout is carried onto.
                WindowFit fit;
                if (TryGetWindowFit(instance.Config.ExteriorShellPrefabName, a, out fit) && fit.FrostSource != -1)
                {
                    if (layoutVertices[a] == null && IsLeftAloneAt(instance.Config.ExteriorShellPrefabName, a)) continue;
                    if (fit.FrostSource >= 0 && fit.FrostSource < count && fit.FrostSource != a && layoutVertices[fit.FrostSource] != null) sources[a] = fit.FrostSource;
                    continue;
                }

                if (layoutVertices[a] == null) continue;

                Vector4 roomUvRect;
                if (!TryGetRoomGlassUvRect(s_OutsideInApertureOpenings[a], out roomUvRect)) continue;
                if (UvRectDistance(layoutUvRects[a], roomUvRect) <= OUTSIDE_IN_FROST_BORROW_UV_TOLERANCE) continue;   // wears it already

                float best = OUTSIDE_IN_FROST_BORROW_UV_TOLERANCE;
                for (int s = 0; s < count; s++)
                {
                    if (s == a || layoutVertices[s] == null) continue;
                    var candidate = s_OutsideInApertures[s];
                    // Like size AS MEASURED. A pane set by hand grows past its neighbors (Safe House A's
                    // window 0 went to 138 cm tall against 124) and is still the same kind of window.
                    if (Mathf.Abs(candidate.BaseWidth - pane.BaseWidth) > OUTSIDE_IN_FROST_BORROW_SIZE_TOLERANCE) continue;
                    if (Mathf.Abs(candidate.BaseHeight - pane.BaseHeight) > OUTSIDE_IN_FROST_BORROW_SIZE_TOLERANCE) continue;
                    float distance = UvRectDistance(layoutUvRects[s], roomUvRect);
                    if (distance > best) continue;
                    best = distance; sources[a] = s;
                }
            }

            // Carried only once every source is chosen, so a source is always its own glass.
            var carriedVertices = new Vector3[count][];
            var carriedUvs = new Vector2[count][];
            for (int a = 0; a < count; a++)
            {
                int s = sources[a];
                if (s < 0) continue;
                var from = s_OutsideInApertures[s]; var to = s_OutsideInApertures[a];
                Vector4 fromRect = s_OutsideInGlassRects[s], toRect = s_OutsideInGlassRects[a];
                float fromWidth = fromRect.y - fromRect.x, fromHeight = fromRect.w - fromRect.z;
                if (fromWidth < 0.01f || fromHeight < 0.01f) { sources[a] = -1; continue; }

                // GLASS IN SEVERAL PANES WEARS THE FROST ONCE PER PANE. The Community Hall's door is four tall
                // panes in one glass object, and one window's frost carried over the whole door was cut up by
                // the door's bars: the source's own glazing bar ran as a clear band through all four panes at
                // one height and no pane had frost along its own edges (owner, 2026-10-09: "Frost is wrong").
                //
                // Each pane is one flat piece showing a pane-shaped PART of a source window's largest piece of
                // frost, at the frost's own proportions. The first try fitted the whole piece to each pane, and
                // a piece 76 cm wide squeezed into a pane a third of that stood every shape in it on end; with
                // the same piece in all four it also repeated (owner: "the angle's off, and it reads a little
                // repetitive"). So the panes take turns among the named source and the other windows of its
                // size, and among the left and right edges of the piece, where the frost gathers.
                List<FrostPane> toPanes = ExtraGlassPanes(instance, a, to);
                if (toPanes != null)
                {
                    var paneSources = new List<int> { s };
                    for (int o = 0; o < count; o++)
                    {
                        if (o == s || o == a || layoutVertices[o] == null || layoutUvs[o] == null) continue;
                        var other = s_OutsideInApertures[o];
                        if (Mathf.Abs(other.BaseWidth - from.BaseWidth) > OUTSIDE_IN_FROST_BORROW_SIZE_TOLERANCE) continue;
                        if (Mathf.Abs(other.BaseHeight - from.BaseHeight) > OUTSIDE_IN_FROST_BORROW_SIZE_TOLERANCE) continue;
                        paneSources.Add(o);
                    }

                    var paneVerts = new List<Vector3>(6 * toPanes.Count);
                    var paneUvList = new List<Vector2>(6 * toPanes.Count);
                    for (int tp = 0; tp < toPanes.Count; tp++)
                    {
                        int src = paneSources[tp % paneSources.Count];
                        var fromPane = s_OutsideInApertures[src];
                        List<FrostPane> pieces = SplitFrostIntoPanes(layoutVertices[src], fromPane.Center, fromPane.Right, fromPane.Up);
                        FrostPane piece = null;
                        for (int k = 0; k < pieces.Count; k++) if (piece == null || pieces[k].Area > piece.Area) piece = pieces[k];
                        if (piece == null || piece.Triangles.Count == 0) continue;
                        float pieceWidth = piece.Rect.y - piece.Rect.x, pieceHeight = piece.Rect.w - piece.Rect.z;
                        Vector4 rect = toPanes[tp].Rect;
                        float paneWidth = rect.y - rect.x, paneHeight = rect.w - rect.z;
                        if (pieceWidth < 0.01f || pieceHeight < 0.01f || paneWidth < 0.01f || paneHeight < 0.01f) continue;

                        // The sheet's place for any point of the piece, from one of its triangles: the frost is
                        // laid flat on the glass, so one triangle gives the whole piece.
                        int t0 = piece.Triangles[0] * 3;
                        Vector2 p0 = FrostPlace(layoutVertices[src][t0], fromPane), p1 = FrostPlace(layoutVertices[src][t0 + 1], fromPane), p2 = FrostPlace(layoutVertices[src][t0 + 2], fromPane);
                        Vector2 uv0 = layoutUvs[src][t0], uv1 = layoutUvs[src][t0 + 1], uv2 = layoutUvs[src][t0 + 2];
                        Vector2 e1 = p1 - p0, e2 = p2 - p0;
                        float det = e1.x * e2.y - e2.x * e1.y;
                        if (Mathf.Abs(det) < 1e-6f) continue;
                        Vector2 uvAcross = ((uv1 - uv0) * e2.y - (uv2 - uv0) * e1.y) / det;    // per meter across
                        Vector2 uvUp = ((uv2 - uv0) * e1.x - (uv1 - uv0) * e2.x) / det;          // per meter up

                        // The largest part of the piece that has the pane's shape, at one scale both ways.
                        float scale = Mathf.Max(paneWidth / pieceWidth, paneHeight / pieceHeight);
                        float partWidth = paneWidth / scale, partHeight = paneHeight / scale;
                        float partLeft = piece.Rect.x + (pieceWidth - partWidth) * ((tp / Mathf.Max(1, paneSources.Count)) % 2 == 0 ? (tp % 2 == 0 ? 0f : 1f) : (tp % 2 == 0 ? 1f : 0f));
                        float partBottom = piece.Rect.z + (pieceHeight - partHeight) * 0.5f;

                        var corner = new Vector3[4]; var cornerUv = new Vector2[4];
                        for (int c = 0; c < 4; c++)
                        {
                            float u = c == 1 || c == 2 ? 1f : 0f, v = c >= 2 ? 1f : 0f;
                            corner[c] = to.Center + to.Right * Mathf.Lerp(rect.x, rect.y, u) + to.Up * Mathf.Lerp(rect.z, rect.w, v);
                            Vector2 place = new Vector2(partLeft + partWidth * u, partBottom + partHeight * v);
                            cornerUv[c] = uv0 + uvAcross * (place.x - p0.x) + uvUp * (place.y - p0.y);
                        }
                        // Wound to face out of the building, as the glass's own triangles are.
                        bool facesOut = Vector3.Dot(Vector3.Cross(corner[1] - corner[0], corner[2] - corner[0]), to.Outward) > 0f;
                        int[] order = facesOut ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
                        for (int k = 0; k < order.Length; k++) { paneVerts.Add(corner[order[k]]); paneUvList.Add(cornerUv[order[k]]); }
                    }
                    if (paneVerts.Count > 0)
                    {
                        carriedVertices[a] = paneVerts.ToArray();
                        carriedUvs[a] = paneUvList.ToArray();
                        continue;
                    }
                }

                var carried = new Vector3[layoutVertices[s].Length];
                for (int i = 0; i < carried.Length; i++)
                {
                    Vector3 fromCenter = layoutVertices[s][i] - from.Center;
                    float across = (Vector3.Dot(fromCenter, from.Right) - fromRect.x) / fromWidth;
                    float upward = (Vector3.Dot(fromCenter, from.Up) - fromRect.z) / fromHeight;
                    carried[i] = to.Center + to.Right * Mathf.Lerp(toRect.x, toRect.y, across) + to.Up * Mathf.Lerp(toRect.z, toRect.w, upward);
                }
                carriedVertices[a] = carried;
            }

            string borrowed = "";
            for (int a = 0; a < count; a++)
            {
                if (sources[a] < 0 || carriedVertices[a] == null) continue;
                layoutVertices[a] = carriedVertices[a];
                layoutUvs[a] = carriedUvs[a] ?? layoutUvs[sources[a]];
                borrowed += (borrowed.Length > 0 ? ", " : "") + a + "<-" + sources[a];
            }

            if (borrowed.Length > 0 && s_PrefVerboseLogging != null && s_PrefVerboseLogging.Value)
            {
                string id = instance.Config.ResolvedInstanceId;
                MelonLogger.Msg($"[DIS-ICE] {id}: su pencereler baska bir pencerenin buz duzenini giyiyor (pencere<-kaynak): {borrowed}. Kaynak, odadaki pencerenin buz resmini tasiyan ayni boydaki camdir.",
                                $"[OUTSIDE-IN] {id}: these windows wear another window's frost layout (window<-source): {borrowed}. The source is the pane of like size that carries the same frost picture as the room's window does inside.");
            }
        }

        // Where a point of a window's frost stands in that window's own frame: x across, y up, in meters.
        private static Vector2 FrostPlace(Vector3 world, ShellAperture window)
        {
            Vector3 d = world - window.Center;
            return new Vector2(Vector3.Dot(d, window.Right), Vector3.Dot(d, window.Up));
        }

        // One pane's worth of glass or frost, in a window's own frame: x, y = lowest and highest across, z, w =
        // lowest and highest up, and the triangles (by number, three corners each) that make it.
        private sealed class FrostPane
        {
            public Vector4 Rect;
            public readonly List<int> Triangles = new List<int>();
            public float Area { get { return (Rect.y - Rect.x) * (Rect.w - Rect.z); } }
        }

        // A list of triangles (three corners each, world space) gathered into the panes they form: triangles
        // whose boxes touch or overlap in the window's frame belong to one pane.
        private static List<FrostPane> SplitFrostIntoPanes(Vector3[] corners, Vector3 center, Vector3 right, Vector3 up)
        {
            var panes = new List<FrostPane>();
            if (corners == null) return panes;
            for (int t = 0; t + 2 < corners.Length; t += 3)
            {
                var pane = new FrostPane();
                for (int c = 0; c < 3; c++)
                {
                    Vector3 d = corners[t + c] - center;
                    float across = Vector3.Dot(d, right), upward = Vector3.Dot(d, up);
                    if (c == 0) pane.Rect = new Vector4(across, across, upward, upward);
                    pane.Rect.x = Mathf.Min(pane.Rect.x, across); pane.Rect.y = Mathf.Max(pane.Rect.y, across);
                    pane.Rect.z = Mathf.Min(pane.Rect.z, upward); pane.Rect.w = Mathf.Max(pane.Rect.w, upward);
                }
                pane.Triangles.Add(t / 3);
                panes.Add(pane);
            }
            const float TOUCHING = 0.015f;
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < panes.Count && !merged; i++)
                    for (int j = i + 1; j < panes.Count && !merged; j++)
                    {
                        Vector4 a = panes[i].Rect, b = panes[j].Rect;
                        if (a.x > b.y + TOUCHING || b.x > a.y + TOUCHING || a.z > b.w + TOUCHING || b.z > a.w + TOUCHING) continue;
                        panes[i].Rect = new Vector4(Mathf.Min(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Min(a.z, b.z), Mathf.Max(a.w, b.w));
                        panes[i].Triangles.AddRange(panes[j].Triangles);
                        panes.RemoveAt(j);
                        merged = true;
                    }
            }
            return panes;
        }

        // The separate panes of a window the row added from its own glass object (ExtraShellGlassObjects), read
        // from that object's mesh; null for any other window and for glass that is one pane.
        private static List<FrostPane> ExtraGlassPanes(SeamlessInteriorInstance instance, int window, ShellAperture pane)
        {
            BuildingWindowOverride row;
            if (instance == null || instance.Config == null || instance.ExteriorShell == null) return null;
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out row) || row.ExtraShellGlassObjects == null) return null;
            int first = s_OutsideInApertures.Count - row.ExtraShellGlassObjects.Length;
            if (window < first || window - first >= row.ExtraShellGlassObjects.Length) return null;
            string want = row.ExtraShellGlassObjects[window - first];
            if (string.IsNullOrEmpty(want)) return null;

            MeshFilter best = null;
            var filters = instance.ExteriorShell.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var f = filters[i];
                if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
                if (!f.gameObject.name.StartsWith(want, System.StringComparison.Ordinal)) continue;
                if (best == null || f.sharedMesh.vertexCount > best.sharedMesh.vertexCount) best = f;
            }
            if (best == null) return null;

            Vector3[] local = best.sharedMesh.vertices; int[] triangles = best.sharedMesh.triangles;
            Transform t = best.transform;
            float halfWidth = pane.Width * 0.5f + 0.05f, halfHeight = pane.Height * 0.5f + 0.05f;
            var facingOut = new List<Vector3>(); var either = new List<Vector3>();
            for (int k = 0; k + 2 < triangles.Length; k += 3)
            {
                Vector3 v0 = t.TransformPoint(local[triangles[k]]), v1 = t.TransformPoint(local[triangles[k + 1]]), v2 = t.TransformPoint(local[triangles[k + 2]]);
                bool inside = true;
                for (int c = 0; c < 3 && inside; c++)
                {
                    Vector3 d = (c == 0 ? v0 : c == 1 ? v1 : v2) - pane.Center;
                    inside = Mathf.Abs(Vector3.Dot(d, pane.Right)) <= halfWidth && Mathf.Abs(Vector3.Dot(d, pane.Up)) <= halfHeight && Mathf.Abs(Vector3.Dot(d, pane.Outward)) <= 0.15f;
                }
                if (!inside) continue;
                either.Add(v0); either.Add(v1); either.Add(v2);
                if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0).normalized, pane.Outward) > 0.5f) { facingOut.Add(v0); facingOut.Add(v1); facingOut.Add(v2); }
            }
            var glass = facingOut.Count > 0 ? facingOut : either;
            if (glass.Count == 0) return null;
            List<FrostPane> panes = SplitFrostIntoPanes(glass.ToArray(), pane.Center, pane.Right, pane.Up);
            return panes.Count > 1 ? panes : null;
        }

        // x, y = lowest and highest across the sheet; z, w = lowest and highest up it.
        private static Vector4 UvRectOf(Vector2[] uvs, int[] indices)
        {
            int length = indices != null ? indices.Length : uvs.Length;
            if (length == 0) return Vector4.zero;
            Vector2 first = uvs[indices != null ? indices[0] : 0];
            var rect = new Vector4(first.x, first.x, first.y, first.y);
            for (int i = 1; i < length; i++)
            {
                Vector2 uv = uvs[indices != null ? indices[i] : i];
                rect.x = Mathf.Min(rect.x, uv.x); rect.y = Mathf.Max(rect.y, uv.x);
                rect.z = Mathf.Min(rect.z, uv.y); rect.w = Mathf.Max(rect.w, uv.y);
            }
            return rect;
        }

        private static float UvRectDistance(Vector4 a, Vector4 b)
        {
            return Mathf.Max(Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)), Mathf.Max(Mathf.Abs(a.z - b.z), Mathf.Abs(a.w - b.w)));
        }

        private static bool TryGetRoomGlassUvRect(InteriorWindowOpening opening, out Vector4 rect)
        {
            rect = Vector4.zero;
            if (opening.Glass == null) return false;
            for (int g = 0; g < opening.Glass.Count; g++)
            {
                var renderer = opening.Glass[g];
                if (renderer == null) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;

                Vector2[] uvs = filter.sharedMesh.uv;
                if (uvs == null || uvs.Length == 0) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int k = 0; k < materials.Length && k < filter.sharedMesh.subMeshCount; k++)
                {
                    if (materials[k] == null || !IsShellGlassMaterial(materials[k])) continue;
                    int[] indices = filter.sharedMesh.GetTriangles(k);
                    if (indices == null || indices.Length == 0) continue;
                    rect = UvRectOf(uvs, indices);
                    return true;
                }
            }
            return false;
        }

        // ─── THE RECESSED WINDOW ───

        private static void BuildOutsideInRecessObjects(SeamlessInteriorInstance instance)
        {
            DestroyOutsideInRecessObjects();

            Shader backingShader = Shader.Find("Unlit/Texture");
            if (backingShader != null)
            {
                s_OutsideInBackingMaterial = new Material(backingShader);
                s_OutsideInBackingMaterial.name = "SeamlessInteriors_WindowBacking";
                s_OutsideInBackingMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;
                EnsureOutsideInBackingTexture();
            }

            Vector3[] shellWorld; int[] shellTriangles;
            TryGetShellWorldMesh(instance, out shellWorld, out shellTriangles);
            int liftedCount = 0, liftedBarCount = 0;

            for (int a = 0; a < s_OutsideInApertures.Count; a++)
            {
                var pane = s_OutsideInApertures[a];
                s_OutsideInBackings.Add(null);
                s_OutsideInLedgeDrifts.Add(null);
                s_OutsideInLedgeDriftBase.Add(null);

                // EVERY window gets a backing, paired or not: it is what stands in for the hidden
                // pane wherever no portal is drawing. Except a window left alone, which keeps the
                // building's own pane and must not have a dark sheet standing in front of it.
                if (s_OutsideInBackingMaterial != null && !IsLeftAloneAt(instance.Config.ExteriorShellPrefabName, a))
                {
                    Vector3 halfRight = pane.Right * (pane.Width * 0.5f + 0.01f);
                    Vector3 halfUp = pane.Up * (pane.Height * 0.5f + 0.01f);

                    var mesh = new Mesh();
                    mesh.name = "SeamlessInteriors_WindowBackingMesh";
                    if (pane.Outline != null)
                        // A window that is not a rectangle: the backing in the glass's own outline, so no dark
                        // rectangle stands out past its slanted edges. See ShapedViews.cs.
                        BuildOutlineSheet(mesh, pane.Outline, pane.Center, pane.Right, pane.Up);
                    else
                    {
                        mesh.vertices = new Vector3[] { pane.Center - halfRight - halfUp, pane.Center - halfRight + halfUp,
                                                        pane.Center + halfRight + halfUp, pane.Center + halfRight - halfUp };
                        mesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
                        // Both windings, so the facing cannot be got wrong for any window normal.
                        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                    }
                    mesh.RecalculateBounds();

                    var backing = new GameObject("SeamlessInteriors_WindowBacking_" + a);
                    backing.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var backingRenderer = backing.AddComponent<MeshRenderer>();
                    backingRenderer.sharedMaterial = s_OutsideInBackingMaterial;
                    backingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    backingRenderer.receiveShadows = false;
                    backing.SetActive(false);
                    s_OutsideInBackings[a] = backing;
                }

                // The drift: a copy of each of the paired interior opening's snow slabs, carried from
                // the interior's glass to the shell's. Its standoff from the glass is kept, which is
                // the whole point: that standoff is what needs the ledge.
                if (!pane.Paired || a >= s_OutsideInApertureDrifts.Count || s_OutsideInApertureDrifts[a] == null) continue;

                // THREE THINGS HAVE TO BE RIGHT FOR A COPY OF AN INTERIOR SLAB TO LOOK LIKE A DRIFT FROM
                // OUTSIDE, and the first version got none of them (owner's screenshot, 2026-09-18: a
                // thin pale ribbon along the sill where a bank of snow should be).
                //
                //   MIRRORED front to back. The slab is modeled to be seen from the ROOM: its room
                //   side has faces and its far side does not, so from outside it is seen from
                //   behind, where only its thin edges survive back-face culling. A 2026-09-15 note
                //   that "a solid object renders from either side" was reasoning, not an observation.
                //   Negating the scale on the axis along the window normal turns the modeled face
                //   outward, and Unity flips the winding for a mirrored transform so it still draws.
                //
                //   SAT ON THE SILL. Carried bottom edge to bottom edge, not center to center. The
                //   shell's small window is 0.55 m tall against an interior opening of another
                //   shape, and center alignment put its drift 24 cm up the wall.
                //
                //   SIZED TO THE SHELL'S PANE. The interior is scaled (1.05, 0.98, 1.05) and its
                //   windows are not the shell's, so an unscaled copy overhangs the frame.
                // NO SNOW OBJECT IN THE ROOM: the snow may be part of the frame's own mesh (Safe House
                // A). See WindowSnow.RoomToBuilding.cs.
                if (s_OutsideInApertureDrifts[a].Count == 0)
                {
                    if (pane.Pretend) continue;
                    Vector4 liftedGlass = a < s_OutsideInGlassRects.Count ? s_OutsideInGlassRects[a]
                                        : new Vector4(-pane.Width * 0.5f, pane.Width * 0.5f, -pane.Height * 0.5f, pane.Height * 0.5f);
                    bool liftedBar;
                    var lifted = BuildLiftedWindowSnow(a, pane, liftedGlass, shellWorld, shellTriangles, out liftedBar);
                    if (lifted == null) continue;
                    s_OutsideInLedgeDrifts[a] = new List<GameObject> { lifted };
                    s_OutsideInLedgeDriftBase[a] = new List<Vector3> { lifted.transform.position };
                    liftedCount++; if (liftedBar) liftedBarCount++;
                    continue;
                }

                Vector3 interiorGlass = s_OutsideInApertureGlass[a];
                Vector2 interiorSize = s_OutsideInApertureInteriorSize[a];
                float scaleAcross = interiorSize.x > 0.01f ? pane.Width / interiorSize.x : 1f;
                float scaleUp = interiorSize.y > 0.01f ? pane.Height / interiorSize.y : 1f;
                Vector3 interiorSill = interiorGlass - pane.Up * (interiorSize.y * 0.5f);
                Vector3 shellSill = pane.Center - pane.Up * (pane.Height * 0.5f);

                var copies = new List<GameObject>();
                var bases = new List<Vector3>();
                var slabs = s_OutsideInApertureDrifts[a];
                for (int k = 0; k < slabs.Count; k++)
                {
                    var slab = slabs[k];
                    if (slab == null) continue;

                    var source = slab.gameObject;
                    var copy = Object.Instantiate(source);
                    copy.name = "SeamlessInteriors_LedgeDrift_" + a;
                    copy.transform.SetParent(null, false);
                    copy.transform.rotation = source.transform.rotation;

                    // LIVE BEFORE IT IS MEASURED. The fit below reads Renderer.bounds, which on an
                    // inactive object reports a box at the origin (see WindowOpenings.cs), and the
                    // clone this is copied from is usually closed now that dressing no longer waits
                    // for the portals. It goes inactive again at the end of this block, before any
                    // camera has drawn it.
                    copy.SetActive(true);
                    var copyRenderers = copy.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < copyRenderers.Length; r++) if (copyRenderers[r] != null) copyRenderers[r].enabled = true;

                    // Where it stood relative to the interior's sill, re-expressed at the shell's,
                    // with the in-plane distances resized. Its standoff from the glass is kept.
                    Vector3 fromSill = source.transform.position - interiorSill;
                    copy.transform.position = shellSill
                        + pane.Right * (Vector3.Dot(fromSill, pane.Right) * scaleAcross)
                        + pane.Up * (Vector3.Dot(fromSill, pane.Up) * scaleUp)
                        + pane.Outward * Vector3.Dot(fromSill, pane.Outward);

                    // Each of the copy's own axes lines up with one of the window's. Resize the two
                    // that lie in the pane and mirror the one along the normal.
                    Vector3 scale = source.transform.lossyScale;
                    Vector3[] localAxes = { copy.transform.right, copy.transform.up, copy.transform.forward };
                    for (int axis = 0; axis < 3; axis++)
                    {
                        float across = Mathf.Abs(Vector3.Dot(localAxes[axis], pane.Right));
                        float up = Mathf.Abs(Vector3.Dot(localAxes[axis], pane.Up));
                        float along = Mathf.Abs(Vector3.Dot(localAxes[axis], pane.Outward));
                        float factor = (along >= across && along >= up) ? -1f : (across >= up ? scaleAcross : scaleUp);
                        if (axis == 0) scale.x *= factor; else if (axis == 1) scale.y *= factor; else scale.z *= factor;
                    }

                    // Mirroring is about the object's pivot, which is not the middle of the slab, so
                    // it jumps along the normal. Measure the jump and take it back out.
                    var copyRenderer = copy.GetComponentInChildren<Renderer>(true);
                    Vector3 unmirrored = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    copy.transform.localScale = unmirrored;
                    float before = copyRenderer != null ? Vector3.Dot(copyRenderer.bounds.center, pane.Outward) : 0f;
                    copy.transform.localScale = scale;
                    float after = copyRenderer != null ? Vector3.Dot(copyRenderer.bounds.center, pane.Outward) : 0f;
                    copy.transform.position += pane.Outward * (before - after);

                    // SEATED BY ITS OWN LOWEST POINT. Carrying it sill to sill relies on the interior
                    // opening's recorded height describing the same extents as the shell's pane, and
                    // for the shell's small window it does not: measured 2026-09-18 with the carry
                    // alone, seven drifts landed within 0.5 cm of their sill and that one 24 cm above
                    // it. The snow's own bottom edge needs no such agreement.
                    //
                    // FITTED TO THIS WINDOW'S OWN GLASS, all three ways, by the drift's own extents:
                    // as wide as the glass plus the overlap, centered on it, and resting on its
                    // bottom edge. Width first, because scaling about the pivot moves the rest.
                    // MEASURED IN THE WINDOW'S OWN AXES, from the mesh bounds' corners carried through
                    // the transform. This used Renderer.bounds, a box aligned to the WORLD, which
                    // inflates on any wall not square to the world axes: on Lake Cabin A_1, whose shell
                    // stands at 7.8 degrees, it made every drift about a centimeter narrow (found
                    // 2026-09-18). The Camp Office stands at 0.0, so it never showed there.
                    if (copyRenderer != null)
                    {
                        Vector4 glass = a < s_OutsideInGlassRects.Count ? s_OutsideInGlassRects[a]
                                      : new Vector4(-pane.Width * 0.5f, pane.Width * 0.5f, -pane.Height * 0.5f, pane.Height * 0.5f);

                        float lo, hi;
                        MeasureAlong(copy, pane.Center, pane.Right, out lo, out hi);
                        float widthWanted = (glass.y - glass.x) + 2f * (OUTSIDE_IN_DRIFT_PAST_GLASS + Mathf.Max(OutsideInDriftOverlap, 0f));
                        if (hi - lo > 0.01f)
                        {
                            Vector3 fitted = copy.transform.localScale;
                            Vector3[] axes = { copy.transform.right, copy.transform.up, copy.transform.forward };
                            int acrossAxis = 0; float bestDot = 0f;
                            for (int axis = 0; axis < 3; axis++) { float d = Mathf.Abs(Vector3.Dot(axes[axis], pane.Right)); if (d > bestDot) { bestDot = d; acrossAxis = axis; } }
                            float factor = widthWanted / (hi - lo);
                            if (acrossAxis == 0) fitted.x *= factor; else if (acrossAxis == 1) fitted.y *= factor; else fitted.z *= factor;
                            copy.transform.localScale = fitted;
                        }

                        MeasureAlong(copy, pane.Center, pane.Right, out lo, out hi);
                        copy.transform.position += pane.Right * ((glass.x + glass.y) * 0.5f - (lo + hi) * 0.5f);

                        MeasureAlong(copy, pane.Center, pane.Up, out lo, out hi);
                        copy.transform.position += pane.Up * (glass.z - lo);

                        // ...AND UP BY PART OF THE SILL'S LIP. See TryMeasureSillLip.
                        float lip;
                        if (TryMeasureSillLip(pane, glass, shellWorld, shellTriangles, out lip))
                            copy.transform.position += pane.Up * Mathf.Clamp(lip * OUTSIDE_IN_DRIFT_RISES_BY_SHARE_OF_SILL_LIP, 0f, 0.015f);

                        // HOW FAR OUT: A FIXED DISTANCE BEHIND THE WINDOW'S OWN BAR, or behind its frame
                        // where it has no bar. See OUTSIDE_IN_DRIFT_BEHIND_RELIEF.
                        float relief;
                        if (TryMeasureWindowRelief(pane, glass, shellWorld, shellTriangles, out relief))
                        {
                            MeasureAlong(copy, pane.Center, pane.Outward, out lo, out hi);
                            copy.transform.position += pane.Outward * ((relief - OUTSIDE_IN_DRIFT_BEHIND_RELIEF) - hi);
                        }
                        else
                        {
                            copy.transform.position += pane.Outward * OUTSIDE_IN_DRIFT_LEGACY_OFFSET;
                        }
                    }

                    // A copy of an interior object is on whatever layer outside-in moved the interior
                    // to, which the player's camera does not draw, and may carry a collider.
                    var parts = copy.GetComponentsInChildren<Transform>(true);
                    for (int t = 0; t < parts.Length; t++) if (parts[t] != null) parts[t].gameObject.layer = 0;
                    var colliders = copy.GetComponentsInChildren<Collider>(true);
                    for (int c = 0; c < colliders.Length; c++) if (colliders[c] != null) colliders[c].enabled = false;

                    // Last, once it sits where it will be seen: the crop is by height in this window.
                    if (IsDriftSillOnly(instance.Config.ExteriorShellPrefabName, a)) CropLedgeDriftToSill(copy, pane);

                    copy.SetActive(false);
                    copies.Add(copy);
                    bases.Add(copy.transform.position);
                }
                s_OutsideInLedgeDrifts[a] = copies;
                s_OutsideInLedgeDriftBase[a] = bases;
            }

            if (liftedCount > 0 && s_DebugBounds)
                MelonLogger.Msg($"[DIS-ICE] {instance.Config.ResolvedInstanceId}: {liftedCount} pencerenin kari cercevesinin kendi aginin icindeydi, oradan alinip disariya kuruldu ({liftedBarCount} tanesinde orta citanin ustunde de kar var).",
                                $"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: {liftedCount} windows' snow was part of the frame's own mesh in the room and was lifted out of it and set on the outside ({liftedBarCount} of them with snow on the center bar too).");
        }

        // ─── THE BAR CAPS ───

        private static void BuildOutsideInBarCaps()
        {
            DestroyOutsideInBarCaps();

            // The shell's most detailed readable mesh, to world space once for all its windows.
            Mesh shellMesh = null;
            Vector3[] shellWorld = null;
            if (s_OutsideInInstance != null && s_OutsideInInstance.ExteriorShell != null)
            {
                Transform shellTransform = null;
                var shellFilters = s_OutsideInInstance.ExteriorShell.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < shellFilters.Length; i++)
                {
                    var candidate = shellFilters[i];
                    if (candidate == null || candidate.sharedMesh == null || !candidate.sharedMesh.isReadable) continue;
                    if (shellMesh != null && candidate.sharedMesh.vertexCount <= shellMesh.vertexCount) continue;

                    // The wall mesh is the one that carries the panes. A roof-snow mesh can be bigger.
                    var candidateRenderer = candidate.GetComponent<MeshRenderer>();
                    if (candidateRenderer == null) continue;
                    bool carriesGlass = false;
                    Material[] candidateMaterials = candidateRenderer.sharedMaterials;
                    for (int k = 0; k < candidateMaterials.Length; k++)
                        if (candidateMaterials[k] != null && IsShellGlassMaterial(candidateMaterials[k])) { carriesGlass = true; break; }
                    // While the building is dressed the panes wear the hidden-pane material instead.
                    for (int k = 0; k < candidateMaterials.Length && !carriesGlass; k++)
                        if (candidateMaterials[k] != null && candidateMaterials[k] == s_OutsideInHiddenPaneMaterial) carriesGlass = true;
                    if (!carriesGlass) continue;
                    shellMesh = candidate.sharedMesh;
                    shellTransform = candidate.transform;
                }
                if (shellMesh != null)
                {
                    Vector3[] local = shellMesh.vertices;
                    shellWorld = new Vector3[local.Length];
                    for (int i = 0; i < local.Length; i++) shellWorld[i] = shellTransform.TransformPoint(local[i]);
                }
            }

            for (int a = 0; a < s_OutsideInApertures.Count; a++)
            {
                s_OutsideInBarCaps.Add(null);
                s_OutsideInBarFront.Add(0f);
                if (!s_OutsideInApertures[a].Paired || s_OutsideInApertures[a].Pretend || a >= s_OutsideInApertureOpenings.Count) continue;

                var opening = s_OutsideInApertureOpenings[a];
                if (opening.PaneRoot == null) continue;

                // The frame, found the way WindowOpenings.cs finds it, and only if it can be read.
                MeshFilter frame = null;
                var filters = opening.PaneRoot.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    if (filters[i] == null || filters[i].sharedMesh == null) continue;
                    if (!filters[i].gameObject.name.EndsWith("_LOD0") || !filters[i].sharedMesh.isReadable) continue;
                    frame = filters[i];
                    break;
                }
                if (frame == null) continue;
                var frameRenderer = frame.GetComponent<MeshRenderer>();
                if (frameRenderer == null || frameRenderer.sharedMaterial == null) continue;

                // The bar's top and underside: faces looking up or down, strictly inside the glass's
                // height, running most of the way across the opening. A frame's head and sill look
                // up and down too, and are ruled out by where they are.
                Vector3[] vertices = frame.sharedMesh.vertices;
                int[] triangles = frame.sharedMesh.triangles;
                Transform frameT = frame.transform;
                float insideHeight = opening.Height * 0.5f - 0.05f;
                float low = float.MaxValue, high = float.MinValue, left = float.MaxValue, rightMost = float.MinValue, front = float.MinValue;
                int faces = 0;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    Vector3 p0 = frameT.TransformPoint(vertices[triangles[t]]) - opening.GlassCenter;
                    Vector3 p1 = frameT.TransformPoint(vertices[triangles[t + 1]]) - opening.GlassCenter;
                    Vector3 p2 = frameT.TransformPoint(vertices[triangles[t + 2]]) - opening.GlassCenter;

                    Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                    if (normal.sqrMagnitude < 1e-10f) continue;
                    if (Mathf.Abs(Vector3.Dot(normal.normalized, opening.Up)) < 0.7f) continue;

                    float height = Vector3.Dot(p0 + p1 + p2, opening.Up) / 3f;
                    if (Mathf.Abs(height) > insideHeight) continue;

                    float longest = Mathf.Max((p0 - p1).magnitude, Mathf.Max((p1 - p2).magnitude, (p0 - p2).magnitude));
                    if (longest < opening.Width * 0.5f) continue;

                    faces++;
                    if (height < low) low = height;
                    if (height > high) high = height;
                    for (int c = 0; c < 3; c++)
                    {
                        Vector3 p = c == 0 ? p0 : c == 1 ? p1 : p2;
                        float across = Vector3.Dot(p, opening.Right), depth = Vector3.Dot(p, opening.Outward);
                        if (across < left) left = across;
                        if (across > rightMost) rightMost = across;
                        if (depth > front) front = depth;
                    }
                }
                // A bar has a top AND an underside. One height alone is a shelf or a sill.
                if (faces < 2 || high - low < 0.01f || high - low > 0.2f) continue;

                // SIZED FROM THE SHELL'S BAR, NOT THE INTERIOR'S. The interior's measured 6.5 cm
                // against the shell's 6.1, undersides level, and a cap cut to the interior's height
                // stood 4 mm proud of the bar it sits behind (owner, 2026-09-18: "I think it's
                // thicker than the outside bar"). The portal lines the two windows up glass center
                // to glass center, so the shell bar's heights carry across as they are, and the
                // owner's two insets are taken from them.
                float shellLow, shellHigh;
                if (TryMeasureShellBar(s_OutsideInApertures[a], shellMesh, shellWorld, out shellLow, out shellHigh))
                {
                    // AND THE BAR ITSELF IS BROUGHT TO THE SAME HEIGHTS, or the cap leaves a slot.
                    // With only the cap cut down, the bar's real top still stood 4 mm above it and
                    // the owner saw straight through: "it's just a tad bit too high, so you can see
                    // the gap underneath it." The frame gets a PRIVATE copy of its mesh with the
                    // bar's top and underside vertices moved; the game's shared mesh, which other
                    // windows and other buildings use, is never written to. Given back on return.
                    float wantedLow = shellLow + OUTSIDE_IN_BAR_UNDERSIDE_OVER_SHELL;
                    float wantedHigh = shellHigh - OUTSIDE_IN_BAR_TOP_UNDER_SHELL;
                    if (wantedHigh - wantedLow > 0.01f)
                    {
                        ResizeInteriorBar(frame, opening, low, high, wantedLow, wantedHigh);
                        low = wantedLow;
                        high = wantedHigh;
                    }
                }

                var mesh = new Mesh();
                mesh.name = "SeamlessInteriors_BarCapMesh";
                mesh.vertices = new Vector3[]
                {
                    opening.Right * left + opening.Up * low, opening.Right * left + opening.Up * high,
                    opening.Right * rightMost + opening.Up * high, opening.Right * rightMost + opening.Up * low
                };
                // The frame's wood, run along the bar at the scale the bar's own height gives it.
                float vSpan = (high - low) / Mathf.Max(rightMost - left, 0.01f);
                mesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(0f, vSpan), new Vector2(1f, vSpan), new Vector2(1f, 0f) };
                mesh.normals = new Vector3[] { opening.Outward, opening.Outward, opening.Outward, opening.Outward };
                // Both windings. From the room it is hidden behind the bar's own room-side face.
                mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                mesh.RecalculateBounds();

                var cap = new GameObject("SeamlessInteriors_BarCap_" + a);
                cap.layer = s_OI.Layer;
                cap.AddComponent<MeshFilter>().sharedMesh = mesh;
                var capRenderer = cap.AddComponent<MeshRenderer>();
                capRenderer.sharedMaterial = frameRenderer.sharedMaterial;
                capRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                s_OutsideInBarCaps[a] = cap;
                s_OutsideInBarFront[a] = front;
            }

            s_OutsideInBarCapsApplied = float.NaN;
            ApplyOutsideInBarCaps();
            HideRoomFramesFromOutside();
        }

        // ─── A ROOM WHOSE WINDOW FRAMES ARE NOT SHOWN FROM OUTSIDE ───
        //
        // For a building whose row in s_BuildingWindowOverrides names the object holding the room's
        // window frames. The object is not switched off, because it holds more than the windows'
        // frames (the gas station's also holds its four door frames): a private copy of its mesh loses
        // the triangles whose three corners all stand in a box round a paired window, the glass
        // plus the casing's width a side and the frame's depth either way. At the gas station that
        // is 326 triangles of 619, tried from the console first.
        //
        // Called where the bar caps are built, once the room has been borrowed, and the copy goes
        // into the same two lists as a frame with a moved bar, so DestroyOutsideInBarCaps gives
        // the game's own mesh back on the way in. It HAS to come back: indoors the casing covers the
        // rim of the hole in the wall, and without it every view out looks far too small (owner,
        // 2026-09-20, on the prototype, which hid them indoors as well). Nothing here runs per frame.
        private const float ROOM_FRAME_HIDDEN_PAST_GLASS = 0.17f;
        private const float ROOM_FRAME_HIDDEN_DEPTH = 0.08f;

        private static void HideRoomFramesFromOutside()
        {
            if (s_OutsideInInstance == null || s_OutsideInInstance.Config == null || s_OutsideInInstance.MasterInterior == null) return;
            BuildingWindowOverride byHand;
            if (!TryGetBuildingWindowOverride(s_OutsideInInstance.Config.ExteriorShellPrefabName, out byHand)) return;
            if (string.IsNullOrEmpty(byHand.RoomFramesHiddenFromOutside)) return;

            MeshFilter frames = null;
            var filters = s_OutsideInInstance.MasterInterior.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length && frames == null; i++)
                if (filters[i] != null && filters[i].gameObject.name == byHand.RoomFramesHiddenFromOutside) frames = filters[i];

            string id = s_OutsideInInstance.Config.ResolvedInstanceId;
            if (frames == null || frames.sharedMesh == null || !frames.sharedMesh.isReadable)
            {
                MelonLogger.Warning($"[DIS-ICE] {id}: odanin pencere cerceveleri '{byHand.RoomFramesHiddenFromOutside}' {(frames == null ? "bulunamadi" : "okunamiyor")}; cerceveler disaridan gorunmeye devam edecek.",
                                    $"[OUTSIDE-IN] {id}: the room's window frames '{byHand.RoomFramesHiddenFromOutside}' {(frames == null ? "were not found" : "cannot be read")}; they stay in the view from outside.");
                return;
            }
            if (s_OutsideInResizedFrames.Contains(frames)) return;

            Mesh original = frames.sharedMesh;
            Vector3[] vertices = original.vertices;
            Transform framesT = frames.transform;
            var world = new Vector3[vertices.Length];
            for (int v = 0; v < vertices.Length; v++) world[v] = framesT.TransformPoint(vertices[v]);

            var byWindow = new bool[vertices.Length];
            int windows = 0;
            for (int a = 0; a < s_OutsideInApertures.Count && a < s_OutsideInApertureOpenings.Count; a++)
            {
                if (!s_OutsideInApertures[a].Paired || s_OutsideInApertures[a].Pretend) continue;
                var opening = s_OutsideInApertureOpenings[a];
                if (opening.PaneRoot == null) continue;
                float halfWidth = (opening.GlassWidth > 0.01f ? opening.GlassWidth : opening.Width) * 0.5f + ROOM_FRAME_HIDDEN_PAST_GLASS;
                float halfHeight = (opening.GlassHeight > 0.01f ? opening.GlassHeight : opening.Height) * 0.5f + ROOM_FRAME_HIDDEN_PAST_GLASS;
                windows++;
                for (int v = 0; v < world.Length; v++)
                {
                    if (byWindow[v]) continue;
                    Vector3 d = world[v] - opening.GlassCenter;
                    if (Mathf.Abs(Vector3.Dot(d, opening.Outward)) > ROOM_FRAME_HIDDEN_DEPTH) continue;
                    if (Mathf.Abs(Vector3.Dot(d, opening.Right)) > halfWidth) continue;
                    if (Mathf.Abs(Vector3.Dot(d, opening.Up)) > halfHeight) continue;
                    byWindow[v] = true;
                }
            }
            if (windows == 0) return;

            Mesh copy = Object.Instantiate(original);
            copy.name = "SeamlessInteriors_FramesHiddenFromOutside_" + original.name;
            int hidden = 0, kept = 0;
            var keep = new List<int>();
            for (int k = 0; k < original.subMeshCount; k++)
            {
                int[] triangles = original.GetTriangles(k);
                keep.Clear();
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    if (byWindow[triangles[t]] && byWindow[triangles[t + 1]] && byWindow[triangles[t + 2]]) { hidden++; continue; }
                    keep.Add(triangles[t]); keep.Add(triangles[t + 1]); keep.Add(triangles[t + 2]);
                    kept++;
                }
                copy.SetTriangles(keep.ToArray(), k);
            }
            if (hidden == 0) { Object.Destroy(copy); return; }

            frames.sharedMesh = copy;
            s_OutsideInResizedFrames.Add(frames);
            s_OutsideInResizedFrameOriginals.Add(original);

            if (s_DebugBounds)
                MelonLogger.Msg($"[DIS-ICE] {id}: odanin pencere cerceveleri disaridan gorunmuyor: {windows} pencerenin cevresinde {hidden} ucgen gizlendi, {kept} ucgen kaldi. Iceri girince oyunun kendi mesh'i geri gelir.",
                                $"[OUTSIDE-IN] {id}: the room's own window frames are left out of the view from outside: {hidden} triangles hidden round {windows} windows, {kept} kept. The game's own mesh comes back on the way in.");
        }

        // Moves every frame vertex standing at the bar's top or underside height to the shell bar's.
        // Nothing else in a window frame stands at those heights: the head and sill are half a
        // meter away.
        private static void ResizeInteriorBar(MeshFilter frame, InteriorWindowOpening opening,
                                              float low, float high, float wantedLow, float wantedHigh)
        {
            const float SAME_HEIGHT = 0.003f;
            const float WORTH_MOVING = 0.0005f;
            bool moveTop = Mathf.Abs(wantedHigh - high) > WORTH_MOVING;
            bool moveUnderside = Mathf.Abs(wantedLow - low) > WORTH_MOVING;
            if (!moveTop && !moveUnderside) return;
            if (s_OutsideInResizedFrames.Contains(frame)) return;

            Mesh original = frame.sharedMesh;
            Mesh copy = Object.Instantiate(original);
            copy.name = "SeamlessInteriors_ResizedFrame_" + original.name;

            Transform frameT = frame.transform;
            Vector3 topShift = frameT.InverseTransformVector(opening.Up * (wantedHigh - high));
            Vector3 undersideShift = frameT.InverseTransformVector(opening.Up * (wantedLow - low));

            Vector3[] vertices = copy.vertices;
            int moved = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                float height = Vector3.Dot(frameT.TransformPoint(vertices[i]) - opening.GlassCenter, opening.Up);
                if (moveTop && Mathf.Abs(height - high) < SAME_HEIGHT) { vertices[i] += topShift; moved++; }
                else if (moveUnderside && Mathf.Abs(height - low) < SAME_HEIGHT) { vertices[i] += undersideShift; moved++; }
            }
            if (moved == 0) { Object.Destroy(copy); return; }

            copy.vertices = vertices;
            copy.RecalculateBounds();
            frame.sharedMesh = copy;
            s_OutsideInResizedFrames.Add(frame);
            s_OutsideInResizedFrameOriginals.Add(original);
        }

        // The shell's own horizontal bar across one window: the heights of its top and underside
        // relative to the pane center. Same test as for the interior's bar, in the shell's frame.
        private static bool TryMeasureShellBar(ShellAperture pane, Mesh shellMesh, Vector3[] shellWorld, out float low, out float high)
        {
            low = float.MaxValue;
            high = float.MinValue;
            if (shellMesh == null || shellWorld == null) return false;
            return TryMeasureShellBar(pane, shellMesh.triangles, shellWorld, out low, out high);
        }

        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]

        private static bool TryMeasureShellBar(ShellAperture pane, int[] triangles, Vector3[] shellWorld, out float low, out float high)
        {
            low = float.MaxValue;
            high = float.MinValue;
            if (triangles == null || shellWorld == null) return false;

            // SEARCHED INSIDE THE PANE AS MEASURED, not as set by hand. Safe House A's window 0 is set
            // to 134 cm tall where its glass measures 124, and a search band that tall took in the
            // frame's own sill and head faces, read "a bar 124 cm thick" and found no bar at all
            // (2026-09-19: that window alone lost the snow on its bar). Heights are still returned
            // from pane.Center, which is what every caller places things by.
            float measuredWidth = pane.BaseWidth > 0f ? pane.BaseWidth : pane.Width;
            float measuredHeight = pane.BaseHeight > 0f ? pane.BaseHeight : pane.Height;
            Vector3 toMeasured = pane.BaseHeight > 0f ? pane.BaseCenter - pane.Center : Vector3.zero;
            float measuredAcross = Vector3.Dot(toMeasured, pane.Right), measuredUp = Vector3.Dot(toMeasured, pane.Up);

            float insideHeight = measuredHeight * 0.5f - 0.05f;
            int faces = 0;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p0 = shellWorld[triangles[t]] - pane.Center;
                Vector3 p1 = shellWorld[triangles[t + 1]] - pane.Center;
                Vector3 p2 = shellWorld[triangles[t + 2]] - pane.Center;

                Vector3 middle = (p0 + p1 + p2) / 3f;
                if (Mathf.Abs(Vector3.Dot(middle, pane.Outward)) > 0.1f) continue;
                if (Mathf.Abs(Vector3.Dot(middle, pane.Right) - measuredAcross) > measuredWidth * 0.5f) continue;
                float height = Vector3.Dot(middle, pane.Up);
                if (Mathf.Abs(height - measuredUp) > insideHeight) continue;

                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                if (normal.sqrMagnitude < 1e-10f) continue;
                if (Mathf.Abs(Vector3.Dot(normal.normalized, pane.Up)) < 0.7f) continue;

                float longest = Mathf.Max((p0 - p1).magnitude, Mathf.Max((p1 - p2).magnitude, (p0 - p2).magnitude));
                if (longest < measuredWidth * 0.5f) continue;

                faces++;
                if (height < low) low = height;
                if (height > high) high = height;
            }
            return faces >= 2 && high - low >= 0.01f && high - low <= 0.2f;
        }

        // Stands each cap at its bar's own front, or just inside the view's start plane when that is
        // further in. Cheap when neither dial has moved.
        private static void ApplyOutsideInBarCaps()
        {
            float key = Mathf.Max(OutsideInWindowRecess, 0f);
            if (key == s_OutsideInBarCapsApplied) return;
            s_OutsideInBarCapsApplied = key;

            for (int a = 0; a < s_OutsideInBarCaps.Count; a++)
            {
                var cap = s_OutsideInBarCaps[a];
                if (cap == null || a >= s_OutsideInApertureOpenings.Count) continue;
                var opening = s_OutsideInApertureOpenings[a];

                float viewStart = Vector3.Dot(opening.PortalCenter - opening.GlassCenter, opening.Outward) - key;
                float depth = Mathf.Min(s_OutsideInBarFront[a], viewStart - OUTSIDE_IN_BAR_CAP_INSIDE_VIEW);
                cap.transform.position = opening.GlassCenter + opening.Outward * depth;
            }
        }

        private static void DestroyOutsideInBarCaps()
        {
            for (int i = 0; i < s_OutsideInBarCaps.Count; i++)
            {
                var cap = s_OutsideInBarCaps[i];
                if (cap == null) continue;
                var filter = cap.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
                Object.Destroy(cap);
            }
            s_OutsideInBarCaps.Clear();
            s_OutsideInBarFront.Clear();

            // Each frame gets the game's own mesh back, so from inside the room nothing has changed.
            for (int i = 0; i < s_OutsideInResizedFrames.Count && i < s_OutsideInResizedFrameOriginals.Count; i++)
            {
                var frame = s_OutsideInResizedFrames[i];
                if (frame == null) continue;
                Mesh copy = frame.sharedMesh;
                frame.sharedMesh = s_OutsideInResizedFrameOriginals[i];
                if (copy != null && copy != s_OutsideInResizedFrameOriginals[i]) Object.Destroy(copy);
            }
            s_OutsideInResizedFrames.Clear();
            s_OutsideInResizedFrameOriginals.Clear();
        }

        // ─── WHERE A DRIFT STANDS, FROM THE WINDOW'S OWN SHAPE ───
        //
        // Two numbers the owner settled by eye on the Camp Office turned out to be statements about
        // that window's frame, and stop being right on the next building. Lake Cabin A's bar stands
        // 2.15 cm proud of its glass, the Camp Office's 3.15, so a drift placed for one buries the
        // other's bar; Lake Cabin A's frame starts exactly where its glass ends, the Camp Office's
        // overlaps its glass by 3 to 5 cm, so a drift cut to the glass reaches one frame and stops
        // short of the other (owner at Lake Cabin A_1, 2026-09-18: "they don't extend all the way to
        // the window frame like they should. And the center horizontal wooden thing is buried in the
        // snow").
        //
        // Stated against the window itself they are the same on both: the drift's face 3 mm (now 2,
        // see below) BEHIND THE BAR'S FRONT (the Camp Office's by-eye 2.9 cm behind a 3.2 cm bar), and 1 cm PAST THE
        // GLASS EACH SIDE, which is hidden under the frame boards wherever they stand prouder than
        // the drift, true on both. Applied live to Lake Cabin A_1 before this was written; owner:
        // "That looks much better." A window with no bar uses its frame's face the same way, which
        // is also the owner's older tolerance: never further out than the frame.
        //
        // 3 mm BECAME 2 mm at Lake Cabin E, the first time the rule was seen on a room turned to its
        // shell (owner, 2026-09-18): "the snow is a little too far into the window frame." Tried
        // live from the console on that cabin's five drifts: 5 mm further out "causes it to stick
        // out past the center horizontal bar", which is the rule's own limit seen by eye; 1 mm
        // further out, "that's as good as it's gonna get."
        private const float OUTSIDE_IN_DRIFT_BEHIND_RELIEF = 0.002f;
        private const float OUTSIDE_IN_DRIFT_PAST_GLASS = 0.01f;

        // An object's extent along one world direction, measured from an origin: every mesh's local
        // bounds, corner by corner, through its transform. Not Renderer.bounds; see the fit above.
        private static void MeasureAlong(GameObject go, Vector3 origin, Vector3 axis, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            var filters = go.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                if (filter == null || filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + new Vector3(((c & 1) == 0 ? 1f : -1f) * b.extents.x,
                                                            ((c & 2) == 0 ? 1f : -1f) * b.extents.y,
                                                            ((c & 4) == 0 ? 1f : -1f) * b.extents.z);
                    float d = Vector3.Dot(filter.transform.TransformPoint(corner) - origin, axis);
                    if (d < lo) lo = d;
                    if (d > hi) hi = d;
                }
            }
            if (lo > hi) { lo = 0f; hi = 0f; }
        }

        // ─── A BUILDING THAT DOES NOT STAND TRUE ───
        //
        // Safe House A is tipped 1.34 degrees as a whole building, presumably to sit on its slope.
        // The mod's shell object for it (STRSPAWN_CabinAExterior_Prefab) has no rotation at all;
        // the child that carries every LOD of the wall (STR_CabinAExteriorBase_Prefab) is rotated
        // (358.80, 116.42, 359.40). The window table is in the untipped parent's frame and every
        // aperture was built with a level outward and Vector3.up, so against the real frame the
        // picture and the frost leaned up to 1.2 degrees in depth on two walls and slanted up to
        // 1.2 degrees sideways on the other two: 2.6 cm over a 1.24 m pane.
        //
        // Owner, 2026-09-18, after a night of frost strips, rectangles that would not center and
        // portals that would not fill their frames at this one cabin: "Seems like both the portal
        // and the frost are slanted compared to the window", then "This is probably what caused
        // all our frost panel problems earlier." Measured two ways that agree: the glass faces
        // 0.6 degrees up on one wall and 0.6 down on the wall opposite, 1.2 and 1.2 on the other
        // pair (a rigid body, sqrt(0.6^2 + 1.2^2) = 1.34); and the transform above. All other 14
        // LakeRegion buildings stand 0.00 degrees off. NOT YET RUN when this was written.
        //
        // The tilt is the rotation that carries world up onto the wall mesh's own up, and nothing
        // else: no turn about the vertical, which the window table already has. Taken from the
        // same mesh TryGetShellWorldMesh reads. A mesh whose own up is nowhere near the world's
        // (an asset authored lying down) says nothing about tilt and is ignored, loudly.
        private const float SHELL_TILT_WORTH_HAVING_DEGREES = 0.05f;
        private const float SHELL_TILT_BELIEVABLE_DEGREES = 10f;

        private static bool TryGetShellTilt(SeamlessInteriorInstance instance, out Quaternion tilt, out float degrees)
        {
            tilt = Quaternion.identity; degrees = 0f;
            if (instance == null || instance.ExteriorShell == null) return false;

            Mesh best = null; Transform bestT = null;
            var filters = instance.ExteriorShell.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var f = filters[i];
                if (f == null || f.sharedMesh == null) continue;
                if (best != null && f.sharedMesh.vertexCount <= best.vertexCount) continue;
                var r = f.GetComponent<MeshRenderer>();
                if (r == null) continue;
                bool carriesGlass = false;
                Material[] mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length && !carriesGlass; k++)
                    if (mats[k] != null && (mats[k] == s_OutsideInHiddenPaneMaterial
                        || IsShellGlassMaterial(mats[k]))) carriesGlass = true;
                if (!carriesGlass) continue;
                best = f.sharedMesh; bestT = f.transform;
            }
            if (bestT == null) return false;

            degrees = Vector3.Angle(Vector3.up, bestT.up);
            if (degrees < SHELL_TILT_WORTH_HAVING_DEGREES) { degrees = 0f; return false; }
            if (degrees > SHELL_TILT_BELIEVABLE_DEGREES)
            {
                MelonLogger.Warning($"[DIS-ICE] {instance.Config.ResolvedInstanceId}: duvar aginin kendi 'yukari'si dunyaninkinden {degrees:F1} derece uzakta; bu bir egim degil, yok sayildi.",
                                    $"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: the wall mesh's own 'up' is {degrees:F1} degrees from the world's; that is not a tilt (the asset was made lying down), ignored.");
                degrees = 0f;
                return false;
            }

            tilt = Quaternion.FromToRotation(Vector3.up, bestT.up);
            return true;
        }

        // Where the eye stands in the ROOM for a viewer standing at `eye` outside the building. Its
        // place is read along the BUILDING's axes (tipped, if the building is) and set down along
        // the ROOM's (level and upright), at the paired interior pane. On a building that stands
        // true the two frames are one and this is eye + Delta, as it always was.
        private static Vector3 CarryEyeIntoRoom(ShellAperture pane, Vector3 eye)
        {
            Vector3 fromPane = eye - pane.Center;
            return pane.Center + pane.Delta
                 + pane.RoomRight * Vector3.Dot(fromPane, pane.Right)
                 + Vector3.up * Vector3.Dot(fromPane, pane.Up)
                 + pane.RoomOutward * Vector3.Dot(fromPane, pane.Outward);
        }

        // The shell's most detailed readable wall mesh, in world space.
        private static bool TryGetShellWorldMesh(SeamlessInteriorInstance instance, out Vector3[] world, out int[] triangles)
        {
            world = null; triangles = null;
            if (instance == null || instance.ExteriorShell == null) return false;

            Mesh best = null; Transform bestT = null;
            var filters = instance.ExteriorShell.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var f = filters[i];
                if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
                if (best != null && f.sharedMesh.vertexCount <= best.vertexCount) continue;
                var r = f.GetComponent<MeshRenderer>();
                if (r == null) continue;
                bool carriesGlass = false;
                Material[] mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length && !carriesGlass; k++)
                    if (mats[k] != null && (mats[k] == s_OutsideInHiddenPaneMaterial
                        || IsShellGlassMaterial(mats[k]))) carriesGlass = true;
                if (!carriesGlass) continue;
                best = f.sharedMesh; bestT = f.transform;
            }
            if (best == null) return false;

            Vector3[] local = best.vertices;
            world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) world[i] = bestT.TransformPoint(local[i]);
            triangles = best.triangles;
            return true;
        }

        // THE DRIFT STANDS ON THE GLASS'S BOTTOM EDGE, AND SOME WINDOWS HAVE A SILL THAT RISES ABOVE IT.
        // The Camp Office, where the drift's fit was settled by eye, has its sill's top 0.1 cm above
        // the bottom of the glass. The lake cabins' windows have a lip there, 1.2 cm up (its two
        // upward faces sit at 1.0 and 1.3, within 2 cm of the glass). With the drift's foot on the
        // glass's edge the owner, at Lake Cabin A_3 on 2026-09-18, asked for it 0.5 cm higher and
        // said of the result "That's the ticket". 0.5 of 1.2 is 0.45, and 0.45 of the Camp Office's
        // 0.1 is half a millimeter, which keeps what he approved there. Two buildings fit any line,
        // so this share is a by-eye value WITH A MEASURED THING UNDER IT, not a derivation; it was
        // dry-run over every LakeRegion window before it was written: cabins 5.3 mm, their small
        // gable panes 1.7, the Camp Office 0.5 to 0.9 (its small window 0, a known open case, 9.8),
        // trailers 0.0. If another style of window disagrees, measure its lip first.
        private const float OUTSIDE_IN_DRIFT_RISES_BY_SHARE_OF_SILL_LIP = 0.45f;

        // The mean height, above the glass's bottom edge, of the shell's upward-looking faces that
        // lie under the pane's width, within 3 cm outside the glass and no more than 3 cm up.
        private static bool TryMeasureSillLip(ShellAperture pane, Vector4 glass, Vector3[] world, int[] triangles, out float lip)
        {
            lip = 0f;
            if (world == null || triangles == null) return false;

            float sum = 0f; int faces = 0;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p0 = world[triangles[t]] - pane.Center, p1 = world[triangles[t + 1]] - pane.Center, p2 = world[triangles[t + 2]] - pane.Center;
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude < 1e-10f || Vector3.Dot(n.normalized, pane.Up) < 0.7f) continue;

                Vector3 middle = (p0 + p1 + p2) / 3f;
                float proud = Vector3.Dot(middle, pane.Outward);
                if (proud < 0f || proud > 0.03f) continue;
                float across = Vector3.Dot(middle, pane.Right);
                if (across < glass.x || across > glass.y) continue;
                float aboveBottom = Vector3.Dot(middle, pane.Up) - glass.z;
                if (aboveBottom < -0.005f || aboveBottom > 0.03f) continue;

                sum += aboveBottom; faces++;
            }

            if (faces == 0) return false;
            lip = sum / faces;
            return true;
        }

        // How far proud of the glass the thing a drift must stay behind stands: the front of the
        // window's horizontal bar if it has one, otherwise the nearest face of its frame. Faces
        // looking out of the building, within a hand's width of this window's glass.
        [System.Runtime.CompilerServices.MethodImpl(CoverOptimised)]
        private static bool TryMeasureWindowRelief(ShellAperture pane, Vector4 glass, Vector3[] world, int[] triangles, out float relief)
        {
            relief = 0f;
            if (world == null || triangles == null) return false;

            // WHICH FACES ARE THE BAR'S. "An outward face inside the pane" is not the bar: a shutter
            // closed flush over the window is one too, 9.5 cm out, and on the four shuttered lake
            // cabin windows (A_1, A_2, A_3 window 2; C window 1) it dragged the mean from the bar's
            // 2.08 cm to 5.8, so the drift stood a centimeter in FRONT of the bar it is meant to sit
            // behind (owner at A_3, 2026-09-18: "the center horizontal bar in this window is not
            // quite lined up like the rest of them"; "we could try to make a rule to classify the
            // center bar a little more precisely"). The bar is the thing whose top and underside
            // TryMeasureShellBar finds, so its front is the outward faces BETWEEN those two heights;
            // a shutter's two big triangles have their middles a hand's width above and below it.
            // Dry run over all 68 LakeRegion windows before it was allowed to change anything: the
            // four shuttered windows come to 2.08, what their unshuttered sisters already measure;
            // the Camp Office's come to 3.14 to 3.17 from 3.41 to 3.44, and its bar was measured by
            // hand at 3.1 to 3.2 long before this rule existed. A window with no bar, or a bar with
            // no front face, is measured as it was before.
            float barLow, barHigh;
            bool barKnown = TryMeasureShellBar(pane, triangles, world, out barLow, out barHigh);
            float onBarSum = 0f; int onBarFaces = 0;

            float barSum = 0f; int barFaces = 0; float frameNearest = float.MaxValue;
            float glassHalfHeight = (glass.w - glass.z) * 0.5f, glassMidUp = (glass.w + glass.z) * 0.5f;
            float glassWidth = glass.y - glass.x;

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p0 = world[triangles[t]] - pane.Center, p1 = world[triangles[t + 1]] - pane.Center, p2 = world[triangles[t + 2]] - pane.Center;
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude < 1e-10f || Vector3.Dot(n.normalized, pane.Outward) < 0.7f) continue;

                Vector3 middle = (p0 + p1 + p2) / 3f;
                float proud = Vector3.Dot(middle, pane.Outward);
                if (proud < 0.003f || proud > 0.12f) continue;

                float across = Vector3.Dot(middle, pane.Right), upward = Vector3.Dot(middle, pane.Up);
                if (across < glass.x - 0.06f || across > glass.y + 0.06f) continue;
                if (upward < glass.z - 0.06f || upward > glass.w + 0.06f) continue;

                float longest = Mathf.Max((p0 - p1).magnitude, Mathf.Max((p1 - p2).magnitude, (p0 - p2).magnitude));
                bool insideHeight = Mathf.Abs(upward - glassMidUp) < glassHalfHeight - 0.05f;
                bool insideWidth = across > glass.x + 0.03f && across < glass.y - 0.03f;

                if (insideHeight && insideWidth && longest >= glassWidth * 0.3f)
                {
                    barSum += proud; barFaces++;
                    if (barKnown && upward > barLow - 0.005f && upward < barHigh + 0.005f) { onBarSum += proud; onBarFaces++; }
                }
                else if (!insideHeight || !insideWidth) { if (proud < frameNearest) frameNearest = proud; }
            }

            if (onBarFaces > 0) { relief = onBarSum / onBarFaces; return true; }
            if (barFaces > 0) { relief = barSum / barFaces; return true; }
            if (frameNearest < float.MaxValue) { relief = frameNearest; return true; }
            return false;
        }

        // Brings everything that depends on the recess to the dial's current value. Cheap when the
        // value has not moved, which is every frame but the ones the owner is dragging it.
        private static void ApplyOutsideInRecess(SeamlessInteriorInstance instance)
        {
            float recess = Mathf.Max(OutsideInWindowRecess, 0f);
            float driftOffset = OutsideInDriftOffset;
            // The overlap changes the drifts' SIZE, not just where they stand, so they are rebuilt.
            // Eight small copies, and only on the frames the dial is being dragged.
            float driftOverlap = OutsideInDriftOverlap;
            if (driftOverlap != s_OutsideInAppliedDriftOverlap)
            {
                bool first = float.IsNaN(s_OutsideInAppliedDriftOverlap);
                s_OutsideInAppliedDriftOverlap = driftOverlap;
                if (!first) { BuildOutsideInRecessObjects(instance); s_OutsideInAppliedRecess = -1f; }
            }

            if (recess == s_OutsideInAppliedRecess && driftOffset == s_OutsideInAppliedDriftOffset) return;
            s_OutsideInAppliedRecess = recess;
            s_OutsideInAppliedDriftOffset = driftOffset;

            // The shell's painted panes stay hidden for as long as outside-in holds the building,
            // whatever the slider says, including zero. One path, no special case.
            if (!s_OutsideInShellPanesHidden) HideShellPanes(instance);

            for (int a = 0; a < s_OutsideInApertures.Count; a++)
            {
                Vector3 outward = s_OutsideInApertures[a].Outward;

                if (a < s_OutsideInBackings.Count && s_OutsideInBackings[a] != null)
                {
                    s_OutsideInBackings[a].transform.position = -outward * (recess - s_OutsideInApertures[a].Toward + OUTSIDE_IN_BACKING_BEHIND_QUAD);
                    bool backingOn = s_OI.CamerasOn && s_OI.Level >= 1f;
                    if (s_OutsideInBackings[a].activeSelf != backingOn) s_OutsideInBackings[a].SetActive(backingOn);
                }

                // A FIXED gap in front of the portal. This is the constant the stack is built round.
                if (a < s_OutsideInFrostOverlays.Count && s_OutsideInFrostOverlays[a] != null)
                    s_OutsideInFrostOverlays[a].transform.position = -outward * (recess - s_OutsideInApertures[a].Toward - OUTSIDE_IN_FROST_PROUD_OF_QUAD);

                if (a < s_OutsideInLedgeDrifts.Count && s_OutsideInLedgeDrifts[a] != null)
                    for (int k = 0; k < s_OutsideInLedgeDrifts[a].Count; k++)
                        if (s_OutsideInLedgeDrifts[a][k] != null)
                            s_OutsideInLedgeDrifts[a][k].transform.position = s_OutsideInLedgeDriftBase[a][k] - outward * (recess - driftOffset);
            }
        }

        private static void HideShellPanes(SeamlessInteriorInstance instance)
        {
            if (s_OutsideInShellPanesHidden || instance == null || instance.ExteriorShell == null) return;

            Shader shader = Shader.Find(OUTSIDE_IN_FROST_SHADER);
            if (shader == null) return;

            // Fully transparent. There is no way to switch off one submesh of a renderer, and the
            // panes share theirs with the walls, the door and the roof.
            s_OutsideInHiddenPaneMaterial = new Material(shader);
            s_OutsideInHiddenPaneMaterial.name = "SeamlessInteriors_HiddenPane";
            s_OutsideInHiddenPaneMaterial.color = new Color(0f, 0f, 0f, 0f);

            string shellName = instance.Config != null ? instance.Config.ExteriorShellPrefabName : null;
            bool anyLeftAlone = false;
            for (int a = 0; a < s_OutsideInApertures.Count && !anyLeftAlone; a++) anyLeftAlone = IsLeftAloneAt(shellName, a);

            var shellRenderers = instance.ExteriorShell.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < shellRenderers.Length; i++)
            {
                var renderer = shellRenderers[i];
                if (renderer == null) continue;

                Material[] current = renderer.sharedMaterials;
                Material[] swapped = null;
                Mesh savedMesh = null, splitMesh = null;
                for (int k = 0; k < current.Length; k++)
                {
                    if (current[k] == null) continue;
                    if (!IsShellGlassMaterial(current[k])) continue;
                    if (swapped == null) swapped = (Material[])current.Clone();
                    swapped[k] = s_OutsideInHiddenPaneMaterial;

                    // A WINDOW LEFT ALONE IS SKIPPED COMPLETELY: its glass is never hidden. See
                    // SplitLeftAloneGlass. One glass submesh to a renderer is split; a second keeps
                    // the plain swap.
                    if (!anyLeftAlone || splitMesh != null) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null) continue;
                    splitMesh = SplitLeftAloneGlass(shellName, filter.sharedMesh, renderer.transform, k);
                    if (splitMesh == null) continue;
                    savedMesh = filter.sharedMesh;
                    filter.sharedMesh = splitMesh;
                    var grown = new Material[swapped.Length + 1];
                    for (int m = 0; m < swapped.Length; m++) grown[m] = swapped[m];
                    grown[swapped.Length] = current[k];
                    swapped = grown;
                }
                if (swapped == null) continue;

                s_OutsideInPaneRenderers.Add(renderer);
                s_OutsideInPaneSavedMaterials.Add(current);
                s_OI.PaneSavedMeshes.Add(savedMesh);
                s_OI.PaneSplitMeshes.Add(splitMesh);
                renderer.sharedMaterials = swapped;
            }
            s_OutsideInShellPanesHidden = true;
        }

        // THE GLASS OF A WINDOW LEFT ALONE IS NEVER TOUCHED. Every pane of a building is one submesh
        // of its wall mesh, so hiding the panes hid these too, and the mod then pasted a copy of their
        // triangles back. The owner, 2026-09-19, of the farmhouse's windows 3 and 21: "something about
        // the texture isn't right, it's not even the same between the two", then "I was hoping the
        // mod could just skip these locations completely." Measured: the building's glass carries
        // vertex colors (RGBA 1,1,1,0 on every glass vertex, where a mesh with none reads 1,1,1,1)
        // and a second UV set, the copy carried neither, and it drew on a renderer of its own.
        //
        // So the wall mesh is cloned WHOLE, which keeps every vertex attribute it has, the glass
        // submesh keeps only the triangles of windows that get a view, and the left-alone windows'
        // triangles move to a NEW LAST submesh that wears the building's own glass material. The
        // same vertices, renderer and material as the game drew before the mod came near. Proven in
        // the console first and approved by him there ("Looks good", log 0919-27).
        //
        // Both faces of a pane go, not only the one facing out: nothing of it is the mod's. The pane
        // is found AS MEASURED (Base*), never as set by hand. Null when there is nothing to split.
        private static Mesh SplitLeftAloneGlass(string shellName, Mesh mesh, Transform meshTransform, int glassSubmesh)
        {
            if (mesh == null || !mesh.isReadable || glassSubmesh < 0 || glassSubmesh >= mesh.subMeshCount) return null;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.GetTriangles(glassSubmesh);
            if (vertices == null || triangles == null || triangles.Length < 3) return null;

            var viewed = new List<int>(triangles.Length);
            var leftAlone = new List<int>(24);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 middle = (meshTransform.TransformPoint(vertices[triangles[t]]) + meshTransform.TransformPoint(vertices[triangles[t + 1]])
                                  + meshTransform.TransformPoint(vertices[triangles[t + 2]])) / 3f;
                bool left = false;
                for (int a = 0; a < s_OutsideInApertures.Count && !left; a++)
                {
                    if (!IsLeftAloneAt(shellName, a)) continue;
                    var pane = s_OutsideInApertures[a];
                    Vector3 fromCenter = middle - pane.BaseCenter;
                    left = Mathf.Abs(Vector3.Dot(fromCenter, pane.Right)) <= pane.BaseWidth * 0.5f + OUTSIDE_IN_FROST_PANE_MARGIN
                        && Mathf.Abs(Vector3.Dot(fromCenter, pane.Up)) <= pane.BaseHeight * 0.5f + OUTSIDE_IN_FROST_PANE_MARGIN
                        && Mathf.Abs(Vector3.Dot(fromCenter, pane.Outward)) <= OUTSIDE_IN_FROST_PANE_MARGIN;
                }
                var list = left ? leftAlone : viewed;
                list.Add(triangles[t]); list.Add(triangles[t + 1]); list.Add(triangles[t + 2]);
            }
            if (leftAlone.Count == 0) return null;

            Mesh split = Object.Instantiate(mesh);
            split.name = mesh.name + "_SeamlessInteriors_SplitGlass";
            int last = split.subMeshCount;
            split.subMeshCount = last + 1;
            split.SetTriangles(viewed.ToArray(), glassSubmesh);
            split.SetTriangles(leftAlone.ToArray(), last);

            if (s_PrefVerboseLogging != null && s_PrefVerboseLogging.Value)
                MelonLogger.Msg($"[DIS-ICE] {mesh.name}: {leftAlone.Count / 3} cam ucgeni oldugu gibi birakildi, {viewed.Count / 3} gizlendi.",
                                $"[OUTSIDE-IN] {mesh.name}: {leftAlone.Count / 3} glass triangles of windows left alone stay as the building has them, {viewed.Count / 3} hidden.");
            return split;
        }

        private static void RestoreShellPanes()
        {
            for (int i = 0; i < s_OutsideInPaneRenderers.Count && i < s_OutsideInPaneSavedMaterials.Count; i++)
            {
                var renderer = s_OutsideInPaneRenderers[i];
                if (renderer == null) continue;
                try
                {
                    // The building's own mesh first, so its materials go back onto the submeshes they belong to.
                    if (i < s_OI.PaneSavedMeshes.Count && s_OI.PaneSavedMeshes[i] != null)
                    {
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null) filter.sharedMesh = s_OI.PaneSavedMeshes[i];
                    }
                    renderer.sharedMaterials = s_OutsideInPaneSavedMaterials[i];
                }
                catch (System.Exception e) { MelonLogger.Warning($"[DIS-ICE] Kabuk cami geri yuklenemedi: {e.Message}", $"[OUTSIDE-IN] A building's painted window pane could not be restored: {e.Message}"); }
            }
            for (int i = 0; i < s_OI.PaneSplitMeshes.Count; i++)
                if (s_OI.PaneSplitMeshes[i] != null) Object.Destroy(s_OI.PaneSplitMeshes[i]);
            s_OutsideInPaneRenderers.Clear();
            s_OutsideInPaneSavedMaterials.Clear();
            s_OI.PaneSavedMeshes.Clear();
            s_OI.PaneSplitMeshes.Clear();
            s_OutsideInShellPanesHidden = false;
            s_OI.GlassShownPastRange = false;

            if (s_OutsideInHiddenPaneMaterial != null) Object.Destroy(s_OutsideInHiddenPaneMaterial);
            s_OutsideInHiddenPaneMaterial = null;
        }

        private static void DestroyOutsideInRecessObjects()
        {
            for (int i = 0; i < s_OutsideInBackings.Count; i++)
            {
                var backing = s_OutsideInBackings[i];
                if (backing == null) continue;
                var filter = backing.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
                Object.Destroy(backing);
            }
            s_OutsideInBackings.Clear();

            for (int i = 0; i < s_OutsideInLedgeDrifts.Count; i++)
                if (s_OutsideInLedgeDrifts[i] != null)
                    for (int k = 0; k < s_OutsideInLedgeDrifts[i].Count; k++)
                    {
                        var drift = s_OutsideInLedgeDrifts[i][k];
                        if (drift == null) continue;
                        // A cropped copy owns its mesh; an uncropped one shares the room's.
                        var filter = drift.GetComponentInChildren<MeshFilter>(true);
                        if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name.EndsWith(LEDGE_DRIFT_SILL_ONLY_SUFFIX, System.StringComparison.Ordinal))
                            Object.Destroy(filter.sharedMesh);
                        Object.Destroy(drift);
                    }
            s_OutsideInLedgeDrifts.Clear();
            s_OutsideInLedgeDriftBase.Clear();

            if (s_OutsideInBackingMaterial != null) Object.Destroy(s_OutsideInBackingMaterial);
            s_OutsideInBackingMaterial = null;
            if (s_OutsideInBackingTexture != null) Object.Destroy(s_OutsideInBackingTexture);
            s_OutsideInBackingTexture = null;
        }

        // ─── SNOW BY HAND: A ROOM COPY CUT TO ITS SILL, AND THE GAME'S OWN DRIFT COPIED ───
        //
        // Both are asked for building by building in s_BuildingWindowOverrides, and both were settled
        // live by the owner's eye before they were written here (LOG-2026-09-30).

        private const string LEDGE_DRIFT_SILL_ONLY_SUFFIX = "_SillOnly";

        // The building's row may pair windows by hand (BuildingWindowOverride.PairedRoomWindows). A room
        // window given to one building window is taken from any other the automatic pairing gave it to.
        private static bool ApplyHandPairs(string shellName, string id, int[] shellToRoom, int roomWindows)
        {
            BuildingWindowOverride row;
            if (!TryGetBuildingWindowOverride(shellName, out row) || row.PairedRoomWindows == null) return false;
            int n = Mathf.Min(row.PairedRoomWindows.Length, shellToRoom.Length);
            for (int s = 0; s < n; s++)
            {
                int room = row.PairedRoomWindows[s];
                if (room >= roomWindows)
                {
                    MelonLogger.Warning($"[OUTSIDE-IN] {id}: the hand pairing gives window {s} room window {room}, but the room has {roomWindows}; left as the automatic pairing had it.");
                    continue;
                }
                if (room >= 0) for (int other = 0; other < shellToRoom.Length; other++) if (shellToRoom[other] == room) shellToRoom[other] = -1;
                shellToRoom[s] = room;
            }
            return true;
        }

        private static bool IsDriftSillOnly(string shellName, int window)
        {
            BuildingWindowOverride row;
            if (!TryGetBuildingWindowOverride(shellName, out row) || row.DriftSillOnly == null) return false;
            return System.Array.IndexOf(row.DriftSillOnly, window) >= 0;
        }

        // Keeps the triangles in the lower quarter of the window and drops the rest, which is the
        // snow on a bar the building's window does not have. Measured on the Camp Office's window 0
        // (55 cm): the sill snow's triangles sit 18 to 26 cm below the window's middle, the bar's
        // within 2 cm of it, nothing between. The room's mesh is shared, so the copy gets its own.
        private static void CropLedgeDriftToSill(GameObject copy, ShellAperture pane)
        {
            var filter = copy.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null) return;
            var source = filter.sharedMesh;
            if (!source.isReadable)
            {
                MelonLogger.Warning($"[OUTSIDE-IN] {copy.name}: the room's snow mesh '{source.name}' cannot be read, so its bar snow is left on.");
                return;
            }

            float keepBelow = -pane.Height * 0.25f;
            var vertices = source.vertices;
            var triangles = source.triangles;
            var kept = new List<int>(triangles.Length);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 middle = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
                if (Vector3.Dot(filter.transform.TransformPoint(middle) - pane.Center, pane.Up) < keepBelow)
                {
                    kept.Add(triangles[i]); kept.Add(triangles[i + 1]); kept.Add(triangles[i + 2]);
                }
            }
            if (kept.Count == 0 || kept.Count == triangles.Length) return;

            var cropped = Object.Instantiate(source);
            cropped.name = source.name + LEDGE_DRIFT_SILL_ONLY_SUFFIX;
            cropped.subMeshCount = 1;
            cropped.triangles = kept.ToArray();
            cropped.RecalculateBounds();
            filter.sharedMesh = cropped;
        }

        // Made once per scene: a copy already on the shell is left where it is. Named SI_ so the
        // hide pass and the watchdog leave it alone (IsOwnPortalObject), like the mod's other objects.
        private static void BuildCopiedShellDrifts(SeamlessInteriorInstance instance)
        {
            BuildingWindowOverride row;
            if (instance == null || instance.ExteriorShell == null) return;
            if (!TryGetBuildingWindowOverride(instance.Config.ExteriorShellPrefabName, out row) || row.CopiedDrifts == null) return;

            var shell = instance.ExteriorShell.transform;
            var parts = shell.GetComponentsInChildren<Transform>(true);
            for (int d = 0; d < row.CopiedDrifts.Length; d++)
            {
                var want = row.CopiedDrifts[d];
                if (want == null || string.IsNullOrEmpty(want.SourceObject)) continue;
                if (want.FromWindow < 0 || want.FromWindow >= s_OutsideInApertures.Count || want.ToWindow < 0 || want.ToWindow >= s_OutsideInApertures.Count) continue;

                string name = "SI_ShellDriftCopy_" + want.SourceObject + "_w" + want.FromWindow + "_to_w" + want.ToWindow;
                bool made = false;
                for (int i = 0; i < parts.Length && !made; i++) if (parts[i] != null && parts[i].name == name) made = true;
                if (made) continue;

                var from = s_OutsideInApertures[want.FromWindow];
                var to = s_OutsideInApertures[want.ToWindow];
                Vector3 overFrom = from.BaseCenter + from.Up * (from.BaseHeight * 0.5f);
                Transform source = null; float best = 1f;
                for (int i = 0; i < parts.Length; i++)
                {
                    var t = parts[i];
                    if (t == null || !t.name.StartsWith(want.SourceObject, System.StringComparison.Ordinal)) continue;
                    float distance = (t.position - overFrom).magnitude;
                    if (distance < best) { best = distance; source = t; }
                }
                if (source == null)
                {
                    MelonLogger.Warning($"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: no '{want.SourceObject}' within 1 m of window {want.FromWindow}'s top, so window {want.ToWindow} gets no copy of it.");
                    continue;
                }

                // Turned as the one window faces from the other; straight opposite walls turn about up.
                Quaternion turn = Vector3.Dot(from.Outward, to.Outward) < -0.99f
                    ? Quaternion.AngleAxis(180f, Vector3.up)
                    : Quaternion.FromToRotation(from.Outward, to.Outward);
                var copy = Object.Instantiate(source.gameObject, source.parent);
                copy.name = name;
                copy.transform.rotation = turn * source.rotation;
                copy.transform.localScale = source.localScale;
                copy.transform.position = to.BaseCenter + to.Right * (want.AcrossCm / 100f) + to.Up * (want.UpCm / 100f) + to.Outward * (want.OutCm / 100f);
                MatchCopiedDriftFade(copy, parts, want.SourceObject);
                if (s_DebugBounds)
                    MelonLogger.Msg($"[DIS-ICE] {instance.Config.ResolvedInstanceId}: oyunun '{source.name}' kari {want.FromWindow}. pencerenin ustunden {want.ToWindow}. pencerenin ustune kopyalandi.",
                                    $"[OUTSIDE-IN] {instance.Config.ResolvedInstanceId}: the game's '{source.name}' over window {want.FromWindow} copied over window {want.ToWindow}.");
            }
        }

        // THE COPY FADES WITH THE BUILDING'S OTHER DRIFTS, not with the one it was copied from. The
        // game placed its drifts at different scales, so each fades at its own distance: the one over
        // the Camp Office's window 5 goes about 34 m before the rest, and its copy on window 7, which
        // faces the approach, went with it while its neighbors stayed (owner, 2026-09-30: "I just want
        // it to look the same as the rest of the snowdrifts on the building"). Its LOD heights are
        // scaled so it is culled where the longest-lasting drift of the same kind on the shell is.
        // The LODs are rebuilt, not edited: an element read from the interop array is a copy.
        private static void MatchCopiedDriftFade(GameObject copy, Transform[] shellParts, string kindPrefix)
        {
            var group = copy.GetComponent<LODGroup>();
            float fade = LodFadeSize(group);
            if (fade <= 0f) return;

            string kind = kindPrefix.Length >= 8 ? kindPrefix.Substring(0, 8) : kindPrefix;   // "TRN_Snow"
            float furthest = 0f;
            for (int i = 0; i < shellParts.Length; i++)
            {
                var t = shellParts[i];
                if (t == null || !t.name.StartsWith(kind, System.StringComparison.Ordinal)) continue;
                furthest = Mathf.Max(furthest, LodFadeSize(t.GetComponent<LODGroup>()));
            }
            if (furthest <= fade) return;

            float factor = fade / furthest;
            var lods = group.GetLODs();
            var scaled = new LOD[lods.Length];
            for (int i = 0; i < lods.Length; i++) scaled[i] = new LOD(lods[i].screenRelativeTransitionHeight * factor, lods[i].renderers);
            group.SetLODs(scaled);
        }

        // Proportional to the distance at which the group is culled: its world size over its last LOD's height.
        private static float LodFadeSize(LODGroup group)
        {
            if (group == null || group.lodCount == 0) return 0f;
            var lods = group.GetLODs();
            float last = lods[lods.Length - 1].screenRelativeTransitionHeight;
            if (last <= 0f) return 0f;
            Vector3 s = group.transform.lossyScale;
            return group.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))) / last;
        }

        private static void DestroyOutsideInFrostOverlays()
        {
            for (int i = 0; i < s_OutsideInFrostOverlays.Count; i++)
            {
                var overlay = s_OutsideInFrostOverlays[i];
                if (overlay == null) continue;

                var filter = overlay.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Object.Destroy(filter.sharedMesh);
                Object.Destroy(overlay);
            }
            s_OutsideInFrostOverlays.Clear();

            if (s_OutsideInFrostMaterial != null) Object.Destroy(s_OutsideInFrostMaterial);
            s_OutsideInFrostMaterial = null;
            DestroyOutsideCovers();
        }


        // ─── TEARDOWN: GIVE EVERYTHING BACK ───
        //
        // Each step stands alone. One shared try/catch would abandon the rest of the cleanup at the
        // first failure, and a half-restored state - the interior still on layer 3 with the main
        // camera's mask already put back, say - is a building that has gone invisible for the rest
        // of the save with nothing tracking why.

        public static void TeardownOutsideInPortals()
        {
            try { TeardownViewMotion(); } catch { }
            // First, and on its own: overlays left behind would hang painted frost in mid air over a
            // window the portal system has stopped drawing.
            DestroyOutsideInFrostOverlays();

            // The window models go back to the game's own choice of detail first, for the same reason.
            try { PinOutsideInWindowModels(s_OI, false); } catch { }
            // The shell gets its painted panes back before anything else can fail: a shell left
            // wearing invisible panes with nothing behind them is a building with holes in it.
            RestoreShellPanes();
            DestroyOutsideInRecessObjects();
            s_OutsideInAppliedRecess = -1f;

            ReturnInteriorFromOutsideIn();

            s_OutsideInApertureDrifts.Clear();
            s_OutsideInApertures.Clear();
            s_OutsideInInstance = null;
            s_OutsideInInstanceId = null;

            // This tears down the BOUND building, and it is then no longer one of the set.
            s_OutsideInBuildings.Remove(s_OI);
            s_OI = s_OutsideInNone;
        }

        // ─── A ROOM'S OWN WINDOW IS NOT DRAWN WHILE IT IS SEEN FROM BEHIND ───
        //
        // Safe House A, owner, 2026-09-18: "They just don't line up ... The interior one seems
        // smaller", then "I can literally see the scene moving inside of that gap where the
        // interior window cannot be seen" and "none of those objects have an outside-facing part".
        // Each of that room's windows is one prefab, a frame 0.97 x 1.29 m round glass of
        // 0.49 x 1.11, modeled to be seen from the room. The building's pane is 0.64 x 1.24: wider
        // than that glass and narrower than that frame. Through it the room's glass read as a
        // smaller window floating in the pane, and round it the frame was seen from behind, where
        // its faces do not draw, its sill's top does, and the room shows through the rest.
        //
        // Seen from outside, the BUILDING'S window is the window. So the room's copy is switched
        // off, by window, for as long as the eye carried into the room stands on that window's
        // OUTWARD side. It is judged per window and not for the room at once because the window on
        // the far wall is seen from the room's side and must stay (owner: "we still need to see
        // the stuff that's inside the windowpane, especially for looking across the room at
        // another window"). Tried live first, as a blanket hide and then by side; owner: "this
        // looks good enough for now." A slide toward the viewer was tried before it and is the
        // wrong tool: the view's near plane rides with the camera and cut the furniture.
        //
        // ONLY WHERE THE PANE IS THE LARGER of the two (either way, by more than 5%). Where the
        // room's glass is the larger, every lake cabin and trailer, the pane crops the glass, the
        // frame's back is never in view, and those buildings were passed by eye as they are.
        //
        // Only what stands AT the window is taken: a renderer under the pane's root whose middle
        // lies within half a meter of the opening and 30 cm of its glass, and that was switched on
        // when first seen. The snow slabs the drifts are copied from are left to their own code.
        private const float ROOM_WINDOW_PANE_LARGER_BY = 1.05f;

        private static void UpdateOutsideInRoomWindowSides(Vector3 eye)
        {
            int count = Mathf.Min(s_OutsideInApertures.Count, s_OutsideInApertureOpenings.Count);

            if (s_OI.RoomWindowRenderers.Count != count)
            {
                RestoreOutsideInRoomWindows();
                for (int a = 0; a < count; a++)
                {
                    List<Renderer> taken = null;
                    var pane = s_OutsideInApertures[a];
                    var opening = s_OutsideInApertureOpenings[a];
                    bool paneLarger = pane.Width > opening.Width * ROOM_WINDOW_PANE_LARGER_BY
                                   || pane.Height > opening.Height * ROOM_WINDOW_PANE_LARGER_BY;

                    if (pane.Paired && !pane.Pretend && opening.PaneRoot != null && opening.Width > 0.01f && paneLarger)
                    {
                        taken = new List<Renderer>();
                        var drifts = a < s_OutsideInApertureDrifts.Count ? s_OutsideInApertureDrifts[a] : null;
                        var renderers = opening.PaneRoot.GetComponentsInChildren<Renderer>(false);
                        for (int r = 0; r < renderers.Length; r++)
                        {
                            var renderer = renderers[r];
                            if (renderer == null || !renderer.enabled) continue;
                            if (drifts != null && drifts.Contains(renderer)) continue;

                            Vector3 from = renderer.bounds.center - opening.GlassCenter;
                            if (Mathf.Abs(Vector3.Dot(from, opening.Right)) > opening.Width * 0.5f + 0.5f) continue;
                            if (Mathf.Abs(Vector3.Dot(from, opening.Up)) > opening.Height * 0.5f + 0.5f) continue;
                            if (Mathf.Abs(Vector3.Dot(from, opening.Outward)) > 0.3f) continue;
                            taken.Add(renderer);
                        }
                    }

                    s_OI.RoomWindowRenderers.Add(taken);
                    s_OI.RoomWindowShown.Add(true);
                }
            }

            for (int a = 0; a < count; a++)
            {
                var taken = s_OI.RoomWindowRenderers[a];
                if (taken == null || taken.Count == 0) continue;

                var opening = s_OutsideInApertureOpenings[a];
                // Hiding the room's own window was the cheap answer to a pane larger than it. With the
                // view fitted the room's frame lies on the building's and is wanted in the picture.
                bool fitted = OutsideInViewFit > 0.5f;
                bool fromBehind = !fitted && Vector3.Dot(CarryEyeIntoRoom(s_OutsideInApertures[a], eye) - opening.GlassCenter, opening.Outward) > 0f;
                bool show = !fromBehind;
                if (s_OI.RoomWindowShown[a] == show) continue;

                s_OI.RoomWindowShown[a] = show;
                for (int r = 0; r < taken.Count; r++) if (taken[r] != null) taken[r].enabled = show;
            }
        }

        // Every room window this took goes back on. Called before the room is handed back, which is
        // before the player can stand in it.
        private static void RestoreOutsideInRoomWindows()
        {
            for (int a = 0; a < s_OI.RoomWindowRenderers.Count; a++)
            {
                var taken = s_OI.RoomWindowRenderers[a];
                if (taken == null) continue;
                for (int r = 0; r < taken.Count; r++) if (taken[r] != null) taken[r].enabled = true;
            }
            s_OI.RoomWindowRenderers.Clear();
            s_OI.RoomWindowShown.Clear();
        }

        // Gives back everything BorrowInteriorForOutsideIn took, and leaves the dressing standing.
        // Safe to call when nothing is borrowed: every step is conditional on its own record.
        private static void ReturnInteriorFromOutsideIn()
        {
            // Its wall cameras' grids and the room pieces they switch off belong to this borrowing.
            try { DestroyOutsideInWall(s_OI); } catch { }
            RestoreOutsideInRoomWindows();

            // The drifts go back ON, which is how the clone keeps them when no portal system is
            // managing it. Inside-out applies each pane's recorded state itself when it sets up.
            for (int i = 0; i < s_OutsideInApertureDrifts.Count; i++)
                if (s_OutsideInApertureDrifts[i] != null) SetRenderersEnabled(s_OutsideInApertureDrifts[i], true);

            var lap = System.Diagnostics.Stopwatch.StartNew();

            // The mask and the camera pool are shared by every held room and go with the last one.
            bool lastHeldRoom = s_OutsideInBorrowed && CountOutsideInHeldRooms() <= 1;

            Camera main = GameManager.GetMainCamera();
            if (lastHeldRoom)
            {
                if (s_OutsideInMaskEdited && main != null) main.cullingMask = s_OutsideInSavedMainMask;
                s_OutsideInMaskEdited = false;
            }

            for (int i = 0; i < s_OutsideInRelayered.Count && i < s_OutsideInSavedLayers.Count; i++)
            {
                var renderer = s_OutsideInRelayered[i];
                // Unity's overloaded == is what detects a destroyed native object here, which a
                // renderer belonging to a clone being torn down may well be.
                if (renderer == null) continue;
                var go = renderer.gameObject;
                if (go != null) go.layer = s_OutsideInSavedLayers[i];
            }
            s_OutsideInRelayered.Clear();
            s_OutsideInSavedLayers.Clear();
            double layersMs = lap.Elapsed.TotalMilliseconds;

            // The clone becomes solid again here, and this is the only place that clears the
            // record. The player may be about to walk into this building; an interior restored
            // without its collision is a floor they fall through.
            for (int i = 0; i < s_OutsideInDisabledColliders.Count; i++)
            {
                var collider = s_OutsideInDisabledColliders[i];
                if (collider != null) collider.enabled = true;
            }
            s_OutsideInDisabledColliders.Clear();
            s_OutsideInDisabledSet.Clear();
            double collidersMs = lap.Elapsed.TotalMilliseconds - layersMs;

            // Only switch the clone off again if this system is what switched it on. The player may
            // have walked inside in the meantime, and closing it under them would be catastrophic.
            //
            // WITH SEVERAL ROOMS HELD, "inside" is no longer reason enough to leave this one on. The
            // player who walks into one building is inside THAT room; the other held rooms stayed
            // switched on with nobody managing them until the author's watchdog found them two
            // seconds later and closed them all in one frame, in the middle of the player's first
            // look round the room (seen 2026-09-18 entering Lake Cabin E: F and A_2 closed 49 ms
            // apart, a hitch the owner felt, under a line about a door left half-finished). So a
            // room is also switched off here, in the door's own frame, when the player is known to
            // be standing in a DIFFERENT open room. Known, not assumed: if they are in no room's
            // volume this stays as cautious as before and the watchdog still has the last word.
            bool playerIsElsewhere = !s_IsPlayerInsideClone || PlayerIsInAnotherParkedBuilding(s_OutsideInInstance);
            if (!playerIsElsewhere && s_OutsideInActivatedClone && s_OutsideInInstance != null)
            {
                var playerT = GameManager.GetPlayerTransform();
                if (playerT != null && !s_OutsideInInstance.IsPositionInVolume(playerT.position, 2.0f))
                {
                    foreach (var other in ActiveInteriors.Values)
                    {
                        if (other == null || other == s_OutsideInInstance || other.MasterInterior == null) continue;
                        if (!other.MasterInterior.activeInHierarchy) continue;
                        if (other.IsPositionInVolume(playerT.position)) { playerIsElsewhere = true; break; }
                    }
                }
            }

            if (s_OutsideInActivatedClone && playerIsElsewhere
                && s_OutsideInInstance != null && s_OutsideInInstance.MasterInterior != null)
            {
                HideInteriorCompletely(s_OutsideInInstance);
                // What the watchdog did after closing a room: the outdoor objects hidden on an
                // open room's account follow the set of open rooms.
                if (s_IsPlayerInsideClone)
                {
                    SyncExternalHiddenObjects();
                    if (s_DebugBounds)
                        MelonLogger.Msg($"[OUTSIDE-IN] {s_OutsideInInstance.Config.ResolvedInstanceId}: oyuncu baska bir binaya girdi, oda kapatildi ({lap.Elapsed.TotalMilliseconds - layersMs - collidersMs:F1} ms).",
                                        $"[OUTSIDE-IN] {s_OutsideInInstance.Config.ResolvedInstanceId}: room switched OFF at the door because the player walked into a different building (took {lap.Elapsed.TotalMilliseconds - layersMs - collidersMs:F1} ms).");
                }
            }
            s_OutsideInActivatedClone = false;
            double hideMs = lap.Elapsed.TotalMilliseconds - layersMs - collidersMs;
            s_OutsideInLastReturnLaps = SeamlessInteriorsLog.English
                ? $"ms per step: restore layers {layersMs:F1}, turn collision back on {collidersMs:F1}, switch off {hideMs:F1}"
                : $"katman {layersMs:F1}, carpisma {collidersMs:F1}, kapatma {hideMs:F1}";

            if (lastHeldRoom) { DestroyOutsideInSlots(); DestroyOutsideInFadeMaterials(); }
            DestroyOutsideInBarCaps();
            s_OutsideInBorrowed = false;
        }
    }
}
