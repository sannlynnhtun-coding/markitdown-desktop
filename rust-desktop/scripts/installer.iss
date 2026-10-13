#ifndef SourceDir
  #error SourceDir must point to the packaged release directory.
#endif
#ifndef OutputDir
  #define OutputDir SourceDir + "\installer"
#endif
[Setup]
AppId={{F1E9711C-AF98-4BF5-B8B3-8EE37C381921}
AppName=MarkItDown
AppVersion=0.3.0
DefaultDirName={localappdata}\Programs\MarkItDown
DefaultGroupName=MarkItDown
UsePreviousGroup=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=MarkItDown-Setup-x64
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\markitdown-desktop.exe
SetupIconFile={#SourceDir}\markitdown.ico
[Files]
Source: "{#SourceDir}\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\vcruntime140*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\markitdown.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\SHA256SUMS.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\runtime\*"; DestDir: "{app}\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[InstallDelete]
Type: files; Name: "{userprograms}\MarkItDown PDF\MarkItDown PDF.lnk"
Type: dirifempty; Name: "{userprograms}\MarkItDown PDF"
Type: files; Name: "{userdesktop}\MarkItDown PDF.lnk"
[Icons]
Name: "{group}\MarkItDown"; Filename: "{app}\markitdown-desktop.exe"
Name: "{autodesktop}\MarkItDown"; Filename: "{app}\markitdown-desktop.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\markitdown-desktop.exe"; Description: "Open MarkItDown"; Flags: nowait postinstall skipifsilent
