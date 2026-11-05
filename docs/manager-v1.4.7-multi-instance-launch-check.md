# Viora Manager v1.4.7 — observable multi-instance launch test

## Why this is needed

v1.4.6 opened every selected account's official Roblox game-start link in its saved browser profile. Opening **four browsers** is NOT equivalent to launching **four separate Roblox Player processes**. The batch previously reported browser attempts without checking whether a new Player window started.

v1.4.7 is a **best-effort compatibility and diagnostic build**, not a promise that Roblox's one-Player-per-machine limit can be bypassed.

## What changed

- The Accounts-first view has an explicit **Multi-instance: OFF/ON** button. It controls the original *opt-in, in-process, legacy named-mutex compatibility experiment*, previously hidden under **Instances & Tools**.
- Turning on the experiment uses the existing checkbox confirmation and must occur **before any Roblox Player is open**. The original Windows/Roblox binary is not edited, patched, injected into or remotely manipulated. Turning it off or closing Manager releases the experiment.
- **Launch selected** with more than one saved account stops immediately if multi-instance mode is OFF, rather than opening several browsers.
- For each selected account, Viora records the set of **visible RobloxPlayerBeta.exe windows**, launches the official game-start link from the account's own browser session, and checks whether the number of **visible Player windows actually increases**. If no new Player window appears during the check, the remaining queue stops and reports that the legacy experiment may be incompatible.
- Browser windows are not counted as Roblox Player clients. Existing account labels and browser profiles are unchanged. Individual launches still require a valid Place ID.
- The existing Instances & Tools page, current process count and window-arrange tool remain available.

## Test it safely

1. Fully close **all** Roblox Player clients (browser windows can remain open).
2. Launch the **v1.4.7 preview**; existing saved accounts should appear.
3. On Accounts, click **Multi-instance: OFF**, read the warning and choose Yes only if you accept the unofficial compatibility experiment. Confirm the button says **experimental ON**.
4. Start with **two** selected accounts, not four. Paste a game's numeric Place ID, click Launch selected and approve the attempt.
5. Observe whether Viora detects a **new, separate Roblox Player window** for the second account. Even if two Player windows start, independently verify which Roblox username each Player actually uses.
6. If two *different* clients really remain running, test three or four accounts.
7. If only one client opens, stop. The old compatibility path may no longer work with your Roblox version; the Manager will NOT force Roblox by modifying binaries, terminating security processes, deleting authentication files, or injecting.

## Important limitations

Roblox staff stated in April 2026 that running multiple desktop Player clients simultaneously isn't officially supported: https://devforum.roblox.com/t/the-ability-to-run-2-roblox-clients-at-once/28334

Roblox anti-cheat documentation warns against modified client applications and external programs that alter client behaviour: https://en.help.roblox.com/hc/en-us/articles/24275616578708-Anti-cheat-Messages

This preview reuses an existing legacy experiment without changing Roblox files, but is **not endorsed or guaranteed by Roblox** and may still create terms/compatibility or account-risk concerns. Multiple saved *browser* sessions and public Roblox identity detection **do not authenticate or select the desktop Roblox Player account**. The check only measures observable window counts, not which Roblox username the client uses.

This version is not a public release. It still needs a real Windows smoke test and the original antivirus investigation should be reviewed before sharing a public binary.