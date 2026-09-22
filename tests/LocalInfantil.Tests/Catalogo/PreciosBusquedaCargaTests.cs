using System.Diagnostics;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace LocalInfantil.Tests.Catalogo;

/// <summary>
/// Prueba integral F02-T03+T04+T05 sobre bases temporales aisladas,
/// eliminadas al terminar. Sin base provisional ni datos reales. Los permisos
/// nuevos se conceden solo en matrices de prueba.
/// </summary>
public sealed class PreciosBusquedaCargaTests
{
    private readonly ITestOutputHelper _salida;

    public PreciosBusquedaCargaTests(ITestOutputHelper salida)
    {
        _salida = salida;
    }

    [Fact]
    public async Task Precios_diferentes_conservan_historia_y_vigencia_unica()
    {
        using var entorno = await EntornoBloque.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var talles = new[] { "4", "6", "8" };
        var variantes = new List<string>();
        foreach (var talle in talles)
        {
            variantes.Add(await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, $"REM-P-{talle}", talle, null), entorno.Sesion));
        }

        await entorno.Precios.EjecutarAsync(variantes[0], 10000, entorno.Sesion);
        await entorno.Precios.EjecutarAsync(variantes[1], 12000, entorno.Sesion);
        await entorno.Precios.EjecutarAsync(variantes[2], 14000, entorno.Sesion);

        var primera = await entorno.Consultas.ObtenerFichaAsync(productoId, Fecha, entorno.Sesion);
        Assert.NotNull(primera);
        Assert.Equal([10000L, 12000L, 14000L], primera.Variantes.Select(v => v.PrecioMinor ?? -1));

        entorno.AvanzarReloj(TimeSpan.FromMinutes(1));
        await entorno.Precios.EjecutarAsync(variantes[0], 11000, entorno.Sesion);

        Assert.Equal(2, await entorno.ContarPreciosAsync(variantes[0]));
        Assert.Equal(1, await entorno.ContarVigentesAsync(variantes[0]));
        var vigente = await entorno.PreciosVigenteAsync(variantes[0], FechaPosterior);
        Assert.Equal(11000, vigente);
        Assert.Equal([11000L, 12000L, 14000L], (await entorno.Consultas.ObtenerFichaAsync(productoId, FechaPosterior, entorno.Sesion))!.Variantes.Select(v => v.PrecioMinor ?? -1));
    }

    [Fact]
    public async Task Cambio_mismo_instante_se_rechaza_sin_perder_historia()
    {
        using var entorno = await EntornoBloque.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var varianteId = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-M-001", null, null), entorno.Sesion);
        await entorno.Precios.EjecutarAsync(varianteId, 10000, entorno.Sesion);

        await Assert.ThrowsAsync<SqliteException>(() => entorno.Precios.EjecutarAsync(varianteId, 11000, entorno.Sesion));

        Assert.Equal(1, await entorno.ContarPreciosAsync(varianteId));
        Assert.Equal(1, await entorno.ContarVigentesAsync(varianteId));
        Assert.Equal(10000, await entorno.PreciosVigenteAsync(varianteId, Fecha));

        entorno.AvanzarReloj(TimeSpan.FromMinutes(1));
        await entorno.Precios.EjecutarAsync(varianteId, 11000, entorno.Sesion);
        Assert.Equal(2, await entorno.ContarPreciosAsync(varianteId));
        Assert.Equal(11000, await entorno.PreciosVigenteAsync(varianteId, FechaPosterior));
    }

    [Fact]
    public async Task Precio_negativo_e_invalido_se_rechaza()
    {
        using var entorno = await EntornoBloque.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var varianteId = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-N-001", null, null), entorno.Sesion);

        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Precios.EjecutarAsync(varianteId, -100, entorno.Sesion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Precios.EjecutarAsync("variante-inexistente", 100, entorno.Sesion));
        Assert.Equal(0, await entorno.ContarPreciosAsync(varianteId));
    }

    [Fact]
    public async Task Busqueda_texto_sku_paginacion_orden()
    {
        using var entorno = await EntornoBloque.CrearAsync();
        for (var i = 0; i < 30; i++)
        {
            await entorno.CrearProducto.EjecutarAsync(new NuevoProducto($"Producto-{i:00} MarcaX", "MarcaX", "Cat"), entorno.Sesion);
        }

        await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera Roja", null, null), entorno.Sesion);

        var porTexto = await entorno.Consultas.BuscarProductosAsync("remera", 20, 0, entorno.Sesion);
        Assert.Single(porTexto);
        var porMarca = await entorno.Consultas.BuscarProductosAsync("marcax", 50, 0, entorno.Sesion);
        Assert.Equal(30, porMarca.Count);

        var pagina1 = await entorno.Consultas.BuscarProductosAsync("Producto", 10, 0, entorno.Sesion);
        var pagina2 = await entorno.Consultas.BuscarProductosAsync("Producto", 10, 10, entorno.Sesion);
        Assert.Equal(10, pagina1.Count);
        Assert.Equal(10, pagina2.Count);
        Assert.Empty(pagina1.Select(p => p.Id).Intersect(pagina2.Select(p => p.Id)));
        var repetida = await entorno.Consultas.BuscarProductosAsync("Producto", 10, 0, entorno.Sesion);
        Assert.Equal(pagina1.Select(p => p.Id), repetida.Select(p => p.Id));
        var nombres = pagina1.Select(p => p.Nombre).ToList();
        Assert.Equal(nombres.OrderBy(n => n), nombres);
    }

    [Fact]
    public async Task Ficha_con_precios_y_existencias_sin_mezclar()
    {
        using var entorno = await EntornoBloque.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-F-004", "4", null), entorno.Sesion);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-F-006", "6", null), entorno.Sesion);
        await entorno.Precios.EjecutarAsync(v1, 10000, entorno.Sesion);
        await entorno.Precios.EjecutarAsync(v2, 12000, entorno.Sesion);
        await entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(v1, 5), new(v2, 8) },
            "op-ficha-001",
            entorno.Sesion);

        var ficha = await entorno.Consultas.ObtenerFichaAsync(productoId, Fecha, entorno.Sesion);
        Assert.NotNull(ficha);
        Assert.True(ficha.Producto.Activo);
        Assert.Equal(2, ficha.Variantes.Count);
        var porSku = ficha.Variantes.ToDictionary(v => v.Sku);
        Assert.Equal(10000, porSku["REM-F-004"].PrecioMinor);
        Assert.Equal(5, porSku["REM-F-004"].Existencias);
        Assert.Equal(12000, porSku["REM-F-006"].PrecioMinor);
        Assert.Equal(8, porSku["REM-F-006"].Existencias);
        Assert.Null(await entorno.Consultas.ObtenerFichaAsync("producto-inexistente", Fecha, entorno.Sesion));
    }

    [Fact]
    public async Task Repetir_carga_no_duplica_y_fallo_revierte_todo()
    {
        using var entorno = await EntornoBloque.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-R-001", null, null), entorno.Sesion);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-R-002", null, null), entorno.Sesion);

        await entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(v1, 3), new(v2, 7) },
            "op-repetida-001",
            entorno.Sesion);

        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(v1, 3), new(v2, 7) },
            "op-repetida-001",
            entorno.Sesion));
        Assert.Equal(3, await entorno.ExistenciasAsync(v1));
        Assert.Equal(7, await entorno.ExistenciasAsync(v2));

        var v3 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-R-003", null, null), entorno.Sesion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(v3, 2), new("variante-inexistente", 9) },
            "op-fallida-001",
            entorno.Sesion));
        Assert.Equal(0, await entorno.MovimientosDeAsync("op-fallida-001"));
        Assert.Equal(0, await entorno.AuditoriaDeAsync("op-fallida-001"));
        Assert.Equal(0, await entorno.ExistenciasAsync(v3));
    }

    [Fact]
    public async Task Carga_rechaza_filas_invalidas_y_con_historial()
    {
        using var entorno = await EntornoBloque.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "REM-V-001", null, null), entorno.Sesion);

        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(v1, -2) }, "op-neg-001", entorno.Sesion));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(v1, 2), new(v1, 3) }, "op-dup-001", entorno.Sesion));
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial>(), "op-vacia-001", entorno.Sesion));

        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 4) }, "op-ok-001", entorno.Sesion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(v1, 4) }, "op-otra-001", entorno.Sesion));
        Assert.Equal(4, await entorno.ExistenciasAsync(v1));
    }

    [Fact]
    public async Task Escrituras_denegadas_y_lecturas_con_sesion()
    {
        using var entorno = await EntornoBloque.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), entorno.Sesion);

        await Assert.ThrowsAsync<SesionInvalidaException>(() => entorno.Precios.EjecutarAsync(productoId, 100, null));
        await Assert.ThrowsAsync<SesionInvalidaException>(() => entorno.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(productoId, 1) }, "op-x", null));
        await Assert.ThrowsAsync<SesionInvalidaException>(() => entorno.Consultas.ObtenerFichaAsync(productoId, Fecha, null));

        using var denegado = await EntornoBloque.CrearAsyncMatrizVacia();
        var sesion = await denegado.SesionValidaAsync();
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => denegado.Precios.EjecutarAsync(productoId, 100, sesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => denegado.Carga.EjecutarAsync(
            new List<FilaCargaInicial> { new(productoId, 1) }, "op-y", sesion));
    }

    [Fact]
    public async Task Migracion_V3_V6_con_datos_y_respaldo()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(directorio, "precios-f01.db");
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV3 = MigracionesF01.Todas.Where(m => m.Version <= 3).ToList();
            Assert.Equal(new[] { 1, 2, 3 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV3, null));

            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var variantes = new RepositorioVariantes(ids);
            var precios = new RepositorioPrecios(ids);
            string varianteId;
            await using (var sesion = await fabrica.IniciarAsync())
            {
                var productos = new RepositorioProductos(ids);
                var productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Ficticio", null, null), Fecha);
                varianteId = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "FIC-V4-001", null, null), Fecha);
                await precios.FijarAsync(sesion, new NuevoPrecio(varianteId, 9000, "ARS", Fecha, null), Fecha);
                await sesion.ConfirmarAsync();
            }

            Assert.Equal(new[] { 4, 5, 6, 7 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));

            Assert.Single(Directory.GetFiles(respaldos, "*.db"));
            await using var lectura = await fabrica.IniciarLecturaAsync();
            var vigente = await precios.ObtenerVigenteAsync(lectura, varianteId, "2026-09-21T13:00:00Z");
            Assert.NotNull(vigente);
            Assert.Equal(9000, vigente.ImporteMinor);
            Assert.True(await IndiceExisteAsync(ruta, "ix_precio_vigente_unico"));

            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
        }
        finally
        {
            if (Directory.Exists(directorio))
            {
                Directory.Delete(directorio, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Busqueda_rinde_en_catalogo_representativo()
    {
        using var entorno = await EntornoBloque.CrearAsync();
        var cronometro = Stopwatch.StartNew();
        for (var i = 0; i < 500; i++)
        {
            var productoId = await entorno.CrearProducto.EjecutarAsync(
                new NuevoProducto($"Prenda-{i:000} MarcaX", i % 2 == 0 ? "MarcaX" : "MarcaY", "Cat"), entorno.Sesion);
            for (var t = 0; t < 4; t++)
            {
                var varianteId = await entorno.CrearVariante.EjecutarAsync(
                    new NuevaVariante(productoId, $"SKU-{i:000}-{t}", t.ToString(), null), entorno.Sesion);
                await entorno.Precios.EjecutarAsync(varianteId, 10000 + i, entorno.Sesion);
            }
        }

        cronometro.Restart();
        var hallados = await entorno.Consultas.BuscarProductosAsync("Prenda-1", 20, 0, entorno.Sesion);
        cronometro.Stop();
        _salida.WriteLine($"Búsqueda en 500 productos: {hallados.Count} filas en {cronometro.ElapsedMilliseconds} ms.");
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(5));

        var primero = hallados[0];
        cronometro.Restart();
        var ficha = await entorno.Consultas.ObtenerFichaAsync(primero.Id, Fecha, entorno.Sesion);
        cronometro.Stop();
        _salida.WriteLine($"Ficha con 4 variantes: {cronometro.ElapsedMilliseconds} ms.");
        Assert.NotNull(ficha);
        Assert.Equal(4, ficha.Variantes.Count);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(5));
    }

    private const string Fecha = "2026-09-21T12:00:00Z";

    private const string FechaPosterior = "2026-09-21T12:01:00Z";

    private static readonly DateTimeOffset MomentoFijo = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static async Task<bool> IndiceExisteAsync(string ruta, string indice)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $nombre;";
        comando.Parameters.AddWithValue("$nombre", indice);
        return Convert.ToInt64(await comando.ExecuteScalarAsync()) > 0;
    }

    private sealed class EntornoBloque : IDisposable
    {
        private readonly string _directorio;
        private readonly FabricaConexionesSqlite _fabrica;
        private readonly GeneradorIds _ids;
        private readonly RelojFijo _reloj;
        private readonly GestorSesiones _sesiones;

        public readonly CrearProducto CrearProducto;
        public readonly CrearVariante CrearVariante;
        public readonly FijarPrecio Precios;
        public readonly CargarExistenciaInicial Carga;
        public readonly ConsultasCatalogo Consultas;
        public readonly Autorizador Autorizador;

        private string? _sesionId;

        public string Sesion => _sesionId ?? throw new InvalidOperationException("Sin sesión registrada.");

        private EntornoBloque(string directorio, FabricaConexionesSqlite fabrica, MatrizPermisos matriz)
        {
            _directorio = directorio;
            _fabrica = fabrica;
            _ids = new GeneradorIds();
            var reloj = new RelojFijo(MomentoFijo);
            _reloj = reloj;
            _sesiones = new GestorSesiones();
            var productos = new RepositorioProductos(_ids);
            var variantes = new RepositorioVariantes(_ids);
            var precios = new RepositorioPrecios(_ids);
            var movimientos = new RepositorioMovimientos(_ids);
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            var autorizador = new Autorizador(_sesiones, new RepositorioUsuarios(_ids), new RepositorioPermisos(), _fabrica, matriz);
            Autorizador = autorizador;
            CrearProducto = new CrearProducto(productos, autorizador, _fabrica, auditoria, _ids, reloj);
            CrearVariante = new CrearVariante(variantes, productos, autorizador, _fabrica, auditoria, _ids, reloj);
            Precios = new FijarPrecio(precios, variantes, autorizador, _fabrica, auditoria, _ids, reloj);
            Carga = new CargarExistenciaInicial(movimientos, variantes, autorizador, _fabrica, auditoria, reloj);
            Consultas = new ConsultasCatalogo(productos, variantes, _sesiones, _fabrica);
        }

        public async Task<long> ContarPreciosAsync(string varianteId)
        {
            await using var conexion = await AbrirCruda();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT COUNT(*) FROM precio WHERE variante_id = $v;";
            comando.Parameters.AddWithValue("$v", varianteId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync());
        }

        public async Task<long> ContarVigentesAsync(string varianteId)
        {
            await using var conexion = await AbrirCruda();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT COUNT(*) FROM precio WHERE variante_id = $v AND vigente_hasta IS NULL;";
            comando.Parameters.AddWithValue("$v", varianteId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync());
        }

        public async Task<long> PreciosVigenteAsync(string varianteId, string fecha)
        {
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            var precios = new RepositorioPrecios(_ids);
            var vigente = await precios.ObtenerVigenteAsync(sesion, varianteId, fecha);
            return vigente?.ImporteMinor ?? -1;
        }

        public async Task<long> ExistenciasAsync(string varianteId)
        {
            var movimientos = new RepositorioMovimientos(_ids);
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            return await movimientos.SumarPorVarianteAsync(sesion, varianteId);
        }

        public async Task<long> MovimientosDeAsync(string operacionId)
        {
            await using var conexion = await AbrirCruda();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT COUNT(*) FROM movimiento_inventario WHERE operacion_id = $op;";
            comando.Parameters.AddWithValue("$op", operacionId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync());
        }

        public async Task<long> AuditoriaDeAsync(string operacionId)
        {
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), _reloj);
            await using var sesion = await _fabrica.IniciarLecturaAsync();
            return (await auditoria.ObtenerPorOperacionAsync(sesion, operacionId)).Count;
        }

        public void AvanzarReloj(TimeSpan paso)
        {
            _reloj.Avanzar(paso);
        }

        public static async Task<EntornoBloque> CrearAsync()
        {
            var matriz = MatrizPermisos.ConValoresConfirmados();
            matriz.Permitir(RolesLocal.Empleada, PermisosCatalogo.ProductosGestionar);
            matriz.Permitir(RolesLocal.Empleada, PermisosCatalogo.VariantesGestionar);
            matriz.Permitir(RolesLocal.Empleada, PermisosCatalogo.PreciosGestionar);
            matriz.Permitir(RolesLocal.Empleada, PermisosInventario.CargaInicialEstablecer);
            return await CrearConMatrizAsync(matriz);
        }

        public static async Task<EntornoBloque> CrearAsyncMatrizVacia()
        {
            return await CrearConMatrizAsync(MatrizPermisos.Vacia());
        }

        public async Task<string> SesionValidaAsync()
        {
            if (_sesionId is not null)
            {
                return _sesionId;
            }

            await using var sesion = await _fabrica.IniciarAsync();
            var usuarios = new RepositorioUsuarios(_ids);
            var hash = new Pbkdf2HashContrasenas();
            var id = await usuarios.CrearAsync(sesion, new NuevoUsuario("lector", RolesLocal.Empleada), hash.CrearHuella("clave-lectora-00"), Fecha);
            await sesion.ConfirmarAsync();
            var valida = new SesionUsuario("sesion-lectora", id, "lector", RolesLocal.Empleada, Fecha);
            _sesiones.Registrar(valida);
            _sesionId = valida.IdSesion;
            return valida.IdSesion;
        }

        private static async Task<EntornoBloque> CrearConMatrizAsync(MatrizPermisos matriz)
        {
            var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(directorio, "bloque-f02.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var entorno = new EntornoBloque(directorio, new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta)), matriz);
            await entorno.SesionValidaAsync();
            return entorno;
        }

        private async Task<SqliteConnection> AbrirCruda()
        {
            var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directorio, "bloque-f02.db"),
                Pooling = false,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            await conexion.OpenAsync();
            return conexion;
        }

        public void Dispose()
        {
            if (Directory.Exists(_directorio))
            {
                Directory.Delete(_directorio, recursive: true);
            }
        }
    }
}
