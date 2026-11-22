# VR-KoboldKare tests

Hand-rolled pure-C# test suites — no test framework, no Unity, no BepInEx, no
game install. Run them:

```bash
./run_tests.sh              # everything
./run_tests.sh xrabi math   # only the named suites
```

## The rules

- **Pure C# only**: a suite must compile with `mcs` referencing nothing but
  `System.*`. No `UnityEngine`, no `BepInEx`, no game assemblies. Anything that
  needs Unity types can't run in CI — extract the math into a pure helper in
  `src/` (see `src/AimConvention.cs`, `src/XrMath.cs`, `src/HudMath.cs`) and
  test that.
- **Self-contained**: one file per suite, `static class Test<Name>` with a
  `public static void Main()`. Exit code 1 = suite failed.
- **Assert, don't print-and-hope**: use the `Eq`/`Near`/`IsTrue` helpers from
  any existing suite.
- **Add the suite to `run_tests.sh`** — an orphaned test file runs for nobody.

## The suites

| Suite | What it guards |
|-------|----------------|
| `xrabi` | Every OpenXR struct size and `XR_TYPE_*` constant against the official `openxr.h`. Two shipped-game incidents made this permanent: the 55/56/57 constant drift (surface: `XR_ERROR_VALIDATION_FAILURE` on acquire) and the OpenXR 1.1 `XrFlags64` discovery (flag fields are 64-bit). |
| `math` | The aim convention (the workaround for the shipped `SetPlayerIntendedFacingDirection` bug — quaternion-components-as-degrees), the OpenXR→Unity pose conversion (`(x, y, -z, -w)` — the `w` sign is load-bearing), and the HUD scale transform (FOV ratio × virtual distance, occlusion-safe 15 m plane). |

## Historical context

The xrabi suite exists because OpenXR constants were hand-copied once and four
of them were wrong — silent at creation time, fatal at acquire time. The rule
since: **never trust memory for a spec constant; extend this suite from
`refs/openxr.h` and let CI hold the line.**
