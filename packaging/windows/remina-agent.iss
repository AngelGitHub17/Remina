; Installateur Windows de l'agent Remina (Inno Setup).
; Attend les binaires publiés dans: ..\..\out\agent (dotnet publish -r win-x64 --self-contained).
; Compilation: ISCC remina-agent.iss  ->  Remina-Agent-Setup.exe

#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif

[Setup]
AppName=Remina Agent
AppVersion={#AppVersion}
AppPublisher=NounoursCorp
DefaultDirName={autopf}\Remina\Agent
DefaultGroupName=Remina
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename=Remina-Agent-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Files]
Source: "..\..\out\agent\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Code]
var
  ServerPage: TInputQueryWizardPage;

// Extrait une valeur texte d'un JSON existant (pour pré-remplir).
function ReadJsonStr(const FileName, Key: string): string;
var s: AnsiString; p, q: Integer;
begin
  Result := '';
  if not LoadStringFromFile(FileName, s) then Exit;
  p := Pos('"' + Key + '"', s); if p = 0 then Exit;
  s := Copy(s, p + Length(Key) + 2, Length(s));
  p := Pos(':', s); if p = 0 then Exit;
  s := Copy(s, p + 1, Length(s));
  p := Pos('"', s); if p = 0 then Exit;
  s := Copy(s, p + 1, Length(s));
  q := Pos('"', s); if q = 0 then Exit;
  Result := Copy(s, 1, q - 1);
end;

procedure InitializeWizard;
var existing, srv: string;
begin
  ServerPage := CreateInputQueryPage(wpSelectDir,
    'Connexion au serveur Remina',
    'Renseigne le serveur et le code d''inscription',
    'Ces valeurs sont écrites dans la configuration de l''agent.');
  ServerPage.Add('URL du serveur (ex : http://192.168.1.10:5080) :', False);
  ServerPage.Add('Code d''inscription :', False);

  // Pré-remplit depuis une installation précédente, si présente.
  existing := ExpandConstant('{app}\appsettings.json');
  srv := ReadJsonStr(existing, 'ServerUrl');
  if srv = '' then srv := 'http://localhost:5080';
  ServerPage.Values[0] := srv;
  ServerPage.Values[1] := ReadJsonStr(existing, 'EnrollmentCode');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  cfg: string;
begin
  if CurStep = ssPostInstall then
  begin
    // Écrit un appsettings.json minimal avec les réponses de l'utilisateur.
    cfg :=
      '{' + #13#10 +
      '  "Logging": { "LogLevel": { "Default": "Information" } },' + #13#10 +
      '  "Remina": {' + #13#10 +
      '    "ServerUrl": "' + ServerPage.Values[0] + '",' + #13#10 +
      '    "EnrollmentCode": "' + ServerPage.Values[1] + '",' + #13#10 +
      '    "IdleMinutes": 5' + #13#10 +
      '  }' + #13#10 +
      '}' + #13#10;
    SaveStringToFile(ExpandConstant('{app}\appsettings.json'), cfg, False);
  end;
end;

[Run]
; Enregistre et démarre le service Windows.
Filename: "{sys}\sc.exe"; Parameters: "create ReminaAgent binPath= ""{app}\Remina.Agent.exe"" start= auto DisplayName= ""Remina Agent"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "description ReminaAgent ""Agent de gestion de parc Remina"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start ReminaAgent"; Flags: runhidden

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ReminaAgent"; Flags: runhidden; RunOnceId: "StopReminaAgent"
Filename: "{sys}\sc.exe"; Parameters: "delete ReminaAgent"; Flags: runhidden; RunOnceId: "DelReminaAgent"
