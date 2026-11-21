; The Windows installer of the desktop app (Inno Setup 6). Built by build-installer.ps1, which
; publishes the app (self-contained, so no .NET is needed on the PC) and passes:
;   /DAppVersion=0.1.2.7   the version from git (see Directory.Build.props)
;   /DPublishDir=...       the published app
;   /DOutputDir=...        where the installer goes
; Installs for the current user or all users (the installer asks), with a Start-menu shortcut, an
; optional desktop shortcut and an uninstaller. A newer version installs over an older one.

#ifndef AppVersion
  #define AppVersion "0.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\out\publish-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\out"
#endif

[Setup]
; Keep the AppId: it's how a new version finds the installed one.
AppId={{6F1C2B3A-8E4D-4C1B-9A7E-2D5F0B1C3E47}
AppName=Lyrious
AppVersion={#AppVersion}
AppVerName=Lyrious {#AppVersion}
AppPublisher=Lyrious
AppPublisherURL=https://lyrious.se
AppSupportURL=https://lyrious.se
DefaultDirName={autopf}\Lyrious
DefaultGroupName=Lyrious
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Lyrious-{#AppVersion}-windows-x64-setup
SetupIconFile=..\icons\Lyrious.ico
UninstallDisplayIcon={app}\Lyrious.Desktop.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "swedish"; MessagesFile: "compiler:Languages\Swedish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Lyrious"; Filename: "{app}\Lyrious.Desktop.exe"
Name: "{autodesktop}\Lyrious"; Filename: "{app}\Lyrious.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Lyrious.Desktop.exe"; Description: "{cm:LaunchProgram,Lyrious}"; Flags: nowait postinstall skipifsilent
