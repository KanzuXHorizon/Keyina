#ifndef MyAppVersion
  #error MyAppVersion must be supplied by the release build.
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by the release build.
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by the release build.
#endif

#define MyAppName "Keyina"
#define MyAppPublisher "Keyina contributors"
#define MyAppExeName "Keyina.Host.exe"
#define MyAppUrl "https://github.com/KanzuXHorizon/Keyina"

[Setup]
AppId={{F03D82B7-506E-4FB4-A6B1-74B0BC17A43C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
AppCopyright=Copyright © 2026 Keyina contributors
DefaultDirName={localappdata}\Programs\Keyina
DefaultGroupName=Keyina
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Keyina-Setup-{#MyAppVersion}-x64
SetupIconFile=..\brand\generated\keyina.ico
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
AppMutex=Local\Keyina.Host
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
ChangesAssociations=no
ChangesEnvironment=no
DisableWelcomePage=no
DisableReadyPage=no
SetupLogging=yes
#ifdef EnableSigning
SignTool=KeyinaSign
SignedUninstaller=yes
#else
SignedUninstaller=no
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Keyina"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--show-settings"; WorkingDir: "{app}"
Name: "{autodesktop}\Keyina"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--show-settings"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--show-settings"; Description: "Launch Keyina"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
