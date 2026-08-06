# Building and testing

## What you need

- .NET SDK **8.0** ([dotnet.microsoft.com/download](https://dotnet.microsoft.com/download))
- Windows 10/11 to run it. Building works on Linux and macOS too — see below.

## Build and test

```bash
dotnet build Twinstall.sln -c Release
dotnet run --project src/Twinstall.Tests -c Release --no-build
```

The test runner prints `passed: N   failed: N` and exits non-zero on failure. That exit code is
the whole contract — there is no test adapter, no XML report and nothing to install.

## Building off Windows

`Twinstall.Core` and `Twinstall.Tests` target `net8.0` and build and run anywhere:

```bash
dotnet run --project src/Twinstall.Tests -c Release
```

`Twinstall.Platform` targets `net8.0-windows`. It still *compiles* on Linux or macOS because the
project sets `EnableWindowsTargeting`; it only runs on Windows.

If you have no .NET SDK but do have mono, the core and its tests compile there too — useful for
a quick check on a machine you don't want to install a SDK on:

```bash
cd src
mcs -target:library -out:/tmp/Twinstall.Core.dll Twinstall.Core/*.cs
mcs -out:/tmp/tests.exe -r:/tmp/Twinstall.Core.dll Twinstall.Tests/*.cs
mono /tmp/tests.exe
```

## Verification status

Being precise about this, because "it builds" and "it works" are different claims and the gap
between them is where the last round of bugs lived.

| Component | Compiled | Analysed | Unit-tested | Run on Windows |
|---|---|---|---|---|
| `Twinstall.Core` (7 files) | .NET SDK 8.0.129 + mono | **yes, clean** | **47 assertions, all passing** | not yet |
| `Twinstall.Tests` | .NET SDK 8.0.129 + mono | **yes, clean** | is the tests | not yet |
| `Twinstall.Platform` (4 files) | against **stub** BCL types only | partially — see below | nothing to unit-test; they are OS calls | **not yet** |
| MSIX packaging | not built | n/a | n/a | **not yet** |

### What the analysers actually said

Roslyn was run with `EnableNETAnalyzers` at `latest-recommended`. It found **12 warnings across
three rules** in the core and tests, all fixed at source rather than suppressed:

| Rule | Count | Where | Fix |
|---|---|---|---|
| CA1051 — do not declare visible instance fields | 10 | `Instance`, `AppConfig`, `RouteResult` | converted to auto-properties; `Instances` is get-only so it can't be swapped out |
| CA2249 — use `Contains` not `IndexOf` | 1 | `CommandLine.IsChildProcess` | `Contains(..., StringComparison.OrdinalIgnoreCase)` |
| CA1861 — constant array argument | 1 | test round-trip split | hoisted to a `static readonly` field |

`Twinstall.Platform` was then analysed the only way possible without NuGet: compiled against
hand-written stub `System.Drawing` and `System.Management` types. That found **three real
defects**, also fixed:

| Rule | Where | Why it mattered |
|---|---|---|
| CA1305 | `ProcessMap`, `Convert.ToInt32(mo["ProcessId"])` | locale-sensitive conversion of a WMI value; now `CultureInfo.InvariantCulture` |
| CA1806 ×2 | `WindowEnumerator`, both loops | `GetWindowThreadProcessId` returns 0 on failure and the `out` pid then means nothing. The return value is now checked, so a window we can't read is skipped rather than attributed to a fabricated process id |

That last one is the kind of thing this exercise is for: it would have shown up as a window
occasionally routed to the wrong instance, which is exactly the bug the product exists to fix,
and it would have been very hard to reproduce deliberately.

### What is still unproven, and why

**The Platform analyser pass used fake BCL types.** Stub `Bitmap`, `Graphics`, `Font` and
`ManagementObjectSearcher` are enough to check control flow and P/Invoke usage, and the three
findings above are trustworthy because they depend only on the real P/Invoke declarations and
the real `System.Convert`. But every rule that reasons about the genuine types — disposal
tracking (CA2000), platform compatibility (CA1416), GDI+ handle lifetime — has never run.
`Twinstall.Platform` therefore keeps `TreatWarningsAsErrors=false`, on the same principle as
before: a red build for findings nobody has read is worse than no gate at all. **Read the first
Windows build, fix what it says, then delete that line from the .csproj.**

**The adapters have never executed.** `WindowEnumerator`, `ProcessMap` and `IconBadger` were
ported from PowerShell and C# that demonstrably worked in the Claude-specific tool, but this
exact code has not run once. The logic they *contain* is minimal by design — every decision was
pushed into `Twinstall.Core`, where it is tested. A wrong P/Invoke signature or a GDI+ call in
the wrong order would still get through everything above.

**Reproducing the stub analyser pass** is a temporary-project trick, not part of the build: a
throwaway `.csproj` targeting `net8.0` with `EnableDefaultCompileItems=false`, `Compile Include`
pointing at the Platform and Core sources plus a stubs file. Worth redoing if you change an
adapter and can't get to a Windows machine; not worth committing, because the stubs would drift.

## CI

`.github/workflows/build.yml`, three jobs:

- **windows** — presets validated, PowerShell linted, solution built, tests run, platform binaries
  published as an artifact.
- **core-portability** — builds and runs the same tests on `ubuntu-latest`. This exists to catch
  a Windows dependency creeping into the decision logic; see
  [ARCHITECTURE.md](ARCHITECTURE.md#repository-layout).
- **package** — tagged releases only. Packs the MSIX with `makeappx` and publishes a SHA-256.
  Store-distributed MSIX is signed by Microsoft; the hash is for people downloading from GitHub.

## Versioning

Set in `Directory.Build.props`. Tag releases `v0.2.0` to match — the `package` job triggers on
`refs/tags/v*`.
