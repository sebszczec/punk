; Inno Setup script for Punk. build.ps1 compiles it with /DVersion, /DSourceDir and /DOutputDir.
; Per-user install (no admin), so the in-app updater can replace the files in place.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist\release"
#endif

[Setup]
; Keep this GUID fixed: a newer Setup with the same AppId upgrades the existing install.
AppId={{4F1C9B2E-7A3D-4C58-B0E6-91D2F8A5C743}
AppName=Punk
AppVersion={#Version}
AppVerName=Punk {#Version}
AppPublisher=Punk
DefaultDirName={autopf}\Punk
DefaultGroupName=Punk
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Setup-Punk-{#Version}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=punk.ico
UninstallDisplayName=Punk
UninstallDisplayIcon={app}\Punk.exe
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce

; Shortcuts start Punk through conhost.exe (classic console window). A console app has no window of its own,
; and Windows Terminal would show its own icon on the taskbar; conhost takes the icon from the shortcut and exe.
[Icons]
Name: "{group}\Punk"; Filename: "{sys}\conhost.exe"; Parameters: """{app}\Punk.exe"""; WorkingDir: "{app}"; IconFilename: "{app}\Punk.exe"
Name: "{group}\Uninstall Punk"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Punk"; Filename: "{sys}\conhost.exe"; Parameters: """{app}\Punk.exe"""; WorkingDir: "{app}"; IconFilename: "{app}\Punk.exe"; Tasks: desktopicon

[InstallDelete]
; Leftovers of an in-app update (see Punk/Update/UpdateApplier.cs).
Type: files; Name: "{app}\*.old"
Type: files; Name: "{app}\*.new"

[UninstallDelete]
Type: files; Name: "{app}\*.old"
Type: files; Name: "{app}\*.new"

[Run]
Filename: "{sys}\conhost.exe"; Parameters: """{app}\Punk.exe"""; WorkingDir: "{app}"; Description: "Launch Punk"; Flags: nowait postinstall skipifsilent

; The user's settings live in %APPDATA%\Punk and are intentionally left alone on uninstall.
