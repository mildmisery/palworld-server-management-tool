# Palworld Server Manager

A Windows desktop tool built with Avalonia and .NET 10 for managing a local
Palworld dedicated server, its configuration, and world saves.

## AI usage disclosure

This project was developed with assistance from AI coding tools,
including GitHub Copilot. AI assistance was used for implementation, debugging,
documentation, and build and release setup.

AI-assisted code can contain mistakes, and successful builds do not guarantee
that every workflow is correct. Use this tool at your own risk and keep
independent backups of your server configuration and world saves. The developer
is not responsible for data loss.

## Features

### Configuration editor

- Grouped settings based on the documented Palworld server parameters, with
  descriptions, default-value labels, and per-field reset buttons.
- Crossplay platform checkboxes for Steam, Xbox, PS5, and Mac.
- Steam installation and library discovery, with a folder confirmation dialog
  and manual folder selection.
- Read and write Palworld's `OptionSettings` configuration format.
- Save and reload the current configuration, load another INI file, or use
  Save As. Loading another configuration makes it the current save target.
- A red unsaved-changes indicator.
- Load settings from `DefaultPalWorldSettings.ini` or update that template from
  the editor, with confirmation and a backup before replacing an existing
  template.

### Server controls and RCON

- Detect local Palworld server processes and display their running status.
- Select the server executable and specify launch arguments.
- Start, stop, and restart the server; restart saves the current settings first.
- Attempt graceful shutdown through RCON, with confirmation before force-stopping.
- Send free-form RCON commands and display responses and errors.
- Quick RCON commands for saving the running world, retrieving server information,
  and listing players.

### World save management

- List the active world, backups, and disabled worlds, with a selectable save
  folder.
- Back up or disable the active world, restore a backup, or enable a disabled
  world.
- Create a new world by backing up and disabling the current world and resetting
  the active configuration from the default template. Palworld creates the new
  world on its next start.
- Human-readable names for new backup and disabled-world entries, while preserving
  the world IDs required for restoration.
- Require the server to be stopped before save operations and ask for confirmation
  before world-changing actions.

**Worlds include their settings:** backups and disabled worlds contain a
`PalWorldSettings.ini` snapshot of the selected configuration file on disk.
Restoring a backup or enabling a disabled world replaces the current selected
configuration file with that snapshot and reloads the editor. Save or reload
unsaved edits before managing worlds. Snapshots can contain server and admin
passwords; keep backups private.

Older backups without a settings snapshot can still be loaded after an explicit
warning; their restore leaves the current configuration unchanged. Creating a
new world preserves the old world and its settings before resetting the active
configuration from the default template.

### Distribution and updates

- Self-contained Windows x64 installer and portable ZIP packages; users do not
  need to install .NET separately.
- GitHub Actions packaging and release publishing.
- Automatic startup and manual update checks against stable GitHub Releases,
  with confirmation before downloading and restarting the manager.

## Running from source

Install the .NET 10 SDK, then run from the project directory:

```powershell
dotnet run
```

The Palworld dedicated server must be installed separately. RCON commands and
graceful shutdown require RCON to be enabled and an admin password configured
on the server.

## Windows release packaging

Releases include both a Velopack installer (`*-Setup.exe`) and a portable ZIP
(`*-Portable.zip`). The portable ZIP can be extracted and run without an
installer. Keep the portable app folder writable for updates to be applied.

## App updates

Packaged releases check for stable GitHub Releases after the startup folder
dialog is confirmed. Use **Check for updates** to check again manually.
The app asks before downloading an update and restarting. Save or reload any
unsaved settings first; editing is disabled during the download. Updating the
manager does not stop the Palworld server or change its settings and save files.
Failed checks or downloads show an error beside the update button and can be
retried. Downloaded updates are not applied automatically on the next launch.

Self-updates are available in Velopack's installer and portable packages, not
when running with `dotnet run`. Existing releases without update-checking code
must be updated manually once to a release containing this feature.

Publish a higher version using the release workflow and retain its full
`.nupkg` package and `releases.win.json` assets on GitHub alongside the user
downloads. These assets are needed for self-updates. Prereleases are excluded.

## Building packages

To package locally, install the .NET 10 SDK and Velopack CLI, then run:

```powershell
dotnet tool install --global vpk --version 1.2.161
.\scripts\package-windows.ps1 -Version 0.1.0
```

Packages are written to `artifacts\releases\<version>`.
Packages are currently unsigned, so Windows may show publisher or SmartScreen
warnings. Set up code signing before distributing broadly.

To publish a GitHub release, push a version tag such as `v0.1.0`. The
`Package Windows release` workflow builds the installer and portable ZIP and
attaches them to the release. You can also run the workflow manually and enter
a semantic version. Windows packages are currently built for x64.
