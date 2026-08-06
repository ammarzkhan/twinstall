# Automatic app detection

No per-app path tables. Everything below is derived at run time from the executable the user
points at, so an app we have never seen works as well as one we have.

---

## Step 0 — Resolve a launcher stub

The executable the user points at is often not the one that runs. Squirrel-packaged apps (Slack,
Discord) put a stub at the install root beside `Update.exe`, with the real binary in
`app-<version>\`. **The stub does not forward `--user-data-dir`**, so everything downstream
produces an accurate but useless "this app can't do profiles".

If `Update.exe` sits beside the target and `app-*` folders exist, redirect to the same executable
name inside the highest-versioned one. Compare versions **numerically** — an ordinal sort puts
`app-4.9.0` above `app-4.10.0`.

`LauncherStub.Resolve`. Measured: `%LOCALAPPDATA%\slack\slack.exe` fails the launch test after
the full 30s; `app-4.51.180\slack.exe` passes in 1.4s. Same application.

Do not generalise this to "the root exe has no markers, so look deeper" — VS Code's root
executable has no markers either and forwards the flag perfectly well. Only the Squirrel
signature triggers a redirect.

---

## Step 1 — Is this actually a Chromium/Electron app?

`--user-data-dir` is a Chromium flag. If the target isn't Chromium-based, profiles cannot work
and we must say so rather than produce a broken instance.

Look in the executable's own folder, `resources\`, **and one level of immediate subdirectories**:

| Marker | Meaning |
|---|---|
| `resources\app.asar` | Electron app bundle |
| `resources\electron.asar` | Electron runtime |
| `icudtl.dat` | Chromium ICU data |
| `chrome_100_percent.pak` / `chrome_200_percent.pak` | Chromium resource packs |
| `v8_context_snapshot.bin` | V8 snapshot |
| `LICENSES.chromium.html` | Chromium licence bundle |

Two or more hits ⇒ confident. Zero ⇒ refuse, and explain why.

This is what rules out new Teams automatically: it is WebView2, not Electron, so it fails here
without anyone needing to know that in advance.

**Why one level down, not just the exe's folder.** Measured on real installs, 7 Aug 2026:

| App | Where the markers actually are | Folder-only scan |
|---|---|---|
| Claude (Store) | beside `claude.exe` | 6 ✅ |
| VS Code | `<install>\e4c7e7b1d6\` — a commit-hash folder | **0 — refused a supported app** |
| Slack | `app-4.51.180\` (after step 0) | **0** |

Only one of three put them where the original scan looked.

**The cost of the sweep, stated plainly.** An app bundling a fixed-version WebView2 runtime in a
subfolder now scores like Electron, because that runtime ships the same Chromium files. This is
tolerable *only* because step 1 is not the last word — step 5 launches the app, and a WebView2
target ignores `--user-data-dir`. **Never let the marker check become the sole gate**, and never
"optimise" step 5 away for apps that score highly here.

---

## Step 2 — Where does its profile root live?

Derived from how the app is packaged, not from a table.

```
exe path contains \WindowsApps\<PackageFullName>\
    → PackageFamilyName = <name>_<publisherId>   (first and last segment of PackageFullName)
    → root = %LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Roaming

otherwise
    → root = %APPDATA%
```

That single rule covers the Store-vs-standalone split that cost us the most time on the
Claude-specific version. Packaged apps get `%APPDATA%` virtualised into `LocalCache\Roaming`;
unpackaged ones don't.

---

## Step 3 — Which folder in that root is the existing profile?

Scan one level deep for Chromium's own markers:

- `Local State` (definitive)
- `Default\Preferences`
- `Network\Cookies`

Rank candidates by:

1. folder name matching the exe's `ProductName`, `FileDescription` or internal name
2. most recently modified

One match ⇒ done. Several ⇒ show them and let the user confirm. None ⇒ the app has never run;
ask them to launch it once.

---

## Step 4 — What URL scheme does it use?

Also derived, not listed. Enumerate `HKCU\Software\Classes` and `HKLM\Software\Classes` for
keys that have a `URL Protocol` value, read each one's `shell\open\command`, and keep the ones
whose command references this executable — or, for packaged apps, its PackageFamilyName.

For MSIX targets, additionally read the package's own `windows.protocol` extensions through
`PackageManager`, since a packaged app's associations may not appear in `Software\Classes` at
all.

That discovers `slack://`, `discord://`, `claude://` and anything else with no hardcoded list.
If nothing is found, the app simply doesn't use callback sign-in and needs no routing — which
is a valid outcome, not a failure.

---

## Step 5 — Verify before committing

**Implemented.** `Twinstall.Core.LaunchProbe` holds the decisions and is unit-tested;
`Twinstall.Platform.LaunchProbeRunner` starts the process and polls. Never yet run against a
real application.

Detection is inference. Prove it:

1. Launch the app with `--user-data-dir=<new empty folder>`
2. Wait up to 30s for `Local State` to appear inside it
3. Appeared ⇒ the app honours the flag; record the profile and continue
4. Didn't ⇒ report honestly that this app doesn't support separate profiles, and change nothing

Without this step we would confidently create broken instances for WebView2 apps, apps with a
custom `userData` path, and anything that ignores the flag. This is the guard that turns a
heuristic into something safe to ship.

Three things the implementation adds that the four steps above don't say, each for a reason:

- **The probe directory is checked against the profiles already in use** (`ValidateTarget`,
  reusing `IsolationCheck`). This is the only detection step that launches the real application
  and writes to disk. Aimed at a live profile, the step meant to protect user data would be the
  thing that damaged it.
- **Creation is checked before the clock.** An app that writes `Local State` exactly as the
  window closes is credited, not failed.
- **Cleanup sweeps by probe token, not just the child process handle.** An app that ignores the
  flag typically re-parents onto the already-running instance, which our handle no longer
  covers. Since the token is a fresh GUID, anything carrying it on its command line is
  unambiguously ours. Left alone, a stray window looks exactly like the bug being tested for.

The probe genuinely starts the user's app, so a window will appear and then vanish. That is
inherent to the test, and the first-run UI should say so before running it rather than let it
be a surprise.

---

## What `apps.json` is still for

Only convenience: a starting list so common apps are pre-filled instead of the user hunting for
an executable. It carries no path knowledge, and detection runs identically for listed and
unlisted apps. An app can be removed from it with no functional change.
