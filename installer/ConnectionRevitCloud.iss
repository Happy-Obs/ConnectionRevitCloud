#define MyAppName "ConnectionRevitCloud"
#define MyAppVersion "1.0.0"
#define MyAppExeName "ConnectionRevitCloud.Client.exe"

[Setup]
AppId={{A1B6F1D5-9F0D-4A1A-8A3B-CRC000000001}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputBaseFilename=ConnectionRevitCloudSetup
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "..\client\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; Положи сюда wireguard-amd64.msi рядом со скриптом при сборке инсталлятора:
Source: "wireguard-amd64.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\wireguard-amd64.msi"" /qn /norestart"; StatusMsg: "Устанавливаем WireGuard..."; Flags: runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent
