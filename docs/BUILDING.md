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

## Releasing

```bash
pwsh -File scripts/publish.ps1
```

Runs the tests first and refuses to publish if they fail, then writes two single-file executables and a `SHA256SUMS.txt` into `artifacts/`:

| Artifact | Size | Needs |
|---|---|---|
| `Twinstall-<v>.exe` | ~0.6 MB | .NET 8 **Desktop** Runtime on the target machine |
| `Twinstall-<v>-standalone.exe` | ~147 MB | nothing |

Prefer the small one where you can. It is not just smaller — a handful of ordinary managed DLLs
beside a normal apphost is the least unusual thing you can hand an antivirus engine.

### Never enable single-file compression

`EnableCompressionInSingleFile` is pinned **off** in `Twinstall.App.csproj`, and this is not a
size preference. Measured on 7 August 2026, on a machine running Bitdefender, Defender and
Malwarebytes:

| Layout | Size | Outcome |
|---|---|---|
| Framework-dependent | 0.7 MB | ✅ builds, survives, runs |
| Self-contained folder | 160 MB | ✅ builds, survives, runs |
| Single-file, uncompressed | 147 MB | ✅ builds, survives, runs |
| **Single-file, compressed** | 65 MB | ❌ **quarantined mid-build, 4/4 attempts** |

A compressed payload embedded in an executable and expanded at run time is the structure of a
packer, which is what malware uses to defeat static analysis. Twinstall is already an unsigned
protocol handler that reads other processes' command lines; it does not need to look packed too.

**If a publish fails with `GenerateBundle` and `UnauthorizedAccessException`, check your AV logs
before assuming a file lock.** That was the first theory here and it was wrong — the file's ACLs,
attributes and open handles were all normal, which is what pointed at disinfection instead.
`scripts/publish.ps1` now throws with that hint rather than shipping a half-written artifact.

### Signing

Releases are currently unsigned, and that is the single biggest obstacle to a non-technical
person installing this: every download shows *"Windows protected your PC"*.

**A `.pfx` file in a CI secret is no longer possible.** Since June 2023 the CA/Browser Forum has
required the private key of every publicly-trusted code-signing certificate to live on
FIPS-140-2 Level 2 hardware — an HSM, a USB token, or a cloud signing service. Any guide that
tells you to base64 a `.pfx` into a secret predates that and cannot be followed today.

That leaves three realistic routes:

| Route | Cost | Hardware | CI |
|---|---|---|---|
| **SignPath Foundation** | free for OSS | none | official GitHub Action |
| **Azure Trusted Signing** | ~$10/month | none | official GitHub Action |
| OV certificate + token | ~$200–400/year | USB token | awkward; the token must be plugged into a machine |

**SignPath Foundation is the natural fit here** — this is MIT-licensed open source built in
public CI, which is exactly what the programme exists for. Azure Trusted Signing is the fallback
if the project ever stops qualifying; note it validates identity and, for individuals, wants
around three years of verifiable history.

A self-signed certificate is **not** a route. It removes nothing: SmartScreen trusts a chain to a
root Windows already trusts, and a self-signed one is not that. It only helps where you can
install the certificate on every target machine yourself.

**What signing does and does not fix:**

- ✅ Removes "Unknown publisher" and shows a real name in the UAC/SmartScreen dialog
- ⚠️ SmartScreen reputation still accrues per-certificate. An OV certificate may keep warning
  until enough people download it; only EV certificates get instant reputation
- ❌ Changes nothing about behavioural antivirus detection. Registering a protocol handler and
  reading other processes' command lines look the same whoever signed the binary

The workflow already has the step, skipped behind the `SIGNING_ENABLED` repository variable.
**One ordering detail is easy to get wrong:** signing rewrites the executable, so any checksum
taken before it is invalid. `publish.ps1` writes `SHA256SUMS.txt` at the end of its own run —
which is *before* signing — so CI regenerates it afterwards. A published checksum that doesn't
match the download is worse than none: it tells a careful user the file was tampered with.

## Verification status

Being precise about this, because "it builds" and "it works" are different claims and the gap
between them is where the last round of bugs lived.

| Component | Compiled | Analysed | Unit-tested | Run on Windows |
|---|---|---|---|---|
| `Twinstall.Core` (11 files) | .NET SDK 8.0.423, real Windows | **yes, clean** | **155 assertions, all passing** | **yes — suite executed** |
| `Twinstall.Tests` | .NET SDK 8.0.423, real Windows | **yes, clean** | is the tests | **yes — `passed: 155  failed: 0`** |
| `Twinstall.Platform` (8 files) | **real BCL, real packages restored** | **yes, clean, warnings now fatal** | nothing to unit-test; they are OS calls | **yes — every adapter has run** |
| `Twinstall.App` (10 files) | real Windows | **yes, clean** | driven through Core | **yes — routes, launches, badges, UI opens** |
| MSIX packaging | not built | n/a | n/a | **not yet** |

All of this changed on 7 August 2026, when the code was first run on a real Windows machine
rather than inferred. Nothing is left in the "compiles but has never executed" category — but
note what that does and does not mean: the adapters have been exercised, not soak-tested, and
the UI has been driven by hand rather than by an automated harness.

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

Ten warnings remain at `latest-all` and are all deliberate: `CA1002` on the get-only
`AppConfig.Instances`, `CA1031` eight times where the documented behaviour is to degrade rather
than throw, `CA1303` on the test runner's own console output. Two of those `CA1031`s guard a
caller-supplied delegate, where narrowing the catch would mean guessing what someone else's
lambda throws. CI fails if the count rises above ten.

`Twinstall.App` also suppresses `CA1303`, `CA2213`, `CA2000` and `CA1308` in that project alone,
with reasons in its `.csproj`. Those are WinForms ownership false positives — a `Form` disposes
its `Controls`, and `Application.Run` disposes the `Form`. The trade-off to remember: a
disposable field that is **not** added to `Controls` won't be flagged there either.

**Careful when re-running that check.** `-p:AnalysisMode=All` alone does nothing, because
`AnalysisLevel` in `Directory.Build.props` re-derives the mode and overrides it — it reports a
clean build that is not clean. Use `-p:AnalysisLevel=latest-all`.

**Three of the seven adapters have still never executed:** `WindowEnumerator`, `IconBadger` and
`ProcessMap`. No live window enumerated, no icon composed, no WMI process map built. They were
ported from code that demonstrably worked in the Claude-specific tool, but this exact code has
not run once. Compiling against the real BCL is a stronger claim than it was; it is not the same
claim. The logic they *contain* is minimal by design — every decision was pushed into
`Twinstall.Core`, where it is tested. A wrong P/Invoke signature or a GDI+ call in the wrong
order would still get through everything above. **All three are needed by the router**, so 3b is
where they get their first real exercise.

`ProcessMap` is the least worrying of the three: `LaunchProbeRunner`'s straggler sweep drives the
same `ManagementObjectSearcher` pattern successfully on this machine, so the mechanism works even
though this particular query has not been run.

**`LaunchProbeRunner`, `ProfileScanner` and `SchemeScanner` have now run**, on 7 August 2026,
against Claude (Store/MSIX), Slack (Squirrel) and VS Code. Process start, marker polling,
process-tree kill, the WMI straggler sweep, directory cleanup, profile enumeration, version-
resource reads, the registry sweep and the package-manifest read all behaved correctly; two
already-running instances were left untouched throughout.

They earned their keep immediately, exposing five detection defects that the heuristics alone
would have shipped — see ENGINEERING.md's facts section. Timings, for reference when something later
feels slow: profile scan 12–25 ms, scheme scan 47–83 ms, launch probe 1.4–4.4 s.

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
