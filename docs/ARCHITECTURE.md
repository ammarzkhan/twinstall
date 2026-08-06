# Twinstall — design and Store roadmap

> *twinstall* (verb) — to run a second, separately signed-in copy of a desktop app.

Generalises the Claude-specific tool into a profile launcher for any Chromium/Electron
desktop app, packaged as MSIX for the Microsoft Store.

---

## The problem, stated generally

Chromium-based desktop apps accept `--user-data-dir`, which gives a completely separate
profile: its own auth, settings, cache and local storage. Electron inherits this. So running
two accounts side by side is, on the surface, trivial.

Two things break in practice, and both are the actual product:

**1. The sign-in callback goes to the wrong instance.** Almost all of these apps authenticate
by opening the system browser and returning through a custom URL scheme — `slack://`,
`discord://`, `vscode://`, `claude://`. Windows allows exactly one handler per scheme, and that
handler launches the app with **no** `--user-data-dir`, which always resolves to the default
profile. A second profile can therefore never receive a callback, no matter which window
initiated it. Closing the first instance doesn't help — the callback simply starts a fresh copy
of the first profile.

**2. You can't tell the windows apart.** Same executable, same AppUserModelID, so Windows draws
identical taskbar buttons and merges them into one.

Twinstall solves both: a router that owns the scheme and dispatches to the instance you were
last using, and Chrome-style profile badges on the taskbar icons.

---

## What changes from the Claude-specific version

| Area | Now | Twinstall |
|---|---|---|
| Target app | Hardcoded Claude | Any Chromium/Electron exe the user points at |
| Discovery | `Get-AppxPackage -Name Claude` | Automatic: Chromium markers, package layout, `Local State` scan |
| Protocol | `claude://` only | Discovered from the registry / package manifest |
| Router | Compiled locally at install | **Pre-built and signed**, shipped in the package |
| Icon badge | Reads Claude's assets | Reads the target app's assets via `PackageManager` or the exe's own icon |
| Install | PowerShell wizard | MSIX with a WinForms management UI |
| Config | `instances.tsv` in `%LOCALAPPDATA%` | Same shape, under the package's local data |

The core logic — Z-order routing, exact-path isolation checks, `WM_SETICON` timing, the
ready-before-launch ordering — carries over unchanged. Those were the hard-won parts.

---

## Why MSIX changes the architecture

**Local compilation has to go.** Store submissions require every PE file to be signed by a
certificate chaining to a Microsoft Trusted Root CA. An executable generated on the user's
machine cannot be signed. The router must be pre-built and shipped inside the package.

Upside: Microsoft signs Store-distributed MSIX, so **the Store channel costs you no
certificate**. You only need your own certificate for direct GitHub downloads, and even that is
optional if you point people at the Store.

**Protocol handling becomes policy-compliant.** Today we write
`HKCU\Software\Classes\<scheme>\shell\open\command` directly, which is the part of policy 10.2.8
we'd fail on. MSIX declares protocols in the manifest instead; Windows then treats the app as a
candidate handler and the *user* chooses in Settings. That is the supported method the policy
asks for, and it removes the registry hack entirely.

**Taskbar settings must become opt-in.** Silently writing `TaskbarGlomLevel` is a Windows
setting change. It stays, but behind an explicit toggle with a plain-language explanation.

**Reading target-app artwork gets better, not worse.** Our current PowerShell shells out to
`Get-AppxPackage` because `WindowsApps` can't be enumerated. A full-trust MSIX app can call
`Windows.Management.Deployment.PackageManager` directly — cleaner and faster.

---

## The open risk I cannot resolve for you

**Will Store certification accept an app that declares other companies' URL schemes in its
manifest?**

Declaring `<uap:Protocol Name="slack" />` is mechanically supported and is the sanctioned
alternative to registry hacking. But an app claiming another vendor's scheme may still draw a
policy 11.2 or 10.1.1 objection, and I have found no published guidance either way.

**Do this before building the full product:** open a Partner Center support ticket describing
exactly this, or submit a minimal build declaring one scheme and see whether it certifies. A day
of waiting is much cheaper than discovering it after the app is finished.

Two fallbacks if the answer is no:

- **User-registered handlers.** Ship without protocol declarations; the app writes the
  association only when the user explicitly asks, via the documented `RegisteredApplications`
  capability route. Slightly worse UX, clearly compliant.
- **Store version without routing.** Ship profile launching and badging to the Store, and offer
  the router as a separate GitHub download. Halves the value, but ships.

---

## Detection model

**No per-app path tables.** Everything is derived at run time from the executable the user
points at — Chromium markers to confirm it supports profiles, package layout to find the
profile root, `Local State` to find the existing profile, the registry and package manifest to
find the URL scheme, and a launch test to prove it before committing.

Full method in [DETECTION.md](DETECTION.md). `presets/apps.json` is now only a convenience list
of executables to look for; it carries no path or scheme knowledge, and an unlisted app is
handled identically to a listed one.

This also removes the need to research each app: new Teams fails the Chromium check
automatically because it is WebView2, without anyone having to know that in advance.

---

## Repository layout

Three projects, split along one line: **can this code be decided, or must it be observed?**

```
Twinstall.sln
Directory.Build.props          shared build settings
src/
  Twinstall.Core/       net8.0           pure decision logic, no OS calls
    PathUtil.cs                          Windows path rules, implemented not inherited
    PackagePaths.cs                      MSIX family name, virtualised profile root
    CommandLine.cs                       --user-data-dir parsing, child-process detection
    ChromiumDetector.cs                  is this exe actually Chromium?
    IsolationCheck.cs                    would these two profiles collide?
    InstanceConfig.cs                    the instances.tsv format
    RouteDecision.cs                     which instance gets the callback
  Twinstall.Platform/   net8.0-windows   thin shells over OS APIs, no decisions
    NativeMethods.cs                     user32 P/Invoke
    WindowEnumerator.cs                  Z-order walk
    ProcessMap.cs                        WMI: running processes -> instances
    IconBadger.cs                        GDI+: compose badge, write .ico, WM_SETICON
  Twinstall.Tests/      net8.0           console runner, references Core only
  Twinstall.Package/                     MSIX manifest and assets
```

**Why the target frameworks differ, and why that matters.** `Twinstall.Core` targets `net8.0`,
not `net8.0-windows`. That is the boundary made mechanical: a Win32, WMI or GDI+ call added to
the core fails to compile rather than quietly making the decision logic untestable off Windows.
CI enforces it from the other side too — a `core-portability` job builds and runs the whole test
suite on `ubuntu-latest`. If that job ever goes red while the Windows job stays green, someone
has put an observation where a decision belongs.

This isn't theoretical tidiness. The first run of these tests failed 14 of 47 assertions purely
because `System.IO.Path` resolves `C:\...` against the working directory on Linux. The fix was
to implement Windows path rules explicitly in `PathUtil` — which is *better* on Windows too,
because `Path.GetFullPath` silently turns a malformed config value into a real path somewhere
unexpected, directly underneath an isolation check.

**Testing without a framework.** `Twinstall.Tests` is a console app whose exit code is the
result. It runs under `dotnet run` in CI with no restore, and under mono anywhere. 134 assertions
across path handling, package-name derivation, command-line parsing, Chromium detection, stub
resolution, isolation, config round-tripping, route selection, profile discovery, scheme matching
and the launch probe. The regressions worth keeping are named after the bugs they'd catch:
`work2 is NOT inside work`, `webview2 app rejected`, `z-order picks the most recently used
window`, `probing a live profile is refused`, `the exe's own folder alone scores nothing - this
was the bug`, `versions order numerically, not as strings`, `the name match wins over the most
recently modified`, `a scheme already taken over reads as foreign`, `the shared Electron internal
name identifies nothing`.

---

## Staged plan

**Stage 1 — de-risk (do this first, ~1 day)**
Minimal MSIX declaring one third-party scheme. Submit. Find out whether certification accepts
it. Everything else depends on the answer.

**Stage 2 — port the core — *done*, see [Repository layout](#repository-layout)**
Router, badging and isolation logic moved into C# projects with a unit-tested core and CI on
`windows-latest`. No new features. What was carried over is listed below; what remains unproven
is listed in [BUILDING.md](BUILDING.md#verification-status).

**Stage 3 — generalise (~3 days)**
Preset loading, detection per app, custom-app flow, profile management UI.

**Stage 4 — Store readiness (~2 days)**
Silent install path, privacy policy, listing assets, age rating, accessibility pass.

**Stage 5 — GitHub release in parallel**
Same codebase, self-signed or unsigned artifact, checksums, SECURITY.md.

---

## What carries the most risk

1. **Certification's view on third-party schemes.** Unknown, blocks the core feature. Test first.
2. **Detection accuracy.** Mitigated by the step-5 launch test: we never commit to a profile
   we haven't proven the app honours. The failure mode becomes "this app isn't supported",
   which is safe, rather than a silently broken instance.
3. **Z-order routing is a heuristic.** Right when you start sign-in from the window you're in;
   wrong if you alt-tab mid-flow. Acceptable, but should be documented in the listing rather
   than discovered.
4. **Antivirus.** Signing plus Store distribution should resolve what we hit, but an app that
   enumerates other processes' command lines will always look unusual to behavioural engines.
