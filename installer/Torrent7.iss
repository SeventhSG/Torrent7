; Torrent7 installer. Built by CI: iscc /DAppVersion=x.y.z installer\Torrent7.iss
; The same AppId every release, so running a newer setup over an older install updates it in place.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6E1F7A3C-7B2D-4E8A-9C11-7A0B3D5E7F21}
AppName=Torrent7
AppVersion={#AppVersion}
AppVerName=Torrent7 {#AppVersion}
AppPublisher=SeventhSG
AppPublisherURL=https://github.com/SeventhSG/Torrent7
AppUpdatesURL=https://github.com/SeventhSG/Torrent7/releases
DefaultDirName={autopf}\Torrent7
DefaultGroupName=Torrent7
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=Torrent7-Setup-{#AppVersion}
SetupIconFile=..\src\Torrent7\Assets\Torrent7.ico
UninstallDisplayIcon={app}\Torrent7.exe
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\publish\win-x64\Torrent7.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Torrent7"; Filename: "{app}\Torrent7.exe"
Name: "{autodesktop}\Torrent7"; Filename: "{app}\Torrent7.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Torrent7.exe"; Description: "Start Torrent7"; Flags: nowait postinstall skipifsilent

[Code]
// When Torrent7 is already installed, say this setup updates it rather than installing a second copy.
function InitializeSetup(): Boolean;
var
  Old: String;
begin
  Result := True;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6E1F7A3C-7B2D-4E8A-9C11-7A0B3D5E7F21}_is1', 'DisplayVersion', Old) or
     RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6E1F7A3C-7B2D-4E8A-9C11-7A0B3D5E7F21}_is1', 'DisplayVersion', Old) then
    Result := MsgBox('Torrent7 ' + Old + ' is installed. This will update it to {#AppVersion}, keeping your torrents and settings.' + #13#10#13#10 + 'Continue?', mbConfirmation, MB_YESNO) = IDYES;
end;
