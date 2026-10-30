; Build through scripts/build-windows.ps1 with Inno Setup 6.3+.
#ifndef AppVersion
  #define AppVersion "2.1.3"
#endif
#ifndef AppSourceDir
  #error AppSourceDir must point to the dotnet publish application directory.
#endif
#ifndef RepoDir
  #error RepoDir must point to the repository root.
#endif
#ifndef PackageDir
  #error PackageDir must point to the installer output directory.
#endif

[Setup]
AppId={{E53A8EEB-44C7-4B86-BA40-73F8229D32C6}
AppName=Simpler FileBot
AppVersion={#AppVersion}
AppPublisher=MirageOne
AppPublisherURL=https://github.com/MirageOne/Simpler-FileBot
AppSupportURL=https://github.com/MirageOne/Simpler-FileBot/issues
DefaultDirName={localappdata}\Programs\Simpler FileBot
DefaultGroupName=Simpler FileBot
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile={#RepoDir}\LICENSE
OutputDir={#PackageDir}
OutputBaseFilename=Simpler-FileBot-{#AppVersion}-setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#AppSourceDir}\Assets\AppIcon.ico
UninstallDisplayIcon={app}\Simpler-FileBot.exe

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#AppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Simpler FileBot"; Filename: "{app}\Simpler-FileBot.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Simpler FileBot"; Filename: "{app}\Simpler-FileBot.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Simpler-FileBot.exe"; Description: "Launch Simpler FileBot"; Flags: nowait postinstall skipifsilent
