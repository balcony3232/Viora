# Viora Manager 2.0 — Live Instance Viewer preview

**Development-only branch:** `feature/manager-2-live-viewer`. Not an official release. The Manager remains a separate Windows executable.

## Features to test

- **Unified Live Viewer:** each visible Roblox thumbnail is clickable and focuses the real Roblox window. The DWM thumbnail is display-only; it cannot accept gameplay controls itself.
- **Compact top controls:** Grid, opt-in Anti-AFK, Refresh, and More. More contains Tile & play windows, Restart FPS and FPS diagnostics.
- **Anti-AFK (off by default):** session-only, opt-in Windows input. After the user has left Windows idle for at least five minutes and the helper has been enabled for about seven minutes, it attempts one Space tap in each real Roblox foreground window approximately every eight minutes. It may steal focus, be blocked by Windows, alter gameplay, and **does not guarantee avoiding Roblox idle disconnects**. No Roblox code injection or background input messages. Disables when Viewer closes.
- **Automatic Discord Rich Presence:** Manager title-bar Discord RPC settings button. When a valid Discord Application ID is provided (or inherited from an existing main Viora settings file), it displays the number of Roblox instances, combined Roblox CPU %, and an optional estimated GPU 3D-engine %. It requires the running Discord desktop app. The ID is not a bot token or secret and is saved in the Manager preferences. Presence can be disabled.
- **GPU measurement:** approximate 3D engine usage from Windows PDH GPU Engine performance counters. Unsupported or inaccessible counters report GPU unavailable; never an invented value.
- **FPS:** Intel PresentMon optional render FPS measurement by PID. If Windows ETW tracing is denied, More → FPS diagnostics shows the actual error and exit code; the FPS card shows a dash.
- **Tile & play windows:** Viora positions real Roblox windows and opens Mini-manager for switching between sessions.
- **Game & performance:** best-effort game name, manual instance label, process RAM/CPU, runtime and PID.

## Windows test sequence

1. Download the newest successful Actions artifact and extract **all** files before opening VioraManager.exe.
2. Launch Manager before Roblox to test auto multi-instance compatibility; test disabling and saving the setting.
3. Start Roblox and open Live Viewer. Check responsive thumbnails, names, RAM, CPU, PID, render FPS (if available).
4. Click a preview and confirm its **real** game window comes forward. Use native WASD and mouse there, not inside the thumbnail.
5. Cycle Grid layouts, then choose **More → Tile & play windows** with two running clients.
6. Turn Anti-AFK on only if desired; check the status after leaving Windows idle. Turn it off again and verify no more automated input.
7. Open **Discord RPC** in the Manager title, verify a valid App ID, open Discord desktop and check instance count/CPU/GPU (GPU may be unavailable). Disable RPC and check that the activity clears.
8. Choose **More → FPS diagnostics** if the FPS reading remains a dash. Error 6 with access denied means Windows denied ETW access, not that Roblox is running at 0 FPS.
9. Test closing and reopening each Roblox client, resizing, display scaling, Windows minimise and live-viewer close/reopen.

## Known limits and risks

- Roblox DWM thumbnails are display-only; real gameplay occurs in the native Roblox window, even when launched via the preview.
- Anti-AFK uses foreground Windows SendInput only after extended real user inactivity, not Roblox injection. It may focus games, press Space, fail due to Windows foreground restrictions, and does not guarantee preventing an idle kick. It is session-only and off by default. Follow the rules of each game.
- Discord RPC requires a **Discord Developer Application ID**; the main Viora app may already have one saved in %LOCALAPPDATA%\Viora\settings.json. Manager stores its own preference in %LOCALAPPDATA%\Viora\Manager\discord-rpc.json. Manager and the main Viora app may replace one another's Discord activity if both publish.
- GPU usage is a best-effort estimate from Windows 3D engine counters, not a precise overall GPU hardware sensor or GPU clock/temperature reading.
- FPS measurement requires Intel PresentMon to start a Windows ETW trace; access may require **Performance Log Users** group membership or an approved elevated launch. Do not turn off Windows security or antivirus.
- GitHub Actions checks compilation, packaged WPF startup and synthetic FPS CSV parsing, but cannot prove Roblox gameplay input, Anti-AFK effectiveness, GPU counter availability, or Discord connectivity on the user's PC.

No official release should be created until manual Windows tests pass.
