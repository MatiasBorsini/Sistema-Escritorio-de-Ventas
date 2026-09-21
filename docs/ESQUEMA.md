# ESQUEMA.md — Esquema inicial F01-T01 y migraciones versionadas

> Estado: esquema V1 implementado y probado en `feat/f01-initial-schema` (2026-09-21).
> F01-T01 sigue en curso: el cierre requiere F01-T02…T06. F00-T02 bloqueada, F00-T06 en curso.

## 1. Convenciones (valen para todas las tablas)

- **Identificadores:** `TEXT` estables (p. ej. GUID/ULID generados en la capa de aplicación cuando corresponda), clave primaria, nunca reutilizados.
- **Dinero:** `INTEGER` en unidades menores (centavos ARS) + columna `moneda TEXT` (`'ARS'` por defecto). Nunca `REAL` ni punto flotante para importes.
- **Fechas:** `TEXT` en UTC ISO-8601. Comparaciones de orden sobre texto son válidas en ese formato.
- **Borrados:** no existen `DELETE` sobre registros comerciales. Todas las foráneas usan `ON DELETE RESTRICT` y las entidades se desactivan con `activo`/`estado`. Las correcciones futuras son reversos auditables, nunca edición silenciosa.
- **Conexiones:** el migrador activa `PRAGMA foreign_keys = ON` en cada conexión que abre. El modo de journaling, bloqueos y repositorios pertenecen a F01-T02 y no se deciden aquí.

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
- Journaling, repositorios, transacciones de negocio → F01-T02.
- Reloj, identificadores y auditoría → F01-T03.
- Respaldo local y restauración → F01-T05.
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

## 6. Limitaciones que quedan para F01-T02 y F01-T05

- **F01-T02:** modo de journaling sin evaluar (SQLite usa el diario de rollback por defecto; WAL no medido); sin manejo de `busy`/bloqueos ni reintentos; sin repositorios ni transacciones de negocio; sin ajuste de `PRAGMA` (`synchronous`, `cache_size`, `temp_store`); cada conexión futura de la aplicación deberá activar `foreign_keys` igual que el migrador.
- **F01-T05:** sin procedimiento de respaldo/restauración de producción (ubicación, retención, ensayo de restauración); el respaldo previo a migrar va al directorio que indique el llamador, sin retención automática; sin cableado del migrador al arranque (cuando se conecte, un fallo debe impedir operar).
- **F02-T03:** superposición de vigencias de precios no impedida a nivel de base (resolver la política antes de fijarla en esquema).
- Compatibilidad entre versiones reales solo probada a nivel de mecanismo (V1→V2 simulada); cada migración futura necesita su propia prueba de actualización con datos ficticios.
