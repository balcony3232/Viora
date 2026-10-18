# Viora — major releases

## v1.1 — Installer, onboarding and appearance (preview)
- **v1.1.2:** Added Glossy (new default), Opaque and layered Glass surface styles, kept independent of colour themes. The Glass style is translucency within Viora rather than desktop acrylic.
- **v1.1.2:** Fixed onboarding theme selection with immediate visual preview, persistence on Finish and rollback when cancelled.
- **v1.1.2:** Routed mouse-wheel scrolling more reliably through the main workspace, without interfering with dropdowns and scrollbars.
- **v1.1.2:** Replaced the hardcoded version text with the running assembly version and added preview-before-sharing GitHub issue reports to the sidebar.
- **v1.1.2:** Issue reports require explicit confirmation, only copy reviewed text to the clipboard, and leave final submission to the user.

## v1.1.1 — Privacy and Getting Started (preview)
- v1.1.1 adds an in-app Getting Started guide, local privacy explanations, a manually reviewed diagnostics JSON export and official-client multi-instance compatibility testing instructions.
- Removed the plaintext stay-signed-in authentication bypass; local account sign-in now requires a password on new application starts.
- Clarified that recovery ZIPs contain sensitive profile/account data and diagnostics omit all account data, game history, process IDs and paths.
- Kept v1.1 changes and a single major changelog, with no per-minor-version documentation files.
- Multi-instance launching remains **unverified** and is not enabled in this build.

## v1.1.0 — Installer and first-run experience (preview)
- Added a per-user Inno Setup installer, Start menu entry, optional desktop shortcut, registered uninstaller and post-install launch.
- Kept the existing portable EXE as an alternative download.
- Updated Windows CI to compile and checksum both EXEs, with both assets intended for GitHub Releases.
- Introduced a four-page first-run wizard for Roblox installation detection, theme selection, experimental graphics profile and optional Discord presence preference.
- Made the local account optional for new users with password-free guest entry; guest mode cannot bypass an existing password-protected local account screen.
- Kept the prior user-data folder intact across installation/uninstallation. Older user profiles skip the first-run wizard unless reopened from Utilities.
- Discord remains disconnected until a valid Application ID is configured; graphics presets are staged in the Viora profile without modifying Roblox during setup.
- **Release state: Windows build/runtime verification pending.** Changes live on feature branch for beta testing.

## v1.0.0 — Public beta
- Published the first versioned user-shareable Windows x64 app, retaining the FastFlag Explorer, Game Hub, local stats, Discord Rich Presence and custom themes.
- Added the compact default purple-forest splash with a lilac wordmark.
- Added beta/pre-release awareness to GitHub's release update checker.

## v0.9 — Viora rebrand
- Renamed Violetstrap to Viora, refreshed the icon and branding, and preserved local data through the transition.

## v0.8 — Workspace update
- Added quick-launch folders, assigned performance profiles, custom theme colours, system tray support, backup/recovery and GitHub Releases checking.

Minor maintenance changes are recorded in relevant major-release notes rather than separate files.
