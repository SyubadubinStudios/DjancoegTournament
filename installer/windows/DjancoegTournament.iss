; Inno Setup 6 script for the Windows installer.
; Built by installer/build-windows.ps1 (or CI):  iscc /DAppVersion=2.1.0 /DSourceDir=<publish dir> /DOutputDir=<out> DjancoegTournament.iss
; Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\DjancoegTournament-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\installers"
#endif

#define AppName "Djancoeg Tournament"
#define AppExe "DjancoegTournament.exe"
#define ServerExe "djancoeg-server.exe"
#define Publisher "Syubadubin Studios"

[Setup]
AppId={{7C1B4E8A-3D5F-4B2A-9E61-D7A1C0F4B2D9}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://github.com/SyubadubinStudios/DjancoegTournament
AppSupportURL=https://github.com/SyubadubinStudios/DjancoegTournament/issues
AppUpdatesURL=https://github.com/SyubadubinStudios/DjancoegTournament/releases
AppCopyright=Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} - Ultimate Indonesian Chaos Battle Arena
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install by default (no admin prompt); the dialog lets players pick "all users".
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=DjancoegTournament-Setup-win-x64
SetupIconFile=..\..\assets\icon\djancoeg.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.ServerShortcut=Dedicated server (LAN/online host)
english.ModsShortcut=Mods folder
english.FirewallRule=Allow the game and server through Windows Firewall (LAN/online play)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "firewall"; Description: "{cm:FirewallRule}"; GroupDescription: "Network"; Check: IsAdminInstallMode

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,.itch.toml"
; Tells the game it is installed (read-only) so mods, editor arenas and Workshop downloads go to %APPDATA%.
Source: "installed.marker"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autoprograms}\{#AppName} - {cm:ServerShortcut}"; Filename: "{app}\{#ServerExe}"; Parameters: "--bots 6"; WorkingDir: "{userappdata}\DjancoegTournament"
Name: "{autoprograms}\{#AppName} - {cm:ModsShortcut}"; Filename: "{userappdata}\DjancoegTournament\mods"
Name: "{autoprograms}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Dirs]
Name: "{userappdata}\DjancoegTournament\mods"

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#AppName}"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes"; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#AppName} server"" dir=in action=allow program=""{app}\{#ServerExe}"" enable=yes"; Flags: runhidden; Tasks: firewall
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#AppName}"""; Flags: runhidden; RunOnceId: "fw1"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#AppName} server"""; Flags: runhidden; RunOnceId: "fw2"

; Player saves (%APPDATA%\DjancoegTournament) are deliberately kept on uninstall.
