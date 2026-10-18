# Viora

**Viora v1.0.0 (public beta)** — an unofficial Windows desktop companion for Roblox, revamped from Violetstrap.

![Viora](Assets/Viora-logo.png)

## Features

- Roblox quick launch, favourite games and assigned per-game performance profiles
- FastFlag Explorer, saved profiles and supported Roblox graphics preferences
- Game Hub with locally observed playtime, sessions and game artwork
- Optional Discord Rich Presence with game and AFK status
- Local profile, dark-purple themes, custom GIF splash and compact forest default splash
- System tray controls, backup/recovery and a versioned GitHub Releases updater

**Compatibility:** Roblox controls which local FastFlags it honours. Not every flag or graphics preference is effective across Roblox versions. This is not an official Roblox product.

The application is currently in public beta. Windows may show an unknown-publisher or SmartScreen warning because the EXE is **not code-signed**. Do not disable security protections globally; only run software after verifying its source and origin.

Local accounts and playtime data are kept on each PC at `%LOCALAPPDATA%\Viora`, not shared between testers. Viora may copy an existing Violetstrap data folder on first launch without deleting the original. Roblox and Discord sign-in credentials are not required by Viora.

## Building from source

Use `build-windows.bat` on Windows with the .NET 8 SDK or run the GitHub Actions workflow. The workflow creates a single self-contained Windows x64 executable plus a SHA-256 file. An untagged workflow run only uploads a temporary workflow artifact, not a permanent public Release asset.


## Reporting beta issues

Use the repository's [Issues](https://github.com/balcony3232/Viora/issues) page. Include the Viora version, Windows version, steps to reproduce, and the exact error message. Remove usernames, passwords, local file paths and private Roblox/Discord information from screenshots and logs.

Alternatively join the discord server and DM any Admins or Owners, Or create a ticket in the server and describe your issue.
For direct contact with the owner DM/add @bbalcony on discord. <@1255858849282523182>

I hope you have fun playing my custom Bootstrapper :P
