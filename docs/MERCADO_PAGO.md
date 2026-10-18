# Mercado Pago Point (F12) — integración propia inicial

> Estado: implementación técnica en `feat/f12-mercado-pago`, sin commit/push.
> Point/cuenta real pendientes (F12-T07). Producción NO habilitada.

## 1. Qué integra y qué no

- Cobro con terminal Point física en modo **PDV** (integrado) vía **Orders API** (`POST /v1/orders`, `type: "point"`).
- La compatibilidad se determina dinámicamente por `operating_mode` devuelto por `GET /terminals/v1/list`. **No hay modelos hardcodeados** (`NEWLAND`/`PAX`/etc. no aparecen en el código): un Point futuro compatible funciona sin modificar LocalInfantil.
- No usa SDK de Mercado Pago ni Payment Intents legacy: HttpClient + System.Text.Json directo.
- No hay webhooks (desktop sin backend público): order activa con polling temporal controlado + reconciliación puntual al arrancar.

## 2. Endpoints utilizados

| Operación | Método y ruta | Notas |
|---|---|---|
| Listar terminales | `GET /terminals/v1/list?limit&offset` | Paginado (50/pág). Campos: `id`, `pos_id`, `store_id`, `external_pos_id`, `operating_mode` |
| Activar PDV | `PATCH /terminals/v1/setup` | Solo acción explícita de Dueña. `terminal_not_allowed_action` → mensaje humano |
| Crear order | `POST /v1/orders` | `type:"point"`, `external_reference:"LI-<op>"` (<=64, sin PII), `expiration_time:"PT15M"`, `transactions.payments[0].amount` (solo el importe MP), `config.point.{terminal_id, print_on_terminal:"no_ticket"}`. Siempre `X-Idempotency-Key` |
| Consultar order | `GET /v1/orders/{id}` | Estados: `created`, `at_terminal`, `processed`, `failed`, `refunded`, `expired`, `canceled` |
| Cancelar order | `POST /v1/orders/{id}/cancel` | Header `x-allow-cancelable-status: at_terminal` si está en terminal |
| Reembolsar | `POST /v1/orders/{id}/refund` | `{amount}` parcial o `{}` total |

Errores mapeados a mensajes humanos sin secretos: 401 credencial inválida, 403 permiso/terminal ajena, 404, 409 terminal ocupada / key reutilizada, 429 rate-limit, 5xx no concluyente, timeout, offline, JSON inválido, 400/412 genérico seguro (nunca se asume éxito ni rechazo definitivo).

## 3. Saga durable e idempotencia

Antes del POST existe fila en `mercado_pago_operacion` con `idempotency_key`, `external_reference`, snapshot relacional de líneas (`mercado_pago_operacion_linea`) y pagos planificados (`mercado_pago_operacion_pago`): sin descontar stock, sin cobro, sin movimiento de caja.

Estados locales: `preparada`, `creando_order`, `esperando_terminal`, `procesada_remota`, `confirmando_local`, `confirmada`, `fallida`, `cancelada`, `expirada`, `requiere_conciliacion` (transiciones validadas en `TransicionesOperacionMp`; estado remoto desconocido → conciliación, nunca éxito/fallo automático).

**Timeout ≠ rechazo**: ante resultado incierto no se crea otra order; se reintenta con la misma key (replay oficial) o se consulta. Solo `processed` habilita la confirmación local idempotente (`ConfirmarVenta` con el mismo `operacion_id`; si ya existe por `ExistePorOperacion`, se vincula). Fallo local tras MP → `requiere_conciliacion`, sin refund automático ni segundo cobro.

## 4. POS, pagos combinados y Caja

Medio `mercado_pago` (no manual directo: `MediosPago.Manuales` excluye MP; POS exige el flujo saga). Combinados permitidos: Σ medios == total (ej. efectivo + MP; MP + transferencia). Cobro 100% MP y combinados verificados. Fiado + MP bloqueado.

`mercado_pago` es medio **no efectivo**: no suma al efectivo esperado del arqueo; Caja muestra columna propia e histórico separado. Cierre bloqueado con operaciones pendientes en la sesión. Reportes F09 incluyen `mercado_pago` en el desglose sin alterar brutas/netas/unidades; no se informan comisiones ni netos acreditados (no se importan liquidaciones).

## 5. Cancelación, reembolsos y F06

Cancelar solo pre-`processed`, contra la `order_id` de la saga propia (nunca órdenes ajenas); sin respuesta confiable no se marca local. Reembolso total/parcial con intención durable previa (`mercado_pago_reembolso`) y misma key ante timeout; el flujo F06 exige cobertura (cobrado MP no reembolsado) y ejecuta el refund remoto en paso explícito visible (crédito reconocido → reembolso confirmado), nunca automático silencioso. Venta con pago MP no se puede anular localmente: redirige a devolución.

## 6. Credencial y configuración

- Access Token propio: DPAPI CurrentUser en `Secrets/mercadopago_token.bin` (nunca SQLite/plano/repo/logs/installer). Interfaz `IMercadoPagoCredencialProvider` preparada para un futuro `OAuthCredencialProvider` sin cambiar el flujo.
- Config no secreta (terminal, modo, sucursal): `Secrets/mercadopago.json`, fuera de la DB comercial.
- Permiso `mercadopago.configurar`: solo Dueña (matriz + enforcement en casos de uso). Cobrar usa `ventas.confirmar`; refunds usan permisos financieros existentes.
- `--dev-db` usa `Secrets/` junto a la DB temporal: nunca toca la credencial productiva; por defecto MP real deshabilitado en dev.

## 7. Recuperación y degradación

Al iniciar sesión se reconcilian operaciones y reembolsos no terminales (banner si quedan pendientes de verificar). Sin internet y sin cobro incierto: MP indisponible, el resto opera (efectivo/transferencia/otros).

## 8. OAuth futuro y límites

Para operar el local con credencial propia alcanza DPAPI. Para vender LocalInfantil a terceros, Mercado Pago exige Authorization Code con redirect URI y credenciales de integrador: el `client_secret` comercial **no** va dentro del desktop. Ver D-48.

## 9. Pendiente para Point real (F12-T07)

Cuenta y credencial de prueba/producción, terminal física en PDV, `LOCALINFANTIL_MP_TEST_ACCESS_TOKEN` para live tests, recorrido manual con dinero de prueba, PC del local. Producción NO habilitada.

## 10. Ampliación de Point: modos, cobro manual y conciliación de liquidaciones

> **Estado: Etapa A implementada** (modos de Point + cobro manual + V17 acotada) y **etapa de conciliación implementada** (importación CSV manual + V18 corregida por V19 + conciliación automática/asistida). La descarga automática por API sigue **solo diseñada**. Ver `D-49`/`D-50` y la especificación completa `docs/LIQUIDACIONES_MERCADO_PAGO.md`.

- **Modos:** además del modo físico de la terminal (`operating_mode` PDV/STANDALONE, remoto, solo Dueña), hay un **modo operativo local** `integrado` | `manual` | `desactivado` (default efectivo `desactivado` sin configuración previa; `integrado` si ya había terminal), en la config no secreta. INTEGRADO usa la saga F12 actual; MANUAL cobra en el Point sin API y registra una declaración `pendiente_conciliacion`. *(implementado)*
- **Anti-doble-cobro:** un cobro incierto (`requiere_conciliacion`) no habilita manual ni duplica el cobro; el cambio de modo se bloquea con pendientes. Timeout ≠ rechazo. *(implementado)*
- **Cobro manual:** venta + pago `mercado_pago` + `mercado_pago_cobro_manual` atómicos (misma transacción que `ConfirmarVenta`), importe bruto, unicidad por operación, sin refund remoto. *(implementado)*
- **Liquidaciones:** conciliación contra el reporte oficial de dinero liberado (`POST /v1/account/release_report`, `GET .../list`, `GET .../{file_name}`), empezando por **importación CSV manual**. Contabilidad separada (bruto/comisión/costo financiero/retenciones/neto/fecha de liquidación) para no mezclar ni contar dos veces. *(implementado: `LectorReporteLiquidacionMp` + V18 `mercado_pago_importacion`/`liquidacion`; dedup por identidad de fila/operación (V19), no por contenido; fixtures ficticios, pendiente validar CSV real)*
- **Conciliación:** automática para integrado por `LI-<operacionId>`/order; asistida y con confirmación humana para manual; importe/fecha solo como ayuda. `SOURCE_ID`↔`payment_id` requiere persistir el `payment_id` (hoy no se guarda). *(implementado: `ConciliarLiquidacionMp` y sección en Configuración → Mercado Pago; vínculo asistido `asistido_humano`)*
- **Esquema V18 + V19:** `mercado_pago_importacion`, `mercado_pago_liquidacion` y `mercado_pago_conciliacion` **creadas** en V18 (+ triggers inmutables). **V19** corrige la identidad de fila: `fila_hash` deja de ser UNIQUE (huella de contenido para marcar posibles duplicados), se agrega `fila_ordinal` (identidad `UNIQUE(importacion_id, fila_ordinal)`) e `identidad_remota` (clave del movimiento financiero). V17 con `mercado_pago_cobro_manual` intacta. V16 e histórico intactos.
- **UX:** selector de modalidad en Administración → Mercado Pago (Configuración); POS con acciones integrado/manual y panel de cobro manual. Sección de **Conciliación** en Configuración → Mercado Pago (seleccionar/previsualizar/importar reporte y conciliar pendientes de forma asistida). *(implementado)*
- No se modifican las reglas de F05 (Caja), F09 (Reportes) ni F06/F12 (devoluciones) por esta ampliación.
