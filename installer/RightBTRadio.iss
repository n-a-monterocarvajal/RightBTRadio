; Instalador Inno Setup de RightBTRadio.
;
; Dependiente del marco: el payload se genera con `dotnet publish --self-contained false
; -p:WindowsAppSDKSelfContained=false` desde scripts\build-installer.ps1. Si falta el .NET Desktop
; Runtime o el Windows App Runtime, se descargan de los bootstrappers oficiales de Microsoft en vez de
; ir embebidos.
;
; La mecánica de detección y descarga viene del instalador de Collatio-Sen, que ya la tenía resuelta:
; descargador integrado de Inno Setup 6.1+ (CreateDownloadPage / DownloadTemporaryFile, WinHTTP por
; dentro), sin plugins de terceros. El cierre del residente por evento con nombre viene de
; RightKeyboard. Las diferencias con ambos están comentadas donde aparecen.

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0-alpha"
#endif

#define AppId "{{4B4B9F1C-7A2E-4C1D-9E56-2F0B8D3A6C41}"
#define AppMutex "Local\RightBTRadio.SingleInstance"
#define CloseEvent "Local\RightBTRadio.Close"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define StartupApprovedKey "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"

; Piso de versión del Windows App Runtime. DEBE seguir a la PackageReference
; Microsoft.WindowsAppSDK de RightBTRadio.WinUI.csproj: si ese paquete sube y este valor no, el
; instalador daría por bueno un runtime más viejo que el que la aplicación pide. En la línea 2.x el
; paquete MSIX lleva la versión real del SDK, así que los dos valores se escriben igual.
#define WindowsAppRuntimeMinVersion "2.4.0.0"

[Setup]
AppId={#AppId}
AppName=RightBTRadio
AppVersion={#AppVersion}
AppPublisher=n-a-monterocarvajal
; Instalación por máquina y elevada. Collatio-Sen instala por usuario y sin elevación porque su
; aplicación no la necesita; esta sí: habilitar y deshabilitar nodos PnP exige administrador, y las
; tareas programadas que hacen ese trabajo se registran durante la instalación. Con un solo UAC al
; principio, ni los bootstrappers de Microsoft ni el registro de las tareas vuelven a pedirlo.
DefaultDirName={autopf}\RightBTRadio
PrivilegesRequired=admin
DisableDirPage=yes
DisableProgramGroupPage=yes
MinVersion=10.0
; "x64compatible" y no el "x64" obsoleto: incluye ARM64 con emulación x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=RightBTRadio.exe,RightBTRadio.WinUI.exe
RestartApplications=no
UninstallDisplayIcon={app}\RightBTRadio.exe
OutputDir={#OutputDir}
OutputBaseFilename=RightBTRadio-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\RightBTRadio.ico

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "startup"; Description: "Iniciar RightBTRadio con Windows"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; HKCU aunque la instalación sea por máquina: el arranque automático es del usuario que instala, igual
; que las tareas programadas. Otro usuario de la misma máquina lo activa desde la ventana de ajustes.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "RightBTRadio"; ValueData: """{app}\RightBTRadio.exe"""; Tasks: startup; Check: ShouldCreateStartupEntry

[Icons]
Name: "{autoprograms}\RightBTRadio"; Filename: "{app}\RightBTRadio.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\RightBTRadio.exe"; Description: "Iniciar RightBTRadio"; Flags: nowait postinstall skipifsilent

[Code]
const
  EventModifyState = $0002;

  DotNetDesktopRuntimeUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  WindowsAppRuntimeUrl = 'https://aka.ms/windowsappsdk/2.4/latest/windowsappruntimeinstall-x64.exe';

  DotNetRuntimeFile = 'windowsdesktop-runtime-win-x64.exe';
  WindowsAppRuntimeFile = 'windowsappruntimeinstall-x64.exe';

var
  NeedsDotNetDesktopRuntime: Boolean;
  NeedsWindowsAppRuntime: Boolean;
  DownloadPage: TDownloadWizardPage;
  DeleteUserData: Boolean;

function OpenEvent(DesiredAccess: LongWord; InheritHandle: Boolean;
  Name: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(EventHandle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function OpenEventUninstall(DesiredAccess: LongWord; InheritHandle: Boolean;
  Name: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall uninstallonly';
function SetEventUninstall(EventHandle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall uninstallonly';
function CloseHandleUninstall(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall uninstallonly';

function IsStartupExternallyDisabled: Boolean;
var
  Approval: AnsiString;
begin
  Result := False;
  if RegQueryBinaryValue(HKCU, '{#StartupApprovedKey}', 'RightBTRadio', Approval) then
    if Length(Approval) > 0 then
      Result := Ord(Approval[1]) = 3;
end;

function ShouldCreateStartupEntry: Boolean;
begin
  Result := WizardIsTaskSelected('startup') and not IsStartupExternallyDisabled;
end;

{ Detección de prerequisitos, tomada de Collatio-Sen. Cada una usa la fuente estable de su
  componente y no el registro: .NET deja rastro con `dotnet --list-runtimes`, y el Windows App
  Runtime se despliega como paquetes MSIX de framework, sin clave en HKLM que leer. }
function DotNetDesktopRuntimeInstalled(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C dotnet --list-runtimes | findstr /C:"Microsoft.WindowsDesktop.App 10."',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function WindowsAppRuntimeInstalled(): Boolean;
var
  ResultCode: Integer;
  Params: String;
begin
  { Se comprueban DOS paquetes: el framework Microsoft.WindowsAppRuntime.2 y el Dynamic Dependency
    Lifetime Manager de x64, Microsoft.WinAppRuntime.DDLM.*-x6. El DDLM es lo que necesita una
    aplicación no empaquetada como esta para resolver la dependencia al arrancar, y puede faltar
    aunque el framework esté. Comprobado en esta estación: con el SDK 2.4.0 están
    Microsoft.WindowsAppRuntime.2 2.4.0.0 y Microsoft.WinAppRuntime.DDLM.2.4.0.0-x6. }
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' +
    '$min = [version]''{#WindowsAppRuntimeMinVersion}''; ' +
    '$fw = @(Get-AppxPackage -Name ''Microsoft.WindowsAppRuntime.2'' -ErrorAction SilentlyContinue | ' +
    'Where-Object { $_.Architecture -eq ''X64'' -and [version]$_.Version -ge $min }); ' +
    '$ddlm = @(Get-AppxPackage -Name ''Microsoft.WinAppRuntime.DDLM.*-x6'' -ErrorAction SilentlyContinue | ' +
    'Where-Object { [version]$_.Version -ge $min }); ' +
    'if ($fw.Count -gt 0 -and $ddlm.Count -gt 0) { exit 0 } else { exit 1 }"';

  { Falla hacia el lado seguro: si PowerShell no arranca, se da el runtime por ausente y se instala. }
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params,
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard();
begin
  NeedsDotNetDesktopRuntime := not DotNetDesktopRuntimeInstalled();
  NeedsWindowsAppRuntime := not WindowsAppRuntimeInstalled();
  DownloadPage := CreateDownloadPage('Componentes necesarios',
    'Descargando los componentes de Microsoft que la aplicación necesita para funcionar.',
    @OnDownloadProgress);
end;

procedure SignalCloseEvent;
var
  EventHandle: THandle;
begin
  EventHandle := OpenEvent(EventModifyState, False, '{#CloseEvent}');
  if EventHandle <> 0 then
  begin
    SetEvent(EventHandle);
    CloseHandle(EventHandle);
  end;
end;

function WaitForApplicationToClose: Boolean;
var
  Attempts: Integer;
begin
  if not CheckForMutexes('{#AppMutex}') then
  begin
    Result := True;
    exit;
  end;

  SignalCloseEvent;
  for Attempts := 1 to 40 do
  begin
    Sleep(250);
    if not CheckForMutexes('{#AppMutex}') then
    begin
      Result := True;
      exit;
    end;
  end;

  Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
begin
  if WaitForApplicationToClose then
    Result := ''
  else
    Result := 'No se pudo cerrar RightBTRadio de forma segura. Ciérrelo desde el icono del área de notificación y vuelva a intentarlo.';
end;

{ Las descargas van después de «Listo para instalar» y antes de copiar el payload: el usuario ya
  confirmó, y si la descarga falla se vuelve a esa página sin dejar nada a medias. }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  if CurPageID <> wpReady then
  begin
    Result := True;
    Exit;
  end;

  if not NeedsDotNetDesktopRuntime and not NeedsWindowsAppRuntime then
  begin
    Result := True;
    Exit;
  end;

  DownloadPage.Clear();
  if NeedsDotNetDesktopRuntime then
    DownloadPage.Add(DotNetDesktopRuntimeUrl, DotNetRuntimeFile, '');
  if NeedsWindowsAppRuntime then
    DownloadPage.Add(WindowsAppRuntimeUrl, WindowsAppRuntimeFile, '');

  DownloadPage.Show();
  try
    try
      DownloadPage.Download();
      Result := True;
    except
      SuppressibleMsgBox(AddPeriod(GetExceptionMessage()), mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide();
  end;
end;

{ En instalación silenciosa Inno no llama a NextButtonClick, así que el bajado vive también aquí.
  Sin esta rama, un /SILENT instalaría la aplicación sin sus prerequisitos. }
function DownloadPrerequisitesSilent(): Boolean;
begin
  Result := False;
  try
    if NeedsDotNetDesktopRuntime then
      DownloadTemporaryFile(DotNetDesktopRuntimeUrl, DotNetRuntimeFile, '', nil);
    if NeedsWindowsAppRuntime then
      DownloadTemporaryFile(WindowsAppRuntimeUrl, WindowsAppRuntimeFile, '', nil);
    Result := True;
  except
    MsgBox('No se pudieron descargar los componentes necesarios:' + #13#10#13#10 +
      AddPeriod(GetExceptionMessage()), mbCriticalError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    if WizardSilent() and (NeedsDotNetDesktopRuntime or NeedsWindowsAppRuntime)
      and not DownloadPrerequisitesSilent() then
      Abort();

    if NeedsDotNetDesktopRuntime then
    begin
      WizardForm.StatusLabel.Caption := 'Instalando el .NET Desktop Runtime...';
      if not Exec(ExpandConstant('{tmp}\') + DotNetRuntimeFile, '/install /quiet /norestart',
        '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      begin
        MsgBox('No se pudo instalar el .NET Desktop Runtime.', mbError, MB_OK);
        Abort();
      end;
    end;

    if NeedsWindowsAppRuntime then
    begin
      WizardForm.StatusLabel.Caption := 'Instalando el Windows App Runtime...';
      if not Exec(ExpandConstant('{tmp}\') + WindowsAppRuntimeFile, '--quiet',
        '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      begin
        MsgBox('No se pudo instalar el Windows App Runtime.', mbError, MB_OK);
        Abort();
      end;
    end;

    if not WizardIsTaskSelected('startup') then
      RegDeleteValue(HKCU, '{#RunKey}', 'RightBTRadio');

    Exit;
  end;

  { Las tareas se registran acá, con los archivos ya copiados y el instalador todavía elevado. Es
    lo que le ahorra al usuario el aviso de UAC que pediría el primer arranque. Quedan a nombre de
    quien instala; otro usuario de la misma máquina las registra desde la aplicación. }
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption := 'Registrando las tareas programadas...';
    if not Exec(ExpandConstant('{app}\RightBTRadio.exe'), '--register-tasks',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      MsgBox('No se pudieron registrar las tareas programadas de RightBTRadio.' + #13#10 + #13#10 +
        'La aplicación las volverá a pedir la primera vez que se ejecute.', mbInformation, MB_OK);
  end;
end;

procedure SignalCloseEventUninstall;
var
  EventHandle: THandle;
begin
  EventHandle := OpenEventUninstall(EventModifyState, False, '{#CloseEvent}');
  if EventHandle <> 0 then
  begin
    SetEventUninstall(EventHandle);
    CloseHandleUninstall(EventHandle);
  end;
end;

function InitializeUninstall: Boolean;
var
  Attempts: Integer;
begin
  Result := True;
  if CheckForMutexes('{#AppMutex}') then
  begin
    SignalCloseEventUninstall;
    for Attempts := 1 to 40 do
    begin
      Sleep(250);
      if not CheckForMutexes('{#AppMutex}') then
        break;
    end;

    if CheckForMutexes('{#AppMutex}') then
    begin
      MsgBox('No se pudo cerrar RightBTRadio de forma segura. Ciérrelo desde el icono del área de notificación y vuelva a ejecutar la desinstalación.',
        mbError, MB_OK);
      Result := False;
      exit;
    end;
  end;

  if UninstallSilent then
    DeleteUserData := ExpandConstant('{param:BORRARDATOS|0}') = '1'
  else
    DeleteUserData := MsgBox(
      '¿Desea eliminar también las preferencias y el registro de RightBTRadio?' + #13#10 + #13#10 +
      'Seleccione No para conservarlos.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  { Antes de borrar los archivos: los dos pasos necesitan el ejecutable todavía en disco. Devolver
    los radios primero, porque quien desinstale con uno deshabilitado se queda sin él y sin la
    aplicación que lo devuelve. El desinstalador ya está elevado, así que ninguno pide UAC. }
  if CurUninstallStep = usUninstall then
  begin
    if Exec(ExpandConstant('{sys}\schtasks.exe'), '/Run /TN RightBTRadio.EnableAll',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Sleep(6000);

    Exec(ExpandConstant('{app}\RightBTRadio.exe'), '--unregister-tasks',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, '{#RunKey}', 'RightBTRadio');
    if DeleteUserData then
      DelTree(ExpandConstant('{localappdata}\RightBTRadio'), True, True, True);
  end;
end;
