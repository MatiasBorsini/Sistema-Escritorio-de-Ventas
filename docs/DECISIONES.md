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

## D-35 — Categorías controladas, códigos internos automáticos y unicidad Talle+Color

- Estado: implementado en el árbol de trabajo (sin commit); pendiente de revisión.
- Fecha: 2026-09-29.
- Fuente: instrucción de la persona usuaria (cierra D-11 Identificación en lo relativo a códigos; etiquetas físicas siguen pendientes).
- Regla: categorías controladas en tabla `categoria` (12 del seed con prefijo único; "Otros" existe pero solo como elección explícita); producto nuevo exige `CategoriaId` activa válida ("Seleccioná una categoría.", sin default automático); columna texto libre `producto.categoria` eliminada en V9 (fin de la doble fuente; `CategoriaId` única fuente); código interno automático e inmutable Producto `{PREFIJO}-{NNN}` (ej REM-001) / Variante `{CODIGO_PRODUCTO}-{NN}` (ej REM-001-01), generado en la misma sesión BEGIN IMMEDIATE del alta con secuencias por categoría y por producto, sin talle/color en el código, sin edición manual, estable ante renombre/reclasificación; texto visible "Código interno" (Sku solo como nombre interno heredado); unicidad Talle+Color por producto con normalización centralizada `NormalizacionVariante` (trim, FormC, UpperInvariant; NULL = vacío) aplicada igual en caso de uso (mensaje humano "Ya existe una variante con ese talle y color.") y en persistencia (columnas `talle_norm`/`color_norm` + UNIQUE); typeahead por código o nombre con resolución exacta sin sugerencia obligatoria (ingreso rápido/lector); V9 `categorias-codigos-internos` (sin referencia a F04); `producto.categoria_id` NOT NULL con FK (heredados van a OTR solo como fallback de migración); `categoria.prefijo` NOT NULL UNIQUE y `categoria.nombre` UNIQUE binario (sin escritor de categorías en la app: solo seed; un futuro editor deberá normalizar con la misma función); creación múltiple de variantes diferida a un caso batch transaccional (sin altas independientes desde UI).
- Alcance: sin F04, sin commit ni push; fixture ficticia `LocalInfantil-fase1/fase1.db` reseteada y resembrada (3 productos, 6 variantes, precios, stock); `docs/ESQUEMA.md` no se toca (documento histórico: no describe el esquema actual).

## D-36 — F00-T02/F00-T06: no bloquean desarrollo; bloquean producción/entrega

- Estado: criterio de planificación vigente; sin commit.
- Fecha: 2026-09-29.
- Fuente: decisión de la persona usuaria (aún no dispone de la PC definitiva del local).
- Regla: F00-T02 y F00-T06 permanecen PENDIENTES de validación real y NO se marcan completadas ("Pendiente — validación en hardware definitivo antes de producción"). Dejan de ser bloqueantes para DESARROLLO: se puede continuar con F04 y fases posteriores usando el entorno actual y DB ficticias. Pasan a ser bloqueantes para instalación definitiva, puesta en producción y entrega al local. Antes de producción será obligatorio validar en la PC definitiva: Windows/arquitectura, publicación, arranque, persistencia, permisos de archivos, DPI/resolución, rendimiento, impresora cuando corresponda y funcionamiento interactivo general. No cambia ningún otro estado de F00/F01/F02/F03.
- Alcance: solo planificación (PLAN.md: F00-T02, F00-T06, aceptación de F00 y registro de avance); sin cambios de código, sin F04 en esta tarea, sin commit ni push.

## D-37 — Cierre F04: POS con venta Disponible, snapshots y anulación total

- Estado: implementado en el árbol de trabajo `feat/f04-pos` (sin commit); pendiente de revisión.
- Fecha: 2026-10-03.
- Fuente: diseño F04 aprobado e instrucciones de implementación de la persona usuaria.
- Decisiones efectivamente adoptadas:
  1. POS sin persistir carrito (solo VM; cancelar limpia con confirmación si hay líneas; sin venta en espera).
  2. La venta consume Disponible (físico - apartado), revalidado dentro del `BEGIN IMMEDIATE`; sin reservas.
  3. Precio revalidado al confirmar: si cambió, aborta sin escribir, actualiza el carrito y pide revisión humana; el snapshot persistido es el vigente aceptado.
  4. Snapshots históricos por línea (código, nombre, talle, color, precio); la venta histórica no depende del catálogo posterior.
  5. `T-000001` como número interno visible (secuencia transaccional); `operacion_id` técnico para idempotencia.
  6. Pagos manuales (efectivo, transferencia, débito, crédito, billetera); importe imputado con S == total; `recibido` solo efectivo con `recibido >= importe`; vuelto derivable no persistido.
  7. Pagos combinados como N filas de pago.
  8. Mercado Pago fuera de alcance (sin API, terminal, QR, OAuth, webhooks).
  9. F04 no escribe tablas `caja_*` (conciliación en F05).
  10. Ticket interno no fiscal con leyenda "COMPROBANTE INTERNO — NO FISCAL", reconstruible desde snapshots, reimpresión inmutable; impresora física pendiente para producción.
  11. Anulación total compensatoria (conserva venta/líneas/pagos, `reversion_venta`, ingresos compensatorios, motivo obligatorio, anti-doble, auditoría en la misma tx); devoluciones parciales diferidas.
  12. Permisos `ventas.confirmar` (Dueña y Empleada) y `ventas.anular` (solo Dueña), deny-by-default.
  13. Idempotencia por `venta.operacion_id` UNIQUE con mensaje humano (sin exponer duplicación cruda).
- Alcance: sin F05, sin commit ni push; V10 `ventas-pos`; suite 330/330; capturas reales 80–89 fuera del árbol git; fixture ficticia con ventas de prueba anuladas (stock restituido).

## D-38 — F05: caja única, arqueo ciego y movimientos compensatorios

- Estado: implementado en `feat/f05-caja`; sin commit ni push.
- Fecha: 2026-10-03.
- Fuente: diseño F05 aprobado por la persona usuaria.
- Decisiones efectivamente adoptadas:
  1. V11 `caja-conciliacion-f05` es aditiva respecto de V10. No vuelve a escribir pagos/ventas F04 ni asigna sesiones/movimientos retroactivos a ventas previas; la migración V10→V11 se prueba con base temporal, respaldo e integridad.
  2. Hay una sola sesión de caja abierta global. La apertura registra fondo inicial, responsable, operación idempotente y auditoría; el fondo no se duplica como ingreso.
  3. Confirmar una venta requiere caja abierta. Pago, venta, consumo de Disponible, movimientos de caja y auditoría comparten la transacción `BEGIN IMMEDIATE`; se genera un efecto de caja por pago.
  4. El efectivo esperado solo considera apertura, pagos en efectivo, movimientos manuales en efectivo y compensaciones en efectivo. Medios no efectivos permanecen desglosados para conciliación y no se tratan como billetes contados.
  5. Los movimientos manuales exigen importe positivo, categoría y motivo. Ingreso, gasto y retiro de Dueña tienen categorías/tipos diferentes; retiro requiere permiso específico.
  6. El arqueo es ciego: el valor esperado no se presenta antes de ingresar el conteo. La previsualización calcula diferencia; el cierre vuelve a calcular esperado dentro de su transacción y exige revisión/reconfirmación si cambió. Diferencia distinta de cero exige observación, se conserva como snapshot y no crea un movimiento artificial de ajuste.
  7. Anular una venta conserva sus snapshots y pagos; cada reintegro compensa el importe no compensado del pago original en la sesión abierta actual, respetando el medio efectivo vigente. La operación revierte venta, stock, caja y auditoría en conjunto. No muta cierres anteriores.
  8. Corregir un medio es solo para Dueña, una vez por pago y en sesión abierta. El pago original queda intacto; se registra corrección inmutable y dos movimientos compensatorios (salida del medio anterior/entrada al nuevo), con motivo y auditoría.
  9. Dueña recibe los permisos de caja previstos por la matriz efectiva; Empleada puede abrir, consultar, mover (según categorías permitidas) y cerrar, pero no retirar fondos de Dueña ni corregir medios. Las denegaciones individuales prevalecen y el backend sigue siendo autoridad.
  10. La interfaz Caja permite apertura, consulta de sesión/movimientos, ingreso/egreso manual, corrección de medio, conciliación no efectiva, arqueo ciego y consulta paginada del histórico; el histórico muestra snapshots, no permite editar cierres.
- Verificación automatizada: tests F05 sobre bases temporales aisladas, incluidos rollback de venta/anulación si falla caja, pagos mixtos, permisos, correcciones, arqueo/cierre, concurrencia secuencial y migración V10→V11 preservando venta y pago históricos.
- Pendiente de evidencia: capturas visuales reales 90–100, que requieren iniciar sesión en una instancia interactiva. No se generaron capturas sintéticas ni se modificaron bases históricas.
- Alcance: exclusivamente F05; sin F06, commit, push ni cambios en `main`.
- Verificación técnica final (2026-10-03): `dotnet build` Debug 0 advertencias/0 errores; suite completa 356/356; filtro F05 25/25; filtro de migración/respaldos 26/26; `git diff --check` sin errores. Capturas 90–100 siguen pendientes de validación visual interactiva.
- Estado F05: implementación técnica completa y validación automatizada completa; validación visual/manual WPF (capturas 90–100) pendiente; validación en la PC real pendiente; producción NO habilitada.

## D-39 — F06: cambios/devoluciones genéricos y liquidación local

- Estado: núcleo local publicado en `7b0dd5e231ce4f8038a165fc82f50684f8f54ed2` (rama `feat/f06-cambios-devoluciones`). Build Debug: 0 advertencias/0 errores; suite focalizada de Cambios: 15/15; suite completa: 369/369. Capturas manuales 101–110 pendientes porque la sesión de automatización no expone ventanas WPF (`apps: []`). F06-T05 permanece bloqueada por D-05; D-01 continúa pendiente para procedimiento fiscal externo y producción.
- Fecha: 2026-10-03.
- Fuente: diseño y autorización de implementación F06 de la persona usuaria.
- Decisiones implementadas:
  1. Una sola operación genérica `C-*` contiene N entradas de la venta original y M salidas nuevas; número separado de T-* y `operacion_id` único para idempotencia. Las operaciones y sus líneas/liquidaciones son históricas e inmutables; no existe anulación de C-*.
  2. Crédito de entrada = cantidad devuelta × precio unitario snapshot de `venta_linea`; la salida usa variante activa, stock Disponible y precio vigente aceptado. El precio se vuelve a consultar dentro de la transacción. Si cambió, no se persiste nada: el resultado específico entrega nombre y precios esperado/vigente, la UI actualiza el valor y exige una nueva confirmación.
  3. Disponible para devolver se revalida dentro de `BEGIN IMMEDIATE`; V12 agrega trigger que impide que las entradas acumuladas superen la cantidad vendida, además de unicidad por operación/línea. Ventas anuladas no admiten cambios.
  4. Diferencia = total nuevo − crédito original. Diferencia cero no exige caja ni registra liquidación/movimiento; toda diferencia distinta de cero exige caja abierta. Cobros positivos admiten los cinco medios F04 y combinación exacta; efectivo conserva recibido/vuelto. No se crea una venta ficticia.
  5. Reintegro negativo por defecto en efectivo con permiso efectivo existente. Medio excepcional exige autorización efectiva de Dueña, directa con sesión de Dueña o credencial presencial secundaria de una Dueña activa desde sesión Empleada; no cambia la sesión ni persiste la contraseña. Se registra el usuario autorizante.
  6. Prenda apta: movimiento de ingreso a Disponible. No apta: ingreso físico más movimiento Apartado con motivo; no aumenta Disponible ni reactiva variantes inactivas.
  7. La búsqueda admite T-* o coincidencias por código/nombre con fechas opcionales y devuelve una lista para selección manual (sin elegir automáticamente si hay varias). No exige ni inventa identidad de cliente.
  8. V12 es aditiva; V1–V11 permanecen intactas. Añade `cambio`, `cambio_entrada`, `cambio_salida`, `cambio_pago`, secuencia C-* y vínculo coherente a `caja_movimiento`; conserva movimientos/operaciones/caja históricos y respalda la migración según política existente.
  9. El comprobante C-* muestra snapshots, importes, medios, autorización si aplica y leyenda «COMPROBANTE INTERNO — NO FISCAL». Consultar/reimprimir no modifica datos. Búsqueda e histórico usan referencias humanas.
  10. `AnularVenta` consulta cambios confirmados dentro de su transacción y rechaza la anulación total si la venta tiene cualquiera; preserva el historial F04/F06.
  11. `cambios.registrar` y `cambios.consultar` se agregan a la misma matriz de autorización efectiva para Dueña/Empleada; se mantienen denegaciones individuales prevalentes y deny-by-default. Autorización sensible puede evaluarse usando la sesión de escritura, sin duplicar reglas.
  12. F06-T05 no se implementa: D-05 y F04 carecen de fuente de verdad para promociones. No se infiere promoción por precio, nombre, cantidad ni proximidad de líneas.
  13. No existe una cantidad de días aprobada. Se muestra fecha y antigüedad meramente informativa cuando resulta pertinente; no se afirma «vencido/fuera de término» ni se bloquea por antigüedad.
  14. D-01 sigue pendiente: la aplicación no emite, anula ni revierte comprobantes fiscales. La función local/ticket interno no constituye autorización de producción.
- Alcance original: solo F06 core, sin cambios en `main`; migración y pruebas usaron bases temporales, sin alterar datos reales.

## D-40 — F07-T01: clientes opcionales, sin cuenta corriente

- Estado: decisión implementada técnicamente en `feat/f07-clientes-cuenta-corriente`; F07-T01 completada técnicamente. Capturas/revisión visual WPF 111–118 pendientes (`apps: []`). Sin commit ni push.
- Fecha: 2026-10-04.
- Fuente: aprobación de F07-T01 y límites de alcance indicados por la persona usuaria.
- Decisiones:
  1. La ficha mínima contiene nombre completo requerido, teléfono y observación opcionales, activación lógica y timestamps. No exige DNI/CUIT/email/domicilio ni hace único el teléfono.
  2. Un cliente es opcional en venta común. V13 agrega `venta.cliente_id` nullable con FK `ON DELETE RESTRICT` y `cliente_nombre_snapshot` nullable. La confirmación valida cliente existente/activo dentro de la misma transacción de la venta. Ventas V1–V12 permanecen sin asociación ni snapshot; no se inventa cliente histórico.
  3. La referencia mutable al cliente facilita búsqueda/historial; tickets e identidad histórica usan solo el snapshot del nombre. No se guarda teléfono en el snapshot. Una edición o desactivación futura no cambia el ticket ya generado.
  4. `clientes.consultar` se concede inicialmente a Dueña y Empleada; `clientes.administrar` se concede a Dueña y se deniega a Empleada por matriz. Se conserva deny-by-default y las concesiones/denegaciones individuales efectivas, con autorización real en Application.
  5. El historial paginado reúne ventas y cambios/devoluciones por `venta_original → cliente`, con T-*/C-*, fecha, total/estado y resumen de diferencia. No calcula ni presenta saldo, deuda, crédito o pagos a cuenta; no hace consultas N+1.
  6. La venta sin cliente sigue utilizando el flujo/pagos actuales. No se crean cuentas, cargos, abonos, saldos a favor, límites, vencimientos, intereses, cuotas, venta parcialmente pagada ni ajustes de deuda. Fiado sigue deshabilitado.
  7. F07-T02/D-02/D-03 continúa pendiente y bloquea activar fiado/cuenta corriente; F07-T03–T06 no se implementaron.
- Verificación técnica: V12→V13 con respaldo y conservación de venta histórica sin cliente; CRUD, validación de nombre, teléfono repetido, búsqueda y paginación; permisos/overrides; asociación opcional POS, snapshot, cliente inactivo, ticket e historial C-*; VM POS/clientes. Build Debug: 0 advertencias/0 errores; pruebas F07-T01: 5/5; suite completa: 374/374; `git diff --check` limpio.
- Verificación manual: no realizada. `cua.getState()` devolvió `apps: []`; capturas 111–118 no generadas ni simuladas. Revisar visualmente a 1200×750 y 980×620 cuando se disponga de sesión WPF interactiva.

## D-41 — F07: política de cuenta corriente y alcance de activación

- Estado: aprobada e implementada técnicamente en la rama de trabajo `feat/f07-clientes-cuenta-corriente`; sin commit ni push. F07 no habilita producción.
- Fecha: 2026-10-04.
- Fuente: aprobación explícita de la persona usuaria para F07-T02–T06.
- Reglas aprobadas:
  1. Solo se vende fiado a un cliente existente y activo. Dueña puede autorizarlo; Empleada solo cuando su permiso individual efectivo `cuenta_corriente.vender_fiado` lo concede. La denegación prevalece y la falta de concesión deniega.
  2. El límite por cliente es opcional y nullable; solo Dueña lo configura. Si existe, el saldo actual más la nueva deuda no puede excederlo. Revalidación dentro de la operación atómica.
  3. La venta puede ser total o parcialmente a cuenta. Todo pago recibido se registra normalmente y afecta caja por su importe recibido; el importe impago genera cargo. Incluso una venta totalmente fiada requiere sesión de caja abierta conforme D-38, pero no crea movimiento monetario por la parte impaga.
  4. El vencimiento, si se informa, es una referencia informativa; no crea intereses ni bloqueo automático.
  5. Abonos pueden combinar medios, nunca exceden deuda y se aplican automáticamente FIFO a cargos pendientes. Cada componente y aplicación conserva importe/medio exactos; el recibo es histórico y no fiscal.
  6. Ajustes son exclusivos de Dueña, requieren motivo y son compensatorios e inmutables; un ajuste no puede producir saldo a favor. El saldo siempre deriva del ledger firmado.
  7. Una devolución/cambio F06 con diferencia negativa y deuda de la venta original exige elección explícita entre aplicar crédito únicamente a esa deuda o reembolsar. Se muestra vista previa y requiere confirmación; se vuelve a validar antes de escribir. Nunca se reduce deuda automáticamente. El remanente al elegir aplicar se procesa como reintegro por el flujo de caja F06. Diferencias positivas no generan deuda.
  8. Anulación total de venta cancela deuda impaga y solo reintegra importes efectivamente recibidos (en venta o abonos), por sus medios originales y mediante caja abierta. Una venta totalmente impaga no genera movimiento de caja. Se conserva la protección F06: ventas con cambio confirmado no admiten anulación total.
  9. V14 es aditiva sobre V13; no altera V1–V13 ni atribuye deuda a ventas históricas. La migración con datos exige y verifica respaldo según el migrador existente.
- Implementación: ledger y aplicaciones inmutables, límite por cliente, recibos con snapshots, integración de fiado en ConfirmarVenta, abonos, ajustes, consulta/historial, anulación, decisión explícita de devolución F06, permisos efectivos y UI local/offline. Sin nuevas dependencias ni cambios a D-13.
- Verificación automática (2026-10-04): `dotnet build LocalInfantil.sln --configuration Debug` correcto, 0 advertencias/0 errores; suite completa 386/386; filtro de clientes/cuenta corriente 17/17; prueba de migración V13→V14 con datos 1/1; `git diff --check` limpio. Las pruebas usan bases temporales aisladas.
- Verificación visual/manual: pendiente; la automatización no expone ventanas WPF (`apps: []`). No se generaron capturas sintéticas. Capturas requeridas 119–129 y flujo manual quedan pendientes en `docs/PRUEBAS_MANUALES.md`.
- Límites: F00-T02/validación de PC real permanecen pendientes; F00-T06 conserva criterios de PC real; D-01 y otras decisiones de producción permanecen abiertas; producción NO habilitada.

## D-42 — F08: respaldos, Drive y recuperación

- Estado: política aprobada e implementada técnicamente en `feat/f08-backup-recovery`; rama sin commit/push al cierre de esta tarea. Sin credenciales/proyecto OAuth real, por lo que no se considera integrada ni validada una cuenta remota. Producción NO habilitada.
- Fecha: 2026-10-05.
- Fuente: aprobación explícita de F08/D-09 por la persona usuaria.
- Decisiones:
  1. D-09 queda resuelta para el diseño: almacenamiento local en `%LOCALAPPDATA%\LocalInfantil\Backups`, resuelto con `Environment.SpecialFolder.LocalApplicationData`; subdirectorios `Local`, `Queue`, `Staging`, `Secrets` y `Locks`. Se detectan rutas que parecen sincronizadas y se advierte; la ruta sigue provisional hasta validar la PC real.
  2. Snapshot consistente cada dos horas de reloj mientras el proceso funciona, uno de catch-up al inicio si la copia válida más reciente tiene al menos dos horas, copia manual y solicitud adicional durable tras confirmación de cierre de caja. El único worker serializa manual/programado/cierre. SQLite/VACUUM se ejecuta fuera del Dispatcher; no se usa servicio Windows ni Task Scheduler.
  3. La fuente de snapshot permanece `IServicioRespaldos`/`ServicioRespaldos` (VACUUM INTO, integridad, claves foráneas, esquema esperado). La base activa no se sincroniza directamente. F08 no agrega V15 ni altera tablas, V14 ni históricos.
  4. Cada archivo es un contenedor versionado `.libackup`: magic `LIBACKUP`, versión y header SecretStream públicos mínimos; metadata y base se protegen. GZip .NET se hace streaming antes del cifrado; no se carga la base completa a RAM. Se registran tamaños original/comprimido/paquete dentro de metadata autenticada y el paquete en el sidecar.
  5. Cifrado con `Sodium.Core` 1.4.1 (MIT) como dependencia estable de distribución y libsodium nativo transitivo 1.0.22 (ISC), invocando únicamente por P/Invoke las funciones oficiales `crypto_secretstream_xchacha20poly1305_*`; se evitó el wrapper SecretStream alpha porque el paquete estable no expone esa API. Se validó en Windows de desarrollo/net10.0 y la publicación win-x64 queda incluida en la verificación de tarea. No se implementó criptografía propia.
  6. Código aleatorio de recuperación de 256 bits, Base32 en grupos con checksum de 24 bits. Se muestra al configurar, exige reingreso antes de guardarse y puede imprimirse solo por elección explícita en una hoja que no se guarda automáticamente. En otra PC el mismo código se puede importar y proteger para el usuario Windows actual.
  7. La clave maestra y tokens OAuth se protegen con DPAPI `CurrentUser` fuera de SQLite/paquetes/sidecars. La frontera `IProtectorSecretos` permite testear almacenamiento; el adaptador productivo usa `ProtectedData`. Si falla el DPAPI local, la app continúa abriendo y la Dueña puede volver a proteger el código original; reemplazar la clave requiere confirmación explícita.
  8. Sidecar JSON por backup, publicación temporal→atómica, con ID estable, timestamp UTC, motivo, formato, ruta, estado, SHA-256, versiones, intentos/error sanitizado e IDs remotos; nunca incluye recovery key, access token, refresh token ni client secret. Se reconcilian manifiestos/archivos autenticando huérfanos; `.tmp` nunca es válido. La referencia a cierre se conserva para deduplicación y se elimina al completar la copia local.
  9. Retención local: todos los backups válidos dentro de siete días; se preservan pendientes, última copia y staging activo. Se limpia solo después de publicar y verificar una nueva copia. Drive: conservar todo hasta siete días; para días >7–30, uno por día priorizando motivo cierre de caja y si no la última copia; >30 días elegible a eliminación. No ejecutar borrado si el listado remoto falla/queda ambiguo; 404 al borrar es idempotente.
  10. Drive usa OAuth Desktop, navegador del sistema, loopback IPv4 `127.0.0.1`, PKCE S256, offline refresh y exclusivamente `https://www.googleapis.com/auth/drive.file`. No incluye `client_secret`; `LOCALINFANTIL_GOOGLE_CLIENT_ID` es configuración pública no secreta. IDs de folder/file se guardan fuera de DB/backup; reservas se persisten en sidecar antes de subir; una repetición consulta el ID reservado y valida metadata/tamaño/MD5 disponible. La conectividad nunca condiciona ventas ni cierre.
  11. El proyecto OAuth y Client ID aún no existen/no fueron proporcionados. Según la guía oficial, el estado Testing limita usuarios listados y sus consentimientos expiran a los siete días; por tanto no sirve para operación silenciosa permanente. Antes de uso sostenido hay que configurar y probar el proyecto destinado a producción. Google contempla una excepción de verificación para apps de uso personal con menos de 100 usuarios, pero puede mostrar warning y no es una aprobación automática para este caso; evaluar branding/política vigente en Cloud Console. Fuentes: [OAuth Desktop/PKCE](https://developers.google.com/identity/protocols/oauth2/native-app), [audiencia y Testing](https://support.google.com/cloud/answer/15549945?hl=en), [excepciones de verificación](https://support.google.com/cloud/answer/13464323?hl=en), [scope drive.file](https://developers.google.com/drive/api/guides/api-specific-auth).
  12. Restauración solo con permiso `respaldos.restaurar`, reautenticación de Dueña, caja cerrada y validación final en proceso helper: descifrado/hash/integrity/foreign_key/schema, copia previa verificada, confirmación, reemplazo del mismo volumen y rollback si la nueva DB no abre. Las copias descargadas usan staging local. Se conserva staging en caso de error para diagnóstico local; no borrar manualmente antes de revisar. El recorrido real de reemplazo/rollback queda pendiente.
  13. La app espera como máximo cinco segundos para detener respaldo al salir. Un upload no bloquea indefinidamente; el paquete local sigue en cola. Temporales incompletos se eliminan/reconcilian al iniciar. El error de copia local requiere atención; fallas transitorias de Drive no se notifican modalmente por cada venta.
- Dependencias nuevas: `Sodium.Core` 1.4.1 y `System.Security.Cryptography.ProtectedData` 10.0.0 en `LocalInfantil.Infrastructure`; `Sodium.Core` trae libsodium 1.0.22 en el runtime nativo. Sin EF Core ni cambio arquitectónico.
- Evidencia automatizada/técnica (2026-10-05): restore Release `win-x64`; build Debug 0 advertencias/0 errores; tests focalizados de respaldos 38/38; suite completa detallada 409/409 (4,3977 min); publicación Release self-contained en `artifacts/publish/net10-f08-win-x64`, 409 archivos / 151.021.404 bytes, sin bases de datos, backups, secretos, temporales ni `.pdb`. `libsodium.dll` se cargó desde la publicación. El ejecutable se inició directamente, obtuvo handle de ventana y cerró normalmente; no hubo inspección visual ni validación manual de UI. La automatización cubre scheduler/catch-up/no solapamiento/cola, cierre durable/idempotente, lock entre instancias, snapshot con escritura WAL concurrente, rollback de intercambio y un E2E aislado de venta/pago/caja/cliente/cuenta/C-* restaurado a otra base. Las pruebas SQLite usan bases temporales; no se alteraron bases del usuario.
- Pendientes: OAuth/Drive real y Client ID; recuperación desde Drive en PC nueva; pruebas visuales/manuales de setup, impresión de hoja, reconexión y recorrido de restauración local/Drive/caja abierta/cancelación/rollback; validación PC/hardware real. Estos pendientes impiden declarar F08 globalmente cerrada y producción sigue NO habilitada.

## D-43 — F09: reportes por fecha del evento, sin segunda verdad

- Estado: T01/T02/T03/T05 implementados técnicamente en `feat/f09-reportes`; T04 bloqueada por D-07; sin commit ni push. D-07 sigue pendiente (no se registra promedio ponderado ni ningún criterio).
- Fecha: 2026-10-06.
- Fuente: auditoría e instrucciones F09 de la persona usuaria.
- Decisiones:
  1. Reportes por fecha del evento: anulaciones, devoluciones, cambios, cobros, abonos y reintegros entran en el período en que ocurrieron; nunca se reescriben períodos históricos.
  2. Ventas brutas = SUM de `venta_linea.subtotal_minor` de confirmadas creadas en el período (incluye fiadas, parciales y luego anuladas/devueltas; excluye C-*, abonos, manuales, ajustes, reintegros); con cantidad de ventas.
  3. Ajustes separados: anulaciones (reversiones del período), entradas F06 (crédito reconocido), salidas F06 (mercadería entregada); netas = brutas - anulaciones - entradas + salidas; ningún C-* se cuenta dos veces.
  4. Cobros = pagos iniciales + abonos + diferencias F06 cobradas, por fecha real y medio; reintegros en línea propia (anulación cobrada, devolución F06, remanente tras aplicar a deuda; nunca cancelación de deuda impaga ni crédito aplicado); flujo neto etiquetado explícitamente; vuelto excluido siempre (derivado, no venta ni reintegro ni medio).
  5. Fiado no infla ventas (total en ventas, solo lo recibido en cobros, deuda del libro real); abonos suman cobro en su fecha sin crear venta.
  6. Corrección de medio reporta el medio vigente sin duplicar (original + nuevo); indicador solo en detalle.
  7. Unidades netas por variante = salidas por venta - entradas F06 + salidas F06, por fecha de evento, columnas brutas/devueltas/salidas/netas, con rollup por producto.
  8. Sin temporada en el esquema: criterio "Sin ventas hace N días" (N editable, valor inicial 30, no regla comercial), separando nunca vendidos; muestra última venta/recepción/movimiento cuando existen.
  9. Permisos `reportes.ventas/caja/inventario/costos`; Dueña todo, Empleada solo inventario; backend real en cada consulta.
  10. Sin gráficos ni exportación inicial; sin pestaña de costos; sin tablas resumen (derivar + medir + indexar); V15 solo con `ix_venta_linea_variante` justificado por medición (22 s ? 33 ms).
- Verificación técnica: build Debug 0/0; suite completa con nuevos tests de ventas/cobros/caja/inventario/permisos/performance; capturas reales 130–136 del build; `git diff --check` limpio. Visual/manual en PC real y D-01 pendientes como en el resto de fases; producción NO habilitada.

## D-44 — F11: identificación por código interno y etiquetas Windows

- Estado: aprobada para esta ampliación técnica en `feat/f11-etiquetas-codigos-batch`; no implica hardware real ni producción.
- Fecha: 2026-10-06.
- Fuente: autorización explícita F11 de la persona usuaria.
- Decisiones:
  1. La identidad del barcode es exclusivamente el `codigo_interno` inmutable de la variante (`REM-001-03`). Formato Code128; no se agrega `barcode_id`, EAN/GTIN, serial individual, código de unidad física ni nuevo esquema. Repetir una variante representa varias etiquetas iguales.
  2. La matriz batch admite talle/color opcionales. Reutiliza exactamente `NormalizacionVariante`; preview es lectura sin reserva, usando la secuencia persistida. Confirmación toma el bloqueo `BEGIN IMMEDIATE`, vuelve a validar bajo la misma transacción, omite combinaciones normalizadas existentes y genera/audita las nuevas con el mismo generador secuencial. La repetición de la misma matriz no duplica filas; cualquier fallo revierte variantes, secuencia y auditoría conjuntamente. El batch no fija precio, crea stock ni recepción.
  3. Code128 se implementa en Infrastructure mediante `ZXing.Net` 0.16.11; la aplicación recibe una matriz binaria neutral y WPF dibuja barras vectoriales, evitando bitmap comprimido y bindings de render externos. La librería resuelve encoding/checksum/quiet zone; el contrato se prueba con decode round-trip. Referencia y licencia del paquete: [NuGet ZXing.Net 0.16.11](https://www.nuget.org/packages/ZXing.Net/0.16.11) (Apache-2.0).
  4. Etiqueta mínima: nombre de producto, atributos existentes cuando los hay, barcode Code128 y código humano. Precio opcional, nunca codificado; cuando se solicita, se lee el precio vigente al abrir preview y se refresca nuevamente antes de mostrar el diálogo de impresión. Si no hay precio vigente no se inventa un valor.
  5. Layout genérico A4 multi-etiqueta `FixedDocument`, calculado desde las dimensiones; defaults de desarrollo editables: 50×30 mm, márgenes 4 mm y separaciones 2 mm. No son tamaño definitivo del local, no se persisten en DB y deben calibrarse físicamente. Una etiqueta se ubica completa dentro de su celda/página. Windows `PrintDialog` se abre solo desde la vista previa por acción explícita; cancelación/error no altera base ni trabajo comercial. No se emite directo ZPL/TSPL/EPL ni se presume impresora.
  6. Read-model de etiquetas consulta variantes, precio vigente, movimientos físicos agregados y apartados agregados en una sola lectura SQLite por producto o lote de IDs; disponible = físico − apartado. No hace N+1 y no requiere tabla/migración. La lectura no concede escritura ni reemplaza autorización de comandos.
  7. Después de confirmar recepción/carga inicial se ofrece abrir impresión con cantidades resultantes. Impresión/cancelación es independiente y jamás revierte/ajusta esas operaciones. Reimpresión no persiste contador y no cambia existencias.
  8. POS resuelve primero el código exacto al Enter, sin esperar debounce. La etiqueta no lleva precio; POS sigue validando precio vigente, variante activa y stock Disponible, suma lecturas repetidas a la fila existente y retorna el foco. Lector USB funciona como teclado (HID) pero no es obligatorio ni recibe permiso administrativo adicional.
  9. No se agrega permiso de impresión específico; la gestión batch queda protegida por el permiso existente `catalogo.variantes.gestionar`. No se alteran permisos comerciales de POS.
- Validación automatizada de esta iteración: ver resultados del cierre en PLAN y las pruebas aisladas en `docs/PRUEBAS_MANUALES.md`. Las bases de tests son temporales; no se cambió V1–V15 ni la DB del usuario.
- Pendiente: apertura/recorrido visual WPF y diálogo PDF en sesión interactiva; lector e impresora física, tamaño/calibración/legibilidad en PC local. El entorno de automatización no expuso aplicaciones abiertas (`apps: []`), por lo que no hay capturas ni se afirma prueba visual. Hardware/Windows del local sigue sin confirmar y producción permanece NO habilitada.

## D-45 — F10: arranque normal y separación de datos

- Estado: decisión técnica de preproducción; no aprueba instalación en la PC del local ni habilita producción.
- Fecha: 2026-10-07.
- Fuente: autorización de implementación F10 y revisión del arranque sobre la base F11/V15.
- Decisiones:
  1. Sin argumentos inicia el modo comercial sobre `%LOCALAPPDATA%\LocalInfantil\Data\localinfantil.db`. `--dev-db <ruta>` requiere selección explícita; `--prototype` queda reservado al diagnóstico histórico explícito. La app nunca abre Prototype automáticamente.
  2. El inicio crea/aplica migraciones antes del bootstrap/login. Si falla backup, migración, historial o apertura, no muestra una sesión comercial utilizable. La base heredada sin historial o con versiones desconocidas se rechaza por el migrador existente.
  3. Los tests de inicialización usan rutas temporales aisladas. Ninguna prueba F10 abre ni altera la base de usuario.
  4. La estructura propuesta de Data/Backups/Secrets/Updates deriva de LocalApplicationData. D-13 sigue pendiente como decisión definitiva de almacenamiento; no cargar datos reales hasta resolverla y validar PC/perfil/backup.

## D-46 — F10: actualización firmada y versionado de preproducción

- Estado: protocolo técnico implementado y probado localmente; canal público, clave de producción y release real no configurados.
- Fecha: 2026-10-07.
- Fuente: autorización de F10 y auditoría del payload firmado existente.
- Decisiones:
  1. Versión de preproducción: `0.9.0`, tomada de `<Version>` del proyecto WPF; no usar `1.0.0`. El instalador recibe esa versión desde el proyecto.
  2. El payload Ed25519 es binario canónico `LocalInfantil.UpdateManifest\0canonical-v1`, seguido por ocho campos UTF-8 cada uno precedido por longitud Int32 big-endian: `manifest_version`, versión, URL, SHA-256 normalizado, tamaño, fecha, versión mínima y notas. No se firma JSON/whitespace arbitrario. Cambiar el formato requiere versión/protocolo nuevo.
  3. Solo se acepta HTTPS en producción; el manifiesto y el instalador se validan por firma, tamaño y SHA-256 antes de colocarlos en `Updates/listos`. El par instalador/manifiesto se vuelve a validar al iniciar. Los parciales se eliminan y nunca se ejecutan.
  4. Una comprobación con versión superior inicia descarga en background; no hay instalación automática. La instalación exige acción/confirmación humana.
  5. El firmador de release toma la clave privada fuera del repositorio y no la copia a artefactos. La clave pública productiva debe configurarse separadamente antes de publicar. Sin URL+clave se informa «Canal de actualizaciones no configurado».

## D-47 — F10: puerta de instalación, permisos y límites de plataforma

- Estado: protección y permisos implementados; las pruebas físicas de instalación/Windows local están pendientes.
- Fecha: 2026-10-07.
- Fuente: autorización de F10 y revisión de riesgos de actualización.
- Decisiones:
  1. Se agrega `actualizaciones.instalar`, Dueña permitida y Empleada denegada por matriz. La interfaz permite administrar actualizaciones solo a Dueña; Application comprueba además la sesión Dueña y vuelve a evaluar el permiso efectivo antes de adquirir la puerta. Las concesiones individuales no permiten a una Empleada instalar; ocultar/deshabilitar WPF no es la barrera de seguridad.
  2. Antes de confirmar se abre una sesión SQLite `BEGIN IMMEDIATE`, se comprueba caja cerrada y ausencia de locks de backup/restauración, y se conserva la sesión hasta cancelación o lanzamiento del instalador. No se cierra la caja automáticamente ni se persiste un lock comercial.
  3. `--dev-db` no crea el coordinador productivo, no abre configuración/canal, no crea `Updates/` y no instala. El estado muestra que las actualizaciones están deshabilitadas en desarrollo.
  4. Publicación objetivo para prueba del desarrollador: Release self-contained `win-x64`; no supone la arquitectura/edición del local. Según la [tabla oficial de sistemas Windows compatibles](https://learn.microsoft.com/en-us/dotnet/core/install/windows), .NET 10 enumera solo ediciones/builds Windows 10 LTSC/Enterprise específicos; otras ediciones/versiones requieren validación y no se consideran soportadas por inferencia de que el ejecutable quizá arranque. .NET 10 es LTS con soporte hasta noviembre de 2028 según la [política oficial de soporte de .NET](https://learn.microsoft.com/dotnet/core/releases-and-support). F00-T02 sigue pendiente.
  5. No se añade migración: V15 permanece como última versión de schema. D-01/D-03, D-13, PC real, Drive real y aprobación de producción siguen pendientes.

- Evidencia técnica al 2026-10-07: restore serial y build Debug estándar completaron con concurrencia limitada; NU1900 indica que el entorno no pudo consultar `https://api.nuget.org/v3/index.json` para auditoría de vulnerabilidades. Build estándar: 0 errores con advertencias NU1900; build final `--no-restore`: 0 errores/8 advertencias (3 NU1900 y 5 warnings nullable/analyzers preexistentes en tests de reportes). Suite detallada completa: 476/476 (4,7494 min); subconjuntos focalizados F10 15/15 y F11 (barcode/lotes/POS) 39/39. Publicación Release self-contained win-x64: 410 archivos, 151.798.645 bytes; instalador Inno Setup 6.7.3 generado correctamente: 48.148.989 bytes. Escaneo de publicación: cero bases, backups, símbolos y secretos; SQLite, ZXing y libsodium presentes. La ejecución de proceso WPF aislada con `--dev-db` no fue inspección visual. No se probaron instalación limpia, upgrade ni uninstall en un perfil aislado; ver `docs/PRUEBAS_MANUALES.md`.

## D-48 - F12: Mercado Pago Point con Orders API, saga durable e integracion propia

- Estado: implementacion tecnica en `feat/f12-mercado-pago`; Point/cuenta real pendientes (F12-T07); produccion NO habilitada.
- Fecha: 2026-10-07.
- Fuente: autorizacion F12 y documentacion oficial vigente de Mercado Pago Point / Orders API.
- Decisiones:
  1. Integracion propia inicial con Orders API (`POST /v1/orders`, `type: "point"`); sin SDK y sin Payment Intents legacy para una integracion nueva.
  2. Descubrimiento dinamico de terminales (`GET /terminals/v1/list` paginado); la compatibilidad la define `operating_mode=PDV`, sin hardcodear modelos (NEWLAND/PAX/etc. no existen en el codigo).
  3. Modo PDV solo por accion explicita de Duena (`PATCH /terminals/v1/setup`); `terminal_not_allowed_action` es mensaje humano, nunca cambio automatico.
  4. Saga durable con snapshot relacional previo al POST e idempotency key persistida; timeout != rechazo; solo `processed` habilita confirmacion local idempotente (mismo `operacion_id`).
  5. Medio `mercado_pago` no efectivo y no manual directo; combinados sin fiado; cierre bloqueado con pendientes; F09 lo desglosa sin metricas inventadas.
  6. Cancelacion pre-processed sobre orders propias; reembolso total/parcial con intencion durable y misma key; F06 con cobertura y sin refunds automaticos; anulacion con MP redirige a devolucion.
  7. Access Token propio en DPAPI CurrentUser (`Secrets/mercadopago_token.bin`); config no secreta fuera de la DB; `--dev-db` aislado; permiso `mercadopago.configurar` solo Duena.
  8. Sin webhook (desktop sin backend): polling temporal + reconciliacion al arrancar; degradacion offline sin bloquear el resto.
  9. OAuth Authorization Code queda como requisito documentado para una futura comercializacion a terceros; el `client_secret` no va en el desktop.
  10. Migracion V16 (`mercado-pago-point-f12`); ultima version de schema pasa a V16.

- Evidencia tecnica al 2026-10-07: build Debug 0 errores/0 warnings CS; suite 525/525; focalizada F12 49/49. Publicacion Release self-contained win-x64 por pipeline F10 (410 archivos, 145,1 MB) + instalador 48.196.634 bytes, sin DB/configs/secretos. Point/cuenta real pendientes; ver `docs/MERCADO_PAGO.md` y `docs/PRUEBAS_MANUALES.md`.

## D-49 - Mercado Pago: ampliacion diseñada de Point (modos) y conciliacion de liquidaciones

- Estado: **Etapa A implementada** (modos de Point + cobro manual + V17 acotada) y **etapa de conciliación implementada** (importación CSV + V18 + conciliación automática/asistida). Point/cuenta real, descarga por API y alcance del token de reportes siguen pendientes. Producción NO habilitada.
- Implementado en `feat/f12-mercado-pago` (Etapa A): `ModosPoint` y `modoLocalInfantil` en la config no secreta; `ConfigurarMercadoPago.CambiarModoAsync` (bloqueo con operaciones no terminales); `RegistrarCobroManualPoint` + `ConfirmarVenta.EjecutarConRegistroPagoMercadoPagoAsync` (registro atómico del cobro manual, sin API); gating de Point en POS (`integrado`/`manual`/`desactivado`); selector de modalidad en Configuración → Mercado Pago; dedup de cobros manuales por importe+terminal con confirmación humana explícita (`ListarCobrosManualesSimilaresAsync` + `CobroManualDuplicadoSospechosoException`, ventana 15 min sin excluir conciliados, sin auto-vinculación); unicidad de comprobante (`ExisteCobroManualPorComprobanteAsync`: un comprobante no se reutiliza en otra operación); migración **V17** con la tabla `mercado_pago_cobro_manual` (+ triggers inmutables).
- Implementado en `feat/f12-mercado-pago` (etapa de conciliación): `LectorReporteLiquidacionMp` (parser CSV RFC4180 puro, BOM UTF-8/16, delimitador `,`/`;`, encabezados requeridos, montos a centavos, fechas a UTC, clasificación por `RECORD_TYPE`/`DESCRIPTION`, errores por fila sin abortar, `fila_hash`); `PrevisualizarImportacionLiquidacionMp` (dry-run), `ImportarLiquidacionMp` (idempotente por `archivo_hash` + `fila_hash`, estados `validado/aplicado/parcial/rechazado`, una transacción), `ConciliarLiquidacionMp` (automática solo por identidad remota `LI-<operacionId>`/order/reembolso local; asistida por confirmación humana `asistido_humano`; devolución/contracargo sin reembolso → `requiere_intervencion`), `ConsultarConciliacionMp` (lectura); migración **V18** aditiva con `mercado_pago_importacion`/`mercado_pago_liquidacion` (`fila_hash UNIQUE`)/`mercado_pago_conciliacion` (+ índices parciales y triggers inmutables); permiso `mercadopago.conciliar` (solo Dueña); sección de Conciliación en Configuración → Mercado Pago.
- Fecha: 2026-10-16.
- Fuente: pedido de diseño técnico y auditoría del circuito VENTA → COBRO → LIQUIDACIÓN → CONCILIACIÓN; documentación oficial vigente de reportes de dinero liberado de Mercado Pago; auditoría del esquema real V1/V10/V15/V16 y de la implementación F12.
- Decisiones (recomendadas; sujetas a confirmación de la Dueña):
  1. Se distinguen dos ejes: **modo físico de la terminal** (`operating_mode` PDV/STANDALONE, remoto, solo lo cambia la Dueña por `PATCH /terminals/v1/setup`) y **modo operativo local** (`modoLocalInfantil` = `integrado` | `manual` | `desactivado`). El modo local vive en la config no secreta `Secrets/mercadopago.json`; el valor por defecto de un sistema nuevo es `desactivado`.
  2. Regla anti-doble-cobro: un cobro remoto incierto (`requiere_conciliacion`) **no habilita** la modalidad manual para esa operación ni permite duplicar el cobro; el cambio de modo se bloquea mientras haya operaciones no terminales. Timeout ≠ rechazo; nunca refund automático ni segundo cobro.
  3. Cobro manual: registra venta + pago `mercado_pago` + declaración `mercado_pago_cobro_manual` en estado `pendiente_conciliacion`; importe siempre **bruto**; la confirmación humana no equivale a validación remota. Dedup por importe+fecha+terminal exige confirmación humana, nunca auto-vinculación; la ventana no excluye los ya conciliados y un comprobante no puede reutilizarse en dos operaciones.
  4. La conciliación se apoya en claves fiables por caso: `EXTERNAL_REFERENCE` (`LI-<operacionId>`) y `ORDER_MP`/`order_id` para integrado; `SOURCE_ID`↔`payment_id` **solo si se persiste el `payment_id`** (hoy no se guarda). La coincidencia por importe/fecha es **ayuda**, jamás conciliación automática.
  5. Contabilidad separada y sin doble conteo: venta comercial, cobro bruto, comisión (`MP_FEE_AMOUNT`), costo financiero (`FINANCING_FEE_AMOUNT`), retenciones (`TAXES_AMOUNT`), neto (`NET_CREDIT_AMOUNT`), fecha de liquidación y retiros (`payout`). No se reduce el precio de venta por comisión, no se toma la retención como pérdida automática, no se confunde saldo MP con acreditación bancaria, no se suman movimientos dos veces. Sin liquidación real se muestra "Pendiente de liquidación".
  6. Reportes oficiales: el circuito se apoya en el reporte de dinero liberado (`POST /v1/account/release_report`, `GET .../list`, `GET .../{file_name}`) y, como apoyo, "Todas las transacciones". Primera etapa por **importación CSV manual**; descarga por API como etapa posterior supeditada a validar el alcance del token/cuenta.
  7. Importación CSV: validación de columnas por `key` técnica, dedup por fila + hash de archivo, fechas normalizadas a UTC conservando offset, importes a centavos, errores por fila sin abortar el archivo, y `RECORD_TYPE`/`DESCRIPTION` clasificados (payment/refund/chargeback/tax/retention/payout/saldos) sin asumir que toda fila es venta.
  8. Esquema V17 aditivo: en Etapa A se crea `mercado_pago_cobro_manual`; `mercado_pago_importacion`, `mercado_pago_liquidacion` y `mercado_pago_conciliacion` quedan propuestas para la etapa de conciliación. V16 y el histórico permanecen intactos. Distinción explícita desconocido (NULL) vs cero.
  9. UX compacta: Administración → Mercado Pago con pestañas Configuración y Conciliación; POS con `Efectivo | Transferencia | Mercado Pago Point`. Conciliación/liquidaciones bajo permiso financiero de Dueña (propuesto `mercadopago.conciliar`), sin nuevas capacidades financieras para la Empleada.
  10. Modo integrado, saga F12, reglas de caja F05, reportes F09 y devoluciones F06/F12 **no se modifican** por este diseño.
- Pendiente de confirmación: modalidad a usar por la Dueña; alcance de reportes del token; clave de dedup definitiva con un CSV real; metadata real de cuotas/tarjeta (bloqueada por F12-T07); D-01/D-07 y producción.
- Detalle completo: `docs/LIQUIDACIONES_MERCADO_PAGO.md`; resumen en `docs/MERCADO_PAGO.md` §10 y `PLAN.md` (F12-B).

## D-50 - Mercado Pago: identidad de fila de liquidación y deduplicación correcta

- Estado: **implementada** (V19 aditiva). Corrige un defecto de integridad financiera de V18. Producción NO habilitada.
- Motivo: V18 declaraba `mercado_pago_liquidacion.fila_hash TEXT NOT NULL UNIQUE` calculado **solo** sobre el contenido de la fila. Dos movimientos legítimos con contenido idéntico (mismo tipo, descripción, fecha e importes, sin identificador remoto) colisionaban y el segundo **desaparecía en silencio** por `INSERT OR IGNORE`, subcontando el bruto/neto del reporte sin error visible.
- Modelo de identidad separado en cuatro planos:
  - **Archivo**: `mercado_pago_importacion.archivo_hash` UNIQUE (sin cambios) → reimportar el mismo archivo se bloquea.
  - **Fila importada**: `(importacion_id, fila_ordinal)` UNIQUE. `fila_ordinal` es la posición de la fila de datos (base 1). Dos filas idénticas del mismo archivo se conservan por separado.
  - **Huella de contenido**: `fila_hash` deja de ser UNIQUE (queda indexada). Solo sirve para **marcar posibles duplicados** entre reportes, nunca para descartar filas.
  - **Identidad de operación financiera**: `identidad_remota` (nueva columna, nullable) con la clave del **movimiento** cuando existe: `SOURCE_ID` propio del proveedor, con prefijo de familia (`C:` cobro; `R:` devolución/contracargo). **No** se usa `ORDER_ID` (orden comercial) ni `EXTERNAL_REFERENCE` (referencia de la venta) como identidad, porque pueden repetirse en varios movimientos de una misma orden/venta (pagos parciales, comisión, costo financiero, retenciones, reembolsos parciales, contracargos) y colapsarían movimientos distintos. Sin `SOURCE_ID`, la fila no aporta identidad y queda ambigua (se conserva y se revisa). No se asume que `SOURCE_ID` aislado sea único para todos los tipos: impuestos, saldos, transferencias y desconocidos no aportan identidad remota.
- Reglas de importación (sin descarte silencioso):
  - Filas idénticas dentro del mismo archivo se persisten individualmente; ninguna se descarta por `INSERT OR IGNORE`.
  - Duplicado entre **reportes solapados**: se omite **solo** con `identidad_remota` coincidente (dedupe por movimiento financiero), nunca por importe/fecha/descripción.
  - Fila **ambigua** (sin identidad remota) cuyo contenido ya existe en otro reporte: se conserva pero queda en `requiere_intervencion` (posible duplicado), sin conciliación automática ni doble conteo.
- Conciliación: no se permite doble vínculo con una misma venta ni con una misma operación de reembolso (se marca `requiere_intervencion` en vez de conciliar dos veces). La importación de una fila **no** implica conciliación. No se alteran ventas, pagos, caja ni inventario.
- Migración **V19** (aditiva, reconstrucción de tabla): elimina el `UNIQUE` inline de `fila_hash`, agrega `fila_ordinal` e `identidad_remota`, repuebla con `ROW_NUMBER()` por importación y `identidad_remota` calculada en SQL (misma precedencia que el parser), recrea índices y triggers, todo en dos fases con FK OFF por tener hijas con FK RESTRICT y triggers de borrado. Se preservan filas, conciliaciones y `foreign_key_check`/`integrity_check`.
- Fecha: 2026-10-17.
- Fuente: auditoría del riesgo de `fila_hash UNIQUE` global detectado al cerrar F12-B; corrección autorizada por la Dueña.
- Pendiente: clave de dedup definitiva con un CSV real; descarga por API; producción.
