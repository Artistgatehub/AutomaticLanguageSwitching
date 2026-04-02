#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define AppName "AutomaticLanguageSwitching"
#define HostName "com.automaticlanguageswitching.host"
#define HostExeName "AutomaticLanguageSwitching.NativeHost.exe"
#define AppPublisher "Jorjio22"

[Setup]
AppId={{8B39C0C7-8F2E-4E5C-A2B2-98EF6DB68F8A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma
SolidCompression=yes
WizardStyle=modern
OutputDir=output
OutputBaseFilename=AutomaticLanguageSwitching-Setup
UninstallDisplayIcon={app}\NativeHost\{#HostExeName}

[Files]
Source: "payload\native-host\*"; DestDir: "{app}\NativeHost"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "payload\extension-unpacked\*"; DestDir: "{app}\Extension"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\native-host\host-manifest.template.json"; DestDir: "{app}\NativeHost"; Flags: ignoreversion
Source: "USER-INSTALL-INSTRUCTIONS.txt"; DestDir: "{app}"; DestName: "README-FIRST.txt"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueName: ""; ValueData: "{app}\NativeHost\{#HostName}.json"; Flags: uninsdeletekey

[Icons]
Name: "{group}\AutomaticLanguageSwitching Instructions"; Filename: "{app}\README-FIRST.txt"

[Run]
Filename: "{app}\README-FIRST.txt"; Description: "Open installation instructions"; Flags: postinstall shellexec skipifsilent
Filename: "{app}\Extension"; Description: "Open the unpacked extension folder"; Flags: postinstall shellexec skipifsilent

[Code]
const
  HostName = '{#HostName}';
  HostExeName = '{#HostExeName}';
  SPI_SETTHREADLOCALINPUTSETTINGS = $104F;
  SPIF_UPDATEINIFILE = $01;
  SPIF_SENDCHANGE = $02;

function SystemParametersInfo(uiAction: UINT; uiParam: UINT; var pvParam: Integer; fWinIni: UINT): BOOL;
  external 'SystemParametersInfoW@user32.dll stdcall';

function ReadTextFile(const FileName: string): string;
var
  Content: AnsiString;
begin
  if not LoadStringFromFile(FileName, Content) then
    RaiseException('Failed to read file: ' + FileName);
  Result := Content;
end;

procedure WriteTextFile(const FileName: string; const Content: string);
begin
  if not SaveStringToFile(FileName, Content, False) then
    RaiseException('Failed to write file: ' + FileName);
end;

procedure GenerateNativeHostManifest;
var
  TemplatePath: string;
  ManifestPath: string;
  HostExePath: string;
  EscapedHostExePath: string;
  ManifestText: string;
begin
  TemplatePath := ExpandConstant('{app}\NativeHost\host-manifest.template.json');
  ManifestPath := ExpandConstant('{app}\NativeHost\' + HostName + '.json');
  HostExePath := ExpandConstant('{app}\NativeHost\' + HostExeName);
  EscapedHostExePath := HostExePath;
  StringChangeEx(EscapedHostExePath, '\', '\\', True);

  ManifestText := ReadTextFile(TemplatePath);
  StringChangeEx(ManifestText, '__HOST_EXE_PATH__', EscapedHostExePath, True);

  WriteTextFile(ManifestPath, ManifestText);
end;

procedure EnablePerAppInputMethodBestEffort;
var
  Enabled: Integer;
begin
  Enabled := 1;

  if SystemParametersInfo(
       SPI_SETTHREADLOCALINPUTSETTINGS,
       0,
       Enabled,
       SPIF_UPDATEINIFILE or SPIF_SENDCHANGE) then
  begin
    Log('Enabled per-app input method setting for the current user.');
  end
  else
  begin
    Log('Failed to enable per-app input method setting.');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    GenerateNativeHostManifest;
    EnablePerAppInputMethodBestEffort;
  end;
end;
