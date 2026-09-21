using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Respaldos;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Infrastructure.Tiempo;
using LocalInfantil.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Respaldos;

/// <summary>
/// Pruebas de F01-T05 sobre bases temporales aisladas que se eliminan al
/// terminar. Nunca tocan la base provisional ni usan datos reales. Cada prueba
/// abre la base restaurada y verifica su contenido: ninguna prueba de copia
/// cuenta como prueba de recuperación.
/// </summary>
public sealed class RespaldoLocalTests
{
    [Fact]
    public async Task Respaldo_recupera_exactamente_los_datos_ficticios()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var servicio = new ServicioRespaldos(new RelojSistema());

            var respaldo = await servicio.CrearRespaldoAsync(ruta, RespaldosEn(raiz), [1, 2, 3]);
            var restaurada = await servicio.RestaurarEnsayoAsync(respaldo.Ruta, EnsayoEn(raiz), [1, 2, 3]);

            Assert.NotEqual(ruta, restaurada);
            foreach (var tabla in TablasNegocio)
            {
                Assert.Equal(await ContarAsync(ruta, tabla), await ContarAsync(restaurada, tabla));
            }

            Assert.Equal("REM-REC-001", await LeerTextoAsync(restaurada, "SELECT sku FROM variante LIMIT 1;"));
            Assert.Equal("op-ficticia", await LeerTextoAsync(restaurada, "SELECT operacion_id FROM auditoria LIMIT 1;"));
            Assert.Equal("Ficticia", await LeerTextoAsync(restaurada, "SELECT nombre FROM usuario LIMIT 1;"));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Respaldo_con_WAL_y_operaciones_confirmadas()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            Assert.Equal("wal", await fabrica.EstablecerJournalAsync("wal"));

            var productos = new RepositorioProductos(new GeneradorIds());
            await using (var sesion = await fabrica.IniciarAsync())
            {
                await productos.InsertarAsync(sesion, new NuevoProducto("PostWAL", null, null), Fecha);
                await sesion.ConfirmarAsync();
            }

            Assert.Equal("wal", await ClaveValorAsync(ruta, "PRAGMA journal_mode;"));

            var servicio = new ServicioRespaldos(new RelojSistema());
            var respaldo = await servicio.CrearRespaldoAsync(ruta, RespaldosEn(raiz), [1, 2, 3]);
            var restaurada = await servicio.RestaurarEnsayoAsync(respaldo.Ruta, EnsayoEn(raiz), [1, 2, 3]);

            Assert.Equal(2L, await ContarAsync(restaurada, "producto"));

            var fabricaRestaurada = new FabricaConexionesSqlite(new OpcionesConexionSqlite(restaurada));
            await using (var sesion = await fabricaRestaurada.IniciarAsync())
            {
                await productos.InsertarAsync(sesion, new NuevoProducto("EscritoEnRestaurada", null, null), Fecha);
                await sesion.ConfirmarAsync();
            }

            Assert.Equal(3L, await ContarAsync(restaurada, "producto"));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Respaldo_verifica_integridad_y_claves()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var servicio = new ServicioRespaldos(new RelojSistema());

            var rutaRespaldo = (await servicio.CrearRespaldoAsync(ruta, RespaldosEn(raiz), [1, 2, 3])).Ruta;
            var verificacion = await servicio.VerificarAsync(rutaRespaldo, [1, 2, 3]);

            Assert.Equal(new[] { 1, 2, 3 }, verificacion.Versiones);
            Assert.True(verificacion.TamanoBytes > 0);
            Assert.Equal("ok", await ClaveValorAsync(rutaRespaldo, "PRAGMA integrity_check;"));
            Assert.Equal(0L, await ContarFilasAsync(rutaRespaldo, "PRAGMA foreign_key_check;"));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Restauracion_ocurre_en_ruta_distinta_sin_tocar_origen()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var antes = new FileInfo(ruta).Length;
            var servicio = new ServicioRespaldos(new RelojSistema());

            var respaldo = await servicio.CrearRespaldoAsync(ruta, RespaldosEn(raiz), [1, 2, 3]);
            var otroDir = EnsayoEn(raiz);
            var restaurada = await servicio.RestaurarEnsayoAsync(respaldo.Ruta, otroDir, [1, 2, 3]);

            Assert.StartsWith(otroDir, restaurada, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(ruta, restaurada);
            Assert.NotEqual(respaldo.Ruta, restaurada);
            Assert.Equal(antes, new FileInfo(ruta).Length);
            Assert.Equal(1L, await ContarAsync(restaurada, "producto"));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Base_restaurada_se_reabre_y_acepta_escrituras()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var servicio = new ServicioRespaldos(new RelojSistema());
            var respaldo = await servicio.CrearRespaldoAsync(ruta, RespaldosEn(raiz), [1, 2, 3]);
            var restaurada = await servicio.RestaurarEnsayoAsync(respaldo.Ruta, EnsayoEn(raiz), [1, 2, 3]);

            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(restaurada));
            var productos = new RepositorioProductos(new GeneradorIds());
            await using (var primera = await fabrica.IniciarAsync())
            {
                await productos.InsertarAsync(primera, new NuevoProducto("Reapertura1", null, null), Fecha);
                await primera.ConfirmarAsync();
            }

            await using (var segunda = await fabrica.IniciarAsync())
            {
                await productos.InsertarAsync(segunda, new NuevoProducto("Reapertura2", null, null), Fecha);
                await segunda.ConfirmarAsync();
            }

            Assert.Equal(3L, await ContarAsync(restaurada, "producto"));
            Assert.Equal("ok", await ClaveValorAsync(restaurada, "PRAGMA integrity_check;"));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Respaldo_corrupto_o_no_reconocido_se_rechaza()
    {
        var raiz = CrearRaizTemporal();
        var ensayo = EnsayoEn(raiz);
        Directory.CreateDirectory(ensayo);
        try
        {
            var servicio = new ServicioRespaldos(new RelojSistema());

            var basura = Path.Combine(ensayo, "basura.db");
            await File.WriteAllBytesAsync(basura, new byte[] { 1, 2, 3, 4, 5 });
            await Assert.ThrowsAsync<RespaldoInvalidoException>(
                () => servicio.RestaurarEnsayoAsync(basura, Path.Combine(raiz, "ensayo2"), null));
            Assert.False(Directory.Exists(Path.Combine(raiz, "ensayo2")));

            var extrana = Path.Combine(ensayo, "extrana.db");
            await using (var creacion = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = extrana,
                Pooling = false,
            }.ToString()))
            {
                await creacion.OpenAsync();
                using var comando = creacion.CreateCommand();
                comando.CommandText = "CREATE TABLE otra (id TEXT PRIMARY KEY);";
                await comando.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<RespaldoInvalidoException>(
                () => servicio.VerificarAsync(extrana, null));

            var ruta = BaseEn(raiz);
            await SembrarAsync(ruta);
            var respaldoV3 = await servicio.CrearRespaldoAsync(ruta, RespaldosEn(raiz), [1, 2, 3]);
            await Assert.ThrowsAsync<RespaldoInvalidoException>(
                () => servicio.VerificarAsync(respaldoV3.Ruta, [1, 2]));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Error_de_destino_falla_explicito_sin_exito_falso()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var servicio = new ServicioRespaldos(new RelojSistema());

            var archivoComun = Path.Combine(raiz, "bloqueado");
            await File.WriteAllTextAsync(archivoComun, "no es un directorio");
            await Assert.ThrowsAsync<RespaldoFallidoException>(
                () => servicio.CrearRespaldoAsync(ruta, archivoComun, [1, 2, 3]));
            Assert.Empty(Directory.GetFiles(raiz, "*.db", SearchOption.AllDirectories)
                .Where(f => !string.Equals(f, ruta, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Dos_respaldos_consecutivos_sin_sobrescritura()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var servicio = new ServicioRespaldos(new RelojSistema());
            var destino = RespaldosEn(raiz);

            var primero = await servicio.CrearRespaldoAsync(ruta, destino, [1, 2, 3]);
            var segundo = await servicio.CrearRespaldoAsync(ruta, destino, [1, 2, 3]);

            Assert.NotEqual(primero.Ruta, segundo.Ruta);
            Assert.True(File.Exists(primero.Ruta));
            Assert.True(File.Exists(segundo.Ruta));
            Assert.Equal("ok", await ClaveValorAsync(primero.Ruta, "PRAGMA integrity_check;"));
            Assert.Equal("ok", await ClaveValorAsync(segundo.Ruta, "PRAGMA integrity_check;"));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Fallo_no_destruye_respaldos_anteriores()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var servicio = new ServicioRespaldos(new RelojSistema());
            var destino = RespaldosEn(raiz);
            var previo = await servicio.CrearRespaldoAsync(ruta, destino, [1, 2, 3]);

            var archivoComun = Path.Combine(raiz, "bloqueado");
            await File.WriteAllTextAsync(archivoComun, "no es un directorio");
            await Assert.ThrowsAsync<RespaldoFallidoException>(
                () => servicio.CrearRespaldoAsync(ruta, archivoComun, [1, 2, 3]));

            Assert.True(File.Exists(previo.Ruta));
            Assert.Equal("ok", await ClaveValorAsync(previo.Ruta, "PRAGMA integrity_check;"));
            Assert.Equal(new[] { 1, 2, 3 }, (await servicio.VerificarAsync(previo.Ruta, [1, 2, 3])).Versiones);
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Publicacion_atomica_sin_temporales_ante_fallo_verificacion()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var servicio = new ServicioRespaldos(new RelojSistema());
            var destino = RespaldosEn(raiz);

            await Assert.ThrowsAsync<RespaldoInvalidoException>(
                () => servicio.CrearRespaldoAsync(ruta, destino, [1, 2, 99]));

            Assert.Empty(Directory.GetFiles(destino, "*.db"));
            Assert.Empty(Directory.GetFiles(destino, "*.tmp-*"));
            Assert.Equal(1L, await ContarAsync(ruta, "producto"));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Migrador_V1_con_datos_respalda_antes_y_migra_V3()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        var respaldos = RespaldosEn(raiz);
        try
        {
            var catalogoV1 = MigracionesF01.Todas.Where(m => m.Version == 1).ToList();
            Assert.Equal(new[] { 1 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV1, null));
            await InsertarFilaLegadaAsync(ruta);

            Assert.Equal(new[] { 2, 3 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));

            var archivo = Assert.Single(Directory.GetFiles(respaldos, "*.db"));
            Assert.Equal("ok", await ClaveValorAsync(archivo, "PRAGMA integrity_check;"));
            Assert.False(await TablaExisteAsync(archivo, "auditoria"));
            Assert.True(await TablaExisteAsync(ruta, "auditoria"));
            Assert.True(await ColumnaExisteAsync(ruta, "usuario", "huella_contrasena"));
            Assert.Equal("legada", await LeerTextoAsync(ruta, "SELECT nombre FROM producto WHERE id = 'p-legado';"));
            Assert.Equal("legada", await LeerTextoAsync(archivo, "SELECT nombre FROM producto WHERE id = 'p-legado';"));

            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Migrador_V2_con_datos_respalda_antes_y_migra_V3()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        var respaldos = RespaldosEn(raiz);
        try
        {
            var catalogoV2 = MigracionesF01.Todas.Where(m => m.Version <= 2).ToList();
            Assert.Equal(new[] { 1, 2 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV2, null));
            await InsertarFilaLegadaAsync(ruta);

            Assert.Equal(new[] { 3 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));

            var archivo = Assert.Single(Directory.GetFiles(respaldos, "*.db"));
            Assert.Equal("ok", await ClaveValorAsync(archivo, "PRAGMA integrity_check;"));
            Assert.True(await TablaExisteAsync(archivo, "auditoria"));
            Assert.False(await ColumnaExisteAsync(archivo, "usuario", "huella_contrasena"));
            Assert.True(await ColumnaExisteAsync(ruta, "usuario", "huella_contrasena"));
            Assert.Equal("legada", await LeerTextoAsync(ruta, "SELECT nombre FROM producto WHERE id = 'p-legado';"));

            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Fallo_respaldo_impide_migrar()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            var catalogoV1 = MigracionesF01.Todas.Where(m => m.Version == 1).ToList();
            Assert.Equal(new[] { 1 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV1, null));
            await InsertarFilaLegadaAsync(ruta);

            var destinoBloqueado = Path.Combine(raiz, "bloqueado");
            await File.WriteAllTextAsync(destinoBloqueado, "no es un directorio");

            await Assert.ThrowsAsync<RespaldoFallidoException>(
                () => new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, destinoBloqueado));

            Assert.False(await TablaExisteAsync(ruta, "auditoria"));
            Assert.Equal("legada", await LeerTextoAsync(ruta, "SELECT nombre FROM producto WHERE id = 'p-legado';"));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public async Task Ensayo_no_sobrescribe_ni_arrastra_auxiliares()
    {
        var raiz = CrearRaizTemporal();
        var ruta = BaseEn(raiz);
        try
        {
            await SembrarAsync(ruta);
            var servicio = new ServicioRespaldos(new RelojSistema());
            var respaldo = await servicio.CrearRespaldoAsync(ruta, RespaldosEn(raiz), [1, 2, 3]);
            var ensayo = EnsayoEn(raiz);
            var restaurada = await servicio.RestaurarEnsayoAsync(respaldo.Ruta, ensayo, [1, 2, 3]);

            Assert.Equal(new[] { Path.GetFileName(restaurada) }, NombresEn(ensayo, "*.db"));
            Assert.Empty(Directory.GetFiles(ensayo, "*.db-wal"));
            Assert.Empty(Directory.GetFiles(ensayo, "*.db-shm"));
            Assert.Empty(Directory.GetFiles(ensayo, "*.db-journal"));

            var colision = Path.Combine(raiz, "ensayo-ocupado");
            Directory.CreateDirectory(colision);
            var ocupante = Path.Combine(colision, Path.GetFileName(respaldo.Ruta));
            await File.WriteAllTextAsync(ocupante, "intacto");
            await Assert.ThrowsAsync<RespaldoFallidoException>(
                () => servicio.RestaurarEnsayoAsync(respaldo.Ruta, colision, [1, 2, 3]));
            Assert.Equal("intacto", await File.ReadAllTextAsync(ocupante));
        }
        finally
        {
            Limpiar(raiz);
        }
    }

    [Fact]
    public void Base_provisional_F00_permanece_intacta()
    {
        var ruta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalInfantil",
            "Prototype",
            "localinfantil-prototype.db");
        var info = new FileInfo(ruta);
        Assert.True(info.Exists);
        Assert.Equal(12288, info.Length);
        var esperado = new DateTime(2026, 9, 20, 23, 46, 44);
        Assert.True(
            Math.Abs((info.LastWriteTime - esperado).TotalSeconds) < 5,
            $"La base provisional cambió: {info.LastWriteTime:o}.");
    }

    private const string Fecha = "2026-09-21T12:00:00Z";

    private static readonly string[] TablasNegocio =
    {
        "usuario", "producto", "variante", "precio", "movimiento_inventario",
        "venta", "venta_linea", "pago", "caja_sesion", "caja_movimiento", "auditoria",
    };

    private static async Task InsertarFilaLegadaAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "INSERT INTO producto (id, nombre, creado_en) VALUES ('p-legado', 'legada', '2026-09-21T12:00:00Z');";
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TablaExisteAsync(string ruta, string tabla)
    {
        await using var conexion = await AbrirCrudaAsync(ruta);
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tabla;";
        comando.Parameters.AddWithValue("$tabla", tabla);
        return Convert.ToInt64(await comando.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> ColumnaExisteAsync(string ruta, string tabla, string columna)
    {
        await using var conexion = await AbrirCrudaAsync(ruta);
        using var comando = conexion.CreateCommand();
        comando.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tabla.Replace("'", "''")}') WHERE name = $columna;";
        comando.Parameters.AddWithValue("$columna", columna);
        return Convert.ToInt64(await comando.ExecuteScalarAsync()) > 0;
    }

    private static string[] NombresEn(string directorio, string patron)
    {
        return Directory.GetFiles(directorio, patron).Select(Path.GetFileName).ToArray()!;
    }

    private static string CrearRaizTemporal()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(raiz);
        return raiz;
    }

    private static string BaseEn(string raiz) => Path.Combine(raiz, "origen-f01.db");

    private static string RespaldosEn(string raiz) => Path.Combine(raiz, "respaldos");

    private static string EnsayoEn(string raiz) => Path.Combine(raiz, "ensayo");

    private static void Limpiar(string raiz)
    {
        if (Directory.Exists(raiz))
        {
            Directory.Delete(raiz, recursive: true);
        }
    }

    private static async Task SembrarAsync(string ruta)
    {
        await new MigradorBaseDatos(ruta).MigrarAsync();
        var ids = new GeneradorIds();
        var reloj = new RelojFijo(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
        var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
        var usuarios = new RepositorioUsuarios(ids);
        var productos = new RepositorioProductos(ids);
        var variantes = new RepositorioVariantes(ids);
        var precios = new RepositorioPrecios(ids);
        var movimientos = new RepositorioMovimientos(ids);
        var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
        var hash = new Pbkdf2HashContrasenas();

        await using var sesion = await fabrica.IniciarAsync();
        var usuarioId = await usuarios.CrearAsync(sesion, new NuevoUsuario("Ficticia", RolesLocal.Empleada), hash.CrearHuella("clave-ficticia-00"), Fecha);
        var productoId = await productos.InsertarAsync(sesion, new NuevoProducto("Remera", "Marca", null), Fecha);
        var varianteId = await variantes.InsertarAsync(sesion, new NuevaVariante(productoId, "REM-REC-001", "6", null), Fecha);
        await precios.FijarAsync(sesion, new NuevoPrecio(varianteId, 15000, "ARS", Fecha, null), Fecha);
        await movimientos.RegistrarAsync(sesion, new NuevoMovimiento(varianteId, "ingreso", 5, null, "op-ficticia", usuarioId), Fecha);
        await auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria("op-ficticia", "prueba.siembra", ActorAuditoria.Sistema, null, "producto", productoId, null));
        await sesion.ConfirmarAsync();
    }

    private static async Task<long> ContarAsync(string ruta, string tabla)
    {
        await using var conexion = await AbrirCrudaAsync(ruta);
        using var comando = conexion.CreateCommand();
        comando.CommandText = $"SELECT COUNT(*) FROM {tabla};";
        return Convert.ToInt64(await comando.ExecuteScalarAsync());
    }

    private static async Task<string?> LeerTextoAsync(string ruta, string sql)
    {
        await using var conexion = await AbrirCrudaAsync(ruta);
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        return await comando.ExecuteScalarAsync() as string;
    }

    private static async Task<object?> ClaveValorAsync(string ruta, string pragma)
    {
        await using var conexion = await AbrirCrudaAsync(ruta);
        using var comando = conexion.CreateCommand();
        comando.CommandText = pragma;
        return await comando.ExecuteScalarAsync();
    }

    private static async Task<long> ContarFilasAsync(string ruta, string sql)
    {
        await using var conexion = await AbrirCrudaAsync(ruta);
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        await using var lector = await comando.ExecuteReaderAsync();
        var filas = 0L;
        while (await lector.ReadAsync())
        {
            filas++;
        }

        return filas;
    }

    private static async Task<SqliteConnection> AbrirCrudaAsync(string ruta)
    {
        var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await conexion.OpenAsync();
        return conexion;
    }
}
