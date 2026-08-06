# Twinstall

Run two accounts of the same desktop app side by side on Windows — and have the sign-in
actually land on the one you meant.

**Status: pre-release scaffold.** The decision logic is written and unit-tested; the Windows
adapters are written but have never run; nothing is packaged or shipped. See
[docs/BUILDING.md](docs/BUILDING.md#verification-status) for exactly what is and isn't proven.

---

## The problem

Chromium-based desktop apps — Slack, Discord, VS Code, Signal, Claude — accept
`--user-data-dir`, which gives you a completely separate profile. Two accounts side by side
looks trivial. Two things break it:

**Sign-in goes to the wrong window.** These apps authenticate through the system browser and
come back via a custom URL scheme (`slack://`, `discord://`). Windows allows exactly one handler
per scheme, and that handler launches the app with *no* `--user-data-dir` — which always resolves
to the default profile. Your second account can therefore never receive a callback, no matter
which window you started from. Closing the first instance doesn't help; the callback just opens
a fresh copy of the first profile.

**You can't tell the windows apart.** Same executable, same AppUserModelID, so Windows draws
identical taskbar buttons and merges them into one.

## What Twinstall does

Takes over the URL scheme and dispatches deliberately — to the window you were last using, by
Z-order, which survives the browser stealing focus — and puts a Chrome-style coloured badge on
each taskbar icon so you can see which is which.

No per-app configuration tables. Point it at an executable and it works out whether the app is
Chromium-based, where its profile lives, and which URL scheme it owns, at run time. See
[docs/DETECTION.md](docs/DETECTION.md).

## Design notes

- [ARCHITECTURE.md](docs/ARCHITECTURE.md) — how it works, why MSIX changes the shape of it,
  and the repository layout
- [DETECTION.md](docs/DETECTION.md) — the five-step method for identifying a target app
- [BUILDING.md](docs/BUILDING.md) — build, test, and an honest verification status
- [STORE-SUBMISSION.md](docs/STORE-SUBMISSION.md) — Microsoft Store checklist

## Known limits

- **Z-order routing is a heuristic.** Right when you start sign-in from the window you're in;
  wrong if you alt-tab mid-flow. The router logs what it chose, every time.
- **One open question blocks the Store route.** Whether certification accepts an app that
  declares another vendor's URL scheme in its manifest is undocumented either way. It needs
  testing with a minimal submission before anything else is worth building. Fallbacks are in
  [ARCHITECTURE.md](docs/ARCHITECTURE.md).
- **Nothing ships third-party artwork.** Icons are composed at run time from the copy of the app
  already installed on your machine. That's a deliberate decision, not an accident.

## Trademarks

Twinstall is not affiliated with, endorsed by, or sponsored by any of the applications it works
with. Product names are trademarks of their respective owners and are used only to describe
compatibility.
