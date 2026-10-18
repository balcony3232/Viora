# Viora

**Viora v1.1.0 — installer and onboarding preview branch.** Unofficial Windows desktop companion for Roblox (formerly Violetstrap).

## Features

- Roblox quick launch, favourites, supported FastFlag presets and graphics preferences
- Game Hub: local playtime and recent session history
- Opt-in Discord Rich Presence (a Discord Application ID is needed, no Discord password)
- Custom splash GIF/image, compact forest default, dark themes, tray controls and backups
- Per-user Windows installer **or** portable single-file EXE
- First-run wizard: Roblox detection, theme, experimental performance profile and Discord privacy preference
- Optional local account or password-free guest mode

## Installer

The Windows workflow builds two distributions from the same source:

- `Viora-v1.1.0-Setup.exe`: per-user installer with Start menu shortcut, optional desktop shortcut and uninstaller.
- `Viora-v1.1.0.exe`: portable EXE for users who prefer not to install.

The installer writes to `%LOCALAPPDATA%\Programs\Viora` without requiring administrator rights. Uninstallation removes the program but deliberately **does not delete** `%LOCALAPPDATA%\Viora` (local settings and histories). Users may delete that folder separately once backups are made.

Viora's existing one-click updater can replace the installed `Viora.exe` when the current user has write permission. New releases are downloaded only after approval. Portable and installed distributions share local settings.

**Unsigned software:** Neither EXE is code-signed. A checksum detects accidental corruption, but a release-supplied checksum alone does not authenticate the publisher. Only run binaries from the official repository and inspect warnings rather than broadly disabling security protections.

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
