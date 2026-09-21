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
