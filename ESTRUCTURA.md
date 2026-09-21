# ESTRUCTURA.md — Organización del proyecto

> Estado: estructura propuesta para C# + WPF + SQLite. No significa que los módulos estén implementados. Crear las carpetas de cada módulo cuando se llegue a su fase; no generar cientos de archivos vacíos.

## 1. Árbol de referencia (estructura objetivo)

```text
LocalInfantil/
├── PLAN.md                         # Fases, tareas, alcance y pruebas
├── ESTRUCTURA.md                   # Este mapa de responsabilidades
├── README.md                       # Requisitos, instalación y comandos
├── .gitignore                      # Excluye binarios, bases, respaldos y secretos
├── LocalInfantil.sln                # Solución de Visual Studio
├── docs/
│   ├── DECISIONES.md               # Aprobadas y pendientes; quién/cuándo decidió
│   ├── REQUISITOS.md                # Reglas del local, sin inventar condiciones
│   └── PRUEBAS_MANUALES.md          # Impresora, instalación, restauración, uso real
├── src/
│   ├── LocalInfantil.Wpf/           # Único ejecutable e interfaz de escritorio
│   │   ├── App.xaml
│   │   ├── App.xaml.cs              # Inicio y composición; no reglas de ventas
│   │   ├── MainWindow.xaml
│   │   ├── MainWindow.xaml.cs       # Solo comportamiento visual
│   │   ├── Views/                  # Crear pantallas a medida que se implementen
│   │   │   ├── Pos/
│   │   │   ├── Productos/
│   │   │   ├── Inventario/
│   │   │   ├── Caja/
│   │   │   ├── Cambios/
│   │   │   ├── Clientes/
│   │   │   └── Configuracion/
│   │   ├── ViewModels/             # Estado de pantalla y comandos
│   │   ├── Controls/               # Controles reutilizables, solo si se necesitan
│   │   ├── Converters/             # Convertidores de binding, solo si se necesitan
│   │   └── Resources/              # Estilos e íconos propios (no datos del local)
│   ├── LocalInfantil.Application/   # Casos de uso y coordinación transaccional
│   │   ├── Features/
│   │   │   ├── Ventas/
│   │   │   ├── Productos/
│   │   │   ├── Inventario/
│   │   │   ├── Caja/
│   │   │   ├── Cambios/
│   │   │   ├── Clientes/
│   │   │   └── CuentasCorrientes/
│   │   ├── Abstractions/           # Interfaces de persistencia/servicios externos
│   │   └── Contracts/              # Solicitudes y respuestas de casos de uso
│   ├── LocalInfantil.Domain/        # Entidades, reglas puras, tipos de valor
│   │   ├── Entities/
│   │   ├── ValueObjects/
│   │   └── Rules/
│   └── LocalInfantil.Infrastructure/# Implementaciones concretas de I/O
│       ├── Persistence/
│       │   ├── Sqlite/
│       │   ├── Repositories/
│       │   └── Migrations/         # SQL versionado, nunca cambiar una ya aplicada
│       ├── Printing/
│       ├── Backups/
│       ├── GoogleDrive/
│       └── Updates/
└── tests/
    └── LocalInfantil.Tests/        # Pruebas de reglas, transacciones y migraciones
```

**Importante:** el árbol muestra el destino final; en F00-T01 solo crear lo necesario para iniciar. `Views/Cambios`, `Backups`, `GoogleDrive` y demás se agregan en sus fases. El nombre `LocalInfantil` es un identificador de trabajo que se puede renombrar al inicio, antes de crear referencias entre proyectos.

## 2. Dependencias permitidas

```text
LocalInfantil.Domain              -> no depende de WPF, SQLite ni servicios externos
LocalInfantil.Application         -> depende de Domain
LocalInfantil.Infrastructure      -> implementa contratos de Application; depende de Application/Domain
LocalInfantil.Wpf                 -> depende de Application y, en el punto de arranque, de Infrastructure
LocalInfantil.Tests               -> depende únicamente de proyectos que prueba
```

- Las vistas solo muestran información y envían comandos a ViewModels; no hacen consultas SQL.
- El ViewModel no calcula saldos definitivos ni modifica registros directamente: solicita casos de uso.
- Application ejecuta operaciones completas; Domain protege reglas e invariantes.
- Infrastructure gestiona SQLite, impresión, respaldos, Drive y actualizaciones.
- Se permite code-behind de WPF para detalles visuales que no sean reglas de negocio.
- No crear API HTTP local, servidor web, microservicios ni capas extras sin necesidad comprobada.

## 3. Ejemplo de ubicación de archivos al implementar una venta

```text
src/LocalInfantil.Wpf/Views/Pos/PosView.xaml
src/LocalInfantil.Wpf/ViewModels/PosViewModel.cs
src/LocalInfantil.Application/Features/Ventas/ConfirmarVenta.cs
src/LocalInfantil.Application/Contracts/ConfirmarVentaRequest.cs
src/LocalInfantil.Domain/Entities/Venta.cs
src/LocalInfantil.Infrastructure/Persistence/Repositories/VentaRepository.cs
tests/LocalInfantil.Tests/Ventas/ConfirmarVentaTests.cs
```

Estos son ejemplos de nombres y responsabilidades, **no archivos creados aún**. No hace falta crear todas las entidades de antemano; mantener agrupación por característica cuando la cantidad de clases crezca.

## 4. Archivos de datos EN LA COMPUTADORA DEL LOCAL, no en el repositorio

```text
<directorio-de-datos-acordado>/
├── data/local.db                   # Base activa de SQLite
├── backups/                        # Copias consistentes locales y cola de subida
└── logs/                           # Diagnóstico con rotación, sin secretos
```

No guardar la base dentro de `src/`, del directorio del instalador, en `Program Files` ni en una carpeta sincronizada directamente con Drive. Se suben **copias** independientes, no la base activa. Ignorar `.db`, archivos WAL/SHM, copias, tokens, contraseñas, certificados y logs en Git.

**Decisión pendiente:** ¿la dueña y la empleada ingresan con el mismo usuario de Windows o con cuentas de Windows distintas? Si usan distintos usuarios de Windows, una carpeta `%LOCALAPPDATA%` por usuario crearía bases separadas; en ese caso evaluar `%PROGRAMDATA%` con permisos adecuados u otra ubicación compartida y protegida. Elegir la ruta definitiva antes de guardar datos reales.

La clave de cifrado debe tener recuperación segura fuera de la computadora. Una copia local en el mismo SSD no sustituye al respaldo remoto en Drive.

## 5. Crear la estructura por fases, no toda de golpe

| Fase | Qué se materializa |
|---|---|
| F00 | Repositorio, solución, proyecto WPF mínimo, proyectos/capas acordados, `.gitignore`, `README`, documentos y prueba básica. La versión objetivo de .NET es provisional hasta F00-T02. |
| F01 | Persistencia, migraciones, auditoría, usuarios y backup local. |
| F02–F03 | Productos, variantes, proveedores, recepciones y movimientos de stock. |
| F04–F05 | POS, ventas, pagos y caja. |
| F06–F07 | Cambios y cuentas corrientes (fiado solo con reglas confirmadas). |
| F08–F09 | Drive, restauración, reportes. |
| F10 | Instalador, actualizaciones y pruebas en la PC real. |
| F11 | Reservas, etiquetas y otras ampliaciones si se aprueban. |

## 6. Convenciones y requisitos para Codex

1. No inventar reglas comerciales o permisos; anotar dudas en `docs/DECISIONES.md`.
2. Antes de crear carpetas o proyectos nuevos, comprobar si ya existe el lugar correcto.
3. Implementar únicamente la tarea indicada en `PLAN.md`, no todo el árbol a la vez.
4. Crear una migración nueva en lugar de reescribir una que ya se aplicó.
5. Conservar ventas históricas, pagos y movimientos; corregir mediante reversos auditables.
6. Mantener el proyecto compilable y reportar resultados reales de compilación/pruebas.
7. No añadir NuGet o librerías de UI solo para resolver un problema que WPF/.NET ya cubre bien.
8. Probar rendimiento con datos razonables y no cargar todo el inventario al iniciar.

## 7. Próximo paso concreto

Empezar **F00-T01** según `PLAN.md`. Crear únicamente la solución y carpetas base que permitan compilar un prototipo mínimo. La estructura de funcionalidades anterior es una referencia para los pasos posteriores, no una orden de construir pantallas vacías.
