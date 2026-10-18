# Viora

**Viora v1.2.0 — Viora Manager prototype branch (NOT a public release).** Unofficial Windows desktop companion for Roblox (formerly Violetstrap).

## Features

- Roblox Quick Launcher, supported FastFlag presets and graphics preferences
- Game Hub: local playtime and recent session history
- Opt-in Discord Rich Presence (a Discord Application ID is needed, no Discord password)
- Custom splash GIF/image, compact forest default, dark themes, tray controls and backups
- Per-user Windows installer **or** portable single-file EXE
- First-run wizard: Roblox detection, theme, experimental performance profile and Discord privacy preference
- Optional local account or password-free guest mode

## Viora Manager (v1.2 preview)

Viora Manager is a **separate Windows EXE**, available as an optional component in Viora Setup. It can also be run independently from the testing build artifact. The interface mirrors the dark-purple Viora concept, with instance cards, process memory, status, session runtime and **per-instance Roblox game names**.

Manager identifies a game's public place ID by matching each Roblox Player process start time to a distinct local Player log file and reading only recent join lines. It then resolves the experience name using public Roblox place/universe endpoints, sending only public IDs. No Roblox credentials, cookies, player names, whole logs or session histories are transmitted. This best-effort feature may show **Game not detected** if logs are absent, names cannot be fetched or process/log matching is ambiguous; it does not guess a title. Each instance has its own log association, including after game teleports.

The Manager monitors **official Roblox Player processes** with visible windows. **Open Roblox** opens the official Roblox website; **Manage windows** focuses one existing Player window or (with user confirmation) tiles already-running windows.

**Experimental multi-instance compatibility (opt-in):** Viora Manager now offers a checkbox to keep a legacy Windows named mutex open for its lifetime. The mechanism influences Roblox's single-instance detection, but **does not patch Roblox, inject code, access cookies, or store Roblox credentials**. This does not guarantee that two Player windows will remain open or that Roblox permits the technique. The checkbox is off by default, is not persisted, requires explicit acknowledgment, and must be enabled before Roblox launches. Closing the Manager or unticking the box releases the mutex. This build does **not** apply Error 773 cookie locking or automatic account switching.

**Simultaneous Roblox Player sessions are NOT confirmed**. Roblox does not officially support simultaneous Player clients, and its client behaviour can change at any time. Do not advertise working or risk-free multi-instance support until real testing has established its behaviour. Roblox Studio's supported multi-client test mode remains a separate alternative.

The workflow now publishes `VioraManager-v1.2.0.exe` as a third distributable, alongside the portable Viora and combined installer. Check that the installer allows Viora Manager to be deselected, that its Start menu and optional desktop shortcut appear correctly, and that uninstall removes both application EXEs without deleting personal AppData.

**Update caveat:** the current Viora in-app updater replaces only the main `Viora.exe`. Manager updates require rerunning a future installer or replacing the separately distributed manager EXE. Full coordinated updates are planned before public v1.2.

## Installer

The Windows workflow builds three distributions from the same source:

- `Viora-v1.2.0-Setup.exe`: per-user installer with Start menu shortcut, optional desktop shortcut and uninstaller.
- `Viora-v1.2.0.exe`: portable EXE for users who prefer not to install.

The installer writes to `%LOCALAPPDATA%\Programs\Viora` without requiring administrator rights. Uninstallation removes the program but deliberately **does not delete** `%LOCALAPPDATA%\Viora` (local settings and histories). Users may delete that folder separately once backups are made.

Viora's existing one-click updater can replace the installed `Viora.exe` when the current user has write permission. New releases are downloaded only after approval. Portable and installed distributions share local settings.

**Unsigned software:** Neither EXE is code-signed. A checksum detects accidental corruption, but a release-supplied checksum alone does not authenticate the publisher. Only run binaries from the official repository and inspect warnings rather than broadly disabling security protections.

## v1.1.2 appearance, scrolling and issue reports

Appearance offers independent colour themes and **Glossy** (default), **Opaque** or **Glass** surface styles. Glossy uses muted gradients, Opaque uses solid surfaces, and Glass blends translucent panels over Viora's own background rather than using desktop acrylic. The selected style is stored locally.

The first-run wizard previews theme/style changes immediately; **Finish** saves and **Cancel** reverts. Main workspace scrolling is handled at the page level while native dropdowns and interactive controls keep their own wheel input. The sidebar version label reads the running assembly version.

A new bottom-left **Report an issue** button opens an editable report with an exact preview and optional minimal diagnostics. After explicit confirmation, Viora copies only the reviewed text and opens GitHub Issues. **Nothing is uploaded or submitted by Viora.** Reports on GitHub are public; never submit sensitive information or full backup ZIPs.

## Privacy and Getting Started (v1.1.1)

Under **Personalise → Utilities**, the **Getting Started tutorial** provides basic installation, launch, graphics, profiles, optional Discord and update guidance. **Privacy & diagnostics** explains which files Viora stores locally and offers a reviewed, optional JSON diagnostic export.

For safety, local account sign-in now requires the password on each new app startup. Viora no longer uses a plaintext stay-signed-in flag to bypass authentication. These are convenience-only local profiles: someone who can alter files in the Windows user profile can reset them. Windows account security remains essential.

Diagnostic exports contain only the application version, Windows platform and whether Roblox was detected. They contain no password hashes, usernames, file paths, process IDs, tokens or game history, and nothing is sent automatically. Full configuration backups **do include private data**, and should never be posted to a Discord server or GitHub issue.

### Multi-instance research

As of October 2026, simultaneously running two normal Roblox Player clients is **not a verified Viora feature**. Bloxstrap removed its previous multi-instance launching after Roblox tightened client behaviour. Viora does not bypass single-instance locks, inject DLLs or alter anti-cheat. The Help Centre includes a simple read-only compatibility test using the official Roblox launcher. A successful test requires two independently usable Windows Player windows. Roblox Studio officially supports up to eight simulated test clients for game developers.

## First-run setup

New installations show an optional **local account creation** screen. **Continue without an account** uses guest mode (no password). If a local account has already been created, guest mode is not offered as an authentication bypass.

After sign-in or guest entry, the initial wizard:

1. Introduces the features and privacy model
2. Detects the official Roblox Player installation (can continue if not found)
3. Offers theme and experimental performance preset choices
4. Asks whether the user intends to configure Discord Rich Presence

The performance selection is saved into the active profile but is **not written to Roblox client files during onboarding**. Discord remains disconnected unless a valid Application ID is configured, even if selected in the wizard. Tracking records local Roblox activity; there is no Viora server or cloud account.

The wizard does **not automatically run for existing v1.0.0 accounts/settings**. It is available later from **Personalise → Utilities → Open setup wizard**.

## Build and test

For source changes, use the `feature/v1.1-installer-onboarding` branch until Windows tests pass. The GitHub Actions workflow builds both EXEs and their SHA-256 files on Windows. Manual dispatch on the feature branch does not publish GitHub Releases; only a version tag matching `Viora.csproj` publishes release assets.

When preparing an official version, test a fresh install (on a Windows account with no Viora AppData), an existing user upgrade, guest mode, local account creation, uninstall, tray, splash, and Discord opt-in/out. Verify a second PC does not inherit the first PC's account or activity history. **Do not publish before these tests pass.**

## Safety / compatibility

Viora is not affiliated with Roblox or Discord. Roblox controls which local FastFlag overrides work and may change its allowlist at any time. Never upload `%LOCALAPPDATA%\Viora`, local password hashes, personal statistics or custom splash media to the public repository. Issues can be reported at https://github.com/balcony3232/Viora/issues.
