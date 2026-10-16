; Inno Setup 脚本 - ImgsToPDF 安装程序
#ifndef MyAppVersion
  #define MyAppVersion "1.3.0"
#endif

#ifndef SourceDir
  #define SourceDir "dist\ImgsToPDF"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{E76D7A69-7527-4079-B649-BD0A899239B1}
AppName=ImgsToPDF
AppVersion={#MyAppVersion}
AppPublisher=Sinryou
AppPublisherURL=https://github.com/Sinryou/ImagesToPDF
AppSupportURL=https://github.com/Sinryou/ImagesToPDF/issues
AppUpdatesURL=https://github.com/Sinryou/ImagesToPDF/releases
DefaultDirName={autopf}\ImgsToPDF
DefaultGroupName=ImgsToPDF
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.txt
OutputDir={#OutputDir}
OutputBaseFilename=ImgsToPDF-v{#MyAppVersion}-win-x64-Setup
SetupIconFile=..\ImgsToPDF\pimejaSike.ico
UninstallDisplayIcon={app}\ImgsToPDF.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\ImgsToPDF"; Filename: "{app}\ImgsToPDF.exe"
Name: "{autodesktop}\ImgsToPDF"; Filename: "{app}\ImgsToPDF.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ImgsToPDF.exe"; Description: "{cm:LaunchProgram,ImgsToPDF}"; Flags: nowait postinstall skipifsilent
