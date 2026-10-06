#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\outputs\phase8\app"
#endif
#ifndef OutputBase
#define OutputBase "AIInvestmentWorkbench-" + MyAppVersion + "-win-x64-Setup"
#endif
[Setup]
AppId={{9758D52B-221C-49CC-8896-1443731F9C69}
AppName=AI Investment Workbench
AppVersion={#MyAppVersion}
AppPublisher=AI Investment Workbench
DefaultDirName={localappdata}\Programs\AIInvestmentWorkbench
DefaultGroupName=AI Investment Workbench
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\outputs\phase8\installer
OutputBaseFilename={#OutputBase}
SetupIconFile=..\src\AIInvestmentWorkbench.App\Assets\Workbench.ico
UninstallDisplayIcon={app}\AIInvestmentWorkbench.App.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\AI Investment Workbench"; Filename: "{app}\AIInvestmentWorkbench.App.exe"; WorkingDir: "{app}"
[Run]
Filename: "{app}\AIInvestmentWorkbench.App.exe"; Description: "Open AI Investment Workbench"; Flags: nowait postinstall skipifsilent unchecked
; No UninstallDelete entry: databases, backups, secrets and logs are outside {app} and retained.
