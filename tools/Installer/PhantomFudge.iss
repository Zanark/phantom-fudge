#ifndef PayloadDir
  #error PayloadDir must name the verified release payload.
#endif
#ifndef AppVersion
  #define AppVersion "0.2.0-preview.3"
#endif
#ifndef WindowsVersion
  #define WindowsVersion "0.2.0.3"
#endif
#ifndef InstallerId
  #define InstallerId "PhantomFudge-7192D36D-377C-428A-A03E-2F47C90E4E8B"
#endif

[Setup]
AppId={#InstallerId}
AppName=Phantom Fudge
AppVersion={#AppVersion}
AppVerName=Phantom Fudge {#AppVersion}
AppPublisher=Zanark
AppPublisherURL=https://github.com/Zanark/phantom-fudge
AppSupportURL=https://github.com/Zanark/phantom-fudge/issues
AppUpdatesURL=https://github.com/Zanark/phantom-fudge/releases
DefaultDirName={localappdata}\Programs\PhantomFudge
DefaultGroupName=Phantom Fudge
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
VersionInfoVersion={#WindowsVersion}
VersionInfoDescription=Phantom Fudge installer
OutputBaseFilename=PhantomFudge-{#AppVersion}-win-x64-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\launcher\PhantomFudge.Launcher.exe
UninstallDisplayName=Phantom Fudge
SetupMutex=PhantomFudgeInstaller
InfoBeforeFile={#PayloadDir}\INSTALLATION-NOTES.txt

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\launcher\*"; DestDir: "{tmp}\PhantomFudgeMaintenance"; Flags: dontcopy recursesubdirs
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\PhantomFudge\Installer\{#InstallerId}"; ValueType: string; ValueName: "WindowsVersion"; ValueData: "{#WindowsVersion}"; Flags: uninsdeletekey

[InstallDelete]
Type: filesandordirs; Name: "{app}\app"
Type: filesandordirs; Name: "{app}\launcher"
Type: filesandordirs; Name: "{app}\runtime"
Type: filesandordirs; Name: "{app}\services"
Type: filesandordirs; Name: "{app}\licenses"
Type: filesandordirs; Name: "{app}\docs"

[Icons]
#ifndef InstallerTest
Name: "{userprograms}\Phantom Fudge"; Filename: "{app}\launcher\PhantomFudge.Launcher.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Phantom Fudge"; Filename: "{app}\launcher\PhantomFudge.Launcher.exe"; WorkingDir: "{app}"; Tasks: desktopicon
#endif

[Code]
const
  InstallationMarker = 'phantom-fudge.install.v1';

function InitializeSetup(): Boolean;
begin
  CreateMutex('Local\PhantomFudge-Installer-Maintenance');
  Result := True;
end;

function IsRecognizedInstallation(const Directory: String): Boolean;
var
  Marker: AnsiString;
begin
  Result := LoadStringFromFile(AddBackslash(Directory) + 'app-installation.id', Marker) and
    (Trim(String(Marker)) = InstallationMarker);
end;

function DirectoryHasFiles(const Directory: String): Boolean;
var
  Item: TFindRec;
begin
  Result := False;
  if FindFirst(AddBackslash(Directory) + '*', Item) then
  begin
    try
      repeat
        if (Item.Name <> '.') and (Item.Name <> '..') then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(Item);
    finally
      FindClose(Item);
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  InstalledVersion: AnsiString;
  RegisteredVersion: String;
  Version: Int64;
  CurrentVersion: Int64;
begin
  Result := '';
  if not DirExists(ExpandConstant('{app}')) or not DirectoryHasFiles(ExpandConstant('{app}')) then
    Exit;
  if not IsRecognizedInstallation(ExpandConstant('{app}')) then
  begin
    Result := 'The destination is not a recognized Phantom Fudge installation. No files were changed.';
    Exit;
  end;
  if not LoadStringFromFile(ExpandConstant('{app}\app-version.txt'), InstalledVersion) or
     not StrToVersion(Trim(String(InstalledVersion)), Version) then
  begin
    if not RegQueryStringValue(HKCU, 'Software\PhantomFudge\Installer\{#InstallerId}',
         'WindowsVersion', RegisteredVersion) or not StrToVersion(RegisteredVersion, Version) then
    begin
      Result := 'The installed and registered version records are invalid. Restore the version record from your matching release before updating.';
      Exit;
    end;
  end;
  if not StrToVersion('{#WindowsVersion}', CurrentVersion) then
  begin
    Result := 'The installed version record is invalid. Repair the installation before updating.';
    Exit;
  end;
  if ComparePackedVersion(Version, CurrentVersion) > 0 then
  begin
    Result := 'A newer Phantom Fudge version is installed. Downgrades are not supported.';
    Exit;
  end;
  ExtractTemporaryFiles('{tmp}\PhantomFudgeMaintenance\*');
  if not Exec(ExpandConstant('{tmp}\PhantomFudgeMaintenance\PhantomFudge.Launcher.exe'),
      '--maintenance "' + ExpandConstant('{app}') + '"', '',
      SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
    Result := 'Close Phantom Fudge with X after saving your text, stop any active transcription, and retry. No process was force-closed. See the local launcher status for details.';
end;

function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
begin
  CreateMutex('Local\PhantomFudge-Installer-Maintenance');
  Result := IsRecognizedInstallation(ExpandConstant('{app}'));
  if Result then
    Result := Exec(ExpandConstant('{app}\launcher\PhantomFudge.Launcher.exe'), '--maintenance', '',
      SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
  if not Result then
    SuppressibleMsgBox('Uninstall was stopped. Close Phantom Fudge with X and stop transcription first. If launcher files are missing, repair using the same installer. Your saved files have not been removed.', mbError, MB_OK, IDOK);
end;
