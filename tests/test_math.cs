// Unit tests for the pure math helpers: the aim convention (the workaround for
// the shipped SetPlayerIntendedFacingDirection bug), the OpenXR→Unity pose
// conversion, and the HUD scale transform. Suite "math" via run_tests.sh.
//
// The euler→quaternion reconstruction below is a self-contained implementation
// of Unity's Quaternion.Euler(x, y, 0) = Y(yaw) · X(pitch) (no Unity needed),
// so the roundtrip is verified against the REAL convention, not a re-statement
// of it.
using System;
using VRKoboldKare;

internal static class TestMath
{
    private static int _failed;
    private static int _checked;

    private static void Near(double expect, double got, double tol, string what)
    {
        _checked++;
        if (Math.Abs(expect - got) > tol)
        {
            _failed++;
            Console.WriteLine("FAIL " + what + ": expected ~" + expect + " got " + got);
        }
    }

    private static void IsTrue(bool ok, string what)
    {
        _checked++;
        if (!ok) { _failed++; Console.WriteLine("FAIL " + what); }
    }

    // Quaternion.Euler(x, y, 0) = Qy(yaw) · Qx(pitch), Unity's Y·X order for zero roll.
    private static void EulerToQuat(double pitchDeg, double yawDeg,
        out double x, out double y, out double z, out double w)
    {
        double p = pitchDeg * Math.PI / 180.0, yw = yawDeg * Math.PI / 180.0;
        double cp = Math.Cos(p / 2), sp = Math.Sin(p / 2);
        double cy = Math.Cos(yw / 2), sy = Math.Sin(yw / 2);
        x = sp * cy; y = cp * sy; z = -sp * sy; w = cp * cy;
    }

    // Quaternion.Euler(-aim.y, aim.x, 0) — OrbitCamera.GetPlayerIntendedRotation.
    private static void AimToQuat(double aimX, double aimY,
        out double x, out double y, out double z, out double w)
    {
        EulerToQuat(-aimY, aimX, out x, out y, out z, out w);
    }

    public static void Main()
    {
        // ---- EulerToAim / AimToEuler roundtrip across the whole sphere ----
        double[] yaws = { 0, 1, 45, 90, 92, 170, 180, 184, 269, 270, 271, 315, 357, 359, 720.5, -90, -179 };
        double[] pitches = { -89, -55, -20, 0, 5, 22, 60, 89 };
        foreach (double yaw in yaws)
        {
            foreach (double pitch in pitches)
            {
                float yawF = (float)yaw;
                float pitchF = (float)pitch;
                AimConvention.EulerToAim(yawF, pitchF, out float ax, out float ay);
                AimConvention.AimToEuler(ax, ay, out float ry, out float rp);
                Near(yaw, ry, 1e-4, "aim yaw roundtrip yaw=" + yaw + " pitch=" + pitch);
                Near(pitch, rp, 1e-4, "aim pitch roundtrip yaw=" + yaw + " pitch=" + pitch);

                // The convention must reconstruct the same rotation:
                // Euler(-aim.y, aim.x, 0) == Euler(pitch, yaw, 0).
                AimToQuat(ax, ay, out double qx, out double qy, out double qz, out double qw);
                EulerToQuat(pitch, yaw, out double ex, out double ey, out double ez, out double ew);
                Near(ex, qx, 1e-6, "conv x yaw=" + yaw + " pitch=" + pitch);
                Near(ey, qy, 1e-6, "conv y yaw=" + yaw + " pitch=" + pitch);
                Near(ez, qz, 1e-6, "conv z yaw=" + yaw + " pitch=" + pitch);
                Near(ew, qw, 1e-6, "conv w yaw=" + yaw + " pitch=" + pitch);
            }
        }

        // The historical bug: quaternion components written as degrees collapse
        // the intent. Guard against re-introducing it: aim.y must equal -pitch,
        // never the quaternion's y component (sin(pitch/2)).
        AimConvention.EulerToAim(0, 60, out float _, out float sixtyPitchAimY);
        Near(-60, sixtyPitchAimY, 1e-9, "aim.y = -pitch (not sin(pitch/2)=0.5)");

        // Wrap180
        Near(0, AimConvention.Wrap180(0), 1e-9, "wrap 0");
        Near(-180, AimConvention.Wrap180(180), 1e-9, "wrap 180 → [-180,180) maps to -180");
        Near(-180, AimConvention.Wrap180(-180), 1e-9, "wrap -180");
        Near(-179, AimConvention.Wrap180(181), 1e-9, "wrap 181");
        Near(179, AimConvention.Wrap180(-181), 1e-9, "wrap -181");
        Near(45, AimConvention.Wrap180(405), 1e-9, "wrap 405");

        // ---- XrMath pose conversion ----
        // Position: (x, y, -z).
        XrMath.ConvertPosition(1f, 2f, 3f, out float px, out float py, out float pz);
        Near(1, px, 1e-5, "pos x");
        Near(2, py, 1e-5, "pos y");
        Near(-3, pz, 1e-5, "pos z");

        // Rotation: (x, y, -z, -w) — with w NOT negated the intent inverts
        // (mirrored yaw and strafe, the 2026-11-21 playtest bug).
        XrMath.ConvertRotation(0.1f, 0.2f, 0.3f, 0.9f, out float rx, out float ryy, out float rz, out float rw);
        Near(0.1, rx, 1e-5, "rot x");
        Near(0.2, ryy, 1e-5, "rot y");
        Near(-0.3, rz, 1e-5, "rot z");
        Near(-0.9, rw, 1e-5, "rot w");

        // Identity stays identity.
        XrMath.ConvertRotation(0f, 0f, 0f, 1f, out float ix, out float iy, out float iz, out float iw);
        Near(0, ix, 1e-5, "identity x");
        Near(0, iy, 1e-5, "identity y");
        Near(0, iz, 1e-5, "identity z");
        Near(-1, iw, 1e-5, "identity w (-1 ≡ identity quaternion)");

        // ---- HudMath ----
        // FOV ratio: VR 99° vertical vs desktop 60° → tan ratio.
        double ratio99_60 = HudMath.FovRatio(99, 60);
        Near(Math.Tan(49.5 * Math.PI / 180) / Math.Tan(30 * Math.PI / 180), ratio99_60, 1e-4, "fov ratio 99/60");
        Near(1, HudMath.FovRatio(60, 60), 1e-9, "fov ratio equal");

        // HUD scale: base × clamped-FOV-ratio × (15 / virtual distance).
        double scale = HudMath.HudScale(1f, 99, 60, 400, 4);
        Near(ratio99_60 * (15.0 / 400), scale, 1e-4, "hud scale base=1 vdist=400");
        Near(ratio99_60 * (15.0 / 100), HudMath.HudScale(1, 99, 60, 100, 4), 1e-4, "hud scale vdist=100");

        // Virtual distance clamps to ≥ 20 (no divide-by-tiny).
        Near(ratio99_60 * (15.0 / 20), HudMath.HudScale(1, 99, 60, 5, 4), 1e-4, "vdist clamp ≥ 20");

        // FOV ratio clamps to [1, 4]; the distance ratio always applies.
        Near(1 * (15.0 / 400), HudMath.HudScale(1, 30, 60, 400, 4), 1e-4, "ratio clamp low");
        Near(4 * (15.0 / 400), HudMath.HudScale(1, 179, 60, 400, 4), 1e-4, "ratio clamp high");

        // Base scale passes through multiplicatively.
        Near(2 * ratio99_60 * (15.0 / 400), HudMath.HudScale(2, 99, 60, 400, 4), 1e-4, "hud scale base=2");

        Console.WriteLine(_failed == 0
            ? "math: OK (" + _checked + " checks)"
            : "math: " + _failed + " FAILURES / " + _checked + " checks");
        if (_failed > 0) Environment.Exit(1);
    }
}
