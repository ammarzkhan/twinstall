# Changelog

Notable changes to Twinstall. Format follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

Nothing yet.

## [0.3.0] — 2026-08-07

First version that is an application rather than a library. Everything below has been run on
real Windows against real installations of Claude (Microsoft Store), Slack and VS Code.

### Added

- **`Twinstall.exe`** — one binary, four modes: URL routing, `--launch <instance>`,
  `--watch [seconds]`, `--compose`, and a management UI when started with no arguments. The
  routing path dispatches on `argv` before touching a WinForms type, so a sign-in callback
  doesn't pay for the UI.
- **Management UI** — pick an app from presets or Browse, run the full detection pass, name
  instances, choose badge colours, and see exactly what will change before it happens.
  Isolation is enforced as you type, not merely warned about.
- **Protocol registration** via `RegisteredApplications` — a ProgId plus a `UrlAssociations`
  capability, after which *you* pick Twinstall in Settings. It never writes the scheme key
  directly.
- **Badged taskbar icons** — the target app's own logo is extracted from its executable at up
  to 256px and composited with a coloured disc. No third-party artwork ships with Twinstall.
- **Start-menu shortcuts** per instance, since without them there is no way to open the second
  account at all.
- **Detection steps 0–5**, complete: launcher-stub resolution, Chromium confirmation, profile
  root derivation, profile discovery, scheme discovery, and a launch test that proves the app
  honours `--user-data-dir` before anything is committed to.
- `LICENSE` (MIT), `SECURITY.md`, `CHANGELOG.md`.

### Fixed

Each of these was found by running the code against real applications, not by review.

- **`ChromiumDetector` refused VS Code.** It only looked beside the executable; VS Code keeps
  every Chromium file in a commit-hash subfolder. It now sweeps one level of subdirectories.
- **Squirrel launcher stubs failed the launch test.** `%LOCALAPPDATA%\slack\slack.exe` does not
  forward `--user-data-dir`, so Slack was reported as unsupported. `LauncherStub.Resolve`
  redirects to `app-<version>\slack.exe`; the same launch then passes in 1.4s.
- **`ProcessMap` counted unrelated programs as running instances.** It matched on executable
  file name alone, so the Claude Code CLI — also `claude.exe`, different path — was reported as
  a running desktop instance. That is enough to turn "nothing is running, ask the user" into a
  confident "only one is running" and route a sign-in to an instance that was never there.
- **Scheme discovery found nothing for already-configured machines.** Keeping only registrations
  whose command references the app misses the case where something else already holds the
  scheme — which is the normal state of a machine set up before. Package-declared schemes are
  now kept regardless of the current holder.
- **`InternalName` was used to identify profiles.** VS Code's is literally `electron`, as is
  most of the ecosystem's; matching it would have selected an unrelated app's profile.
- **Every preset pointed at the wrong executable**, including the one marked verified.
- **`CA5392`** — no P/Invoke pinned its DLL search path, so a `user32.dll` placed beside the
  executable would have been preferred over the real one. All imports now pin System32.
- The launch probe left an empty `%TEMP%\Twinstall` behind.

### Known limits

- **Z-order routing is a heuristic.** Correct when you begin sign-in from the window you are in;
  wrong if you alt-tab mid-flow. Every decision is logged.
- **Per-window taskbar icons need "Combine taskbar buttons: Never"**, a system-wide Windows
  setting that only takes effect after a sign-out. It is offered as an explicit opt-in and is
  never changed silently.
- **Unsigned.** See [SECURITY.md](SECURITY.md) for why antivirus software may object and what
  we will never advise you to do about it.
- Not packaged for the Microsoft Store. Whether certification accepts an app declaring another
  vendor's URL scheme is still untested.

## [0.2.0] — 2026-08-06

### Added

- `Twinstall.Core` — decision logic with no OS dependency, targeting `net8.0` so a Windows call
  fails to compile. Path rules, package-name derivation, command-line parsing, Chromium
  detection, isolation checks, config parsing, route selection.
- `Twinstall.Platform` — thin Win32/WMI/GDI+ adapters holding no decisions.
- `Twinstall.Tests` — a console runner whose exit code is the result. No framework, no restore.
