# Liquidaciones y conciliación de Mercado Pago — diseño técnico

> **Estado: IMPLEMENTADO (etapa de conciliación, V18 → V19).** El circuito
> `VENTA → COBRO → LIQUIDACIÓN → CONCILIACIÓN` está implementado para
> **importación manual de CSV oficial** (`LectorReporteLiquidacionMp`,
> `PrevisualizarImportacionLiquidacionMp`, `ImportarLiquidacionMp`,
> `ConciliarLiquidacionMp`, `ConsultarConciliacionMp`), con migración V18
> aditiva (`mercado_pago_importacion` / `liquidacion` / `conciliacion`),
> corregida por **V19** (identidad de fila y deduplicación correcta), y
> sección en Configuración → Mercado Pago. La descarga automática por API
> (`/v1/account/release_report*`) sigue **solo diseñada**. No se agrega SDK de
> Mercado Pago; no se habilita producción. Las reglas de F04/F05/F06/F09/F12
> vigentes no se modifican. Ver `D-49` y `D-50` en `docs/DECISIONES.md`.
>
> Único local físico en Argentina; única terminal conocida: Mercado Pago Point.
> Windows WPF / .NET 10 / SQLite, offline-first. La integración F12 existente
> usa Orders API y solo modalidad **integrada (PDV)**.

---

## 1. Objetivo y alcance

Completar el circuito de dinero de Mercado Pago Point para poder explicar, con
datos reales:

- cuánto se **vendió** (importe comercial de la venta);
- cuánto se **cobró** (bruto por medio);
- cuándo y cuánto se **liquidó** (neto acreditado tras deducciones);
- qué quedó **sin conciliar** y por qué.

Se admiten dos modalidades operativas de Point y una tercera de apagado:

| Modo | Qué hace LocalInfantil | Terminal física |
|---|---|---|
| **INTEGRADO** | Envía el importe por API, consulta el resultado remoto y confirma por la saga F12 vigente. | `operating_mode = PDV` |
| **MANUAL** | El operador cobra en el Point de forma autónoma; LocalInfantil registra el cobro declarado y lo deja **pendiente de conciliación externa**. | `operating_mode = STANDALONE` (o PDV usado sin API) |
| **DESACTIVADO** | Point no se ofrece como medio operativo en el POS. | cualquiera |

Fuera de alcance explícito: multi-procesador genérico, selector de cuotas en
POS, facturación fiscal, integración con otros adquirentes.

---

## 2. Estado real auditado (lo que ya existe)

### 2.1 Flujo actual (solo integrado)

`IniciarCobroPoint` valida permisos (`ventas.confirmar`), valida líneas (variante
activa, Disponible real, precio vigente contra `precio_esperado`), exige caja
abierta, persiste la **saga durable** (operación + líneas snapshot + pagos
planificados) **antes** del POST, crea la order (`POST /v1/orders`,
`type:"point"`, `external_reference:"LI-<operacionId>"`, `expiration_time:"PT15M"`,
`config.point.{terminal_id, print_on_terminal:"no_ticket"}`) con
`X-Idempotency-Key`, y deja la operación en `esperando_terminal` o
`procesada_remota`.

`ConsultarCobroPoint` consulta `GET /v1/orders/{id}`, mapea estado remoto→local
(`TransicionesOperacionMp`) y, solo con `processed`, ejecuta la **confirmación
local idempotente** (`ConfirmarVenta` con el mismo `operacion_id`). Timeout ≠
rechazo. Estado remoto desconocido → `requiere_conciliacion`.

### 2.2 Tablas y campos relevantes (V16 ya aplicada)

- `mercado_pago_operacion`: `id`, `operacion_id` UNIQUE, `idempotency_key` UNIQUE,
  `external_reference` UNIQUE (≤64), `order_id` UNIQUE nullable, `terminal_id`,
  `importe_minor` (>0), `moneda` (=ARS), `estado_local` (10 estados),
  `estado_mp` (7 + desconocido), `venta_id` FK nullable, `caja_sesion_id` FK,
  `usuario_id` FK, `creada_en`, `actualizada_en`, `ultimo_error`.
  CHECK `(estado_local='confirmada') = (venta_id IS NOT NULL)`. Borrado prohibido por trigger.
- `mercado_pago_operacion_linea`: snapshot relacional inmutable (código, nombre,
  talle, color, cantidad, precio unitario, subtotal). Sin edición ni borrado.
- `mercado_pago_operacion_pago`: pago planificado inmutable
  (`medio` incluye `mercado_pago`, importe, recibido para efectivo).
- `mercado_pago_reembolso`: intención durable de refund (`order_id`, importe,
  `idempotency_key`, `reembolso_operacion_id`, estados locales, `estado_mp`).
- `pago`, `caja_movimiento`, `cambio`, `cambio_pago`, `pago_correccion`: V16 ya
  admite `medio='mercado_pago'` en sus CHECK.
- `auditoria`: eventos con `operacion_id`, actor, referencia y detalle.

### 2.3 Qué **sobrevive** al reinicio y qué es inmutable

- Sobreviven: la operación saga con su key/order/external_reference, sus líneas
  y pagos snapshot, el vínculo a la venta confirmada, el reembolso y toda la
  caja/venta/pago derivados (efectos de caja por pago).
- Inmutable: líneas y pagos planificados (triggers), operación no borrable,
  venta/pago/cambio/caja históricos; las correcciones se hacen por reverso, no
  por edición.

### 2.4 Cómo se vincula hoy order ↔ venta ↔ payment

- order ↔ operación: `mercado_pago_operacion.order_id` (y `external_reference`).
- operación ↔ venta: `mercado_pago_operacion.venta_id`, fijado en la confirmación
  local idempotente por `operacion_id` (`ConfirmarVenta` con el mismo id; si ya
  existe venta por esa operación, se vincula sin duplicar).
- venta ↔ pago: filas `pago(medio='mercado_pago')`.
- **`payment_id` (id de pago de MP) NO se persiste hoy.** `PagoOrdenMp.Id` se
  parsea en el cliente pero no se guarda en la base comercial.

### 2.5 Restricciones reales de SQLite/arquitectura

- SQLite sin `ALTER … DROP COLUMN`; los cambio de CHECK requieren **rebuild con
  copia íntegra + recrear triggers/índices** (patrón ya usado en V10/V12/V16).
- Sin backend público ⇒ sin webhooks; reconciliación por polling/reconciliación.
- Offline-first: la conectividad nunca condiciona ventas ni cierre.
- Decimales: importes en `INTEGER` de centavos (`*_minor`). El texto de MP usa
  punto decimal; el conversor ya existe (`ConvertidorMontosMp`).

---

## 3. Modos de Point (INTEGRADO / MANUAL / DESACTIVADO)

### 3.1 Dos ejes distintos que NO deben confundirse

- **Modo de la terminal (físico, remoto):** `operating_mode` = `PDV` (acepta
  órdenes por API) o `STANDALONE` (autónomo). Lo define Mercado Pago; se cambia
  solo con `PATCH /terminals/v1/setup` por acción explícita de Dueña.
- **Modo de LocalInfantil (operativo, local):** `INTEGRADO`, `MANUAL` o
  `DESACTIVADO`. Decide **cómo** usa la Dueña el Point desde el sistema.

Regla dura: LocalInfantil **nunca** cambia el modo físico sin acción explícita y
validada de Dueña. Un cambio de modo local no debe forzar un `PATCH` remoto.

### 3.2 Modelo de configuración y persistencia

Extender la configuración no secreta `Secrets/mercadopago.json` (fuera de la DB,
igual que hoy) con:

```json
{
  "terminalId": "…",
  "modoTerminal": "PDV",
  "modoLocalInfantil": "integrado | manual | desactivado",
  "sucursalId": "…",
  "posId": "…",
  "conectadoEn": "…",
  "ultimaComprobacionUtc": "…"
}
```

- **Modo inicial seguro:** `desactivado` hasta que la Dueña configure credencial,
  terminal y elija modalidad. Para el local ya operativo con F12, el valor
  inicial efectivo puede dejarse en `integrado`, pero el default de un sistema
  nuevo es `desactivado` (no ofrecer Point sin configurar).
- `modoLocalInfantil` se resuelve en Application, no en la UI (la UI solo refleja
  y solicita). Falta de permiso o modo `desactivado` deniegan por defecto.

### 3.3 Permisos

- `mercadopago.configurar` (ya existe, solo Dueña) cubre elegir/cambiar modalidad
  y configurar terminal.
- Cobrar en POS sigue con `ventas.confirmar`.
- **Nuevo:** un permiso financiero para conciliar/importar
  (`mercadopago.conciliar`, solo Dueña) y consultar liquidaciones.
- Cobro **manual** usa `ventas.confirmar` + una confirmación explícita del
  importe; no otorga capacidades financieras adicionales a la Empleada.

### 3.4 Cambio de modo seguro (sin operaciones pendientes)

Cambiar `integrado → manual`, `manual → integrado` o cualquiera → `desactivado`
solo se permite si **no hay** operaciones no terminales para la terminal
(`ListarNoTerminalesAsync` sin pendientes de esa sesión/terminal). Si hay un
cobro incierto, el cambio se bloquea con mensaje humano y se obliga a reconciliar
primero. El cambio se audita. Nunca se cambia el modo para "escapar" de un cobro
incierto (ver §4).

### 3.5 Comportamiento offline

- `integrado` sin internet: no se ofrece cobro Point; el resto de medios opera.
- `manual`: el cobro se registra localmente aunque no haya internet (la
  declaración es local); la conciliación queda pendiente hasta tener red/reporte.
- `desactivado`: Point no aparece.

### 3.6 Efecto sobre ventas nuevas

- `integrado`: venta 100% o combinada con MP por la saga, como hoy.
- `manual`: nueva ruta de cobro que registra venta + pago `mercado_pago` **y** un
  registro `mercado_pago_cobro_manual` en estado `pendiente_conciliacion`.
- `desactivado`: el POS no muestra Point; si existían ventas históricas con MP,
  se siguen viendo y conciliando, pero no se generan nuevas.

---

## 4. Protección contra doble cobro

Regla rectora: **un cobro remoto incierto NO habilita automáticamente la
modalidad manual para esa misma operación.** Primero se reconcilia o se resuelve
por el procedimiento autorizado. La saga F12 sigue siendo la única fuente de
verdad del cobro integrado.

| # | Escenario | Comportamiento exigido |
|---|---|---|
| 1 | Integrado **aprobado** (`processed`) | Confirmación local idempotente por `operacion_id`. Una sola venta. |
| 2 | Integrado **rechazado** (`failed`) | Operación `fallida`; no se crea venta; se puede reintentar con **nueva** key. |
| 3 | Integrado **cancelado** (`canceled`) | Operación `cancelada`; sin venta; liberado el pendiente de sesión. |
| 4 | Integrado **pendiente** (`created`/`at_terminal`) | `esperando_terminal`; bloquea nuevos cobros de esa terminal/sesión. |
| 5 | **Conexión perdida tras el POST** | `requiere_conciliacion`; se reintenta con la **misma** key (replay) o se consulta. Nunca segundo POST con key nueva. |
| 6 | Integrado **resultado incierto** | `requiere_conciliacion`; el cobro local no se confirma ni se marca fallido. |
| 7 | **Reinicio durante el cobro** | Al iniciar sesión, reconciliación de no terminales; banner si quedan pendientes. |
| 8 | Usuario intenta **pasar a manual** con cobro incierto | **Bloqueado** hasta reconciliar (§3.4). |
| 9 | Usuario intenta **registrar de nuevo** el cobro | Si la operación sigue no terminal, se bloquea `IniciarCobroPoint` ("Hay un cobro pendiente"). En manual, dedup por importe+fecha+terminal con confirmación humana, nunca automática. |

Complementos:

- Un mismo pago `mercado_pago` no puede generar dos efectos de caja
  (índices únicos de `caja_movimiento` ya lo garantizan).
- Un cobro manual no puede enlazarse a dos ventas distintas (§8).
- No se hace refund automático ni segundo cobro ante fallo local.

---

## 5. Seguridad y flujo del cobro manual

### 5.1 Flujo nominal

`Venta $30.000 → Point manual $30.000 → operador verifica APROBADO en el
dispositivo → registra el cobro aprobado en LocalInfantil → venta registrada →
conciliación pendiente.`

El importe registrado es **bruto**, nunca neto. La confirmación humana **no**
equivale a validación remota: el estado inicial es `pendiente_conciliacion`.

### 5.2 Datos obligatorios razonables

- Importe cobrado (debe coincidir exactamente con el importe que se imputa a
  `mercado_pago` en la venta; comparación explícita antes de confirmar).
- Fecha/hora del cobro (por defecto ahora; editable con motivo si se registra
  diferido).
- Terminal/pos (de la configuración) — al menos el `terminal_id`/`pos_id`
  configurado si existe.
- Referencia del comprobante **cuando exista** (número de operación / últimos 4 +
  autorización del ticket del Point). **Opcional pero recomendada**; su presencia
  mejora la conciliación exacta.

### 5.3 Validaciones

- Importe > 0 y entero en centavos; coincide con el imputado.
- Caja abierta (la venta F04/F05 lo exige igual).
- Una sola fila MP por venta (igual que integrado).
- No fiado combinado con Point (regla vigente).
- Si el operador declara "comprobante disponible", se pide la referencia; si no,
  se marca explícitamente "sin comprobante".

### 5.4 Idempotencia y duplicados

- `mercado_pago_cobro_manual.operacion_id` UNIQUE para idempotencia de la venta
  (aunque lleguen dos confirmaciones simultáneas, el índice único deja una sola).
- Detección de duplicados manuales por ventana de tiempo + importe + terminal:
  si aparece un candidato ya declarado, advertir y exigir confirmación humana
  explícita ("¿es una segunda venta del mismo importe o un duplicado?"). Nunca
  auto-descartar ni auto-vincular. La ventana **no excluye cobros ya
  conciliados**: la conciliación del primer cobro no dice nada sobre el intento
  nuevo, que puede ser el segundo de un duplicado.
- Una referencia de comprobante declarada no puede reutilizarse en otra
  operación (un comprobante = un cobro físico): se rechaza de forma
  determinista, incluso con la confirmación de duplicado por importe.
- Reimportación del mismo CSV no duplica filas (§7).

### 5.5 Corrección de errores

- Un cobro manual no se edita ni se borra. Se corrige con un **registro
  compensatorio** auditado (anulación del cobro manual declarado) que:
  - no borra la venta ni el pago;
  - si corresponde, sigue el flujo F04/F06 de anulación/devolución vigente;
  - deja rastro en `auditoria`.

### 5.6 Vínculo con devolución y reintegro

- Un cobro manual sin identidad remota **no** habilita refund por API (§13).
- La devolución de una venta manual se registra localmente y la liquidación del
  refund se busca luego en el reporte (fila `refund`), nunca se asume.

---

## 6. Reportes oficiales de Mercado Pago (investigación)

Fuente: documentación oficial vigente
([campos del reporte](https://www.mercadopago.com.ar/developers/es/docs/reports/released-money/report-fields),
[generación por API](https://www.mercadopago.com.ar/developers/es/docs/reports/released-money/api)).

### 6.1 Reporte de Liquidaciones ("dinero liberado")

Afecta el saldo disponible de la cuenta. Endpoints (requieren el access token de
la cuenta; el token propio de F12 sirve si tiene alcance):

| Operación | Método y ruta |
|---|---|
| Configurar | `POST /v1/account/release_report/config` |
| Consultar config | `GET /v1/account/release_report/config` |
| Actualizar config | `PUT /v1/account/release_report/config` |
| **Generar (on demand)** | `POST /v1/account/release_report` (body `begin_date`, `end_date`; responde 202 Accepted y luego 203 si no se pudo) |
| **Listar generados** | `GET /v1/account/release_report/list` |
| **Descargar** | `GET /v1/account/release_report/{file_name}` (CSV) |

Campos confirmados y su uso (no se asume que todos los campos estén siempre;
algunos pueden venir vacíos):

| Columna | Uso en LocalInfantil |
|---|---|
| `DATE` | Fecha de liquidación (afecta saldo). |
| `SOURCE_ID` | Id de transacción/pago en MP (join candidato). |
| `EXTERNAL_REFERENCE` | El `LI-<operacionId>` que **nosotros** fijamos en integrado; puede venir **vacío** (facturas, transferencias, y potencialmente Point manual). |
| `RECORD_TYPE` | `initial_available_balance`, `release`, `total`, `available_balance`. |
| `DESCRIPTION` | `payment`, `refund`, `chargeback`, `tax_payment_*`, `tax_withdholding*`, `payout`, `shipping*`, `asset_management_*`, etc. |
| `NET_CREDIT_AMOUNT` / `NET_DEBIT_AMOUNT` | Neto acreditado/debitado. |
| `GROSS_AMOUNT` | Bruto de la operación. |
| `MP_FEE_AMOUNT` | Comisión MP/ML (incluye IVA). |
| `FINANCING_FEE_AMOUNT` | Costo cuotas sin interés. |
| `SHIPPING_FEE_AMOUNT`, `COUPON_AMOUNT`, `EFFECTIVE_COUPON_AMOUNT` | No aplican al local (sin envíos/cupones), pero se conservan. |
| `TAXES_AMOUNT` (+ `TAX_DETAIL`) | Retenciones/percepciones IIBB, IVA, créditos/débitos. |
| `INSTALLMENTS` | Cantidad de cuotas. |
| `PAYMENT_METHOD` / `PAYMENT_METHOD_TYPE` | `visa`/`master`/`account_money`, `credit_card`/`debit_card`/… |
| `POS_ID` / `POS_NAME` / `EXTERNAL_POS_ID` | Identifica caja física. |
| `STORE_ID` / `EXTERNAL_STORE_ID` | Identifica sucursal. |
| `ORDER_ID` / `ORDER_MP` | Id de la orden (MP). |
| `POI_ID` | Número de serie del lector (Point). |
| `FRANCHISE`, `LAST_FOUR_DIGITS`, `CARD_INITIAL_NUMBER` | Metadata de tarjeta no sensible. |
| `OPERATION_TAGS` (JSON) | Pago vía Point se etiqueta `PO`. |
| `METADATA` (JSON) | Datos extra (incluye ids de devoluciones parciales). |
| `CURRENCY` | `ARS`. |

### 6.2 Reporte "Todas las transacciones" (account-money)

Complementa: incluye operaciones que **afectan el saldo** sin ser liquidaciones y
no incluye las futuras. Disponible por el mismo mecanismo. Sirve como fuente de
apoyo, no como verdad de liquidación.

### 6.3 Vigencia / alcance / permisos

- La cuenta y el token deben tener acceso a estas APIs; **no se assume** que el
  token de F12 (pensado para Point) tenga alcance de reportes: se verifica en la
  etapa D antes de automatizar.
- Los nombres de columnas dependen del idioma configurado (`report_translation`);
  la importación debe ser tolerante a mayúsculas y a `report_translation=es`
  (encabezados traducidos) — se recomienda fijar `report_translation=en` o
  mapear por `key` técnica, no por texto visible.

---

## 7. Importación CSV (etapa inicial, sin API)

Objetivo: admitir un reporte oficial descargado por la Dueña desde el panel de
Mercado Pago, sin depender todavía de la API de reportes.

### 7.1 Requisitos

- Lectura del CSV oficial con separador y **encoding** detectados; soporte de
  comillas y saltos internos.
- **Validación de columnas** por nombre técnico; si falta una columna obligatoria,
  se rechaza la importación con mensaje claro.
- Control de **formato/versión**: se registra el conjunto de columnas y un
  `formato_reporte` para poder cambiar el parser sin romper histórico.
- **Importes decimales exactos** → convertidos a centavos con validación de
  máximo 2 decimales; sin `double`.
- Soporte `ARS`; otras monedas se rechazan o marcan.
- **Fechas y zonas**: `DATE`/`TRANSACTION_DATE` llegan con offset; se normalizan a
  UTC conservando el offset original del archivo para trazabilidad.
- **Origen**: se registra archivo, hash SHA-256, cuenta, rango y usuario.
- **Identificación del reporte** importado (una fila `mercado_pago_importacion`).
- **Evitar reimportación duplicada**: la clave única de dedup por fila (§14) y el
  hash de archivo impiden duplicar movimientos.
- **Errores por fila**: no abortan todo el archivo; se listan y se conservan.
- **Validación antes de aplicar** (dry-run) y **resultado revisable**.

### 7.2 Distinción de filas (`RECORD_TYPE` + `DESCRIPTION`)

| Origen reporte | Tratamiento |
|---|---|
| `payment` (`release`) | Cobro acreditado; candidato a conciliar con venta/pago. |
| `refund` | Devolución; se asocia a la venta/pago originante si existe; si no, queda como movimiento externo. |
| `chargeback` | Contracargo; nunca se cuenta como venta; requiere revisión. |
| `MP_FEE_AMOUNT`/comisiones | Se conservan como análisis/trazabilidad, **sin** restarlas del precio de venta. |
| `tax_payment_*`, `tax_withdholding*` (retenciones/impuestos) | Se registran por separado en `TAXES_AMOUNT`; no son pérdida definitiva del comercio. |
| `payout` / `pre_payout_*` / `pos_payout_*` (transferencia/retiro) | **Nunca** se duplican como gasto ni como nueva venta. |
| `initial_available_balance` / `total` / `available_balance` | Saldos de control; no son ventas. |
| `asset_management_*`, `credit_payment`, `reserve_for_*`, `shipping*` | Movimientos externos/otros; se conservan sin afectar ventas. |

**No se asume que todas las filas del reporte sean ventas de LocalInfantil.**

---

## 8. Conciliación automática y asistida

### 8.1 Vínculos confiables según el esquema real

| Clave | Confiable cuando | Nota |
|---|---|---|
| `EXTERNAL_REFERENCE` = `LI-<operacionId>` | Cobros **integrados** (nosotros la fijamos) | La más fuerte; pero MP advierte que puede venir vacía en varios casos. |
| `ORDER_MP` / `ORDER_ID` ↔ `mercado_pago_operacion.order_id` | Integrado (tenemos la order) | Buena para integrado. |
| `SOURCE_ID` ↔ `PagoOrdenMp.Id` | Solo si **persistimos** el `payment_id` | Hoy no se persiste; ver esquema V17. |
| importe + fecha + terminal | Apoyo | Jamás conciliación automática por parecido. |

No se asume equivalencia entre `SOURCE_ID`, `ORDER_ID`, `ORDER_MP` ni
`EXTERNAL_REFERENCE`: son identificadores distintos.

### 8.2 Integrado (automático cuando hay evidencia)

- Si `EXTERNAL_REFERENCE` matchea `LI-<operacionId>` → asociación exacta.
- Si no, `SOURCE_ID`/`ORDER_MP` contra `payment_id`/`order_id` persistidos.
- Si ninguna clave está presente → queda `no_relacionado` (no se adivina).

### 8.3 Manual (asistido)

- El reporte puede no traer `external_reference` de LocalInfantil.
- Estrategia: vinculación exacta si existe identificador fiable (número de
  comprobante capturado); si no, **búsqueda de candidatos por importe y ventana de
  fecha como AYUDA**; **confirmación humana obligatoria** de coincidencias
  ambiguas; estado `no_relacionado` si falta evidencia.
- Nunca se concilia automáticamente solo porque importe y fecha se parecen.
- Impedir que una transacción externa se vincule a dos ventas distintas: la fila
  de liquidación tiene una única referencia de venta/pago (UNIQUE por línea) y la
  asociación manual es explícita y auditada.

### 8.4 Estados de conciliación

`pendiente`, `conciliada`, `con_diferencia`, `no_relacionada`, `requiere_intervencion`.

---

## 9. Importes y contabilidad

Cifras **separadas**, sin mezclar:

1. Importe comercial de la venta (`venta.total_minor`).
2. Cobro bruto por medio (`pago.importe_minor`, `medio='mercado_pago'`).
3. Comisiones de procesamiento (`MP_FEE_AMOUNT`).
4. Costo financiero del comercio (`FINANCING_FEE_AMOUNT`, cuotas sin interés).
5. Retenciones/impuestos (`TAXES_AMOUNT`, `TAX_DETAIL`).
6. Neto liquidado (`NET_CREDIT_AMOUNT` − `NET_DEBIT_AMOUNT`).
7. Fecha de disponibilidad (`DATE` de liquidación).
8. Transferencias bancarias / retiros (`payout`), si se contemplan.

Reglas:

- **No** se modifica el precio original de las prendas.
- **No** se reduce el importe de venta por la comisión.
- **No** se considera la retención como pérdida definitiva automática.
- **No** se confunde saldo disponible en Mercado Pago con acreditación bancaria.
- **No** se suman dos veces movimientos.
- Sin liquidación real → se muestra **"Pendiente de liquidación"**. No se inventan
  comisiones por porcentajes supuestos.
- La ganancia real **no** es "venta − comisión": dependen costos y otros gastos
  (D-07 sigue pendiente).

---

## 10. Cuotas y tarjetas

- Auditar en `GET /v1/orders` de una operación real qué metadata viene: tipo de
  instrumento, marca, cantidad de cuotas, `payment_id`. (Pendiente F12-T07 con
  terminal real.)
- Persistir como **snapshot no sensible** lo útil: `installments`,
  `payment_method`/`payment_method_type`, franquicia, últimos 4 dígitos.
- **Nunca** guardar PAN, CVV ni datos sensibles de tarjeta.
- La metadata de cuotas **no** modifica automáticamente el precio de venta.
- **No** se crea selector de cuotas en el POS en esta etapa.

---

## 11. Impacto en Caja (F05, sin cambiar sus reglas)

Se conserva la separación actual:

- arqueo de **efectivo**;
- cobros por **Point** (no efectivos);
- liquidaciones;
- transferencias posteriores.

Ejemplo: Venta efectivo $20.000 + Venta Point $30.000 → Total ventas $50.000 /
Efectivo $20.000 / Point bruto $30.000. Si MP liquida neto $28.500, **no** se crea
una diferencia de caja de $1.500: se explica en conciliación MP. `mercado_pago`
sigue siendo medio no efectivo; no suma al efectivo esperado del arqueo.

---

## 12. Impacto en Reportes (F09)

Revisar para evitar:

- comisiones restadas dos veces;
- cobros sumados dos veces;
- devoluciones contadas incorrectamente;
- transferencias/retiros contados como ventas;
- confundir neto acreditado con ganancia.

Hoy F09 ya incluye `mercado_pago` en el desglose de medios sin alterar
brutas/netas/unidades y **no** informa comisiones ni netos (no se importan
liquidaciones). El diseño agrega, como consultas futuras reutilizables (sin
reescribir períodos históricos):

- Cobros brutos por medio (ya existe).
- **Liquidado neto** por período (solo si hay importación; si no, vacío).
- **Comisiones y retenciones** como partidas separadas.
- **Pendiente de conciliar** (monto y cantidad).

---

## 13. Impacto en Devoluciones (F06/F12)

Diferenciar:

- pago Point **integrado** con identidad remota segura (order_id propio);
- pago Point **manual** todavía no relacionado;
- pago manual relacionado posteriormente;
- refund **confirmado**, **incierto**, **rechazado**.

Reglas:

- **No** se autoriza automáticamente un refund por API si no existe identidad
  remota segura (order_id propio). Para manual sin identidad, el refund se
  gestiona fuera y la liquidación se busca luego en el reporte.
- **No** se duplican devoluciones.
- **No** se cambian las reglas F06/F12 vigentes en esta tarea.

---

## 14. Esquema mínimo (V17)

> **Estado Etapa A:** `14.1 mercado_pago_cobro_manual` **está escrita** (migración
> V17 `mercado-pago-cobro-manual-f12`, con triggers). Las tablas `14.2`–`14.4`
> (`importacion`/`liquidacion`/`conciliacion`) siguen **solo propuestas** para la
> etapa de conciliación.

Prioridad: reutilizar lo existente. V16 permanece intacta. La **V17
aditiva** contempla 4 tablas sin tocar columnas históricas; en Etapa A solo se
crea la primera. Alternativa: las tres
primeras bastan si se decide no persistir el cobro manual por separado (menos
trazabilidad). Se recomiendan las cuatro.

### 14.1 `mercado_pago_cobro_manual` (declaración humana)

| Columna | Tipo / constraint | Justificación |
|---|---|---|
| `id` | TEXT PK | Id interno. |
| `operacion_id` | TEXT NOT NULL UNIQUE | Idempotencia con `ConfirmarVenta`; evita doble venta por reintento. |
| `venta_id` | TEXT NOT NULL FK → `venta` RESTRICT | Venta generada. |
| `pago_id` | TEXT NOT NULL FK → `pago` RESTRICT | Pago `mercado_pago` asociado. |
| `importe_minor` | INTEGER NOT NULL > 0 | Bruto declarado (coincide con el pago). |
| `comprobante_ref` | TEXT NULL | Nº de operación/autorización del ticket; habilita match exacto. |
| `terminal_id` | TEXT NULL | Terminal usada si se conoce. |
| `pos_id` | TEXT NULL | Caja física si se conoce. |
| `declarado_en` | TEXT NOT NULL | Fecha/hora del cobro declarado. |
| `estado_conciliacion` | TEXT NOT NULL CHECK IN ('pendiente','conciliada','con_diferencia','no_relacionada','requiere_intervencion') | Seguimiento humano. |
| `usuario_id` | TEXT NOT NULL FK → `usuario` RESTRICT | Auditoría. |
| `caja_sesion_id` | TEXT NOT NULL FK → `caja_sesion` RESTRICT | Coherencia con caja. |
| `creado_en`, `actualizada_en` | TEXT NOT NULL | Trazabilidad. |

Triggers: no editar `importe_minor`/`venta_id`/`pago_id`; no borrar (histórico).

### 14.2 `mercado_pago_importacion` (provenance del reporte)

| Columna | Tipo / constraint | Justificación |
|---|---|---|
| `id` | TEXT PK | Id. |
| `origen` | TEXT NOT NULL CHECK IN ('csv_manual','api') | Cómo se obtuvo. |
| `archivo_nombre` | TEXT NULL | Nombre del CSV. |
| `archivo_hash` | TEXT NULL | SHA-256; evita reimportar el mismo archivo. |
| `cuenta_mp` | TEXT NULL | Cuenta de la que proviene. |
| `formato_reporte` | TEXT NULL | Versión del set de columnas. |
| `zona_horaria` | TEXT NULL | `display_timezone` del reporte. |
| `rango_desde`, `rango_hasta` | TEXT NULL | cobertura declarada. |
| `filas_totales`, `filas_aplicadas`, `filas_error` | INTEGER NOT NULL DEFAULT 0 | Resultado. |
| `estado` | TEXT NOT NULL CHECK IN ('validado','aplicado','rechazado','parcial') | Control. |
| `usuario_id` | TEXT NULL FK → `usuario` RESTRICT | Quién importó. |
| `creado_en` | TEXT NOT NULL | Fecha. |

Índice UNIQUE parcial sobre `archivo_hash` (WHERE `archivo_hash IS NOT NULL`).

### 14.3 `mercado_pago_liquidacion` (fila del reporte, autocontenida)

Campos principales (todos nullable salvo los de identidad), distinguiendo
**desconocido (NULL)** de **cero (0)**:

- Identidad/dedup: `id` PK; `importacion_id` FK; **`fila_ordinal`** (posición
  de la fila de datos, base 1); **`identidad_remota`** (clave confiable del
  proveedor, NULL si no aplica); `source_id`; `record_type`;
  `description`; `transaction_date`; `date` (liquidación); `currency`.
- Referencias: `external_reference`; `order_mp`; `order_id`.
- Importes (`*_minor` INTEGER): `gross`, `net_credit`, `net_debit`, `mp_fee`,
  `financing_fee`, `shipping_fee`, `taxes`, `coupon`, `effective_coupon`.
- Contexto: `installments`; `payment_method`; `payment_method_type`; `pos_id`;
  `poi_id`; `store_id`; `operation_tags` (JSON); `metadata_json` (JSON).
- Control: `estado_conciliacion`; `venta_id` FK NULL; `pago_id` FK NULL;
  `operacion_mp_id` FK NULL; `cobro_manual_id` FK NULL; `observacion`;
  `creado_en`.

**Modelo de identidad (V19), en cuatro planos separados:**

- **Archivo:** `mercado_pago_importacion.archivo_hash` UNIQUE (índice parcial) →
  reimportar el mismo archivo se bloquea.
- **Fila importada:** `UNIQUE(importacion_id, fila_ordinal)`. Dos filas
  idénticas dentro del mismo archivo se conservan como dos filas.
- **Huella de contenido:** `fila_hash` (SHA-256 de la fila) **no es única**;
  solo sirve para **marcar posibles duplicados** entre reportes, nunca para
  descartar una fila.
- **Identidad de operación financiera:** `identidad_remota`, clave normalizada
  del **movimiento** aportada por el proveedor. Se deriva del identificador
  propio del movimiento (`SOURCE_ID`): cobro → `"C:"+SOURCE_ID`;
  devolución/contracargo → `"R:"+SOURCE_ID`; el resto (impuesto, saldo,
  transferencia, desconocido) y las filas sin `SOURCE_ID` → NULL.
  **Nunca** se usa `ORDER_ID` (orden comercial) ni `EXTERNAL_REFERENCE`
  (referencia de la venta) como identidad: pueden repetirse en varios
  movimientos de una misma venta/orden (pagos parciales, comisión, costo
  financiero, retenciones, reembolsos parciales, contracargos) y colapsarían
  movimientos distintos, borrando dinero legítimo.

**Deduplicación:** entre reportes solapados se omite una fila **solo** con
`identidad_remota` coincidente (mismo movimiento financiero). Una fila ambigua
(sin identidad remota) cuyo contenido ya aparecía en otro reporte **se
conserva** y queda en `requiere_intervencion` (posible duplicado), sin
conciliación automática ni doble conteo. No se descarta ninguna fila por
coincidencia de importe/fecha/descripción.

Índices: por `external_reference`, `source_id`, `order_mp`, `estado_conciliacion`,
`date`, `fila_hash`, `identidad_remota`.

### 14.4 `mercado_pago_conciliacion` (enlace explícito y auditado)

| Columna | Constraint | Justificación |
|---|---|---|
| `id` | TEXT PK | Id. |
| `liquidacion_id` | TEXT NOT NULL UNIQUE FK | Una fila de reporte se ata a lo sumo una vez. |
| `venta_id` | TEXT NULL FK | |
| `pago_id` | TEXT NULL FK | |
| `operacion_mp_id` | TEXT NULL FK | |
| `cobro_manual_id` | TEXT NULL FK | |
| `tipo_match` | TEXT CHECK IN ('exacto_external_ref','exacto_payment_id','exacto_order','asistido_humano') | Explicita el grado de certeza. |
| `confirmado_por_usuario_id` | TEXT NULL FK | Obligatorio para `asistido_humano`. |
| `creado_en` | TEXT NOT NULL | |

Regla: al menos uno de los FK de origen no nulo; `asistido_humano` exige
`confirmado_por_usuario_id`. Un `pago_id`/`operacion_mp_id` puede recibir varias
filas de liquidación (pago + comisión + retención); **una venta distinta no
puede** recibir la misma fila.

### 14.5 Compatibilidad y protección contra doble conteo

- V17 no altera ventas/pagos/caja/listas históricas.
- La dedup por fila + hash de archivo impide importar dos veces.
- Los estados de conciliación son de reporte; **no** reescriben F09 ni ventas.
- Distinción explícita unknown vs zero.

---

## 15. Experiencia de usuario (compacta)

### 15.1 Administración → Mercado Pago

Pestañas:

- **Configuración**: credencial (ya), terminal (ya), **Modalidad** (Integrado /
  Manual / Desactivado) con explicación de una línea y advertencia al cambiar si
  hay pendientes; botón "Probar conexión".
- **Conciliación**: lista de operaciones **pendientes / conciliadas / con
  diferencias / no relacionadas / problemas**; botones "Importar CSV" y, más
  adelante, "Descargar de Mercado Pago".

### 15.2 POS — medios

`Efectivo | Transferencia | Mercado Pago Point`. Otros medios históricos/manuales
solo si corresponden.

- **Point manual**: mostrar importe y pasos claros ("1) Cobrá $X en el Point.
  2) Verificá APROBADO. 3) Registrá el cobro."), campo de importe y referencia
  opcional, advertencia "quedará pendiente de conciliación".
- **Point integrado**: acción "Enviar cobro" y estados reales
  (preparando/en terminal/aprobado/rechazado/pendiente de verificar).

### 15.3 Conciliación

- La **Dueña** administra liquidaciones; la **Empleada** realiza cobros sin ver
  contabilidad financiera innecesaria.
- Sin estadísticas ficticias: si no hay datos, se muestra vacío honesto.

---

## 16. Plan de implementación por etapas

Cada etapa: alcance, archivos, migración, dependencias, riesgos, tests,
aceptación, bloqueos.

### Etapa A — Modo Point + POS manual/integrado

- **Alcance:** campo `modoLocalInfantil` en config; selector en Configuración;
  flujo de cobro manual (venta + pago + `mercado_pago_cobro_manual`); gating del
  POS por modo.
- **Archivos:** `ConfigurarMercadoPago`, `IniciarCobroPoint`/nuevo
  `RegistrarCobroManualPoint`, `IniciarCobroManualPoint` (caso de uso),
  `PosViewModel`, `MercadoPagoViewModel`/vista, `AlmacenConfigMercadoPago`.
- **Migración:** V17 (tabla `mercado_pago_cobro_manual`) — solo si la etapa C no
  la aporta antes; puede adelantarse.
- **Dependencias:** D-49; decisión de la Dueña sobre modalidad (puede operar en
  manual de inmediato).
- **Riesgos:** doble declaración humana; mitigado por dedup + confirmación.
- **Aceptación:** en `--dev-db`, cobro manual registra venta/pago/cobro y queda
  pendiente; cambiar a desactivado oculta Point; bloqueo de cambio con incierto.

### Etapa B — Metadata y referencias del cobro

- **Alcance:** capturar y persistir `payment_id`, comprobante, terminal/pos,
  metadata de tarjeta/cuotas no sensible; snapshot en integrado.
- **Archivos:** `PagoOrdenMp`, cliente MP, repositorio MP, migración si hace
  falta columna.
- **Bloqueo externo:** confirmar con terminal real (F12-T07) qué devuelve
  `GET /v1/orders`.

### Etapa C — Importación CSV oficial + conciliación

- **Alcance:** importar CSV, validar, dedup, conciliar integrado automático y
  manual asistido, pantalla de conciliación.
- **Archivos:** `ImportarLiquidacionMp`, `ConciliarLiquidaciones`, parser CSV,
  V17 (`mercado_pago_importacion`, `mercado_pago_liquidacion`,
  `mercado_pago_conciliacion`).
- **Dependencias:** un CSV real de la cuenta (aunque sea de prueba).
- **Riesgos:** variaciones de columnas/idioma; mitigación con `key` técnica y
  `formato_reporte`.

### Etapa D — Descarga automatizada por API

- **Alcance:** generar/descargar el reporte por API (endpoints §6.1), programar
  opcionalmente.
- **Bloqueo externo:** validar que el token/cuenta tenga alcance de reportes;
  no asumirlo.

---

## 17. Pruebas de aceptación futuras (fixtures falsos, sin credenciales reales)

| # | Fixture | Resultado esperado |
|---|---|---|
| 1 | Venta 30.000 / bruto MP 30.000 / comisión 1.500 / neto 28.500 | Cobro 30.000; comisión y neto separados; venta NO se reduce. |
| 2 | Venta 30.000 sin liquidación | "Pendiente de liquidación"; venta/cobro intactos. |
| 3 | Point integrado aprobado | Una sola venta; conciliación automática por `LI-<op>`. |
| 4 | Point manual confirmado humanamente | Venta + `pendiente_conciliacion`; no "conciliado". |
| 5 | Reimportación del mismo CSV | 0 filas nuevas (dedup global + hash). |
| 6 | Dos pagos igual importe y fecha | No se auto-concilia; candidatos + confirmación humana. |
| 7 | Pago que no corresponde al local | `no_relacionada`; no crea venta. |
| 8 | Devolución/reembolso | Fila `refund` asociada al pago originante; no duplica. |
| 9 | Contracargo | `chargeback` no cuenta como venta; revisión. |
| 10 | Impuestos/retenciones separados | `TAXES_AMOUNT` en línea propia; no pérdida automática. |
| 11 | Reporte con filas de saldo/retiros | `initial_available_balance`/`total`/`payout` no son ventas ni gastos. |
| 12 | Operación Point incierta | `requiere_conciliacion`; manual bloqueado para esa operación. |
| 13 | Pago mixto efectivo + Point | Efectivo en arqueo; Point en desglose; total correcto. |
| 14 | Diferencia real inexplicable | Estado `con_diferencia`; no se inventa ajuste. |

---

## 18. Decisiones pendientes, riesgos y bloqueos

- **Confirmar con la Dueña** qué modalidad usará (integrado/manual/desactivado) y
  si se admite introducir el nº de comprobante del Point.
- **Confirmar** el alcance del token para reportes (`/v1/account/release_report*`).
- **Definir** una clave de dedup exacta con un CSV real (Etapa C).
- **F12-T07** (terminal y credencial reales) bloquea la metadata real (Etapa B).
- Riesgo de filas `EXTERNAL_REFERENCE` vacías en manual → conciliación asistida.
- D-01/D-07, validación de PC real y producción siguen pendientes ajenos a este
  diseño.

---

## 19. Fuera de alcance de esta especificación

- Implementación de código, migraciones V17, tests nuevos.
- Cambios a reglas F04/F05/F06/F09/F12.
- Webhooks, backend público, `client_secret` en el desktop.
- Selector de cuotas en POS, facturación fiscal, multi-procesador.
