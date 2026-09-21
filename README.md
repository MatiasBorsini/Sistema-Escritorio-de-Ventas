# LocalInfantil

Estructura inicial para una aplicación de escritorio de un único local de ropa infantil. El alcance actual incluye la publicación de prueba de **F00-T06**: navegación de demostración, comprobación mínima de SQLite, prototipo de impresión y una carpeta self-contained para el equipo de desarrollo. No contiene flujos comerciales ni datos reales del comercio.

## Requisitos provisionales

- Windows para compilar y ejecutar el proyecto WPF.
- SDK de .NET 8.0.x o superior compatible con `net8.0`.

El framework objetivo sigue siendo `net8.0` / `net8.0-windows` **de forma provisional** para conservar el esqueleto compilable. .NET 8 finaliza soporte en noviembre de 2026; .NET 10 LTS es la candidata para la decisión final, sujeta a validar la versión, edición y arquitectura del Windows real del local. Ver [DECISIONES.md](docs/DECISIONES.md).

## Estructura

- `src/LocalInfantil.Domain`: reglas y tipos puros, sin dependencias de UI ni SQLite.
- `src/LocalInfantil.Application`: casos de uso; depende de Domain.
- `src/LocalInfantil.Infrastructure`: adaptador SQLite de la comprobación técnica e I/O futuro; depende de Application y Domain.
- `src/LocalInfantil.Wpf`: único ejecutable WPF; depende de Application e Infrastructure.
- `tests/LocalInfantil.Tests`: pruebas de Domain y de la navegación WPF del prototipo.

La ventana principal ofrece Inicio y Estado del prototipo mediante MVVM simple. En Estado se puede ejecutar una comprobación técnica SQLite y abrir una vista previa de impresión con datos ficticios. Solo desde esa vista se abre el diálogo nativo para seleccionar y confirmar una impresora; no se imprime automáticamente. No realiza operaciones comerciales ni conexiones a red.

La base SQLite, sus archivos WAL/SHM, respaldos, logs y secretos se mantienen fuera del repositorio. Solo para el prototipo, la ruta predeterminada es `%LOCALAPPDATA%\LocalInfantil\Prototype\localinfantil-prototype.db`; no es una decisión de producción. La ruta definitiva de datos depende de D-13 y se resolverá antes de guardar datos reales.

## Comandos

Desde la raíz de la solución:

```powershell
dotnet restore LocalInfantil.sln
dotnet build LocalInfantil.sln --configuration Debug
dotnet test LocalInfantil.sln --configuration Debug --no-build
```

## Publicación de prueba

La publicación actual es una carpeta self-contained de prueba para la arquitectura confirmada del equipo de desarrollo (`win-x64`). No es un instalador ni valida la arquitectura de la PC del local.

```powershell
dotnet publish src\LocalInfantil.Wpf\LocalInfantil.Wpf.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts\publish\win-x64 --disable-build-servers -m:1 -p:DebugType=None -p:DebugSymbols=false
.\artifacts\publish\win-x64\LocalInfantil.Wpf.exe
```

La carpeta publicada se puede eliminar sin borrar datos de usuario porque la comprobación SQLite usa `%LOCALAPPDATA%\LocalInfantil\Prototype\`, fuera de `artifacts\publish`. Para borrar solo la publicación de prueba:

```powershell
Remove-Item -LiteralPath .\artifacts\publish\win-x64 -Recurse
```

## Convenciones

- Código y documentación en español cuando describan el negocio; nombres de tipos y APIs de .NET se mantienen claros y consistentes.
- `Nullable` e `ImplicitUsings` están habilitados en todos los proyectos.
- Las dependencias siguen el sentido indicado en `ESTRUCTURA.md`; WPF nunca accede a SQLite directamente.
- El único paquete de SQLite incorporado para F00-T04 es `Microsoft.Data.Sqlite` 8.0.31; no se utiliza Entity Framework ni otro ORM.

## Estado

Consultar `PLAN.md` para la planificación y `docs/DECISIONES.md` para decisiones pendientes. F00-T02 queda bloqueada hasta validar la PC del local; F00-T06 continúa pendiente de validación visual de la publicación, instalador/desinstalación reales y pruebas en ese equipo.
