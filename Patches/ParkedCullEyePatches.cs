using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SeamlessInteriors
{
    // The second half of the parked cull eye. See Core/SeamlessInteriorsMod.ParkedCullEye.cs for why
    // it exists at all.
    //
    // RenderObjectInstance takes its POSITION from camera fields the mod can redirect, but its
    // frustum planes, and the frustum corners its shadow-caster volume is built from, come from
    // MainCameraFrustrum, which is handed the camera as an argument. Measured with the fields
    // redirected and these patches absent: every frustum plane still passed through the real
    // camera, ParkOffset above the eye the distances were measured from, and tree shadows
    // disappeared entirely.
    //
    // All three methods are called once per frame with FPSCamera, none is inlined, and the ref
    // array parameters go through the Harmony trampoline cleanly. Verified live on 2026-09-18
    // with call counters before any swap was enabled.
    //
    // The prefixes do nothing at all unless the stand-in eye is in force, which is only while
    // the player is inside a parked clone.
    internal static class ParkedCullEyeSwap
    {
        internal static void Apply(ref Camera c)
        {
            if (!SeamlessInteriorsMod.ParkedCullEyeInForce) return;

            Camera eye = SeamlessInteriorsMod.ParkedCullEyeCamera;
            if (eye == null || c == null) return;

            Camera main = GameManager.GetMainCamera();
            if (main == null) return;

            // The Camera handed to a patch is a fresh managed wrapper every call, so identity has
            // to be compared by instance id. Anything that is not the player's camera is left
            // alone: this is not the only caller these methods could ever have.
            if (c.GetInstanceID() != main.GetInstanceID()) return;

            // Bring the stand-in up to date at the moment the game asks for the frustum. The game
            // moves FPSCamera late in the frame, so the pose copied by the mod's own tick can be a
            // frame behind the head.
            SeamlessInteriorsMod.SyncParkedCullEye(main);
            c = eye;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.MainCameraFrustrum), nameof(Il2Cpp.MainCameraFrustrum.RecalculateFrustumPlanes), new System.Type[] { typeof(Camera) })]
    public class ParkedCullEyeCapturePlanesPatch
    {
        public static void Prefix(ref Camera c)
        {
            ParkedCullEyeSwap.Apply(ref c);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.MainCameraFrustrum), nameof(Il2Cpp.MainCameraFrustrum.RecalculateFrustumPlanes),
        new System.Type[] { typeof(Camera), typeof(Il2CppStructArray<Plane>), typeof(float) },
        new ArgumentType[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public class ParkedCullEyeFrustumPlanesPatch
    {
        public static void Prefix(ref Camera c)
        {
            ParkedCullEyeSwap.Apply(ref c);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.MainCameraFrustrum), nameof(Il2Cpp.MainCameraFrustrum.RecalculateFrustumCorners),
        new System.Type[] { typeof(Camera), typeof(Il2CppStructArray<Vector3>), typeof(float) },
        new ArgumentType[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public class ParkedCullEyeFrustumCornersPatch
    {
        public static void Prefix(ref Camera c)
        {
            ParkedCullEyeSwap.Apply(ref c);
        }
    }

    // ─── LEAVING FOR THE MAIN MENU ───
    //
    // The stand-in eye has to be let go BEFORE the scene starts coming down. See
    // SuspendParkedCullEyeForSceneExit for what happens otherwise. Both of the game's routes to
    // the menu, the two that a dozen other mods hook for the same kind of reason.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.GameManager), nameof(Il2Cpp.GameManager.DoExitToMainMenu))]
    public class ParkedCullEyeExitToMenuPatch
    {
        public static void Prefix()
        {
            SeamlessInteriorsMod.SuspendParkedCullEyeForSceneExit();
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.GameManager), nameof(Il2Cpp.GameManager.LoadMainMenu))]
    public class ParkedCullEyeLoadMainMenuPatch
    {
        public static void Prefix()
        {
            SeamlessInteriorsMod.SuspendParkedCullEyeForSceneExit();
        }
    }

    // ─── WILDLIFE, AND ANYTHING ELSE THAT ASKS WHETHER A POSITION IS ON SCREEN ───
    //
    // The frustum patches above do not reach wildlife. With them in force and everything else
    // vanilla, a wolf in plain view through a window still vanished when the player zoomed on it:
    // its renderers were switched off and it was demoted to an imposter, which also stops it
    // pathing, because the game fully simulates an animal to 1000 m on screen and to only 50 m off
    // screen. The verdict comes from Utils.PositionIsOnscreen(pos, adjustViewport), measured on
    // 2026-09-18 firing exactly once per animal per frame.
    //
    // IT IS FIXED BY MOVING THE QUESTION, NOT BY ANSWERING IT. The player's real camera is the
    // stand-in eye plus ParkOffset, with the same rotation and field of view, so asking the real
    // camera about (pos + ParkOffset) is exactly asking the stand-in eye about pos. Nothing here
    // needs to know how the game decides, including what adjustViewport means.
    //
    // Measured with this shift on: the wolf in the window's view got its renderers back, stopped
    // being an imposter and resumed pathing, while the four animals outside that view stayed
    // culled exactly as the base game leaves them. BaseAi.s_ManualFrustumCullingEnabled and the
    // imposter distances stay at the game's own values. Switching those off also works and is the
    // wrong fix: it makes every animal in the region render and simulate all the time.
    //
    // The method has ten native callers, so this very likely corrects more than wildlife - the
    // spawners decide where it is safe to spawn by asking the same kind of question. Which of
    // them route through here has not been established.
    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.Utils), nameof(Il2Cpp.Utils.PositionIsOnscreen), new System.Type[] { typeof(Vector3), typeof(float) })]
    public class ParkedCullEyePositionOnScreenPatch
    {
        public static void Prefix(ref Vector3 pos)
        {
            if (!SeamlessInteriorsMod.ParkedCullEyeInForce) return;
            pos += SeamlessInteriorsMod.ParkedCullEyeOffset;
        }
    }
}
