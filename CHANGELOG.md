# Viora — major releases

## v1.3 — Stability, smaller updates and visual refinement (development)
- **C · Performance & stability:** Remove the crashing favourites interface and all favourite mutation/filter/import paths from Game Hub and Quick Launcher. Keep normal saved game shortcuts and read historical favourite values without deleting existing user settings or game history. Retire the experimental crash-diagnostics popup cascade; prioritise controlled testing and fixes backed by error traces.
- **C · Performance & stability (next):** Audit redundant UI refreshes, settings persistence, background CPU/RAM and session-statistics correctness; benchmark against the v1.2.1 baseline.
- **D · Lightweight updating (planned):** Separate Launcher and Manager update payloads, offer a small framework-dependent Windows build that reuses an installed .NET Desktop Runtime, verify downloads before applying and allow rollback. Don't ship a fragile binary patcher.
- **E · UI & customisation (planned):** Harmonise the near-black/lilac surfaces, improve navigation/layout consistency and responsiveness, refine animation/performance without reintroducing white panels.
- **Release gate:** Keep the current working two-instance Manager unchanged; don't merge or ask users to download another package until a coherent test milestone is ready.

## v1.2 — Viora Manager & performance pass (testing only)
- **v1.2.1:** Preserve Manager's instance-card view models across refreshes; update bindings only when the displayed label, title or memory changes, preventing unnecessary WPF visual-tree rebuilds.
- **v1.2.1:** Read only appended Roblox Player log data (with short overlap) and move parsing off the UI thread. Keep per-process log isolation and public game-name lookup.
- **v1.2.1:** Poll Manager every 5 seconds when no Roblox Player is visible, reverting to 3 seconds with running games.
- **v1.2.1:** Skip periodic redraws of hidden statistics, charts and game activity pages in Viora; refresh on navigation into them. Local session capture and periodic settings persistence continue independently.
- **v1.2.1:** These are candidate optimisations awaiting measured comparison with v1.2.0, not claims of lower CPU/RAM.
- Created **Viora Manager**, a separate dark-purple WPF Windows EXE featuring live official Roblox Player window detection, process runtime, memory total and individually labelled instance cards.
- Added per-instance game titles with best-effort unique matching of Roblox Player process start times to local log files, extraction of public place/universe IDs and optional public Roblox name lookups. Missing or ambiguous data shows an unknown game, never an invented title.
- Added manual refresh, official Roblox website launch, and user-approved tiling of existing Player windows. These functions do not require modifications to Roblox.
- Made Manager an optional Windows Setup component with Start menu and desktop shortcut choices.
- Extended the Windows build to package Viora EXE, Viora Manager EXE and combined Setup, each with SHA-256 checksum files.
- Added an **off-by-default, explicitly acknowledged experimental mutex compatibility switch** for investigating simultaneous official Player sessions. It holds a legacy Windows singleton mutex only while Manager is open; it performs no client injection, Roblox file changes, cookie access or credential storage.
- **Multi-instance launching is not verified or officially supported.** The switch is a local test, not a reliable public launcher. Studio multiplayer testing remains a distinct workflow.
- **Coordinated self-update for both EXEs is not complete.** Windows runtime testing is required before publishing this as a public release.

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
