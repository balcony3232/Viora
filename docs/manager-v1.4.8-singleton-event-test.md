# Viora Manager v1.4.8 — singleton event compatibility experiment

The v1.4.7 test was opening the first Roblox Player but could not get a second running. The code held a Windows Mutex named `ROBLOX_singletonMutex`. Roblox's legacy single-Player check also relies on a differently named object, `ROBLOX_singletonEvent`, so holding only the former can leave the relevant check active.

This testing branch changes the legacy compatibility handle to the `ROBLOX_singletonEvent` object name, using a Windows named Mutex. Windows named objects share a namespace; a named Mutex prevents a different object type (Event) from being created with that name. Historical Roblox client logic treated failure to create its singleton Event as a reason not to enforce that check. This is **not an official Roblox feature and might no longer apply** in modern releases.

The app still does not inspect Roblox cookies, alter/inject into Roblox binaries, enumerate/close Roblox's kernel handles, disable antivirus or force-close other programs.

## How to test

1. Exit **all** Roblox desktop Players. Use a clean Windows session if Roblox is stuck in the background; close other multi-instance tools.
2. Start the Manager v1.4.8 test executable. Check your saved accounts still appear.
3. Click **Multi-instance: OFF** on Accounts and explicitly enable the experiment **before** the first Player starts. The button will show the experimental ON state.
4. Enter a game Place ID and select just **two** saved accounts. Press **Launch selected**.
5. Viora opens the first browser game-start link and checks for a real desktop Player window. It then opens the next browser link and checks again.
6. If only one real Player starts within the window-check timeout, the queue stops; do not assume four browsers equals four Player instances.
7. If two Players start, **manually verify the usernames inside both game clients**. Separate WebView2 browser sessions do not guarantee separate native Player identities.

## Risks and limitations

Roblox says that running multiple desktop Player clients on one PC is not supported, and older compatibility methods can stop working after an update. Even if the object-name fix allows a second process, Roblox's account-session logic may reuse the currently signed-in desktop Player. Neither game-start links nor the authenticated-user endpoint select the native Player's Roblox identity.

This branch is a private/development test and **does not replace or publish any previous Manager release**. Do not claim success before a real Windows test. There is also an unresolved earlier unsigned-binary antivirus concern.

Background: [Roblox singleton bug report](https://devforum.roblox.com/t/robloxs-mutex-breaks-sometimes-causes-unnecessary-log-spam-and-allows-multiple-windows-of-roblox-to-be-opened-at-all-times/3456153), [Roblox developer response on unsupported multi-client use](https://devforum.roblox.com/t/the-ability-to-run-2-roblox-clients-at-once/28334).