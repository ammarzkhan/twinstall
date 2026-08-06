# Twinstall — handoff

Written 6 August 2026, at the end of Stage 2. `CLAUDE.md` is the operational version an agent
reads; this is the narrative — why things are the way they are, and what I'd want to know if I
were picking this up cold.

---

## Where this came from

It started as a specific annoyance: two Claude Desktop instances, two accounts, and every
browser sign-in landing on the wrong one. That got solved properly — the working tool is in
`reference/claude-multi-instance/`, it runs on the author's machine, and it does the whole job:
separate profiles, correct callback routing, Chrome-style badged taskbar icons, a wizard
installer, an uninstaller registered in Add/Remove Programs.

The generalisation came from noticing that the problem isn't Claude's. Slack, Discord, VS Code
and Signal all have exactly the same shape: Chromium under the hood, `--user-data-dir` support,
a custom URL scheme, and one Windows handler slot. A tool that solves it for one app solves it
for all of them, and it stops trading on anyone else's brand in the process — which is what made
the Store path plausible at all.

## What was actually built

Stage 2 was scoped as "port the core, no new features," and that's what happened. The result is
a library, not an application:

- **`Twinstall.Core`** — seven files, zero OS calls. Path comparison, MSIX package-name
  derivation, command-line parsing, Chromium detection, isolation checks, config parsing, route
  selection. 47 assertions, all passing. Roslyn clean with warnings-as-errors on.
- **`Twinstall.Platform`** — four adapters over user32, WMI and GDI+. Written, analysed against
  stub types, **never executed**.
- **`Twinstall.Tests`** — a console app whose exit code is the result. No framework, no restore,
  runs under mono.

The split into two assemblies with different target frameworks is the one design decision worth
defending. `Core` targets `net8.0`, so a Windows API call there fails to compile; CI runs the
same tests on Ubuntu to catch it from the other side. That isn't tidiness — the first test run
failed 14 of 47 because `System.IO.Path` resolves `C:\...` against the working directory on
Linux, and the fix (implementing Windows path rules explicitly) turned out to be better on
Windows too, because `Path.GetFullPath` would otherwise convert a malformed config value into a
real path right underneath an isolation check.

## What is honestly not built

There is no application. No router, no launcher, no installer, no UI. The MSIX manifest points
at a `Twinstall.exe` that doesn't exist. Detection steps 3–5 — profile discovery, scheme
discovery, and the launch test that proves an app honours the profile before committing to it —
are documented but not written.

So: the engine exists and is tested. The car does not.

## The part that could go wrong quietly

`Twinstall.Platform` has never run. It was ported from code that demonstrably works, and the
adapters are deliberately thin, but "compiles" and "works" are different claims. The analyser
pass on it used hand-written stub `System.Drawing` and `System.Management` types, which was
enough to catch three real defects — a locale-sensitive `Convert.ToInt32` and, twice, an ignored
`GetWindowThreadProcessId` return value that could have attributed a window to a fabricated
process id. That last one would have shown up as an occasional window routed to the wrong
instance: exactly the bug the product exists to fix, and essentially impossible to reproduce on
demand. Worth remembering that a stub-based analyser run found it and 47 unit tests didn't.

Everything that needs the real BCL — disposal tracking, GDI+ handle lifetime, platform
compatibility — has still never been checked. That's why `Platform` keeps warnings non-fatal
while `Core` and `Tests` don't.

## The decision that gates everything

Whether Microsoft Store certification accepts an app declaring another vendor's URL scheme in
its manifest. Policy v7.19 is silent on it — not permitted, not prohibited, no guidance at all.
Both searches for precedent came up empty.

This is worth a day of waiting before it's worth a week of building. Submit a minimal MSIX with
one declared scheme and find out. If the answer is no, the fallbacks are real but worse:
user-registered handlers via `RegisteredApplications` (clearly compliant, slightly worse UX), or
a Store build without routing plus the router as a separate GitHub download (halves the value,
but ships).

## Suggested order of work

1. **Submit the minimal MSIX.** Everything downstream depends on the answer, and waiting is free.
2. **Finish detection**, launch test included. Without it the tool can commit to a profile the
   target app quietly ignores.
3. **Build the router**, porting `reference/router/ClaudeRouter.cs`.
4. **Build the launcher and icon application.** `IconBadger` already exists and is uncalled.
5. **Build the first-run UI**, gated by `IsolationCheck.Validate`.
6. **Package.** Manifest identity, assets, `makeappx` — the CI job is already written.

Steps 2–5 each end somewhere testable, which matters more than usual here because the failure
modes are timing-dependent and hard to reproduce.

## A parallel option worth considering

`reference/claude-multi-instance/` works today. Publishing *that* on GitHub is an afternoon:
a LICENSE, a `SECURITY.md` explaining the antivirus story honestly, SHA-256 checksums, and a
trademark disclaimer. It reaches the people who have this problem now, and it doesn't block
anything. The earlier assessment in `reference/Publishing-Assessment.md` covers why the Store
was the wrong home for the Claude-specific version and GitHub was the right one.

Twinstall is the better product. It is also several stages from existing, and those are
independent decisions.

## What I'd want a reviewer to check first

- That `Core` still has no OS dependency (the Ubuntu CI job is the guard, but read it too).
- That nothing in `Platform` grew an `if` about *which* instance — decisions belong in `Core`.
- That path matching is still exact-normalised everywhere. `work` and `work2` must stay distinct.
- That the router never logs a URL query string. Live OAuth codes pass through it.
