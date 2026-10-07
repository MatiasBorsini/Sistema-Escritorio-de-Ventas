# Pruebas manuales

## F08 — Verificación técnica automatizada (2026-10-05; no sustituye pruebas manuales)

- `dotnet restore LocalInfantil.sln --runtime win-x64 --property:Configuration=Release`: correcto. El primer `publish --no-restore` sin activos runtime falló con `NETSDK1047`; tras el restore indicado, la publicación concluyó correctamente.
- `dotnet build LocalInfantil.sln --configuration Debug`: correcto, 0 advertencias y 0 errores.
- `dotnet test tests/LocalInfantil.Tests/LocalInfantil.Tests.csproj --configuration Debug --filter FullyQualifiedName~Respaldos --logger "console;verbosity=normal"`: correcto, 38/38 pruebas de respaldos.
- `dotnet test LocalInfantil.sln --configuration Debug --logger "console;verbosity=normal"`: correcto, 409/409 pruebas superadas en 4,3977 minutos. La búsqueda representativa de Catálogo tardó 2m55s y terminó; no se canceló.
- `dotnet publish src/LocalInfantil.Wpf/LocalInfantil.Wpf.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output artifacts/publish/net10-f08-win-x64 -m:1 -p:DebugType=None -p:DebugSymbols=false`: correcto, publicación de 409 archivos / 151.021.404 bytes (144,03 MiB). `libsodium.dll` se cargó correctamente. No se encontraron DB, backups, secretos, temporales ni PDB; `.deps.json` y `.runtimeconfig.json` son metadatos normales de .NET.
- Inicio directo de `LocalInfantil.Wpf.exe`: el proceso permaneció activo y obtuvo un handle de ventana después de 5 segundos; cerró normalmente. No se inspeccionó visualmente la UI, no se inició sesión y no se probaron operaciones manuales de F08; esta comprobación no sustituye la validación visual.
- Las pruebas automatizadas operan con bases temporales. No se abrió, escribió ni migró una base de usuario/histórica.
- Build de Release se produjo como parte de `dotnet publish` y finalizó correctamente con 0 advertencias/0 errores.
- `git diff --check`: ejecutado, salida limpia (código 0).

Pendientes manuales F08: configuración y OAuth/Drive real; autorización/reconexión y retry con cuenta de prueba; capturas y recorrido visual WPF; restauración local completa, cancelación y revisión visual de caja abierta/rollback/staging; restauración en una PC/perfil Windows nuevo; repetición manual del comportamiento post-cierre/scheduler; validación de PC/hardware real. No se afirma que esos escenarios se hayan probado. La cobertura automatizada F08-T07 está completa, pero F08-T04 y F08-T06 siguen parciales; F08 no queda cerrada y producción NO está habilitada.

## F00-T01 — Estructura inicial

- Compilación ejecutada el 2026-09-20 en Windows: `dotnet build LocalInfantil.sln --configuration Debug --no-restore`, correcta (0 advertencias, 0 errores).
- Pruebas ejecutadas el 2026-09-20: `dotnet test LocalInfantil.sln --configuration Debug --no-build`, correcta (1 superada de 1). La prueba confirma que el ensamblado Domain está disponible desde el proyecto de pruebas.
- No corresponde abrir una ventana ni probar SQLite o impresión en esta tarea: esas verificaciones pertenecen a F00-T03, F00-T04 y F00-T05.

Las pruebas en la PC del local, la instalación, la base local y la impresora quedan pendientes.

## F00-T03 — Ventana y navegación básica

- Compilación ejecutada el 2026-09-20: `dotnet build LocalInfantil.sln --configuration Debug --no-restore`, correcta (0 advertencias, 0 errores).
- Pruebas ejecutadas el 2026-09-20: `dotnet test LocalInfantil.sln --configuration Debug --no-build`, correctas (2 superadas de 2). La prueba de navegación confirma que `MainWindowViewModel` cambia de Inicio a Estado del prototipo sin usar servicios externos.
- Revisión de código: las pantallas usan únicamente datos estáticos en memoria; no contienen conexiones, archivos de datos ni llamadas de red.
- Intento automatizado de apertura: `LocalInfantil.Wpf.exe` inició y su proceso respondió, pero la automatización de esta sesión no expuso la ventana ni permitió accionar sus botones.
- Validación manual interactiva informada por la persona usuaria el 2026-09-20: apertura correcta de la ventana, navegación correcta entre Inicio y Estado del prototipo, comprobación correcta sin conexión a internet y cierre correcto de la aplicación. No se observaron errores ni esperas por servicios externos.

La validación visual manual de F00-T03 está completada.

## F00-T04 — Integración inicial de SQLite

- Restauración ejecutada el 2026-09-20: `dotnet restore LocalInfantil.sln`, correcta; todos los proyectos estaban actualizados.
- Compilación ejecutada el 2026-09-20: `dotnet build LocalInfantil.sln --configuration Debug --no-restore`, correcta (0 advertencias, 0 errores).
- Pruebas ejecutadas el 2026-09-20: `dotnet test LocalInfantil.sln --configuration Debug --no-build`, correctas (3 superadas de 3). La prueba de integración crea una base temporal, inicializa la tabla técnica, escribe un valor, abre conexiones nuevas para leerlo y vuelve a abrir una conexión adicional para confirmar que el valor sobrevive; después elimina la carpeta temporal.
- Intento de apertura: `LocalInfantil.Wpf.exe` permaneció activo durante tres segundos y expuso el título `LocalInfantil — Prototipo`. El proceso de prueba se cerró al terminar. No se requirió internet para iniciarlo ni para las pruebas de SQLite.
- La automatización disponible no expuso la ventana WPF ni permitió pulsar el botón de comprobación; por ello queda pendiente la validación manual interactiva de la pantalla Estado del prototipo: navegar a ella, ejecutar «Comprobar persistencia local», confirmar los tres estados «Correcta», verificar ausencia de error, volver a Inicio y cerrar la aplicación.
- Validación manual interactiva informada por la persona usuaria el 2026-09-20: la aplicación y la pantalla Estado abrieron correctamente; escritura, lectura y comparación SQLite fueron correctas. Después de cerrar y volver a abrir la aplicación, la comprobación volvió a ser correcta.
- Pendiente: no se informó una prueba explícita de esta comprobación con la computadora sin conexión a internet. Debe realizarse antes de cerrar F00-T04.
- Validación sin conexión informada por la persona usuaria el 2026-09-20: SQLite funciona correctamente sin acceso a internet. La validación manual de F00-T04 queda completada.

## F00-T05 — Prototipo de impresión

- Implementación preparada el 2026-09-20 con componentes estándar de WPF: vista previa (`DocumentViewer`) y selección/confirmación de impresora (`PrintDialog`). No se enviaron trabajos de impresión durante esta tarea.
- Verificación posterior a la instalación de Visual Studio, 2026-09-20: `dotnet restore LocalInfantil.sln` correcto; `dotnet build LocalInfantil.sln --configuration Debug --no-restore` correcto (0 advertencias, 0 errores); `dotnet test LocalInfantil.sln --configuration Debug --no-build` correcto (4 superadas de 4).
- La prueba automática de impresión valida que el generador entrega tres líneas ficticias y un total de `$ 53.500`; no abre el diálogo ni envía impresión.
- `LocalInfantil.Wpf.exe` inició, permaneció activo y expuso el título `LocalInfantil — Prototipo`; se cerró sin enviar trabajos. La automatización de esta sesión no expone ventanas WPF nativas, por lo que no fue posible inspeccionar ni accionar la vista previa o el diálogo de impresión.
- Pendiente manual: abrir Estado del prototipo, revisar el documento ficticio en la vista previa, abrir el diálogo de impresión, cancelarlo y comprobar que no se envía trabajo; luego, con autorización de la persona usuaria, seleccionar una impresora disponible y confirmar manualmente el resultado. Repetir la prueba con la impresora de la PC del local antes de producción.
- Validación manual interactiva informada por la persona usuaria el 2026-09-20: la vista previa mostró correctamente los tres productos y el total de `$53.500`; el diálogo de impresión de Windows abrió; cancelarlo no generó ningún trabajo; y Microsoft Print to PDF generó correctamente el documento.
- Pendiente fuera del cierre de F00-T05: repetir la impresión con el equipo físico y la impresora del local antes de producción.

## F00-T06 — Publicación distribuible de prueba

- Publicación self-contained Release generada el 2026-09-20 para `win-x64`, la arquitectura confirmada del equipo de desarrollo: `artifacts\publish\win-x64`. No representa una publicación para la PC del local, cuya arquitectura sigue sin confirmarse.
- Comando utilizado: `dotnet publish src\LocalInfantil.Wpf\LocalInfantil.Wpf.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output artifacts\publish\win-x64 --disable-build-servers -m:1 -p:DebugType=None -p:DebugSymbols=false`.
- Inspección de salida: 471 archivos, 162,13 MB, ejecutable presente; no incluye archivos `.db`, `.sqlite`, respaldos, archivos fuente ni símbolos `.pdb`.
- Ejecución fuera de Visual Studio: `LocalInfantil.Wpf.exe` se inició directamente desde la carpeta publicada, permaneció activo y expuso el título `LocalInfantil — Prototipo`. La ventana se detectó a los 2.401 ms en este equipo. Esta cifra mide solo hasta exponer la ventana y no constituye una medición de rendimiento de la PC del local.
- Validación manual interactiva informada por la persona usuaria el 2026-09-20, desde `artifacts\publish\win-x64`: la aplicación abrió fuera de Visual Studio, la navegación y SQLite funcionaron, cerró y volvió a abrir correctamente, la vista previa de impresión y el diálogo de Windows funcionaron y este último pudo cancelarse; las operaciones locales funcionaron sin internet y el arranque/navegación se percibieron fluidos. No se observaron problemas.
- Pendiente para cierre de F00-T06: crear/probar un instalador y una desinstalación reales que conserven `%LOCALAPPDATA%`; validar compatibilidad, arranque y fluidez en la PC del local; no eliminar los datos de usuario al limpiar la carpeta publicada.

## Migración controlada a .NET 10 — Validación manual registrada

- Verificación técnica ejecutada el 2026-09-20 en el equipo de desarrollo: restore, compilaciones Debug/Release y 4 pruebas automatizadas correctas. La publicación self-contained `win-x64` se generó en `artifacts\publish\net10-win-x64`; el proceso del ejecutable inició, respondió y expuso el título `LocalInfantil — Prototipo`.
- Validación manual informada por la persona usuaria el 2026-09-21, desde la carpeta publicada y fuera de Visual Studio: apertura, navegación, SQLite, funcionamiento sin internet, vista previa, impresión de prueba y fluidez correctas, sin problemas observados. No se registraron mediciones.
- Pendiente para cierre de F00-T06: crear/probar un instalador y una desinstalación reales que conserven `%LOCALAPPDATA%`; validar compatibilidad, arranque y fluidez en la PC del local; no eliminar los datos de usuario al limpiar la carpeta publicada.
- Antes de producción, repetir la prueba con la impresora física y la PC del local. Registrar edición, versión/build y arquitectura de Windows; esa comprobación es necesaria para cerrar F00-T02.

## F00-T06 — Instalador de prueba (Inno Setup 6, validación manual registrada)

- Generación verificada el 2026-09-21 en el equipo de desarrollo: `dotnet restore`, build Release (0 advertencias, 0 errores) y `dotnet test` (4/4) correctos; `installer\Build-PrototypeInstaller.ps1` limpió `artifacts\publish\net10-win-x64` (verificando antes que no contenía datos personales), regeneró la publicación Release (407 archivos, 148.648.229 bytes, sin `.db`/`.sqlite`, respaldos, fuentes, `.pdb` ni secretos), la reverificó e ISCC 6.7.3 generó `artifacts\installer\LocalInfantil-Prototype-Setup-net10-win-x64.exe` (47.321.824 bytes) sin errores. El instalador y la publicación quedan fuera de git; solo el `.iss` y el script de build están versionados.
- Carpeta de datos que usa el programa: `%LOCALAPPDATA%\LocalInfantil\Prototype\localinfantil-prototype.db` (ruta provisional, D-13 pendiente). Abrir la app no escribe nada; solo pulsar «Comprobar persistencia local» crea/escribe esa base.
- Validación manual informada por la persona usuaria el 2026-09-21, ejecutada en una cuenta local estándar independiente con la aplicación cerrada en la cuenta principal: (1) creación de la cuenta de prueba correcta; (2) instalación correcta; (3) acceso directo y apertura correctos; (4) navegación, SQLite e impresión correctos; (5) funcionamiento sin internet correcto; (6) desinstalación correcta; (7) base SQLite de la cuenta de prueba conservada con el mismo hash SHA-256; (8) base SQLite de la cuenta habitual conservada con el mismo hash SHA-256. Sin errores ni observaciones. No se registraron valores de hash, nombres de cuenta ni mediciones.
- Procedimiento manual ejecutado el 2026-09-21 (cuenta estándar de Windows independiente para pruebas):
  1. Desde tu sesión (cuenta principal), con la aplicación cerrada, registrar hash/tamaño de `%LOCALAPPDATA%\LocalInfantil\Prototype\localinfantil-prototype.db` (solo lectura, p. ej. `Get-FileHash`). Este es el valor "antes".
  2. Crear un usuario local estándar de prueba e iniciar sesión en esa cuenta (cuenta estándar, no administradora). Todo lo que instale y genere quedará bajo su propio perfil, físicamente separado de la cuenta principal.
  3. En la sesión de prueba, comprobar la ruta del perfil en esa misma sesión (p. ej. `echo %LOCALAPPDATA%` en `cmd`) y anotarla sin presuponer su valor: la base aislada vivirá en `<perfil-de-prueba>\LocalInfantil\Prototype\localinfantil-prototype.db`.
  4. En la sesión de prueba, ejecutar `artifacts\installer\LocalInfantil-Prototype-Setup-net10-win-x64.exe` (instalación por usuario, sin admin) y verificar la entrada «LocalInfantil (Prototipo)» en Configuración > Aplicaciones instaladas y el acceso directo del menú Inicio.
  5. Abrir la app desde el acceso directo; navegar Inicio/Estado; confirmar que aún NO existe la base en el perfil de prueba (abrir no escribe).
  6. Pulsar «Comprobar persistencia local» (tres estados «Correcta»), vista previa e impresión de prueba; repetir sin internet.
  7. Confirmar la ruta efectiva: el archivo debe haber aparecido exactamente en `<perfil-de-prueba>\LocalInfantil\Prototype\` con fecha/hora de la prueba —esa es la base aislada—.
  8. Desinstalar desde la sesión de prueba (Configuración > Aplicaciones instaladas > «LocalInfantil (Prototipo)» > Desinstalar).
  9. Verificar conservación: primero comprobar que la base de prueba sigue existiendo tras desinstalar (el desinstalador solo retira `...\Programs\LocalInfantil-Prototype` de esa cuenta) —no borrarla antes de esta comprobación— y después, desde tu sesión con la aplicación cerrada, verificar que la base principal conserva el hash/tamaño "antes". Recién entonces, limpieza manual de la cuenta/datos de prueba.
- Corrección 2026-09-21 (revisión): el aislamiento mediante `set LOCALAPPDATA=...` en un `cmd` queda ELIMINADO como garantía y no debe usarse. La aplicación resuelve su ruta con `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`, que consulta la carpeta conocida del shell del usuario actual e ignora la variable de entorno del proceso: lanzar el exe con `LOCALAPPDATA` modificada seguiría escribiendo en la base real.
- Pendiente para cierre de F00-T06: validar compatibilidad, arranque y fluidez en la PC del local y registrar el resultado. La cuenta de prueba y sus datos se conservan hasta tu indicación; no se avanzó de fase.

## F06 — Cambios y devoluciones

- Verificación visual/manual pendiente al 2026-10-03. La sesión de automatización informó `apps: []`; no fue posible observar ni accionar ventanas WPF y no se generaron capturas sintéticas.
- Quedan pendientes las capturas reales 101–110, todas a 1200×750 (verificar también tamaño mínimo 980×620): 101 búsqueda; 102 venta seleccionada; 103 cambio sin diferencia; 104 devolución no apta; 105 diferencia a cobrar; 106 diferencia a reintegrar; 107 autorización presencial de Dueña; 108 confirmación C-*; 109 histórico; 110 comprobante.
- Prueba manual a realizar con base ficticia/aislada: buscar por número T-* y por código/nombre/fechas; cuando haya varias ventas elegir manualmente; hacer un cambio 1→1 sin diferencia con caja cerrada; devolución apta y no apta (confirmar Físico/Disponible/Apartado); diferencia positiva con pago mixto; reintegro efectivo; reintegro por medio excepcional con autorización puntual de Dueña; revisar histórico y comprobante C-*; reimprimir/cancelar sin alterar datos; verificar que AnularVenta rechace una venta con cambios. No usar bases reales ni enviar impresión automáticamente.
- Verificación automática final: `dotnet build LocalInfantil.sln --configuration Debug` correcta con 0 advertencias/0 errores; suite focalizada de Cambios 15/15; `dotnet test LocalInfantil.sln --configuration Debug --no-build` correcta, 369/369. Incluye migración V12/respaldo, límites y concurrencia de devolución, diferencia cero, cinco medios/combinado, autorización, búsqueda, snapshots y protección de anulación. Esta evidencia no sustituye la comprobación visual de WPF.
- F06-T05 promociones sigue bloqueada por D-05; D-01 y PC real continúan pendientes antes de producción.

## F07-T01 — Clientes opcionales

- Verificación automática de implementación registrada el 2026-10-04; las pruebas y el build final se detallan en `PLAN.md`.
- Inspección de interfaz pendiente. La comprobación del entorno de automatización devolvió `apps: []`; no se pudieron capturar ni operar ventanas WPF y no se generaron imágenes sintéticas.
- Capturas pendientes, reales y a la misma resolución: 111 Clientes; 112 alta; 113 cliente seleccionado; 114 historial de venta/cambio; 115 POS sin cliente; 116 búsqueda; 117 cliente seleccionado y limpieza; 118 ticket con snapshot de nombre. Revisar a 1200×750 y comprobar además el mínimo de 980×620.
- Al probar con una base ficticia aislada: crear un cliente con nombre, teléfono y observación; buscar por nombre y teléfono, recorrer páginas; editar y desactivar/reactivar; confirmar que la desactivación pide confirmación y conserva el historial; en POS verificar el estado inicial «Sin cliente», asociar/limpiar un cliente activo y completar una venta con pago total; comprobar que un cliente inactivo no puede asociarse a una nueva venta; revisar consulta e impresión del ticket para que muestren el nombre snapshot y nunca teléfono; verificar historial T-* y C-* con fecha/importe/referencia humana.
- Validar inicio de sesión Dueña y Empleada: ambas pueden consultar por configuración inicial; Empleada no administra por defecto. Confirmar que los permisos individuales efectivos son respetados. No usar datos reales ni interpretar total vendido como deuda.
- Fiado, cuenta corriente, abonos y saldos no forman parte de esta prueba ni están habilitados. La validación manual pendiente no se reporta como realizada.

## F07-T02–T06 — Cuenta corriente (verificación visual/manual pendiente)

- No se realizó validación manual de las ventanas. El entorno de automatización devolvió `apps: []`; no se generaron capturas sintéticas ni se alteraron bases históricas.
- Capturas reales pendientes, todas a la misma resolución: 119 ficha de cliente con límite/saldo; 120 POS con venta parcial fiada; 121 POS con venta 100% fiada; 122 cuenta corriente; 123 registrar abono; 124 vista previa de aplicación FIFO; 125 recibo A-*; 126 ajuste; 127 elección F06 entre aplicar/reintegrar; 128 devolución aplicada a deuda; 129 anulación e historial.
- Usar una base ficticia aislada y anotar antes su ruta. No usar la base provisional ni bases reales. Verificar: venta sin cliente sigue operando; fiado requiere cliente activo y permiso efectivo; Empleada sin concesión no ve/no ejecuta fiado y una denegación individual prevalece; el límite opcional bloquea excedentes; venta parcial registra solo lo recibido en caja; la venta totalmente fiada no crea entrada monetaria y aun así respeta la sesión de caja abierta requerida por D-38.
- Probar cargos múltiples y abonos con más de un medio: comprobar orden FIFO, importe y medio de cada componente, saldo exacto posterior, recibo reconstruible y que repetir/abonar de más no genere saldo a favor ni una segunda venta. Confirmar que reiniciar la aplicación conserva saldo e historial.
- Como Dueña, probar ajuste con motivo, rechazo de ajuste que genere saldo a favor y cambio de límite. Confirmar que Empleada no puede ejecutar esas acciones, incluso por invocación directa.
- Para devolución F06 con deuda, revisar ambas elecciones: aplicar solo a la deuda de la venta original o reintegrar; inspeccionar la vista previa y confirmar expresamente. Cancelar no debe escribir. La aplicación a deuda nunca debe ser automática; cualquier remanente se reembolsa por el flujo F06/caja. Diferencia positiva no debe crear deuda.
- Anular una venta fiada totalmente impaga y otra con pagos/abonos: la primera cancela deuda sin caja; la segunda cancela saldo impago y reembolsa únicamente dinero recibido, por los medios originales. Confirmar que una venta con cambio confirmado siga protegida contra anulación total.
- Inspeccionar cliente, historial, cuenta y recibos: sin GUID, IDs técnicos, nombres internos de eventos ni payload de auditoría. Confirmar funcionamiento offline y que F00-T02/PC real siguen pendientes. No usar impresora real ni tratar el recibo interno como comprobante fiscal.
- Estado reportado al 2026-10-04: pendientes todas las capturas 119–129 y los recorridos anteriores. No hay evidencia visual/manual y producción permanece NO habilitada.
- Verificación automática asociada, 2026-10-04: build Debug 0 advertencias/0 errores; suite completa 386/386; filtro de Clientes/cuenta corriente 17/17, incluida migración V13→V14 con cliente, ventas con/sin cliente, caja y cambio histórico sin deuda retroactiva; `git diff --check` limpio. No sustituye las pruebas manuales de arriba.

## F08 — respaldos y recuperación (pendiente de validación manual/remota)

No se ejecutó OAuth, Drive real ni restauración visual durante la implementación: no se proporcionaron credenciales ni un Cloud Project. Tampoco se modificó una base del usuario. Toda prueba manual debe usar una base ficticia aislada y una carpeta temporal; no copiar ni señalar la DB real del local/desarrollo.

### Preparar OAuth de prueba cuando exista autorización/Cloud Project

1. En Google Cloud Console, crear/seleccionar un proyecto aislado de desarrollo, habilitar Google Drive API y configurar Google Auth Platform (nombre identificable LocalInfantil, email de soporte/contacto y audiencia apropiada para la cuenta de prueba).
2. Declarar exclusivamente el scope `https://www.googleapis.com/auth/drive.file`; no agregar `drive`, `drive.readonly` ni otros scopes. Crear un OAuth client tipo **Desktop app** y conservar su Client ID como configuración pública no secreta. No crear ni distribuir un client secret.
3. Si el proyecto queda en estado Testing, agregar explícitamente la cuenta de prueba como test user. La autorización en ese estado expira a los siete días; es solo una prueba temporal, no la configuración de producción.
4. En una consola PowerShell de prueba, antes de iniciar la aplicación, establecer para ese proceso: `$env:LOCALINFANTIL_GOOGLE_CLIENT_ID = '<client-id-desktop>'`. No poner el valor en Git, logs, tickets ni archivos de datos. Iniciar la aplicación con `--dev-db` apuntando a una DB temporal aislada.
5. Como Dueña, entrar en Respaldos, generar el código de recuperación, copiarlo fuera del directorio `Backups`, reingresarlo para confirmar y, si se desea, usar explícitamente «Imprimir hoja». Cancelar el diálogo de impresión no debe escribir archivo alguno. Guardar dos copias físicas, una fuera del local.
6. Crear una copia manual. Abrir el navegador del sistema, completar el consentimiento y volver a la aplicación. Verificar que la lista remota muestra la copia solo después de la confirmación, con nombre `.libackup`, tamaño coincidente y sin PII ni tokens. Verificar en Drive la carpeta administrada LocalInfantil.
7. Desconectar Internet y realizar únicamente operaciones comerciales ficticias en la DB temporal: deben funcionar; los fallos remotos no bloquean venta/caja ni abren un modal por cada operación. La copia local debe seguir disponible y el estado quedar pendiente. Restaurar la conectividad, reconectar/reintentar y comprobar que se reutiliza el mismo ID remoto sin duplicados.
8. Probar la caducidad/revocación de credenciales solo con esta cuenta de prueba. La app debe indicar «Volver a conectar Google Drive» y preservar las copias locales. No usar la cuenta personal/producción para provocar una revocación.

### Restauración aislada y PC nueva

1. Preparar dos bases temporales independientes (A origen y B destino) y registrar sus rutas/hashes. A debe contener datos ficticios de prueba representativos (venta, pago, caja, cliente, cuenta y C-* cuando se prepare el fixture integral); nunca apuntar a una DB histórica/real.
2. Crear un backup local y, cuando Drive real esté configurado, confirmar su subida. Copiar el código de recuperación fuera de A. Mantener la aplicación cerrada antes de copiar/alterar paquetes.
3. Caso local: iniciar la app con base A, ir a Respaldos y seleccionar `.libackup`; probar clave incorrecta/archivo corrupto (deben rechazarse sin cambiar A); dejar caja abierta (debe rechazar); luego cerrar caja, reautenticar Dueña, importar/proteger el código original y restaurar confirmando expresamente. La app helper debe cerrar, reemplazar, abrir la DB restaurada y permitir comprobar los datos. No enviar trabajos de impresión reales.
4. Cancelar en la confirmación final y comprobar que A conserva el mismo hash; la DB descifrada de staging debe limpiarse en una cancelación normal.
5. Forzar solo en B una falla controlada de apertura/reemplazo (sin tocar A), comprobar rollback a la DB anterior y que el staging queda preservado para diagnóstico. Registrar el mensaje seguro y retirar a mano los temporales solo después de verificar hashes.
6. Caso PC nueva: usar perfil Windows/VM limpio sin los archivos DPAPI de A; instalar una compilación de prueba, conectar la misma cuenta Drive desde navegador, listar las copias de la app, descargar una y proteger en DPAPI el código externo original. Descifrar/verificar, reautenticar Dueña, validar backup previo, confirmar reemplazo y comprobar apertura/históricos. Confirmar que el token/DPAPI anterior no es requerido.
7. Verificar reintento/backoff, retención local (7 días, última copia y pendientes protegidos) y política remota (todos 0–7 días, una por día 8–30 priorizando cierre, >30 elegible). No probar borrado remoto destructivo fuera de un Drive de prueba. Si falla/queda ambiguo el listado, comprobar que no se inicia limpieza.
8. Repetir la UI de Respaldos en 1200×750 y 980×620: estados sin copias, pendiente, confirmada, reconexión y error local; acción de consultar/descargar Drive y flujo de reautenticación/confirmación. No se generaron capturas de F08 durante esta ejecución.

### Evidencia de esta implementación

- Ejecutada en el equipo de desarrollo y sobre DB temporales: pruebas focalizadas 38/38 de paquete autenticado, checksum recovery, reconciliación sidecar/reinicio, protector fake, retry/ID remoto, retención local/Drive fake, scheduler/catch-up/no solapamiento, cola de cierre durable, bloqueo interproceso, snapshot WAL y E2E completo restaurado a otra base. Build Debug 0/0, suite 409/409, publicación e inicio del ejecutable registrados arriba.
- No ejecutadas: autorización OAuth real, API Drive real, credenciales caducadas/revocadas, PC nueva, UI/manual de impresión de hoja/restauración, backup F08 durante operación de ventas con fixture E2E completo, pruebas de apagado/arranque manuales ni validación de PC/hardware real. No se afirma éxito para esos escenarios.
- Referencia de política Google consultada el 2026-10-05: [OAuth Desktop/PKCE y loopback](https://developers.google.com/identity/protocols/oauth2/native-app), [consentimientos en Testing](https://support.google.com/cloud/answer/15549945?hl=en), [excepciones de verificación](https://support.google.com/cloud/answer/13464323?hl=en), [scope drive.file](https://developers.google.com/drive/api/guides/api-specific-auth). La elegibilidad de uso personal (<100 usuarios) no se presume: debe revisarse para la configuración real.

## F11 — variantes por matriz, etiquetas y entrada POS (pendiente de validación visual/manual)

No se realizaron pruebas manuales de F11 en esta sesión. La automatización de escritorio no expuso ventanas nativas (`apps: []`), por lo que no se generaron capturas sintéticas ni se afirma apertura visual, diálogo de impresión, cancelación o flujo manual de POS. Las pruebas automatizadas usan bases SQLite temporales aisladas. Para cualquier recorrido manual, usar exclusivamente `--dev-db` con una DB ficticia temporal; no usar ni modificar `%LOCALAPPDATA%\LocalInfantil\Prototype\localinfantil-prototype.db` ni una base histórica/real.

### Recorrido a repetir cuando haya sesión gráfica

1. Iniciar la aplicación de desarrollo con una ruta temporal aislada (`LocalInfantil.Wpf.exe --dev-db <ruta-temporal>`). Crear un producto ficticio. En su ficha, abrir **Crear por matriz**, ingresar varios talles/colores y generar la matriz. Desmarcar alguna combinación. Verificar que vista previa muestra códigos futuros consecutivos, que una combinación normalizada ya existente figura como tal y que cancelar no crea variantes.
2. Confirmar un lote de 12 combinaciones (o una matriz equivalente), revisar códigos consecutivos e inmutables, una fila por combinación y auditoría. Confirmar que el alta no fija precio ni agrega stock. Repetir el mismo lote: no debe duplicar. Intentar talle solo, color solo y sin atributos sin introducir valores ficticios para `NULL`.
3. Cargar precio/stock por los flujos existentes. Desde **Imprimir etiquetas**, comprobar códigos, nombre, variante y Disponible. Probar **Poner stock disponible**, **Limpiar cantidades**, cantidades 0/1/N y reimpresión. Validar el precio opcional; el barcode debe contener solamente el código interno y el código humano debe seguir legible debajo.
4. Probar varios tamaños de etiqueta/márgenes/separaciones modificables. Revisar vista previa a página completa y multi-página; ninguna etiqueta ni barcode debe quedar cortado. Revisar Code128, quiet zone, contraste y textos para variantes con talle/color ausentes y nombre largo.
5. Usar Microsoft Print to PDF para prueba si está disponible. En el diálogo Windows, cancelar y confirmar que no aparece un trabajo ni se altera inventario. Luego confirmar explícitamente un trabajo PDF de prueba y revisar la cantidad, paginación y contenido. No enviar trabajos a una impresora física en esta validación.
6. Confirmar una recepción ficticia con dos variantes (por ejemplo 3 y 5 unidades). Abrir **Imprimir etiquetas de esta recepción** y revisar cantidades precargadas. Cancelar/cerrar impresión; comprobar que la recepción continúa confirmada y el stock conserva los valores posteriores a la recepción. Repetir con carga inicial desde la ficha: etiquetas opcionales con las cantidades establecidas.
7. En POS, escribir un código interno exacto y Enter (sin lector), comprobar alta inmediata y foco. Repetirlo tres veces: una sola fila con cantidad 3. Probar código inexistente, variante sin precio, variante sin Disponible e inactiva: mensajes compactos, sin alta incorrecta y foco recuperado.
8. Si se capturan pantallas, usar el mismo tamaño de ventana/resolución para capturas 138–144: (138) matriz, (139) duplicado, (140) selección etiquetas, (141) preview con precio, (142) sin precio, (143) recepción y cantidades propuestas, (144) POS con código. Mantener 1200×750 para comparación y verificar también 980×620 donde aplique. No versionar capturas si la política del repositorio las mantiene fuera de Git.

Pendiente para F11-F: probar lector HID/teclado e impresora física elegidos para el local, verificar legibilidad, calibración, dimensiones y driver Windows en la PC real. No se conoce todavía edición/versión/arquitectura de Windows ni modelo de impresora/lector. F11 no queda cerrada por estas pruebas técnicas y producción no está habilitada.

### Evidencia técnica F11

- Base de desarrollo: `f7a804f029450306b7df3639d3fe0ab6689bace4`; trabajo en rama separada `feat/f11-etiquetas-codigos-batch`; sin commit ni push. El checkout F10 preexistente permaneció aparte y no se mezcló.
- Pruebas agregadas: matriz 12 códigos, secuencia y stock sin cambios; normalización, duplicados, idempotencia, combinaciones opcionales, rollback total ante trigger de fallo y concurrencia; Code128 con quiet zone y round-trip; layout/paginación; consulta actual precio/Disponible agregado; escaneos repetidos, inexistente, sin stock/sin precio; precarga posterior a recepción y carga inicial.
- Verificación final (2026-10-06): `dotnet restore LocalInfantil.sln` correcto; `dotnet build LocalInfantil.sln --configuration Debug --no-restore` correcto, 0 advertencias/0 errores; suite focalizada F11 41/41; suite completa con salida normal/detallada 461/461 en 5,3657 minutos; `git diff --check` limpio. La suite se ejecutó completa hasta finalizar, incluidos sus casos largos.
- Inspección de automatización de escritorio: `apps: []`; no se pudo abrir/inspeccionar visualmente la ventana WPF ni se generaron capturas. Esta limitación no se registra como defecto funcional demostrado. Quedan pendientes el recorrido visual/manual y F11-F en hardware real.
- No se tocaron bases reales ni se agregó una base `.db`; no se generaron capturas. Validación manual WPF/PDF y hardware real continúan pendientes.
