# Privacy

**Twinstance collects nothing, sends nothing, and has nowhere to send it to.**

It contains no network code at all. Not disabled, not opt-out — absent. There is no analytics, no
telemetry, no crash reporting, no update check, and no account to create. Nothing you do in
Twinstance is transmitted anywhere, because the program has no means of transmitting anything.

That is a checkable claim rather than a promise. The whole source is in this repository, and
neither `Twinstance.Core`, `Twinstance.Platform` nor `Twinstance.App` references `System.Net`,
`HttpClient`, sockets, or any HTTP library. The only two third-party packages are
`System.Drawing.Common` (for composing badged icons) and `System.Management` (for reading which
programs are running). Neither one talks to a network.

If your firewall ever shows Twinstance making a connection, that is a bug worth reporting — see
[SECURITY.md](SECURITY.md).

## What it stores, and where

Everything Twinstance writes lives under `%LOCALAPPDATA%\Twinstance` and in your own user section
of the registry. Nothing is written outside your Windows profile, and nothing needs an
administrator.

| Where | What |
|---|---|
| `%LOCALAPPDATA%\Twinstance\config.tsv` | the application you set up, its URL scheme, the names **you** chose for your accounts, their profile folder paths, badge colours |
| `%LOCALAPPDATA%\Twinstance\twinstance.log` | what it did and why — setup steps, and which account each sign-in link was routed to |
| `%LOCALAPPDATA%\Twinstance\icons\*.ico` | badged icons, composed at run time from the icon of the app already on your machine |
| `HKEY_CURRENT_USER` | the protocol handler registration, and the entry that makes Twinstance appear in Settings → Apps |

The account names are whatever you typed — *Work*, *Personal*, a person's name if that is what you
called it. They stay on your machine.

## Sign-in links are truncated before they are written

Routing a sign-in link is the one moment Twinstance handles something sensitive. The callback URL
your browser hands back carries a live authorisation code in its query string.

**That part never reaches the disk.** The URL is cut at the first `?` or `#` and recorded as
`scheme://host?<redacted>` — enough to see which link arrived and where it went, with the secret
removed before the line is written rather than after.

Twinstance never sees your password. Signing in happens between your browser and the
application's own servers, exactly as it would without Twinstance installed. Its only job is
deciding *which window* the result lands in.

## What it reads

To route a link correctly, Twinstance asks Windows which copies of the target application are
running and what command line each was started with — that is how it tells your Work window from
your Personal one, since the difference is the profile folder passed at startup.

This is read-only, it is limited to processes matching the application you configured, and the
command lines are used for the routing decision and then discarded. They are **not** written to
the log; only the account name and process id are.

## Your account data belongs to the app, not to us

The folders holding your sessions, messages and sign-ins are created by the application being
duplicated — Slack, Discord, Claude, whichever — in its own format, in its own location.
Twinstance points the application at them and never reads their contents.

Uninstalling Twinstance deliberately leaves those folders alone, and tells you where they are so
you can remove them yourself if you want to. Deleting someone's signed-in accounts as a side
effect of removing a launcher would be the wrong default.

## Removing everything

**Settings → Apps → Installed apps → Twinstance → Uninstall**, like any other program. That
removes the executable, the shortcuts, the badged icons, the registry entries, and the log.

Inside the app, **Remove Twinstance's changes** does the same without uninstalling.

## Children

Twinstance is a desktop utility with no accounts, no content and no network access. It is not
directed at children and collects no information from anyone, of any age.

## Questions

Open an issue on [the repository](https://github.com/ammarzkhan/twinstance/issues). For anything
security-sensitive, use **Security → Report a vulnerability** instead, as described in
[SECURITY.md](SECURITY.md).

*Last reviewed: 8 August 2026.*
