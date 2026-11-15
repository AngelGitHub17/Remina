; Installateur Windows de l'agent Remina avec interface (barre des tâches).
; Attend les binaires publiés dans: ..\..\out\tray (publish win-x64 self-contained).
; Compilation: ISCC remina-agent-tray.iss  ->  Remina-Agent-GUI-Setup.exe
;
; Contrairement au service, l'agent graphique tourne dans la session de l'utilisateur :
; verrouillage et messages l'atteignent, et il affiche une icône + une fenêtre d'état.

#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif

[Setup]
AppName=Remina Agent (interface)
AppVersion={#AppVersion}
AppPublisher=NounoursCorp
DefaultDirName={autopf}\Remina\AgentGUI
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename=Remina-Agent-GUI-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Files]
Source: "..\..\out\tray\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Registry]
; Démarrage automatique pour toute session ouverte sur ce poste.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "ReminaAgent"; ValueData: """{app}\Remina.Agent.Tray.exe"""; \
  Flags: uninsdeletevalue

[Icons]
Name: "{autoprograms}\Remina Agent"; Filename: "{app}\Remina.Agent.Tray.exe"

[Code]
var
  ServerPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  ServerPage := CreateInputQueryPage(wpSelectDir,
    'Connexion au serveur Remina',
    'Renseigne le serveur et le code d''inscription',
    'Ces valeurs sont écrites dans la configuration de l''agent.');
  ServerPage.Add('URL du serveur (ex : http://192.168.1.10:5080) :', False);
  ServerPage.Add('Code d''inscription :', False);
  ServerPage.Values[0] := 'http://localhost:5080';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var cfg: string;
begin
  if CurStep = ssPostInstall then
  begin
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
Filename: "{app}\Remina.Agent.Tray.exe"; Description: "Lancer Remina Agent"; Flags: nowait postinstall skipifsilent
