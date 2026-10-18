# Viora

**Viora v1.0.0 (public beta)** — an unofficial Windows desktop companion for Roblox, formerly known as Violetstrap.

![Viora](Assets/Viora-logo.png)

## Features

- Roblox quick launch, favourite games and assigned per-game performance profiles
- FastFlag Explorer, saved profiles and supported Roblox graphics preferences
- Game Hub with locally observed playtime, sessions and game artwork
- Optional Discord Rich Presence with game and AFK status
- Local profile, dark-purple themes, custom GIF splash and compact forest default splash
- System tray controls, backup/recovery and a versioned GitHub Releases updater

**Compatibility:** Roblox controls which local FastFlags it honours. Not every flag or graphics preference is effective across Roblox versions. This is not an official Roblox product.

## Downloads for testers

After the v1.0.0 Windows build passes a manual launch test, download **Viora-v1.0.0.exe** from [GitHub Releases](https://github.com/balcony3232/Viora/releases). The release also provides its SHA-256 checksum. This Windows executable is self-contained; extract it from any downloaded ZIP before running it.

The application is currently in public beta. Windows may show an unknown-publisher or SmartScreen warning because the EXE is **not code-signed**. Do not disable security protections globally; only run software after verifying its source and origin.

Local accounts and playtime data are kept on each PC at `%LOCALAPPDATA%\Viora`, not shared between testers. Viora may copy an existing Violetstrap data folder on first launch without deleting the original. Roblox and Discord sign-in credentials are not required by Viora.

## Publishing v1.0.0

1. Check [GitHub Actions](https://github.com/balcony3232/Viora/actions/workflows/windows-release.yml) by running **Windows EXE build and release** on `main`. Download and test its EXE artifact on Windows.
2. Create the `v1.0.0` tag from the tested `main` branch, then publish a GitHub Release titled **Viora v1.0.0 — Public Beta** and select **Set as a pre-release**.
3. The workflow is triggered by the tag push and should upload `Viora-v1.0.0.exe` and `Viora-v1.0.0.exe.sha256` to that Release. Confirm both assets are present before sharing the release link.
4. Test the published download and the updater against the published release.

**Do not publish personal settings, saved account data, statistics, custom user images, tokens or content from `%LOCALAPPDATA%`.** Keep the single `CHANGELOG.md` for major release notes.

## Building from source

Use `build-windows.bat` on Windows with the .NET 8 SDK or run the GitHub Actions workflow. The workflow creates a single self-contained Windows x64 executable plus a SHA-256 file. An untagged workflow run only uploads a temporary workflow artifact, not a permanent public Release asset.

## Update system

Viora checks the public `balcony3232/Viora` releases feed, including pre-releases. After confirmation, it downloads a versioned executable and checksum, verifies the checksum, stages the new executable, closes Viora, and uses a separate helper process to replace and reopen it. Updates require write permission to Viora's installed executable directory. Settings and local accounts are not replaced.

**Security note:** SHA-256 detects a mismatch between the file and its checksum but does not authenticate the publisher when both are supplied by the same GitHub release. Updates are not code-signed; use only a trusted repository and build source.

## Reporting beta issues

Use the repository's [Issues](https://github.com/balcony3232/Viora/issues) page. Include the Viora version, Windows version, steps to reproduce, and the exact error message. Remove usernames, passwords, local file paths and private Roblox/Discord information from screenshots and logs.
