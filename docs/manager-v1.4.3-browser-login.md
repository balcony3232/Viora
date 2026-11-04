# Viora Manager v1.4.3 — Roblox browser-login onboarding

## Guided Add Account flow

This version makes **Add an Account** on the Manager's Connected Accounts section open a separate, isolated Edge browser profile and navigate directly to **https://www.roblox.com/login**. In the account dialog:

1. If you prefer Chrome or Brave, choose that browser before pressing **Open Roblox login** (the top-level Add button defaults to Edge).
2. Sign in **on Roblox's real site**, including Roblox's normal two-step verification when required. Viora is not involved in the sign-in process.
3. Come back to Viora, type your Roblox **username as a label**, and optionally enter a game/subplace **Place ID**.
4. Click **Finish and save account**.
5. Viora remembers the account label and which isolated browser profile it opened, then displays the account in Connected Accounts. Use **Sign in** and **Play** on that row to reopen the same browser profile.

**An important distinction:** the app cannot verify the browser's authenticated Roblox username simply because you signed in on the website. The username you enter after login is an **unverified label**. Checking real identities automatically requires [a registered Roblox OAuth 2.0 application](https://create.roblox.com/docs/cloud/auth/oauth2-overview) using PKCE and the OpenID Connect `openid profile` scopes. No Roblox OAuth client is registered or bundled in this preview. We do not attempt to inspect browser cookies, Roblox sessions, saved passwords, or local browser traffic.

## Browser data and privacy

Viora saves plain account shortcut metadata in:
`%LOCALAPPDATA%\Viora\Manager\accounts.json`

For newly added accounts, Chrome/Edge/Brave itself maintains an **isolated browser data folder** at:
`%LOCALAPPDATA%\Viora\Manager\BrowserProfiles\<random-account-id>\`

**The browser may store Roblox login cookies and other normal browsing information in its own private folder.** Viora Manager passes the directory to the browser using `--user-data-dir` and never opens, reads or copies browser cookies/passwords itself. The account ID is randomly generated and validated before constructing a path.

Removing a saved shortcut **does not automatically delete its browser data**, because that could log out a user or delete data while the browser is running. If a user wants to remove the session completely, they should close that browser profile and manually remove the corresponding directory, after backing up anything they wish to keep.

The existing manually created legacy browser-profile shortcuts (Default, Profile N) remain readable, editable and usable. Managed browser profile shortcuts lock browser/profile selection when edited to avoid silently switching to the wrong Roblox login.

## Roblox Player limitation

Clicking **Play** in Connected Accounts attempts to open Roblox's official game-start link in that browser profile. That **does not guarantee** that the desktop Roblox Player uses the same account, nor that more than one desktop Roblox Player launches simultaneously. The experimental multi-instance toggle is separate. Verify the account displayed in the Player.

## Testing checklist

- [ ] GitHub Actions compiles a self-contained win-x64 Manager v1.4.3 preview with SHA-256.
- [ ] Click the main **Add an Account** button: Edge opens `www.roblox.com/login`; accounts dialog explains the follow-up.
- [ ] Sign into Roblox using the official browser; return and save a valid username label.
- [ ] Restart Viora; saved account is still present. Reopen Sign in; Roblox browser session should persist unless cookies were cleared.
- [ ] Add a second account in a separate isolated browser profile; verify the two browser sessions do not share sign-in state.
- [ ] Browser choices Chrome/Brave open correctly or produce clear errors when not installed.
- [ ] Edit existing managed and legacy shortcuts without losing browser-profile mapping.
- [ ] Test duplicate username, invalid username, invalid game ID, network/browsers unavailable, read-only/recovery mode.
- [ ] Test Play on game and subplace IDs; confirm Roblox Player's identity manually.
- [ ] Check no passwords, cookies or OAuth access/refresh tokens are copied into `accounts.json` or Viora logs.
- [ ] Review existing unsigned-binary AV report before any public release; a successful build alone is not a security clearance.

**Development only**: The published Manager release, online accounts backend, and original Manager window-control features remain untouched.
