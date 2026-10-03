using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Persistencia;

/// <summary>
/// Pruebas de F01-T03 sobre bases temporales aisladas que se eliminan al
/// terminar: identificadores estables, reloj determinista, auditoría mínima,
/// correlación por operacion_id, rollback conjunto y migración V1→V2 con datos.
/// La operación de negocio de prueba es ficticia (un producto de prueba con su
/// auditoría en la misma transacción): no implementa ventas ni stock comercial.
/// </summary>
public sealed class AuditoriaTests
{
    [Fact]
    public async Task Identificadores_distintos_y_estables_al_recuperar()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial(), new RelojFijo(MomentoFijo));

            string primero, segundo;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                primero = await auditoria.RegistrarAsync(sesion, EventoSistema("op-ficticia-1", "prueba.inicio"));
                segundo = await auditoria.RegistrarAsync(sesion, EventoSistema("op-ficticia-2", "prueba.inicio"));
                await sesion.ConfirmarAsync();
            }

            Assert.NotEqual(primero, segundo);
            Assert.Equal("id-0001", primero);
            Assert.Equal("id-0002", segundo);

            await using var lectura = await fabrica.IniciarLecturaAsync();
            var recuperados = await auditoria.ObtenerPorOperacionAsync(lectura, "op-ficticia-1");
            var unico = Assert.Single(recuperados);
            Assert.Equal(primero, unico.Id);
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Reloj_fijo_fechas_deterministas()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var reloj = new RelojFijo(MomentoFijo);
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial(), reloj);

            await using (var sesion = await fabrica.IniciarAsync())
            {
                await auditoria.RegistrarAsync(sesion, EventoSistema("op-reloj", "prueba.marca"));
                reloj.Avanzar(TimeSpan.FromHours(1));
                await auditoria.RegistrarAsync(sesion, EventoSistema("op-reloj", "prueba.marca"));
                await sesion.ConfirmarAsync();
            }

            await using var lectura = await fabrica.IniciarLecturaAsync();
            var eventos = await auditoria.ObtenerPorOperacionAsync(lectura, "op-reloj");
            Assert.Equal(2, eventos.Count);
            Assert.Equal(MomentoFijo.ToString("o"), eventos[0].CreadoEn);
            Assert.Equal(MomentoFijo.AddHours(1).ToString("o"), eventos[1].CreadoEn);
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Operacion_ficticia_y_auditoria_se_confirman_juntas()
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
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), new RelojFijo(MomentoFijo));

            string productoId;
            string varianteId;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Ficticio", null, null), FechaTexto);
                varianteId = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, null, null), FechaTexto);
                await movimientos.RegistrarAsync(
                    sesion,
                    new NuevoMovimiento(varianteId, "ingreso", 10, null, "op-ficticia-3", null),
                    FechaTexto);
                await auditoria.RegistrarAsync(
                    sesion,
                    new NuevoEventoAuditoria("op-ficticia-3", "prueba.operacion_confirmada", ActorAuditoria.Sistema, null, "producto", productoId, "alta ficticia"));
                await auditoria.RegistrarAsync(
                    sesion,
                    new NuevoEventoAuditoria("op-ficticia-3", "prueba.operacion_revisada", ActorAuditoria.Sistema, null, "producto", productoId, null));
                await sesion.ConfirmarAsync();
            }

            await using var lectura = await fabrica.IniciarLecturaAsync();
            var producto = await productos.ObtenerPorIdAsync(lectura, productoId);
            Assert.NotNull(producto);
            Assert.Equal(productoId, producto.Id);
            var variante = await variantes.ObtenerPorIdAsync(lectura, varianteId);
            Assert.NotNull(variante);
            Assert.False(string.IsNullOrWhiteSpace(variante.CodigoInterno));
            Assert.NotEqual(productoId, varianteId);
            Assert.Equal("op-ficticia-3", await ConsultarOperacionMovimientoAsync(ruta, varianteId));
            var eventos = await auditoria.ObtenerPorOperacionAsync(lectura, "op-ficticia-3");
            Assert.Equal(2, eventos.Count);
            Assert.All(eventos, e => Assert.Equal("op-ficticia-3", e.OperacionId));
            Assert.All(eventos, e => Assert.Equal(ActorAuditoria.Sistema, e.ActorTipo));
            Assert.All(eventos, e => Assert.Equal(productoId, e.ReferenciaId));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Rollback_conjunto_si_falla_la_operacion_ficticia()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var productos = new RepositorioProductos(new GeneradorIds());
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), new RelojFijo(MomentoFijo));

            string productoId;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Ficticio", null, null), FechaTexto);
                await auditoria.RegistrarAsync(
                    sesion,
                    new NuevoEventoAuditoria("op-ficticia-4", "prueba.operacion_confirmada", ActorAuditoria.Sistema, null, "producto", productoId, null));
                await Assert.ThrowsAsync<SqliteException>(() => auditoria.RegistrarAsync(
                    sesion,
                    new NuevoEventoAuditoria("op-ficticia-4", "prueba.revision_manual", ActorAuditoria.Usuario, null, "producto", productoId, null)));
            }

            await using var verificacion = await fabrica.IniciarLecturaAsync();
            Assert.Null(await productos.ObtenerPorIdAsync(verificacion, productoId));
            Assert.Empty(await auditoria.ObtenerPorOperacionAsync(verificacion, "op-ficticia-4"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Actor_usuario_sin_id_se_rechaza()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial(), new RelojFijo(MomentoFijo));

            await using (var sesion = await fabrica.IniciarAsync())
            {
                await Assert.ThrowsAsync<SqliteException>(() => auditoria.RegistrarAsync(
                    sesion,
                    new NuevoEventoAuditoria("op-ficticia-5", "prueba.revision_manual", ActorAuditoria.Usuario, null, null, null, null)));
            }

            await using var verificacion = await fabrica.IniciarLecturaAsync();
            Assert.Empty(await auditoria.ObtenerPorOperacionAsync(verificacion, "op-ficticia-5"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Migracion_V1_V6_con_datos_respaldo_y_sin_duplicados()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV1 = MigracionesF01.Todas.Where(m => m.Version == 1).ToList();
            Assert.Equal(new[] { 1 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV1, null));

            // Siembra con SQL de forma V1 (sin columnas V9): los repositorios
            // actuales requieren el esquema completo.
            await using (var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ruta,
                Pooling = false,
            }.ToString()))
            {
                await conexion.OpenAsync();
                using var comando = conexion.CreateCommand();
                comando.CommandText = "INSERT INTO producto (id, nombre, creado_en) VALUES ('p-v1', 'Ficticio V1', $ahora);";
                comando.Parameters.AddWithValue("$ahora", FechaTexto);
                await comando.ExecuteNonQueryAsync();
            }

            Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));

            var archivosRespaldo = Directory.GetFiles(respaldos, "*.db");
            Assert.Single(archivosRespaldo);
            Assert.Equal("ok", await ConsultarIntegridadAsync(archivosRespaldo[0]));

            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), new RelojFijo(MomentoFijo));
            await using (var sesion = await fabrica.IniciarAsync())
            {
                await auditoria.RegistrarAsync(sesion, EventoSistema("op-post-v2", "prueba.migracion_ok"));
                await sesion.ConfirmarAsync();
            }

            await using var lectura = await fabrica.IniciarLecturaAsync();
            Assert.Single(await auditoria.ObtenerPorOperacionAsync(lectura, "op-post-v2"));
            var productosLectura = new RepositorioProductos(new GeneradorIds());
            var heredado = await productosLectura.ObtenerPorIdAsync(lectura, await UnicoProductoIdAsync(ruta));
            Assert.NotNull(heredado);
            // Backfill V9: la fila heredada recibe código provisorio MIG-*.
            Assert.StartsWith("MIG-", heredado.CodigoInterno);

            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    private static readonly DateTimeOffset MomentoFijo = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private const string FechaTexto = "2026-09-21T12:00:00Z";

    private static NuevoEventoAuditoria EventoSistema(string operacion, string tipo)
    {
        return new NuevoEventoAuditoria(operacion, tipo, ActorAuditoria.Sistema, null, null, null, null);
    }

    private static (string Directorio, string Ruta) CrearBaseTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        return (directorio, Path.Combine(directorio, "auditoria-f01.db"));
    }

    private static void Limpiar(string directorio)
    {
        if (Directory.Exists(directorio))
        {
            Directory.Delete(directorio, recursive: true);
        }
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

    private static async Task<string> ConsultarOperacionMovimientoAsync(string ruta, string varianteId)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT operacion_id FROM movimiento_inventario WHERE variante_id = $variante;";
        comando.Parameters.AddWithValue("$variante", varianteId);
        return (await comando.ExecuteScalarAsync() as string)!;
    }

    private static async Task<string> UnicoProductoIdAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT id FROM producto LIMIT 1;";
        return (await comando.ExecuteScalarAsync() as string)!;
    }
}
