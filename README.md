# LocalInfantil

Aplicación WPF de escritorio para un único local de ropa infantil. En la rama actual se implementa F08 de respaldos locales cifrados, cola de Google Drive y recuperación, sobre F07 (clientes/cuenta corriente). F08 tiene validación automatizada local; OAuth/Drive real y la restauración visual/manual siguen pendientes. No está habilitada para producción ni contiene datos reales del comercio.

## Requisitos provisionales

- Windows para compilar y ejecutar el proyecto WPF.
- SDK de .NET 10.0.x compatible con `net10.0`.

Los proyectos usan `net10.0` / `net10.0-windows` como migración controlada y verificada en el equipo de desarrollo. .NET 10 es LTS hasta noviembre de 2028. Esto no aprueba su instalación en el local: la edición, versión y arquitectura del Windows real siguen pendientes en F00-T02. Ver [DECISIONES.md](docs/DECISIONES.md).

## Estructura

- `src/LocalInfantil.Domain`: reglas y tipos puros, sin dependencias de UI ni SQLite.
- `src/LocalInfantil.Application`: casos de uso; depende de Domain.
- `src/LocalInfantil.Infrastructure`: persistencia SQLite, migraciones e I/O; depende de Application y Domain.
- `src/LocalInfantil.Wpf`: único ejecutable WPF; depende de Application e Infrastructure.
- `tests/LocalInfantil.Tests`: pruebas de reglas, casos de uso, persistencia, migraciones y UI ViewModel.

La aplicación utiliza navegación WPF/MVVM y opera localmente. Clientes permite asociar un cliente opcional a una venta común; la venta fiada requiere un cliente activo, límite explícito y permiso efectivo. La cuenta corriente registra sus movimientos y asignaciones sin backfill de ventas históricas. Los tickets usan snapshots históricos y permanecen marcados como comprobantes internos no fiscales.

La base SQLite, sus archivos WAL/SHM, respaldos, logs y secretos se mantienen fuera del repositorio. Solo para el prototipo, la ruta predeterminada es `%LOCALAPPDATA%\LocalInfantil\Prototype\localinfantil-prototype.db`; no es una decisión de producción. La ruta definitiva de datos depende de D-13 y se resolverá antes de guardar datos reales.

## Respaldos F08 (implementación técnica; no producción)

Los backups locales se escriben en `%LOCALAPPDATA%\LocalInfantil\Backups` (ruta aún provisional) en un contenedor `.libackup`: snapshot SQLite de F01, GZip streaming y cifrado autenticado SecretStream. Manifiestos/cola y secretos están en subdirectorios separados; los secretos no entran en el backup. La restauración exige código externo, permiso/reautenticación de Dueña, caja cerrada, copia previa y confirmación final. Ver [D-42](docs/DECISIONES.md) y [procedimiento manual](docs/PRUEBAS_MANUALES.md).

Para habilitar la conexión de desarrollo, crear un OAuth Desktop Client en el Cloud Project correspondiente, declarar únicamente `drive.file` y proporcionar el Client ID mediante la variable pública no secreta `LOCALINFANTIL_GOOGLE_CLIENT_ID` antes de iniciar el proceso. No se guardan credenciales reales en el repositorio. OAuth/Drive real aún no se probó; una autorización del proyecto OAuth en estado Testing expira a los siete días y no sirve como configuración silenciosa permanente. La elegibilidad de uso personal y los requisitos de branding/privacidad deben revisarse con la configuración real de Google. No subir una DB real ni usar esta implementación en producción hasta la validación manual y de la PC destino.

Dependencias criptográficas de F08: `Sodium.Core` 1.4.1 (MIT) trae libsodium nativo 1.0.22 (ISC), invocado para las primitivas oficiales XChaCha20-Poly1305 SecretStream mediante una frontera P/Invoke acotada; `System.Security.Cryptography.ProtectedData` 10.0.0 implementa DPAPI CurrentUser. La publicación de prueba es self-contained `win-x64`; no confirma arquitectura del local.

## Comandos

Desde la raíz de la solución:

```powershell
dotnet restore LocalInfantil.sln
dotnet build LocalInfantil.sln --configuration Debug
dotnet test LocalInfantil.sln --configuration Debug --no-build
```

## Publicación de prueba

La publicación .NET 10 actual es una carpeta self-contained de prueba para la arquitectura confirmada del equipo de desarrollo (`win-x64`). No es un instalador ni valida la arquitectura de la PC del local. La publicación .NET 8 anterior permanece separada en `artifacts\publish\win-x64`. F08-T07 tiene cobertura automatizada completa (suite 409/409); F08 sigue en curso porque OAuth/Drive real y la validación manual WPF/restauración están pendientes.

```powershell
dotnet restore LocalInfantil.sln --runtime win-x64 --property:Configuration=Release
dotnet publish src\LocalInfantil.Wpf\LocalInfantil.Wpf.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output artifacts\publish\net10-f08-win-x64 -m:1 -p:DebugType=None -p:DebugSymbols=false
.\artifacts\publish\net10-f08-win-x64\LocalInfantil.Wpf.exe
```

La carpeta publicada se puede eliminar sin borrar datos de usuario porque la comprobación SQLite usa `%LOCALAPPDATA%\LocalInfantil\Prototype\`, fuera de `artifacts\publish`. Para borrar solo la publicación de prueba:

```powershell
Remove-Item -LiteralPath .\artifacts\publish\net10-f08-win-x64 -Recurse
```

## Instalador de prueba (F00-T06, Inno Setup 6)

El instalador de prueba se genera con Inno Setup 6 a partir de la publicación anterior. Solo están versionados `installer/inno/LocalInfantil-Prototype.iss` y `installer/Build-PrototypeInstaller.ps1`; el Setup generado queda en `artifacts/installer/` (fuera de git). Instala por usuario (sin admin) en `%LOCALAPPDATA%\Programs\LocalInfantil-Prototype`, crea acceso directo en el menú Inicio y se desinstala desde Configuración > Aplicaciones instaladas sin tocar `%LOCALAPPDATA%\LocalInfantil` (D-13 sigue pendiente; la ruta SQLite provisional no cambia). Requiere tener instalado Inno Setup 6 (`ISCC.exe`). No incluye actualizaciones automáticas ni afirma compatibilidad con la PC del local.

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-PrototypeInstaller.ps1
```

El procedimiento de prueba manual con datos ficticios aislados está en `docs/PRUEBAS_MANUALES.md`. No ejecutar instalación/desinstalación sin leerlo: protege la base provisional existente.

## Convenciones

- Código y documentación en español cuando describan el negocio; nombres de tipos y APIs de .NET se mantienen claros y consistentes.
- `Nullable` e `ImplicitUsings` están habilitados en todos los proyectos.
- Las dependencias siguen el sentido indicado en `ESTRUCTURA.md`; WPF nunca accede a SQLite directamente.
- El único paquete de SQLite incorporado para F00-T04 es `Microsoft.Data.Sqlite` 10.0.12; no se utiliza Entity Framework ni otro ORM.

## Estado

Consultar `PLAN.md` para el estado por tarea y `docs/DECISIONES.md` para decisiones. F08-T01–T03, T05 y T07 están implementadas y verificadas técnicamente; T04 sigue parcial por falta de OAuth/Drive real y T06 por falta de restauración manual y prueba en PC nueva. Build Debug 0/0, suite completa 409/409 y pruebas focalizadas de respaldos 38/38; la publicación self-contained win-x64 (409 archivos, 151.021.404 bytes) inició y cerró, sin inspección visual de la ventana. F07 visual/manual y F08 visual/manual siguen pendientes; F00-T02/PC real permanece bloqueada. Producción NO habilitada.
