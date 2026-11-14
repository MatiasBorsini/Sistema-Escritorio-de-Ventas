using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Persistencia;

/// <summary>
/// Escenarios F01-T06 sin cobertura previa: restricciones RESTRICT, unicidad
/// de nombre de usuario, ausencia de parciales tras fallos de FK y rollback
/// ante violación de unicidad a mitad de operación compuesta. Todo lo demás
/// (migraciones, journaling, bloqueos, respaldos, auditoría) ya estaba
/// cubierto y no se duplica. Bases temporales aisladas, eliminadas al terminar.
/// </summary>
public sealed class IntegridadTests
{
    [Fact]
    public async Task Restrict_impide_borrar_registros_con_dependientes()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var productos = new RepositorioProductos(ids);
            var variantes = new RepositorioVariantes(ids);
            var movimientos = new RepositorioMovimientos(ids);

            string productoId;
            string varianteId;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Remera", null, null), Fecha);
                varianteId = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, null, null), Fecha);
                await movimientos.RegistrarAsync(sesion, new NuevoMovimiento(varianteId, "ingreso", 4, null, "op-rt", null), Fecha);
                await sesion.ConfirmarAsync();
            }

            await using var cruda = await AbrirEscrituraAsync(ruta);
            await Assert.ThrowsAsync<SqliteException>(() => BorrarAsync(cruda, "producto", productoId));
            await Assert.ThrowsAsync<SqliteException>(() => BorrarAsync(cruda, "variante", varianteId));

            await using var verificacion = await fabrica.IniciarLecturaAsync();
            Assert.NotNull(await productos.ObtenerPorIdAsync(verificacion, productoId));
            Assert.NotNull(await variantes.ObtenerPorIdAsync(verificacion, varianteId));
            Assert.Equal(4, await movimientos.SumarPorVarianteAsync(verificacion, varianteId));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Nombre_usuario_duplicado_se_rechaza()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var usuarios = new RepositorioUsuarios(new GeneradorIds());

            await using (var sesion = await fabrica.IniciarAsync())
            {
                await usuarios.CrearAsync(sesion, new NuevoUsuario("repetido", RolesLocal.Empleada), "huella-1", Fecha);
                await sesion.ConfirmarAsync();
            }

            await using (var sesion = await fabrica.IniciarAsync())
            {
                var error = await Assert.ThrowsAsync<SqliteException>(() =>
                    usuarios.CrearAsync(sesion, new NuevoUsuario("repetido", RolesLocal.Empleada), "huella-2", Fecha));
                Assert.Contains("UNIQUE", error.Message);
            }
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Insercion_huerfana_no_deja_parciales()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var movimientos = new RepositorioMovimientos(new GeneradorIds());

            await using (var sesion = await fabrica.IniciarAsync())
            {
                await Assert.ThrowsAsync<SqliteException>(() => movimientos.RegistrarAsync(
                    sesion,
                    new NuevoMovimiento("variante-inexistente", "ingreso", 3, null, "op-huerfana", null),
                    Fecha));
                await sesion.RevertirAsync();
            }

            Assert.Equal(0L, await ContarAsync(ruta, "movimiento_inventario"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Violacion_unicidad_a_mitad_de_operacion_revierte_todo()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var productos = new RepositorioProductos(ids);
            var variantes = new RepositorioVariantes(ids);

            string productoId;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Campera", null, "cat-otr"), "CAM-001", Fecha);
                await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, null, null), "CAM-001-01", Fecha);
                // Con códigos automáticos el duplicado ya no surge del caso de
                // uso: se provoca con el código explícito a nivel repositorio.
                var error = await Assert.ThrowsAsync<SqliteException>(() =>
                    variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "8", null), "CAM-001-01", Fecha));
                Assert.Contains("variante.codigo_interno", error.Message);
            }

            await using var verificacion = await fabrica.IniciarLecturaAsync();
            Assert.Null(await productos.ObtenerPorIdAsync(verificacion, productoId));
            Assert.Equal(0L, await ContarAsync(ruta, "producto"));
            Assert.Equal(0L, await ContarAsync(ruta, "variante"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Norma_talle_color_impide_coexistencia_insensible_a_caso_y_espacios()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var productos = new RepositorioProductos(ids);
            var variantes = new RepositorioVariantes(ids);

            // Persistencia directa (sin caso de uso): la norma la calcula el
            // repositorio con la misma función centralizada de Application.
            string productoId;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Remera", null, "cat-rem"), "REM-001", Fecha);
                await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "M", "Rojo"), "REM-001-01", Fecha);
                await sesion.ConfirmarAsync();
            }

            await using (var sesion = await fabrica.IniciarAsync())
            {
                var errorCaso = await Assert.ThrowsAsync<SqliteException>(() =>
                    variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "m", "rojo"), "REM-001-02", Fecha));
                Assert.Contains("talle_norm", errorCaso.Message);
            }

            await using (var sesion = await fabrica.IniciarAsync())
            {
                var errorEspacios = await Assert.ThrowsAsync<SqliteException>(() =>
                    variantes.InsertarAsync(sesion, new NuevaVariante(productoId, " M ", " rojo "), "REM-001-03", Fecha));
                Assert.Contains("talle_norm", errorEspacios.Message);
            }

            // Caso Unicode: 'ñ'/'ámbar' normaliza igual que 'Ñ'/'Ámbar'
            // (FormC + UpperInvariant, más allá del UPPER ASCII de SQLite).
            await using (var sesion = await fabrica.IniciarAsync())
            {
                await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "Ñ", "Ámbar"), "REM-001-04", Fecha);
                await sesion.ConfirmarAsync();
            }

            await using (var sesion = await fabrica.IniciarAsync())
            {
                var errorUnicode = await Assert.ThrowsAsync<SqliteException>(() =>
                    variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "ñ", "ámbar"), "REM-001-05", Fecha));
                Assert.Contains("talle_norm", errorUnicode.Message);
            }

            await using var verificacion = await fabrica.IniciarLecturaAsync();
            Assert.Equal(2L, await ContarAsync(ruta, "variante"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Norma_unicode_caso_insensible_en_update()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var productos = new RepositorioProductos(ids);
            var variantes = new RepositorioVariantes(ids);

            string v1;
            string v2;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                var productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Remera", null, "cat-rem"), "REM-001", Fecha);
                v1 = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "Ñ", "Ámbar"), "REM-001-01", Fecha);
                v2 = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "L", null), "REM-001-02", Fecha);
                await sesion.ConfirmarAsync();
            }

            // 'ñ'/'ámbar' normaliza igual que 'Ñ'/'Ámbar' (FormC+UpperInvariant,
            // más allá del UPPER ASCII de SQLite): el UPDATE colisiona.
            await using (var sesion = await fabrica.IniciarAsync())
            {
                var error = await Assert.ThrowsAsync<SqliteException>(() =>
                    variantes.ActualizarAsync(sesion, v2, new EdicionVariante("ñ", "ámbar")));
                Assert.Contains("talle_norm", error.Message);
            }

            _ = v1;
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Producto_sin_categoria_no_puede_persistirse()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var productos = new RepositorioProductos(new GeneradorIds());

            // Inserción cruda sin categoria_id: viola NOT NULL aunque el caso
            // de uso ya lo exige con mensaje humano.
            await using var cruda = await AbrirEscrituraAsync(ruta);
            using var comando = cruda.CreateCommand();
            comando.CommandText = "INSERT INTO producto (id, codigo_interno, nombre, creado_en) VALUES ('p-sin-cat', 'REM-999', 'Remera', $ahora);";
            comando.Parameters.AddWithValue("$ahora", Fecha);
            var error = await Assert.ThrowsAsync<SqliteException>(() => comando.ExecuteNonQueryAsync());
            Assert.Contains("categoria_id", error.Message);

            Assert.Equal(0L, await ContarAsync(ruta, "producto"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Prefijo_y_nombre_de_categoria_no_se_duplican()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var categorias = new RepositorioCategorias(new GeneradorIds());

            await using (var sesion = await fabrica.IniciarAsync())
            {
                var errorPrefijo = await Assert.ThrowsAsync<SqliteException>(() =>
                    categorias.InsertarAsync(sesion, new NuevaCategoria("Otra cosa", "REM", 200), Fecha));
                Assert.Contains("categoria.prefijo", errorPrefijo.Message);

                var errorNombre = await Assert.ThrowsAsync<SqliteException>(() =>
                    categorias.InsertarAsync(sesion, new NuevaCategoria("Remeras y tops", "RMT", 200), Fecha));
                Assert.Contains("categoria.nombre", errorNombre.Message);
            }

            Assert.Equal(12L, await ContarAsync(ruta, "categoria"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    private const string Fecha = "2026-09-21T12:00:00Z";

    private static (string Directorio, string Ruta) CrearBaseTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        return (directorio, Path.Combine(directorio, "integridad-f01.db"));
    }

    private static void Limpiar(string directorio)
    {
        if (Directory.Exists(directorio))
        {
            Directory.Delete(directorio, recursive: true);
        }
    }

    private static async Task<SqliteConnection> AbrirEscrituraAsync(string ruta)
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

    private static async Task BorrarAsync(SqliteConnection conexion, string tabla, string id)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = $"DELETE FROM {tabla} WHERE id = $id;";
        comando.Parameters.AddWithValue("$id", id);
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<long> ContarAsync(string ruta, string tabla)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = $"SELECT COUNT(*) FROM {tabla};";
        return Convert.ToInt64(await comando.ExecuteScalarAsync());
    }
}
