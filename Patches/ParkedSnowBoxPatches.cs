using HarmonyLib;
using Il2Cpp;
using Il2CppTLD.WeatherParticle;
using UnityEngine;

namespace SeamlessInteriors
{
    // ─── THE GAME'S SNOW LIVES IN A BOX IN FRONT OF THE PLAYER'S EYE ───
    //
    // Falling and blowing snow are kept alive by WeatherParticleJob, which wraps every flake into a
    // box placed from WeatherParticleManager.m_Common: m_CamPos is the eye, m_CameraMat is a TRS at
    // the eye plus half the box depth along the view, with the camera's rotation, m_CameraInvMat is
    // its inverse, and the box (m_CameraBounds, 107.5 x 60.5 x 45 m measured in a blizzard) is local
    // to that matrix. So the snow follows the player's eye AND gaze.
    //
    // In a parked room the eye is ParkOffset (40 m then, 80 m since 2026-09-28) above the window cameras. Looking level the box
    // stopped about 30 m below the eye and the views had almost no snow; looking up there was
    // less, looking down more (owner, 2026-09-22: "Looking down, the snow gets thicker"). Snow
    // also fell INSIDE the parked room, which vanilla never has.
    //
    // THE FIX IS THE SAME AS FOR THE TREES: CORRECT THE EYE. While the stand-in eye is in force,
    // the box is moved by -ParkOffset just before each job is scheduled, which puts it in front of
    // the window cameras, facing where the player looks. Both snow systems share m_Common.
    //
    // It has to happen here. Rewriting m_Common from a LateUpdate did nothing at all: the game
    // refills it before the job reads it. A prefix on the two schedule methods (no parameters, not
    // inlined) fired and worked, tried in the console with the owner in the hangar in a blizzard:
    // "Looks like a full-on blizzard outside and no snow inside."
    //
    // Both methods run each frame and share one m_Common, so the shift is guarded: it is only
    // applied while m_CamPos still stands at the player's eye, and the second call finds it moved.
    internal static class ParkedSnowBoxShift
    {
        internal static void Apply(WeatherParticleManager manager)
        {
            if (!SeamlessInteriorsMod.ParkedCullEyeInForce || manager == null) return;

            Camera main = GameManager.GetMainCamera();
            if (main == null) return;

            CommonVars common = manager.m_Common;
            if ((common.m_CamPos - main.transform.position).sqrMagnitude > 1f) return;

            Vector3 d = -SeamlessInteriorsMod.ParkedCullEyeOffset;
            common.m_CamPos += d;
            Matrix4x4 m = common.m_CameraMat;
            m.m03 += d.x; m.m13 += d.y; m.m23 += d.z;
            // AND PUSHED FORWARD TO THE GLASS. The box runs 45 m from the eye along the gaze, and from
            // across the hangar that ended 0.3 m past the window (0927-11): no snow in the view until he
            // walked closer. A parked room shows no snow indoors, so the box starts at the nearest
            // window glass on screen instead (less 0.3 m). Owner, 2026-09-27, in the console: "Visible
            // now from the furthest I can get", and no popping seen on zoom or panning (0927-18).
            float push = SeamlessInteriorsMod.NearestWindowGlassAlongGaze(main);
            if (push > 0f)
            {
                Vector3 axis = new Vector3(m.m02, m.m12, m.m22).normalized;
                m.m03 += axis.x * push; m.m13 += axis.y * push; m.m23 += axis.z * push;
            }
            common.m_CameraMat = m;
            common.m_CameraInvMat = m.inverse;
            manager.m_Common = common;
        }
    }

    [HarmonyPatch(typeof(WeatherParticleManager), "ScheduleFallingSnowParticleJobs")]
    public class ParkedSnowBoxFallingPatch
    {
        public static void Prefix(WeatherParticleManager __instance) { ParkedSnowBoxShift.Apply(__instance); }
    }

    [HarmonyPatch(typeof(WeatherParticleManager), "ScheduleBlowingSnowParticleJobs")]
    public class ParkedSnowBoxBlowingPatch
    {
        public static void Prefix(WeatherParticleManager __instance) { ParkedSnowBoxShift.Apply(__instance); }
    }
}
