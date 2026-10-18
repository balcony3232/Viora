; Viora per-user installer, with an optional separate Viora Manager application.
#define AppVersion GetEnv("VIORA_VERSION")
#if AppVersion == ""
  #define AppVersion "1.3.0"
#endif

[Setup]
AppId=Viora.Windows.User
AppName=Viora
AppVersion={#AppVersion}
AppVerName=Viora {#AppVersion}
AppPublisher=Viora Contributors
DefaultDirName={localappdata}\Programs\Viora
DefaultGroupName=Viora
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish
OutputBaseFilename=Viora-v{#AppVersion}-Setup
SetupIconFile=..\Assets\Viora.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Viora.exe
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
; One custom installation type removes the Full/Compact dropdown.
; The optional Manager appears as a normal check box, checked by default.
Name: "custom"; Description: "Select components"; Flags: iscustom

[Components]
Name: "main"; Description: "Viora Launcher (required)"; Types: custom; Flags: fixed
Name: "manager"; Description: "Install Viora Manager (optional)"; Types: custom

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcuts"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\publish\Viora-v{#AppVersion}.exe"; DestDir: "{app}"; DestName: "Viora.exe"; Flags: ignoreversion; Components: main
Source: "..\publish\VioraManager-v{#AppVersion}.exe"; DestDir: "{app}"; DestName: "VioraManager.exe"; Flags: ignoreversion; Components: manager

[Icons]
Name: "{autoprograms}\Viora"; Filename: "{app}\Viora.exe"; Components: main
Name: "{autodesktop}\Viora"; Filename: "{app}\Viora.exe"; Components: main; Tasks: desktopicon
Name: "{autoprograms}\Viora Manager"; Filename: "{app}\VioraManager.exe"; Components: manager
Name: "{autodesktop}\Viora Manager"; Filename: "{app}\VioraManager.exe"; Components: manager; Tasks: desktopicon

[Run]
Filename: "{app}\Viora.exe"; Description: "Launch Viora"; Flags: nowait postinstall skipifsilent; Components: main

; Keep existing AppData on uninstall; offer manual deletion after personal backups.
