# LocalInfantil

Aplicación WPF de escritorio para un único local de ropa infantil. La rama actual contiene F11 (variantes por matriz, etiquetas Code128 y búsqueda POS) y el trabajo de preproducción F10 trasladado sobre F11. El actualizador y el instalador requieren configuración y verificaciones externas pendientes. F08 conserva pendientes OAuth/Drive real y restauración manual. No está habilitada para producción ni debe usarse con datos comerciales reales todavía.

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

F11 agrega alta de variantes por matriz, etiquetas Code128 basadas únicamente en el código interno existente y búsqueda exacta en POS compatible con lectores USB tipo teclado. Las etiquetas usan preview y el diálogo de impresión Windows; los tamaños iniciales son parámetros de desarrollo y no están calibrados para hardware del local. La impresión nunca modifica stock.

F12 agrega cobro con Mercado Pago Point (modo PDV) por Orders API, sin SDK: descubrimiento dinámico de terminales (sin modelos hardcodeados), saga durable con idempotency key, reconciliación al arrancar, medio `mercado_pago` no efectivo y reembolsos con intención durable. La credencial propia va en DPAPI CurrentUser (`Secrets/mercadopago_token.bin`), nunca en SQLite ni en el repo; `--dev-db` usa secretos aislados. Solo Dueña configura; se requiere cuenta/terminal real pendiente (F12-T07). Ver [D-48](docs/DECISIONES.md) y [docs/MERCADO_PAGO.md](docs/MERCADO_PAGO.md). Producción NO habilitada.

Sin argumentos, la aplicación de preproducción resuelve `%LOCALAPPDATA%\LocalInfantil\Data\localinfantil.db`; desarrollo requiere `--dev-db <ruta>` y diagnóstico histórico requiere `--prototype` explícito. Nunca se abre Prototype automáticamente. Aunque la ruta productiva de arranque está implementada, D-13 sigue pendiente como decisión definitiva y no se autoriza cargar datos reales. DB, WAL/SHM, respaldos, secretos y updates permanecen fuera del repositorio y del directorio de instalación.

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

## F10: instalador y actualizaciones (preproducción)

`installer/Build-Installer.ps1` publica Release self-contained `win-x64`, comprueba que no haya DB/respaldos/secretos/símbolos y, si está instalado Inno Setup 6, genera un instalador por usuario con nombre estable y desinstalación que no elimina los datos. `ISCC.exe` no está disponible en todos los entornos; sin él la generación del instalador queda pendiente. La instalación clean/upgrade/uninstall debe hacerse en una VM/perfil aislado antes de cerrar F10-T01.

El actualizador usa manifiesto Ed25519 canónico, tamaño y SHA-256; una versión superior se descarga y valida automáticamente, pero nunca se instala automáticamente. Requiere canal HTTPS y clave pública reales todavía no configurados. Clave privada solo fuera del repositorio/instalador/PC; consultar [ACTUALIZACIONES](docs/ACTUALIZACIONES.md). `--dev-db` deshabilita las rutas productivas del updater. El schema vigente es V16, agregado por F12 para la persistencia durable/reconciliación de operaciones Point y el medio `mercado_pago`; no almacena credenciales ni Access Token. Producción NO habilitada.

```powershell
.\installer\Build-Installer.ps1
# Crear manifest.json + manifest.sig cuando exista canal/clave aprobados:
.\tools\Crear-ManifiestoActualizacion.ps1 -Instalador <exe> -UrlHttps <url> -PublicadoUtc <ISO-UTC> -VersionMinima 0.9.0 -Notas <texto> -ClavePrivadaFueraDelRepo <ruta-segura> -Salida <carpeta>
```

Ver [guía de uso](docs/GUIA_USO.md), [checklist PC real](docs/CHECKLIST_PC_REAL.md) y [procedimiento de piloto](docs/PRUEBA_PILOTO.md). Ninguno está ejecutado ni sustituye la validación manual.

La carpeta publicada se puede eliminar sin borrar datos de usuario porque la base de preproducción usa `%LOCALAPPDATA%\LocalInfantil\Data\`, fuera de `artifacts\publish`. Para borrar solo la publicación de prueba:

```powershell
Remove-Item -LiteralPath .\artifacts\publish\net10-f08-win-x64 -Recurse
```

## Instalador de prueba anterior (F00-T06, Inno Setup 6)

El procedimiento y script del prototipo F00 se conservan como renombres/histórico del trabajo de F10 en WIP; el instalador vigente está descrito arriba. Requiere Inno Setup 6 (`ISCC.exe`). Ninguno afirma compatibilidad con la PC del local.

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

El procedimiento de prueba manual con datos ficticios aislados está en `docs/PRUEBAS_MANUALES.md`. No ejecutar instalación/desinstalación sin leerlo: protege la base provisional existente.

## Convenciones

- Código y documentación en español cuando describan el negocio; nombres de tipos y APIs de .NET se mantienen claros y consistentes.
- `Nullable` e `ImplicitUsings` están habilitados en todos los proyectos.
- Las dependencias siguen el sentido indicado en `ESTRUCTURA.md`; WPF nunca accede a SQLite directamente.
- El único paquete de SQLite incorporado es `Microsoft.Data.Sqlite` 10.0.12; no se utiliza Entity Framework ni otro ORM.
- F11 usa `ZXing.Net` 0.16.11 solo en Infrastructure para codificar Code128; WPF dibuja el resultado en vector. No se agregó una migración de base para barcode ni seriales.
- F10 reutiliza `Sodium.Core` 1.4.1 para firmas Ed25519. No se agregan paquetes para updater; la clave productiva no se ha creado/configurado.

## Estado

Consultar `PLAN.md` para el estado por tarea y `docs/DECISIONES.md` para decisiones. F11-A–E tienen validación técnica publicada; F11-F y validación visual/manual siguen pendientes. F10 continúa en curso: publicación e instalador de prueba se generaron, pero faltan instalación limpia/upgrade/desinstalación en un entorno aislado, canal firmado y clave/release productivos, simulación de interrupciones, PC del local y validación Dueña/contador (D-01/D-03). La suite registrada para F10 pasó 476/476 en 4,7494 minutos; las pruebas focalizadas F10 15/15 y subconjuntos relevantes de F11 39/39. El build Debug registrado para F10 terminó con 0 errores y 8 advertencias: NU1900 por el índice remoto de vulnerabilidades no accesible y cinco warnings nullable/xUnit preexistentes de reportes. F12 agrega V16 para Mercado Pago Point; no almacena credenciales ni Access Token. F12-T07, F11-F, la validación visual/manual y la PC real siguen pendientes. Producción NO habilitada.
