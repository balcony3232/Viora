# Viora

Unofficial, personal Windows Roblox companion with a dark-purple interface.

Features: Roblox launching, FastFlag Explorer, performance presets and supported graphics settings, session and game statistics, favourites/quick launch, opt-in Discord Rich Presence, local profiles, theme editor, GIF splash screens, system tray and backups.

## Splash screen

The default startup splash displays the bundled purple forest full-screen with a single centred bold pink **Viora** wordmark. Custom image and GIF modes retain the original compact floating window with configurable size, logo and text. Default splash size and logo/text toggles are intentionally disabled because they do not apply to the full-screen design. Existing custom selections remain unchanged during upgrades. Press Escape to dismiss the full-screen preview.

## Build

`build-windows.bat` on Windows with .NET 8 SDK, or run the GitHub Actions `windows-release.yml` workflow.

The workflow produces a self-contained, single-file Windows executable and SHA-256 checksum. GitHub Release assets are published only for tags matching the version in `Viora.csproj`.

## Built-in updates

The update page checks the official `balcony3232/Viora` public GitHub Releases feed. When a newer release is available, **Install update & restart** requests approval, stages a checksum-verified download under local AppData, then launches a separate PowerShell helper. The helper waits for Viora to close, retains the prior EXE as `<current EXE>.previous`, replaces the EXE, verifies the checksum again and restarts Viora. If installation fails, it attempts to restore the old executable; details are written to `%LOCALAPPDATA%\Viora\updates\installer\update.log`. App settings and account data are left untouched.

The first v0.9.2 installation is manual; subsequent published releases can be installed in the application. The executable **filename remains unchanged** during self-updates, even though the internal version changes. The install location must be writable by the current Windows user; otherwise, manual installation is required.

Downloaded updates are not Authenticode-signed. SHA-256 verifies consistency with the release checksum but is **not equivalent to publisher identity verification**. One-click installation is limited to the official repository configured in source; build only from trusted commits.

## Privacy and compatibility

Account hashes, personal stats, custom media and other user settings remain in `%LOCALAPPDATA%\Viora`. Never commit them to the public repo. Roblox only honours locally overridden FastFlags on its published allowlist, and updates may change compatibility. Viora is not affiliated with Roblox or Discord.
