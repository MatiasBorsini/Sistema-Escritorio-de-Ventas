using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Wpf.Infra;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Wpf;

public sealed class F10StartupIntegrationTests
{
    [Fact]
    public async Task Base_nueva_migra_hasta_V16_y_solicita_bootstrap_sin_login()
    {
        using var entorno = new Entorno();
        var estado = await new InicializadorArranque(new ServiciosComerciales(entorno.BaseDatos))
            .InicializarAsync(entorno.Respaldos);

        Assert.True(estado.RequiereBootstrapDuena);
        Assert.Equal(16, estado.MigracionesAplicadas.Count);
        Assert.Equal(16, await LeerVersionAsync(entorno.BaseDatos));
        Assert.False(File.Exists(Path.Combine(entorno.Raiz, "Prototype", "localinfantil-prototype.db")));
    }

    [Fact]
    public async Task Backup_previo_fallido_aborta_arranque_y_no_avanza_el_schema()
    {
        using var entorno = new Entorno();
        await new MigradorBaseDatos(entorno.BaseDatos).MigrarAsync(MigracionesF01.Todas.Take(1).ToArray());
        File.WriteAllText(entorno.Respaldos, "no es un directorio");

        await Assert.ThrowsAsync<RespaldoFallidoException>(() => new InicializadorArranque(new ServiciosComerciales(entorno.BaseDatos))
            .InicializarAsync(entorno.Respaldos));

        Assert.Equal(1, await LeerVersionAsync(entorno.BaseDatos));
    }

    [Fact]
    public async Task Migracion_desde_version_anterior_crea_backup_previo_verificado()
    {
        using var entorno = new Entorno();
        await new MigradorBaseDatos(entorno.BaseDatos).MigrarAsync(MigracionesF01.Todas.Take(1).ToArray());

        var estado = await new InicializadorArranque(new ServiciosComerciales(entorno.BaseDatos))
            .InicializarAsync(entorno.Respaldos);

        Assert.True(estado.RequiereBootstrapDuena);
        Assert.Equal(15, estado.MigracionesAplicadas.Count);
        Assert.NotEmpty(Directory.GetFiles(entorno.Respaldos));
        Assert.Equal(16, await LeerVersionAsync(entorno.BaseDatos));
    }

    [Fact]
    public async Task Historial_de_schema_desconocido_aborta_arranque_sin_operar()
    {
        using var entorno = new Entorno();
        await new MigradorBaseDatos(entorno.BaseDatos).MigrarAsync(MigracionesF01.Todas.Take(1).ToArray());
        await using (var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = entorno.BaseDatos }.ToString()))
        {
            await conexion.OpenAsync();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "INSERT INTO schema_migrations(version,nombre,aplicada_en) VALUES(999,'desconocida','test');";
            await comando.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => new InicializadorArranque(new ServiciosComerciales(entorno.BaseDatos))
            .InicializarAsync(entorno.Respaldos));
        Assert.Equal(999, await LeerVersionAsync(entorno.BaseDatos));
    }

    private static async Task<int> LeerVersionAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        return Convert.ToInt32(await comando.ExecuteScalarAsync());
    }

    private sealed class Entorno : IDisposable
    {
        public string Raiz { get; } = Path.Combine(Path.GetTempPath(), "LocalInfantil-startup-" + Guid.NewGuid().ToString("N"));
        public string BaseDatos => Path.Combine(Raiz, "Data", "localinfantil.db");
        public string Respaldos => Path.Combine(Raiz, "Backups");
        public Entorno() => Directory.CreateDirectory(Raiz);
        public void Dispose() { try { Directory.Delete(Raiz, recursive: true); } catch (IOException) { } }
    }
}
