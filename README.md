# Twinstall

Run two accounts of the same desktop app side by side on Windows — and have the sign-in
actually land on the one you meant.

**Status: working, unsigned, not yet packaged.** Detection and routing have been run
end-to-end on real installations of Claude (Microsoft Store), Slack and VS Code. It is not on
the Microsoft Store and the binaries are not code-signed — please read
[SECURITY.md](SECURITY.md) before you run it, and [docs/BUILDING.md](docs/BUILDING.md) for
exactly what is and isn't proven.

---

## The problem

Chromium-based desktop apps — Slack, Discord, VS Code, Signal, Claude — accept
`--user-data-dir`, which gives you a completely separate profile. Two accounts side by side
looks trivial. Two things break it:

**Sign-in goes to the wrong window.** These apps authenticate through the system browser and
come back via a custom URL scheme (`slack://`, `claude://`). Windows allows exactly one handler
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
Chromium-based, where its profile lives, which URL scheme it owns, and — by actually launching
it once — whether it really honours separate profiles at all. See
[docs/DETECTION.md](docs/DETECTION.md).

---

## Getting started

### Install it

Download **one file** from [Releases](../../releases) and run it. It offers to install itself
into `%LOCALAPPDATA%\Programs\Twinstall`, adds a Start-menu entry, and appears in
Settings → Apps → Installed apps so it can be removed like anything else. No administrator
prompt; nothing is written outside your own user profile.

| File | Size | Needs |
|---|---|---|
| `Twinstall-<version>.exe` | ~0.6 MB | the [.NET 8 **Desktop** Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| `Twinstall-<version>-standalone.exe` | ~147 MB | nothing at all |

Take the small one unless you would rather not install a runtime. Both are a single file with
everything inside — there is no folder to keep, and nothing to unpack.

Choosing **No** at the install prompt runs it where it stands, which is fine for trying it out.
Bear in mind that shortcuts and the protocol handler record an absolute path, so a copy run from
Downloads stops working the moment that folder is tidied up.

Verify what you downloaded against `SHA256SUMS.txt`:

```bash
powershell -NoProfile -Command "Get-FileHash .\Twinstall-0.3.0.exe -Algorithm SHA256"
```

The binaries are **unsigned**. [SECURITY.md](SECURITY.md) explains what that means, why
antivirus software may object, and what we will never advise you to do about it.

### Build it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). From the repository
root:

```bash
dotnet build Twinstall.sln -c Release
```

Run the tests — the exit code is the result:

```bash
dotnet run --project src/Twinstall.Tests -c Release --no-build
```

Produce a self-contained folder you can copy to another machine, with no runtime to install:

```bash
dotnet publish src/Twinstall.App -c Release -r win-x64 --self-contained -o publish
```

### Set it up

Run `Twinstall.exe` with no arguments.

1. **Pick the application.** Choose a preset or Browse to its `.exe`. Presets are convenience
   only — an app that isn't listed works identically.
2. **Press "Check this app".** This runs the full detection pass, ending with a launch test that
   starts the app once against a throwaway profile to prove it honours `--user-data-dir`. A
   window will appear and close again. If the app doesn't support profiles, Twinstall says so
   and changes nothing.
3. **Add a second instance.** Name it, pick a badge colour. Twinstall refuses profile folders
   that are identical to, or nested inside, one another — that isolation is the entire basis for
   claiming the accounts stay separate.
4. **Press "Set up Twinstall".** It shows you everything it is about to change and waits for
   you to agree.
5. **Choose Twinstall in Settings.** Windows only lets *you* pick a default handler, so Settings
   opens at the end. Find the scheme and select Twinstall.

Then open your second account from its Start-menu shortcut.

### Command line

| Command | What it does |
|---|---|
| `Twinstall.exe` | management UI |
| `Twinstall.exe <scheme>://...` | route a link — this is what Windows invokes |
| `Twinstall.exe --launch "<name>"` | start one instance and badge it |
| `Twinstall.exe --watch [seconds]` | keep badging taskbar icons; `0` runs until killed |
| `Twinstall.exe --compose` | rebuild badged icons, e.g. after the target app updates |

### Removing it

"Remove Twinstall's changes" in the UI undoes the registry entries, shortcuts, icons and config.
**Your profile folders are deliberately left alone** — they hold live sessions, and deleting
them is your call. Everything Twinstall touches is listed in [SECURITY.md](SECURITY.md).

---

## Known limits

- **Z-order routing is a heuristic.** Right when you start sign-in from the window you're in;
  wrong if you alt-tab mid-flow. The router logs what it chose, every time.
- **Per-window taskbar icons need "Combine taskbar buttons: Never."** That's a system-wide
  Windows setting affecting every app, and it only takes effect after you sign out and back in.
  Twinstall offers it as an explicit opt-in and never changes it silently.
- **The binaries are unsigned**, and an unsigned protocol handler is a shape antivirus software
  is right to look at closely. [SECURITY.md](SECURITY.md) explains exactly why, and what we will
  never advise you to do about it.
- **New Teams cannot be supported.** It is WebView2, not Electron, so it has no
  `--user-data-dir`. Detection rejects it automatically rather than producing something broken.
- **Not on the Microsoft Store.** Whether certification accepts an app that declares another
  vendor's URL scheme is undocumented either way and still untested.

## Design notes

- [ARCHITECTURE.md](docs/ARCHITECTURE.md) — how it works, why MSIX changes the shape of it,
  and the repository layout
- [DETECTION.md](docs/DETECTION.md) — the method for identifying a target app, and what
  running it against real installs actually taught us
- [BUILDING.md](docs/BUILDING.md) — build, test, and an honest verification status
- [SECURITY.md](SECURITY.md) — what it touches, how callback URLs are handled, threat model
- [STORE-SUBMISSION.md](docs/STORE-SUBMISSION.md) — Microsoft Store checklist

## Contributing

Read [CLAUDE.md](CLAUDE.md) first. It records the decisions that look arbitrary but aren't, and
the mistakes already made, so they don't get made again. Two rules matter most:

- `Twinstall.Core` targets `net8.0`, not `net8.0-windows`, so an OS call there fails to compile.
  Decisions live there and are unit-tested; `Twinstall.Platform` only observes.
- Path matching is on exact normalised paths, never substrings. `work` and `work2` must stay
  distinct, and there's a regression test named after it.

## Licence

[MIT](LICENSE).

## Trademarks

Twinstall is not affiliated with, endorsed by, or sponsored by Anthropic, Slack Technologies,
Discord, Microsoft, Signal, or any other application vendor. Product names are trademarks of
their respective owners and are used only to describe compatibility.

**No third-party artwork ships with Twinstall.** Badged icons are composed at run time from the
copy of the application already installed on your machine. That is a deliberate decision, not an
accident — see [LogoExtractor](src/Twinstall.Platform/LogoExtractor.cs).
