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
AppVerName={#AppName}
AppPublisher=lukewire129
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
Name: "addtopath"; Description: "Add TokenDock to PATH"; GroupDescription: "Command line:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  EnvironmentKey = 'Environment';
  PathValue = 'Path';

function GetUserPath(): string;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, PathValue, Result) then
    Result := '';
end;

function PathContains(FullPath: string; Entry: string): Boolean;
begin
  Result := Pos(';' + Lowercase(Entry) + ';', ';' + Lowercase(FullPath) + ';') > 0;
end;

procedure SetUserPath(Value: string);
begin
  RegWriteStringValue(HKCU, EnvironmentKey, PathValue, Value);
end;

procedure AddToUserPath(Entry: string);
var
  CurrentPath: string;
begin
  CurrentPath := GetUserPath();
  if PathContains(CurrentPath, Entry) then
    Exit;

  if CurrentPath = '' then
    SetUserPath(Entry)
  else
    SetUserPath(CurrentPath + ';' + Entry);
end;

procedure RemoveFromUserPath(Entry: string);
var
  CurrentPath: string;
  NormalizedPath: string;
begin
  CurrentPath := GetUserPath();
  NormalizedPath := ';' + CurrentPath + ';';
  StringChange(NormalizedPath, ';' + Entry + ';', ';');

  if Length(NormalizedPath) > 0 then
  begin
    if Copy(NormalizedPath, 1, 1) = ';' then
      Delete(NormalizedPath, 1, 1);
    if Copy(NormalizedPath, Length(NormalizedPath), 1) = ';' then
      Delete(NormalizedPath, Length(NormalizedPath), 1);
  end;

  if NormalizedPath <> CurrentPath then
    SetUserPath(NormalizedPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToUserPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromUserPath(ExpandConstant('{app}'));
end;
