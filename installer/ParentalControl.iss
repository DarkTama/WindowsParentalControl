; ParentalControl Inno Setup Installer
; Requires Inno Setup 6.2+ (https://jrsoftware.org/isdownload.php)

#define MyAppName      "Parental Control"
#define MyAppVersion   "1.2.0"
#define MyAppPublisher "ParentalControl"
#define MyAppExeName   "ParentalControl.Admin.exe"
#define ServiceExeName "ParentalControl.Service.exe"
#define ServiceName    "ParentalControl.Service"
#define AgentExeName   "ParentalControl.Agent.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={commonpf}\ParentalControl
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=ParentalControlSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\admin\{#MyAppExeName}
SetupIconFile=..\icon.ico
CloseApplications=force

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Service files
Source: "..\publish\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs

; Admin app files
Source: "..\publish\admin\*"; DestDir: "{app}\admin"; Flags: ignoreversion recursesubdirs createallsubdirs
; Agent files
Source: "..\publish\agent\*"; DestDir: "{app}\agent"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ParentalControlAgent"; ValueData: """{app}\agent\{#AgentExeName}"""; Flags: uninsdeletevalue

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\admin\{#MyAppExeName}"; \
    IconFilename: "{app}\admin\{#MyAppExeName}"; Comment: "Launch Parental Control Admin"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\admin\{#MyAppExeName}"; \
    IconFilename: "{app}\admin\{#MyAppExeName}"; Comment: "Launch Parental Control Admin"

[Run]
Filename: "{app}\admin\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop agent and delete the service before files are removed
Filename: "taskkill"; Parameters: "/F /IM {#AgentExeName}"; \
    RunOnceId: "StopAgent"; Flags: runhidden waituntilterminated
Filename: "sc"; Parameters: "stop {#ServiceName}"; \
    RunOnceId: "StopService"; Flags: runhidden waituntilterminated
Filename: "sc"; Parameters: "delete {#ServiceName}"; \
    RunOnceId: "DeleteService"; Flags: runhidden waituntilterminated

[UninstallDelete]
; Remove desktop shortcut
Type: files; Name: "{commondesktop}\{#MyAppName}.lnk"
; Remove ProgramData folder on uninstall (logs, database)
Type: filesandordirs; Name: "C:\ProgramData\ParentalControl"
; Remove entire install directory (Program Files\ParentalControl)
Type: filesandordirs; Name: "{app}"

[Code]
function ServiceExists(): Boolean;
var
    ResultCode: Integer;
begin
    Result := Exec('sc', ExpandConstant('query {#ServiceName}'),
                   '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
              and (ResultCode = 0);
end;

procedure StopService();
var
    ResultCode: Integer;
begin
    Exec('sc', ExpandConstant('stop {#ServiceName}'),
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2500);
end;

procedure UpdateService();
var
    ResultCode: Integer;
    BinPath: String;
begin
    BinPath := ExpandConstant('"{app}\service\{#ServiceExeName}"');
    Exec('sc', ExpandConstant('config {#ServiceName}')
         + ' binPath= ' + BinPath
         + ' start= auto',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc', ExpandConstant('failure {#ServiceName}')
         + ' reset= 86400 actions= restart/10000/restart/30000/restart/60000',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CreateService();
var
    ResultCode: Integer;
    BinPath: String;
begin
    BinPath := ExpandConstant('"{app}\service\{#ServiceExeName}"');

    Exec('sc', ExpandConstant('create {#ServiceName}')
         + ' binPath= ' + BinPath
         + ' start= auto'
         + ' obj= LocalSystem'
         + ' DisplayName= "Parental Control Service"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Set description
    Exec('sc', ExpandConstant('description {#ServiceName}')
         + ' "Monitors and enforces parental control time limits."',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Configure service to restart on failure (10s, 30s, 60s)
    Exec('sc', ExpandConstant('failure {#ServiceName}')
         + ' reset= 86400 actions= restart/10000/restart/30000/restart/60000',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StartService();
var
    ResultCode: Integer;
    Retries: Integer;
    Started: Boolean;
begin
    Started := False;
    Sleep(2000);  // Let SCM finish registering the service

    for Retries := 1 to 3 do
    begin
        Log(Format('StartService: attempt %d of 3', [Retries]));
        if Exec('sc', ExpandConstant('start {#ServiceName}'),
                '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        begin
            if (ResultCode = 0) or (ResultCode = 1056) then  // 1056 = already running
            begin
                Log('StartService: service started successfully');
                Started := True;
                Break;
            end;
            Log(Format('StartService: sc start returned %d', [ResultCode]));
        end
        else
            Log('StartService: Exec call itself failed');

        if Retries < 3 then
            Sleep(1000 + Retries * 1000);  // Escalating: 2s, 3s
    end;

    if not Started then
        MsgBox('The Parental Control service could not be started automatically. '
               + 'It will start after the next reboot, or you can start it manually '
               + 'from the Services console (services.msc).',
               mbInformation, MB_OK);
end;

procedure StopRunningProcesses();
var
    ResultCode: Integer;
begin
    Exec('taskkill', ExpandConstant('/F /T /IM {#AgentExeName}'),
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill', ExpandConstant('/F /T /IM {#MyAppExeName}'),
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
    StopRunningProcesses();
    if ServiceExists() then
        StopService();
    Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
    if CurStep = ssInstall then
    begin
        StopRunningProcesses();
        if ServiceExists() then
            StopService();
    end
    else if CurStep = ssPostInstall then
    begin
        // After files are copied: create if new or update if existing, then start
        if not ServiceExists() then
            CreateService()
        else
            UpdateService();
        StartService();
    end;
end;
