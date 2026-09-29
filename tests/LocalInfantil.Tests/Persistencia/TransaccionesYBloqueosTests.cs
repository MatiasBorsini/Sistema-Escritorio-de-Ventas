using System.Diagnostics;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Persistencia;

/// <summary>
/// Pruebas de F01-T02 sobre bases temporales aisladas que se eliminan al
/// terminar: claves foráneas por conexión, reapertura, bloqueo con tiempo de
/// espera mapeado, rollback compuesto y repositorios mínimos.
/// </summary>
public sealed class TransaccionesYBloqueosTests
{
    [Fact]
    public async Task Claves_foraneas_activas_en_cada_conexion()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var variantes = new RepositorioVariantes(new GeneradorIds());

            await using (var primera = await fabrica.IniciarAsync())
            {
                await Assert.ThrowsAsync<SqliteException>(() =>
                    variantes.InsertarAsync(primera, new NuevaVariante("producto-inexistente", null, null), Fecha1));
            }

            await using (var segunda = await fabrica.IniciarAsync())
            {
                await Assert.ThrowsAsync<SqliteException>(() =>
                    variantes.InsertarAsync(segunda, new NuevaVariante("producto-inexistente", null, null), Fecha1));
            }
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Escritura_lectura_cierre_y_reapertura()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var productos = new RepositorioProductos(new GeneradorIds());
            var variantes = new RepositorioVariantes(new GeneradorIds());

            string productoId;
            string varianteId;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Remera", "Marca", null), Fecha1);
                varianteId = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "6", null), Fecha1);
                await sesion.ConfirmarAsync();
            }

            await using var reapertura = await fabrica.IniciarAsync();
            var producto = await productos.ObtenerPorIdAsync(reapertura, productoId);
            var variante = await variantes.ObtenerPorIdAsync(reapertura, varianteId);
            Assert.NotNull(producto);
            Assert.Equal("Remera", producto.Nombre);
            Assert.NotNull(variante);
            Assert.Equal(productoId, variante.ProductoId);
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Bloqueo_entre_conexiones_mapea_ocupado_al_vencer()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var rapida = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta, esperaBloqueoMs: 500));
            var productos = new RepositorioProductos(new GeneradorIds());

            await using var bloqueo = await fabrica.IniciarAsync();
            await productos.InsertarAsync(bloqueo, new NuevoProducto("Bloqueante", null, null), Fecha1);

            // Con sesiones de escritura inmediata, el conflicto ya surge al iniciar:
            // BEGIN IMMEDIATE no puede tomar el bloqueo que retiene la otra sesión.
            var cronometro = Stopwatch.StartNew();
            var error = await Assert.ThrowsAsync<AlmacenamientoOcupadoException>(() => rapida.IniciarAsync());
            cronometro.Stop();
            Assert.IsType<SqliteException>(error.InnerException);
            Assert.True(
                ((SqliteException)error.InnerException!).SqliteErrorCode is 5 or 6 or 9,
                $"Código SQLite inesperado: {((SqliteException)error.InnerException!).SqliteErrorCode}.");
            Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(15), $"La contienda tardó {cronometro.Elapsed} en fallar.");

            await bloqueo.RevertirAsync();

            await using var verificacion = await fabrica.IniciarAsync();
            var movimientos = new RepositorioMovimientos(new GeneradorIds());
            Assert.Equal(0, await movimientos.SumarPorVarianteAsync(verificacion, "sin-variante"));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Rollback_compuesto_ante_error_no_deja_parciales()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var productos = new RepositorioProductos(new GeneradorIds());
            var variantes = new RepositorioVariantes(new GeneradorIds());

            string productoId;
            string varianteId;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Remera", null, null), Fecha1);
                varianteId = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "8", null), Fecha1);
                await Assert.ThrowsAsync<SqliteException>(() =>
                    variantes.InsertarAsync(sesion, new NuevaVariante("producto-inexistente", null, null), Fecha1));
            }

            await using var verificacion = await fabrica.IniciarAsync();
            Assert.Null(await productos.ObtenerPorIdAsync(verificacion, productoId));
            Assert.Null(await variantes.ObtenerPorIdAsync(verificacion, varianteId));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public async Task Repositorios_precio_y_movimiento_redondean_el_flujo()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var productos = new RepositorioProductos(new GeneradorIds());
            var variantes = new RepositorioVariantes(new GeneradorIds());
            var precios = new RepositorioPrecios(new GeneradorIds());
            var movimientos = new RepositorioMovimientos(new GeneradorIds());

            string varianteId;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                var productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Pantalón", null, null), Fecha1);
                varianteId = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "4", null), Fecha1);
                await precios.FijarAsync(sesion, new NuevoPrecio(varianteId, 15000, "ARS", Fecha1, null), Fecha1);
                await movimientos.RegistrarAsync(
                    sesion,
                    new NuevoMovimiento(varianteId, "ingreso", 10, null, "op-1", null),
                    Fecha1);
                await sesion.ConfirmarAsync();
            }

            await using var lectura = await fabrica.IniciarAsync();
            var vigente = await precios.ObtenerVigenteAsync(lectura, varianteId, "2026-09-21T13:00:00Z");
            Assert.NotNull(vigente);
            Assert.Equal(15000, vigente.ImporteMinor);
            Assert.Equal(10, await movimientos.SumarPorVarianteAsync(lectura, varianteId));
        }
        finally
        {
            Limpiar(directorio);
        }
    }

    [Fact]
    public void Opciones_invalidas_se_rechazan()
    {
        Assert.Throws<ArgumentException>(() => new OpcionesConexionSqlite(string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcionesConexionSqlite("base.db", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpcionesConexionSqlite("base.db", -100));
        Assert.Throws<ArgumentNullException>(() => new FabricaConexionesSqlite(null!));
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
}
