using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Persistencia;

/// <summary>
/// Evaluación reproducible de WAL frente al diario convencional, siempre sobre
/// bases temporales aisladas que se eliminan al terminar. No demuestra
/// resistencia a cortes eléctricos: esa garantía requiere otras verificaciones.
/// </summary>
public sealed class ModoJournalTests
{
    [Fact]
    public async Task Wal_se_establece_y_persiste_tras_reapertura()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));

            Assert.Equal("wal", await fabrica.EstablecerJournalAsync("wal"));

            var repositorio = new RepositorioProductos(new GeneradorIds());
            string id;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                id = await repositorio.InsertarAsync(sesion, new NuevoProducto("Remera", null, null), Fecha1);
                await sesion.ConfirmarAsync();
            }

            await using var reapertura = await fabrica.IniciarAsync();
            Assert.Equal("wal", await ConsultarJournalAsync(ruta));
            Assert.Equal("ok", await ConsultarIntegridadAsync(ruta));
            Assert.NotNull(await repositorio.ObtenerPorIdAsync(reapertura, id));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Wal_permite_lectura_durante_escritura_no_confirmada()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            Assert.Equal("wal", await fabrica.EstablecerJournalAsync("wal"));

            var repositorio = new RepositorioProductos(new GeneradorIds());
            string idConfirmado;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                idConfirmado = await repositorio.InsertarAsync(sesion, new NuevoProducto("Confirmada", null, null), Fecha1);
                await sesion.ConfirmarAsync();
            }

            await using var escritura = await fabrica.IniciarAsync();
            var idPendiente = await repositorio.InsertarAsync(escritura, new NuevoProducto("Pendiente", null, null), Fecha1);

            await using var lectura = await fabrica.IniciarLecturaAsync();
            Assert.NotNull(await repositorio.ObtenerPorIdAsync(lectura, idConfirmado));
            Assert.Null(await repositorio.ObtenerPorIdAsync(lectura, idPendiente));

            await escritura.RevertirAsync();
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Diario_convencional_funciona_como_base()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));

            Assert.Equal("delete", await fabrica.EstablecerJournalAsync("delete"));

            var repositorio = new RepositorioProductos(new GeneradorIds());
            string id;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                id = await repositorio.InsertarAsync(sesion, new NuevoProducto("Remera", null, null), Fecha1);
                await sesion.ConfirmarAsync();
            }

            await using var reapertura = await fabrica.IniciarAsync();
            Assert.Equal("delete", await ConsultarJournalAsync(ruta));
            Assert.Equal("ok", await ConsultarIntegridadAsync(ruta));
            Assert.NotNull(await repositorio.ObtenerPorIdAsync(reapertura, id));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Journal_no_cambia_en_cada_apertura()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            Assert.Equal("wal", await fabrica.EstablecerJournalAsync("wal"));

            await using var primera = await fabrica.IniciarAsync();
            Assert.Equal("wal", await ConsultarJournalAsync(ruta));
            await primera.ConfirmarAsync();

            await using var segunda = await fabrica.IniciarAsync();
            Assert.Equal("wal", await ConsultarJournalAsync(ruta));
            await segunda.ConfirmarAsync();
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Modo_journal_no_admitido_se_rechaza()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));

            await Assert.ThrowsAsync<ArgumentException>(() => fabrica.EstablecerJournalAsync("memory"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    private const string Fecha1 = "2026-09-21T12:00:00Z";

    private static (string Directorio, string Ruta) CrearBaseTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        return (directorio, Path.Combine(directorio, "fundamentos-f01.db"));
    }

    private static void Limpiar(string directorio)
    {
        if (Directory.Exists(directorio))
        {
            Directory.Delete(directorio, recursive: true);
        }
    }

    private static async Task<string> ConsultarJournalAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "PRAGMA journal_mode;";
        return ((await comando.ExecuteScalarAsync() as string) ?? string.Empty).ToLowerInvariant();
    }

    private static async Task<string?> ConsultarIntegridadAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "PRAGMA integrity_check;";
        return await comando.ExecuteScalarAsync() as string;
    }
}
