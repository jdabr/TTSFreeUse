; Inno Setup script for the TTSFreeUse Windows installer.
; Build with installer\build-installer.ps1 (needs Inno Setup 6: https://jrsoftware.org/isinfo.php).

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{6F1D5C2A-8B3E-4C7A-9D2F-1E5B7A3C9F40}
AppName=TTSFreeUse
AppVersion={#AppVersion}
AppVerName=TTSFreeUse {#AppVersion}
AppPublisher=TTSFreeUse contributors
AppPublisherURL=https://github.com/jdabr/TTSFreeUse
AppSupportURL=https://github.com/jdabr/TTSFreeUse/issues
AppCopyright=Copyright (c) 2026 Jdabr
; Installs for the current user by default (no admin password needed), into
; %LOCALAPPDATA%\Programs\TTSFreeUse. People can choose "all users" if they're an admin.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\TTSFreeUse
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=TTSFreeUse-Setup-{#AppVersion}
SetupIconFile=..\TTSFreeUse.Desktop\app.ico
UninstallDisplayIcon={app}\TTSFreeUse.exe
UninstallDisplayName=TTSFreeUse
InfoBeforeFile=before-you-install.txt
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The app holds this mutex while running, so setup/uninstall can ask to close it.
AppMutex=TTSFreeUse.Desktop
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\TTSFreeUse"; Filename: "{app}\TTSFreeUse.exe"; Comment: "Listen to your documents"
Name: "{autodesktop}\TTSFreeUse"; Filename: "{app}\TTSFreeUse.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TTSFreeUse.exe"; Description: "{cm:LaunchProgram,TTSFreeUse}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Large files the app downloaded (voice model ~330 MB, audio converter ~110 MB).
; Saved audio in Documents\TTSFreeUse and the pronunciation list are kept.
Type: filesandordirs; Name: "{localappdata}\TTSFreeUse\models"
Type: filesandordirs; Name: "{localappdata}\TTSFreeUse\tools"
