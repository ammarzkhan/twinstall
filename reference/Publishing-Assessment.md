# Publishing assessment: Microsoft Store and GitHub

Assessed against the Microsoft Store Policies (v7.19) and the MSI/EXE submission requirements,
fetched 4 Aug 2026. Sources at the end.

---

## Microsoft Store — verdict: no, and packaging is the least of it

You asked whether it needs to become an MSI. **Packaging is not the blocker.** The Store has
accepted plain `.msi` and `.exe` Win32 submissions for years, so that part is a day's work.
There are three real blockers, and the first one is fatal as the product currently stands.

### Blocker 1 — Intellectual property (fatal without permission)

Policy **10.1.1** is explicit: your product *"must not use a name, images, or any other metadata
that is the same as that of other products unless the product is also published by you"*, and
must not *"claim to be from a company if you don't have permission."*

Policy **11.2** requires that all content be *"originally created by you, appropriately licensed
from the third-party rights holder, used as permitted by the rights holder, or used as otherwise
permitted by law."*

As it stands the product:

- is named after Anthropic's product,
- displays an icon derived from Anthropic's logo,
- exists solely to alter how Anthropic's application behaves.

No packaging format fixes this. It needs either written permission from Anthropic, or the
product needs to stop trading on their brand. **This is the decision that determines everything
else**, so it's worth making before spending money on certificates.

### Blocker 2 — Code signing (hard requirement, real cost)

Every submitted binary **and all PE files inside it** must be signed by a certificate chaining
to a CA in the Microsoft Trusted Root Program.

That collides with the current architecture: `ClaudeRouter.exe` is **compiled on the user's
machine at install time**, so it cannot be signed. To comply, the exe would have to be
pre-built and signed, shipped inside the installer — which is a straightforward change, but it
gives up the "nothing binary is downloaded, you can read the source" property that currently
makes the AV story defensible.

Cost: roughly £200–400/year for an OV certificate, more for EV. Not optional for the Store, and
it would also solve most of your Bitdefender friction.

### Blocker 3 — System modification (arguable, needs care)

Policy **10.2.8** requires *supported methods and user consent* to change Windows settings.

- **Defensible:** registering under `RegisteredApplications` with a `UrlAssociations` capability
  and letting the user pick in Settings is the sanctioned path. That's what we do.
- **Not defensible:** directly overwriting `HKCU\Software\Classes\claude\shell\open\command` to
  seize an existing scheme, and silently writing `TaskbarGlomLevel`. Both would need to be
  removed or moved behind an explicit, clearly-labelled opt-in.

### Also required before submission

| Requirement | Status |
|---|---|
| Installer must run **silently**, no UI (UAC excepted) | We ship a wizard. `-Silent` exists, so this is easy |
| Standalone installer, no downloading during install | Already true |
| HTTPS, versioned, immutable download URL | Needs hosting (GitHub Releases works) |
| Privacy policy URL — mandatory for Win32 products | Needs writing; trivial, we collect nothing |
| Distinct value, quality bar, age rating, listing assets | Fine |
| Developer account | ~$19 one-off for an individual |

### What a Store-eligible version looks like

Rename away from "Claude". Ship your own icon. Generalise it to *any* Electron app with a
`--user-data-dir` — Slack, Discord, VS Code, Signal all have the same multi-account problem. Then
you're publishing a legitimate general-purpose profile launcher, the IP objection evaporates,
and the product is arguably more useful. Pre-build and sign the exe, add `/silent`, write a
privacy policy.

That's a real project — call it a few days plus the certificate. Worth it only if you want
distribution, not if you want two Claude windows.

---

## GitHub — verdict: yes, and it's the right home

Nothing above blocks a GitHub release. Referring to Claude by name to describe what the tool is
compatible with is ordinary descriptive use, and the repository contains **no Anthropic
artwork** — the icon is derived at install time from the copy already on the user's machine.
That's worth stating explicitly in the README as a deliberate design decision, because it is one.

### Before you publish

**Legal hygiene**

- `LICENSE` — MIT or Apache-2.0. Apache-2.0 if you want an explicit patent grant.
- Trademark line in the README: *"Not affiliated with, endorsed by, or sponsored by Anthropic.
  Claude is a trademark of Anthropic PBC."*
- Don't commit any `.ico` or logo file. Generate at install time, as now.

**Trust — this matters more than usual, because AV flags it**

- `SECURITY.md` explaining exactly what the router does, why antivirus objects, and what a
  reviewer should look at. Being first to say "yes, this looks like a PowerShell hijack, here's
  why it isn't" is far better than a user discovering it.
- A "what this changes on your system" section: every file path and registry key, and the
  uninstall path. You have this content already; make it prominent.
- Publish SHA-256 checksums with each release.
- Consider signing releases even for GitHub. Same certificate cost, and it removes most of the
  friction you hit — this is the single highest-value optional step.

**Engineering**

- CI: run PSScriptAnalyzer over the script and compile the embedded C# on a Windows runner.
  That would have caught several of the bugs we hit by hand.
- Tag releases; attach the zip as a release asset rather than committing it.
- Issue templates that ask for `router.log` and `icon.log` up front.
- `CHANGELOG.md`.

**Documentation**

Your README is already strong on the *why*. Add: a screenshot of the two badged taskbar icons,
the supported matrix (Store vs standalone build, Win10/11), and known limits — you have those.

---

## My recommendation

Publish on GitHub. Skip the Store unless you decide to generalise the tool and rename it.

The Store path costs a certificate, a rewrite of the icon and naming, and a rearchitecture to
pre-signed binaries — and at the end you would have a product whose entire premise depends on
another company not objecting. The GitHub path costs an afternoon of documentation, reaches
exactly the people who would want this, and keeps the "read the source, nothing is downloaded"
property that makes it trustworthy.

If you do want the Store, do the rename-and-generalise version. It's a better product anyway.

---

## Sources

- [Microsoft Store Policies (v7.19)](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)
- [App package requirements for MSI/EXE apps](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)
- [Trademark and copyright protection — Partner Center](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/trademark-and-copyright-protection)
- [How to distribute your Win32 app through the Microsoft Store](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-distribute-your-win32-app-through-microsoft-store)
- [A principled approach to app pinning and app defaults in Windows](https://blogs.windows.com/windowsexperience/2023/03/17/a-principled-approach-to-app-pinning-and-app-defaults-in-windows/)
