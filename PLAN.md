# PLAN.md — Sistema de gestión para local de ropa infantil

> **Versión del plan:** 1.0 · **Estado:** planificación técnica inicial · **Idioma del proyecto:** español.
>
> Documento de trabajo para el desarrollador y para Codex. Este archivo **no certifica que un requisito no validado haya sido aprobado por la dueña**. Actualizar las decisiones y los estados a medida que se resuelvan.

## 0. Objetivo y límites

Construir una aplicación de escritorio para gestionar un **único local de ropa infantil en Argentina**, utilizada en **una computadora Windows** por la dueña y su empleada. Las operaciones de mostrador deben funcionar **sin internet**. El software debe ser ágil en una PC potencialmente antigua (estimación no verificada: i3 antiguo, 4 GB RAM, SSD y Windows 10).

El negocio necesita trabajar con prendas y variantes (talle/color), ventas y pagos, inventario, recepción de mercadería, caja, cambios/devoluciones y posiblemente cuentas corrientes. El sistema es deliberadamente **flexible en decisiones comerciales**, pero estricto en integridad de datos, movimientos de dinero y trazabilidad.

**Fuera de alcance inicial:** varias sucursales, acceso remoto de la dueña, servidor propio, e-commerce, aplicación móvil, sincronización entre varias cajas y reservas/señas (reservadas para una versión posterior). No agregar ninguna de estas características sin una decisión explícita.

### 0.1. Decisiones confirmadas

- Plataforma: aplicación de escritorio **Windows**, **una sola PC**, operación local/offline.
- Usuarios: **dueña y empleada**; los permisos serán configurables. Ambas pueden autorizar devoluciones de dinero. La dueña decide el medio de reintegro excepcional; el medio habitual es efectivo.
- Medios de cobro que el local acepta: **efectivo, transferencia, tarjeta de débito, tarjeta de crédito y billeteras virtuales**. Contemplar pagos combinados.
- La mercadería llega **sin códigos de barras**. Existe **impresora común**; no dar por adquiridos lector ni impresora de etiquetas.
- Cambios: por talle/color y por otros productos; reconocer el **importe originalmente pagado**; si el artículo nuevo es más caro, cobrar la diferencia; si es más barato, el cliente puede agregar productos y se reintegra el remanente.
- Devoluciones con reintegro: permitidas. **Dueña y empleada** pueden autorizarlas; efectivo es el medio normal.
- Promoción específica de dos prendas: la **devolución voluntaria parcial** normalmente requiere devolver ambas; cambiar el talle de una puede conservar la promoción cuando se mantienen sus condiciones. No extrapolar esta regla a todas las promociones ni a reclamos amparados por derechos legales.
- Plazos de cambios: **no bloquear por vencimiento**; permitir excepciones comerciales con registro.
- Reservas y señas: **versión posterior**.
- La dueña **no necesita acceso remoto**.
- Respaldo remoto: **Google Drive**, con copias **cada dos horas y al cerrar caja**; funcionamiento offline implica cola de pendientes y reintentos al volver la conexión.
- Desarrollo incremental: primera versión funcional seguida por ampliaciones.
- Actualizaciones automáticas cuando exista internet, con instalación **controlada en un momento seguro**, no durante una venta.

### 0.2. Decisiones técnicas provisionales, NO irrevocables

- **C# + WPF + SQLite** como candidato inicial. Comprobar compatibilidad de la versión elegida de .NET con el Windows real antes de fijarla y antes de instalar en producción; no seleccionar una versión sin soporte para salir del paso.
- Una solución .NET modular **sin servidor HTTP, microservicios ni contenedores**. No añadir dependencias porque sí.
- SQLite como archivo local fuera de `Program Files` y fuera de la carpeta del instalador; transacciones explícitas, claves foráneas, migraciones versionadas y copias consistentes.
- Se priorizan controles WPF estándar, navegación por teclado y virtualización de listas. No incluir estilos/animaciones pesadas sin necesidad.

### 0.3. Decisiones pendientes: NO INVENTAR

| ID | Tema | Qué falta resolver | Tratamiento mientras tanto |
|---|---|---|---|
| D-01 | Facturación fiscal | Procedimiento real y definición con contador; integración vs. emisión externa; cómo vincular comprobantes | Registrar referencia fiscal opcional; **no presentar un ticket interno como factura**. Bloquea salida a producción sin procedimiento confirmado. |
| D-02 | Cuentas corrientes | Política específica aprobada para F07 (uso, permisos de fiado, límites, vencimiento informativo, abonos, ajustes y devoluciones) | Resuelta para el alcance F07 por D-41. No habilita producción ni resuelve obligaciones fiscales/operativas ajenas al alcance. |
| D-03 | Permisos | Matriz completa Dueña/Empleada: descuentos, ajustes, ventas fiadas, anulación, reportes y costos; sigue pendiente salvo permisos explícitamente confirmados por decisiones posteriores. `Administrador`/D-21 fue reemplazado por Dueña/D-27. | Infraestructura configurable; denegar por defecto lo no confirmado. |
| D-04 | Excepciones de reintegro | Cómo registra la dueña autorización de otro medio si no está presente | Mantener efectivo como flujo normal; no inventar autorización remota. |
| D-05 | Política de promociones | Otras promociones y condiciones para mantener/recalcular descuentos | Soportar vínculo entre líneas de venta y promoción; implementar únicamente reglas confirmadas. |
| D-06 | Cambios | Comprobantes/identificación alternativa, estado de producto, tratamiento de descuentos de varios artículos | Diseñar estructura flexible; no introducir bloqueos arbitrarios. |
| D-07 | Costos | Criterio de valoración de reposiciones y margen bruto | Guardar costos históricos cuando se acuerde el método; no mostrar ganancia neta ficticia. |
| D-08 | Hardware | Modelo de impresora, versión/edición/arquitectura real de Windows, rendimiento | Usar impresión estándar en prototipo; validar PC real antes del despliegue. |
| D-09 | Respaldo | Política técnica aprobada por D-42; quedan por validar Google OAuth/Drive real y PC/hardware de destino antes de producción | Implementación F08 no autoriza cargar datos reales ni producción. Mantener backups locales y estado pendiente ante cualquier falla remota. |
| D-10 | Actualizaciones | Proveedor/canal de publicación, firma, política de instalación y recuperación | Desarrollar una actualización de prueba antes de producción. |
| D-11 | Identificación | Convención de códigos internos, etiquetas futuras, talles reales, artículos sin variantes | Resuelto 2026-09-29 por la persona usuaria (ver D-35): categorías controladas obligatorias (12 del seed, "Otros" solo explícito), código interno automático e inmutable Producto {PREFIJO}-{NNN} / Variante {CODIGO_PRODUCTO}-{NN} (texto visible "Código interno"), unicidad Talle+Color normalizada, typeahead por código o nombre con resolución exacta sin sugerencia obligatoria, sin lector requerido. Etiquetas físicas y creación múltiple batch pendientes (esta última diferida a un caso transaccional aparte). |
| D-12 | Obligaciones comerciales | Verificación de políticas de cambios, promociones, medios de pago y reintegros aplicables en Argentina | Validar con dueña/contador y asesoramiento adecuado; no implementar reglas incompatibles con obligaciones legales. |
| D-13 | Usuarios de Windows y ruta de datos | Confirmado 2026-09-21 por el desarrollador: una sola PC y una única cuenta de Windows compartida; no hay escenario multi-cuenta. Falta concretar la carpeta definitiva de producción | La ruta provisional `%LOCALAPPDATA%` ya no presenta el riesgo de bases separadas; no fijar la ruta definitiva sin comprobarlo. Ver `ESTRUCTURA.md`. |

**Regla de decisiones:** actualizar esta tabla con fecha, respuesta, fuente (dueña/desarrollador/contador), impacto y criterio de aceptación; no sustituir «pendiente» por una suposición.

---

## 1. Instrucciones permanentes para Codex

1. **Leer todo `PLAN.md` y el código relevante antes de cambiar archivos.** Identificar fase activa, tarea concreta, dependencias y pendientes.
2. **Implementar una sola tarea identificada (ej. F02-T03) por iteración.** No avanzar de fase por cuenta propia ni generar toda la app en una solicitud.
3. Antes de programar, comunicar brevemente: alcance, archivos previstos, dependencias, migraciones y pruebas. Si una decisión pendiente bloquea la tarea, detener únicamente ese punto y continuar con trabajo independiente; **no inventar reglas de negocio**.
4. Mantener **interfaz / aplicación / dominio / infraestructura** separadas sin crear abstracciones innecesarias. Nada de SQL dentro de controles XAML ni lógica de negocio en code-behind. Se admite code-behind limitado a detalles visuales.
5. No eliminar ventas, cobros, movimientos de stock ni asientos de cuenta corriente para corregir errores: usar anulaciones, reversos o movimientos compensatorios auditables.
6. En cada operación que altere venta, pagos, caja, deuda o stock, usar **transacciones atómicas**, validación e identificadores de operación. Prevenir confirmaciones duplicadas.
7. Manejar dinero mediante representaciones decimales exactas (`decimal` en C# y un formato persistido exacto definido explícitamente, preferentemente unidades menores enteras si la política de precios lo permite). **No usar `double`/`float` para importes.** Definir y probar redondeos antes de aplicarlos.
8. Priorizar rendimiento: consultas paginadas, índices justificados, virtualización de grillas, tareas de I/O fuera del hilo UI. No cargar el catálogo entero al iniciar.
9. No exponer credenciales de Drive ni claves de cifrado en repositorio, logs, muestras o instalador. Guardar secretos mediante mecanismo seguro compatible con Windows.
10. Escribir pruebas del dominio y de integración para cada operación relevante; realizar pruebas manuales para impresión, instalador y restauración. **Compilar no equivale a probar.**
11. Cualquier migración de esquema requiere versión, ensayo sobre una copia, respaldo anterior, verificación y tratamiento explícito de fallos. No reescribir datos históricos silenciosamente.
12. Al cerrar cada tarea, informar: cambios, pruebas ejecutadas y resultados reales, limitaciones, decisiones pendientes y próximo paso propuesto. Solo marcar `[x]` si se verificaron todos los criterios.
13. No agregar acceso remoto, API web, módulos de marketing, fiado habilitado ni integraciones fiscales sin requerimiento validado.
14. Mantener el proyecto construible después de cada tarea. Proteger los datos de prueba y conservar respaldo antes de ensayos destructivos.

### Formato de seguimiento por tarea

```md
#### Fxx-Tyy — Nombre de tarea
- Estado: [ ] Pendiente / [>] En curso / [x] Terminado / [!] Bloqueado
- Objetivo:
- Dependencias:
- Archivos afectados:
- Criterios de aceptación:
- Pruebas realizadas y resultado:
- Pendientes / decisiones:
```

### Definición de terminado (DoD) de CADA tarea

- [ ] Compila en la configuración acordada, sin errores nuevos.
- [ ] Respeta alcance y decisiones; no incorpora suposiciones ocultas.
- [ ] Pruebas automáticas pertinentes ejecutadas y documentadas; pruebas manuales descritas cuando correspondan.
- [ ] Gestión de errores y mensajes entendibles para la empleada.
- [ ] No expone datos sensibles ni rompe la trazabilidad.
- [ ] Si modifica persistencia, incluye migración y prueba con datos existentes.
- [ ] Se actualizan este plan y el registro de decisiones cuando corresponda.

---

## 2. Arquitectura objetivo mínima

```text
LocalInfantil.Wpf             -> Vistas, ViewModels, comandos, navegación y validación visual
LocalInfantil.Application     -> Casos de uso: vender, cobrar, ajustar stock, cambiar, etc.
LocalInfantil.Domain          -> Entidades, reglas puras y tipos de valor
LocalInfantil.Infrastructure  -> SQLite, repositorios, impresión, backups, Drive y actualización
LocalInfantil.Tests           -> Pruebas del dominio y de integración (separar si crecen)
```

El árbol concreto de carpetas, dependencias y responsabilidades está en **`ESTRUCTURA.md`**. `LocalInfantil` es un nombre provisional del proyecto; decidir el nombre definitivo al iniciar F00. La estructura final se materializa por fases: **no crear carpetas vacías de módulos futuros**. La dirección de dependencias debe impedir que el dominio dependa de WPF o de SQLite.

- **Datos:** SQLite local; una única fuente de verdad. La interfaz nunca debe abrir conexiones SQL directamente.
- **Interfaz:** WPF/MVVM pragmático. Validaciones en UI para ayudar al usuario; reglas definitivas en aplicación/dominio.
- **Servicios externos:** Drive y actualizador no pueden impedir ventas offline. Registro de fallas y reintentos.
- **Impresión:** adaptador desacoplado; el negocio puede registrar una venta aunque falle la impresión y debe poder reimprimir sin duplicar la venta.
- **Auditoría:** usuario, fecha, tipo de operación, identificador correlacionado y detalle mínimo necesario; nunca contraseñas o tokens.
- **Fechas y dinero:** acordar convenciones de fechas, zona horaria y formato de moneda; guardar marcas temporales de forma consistente y presentar horario del local.
- **Diagnóstico:** logs locales con rotación, información técnica acotada, sin secretos ni datos personales innecesarios.

### Invariantes obligatorias

- Una venta confirmada no existe a medias: líneas, pagos iniciales, deuda si corresponde y movimientos de stock quedan consistentes.
- Una venta fiada no registra como cobrado el saldo pendiente; un abono posterior no cuenta como segunda venta.
- El stock se explica mediante movimientos; no se sobrescribe un número sin registrar causa.
- El reembolso genera movimiento financiero y, cuando aplique, afecta la caja; la devolución de prenda solo aumenta stock vendible si fue clasificada apta.
- Se conservan los importes originales y las condiciones promocionales de cada venta; cambios posteriores son nuevas operaciones auditables.
- La flexibilidad comercial permite ajustes explícitos con motivo/permisos, nunca ediciones silenciosas de históricos.
- Copias no verificadas o pendientes de subida **no** se muestran como respaldos remotos exitosos.

---

## 3. Roadmap de implementación

### F00 — Viabilidad técnica y arranque (primera fase; comenzar aquí)

**Objetivo:** confirmar que el stack es viable y que el ciclo compilar → instalar → abrir → guardar → imprimir funciona en una PC Windows de prueba sin construir todavía la aplicación completa.

- [x] **F00-T01.** Crear repositorio, `.gitignore`, `README.md`, `PLAN.md`, `ESTRUCTURA.md` y estructura mínima de solución conforme a `ESTRUCTURA.md`; fijar convenciones de código y comandos de build/test. Usar framework provisional solo si es necesario para compilar, anotarlo y validarlo en F00-T02.
- [!] **F00-T02.** Elegir versión de .NET/WPF tras verificar compatibilidad y soporte con el Windows concreto; anotar la decisión en `docs/DECISIONES.md`. La solución fue migrada y verificada técnicamente en el equipo de desarrollo con .NET 10, pero esa prueba no confirma compatibilidad de producción. **Pendiente — validación en hardware definitivo antes de producción:** falta validar edición, versión y arquitectura de la PC del local. No bloquea el desarrollo de módulos comerciales (decisión 2026-09-29, ver D-36); bloquea instalación definitiva, puesta en producción y entrega al local.
- [x] **F00-T03.** Crear ventana WPF funcional con navegación básica y datos ficticios; abrir sin internet y comprobar que no hay operaciones bloqueadas por servicios externos.
- [x] **F00-T04.** Integrar SQLite en una ruta de datos de usuario y ejecutar una lectura/escritura simple mediante infraestructura, sin SQL en la vista. Validaciones automáticas y manuales correctas, incluida la reapertura y la operación sin conexión. D-13 sigue pendiente para cualquier uso con datos reales.
- [x] **F00-T05.** Probar impresión básica en impresora común o, si no está disponible, generar vista previa y registrar la prueba real como pendiente. Vista previa, diálogo de Windows, cancelación sin trabajo e impresión de prueba con Microsoft Print to PDF validados manualmente. La prueba con el equipo físico del local sigue pendiente fuera de este cierre técnico.
- [>] **F00-T06.** Publicar instalador/prototipo de prueba, abrirlo y medir cualitativamente arranque y fluidez; probar en la PC real cuando esté disponible. La publicación self-contained .NET 10 `win-x64` se generó en el equipo de desarrollo y fue validada manualmente el 2026-09-21 desde su carpeta, fuera de Visual Studio (apertura, navegación, SQLite, funcionamiento sin internet, vista previa, impresión de prueba y fluidez correctas, sin problemas observados, sin mediciones registradas). El instalador de prueba (Inno Setup 6, `installer/`) se genera en este equipo y fue validado manualmente el 2026-09-21 en una cuenta local estándar independiente (instalación, acceso directo, SQLite, impresión, funcionamiento sin internet, desinstalación y conservación de ambas bases con el mismo hash SHA-256 correctos, sin errores ni observaciones); falta la validación en la PC del local. Igual que F00-T02: pendiente de hardware definitivo; no bloquea desarrollo, bloquea producción/entrega (decisión 2026-09-29, ver D-36).

**Aceptación:** arranca offline, crea/lee datos locales, interfaz no se congela en operaciones simples, puede instalarse y desinstalarse sin borrar datos de usuario; limitaciones de Windows/impresora documentadas. **DESARROLLO:** permitido continuar con módulos comerciales (incluidas F04 y fases posteriores) usando el entorno actual y DB ficticias. **PRODUCCIÓN/ENTREGA:** bloqueadas hasta completar la validación en la PC definitiva (F00-T02 y F00-T06 pendientes: Windows/arquitectura, publicación, arranque, persistencia, permisos de archivos, DPI/resolución, rendimiento, impresora cuando corresponda, funcionamiento interactivo general).

### F01 — Fundamentos: persistencia, operaciones y seguridad básica

**Objetivo:** establecer datos seguros y un esqueleto reutilizable antes de introducir dinero real.

- [x] **F01-T01.** Especificar esquema inicial y migraciones versionadas: usuarios, productos, variantes, precios, movimientos y estructuras mínimas de ventas/pagos/caja; documentar claves, restricciones y referencias. Terminado el 2026-09-21 en `feat/f01-initial-schema`: esquema V1 + `MigradorBaseDatos` (respaldo previo verificado, casos admitidos explícitos, historial sin saltos ni desconocidas, fallo atómico por versión) + 16/16 pruebas en bases temporales; ver `docs/ESQUEMA.md`. F00-T02 sigue bloqueada y F00-T06 sigue en curso.
- [x] **F01-T02.** Configurar SQLite: claves foráneas, modo de journaling elegido y probado, manejo de errores de bloqueo, transacciones y repositorios. Terminado el 2026-09-21 en `feat/f01-sqlite-foundations`: fábrica con FK + `busy_timeout` + `CommandTimeout` en cada comando (sesiones, repos, migrador), WAL elegido y probado, sesiones escritura/lectura, `AlmacenamientoOcupadoException` solo para 5/6 sin reintentos, 4 repositorios mínimos; 32/32 pruebas en bases temporales; ver `docs/ESQUEMA.md` §7 y D-18. F00-T02 sigue bloqueada y F00-T06 sigue en curso.
- [x] **F01-T03.** Crear identificadores estables de operaciones, reloj inyectable para pruebas y registro de auditoría mínimo. Terminado el 2026-09-21 en `feat/f01-operation-audit`: `IGeneradorIds`/`IReloj` (+falsos), migración V2 `auditoria` (V1 intacta), `RepositorioAuditoria` en la misma tx; 38/38 pruebas en bases temporales; ver `docs/ESQUEMA.md` §8 y D-19. F00-T02 sigue bloqueada y F00-T06 sigue en curso.
- [x] **F01-T04.** Implementar autenticación local con contraseñas protegidas mediante algoritmo adecuado, roles y permisos por operación; permisos no confirmados permanecen configurables/no concedidos automáticamente. Terminado el 2026-09-21 en `feat/f01-local-auth`: PBKDF2 + V3 + login/logout + matriz configurable + `Autorizador` + regla Administrador (D-21); 66/66 pruebas en bases temporales; ver `docs/ESQUEMA.md` §9 y D-20/D-21. F00-T02 sigue bloqueada y F00-T06 sigue en curso.
- [x] **F01-T05.** Crear respaldo LOCAL consistente, validación y restauración de ensayo sobre copia de prueba; nunca copiar en caliente el archivo SQLite sin procedimiento consistente. Terminado el 2026-09-21 en `feat/f01-local-backup`: `IServicioRespaldos`/`ServicioRespaldos` (VACUUM INTO, publicación atómica, verificación integral, ensayo aislado) reutilizado por el migrador; 81/81 pruebas; ver `docs/ESQUEMA.md` §10 y D-22. F00-T02 sigue bloqueada y F00-T06 sigue en curso.
- [x] **F01-T06.** Crear pruebas de migración, integridad referencial y fallo/reversión de transacciones. Terminado el 2026-09-21 en `feat/f01-integrity-tests`: matriz §11 en `docs/ESQUEMA.md` con 85/85 pruebas (4 nuevas: RESTRICT, unicidad de nombre, parciales, unicidad a mitad de operación); sin defectos en código F01; ver §11. F00-T02 sigue bloqueada y F00-T06 sigue en curso. No validado en la PC del local ni listo para producción.

**Aceptación:** restauración de prueba exitosa; operaciones fallidas no dejan datos parciales; usuarios separados; el almacenamiento persiste después de reinstalar/actualizar el prototipo.

### F02 — Catálogo y variantes

**Objetivo:** registrar mercadería sin depender de códigos de barras externos.

- [x] **F02-T01.** Alta/edición/consulta/desactivación de producto padre, marca, categoría y atributos necesarios; no eliminar productos con historial. Terminado el 2026-09-21 en `feat/f02-products-variants` con F02-T02: casos de uso + repos + permisos definidos sin conceder; 95/95 pruebas; ver `docs/ESQUEMA.md` §12 y D-23. Permisos denegados por defecto hasta D-03; catálogo no operativo para el local. F00-T02 sigue bloqueada y F00-T06 sigue en curso.
- [x] **F02-T02.** Variantes flexibles por talle/color, incluidos artículos sin alguno de esos atributos; SKU interno único y buscable. Terminado en `feat/f02-products-variants` con F02-T01 (mismo alcance y evidencia).
- [>] **F02-T03.** Precio/costo y estado vigente; conservar instantáneas para ventas futuras. Si el método de costo aún no está confirmado, no publicar márgenes definitivos. Precios V4 + interfaz WPF FijarPrecio terminada (formato ARS exacto a centavos, historial conservado, sin costos/FIFO); costos/márgenes pendientes D-07.
- [x] **F02-T04.** Búsqueda textual rápida e índices medidos; ficha de producto con variantes, existencias y estado. Terminada el 2026-09-21: búsqueda paginada + ficha medidas (8 ms / 5 ms), sin FTS.
- [x] **F02-T05.** Grilla de carga inicial de variantes, con separación inequívoca entre **establecer cantidad inicial** y **sumar ingreso**. Implementada y probada en `feat/wpf-catalog-prices-initial-stock`: grilla WPF multi-fila ESTABLECER con operacion_id estable, validaciones y rollback total; ver `docs/ESQUEMA.md` §13/§18 y D-24. Validación visual interactiva pendiente (modo --dev-db). SUMAR es F03. Criterios de aceptación cumplidos en pruebas automatizadas; pantalla no probada interactivamente.

**Aceptación:** un producto con distintos talles/colores no mezcla existencias; búsqueda por texto/SKU responde sin cargar todas las filas; un producto usado puede desactivarse conservando historial.

### F03 — Proveedores, ingresos y stock trazable

**Objetivo:** poder explicar cada unidad que entra o sale.

- [x] **F03-T01.** Registro mínimo de proveedores, solo campos comerciales acordados. Terminado el 2026-09-21 en `feat/f03-suppliers-receiving`: `Crear/Editar/DesactivarProveedor` + consultas + permiso definido sin conceder; ver `docs/ESQUEMA.md` §15 y D-26. Sin declarar F03 terminada ni el sistema listo para producción.
- [x] **F03-T02.** Recepción con productos, variantes, cantidades y costos; confirmación atómica que genera movimientos de ingreso. Terminado el 2026-09-21 en `feat/f03-suppliers-receiving`: `ConfirmarRecepcion` + costo histórico informado (D-07 parcial, sin valuación) + V5; ver §15 y D-26.
- [x] **F03-T03.** Ajustes de inventario con motivo, responsable y movimientos inmutables/compensables. Terminado el 2026-09-21 en `feat/f03-adjustments-stock-history`: `AjustarInventario` auditado y compensatorio; ver `docs/ESQUEMA.md` §14 y D-25. Sin declarar F03 terminada ni el sistema listo para producción.
- [x] **F03-T04.** Consulta de existencias e historial por SKU; distinguir stock dañado/no vendible cuando corresponda. Implementado en `feat/f03-damaged-setaside-stock`: libro `apartado_movimiento` + migración V8, apartar/reincorporar/baja definitiva, consultas Físico/Disponible/Apartado e historial, adaptación de ajustes/reversión/conteos al invariante, pantalla WPF “No vendible”; ver `docs/ESQUEMA.md` §22 y D-33. Validación visual pendiente (sin escritorio). F03 no cerrada.
- [x] **F03-T05.** Inventario físico simple: registrar conteo, mostrar diferencias y aprobar ajuste; si el alcance se reduce, dejar el conteo formal para ampliación sin permitir ajustes invisibles. Implementado en `feat/wpf-stocktake-receiving-recovery`: recepción durable con `AlmacenRecepcionPendiente` (`.pending-recepcion.json` atómico, reconciliación tras reinicio, sin duplicar), parser ARS exacto `1.234,50` sin redondeo silencioso, y pantalla WPF `ConteoViewModel`/`ConteoView` con selección parcial, borradores recuperables, corrección, informe esperado/contado/diferencia, confirmación explícita, aprobación sin ajustes directos, foto vieja y doble aprobación bloqueadas; ver `docs/ESQUEMA.md` §17/§20 y D-28/D-32. Validación visual WPF pendiente (sin escritorio). F03-T04 sigue [>].

**Aceptación:** ingreso + ajuste explican exactamente las existencias; nunca hay modificación de stock sin movimiento; revertir recepción errónea no borra historial.

### F04 — Punto de venta y pagos (primer flujo comercial integral)

**Objetivo:** completar una venta real de punta a punta, inicialmente con datos de prueba.

- [x] **F04-T01.** Pantalla POS optimizada para teclado (`PosView`/`PosViewModel` en rama `feat/f04-pos`): buscador asistido reutilizado (código/nombre, Enter agrega directo, foco vuelve solo), carrito dominante sin filas duplicadas, cantidades +/−, totales, validación de Disponible al agregar; sin venta en espera (diferida explícitamente, no persiste borradores).
- [x] **F04-T02.** Medios manuales efectivo/transferencia/débito/crédito/billetera como pagos separados; suma imputada == total, vuelto derivable (`recibido − importe`, solo efectivo, CHECK en V10); sin Mercado Pago ni integraciones.
- [x] **F04-T03.** `ConfirmarVenta` atómica (sesión `BEGIN IMMEDIATE`): revalida Disponible y precio vigente dentro de la tx, aborta con `PrecioCambiado` sin escribir si cambió, idempotencia por `venta.operacion_id` UNIQUE + botón deshabilitado en curso.
- [x] **F04-T04.** Ticket **interno no fiscal** (`DocumentoTicketVenta` desde snapshots + renderer + `TicketVentaWindow` con vista previa/impresión, leyenda "COMPROBANTE INTERNO — NO FISCAL"); número estable `T-000001`; reimpresión inmutable. Procedimiento fiscal real pendiente de D-01; renderer/vista previa implementados, prueba de impresora física pendiente para producción.
- [x] **F04-T05.** Solo anulación TOTAL auditada (`AnularVenta` + `reversion_venta` + ingresos compensatorios + motivo obligatorio + anti-doble); sin parciales (diferidas), sin borrar. Autorización definida: `ventas.anular` solo Dueña.
- [x] **F04-T06.** Tests cubriendo último artículo, pago combinado, vuelto, pago insuficiente/mayor, recibido inválido, error al persistir (rollback total, secuencia no avanza), venta duplicada por `operacion_id`, precio cambiado, histórico inmutable, restitución y doble anulación, permisos por rol, migración V9→V10, VMs POS/consulta/ticket y parseo de miles.

**Aceptación:** venta completada modifica stock una sola vez, cobros coinciden con pagos, ticket reimpreso no altera registros y una falla intermedia revierte toda la confirmación. **Prohibido uso productivo si D-01 y estrategia de respaldos remotos siguen abiertos.** No se declara D-01 resuelta, ni PC real validada, ni impresora física validada.

### F05 — Caja y conciliación básica

**Objetivo:** registrar dinero real sin confundirlo con ventas.

- [x] **F05-T01.** Apertura, fondo inicial y sesión de caja vinculada al usuario; una sola sesión abierta, apertura auditada y sin movimiento ficticio de apertura (V11).
- [x] **F05-T02.** Cobros de ventas dentro de la transacción de venta; ingresos/egresos manuales clasificados y justificados; retiro de Dueña separado de gasto y restringido por permiso.
- [x] **F05-T03.** Arqueo ciego: primero se ingresa el efectivo contado, luego se revela esperado/diferencia; cierre inmutable con responsables y observación obligatoria si hay diferencia. El valor se recalcula bajo transacción para evitar cierre sobre saldo desactualizado.
- [x] **F05-T04.** Desglose de efectivo, transferencia, débito, crédito y billetera para conciliación; solo efectivo integra el efectivo esperado/contado.
- [x] **F05-T05.** Anulación total compensatoria en la sesión abierta actual y corrección de medio mediante movimientos compensatorios, preservando venta, pago original y sesiones cerradas.
- [x] **F05-T06.** Tests automatizados para apertura, pagos simples/mixtos, movimientos manuales, permisos, fallos atómicos, anulaciones, correcciones, arqueo/diferencias, cierre, concurrencia y migración V10→V11.

**Aceptación:** caja esperada se reconcilia con movimientos; el cierre no modifica ni reescribe ventas; existe rastro de quién abrió/cerró. Verificado mediante suite automatizada y migración temporal V10→V11 con preservación de venta/pago e integridad SQLite. **Pendiente de revisión visual manual de las capturas 90–100**; no se presentan como capturadas.

**Registro F05 (2026-10-03):** migración aditiva V11, casos de uso, repositorio, permisos, navegación e interfaz implementados en `feat/f05-caja`. `dotnet build` Debug: 0 advertencias/0 errores; suite completa: 356/356; tests F05: 25/25; pruebas de migración/respaldos: 26/26; `git diff --check`: sin errores. Capturas visuales reales 90–100 quedan pendientes de una sesión interactiva autenticada; no se alteró ninguna base histórica ni se generaron capturas sintéticas.

**Estado F05:** implementación técnica completa y validación automatizada completa; validación visual/manual WPF (capturas 90–100) pendiente; validación en la PC real pendiente; **producción NO habilitada**.

### F06 — Cambios, devoluciones y reglas comerciales flexibles

**Objetivo:** una operación genérica para devolver N artículos y entregar M artículos, en lugar de programar cada combinación por separado.

- [x] **F06-T01.** Recuperar venta original y unidades todavía disponibles para devolución; evitar devolver la misma unidad dos veces. Validación Application dentro de `BEGIN IMMEDIATE` y trigger acumulado en SQLite.
- [x] **F06-T02.** Registrar múltiples artículos de entrada y salida, reconocer importes originalmente pagados y calcular diferencias mediante una única operación genérica N/M.
- [x] **F06-T03.** Diferencia positiva → cobrar; negativa → reintegrar el remanente. Reintegro efectivo habitual; medio no efectivo con autorización puntual presencial de Dueña.
- [x] **F06-T04.** Clasificar prendas devueltas aptas/no aptas; aptas vuelven a Disponible; no aptas aumentan Físico y Apartado con motivo obligatorio.
- [!] **F06-T05.** Promoción específica de dos artículos: **BLOQUEADA por D-05**; F04 no registra ni permite reconstruir una promoción real. No inferir ni implementar reglas promocionales.
- [x] **F06-T06.** Mostrar fecha/antigüedad informativa sin inventar un plazo aprobado ni bloquear por vencimiento; no se muestran estados «vencido/fuera de término».
- [x] **F06-T07.** Liquidaciones locales en caja y auditoría; cambios C-* inmutables. Reversos fiscales externos no implementados: siguen sujetos a D-01 antes de producción.

**Aceptación técnica del núcleo local:** stock, importes, caja e historial se confirman en una transacción; precio original se conserva; fallos/no idempotencia no producen parciales; ticket C-* interno/no fiscal reimprimible sin mutación; anulación F04 se rechaza tras cualquier cambio. F06-T05 sigue bloqueada por D-05. Capturas WPF 101–110 y validación en PC real siguen pendientes; D-01 impide habilitar producción por el aspecto fiscal externo.

**Registro F06 (2026-10-03):** V12 aditiva; casos de uso, repositorios, autorización efectiva dentro de transacción, caja/stock/auditoría, búsqueda, histórico/ticket y UI implementados y publicados en `7b0dd5e231ce4f8038a165fc82f50684f8f54ed2` desde `feat/f06-cambios-devoluciones`. `dotnet build` Debug: 0 advertencias/0 errores; suite focalizada de Cambios: 15/15; suite completa: 369/369. Capturas WPF 101–110 pendientes porque la automatización no expone ventanas (`apps: []`); ver D-39.

### F07 — Clientes y cuentas corrientes

**Objetivo:** clientes y cuenta corriente local bajo la política específica D-41; la autorización sigue sujeta a la matriz efectiva y al deny-by-default.

- [x] **F07-T01.** Cliente opcional para venta común; ficha mínima y consulta de historial. V13 aditiva; alta/edición/desactivación, búsqueda paginada por nombre/teléfono, permisos, asociación opcional POS con snapshot histórico y consulta de T-*/C-* vinculados. Ver D-40. Pruebas técnicas aprobadas; capturas/revisión visual WPF 111–118 pendientes (`apps: []`), no bloquean el cierre técnico.
- [x] **F07-T02.** Política específica de cuenta corriente aprobada y registrada en D-41; el vencimiento es informativo, el límite es opcional por cliente y las decisiones de abono/devolución quedan explícitas.
- [x] **F07-T03.** Modelo V14 aditivo con cargos, abonos, ajustes auditados, aplicaciones FIFO y saldo derivado de movimientos inmutables; sin migrar ventas históricas a deuda.
- [x] **F07-T04.** Venta fiada total/parcial con cliente activo y permiso efectivo; solo pagos recibidos afectan caja, el saldo impago se registra como cargo y se valida el límite dentro de la operación.
- [x] **F07-T05.** Abonos combinables por medio, aplicación exacta FIFO, recibo histórico y consulta; no se admite exceder la deuda ni se crea otra venta.
- [x] **F07-T06.** Pruebas automatizadas para venta parcial/fiada, límite y permisos, abonos, ajustes, devolución, anulación, migración e invariantes de caja/deuda. Ver D-41 y `docs/PRUEBAS_MANUALES.md`.

**Aceptación técnica:** saldo explicable desde movimientos inmutables, caja no incluye saldos impagos, permisos efectivos validados en Application, operaciones atómicas e históricas. Build y suite completos registrados en el avance F07. La validación visual WPF sigue pendiente; F07 no habilita producción.

**Límite vigente:** F07-T01–T06 están implementadas y verificadas técnicamente. Capturas/revisión visual WPF 111–129 pendientes porque la automatización no expone ventanas (`apps: []`). F00-T02 y la PC real siguen pendientes; producción NO habilitada. D-03 conserva pendientes generales fuera de las autorizaciones específicas aprobadas en D-41.

### F08 — Respaldos remotos y recuperación (obligatorio antes de producción)

**Objetivo:** automatizar la protección de datos del negocio.

- [x] **F08-T01.** Política aprobada y registrada en D-42: retención, secreto/código de recuperación, ruta, cifrado, operación offline, POS/caja no bloqueados y restauración solo con caja cerrada.
- [x] **F08-T02.** Coordinador con worker único; snapshots consistentes mediante `IServicioRespaldos`/`ServicioRespaldos`, programados por ventanas fijas de 2 h, catch-up al arranque y solicitud durable posterior al cierre confirmado. La salida de app espera hasta 5 s y temporales se reconcilian al iniciar.
- [x] **F08-T03.** Paquete versionado `.libackup`, GZip streaming, libsodium SecretStream autenticado, metadata protegida, publicación/sidecar atómicos, verificación y retención local de 7 días con protección de pendientes/última copia.
- [>] **F08-T04.** OAuth Desktop/PKCE, loopback, scope `drive.file`, carpeta/IDs, tokens DPAPI, subida idempotente y confirmación implementados. Pendientes Cloud Client ID/proyecto, OAuth real, acceso Drive real y reautorización manual; no probados por falta de credenciales.
- [x] **F08-T05.** Backoff acotado y retry con ID reservado estable, estados locales/remotos, retención remota idempotente y UI de atención sin condicionar ventas/caja; tests fake cubren timeout, retry, duplicado y fallo de listado. Validación visual WPF y remota real pendientes.
- [>] **F08-T06.** Restauración local y desde Drive implementada: permiso/reautenticación Dueña, código, caja cerrada, staging validado, backup previo, confirmación y helper de reemplazo/rollback. Pruebas automatizadas verifican rechazo con caja abierta, preservación de la base activa ante staging/clave/formato inválidos, reemplazo correcto y rollback ante fallas antes/durante el intercambio. Pendientes recorrido manual completo, PC nueva y Drive real; no se declara verificada punta a punta.
- [x] **F08-T07.** Pruebas automáticas de paquete/chunks/tamper/key/truncamiento, checksum, protección secreta abstraída, sidecar/reinicio, retry/ID, retención local/remota, lock entre instancias, scheduler/catch-up/no solapamiento/encolado y cierre durable/idempotente. Incluye snapshot durante escritura WAL y E2E aislado de venta/pago/caja/cliente/cuenta/C-* con restauración a otra base y verificación de integridad/relaciones.

**Evidencia técnica (2026-10-05):** rama `feat/f08-backup-recovery`, HEAD/base `dc8d55c9753f3592a10221581a4ad91aafea3239`; restore Release `win-x64` correcto; build Debug 0 advertencias/0 errores; tests focalizados de respaldos 38/38; suite completa detallada 409/409 (4,3977 min); publicación Release self-contained `win-x64` correcta en `artifacts/publish/net10-f08-win-x64` (409 archivos, 151.021.404 bytes; sin DB, backups, secretos, temporales ni símbolos PDB). `libsodium.dll` se cargó desde la publicación. El ejecutable publicado se inició directamente, obtuvo handle de ventana y cerró normalmente; no se inspeccionó visualmente ni se probaron flujos WPF manuales. Los tests usan bases temporales. No se alteraron bases del usuario.

**Aceptación global aún pendiente:** no hay credenciales/proyecto OAuth, por lo que Drive real no se validó; tampoco se comprobó manualmente la restauración completa en otra PC. Validación visual WPF/manual, PC real, recuperación en hardware destino y configuración OAuth/Drive continúan pendientes. F08-T04 y F08-T06 siguen parciales por esas dependencias externas/manuales. No hay aprobación de producción.

### F09 — Reportes básicos y operación cotidiana

**Objetivo:** información útil que no contradiga ventas ni caja.

- [x] **F09-T01.** Ventas diarias/mensuales y filtro de fechas; separar ventas brutas/netas y cobros. Semántica por fecha del evento (D-43): brutas = líneas de confirmadas creadas en el período; ajustes = anulaciones + entradas − salidas F06 ocurridos en el período; netas = brutas + ajustes. Hoy/Este mes/Personalizado, resumen por día con drill a detalle, detalle con filas venta/anulación/cambio. Ver D-43.
- [x] **F09-T02.** Desglose por medio de pago (ventas/abonos/cambios/reintegros/neto por medio vigente con corrección aplicada), efectivo teórico reutilizando el libro F05 (sin caja paralela) e historial de caja con filtro de fechas (nuevo `ListarSesionesPorFechaAsync`; detalle con consulta F05 existente).
- [x] **F09-T03.** Stock actual en vivo (Disponible/Apartado/Físico, filtros texto/categoría/cero/hasta N), unidades más vendidas netas por evento (columnas brutas/devueltas/salidas/netas, variante o producto), sin-ventas con N editable (nunca vendidos vs sin ventas recientes + última venta/recepción/movimiento). Sin temporada (no existe en el esquema) ni stock mínimo comercial.
- [>] **F09-T04.** BLOQUEADA por D-07: sin margen, costo de venta, ganancia ni rentabilidad en UI ni consultas. Permiso `reportes.costos` reservado sin pantalla.
- [x] **F09-T05.** Fixture temporal representativa (300 productos, 1200 variantes, 5000 ventas, 15000 líneas, pagos, ~200 operaciones y 300 movimientos de caja, 300 cambios, 50 clientes/abonos): todo ≤ 33 ms salvo `SinVentasAsync` (22 s → 33 ms) que justificó V15 (`ix_venta_linea_variante`, solo índices). Evidencia en `docs/RENDIMIENTO_REPORTES.md`.

**Aceptación:** reportes cuadran con ventas/pagos de pruebas, devoluciones no se cuentan dos veces, consultas frecuentes son ágiles. F09 no se declara completamente cerrada (T04 bloqueada por D-07).

### F10 — Instalación, actualización y entrega

**Objetivo:** que el familiar pueda usarlo sin depender de un entorno de desarrollo.

- [ ] **F10-T01.** Empaquetado e instalador Windows; datos almacenados fuera del directorio de instalación; acceso directo y desinstalación segura.
- [ ] **F10-T02.** Canal de publicación y actualización automática segura; descargar con internet e instalar cuando no haya venta/cambio/cierre abierto.
- [ ] **F10-T03.** Backup previo a migraciones; pruebas de actualización, interrupción de energía simulada y procedimiento de recuperación.
- [ ] **F10-T04.** Probar con la PC del local: inicio, búsqueda, venta, impresión, cierre y backup; registrar RAM, tiempos y bloqueos reales.
- [ ] **F10-T05.** Validar facturación/procedimiento con dueña y contador, permisos, precios, datos iniciales y condiciones comerciales antes de uso real.
- [ ] **F10-T06.** Documentar guía de uso breve, recuperación, contacto de soporte y procedimiento manual si la PC falla.

**Aceptación:** instalación limpia, upgrade sin pérdida de datos, respaldo/restauración comprobados, procedimiento fiscal acordado y una jornada de prueba sin inconsistencias graves. Solo entonces habilitar producción.

### F11 — Identificación física por variante

- [x] **F11-A.** Crear variantes en lote desde un producto existente mediante matriz talle/color; cubrir combinaciones parciales y ausencia de atributos, normalización central, duplicados, vista previa de códigos, secuencia compartida, auditoría y transacción atómica. El lote no fija precio ni modifica stock.
- [x] **F11-B.** Generar Code128 a partir exclusivamente del código interno de variante existente. Dependencia técnica en Infrastructure; sin columna/código alternativo ni serial individual.
- [x] **F11-C.** Componer etiquetas vectoriales con producto, talle/color cuando existan, barras, código legible y precio vigente opcional. Medidas editables de desarrollo en milímetros; la matriz de impresión no se persiste.
- [x] **F11-D.** Vista previa `FixedDocument`, distribución multi-etiqueta y paginación A4; diálogo genérico de impresión Windows solo tras acción explícita. Cancelar/no poder imprimir no altera stock ni operación confirmada. Accesos opcionales tras recepción/carga inicial y reimpresión desde ficha.
- [x] **F11-E.** Resolver Enter del POS primero por código exacto, sin esperar debounce; agregar/incrementar la misma fila con precio vigente y Disponible, devolver foco y mostrar mensajes operativos para inexistente/sin precio/sin stock. Lector HID/teclado no es obligatorio.
- [>] **F11-F.** Validación física pendiente: lector e impresora del local, calibración, tamaño y legibilidad. Hardware/Windows real aún no confirmado; no se simula ni se considera requisito para estas pruebas técnicas.
- [ ] Funcionalidades posteriores no autorizadas por esta entrega: reservas/señas, promociones adicionales, fidelización y reportes nuevos.

**Estado:** F11-A–E implementadas y verificadas técnicamente en rama aislada `feat/f11-etiquetas-codigos-batch`; F11-F permanece pendiente. F11 no se declara cerrada ni habilita producción. No se agrega migración a V1–V15 ni se modifica F10. Ver D-44 y `docs/PRUEBAS_MANUALES.md`.

### F12 — Integración Mercado Pago (roadmap; futura, no iniciada)

- [ ] Identificar el modelo exacto de Point/terminal disponible y verificar la API oficial vigente compatible antes de decidir la integración.
- [ ] Desde POS, iniciar pagos y representar estados pendiente, aprobado, rechazado y cancelado; diseñar idempotencia, timeout y recuperación ante resultado incierto.
- [ ] Conservar la referencia externa de Mercado Pago e integrar la conciliación con Caja, anulaciones y reintegros sin duplicar movimientos.
- [ ] Definir el comportamiento degradado cuando no haya Internet y probarlo con credenciales y dispositivo reales en entorno controlado.

**Estado:** futura y no iniciada. Mercado Pago no está implementado: esta entrada no agrega paquetes, endpoints ni secretos, no modifica F04/F05 y no es requisito para cerrar F11.

---

## 4. Orden de implementación y entregas

| Entrega | Fases | Resultado comprobable | ¿Usar con datos reales? |
|---|---|---|---|
| E0 | F00 | Prototipo abre offline, guarda y prueba impresión/instalador | No |
| E1 | F01–F03 | Productos, stock e ingresos consistentes; backup local de prueba | No |
| E2 | F04–F05 | Venta, pagos y caja completos con pruebas | No, hasta resolver fiscalidad y protección remota |
| E3 | F06–F07 | Cambios y clientes/cuentas corrientes si se aprueban reglas | No, hasta validación final |
| E4 | F08–F10 | Drive, restauración, reportes, actualizaciones, validación PC real | Sí, solo después del checklist de salida |
| E5 | F11 | Ampliaciones validadas por uso real | Según cada entrega |
| E6 | F12 | Integración Mercado Pago (roadmap; no iniciada) | No, hasta validar API, dispositivo y procedimiento operativo |

**No es una estimación de fechas ni horas.** Si alguna tarea resulta demasiado grande, descomponerla conservando sus criterios de aceptación.

---

## 5. Escenarios de prueba obligatorios de punta a punta

- [ ] Dos intentos de vender la última unidad: no se genera stock incorrecto.
- [ ] Doble clic en «Confirmar»: no se duplica la venta ni el cobro.
- [ ] Cobro combinado y vuelto: sumas exactas, sin redondeos ocultos.
- [ ] Error de disco/SQLite durante confirmación: ninguna operación parcial persistida.
- [ ] Ticket no imprime: venta confirmada sigue intacta y se puede reimprimir.
- [ ] Anulación: registra reverso y no elimina evidencia.
- [ ] Cambio de talle de promoción válida: conserva la promoción según regla acordada.
- [ ] Devolución voluntaria parcial de promoción por dos: advertencia y flujo de excepción auditada; distinguir reclamo legal.
- [ ] Cambio por artículos más baratos: agrega productos y reintegra diferencia en efectivo; caja refleja egreso.
- [ ] Prenda devuelta defectuosa: no vuelve a stock vendible.
- [ ] Devolución repetida de una unidad: se detecta y no duplica ingreso ni reintegro.
- [ ] Fiado y abonos, **si está habilitado**: venta una sola vez; caja solo cobros; saldo correcto.
- [ ] Cierre de caja ciego: registra diferencias sin alterar ventas.
- [ ] Pérdida de internet: venta continúa, Drive queda pendiente y reintenta.
- [ ] Restauración desde una copia remota: cuadran ventas, stock y caja del punto restaurado.
- [ ] Actualización con base existente: migración correcta, respaldo previo y sin pérdida de datos.
- [ ] PC de 4 GB (si se confirma): POS responde fluidamente durante búsqueda/cobro mientras se realiza respaldo.

---

## 6. Checklist antes de utilizar el sistema en el local

- [ ] Dueña valida flujos principales, permisos y política de fiado (o fiado permanece deshabilitado).
- [ ] Procedimiento de facturación fiscal definido y operativo; un ticket interno no se confunde con factura.
- [ ] Políticas comerciales verificadas para cambios, defectos, promociones y medios de pago.
- [ ] Stock inicial revisado y conciliado físicamente.
- [ ] Caja inicial y medios de pago configurados.
- [ ] Usuarios individuales creados; contraseñas y autorizaciones verificadas.
- [ ] Impresión probada con el equipo real.
- [ ] Respaldo local y Drive probados; restauración real de prueba documentada.
- [ ] Retención, clave de cifrado, instrucciones de recuperación y persona responsable definidos.
- [ ] Instalación y actualización en equipo real probadas; estrategia de seguridad/soporte para Windows confirmada.
- [ ] Guía breve para empleada y dueña, incluyendo corte de luz, falla de impresión y pérdida de PC.

---

## 7. Primera instrucción lista para pegar en Codex

```text
Leé PLAN.md y ESTRUCTURA.md completos. Vamos a ejecutar exclusivamente F00-T01 (estructura inicial del proyecto).

Antes de editar, decime brevemente qué archivos vas a crear y qué decisiones técnicas siguen pendientes. No inventes datos del hardware, versiones de .NET ni reglas comerciales.

Creá la estructura mínima necesaria para una solución C# + WPF + SQLite candidata conforme a ESTRUCTURA.md, el .gitignore y README con instrucciones de compilación. No crees carpetas vacías de módulos futuros; no construyas ventas, stock ni pantallas adicionales. Evitá paquetes innecesarios. Si se necesita fijar una versión provisional de .NET para compilar, documentala como tal para F00-T02.

Al terminar, ejecutá los comandos de compilación disponibles en el entorno e informá el resultado real. Si el entorno no puede compilar WPF por no ser Windows, dejá instrucciones de validación para Windows y no afirmes que compiló. Marcá F00-T01 como completada únicamente si cumple sus criterios; proponé F00-T02 como siguiente paso, sin ejecutarla.
```

## 8. Registro de avance

| Fecha | Tarea | Estado | Evidencia / commit | Pendientes |
|---|---|---|---|---|
| 2026-09-20 | F00-T01 | Terminado | `dotnet build` correcto (0 advertencias, 0 errores); `dotnet test` correcto (1/1) | Framework `net8.0` provisional y compatibilidad con PC real pendientes para F00-T02 |
| 2026-09-20 | F00-T02 | Bloqueado para cierre | .NET 8 compiló y pruebas 1/1; .NET 10 LTS evaluado documentalmente; intento de apertura WPF no verificable visualmente en esta sesión | PC del local: edición/build/canal/arquitectura de Windows, apertura interactiva y evaluación básica de rendimiento |
| 2026-09-20 | F00-T03 | Terminado | Navegación MVVM con datos ficticios; compilación correcta (0/0); pruebas 2/2; validación manual interactiva correcta, incluido el cierre, informada por la persona usuaria | — |
| 2026-09-20 | F00-T04 | Terminado | Restauración y compilación correctas (0/0); pruebas 4/4, incluida SQLite temporal con reapertura; validación manual de escritura, lectura, coincidencia, reapertura y operación sin conexión correcta | D-13 continúa pendiente; la ruta predeterminada no está aprobada para datos reales |
| 2026-09-20 | F00-T05 | Terminado | Compilación correcta (0/0); pruebas 4/4; validación manual de vista previa, diálogo, cancelación e impresión de prueba con Microsoft Print to PDF correcta | Probar con la impresora física y la PC del local antes de producción (D-08) |
| 2026-09-20 | F00-T06 | En curso: instalador y PC real pendientes | Publicación self-contained Release `win-x64` validada manualmente: apertura fuera de Visual Studio, navegación, SQLite, reapertura, vista previa, diálogo/cancelación de impresión, operación offline y fluidez cualitativa correctas | Instalador y desinstalación reales que preserven datos de usuario; prueba de rendimiento/compatibilidad en la PC del local |
| 2026-09-21 | Migración .NET 10 | Validación manual registrada; F00-T06 sigue en curso, F00-T02 sigue bloqueada | Publicación `artifacts\publish\net10-win-x64` validada manualmente fuera de Visual Studio: apertura, navegación, SQLite, funcionamiento sin internet, vista previa, impresión de prueba y fluidez correctas, sin problemas observados y sin mediciones registradas | Instalador y desinstalación reales que preserven datos de usuario; validación de compatibilidad y fluidez en la PC del local; F00-T02 pendiente de edición, versión y arquitectura de ese equipo |
| 2026-09-21 | F00-T06 instalador | En curso: PC real pendiente; F00-T02 sigue bloqueada | Inno Setup 6.7.3 instalado (firma Pyrsys B.V. válida); restore/build Release 0/0 y tests 4/4 correctos; script corregido para regenerar la publicación desde limpieza acotada a `artifacts\publish\net10-win-x64` (con guarda de datos personales) y abortar si publish falla; Setup regenerado (47.321.824 bytes) desde publicación verificada (407 archivos, 0 prohibidos); scripts `.iss`/`.ps1` versionados, salida fuera de git | Validación en la PC del local |
| 2026-09-21 | F00-T06 validación manual | En curso: PC real pendiente; F00-T02 sigue bloqueada | Instalador validado manualmente en cuenta local estándar independiente: cuenta, instalación, acceso directo/apertura, navegación/SQLite/impresión, sin internet, desinstalación y conservación de ambas bases (mismo hash SHA-256) correctos; sin errores ni observaciones | Validación de compatibilidad, arranque y fluidez en la PC del local |
| 2026-09-21 | D-13 | Cuenta única confirmada | Una sola PC y una única cuenta de Windows compartida (fuente: desarrollador; no es decisión de la dueña); sin escenario multi-cuenta | Concretar la carpeta definitiva de producción |
| 2026-09-21 | F01-T01 | Terminado en `feat/f01-initial-schema`; F00-T02 bloqueada, F00-T06 en curso | Esquema V1 (11 tablas + bitácora) en `src/.../Persistence/Migrations/`; restore/build 0/0 y 16/16 pruebas en bases temporales (vacía, idempotencia, FK, UNIQUE/CHECK, stock derivado, actualización con datos + respaldo verificado, negativa sin respaldo, fallo atómico, versión desconocida, catálogo duplicado, esquema heredado sin historial, salto V2-sin-V1); `docs/ESQUEMA.md` con restricciones, semántica de fallos y ausencias justificadas; base provisional intacta; sin commit | F01-T02…T06; ruta definitiva de producción; decisiones D-01/D-02/D-03/D-05/D-07 |
| 2026-09-21 | F01-T02 | Terminado en `feat/f01-sqlite-foundations`; F00-T02 bloqueada, F00-T06 en curso | Fábrica + sesiones + 4 repositorios mínimos; WAL elegido y probado; mapeo corregido a solo 5/6 con prueba de distinción 5/6 frente a 9/1/19; `CommandTimeout` en cada comando (migrador 60 s); restore/build 0/0 y 32/32 pruebas en bases temporales; base provisional intacta; sin commit | F01-T03…T06; ruta definitiva de producción |
| 2026-09-21 | F01-T03 | Terminado en `feat/f01-operation-audit`; F00-T02 bloqueada, F00-T06 en curso | `IGeneradorIds`/`IReloj` (+falsos deterministas), V2 `auditoria`, `RepositorioAuditoria` en misma tx; 4 repos V1 con generador inyectado; restore/build 0/0 y 38/38 pruebas en bases temporales (ids, reloj, correlación operacion_id entre repos y auditoría, rollback conjunto, actor sin id, V1→V2 real con respaldo); V1 intacta; base provisional intacta; sin commit | F01-T04…T06; ruta definitiva de producción |
| 2026-09-21 | F01-T04 | Terminado en `feat/f01-local-auth`; F00-T02 bloqueada, F00-T06 en curso | PBKDF2-SHA512 220k + política 12–256 + V3 + login/logout/sesiones no forjables/matriz/autorizador + regla Administrador (D-21); restore/build 0/0 y 66/66 pruebas en bases temporales; base provisional intacta | F01-T05…T06; recuperación del Administrador, identidad de su titular, inactividad, matriz D-03 |
| 2026-09-21 | F01-T05 | Terminado en `feat/f01-local-backup`; F00-T02 bloqueada, F00-T06 en curso | `IServicioRespaldos`/`ServicioRespaldos` reutilizado por el migrador; restore/build 0/0 y 81/81 pruebas (15 de respaldo en temporales, incluido V1→V3 y V2→V3 con respaldo previo y publicación atómica); base provisional intacta | F01-T06; D-09 producción (carpeta, retención, cifrado, restauración, automatismos, Drive) |
| 2026-09-21 | F01-T06 | Terminado en `feat/f01-integrity-tests`; F00-T02 bloqueada, F00-T06 en curso | Matriz §11 documentada; restore/build 0/0 y 85/85 pruebas en bases temporales (4 nuevas, resto preexistente verificado; sin defectos en código); base provisional intacta | F02; ruta de datos, respaldos, recuperación del Administrador, permisos no confirmados; sin validación en PC del local |

| 2026-09-21 | F02-T01+T02 | Terminado en `feat/f02-products-variants`; F00-T02 bloqueada, F00-T06 en curso | Casos de uso + repos completados + permisos definidos sin conceder (D-23, denegados por defecto); restore/build 0/0 y 95/95 pruebas (10 integrales en temporales + matriz que deniega); sin migración; base provisional intacta | F02-T03…; D-03, D-11, ruta definitiva; catálogo no operativo |
| 2026-09-21 | F02-T03+T04+T05 | Terminado T04, en curso T03 (costos D-07) y T05 (grilla visual); F00-T02 bloqueada, F00-T06 en curso | Precios (V4) + búsqueda/ficha medidas + carga inicial ESTABLECER; restore/build 0/0 y 106/106 pruebas (11 nuevas en temporales); V1/V2/V3 intactas; base provisional intacta | F03; D-03, D-07, D-09, ruta definitiva |

| 2026-09-21 | F03-T03+T04 | Terminado T03, en curso T04 (dañado pendiente); F00-T02 bloqueada, F00-T06 en curso | `AjustarInventario` + `ConsultasInventario` + permiso definido sin conceder (D-25); restore/build 0/0 y 113/113 pruebas (8 en temporales, incl. anti-duplicado tras reapertura); sin migración; base provisional intacta | Resto F03 (T01/T02/T05); D-03, D-13, dañado pendiente |

| 2026-09-21 | F03-T01+T02 | Terminado en `feat/f03-suppliers-receiving`; F00-T02 bloqueada, F00-T06 en curso | Proveedores + recepción atómica con costo histórico (D-07 parcial) + V5; restore/build 0/0 y 123/123 pruebas (10 en temporales + V4→V5 y costo-cero); V1-V4 intactas; base provisional intacta | Resto F03 (T05); D-03, D-07, D-09, ruta definitiva; F03 no terminada |

| 2026-09-21 | Administración Dueña | Terminado en `feat/owner-users-individual-permissions` (merge 3109eeb); F00-T02 bloqueada, F00-T06 en curso | V6 + permisos individuales con denegación explícita + régimen Dueña (D-27, reemplaza D-21); restore/build 0/0 y 132/132 pruebas en temporales; V1-V5 intactas; base provisional intacta | Recuperación de Dueña, inactividad, resto D-03, PC real |
| 2026-09-21 | F03-T05 + Reversión | Integrado en `main` `8eae690`; F00-T02 bloqueada, F00-T06 en curso; F03-T04 sigue parcial por dañado | Conteo parcial, foto vieja, doble aprobación, reversión multilínea/duplicada/stock insuficiente + V6→V7 con respaldo y fallo atómico; restore/build 0/0 y 144/144 pruebas (12 del bloque + 132 previas); V1-V6 intactas hasta V7; base provisional intacta | Pantalla WPF conteo, F03-T04 dañado, D-07/D-09/PC real, recuperación Dueña |
| 2026-09-26 | Rediseño UI WPF (etapa aprobada y cerrada) | Terminado y validado en `feat/wpf-ui-redesign-phase1`; F04 no iniciado | `LoginWindow` separada + shells por módulos + master/detail (Catálogo, Proveedores, Usuarios, Consultas) + `FichaProductoWindow`/`DetalleRecepcionWindow` + Conteo por pasos 1-4 + Inicio con datos reales (`ConsultasInicio`/`RepositorioInicio`); `dotnet build` Debug 0 advertencias 0 errores; 263/263 pruebas; `git diff --check` limpio; mínima 980×620 validada (`DevMainWindow` 1200×750, `MinWidth=980 MinHeight=620`, captura 64); sin `.db`/capturas/binarios en el árbol; ver D-34 | `docs/ui-reference/` queda fuera del commit (material de trabajo, 4 archivos); `ContentStringFormat` propagado en los 5 templates de `Button` (fix puntual de cierre); F00-T02 sigue bloqueada y F00-T06 sigue en curso |
| 2026-09-29 | F00-T02/F00-T06 (criterio de planificación) | Pendientes de hardware definitivo; dejan de bloquear DESARROLLO | Decisión de la persona usuaria (ver D-36): F00-T02 y F00-T06 permanecen pendientes, no se marcan completadas; no bloquean el desarrollo de F04 ni fases posteriores (entorno actual + DB ficticias); pasan a bloquear instalación definitiva, puesta en producción y entrega al local | Validación obligatoria en PC definitiva antes de producción: Windows/arquitectura, publicación, arranque, persistencia, permisos de archivos, DPI/resolución, rendimiento, impresora cuando corresponda, funcionamiento interactivo general |
| 2026-10-03 | F06 core | Núcleo implementado y publicado en `7b0dd5e231ce4f8038a165fc82f50684f8f54ed2`; T01–T04/T06/T07 completadas técnicamente; T05 bloqueada por D-05 | V12 aditiva; build Debug 0 advertencias/0 errores; suite focalizada 15/15; suite completa 369/369; ver D-39 | Capturas WPF 101–110 pendientes (`apps: []`); D-01 procedimiento fiscal externo y validación PC real pendientes; no producción |
| 2026-10-04 | F07-T01 | Implementada y validada técnicamente en `feat/f07-clientes-cuenta-corriente`; F07 seguía parcial en ese corte | V13 aditiva; clientes/CRUD lógico, permisos efectivos, consulta paginada, asociación POS opcional, snapshot histórico en consulta/ticket y ventas/cambios en historial; build Debug 0 advertencias/0 errores; F07-T01 5/5; suite 374/374; `git diff --check` limpio | WPF manual/capturas 111–118 pendientes (`apps: []`); F00-T02 y PC real pendientes |
| 2026-10-04 | F07-T02–T06 | Implementadas y validadas técnicamente; sin commit/push | Política D-02 aprobada (D-41); V14 aditiva y migración V13→V14 con respaldo probado; fiado parcial/total, ledger, abonos FIFO y por medio, ajustes, recibo, integración de caja/F06/anulación, permisos; build Debug 0/0; tests 386/386 (17/17 focalizados de Clientes/cuenta corriente); `git diff --check` limpio | Capturas/revisión visual manual 119–129 pendientes (`apps: []`); capturas 111–118 de T01 también pendientes; validación PC real/F00-T02 y F00-T06 pendiente; D-01 y producción NO habilitada |
| 2026-10-06 | F11-A–E | Implementadas y verificadas técnicamente en `feat/f11-etiquetas-codigos-batch`; sin commit/push | `dotnet restore LocalInfantil.sln` correcto; build Debug 0 advertencias/0 errores; suite focalizada 41/41 y suite completa detallada 461/461 (5,3657 min); `git diff --check` limpio. Lote transaccional/idempotente, Code128 round-trip, etiquetas vectoriales A4, impresión Windows y escaneo exacto POS; sin migración V1–V15 ni cambios de stock por imprimir | Validación visual WPF no disponible (`apps: []`); probar manualmente matriz, preview/PDF/cancelación, cargas y POS con `--dev-db`; F11-F lector/impresora/calibración y PC real pendientes; producción NO habilitada |

> Actualizar esta tabla al final de cada iteración. No inventar resultados de compilación, pruebas ni pruebas en hardware real.
