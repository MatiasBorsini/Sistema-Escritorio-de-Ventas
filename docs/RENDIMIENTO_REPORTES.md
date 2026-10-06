# Rendimiento de reportes (F09-T05)

Evidencia técnica de medición. No poner benchmarks gigantes dentro de PLAN.

## Fixture temporal representativa

Base temporal aislada (se elimina al terminar; nunca datos reales), sembrada
con SQL directo en una transacción (`ReportesPerformanceTests`):
300 productos, 1200 variantes (precios + stock inicial), 5000 ventas en
61 días con 3 líneas cada una (15000 líneas), pagos por medio (5% fiadas con
cargo, 5% anuladas con reversión), ~5300 operaciones y movimientos de caja
(8 sesiones, 300 manuales, 20 correcciones de medio con par compensatorio),
300 cambios (entradas aptas + salidas alternadas con cobro), 50 clientes con
abono y componente. Sin índices por intuición: solo los del esquema.

## Medición inicial (sin índices nuevos)

Consulta frecuente sobre el fixture, una lectura cada una:

| Consulta | Tiempo |
|---|---|
| ventas hoy / mes / rango (resumen) | 7 / 14 / 11 ms |
| ventas por día | 13 ms |
| detalle de ventas (p. 1) | 3 ms |
| desglose de medios | 2 ms |
| stock actual (vacío y con texto) | 1 / 1 ms |
| ranking y ranking por producto | 14 / 21 ms |
| **sin ventas** | **22278 ms** |

Cuello identificado: `SinVentasAsync` evalúa tres subconsultas correlacionadas
por variante para filtrar; la de `venta_linea` hacía full scan (15000 filas
× 1200 variantes) porque no existía índice por `variante_id`
(solo había `ix_venta_linea_venta`).

## Índice agregado (justificado por la medición)

V15 `reportes-indices-f09` (solo índices, sin tablas, sin tocar V14):

```sql
CREATE INDEX IF NOT EXISTS ix_venta_linea_variante ON venta_linea(variante_id);
```

No se agregaron índices de fecha (`venta.creada_en`, `pago.creado_en`):
los rangos ya responden en ~10 ms con los índices existentes.

## Medición final (con V15)

| Consulta | Tiempo |
|---|---|
| ventas hoy / mes / rango | 7 / 14 / 11 ms |
| ventas por día | 13 ms |
| detalle de ventas | 4 ms |
| desglose de medios | 2 ms |
| stock actual / con texto | 1 / 1 ms |
| ranking / por producto | 14 / 21 ms |
| sin ventas | **33 ms** |
| historial de caja | 1 ms |

Conclusión: todo claramente interactivo (< 100 ms; techo orientativo < 2 s).
No se materializa ninguna tabla resumen. El test
`Consultas_frecuentes_responden_en_tiempo_interactivo` fija un techo
generoso de 15 s por consulta (absorbe máquinas lentas sin ocultar
regresiones gruesas).
