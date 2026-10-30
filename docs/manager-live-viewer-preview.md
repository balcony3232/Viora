# Viora Manager 2.0 — Live Instance Viewer preview

**Development-only branch:** `feature/manager-2-live-viewer`. Not an official release. The Manager remains a separate Windows executable.

## Features to test

- **Dashboard:** real-time DWM thumbnails of existing visible official Roblox Player windows. Single-window layouts grow to fill the viewport; Auto, 1, 2, 3 and 4-column options.
- **Interactive:** click a preview to activate the **real** Roblox window so the game receives native keyboard and mouse input. DWM thumbnails do not accept game input, and attempting to send synthetic Win32 background messages was unreliable. The viewer deliberately does not claim that games are playable inside the thumbnail itself.\n- **Tile & play windows:** confirm to arrange the existing real Roblox windows side by side with the Manager's layout feature, minimise the viewer and open the always-on-top Mini-manager for choosing which game to focus. No Roblox binaries or credentials are modified.
- Each card shows best-effort game title from the Manager's existing local log mapping, RAM, process CPU estimate, runtime and PID. It shows measured **Render FPS** by Roblox PID through an optional official Intel PresentMon ETW helper. The label is not the game's on-screen FPS overlay or the rate the monitor actually displays; measurements may be unavailable on some machines.
- Optional account label can be entered manually per running process. It is **not authenticated or automatically derived**, and disappears when the instance or Viewer closes; Viora never reads Roblox sign-in credentials.
- Direct Dashboard / Interactive (test) buttons and click-to-cycle grid selector (no inaccessible dropdown). Dark-purple controls and account-label input.
- Automatic experimental multi-instance mode **enabled by default in this preview** when Viora Manager launches before Roblox. The setting is displayed prominently and persists to `%LOCALAPPDATA%\\Viora\\Manager\\startup-preferences.json`. Users can disable it at any time. If Roblox is already running, auto-start is skipped with a visible message. This setting is separate from the Live Viewer and does not itself launch extra clients.

## Windows test sequence

1. Download the **newest green** `Viora Manager Live Viewer Preview` Actions artifact, extract it and run `VioraManager.exe`.
2. Launch Manager **before** Roblox if testing automatic multi-instance compatibility. Verify both checkboxes, status, and startup persistence. Uncheck automatic mode, restart, and verify that it remains disabled.
3. Start Roblox normally, open Live viewer and confirm the game name, RAM, runtime, CPU and rendered preview. Check that both Roblox clients eventually show plausible, independent **Render FPS** when active; the exact values will vary.
4. Click the Grid button to cycle Auto/1/2/3/4-column layout. With a single running instance it should use the available width rather than a tiny preview.
5. In **Interactive**, click the preview: the corresponding real Roblox window should come forward. Test ordinary WASD, mouse and camera controls in that **real** window, not inside the passive preview.\n6. Choose **Tile & play windows**, confirm, then test using mouse/keyboard directly in both real tiled Roblox windows. Switch between accounts with Mini-manager; return to the viewer from the taskbar.
7. Test second client only if independently permitted by Roblox and available, then close/reopen it and check that stale PID previews disappear. Test minimized/occluded windows, resize, display scaling, and Viewer close/reopen.

## Known limits and risks

- DWM thumbnail previews are display-only. Roblox native controls operate in the foreground real game window or the tiled real-window arrangement, **not within the preview**. The old Win32 background-input forwarding has been removed after an unsuccessful Windows test.
- DWM thumbnails may be blank when the source is minimized or protected, and scaled previews may have approximate pointer coordinates.
- No verified Roblox username is available through the Manager's existing process APIs. The manually assigned label must not be mistaken for verification.
- FPS uses **Intel PresentMon 2.5.1** (MIT-licensed official console helper), which is bundled as a separate file in this test ZIP. The workflow checks its official release SHA-256 before publishing. Extract **all files in the ZIP**, including `PresentMon-2.5.1-x64.exe`, next to `VioraManager.exe`. The license is in `PresentMon-LICENSE.txt`.
- PresentMon measures **application frame presents (Render FPS)** using Windows event tracing. It may differ from frames displayed, especially on minimized or background windows. It is not a reading from inside Roblox itself. The helper runs only while Live Viewer is open, uses an in-memory stdout stream instead of writing frame traces, and is stopped when the Viewer closes. No game code injection, process patching or credential access is involved.
- If the helper cannot start, its CSV format changes, or Windows refuses the trace session, the card shows a dash. Use **FPS details** to view/copy the real helper error and process exit code, then **Restart FPS** to retry. Intel PresentMon documents that trace access may require the Windows **Performance Log Users** group or an approved elevated context. Do not disable antivirus, Roblox protections or Windows security. Frame capture may have a performance impact.
- The experimental multi-instance named-mutex behaviour is unofficial and may stop working after a Roblox update. It is reversible on Manager exit and does not alter Roblox files, credentials or install anything to the client.
- The GitHub Actions pipeline performs a Windows publish and packaged WPF startup smoke test, **not a real Roblox gameplay interaction test**.

No official release should be created until manual Windows tests pass.
