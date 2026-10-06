namespace LocalInfantil.Infrastructure.Persistence.Migrations;

/// <summary>
/// Catálogo de migraciones. V1: esquema inicial. V2: auditoría.
/// V3: huella de contraseña. V4: precio vigente único. V5: proveedores y
/// recepciones. V6: permisos individuales por usuario y conversión del único
/// Administrador activo en Dueña (D-27). V7: conteo físico y reversión de
/// recepciones (F03-T05). V8: stock apartado/no vendible y foto de
/// clasificación para conteos (F03-T04, D-33). V9: categorías controladas,
/// código interno automático de producto y variante, y unicidad Talle+Color.
/// V10: ventas POS. V11: caja y conciliación. V12: cambios/devoluciones F06.
/// V13: clientes opcionales e instantánea de nombre en ventas F07-T01.
/// V14: libro de cuenta corriente, límites, abonos, asignaciones y vínculo de reintegros.
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
        new(9, "categorias-codigos-internos", SqlV9),
        new(10, "ventas-pos", SqlV10),
        new(11, "caja-conciliacion-f05", SqlV11),
        new(12, "cambios-devoluciones-f06", SqlV12),
        new(13, "clientes-ventas-opcionales-f07", SqlV13),
        new(14, "cuenta-corriente-clientes-f07", SqlV14),
        new(15, "reportes-indices-f09", SqlV15),
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

    private const string SqlV9 = """
        -- Categorías controladas (prefijo para código interno, ej REM).
        -- Categoría obligatoria para productos nuevos (sin default "Otros").
        -- prefijo UNIQUE: dos categorías no pueden generar los mismos códigos.
        CREATE TABLE IF NOT EXISTS categoria (
            id TEXT PRIMARY KEY,
            nombre TEXT NOT NULL UNIQUE CHECK (nombre <> ''),
            prefijo TEXT NOT NULL UNIQUE CHECK (prefijo <> ''),
            activa INTEGER NOT NULL DEFAULT 1 CHECK (activa IN (0, 1)),
            orden INTEGER NOT NULL DEFAULT 0,
            creado_en TEXT NOT NULL
        );

        -- Seed primero: el fallback de compatibilidad para heredados es OTR.
        INSERT OR IGNORE INTO categoria (id, nombre, prefijo, activa, orden, creado_en) VALUES
            ('cat-rem', 'Remeras y tops', 'REM', 1, 10, '2026-01-01T00:00:00Z'),
            ('cat-buz', 'Buzos y sweaters', 'BUZ', 1, 20, '2026-01-01T00:00:00Z'),
            ('cat-abr', 'Camperas y abrigos', 'ABR', 1, 30, '2026-01-01T00:00:00Z'),
            ('cat-pan', 'Pantalones y calzas', 'PAN', 1, 40, '2026-01-01T00:00:00Z'),
            ('cat-sho', 'Shorts y bermudas', 'SHO', 1, 50, '2026-01-01T00:00:00Z'),
            ('cat-ves', 'Vestidos y polleras', 'VES', 1, 60, '2026-01-01T00:00:00Z'),
            ('cat-con', 'Conjuntos', 'CON', 1, 70, '2026-01-01T00:00:00Z'),
            ('cat-bod', 'Bodies y enteritos', 'BOD', 1, 80, '2026-01-01T00:00:00Z'),
            ('cat-pij', 'Pijamas', 'PIJ', 1, 90, '2026-01-01T00:00:00Z'),
            ('cat-int', 'Ropa interior y medias', 'INT', 1, 100, '2026-01-01T00:00:00Z'),
            ('cat-acc', 'Accesorios', 'ACC', 1, 110, '2026-01-01T00:00:00Z'),
            ('cat-otr', 'Otros', 'OTR', 1, 120, '2026-01-01T00:00:00Z');

        -- Columnas nuevas de variante. talle_norm/color_norm son
        -- infraestructura (el texto humano queda en talle/color) y las
        -- escribe el repositorio con la misma función central de Application.
        ALTER TABLE variante ADD COLUMN codigo_interno TEXT NOT NULL DEFAULT '' CHECK (codigo_interno <> '' OR codigo_interno = '');
        ALTER TABLE variante ADD COLUMN talle_norm TEXT NOT NULL DEFAULT '';
        ALTER TABLE variante ADD COLUMN color_norm TEXT NOT NULL DEFAULT '';

        -- Código interno provisorio para ADD COLUMN con filas heredadas.
        ALTER TABLE producto ADD COLUMN codigo_interno TEXT NOT NULL DEFAULT '' CHECK (codigo_interno <> '' OR codigo_interno = '');
        ALTER TABLE producto ADD COLUMN categoria_id TEXT NULL REFERENCES categoria(id) ON DELETE SET NULL;

        -- Backfill heredados (una sola convención: la variante espeja su SKU;
        -- el producto recibe MIG-*). Las normas se aproximan en SQL
        -- (UPPER/ TRIM ASCII); las escrituras reales las calcula C#.
        UPDATE variante SET codigo_interno = sku WHERE codigo_interno = '';
        UPDATE producto SET codigo_interno = 'MIG-' || substr(id, 1, 8) WHERE codigo_interno = '';
        UPDATE variante SET talle_norm = COALESCE(UPPER(TRIM(talle)), ''), color_norm = COALESCE(UPPER(TRIM(color)), '');

        -- Reconstrucción de producto a su forma final: categoria_id NOT NULL
        -- con FK, sin la columna texto libre heredada (fin de la doble
        -- fuente), código interno único. Heredados sin categoría van a OTR
        -- (compatibilidad de migración; la UX sigue exigiendo elección).
        -- defer_foreign_keys difiere las FK inmediatas hasta el COMMIT, donde
        -- las filas ya copiadas satisfacen todas las referencias.
        PRAGMA defer_foreign_keys = ON;
        CREATE TABLE producto_nuevo (
            id TEXT PRIMARY KEY,
            codigo_interno TEXT NOT NULL UNIQUE CHECK (codigo_interno <> ''),
            nombre TEXT NOT NULL CHECK (nombre <> ''),
            marca TEXT NULL,
            categoria_id TEXT NOT NULL REFERENCES categoria(id) ON DELETE SET NULL,
            activo INTEGER NOT NULL DEFAULT 1 CHECK (activo IN (0, 1)),
            creado_en TEXT NOT NULL
        );
        INSERT INTO producto_nuevo (id, codigo_interno, nombre, marca, categoria_id, activo, creado_en)
            SELECT id, codigo_interno, nombre, marca, COALESCE(categoria_id, 'cat-otr'), activo, creado_en FROM producto;
        DROP TABLE producto;
        ALTER TABLE producto_nuevo RENAME TO producto;
        PRAGMA defer_foreign_keys = OFF;

        -- Unicidad real de códigos de variante.
        CREATE UNIQUE INDEX IF NOT EXISTS ix_variante_codigo_interno ON variante(codigo_interno);

        -- Unicidad lógica Talle+Color por producto sobre la representación
        -- normalizada persistida (misma función que valida el caso de uso).
        CREATE UNIQUE INDEX IF NOT EXISTS ix_variante_talle_color_unico ON variante(producto_id, talle_norm, color_norm);

        -- Tablas de secuencia para generación automática (dentro de la misma
        -- sesión BEGIN IMMEDIATE del alta, sin transacciones anidadas)
        CREATE TABLE IF NOT EXISTS secuencia_producto (
            categoria_id TEXT PRIMARY KEY REFERENCES categoria(id) ON DELETE RESTRICT,
            ultimo_numero INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS secuencia_variante (
            producto_id TEXT PRIMARY KEY REFERENCES producto(id) ON DELETE RESTRICT,
            ultimo_numero INTEGER NOT NULL DEFAULT 0
        );
        """;

    private const string SqlV10 = """
        -- Venta: número visible estable + idempotencia + autor. Sin ventas
        -- reales en ninguna base (F04 no implementado hasta esta versión).
        ALTER TABLE venta ADD COLUMN numero TEXT NOT NULL DEFAULT '';
        ALTER TABLE venta ADD COLUMN operacion_id TEXT NOT NULL DEFAULT '';
        ALTER TABLE venta ADD COLUMN creado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT;
        UPDATE venta SET numero = 'MIG-' || substr(id, 1, 8) WHERE numero = '';
        UPDATE venta SET operacion_id = 'MIG-' || substr(id, 1, 8) WHERE operacion_id = '';
        CREATE UNIQUE INDEX IF NOT EXISTS ix_venta_numero ON venta(numero);
        CREATE UNIQUE INDEX IF NOT EXISTS ix_venta_operacion ON venta(operacion_id);
        CREATE INDEX IF NOT EXISTS ix_venta_estado ON venta(estado);

        -- Correlativo visible T-000001 (patrón secuencias V9, clave fija).
        CREATE TABLE IF NOT EXISTS secuencia_venta (
            id TEXT PRIMARY KEY CHECK (id = 'venta'),
            ultimo_numero INTEGER NOT NULL DEFAULT 0
        );
        INSERT OR IGNORE INTO secuencia_venta (id, ultimo_numero) VALUES ('venta', 0);

        -- Snapshots históricos en línea: la venta sigue comprensible aunque
        -- cambien nombre/código/talle/color/precio. variante_id queda como
        -- referencia técnica adicional.
        ALTER TABLE venta_linea ADD COLUMN codigo_interno TEXT NOT NULL DEFAULT '';
        ALTER TABLE venta_linea ADD COLUMN nombre_producto TEXT NOT NULL DEFAULT '';
        ALTER TABLE venta_linea ADD COLUMN talle TEXT NULL;
        ALTER TABLE venta_linea ADD COLUMN color TEXT NULL;

        -- Efectivo distingue imputado (importe, suma == total) de entregado
        -- (recibido, solo efectivo, >= importe; vuelto derivable). Sin ventas
        -- reales, ninguna fila heredada puede violar el CHECK.
        ALTER TABLE pago ADD COLUMN recibido_minor INTEGER NULL CHECK (
            (medio = 'efectivo' AND recibido_minor IS NOT NULL AND recibido_minor >= importe_minor)
            OR (medio <> 'efectivo' AND recibido_minor IS NULL));

        -- Reversión total auditada (espejo de reversion_recepcion V7).
        CREATE TABLE IF NOT EXISTS reversion_venta (
            id TEXT PRIMARY KEY,
            venta_id TEXT NOT NULL UNIQUE REFERENCES venta(id) ON DELETE RESTRICT,
            operacion_id TEXT NOT NULL UNIQUE CHECK (operacion_id <> ''),
            motivo TEXT NOT NULL CHECK (motivo <> ''),
            creado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_reversion_venta_venta ON reversion_venta(venta_id);
        """;

    private const string SqlV11 = """
        -- La sesión conserva el fondo inicial y, al cerrar, una foto inmutable
        -- del arqueo. Los ids LEGACY identifican filas previas sin inventar
        -- operaciones ni auditoría retroactivas.
        ALTER TABLE caja_sesion ADD COLUMN cerrado_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT;
        ALTER TABLE caja_sesion ADD COLUMN operacion_apertura_id TEXT NOT NULL DEFAULT '';
        ALTER TABLE caja_sesion ADD COLUMN operacion_cierre_id TEXT NULL;
        ALTER TABLE caja_sesion ADD COLUMN efectivo_esperado_minor INTEGER NULL;
        ALTER TABLE caja_sesion ADD COLUMN efectivo_contado_minor INTEGER NULL CHECK (efectivo_contado_minor IS NULL OR efectivo_contado_minor >= 0);
        ALTER TABLE caja_sesion ADD COLUMN diferencia_minor INTEGER NULL;
        ALTER TABLE caja_sesion ADD COLUMN observacion_cierre TEXT NULL;
        UPDATE caja_sesion SET operacion_apertura_id = 'LEGACY-APERTURA-' || id WHERE operacion_apertura_id = '';
        CREATE UNIQUE INDEX IF NOT EXISTS ux_caja_sesion_operacion_apertura ON caja_sesion(operacion_apertura_id);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_caja_sesion_operacion_cierre ON caja_sesion(operacion_cierre_id) WHERE operacion_cierre_id IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_caja_una_sesion_abierta ON caja_sesion(estado) WHERE estado = 'abierta';
        CREATE INDEX IF NOT EXISTS ix_caja_sesion_cerrada_fecha ON caja_sesion(cerrada_en DESC, id DESC);

        CREATE TABLE caja_operacion (
            id TEXT PRIMARY KEY,
            operacion_id TEXT NOT NULL UNIQUE CHECK (operacion_id <> ''),
            sesion_id TEXT NOT NULL REFERENCES caja_sesion(id) ON DELETE RESTRICT,
            tipo TEXT NOT NULL CHECK (tipo IN ('apertura', 'cierre', 'venta', 'anulacion', 'movimiento_manual', 'correccion_medio', 'reintegro')),
            usuario_id TEXT NOT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL
        );
        CREATE INDEX ix_caja_operacion_sesion ON caja_operacion(sesion_id, creado_en DESC, id DESC);

        -- Se reconstruye solo caja_movimiento para ampliar tipos/restricciones;
        -- todas las filas anteriores se copian literalmente y los metadatos
        -- F05 quedan NULL, pues no hubo escritor de caja en F04.
        ALTER TABLE caja_movimiento RENAME TO caja_movimiento_v10;
        CREATE TABLE caja_movimiento_nuevo (
            id TEXT PRIMARY KEY,
            sesion_id TEXT NOT NULL REFERENCES caja_sesion(id) ON DELETE RESTRICT,
            tipo TEXT NOT NULL CHECK (tipo IN ('venta', 'ingreso', 'egreso', 'reintegro', 'correccion')),
            importe_minor INTEGER NOT NULL CHECK (importe_minor <> 0),
            venta_id TEXT NULL REFERENCES venta(id) ON DELETE RESTRICT,
            motivo TEXT NULL,
            creado_en TEXT NOT NULL,
            caja_operacion_id TEXT NULL REFERENCES caja_operacion(id) ON DELETE RESTRICT,
            pago_id TEXT NULL REFERENCES pago(id) ON DELETE RESTRICT,
            medio TEXT NULL CHECK (medio IS NULL OR medio IN ('efectivo', 'transferencia', 'debito', 'credito', 'billetera')),
            categoria TEXT NULL,
            usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            movimiento_origen_id TEXT NULL REFERENCES caja_movimiento_nuevo(id) ON DELETE RESTRICT,
            CHECK (tipo <> 'venta' OR venta_id IS NOT NULL)
        );
        INSERT INTO caja_movimiento_nuevo (id, sesion_id, tipo, importe_minor, venta_id, motivo, creado_en)
            SELECT id, sesion_id, tipo, importe_minor, venta_id, motivo, creado_en FROM caja_movimiento_v10;
        DROP TABLE caja_movimiento_v10;
        ALTER TABLE caja_movimiento_nuevo RENAME TO caja_movimiento;
        CREATE INDEX ix_caja_movimiento_sesion ON caja_movimiento(sesion_id);
        CREATE INDEX ix_caja_movimiento_sesion_fecha ON caja_movimiento(sesion_id, creado_en DESC, id DESC);
        CREATE INDEX ix_caja_movimiento_venta ON caja_movimiento(venta_id) WHERE venta_id IS NOT NULL;
        CREATE INDEX ix_caja_movimiento_pago ON caja_movimiento(pago_id) WHERE pago_id IS NOT NULL;
        CREATE INDEX ix_caja_movimiento_operacion ON caja_movimiento(caja_operacion_id) WHERE caja_operacion_id IS NOT NULL;
        CREATE UNIQUE INDEX ux_caja_movimiento_pago_efecto ON caja_movimiento(caja_operacion_id, pago_id, tipo, medio)
            WHERE caja_operacion_id IS NOT NULL AND pago_id IS NOT NULL;
        CREATE UNIQUE INDEX ux_caja_movimiento_pago_venta ON caja_movimiento(pago_id)
            WHERE pago_id IS NOT NULL AND tipo = 'venta';

        CREATE TABLE pago_correccion (
            id TEXT PRIMARY KEY,
            pago_id TEXT NOT NULL UNIQUE REFERENCES pago(id) ON DELETE RESTRICT,
            medio_anterior TEXT NOT NULL CHECK (medio_anterior IN ('efectivo', 'transferencia', 'debito', 'credito', 'billetera')),
            medio_nuevo TEXT NOT NULL CHECK (medio_nuevo IN ('efectivo', 'transferencia', 'debito', 'credito', 'billetera')),
            importe_minor INTEGER NOT NULL CHECK (importe_minor > 0),
            motivo TEXT NOT NULL CHECK (trim(motivo) <> ''),
            usuario_id TEXT NOT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            operacion_id TEXT NOT NULL UNIQUE CHECK (operacion_id <> ''),
            caja_operacion_id TEXT NOT NULL UNIQUE REFERENCES caja_operacion(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL,
        CHECK (medio_anterior <> medio_nuevo)
        );

        CREATE TRIGGER trg_caja_sesion_apertura_valida
        BEFORE INSERT ON caja_sesion
        WHEN NEW.estado <> 'abierta' OR NEW.operacion_apertura_id IS NULL OR trim(NEW.operacion_apertura_id) = ''
          OR NEW.cerrada_en IS NOT NULL OR NEW.cerrado_por_usuario_id IS NOT NULL
          OR NEW.operacion_cierre_id IS NOT NULL OR NEW.efectivo_esperado_minor IS NOT NULL
          OR NEW.efectivo_contado_minor IS NOT NULL OR NEW.diferencia_minor IS NOT NULL
        BEGIN SELECT RAISE(ABORT, 'la sesión debe crearse abierta y sin datos de cierre'); END;

        CREATE TRIGGER trg_caja_operacion_sesion_abierta
        BEFORE INSERT ON caja_operacion
        WHEN NOT EXISTS (SELECT 1 FROM caja_sesion s WHERE s.id = NEW.sesion_id AND s.estado = 'abierta')
        BEGIN SELECT RAISE(ABORT, 'la sesión de caja está cerrada'); END;

        -- Cierre nuevo debe tener snapshot consistente. Las sesiones cerradas
        -- preexistentes se conservan como legacy sin fabricar ese snapshot.
        CREATE TRIGGER trg_caja_sesion_cierre_completo
        BEFORE UPDATE OF estado ON caja_sesion
        WHEN OLD.estado = 'abierta' AND NEW.estado = 'cerrada'
        BEGIN
            SELECT CASE WHEN NEW.cerrado_por_usuario_id IS NULL OR NEW.cerrada_en IS NULL
                OR NEW.operacion_cierre_id IS NULL OR NEW.efectivo_esperado_minor IS NULL
                OR NEW.efectivo_contado_minor IS NULL OR NEW.diferencia_minor IS NULL
                OR NEW.efectivo_contado_minor - NEW.efectivo_esperado_minor <> NEW.diferencia_minor
                OR (NEW.diferencia_minor <> 0 AND trim(COALESCE(NEW.observacion_cierre, '')) = '')
                THEN RAISE(ABORT, 'cierre de caja incompleto o inconsistente') END;
        END;

        CREATE TRIGGER trg_caja_sesion_solo_cierre
        BEFORE UPDATE ON caja_sesion
        WHEN NOT (
            OLD.estado = 'abierta' AND NEW.estado = 'cerrada'
            AND NEW.id = OLD.id AND NEW.usuario_id = OLD.usuario_id
            AND NEW.fondo_inicial_minor = OLD.fondo_inicial_minor AND NEW.moneda = OLD.moneda
            AND NEW.abierta_en = OLD.abierta_en AND NEW.operacion_apertura_id = OLD.operacion_apertura_id
        )
        BEGIN SELECT RAISE(ABORT, 'la sesión de caja es inmutable salvo su cierre'); END;

        CREATE TRIGGER trg_caja_sesion_no_borrar
        BEFORE DELETE ON caja_sesion BEGIN SELECT RAISE(ABORT, 'la sesión de caja es histórica'); END;
        CREATE TRIGGER trg_caja_movimiento_sesion_abierta
        BEFORE INSERT ON caja_movimiento
        WHEN NOT EXISTS (SELECT 1 FROM caja_sesion s WHERE s.id = NEW.sesion_id AND s.estado = 'abierta')
        BEGIN SELECT RAISE(ABORT, 'la sesión de caja está cerrada'); END;
        CREATE TRIGGER trg_caja_movimiento_datos_coherentes
        BEFORE INSERT ON caja_movimiento
        WHEN (NEW.tipo = 'venta' AND (NEW.pago_id IS NULL OR NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.categoria IS NOT NULL OR NEW.importe_minor <= 0
              OR NOT EXISTS (SELECT 1 FROM pago p WHERE p.id = NEW.pago_id AND p.venta_id = NEW.venta_id AND p.medio = NEW.medio AND p.importe_minor = NEW.importe_minor)))
          OR (NEW.tipo = 'ingreso' AND (COALESCE(NEW.categoria, '') NOT IN ('cambio_fondo', 'otro_ingreso') OR NEW.importe_minor <= 0 OR NEW.medio IS NULL OR NEW.medio <> 'efectivo' OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.venta_id IS NOT NULL OR NEW.pago_id IS NOT NULL OR trim(COALESCE(NEW.motivo, '')) = ''))
          OR (NEW.tipo = 'egreso' AND (COALESCE(NEW.categoria, '') NOT IN ('gasto', 'retiro_propietario', 'otro_egreso') OR NEW.importe_minor >= 0 OR NEW.medio IS NULL OR NEW.medio <> 'efectivo' OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.venta_id IS NOT NULL OR NEW.pago_id IS NOT NULL OR trim(COALESCE(NEW.motivo, '')) = ''))
          OR (NEW.tipo = 'reintegro' AND (NEW.pago_id IS NULL OR NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.importe_minor >= 0 OR trim(COALESCE(NEW.motivo, '')) = ''
              OR NOT EXISTS (SELECT 1 FROM pago p WHERE p.id = NEW.pago_id AND p.venta_id = NEW.venta_id)
              OR COALESCE(-NEW.importe_minor, 0) + COALESCE((SELECT -SUM(m.importe_minor) FROM caja_movimiento m WHERE m.pago_id = NEW.pago_id AND m.tipo = 'reintegro'), 0)
                 > COALESCE((SELECT p.importe_minor FROM pago p WHERE p.id = NEW.pago_id), 0)))
          OR (NEW.tipo = 'correccion' AND (NEW.pago_id IS NULL OR NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR trim(COALESCE(NEW.motivo, '')) = ''
              OR NOT EXISTS (SELECT 1 FROM pago_correccion pc WHERE pc.pago_id = NEW.pago_id AND pc.caja_operacion_id = NEW.caja_operacion_id
                  AND ((NEW.medio = pc.medio_anterior AND NEW.importe_minor = -pc.importe_minor) OR (NEW.medio = pc.medio_nuevo AND NEW.importe_minor = pc.importe_minor)))))
        BEGIN SELECT RAISE(ABORT, 'los datos del movimiento de caja no son coherentes'); END;

        CREATE TRIGGER trg_caja_movimiento_operacion_coherente
        BEFORE INSERT ON caja_movimiento
        WHEN NEW.caja_operacion_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM caja_operacion o
            WHERE o.id = NEW.caja_operacion_id AND o.sesion_id = NEW.sesion_id
              AND ((NEW.tipo = 'venta' AND o.tipo = 'venta')
                OR (NEW.tipo IN ('ingreso', 'egreso') AND o.tipo = 'movimiento_manual')
                OR (NEW.tipo = 'reintegro' AND o.tipo IN ('anulacion', 'reintegro'))
                OR (NEW.tipo = 'correccion' AND o.tipo = 'correccion_medio'))
        )
        BEGIN SELECT RAISE(ABORT, 'el movimiento no corresponde a la operación o sesión de caja'); END;
        CREATE TRIGGER trg_caja_movimiento_no_editar
        BEFORE UPDATE ON caja_movimiento BEGIN SELECT RAISE(ABORT, 'el movimiento de caja es inmutable'); END;
        CREATE TRIGGER trg_caja_movimiento_no_borrar
        BEFORE DELETE ON caja_movimiento BEGIN SELECT RAISE(ABORT, 'el movimiento de caja es histórico'); END;
        CREATE TRIGGER trg_caja_operacion_no_editar
        BEFORE UPDATE ON caja_operacion BEGIN SELECT RAISE(ABORT, 'la operación de caja es inmutable'); END;
        CREATE TRIGGER trg_caja_operacion_no_borrar
        BEFORE DELETE ON caja_operacion BEGIN SELECT RAISE(ABORT, 'la operación de caja es histórica'); END;
        CREATE TRIGGER trg_pago_correccion_no_editar
        BEFORE UPDATE ON pago_correccion BEGIN SELECT RAISE(ABORT, 'la corrección de pago es inmutable'); END;
        CREATE TRIGGER trg_pago_correccion_no_borrar
        BEFORE DELETE ON pago_correccion BEGIN SELECT RAISE(ABORT, 'la corrección de pago es histórica'); END;
        CREATE TRIGGER trg_pago_correccion_sesion_abierta
        BEFORE INSERT ON pago_correccion
        WHEN NOT EXISTS (
            SELECT 1 FROM caja_operacion o JOIN caja_sesion s ON s.id = o.sesion_id
            WHERE o.id = NEW.caja_operacion_id AND o.tipo = 'correccion_medio' AND s.estado = 'abierta'
        )
        BEGIN SELECT RAISE(ABORT, 'la corrección requiere una caja abierta'); END;
        CREATE TRIGGER trg_pago_inmutable
        BEFORE UPDATE ON pago BEGIN SELECT RAISE(ABORT, 'el pago de venta es inmutable'); END;
        CREATE TRIGGER trg_pago_no_borrar
        BEFORE DELETE ON pago BEGIN SELECT RAISE(ABORT, 'el pago de venta es histórico'); END;
        """;

    private const string SqlV12 = """
        PRAGMA defer_foreign_keys = ON;
        CREATE TABLE cambio (
            id TEXT PRIMARY KEY,
            numero TEXT NOT NULL UNIQUE,
            operacion_id TEXT NOT NULL UNIQUE CHECK (trim(operacion_id) <> ''),
            venta_id TEXT NOT NULL REFERENCES venta(id) ON DELETE RESTRICT,
            usuario_id TEXT NOT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            autorizado_reintegro_por_usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL,
            importe_entradas_minor INTEGER NOT NULL CHECK (importe_entradas_minor >= 0),
            importe_salidas_minor INTEGER NOT NULL CHECK (importe_salidas_minor >= 0),
            diferencia_minor INTEGER NOT NULL CHECK (diferencia_minor = importe_salidas_minor - importe_entradas_minor),
            medio_reintegro TEXT NULL CHECK (medio_reintegro IS NULL OR medio_reintegro IN ('efectivo','transferencia','debito','credito','billetera')),
            motivo TEXT NULL
        );
        CREATE INDEX ix_cambio_venta_fecha ON cambio(venta_id, creado_en DESC);
        CREATE TABLE cambio_entrada (
            id TEXT PRIMARY KEY,
            cambio_id TEXT NOT NULL REFERENCES cambio(id) ON DELETE RESTRICT,
            venta_linea_id TEXT NOT NULL REFERENCES venta_linea(id) ON DELETE RESTRICT,
            variante_id TEXT NOT NULL REFERENCES variante(id) ON DELETE RESTRICT,
            cantidad INTEGER NOT NULL CHECK (cantidad > 0),
            precio_unitario_minor INTEGER NOT NULL CHECK (precio_unitario_minor >= 0),
            apta INTEGER NOT NULL CHECK (apta IN (0,1)),
            motivo_no_apta TEXT NULL,
            codigo_interno TEXT NOT NULL,
            nombre_producto TEXT NOT NULL,
            talle TEXT NULL,
            color TEXT NULL,
            UNIQUE(cambio_id, venta_linea_id),
            CHECK ((apta = 1 AND motivo_no_apta IS NULL) OR (apta = 0 AND trim(COALESCE(motivo_no_apta,'')) <> ''))
        );
        CREATE TABLE cambio_salida (
            id TEXT PRIMARY KEY,
            cambio_id TEXT NOT NULL REFERENCES cambio(id) ON DELETE RESTRICT,
            variante_id TEXT NOT NULL REFERENCES variante(id) ON DELETE RESTRICT,
            cantidad INTEGER NOT NULL CHECK (cantidad > 0),
            precio_unitario_minor INTEGER NOT NULL CHECK (precio_unitario_minor >= 0),
            codigo_interno TEXT NOT NULL,
            nombre_producto TEXT NOT NULL,
            talle TEXT NULL,
            color TEXT NULL,
            UNIQUE(cambio_id, variante_id)
        );
        CREATE TABLE cambio_pago (
            id TEXT PRIMARY KEY,
            cambio_id TEXT NOT NULL REFERENCES cambio(id) ON DELETE RESTRICT,
            direccion TEXT NOT NULL CHECK (direccion IN ('cobro','reintegro')),
            medio TEXT NOT NULL CHECK (medio IN ('efectivo','transferencia','debito','credito','billetera')),
            importe_minor INTEGER NOT NULL CHECK (importe_minor > 0),
            recibido_minor INTEGER NULL CHECK (recibido_minor IS NULL OR recibido_minor >= importe_minor),
            UNIQUE(cambio_id, direccion, medio)
        );
        CREATE TABLE secuencia_cambio (id INTEGER PRIMARY KEY CHECK(id=1), siguiente_numero INTEGER NOT NULL CHECK(siguiente_numero>0));
        INSERT INTO secuencia_cambio(id,siguiente_numero) VALUES(1,1);
        CREATE INDEX ix_cambio_entrada_linea ON cambio_entrada(venta_linea_id);

        CREATE TRIGGER trg_cambio_requiere_venta_confirmada
        BEFORE INSERT ON cambio
        WHEN NOT EXISTS (SELECT 1 FROM venta v WHERE v.id = NEW.venta_id AND v.estado = 'confirmada')
        BEGIN SELECT RAISE(ABORT, 'el cambio requiere venta confirmada'); END;
        CREATE TRIGGER trg_cambio_entrada_limite_vendido
        BEFORE INSERT ON cambio_entrada
        BEGIN
          SELECT CASE WHEN NOT EXISTS (
            SELECT 1 FROM cambio c JOIN venta_linea l ON l.id = NEW.venta_linea_id
            WHERE c.id = NEW.cambio_id AND c.venta_id = l.venta_id AND l.variante_id = NEW.variante_id
              AND NEW.cantidad + COALESCE((SELECT SUM(e.cantidad) FROM cambio_entrada e WHERE e.venta_linea_id = l.id),0) <= l.cantidad
          ) THEN RAISE(ABORT, 'la cantidad supera unidades vendidas disponibles') END;
        END;
        CREATE TRIGGER trg_cambio_no_editar BEFORE UPDATE ON cambio BEGIN SELECT RAISE(ABORT, 'el cambio es inmutable'); END;
        CREATE TRIGGER trg_cambio_no_borrar BEFORE DELETE ON cambio BEGIN SELECT RAISE(ABORT, 'el cambio es histórico'); END;
        CREATE TRIGGER trg_cambio_entrada_no_editar BEFORE UPDATE ON cambio_entrada BEGIN SELECT RAISE(ABORT, 'la entrada del cambio es inmutable'); END;
        CREATE TRIGGER trg_cambio_entrada_no_borrar BEFORE DELETE ON cambio_entrada BEGIN SELECT RAISE(ABORT, 'la entrada del cambio es histórica'); END;
        CREATE TRIGGER trg_cambio_salida_no_editar BEFORE UPDATE ON cambio_salida BEGIN SELECT RAISE(ABORT, 'la salida del cambio es inmutable'); END;
        CREATE TRIGGER trg_cambio_salida_no_borrar BEFORE DELETE ON cambio_salida BEGIN SELECT RAISE(ABORT, 'la salida del cambio es histórica'); END;
        CREATE TRIGGER trg_cambio_pago_no_editar BEFORE UPDATE ON cambio_pago BEGIN SELECT RAISE(ABORT, 'el pago del cambio es inmutable'); END;
        CREATE TRIGGER trg_cambio_pago_no_borrar BEFORE DELETE ON cambio_pago BEGIN SELECT RAISE(ABORT, 'el pago del cambio es histórico'); END;

        -- Amplía los tipos sin alterar V11 ni reescribir históricos.
        DROP TRIGGER trg_caja_movimiento_sesion_abierta;
        DROP TRIGGER trg_caja_movimiento_datos_coherentes;
        DROP TRIGGER trg_caja_movimiento_operacion_coherente;
        DROP TRIGGER trg_caja_movimiento_no_editar;
        DROP TRIGGER trg_caja_movimiento_no_borrar;
        CREATE TABLE caja_movimiento_nueva (
            id TEXT PRIMARY KEY, sesion_id TEXT NOT NULL REFERENCES caja_sesion(id) ON DELETE RESTRICT,
            tipo TEXT NOT NULL CHECK (tipo IN ('venta','ingreso','egreso','reintegro','correccion','cambio_cobro','cambio_reintegro')),
            importe_minor INTEGER NOT NULL CHECK (importe_minor <> 0),
            venta_id TEXT NULL REFERENCES venta(id) ON DELETE RESTRICT,
            motivo TEXT NULL, creado_en TEXT NOT NULL,
            caja_operacion_id TEXT NULL REFERENCES caja_operacion(id) ON DELETE RESTRICT,
            pago_id TEXT NULL REFERENCES pago(id) ON DELETE RESTRICT,
            medio TEXT NULL CHECK (medio IS NULL OR medio IN ('efectivo','transferencia','debito','credito','billetera')),
            categoria TEXT NULL, usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            movimiento_origen_id TEXT NULL REFERENCES caja_movimiento_nueva(id) ON DELETE RESTRICT,
            cambio_pago_id TEXT NULL REFERENCES cambio_pago(id) ON DELETE RESTRICT,
            CHECK (tipo <> 'venta' OR venta_id IS NOT NULL),
            CHECK ((tipo = 'cambio_cobro' AND importe_minor > 0 AND cambio_pago_id IS NOT NULL) OR tipo <> 'cambio_cobro'),
            CHECK ((tipo = 'cambio_reintegro' AND importe_minor < 0 AND cambio_pago_id IS NOT NULL) OR tipo <> 'cambio_reintegro')
        );
        INSERT INTO caja_movimiento_nueva (id,sesion_id,tipo,importe_minor,venta_id,motivo,creado_en,caja_operacion_id,pago_id,medio,categoria,usuario_id,movimiento_origen_id)
          SELECT id,sesion_id,tipo,importe_minor,venta_id,motivo,creado_en,caja_operacion_id,pago_id,medio,categoria,usuario_id,movimiento_origen_id FROM caja_movimiento;
        DROP TABLE caja_movimiento;
        ALTER TABLE caja_movimiento_nueva RENAME TO caja_movimiento;
        CREATE INDEX ix_caja_movimiento_sesion ON caja_movimiento(sesion_id);
        CREATE INDEX ix_caja_movimiento_sesion_fecha ON caja_movimiento(sesion_id, creado_en DESC, id DESC);
        CREATE INDEX ix_caja_movimiento_venta ON caja_movimiento(venta_id) WHERE venta_id IS NOT NULL;
        CREATE INDEX ix_caja_movimiento_pago ON caja_movimiento(pago_id) WHERE pago_id IS NOT NULL;
        CREATE INDEX ix_caja_movimiento_operacion ON caja_movimiento(caja_operacion_id) WHERE caja_operacion_id IS NOT NULL;
        CREATE INDEX ix_caja_movimiento_cambio_pago ON caja_movimiento(cambio_pago_id) WHERE cambio_pago_id IS NOT NULL;
        CREATE UNIQUE INDEX ux_caja_movimiento_cambio_pago ON caja_movimiento(cambio_pago_id) WHERE cambio_pago_id IS NOT NULL;
        CREATE UNIQUE INDEX ux_caja_movimiento_pago_efecto ON caja_movimiento(caja_operacion_id,pago_id,tipo,medio) WHERE caja_operacion_id IS NOT NULL AND pago_id IS NOT NULL;
        CREATE UNIQUE INDEX ux_caja_movimiento_pago_venta ON caja_movimiento(pago_id) WHERE pago_id IS NOT NULL AND tipo='venta';

        CREATE TRIGGER trg_caja_movimiento_sesion_abierta BEFORE INSERT ON caja_movimiento
        WHEN NOT EXISTS (SELECT 1 FROM caja_sesion s WHERE s.id=NEW.sesion_id AND s.estado='abierta')
        BEGIN SELECT RAISE(ABORT,'la sesión de caja está cerrada'); END;
        CREATE TRIGGER trg_caja_movimiento_operacion_coherente BEFORE INSERT ON caja_movimiento
        WHEN NEW.caja_operacion_id IS NOT NULL AND NOT EXISTS (
          SELECT 1 FROM caja_operacion o WHERE o.id=NEW.caja_operacion_id AND o.sesion_id=NEW.sesion_id
          AND ((NEW.tipo='venta' AND o.tipo='venta') OR (NEW.tipo IN ('ingreso','egreso') AND o.tipo='movimiento_manual')
          OR (NEW.tipo='reintegro' AND o.tipo IN ('anulacion','reintegro')) OR (NEW.tipo='correccion' AND o.tipo='correccion_medio')
          OR (NEW.tipo='cambio_cobro' AND o.tipo='movimiento_manual')
          OR (NEW.tipo='cambio_reintegro' AND o.tipo='reintegro')))
        BEGIN SELECT RAISE(ABORT,'el movimiento no corresponde a la operación o sesión de caja'); END;
        CREATE TRIGGER trg_caja_movimiento_datos_coherentes BEFORE INSERT ON caja_movimiento
        WHEN (NEW.tipo='venta' AND (NEW.pago_id IS NULL OR NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.categoria IS NOT NULL OR NEW.importe_minor<=0
          OR NOT EXISTS(SELECT 1 FROM pago p WHERE p.id=NEW.pago_id AND p.venta_id=NEW.venta_id AND p.medio=NEW.medio AND p.importe_minor=NEW.importe_minor)))
          OR (NEW.tipo='ingreso' AND (COALESCE(NEW.categoria,'') NOT IN ('cambio_fondo','otro_ingreso') OR NEW.importe_minor<=0 OR NEW.medio<>'efectivo' OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.venta_id IS NOT NULL OR NEW.pago_id IS NOT NULL OR trim(COALESCE(NEW.motivo,''))=''))
          OR (NEW.tipo='egreso' AND (COALESCE(NEW.categoria,'') NOT IN ('gasto','retiro_propietario','otro_egreso') OR NEW.importe_minor>=0 OR NEW.medio<>'efectivo' OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.venta_id IS NOT NULL OR NEW.pago_id IS NOT NULL OR trim(COALESCE(NEW.motivo,''))=''))
          OR (NEW.tipo='reintegro' AND (NEW.pago_id IS NULL OR NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.importe_minor>=0 OR trim(COALESCE(NEW.motivo,''))='' OR NOT EXISTS(SELECT 1 FROM pago p WHERE p.id=NEW.pago_id AND p.venta_id=NEW.venta_id)
             OR COALESCE(-NEW.importe_minor,0)+COALESCE((SELECT -SUM(m.importe_minor) FROM caja_movimiento m WHERE m.pago_id=NEW.pago_id AND m.tipo='reintegro'),0)>COALESCE((SELECT p.importe_minor FROM pago p WHERE p.id=NEW.pago_id),0)))
          OR (NEW.tipo='correccion' AND (NEW.pago_id IS NULL OR NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR trim(COALESCE(NEW.motivo,''))='' OR NOT EXISTS(SELECT 1 FROM pago_correccion pc WHERE pc.pago_id=NEW.pago_id AND pc.caja_operacion_id=NEW.caja_operacion_id AND ((NEW.medio=pc.medio_anterior AND NEW.importe_minor=-pc.importe_minor) OR (NEW.medio=pc.medio_nuevo AND NEW.importe_minor=pc.importe_minor)))))
          OR (NEW.tipo IN ('cambio_cobro','cambio_reintegro') AND (NEW.cambio_pago_id IS NULL OR NEW.medio IS NULL OR NEW.usuario_id IS NULL OR NEW.caja_operacion_id IS NULL
          OR NOT EXISTS(SELECT 1 FROM cambio_pago p WHERE p.id=NEW.cambio_pago_id AND p.medio=NEW.medio AND p.importe_minor=abs(NEW.importe_minor)
          AND ((NEW.tipo='cambio_cobro' AND p.direccion='cobro') OR (NEW.tipo='cambio_reintegro' AND p.direccion='reintegro')))))
        BEGIN SELECT RAISE(ABORT,'los datos del movimiento de caja no son coherentes'); END;
        CREATE TRIGGER trg_caja_movimiento_no_editar BEFORE UPDATE ON caja_movimiento BEGIN SELECT RAISE(ABORT,'el movimiento de caja es inmutable'); END;
        CREATE TRIGGER trg_caja_movimiento_no_borrar BEFORE DELETE ON caja_movimiento BEGIN SELECT RAISE(ABORT,'el movimiento de caja es histórico'); END;
        """;

    private const string SqlV13 = """
        CREATE TABLE cliente (
            id TEXT PRIMARY KEY,
            nombre_completo TEXT NOT NULL CHECK (trim(nombre_completo) <> ''),
            telefono TEXT NULL,
            observacion TEXT NULL,
            activo INTEGER NOT NULL DEFAULT 1 CHECK (activo IN (0,1)),
            creado_en TEXT NOT NULL,
            actualizado_en TEXT NOT NULL
        );
        CREATE INDEX ix_cliente_nombre ON cliente(nombre_completo COLLATE NOCASE, id);
        CREATE INDEX ix_cliente_activo_nombre ON cliente(activo, nombre_completo COLLATE NOCASE, id);
        CREATE INDEX ix_cliente_telefono ON cliente(telefono COLLATE NOCASE, id);

        ALTER TABLE venta ADD COLUMN cliente_id TEXT NULL REFERENCES cliente(id) ON DELETE RESTRICT;
        ALTER TABLE venta ADD COLUMN cliente_nombre_snapshot TEXT NULL;
        CREATE INDEX ix_venta_cliente_fecha ON venta(cliente_id, creada_en DESC, id DESC) WHERE cliente_id IS NOT NULL;
        """;

    private const string SqlV14 = """
        ALTER TABLE cliente ADD COLUMN limite_credito_minor INTEGER NULL CHECK (limite_credito_minor IS NULL OR limite_credito_minor >= 0);
        ALTER TABLE cambio ADD COLUMN decision_cuenta TEXT NULL CHECK (decision_cuenta IS NULL OR decision_cuenta IN ('aplicar_a_deuda','reintegrar'));
        ALTER TABLE cambio ADD COLUMN deuda_antes_minor INTEGER NULL CHECK (deuda_antes_minor IS NULL OR deuda_antes_minor >= 0);
        ALTER TABLE cambio ADD COLUMN importe_aplicado_deuda_minor INTEGER NOT NULL DEFAULT 0 CHECK (importe_aplicado_deuda_minor >= 0);
        ALTER TABLE cambio ADD COLUMN deuda_despues_minor INTEGER NULL CHECK (deuda_despues_minor IS NULL OR deuda_despues_minor >= 0);

        CREATE TABLE cuenta_corriente_abono (
            id TEXT PRIMARY KEY,
            numero TEXT NOT NULL UNIQUE CHECK (numero GLOB 'A-[0-9][0-9][0-9][0-9][0-9][0-9]'),
            cliente_id TEXT NOT NULL REFERENCES cliente(id) ON DELETE RESTRICT,
            importe_minor INTEGER NOT NULL CHECK (importe_minor > 0),
            saldo_anterior_minor INTEGER NOT NULL CHECK (saldo_anterior_minor >= importe_minor),
            saldo_posterior_minor INTEGER NOT NULL CHECK (saldo_posterior_minor = saldo_anterior_minor-importe_minor),
            operacion_id TEXT NOT NULL UNIQUE CHECK (trim(operacion_id) <> ''),
            usuario_id TEXT NOT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL
        );
        CREATE INDEX ix_cc_abono_cliente_fecha ON cuenta_corriente_abono(cliente_id, creado_en DESC, id DESC);

        CREATE TABLE cuenta_corriente_abono_componente (
            id TEXT PRIMARY KEY,
            abono_id TEXT NOT NULL REFERENCES cuenta_corriente_abono(id) ON DELETE RESTRICT,
            medio TEXT NOT NULL CHECK (medio IN ('efectivo','transferencia','debito','credito','billetera')),
            importe_minor INTEGER NOT NULL CHECK (importe_minor > 0),
            recibido_minor INTEGER NULL,
            CHECK ((medio='efectivo' AND recibido_minor IS NOT NULL AND recibido_minor>=importe_minor)
                OR (medio<>'efectivo' AND recibido_minor IS NULL))
        );
        CREATE INDEX ix_cc_abono_componente_abono ON cuenta_corriente_abono_componente(abono_id,id);

        CREATE TABLE cuenta_corriente_movimiento (
            id TEXT PRIMARY KEY,
            cliente_id TEXT NOT NULL REFERENCES cliente(id) ON DELETE RESTRICT,
            tipo TEXT NOT NULL CHECK (tipo IN ('cargo_venta','abono','ajuste_positivo','ajuste_negativo','credito_devolucion','cancelacion_deuda')),
            importe_minor INTEGER NOT NULL CHECK (importe_minor <> 0),
            venta_id TEXT NULL REFERENCES venta(id) ON DELETE RESTRICT,
            cambio_id TEXT NULL REFERENCES cambio(id) ON DELETE RESTRICT,
            abono_id TEXT NULL REFERENCES cuenta_corriente_abono(id) ON DELETE RESTRICT,
            operacion_id TEXT NOT NULL UNIQUE CHECK (trim(operacion_id) <> ''),
            vencimiento TEXT NULL,
            motivo TEXT NULL,
            usuario_id TEXT NOT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            creado_en TEXT NOT NULL,
            CHECK ((tipo='cargo_venta' AND importe_minor>0 AND venta_id IS NOT NULL AND cambio_id IS NULL AND abono_id IS NULL)
                OR (tipo='abono' AND importe_minor<0 AND venta_id IS NULL AND cambio_id IS NULL AND abono_id IS NOT NULL)
                OR (tipo='ajuste_positivo' AND importe_minor>0 AND venta_id IS NULL AND cambio_id IS NULL AND abono_id IS NULL AND trim(COALESCE(motivo,''))<>'')
                OR (tipo='ajuste_negativo' AND importe_minor<0 AND venta_id IS NULL AND cambio_id IS NULL AND abono_id IS NULL AND trim(COALESCE(motivo,''))<>'')
                OR (tipo='credito_devolucion' AND importe_minor<0 AND venta_id IS NOT NULL AND cambio_id IS NOT NULL AND abono_id IS NULL)
                OR (tipo='cancelacion_deuda' AND importe_minor<0 AND venta_id IS NOT NULL AND cambio_id IS NULL AND abono_id IS NULL))
        );
        CREATE UNIQUE INDEX ux_cc_cargo_venta ON cuenta_corriente_movimiento(venta_id) WHERE tipo='cargo_venta';
        CREATE INDEX ix_cc_mov_cliente_fecha ON cuenta_corriente_movimiento(cliente_id, creado_en DESC, id DESC);
        CREATE INDEX ix_cc_mov_venta ON cuenta_corriente_movimiento(venta_id) WHERE venta_id IS NOT NULL;
        CREATE INDEX ix_cc_mov_cambio ON cuenta_corriente_movimiento(cambio_id) WHERE cambio_id IS NOT NULL;

        CREATE TABLE cuenta_corriente_aplicacion (
            id TEXT PRIMARY KEY,
            movimiento_reductor_id TEXT NOT NULL REFERENCES cuenta_corriente_movimiento(id) ON DELETE RESTRICT,
            movimiento_cargo_id TEXT NOT NULL REFERENCES cuenta_corriente_movimiento(id) ON DELETE RESTRICT,
            abono_componente_id TEXT NULL REFERENCES cuenta_corriente_abono_componente(id) ON DELETE RESTRICT,
            importe_minor INTEGER NOT NULL CHECK (importe_minor > 0),
            saldo_cargo_posterior_minor INTEGER NULL CHECK (saldo_cargo_posterior_minor IS NULL OR saldo_cargo_posterior_minor >= 0),
            creado_en TEXT NOT NULL,
            UNIQUE (movimiento_reductor_id,movimiento_cargo_id,abono_componente_id)
        );
        CREATE INDEX ix_cc_aplicacion_cargo ON cuenta_corriente_aplicacion(movimiento_cargo_id);
        CREATE INDEX ix_cc_aplicacion_reductor ON cuenta_corriente_aplicacion(movimiento_reductor_id);
        CREATE INDEX ix_cc_aplicacion_componente ON cuenta_corriente_aplicacion(abono_componente_id) WHERE abono_componente_id IS NOT NULL;

        CREATE TABLE secuencia_abono (
            id INTEGER PRIMARY KEY CHECK (id=1),
            ultimo_numero INTEGER NOT NULL CHECK (ultimo_numero>=0)
        );
        INSERT INTO secuencia_abono(id,ultimo_numero) VALUES (1,0);

        -- Se amplía la tabla sin tocar movimientos existentes. El self-FK de
        -- movimiento_origen se vuelve a declarar apuntando a la tabla nueva.
        DROP TRIGGER trg_caja_movimiento_sesion_abierta;
        DROP TRIGGER trg_caja_movimiento_datos_coherentes;
        DROP TRIGGER trg_caja_movimiento_operacion_coherente;
        DROP TRIGGER trg_caja_movimiento_no_editar;
        DROP TRIGGER trg_caja_movimiento_no_borrar;
        CREATE TABLE caja_movimiento_nueva (
            id TEXT PRIMARY KEY, sesion_id TEXT NOT NULL REFERENCES caja_sesion(id) ON DELETE RESTRICT,
            tipo TEXT NOT NULL CHECK (tipo IN ('venta','ingreso','egreso','reintegro','correccion','cambio_cobro','cambio_reintegro','abono')),
            importe_minor INTEGER NOT NULL CHECK (importe_minor <> 0),
            venta_id TEXT NULL REFERENCES venta(id) ON DELETE RESTRICT,
            motivo TEXT NULL, creado_en TEXT NOT NULL,
            caja_operacion_id TEXT NULL REFERENCES caja_operacion(id) ON DELETE RESTRICT,
            pago_id TEXT NULL REFERENCES pago(id) ON DELETE RESTRICT,
            medio TEXT NULL CHECK (medio IS NULL OR medio IN ('efectivo','transferencia','debito','credito','billetera')),
            categoria TEXT NULL, usuario_id TEXT NULL REFERENCES usuario(id) ON DELETE RESTRICT,
            movimiento_origen_id TEXT NULL REFERENCES caja_movimiento_nueva(id) ON DELETE RESTRICT,
            cambio_pago_id TEXT NULL REFERENCES cambio_pago(id) ON DELETE RESTRICT,
            abono_componente_id TEXT NULL REFERENCES cuenta_corriente_abono_componente(id) ON DELETE RESTRICT,
            CHECK (tipo <> 'venta' OR venta_id IS NOT NULL),
            CHECK ((tipo = 'cambio_cobro' AND importe_minor > 0 AND cambio_pago_id IS NOT NULL) OR tipo <> 'cambio_cobro'),
            CHECK ((tipo = 'cambio_reintegro' AND importe_minor < 0 AND cambio_pago_id IS NOT NULL) OR tipo <> 'cambio_reintegro'),
            CHECK ((tipo = 'abono' AND importe_minor > 0 AND abono_componente_id IS NOT NULL) OR tipo <> 'abono')
        );
        INSERT INTO caja_movimiento_nueva (id,sesion_id,tipo,importe_minor,venta_id,motivo,creado_en,caja_operacion_id,pago_id,medio,categoria,usuario_id,movimiento_origen_id,cambio_pago_id)
            SELECT id,sesion_id,tipo,importe_minor,venta_id,motivo,creado_en,caja_operacion_id,pago_id,medio,categoria,usuario_id,movimiento_origen_id,cambio_pago_id FROM caja_movimiento;
        DROP TABLE caja_movimiento;
        ALTER TABLE caja_movimiento_nueva RENAME TO caja_movimiento;
        CREATE INDEX ix_caja_movimiento_sesion ON caja_movimiento(sesion_id);
        CREATE INDEX ix_caja_movimiento_sesion_fecha ON caja_movimiento(sesion_id,creado_en DESC,id DESC);
        CREATE INDEX ix_caja_movimiento_venta ON caja_movimiento(venta_id) WHERE venta_id IS NOT NULL;
        CREATE INDEX ix_caja_movimiento_pago ON caja_movimiento(pago_id) WHERE pago_id IS NOT NULL;
        CREATE INDEX ix_caja_movimiento_operacion ON caja_movimiento(caja_operacion_id) WHERE caja_operacion_id IS NOT NULL;
        CREATE INDEX ix_caja_movimiento_cambio_pago ON caja_movimiento(cambio_pago_id) WHERE cambio_pago_id IS NOT NULL;
        CREATE INDEX ix_caja_movimiento_abono_componente ON caja_movimiento(abono_componente_id) WHERE abono_componente_id IS NOT NULL;
        CREATE UNIQUE INDEX ux_caja_movimiento_cambio_pago ON caja_movimiento(cambio_pago_id) WHERE cambio_pago_id IS NOT NULL;
        CREATE UNIQUE INDEX ux_caja_movimiento_pago_efecto ON caja_movimiento(caja_operacion_id,pago_id,tipo,medio) WHERE caja_operacion_id IS NOT NULL AND pago_id IS NOT NULL;
        CREATE UNIQUE INDEX ux_caja_movimiento_pago_venta ON caja_movimiento(pago_id) WHERE pago_id IS NOT NULL AND tipo='venta';
        CREATE UNIQUE INDEX ux_caja_movimiento_abono_componente ON caja_movimiento(caja_operacion_id,abono_componente_id) WHERE tipo='abono';

        CREATE TRIGGER trg_caja_movimiento_sesion_abierta BEFORE INSERT ON caja_movimiento
        WHEN NOT EXISTS (SELECT 1 FROM caja_sesion s WHERE s.id=NEW.sesion_id AND s.estado='abierta')
        BEGIN SELECT RAISE(ABORT,'la sesión de caja está cerrada'); END;
        CREATE TRIGGER trg_caja_movimiento_operacion_coherente BEFORE INSERT ON caja_movimiento
        WHEN NEW.caja_operacion_id IS NOT NULL AND NOT EXISTS (
          SELECT 1 FROM caja_operacion o WHERE o.id=NEW.caja_operacion_id AND o.sesion_id=NEW.sesion_id
          AND ((NEW.tipo='venta' AND o.tipo='venta') OR (NEW.tipo IN ('ingreso','egreso','abono') AND o.tipo='movimiento_manual')
          OR (NEW.tipo='reintegro' AND o.tipo IN ('anulacion','reintegro')) OR (NEW.tipo='correccion' AND o.tipo='correccion_medio')
          OR (NEW.tipo='cambio_cobro' AND o.tipo='movimiento_manual') OR (NEW.tipo='cambio_reintegro' AND o.tipo='reintegro')))
        BEGIN SELECT RAISE(ABORT,'el movimiento no corresponde a la operación o sesión de caja'); END;
        CREATE TRIGGER trg_caja_movimiento_datos_coherentes BEFORE INSERT ON caja_movimiento
        WHEN (NEW.tipo='venta' AND (NEW.pago_id IS NULL OR NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.categoria IS NOT NULL OR NEW.importe_minor<=0
          OR NOT EXISTS(SELECT 1 FROM pago p WHERE p.id=NEW.pago_id AND p.venta_id=NEW.venta_id AND p.medio=NEW.medio AND p.importe_minor=NEW.importe_minor)))
          OR (NEW.tipo='ingreso' AND (COALESCE(NEW.categoria,'') NOT IN ('cambio_fondo','otro_ingreso') OR NEW.importe_minor<=0 OR NEW.medio<>'efectivo' OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.venta_id IS NOT NULL OR NEW.pago_id IS NOT NULL OR trim(COALESCE(NEW.motivo,''))=''))
          OR (NEW.tipo='egreso' AND (COALESCE(NEW.categoria,'') NOT IN ('gasto','retiro_propietario','otro_egreso') OR NEW.importe_minor>=0 OR NEW.medio<>'efectivo' OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.venta_id IS NOT NULL OR NEW.pago_id IS NOT NULL OR trim(COALESCE(NEW.motivo,''))=''))
          OR (NEW.tipo='reintegro' AND (NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NEW.importe_minor>=0 OR trim(COALESCE(NEW.motivo,''))=''
             OR NOT ((NEW.pago_id IS NOT NULL AND NEW.abono_componente_id IS NULL AND EXISTS(SELECT 1 FROM pago p WHERE p.id=NEW.pago_id AND p.venta_id=NEW.venta_id)
                 AND COALESCE(-NEW.importe_minor,0)+COALESCE((SELECT -SUM(m.importe_minor) FROM caja_movimiento m WHERE m.pago_id=NEW.pago_id AND m.tipo='reintegro'),0)<=COALESCE((SELECT p.importe_minor FROM pago p WHERE p.id=NEW.pago_id),0))
               OR (NEW.pago_id IS NULL AND NEW.abono_componente_id IS NOT NULL AND NEW.venta_id IS NOT NULL
                 AND EXISTS(SELECT 1 FROM cuenta_corriente_abono_componente ac JOIN cuenta_corriente_aplicacion ap ON ap.abono_componente_id=ac.id JOIN cuenta_corriente_movimiento cargo ON cargo.id=ap.movimiento_cargo_id WHERE ac.id=NEW.abono_componente_id AND cargo.venta_id=NEW.venta_id AND ac.medio=NEW.medio)
                 AND COALESCE(-NEW.importe_minor,0)+COALESCE((SELECT -SUM(m.importe_minor) FROM caja_movimiento m WHERE m.abono_componente_id=NEW.abono_componente_id AND m.tipo='reintegro'),0)<=COALESCE((SELECT ac.importe_minor FROM cuenta_corriente_abono_componente ac WHERE ac.id=NEW.abono_componente_id),0)))))
          OR (NEW.tipo='correccion' AND (NEW.pago_id IS NULL OR NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR trim(COALESCE(NEW.motivo,''))='' OR NOT EXISTS(SELECT 1 FROM pago_correccion pc WHERE pc.pago_id=NEW.pago_id AND pc.caja_operacion_id=NEW.caja_operacion_id AND ((NEW.medio=pc.medio_anterior AND NEW.importe_minor=-pc.importe_minor) OR (NEW.medio=pc.medio_nuevo AND NEW.importe_minor=pc.importe_minor)))))
          OR (NEW.tipo IN ('cambio_cobro','cambio_reintegro') AND (NEW.cambio_pago_id IS NULL OR NEW.medio IS NULL OR NEW.usuario_id IS NULL OR NEW.caja_operacion_id IS NULL OR NOT EXISTS(SELECT 1 FROM cambio_pago p WHERE p.id=NEW.cambio_pago_id AND p.medio=NEW.medio AND p.importe_minor=abs(NEW.importe_minor) AND ((NEW.tipo='cambio_cobro' AND p.direccion='cobro') OR (NEW.tipo='cambio_reintegro' AND p.direccion='reintegro')))))
          OR (NEW.tipo='abono' AND (NEW.importe_minor<=0 OR NEW.venta_id IS NOT NULL OR NEW.pago_id IS NOT NULL OR NEW.cambio_pago_id IS NOT NULL OR NEW.categoria IS NOT NULL OR NEW.abono_componente_id IS NULL OR NEW.medio IS NULL OR NEW.caja_operacion_id IS NULL OR NEW.usuario_id IS NULL OR NOT EXISTS(SELECT 1 FROM cuenta_corriente_abono_componente c WHERE c.id=NEW.abono_componente_id AND c.medio=NEW.medio AND c.importe_minor=NEW.importe_minor)))
        BEGIN SELECT RAISE(ABORT,'los datos del movimiento de caja no son coherentes'); END;
        CREATE TRIGGER trg_caja_movimiento_no_editar BEFORE UPDATE ON caja_movimiento BEGIN SELECT RAISE(ABORT,'el movimiento de caja es inmutable'); END;
        CREATE TRIGGER trg_caja_movimiento_no_borrar BEFORE DELETE ON caja_movimiento BEGIN SELECT RAISE(ABORT,'el movimiento de caja es histórico'); END;

        CREATE TRIGGER trg_cc_movimiento_coherente BEFORE INSERT ON cuenta_corriente_movimiento
        WHEN (NEW.tipo='cargo_venta' AND (NOT EXISTS(SELECT 1 FROM venta v WHERE v.id=NEW.venta_id AND v.cliente_id=NEW.cliente_id AND v.estado='confirmada')
              OR NEW.importe_minor != COALESCE((SELECT v.total_minor-COALESCE((SELECT SUM(p.importe_minor) FROM pago p WHERE p.venta_id=v.id),0) FROM venta v WHERE v.id=NEW.venta_id),-1)))
          OR (NEW.tipo='abono' AND (NEW.importe_minor != -(SELECT a.importe_minor FROM cuenta_corriente_abono a WHERE a.id=NEW.abono_id AND a.cliente_id=NEW.cliente_id)
              OR (SELECT COALESCE(SUM(c.importe_minor),0) FROM cuenta_corriente_abono_componente c WHERE c.abono_id=NEW.abono_id) != -NEW.importe_minor))
          OR (NEW.tipo='credito_devolucion' AND (NOT EXISTS(SELECT 1 FROM cambio c JOIN venta v ON v.id=c.venta_id WHERE c.id=NEW.cambio_id AND c.venta_id=NEW.venta_id AND v.cliente_id=NEW.cliente_id)
              OR NEW.importe_minor >= 0))
          OR (NEW.tipo IN ('cancelacion_deuda','credito_devolucion') AND NEW.venta_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM venta v WHERE v.id=NEW.venta_id AND v.cliente_id=NEW.cliente_id))
        BEGIN SELECT RAISE(ABORT,'el movimiento de cuenta corriente no coincide con sus referencias'); END;

        CREATE TRIGGER trg_cc_aplicacion_coherente BEFORE INSERT ON cuenta_corriente_aplicacion
        WHEN NOT EXISTS(SELECT 1 FROM cuenta_corriente_movimiento red JOIN cuenta_corriente_movimiento cargo ON cargo.id=NEW.movimiento_cargo_id
             WHERE red.id=NEW.movimiento_reductor_id AND red.importe_minor<0 AND cargo.tipo IN ('cargo_venta','ajuste_positivo') AND cargo.importe_minor>0 AND red.cliente_id=cargo.cliente_id)
          OR (NEW.abono_componente_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM cuenta_corriente_abono_componente c JOIN cuenta_corriente_movimiento red ON red.abono_id=c.abono_id WHERE c.id=NEW.abono_componente_id AND red.id=NEW.movimiento_reductor_id AND red.tipo='abono'))
          OR COALESCE((SELECT SUM(importe_minor) FROM cuenta_corriente_aplicacion WHERE movimiento_reductor_id=NEW.movimiento_reductor_id),0)+NEW.importe_minor > -(SELECT importe_minor FROM cuenta_corriente_movimiento WHERE id=NEW.movimiento_reductor_id)
          OR COALESCE((SELECT SUM(importe_minor) FROM cuenta_corriente_aplicacion WHERE movimiento_cargo_id=NEW.movimiento_cargo_id),0)+NEW.importe_minor > (SELECT importe_minor FROM cuenta_corriente_movimiento WHERE id=NEW.movimiento_cargo_id)
          OR (NEW.abono_componente_id IS NOT NULL AND COALESCE((SELECT SUM(importe_minor) FROM cuenta_corriente_aplicacion WHERE abono_componente_id=NEW.abono_componente_id),0)+NEW.importe_minor > (SELECT importe_minor FROM cuenta_corriente_abono_componente WHERE id=NEW.abono_componente_id))
        BEGIN SELECT RAISE(ABORT,'la asignación supera el reductor o el saldo del cargo'); END;

        CREATE TRIGGER trg_cc_movimiento_no_editar BEFORE UPDATE ON cuenta_corriente_movimiento BEGIN SELECT RAISE(ABORT,'el movimiento de cuenta corriente es inmutable'); END;
        CREATE TRIGGER trg_cc_movimiento_no_borrar BEFORE DELETE ON cuenta_corriente_movimiento BEGIN SELECT RAISE(ABORT,'el movimiento de cuenta corriente es histórico'); END;
        CREATE TRIGGER trg_cc_abono_no_editar BEFORE UPDATE ON cuenta_corriente_abono BEGIN SELECT RAISE(ABORT,'el recibo de abono es inmutable'); END;
        CREATE TRIGGER trg_cc_abono_no_borrar BEFORE DELETE ON cuenta_corriente_abono BEGIN SELECT RAISE(ABORT,'el recibo de abono es histórico'); END;
        CREATE TRIGGER trg_cc_componente_no_editar BEFORE UPDATE ON cuenta_corriente_abono_componente BEGIN SELECT RAISE(ABORT,'el componente del abono es inmutable'); END;
        CREATE TRIGGER trg_cc_componente_no_borrar BEFORE DELETE ON cuenta_corriente_abono_componente BEGIN SELECT RAISE(ABORT,'el componente del abono es histórico'); END;
        CREATE TRIGGER trg_cc_aplicacion_no_editar BEFORE UPDATE ON cuenta_corriente_aplicacion BEGIN SELECT RAISE(ABORT,'la aplicación de cuenta corriente es inmutable'); END;
        CREATE TRIGGER trg_cc_aplicacion_no_borrar BEFORE DELETE ON cuenta_corriente_aplicacion BEGIN SELECT RAISE(ABORT,'la aplicación de cuenta corriente es histórica'); END;
        """;

    /// <summary>
    /// V15 solo índices, sin tablas ni cambios a V14. Justificado por medición
    /// F09-T05: SinVentasAsync tardaba ~22 s en 1200 variantes/15000 líneas
    /// por full scan de venta_linea en la subconsulta correlacionada
    /// (no existía índice por variante_id).
    /// </summary>
    private const string SqlV15 = """
        CREATE INDEX IF NOT EXISTS ix_venta_linea_variante ON venta_linea(variante_id);
        """;
}
