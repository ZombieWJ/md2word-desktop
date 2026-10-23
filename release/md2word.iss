#ifndef AppSource
  #error AppSource is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif

[Setup]
AppId={{A73DF38C-55D1-48F8-B8C9-5A5D3D50C28B}
AppName=md2word
AppVersion={#AppVersion}
AppPublisher=ZombieWJ
AppPublisherURL=https://github.com/ZombieWJ/md2word-desktop
AppSupportURL=https://github.com/ZombieWJ/md2word-desktop/issues
DefaultDirName={localappdata}\Programs\md2word
DefaultGroupName=md2word
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
AppMutex=Local\md2word-desktop-running
SetupMutex=Local\md2word-desktop-setup
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\Md2Word.Desktop.exe
SetupIconFile=..\resources\branding\app.ico
OutputBaseFilename=md2word-{#AppVersion}-win-x64-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
VersionInfoVersion={#AppFileVersion}
VersionInfoProductVersion={#AppFileVersion}
VersionInfoProductTextVersion={#AppVersion}
SignedUninstaller=no
SetupLogging=yes

[Languages]
Name: chinese; MessagesFile: compiler:Languages\ChineseSimplified.isl

[Tasks]
Name: desktopicon; Description: 创建桌面快捷方式; GroupDescription: 快捷方式：; Flags: unchecked

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Excludes: "portable.flag,便携版使用说明.txt"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Markdown转Word"; Filename: "{app}\Md2Word.Desktop.exe"; IconFilename: "{app}\Md2Word.Desktop.exe"; IconIndex: 0
Name: "{autodesktop}\Markdown转Word"; Filename: "{app}\Md2Word.Desktop.exe"; IconFilename: "{app}\Md2Word.Desktop.exe"; IconIndex: 0; Tasks: desktopicon

[Run]
Filename: "{app}\Md2Word.Desktop.exe"; Description: 打开Markdown转Word; Flags: nowait postinstall skipifsilent
