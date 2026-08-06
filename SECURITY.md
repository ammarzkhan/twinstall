# Security

## Reporting a vulnerability

Open a private security advisory through GitHub's **Security → Report a vulnerability** on this
repository. Please don't file a public issue for anything exploitable.

---

## Read this before your antivirus does

Twinstall does several things that look, from a behavioural-detection point of view, exactly
like malware. It is better that you hear it here than from a quarantine notification.

**It registers itself as a URL protocol handler.** An unsigned executable in `%LOCALAPPDATA%`
that claims `slack://` or `claude://` is a recognised hijack pattern, because that is precisely
how credential-stealing middleware inserts itself into a sign-in flow. Twinstall *is* middleware
in a sign-in flow. The difference is what it does with the link, not the shape of the mechanism.

**It reads other processes' command lines.** Matching a window to a profile means asking WMI for
`Win32_Process.CommandLine` across every process sharing the target's executable name. Process
enumeration of that kind is a standard reconnaissance step and behavioural engines score it.

**It sends `WM_SETICON` to windows belonging to other processes**, which is cross-process UI
manipulation.

**It starts another vendor's executable with arguments you didn't type.** Detection step 5
launches the target app against a throwaway profile to prove it honours `--user-data-dir`, then
kills it again.

All of that is the product working. None of it is hidden, and it is all in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/DETECTION.md](docs/DETECTION.md).

### What we will never tell you to do

**Never exclude `powershell.exe` from behavioural monitoring**, and be suspicious of any tool
that asks you to. An earlier Claude-specific version of this project registered
`powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass` as a protocol handler and was
blocked by Bitdefender's Advanced Threat Defense. **That block was correct.** The fix was to
remove PowerShell from the protocol chain entirely and ship a compiled executable instead — not
to suppress the detector.

The current design is narrower privilege, not automatically fewer alerts. If your AV objects,
the honest options are to inspect the source, build it yourself, or not run it.

---

## How Twinstall is built, and why it isn't packed

**Measured, 7 August 2026.** Publishing with .NET's `EnableCompressionInSingleFile=true`
produced a binary that Bitdefender quarantined **during the build itself**, mid-bundle, on four
consecutive attempts. The first diagnosis — a transient scanner file-lock — was wrong; the file's
ACLs and attributes were normal and nothing had it open. It was being disinfected.

The same publish with compression off succeeded, survived, and ran. So did the self-contained
folder layout and the framework-dependent one.

| Layout | Size | Result on a machine running Bitdefender, Defender and Malwarebytes |
|---|---|---|
| Framework-dependent | 0.7 MB | ✅ builds, survives, runs |
| Self-contained folder | 160 MB | ✅ builds, survives, runs |
| Single-file, uncompressed | 147 MB | ✅ builds, survives, runs |
| **Single-file, compressed** | 65 MB | ❌ **quarantined during the build, 4/4 attempts** |

The heuristic is not being unreasonable. A compressed payload embedded in an executable and
expanded at run time is the defining structure of a packer or dropper, and packing is what
malware does to defeat static analysis. Twinstall is already an unsigned protocol handler that
enumerates other processes' command lines. It does not need to also look packed.

**Releases therefore never enable compression.** The setting is pinned off in
[`Twinstall.App.csproj`](src/Twinstall.App/Twinstall.App.csproj) with the reasoning next to it,
and [`scripts/publish.ps1`](scripts/publish.ps1) fails loudly rather than shipping an artifact
that was quarantined out from under it.

## If your antivirus flags it anyway

It may. The binaries are **unsigned**, which means they carry no publisher reputation at all, and
SmartScreen will show "Windows protected your PC" for a new unsigned executable regardless of
what is inside it. Behavioural engines may also fire at the moment Twinstall registers itself as
a protocol handler, which no amount of build tuning will change — that is the product working.

Reasonable things to do:

1. **Check the hash.** Every release ships `SHA256SUMS.txt`. Verify the file you downloaded is
   the file that was published:
   ```
   Get-FileHash .\twinstall-0.3.0-win-x64.zip -Algorithm SHA256
   ```
2. **Build it yourself.** One command, no network beyond NuGet — see
   [docs/BUILDING.md](docs/BUILDING.md). A binary you compiled from source you can read is
   strictly better than one you trusted.
3. **Upload it to VirusTotal** and see whether it is one engine or twenty.
4. **Report the false positive** to the vendor that flagged it. Bitdefender, Malwarebytes and
   Microsoft all take submissions, and it is the only thing that fixes the problem for the next
   person rather than just for you.

And the thing not to do: **do not disable your antivirus, and do not add broad exclusions** — not
for `powershell.exe`, not for your whole downloads folder, not for a directory you will later
forget about. If you do not trust this binary enough to run it as-is, the correct response is to
build it from source or not run it, not to blind the thing that is protecting you.

## Code signing

The real fix is an Authenticode certificate, and Twinstall does not have one yet. Signing gives
the binary an identity, lets reputation accumulate across installs, and removes the SmartScreen
warning once it has. Until then, unsigned releases plus published hashes plus reproducible
source is the honest offer — not a claim that the warnings are wrong.

---

## What Twinstall touches

Everything is per-user. There is no service, no driver, no admin elevation, and nothing outside
your own profile.

| Location | What | Removed by |
|---|---|---|
| `%LOCALAPPDATA%\Twinstall\` | config, log, composed icons | "Remove Twinstall's changes" |
| `HKCU\Software\Classes\Twinstall.Url.<scheme>` | our own ProgId | same |
| `HKCU\Software\Twinstall\Capabilities` | UrlAssociations declaration | same |
| `HKCU\Software\RegisteredApplications` | one value naming the above | same |
| `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Twinstall\` | per-instance shortcuts | same |
| `HKCU\...\Explorer\Advanced\TaskbarGlomLevel` | **only if you tick the box** | set it back yourself |

Your profile folders and everything signed into them are never deleted by the uninstall path.
That is deliberate — those contain live sessions and are yours to remove.

### It does not write the scheme key directly

Twinstall registers a ProgId and a `UrlAssociations` capability under `RegisteredApplications`,
and then **you** choose it in Settings → Default apps. It never writes
`HKCU\Software\Classes\<scheme>\shell\open\command` behind your back. That shortcut is what the
predecessor tool did; it is a genuine hijack, it loses to a Store app's declared protocol anyway,
and it is the part of Microsoft Store policy 10.2.8 this would fail.

---

## Handling of secrets

**Sign-in callbacks carry live OAuth authorization codes in their query strings.**

The log at `%LOCALAPPDATA%\Twinstall\twinstall.log` records every routing decision, because a
link opening the wrong account leaves no other evidence. Every URL is written through a single
redaction function that strips the query and fragment before the string reaches disk:

```
2026-08-07 03:40:03  url: claude://twinstall/route-check?<redacted>
```

Twinstall never parses, stores, transmits or inspects the contents of a callback. It reads the
scheme to know the link is for it, and passes the URL through to the target application
unchanged. **There is no network code in this project at all.**

## Threat model, honestly

- **Routing is a heuristic.** Z-order picks the window you used most recently. It is right when
  you begin sign-in from the window you are in, and wrong if you alt-tab mid-flow. Every choice
  is logged, so a mis-route is diagnosable rather than mysterious.
- **A malicious app that already runs as you can do everything Twinstall does.** It grants no
  privilege that wasn't already available to any program in your user session.
- **Twinstall sits in an authentication path.** If you don't trust the binary, don't make it your
  default handler. Building from source takes one command; see
  [docs/BUILDING.md](docs/BUILDING.md).
