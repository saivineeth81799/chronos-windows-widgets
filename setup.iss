; Inno Setup configuration script for Chronos Widgets
#define MyAppName "Chronos Widgets"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Chronos"
#define MyAppExeName "WpfWidgets.exe"

[Setup]
AppId={{E6D012B5-16B7-4D2A-949B-51C7F4AF5EB1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
SetupIconFile=D:\chronos-widgets\app-icon-light.ico
OutputBaseFilename=ChronosWidgetsSetup
OutputDir=D:\chronos-widgets
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest


[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "WpfWidgets-SelfContained\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json"
; Use onlyifdoesntexist to prevent overwriting user credentials on future updates
Source: "WpfWidgets-SelfContained\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[UninstallDelete]
; Remove app data directories created at runtime
Type: filesandordirs; Name: "{localappdata}\ChronosWidgets"
Type: filesandordirs; Name: "{localappdata}\WindowsWidgets"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Remove startup registry entries created by the app at runtime (both 32-bit and 64-bit registry views) }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ChronosWidgets');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WindowsWidgets');
    RegDeleteValue(HKEY_LOCAL_MACHINE, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ChronosWidgets');
    RegDeleteValue(HKEY_LOCAL_MACHINE, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WindowsWidgets');

    { Remove Windows startup approval entries }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'ChronosWidgets');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'WindowsWidgets');
    RegDeleteValue(HKEY_LOCAL_MACHINE, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'ChronosWidgets');
    RegDeleteValue(HKEY_LOCAL_MACHINE, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'WindowsWidgets');

    { Remove startup entries from Wow6432Node if present }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run', 'ChronosWidgets');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run', 'WindowsWidgets');
    RegDeleteValue(HKEY_LOCAL_MACHINE, 'Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run', 'ChronosWidgets');
    RegDeleteValue(HKEY_LOCAL_MACHINE, 'Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run', 'WindowsWidgets');
  end;
end;
