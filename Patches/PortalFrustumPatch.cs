using HarmonyLib;
using Il2Cpp;

namespace SeamlessInteriors
{
    // ─── THE PORTAL CLIP MUST SEE THE FIELD OF VIEW THAT IS ACTUALLY RENDERED ───
    //
    // A portal renders only the slice of its window that is on screen, and it finds that slice by
    // clipping the opening against the main camera's frustum. Those five planes were being built
    // inside the mod's own OnLateUpdate - which runs BEFORE vp_FPSCamera.LateUpdate applies the
    // player's zoom. So the clip was using the UNZOOMED field of view, every frame.
    //
    // Measured 2026-09-22, player zoomed at a Camp Office window. The five planes the mod held,
    // against the five the camera actually had: the NEAR plane agreed to 1.0000, and only the four
    // side planes - the only ones that depend on field of view - disagreed. Working the angles back
    // gave the mod's planes a 67.8 degree vertical frustum while the camera was rendering at 16.9.
    // 67.8 is the unzoomed value.
    //
    // The cost: the clip believed about four times more of the window was visible than was, kept
    // all of it, and spread the render target over it. A 1.05 m wide opening had 1.045 m rendered
    // when 0.337 m was on screen. The player saw roughly a 1238 x 510 image stretched over
    // 3840 x 2160. Owner: "compared to non-portal windows without the prefab parked, zooming in
    // looks perfect. This is a major defect. Players WILL stand at the window and zoom in."
    //
    // It is worst exactly when zoomed, because zooming shrinks what is really visible while the
    // clip carries on using the wide value, so the error grows as the player leans in to look.
    //
    // Camera.onPreCull, the usual place for this, is not exposed by this interop build (checked
    // live: the event, its add and its remove are all absent). vp_FPSCamera.LateUpdate is where the
    // zoom lands, so the planes are captured immediately after it. The mod's own tick then uses the
    // planes captured at the end of the previous frame: one frame old, but describing the frustum
    // that was really rendered, which is worth far more than a current reading of the wrong number.
    [HarmonyPatch(typeof(Il2Cpp.vp_FPSCamera), nameof(Il2Cpp.vp_FPSCamera.LateUpdate))]
    public class PortalFrustumCapturePatch
    {
        public static void Postfix()
        {
            SeamlessInteriorsMod.CaptureMainCameraFrustumLate();
            // The wall cameras need this frame's lens, after the zoom, and the planes just captured.
            SeamlessInteriorsMod.TickWallCameras();
            // The same for the views into rooms from outside; the mod's tick handed out the slots.
            SeamlessInteriorsMod.TickOutsideInWallCameras();
        }
    }
}
