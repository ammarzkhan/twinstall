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
| `Twinstall.Core` (9 files) | .NET SDK 8.0.423, real Windows | **yes, clean** | **95 assertions, all passing** | **yes — suite executed** |
| `Twinstall.Tests` | .NET SDK 8.0.423, real Windows | **yes, clean** | is the tests | **yes — `passed: 95  failed: 0`** |
| `Twinstall.Platform` (5 files) | **real BCL, real packages restored** | **yes, clean, warnings now fatal** | nothing to unit-test; they are OS calls | **`LaunchProbeRunner` yes; the other four no** |
| MSIX packaging | not built | n/a | n/a | **not yet** |

The first three rows changed on 7 August 2026, when the suite was first run on a real Windows
machine rather than inferred. Nothing in `Twinstall.Platform` has still ever *executed*.

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

**~~The Platform analyser pass used fake BCL types.~~ Resolved 7 August 2026.** The real
packages have now been restored and the real rules have run. The specific fears turned out to be
unfounded: disposal tracking (CA2000), platform compatibility (CA1416) and the GDI+ handle rules
report nothing. `TreatWarningsAsErrors=false` is gone from the .csproj — warnings are fatal in
all three projects.

The stricter pass was not free of findings, though. Two were worth fixing:

| Rule | Where | Why it mattered |
|---|---|---|
| CA5392 ×7 | every P/Invoke in `NativeMethods` | no `DefaultDllImportSearchPaths`, so the loader searches the application directory before System32. A `user32.dll` dropped beside the exe would win. This exe is registered as a protocol handler and launched by Windows on a URL, which is not a shape where that should be left open |
| CA1307 | `ProcessMap`, `Replace` building the WMI query | culture-sensitive string comparison — same family as the CA1305 above |

Seven warnings remain at `latest-all` and are all deliberate: `CA1002` on the get-only
`AppConfig.Instances`, `CA1031` five times where the documented behaviour is to degrade rather
than throw, `CA1303` on the test runner's own console output. Two of those `CA1031`s guard a
caller-supplied delegate, where narrowing the catch would mean guessing what someone else's
lambda throws.

**Careful when re-running that check.** `-p:AnalysisMode=All` alone does nothing, because
`AnalysisLevel` in `Directory.Build.props` re-derives the mode and overrides it — it reports a
clean build that is not clean. Use `-p:AnalysisLevel=latest-all`.

**Three of the five adapters have still never executed.** `WindowEnumerator`, `ProcessMap` and
`IconBadger` were ported from code that demonstrably worked in the Claude-specific tool, but this
exact code has not run once: no live window enumerated, no WMI process map built, no icon
composed. Compiling against the real BCL is a stronger claim than it was; it is not the same
claim. The logic they *contain* is minimal by design — every decision was pushed into
`Twinstall.Core`, where it is tested. A wrong P/Invoke signature or a GDI+ call in the wrong
order would still get through everything above.

**`LaunchProbeRunner` has now run**, on 7 August 2026, against Claude (Store/MSIX), Slack
(Squirrel) and VS Code. Process start, marker polling, process-tree kill, the WMI straggler sweep
and directory cleanup all behaved correctly; two already-running instances were left untouched
throughout. It also earned its keep immediately by exposing three detection defects that the
marker heuristic alone would have shipped — see CLAUDE.md's facts section.

That leaves `ProcessMap` as the interesting untested one: it is the other WMI consumer, and the
straggler sweep in `LaunchProbeRunner` exercises the same `ManagementObjectSearcher` pattern
successfully, so the mechanism is at least known to work on this machine.

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
