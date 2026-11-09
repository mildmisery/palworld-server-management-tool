# palworld-server-management-tool
This is a tool to manage your palworld dedicated server settings and save files

## Windows release packaging

Releases include both a Velopack installer (`*-Setup.exe`) and a portable ZIP
(`*-Portable.zip`). The portable ZIP can be extracted and run without an
installer. Velopack startup hooks are included as an update foundation; in-app
update checking is not implemented yet. When it is added, the portable app
folder must remain writable for updates to be applied.

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
