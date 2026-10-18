# Viora Manager v1.4.1 — Account Shortcuts (development preview)

This adds **Accounts** to the existing, simple Viora Manager. The public standalone
v1.4.0 Manager release is unchanged until this separate preview is reviewed.

## What the feature actually does

- Click **Accounts** at the bottom of Viora Manager.
- Save a Roblox **username label**, your preferred **Edge or Chrome browser**,
  a separate **browser profile folder**, and an optional numeric Roblox **game place ID**
  (or the official `https://www.roblox.com/games/<id>` game URL).
- Click **Sign in / Roblox** to open `https://www.roblox.com/home` in that browser
  profile. Sign in using Roblox's actual website.
- Click **Open game** to open `https://www.roblox.com/games/<id>` in the same
  browser profile, then click **Play** on Roblox's website.
- Use different Edge/Chrome browser profile folders for different Roblox accounts,
  such as `Default`, `Profile 1`, `Profile 2`. These are browser **folder identifiers**,
  not the visible profile names in the browser. Find the correct folder in
  `edge://version` or `chrome://version` → **Profile Path**.
- Saved shortcuts can be edited or removed. Removal does not affect the real
  Roblox account, browser cookies or passwords.

## Important limitations

- Viora Manager never collects passwords, authentication cookies or Roblox tokens.
  Only shortcut metadata is saved, in
  `%LOCALAPPDATA%\Viora\Manager\accounts.json`.
  Before overwriting this file, the Manager saves an `accounts.json.bak` copy.
  If settings are corrupt/unreadable, the app opens the shortcut editor read-only
  to preserve the existing file rather than silently overwriting it.
- Selecting a username **does not log Roblox in as that user**. The current login
  is determined by the selected browser profile and official Roblox website.
  Users should check which Roblox account is displayed before clicking Play.
- Clicking **Open game** does **not** automatically enter a game or bind a desktop
  Roblox Player process to the selected username. It opens the correct official
  game page and the user presses Play.
- The separate experimental multi-instance mode is **not modified**. Roblox does
  not officially support simultaneous Player clients; additional Player instances
  may fail to start or the client may reuse an existing session. Browser profile
  isolation does not guarantee separate desktop Player instances.
- Only known Edge/Chrome profile folder labels can be selected; arbitrary command
  lines, URLs and executable paths are never accepted.

## Build and testing

A separate workflow, `.github/workflows/manager-account-shortcuts-preview.yml`,
publishes only a self-contained Windows x64 Manager EXE and SHA-256 artifact.
It does **not** tag or alter the previously published 1.4.0 Manager release.

Acceptance checks:
1. Windows 10/11 x64: manager starts; original multi-instance toggle, live
   game labels and window arrangement still work.
2. Save two shortcuts under distinct browser profiles, restart, verify persistence.
3. Both **Sign in / Roblox** actions open the correct Edge/Chrome profile
   and official Roblox website (sign-in occurs only on the Roblox site).
4. **Open game** opens the correct page for each saved game ID. Observe whether
   Roblox itself starts a separate Player; never promise guaranteed separation.
5. Test invalid usernames, duplicate usernames/profiles, malformed and
   non-Roblox URLs, no game ID, missing browser, edit/remove and settings recovery.
6. Confirm no credentials, cookies, token strings, or profile data are copied.
7. Only publish a v1.4.1 standalone Manager release after a successful Windows
   build and real-world account switching smoke tests.

