; Viora per-user installer. Build after dotnet publish has produced Viora-vVERSION.exe.
#define AppVersion GetEnv("VIORA_VERSION")
#if AppVersion == ""
  #define AppVersion "1.1.0"
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

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\publish\Viora-v{#AppVersion}.exe"; DestDir: "{app}"; DestName: "Viora.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Viora"; Filename: "{app}\Viora.exe"
Name: "{autodesktop}\Viora"; Filename: "{app}\Viora.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Viora.exe"; Description: "Launch Viora"; Flags: nowait postinstall skipifsilent

; AppData is intentionally not removed on uninstall. Users can delete
; %LOCALAPPDATA%\Viora manually after exporting any wanted settings.
