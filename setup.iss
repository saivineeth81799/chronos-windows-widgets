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

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
