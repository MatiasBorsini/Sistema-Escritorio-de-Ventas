# Pruebas manuales

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
