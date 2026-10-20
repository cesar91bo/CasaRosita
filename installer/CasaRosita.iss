; Compilar con Inno Setup 6 después de scripts/Publish.ps1.
; SQL Server Express debe instalarse previamente desde un medio offline oficial.
[Setup]
AppId={{320AD37C-1764-4F04-817C-B64B0B933DCA}
AppName=Casa Rosita
AppVersion=0.1.0
DefaultDirName={autopf}\CasaRositaPackage
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputBaseFilename=CasaRosita-0.1.0-win-x64
Compression=lzma2
SolidCompression=yes
Uninstallable=no
WizardStyle=modern
[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}\payload"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\scripts\*.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if not RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\MSSQL$SQLEXPRESS') then
    Result := 'Instalá previamente SQL Server Express local (instancia SQLEXPRESS) desde el instalador offline oficial. Después volvé a ejecutar Casa Rosita.';
end;
procedure CurStepChanged(CurStep: TSetupStep);
var ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      ExpandConstant('-NoProfile -ExecutionPolicy Bypass -File "{app}\scripts\Install.ps1" -PublishFolder "{app}\payload"'),
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      RaiseException('Falló la configuración. La instalación NO está validada. Revisá los registros en ProgramData\CasaRosita\Logs y ejecutá scripts\Install.ps1 desde PowerShell como administrador para ver el diagnóstico.');
end;
