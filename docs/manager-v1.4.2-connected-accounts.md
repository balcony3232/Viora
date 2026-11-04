# Viora Manager v1.4.2 — Connected Accounts (unreleased preview)

Implements the main-window account list described in the project-owner's drawing, without replacing the original simple Manager.

## What is implemented

- A **Connected Accounts** section on the main Manager screen with a compact scrolling row for each saved Roblox username, associated browser/profile and saved game ID.
- **+ Add accounts** opens the existing editor. You can add, edit and remove shortcuts; records are saved in `%LOCALAPPDATA%\Viora\Manager\accounts.json` and the previous save is backed up before overwriting.
- **Sign in** opens the official Roblox home page in the chosen Edge, Chrome or Brave profile. Sign in on Roblox's own website; Manager never asks for passwords.
- **▶ Play** opens Roblox's [documented game-start deep link](https://create.roblox.com/docs/production/promotion/deeplinks): `https://www.roblox.com/games/start?placeId=<id>`, in the saved browser profile. The link format is currently marked **deprecated** by Roblox and may change.
- **GAME / SUBPLACE ID** at the top accepts a numeric Place ID or `https://www.roblox.com/games/<id>/...` link. When populated, it overrides the individual account's saved game for any Play button. When blank, each Play button uses the game stored for its account.
- Supports up to 1,000 saved shortcuts as an on-disk safety bound (not a claim that Roblox can run 1,000 simultaneous accounts). Browser profile folders accept `Default` or `Profile 1` through `Profile 9999`; each account must use its own browser profile.
- Retains the original experimental multi-instance compatibility toggle, game detection, window arrangement and separate Account editor.

## Important behavior and limits

The saved username is **only a label**, not proof of active Roblox authentication.

**A browser's signed-in Roblox account may differ from the Roblox Player's active account**. Roblox has a [2026 developer-reported case](https://devforum.roblox.com/t/roblox-doesnt-use-the-account-on-the-browser-when-the-play-button-is-pressed/4422058) where clicking Play in a browser used the Roblox Player's separate signed-in identity. Viora never selects or verifies an authenticated desktop Player session. If the Roblox Player uses the wrong account, stop and check Roblox's active account; Viora does not switch it.

The optional multi-instance compatibility experiment may fail across Roblox updates, and opening several game-start links does **not** guarantee separate clients. Do not claim fully automatic multi-account joins or authenticated mass launching.

The Manager only launches known browser executables from their expected Program Files or LocalAppData installation paths and only opens official HTTPS Roblox URLs. It does not collect Roblox passwords or `.ROBLOSECURITY` cookies, inject code, derive authentication tickets, or read browser profiles.

## Manual acceptance checklist

1. Build the standalone win-x64 Manager through `.github/workflows/manager-connected-accounts-preview.yml`.
2. On a Windows machine, open Manager and verify the original stats, multi-instance setting and detected instances still appear.
3. Add 2+ distinct browser-profile shortcuts; check that rows appear in the main window and persist after closing and reopening.
4. Enter a numeric game ID or paste an official Roblox game URL into the shared field; click Play on each row, confirm the **game-start link** opens under the configured browser profile.
5. Clear the shared game input; confirm Play uses that account's saved game ID. Without a saved game, a clear validation message must appear.
6. Check that entering a subplace's **Place ID** opens the corresponding deep link; the game might still decide whether that subplace can be joined.
7. Verify user confirmation warns about Roblox Player identity mismatch, and check which Roblox account actually opens.
8. Test Chrome, Edge, Brave, profiles `Default`, `Profile 1`, `Profile 16`, malformed/non-Roblox URLs, duplicate username/profile, missing browser.
9. Reopen the editor to remove/edit shortcuts. Verify file corruption does not overwrite existing on-disk settings.
10. Confirm no Roblox credentials/tokens are stored, and don't claim the experimental multi-instance helper is supported by Roblox.

## Release

This is **version 1.4.2 development-only**. Public Manager v1.4.1 release binaries and the separate launcher are unchanged. The GitHub Actions artifact is a preview, not a signed or security-certified release. The original antivirus investigation still applies; do not dismiss vendor detections as false positives without evidence.
