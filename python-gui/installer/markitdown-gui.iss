#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef MySourceDir
  #error MySourceDir must point to the PyInstaller output directory
#endif
#ifndef MyOutputDir
  #error MyOutputDir must point to the installer artifact directory
#endif
#ifndef MyRepoRoot
  #error MyRepoRoot must point to the repository root
#endif
#ifndef MyPackageRoot
  #error MyPackageRoot must point to the Python GUI package root
#endif

#define MyAppName "MarkItDown"
#define MyAppExeName "MarkItDown.exe"
#define MyAppId "{{5D0D75AB-70A9-4C99-A59B-74BE168C231B}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=MarkItDown contributors
AppPublisherURL=https://github.com/microsoft/markitdown
AppSupportURL=https://github.com/microsoft/markitdown/issues
DefaultDirName={autopf}\MarkItDown
DefaultGroupName=MarkItDown
DisableProgramGroupPage=yes
LicenseFile={#MyRepoRoot}\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#MyOutputDir}
OutputBaseFilename=MarkItDown-Setup-{#MyAppVersion}-win-x64
SetupIconFile={#MyPackageRoot}\src\markitdown_gui\assets\markitdown-app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UsedUserAreasWarning=no
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription=MarkItDown desktop converter installer
VersionInfoProductName=MarkItDown
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MarkItDown"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\MarkItDown"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MarkItDown"; Flags: nowait postinstall skipifsilent
