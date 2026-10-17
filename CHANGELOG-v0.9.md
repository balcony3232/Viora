# Viora v0.9.0 — Rebrand

## Rebrand
- Renamed app, assembly, root namespace, executable, title bar, sidebar, splash screen default text, system tray identity, and Discord defaults from Violetstrap to Viora.
- New original restrained purple V logo, transparent logo PNG, and Windows `.ico` for app/taskbar/tray.
- Changed Windows build and GitHub Releases artifact names to `Viora-v0.9.0.exe` and matching SHA-256 checksum.

## Preservation and migration
- First launch copies existing `%LOCALAPPDATA%\Violetstrap` to `%LOCALAPPDATA%\Viora` without deleting the old files.
- Existing sign-in account, local themes, favourites, graphics presets, saved sessions, GIF splash configurations, and Discord settings are retained by design.
- If data migration fails, Viora falls back to the old folder instead of discarding existing files.
- Old custom-media absolute paths can still point to `%LOCALAPPDATA%\Violetstrap`; keep the old directory until verified.

## Releases/updates
- `balcony3232/Viora` is the default update repository.
- GitHub Actions builds a Windows single-file self-contained EXE on manual dispatch or matching tag.
- Tag publication also creates a GitHub Release with the EXE and checksum; manual builds create downloadable workflow artifacts.
- Updater checks GitHub Releases and verifies checksums, but does not yet install automatically.

## Unchanged features
- FastFlag Explorer, gameplay/performance controls, quick launcher, Discord Rich Presence, tracking fix, Game Hub, theme studio, splash GIFs, system tray, backup centre.

## Test requirements
- This source was only statically validated here. Windows compilation, migration, GIF previews, Discord and update downloads require testing on Windows before calling the release stable.
