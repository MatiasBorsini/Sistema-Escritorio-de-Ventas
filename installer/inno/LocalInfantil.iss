; LocalInfantil — Instalador productivo F10 (Inno Setup 6).
;
; IMPORTANTE: AppId estable de producción. No volver a cambiarlo después del
; primer release productivo: Windows lo usa como identidad del producto para
; upgrade in-place y desinstalación.
;
; - D-13: la base productiva vive fuera de la carpeta de instalación
;   (%LOCALAPPDATA%\LocalInfantil\Data). Sin argumentos, la app abre
;   producción; nunca el prototipo histórico.
; - Incluye el cliente de actualización; el canal/clave productivos no se
;   empaquetan y deben configurarse por separado.
;
; Qué instala:
; - Exclusivamente el programa y sus dependencias desde
;   artifacts\publish\net10-f10-win-x64 (publicación Release self-contained .NET 10).
; - Acceso directo en el menú Inicio (+ escritorio opcional) para LocalInfantil.
; - Entrada de desinstalación en el mecanismo habitual de Windows.
;
; Garantías:
; - No empaqueta bases SQLite, respaldos, credenciales ni archivos personales
;   (doble protección: el script Build-Installer.ps1 aborta si los detecta
;   en la publicación, y la sección [Files] los excluye de todos modos).
; - La desinstalación NO toca %LOCALAPPDATA%\LocalInfantil: no hay ninguna
;   directiva [UninstallDelete] sobre datos de usuario. La base productiva,
;   sus WAL/SHM, respaldos y secretos se conservan tras desinstalar.
;
; Instalación por usuario (PrivilegesRequired=lowest): no requiere admin y
; escribe en {localappdata}\Programs\LocalInfantil.

#define PublishDir "..\..\artifacts\publish\net10-f10-win-x64"
#define InstallerOutDir "..\..\artifacts\installer"

[Setup]
AppId={{12D6BA9A-C7A1-4C5A-A409-77BAF63448C9}
AppName=LocalInfantil
AppVersion={#AppVersion}
AppVerName=LocalInfantil {#AppVersion}
DefaultDirName={localappdata}\Programs\LocalInfantil
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir={#InstallerOutDir}
OutputBaseFilename=LocalInfantil-Setup-{#AppVersion}-net10-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayName=LocalInfantil

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.db-shm,*.db-wal,*.db3,*.sqlite,*.sqlite-shm,*.sqlite-wal,*.sqlite3,*.libackup,*.dpapi,*.log,*.bak,*.pfx,*.p12,*.pem,*.key,*.token,secrets.json,appsettings.Local.json,credentials.json,client_secrets.json,token.json,tokens.json"

[Icons]
Name: "{autoprograms}\LocalInfantil"; Filename: "{app}\LocalInfantil.Wpf.exe"
Name: "{autodesktop}\LocalInfantil"; Filename: "{app}\LocalInfantil.Wpf.exe"; Tasks: desktopicon
