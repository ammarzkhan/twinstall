# Twinstall — project context

Read this before touching anything. It exists so you don't re-derive facts that were paid
for once already, and don't undo decisions that look arbitrary but aren't.

---

## What Twinstall is

A Windows tool that lets someone run **two accounts of the same Chromium/Electron desktop app
side by side** — Slack, Discord, VS Code, Signal, Claude — and have browser sign-in callbacks
land on the window they actually meant.

Two problems, both real, both solved in the reference implementation:

1. **The sign-in callback goes to the wrong instance.** These apps authenticate through the
   system browser and return via a custom URL scheme (`slack://`, `claude://`). Windows allows
   exactly one handler per scheme, and that handler launches the app with **no**
   `--user-data-dir`, which always resolves to the default profile. A second profile can
   therefore *never* receive a callback. Closing the first instance doesn't help — the callback
   just opens a fresh copy of the first profile. **This was proven, not inferred:** `Win32_Process`
   showed `Claude.exe "claude://login/google-auth?code=..."` with no `--user-data-dir`.
2. **You can't tell the windows apart.** Same exe, same AppUserModelID, so Windows draws
   identical taskbar buttons and merges them.

Twinstall takes over the scheme and dispatches by **Z-order** (the window highest in the stack is
the one you were last in, and that survives the browser stealing focus), and puts Chrome-style
coloured badges on the taskbar icons.

---

## Current state — read this carefully

**There is no application yet.** The only `Main()` is the test runner. The MSIX manifest declares
`Executable="Twinstall.exe"`, which does not exist and nothing builds.

What exists is a **library**, and it is good: 7 files of pure decision logic with 47 passing
tests, plus 4 thin Win32 adapters.

| | State |
|---|---|
| `src/Twinstall.Core` | ✅ written, analysed clean, **47/47 tests passing** |
| `src/Twinstall.Tests` | ✅ 47 assertions, console runner, exit code is the result |
| `src/Twinstall.Platform` | ⚠️ written, **never executed**, only analysed against stub BCL types |
| Router executable | ❌ does not exist |
| Launcher executable | ❌ does not exist |
| Installer / first-run UI | ❌ does not exist |
| Detection steps 3–5 | ❌ not implemented (see `docs/DETECTION.md`) |
| MSIX packaging | ❌ manifest is a sketch with `REPLACE` placeholders and no assets |

`reference/` holds the **working** Claude-specific version this is generalised from. It runs on
the author's machine today. Port from it; don't reinvent it.

---

## Architecture rules — do not break these

**1. `Twinstall.Core` targets `net8.0`, NOT `net8.0-windows`. Keep it that way.**
Everything in Core is a *decision*: path comparison, package-name derivation, command-line
parsing, Chromium detection, isolation checks, route selection. No Win32, no WMI, no GDI+, no
registry, no file I/O beyond reading a config. The target framework is what enforces it — a
Windows call there fails to compile. CI has a `core-portability` job that runs the whole test
suite on Ubuntu for the same reason. If you need an OS fact in a decision, **pass it in as a
parameter** (that's why `ChromiumDetector.Score` takes a `Func<string,bool> fileExists`).

**2. `Twinstall.Platform` holds no decisions.** It observes Windows and returns facts. If you
find yourself writing an `if` about *which instance* inside Platform, it belongs in Core where
it can be tested.

**3. `PathUtil` implements Windows path rules explicitly. Do not replace it with `System.IO.Path`.**
`Path.GetFullPath` resolves anything non-rooted against the current working directory, which
silently turns a malformed config value into a real path somewhere unexpected — directly
underneath an isolation check. It also makes the logic untestable off Windows. This was
discovered the hard way: 14 of 47 tests failed on the first run for exactly this reason.

**4. Matching is on exact normalised paths, never substrings.** `work` and `work2` must never be
conflated. There is a regression test named after it.

---

## Build and test

```bash
dotnet build Twinstall.sln -c Release
dotnet run --project src/Twinstall.Tests -c Release --no-build   # exit code is the result
```

Expect `passed: 47   failed: 0`. There is no test framework — the runner is a console app, so it
works with no restore and runs under mono too.

`Twinstall.Core` and `Twinstall.Tests` have **zero package references** and build offline.
`Twinstall.Platform` needs `System.Drawing.Common` and `System.Management` from NuGet.

**Warnings are errors in Core and Tests** — that was earned by reading the analyser output and
fixing all 12 findings. **Platform opts out**, deliberately: it has only ever been analysed
against stub BCL types, so disposal tracking (CA2000), platform compatibility (CA1416) and GDI+
handle rules have never run. Read the first real Windows build, fix what it says, then delete
`<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` from `Twinstall.Platform.csproj`.

---

## Facts that cost real time to establish

**MSIX profile virtualisation.** For Store-installed apps, `%APPDATA%` becomes
`%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Roaming`. The family name is derived
from the full name by taking the **first and last** underscore-delimited segments:
`Claude_1.24012.11.0_x64__pzs8sxrjxfjjc` → `Claude_pzs8sxrjxfjjc`. Implemented in
`PackagePaths.FamilyFromFullName`, with a test.

**Store-installed exes CAN be launched with arguments.** An early theory said `WindowsApps` ACLs
prevented passing `--user-data-dir` to a Store build. That is **wrong** — a plain `.cmd` doing
exactly that works. Don't build a portable-copy workaround for a problem that doesn't exist.

**Protocol registration: use `RegisteredApplications`, not `HKCU\Software\Classes`.** Writing the
scheme key directly does not win against an MSIX app's declared protocol, and it's the part of
Store policy 10.2.8 you'd fail. Registering a ProgId plus a `UrlAssociations` capability under
`RegisteredApplications`, then letting the user pick in Settings → Default apps, **does** work
and is the sanctioned method. Verified working on a real machine.

**`WM_SETICON` only sticks if it lands before Windows creates the taskbar button.** The launcher
must start the app and then apply the icon in a tight loop. Applying it after the window is
already on the taskbar silently does nothing. This is timing, not AppUserModelID — an early
theory blamed AUMID and was wrong.

**Per-window icons need `TaskbarGlomLevel = 2`** ("Combine taskbar buttons: Never"), and it only
takes effect after sign-out/in. This is a Windows settings change, so under Store policy 10.2.8
it **must be behind an explicit, clearly-labelled opt-in**, not written silently.

**Z-order is the only available signal.** The `anon_id` in an OAuth callback URL is generated
browser-side and matches no profile, so deterministic routing is impossible. Z-order is a
heuristic: right when you start sign-in from the window you're in, wrong if you alt-tab
mid-flow. Log every decision. Document the limit rather than letting users discover it.

**Antivirus.** Registering `powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass` as a
protocol handler gets blocked by Bitdefender's Advanced Threat Defense, and rightly — that's the
exact shape of a PowerShell hijack. The fix was to move PowerShell out of the protocol chain
entirely and use a compiled exe. Be honest in the docs: an unsigned exe in `%LOCALAPPDATA%`
registered as a handler is *also* a recognised malware pattern. It's narrower privilege, not
automatically fewer alerts. **Never** advise excluding `powershell.exe` from behavioural
monitoring.

**Claude's Cowork VM needs a zero-byte VHDX placeholder** in a second instance to suppress a
first-run error. Empirical, not documented behaviour. Keep it app-specific; don't generalise it.

**New Teams is WebView2, not Electron**, so it has no `--user-data-dir` and cannot be supported.
The Chromium marker check rejects it automatically — which is the point of detecting rather than
maintaining a table of known apps.

---

## What to build next — Stage 3

Order matters; each step is testable before the next.

**3a. Finish detection (`docs/DETECTION.md` steps 3–5).** Currently only steps 1–2 exist.
- Profile discovery: read the app's `Local State` / package layout to find the existing profile
- Scheme discovery: read the registry and, for packaged apps, the package manifest
- **The launch test** — start the target with a throwaway `--user-data-dir` and confirm it
  actually creates a profile there. This is the safety mechanism the whole detection design
  rests on; without it, Twinstall can commit to a profile the app ignores. Do not skip it.

**3b. `Twinstall.Router` — a new `net8.0-windows` WinExe.**
Modes: `--launch <instance>`, `--watch [seconds]`, and default `<scheme>://...` routing.
Wire the existing pieces: `InstanceConfig.Load` → `ProcessMap.Build` →
`WindowEnumerator.ProcessIdsTopDown` → `RouteDecision.Choose` → launch. Fall back to the default
profile if the config is missing or corrupt rather than stranding the user. Log every decision,
**never log URL query strings** (they contain live OAuth codes).
`reference/router/ClaudeRouter.cs` is the working version — port it.

**3c. Launcher + icon application.** Start the target on the right profile, then apply the badge
in a tight loop for ~60s so it lands before the taskbar button exists. `IconBadger.Compose` and
`.Apply` already exist and are uncalled.

**3d. First-run / management UI.** Pick an app (presets or Browse), name instances, choose
colours, show what will change on the system. `IsolationCheck.Validate` must gate this — refuse
identical or nested profile directories.

**3e. Then, and only then, packaging.** Fill in the manifest identity, produce the assets, wire
`makeappx` in CI (the workflow already has the job).

---

## Mistakes already made — don't repeat them

- Don't put placeholder text in commands you hand the user (`cd "<folder where you unzipped>"`).
  It gets pasted literally. Give self-locating commands.
- `Write-Host` output does not go to the pipeline, so `Tee-Object` captures nothing from the
  installer. Use `Start-Transcript`.
- `$args` is a reserved PowerShell automatic variable. Don't shadow it.
- `BinaryWriter.Write(byte[])` vs `Write(byte[], int, int)` — passing an `Object[]` silently
  writes 1 byte. Caught only by a byte-level test.
- Don't write runtime files without handling the case where the file is locked by a running
  process. Rename-and-degrade beats failing the install.
- When a theory about a failure doesn't pan out, **check before proposing the next one.** Three
  successive wrong diagnoses (AV quarantine, read-only attributes, ACLs) cost more time than
  reading the actual exception would have.

---

## The one open question that blocks the Store

**Will certification accept an app that declares another vendor's URL scheme in its manifest?**

Declaring `<uap3:Protocol Name="slack" />` is mechanically supported and is the sanctioned
alternative to registry hacking. But Store Policies v7.19 (effective 14 Oct 2025, re-checked
6 Aug 2026) contains **no text either way** about an app interoperating with another company's
application. It is neither permitted nor prohibited in writing.

**Resolve this before building the packaging work.** Submit a minimal MSIX declaring one scheme
and see whether it certifies. Fallbacks if the answer is no are in `docs/ARCHITECTURE.md`.

Also required for the Store, none of it done: privacy policy URL (**mandatory** for Win32
products under 10.5.1 regardless of what you collect), developer account, listing assets, age
rating, silent install path.

---

## Repo hygiene still missing

`LICENSE` (MIT or Apache-2.0), `SECURITY.md` (explain what the router does and why AV objects —
being first to say "yes, this looks like a PowerShell hijack, here's why it isn't" beats a user
discovering it), `CHANGELOG.md`, and a trademark disclaimer. Never commit a `.ico` or any
third-party logo: icons are composed at run time from the copy already on the user's machine,
and that is a deliberate IP decision, not an accident.
