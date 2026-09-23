namespace LocalInfantil.Infrastructure.Persistence.Migrations;

/// <summary>
/// Catálogo de migraciones. V1: esquema inicial. V2: auditoría.
/// V3: huella de contraseña. V4: precio vigente único. V5: proveedores y
/// recepciones. V6: permisos individuales por usuario y conversión del único
/// Administrador activo en Dueña (D-27). V7: conteo físico y reversión de
/// recepciones (F03-T05). V8: stock apartado/no vendible y foto de
/// clasificación para conteos (F03-T04, D-33). Ver docs/ESQUEMA.md.
/// </summary>
public static class MigracionesF01
{
    public static IReadOnlyList<Migracion> Todas { get; } = new List<Migracion>
    {
        new(1, "esquema-inicial-f01", SqlV1),
        new(2, "auditoria-f01", SqlV2),
        new(3, "huella-contrasena-f01", SqlV3),
        new(4, "precio-vigente-unico-f02", SqlV4),
        new(5, "proveedores-recepcion-f03", SqlV5),
        new(6, "permisos-individuales-duena", SqlV6),
        new(7, "conteo-reversion-f03", SqlV7),
        new(8, "stock-no-vendible-f03", SqlV8),
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

    private const string SqlV3 = """
        ALTER TABLE usuario ADD COLUMN huella_contrasena TEXT NULL;
        """;

    private const string SqlV4 = """
        CREATE UNIQUE INDEX IF NOT EXISTS ix_precio_vigente_unico ON precio(variante_id) WHERE vigente_hasta IS NULL;
        """;

    private const string SqlV5 = """
        CREATE TABLE IF NOT EXISTS proveedor (
            id TEXT PRIMARY KEY,
            nombre TEXT NOT NULL CHECK (nombre <> ''),
            activo INTEGER NOT NULL DEFAULT 1 CHECK (activo IN (0, 1)),
            creado_en TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS recepcion (
            id TEXT PRIMARY KEY,
            proveedor_id TEXT NOT NULL REFERENCES proveedor(id) ON DELETE RESTRICT,
            operacion_id TEXT NOT NULL UNIQUE CHECK (operacion_id <> ''),
            comprobante_ref TEXT NULL,
            creado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS recepcion_linea (
            id TEXT PRIMARY KEY,
            recepcion_id TEXT NOT NULL REFERENCES recepcion(id) ON DELETE RESTRICT,
            variante_id TEXT NOT NULL REFERENCES variante(id) ON DELETE RESTRICT,
            cantidad INTEGER NOT NULL CHECK (cantidad > 0),
            costo_unitario_minor INTEGER NULL CHECK (costo_unitario_minor IS NULL OR costo_unitario_minor >= 0),
            moneda TEXT NULL,
            CHECK ((costo_unitario_minor IS NULL AND moneda IS NULL) OR (costo_unitario_minor IS NOT NULL AND moneda IS NOT NULL AND moneda <> ''))
        );

        CREATE INDEX IF NOT EXISTS ix_recepcion_proveedor ON recepcion(proveedor_id);
        CREATE INDEX IF NOT EXISTS ix_recepcion_linea_recepcion ON recepcion_linea(recepcion_id);
        CREATE INDEX IF NOT EXISTS ix_recepcion_linea_variante ON recepcion_linea(variante_id);
        """;

    private const string SqlV6 = """
        CREATE TABLE IF NOT EXISTS permiso_usuario (
            usuario_id TEXT NOT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            permiso TEXT NOT NULL CHECK (permiso <> ''),
            concedido INTEGER NOT NULL CHECK (concedido IN (0, 1)),
            actualizado_en TEXT NOT NULL,
            actualizado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            PRIMARY KEY (usuario_id, permiso)
        );

        CREATE INDEX IF NOT EXISTS ix_permiso_usuario_permiso ON permiso_usuario(permiso);

        UPDATE usuario SET rol = 'duena' WHERE rol = 'administrador'
            AND (SELECT COUNT(*) FROM usuario WHERE rol = 'administrador') = 1
            AND (SELECT COUNT(*) FROM usuario WHERE rol = 'administrador' AND activo = 1) = 1;

        CREATE TEMP TABLE "F01-V6_sin_administradores_residuales" (ok INTEGER NOT NULL CHECK (ok = 1));
        INSERT INTO "F01-V6_sin_administradores_residuales" (ok)
            SELECT CASE WHEN (SELECT COUNT(*) FROM usuario WHERE rol = 'administrador') > 0 THEN 0 ELSE 1 END;
        CREATE TEMP TABLE "F01-V6_con_duena_activa_identificable" (ok INTEGER NOT NULL CHECK (ok = 1));
        INSERT INTO "F01-V6_con_duena_activa_identificable" (ok)
            SELECT CASE WHEN (SELECT COUNT(*) FROM usuario) > 0
                         AND (SELECT COUNT(*) FROM usuario WHERE rol = 'duena' AND activo = 1) = 0
                        THEN 0 ELSE 1 END;
        DROP TABLE "F01-V6_sin_administradores_residuales";
        DROP TABLE "F01-V6_con_duena_activa_identificable";
        """;

    private const string SqlV7 = """
        CREATE TABLE IF NOT EXISTS conteo (
            id TEXT PRIMARY KEY,
            operacion_id TEXT NOT NULL UNIQUE CHECK (operacion_id <> ''),
            estado TEXT NOT NULL DEFAULT 'borrador' CHECK (estado IN ('borrador', 'aprobado')),
            creado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL,
            aprobado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            aprobado_en TEXT NULL,
            CHECK (aprobado_en IS NULL OR aprobado_en >= creado_en)
        );

        CREATE TABLE IF NOT EXISTS conteo_detalle (
            conteo_id TEXT NOT NULL REFERENCES conteo(id) ON DELETE RESTRICT,
            variante_id TEXT NOT NULL REFERENCES variante(id) ON DELETE RESTRICT,
            existencia_esperada INTEGER NOT NULL,
            movimientos_contados INTEGER NOT NULL CHECK (movimientos_contados >= 0),
            cantidad_contada INTEGER NOT NULL CHECK (cantidad_contada >= 0),
            PRIMARY KEY (conteo_id, variante_id)
        );

        CREATE TABLE IF NOT EXISTS reversion_recepcion (
            id TEXT PRIMARY KEY,
            recepcion_id TEXT NOT NULL UNIQUE REFERENCES recepcion(id) ON DELETE RESTRICT,
            operacion_id TEXT NOT NULL UNIQUE CHECK (operacion_id <> ''),
            motivo TEXT NOT NULL CHECK (motivo <> ''),
            creado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_conteo_detalle_variante ON conteo_detalle(variante_id);
        CREATE INDEX IF NOT EXISTS ix_reversion_recepcion_recepcion ON reversion_recepcion(recepcion_id);
        """;

    private const string SqlV8 = """
        CREATE TABLE IF NOT EXISTS apartado_movimiento (
            id TEXT PRIMARY KEY,
            variante_id TEXT NOT NULL REFERENCES variante(id) ON DELETE RESTRICT,
            tipo TEXT NOT NULL CHECK (tipo IN ('apartar', 'reincorporar', 'baja_definitiva')),
            cantidad INTEGER NOT NULL CHECK (cantidad > 0),
            motivo TEXT NOT NULL CHECK (motivo <> ''),
            operacion_id TEXT NOT NULL UNIQUE CHECK (operacion_id <> ''),
            creado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_apartado_variante ON apartado_movimiento(variante_id);

        CREATE TABLE IF NOT EXISTS conteo_clasificacion (
            conteo_id TEXT NOT NULL REFERENCES conteo(id) ON DELETE RESTRICT,
            variante_id TEXT NOT NULL REFERENCES variante(id) ON DELETE RESTRICT,
            apartado_esperado INTEGER NOT NULL CHECK (apartado_esperado >= 0),
            clasificaciones_contadas INTEGER NOT NULL CHECK (clasificaciones_contadas >= 0),
            PRIMARY KEY (conteo_id, variante_id)
        );
        """;
}
