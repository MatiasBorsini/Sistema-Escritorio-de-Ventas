; LocalInfantil — Instalador de prueba F00-T06 (Inno Setup 6).
;
; Prototipo de prueba, NO despliegue en producción:
; - F00-T02 sigue bloqueada: no se afirma compatibilidad con la PC del local.
; - D-13 sigue pendiente: la ruta SQLite provisional no cambia y la base vive
;   fuera de la carpeta de instalación (%LOCALAPPDATA%\LocalInfantil\Prototype).
; - No incluye actualizaciones automáticas (fuera de alcance de esta tarea).
;
; Qué instala:
; - Exclusivamente el programa y sus dependencias desde
;   artifacts\publish\net10-win-x64 (publicación Release self-contained .NET 10).
; - Acceso directo en el menú Inicio (+ escritorio opcional) para LocalInfantil.
; - Entrada de desinstalación en el mecanismo habitual de Windows.
;
; Garantías:
; - No empaqueta bases SQLite, respaldos, credenciales ni archivos personales
;   (doble protección: el script Build-PrototypeInstaller.ps1 aborta si los
;   detecta en la publicación, y la sección [Files] los excluye de todos modos).
; - La desinstalación NO toca %LOCALAPPDATA%\LocalInfantil: no hay ninguna
;   directiva [UninstallDelete] sobre datos de usuario. La base provisional,
;   sus WAL/SHM y cualquier dato ficticio se conservan tras desinstalar.
;
; Instalación por usuario (PrivilegesRequired=lowest): no requiere admin y
; escribe en {localappdata}\Programs\LocalInfantil-Prototype.

#define PublishDir "..\..\artifacts\publish\net10-win-x64"
#define InstallerOutDir "..\..\artifacts\installer"

[Setup]
AppId={{787FB00F-0818-4921-9A7D-54BDD3EF6A96}
AppName=LocalInfantil (Prototipo)
AppVersion=0.0.1
AppVerName=LocalInfantil (Prototipo) 0.0.1
AppPublisher=LocalInfantil — prototipo de prueba F00
DefaultDirName={localappdata}\Programs\LocalInfantil-Prototype
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir={#InstallerOutDir}
OutputBaseFilename=LocalInfantil-Prototype-Setup-net10-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayName=LocalInfantil (Prototipo)

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.db-shm,*.db-wal,*.sqlite,*.sqlite-shm,*.sqlite-wal,*.log,*.bak,*.pfx,*.p12,*.pem,*.key,*.token,secrets.json,appsettings.Local.json"

[Icons]
Name: "{autoprograms}\LocalInfantil (Prototipo)"; Filename: "{app}\LocalInfantil.Wpf.exe"
Name: "{autodesktop}\LocalInfantil (Prototipo)"; Filename: "{app}\LocalInfantil.Wpf.exe"; Tasks: desktopicon
