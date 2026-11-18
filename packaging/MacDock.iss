#ifndef BuildVersion
  #error BuildVersion is required
#endif
#ifndef Payload
  #error Payload is required
#endif
#ifndef OutputFolder
  #error OutputFolder is required
#endif
#ifndef ProductId
  #define ProductId "MacDock.Desktop"
#endif
#ifndef ProductName
  #define ProductName "MacDock"
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#BuildVersion}
AppPublisher=MacDock
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
AppMutex=Local\MacDock.Desktop.v1
SetupMutex=Local\MacDock.Setup.v1
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\versions\{#BuildVersion}\MacDock.exe
SetupIconFile=..\Assets\Brand\MacDock.ico
OutputDir={#OutputFolder}
OutputBaseFilename=MacDock-{#BuildVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=yes
UsePreviousLanguage=no
VersionInfoVersion={#BuildVersion}
VersionInfoCompany=MacDock
VersionInfoDescription=MacDock Windows installer

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zhcn"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
en.DesktopShortcut=Create a desktop shortcut
zhcn.DesktopShortcut=创建桌面快捷方式
en.StartOnSignIn=Start MacDock when I sign in
zhcn.StartOnSignIn=登录 Windows 时启动 MacDock
en.LaunchMacDock=Start MacDock
zhcn.LaunchMacDock=启动 MacDock

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartOnSignIn}"; Flags: unchecked

[Files]
Source: "{#Payload}\*"; DestDir: "{app}\versions\{#BuildVersion}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\versions\{#BuildVersion}\MacDock.exe"; Check: ShortcutAvailable(ExpandConstant('{userprograms}\{#ProductName}.lnk')); Flags: uninsneveruninstall
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\versions\{#BuildVersion}\MacDock.exe"; Check: DesktopShortcutWanted; Flags: uninsneveruninstall

[Run]
Filename: "{app}\versions\{#BuildVersion}\MacDock.exe"; Description: "{cm:LaunchMacDock}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  StartupTaskName = 'MacDock';

function ManagedExecutable(Path: String): Boolean;
var Base: String;
begin
  Base := ExpandConstant('{app}\versions\');
  Result := (CompareText(Copy(Path, 1, Length(Base)), Base) = 0) and
    (CompareText(ExtractFileName(Path), 'MacDock.exe') = 0);
end;

function CurrentUserAccount: String;
var Domain, User: String;
begin
  User := GetUserNameString;
  Domain := GetEnv('USERDOMAIN');
  if (Domain <> '') and (CompareText(Domain, User) <> 0) then
    Result := Domain + '\' + User
  else
    Result := User;
end;

function QueryOwnStartupTask(var TargetsOwn: Boolean): Boolean;
var
  Service, Folder, Task, Actions, EmptyVar: Variant;
  ActionPath: String;
begin
  Result := False;
  TargetsOwn := False;
  try
    Service := CreateOleObject('Schedule.Service');
    Service.Connect(EmptyVar, EmptyVar, EmptyVar, EmptyVar);
    Folder := Service.GetFolder('\');
    Task := Folder.GetTask(StartupTaskName);
    Result := True;
    Actions := Task.Definition.Actions;
    if Actions.Count >= 1 then begin
      ActionPath := Actions.Item[1].Path;
      TargetsOwn := ManagedExecutable(ActionPath);
    end;
  except
  end;
end;

function OwnRunStartup: Boolean;
var Current: String;
begin
  Result := RegQueryStringValue(HKCU, RunKey, 'MacDock', Current) and
    ManagedExecutable(RemoveQuotes(Current));
end;

function OwnStartup: Boolean;
var TargetsOwnTask: Boolean;
begin
  QueryOwnStartupTask(TargetsOwnTask);
  Result := OwnRunStartup or TargetsOwnTask;
end;

function TryRegisterStartupTask(ExePath: String): Boolean;
var
  Service, Folder, Task, Trigger, Action, EmptyVar: Variant;
  UserId: String;
begin
  Result := False;
  try
    UserId := CurrentUserAccount;
    Service := CreateOleObject('Schedule.Service');
    Service.Connect(EmptyVar, EmptyVar, EmptyVar, EmptyVar);
    Folder := Service.GetFolder('\');
    Task := Service.NewTask(0);
    Task.RegistrationInfo.Description := 'MacDock 开机登录快速启动';
    Task.Principal.UserId := UserId;
    Task.Principal.LogonType := 3;
    Task.Principal.RunLevel := 0;
    Task.Settings.Enabled := True;
    Task.Settings.DisallowStartIfOnBatteries := False;
    Task.Settings.StopIfGoingOnBatteries := False;
    Task.Settings.ExecutionTimeLimit := 'PT0S';
    Task.Settings.AllowDemandStart := True;
    Task.Settings.StartWhenAvailable := True;
    Task.Settings.MultipleInstances := 2;
    Task.Settings.Compatibility := 2;
    Trigger := Task.Triggers.Create(9);
    Trigger.UserId := UserId;
    Trigger.Enabled := True;
    Action := Task.Actions.Create(0);
    Action.Path := ExePath;
    Action.WorkingDirectory := ExtractFileDir(ExePath);
    Folder.RegisterTaskDefinition(StartupTaskName, Task, 6, EmptyVar, EmptyVar, 3, EmptyVar);
    Result := True;
  except
    Log('Schedule.Service registration failed; falling back to Run key.');
  end;
end;

procedure DeleteStartupTask;
var
  Service, Folder, EmptyVar: Variant;
  ResultCode: Integer;
begin
  try
    Service := CreateOleObject('Schedule.Service');
    Service.Connect(EmptyVar, EmptyVar, EmptyVar, EmptyVar);
    Folder := Service.GetFolder('\');
    Folder.DeleteTask(StartupTaskName, 0);
    Exit;
  except
  end;
  Exec('schtasks.exe', '/Delete /F /TN "MacDock"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function ManagedShortcut(Path: String): Boolean;
var Shell, Link: Variant;
begin
  Result := False;
  if not FileExists(Path) then Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Link := Shell.CreateShortcut(Path);
    Result := ManagedExecutable(Link.TargetPath);
  except
    Log('Could not verify an existing shortcut. Leaving it intact.');
  end;
end;

function ShortcutAvailable(Path: String): Boolean;
begin
  Result := (not FileExists(Path)) or ManagedShortcut(Path);
end;

function DesktopShortcutWanted: Boolean;
var Path: String;
begin
  Path := ExpandConstant('{userdesktop}\{#ProductName}.lnk');
  Result := ManagedShortcut(Path) or (WizardIsTaskSelected('desktopicon') and not FileExists(Path));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var ExePath: String;
begin
  if CurStep = ssPostInstall then begin
    if WizardIsTaskSelected('startup') or OwnStartup then begin
      ExePath := ExpandConstant('{app}\versions\{#BuildVersion}\MacDock.exe');
      if TryRegisterStartupTask(ExePath) then
        RegDeleteValue(HKCU, RunKey, 'MacDock')
      else
        RegWriteStringValue(HKCU, RunKey, 'MacDock', '"' + ExePath + '"');
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartLink, DesktopLink: String;
  TargetsOwnTask: Boolean;
begin
  if CurUninstallStep = usUninstall then begin
    StartLink := ExpandConstant('{userprograms}\{#ProductName}.lnk');
    DesktopLink := ExpandConstant('{userdesktop}\{#ProductName}.lnk');
    if ManagedShortcut(StartLink) then DeleteFile(StartLink);
    if ManagedShortcut(DesktopLink) then DeleteFile(DesktopLink);
  end;
  if CurUninstallStep = usPostUninstall then begin
    QueryOwnStartupTask(TargetsOwnTask);
    if TargetsOwnTask then
      DeleteStartupTask;
    if OwnRunStartup then
      RegDeleteValue(HKCU, RunKey, 'MacDock');
  end;
end;
