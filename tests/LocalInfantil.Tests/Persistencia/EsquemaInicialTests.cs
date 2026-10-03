using LocalInfantil.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Persistencia;

/// <summary>
/// Pruebas del esquema inicial F01-T01. Todas usan bases temporales aisladas
/// que se eliminan al terminar; nunca tocan la base provisional del equipo.
/// </summary>
public sealed class EsquemaInicialTests
{
    [Fact]
    public async Task Aplica_migracion_sobre_base_vacia_y_registra_version()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync(CatalogoV1(), null);

            Assert.Equal(new[] { 1 }, aplicadas);

            await using var conexion = await AbrirConexionAsync(ruta);
            var tablas = new HashSet<string>(StringComparer.Ordinal);
            using (var consulta = conexion.CreateCommand())
            {
                consulta.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
                await using var lector = await consulta.ExecuteReaderAsync();
                while (await lector.ReadAsync())
                {
                    tablas.Add(lector.GetString(0));
                }
            }

            foreach (var esperada in new[]
            {
                "schema_migrations", "usuario", "producto", "variante", "precio",
                "movimiento_inventario", "venta", "venta_linea", "pago",
                "caja_sesion", "caja_movimiento",
            })
            {
                Assert.Contains(esperada, tablas);
            }

            using var version = conexion.CreateCommand();
            version.CommandText = "SELECT version FROM schema_migrations;";
            var registrada = await version.ExecuteScalarAsync();
            Assert.Equal(1L, registrada);
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Segunda_ejecucion_no_aplica_nada()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            var migrador = new MigradorBaseDatos(ruta);

            var primera = await migrador.MigrarAsync(CatalogoV1(), null);
            var segunda = await migrador.MigrarAsync(CatalogoV1(), null);

            Assert.Equal(new[] { 1 }, primera);
            Assert.Empty(segunda);
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Claves_foraneas_rechazan_referencias_inexistentes()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            await using var conexion = await AbrirConexionAsync(ruta);

            var movimientoHuerfano = CrearComando(
                conexion,
                "INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, operacion_id, creado_en) VALUES ($id, $variante, 'ingreso', 1, $op, $ahora);",
                ("$id", NuevoId()), ("$variante", NuevoId()), ("$op", NuevoId()), ("$ahora", Ahora()));
            await Assert.ThrowsAsync<SqliteException>(() => movimientoHuerfano.ExecuteNonQueryAsync());

            var varianteHuerfana = CrearComando(
                conexion,
                "INSERT INTO variante (id, producto_id, sku, codigo_interno, creado_en) VALUES ($id, $producto, $sku, $codigo, $ahora);",
                ("$id", NuevoId()), ("$producto", NuevoId()), ("$sku", "SKU-" + NuevoId()), ("$codigo", "COD-" + NuevoId()), ("$ahora", Ahora()));
            await Assert.ThrowsAsync<SqliteException>(() => varianteHuerfana.ExecuteNonQueryAsync());
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Sku_duplicado_y_restricciones_rechazados()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            await using var conexion = await AbrirConexionAsync(ruta);

            var productoId = NuevoId();
            await CrearComando(
                conexion,
                "INSERT INTO producto (id, codigo_interno, nombre, categoria_id, creado_en) VALUES ($id, 'REM-001', 'Remera', 'cat-rem', $ahora);",
                ("$id", productoId), ("$ahora", Ahora())).ExecuteNonQueryAsync();

            const string sku = "REM-UNICO-001";
            await CrearComando(
                conexion,
                "INSERT INTO variante (id, producto_id, sku, codigo_interno, talle, talle_norm, color_norm, creado_en) VALUES ($id, $producto, $sku, $codigo, '6', '6', '', $ahora);",
                ("$id", NuevoId()), ("$producto", productoId), ("$sku", sku), ("$codigo", "REM-001-01"), ("$ahora", Ahora())).ExecuteNonQueryAsync();

            // Mismo SKU con distinta norma y código: solo viola variante.sku.
            var duplicado = CrearComando(
                conexion,
                "INSERT INTO variante (id, producto_id, sku, codigo_interno, talle, talle_norm, color_norm, creado_en) VALUES ($id, $producto, $sku, $codigo, '8', '8', '', $ahora);",
                ("$id", NuevoId()), ("$producto", productoId), ("$sku", sku), ("$codigo", "REM-001-02"), ("$ahora", Ahora()));
            var errorDuplicado = await Assert.ThrowsAsync<SqliteException>(() => duplicado.ExecuteNonQueryAsync());
            Assert.Contains("variante.sku", errorDuplicado.Message);

            // Mismo código con distinto SKU y norma: solo viola el código.
            var codigoDuplicado = CrearComando(
                conexion,
                "INSERT INTO variante (id, producto_id, sku, codigo_interno, talle, talle_norm, color_norm, creado_en) VALUES ($id, $producto, $sku2, $codigo, '4', '4', '', $ahora);",
                ("$id", NuevoId()), ("$producto", productoId), ("$sku2", "REM-UNICO-002"), ("$codigo", "REM-001-01"), ("$ahora", Ahora()));
            var errorCodigo = await Assert.ThrowsAsync<SqliteException>(() => codigoDuplicado.ExecuteNonQueryAsync());
            Assert.Contains("variante.codigo_interno", errorCodigo.Message);

            // Misma norma Talle+Color con distinto texto visible/código:
            // viola la unicidad lógica aunque el texto difiera en espacios.
            var talleColorDuplicado = CrearComando(
                conexion,
                "INSERT INTO variante (id, producto_id, sku, codigo_interno, talle, talle_norm, color_norm, creado_en) VALUES ($id, $producto, $sku3, $codigo3, ' 6 ', '6', '', $ahora);",
                ("$id", NuevoId()), ("$producto", productoId), ("$sku3", "REM-UNICO-003"), ("$codigo3", "REM-001-03"), ("$ahora", Ahora()));
            var errorTalleColor = await Assert.ThrowsAsync<SqliteException>(() => talleColorDuplicado.ExecuteNonQueryAsync());
            Assert.Contains("talle_norm", errorTalleColor.Message);

            var precioNegativo = CrearComando(
                conexion,
                "INSERT INTO precio (id, variante_id, importe_minor, vigente_desde, creado_en) VALUES ($id, (SELECT id FROM variante WHERE sku = $sku), -5, $ahora, $ahora);",
                ("$id", NuevoId()), ("$sku", sku), ("$ahora", Ahora()));
            await Assert.ThrowsAsync<SqliteException>(() => precioNegativo.ExecuteNonQueryAsync());

            var ajusteSinMotivo = CrearComando(
                conexion,
                "INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, operacion_id, creado_en) VALUES ($id, (SELECT id FROM variante WHERE sku = $sku), 'ajuste', 2, $op, $ahora);",
                ("$id", NuevoId()), ("$sku", sku), ("$op", NuevoId()), ("$ahora", Ahora()));
            await Assert.ThrowsAsync<SqliteException>(() => ajusteSinMotivo.ExecuteNonQueryAsync());
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Stock_se_explica_como_suma_de_movimientos()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            await using var conexion = await AbrirConexionAsync(ruta);

            var productoId = NuevoId();
            await CrearComando(
                conexion,
                "INSERT INTO producto (id, codigo_interno, nombre, categoria_id, creado_en) VALUES ($id, 'PAN-001', 'Pantalón', 'cat-pan', $ahora);",
                ("$id", productoId), ("$ahora", Ahora())).ExecuteNonQueryAsync();

            var varianteId = NuevoId();
            await CrearComando(
                conexion,
                "INSERT INTO variante (id, producto_id, sku, codigo_interno, talle, creado_en) VALUES ($id, $producto, 'PAN-T4', 'PAN-001-01', '4', $ahora);",
                ("$id", varianteId), ("$producto", productoId), ("$ahora", Ahora())).ExecuteNonQueryAsync();

            await CrearComando(
                conexion,
                "INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, operacion_id, creado_en) VALUES ($id, $variante, 'ingreso', 10, $op, $ahora);",
                ("$id", NuevoId()), ("$variante", varianteId), ("$op", NuevoId()), ("$ahora", Ahora())).ExecuteNonQueryAsync();
            await CrearComando(
                conexion,
                "INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, operacion_id, creado_en) VALUES ($id, $variante, 'egreso', -3, $op, $ahora);",
                ("$id", NuevoId()), ("$variante", varianteId), ("$op", NuevoId()), ("$ahora", Ahora())).ExecuteNonQueryAsync();

            using var suma = conexion.CreateCommand();
            suma.CommandText = "SELECT SUM(cantidad) FROM movimiento_inventario WHERE variante_id = $variante;";
            suma.Parameters.AddWithValue("$variante", varianteId);
            Assert.Equal(7L, await suma.ExecuteScalarAsync());
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Actualiza_base_de_version_anterior_con_datos_y_conserva_registros()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV1 = CatalogoV1();
            Assert.Equal(new[] { 1 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV1, null));

            await InsertarRegistrosFicticiosAsync(ruta);

            var catalogoV2 = catalogoV1
                .Concat(new[]
                {
                    new Migracion(
                        2,
                        "simulacion-mejora-futura-solo-prueba",
                        "CREATE TABLE IF NOT EXISTS simulacion_futura (id TEXT PRIMARY KEY, nota TEXT NULL);"),
                })
                .ToList();
            var aplicadas = await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV2, respaldos);

            Assert.Equal(new[] { 2 }, aplicadas);

            var archivosRespaldo = Directory.GetFiles(respaldos, "*.db");
            Assert.Single(archivosRespaldo);
            Assert.Equal("ok", await ConsultarIntegridadAsync(archivosRespaldo[0]));

            await using var conexion = await AbrirConexionAsync(ruta);
            Assert.Equal("Empleada Ficticia", await ConsultarTextoAsync(conexion, "SELECT nombre FROM usuario WHERE id = 'u-ficticia';"));
            Assert.Equal("REM-FIC-001", await ConsultarTextoAsync(conexion, "SELECT sku FROM variante WHERE id = 'v-ficticia';"));
            using var suma = conexion.CreateCommand();
            suma.CommandText = "SELECT SUM(cantidad) FROM movimiento_inventario WHERE variante_id = 'v-ficticia';";
            Assert.Equal(5L, await suma.ExecuteScalarAsync());
            using var futura = conexion.CreateCommand();
            futura.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'simulacion_futura';";
            Assert.Equal(1L, await futura.ExecuteScalarAsync());

            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV2, respaldos));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Migrar_con_datos_sin_respaldo_se_niega()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            var catalogoV1 = CatalogoV1();
            await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV1, null);
            await InsertarRegistrosFicticiosAsync(ruta);

            var catalogoV2 = catalogoV1
                .Concat(new[]
                {
                    new Migracion(
                        2,
                        "simulacion-mejora-futura-solo-prueba",
                        "CREATE TABLE IF NOT EXISTS simulacion_futura (id TEXT PRIMARY KEY);"),
                })
                .ToList();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new MigradorBaseDatos(ruta).MigrarAsync(catalogoV2, null));

            await using var conexion = await AbrirConexionAsync(ruta);
            using var futura = conexion.CreateCommand();
            futura.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'simulacion_futura';";
            Assert.Equal(0L, await futura.ExecuteScalarAsync());
            Assert.Equal("Empleada Ficticia", await ConsultarTextoAsync(conexion, "SELECT nombre FROM usuario WHERE id = 'u-ficticia';"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Migracion_fallida_no_deja_estructura_parcial_y_conserva_datos()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV1 = CatalogoV1();
            await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV1, null);
            await InsertarRegistrosFicticiosAsync(ruta);

            var catalogoRoto = catalogoV1
                .Concat(new[] { new Migracion(2, "simulacion-rota-solo-prueba", "CREATE TABL rota (id TEXT;"), })
                .ToList();

            await Assert.ThrowsAsync<SqliteException>(
                () => new MigradorBaseDatos(ruta).MigrarAsync(catalogoRoto, respaldos));

            await using var conexion = await AbrirConexionAsync(ruta);
            var versiones = new List<long>();
            using (var consulta = conexion.CreateCommand())
            {
                consulta.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";
                await using var lector = await consulta.ExecuteReaderAsync();
                while (await lector.ReadAsync())
                {
                    versiones.Add(lector.GetInt64(0));
                }
            }

            Assert.Equal(new[] { 1L }, versiones);
            using var parcial = conexion.CreateCommand();
            parcial.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'rota';";
            Assert.Equal(0L, await parcial.ExecuteScalarAsync());
            Assert.Equal("Remera Ficticia", await ConsultarTextoAsync(conexion, "SELECT nombre FROM producto WHERE id = 'p-ficticio';"));

            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV1, respaldos));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Base_con_version_desconocida_se_rechaza_sin_aplicar_nada()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            var catalogoV1 = CatalogoV1();
            await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV1, null);
            await InsertarRegistrosFicticiosAsync(ruta);

            await using (var conexion = await AbrirConexionAsync(ruta))
            {
                using var adelanto = conexion.CreateCommand();
                adelanto.CommandText = "INSERT INTO schema_migrations (version, nombre, aplicada_en) VALUES (99, 'version-futura-desconocida', '2026-09-21T00:00:00Z');";
                await adelanto.ExecuteNonQueryAsync();
            }

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new MigradorBaseDatos(ruta).MigrarAsync(catalogoV1, null));
            Assert.Contains("99", error.Message);

            await using var verificacion = await AbrirConexionAsync(ruta);
            Assert.Equal("Remera Ficticia", await ConsultarTextoAsync(verificacion, "SELECT nombre FROM producto WHERE id = 'p-ficticio';"));
            using var bitacora = verificacion.CreateCommand();
            bitacora.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 99;";
            Assert.Equal(1L, await bitacora.ExecuteScalarAsync());
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Catalogo_con_version_duplicada_se_rechaza()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            var catalogoDuplicado = MigracionesF01.Todas
                .Concat(new[] { new Migracion(1, "duplicada-solo-prueba", "SELECT 1;"), })
                .ToList();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new MigradorBaseDatos(ruta).MigrarAsync(catalogoDuplicado, null));
            Assert.Contains("duplicada", error.Message);
            Assert.False(File.Exists(ruta));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Base_heredada_sin_historial_se_rechaza_sin_modificarla()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            Directory.CreateDirectory(directorio);
            await using (var conexion = await AbrirConexionAsync(ruta))
            {
                using var tabla = conexion.CreateCommand();
                tabla.CommandText = "CREATE TABLE esquema_heredado (id TEXT PRIMARY KEY, dato TEXT NOT NULL);";
                await tabla.ExecuteNonQueryAsync();
                using var fila = conexion.CreateCommand();
                fila.CommandText = "INSERT INTO esquema_heredado (id, dato) VALUES ('h1', 'heredado');";
                await fila.ExecuteNonQueryAsync();
            }

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
            Assert.Contains("heredado", error.Message);

            await using var verificacion = await AbrirConexionAsync(ruta);
            Assert.Equal("heredado", await ConsultarTextoAsync(verificacion, "SELECT dato FROM esquema_heredado WHERE id = 'h1';"));
            using var bitacora = verificacion.CreateCommand();
            bitacora.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_migrations';";
            Assert.Equal(0L, await bitacora.ExecuteScalarAsync());
            Assert.False(Directory.Exists(respaldos));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Historial_con_salto_se_rechaza_sin_ejecutar_migraciones()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            Directory.CreateDirectory(directorio);
            await using (var conexion = await AbrirConexionAsync(ruta))
            {
                using var bitacora = conexion.CreateCommand();
                bitacora.CommandText = """
                    CREATE TABLE schema_migrations (
                        version INTEGER PRIMARY KEY,
                        nombre TEXT NOT NULL,
                        aplicada_en TEXT NOT NULL
                    );
                    """;
                await bitacora.ExecuteNonQueryAsync();
                using var fila = conexion.CreateCommand();
                fila.CommandText = "INSERT INTO schema_migrations (version, nombre, aplicada_en) VALUES (2, 'futura-sin-v1', '2026-09-21T00:00:00Z');";
                await fila.ExecuteNonQueryAsync();
                using var legado = conexion.CreateCommand();
                legado.CommandText = "CREATE TABLE legado (id TEXT PRIMARY KEY); INSERT INTO legado (id) VALUES ('l1');";
                await legado.ExecuteNonQueryAsync();
            }

            var catalogoV2 = CatalogoV1()
                .Concat(new[]
                {
                    new Migracion(
                        2,
                        "simulacion-mejora-futura-solo-prueba",
                        "CREATE TABLE IF NOT EXISTS simulacion_futura (id TEXT PRIMARY KEY);"),
                })
                .ToList();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new MigradorBaseDatos(ruta).MigrarAsync(catalogoV2, respaldos));
            Assert.Contains("versión 1", error.Message);

            await using var verificacion = await AbrirConexionAsync(ruta);
            using var tablas = verificacion.CreateCommand();
            tablas.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('usuario', 'simulacion_futura');";
            Assert.Equal(0L, await tablas.ExecuteScalarAsync());
            Assert.Equal("l1", await ConsultarTextoAsync(verificacion, "SELECT id FROM legado WHERE id = 'l1';"));
            Assert.False(Directory.Exists(respaldos));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Migracion_V4_con_vigencias_incompatibles_falla_sin_cambios()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV3 = MigracionesF01.Todas.Where(m => m.Version <= 3).ToList();
            Assert.Equal(new[] { 1, 2, 3 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV3, null));
            await InsertarRegistrosFicticiosAsync(ruta);

            await using (var conexion = await AbrirConexionAsync(ruta))
            {
                foreach (var sql in new[]
                {
                    "INSERT INTO precio (id, variante_id, importe_minor, vigente_desde, creado_en) VALUES ('px-1', 'v-ficticia', 10000, '2026-09-21T12:00:00Z', '2026-09-21T12:00:00Z');",
                    "INSERT INTO precio (id, variante_id, importe_minor, vigente_desde, creado_en) VALUES ('px-2', 'v-ficticia', 12000, '2026-09-21T12:00:00Z', '2026-09-21T12:00:00Z');",
                })
                {
                    using var comando = conexion.CreateCommand();
                    comando.CommandText = sql;
                    await comando.ExecuteNonQueryAsync();
                }
            }

            await Assert.ThrowsAsync<SqliteException>(
                () => new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));

            await using var verificacion = await AbrirConexionAsync(ruta);
            Assert.Equal(new[] { 1L, 2L, 3L }, await VersionesAsync(verificacion));
            Assert.Equal(3L, await ContarAsync(verificacion, "precio", "variante_id = 'v-ficticia'"));
            Assert.Equal("Remera Ficticia", await ConsultarTextoAsync(verificacion, "SELECT nombre FROM producto WHERE id = 'p-ficticio';"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    private static async Task<List<long>> VersionesAsync(SqliteConnection conexion)
    {
        var versiones = new List<long>();
        using var consulta = conexion.CreateCommand();
        consulta.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";
        await using var lector = await consulta.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            versiones.Add(lector.GetInt64(0));
        }

        return versiones;
    }

    private static async Task<long> ContarAsync(SqliteConnection conexion, string tabla, string condicion)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = $"SELECT COUNT(*) FROM {tabla} WHERE {condicion};";
        return Convert.ToInt64(await comando.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migracion_V4_V6_con_datos_respaldo_y_sin_duplicados()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV4 = MigracionesF01.Todas.Where(m => m.Version <= 4).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV4, null));
            await InsertarRegistrosFicticiosAsync(ruta);

            Assert.Equal(new[] { 5, 6, 7, 8, 9, 10 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));

            var archivo = Assert.Single(Directory.GetFiles(respaldos, "*.db"));
            Assert.Equal("ok", await ConsultarIntegridadAsync(archivo));
            await using var verificacion = await AbrirConexionAsync(ruta);
            Assert.Equal("Remera Ficticia", await ConsultarTextoAsync(verificacion, "SELECT nombre FROM producto WHERE id = 'p-ficticio';"));
            using var tablas = verificacion.CreateCommand();
            tablas.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('proveedor', 'recepcion', 'recepcion_linea');";
            Assert.Equal(3L, await tablas.ExecuteScalarAsync());

            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Migracion_V10_ventas_pos_con_datos_y_sin_duplicados()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV9 = MigracionesF01.Todas.Where(m => m.Version <= 9).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV9, null));
            await InsertarRegistrosFicticiosV9Async(ruta);
            await using (var previa = await AbrirConexionAsync(ruta))
            {
                await CrearComando(
                    previa,
                    "INSERT INTO venta (id, estado, total_minor, creada_en) VALUES ('v-ficticia', 'confirmada', 20000, $ahora);",
                    ("$ahora", Ahora())).ExecuteNonQueryAsync();
                // Legado no-efectivo: migra limpio (un efectivo sin recibido
                // no puede existir en datos reales: la columna nace en V10).
                await CrearComando(
                    previa,
                    "INSERT INTO pago (id, venta_id, medio, importe_minor, creado_en) VALUES ('pago-ficticio', 'v-ficticia', 'debito', 20000, $ahora);",
                    ("$ahora", Ahora())).ExecuteNonQueryAsync();
            }

            Assert.Equal(new[] { 10 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));

            var archivo = Assert.Single(Directory.GetFiles(respaldos, "*.db"));
            Assert.Equal("ok", await ConsultarIntegridadAsync(archivo));
            await using var verificacion = await AbrirConexionAsync(ruta);
            // Backfill: número y operación provisorios, recibido queda NULL.
            Assert.Equal("MIG-v-fictic", await ConsultarTextoAsync(verificacion, "SELECT numero FROM venta WHERE id = 'v-ficticia';"));
            Assert.Equal("MIG-v-fictic", await ConsultarTextoAsync(verificacion, "SELECT operacion_id FROM venta WHERE id = 'v-ficticia';"));
            using var tablas = verificacion.CreateCommand();
            tablas.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('secuencia_venta', 'reversion_venta');";
            Assert.Equal(2L, await tablas.ExecuteScalarAsync());
            using var columnas = verificacion.CreateCommand();
            columnas.CommandText = "SELECT COUNT(*) FROM pragma_table_info('venta_linea') WHERE name IN ('codigo_interno', 'nombre_producto', 'talle', 'color');";
            Assert.Equal(4L, await columnas.ExecuteScalarAsync());
            using var recibido = verificacion.CreateCommand();
            recibido.CommandText = "SELECT COUNT(*) FROM pragma_table_info('pago') WHERE name = 'recibido_minor';";
            Assert.Equal(1L, await recibido.ExecuteScalarAsync());

            // Unicidad real: ni número ni operación se repiten.
            var duplicada = CrearComando(
                verificacion,
                "INSERT INTO venta (id, numero, operacion_id, estado, total_minor, creada_en) VALUES ($id, 'T-000001', $op, 'confirmada', 1000, $ahora);",
                ("$id", NuevoId()), ("$op", NuevoId()), ("$ahora", Ahora()));
            await duplicada.ExecuteNonQueryAsync();
            var numeroRepetido = CrearComando(
                verificacion,
                "INSERT INTO venta (id, numero, operacion_id, estado, total_minor, creada_en) VALUES ($id, 'T-000001', $op, 'confirmada', 1000, $ahora);",
                ("$id", NuevoId()), ("$op", NuevoId()), ("$ahora", Ahora()));
            var errorNumero = await Assert.ThrowsAsync<SqliteException>(() => numeroRepetido.ExecuteNonQueryAsync());
            Assert.Contains("venta.numero", errorNumero.Message);

            // Efectivo sin recibido o con recibido menor se rechaza.
            var sinRecibido = CrearComando(
                verificacion,
                "INSERT INTO pago (id, venta_id, medio, importe_minor, creado_en) VALUES ($id, 'v-ficticia', 'efectivo', 1000, $ahora);",
                ("$id", NuevoId()), ("$ahora", Ahora()));
            await Assert.ThrowsAsync<SqliteException>(() => sinRecibido.ExecuteNonQueryAsync());
            var recibidoMenor = CrearComando(
                verificacion,
                "INSERT INTO pago (id, venta_id, medio, importe_minor, recibido_minor, creado_en) VALUES ($id, 'v-ficticia', 'efectivo', 1000, 900, $ahora);",
                ("$id", NuevoId()), ("$ahora", Ahora()));
            await Assert.ThrowsAsync<SqliteException>(() => recibidoMenor.ExecuteNonQueryAsync());
            var recibidoAjeno = CrearComando(
                verificacion,
                "INSERT INTO pago (id, venta_id, medio, importe_minor, recibido_minor, creado_en) VALUES ($id, 'v-ficticia', 'debito', 1000, 1000, $ahora);",
                ("$id", NuevoId()), ("$ahora", Ahora()));
            await Assert.ThrowsAsync<SqliteException>(() => recibidoAjeno.ExecuteNonQueryAsync());

            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    /// <summary>
    /// Siembra con forma V9 (códigos, normas y categoría obligatoria): la
    /// siembra V1 no compila contra el esquema V9 (columnas inexistentes).
    /// </summary>
    private static async Task InsertarRegistrosFicticiosV9Async(string ruta)
    {
        await using var conexion = await AbrirConexionAsync(ruta);
        foreach (var sql in new[]
        {
            "INSERT INTO producto (id, codigo_interno, nombre, categoria_id, creado_en) VALUES ('p-ficticio', 'REM-FIC', 'Remera Ficticia', 'cat-rem', '2026-09-21T00:00:00Z');",
            "INSERT INTO variante (id, producto_id, sku, codigo_interno, talle, talle_norm, color_norm, creado_en) VALUES ('v-ficticia', 'p-ficticio', 'REM-FIC-001', 'REM-FIC-001', '6', '6', '', '2026-09-21T00:00:00Z');",
        })
        {
            using var comando = conexion.CreateCommand();
            comando.CommandText = sql;
            await comando.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertarRegistrosFicticiosAsync(string ruta)
    {
        await using var conexion = await AbrirConexionAsync(ruta);
        foreach (var sql in new[]
        {
            "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-ficticia', 'Empleada Ficticia', 'empleada', '2026-09-21T00:00:00Z');",
            "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-duena', 'Duena Ficticia', 'duena', '2026-09-21T00:00:00Z');",
            "INSERT INTO producto (id, nombre, marca, creado_en) VALUES ('p-ficticio', 'Remera Ficticia', 'Marca Ficticia', '2026-09-21T00:00:00Z');",
            "INSERT INTO variante (id, producto_id, sku, talle, creado_en) VALUES ('v-ficticia', 'p-ficticio', 'REM-FIC-001', '6', '2026-09-21T00:00:00Z');",
            "INSERT INTO precio (id, variante_id, importe_minor, vigente_desde, creado_en) VALUES ('pr-ficticio', 'v-ficticia', 15000, '2026-09-21T00:00:00Z', '2026-09-21T00:00:00Z');",
            "INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, operacion_id, creado_en) VALUES ('m-ficticio', 'v-ficticia', 'ingreso', 5, 'op-ficticia', '2026-09-21T00:00:00Z');",
        })
        {
            using var comando = conexion.CreateCommand();
            comando.CommandText = sql;
            await comando.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string?> ConsultarTextoAsync(SqliteConnection conexion, string sql)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        return await comando.ExecuteScalarAsync() as string;
    }

    private static async Task<string?> ConsultarIntegridadAsync(string rutaRespaldo)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = rutaRespaldo,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "PRAGMA integrity_check;";
        return await comando.ExecuteScalarAsync() as string;
    }

    /// <summary>
    /// Catálogo fijado a V1 para las pruebas del esquema inicial: el catálogo
    /// productivo crece con cada migración y estos tests verifican V1.
    /// </summary>
    private static List<Migracion> CatalogoV1()
    {
        return MigracionesF01.Todas.Where(m => m.Version == 1).ToList();
    }

    private static (string Directorio, string Ruta) CrearBaseTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        return (directorio, Path.Combine(directorio, "esquema-f01.db"));
    }

    private static void Limpiar(string directorio)
    {
        if (Directory.Exists(directorio))
        {
            Directory.Delete(directorio, recursive: true);
        }
    }

    private static async Task<SqliteConnection> AbrirConexionAsync(string ruta)
    {
        var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
        }.ToString());
        await conexion.OpenAsync();
        using var pragma = conexion.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync();
        return conexion;
    }

    private static SqliteCommand CrearComando(SqliteConnection conexion, string sql, params (string Nombre, object Valor)[] parametros)
    {
        var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        foreach (var (nombre, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nombre, valor);
        }

        return comando;
    }

    private static string NuevoId() => Guid.NewGuid().ToString("N");

    private static string Ahora() => DateTime.UtcNow.ToString("o");
}
