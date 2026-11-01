; Salmon Launcher (SL) — installer script for Inno Setup 6
; Copyright (C) 2026 Hugo Mou
; SPDX-License-Identifier: GPL-3.0-or-later
;
; 构建方式:先用 dotnet publish 生成自包含产物到 artifacts\publish,
; 再执行 ISCC.exe /DAppVersion=1.0.0 installer\SL.iss
; 或直接运行 installer\build-installer.ps1。

#define AppName "Salmon Launcher"
#define AppExe "SL.exe"
#define AppPublisher "Hugo Mou"
#define AppUrl "https://github.com/HugoMou/salmon-launcher"

#ifndef AppVersion
  #define AppVersion "1.0.0"
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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 用户数据(实例、存档、日志、Java)位于 %APPDATA%\SalmonLauncher,
; 卸载时保留,避免误删游戏内容。