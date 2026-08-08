# Store submission checklist

Ordered so the thing that can kill the project is first.

## 0. De-risk before building (do this first)

- [ ] Register a Partner Center individual account (~$19 one-off)
- [ ] Reserve the name **Twinstall**
- [ ] Build a minimal MSIX declaring **one** third-party protocol (see `AppxManifest.xml`)
- [ ] Submit it and record the certification outcome
- [ ] If rejected: fall back to user-initiated `RegisteredApplications` registration, or ship
      routing only via GitHub

Everything below is wasted effort if this step fails, which is why it is first.

## 1. Package

- [ ] MSIX, x64, `runFullTrust`, min Windows 10 19041
- [ ] All PE files signed (Microsoft signs the Store copy; sign your own for GitHub)
- [ ] Installs per-user, no admin prompt
- [ ] Silent install path exists and is tested
- [ ] Clean uninstall — no orphaned registry keys or profile folders

## 2. Policy compliance

- [ ] **10.1.1** — title is brandable, no descriptive text, no third-party product names in the title
- [ ] **11.2** — no third-party artwork anywhere in the package; icons read from the user's
      installed copy at run time
- [ ] **10.2.8** — no direct writes to `HKCU\Software\Classes\<scheme>`; protocol association
      via the manifest and the user's choice in Settings
- [ ] Taskbar setting change is opt-in with a plain-language explanation
- [ ] **10.2.2** — no dynamic code loading; the router ships pre-built, not compiled on device

## 3. Listing

- [ ] Privacy policy URL — mandatory for Win32/full-trust products
- [ ] Screenshots: badged taskbar, profile manager, first-run
- [ ] Short description avoiding "Slack", "Discord" etc. in the *title*; naming them in the
      description as supported apps is ordinary descriptive use
- [ ] Age rating questionnaire
- [ ] Support contact and a link to the GitHub issues page

## 4. Trademark hygiene

- [ ] Trademark search for "Twinstall" in classes 9 and 42 before launch. A quick web search
      found no collisions, but that is not clearance — Twinify, TwinUp and Twinit are all taken
      in the digital-twin space, so the neighbourhood is busy even though this exact word is free
- [ ] Disclaimer in listing and README: not affiliated with, endorsed by, or sponsored by any
      of the supported applications' vendors
- [ ] No vendor logos in store assets

## 5. Quality

- [ ] Every preset actually tested on a real install of that app — mark `verified: true` only
      after end-to-end testing
- [ ] Accessibility: keyboard navigation, screen-reader labels, contrast
- [ ] High-DPI at 100/150/200%
- [ ] Behaviour when the target app is missing, updated, or uninstalled
