// HudMath — the pure math of the converted-canvas HUD transform.
//
// Converted ScreenSpaceCamera canvases are parked at a NEAR plane distance so
// world geometry can't occlude them (the mountains-clipping-the-chat bug),
// then scaled by (FOV ratio × virtual-distance ratio) so the angular size
// matches what the desktop HUD would look like. Pure C# — unit-tested in CI
// (tests/test_hudmath.cs).
namespace VRKoboldKare
{
    public static class HudMath
    {
        /// <summary>Plane distance for converted HUD canvases: only geometry
        /// closer than this can occlude them.</summary>
        public const float NearPlaneDistance = 15f;

        /// <summary>Minimum virtual distance (guard against divide-by-tiny).</summary>
        public const float MinVirtualDistance = 20f;

        /// <summary>Angular-size ratio between the VR projection and the
        /// desktop camera FOV. &gt;1 when the VR projection is wider.</summary>
        public static float FovRatio(float currentVFovDeg, float camFovDeg)
        {
            const double d2r = System.Math.PI / 180.0;
            double t1 = System.Math.Tan(System.Math.Max(1.0, currentVFovDeg) * 0.5 * d2r);
            double t2 = System.Math.Tan(System.Math.Max(1.0, camFovDeg) * 0.5 * d2r);
            return (float)(t1 / System.Math.Max(1e-6, t2));
        }

        /// <summary>Fraction of the headset viewport the UI should occupy:
        /// fits a HudWidth × HudHeight virtual-pixel panel inside the actual
        /// view. Clamped to (0.02, 1].</summary>
        public static float UiFitFraction(float hudWidthPx, float hudHeightPx,
            float viewWidthPx, float viewHeightPx)
        {
            if (viewWidthPx < 1f || viewHeightPx < 1f) return 1f;
            float fx = hudWidthPx / viewWidthPx;
            float fy = hudHeightPx / viewHeightPx;
            float f = System.Math.Min(fx, fy);
            return System.Math.Min(1f, System.Math.Max(0.02f, f));
        }

        /// <summary>
        /// Final canvas scaleFactor: base (Unity's own) × FOV ratio
        /// (clamped to [1, maxRatio]) × near/virtual distance ratio ×
        /// the UI fit fraction.
        /// </summary>
        public static float HudScale(float baseScale, float currentVFovDeg, float camFovDeg,
            float virtualDistance, float maxRatio)
        {
            float vd = virtualDistance < MinVirtualDistance ? MinVirtualDistance : virtualDistance;
            float ratio = FovRatio(currentVFovDeg, camFovDeg);
            if (ratio < 1f) ratio = 1f;
            if (ratio > maxRatio) ratio = maxRatio;
            return baseScale * ratio * (NearPlaneDistance / vd);
        }

        public static float HudScaleFit(float baseScale, float currentVFovDeg, float camFovDeg,
            float virtualDistance, float maxRatio, float hudW, float hudH, float viewW, float viewH)
        {
            return HudScale(baseScale, currentVFovDeg, camFovDeg, virtualDistance, maxRatio)
                   * UiFitFraction(hudW, hudH, viewW, viewH);
        }
    }
}
