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
                varianteId = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "REM-RT-001", null, null), Fecha);
                await movimientos.RegistrarAsync(sesion, new NuevoMovimiento(varianteId, "ingreso", 4, null, "op-rt", null), Fecha);
                await sesion.ConfirmarAsync();
            }

            await using var cruda = await AbrirEscrituraAsync(ruta);
            await Assert.ThrowsAsync<SqliteException>(() => BorrarAsync(cruda, "producto", productoId));
            await Assert.ThrowsAsync<SqliteException>(() => BorrarAsync(cruda, "variante", varianteId));

            await using var verificacion = await fabrica.IniciarLecturaAsync();
            Assert.NotNull(await productos.ObtenerPorIdAsync(verificacion, productoId));
            Assert.NotNull(await variantes.ObtenerPorSkuAsync(verificacion, "REM-RT-001"));
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
                productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Campera", null, null), Fecha);
                await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "CAM-U-001", null, null), Fecha);
                await Assert.ThrowsAsync<SqliteException>(() =>
                    variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "CAM-U-001", "8", null), Fecha));
            }

            await using var verificacion = await fabrica.IniciarLecturaAsync();
            Assert.Null(await productos.ObtenerPorIdAsync(verificacion, productoId));
            Assert.Null(await variantes.ObtenerPorSkuAsync(verificacion, "CAM-U-001"));
            Assert.Equal(0L, await ContarAsync(ruta, "producto"));
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
