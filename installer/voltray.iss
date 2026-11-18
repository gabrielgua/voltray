; Voltray installer (Inno Setup 6). Per-user, no admin rights.
;   iscc /DVersion=0.1.0 /DSource=..\artifacts\publish\Voltray installer\voltray.iss
; Output: artifacts\release\Voltray-<version>-setup.exe

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Source
  #define Source "..\artifacts\publish\Voltray"
#endif

[Setup]
AppId={{6B1E2F4A-6E0D-4C55-9C6B-2B9F2E7A1D30}
AppName=Voltray
AppVersion={#Version}
AppVerName=Voltray {#Version}
AppPublisher=Gabriel Guaita
AppPublisherURL=https://github.com/gabrielgua/voltray
AppSupportURL=https://github.com/gabrielgua/voltray/issues
DefaultDirName={localappdata}\Programs\Voltray
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\release
OutputBaseFilename=Voltray-{#Version}-setup
SetupIconFile=..\assets\voltray.ico
UninstallDisplayIcon={app}\Voltray.exe
UninstallDisplayName=Voltray
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Updating: close the running Voltray (only files in {app}), reopen it afterwards
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ptbr.Autostart=Iniciar o Voltray com o Windows
en.Autostart=Start Voltray with Windows
ptbr.Launch=Abrir o Voltray agora
en.Launch=Open Voltray now

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Voltray"; Filename: "{app}\Voltray.exe"

[Registry]
; Same value the app writes when "Iniciar com o Windows" is turned on in its settings
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Voltray"; ValueData: """{app}\Voltray.exe"""; Tasks: autostart

[Run]
Filename: "{app}\Voltray.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close this installation's Voltray (tray app and settings window) before its files are removed
Filename: "powershell.exe"; Parameters: "-NoProfile -Command ""Get-Process Voltray,Voltray.Settings -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -like '{app}\*' } | Stop-Process -Force"""; Flags: runhidden waituntilterminated; RunOnceId: "CloseVoltray"

[Code]
// Remove "start with Windows" on uninstall, but only if it points at this installation
// (the app may have set it, or the user may run another copy elsewhere).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: string;
begin
  if CurUninstallStep = usPostUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Voltray', Value) then
      if Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Value)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Voltray');
end;
