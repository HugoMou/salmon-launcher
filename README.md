# SalmonLauncher (SL)

**English** | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-Hant.md) | [日本語](README.ja.md)

A Minecraft Java Edition launcher built with **WinUI 3 / Windows App SDK** and **.NET 10**. Installation, launching, instances and mods, multiplayer, skin preview, crash analysis and theming all live inside one two-pane interface.

- Version: 1.0.3
- Platform: Windows 10 (2004+) / Windows 11, x64
- Licence: GNU General Public License v3.0 or later (GPL-3.0-or-later)

---

## Features

| Area | What it does |
| --- | --- |
| Launching | Instance picker, memory and window size, extra JVM arguments, one-click launch with live status |
| Installation | Official Mojang version manifest, loaders (Fabric / Quilt / Forge / NeoForge), mirror fallback, resume, SHA-1 verification |
| Instances and mods | Per-instance folders, mod toggling (`.jar` ↔ `.jar.disabled`), importing local mods, save backups |
| Multiplayer | Self-hosted relay, LAN scanning and manual relay |
| Content | Modrinth search for mods, shaders and resource packs, installed into the selected instance |
| Appearance | Four languages, light and dark themes, custom accent colour, block-colour theme workshop, wallpapers |
| Diagnostics | Reads the tail of `logs/latest.log`, matches crash signatures and suggests fixes |
| Accounts | Microsoft sign-in (built-in browser, device code, system browser) and unlimited offline accounts |

## Requirements

- Windows 10 version 2004 (build 19041) or newer, or Windows 11; x64 only.
- The first launch needs an internet connection to fetch the version manifest. Game files, libraries and assets come from Mojang's official sources or public mirrors.
- Java is downloaded on demand (Eclipse Temurin) by default; an existing local Java installation can be used instead.

## Download and install

**Latest release: v1.0.3**

| Package | File | Notes |
| --- | --- | --- |
| Installer (recommended) | `SL-Setup-1.0.3-win64.exe` | Standard Windows setup wizard with **Simplified Chinese / Traditional Chinese / English / Japanese** UI; adds a Start menu shortcut and an uninstall entry; can install for the current user without administrator rights |
| Portable | `SL-1.0.3-portable-win64.zip` | Unzip and run; writes nothing to the registry; handy on a USB drive |

Both packages bundle the complete .NET 10 and Windows App SDK runtimes, so **no separate runtime installation is needed**. Download: [GitHub Releases](https://github.com/HugoMou/salmonlauncher/releases).

The installer and uninstaller are signed with a self-signed certificate (CN=SalmonLauncher, O=Hugo Mou), generated locally by the build script. Machines that have not imported that certificate may still see "Unknown publisher" in SmartScreen; that is expected for self-signing and does not mean the file was altered — check the SHA-256 instead. Uninstalling keeps instances and saves under `%APPDATA%\SalmonLauncher`.

## Build

You need the .NET 10 SDK and Windows App SDK 1.8 (restored automatically through NuGet). From the repository root:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build .\SalmonLauncher.sln -c Debug -p:Platform=x64
```

The output lands in:

```
src\SL.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\SL.exe
```

Use `-c Release` for a release build. `TreatWarningsAsErrors` is enabled, so any warning fails the build.

### Build the installer and portable package

```powershell
.\installer\build-installer.ps1 -Version 1.0.3
```

The script runs `dotnet publish` (Release, self-contained), compiles `installer\SL.iss` with Inno Setup, and writes `SL-Setup-<version>-win64.exe` plus `SL-<version>-portable-win64.zip` into `dist\`. [Inno Setup 6](https://jrsoftware.org/isinfo.php) is required.

## Project layout

```
src\SL.App    Presentation (WinUI 3 pages, controls, navigation, localization, themes)
src\SL.Core   Business logic (transfer engine, launch pipeline, metadata, accounts, diagnostics, multiplayer)
src\SL.Data   Data layer (folder layout, atomic writes, settings and cache)
```

Dependencies flow strictly one way: `SL.App → SL.Core → SL.Data`.

## Data folder

Settings, accounts, logs, instances and worlds all stay on your machine:

```
%APPDATA%\SalmonLauncher
```

Copy the whole folder to migrate to another PC. Uninstalling the app does not remove instances or worlds.

## Download sources

- Official: `piston-meta.mojang.com`, `libraries.minecraft.net`, `resources.download.minecraft.net`
- China mirror: `bmclapi2.bangbang93.com` (mirror-first can be toggled in settings)
- Java: `api.adoptium.net`, TUNA mirror, GitHub Releases
- Modrinth: official API and a China mirror (toggleable)

Every downloaded file is finally verified with SHA-1 or SHA-256, and is re-downloaded on mismatch.

## Languages

The interface ships in **简体中文**, **繁體中文**, **English** and **日本語**, follows the system language by default, and can be switched anytime under Appearance & settings. The licence and terms texts are available in all four languages too.

## Copyright and licence

```
SalmonLauncher (SL)
Copyright (C) 2026 Hugo Mou

This project is released under the GNU General Public License, version 3
or any later version. The full licence text is in the LICENSE file at the
repository root, or at https://www.gnu.org/licenses/gpl-3.0.html
```

Under the GPL you may run, study, modify and redistribute this software, including for a fee, as long as you keep the copyright and licence notices and give recipients the complete corresponding source code.

## Disclaimer

- This is a free third-party tool with no affiliation or authorization from **Mojang Studios** or **Microsoft**. **Minecraft** is a trademark of Mojang Studios.
- The app does not bundle, distribute or modify any Minecraft game files; they are fetched from official sources or public mirrors.
- Mods, shaders and resource packs come from public platforms such as Modrinth and remain the property of their authors.
- The software is provided "as is", without any express or implied warranty. Back up your worlds and instances before important operations.
- Using this app means you have read and accepted the Terms of Use and Disclaimer shown inside the app.
