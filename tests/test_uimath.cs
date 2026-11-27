// Unit tests for the pure UI math: the screenshot file-name generator.
// Suite "uimath" via run_tests.sh. (The options-tab slot-compaction helper
// was removed with its feature — the tab list wheel-scrolls at full size.)
using System;
using VRKoboldKare;

internal static class TestUiMath
{
    private static int _failed;
    private static int _checked;

    private static void IsTrue(bool ok, string what)
    {
        _checked++;
        if (!ok) { _failed++; Console.WriteLine("FAIL " + what); }
    }

    public static void Main()
    {
        // ---- screenshot file name: sortable, unique across a burst ----
        string a = UiMath.ScreenshotFileName(new DateTime(2026, 11, 27, 9, 5, 3, 42));
        IsTrue(a == "VRKK_20261127_090503_042.jpg", "file name format: " + a);
        string b = UiMath.ScreenshotFileName(new DateTime(2026, 1, 2, 23, 59, 59, 999));
        IsTrue(b == "VRKK_20260102_235959_999.jpg", "zero-padded month/day: " + b);
        IsTrue(a.EndsWith(".jpg", StringComparison.Ordinal), "jpg extension (EncodeToPNG is stripped from this game build)");
        IsTrue(UiMath.ScreenshotFileName(new DateTime(2026, 11, 27, 9, 5, 3, 43)) != a, "millisecond suffix separates bursts");

        Console.WriteLine("uimath: " + _checked + " checks, " + _failed + " failures");
        if (_failed > 0) Environment.Exit(1);
    }
}
