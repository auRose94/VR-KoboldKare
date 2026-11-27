// VR-KoboldKare — pure UI/layout math (no Unity references — CI-testable with
// plain mono, same contract as AimConvention/HudMath/XrMath).
//
//   ScreenshotFileName — timestamped screenshot file name (stable, sortable).
//
//   (The options-tab slot-compaction helper that lived here was removed with
//   the feature: the tab list wheel-scrolls at full size now, and the
//   compaction was additionally fed by a coordinate-space bug — anchored-space
//   Ys compared against pivot-local-space limits. Lesson lives in the
//   changelog; the scroll design means there is no compaction to test.)
//
using System;

namespace VRKoboldKare
{
    public static class UiMath
    {
        /// <summary>Screenshot file name: VRKK_20261127_153000_123.jpg — local
        /// time, sortable, millisecond suffix so bursts never collide. JPG, not
        /// PNG: this game's Unity build stripped EncodeToPNG entirely (verified
        /// against the Managed assemblies); EncodeToJPG at 95 is visually
        /// lossless for debugging.</summary>
        public static string ScreenshotFileName(DateTime localTime)
        {
            return string.Format("VRKK_{0}{1}{2}_{3}{4}{5}_{6:000}.jpg",
                localTime.Year, localTime.Month.ToString("00"), localTime.Day.ToString("00"),
                localTime.Hour.ToString("00"), localTime.Minute.ToString("00"),
                localTime.Second.ToString("00"), localTime.Millisecond);
        }
    }
}
