#ifndef AppVersion
  #define AppVersion "4.2.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

#define AppName "공공 AX 업무 매크로"
#define AppExeName "공공AX-업무매크로.exe"
#define AppPublisher "Public AX Local contributors"
#define AppUrl "https://github.com/obundh/gonggong-ax-local-4"

[Setup]
AppId={{1F206A8E-78B4-4B6D-AF74-37AA0D58BE41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases/latest
DefaultDirName={localappdata}\Programs\GonggongAX\Series4
DefaultGroupName={#AppName}
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=GonggongAX-Series4-Setup-x64-v{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\GonggongAX.Series4.Desktop
CreateUninstallRegKey=not IsSmokeTest
UsePreviousAppDir=not IsSmokeTest
UsePreviousGroup=not IsSmokeTest
UninstallDisplayIcon={app}\{#AppExeName}
AppReadmeFile={app}\README-FIRST.txt
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} 설치 프로그램
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Check: not IsSmokeTest
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Check: not IsSmokeTest

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} 실행"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
const
  VcRuntimeUrl = 'https://aka.ms/vc14/vc_redist.x64.exe';
  VcRuntimeRegistryKey = 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';

function IsSmokeTest: Boolean;
begin
  Result := ExpandConstant('{param:AXSMOKETEST|0}') = '1';
end;

function IsVcRuntimeInstalled: Boolean;
var
  Installed: Cardinal;
begin
  Result :=
    RegQueryDWordValue(HKLM64, VcRuntimeRegistryKey, 'Installed', Installed) and
    (Installed = 1);
end;

function PowerShellSingleQuoted(Value: String): String;
begin
  StringChangeEx(Value, '''', '''''', True);
  Result := '''' + Value + '''';
end;

function HasValidMicrosoftSignature(const FileName: String): Boolean;
var
  PowerShellPath: String;
  PowerShellCommand: String;
  ExitCode: Integer;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  PowerShellCommand :=
    '$signature = Get-AuthenticodeSignature -LiteralPath ' +
    PowerShellSingleQuoted(FileName) +
    '; if ($signature.Status -ne ''Valid'' -or ' +
    '$signature.SignerCertificate.Subject -notmatch ''Microsoft Corporation'') { exit 1 }';
  Result :=
    Exec(
      PowerShellPath,
      '-NoLogo -NoProfile -NonInteractive -Command "' + PowerShellCommand + '"',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ExitCode
    ) and
    (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RedistPath: String;
  ExitCode: Integer;
begin
  Result := '';
  if not IsVcRuntimeInstalled then
  begin
    if IsSmokeTest then
    begin
      Result := 'Smoke test requires an existing Visual C++ runtime; machine changes are disabled.';
      Exit;
    end;
    Log('Microsoft Visual C++ x64 런타임이 없어 공식 설치 파일을 자동으로 준비합니다.');
    RedistPath := ExpandConstant('{tmp}\vc_redist.x64.exe');
    try
      DownloadTemporaryFile(VcRuntimeUrl, 'vc_redist.x64.exe', '', nil);
    except
      Result :=
        'Microsoft Visual C++ x64 런타임을 내려받지 못했습니다.' + #13#10 +
        '인터넷 연결을 확인하거나 Microsoft 공식 페이지에서 먼저 설치하세요.' + #13#10 +
        GetExceptionMessage;
      Exit;
    end;

    if not HasValidMicrosoftSignature(RedistPath) then
    begin
      Result := '내려받은 Visual C++ 런타임의 Microsoft 서명을 확인하지 못해 실행하지 않았습니다.';
      Exit;
    end;

    if not Exec(
      RedistPath,
      '/install /quiet /norestart',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ExitCode
    ) then
    begin
      Result := 'Microsoft Visual C++ x64 런타임 설치 프로그램을 시작하지 못했습니다.';
      Exit;
    end;

    if ExitCode = 3010 then
      NeedsRestart := True
    else if (ExitCode <> 0) and (ExitCode <> 1638) then
    begin
      Result := Format('Microsoft Visual C++ x64 런타임 설치가 실패했습니다. 종료 코드: %d', [ExitCode]);
      Exit;
    end;

    if not IsVcRuntimeInstalled then
    begin
      Result := 'Microsoft Visual C++ x64 런타임 설치를 확인하지 못했습니다.';
      Exit;
    end;
  end;

  if not FileExists(ExpandConstant('{sys}\mfplat.dll')) then
    Log(
      'Media Foundation을 찾지 못했습니다. Windows N/KN에서는 화면 녹화를 위해 Media Feature Pack이 필요할 수 있습니다.'
    );
end;
