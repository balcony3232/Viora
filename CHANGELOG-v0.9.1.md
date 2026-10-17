# Viora v0.9.1 — UI Clarity & Splash Rendering

## Changes

### Consistent branding
- Capitalised **Viora** in the sidebar and account screen.
- Capitalised **Your Roblox workspace** in the sidebar subtitle.
- Updated the main window title to `Viora — Roblox Workspace`.
- Corrected the sidebar version label from v0.8.0 to v0.9.1.
- Reduced top sidebar header margins and spacing to avoid wasting vertical space.

### Sharper standalone splash
- Switched the splash from a per-pixel transparent, layered WPF window to an opaque background.
- Removed the parent-panel DropShadowEffect that could force text and images to be composited together.
- Enabled ClearType text rendering, display-oriented text layout, pixel snapping and layout rounding.
- Applied high-quality scaling to the splash banner background and image/GIF surface.
- Requested native rounded window corners on Windows 11 via DWM, with a safe no-op fallback on earlier Windows versions.
- Preserved compact/medium/large splash modes, GIF playback, still images, startup text and fade settings.
- GIF output remains limited by the **native resolution and frame quality** of the selected source image.

### Splash duration slider
- Increased visible track thickness and contrast against the dark background.
- Increased thumb diameter and added a high-contrast light outline.
- Enlarged the track's layout space to prevent the thumb being clipped by the short track.
- Retained 0.5–10 second bounds and 0.5-second increments.

### Neutral interface copy
- Reworded on-screen explanations across Overview, Performance, FastFlags, Game Hub, Activity, Discord, Appearance, Splash Screen, Quick Launcher, Updates and Recovery.
- Replaced conversational prose with neutral interface descriptions, labels and safety notices.
- Retained the user-selected display name in the home greeting and local account functions for existing personalisation.
- Revised a handful of system dialogs and status messages to match the same voice.

### Version & compatibility
- Updated assembly, file, and package versions to **0.9.1**.
- Updated `build-windows.bat` and source README for `Viora-v0.9.1.exe` output.
- Retained local settings, profiles, session tracking, backup/restore, performance tools, Discord Rich Presence, and the existing GitHub Windows build workflow.

## Verification
- XML/XAML and project files parsed successfully in the authoring environment.
- Verified existing named event handlers and all referenced XAML names remain present.
- Verified ZIP archive integrity.
- **Windows compilation and rendered visual appearance are not verified here.** Use the GitHub Actions Windows runner or `build-windows.bat` for the release build.

## Release process
1. Extract the ZIP and copy the project contents into the local GitHub Desktop checkout of `balcony3232/Viora`.
2. Inspect the changes; do not commit account data, `settings.json`, backups, custom private media or `%LOCALAPPDATA%` files.
3. Commit and **Push origin**.
4. Trigger **Actions → Windows EXE build and release → Run workflow** on `main`.
5. Download the `Viora-Windows-exe` artifact, extract and test `Viora-v0.9.1.exe`.
6. After verification, create and push the `v0.9.1` tag to publish the versioned EXE and checksum as a GitHub Release.
