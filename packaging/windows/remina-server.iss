; Installateur Windows du serveur Remina (Inno Setup).
; Attend les binaires publiés dans: ..\..\out\server (dotnet publish -r win-x64 --self-contained).
; Compilation: ISCC remina-server.iss  ->  Remina-Server-Setup.exe

#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif

[Setup]
AppName=Remina Server
AppVersion={#AppVersion}
AppPublisher=NounoursCorp
DefaultDirName={autopf}\Remina\Server
DefaultGroupName=Remina
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename=Remina-Server-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Files]
Source: "..\..\out\server\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Code]
var
  AdminPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  AdminPage := CreateInputQueryPage(wpSelectDir,
    'Configuration du serveur Remina',
    'Paramètres d''écoute et clé d''administration',
    'Ces valeurs sont écrites dans la configuration du serveur.');
  AdminPage.Add('URL d''écoute (ex : http://0.0.0.0:5080) :', False);
  AdminPage.Add('Clé d''administration (X-Admin-Key) :', False);
  AdminPage.Values[0] := 'http://0.0.0.0:5080';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  cfg: string;
begin
  if CurStep = ssPostInstall then
  begin
    cfg :=
      '{' + #13#10 +
      '  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },' + #13#10 +
      '  "AllowedHosts": "*",' + #13#10 +
      '  "Urls": "' + AdminPage.Values[0] + '",' + #13#10 +
      '  "ConnectionStrings": { "Remina": "Data Source=remina.db" },' + #13#10 +
      '  "Admin": { "ApiKey": "' + AdminPage.Values[1] + '" }' + #13#10 +
      '}' + #13#10;
    SaveStringToFile(ExpandConstant('{app}\appsettings.json'), cfg, False);
  end;
end;

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create ReminaServer binPath= ""{app}\Remina.Server.exe"" start= auto DisplayName= ""Remina Server"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "description ReminaServer ""Serveur de gestion de parc Remina"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start ReminaServer"; Flags: runhidden

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ReminaServer"; Flags: runhidden; RunOnceId: "StopReminaServer"
Filename: "{sys}\sc.exe"; Parameters: "delete ReminaServer"; Flags: runhidden; RunOnceId: "DelReminaServer"
