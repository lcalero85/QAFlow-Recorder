#define MyAppName "QAFlow Recorder"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "QAFlow"
#define MyAppExeName "QAFlowRecorder.exe"

#ifndef PublishDir
  #define PublishDir "..\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif
#ifndef WebView2Installer
  #define WebView2Installer "Dependencies\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
#endif
#ifndef AppIcon
  #define AppIcon "..\Assets\qaflow.ico"
#endif

[Setup]
AppId={{6B138E1A-5715-4CA0-BF08-41E5A0F6B906}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\QAFlow Recorder
DefaultGroupName=QAFlow Recorder
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=QAFlow_Recorder_1.0_Setup_x64
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebView2Installer}"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{autoprograms}\QAFlow Recorder"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\QAFlow Recorder"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated
;Filename: "{app}\{#MyAppExeName}"; Description: "Launch QAFlow Recorder"; Flags: nowait postinstall skipifsilent
