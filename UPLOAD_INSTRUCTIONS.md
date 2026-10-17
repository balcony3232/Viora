# Upload the Viora v0.9.0 source

1. Download the Viora v0.9.0 source ZIP from your ChatGPT conversation and extract it.
2. In GitHub Desktop, clone `balcony3232/Viora`.
3. Copy **the contents** of the extracted source folder into your cloned repo, including `.github/workflows/windows-release.yml`.
4. Before committing, inspect the changed files. **Never include** `%LOCALAPPDATA%` data, passwords, `settings.json`, `local-account.json`, stats, backups or private splash GIFs.
5. Commit and push to `main`.
6. Open **Actions → Windows EXE build and release → Run workflow** to build the Windows executable.
7. Download the build artifact and test it before publishing. Only then push a matching `v0.9.0` tag to create a GitHub Release with the `.exe` and `.sha256` files.

The updater needs a published release; merely creating the repo is not sufficient. Until then, v0.9.0's update check will report no release.
