# Viora Manager 2.0 — Live Instance Viewer preview

**Development-only branch:** `feature/manager-2-live-viewer`. Not an official release. The Manager remains a separate Windows executable.

## Features to test

- **Dashboard:** real-time DWM thumbnails of existing visible official Roblox Player windows. Single-window layouts grow to fill the viewport; Auto, 1, 2, 3 and 4-column options.
- **Interactive (test):** click a preview to select it, then try mouse clicks, drag, scrolling and keyboard input in the preview. Messages are forwarded to the existing Roblox window through normal Win32 window messages, not through hooking/injection. Press **Esc** to leave preview input focus. Roblox may reject the messages, especially Raw Input/game movement/camera. Use **Focus game** for reliable native input. This is **not confirmed full in-grid gameplay**.
- Each card shows best-effort game title from the Manager's existing local log mapping, RAM, process CPU estimate, runtime and PID. It shows measured **Render FPS** by Roblox PID through an optional official Intel PresentMon ETW helper. The label is not the game's on-screen FPS overlay or the rate the monitor actually displays; measurements may be unavailable on some machines.
- Optional account label can be entered manually per running process. It is **not authenticated or automatically derived**, and disappears when the instance or Viewer closes; Viora never reads Roblox sign-in credentials.
- Direct Dashboard / Interactive (test) buttons and click-to-cycle grid selector (no inaccessible dropdown). Dark-purple controls and account-label input.
- Automatic experimental multi-instance mode **enabled by default in this preview** when Viora Manager launches before Roblox. The setting is displayed prominently and persists to `%LOCALAPPDATA%\\Viora\\Manager\\startup-preferences.json`. Users can disable it at any time. If Roblox is already running, auto-start is skipped with a visible message. This setting is separate from the Live Viewer and does not itself launch extra clients.

## Windows test sequence

1. Download the **newest green** `Viora Manager Live Viewer Preview` Actions artifact, extract it and run `VioraManager.exe`.
2. Launch Manager **before** Roblox if testing automatic multi-instance compatibility. Verify both checkboxes, status, and startup persistence. Uncheck automatic mode, restart, and verify that it remains disabled.
3. Start Roblox normally, open Live viewer and confirm the game name, RAM, runtime, CPU and rendered preview. Check that both Roblox clients eventually show plausible, independent **Render FPS** when active; the exact values will vary.
4. Click the Grid button to cycle Auto/1/2/3/4-column layout. With a single running instance it should use the available width rather than a tiny preview.
5. In Interactive (test), click the preview and try a harmless GUI button or a short keyboard test; try dragging/scrolling. Confirm Esc exits focus, and **Focus game** activates the native Roblox window.
6. Test second client only if independently permitted by Roblox and available, then close/reopen it and check that stale PID previews disappear. Test minimized/occluded windows, resize, display scaling, and Viewer close/reopen.

## Known limits and risks

- Roblox may ignore background `PostMessage` input, especially camera/character movement; the native foreground-window fallback is the supported testing path.
- DWM thumbnails may be blank when the source is minimized or protected, and scaled previews may have approximate pointer coordinates.
- No verified Roblox username is available through the Manager's existing process APIs. The manually assigned label must not be mistaken for verification.
- FPS uses **Intel PresentMon 2.5.1** (MIT-licensed official console helper), which is bundled as a separate file in this test ZIP. The workflow checks its official release SHA-256 before publishing. Extract **all files in the ZIP**, including `PresentMon-2.5.1-x64.exe`, next to `VioraManager.exe`. The license is in `PresentMon-LICENSE.txt`.
- PresentMon measures **application frame presents (Render FPS)** using Windows event tracing. It may differ from frames displayed, especially on minimized or background windows. It is not a reading from inside Roblox itself. The helper runs only while Live Viewer is open, uses an in-memory stdout stream instead of writing frame traces, and is stopped when the Viewer closes. No game code injection, process patching or credential access is involved.
- If the helper cannot start, its CSV format changes, or Windows refuses the trace session, the card shows a dash and the Viewer status explains what went wrong. **Restart FPS** retries the helper. Frame capture may have a performance impact. It does not need the user to turn off antivirus.
- The experimental multi-instance named-mutex behaviour is unofficial and may stop working after a Roblox update. It is reversible on Manager exit and does not alter Roblox files, credentials or install anything to the client.
- The GitHub Actions pipeline performs a Windows publish and packaged WPF startup smoke test, **not a real Roblox gameplay interaction test**.

No official release should be created until manual Windows tests pass.
