; Viora per-user installer, with an optional separate Viora Manager application.
#define AppVersion GetEnv("VIORA_VERSION")
#if AppVersion == ""
  #define AppVersion "1.2.0"
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
Name: "full"; Description: "Viora + Viora Manager"
Name: "compact"; Description: "Viora only"

[Components]
Name: "main"; Description: "Viora Launcher (required)"; Types: full compact; Flags: fixed
Name: "manager"; Description: "Viora Manager (optional session companion)"; Types: full

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
