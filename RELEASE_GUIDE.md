# Publishing Viora on GitHub

Repository: https://github.com/balcony3232/Viora

## Upload once

Use GitHub Desktop or `git` to upload all source files, including the hidden `.github/workflows/windows-release.yml` and `.gitignore`.
Do not upload any `%LOCALAPPDATA%` files, your personal stats, passwords, splash GIFs or backups.

## Validate Windows executable

Go to **Actions > Windows EXE build and release > Run workflow** on the `main` branch.
Download the `Viora-Windows-exe` artifact from the successful workflow and test `Viora-v0.9.0.exe` on Windows.

## Publish release

After the build passes: tag the source commit `v0.9.0` and push that tag.
GitHub Actions will build it again and publish a GitHub Release with the EXE and SHA-256 file.
The version tag must match `<Version>` in `Viora.csproj`.

Ensure repository Actions permissions allow creating releases, and do not publish if the build fails.

## Updating Viora

In the app Updates/Recovery page the repository should say `balcony3232/Viora`.
Choose Check for updates. The latest published release must have the filename
`Viora-vVERSION.exe` and matching `.sha256`. Viora verifies checksum before download;
it does NOT automatically replace running versions.
