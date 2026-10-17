# Viora v0.9.1 — Roblox companion

Viora is a Windows-only, unofficial Roblox companion. Formerly known as Violetstrap.
It includes a launcher, FastFlag Explorer, profiles, custom splash screens and GIFs,
Roblox activity history, game favourites, Discord Rich Presence, supported performance settings,
themes, system tray controls, backup/recovery, and an opt-in GitHub Releases update checker.

> Unofficial third-party project. Not affiliated with Roblox or Discord.
> Test against the current Roblox client. Local FastFlag overrides are limited by Roblox's allowlist.

## Build

- Install the .NET 8 SDK for Windows.
- Run `build-windows.bat`.
- Successful build outputs `release\Viora-v0.9.1.exe`.
- You can also use `.github/workflows/windows-release.yml` on a GitHub-hosted Windows runner.
- For an official release, push a matching tag `v0.9.1` and inspect the build before sharing it.

## Updating from Violetstrap

On first launch, Viora tries to **copy** `%LOCALAPPDATA%\Violetstrap` into
`%LOCALAPPDATA%\Viora` and leaves the old folder untouched as a backup.
If the migration cannot complete (e.g. locked files), Viora temporarily uses the
old folder rather than silently discarding your settings. Back up data before updating.
Custom splash/media paths may still point to the old location; keep that folder until verified.

## Updates

Viora defaults its update source to `balcony3232/Viora`.
Once that repository has a successfully published tagged GitHub Release with
`Viora-vVERSION.exe` and `Viora-vVERSION.exe.sha256`, the update checker can
check and download a verified executable. It **does not silently install updates**.
It is not functional until an actual release exists.

## Privacy and publishing

The user profile, local password hash, playtime statistics, Discord application ID,
custom GIFs, local themes, and configuration files live under local AppData, not
inside the source. Do **not** commit AppData, backups, or personal launch logs.
This repository contains only distributable source/assets.

## More

See `CHANGELOG-v0.9.md`, `RELEASE_GUIDE.md`, and `.github/workflows/windows-release.yml`.

## v0.9.1 update

See [CHANGELOG-v0.9.1.md](CHANGELOG-v0.9.1.md) for the neutral-copy refresh, compact sidebar header, clearer duration slider and sharper opaque splash-window rendering. Existing `%LOCALAPPDATA%\Viora` state remains compatible.

For the public GitHub repository `balcony3232/Viora`, commit and push these source files before running the Windows EXE workflow. Do not upload personal settings, statistics or account backups.
