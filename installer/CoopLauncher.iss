#define MyAppName "Coop Launcher"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.2"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{D21D9093-7B28-4D05-9BE5-83905D8D15C3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Aguadecoco-byte and Coop Launcher contributors
AppPublisherURL=https://github.com/Aguadecoco-byte/cooplauncher
AppSupportURL=https://github.com/Aguadecoco-byte/cooplauncher/issues
AppUpdatesURL=https://github.com/Aguadecoco-byte/cooplauncher/releases
DefaultDirName={localappdata}\Programs\Coop Launcher
DefaultGroupName=Coop Launcher
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#InstallerOutputDir}
OutputBaseFilename=CoopLauncher-Setup-{#MyAppVersion}
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\CoopLauncher.exe
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany=Aguadecoco-byte and Coop Launcher contributors
VersionInfoDescription=Coop Launcher Windows Setup
VersionInfoProductName=Coop Launcher
VersionInfoProductVersion={#MyAppVersion}
VersionInfoTextVersion={#MyAppVersion}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\CoopLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Coop Launcher"; Filename: "{app}\CoopLauncher.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Coop Launcher"; Filename: "{app}\CoopLauncher.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\CoopLauncher.exe"; Description: "Abrir Coop Launcher"; Flags: nowait postinstall skipifsilent
