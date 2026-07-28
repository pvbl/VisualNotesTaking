#define AppVersion GetEnv("VISUALNOTES_VERSION")
#define PublishDir GetEnv("VISUALNOTES_PUBLISH_DIR")
#define OutputDir GetEnv("VISUALNOTES_OUTPUT_DIR")

[Setup]
AppId={{C9BB95BB-43BA-41CD-90E8-5C1167722BAA}
AppName=VisualNotes
AppVersion={#AppVersion}
DefaultDirName={autopf}\VisualNotes
DefaultGroupName=VisualNotes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
Compression=lzma2
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=VisualNotes-{#AppVersion}-win-x64-setup
UninstallDisplayIcon={app}\VisualNotes.App.exe
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
PrivilegesRequired=admin

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VisualNotes"; Filename: "{app}\VisualNotes.App.exe"
Name: "{autodesktop}\VisualNotes"; Filename: "{app}\VisualNotes.App.exe"; Tasks: desktopicon

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\VisualNotes.App.exe"; Description: "Launch VisualNotes"; Flags: nowait postinstall skipifsilent

; User data intentionally lives under LocalAppData (or a folder selected at first
; run). Never add an [UninstallDelete] entry for those locations: upgrades, repair
; and uninstall must preserve sessions, credentials and configuration.
