# Changelog

Notable changes to Twinstall. Format follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Changed — the management UI

Rebuilt as a four-step flow instead of one page. The old window showed app selection, a raw
detection dump, an empty grid and the apply controls all at once, so the first thing a new user
saw was four things they could not do yet.

- **Only apps that are actually installed are offered.** A dropdown listing twenty apps, fifteen
  of which you do not have, is not a menu — it is a quiz. The list is now built by resolving
  every preset against this machine.
- **Results are in plain language**, as ticked statements — "Built on Chromium, so it supports
  separate profiles", "Sign-in links use slack:// — Twinstall can route those" — with the
  technical dump moved behind a *Technical details* toggle for when it is wanted.
- **Follows the system theme**, light or dark, including the title bar, and uses the user's own
  Windows accent colour rather than a hard-coded brand colour.
- Accounts are shown as rows with their badge colour, not a grey grid; the colour picker is
  swatches rather than a system colour dialog.
- Headings use the app's real name. `Path.GetFileNameWithoutExtension` produced "slack can do
  this"; it now prefers the preset's display name, then the binary's `ProductName`.

### Fixed — the last step could be skipped without noticing

The final screen said "You're set up" and offered **Finish** whether or not the scheme handler
had actually been chosen. Everything else can succeed — profiles, shortcuts, badged icons — and
the product still not do its job, because a sign-in will keep landing on the wrong account. The
one step that matters was the one the app was quietest about.

- The screen now checks whether Windows is really handing us the scheme, and says **"One step
  left"** with a red mark when it is not.
- It re-checks by itself whenever the window is activated, so coming back from Settings answers
  "did that work?" without being asked.
- **Finish** becomes **Finish anyway** and explains what will still be broken before closing.
- The check reads the **UserChoice** key, not just `HKCU\Software\Classes\<scheme>`. Choosing an
  app in Settings writes the former and leaves the latter alone, so the obvious check reports
  failure at the exact moment the user has just succeeded.

### Added

- **A one-click way to set the handler.** When nothing yet claims the scheme, Twinstall opens a
  harmless self-test link of its own, which makes *Windows* show its "How do you want to open
  this?" chooser with Twinstall in the list. That is far kinder than describing where to click.
  The link is recognised by the router, confirms success, and is never forwarded to the target
  application.
- **A drawn walkthrough for the Settings route**, because "open Settings and pick Twinstall" is
  not followable by a newcomer: that page has two search boxes, and typing the scheme into the
  wrong one silently finds nothing. The three steps now name which box, what to type, and what
  to click. Drawn as a schematic rather than screenshotted — a picture of the Settings app would
  be Microsoft's artwork inside our binary, which is the same thing we avoid for every other
  vendor.

- **A logo, and a colour of its own.** The mark is the same rounded tile twice, told apart by
  colour — which is the product in one shape. `assets/logo.svg` is the source;
  `scripts/make-logo.ps1` draws the identical geometry with GDI+ and emits the PNG set, a
  multi-size `.ico`, and the MSIX tile assets, so nothing is hand-drawn or unreproducible.

  The accent is **teal**, chosen against two constraints rather than taste. Twinstall's icon
  sits in the taskbar directly beside Slack, Discord, VS Code and Claude, and must not read as
  an official add-on for any of them — so aubergine, blurple and Microsoft blue were out. And
  the per-account badge colours are the signal this product exists to provide, so the app's own
  chrome has to stay clear of that palette instead of competing with it. Cyan was dropped from
  the badge palette for the same reason.

  This replaces following the Windows accent colour. That is the right behaviour for a system
  utility and the wrong one for something with a brand of its own.

  `assets/twinstall.ico` is the one exception to the repository's no-icons rule. That rule
  exists to keep *other vendors'* artwork out, and is unchanged for every other case.

- `Twinstall.exe --preview <1-5>` — opens one screen directly with representative data, so a
  layout change can be looked at without clicking through the flow. Seeds nothing, starts
  nothing, applies nothing.

### Fixed

- **Preset lookup failed for every Microsoft Store app.** A normal application cannot *list*
  `C:\Program Files\WindowsApps` — the ACL grants traverse but not read, so `Directory.Exists`
  returns true while `Directory.GetDirectories` throws `UnauthorizedAccessException`. The
  exception was being swallowed, so choosing "Claude" simply filled in nothing. Slack, under
  `%LOCALAPPDATA%`, worked fine, which is what made the failure look arbitrary.

  Install paths are now read from `MrtCache` under HKCU, which is readable without elevation,
  and each candidate is confirmed against disk because that key remembers uninstalled versions.

  Worth recording: an interactive shell *can* list `WindowsApps`, so testing from a terminal
  never reproduced this. Neither elevation nor process bitness was the cause — both were
  checked and ruled out. Only running from the application's own process showed it.

- **The UI failed silently.** A preset that resolved to nothing, "Check this app" with no
  application chosen, and "Add" before checking one all reported into a status label at the
  bottom of the window, so they read as "nothing happens when I click". Anything the user has
  to act on now says so in a dialog.

### Added

- `Twinstall.exe --presets` — traces every preset hint, what it expands to, and the actual
  exception when it finds nothing. Answers "why didn't it find my app?" without guesswork.
- Preset list grown from 5 apps to 20: Loom and ClickUp (both measured), plus Cursor, Obsidian,
  Notion, Figma, GitHub Desktop, 1Password, Bitwarden, Joplin, Element, Postman, Insomnia,
  Evernote and Superhuman (inferred layouts, never run here).
- Every preset now carries a `provenance` field — `measured` or `inferred` — so the difference
  between "we ran the probe against this" and "this follows a usual installer convention" is
  visible rather than implied.

- New `ProbeVerdict.LaunchBlocked`, for when Windows refuses to start the target at all. That
  is a different answer from `NotHonoured` — we did not learn that the app ignores
  `--user-data-dir`, we learned we could not ask — and collapsing the two would claim something
  unearned. Found on OpenAI Codex, a Store app whose executable denies `CreateProcess` by every
  route while Claude's, in the same folder with byte-identical ACLs, starts normally.

### Changed

- `apps.json` states explicitly that it is **not** a compatibility matrix. Presence does not mean
  supported and absence does not mean unsupported; only the step-5 launch test decides.

  Prompted by a circulated list of Electron apps that sorted them by concurrency mode and placed
  Claude and Loom under "single active account only". Both measure as `Honoured` here, and two
  Claude instances have run side by side on the development machine throughout. Such lists
  describe an app's own account-switching UI, not whether it honours `--user-data-dir` — which is
  a different question, and the reason this project exists.

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
