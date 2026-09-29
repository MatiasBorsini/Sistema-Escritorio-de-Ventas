# Decisiones técnicas y pendientes

## D-00 — Framework provisional de F00-T01

- Estado: antecedente reemplazado por D-15.
- Fecha: 2026-09-20.
- Fuente: estructura inicial de Codex; no es una decisión de la dueña.
- Decisión histórica: los proyectos usaron `net8.0`; el ejecutable WPF usó `net8.0-windows`.
- Motivo: el entorno de desarrollo tiene el SDK 8.0 y el runtime Windows Desktop 8.0 instalados; .NET 8 es LTS y permite compilar el esqueleto sin agregar dependencias.
- Criterio para confirmar o reemplazar: F00-T02 debe comprobar versión, edición y arquitectura del Windows de la PC del local, además del soporte vigente del framework elegido. No se realizará despliegue con esta decisión sin esa validación.

## D-00 — Actualización F00-T02: versión de .NET y compatibilidad Windows

- Estado: **provisional; decisión final bloqueada por falta de acceso a la PC del local**.
- Fecha: 2026-09-20.
- Fuente: verificación local y documentación oficial de Microsoft.
- Candidata recomendada para decidir al validar el equipo: **.NET 10 LTS**. Microsoft indica soporte hasta el 15 de noviembre de 2028, mientras que .NET 8 LTS finaliza el 11 de noviembre de 2026. Fuentes: [ciclo de vida de .NET](https://learn.microsoft.com/lifecycle/products/microsoft-net-and-net-core) y [versiones y soporte de .NET](https://learn.microsoft.com/dotnet/core/releases-and-support).
- Decisión histórica: en ese momento no se modificó ningún `TargetFramework`; se conservó `net8.0` / `net8.0-windows` hasta realizar la migración controlada posterior. Ver D-15 para el estado actual.
- Compatibilidad oficial con Windows 10: la tabla vigente de Microsoft limita Windows 10 a las ediciones LTSC y Enterprise; incluye las versiones 21H2, 1809 y 1607 de esos canales para .NET 8 y .NET 10. Por lo tanto, no se puede afirmar soporte oficial para una PC con Windows 10 de edición, versión o arquitectura desconocidas. Fuente: [instalación de .NET en Windows](https://learn.microsoft.com/dotnet/core/install/windows).
- Distinción necesaria: una aplicación puede llegar a ejecutarse en un Windows no cubierto por esa tabla, pero Microsoft no la prueba ni da soporte oficial cuando el sistema operativo no tiene soporte. Esa posibilidad no habilita una decisión de despliegue.
- Arquitectura: el proyecto no fija plataforma de CPU. La documentación oficial lista x86, x64 y Arm64 para .NET 8 y .NET 10 en las ediciones Windows 10 compatibles; la arquitectura real del local sigue sin confirmarse.

### Verificaciones ejecutadas en el entorno de desarrollo

- Entorno disponible: Windows `10.0.22631`, RID `win-x64`; es un entorno de desarrollo Windows 11, no evidencia sobre la PC del local.
- SDK instalados: .NET SDK 8.0.424 y 9.0.304. Runtimes Desktop: 8.0.19, 8.0.30 y 9.0.8. No hay SDK ni runtime .NET 10, por lo que no fue posible compilar la alternativa .NET 10 localmente sin instalar componentes nuevos.
- `dotnet build LocalInfantil.sln --configuration Debug --no-restore`: correcto, 0 advertencias y 0 errores.
- `dotnet test LocalInfantil.sln --configuration Debug --no-build`: correcto, 1 de 1 prueba superada.
- Se inició temporalmente `LocalInfantil.Wpf.exe`: el proceso permaneció activo y respondía. La sesión de automatización no expuso una ventana nativa y el proceso no informó un identificador de ventana, por lo que **la apertura visual de `MainWindow` no queda verificada**. El proceso se cerró al finalizar la comprobación.
- Rendimiento: no se evaluó en la PC objetivo ni con la carga funcional futura. El consumo observado durante el intento de arranque del esqueleto no representa una medición del sistema del local.

### Validación obligatoria pendiente en la PC del local

1. Registrar edición, versión/build, canal de soporte y arquitectura de Windows.
2. Confirmar que ese Windows sigue en soporte oficial y que aparece en la matriz de .NET de la versión elegida.
3. Instalar el runtime **Desktop** de la versión aprobada y abrir el prototipo WPF en sesión interactiva.
4. Medir de forma cualitativa el arranque y la respuesta con la memoria disponible; el hardware estimado de 4 GB no está confirmado.

Mientras tanto, pueden abordarse F00-T03, F00-T04 y F00-T05 usando el framework provisional, sabiendo que deberán recompilarse y repetirse sus verificaciones si la decisión final cambia. Esta tarea no inicia esas fases.

## Decisiones del plan aún abiertas

Se mantienen pendientes D-01 a D-13 de `PLAN.md`. Para F00 afectan especialmente:

- D-08: falta validar el hardware, incluido el Windows real.
- D-13: cuenta única de Windows confirmada por el desarrollador el 2026-09-21 (una sola PC, sin uso multi-cuenta); falta concretar la ruta definitiva de producción.

No se tomaron decisiones sobre facturación, permisos, cuentas corrientes, promociones, costos, respaldos remotos ni actualizaciones.

## D-13 — Ruta SQLite provisional para F00-T04

- Estado: cuenta única confirmada; ruta definitiva de producción todavía pendiente.
- Fecha: 2026-09-20 (provisional); actualización 2026-09-21.
- Fuente: instrucción de F00-T04 de la persona usuaria; actualización confirmada por el desarrollador el 2026-09-21 (no es una decisión de la dueña).
- Decisión registrada: el sistema funcionará en una sola computadora; la dueña y la empleada utilizarán una única cuenta de Windows; no se necesita compartir la base entre diferentes cuentas de Windows. Desaparece la incertidumbre sobre múltiples cuentas: una ruta bajo el perfil único (p. ej. `%LOCALAPPDATA%`) ya no produciría bases separadas por usuario.
- Implementación provisional: al ejecutar la comprobación técnica desde la aplicación, la ruta predeterminada es `%LOCALAPPDATA%\LocalInfantil\Prototype\localinfantil-prototype.db`. La construcción de esa ruta está centralizada en `RutaBaseDatosProvisional`; la implementación SQLite recibe cualquier ruta por constructor, por lo que las pruebas inyectan un directorio temporal independiente.
- Alcance: contiene solamente la tabla técnica `technical_persistence_probe` y un valor generado para comprobar escritura/lectura. No contiene datos reales, entidades ni migraciones comerciales.
- Decisión no tomada: la ruta definitiva de producción todavía debe concretarse (carpeta exacta, permisos y recuperación), pero ya sin el escenario de cuentas múltiples. Ninguna ruta queda aprobada para datos del comercio hasta esa definición.

## D-08 — Impresión de prueba en F00-T05

- Estado: prototipo validado; pendiente de validación con la impresora y la PC del local.
- Fecha: 2026-09-20.
- Fuente: instrucción de F00-T05 de la persona usuaria.
- Implementación: se usa únicamente WPF estándar (`FixedDocument`, `DocumentViewer` y `PrintDialog`), sin biblioteca externa ni modelo de impresora supuesto. El documento contiene datos ficticios y se etiqueta como prueba, no como factura ni venta.
- Flujo: primero se abre una vista previa modal; desde ella, «Imprimir…» abre el diálogo nativo de Windows para seleccionar impresora y confirmar. Cancelar no envía ningún trabajo y los errores se muestran en la misma vista.
- Validación de prototipo informada por la persona usuaria: vista previa correcta, cancelación sin trabajo e impresión correcta mediante Microsoft Print to PDF.
- Pendiente: de forma obligatoria antes de producción, repetir la prueba con la impresora física y la PC del local.

## D-14 — Publicación de prueba F00-T06

- Estado: provisional; no es instalador ni decisión de despliegue.
- Fecha: 2026-09-20.
- Fuente: publicación técnica de F00-T06 solicitada por la persona usuaria.
- Decisión: se generó una carpeta self-contained Release para `win-x64` en `artifacts\publish\win-x64`, porque el equipo de desarrollo informa ese RID. El ejecutable contiene el runtime y dependencias necesarias para ese destino, sin requerir que el equipo de prueba tenga instalado el runtime .NET 8.
- Límites: no se infiere que la PC del local sea x64 ni se modifica F00-T02. La carpeta no es un instalador; no se probaron instalación/desinstalación ni actualización. La ruta SQLite permanece fuera de ella, en la ubicación provisional de D-13.
- Evidencia: carpeta generada sin bases de datos, respaldos, fuentes ni símbolos de depuración; el ejecutable se validó manualmente fuera de Visual Studio con navegación, SQLite, reapertura, vista previa, diálogo/cancelación de impresión, operación offline y fluidez cualitativa correctas.

## D-15 — Migración controlada a .NET 10

- Estado: migración técnica verificada en el equipo de desarrollo; aprobación de producción pendiente de F00-T02.
- Fecha: 2026-09-20.
- Fuente: instrucción de la persona usuaria y documentación oficial de Microsoft.
- Decisión: los cinco proyectos ahora destinan `net10.0`; los dos proyectos Windows (WPF y pruebas) usan `net10.0-windows`. `Microsoft.Data.Sqlite` se actualizó de 8.0.31 a 10.0.12, la versión estable vigente de Microsoft/NuGet. Los paquetes de pruebas no cambiaron porque restore, compilaciones y pruebas fueron correctos con sus versiones existentes.
- Motivo: .NET 10 es LTS hasta el 15 de noviembre de 2028, mientras que .NET 8 termina soporte el 11 de noviembre de 2026. Fuentes: [ciclo de vida de .NET](https://learn.microsoft.com/lifecycle/products/microsoft-net-and-net-core), [versiones y soporte](https://learn.microsoft.com/dotnet/core/releases-and-support), [instalación de .NET en Windows](https://learn.microsoft.com/dotnet/core/install/windows) y [Microsoft.Data.Sqlite 10.0.12](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12).
- Compatibilidad y límite: WPF solo se ejecuta en Windows y requiere el runtime Desktop cuando no se distribuye self-contained. La matriz oficial incluye .NET 10 para Windows 10 únicamente en ediciones LTSC/Enterprise señaladas por Microsoft, con x86, x64 y Arm64. No se conoce aún edición, versión ni arquitectura de la PC del local; que el programa pueda ejecutarse en otro Windows no equivale a soporte oficial. Por ello esta decisión no cierra F00-T02 ni autoriza instalación en producción.
- Alcance conservado: no se modificaron arquitectura, lógica de SQLite, esquema/base existente ni ruta provisional D-13. Las pruebas automatizadas siguen usando una base temporal aislada.
- Verificación técnica: SDK 10.0.401 y runtime Windows Desktop 10.0.12 disponibles. `dotnet restore LocalInfantil.sln`, build Debug y Release completaron sin advertencias ni errores; las 4 pruebas automatizadas superaron. La publicación self-contained Release `win-x64` se generó en `artifacts\publish\net10-win-x64` (407 archivos, 148.648.229 bytes), sin bases de datos, respaldos, fuentes, símbolos ni secretos detectados. El ejecutable inició y respondió técnicamente. Validación manual informada por la persona usuaria el 2026-09-21 desde esa carpeta, fuera de Visual Studio: apertura, navegación, SQLite, funcionamiento sin internet, vista previa, impresión de prueba y fluidez correctas, sin problemas observados.
- Publicación: la primera restauración para RID falló porque el entorno aislado bloqueaba las consultas al feed de NuGet. Los paquetes runtime 10.0.12 ya estaban en caché; `dotnet restore LocalInfantil.sln --runtime win-x64 --ignore-failed-sources --disable-build-servers -m:1` y la publicación posterior sin restore finalizaron correctamente. Esta salida es solo para el equipo de desarrollo x64, no determina la arquitectura del local.

## D-16 — Instalador de prueba F00-T06 (Inno Setup 6)

- Estado: script y generación verificados en el equipo de desarrollo; instalación/desinstalación manuales pendientes.
- Fecha: 2026-09-21.
- Fuente: instrucción de la persona usuaria (avanzar solo instalación/desinstalación de F00-T06) y autorización para instalar Inno Setup 6.
- Opción elegida: **Inno Setup 6.7.3** (instalador oficial `innosetup-6.7.3.exe` de https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/, firma Authenticode válida de Pyrsys B.V., instalado silencioso en el equipo de desarrollo). Motivo: un único script `.iss` versionable y fácil de mantener; empaqueta la publicación self-contained tal cual; genera entrada real en el mecanismo habitual de Windows (Agregar/Quitar programas) con desinstalador propio; permite instalación por usuario sin admin fuera de `Program Files`; y por defecto solo retira lo instalado, sin tocar datos de usuario. WiX se descartó por sobrecarga MSI innecesaria para un prototipo; MSIX por requerir SDK/firma y complicar la distribución de prueba; un instalador solo con PowerShell por ser más artesanal como mecanismo estándar.
- Alcance: `installer/inno/LocalInfantil-Prototype.iss` + `installer/Build-PrototypeInstaller.ps1` (versionados). El Setup generado (`artifacts/installer/`, ignorado en git) instala solo programa y dependencias en `{localappdata}\Programs\LocalInfantil-Prototype`, crea acceso directo en el menú Inicio (escritorio opcional) y no incluye actualizaciones automáticas (fuera de alcance). El script regenera siempre la publicación Release antes de compilar: limpia `artifacts\publish\net10-win-x64` solo si esa carpeta no contiene datos personales (verificando además que la ruta sea exactamente la esperada; nada fuera de ella se toca), ejecuta `dotnet publish`, aborta si la publicación falla o si detecta archivos privados, y recién entonces invoca ISCC. No empaqueta bases SQLite, respaldos, credenciales ni archivos personales: el build aborta si los detecta en la publicación y el `.iss` los excluye además por patrón. La desinstalación no toca `%LOCALAPPDATA%\LocalInfantil` (sin directivas `[UninstallDelete]` sobre datos). No afirma compatibilidad con la PC del local; D-13 sigue pendiente y la ruta SQLite provisional no cambia.
- Verificación 2026-09-21: restore, build Release (0 advertencias, 0 errores) y 4/4 pruebas correctas; publicación regenerada por el propio script desde carpeta limpiada (`artifacts\publish\net10-win-x64`, 407 archivos, 148.648.229 bytes, 0 archivos prohibidos); ISCC compiló el Setup (`LocalInfantil-Prototype-Setup-net10-win-x64.exe`, 47.321.824 bytes) sin errores. La inspección binaria directa del Setup no es concluyente (sección compilada comprimida); la garantía de contenido es el escaneo previo de la publicación + `Excludes` del `.iss`. Validación manual informada por la persona usuaria el 2026-09-21 en cuenta local estándar independiente: instalación, acceso directo/apertura, SQLite, impresión, funcionamiento sin internet, desinstalación y conservación de ambas bases (mismo hash SHA-256) correctos, sin errores ni observaciones. Resta la validación en la PC del local (ver `docs/PRUEBAS_MANUALES.md`).

## D-18 — Fundamentos SQLite F01-T02 (journaling WAL)

- Estado: implementado y probado en `feat/f01-sqlite-foundations`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: instrucción de la persona usuaria (F01-T02) y pruebas reproducibles en bases temporales.
- Decisión: modo de journaling **WAL**, establecido una sola vez por base (nunca en cada apertura). Motivo: en una PC de un solo puesto, los lectores (UI, futuros respaldos durante ventas) no se bloquean ante escritores; recuperación estándar ante cierres; archivos `-wal`/`-shm` transitorios ya ignorados en git. El diario convencional (`delete`) también se verificó funcional como base de comparación. Esta decisión NO afirma resistencia a cortes eléctricos.
- Alcance: `FabricaConexionesSqlite` (ruta explícita, `foreign_keys` y `busy_timeout` por conexión, `CommandTimeout` derivado y en cada comando incluidos pragmas), sesiones de escritura (`BEGIN IMMEDIATE`) y lectura (sin transacción), `AlmacenamientoOcupadoException` solo para `BUSY`/`LOCKED` (el resto, incluido `INTERRUPT`, se propaga sin disfrazar) sin reintentos automáticos, migrador con 60 s por comando, repositorios mínimos (productos, variantes, precios, movimientos) con interfaces en Application. Hallazgo: el `BeginTransaction()` implícito colgaba ~30 s ante contienda; el control explícito con tiempos configurados lo redujo al tiempo esperado (prueba con límite de 15 s, código 5 verificado).
- D-13 sigue pendiente (ruta definitiva de producción); la base provisional de F00 no fue tocada.

## D-19 — Operaciones, reloj y auditoría F01-T03

- Estado: implementado y probado en `feat/f01-operation-audit`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: instrucción de la persona usuaria (F01-T03).
- Decisión: identificadores centralizados e inyectables (`IGeneradorIds`; GUID sin guiones en producción); reloj UTC inyectable (`IReloj`; sistema en producción, fijo en pruebas; formato `"o"` al persistir); auditoría mínima en tabla `auditoria` (migración V2, sin alterar V1) con `operacion_id`, `tipo_evento` libre, `actor_tipo` usuario/sistema con `actor_id` obligatorio para usuario, referencia opcional y detalle sin secretos. `operacion_id` correlaciona pero no garantiza idempotencia por sí solo. Negocio y auditoría comparten sesión/transacción.
- Alcance: sin permisos (F01-T04), sin autenticación, sin respaldos generales (F01-T05), sin ventas ni catálogo; sin cableado al arranque; base provisional intacta.

## D-20 — Autenticación local F01-T04

- Estado: implementado y probado en `feat/f01-local-auth`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: instrucción de la persona usuaria (F01-T04).
- Decisión: PBKDF2-HMAC-SHA512 (220000 iteraciones, sal 16 bytes, subclave 32 bytes, formato versionado `v1$…`, comparación constante; conteos manipulados no se ejecutan) sin dependencias nuevas; política 12–256 sin clases arbitrarias; cuentas locales independientes de Windows; error genérico sin enumeración con verificación señuelo y retardo de 500 ms sin bloqueos; sesiones en memoria no forjables (resolución interna) con relectura de vigencia; alta inicial solo en base sin historial de usuarios (ni desactivados), en la misma transacción; alta posterior y todo restablecimiento denegados por defecto hasta D-03 (ni la sesión válida los concede); matriz configurable con denegación por defecto y solo los dos permisos de reintegro confirmados; auditoría sin secretos.
- Alcance: migración V3 solo agrega `huella_contrasena` (V1/V2 intactas); sin pantallas ni cableado al arranque; sin operaciones comerciales; sin licencias; pendientes la administración por rol, la recuperación autorizada, la caducidad/inactividad de sesión, el plazo correspondiente y la matriz completa (D-03); base provisional intacta.

## D-21 — Administración de cuentas por rol Administrador (regla confirmada)

- Estado: implementado y probado en `feat/f01-local-auth`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: regla de negocio confirmada por la persona usuaria (avanza D-03 solo en este punto; el resto de la matriz sigue pendiente).
- Regla: solo el rol técnico Administrador crea y desactiva cuentas y cambia o restablece contraseñas (incluidas las de dueña y empleada, que no cambian ni la propia). El Administrador cambia la suya solo con su contraseña actual. Administrador no recibe permisos comerciales automáticamente. Permiso explícito `usuarios.administrar` (solo Administrador), verificado en Application.
- Alcance: la primera cuenta por bootstrap es obligatoriamente Administrador, sin selección libre; no se asigna ese rol por vía genérica; nunca se desactiva la última cuenta administradora activa. Pendiente para antes de producción: recuperación de contraseñas (Administrador y dueña) sin solución aprobada (sin maestras, puertas traseras, Firebase, servidor externo, dominio ni códigos); permisos individuales por usuario y administración desde la cuenta de la dueña (requisitos acordados, aún no implementados; autenticación sin cambios en este bloque); identidad de la persona que tendrá la cuenta Administrador; resto de D-03.

## D-27 — Administración local de la Dueña y permisos individuales

- Estado: implementado y probado en `feat/owner-users-individual-permissions`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: regla confirmada por la persona usuaria (reemplaza D-21; avanza D-03 en permisos individuales).
- Regla: sin superadministrador del desarrollador ni cuentas universales; primera cuenta = Dueña; rol Administrador retirado con conversión V6 solo si es inequívoca (falla segura en caso ambiguo); `usuarios.administrar` de Dueña y no configurable por usuario; permisos comerciales configurables por empleado con auditoría y denegación explícita que prevalece sobre la matriz; última dueña activa protegida.
- Alcance: sin recuperación de Dueña aprobada (sin Firebase/servidor/email/dominio/códigos); sin inactividad; V1-V5 intactas; base provisional intacta.

## D-26 — Bloque F03-T01+T02 (proveedores y recepción)

- Estado: implementado y probado en `feat/f03-suppliers-receiving`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: instrucción de la persona usuaria (bloque F03-T01+T02).
- Decisión: proveedores mínimos sin contacto/CUIT obligatorios; recepción atómica multi-línea con costo histórico informado (NULL = desconocido, sin descomponer impuestos ni promedios); permisos `compras.proveedores.gestionar` y `compras.recepcion.confirmar` definidos sin conceder; migración V5.
- Alcance: sin SUMAR distinto de ingreso, sin costos D-07, sin ventas/caja; V1-V4 intactas; base provisional intacta.

## D-25 — Bloque F03-T03+T04 (ajustes e historial)

- Estado: implementado y probado en `feat/f03-adjustments-stock-history`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: instrucción de la persona usuaria (bloque F03-T03+T04).
- Decisión: ajustes por movimientos inmutables con motivo y responsable, sin stock negativo resultante y sin duplicar por `operacion_id`; corrección solo por compensatorio; existencias derivadas e historial paginado; permiso `inventario.ajuste_stock` definido sin conceder; sin clasificación de dañado (pendiente); sin migración (V5 innecesaria).
- Alcance: sin proveedores/recepción, costos, ventas ni caja; sin escrituras desde WPF; V1-V4 intactas; base provisional intacta.

## D-24 — Bloque F02-T03+T04+T05 (precios, búsqueda, carga inicial)

- Estado: implementado y probado en `feat/f02-prices-search-initial-stock`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: instrucción de la persona usuaria (bloque F02-T03+T04+T05).
- Decisión: precios con historial por filas y vigencia única (índice parcial V4); sin costos por D-07 pendiente (T03 parcial); búsqueda SQL paginada sin FTS (mediciones: 8 ms / 5 ms); carga inicial solo ESTABLECE con `operacion_id` anti-duplicado; permisos `catalogo.precios.gestionar` e `inventario.carga_inicial` definidos sin conceder; ViewModel de grilla pendiente.
- Alcance: sin recepción/ajustes/ventas (F03/F04); sin escrituras desde WPF; V1/V2/V3 intactas; base provisional intacta.

## D-23 — Catálogo F02-T01+T02 (permisos y reglas de integridad)

- Estado: implementado y probado en `feat/f02-products-variants`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: instrucción de la persona usuaria (bloque F02-T01+T02).
- Decisión: permisos `catalogo.productos.gestionar` y `catalogo.variantes.gestionar` definidos y no concedidos a ningún rol (D-03 pendiente); SKU, producto e id de variante inmutables; edición por reemplazo total sin listas cerradas; desactivación lógica con negativa explícita si el producto conserva variantes activas; lecturas con sesión vigente sin permiso específico (interino); sin migración (tablas V1 reutilizadas).
- Alcance: sin precios, costos, stock, ventas ni búsquedas avanzadas; sin escrituras desde WPF; base provisional intacta.

## D-22 — Respaldo local F01-T05 (mecanismo)

- Estado: implementado y probado en `feat/f01-local-backup`; pendiente de revisión.
- Fecha: 2026-09-21.
- Fuente: instrucción de la persona usuaria (F01-T05).
- Decisión: `VACUUM INTO` como mecanismo (instantánea atómica, válida en uso y en WAL, sin copiar en caliente ni arrastrar auxiliares); verificado = `integrity_check` ok + `foreign_key_check` vacío + esquema reconocido + versiones esperadas; restauración solo como ensayo en temporal aislado, nunca sobre el origen. El migrador reutiliza este servicio para su respaldo previo.
- Alcance: rutas explícitas del llamador (producción pendiente, D-09); sin Drive, sin automatismos, sin cableado al arranque ni al cierre de caja; sin restauración destructiva; V1/V2/V3 intactas; base provisional intacta.

## D-27 — Administracion local de la Duena y permisos individuales (reemplaza Administrador)

- Estado: integrado en `main` en `3109eeb`; pendiente de revision final.
- Fecha: 2026-09-21.
- Fuente: regla confirmada (reemplaza D-21).
- Regla: primera cuenta = Duena; rol Administrador retirado con conversion V6 solo si es inequivoca; `usuarios.administrar` de Duena y no configurable por usuario; permisos comerciales por empleado con denegacion explicita que prevalece sobre matriz; ultima duena activa protegida.
- Alcance: sin recuperacion aprobada; V1-V5 intactas.

## D-28 — Conteo fisico y reversion de recepciones F03-T05

- Estado: integrado en `main` en `8eae690`; pendiente de revision.
- Fecha: 2026-09-21.
- Fuente: bloque F03-T05 + reversiones.
- Regla: conteo parcial no supone cero, foto vieja por `movimientos_contados` (no solo saldo) dentro de tx, todo/nada, anti-doble; reversion completa multilinea con stock suficiente verificado en la misma tx, sin borrar historial/costos, anti-doble; permisos `conteo_registrar`/`conteo_aprobar`/`recepcion_revertir` no concedidos; V7 con respaldo previo y fallo atomico.
- Alcance: sin pantalla WPF, sin danado/no vendible (F03-T04), sin Drive/costos/recuperacion; V1-V6 intactas.

## D-29 — Primer flujo WPF real: login y administracion Dueña (aislamiento de bases)

- Estado: integrado en `61b267d`; pendiente de revision.
- Fecha: 2026-09-22.
- Fuente: bloque autorizado (App.xaml.cs, ViewModels, Views).
- Regla: sin autenticacion paralela en WPF (reusa Application); bootstrap solo si base sin usuarios (primera = Dueña), sino login; sesion invalida tras logout y no permite pantallas protegidas; errores sin enumeracion; PasswordBox sin texto plano/logs; offline; aislamiento con precedencia arg > env y ResolveLinkTarget para symlinks/junctions (limitación: alias de directorio sin archivo solo sintáctico, modo dev restringido); administracion solo Dueña con confirmación Confirmar/Cancelar y backend autoridad, sin crear otra Dueña ni configurar `usuarios.administrar`; V1-V7 intactas.
- Alcance: sin recuperacion de Dueña, Firebase, Drive, respaldos auto, ventas ni cambios de inventario; ruta productiva y PC real pendientes.

## D-30 — WPF catálogo, precios y carga inicial ESTABLECER (sin costos)

- Estado: integrado en `70ec664`; pendiente de revision.
- Fecha: 2026-09-22.
- Fuente: bloque autorizado (CatalogoViewModel/CatalogoView).
- Regla: reutiliza `Crear/Editar/DesactivarProducto/Variante`, `FijarPrecio`, `ConsultasCatalogo` (paginada + SKU) y `CargarExistenciaInicial`; Dueña no recibe permisos automáticamente, se otorgan explícitamente; lecturas con sesión vigente sin permiso de escritura; SKU inmutable, talle/color opcionales, sin listas cerradas, desactivación conserva historial; precio ARS exacto a centavos sin float/double, historial nunca editado; grilla ESTABLECER multi-fila con operacion_id estable (contenido cambiado → nuevo id, reintento idéntico conserva id, rollback total), etiquetada inequívocamente no recepción/suma; V1-V7 intactas.
- Alcance: sin costos/FIFO/promedio/impuestos/márgenes (D-07), sin recepción/POS/caja/Drive, sin ruta productiva, validación visual interactiva pendiente.

## D-31 — WPF proveedores, recepción SUMAR y reversión completa (aislamiento fail-closed)

- Estado: implementado en `feat/wpf-suppliers-receiving`; pendiente de revision.
- Fecha: 2026-09-22.
- Fuente: bloque autorizado (Proveedores/Recepcion/RecepcionesConsulta ViewModels).
- Regla: `IRepositorioRecepciones.Listar/Obtener` + `ConsultasRecepciones` (sesión vigente, sin SQL en WPF); `ServiciosComerciales` reutiliza `Crear/Editar/DesactivarProveedor`, `ConfirmarRecepcion` (SUMAR ingreso, costo NULL vs 0, ARS exacto, variantes repetidas/cantidades inválidas sin parciales) y `RevertirRecepcion` (COMPLETA con motivo, stock insuficiente rechaza toda, no doble, conserva recepción/costos/movimientos/auditoría); operacion_id estable con instantánea inmutable y rotación solo tras éxito; selector fail-closed con hard-link (FileIndex) y reparse de todos los antecesores, ouvre handle de solo metadatos (CreateFile acceso cero) sin leer SQLite, `--dev-db` inválido produce error; V1-V7 intactas, sin V8.
- Alcance: sin reversión parcial/devolución/nota crédito/contabilidad, sin POS/caja/Drive, sin valoración costos, validación visual pendiente.

## D-32 — Incidente base provisional (2026-09-22): original perdido, archivo actual es recreación vacía

- Estado: registrado; corrección del test aplicada en `feat/wpf-stocktake-receiving-recovery`; pendiente de revisión.
- Fecha: 2026-09-22.
- Fuente: auditoría de suite (test `Selector_falla_cerrado_si_no_puede_comprobar_identidad`, versión anterior).
- Registro: el test sobrescribió la base provisional original con texto y luego la eliminó (`File.WriteAllText` + `File.Delete`); no fue migración ni copia. Después se creó manualmente un archivo vacío de 12.288 bytes con la fecha anterior: **NO fue una restauración**. No existe hash anterior ni copia auténtica identificada; no puede afirmarse que los datos originales estén preservados. El usuario confirmó que no había cargado datos propios (solo tabla técnica del prototipo F00).
- Regla: la suite usa exclusivamente archivos ficticios temporales; ningún test escribe, elimina, migra, reemplaza ni restaura la ruta provisional real (ver `docs/ESQUEMA.md` §21). El test `Base_provisional_F00_permanece_intacta` fue retirado (no demostraba igualdad de contenido) y reemplazado por `Sonda_prototipo_funciona_sobre_archivo_ficticio`.
- Alcance: sin cambios en V1–V7; sin restauración adicional sin autorización.

## D-33 — Stock apartado / dañado / no vendible (F03-T04)

- Estado: implementado en `feat/f03-damaged-setaside-stock`; pendiente de revisión.
- Fecha: 2026-09-23.
- Fuente: decisión del usuario (prenda dañada/en revisión permanece en el local pero no puede venderse; baja definitiva deja de existir físicamente).
- Regla: estados Disponible, Apartada/no vendible y Baja definitiva; apartar/reincorporar con motivo (no alteran el físico), baja de apartadas con confirmación y motivo (reduce apartado y físico atómicamente); invariantes físico = disponible + apartado y ninguna cantidad negativa; recepción/carga suman disponible; ajustes negativos, reversión y aprobación de conteos nunca consumen apartadas inadvertidamente; conteos cuentan apartadas y detectan cambios de clasificación con mismo saldo; permisos `inventario.no_vendible.gestionar` y `inventario.baja_definitiva.autorizar` separados y denegados por defecto; V8 aditiva (`apartado_movimiento` + `conteo_clasificacion`), V1–V7 intactas, histórico existente = disponible.
- Alcance: sin POS/caja/Drive, sin valoración de costos; F03 no cerrada (falta validación visual).

## Ajustes visuales menores no bloqueantes — E4-B

- Estado: registrados para una pasada posterior; no bloquean E4-B.
- Catálogo: corregir textos truncados en la paginación y su toolbar.
- Recepción: revisar anchos de columnas para evitar encabezados cortados.
- Inventario: seguir compactando cuando surja naturalmente, sin retroceder en densidad ni volver a layouts excesivamente aireados.

## D-34 — Rediseño UI WPF (etapa aprobada y cerrada; F04 no iniciado)

- Estado: terminado y validado en `feat/wpf-ui-redesign-phase1`; pendiente de revisión y commit.
- Fecha: 2026-09-26.
- Fuente: instrucción de la persona usuaria (cierre documental/técnico del rediseño; etapa aprobada).
- Decisiones consolidadas, verificadas contra el estado real del repositorio:
  1. `LoginWindow` separada: `Views/LoginWindow.xaml(.cs)` existe como `Window`; el enrutamiento bootstrap/login vive en `App.xaml.cs`.
  2. Navegación agrupada por módulos: `ShellDuenaView` agrupa Inicio, Catálogo, Compras (Proveedores, Recepción, Consultas y reversiones), Inventario (Consulta, Conteo físico, No vendible) y Administración (Usuarios), con Perfil y Cerrar sesión a la derecha.
  3. Patrón master/detail para Catálogo, Proveedores, Usuarios y Consultas: `CatalogoView`, `ProveedoresView`, `AdministracionUsuariosView` y `RecepcionesConsultaView` usan selección + panel de detalle.
  4. Ventanas secundarias para ficha de producto y detalle de recepción: `FichaProductoWindow` (+ `FichaProductoViewModel`, `IDialogosCatalogo`) y `DetalleRecepcionWindow` (+ `DetalleRecepcionViewModel`, `IDialogosRecepciones`).
  5. Conteo físico por pasos: `ConteoViewModel` implementa el flujo 1-4 (seleccionar → contar → revisar → aprobar) con `PasoActual`, `SiguientePaso`/`AnteriorPaso`/`IrAPaso` (el stepper solo permite retroceder).
  6. Inicio operativo basado en datos reales: `ConsultasInicio` + `RepositorioInicio` + `DatosInicio` alimentan `PortadaViewModel` (acciones por permiso, conteos pendientes, inventario, actividad); sin datos ficticios en Inicio.
  7. Resolución mínima validada 980×620: `DevMainWindow` declara 1200×750 con `MinWidth=980 MinHeight=620`; captura nativa 64 a 980×620; `ScrollViewer` de respaldo solo si la altura no alcanza.
  8. Tokens/`ControlTemplate` centralizados: paleta y estilos en `Recursos/DisenoRecursos.xaml` (`Color.*`, `EstiloBoton*`, etc.); `ContentStringFormat` se propaga con `TemplateBinding` en los 5 templates de `Button` (Primario/Secundario/Quiet/Destructivo/DangerOutline) tras el fix puntual de Inicio; `EstiloSegmento` (`RadioButton`) no lo necesita (sin usos con `StringFormat`). El texto visible de Inicio lo forma la VM (`TextoConteosPendientes`/`TextoApartado` con singular/plural).
  9. No inventar precio de producto, SKU de producto, stock mínimo ni KPIs inexistentes: verificación 2026-09-26 sin ocurrencias de `StockMinimo`/`Kpi`/`PrecioSugerido`/`SkuGenerado` en ViewModels WPF; precio ARS exacto a centavos, SKU inmutable, sin stock mínimo ni KPIs en la interfaz.
- Verificación de cierre: `dotnet build` Debug 0 advertencias 0 errores; suite 263/263; `git diff --check` limpio; sin `.db`, capturas, harnesses, binarios ni artefactos de build en el árbol git (`bin/`/`obj/`/`artifacts/` excluidos del scan; `docs/ui-reference/` son 4 archivos de trabajo HTML/md que quedan sin trackear y fuera del commit propuesto).
- Alcance: sin F04 (ninguna tarea iniciada), sin cambios de reglas comerciales, permisos ni migraciones nuevas; `LocalInfantil.sln`, `AssemblyInfo.cs`, `MainWindow.xaml(.cs)` solo presentan normalización de fin de línea, sin cambio de contenido.

## D-35 � Categor�as controladas, c�digos internos autom�ticos y unicidad Talle+Color

- Estado: implementado en el �rbol de trabajo (sin commit); pendiente de revisi�n.
- Fecha: 2026-09-29.
- Fuente: instrucci�n de la persona usuaria (cierra D-11 Identificaci�n en lo relativo a c�digos; etiquetas f�sicas siguen pendientes).
- Regla: categor�as controladas en tabla `categoria` (12 del seed con prefijo �nico; "Otros" existe pero solo como elecci�n expl�cita); producto nuevo exige `CategoriaId` activa v�lida ("Seleccion� una categor�a.", sin default autom�tico); columna texto libre `producto.categoria` eliminada en V9 (fin de la doble fuente; `CategoriaId` �nica fuente); c�digo interno autom�tico e inmutable Producto `{PREFIJO}-{NNN}` (ej REM-001) / Variante `{CODIGO_PRODUCTO}-{NN}` (ej REM-001-01), generado en la misma sesi�n BEGIN IMMEDIATE del alta con secuencias por categor�a y por producto, sin talle/color en el c�digo, sin edici�n manual, estable ante renombre/reclasificaci�n; texto visible "C�digo interno" (Sku solo como nombre interno heredado); unicidad Talle+Color por producto con normalizaci�n centralizada `NormalizacionVariante` (trim, FormC, UpperInvariant; NULL = vac�o) aplicada igual en caso de uso (mensaje humano "Ya existe una variante con ese talle y color.") y en persistencia (columnas `talle_norm`/`color_norm` + UNIQUE); typeahead por c�digo o nombre con resoluci�n exacta sin sugerencia obligatoria (ingreso r�pido/lector); V9 `categorias-codigos-internos` (sin referencia a F04); `producto.categoria_id` NOT NULL con FK (heredados van a OTR solo como fallback de migraci�n); `categoria.prefijo` NOT NULL UNIQUE y `categoria.nombre` UNIQUE binario (sin escritor de categor�as en la app: solo seed; un futuro editor deber� normalizar con la misma funci�n); creaci�n m�ltiple de variantes diferida a un caso batch transaccional (sin altas independientes desde UI).
- Alcance: sin F04, sin commit ni push; fixture ficticia `LocalInfantil-fase1/fase1.db` reseteada y resembrada (3 productos, 6 variantes, precios, stock); `docs/ESQUEMA.md` no se toca (documento hist�rico: no describe el esquema actual).
