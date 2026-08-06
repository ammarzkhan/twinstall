# Claude Multi-Instance Setup for Windows

Run two Claude Desktop instances side by side, signed into different accounts, with
**completely separate** settings, MCP servers, Cowork VMs and chat history — and with
browser sign-ins landing on the instance you actually meant.

Works with both Claude Desktop builds: **Microsoft Store (MSIX)** and **standalone
(Squirrel)**. Windows 10 and 11. No administrator rights.

---

## Run it

1. Install Claude Desktop and **launch it once** so it creates its profile.
2. Double-click **`INSTALL.cmd`** — a five-page wizard.
3. Do the two manual steps it prints at the end.

`VERIFY.cmd` checks an existing setup. `UNINSTALL.cmd` removes it — and the installer also
registers itself in **Settings → Apps → Installed apps** as *Claude Multi-Instance Setup*, so
you can uninstall it the normal Windows way too.

**Re-running is safe.** It overwrites its own files and registry entries, so it doubles as
the upgrade and repair path. Enter the *same instance name* as before and your existing
profile and sign-in are kept untouched.

---

## Antivirus

**The router is a small executable, compiled on your machine at install time.**

Earlier versions registered `powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass` as
the `claude://` handler. Bitdefender's Advanced Threat Defense blocks that at runtime, and it
is right to: a protocol handler that launches a hidden, policy-bypassing script host is the
exact shape of a PowerShell hijack.

So PowerShell is no longer in the protocol chain at all. The installer compiles
`ClaudeRouter.exe` from the C# source embedded in the setup script, using the C# compiler
that ships with the .NET Framework on every Windows machine. Nothing is downloaded.

This is genuinely narrower, not just quieter:

- A `.ps1` handler is a **generic script host**. Anything that can write to that file gets
  arbitrary code execution as you, every time you click a `claude://` link.
- `ClaudeRouter.exe` does four things and cannot be told to do a fifth: read a config file,
  list Claude processes, check window order, start `Claude.exe`.

Be clear-eyed about the tradeoff, though. An unsigned executable in `%LOCALAPPDATA%`
registered as a protocol handler is *also* a recognised malware pattern, so some engines may
object to the exe instead. The honest summary is: **narrower privilege, not automatically
fewer alerts.**

**If your AV still objects,** exclude the folder `%LOCALAPPDATA%\ClaudeRouter` in both places
(they are separate lists in Bitdefender):

| Where | What to add |
|---|---|
| Protection → Antivirus → Settings → Manage exceptions | Folder: `%LOCALAPPDATA%\ClaudeRouter` |
| Protection → Advanced Threat Defense → Settings → Manage exceptions | The same folder |

Add the **folder**, never `powershell.exe`. Excluding PowerShell from behavioural monitoring
is a real reduction in your machine's security.

**If the compiler is unavailable** on some machine, setup detects it, falls back to the
PowerShell router, and tells you in the log. It degrades rather than breaking.

### Sharing it with family

If the person you're giving this to can't evaluate the paragraphs above themselves, think
twice. A tool that might ask you to poke a hole in your antivirus is fine for someone who
understands exactly what the hole is, and not fine as a favour for a relative who will just
click Allow. For them, one instance plus a browser tab is a better answer.

Nothing here is code-signed. Signing would remove most of the friction, but it needs a real
certificate — a self-signed root would just move the trust problem somewhere worse.

---

## What isolation actually means here

Each instance gets its own `--user-data-dir`. Nothing is shared:

| | Shared? |
|---|---|
| Auth tokens / session | No |
| Settings, MCP servers | No |
| Cowork VM bundle | No |
| Chat history cache | No |
| Cookies / local storage | No |
| App binary and updater | Yes — one install serves both |

The installer **refuses to proceed** if the two profile folders would be the same, or
nested one inside the other. Matching is done on exact normalised paths, so a name like
`work` can never be confused with `work2`.

`VERIFY.cmd` re-checks this at any time.

---

## Why the router is necessary

Windows allows one handler for the `claude://` scheme, and that handler launches Claude with
**no** `--user-data-dir` — which always resolves to the default profile. A second profile can
therefore **never** receive an OAuth callback, no matter which window you started from.
Closing the first instance doesn't help: the callback just spawns a fresh copy of the *first*
profile.

The router takes over that handler and dispatches deliberately:

1. **Z-order** — the Claude window higher in the window stack is the one you were last in,
   and that survives the browser taking focus. This is the case that fires in practice.
2. **Only one running** → that one.
3. **A colour-coded picker** if it's genuinely ambiguous.

If the manifest is ever missing or corrupt, the router falls back to the default profile
rather than stranding you.

---

## Launching the second instance

The installer creates **`Claude (<name>)`** shortcuts on your Desktop and in the Start menu,
carrying the tinted Claude logo — not a batch file. They point at `ClaudeRouter.exe --launch
<name>`, which creates the Cowork VM placeholder, starts Claude on the right profile, and
applies the taskbar icon as the window appears. No console window at any point.

`Refresh Claude icons` re-applies the icons to windows that are already open.

---

## Why the taskbar icon works the way it does

`WM_SETICON` only sticks if it lands **before** Windows creates the taskbar button. The
launcher exe starts Claude and then applies the icon in a tight loop for 60 seconds, so it is
always in place first. (The fallback batch launcher achieves the same thing with a
ready-marker handshake, since PowerShell needs a variable amount of time to warm up.)

Windows also merges both windows into one button unless *Combine taskbar buttons and hide
labels* is set to **Never**. The installer sets it; it applies after you sign out and in.

Windows opened *later* get the default icon, since icons are per-window. The optional
background watcher (on by default) handles that.

---

## Troubleshooting

**Sign-in went to the wrong instance**

```powershell
$l = "$env:LOCALAPPDATA\ClaudeRouter\router.log"
Remove-Item $l -EA 0; Start-Process 'claude://'; Start-Sleep 3; Get-Content $l -Tail 3
```

- `-> second (via z-order …)` — working.
- No log at all — Windows is bypassing the router. Settings → Apps → Default apps, type
  `claude` in the **top** box, and pick the entry badged *New* — **ClaudeRouter** normally,
  or **Windows PowerShell** if setup had to fall back. Then Set default.
- Log shows an error — read it; the router logs exceptions rather than failing silently.

**Taskbar icon didn't change**

```powershell
Get-Content "$env:LOCALAPPDATA\ClaudeRouter\icon.log" -Tail 20
```

- `applied to hwnd …` but no visual change → the button already existed. Launch via the
  desktop `.cmd`, not any other route.
- Both windows share one button → taskbar combining still on. See above.
- Nothing logged → the watcher never started, or AV blocked it.

**Claude updated and something broke.** Everything resolves `Claude.exe` at run time, so
updates are handled. An update can re-claim the `claude://` association — just re-run
`INSTALL.cmd`, it's safe to run repeatedly.

---

## Command line

```powershell
.\Setup-ClaudeMultiInstance.ps1                                  # wizard
.\Setup-ClaudeMultiInstance.ps1 -Silent -InstanceName work -Color '#059669'
.\Setup-ClaudeMultiInstance.ps1 -Silent -TintMain -AlwaysOnWatcher
.\Setup-ClaudeMultiInstance.ps1 -Verify
.\Setup-ClaudeMultiInstance.ps1 -Uninstall
```

A third instance: run the wizard again with a different name, then add it to
`%LOCALAPPDATA%\ClaudeRouter\instances.json` **and** `instances.tsv`. The router handles any number.

---

## Known limits

- **Z-order is a heuristic**, not a guarantee. It's right when you start the sign-in from the
  window you're in; alt-tab mid-flow and it will guess wrong. The log always says what it chose.
- **`UserChoice` stays empty.** The association rides on the RegisteredApplications
  capability. A Windows feature update could disturb it — `VERIFY.cmd` is the three-second check.
- **The Cowork VHDX placeholder** the launcher creates is a zero-byte file that suppresses a
  first-run error. Cowork works in the second instance despite it, but that guard is
  empirical, not documented behaviour.
- **Nothing is code-signed** — see the Antivirus section.
- **The router exe is compiled locally**, so it differs byte-for-byte between machines. That
  is expected; it is built from the C# source inside `Setup-ClaudeMultiInstance.ps1`, which
  you can read before running anything.
