namespace LocalInfantil.Infrastructure.Persistence.Migrations;

/// <summary>
/// Catálogo de migraciones F01. La versión 1 crea el esquema inicial:
/// usuarios, productos, variantes, precios, movimientos de inventario y
/// estructuras mínimas de ventas, pagos y caja. La versión 2 agrega el
/// registro mínimo de auditoría (F01-T03). Ver docs/ESQUEMA.md para
/// el detalle de tablas, restricciones y decisiones pendientes.
/// </summary>
public static class MigracionesF01
{
    public static IReadOnlyList<Migracion> Todas { get; } = new List<Migracion>
    {
        new(1, "esquema-inicial-f01", SqlV1),
        new(2, "auditoria-f01", SqlV2),
    }.AsReadOnly();

    private const string SqlV1 = """
        CREATE TABLE IF NOT EXISTS usuario (
            id TEXT PRIMARY KEY,
            nombre TEXT NOT NULL UNIQUE,
            rol TEXT NOT NULL,
            activo INTEGER NOT NULL DEFAULT 1 CHECK (activo IN (0, 1)),
            creado_en TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS producto (
            id TEXT PRIMARY KEY,
            nombre TEXT NOT NULL CHECK (nombre <> ''),
            marca TEXT NULL,
            categoria TEXT NULL,
            activo INTEGER NOT NULL DEFAULT 1 CHECK (activo IN (0, 1)),
            creado_en TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS variante (
            id TEXT PRIMARY KEY,
            producto_id TEXT NOT NULL REFERENCES producto(id) ON DELETE RESTRICT,
            sku TEXT NOT NULL UNIQUE CHECK (sku <> ''),
            talle TEXT NULL,
            color TEXT NULL,
            activa INTEGER NOT NULL DEFAULT 1 CHECK (activa IN (0, 1)),
            creado_en TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS precio (
            id TEXT PRIMARY KEY,
            variante_id TEXT NOT NULL REFERENCES variante(id) ON DELETE RESTRICT,
            importe_minor INTEGER NOT NULL CHECK (importe_minor >= 0),
            moneda TEXT NOT NULL DEFAULT 'ARS' CHECK (moneda <> ''),
            vigente_desde TEXT NOT NULL,
            vigente_hasta TEXT NULL,
            creado_en TEXT NOT NULL,
            CHECK (vigente_hasta IS NULL OR vigente_hasta > vigente_desde)
        );

        CREATE TABLE IF NOT EXISTS movimiento_inventario (
            id TEXT PRIMARY KEY,
            variante_id TEXT NOT NULL REFERENCES variante(id) ON DELETE RESTRICT,
            tipo TEXT NOT NULL CHECK (tipo IN ('inicial', 'ingreso', 'egreso', 'ajuste')),
            cantidad INTEGER NOT NULL,
            motivo TEXT NULL,
            operacion_id TEXT NOT NULL CHECK (operacion_id <> ''),
            creado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL,
            CHECK (
                (tipo IN ('inicial', 'ingreso') AND cantidad > 0)
                OR (tipo = 'egreso' AND cantidad < 0)
                OR (tipo = 'ajuste' AND cantidad <> 0)
            ),
            CHECK (tipo <> 'ajuste' OR (motivo IS NOT NULL AND motivo <> ''))
        );

        CREATE TABLE IF NOT EXISTS venta (
            id TEXT PRIMARY KEY,
            estado TEXT NOT NULL DEFAULT 'pendiente' CHECK (estado IN ('pendiente', 'confirmada', 'anulada')),
            total_minor INTEGER NOT NULL CHECK (total_minor >= 0),
            moneda TEXT NOT NULL DEFAULT 'ARS' CHECK (moneda <> ''),
            referencia_fiscal TEXT NULL,
            creada_en TEXT NOT NULL,
            confirmada_en TEXT NULL,
            CHECK (confirmada_en IS NULL OR confirmada_en >= creada_en)
        );

        CREATE TABLE IF NOT EXISTS venta_linea (
            id TEXT PRIMARY KEY,
            venta_id TEXT NOT NULL REFERENCES venta(id) ON DELETE RESTRICT,
            variante_id TEXT NOT NULL REFERENCES variante(id) ON DELETE RESTRICT,
            cantidad INTEGER NOT NULL CHECK (cantidad > 0),
            precio_unitario_minor INTEGER NOT NULL CHECK (precio_unitario_minor >= 0),
            subtotal_minor INTEGER NOT NULL CHECK (subtotal_minor >= 0),
            promocion_ref TEXT NULL,
            CHECK (subtotal_minor = cantidad * precio_unitario_minor)
        );

        CREATE TABLE IF NOT EXISTS pago (
            id TEXT PRIMARY KEY,
            venta_id TEXT NOT NULL REFERENCES venta(id) ON DELETE RESTRICT,
            medio TEXT NOT NULL CHECK (medio IN ('efectivo', 'transferencia', 'debito', 'credito', 'billetera')),
            importe_minor INTEGER NOT NULL CHECK (importe_minor > 0),
            creado_en TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS caja_sesion (
            id TEXT PRIMARY KEY,
            usuario_id TEXT NOT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            estado TEXT NOT NULL DEFAULT 'abierta' CHECK (estado IN ('abierta', 'cerrada')),
            fondo_inicial_minor INTEGER NOT NULL CHECK (fondo_inicial_minor >= 0),
            moneda TEXT NOT NULL DEFAULT 'ARS' CHECK (moneda <> ''),
            abierta_en TEXT NOT NULL,
            cerrada_en TEXT NULL,
            CHECK (cerrada_en IS NULL OR cerrada_en >= abierta_en)
        );

        CREATE TABLE IF NOT EXISTS caja_movimiento (
            id TEXT PRIMARY KEY,
            sesion_id TEXT NOT NULL REFERENCES caja_sesion(id) ON DELETE RESTRICT,
            tipo TEXT NOT NULL CHECK (tipo IN ('venta', 'ingreso', 'egreso', 'reintegro')),
            importe_minor INTEGER NOT NULL CHECK (importe_minor <> 0),
            venta_id TEXT NULL REFERENCES venta(id) ON DELETE RESTRICT,
            motivo TEXT NULL,
            creado_en TEXT NOT NULL,
            CHECK (tipo <> 'venta' OR venta_id IS NOT NULL)
        );

        CREATE INDEX IF NOT EXISTS ix_variante_producto ON variante(producto_id);
        CREATE INDEX IF NOT EXISTS ix_precio_variante ON precio(variante_id);
        CREATE INDEX IF NOT EXISTS ix_movimiento_variante ON movimiento_inventario(variante_id);
        CREATE INDEX IF NOT EXISTS ix_venta_linea_venta ON venta_linea(venta_id);
        CREATE INDEX IF NOT EXISTS ix_pago_venta ON pago(venta_id);
        CREATE INDEX IF NOT EXISTS ix_caja_movimiento_sesion ON caja_movimiento(sesion_id);
        """;

    private const string SqlV2 = """
        CREATE TABLE IF NOT EXISTS auditoria (
            id TEXT PRIMARY KEY,
            operacion_id TEXT NOT NULL CHECK (operacion_id <> ''),
            tipo_evento TEXT NOT NULL CHECK (tipo_evento <> ''),
            actor_tipo TEXT NOT NULL CHECK (actor_tipo IN ('usuario', 'sistema')),
            actor_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            referencia_tipo TEXT NULL,
            referencia_id TEXT NULL,
            detalle TEXT NULL,
            creado_en TEXT NOT NULL,
            CHECK (actor_tipo <> 'usuario' OR actor_id IS NOT NULL)
        );

        CREATE INDEX IF NOT EXISTS ix_auditoria_operacion ON auditoria(operacion_id);
        """;
}
