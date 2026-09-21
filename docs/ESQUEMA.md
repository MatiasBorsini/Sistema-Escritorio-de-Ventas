# ESQUEMA.md — Esquema inicial F01-T01, migraciones y fundamentos F01-T02

> Estado: F01-T01 terminado; F01-T02 implementado y probado en `feat/f01-sqlite-foundations` (2026-09-21), pendiente de revisión.
> F00-T02 bloqueada, F00-T06 en curso.

## 1. Convenciones (valen para todas las tablas)

- **Identificadores:** `TEXT` estables (p. ej. GUID/ULID generados en la capa de aplicación cuando corresponda), clave primaria, nunca reutilizados.
- **Dinero:** `INTEGER` en unidades menores (centavos ARS) + columna `moneda TEXT` (`'ARS'` por defecto). Nunca `REAL` ni punto flotante para importes.
- **Fechas:** `TEXT` en UTC ISO-8601. Comparaciones de orden sobre texto son válidas en ese formato.
- **Borrados:** no existen `DELETE` sobre registros comerciales. Todas las foráneas usan `ON DELETE RESTRICT` y las entidades se desactivan con `activo`/`estado`. Las correcciones futuras son reversos auditables, nunca edición silenciosa.
- **Conexiones:** la fábrica abre cada conexión con `PRAGMA foreign_keys = ON` y `PRAGMA busy_timeout` configurado. El modo de journaling se establece una sola vez (ver §7) y nunca en cada apertura.

## 2. Tablas de la migración V1 (`esquema-inicial-f01`)

| Tabla | Clave primaria | Foráneas (RESTRICT) | Restricciones destacadas |
|---|---|---|---|
| `schema_migrations` | `version INTEGER` | — | Bitácora de versiones aplicadas (`nombre`, `aplicada_en`). |
| `usuario` | `id TEXT` | — | `nombre UNIQUE`; `activo IN (0,1)`. Sin credenciales: pertenecen a F01-T04 (D-03). Valores de `rol` sin `CHECK` porque la matriz de permisos sigue pendiente (D-03). |
| `producto` | `id TEXT` | — | `nombre <> ''`; `marca`/`categoria` como texto libre anulable (F02 decidirá si se normalizan); `activo IN (0,1)`. |
| `variante` | `id TEXT` | `producto_id → producto` | `sku UNIQUE` y no vacío: una variante = un stock independiente. `talle`/`color` anulables (artículos sin esos atributos y talles reales pendientes, D-11). |
| `precio` | `id TEXT` | `variante_id → variante` | `importe_minor >= 0`; vigencia con `vigente_hasta > vigente_desde` (NULL = vigente). **Sin columna de costo**: el criterio sigue pendiente (D-07). |
| `movimiento_inventario` | `id TEXT` | `variante_id → variante`, `creado_por_usuario_id → usuario` (anulable) | `tipo IN ('inicial','ingreso','egreso','ajuste')`; signo coherente con el tipo (`ingreso`/`inicial` > 0, `egreso` < 0, `ajuste` <> 0); `ajuste` exige `motivo` no vacío (F03-T03); `operacion_id` correlaciona la operación. **El stock es `SUM(cantidad)` por variante**: no hay columna de existencias que sobrescribir. |
| `venta` | `id TEXT` | — | `estado IN ('pendiente','confirmada','anulada')` (la anulación futura es reverso auditado, no borrado); `total_minor >= 0`; `referencia_fiscal` solo como referencia externa opcional (D-01: el ticket interno nunca es factura). Sin cliente, descuentos, impuestos ni saldos: políticas sin confirmar (D-02, D-05, F07). |
| `venta_linea` | `id TEXT` | `venta_id → venta`, `variante_id → variante` | `cantidad > 0`; importes >= 0 con `subtotal = cantidad × precio_unitario` (instantánea histórica); `promocion_ref` texto libre anulable como gancho para F06 (sin tabla de promociones: D-05). |
| `pago` | `id TEXT` | `venta_id → venta` | `medio IN ('efectivo','transferencia','debito','credito','billetera')` (medios confirmados en PLAN 0.1); `importe_minor > 0`. Pagos combinados = varias filas. |
| `caja_sesion` | `id TEXT` | `usuario_id → usuario` | `estado IN ('abierta','cerrada')`; `fondo_inicial_minor >= 0`; `cerrada_en >= abierta_en`. |
| `caja_movimiento` | `id TEXT` | `sesion_id → caja_sesion`, `venta_id → venta` (anulable) | `tipo IN ('venta','ingreso','egreso','reintegro')`; `importe_minor <> 0`; `tipo = 'venta'` exige `venta_id`. |

Índices (todos justificados por consultas de conciliación): `ix_variante_producto`, `ix_precio_variante`, `ix_movimiento_variante`, `ix_venta_linea_venta`, `ix_pago_venta`, `ix_caja_movimiento_sesion`.

La migración V2 agrega `auditoria` (ver §8); V1 publicada en main no se modificó.

## 3. Migraciones: reglas obligatorias

1. Versión identificable (`schema_migrations`), ejecución única, en orden, cada una en su transacción.
2. Nunca borrar ni recrear una base existente para actualizarla.
3. Una base preexistente con contenido no recibe ninguna modificación de esquema antes del respaldo: el migrador primero inspecciona en solo lectura (existencia de `schema_migrations` e historial), y recién después de validar y respaldar escribe.
4. Casos admitidos, explícitos: base nueva/vacía, o base con historial `schema_migrations` propio. Un esquema heredado sin historial (incluida la base técnica de F00) se rechaza con excepción, sin crear bitácora, sin respaldo y sin aplicar nada.
5. El historial aplicado debe ser un prefijo exacto del catálogo: sin versiones desconocidas (base más nueva que la app) ni saltos (V2 sin V1). Cualquier incoherencia se rechaza sin ejecutar migraciones.
6. Con base preexistente y pendientes: respaldo consistente previo (`VACUUM INTO`, atómico y válido con la base en uso) verificado con `integrity_check` en una conexión independiente de solo lectura; si el respaldo no verifica, se elimina y se aborta. Sin directorio de respaldos, la migración con datos se niega con excepción.
7. Semántica exacta ante fallos: cada migración es atómica por sí misma (su DDL y su registro van en la misma transacción; si falla, esa versión no queda registrada y sus objetos se revierten). NO hay atomicidad entre versiones distintas: si falla la segunda de varias pendientes, las anteriores ya confirmadas quedan registradas y las restantes siguen pendientes para la próxima ejecución. Esto no es un defecto respecto de los criterios vigentes: la garantía exigida es que el fallo sea visible (excepción), que no quede estructura parcial sin registrar y que la aplicación no opere hasta que `MigrarAsync` complete con éxito; el registro por versión hace que retomar sea seguro.
8. Cada versión futura debe probarse en base vacía y en base de versión anterior con registros ficticios, incluyendo errores, integridad y conservación.
9. Sin promesas de compatibilidad con versiones antiguas de la app sin pruebas específicas.

Implementación: `src/LocalInfantil.Infrastructure/Persistence/Migrations/` (`Migracion`, `MigracionesF01`, `MigradorBaseDatos`). El catálogo inyectable existe para probar actualizaciones; el catálogo productivo es `MigracionesF01.Todas`.

## 4. Intencionalmente ausente (con decisión que lo bloquea)

- Credenciales y matriz de permisos → F01-T04 (D-03).
- Costos y márgenes → D-07 (se agregarán con recepciones en F03).
- Clientes, fiados, saldos y abonos → D-02/F07 (el esquema no habilita venta fiada).
- Promociones y reglas de cambio/devolución → D-05/F06 (solo `promocion_ref` como gancho).
- Descuentos e impuestos → sin política confirmada.
- Normalización de marca/categoría/talle → F02 (D-11).
- Reloj, identificadores y auditoría → F01-T03.
- Respaldo de producción, retención, cifrado y Drive → F01-T05 (§10) y F08 (D-09).
- Ruta definitiva de producción → pendiente (D-13: cuenta única ya confirmada, falta concretar la carpeta).

## 5. Pruebas (16/16 correctas el 2026-09-21, solo bases temporales aisladas)

- Creación del esquema sobre base vacía + registro de versión.
- Re-ejecución idempotente (0 pendientes).
- FK rechazan referencias inexistentes; UNIQUE de SKU; CHECK de precio negativo y de ajuste sin motivo.
- Stock como suma de movimientos.
- Actualización V1→V2 simulada con registros ficticios: respaldo generado y verificado, registros conservados.
- Migración con datos sin respaldo: negada, sin cambios.
- Migración fallida: sin estructura parcial, datos intactos, recuperación idempotente.
- Base con versión desconocida (99): rechazada sin aplicar nada, datos intactos.
- Catálogo con versión duplicada: rechazado antes de crear la base.
- Base heredada sin historial: rechazada sin crear bitácora, sin respaldo y sin modificar sus datos.
- Historial con salto (V2 sin V1): rechazado sin ejecutar migraciones, datos intactos, sin respaldo generado.
- La base provisional del equipo no fue tocada por ninguna prueba (verificación punto 7: el código de migraciones y las pruebas no referencian su ruta; solo usan directorios temporales con GUID).

## 6. Limitaciones que quedan para otros

- **F01-T05 producción:** sin ubicación, retención, cifrado ni ensayo de restauración reales (ver D-09 y §10); el respaldo previo a migrar va al directorio que indique el llamador, sin retención automática; sin cableado del migrador al arranque (cuando se conecte, un fallo debe impedir operar).
- **F02-T03:** superposición de vigencias de precios no impedida a nivel de base (resolver la política antes de fijarla en esquema).
- Compatibilidad entre versiones reales solo probada a nivel de mecanismo (V1→V2 simulada); cada migración futura necesita su propia prueba de actualización con datos ficticios.

## 7. Fundamentos SQLite F01-T02 (fábrica, journaling, bloqueos, transacciones, repositorios)

Implementación: `Application/Abstractions/` (`ISesionPersistencia`, `IFabricaSesionesPersistencia`, `AlmacenamientoOcupadoException`, `IRepositorio{Productos,Variantes,Precios,Movimientos}`), `Application/Contracts/` (`Datos{Producto,Variante,Precio,Movimiento}` con `creadoEn` explícito hasta que F01-T03 aporte el reloj), `Infrastructure/Persistence/Sqlite/` (`OpcionesConexionSqlite`, `FabricaConexionesSqlite`, `SesionPersistenciaSqlite`) y `Infrastructure/Persistence/Repositorios/` (4 repositorios mínimos sobre el esquema V1). WPF no contiene SQL ni referencias nuevas: los repositorios solo se usan desde pruebas.

- **Journaling elegido: WAL.** Evidencia reproducible: se establece y persiste tras reapertura con datos e integridad `ok`; un lector concurrente lee lo confirmado durante una escritura sin confirmar; el modo no cambia en cada apertura; el diario convencional (`delete`) también se verificó como base funcional. Motivo: en una PC de un solo puesto, los lectores (UI, futuros respaldos durante ventas) no deben bloquearse ante escritores; archivos `-wal`/`-shm` transitorios, ya ignorados en git. Esto NO demuestra resistencia a cortes eléctricos: esa garantía requiere otras verificaciones.
- **Sesiones explícitas:** las de escritura abren con `BEGIN IMMEDIATE` (toman el bloqueo al iniciar, sin sorpresas a mitad de la operación); las de lectura (`IniciarLecturaAsync`) no abren transacción y nunca bloquean escritores; confirmar una sesión de lectura o escribir en ella es un error. Liberar sin confirmar revierte. Hallazgo de diseño: el `BeginTransaction()` implícito de la primera versión colgaba ~30 s ante contienda; el control explícito lo redujo al tiempo configurado (ver pruebas).
- **Tiempos configurables (dos capas):** `OpcionesConexionSqlite` exige ruta explícita y `EsperaBloqueoMs > 0` (por defecto 5000 ms: cubre bloqueos breves sin congelar; el E/S corre fuera del hilo UI). Capa 1: `PRAGMA busy_timeout` por conexión. Capa 2: `CommandTimeout` en cada comando (incluido el `BEGIN`), derivado en segundos con un mínimo de 1. El modo de journaling nunca se reestablece al abrir.
- **Bloqueos sin reintentos:** solo `SQLITE_BUSY` (5) y `SQLITE_LOCKED` (6) se traducen a `AlmacenamientoOcupadoException` con la técnica anidada; `SQLITE_INTERRUPT` (9) y el resto se propagan sin disfrazar. No hay reintentos automáticos de escritura: repetir podría duplicar una operación ya confirmada. Un tiempo vencido en la confirmación deja estado desconocido: no reintentar a ciegas, conciliar con `operacion_id`.
- **Tiempos en todos los comandos:** cada comando de sesiones, repositorios, fábrica (incluidos pragmas y `BEGIN IMMEDIATE`) y migrador (60 s fijos, con holgura para `VACUUM INTO`) lleva `CommandTimeout` explícito; no hay bucles de reintento en el código.
- **Pruebas F01-T02 (solo temporales):** FK activas en cada conexión; escritura/lectura/cierre/reapertura por repositorios; bloqueo entre conexiones con fallo rápido mapeado (< 15 s, código 5) y sin parciales; rollback compuesto ante error intermedio; round-trip de precios (vigencia) y movimientos (suma); opciones inválidas rechazadas; distinción 5/6 frente a 9/1/19 sin disfrazar; journal WAL/delete, persistencia, integridad y estabilidad del modo.

## 8. Operaciones, reloj y auditoría F01-T03 (en curso, pendiente de revisión)

Implementación en `feat/f01-operation-audit`: `IGeneradorIds`/`GeneradorIds` (GUID sin guiones, como V1) con `GeneradorIdsSecuencial` para pruebas; `IReloj`/`RelojSistema` (UTC) con `RelojFijo` para pruebas; migración V2 `auditoria-f01`; `IRepositorioAuditoria`/`RepositorioAuditoria` (recibe generador y reloj, escribe en la misma sesión de la operación).

- **Identificadores:** centralizados e inyectables; los 4 repositorios V1 ahora reciben `IGeneradorIds` (antes `Guid` directo; la sonda F00 y el migrador no se tocaron, fuera de alcance). Consultar devuelve los ids guardados, nunca genera nuevos.
- **Reloj:** `DateTimeOffset` UTC formateado `"o"` al persistir (misma convención §1). El código nuevo no usa `DateTime.UtcNow` disperso; el existente del migrador se conserva sin refactorizar.
- **Auditoría (`auditoria`):** `operacion_id`, `tipo_evento` (texto libre, sin taxonomía inventada), `actor_tipo IN ('usuario','sistema')` con `actor_id` obligatorio para `usuario` (restricción a nivel de base: no se atribuye a una persona sin conocerla; `actor_id` referencia a `usuario`), `referencia_tipo`/`referencia_id` libres cuando corresponda, `detalle` sin secretos, `creado_en`, índice por `operacion_id`. Sin permisos (F01-T04).
- **`operacion_id` correlaciona pero no garantiza idempotencia por sí solo:** permite rastrear qué registros pertenecen a la misma operación; la protección contra duplicados (p. ej. doble confirmación) se implementará sobre esta base en F04 y requiere verificación explícita antes de reejecutar.
- **Atomicidad conjunta:** negocio + auditoría comparten sesión/transacción (demostrado con operación ficticia: producto de prueba + 2 eventos; ante fallo de un evento, ni el producto ni la auditoría persisten).
- **Pruebas F01-T03 (solo temporales):** ids distintos y estables; reloj fijo determinista; escritura/lectura/correlación; rollback conjunto; actor sin id rechazado; V1→V2 real con datos ficticios, respaldo verificado, conservación y re-ejecución vacía.
- **Límites:** sin cableado al arranque; sin autenticación/permisos (F01-T04); sin respaldos remotos (F08) ni copias automáticas; sin ventas ni catálogo comercial; ruta definitiva pendiente (D-13).

## 10. Respaldo local F01-T05 (en curso, pendiente de revisión)

Implementación en `feat/f01-local-backup`: `IServicioRespaldos`/`ServicioRespaldos` (`Infrastructure/Persistence/Respaldos/`), excepciones `RespaldoFallidoException`/`RespaldoInvalidoException`, registros `RespaldoVerificado`/`VerificacionRespaldo`. El migrador reutiliza el servicio para su respaldo previo (sin duplicar lógica; mantiene su nombre `respaldo-previo-vN`).

- **Mecanismo y justificación:** `VACUUM INTO` sobre la base indicada: instantánea atómica válida con la base en uso y en cualquier journaling (el WAL se consolida en la copia), sin copiar el `.db` en caliente y sin arrastrar `-wal`/`-shm`/`-journal` (solo se crea el `.db` destino, con nombre único que jamás se sobrescribe). Publicación segura: se escribe un temporal exclusivo en el mismo directorio, se verifica y recién entonces se publica con un movimiento atómico; ante un fallo solo se limpia ese temporal y ningún nombre definitivo aparece incompleto. Bloqueos breves únicamente; sin detener la operación. Los respaldos quedan fuera del repositorio y del instalador.
- **Verificado significa:** `integrity_check` ok en conexión independiente de solo lectura + `foreign_key_check` vacío + `schema_migrations` presente + versiones exactamente las esperadas (si se indican). Solo entonces el respaldo cuenta como exitoso; ante cualquier fallo no queda éxito falso (los parciales propios se eliminan, los anteriores no se tocan) y no hay reintentos.
- **Restauración de ensayo:** verifica primero, copia solo el `.db` a un directorio temporal aislado (nunca al origen), reabre y comprueba integridad; el contenido y la escritura posterior los verifica el llamador. Nunca sobrescribe el origen ni borra el respaldo original. La copia hereda modo de diario convencional (propio de `VACUUM INTO`); la producción re-establecerá WAL al restaurar (procedimiento pendiente).
- **Errores:** origen inexistente, destino inválido, corrupción, esquema no reconocido, versiones inesperadas y destino ocupado se traducen a fallos explícitos tipados, sin secretos en mensajes.
- **Pruebas F01-T05 (solo temporales):** recuperación exacta fila por fila, WAL con operaciones confirmadas, integridad + FK, ruta distinta sin tocar origen, reapertura con escrituras, rechazo de corrupto/no reconocido/versiones, destino inválido, dos consecutivos, fallo sin destruir anteriores, publicación atómica sin temporales ante fallo de verificación, migrador V1→V3 y V2→V3 con respaldo previo correspondiente al estado anterior, fallo de respaldo que impide migrar, ensayo sin sobrescritura ni auxiliares, provisional intacta.
- **Pendiente para producción (D-09, sin inventar):** carpeta definitiva, cantidad/antigüedad de copias, cifrado y su clave, procedimiento definitivo de restauración, copias automáticas y Drive (F08).

## 9. Autenticación local F01-T04 (en curso, pendiente de revisión)

Implementación en `feat/f01-local-auth`: `IHashContrasenas`/`Pbkdf2HashContrasenas`, `IRepositorioUsuarios`/`RepositorioUsuarios`, casos de uso `IniciarSesion`/`CerrarSesion`/`CrearUsuario`/`RestablecerContrasena`, `GestorSesiones`, `MatrizPermisos` + `PermisosComerciales` + `RolesLocal`, `IAutorizador`/`Autorizador`, migración V3 `huella-contrasena-f01`.

- **Algoritmo:** PBKDF2-HMAC-SHA512, 220000 iteraciones (referencia OWASP), sal de 16 bytes (CSPRNG), subclave de 32 bytes, comparación en tiempo constante. Formato versionado `v1$iteraciones$sal$huella` (Base64). Las huellas de parámetros anteriores verifican pero piden actualización. Un conteo manipulado por encima del vigente no se ejecuta (evita consumo desproporcionado) y esa huella queda inválida.
- **Política mínima (propuesta documentada):** entre 12 y 256 caracteres, sin clases de caracteres arbitrarias y admitiendo frases de corrido. Solo se exige no vacía por debajo de ese mínimo; la complejidad por composición no se impone.
- **Esquema V3:** `ALTER TABLE usuario ADD COLUMN huella_contrasena TEXT NULL` (V1/V2 intactas). Las filas preexistentes quedan con NULL y la verificación falla cerrada ante huella ausente. Cuentas locales independientes de la cuenta de Windows (dueña y empleada).
- **Autenticación:** error genérico sin enumeración; verificación señuelo + retardo configurable (500 ms, sin bloqueos permanentes) ante desconocido/inactivo/sin huella; desactivados no autentican; sin credenciales predeterminadas ni embebidas; sin texto plano en ningún lado (código, logs, auditoría).
- **Sesiones no forjables:** ningún caso de uso acepta un objeto de sesión del llamador; todos reciben el id y lo resuelven en `GestorSesiones`. Alta inicial solo en base que nunca tuvo usuarios (ni siquiera desactivados), sin sesión y en la misma transacción que la creación, forzando el rol Administrador. Con cualquier historial, el alta exige `usuarios.administrar` y nunca asigna el rol Administrador por vía genérica; la desactivación y el restablecimiento exigen el mismo permiso; el restablecimiento propio exige además la contraseña actual. Ids de sesión del generador criptográfico (GUID de 32 hex, únicos).
- **Autorización separada:** `Autorizador` exige sesión vigente, re-lee al usuario (desactivar expulsa y revoca de inmediato, incluida la última-admin que nunca se desactiva; los cambios de matriz aplican al instante al leerse en vivo) y solo concede permisos explícitos. Matriz configurable con denegación por defecto; concedidos: `usuarios.administrar` (solo Administrador), `reintegro.autorizar` (dueña y empleada) y `reintegro.medio_excepcional` (dueña). El Administrador no recibe permisos comerciales automáticamente. Sin privilegios sobre descuentos, ajustes, fiados, anulaciones, reportes ni costos. Verificación en Application (ensamblado probado), sin WPF.
- **Autorización separada:** `Autorizador` exige sesión vigente, re-lee al usuario (desactivar expulsa y revoca de inmediato; los cambios de matriz aplican al instante al leerse en vivo) y solo concede permisos explícitos. Matriz configurable con denegación por defecto; únicos concedidos: `reintegro.autorizar` (ambos roles) y `reintegro.medio_excepcional` (dueña). Sin privilegios sobre descuentos, ajustes, fiados, anulaciones, reportes ni costos. Verificación en Application (ensamblado probado), sin WPF.
- **Inactividad (propuesta, sin plazo decidido):** en esta PC compartida conviene cerrar sesiones tras inactividad usando el reloj inyectable (ya disponible) y `CerrarSesion`; el plazo concreto queda pendiente de tu decisión y no se implementó. La sesión cerrada no es reutilizable (probado).
- **Autorización separada:** `Autorizador` exige sesión vigente, re-lee al usuario (desactivar expulsa y revoca de inmediato; los cambios de matriz aplican al instante al leerse en vivo) y solo concede permisos explícitos. Matriz configurable con denegación por defecto; únicos concedidos: `reintegro.autorizar` (ambos roles) y `reintegro.medio_excepcional` (dueña). Sin privilegios sobre descuentos, ajustes, fiados, anulaciones, reportes ni costos. Verificación en Application (ensamblado probado), sin WPF.
- **Inactividad (propuesta, sin plazo decidido):** en esta PC compartida conviene cerrar sesiones tras inactividad usando el reloj inyectable (ya disponible) y `CerrarSesion`; el plazo concreto queda pendiente de tu decisión y no se implementó.
- **Auditoría:** inicio/cierre/alta/restablecimiento con actor correcto (fallos como sistema sin el nombre intentado); ningún registro contiene contraseñas.
- **Esquema V3:** `ALTER TABLE usuario ADD COLUMN huella_contrasena TEXT NULL` (V1/V2 intactas). Las filas preexistentes quedan con NULL y la verificación falla cerrada ante huella ausente. Cuentas locales independientes de la cuenta de Windows (dueña y empleada).
- **Autenticación:** error genérico sin enumeración; verificación señuelo + retardo configurable (500 ms, sin bloqueos permanentes) ante desconocido/inactivo/sin huella; desactivados no autentican; sin credenciales predeterminadas ni embebidas; sin texto plano en ningún lado (código, logs, auditoría).
- **Sesiones y autorización separadas:** `GestorSesiones` en memoria; `Autorizador` exige sesión vigente, re-lee al usuario (desactivar expulsa y revoca de inmediato) y solo concede permisos explícitos. Matriz configurable con denegación por defecto; únicos concedidos: `reintegro.autorizar` (ambos roles) y `reintegro.medio_excepcional` (solo dueña). Sin privilegios sobre descuentos, ajustes, fiados, anulaciones, reportes ni costos. Sin reintegros implementados: solo identificadores + pruebas.
- **Auditoría:** inicio/cierre/alta/restablecimiento con actor correcto (fallos como sistema sin el nombre intentado); ningún registro contiene contraseñas.
- **Pendiente para antes de producción (no inventado):** recuperación del acceso si el Administrador olvida su contraseña (sin maestras, puertas traseras ni recuperaciones anónimas); identidad de la persona que tendrá la cuenta Administrador; caducidad e inactividad de sesión (plazo a confirmar); complejidad más allá de longitud; matriz completa D-03.

## 11. Matriz F01-T06 (85/85 correctas el 2026-09-21, equipo de desarrollo)

Pruebas automáticas en este equipo, no validación en la PC del local. Cobertura preexistente (F01-T01…T05) salvo las 4 marcadas como nuevas, agregadas por no tener equivalente.

**A. Migraciones (semántica: atomicidad por versión, no de la cadena):** vacía→vigente, V1→V3 y V2→V3 con datos y respaldo previo, re-ejecución vacía, desconocidas/huecos/heredadas rechazadas, fallo con reversión sin registro falso, respaldo obligatorio y bloqueante — cubiertas (EsquemaInicialTests, AuditoriaTests, RespaldoLocalTests).

**B. Integridad:** FK activas por conexión e inserciones huérfanas sin parciales (preexistente); `RESTRICT` ante borrado con dependientes, unicidad de nombre de usuario y unicidad a mitad de operación (nuevas, IntegridadTests); `foreign_key_check` + `integrity_check` sobre base representativa (preexistente, vía respaldos).

**C. Transacciones (operación ficticia con movimiento + auditoría):** confirmación conjunta, rollback al descartar, FK a mitad de operación, segunda conexión con solo confirmados, fallo de auditoría sin negocio persistido (preexistentes); violación de unicidad a mitad de operación (nueva).

**D. Respaldos:** sin cambios en F01-T06; garantías de F01-T05 intactas y re-ejecutadas (15 pruebas).

Sin defectos reales en código F01: las 4 pruebas nuevas pasaron contra la implementación existente a la primera (tras corregir el acceso a conexiones en la prueba, sin tocar producción).
- **Pruebas F01-T04 (solo temporales):** login correcto/incorrecto sin enumeración, sales únicas por usuario, desactivado y sesión inválida/cierre, denegación por defecto, matriz de reintegros por rol, autorización en Application (ensamblado verificado, sin WPF), sesiones no forjables (forjada y sin sesión rechazadas), bootstrap sin reapertura con desactivados y con rol forzado a Administrador, alta/desactivación/restablecimiento solo con permiso (incluida propia con actual), denegación a dueña/empleada, sin auto-asignación de Administrador, último admin protegido, admin sin comerciales, ids impredecibles, política de longitud, huella anterior y conteo manipulado, auditoría sin secretos, mecanismo de huella a nivel repositorio, V2→V3 con datos, respaldo, conservación y re-ejecución vacía.
