# Viora Manager v1.4.6 — Embedded Roblox Login (development preview)

The previous login design opened Roblox in a separate Edge/Chrome window, but Viora couldn't tell which Roblox identity was signed in. The optional OAuth design required registering an app. This branch instead uses **Microsoft WebView2**, an embedded browser, to display Roblox's normal login page and detect a signed-in account through Roblox's cookie-authenticated, read-only [authenticated-user endpoint](https://create.roblox.com/docs/cloud/reference/domains/users).

## What the user sees

1. Open Viora Manager. **Accounts remains the first screen**.
2. Click **+ Add an Account**. A new Roblox browser window opens **inside Viora** at `https://www.roblox.com/login`.
3. Log in normally, including Roblox's own password, CAPTCHA and two-step verification (if prompted).
4. After Roblox goes to its home page, Viora automatically navigates that **same WebView2 session** to `https://users.roblox.com/v1/users/authenticated`, reads *only* the JSON containing `id`, `name`, and `displayName`, then closes the login window and saves the identity.
5. If Roblox's login is handled as a single-page update and automatic detection doesn't happen, press **Detect signed-in account** at the bottom of the embedded window.
6. The account list shows the real username/User ID/display name; the existing public avatar fetcher downloads the user's public headshot.
7. Existing accounts made with the old external Edge login flow are not deleted. Press **Sign in** beside one to migrate it to an embedded browser profile. This requires signing in **once more**, because the old Edge/Chrome browser cookies are intentionally not read or copied.

No OAuth application, Client ID or Client Secret is required for this experiment. The previous OAuth code is retained in the source branch for comparison but its setup controls are hidden from the normal editor.

## What Viora does and does not handle

- **Per-account browser login state** is stored by the Microsoft Edge WebView2 Runtime at:
  `%LOCALAPPDATA%\Viora\Manager\WebViewProfiles\<random-account-GUID>\`
- Viora does **not** read browser cookies, export `.ROBLOSECURITY`, handle passwords, copy authentication tokens, call browser cookie-manager APIs or persist page contents.
- The only account data stored in `%LOCALAPPDATA%\Viora\Manager\accounts.json` is username, public Roblox ID/display name, browser-profile reference, local nickname, group/settings and a timestamp for the most recent identity check.
- The popup reads page text **only when the currently displayed host is exactly `users.roblox.com` and the URL path is exactly `/v1/users/authenticated`**.
- The WPF app does not send the identity to any third-party service; its public profile thumbnail lookup uses Roblox's public API.
- **Windows WebView2 Runtime is required.** It is typically installed on supported Windows systems, but may need separate installation. This is a new dependency even though the Manager EXE uses self-contained .NET 8.
- A browser session can remain on disk after removing the local account shortcut. Removing the shortcut does not silently wipe a user's browser data.
- Logging into a browser and detecting the identity **does not bind a desktop Roblox Player to that account**. The desktop Player might still use its own session, and simultaneous Roblox Player launches remain experimental and not guaranteed.
- Roblox may block unsupported embedded-browser logins, show additional verification screens or change its authenticated-user API. This is a testing preview, not a promise that the browser login method will always work.
- The original public Viora Manager releases remain untouched, and this feature branch is not a published stable release.

## Tests needed before any public release

- [ ] Windows .NET 8 x64 GitHub Actions compilation succeeds; executable and SHA-256 are attached as testing artifacts.
- [ ] On a real Windows PC with Edge WebView2 Runtime installed, clicking **Add an Account** shows the real `www.roblox.com` login.
- [ ] After successful login, Roblox's authenticated-user endpoint returns a real user identity and Viora automatically saves it.
- [ ] If navigation doesn't trigger detection, manual **Detect signed-in account** works and no credentials are requested by Viora.
- [ ] Create two accounts; browser profiles remain independent and persistent after restarting Manager.
- [ ] Existing external Edge/Chrome account shortcuts can connect in-app without losing their old session or local data.
- [ ] Avatar, custom nickname, User ID, groups/tags and edit settings remain intact.
- [ ] Clicking **Sign in**/ **Play** on an embedded account reuses its own WebView2 data directory and never another profile's session.
- [ ] Launch Selected queue uses each embedded profile and warns users to check the desktop Player identity.
- [ ] Handle incorrect password, MFA, CAPTCHA, Roblox login refusing embedded browsers, network failure, no WebView2 Runtime, missing account data and file corruption without credential leakage.
- [ ] Public security review and VirusTotal investigation are still pending.

See Microsoft [WebView2 user data folder guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder) and Roblox's [authenticated-user endpoint](https://create.roblox.com/docs/cloud/reference/domains/users).