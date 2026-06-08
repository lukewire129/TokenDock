#define AppName "TokenDock"
#define AppExeName "TokenDock.exe"
#define AppVersion GetEnv("APP_VERSION")
#define PublishDir GetEnv("PUBLISH_DIR")
#define InstallerDir GetEnv("INSTALLER_DIR")
#define SourceRoot GetEnv("GITHUB_WORKSPACE")

#if AppVersion == ""
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{9A8452FE-794B-4BE6-8F66-178A603A36EA}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=TokenDock
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#InstallerDir}
OutputBaseFilename=TokenDockSetup-v{#AppVersion}-win-x64
SetupIconFile={#SourceRoot}\src\TokenDock\Assets\AppIcon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
