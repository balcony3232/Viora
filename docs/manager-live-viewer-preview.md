# Viora Manager 2.0 — Live Instance Viewer preview

**Development-only branch:** `feature/manager-2-live-viewer`. Not an official release. The Manager remains a separate Windows executable.

## Features to test

- **Dashboard:** real-time DWM thumbnails of existing visible official Roblox Player windows. Single-window layouts grow to fill the viewport; Auto, 1, 2, 3 and 4-column options.
- **Interactive (test):** click a preview to select it, then try mouse clicks, drag, scrolling and keyboard input in the preview. Messages are forwarded to the existing Roblox window through normal Win32 window messages, not through hooking/injection. Press **Esc** to leave preview input focus. Roblox may reject the messages, especially Raw Input/game movement/camera. Use **Focus game** for reliable native input. This is **not confirmed full in-grid gameplay**.
- Each card shows best-effort game title from the Manager's existing local log mapping, RAM, process CPU estimate, runtime and PID. It explicitly states **game FPS unavailable**; no made-up readings.
- Optional account label can be entered manually per running process. It is **not authenticated or automatically derived**, and disappears when the instance or Viewer closes; Viora never reads Roblox sign-in credentials.
- Dark-purple dropdowns, options and account-label input.
- Automatic experimental multi-instance mode **enabled by default in this preview** when Viora Manager launches before Roblox. The setting is displayed prominently and persists to `%LOCALAPPDATA%\\Viora\\Manager\\startup-preferences.json`. Users can disable it at any time. If Roblox is already running, auto-start is skipped with a visible message. This setting is separate from the Live Viewer and does not itself launch extra clients.

## Windows test sequence

1. Download the **newest green** `Viora Manager Live Viewer Preview` Actions artifact, extract it and run `VioraManager.exe`.
2. Launch Manager **before** Roblox if testing automatic multi-instance compatibility. Verify both checkboxes, status, and startup persistence. Uncheck automatic mode, restart, and verify that it remains disabled.
3. Start Roblox normally, open Live viewer and confirm the game name, RAM, runtime, CPU and rendered preview.
4. Change Auto/1/2/3/4-column layout. With a single running instance it should use the available width rather than a tiny preview.
5. In Interactive (test), click the preview and try a harmless GUI button or a short keyboard test; try dragging/scrolling. Confirm Esc exits focus, and **Focus game** activates the native Roblox window.
6. Test second client only if independently permitted by Roblox and available, then close/reopen it and check that stale PID previews disappear. Test minimized/occluded windows, resize, display scaling, and Viewer close/reopen.

## Known limits and risks

- Roblox may ignore background `PostMessage` input, especially camera/character movement; the native foreground-window fallback is the supported testing path.
- DWM thumbnails may be blank when the source is minimized or protected, and scaled previews may have approximate pointer coordinates.
- No Roblox username or in-game FPS is available reliably through this non-invasive Windows process/window API. The manually assigned label must not be mistaken for verification.
- The experimental multi-instance named-mutex behaviour is unofficial and may stop working after a Roblox update. It is reversible on Manager exit and does not alter Roblox files, credentials or install anything to the client.
- The GitHub Actions pipeline performs a Windows publish and packaged WPF startup smoke test, **not a real Roblox gameplay interaction test**.

No official release should be created until manual Windows tests pass.
