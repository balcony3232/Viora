# Viora Manager v1.4.4 — Accounts Home & Public Profile Preview

## Intent
The first screen of Viora Manager is now **Accounts**, with all saved account shortcuts in one large scrolling list. The previous compact **Instances & Tools** page preserves the original Manager's RAM/instance count, experimental multi-instance setting, process/game detection, Roblox button and window arranger.

This is an **independent implementation** of useful account-manager patterns discussed in the [AHK Vault guide](https://ahkvault.com/blog/how-to-use-the-roblox-account-manager), not a copy of the vendor's code or UI.

## Functionality in the preview

| Area | Implemented |
|---|---|
| Account first | Main window opens on Accounts by default; original process tools are one click away |
| Browser sign-in | + Add an Account saves an isolated browser profile and opens Roblox's official login page |
| Identity & avatars | Enter a real Roblox username in Edit -> optional details; Refresh profiles obtains **public** user ID, Roblox display name and headshot from Roblox's unauthenticated public APIs |
| Custom names | Alias takes priority over public display name; legacy generic Account1/Account2 labels remain until linked/renamed |
| Organisation | Groups, comma-separated tags, private notes, favorites and pinned accounts; filtering/search across name/group/tags/notes; favorites/pinned-first sorting |
| Account actions | Edit, Sign in, Play, Ctrl/Shift multiple row selection, select all, clear selection |
| Bulk launches | Up to ten official game-start browser links per queue, with 2/3/4/5/10-second adjustable inter-launch delay and Stop queue; never claims guaranteed client launches |
| Game input | Shared numeric Place ID or official Roblox game URL override; otherwise each account uses saved Place ID |
| Server input | Optional validated Game Job GUID; Browse servers via public API, filter player counts, pick a server or random from a returned page |
| History | Locally recorded **attempted browser game-start links**, last 50; select one to reapply Place/Job ID. Not a verified Roblox join history |
| Export/import | Metadata-only JSON export and validated merge import, backups of local accounts.json before writes. Browser cookie database and sessions are **not** exported |
| Privacy | Never asks for .ROBLOSECURITY, passwords, MFA codes, browser cookies, or authentication tickets |

## Important limitations

**The Manager does not verify that the logged-in browser is the same Roblox identity as the manually linked public profile.** Public display names/headshots are for identification only; an entered Roblox username can refer to someone else. The screenshot of the public Roblox profile must not be presented as proof of authenticated account ownership.

Browser profile separation does **not guarantee** that Roblox Player uses the same account, nor guarantee multiple Roblox Player windows. The official Roblox `games/start?placeId=...` deep link is [deprecated](https://create.roblox.com/docs/production/promotion/deeplinks), and Job GUID selection may fail as games and servers change. No login-state monitoring is available without a legitimate, separately authorized API flow.

The AHK Vault guide also describes cookie import/copy/health checks, unattended crash rejoin, bulk cookie refresh, forced executable/mutex management, dynamic server hopping, saved-server groups, Windows autostart, a floating overlay, and protected credential backups. **Those are not included in this first Accounts Home preview.** Several depend on storing privileged Roblox authentication cookies or reliably verifying which Roblox Player process belongs to which account; implementing them under the current browser-only design would risk account exposure or wrong-account launches. Never upload secret browser data to VirusTotal or GitHub. We may implement safe equivalents after independent design and testing.

Existing account metadata is stored in `%LOCALAPPDATA%\Viora\Manager\accounts.json`, and managed Edge/Chrome/Brave browser data is stored by the browser under `%LOCALAPPDATA%\Viora\Manager\BrowserProfiles\<random-id>`. Deleting a shortcut does not delete the browser session or cookies. Account metadata backups exclude browser session data and do not preserve login across PCs.

## Test checklist before release

- [ ] Standalone win-x64 build succeeds, artifact contains expected EXE and SHA-256.
- [ ] Existing v1.4.3 account shortcuts load without data loss; old ManagedBrowserProfile GUID references still work.
- [ ] Accounts is first screen, Instances & Tools still shows live Roblox stats and window controls.
- [ ] Add at least two accounts and sign in separately; profile login remains isolated across browser restarts.
- [ ] In Edit, enter a real Roblox username; Refresh profiles shows the correct public display name/avatar; manual nickname overrides it. Generic Account1 isn't mistaken for a linked Roblox profile.
- [ ] Test favorites/pins, groups, tags, notes, search/filter and edit persistence across application restart.
- [ ] Test Place ID, subplace ID, game URL, optional Job GUID, server browser and random selection with a live Roblox game.
- [ ] Verify 2-account selection and controlled launch queue, delay, stop; **confirm actual Player identity by hand**.
- [ ] Export metadata and import back; duplicate shortcuts skipped, local backup created, no browser tokens exported.
- [ ] Apply a prior launch history entry; avoid claiming successful game joins.
- [ ] Check latency and failures when Roblox public APIs return 429/403 or network is unavailable: local management must still work.
- [ ] Keep the previous VirusTotal security investigation open; an unsigned build passing CI is NOT proof of malware-free operation.

**Release policy:** Development branch only. The published public Manager release remains unchanged until real Windows smoke and safety testing are complete.
