// XrMath — the pure OpenXR→Unity coordinate conversion.
//
// OpenXR is right-handed with -Z forward; Unity is left-handed with +Z
// forward. The pose conversion is a conjugation by the Z-flip reflection
// D = diag(1,1,-1):
//   position: (x, y, -z)
//   rotation: (x, y, -z, -w)          ← the -w matters: (x, y, -z, +w) is the
//                                        INVERSE rotation (mirrored yaw AND
//                                        strafe — cost a full debug cycle).
// Pure C# — unit-tested in CI (tests/test_xrmath.cs).
namespace VRKoboldKare
{
    public static class XrMath
    {
        public static void ConvertPosition(float x, float y, float z,
            out float ox, out float oy, out float oz)
        {
            ox = x;
            oy = y;
            oz = -z;
        }

        public static void ConvertRotation(float x, float y, float z, float w,
            out float ox, out float oy, out float oz, out float ow)
        {
            ox = x;
            oy = y;
            oz = -z;
            ow = -w;
        }
    }
}
