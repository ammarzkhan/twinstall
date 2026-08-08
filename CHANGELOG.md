# Changelog

Notable changes to Twinstance. Format follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Fixed — the only way to undo a setup was hidden on PCs with several apps installed

"Remove Twinstance's changes" sat at the bottom of step 1, under the list of applications found
on the PC — and the length of that list is decided by the machine, not by the program. Twinstance
knows about twenty apps; a PC with five of them installed produced a page 502px tall inside a
448px panel, so the button was below the fold, reachable only by scrolling. Nothing else in the
window removes a setup once it is finished, and step 1 is the one screen nobody thinks to scroll:
everything it asks for is at the top.

**The button has moved into the footer**, on the left of the row that already holds Back and
Continue. The footer is measured from the bottom edge of the window, so the button is in the same
place whether the PC has one known app or twenty, and it cannot be pushed anywhere by the list
above it. It still appears only when there is a saved setup to remove. The status line beside it
gives up the width the button takes, and gets it all back on the screens where the button is
absent — every other step's footer is unchanged, to the pixel.

Two things follow from it no longer being page content:

- **Step 1 now fits with no scrollbar at all** at the default window size on a five-app machine,
  where before the scrollbar appeared purely because of this button.
- **The button no longer destroys itself mid-click.** It used to be rebuilt with the page, and
  removing a setup rebuilds the page — from inside the button's own `Click` handler, disposing
  the control still executing it. That is the exact hazard `ShowLater` was added for. As
  permanent chrome it is never disposed, so the deferral is not needed and the risk is gone.

## [0.4.0] — 2026-08-08

### Changed — the project is now called Twinstance

The previous name was Twinstall. `twinstall.com` belongs to an unrelated company, and the rest of
the first page of results was already spoken for by a Tripwire install script and a TWRP header
file. None of that is a trademark problem. But a tool aimed squarely at people who are not
comfortable installing software is badly served by a name they cannot successfully search for,
and the cost of changing it only ever goes up.

There are no functional changes in this release. Everything the old name touched moved with it,
and three of those are not cosmetic:

- The executable is `Twinstance.exe`, installed to `%LOCALAPPDATA%\Programs\Twinstance`.
- Settings live in `%LOCALAPPDATA%\Twinstance`.
- The protocol handler registers as `Twinstance.Url.<scheme>`, under a `Twinstance` entry in
  `RegisteredApplications`.

**Anyone already running 0.3.0 should uninstall it first** — Settings → Apps → Installed apps →
Twinstall → Uninstall — then install 0.4.0 and pick Twinstance again as the default handler.
The two builds cannot tidy up after each other: each cleans only the registry entries and
shortcuts bearing its own name, and by every identifier that matters, the old install is a
different program. Account profiles are unaffected. They belong to the app being duplicated, not
to this one, so existing sign-ins survive the change.

0.3.0 was published for a day and downloaded by nobody. Its release has been removed rather than
left on the releases page under a name that no longer refers to anything.

## [0.3.0] — 2026-08-07

*Released under the previous name, Twinstall. Withdrawn; superseded by 0.4.0.*


### Added — it is one file now, and it installs itself

Sharing it previously meant handing someone a zip, which they unpacked, then hunted for the
executable among a handful of DLLs, then ran from wherever it landed. That last part is not
cosmetic: shortcuts and the registered protocol handler both record an absolute path, so a copy
run from Downloads stops routing sign-ins the moment that folder is tidied up, with nothing to
say why.

- **Releases are single executables.** `Twinstance-<version>.exe` (~0.6 MB, needs the .NET 8
  Desktop Runtime) and `Twinstance-<version>-standalone.exe` (~147 MB, needs nothing). The
  presets are embedded, so a single file really is a single file.
- **First run offers to install.** It copies itself to `%LOCALAPPDATA%\Programs\Twinstance`, adds
  a Start-menu entry for itself, registers in Settings → Apps → Installed apps, and relaunches
  from there. Declining runs it in place, which is fine for a look.
- Neither build enables single-file compression. That gets the output quarantined mid-bundle —
  see [SECURITY.md](SECURITY.md).

### Fixed

- **Renaming an account left its old shortcut behind for ever.** Shortcuts were only ever added,
  never reconciled, so a Start menu accumulated one entry per name an account had ever had.
  Twinstance's own shortcuts are now cleared before writing the current set.

  They are identified by *where they point* — a target named `Twinstance.exe` with a `--launch`
  argument — not by name, so a desktop is never swept for a pattern. Matching on the full path
  was tried first and was wrong: shortcuts left by an earlier install location point at the old
  folder, which is precisely the stale case worth removing.

### Changed — the management UI

Rebuilt as a four-step flow instead of one page. The old window showed app selection, a raw
detection dump, an empty grid and the apply controls all at once, so the first thing a new user
saw was four things they could not do yet.

- **Only apps that are actually installed are offered.** A dropdown listing twenty apps, fifteen
  of which you do not have, is not a menu — it is a quiz. The list is now built by resolving
  every preset against this machine.
- **Results are in plain language**, as ticked statements — "Built on Chromium, so it supports
  separate profiles", "Sign-in links use slack:// — Twinstance can route those" — with the
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

- **One button that lands on the right page.**
  `ms-settings:defaultapps?registeredAppUser=Twinstance` opens Settings at
  *Apps → Default apps → Twinstance*, where the scheme is the only thing listed and
  "Choose a default" is one click away. No searching, and no instructions for navigating there
  by hand — a button that opens the page makes them noise.

  Three approaches were tried; the two failures are recorded so nobody repeats them:

  - **Opening a link of the scheme does nothing** while nothing is registered for it.
    `ShellExecute` returns without starting anything *and without raising an error*. Windows
    has a "how do you want to open this?" chooser for unknown file types, not for URL protocols.
  - **`IApplicationAssociationRegistrationUI::LaunchAdvancedAssociationUI`**, the API documented
    for precisely this, now shows a message box reading *"To change your default apps, go to
    Settings > Apps > Default apps"* and opens nothing. Deprecated in all but name.
  - Plain `ms-settings:defaultapps` lands on a page with two search boxes, where typing the
    scheme into the wrong one reports "We couldn't find anything to show here".

  Windows blocks programmatic changes to this setting deliberately — it is how browsers used to
  hijack one another — so no app can set it for the user. Being one click away is the ceiling,
  and that is what this now achieves.

- **A drawn walkthrough of the two remaining clicks**, as a schematic rather than a screenshot:
  a picture of the Settings app would be Microsoft's artwork inside our binary, which is the
  same thing we avoid for every other vendor.

- **A rollback offer when setup is abandoned.** Closing the window with the handler unset means
  leaving a machine that has been changed but does not work. It now offers to undo the
  shortcuts, icons, registry entries and — only if this run enabled it — the taskbar setting.
  Profile folders are never touched: they hold live sessions, and deleting an account someone
  has signed into because they closed a window would be indefensible.

- **A logo, and a colour of its own.** The mark is the same rounded tile twice, told apart by
  colour — which is the product in one shape. `assets/logo.svg` is the source;
  `scripts/make-logo.ps1` draws the identical geometry with GDI+ and emits the PNG set, a
  multi-size `.ico`, and the MSIX tile assets, so nothing is hand-drawn or unreproducible.

  The accent is **teal**, chosen against two constraints rather than taste. Twinstance's icon
  sits in the taskbar directly beside Slack, Discord, VS Code and Claude, and must not read as
  an official add-on for any of them — so aubergine, blurple and Microsoft blue were out. And
  the per-account badge colours are the signal this product exists to provide, so the app's own
  chrome has to stay clear of that palette instead of competing with it. Cyan was dropped from
  the badge palette for the same reason.

  This replaces following the Windows accent colour. That is the right behaviour for a system
  utility and the wrong one for something with a brand of its own.

  `assets/twinstance.ico` is the one exception to the repository's no-icons rule. That rule
  exists to keep *other vendors'* artwork out, and is unchanged for every other case.

- `Twinstance.exe --preview <1-5>` — opens one screen directly with representative data, so a
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

- `Twinstance.exe --presets` — traces every preset hint, what it expands to, and the actual
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

### Earlier in this release

First version that is an application rather than a library. Everything below has been run on
real Windows against real installations of Claude (Microsoft Store), Slack and VS Code.

### Added

- **`Twinstance.exe`** — one binary, four modes: URL routing, `--launch <instance>`,
  `--watch [seconds]`, `--compose`, and a management UI when started with no arguments. The
  routing path dispatches on `argv` before touching a WinForms type, so a sign-in callback
  doesn't pay for the UI.
- **Management UI** — pick an app from presets or Browse, run the full detection pass, name
  instances, choose badge colours, and see exactly what will change before it happens.
  Isolation is enforced as you type, not merely warned about.
- **Protocol registration** via `RegisteredApplications` — a ProgId plus a `UrlAssociations`
  capability, after which *you* pick Twinstance in Settings. It never writes the scheme key
  directly.
- **Badged taskbar icons** — the target app's own logo is extracted from its executable at up
  to 256px and composited with a coloured disc. No third-party artwork ships with Twinstance.
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
  file name alone, so a separate command-line tool — also `claude.exe`, different path — was reported as
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
- The launch probe left an empty `%TEMP%\Twinstance` behind.

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

- `Twinstance.Core` — decision logic with no OS dependency, targeting `net8.0` so a Windows call
  fails to compile. Path rules, package-name derivation, command-line parsing, Chromium
  detection, isolation checks, config parsing, route selection.
- `Twinstance.Platform` — thin Win32/WMI/GDI+ adapters holding no decisions.
- `Twinstance.Tests` — a console runner whose exit code is the result. No framework, no restore.