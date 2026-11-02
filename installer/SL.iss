; SalmonLauncher (SL) — installer script for Inno Setup 6
; Copyright (C) 2026 Hugo Mou
; SPDX-License-Identifier: GPL-3.0-or-later
;
; Build: run dotnet publish to produce the self-contained output in artifacts\publish,
; then run ISCC.exe /DAppVersion=1.0.3 installer\SL.iss
; or simply run installer\build-installer.ps1.

#define AppName "SalmonLauncher"
#define AppExe "SL.exe"
#define AppPublisher "Hugo Mou"
#define AppUrl "https://github.com/HugoMou/salmonlauncher"

#ifndef AppVersion
  #define AppVersion "1.0.3"
#endif

#define BuildDir "..\artifacts\publish"

[Setup]
AppId={{6F1A4C2E-9B7D-4E83-A5C1-2D7F8E9A0B3C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
OutputDir=..\dist
OutputBaseFilename=SL-Setup-{#AppVersion}-win64
SetupIconFile=..\src\SL.App\Assets\Logo.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
LicenseFile=..\LICENSE
CloseApplications=yes

; Four-language setup wizard; .isl files live in the Inno Setup 6 Languages folder.
[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\Assets\Logo.ico"; IconIndex: 0
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\Assets\Logo.ico"; IconIndex: 0; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User data (instances, saves, logs, Java) lives in %APPDATA%\SalmonLauncher and is
; kept on uninstall so game content is never deleted by accident.