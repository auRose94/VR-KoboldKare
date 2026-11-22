// AimConvention — the pure math of OrbitCamera's intent encoding.
//
// OrbitCamera.GetPlayerIntendedRotation() = Quaternion.Euler(-aim.y, aim.x, 0),
// i.e. aim.x carries the YAW (degrees) and aim.y the NEGATED PITCH.
//
// WHY THIS EXISTS: the shipped OrbitCamera.SetPlayerIntendedFacingDirection has
// a bug — it writes the quaternion's y/x COMPONENTS into _aim as if they were
// Euler degrees (the euler local is computed and never used), collapsing the
// intent to ≈north for any other direction. VRKoboldKare drives _aim directly
// through this convention instead (VRManager.SetOrbitAim, VRPatches prefix).
//
// Pure C#: no Unity/BepInEx references — unit-tested in CI (tests/test_aim.cs).
namespace VRKoboldKare
{
    public static class AimConvention
    {
        /// <summary>Euler (yaw, pitch in degrees) → aim components for _aim.</summary>
        public static void EulerToAim(float eulerYawDeg, float eulerPitchDeg, out float aimX, out float aimY)
        {
            aimX = eulerYawDeg;
            aimY = -eulerPitchDeg;
        }

        /// <summary>Aim components → Euler (yaw, pitch in degrees).</summary>
        public static void AimToEuler(float aimX, float aimY, out float yawDeg, out float pitchDeg)
        {
            yawDeg = aimX;
            pitchDeg = -aimY;
        }

        /// <summary>Wrap an angle into [-180, 180) — OrbitCamera's unclamped-yaw wrap.</summary>
        public static float Wrap180(float deg)
        {
            deg %= 360f;
            if (deg >= 180f) deg -= 360f;
            if (deg < -180f) deg += 360f;
            return deg;
        }
    }
}
