using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Inventario;

public sealed class ConteoReversionTests
{
    [Fact]
    public async Task Conteo_parcial_no_supone_cero_para_no_incluidas()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "4", null), entorno.SesionDuena);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "6", null), entorno.SesionDuena);
        var v3 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "8", null), entorno.SesionDuena);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 5), new(v2, 8), new(v3, 3) }, "op-ini-parcial", entorno.SesionDuena);
        var conteoId = await entorno.CrearConteo.EjecutarAsync(new List<FilaConteo> { new(v1, 5), new(v2, 10) }, "op-conteo-parcial", entorno.SesionDuena);
        var informe = await entorno.InformeAsync(conteoId);
        Assert.Equal(2, informe.Count);
        Assert.DoesNotContain(informe, l => l.VarianteId == v3);
        Assert.Contains(informe, l => l.VarianteId == v1 && l.Esperado == 5 && l.Contado == 5 && l.Diferencia == 0);
        Assert.Contains(informe, l => l.VarianteId == v2 && l.Esperado == 8 && l.Contado == 10 && l.Diferencia == 2);
    }

    [Fact]
    public async Task Diferencias_positiva_negativa_y_cero()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "4", null), entorno.SesionDuena);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "6", null), entorno.SesionDuena);
        var v3 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "8", null), entorno.SesionDuena);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 5), new(v2, 5), new(v3, 5) }, "op-ini-dif", entorno.SesionDuena);
        var conteoId = await entorno.CrearConteo.EjecutarAsync(new List<FilaConteo> { new(v1, 8), new(v2, 3), new(v3, 5) }, "op-conteo-dif", entorno.SesionDuena);
        await entorno.AprobarConteo.EjecutarAsync(conteoId, "op-aprob-dif", entorno.SesionDuena);
        Assert.Equal(8, await entorno.ExistenciasDeAsync(v1));
        Assert.Equal(3, await entorno.ExistenciasDeAsync(v2));
        Assert.Equal(5, await entorno.ExistenciasDeAsync(v3));
        Assert.Equal(1, await entorno.MovimientosDeAsync("op-aprob-dif", v1));
        Assert.Equal(1, await entorno.MovimientosDeAsync("op-aprob-dif", v2));
        Assert.Equal(0, await entorno.MovimientosDeAsync("op-aprob-dif", v3));
        Assert.Equal("aprobado", await entorno.EstadoConteoAsync(conteoId));
    }

    [Fact]
    public async Task Variante_duplicada_y_cantidades_invalidas_se_rechazan()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), entorno.SesionDuena);
        await Assert.ThrowsAsync<ArgumentException>(() => entorno.CrearConteo.EjecutarAsync(new List<FilaConteo> { new(v1, 5), new(v1, 3) }, "op-dup", entorno.SesionDuena));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.CrearConteo.EjecutarAsync(new List<FilaConteo> { new("no-existe", 5) }, "op-bad-variante", entorno.SesionDuena));
        Assert.Equal(0, await entorno.ConteosPorOperacionAsync("op-bad-variante"));
    }

    [Fact]
    public async Task Borrador_corregido_y_luego_aprobado()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), entorno.SesionDuena);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 5) }, "op-ini-b", entorno.SesionDuena);
        var conteoId = await entorno.CrearConteo.EjecutarAsync(new List<FilaConteo> { new(v1, 3) }, "op-conteo-b", entorno.SesionDuena);
        var informe1 = await entorno.InformeAsync(conteoId);
        Assert.Equal(-2, informe1[0].Diferencia);
        await entorno.CorregirConteo.EjecutarAsync(conteoId, new List<FilaConteo> { new(v1, 7) }, entorno.SesionDuena);
        var informe2 = await entorno.InformeAsync(conteoId);
        Assert.Equal(2, informe2[0].Diferencia);
        await entorno.AprobarConteo.EjecutarAsync(conteoId, "op-aprob-b", entorno.SesionDuena);
        Assert.Equal(7, await entorno.ExistenciasDeAsync(v1));
        Assert.True(await entorno.HayAuditoriaDeAsync("op-aprob-b"));
    }

    [Fact]
    public async Task Doble_aprobacion_rechazada_incluso_tras_reapertura()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), entorno.SesionDuena);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 5) }, "op-ini-da", entorno.SesionDuena);
        var conteoId = await entorno.CrearConteo.EjecutarAsync(new List<FilaConteo> { new(v1, 7) }, "op-conteo-da", entorno.SesionDuena);
        await entorno.AprobarConteo.EjecutarAsync(conteoId, "op-aprob-da", entorno.SesionDuena);
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.AprobarConteo.EjecutarAsync(conteoId, "op-aprob-da2", entorno.SesionDuena));
        var reabierto = entorno.AprobarReabierto();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reabierto.EjecutarAsync(conteoId, "op-aprob-da3", entorno.SesionDuena));
        Assert.Equal(7, await entorno.ExistenciasDeAsync(v1));
    }

    [Fact]
    public async Task Cambio_stock_entre_conteo_y_aprobacion_con_mismo_saldo_se_rechaza()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), entorno.SesionDuena);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 5) }, "op-ini-cs", entorno.SesionDuena);
        var conteoId = await entorno.CrearConteo.EjecutarAsync(new List<FilaConteo> { new(v1, 5) }, "op-conteo-cs", entorno.SesionDuena);
        await entorno.Ajuste.EjecutarAsync(v1, 3, "Entrada", "op-aj-cs1", entorno.SesionDuena);
        await entorno.Ajuste.EjecutarAsync(v1, -3, "Salida", "op-aj-cs2", entorno.SesionDuena);
        Assert.Equal(5, await entorno.ExistenciasDeAsync(v1));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.AprobarConteo.EjecutarAsync(conteoId, "op-aprob-cs", entorno.SesionDuena));
        Assert.Contains("foto vieja", error.Message);
        Assert.Equal("borrador", await entorno.EstadoConteoAsync(conteoId));
        Assert.Equal(0, await entorno.MovimientosDeAsync("op-aprob-cs"));
        Assert.Equal(0, await entorno.AuditoriaDeAsync("op-aprob-cs"));
    }

    [Fact]
    public async Task Fallo_intermedio_en_aprobacion_no_deja_parciales()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), entorno.SesionDuena);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "6", null), entorno.SesionDuena);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 5), new(v2, 5) }, "op-ini-fi", entorno.SesionDuena);
        var conteoId = await entorno.CrearConteo.EjecutarAsync(new List<FilaConteo> { new(v1, 8), new(v2, 8) }, "op-conteo-fi", entorno.SesionDuena);
        await entorno.Ajuste.EjecutarAsync(v2, 1, "Movimiento", "op-aj-fi", entorno.SesionDuena);
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.AprobarConteo.EjecutarAsync(conteoId, "op-aprob-fi", entorno.SesionDuena));
        Assert.Equal("borrador", await entorno.EstadoConteoAsync(conteoId));
        Assert.Equal(0, await entorno.MovimientosDeAsync("op-aprob-fi"));
        Assert.Equal(0, await entorno.AuditoriaDeAsync("op-aprob-fi"));
        Assert.Equal(5, await entorno.ExistenciasDeAsync(v1));
    }

    [Fact]
    public async Task Permisos_diferentes_y_revocacion_en_sesion_abierta()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var anaId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", entorno.SesionDuena);
        var luciaId = await entorno.Alta.EjecutarAsync(new NuevoUsuario("lucia", RolesLocal.Empleada), "clave-lucia-000000", entorno.SesionDuena);
        await entorno.Permisos.EjecutarAsync(anaId, PermisosInventario.ConteoRegistrar, true, entorno.SesionDuena);
        await entorno.Permisos.EjecutarAsync(luciaId, PermisosCompras.RecepcionConfirmar, true, entorno.SesionDuena);
        var ana = await entorno.Login.EjecutarAsync("ana", "clave-ana-00000000");
        var lucia = await entorno.Login.EjecutarAsync("lucia", "clave-lucia-000000");
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), entorno.SesionDuena);
        await entorno.Carga.EjecutarAsync(new List<FilaCargaInicial> { new(v1, 5) }, "op-ini-pd", entorno.SesionDuena);
        var conteoId = await entorno.CrearConteoConSesionAsync(new List<FilaConteo> { new(v1, 5) }, "op-conteo-pd", ana.IdSesion);
        Assert.NotNull(conteoId);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => entorno.CrearConteoConSesionAsync(new List<FilaConteo> { new(v1, 5) }, "op-conteo-pd2", lucia.IdSesion));
        await entorno.Permisos.EjecutarAsync(anaId, PermisosInventario.ConteoRegistrar, false, entorno.SesionDuena);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => entorno.CrearConteoConSesionAsync(new List<FilaConteo> { new(v1, 5) }, "op-conteo-pd3", ana.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => entorno.Permisos.EjecutarAsync(luciaId, PermisosInventario.ConteoRegistrar, true, ana.IdSesion));
    }

    [Fact]
    public async Task Reversion_exitosa_multilinea()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var proveedorId = await entorno.Proveedores.CrearProveedorAsync(new NuevoProveedor("Mayorista"), entorno.SesionDuena);
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "4", null), entorno.SesionDuena);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "6", null), entorno.SesionDuena);
        var recepcionId = await entorno.Recepcion.EjecutarAsync(proveedorId, new List<LineaRecepcion> { new(v1, 10, 8000, "ARS"), new(v2, 6, null, null) }, "op-rec-rv", "FC-001", entorno.SesionDuena);
        Assert.Equal(10, await entorno.ExistenciasDeAsync(v1));
        await entorno.Revertir.EjecutarAsync(recepcionId, "Error de carga", "op-rev-rv", entorno.SesionDuena);
        Assert.Equal(0, await entorno.ExistenciasDeAsync(v1));
        Assert.Equal(0, await entorno.ExistenciasDeAsync(v2));
        Assert.Equal(2, await entorno.MovimientosDeAsync("op-rev-rv"));
        Assert.True(await entorno.HayAuditoriaDeAsync("op-rev-rv"));
        Assert.Equal(8000, await entorno.CostoDeAsync(recepcionId, v1));
        Assert.Null(await entorno.CostoDeAsync(recepcionId, v2));
        Assert.Equal("Error de carga", await entorno.MotivoReversionAsync(recepcionId));
    }

    [Fact]
    public async Task Reversion_duplicada_tras_reapertura_se_rechaza()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var proveedorId = await entorno.Proveedores.CrearProveedorAsync(new NuevoProveedor("Mayorista"), entorno.SesionDuena);
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), entorno.SesionDuena);
        var recepcionId = await entorno.Recepcion.EjecutarAsync(proveedorId, new List<LineaRecepcion> { new(v1, 4, null, null) }, "op-rec-rd", null, entorno.SesionDuena);
        await entorno.Revertir.EjecutarAsync(recepcionId, "Duplicada", "op-rev-rd", entorno.SesionDuena);
        var reabierta = entorno.RevertirReabierta();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reabierta.EjecutarAsync(recepcionId, "Duplicada", "op-rev-rd2", entorno.SesionDuena));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Revertir.EjecutarAsync(recepcionId, "Duplicada", "op-rev-rd3", entorno.SesionDuena));
        Assert.Equal(0, await entorno.ExistenciasDeAsync(v1));
    }

    [Fact]
    public async Task Reversion_stock_insuficiente_rechazo_atomico()
    {
        using var entorno = await EntornoStocktake.CrearAsync();
        var proveedorId = await entorno.Proveedores.CrearProveedorAsync(new NuevoProveedor("Mayorista"), entorno.SesionDuena);
        var productoId = await entorno.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), entorno.SesionDuena);
        var v1 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "4", null), entorno.SesionDuena);
        var v2 = await entorno.CrearVariante.EjecutarAsync(new NuevaVariante(productoId, "6", null), entorno.SesionDuena);
        var recepcionId = await entorno.Recepcion.EjecutarAsync(proveedorId, new List<LineaRecepcion> { new(v1, 5, null, null), new(v2, 5, null, null) }, "op-rec-ri", null, entorno.SesionDuena);
        await entorno.Ajuste.EjecutarAsync(v1, -3, "Venta", "op-venta-ri", entorno.SesionDuena);
        Assert.Equal(2, await entorno.ExistenciasDeAsync(v1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.Revertir.EjecutarAsync(recepcionId, "Intento", "op-rev-ri", entorno.SesionDuena));
        Assert.Equal(2, await entorno.ExistenciasDeAsync(v1));
        Assert.Equal(5, await entorno.ExistenciasDeAsync(v2));
        Assert.Equal(0, await entorno.MovimientosDeAsync("op-rev-ri"));
        Assert.Equal(0, await entorno.AuditoriaDeAsync("op-rev-ri"));
        Assert.False(await entorno.HayReversionAsync(recepcionId, "op-rev-ri"));
    }

    [Fact]
    public async Task Migracion_V6_V7_con_datos_respaldo_y_fallo_atomico()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV6 = MigracionesF01.Todas.Where(m => m.Version <= 6).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV6, null));
            await SembrarRecepcionSoloAsync(ruta);
            Assert.Equal(new[] { 7, 8, 9, 10, 11, 12, 13, 14 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
            var respaldo = Assert.Single(Directory.GetFiles(respaldos, "*.db"));
            Assert.Equal("ok", await ClaveValorAsync(respaldo, "PRAGMA integrity_check;"));
            Assert.True(await TablaExisteAsync(ruta, "conteo"));
            Assert.True(await TablaExisteAsync(ruta, "reversion_recepcion"));
            Assert.Equal("ficticia-rec", await LeerTextoAsync(ruta, "SELECT operacion_id FROM recepcion LIMIT 1;"));
            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
            var catalogoRoto = catalogoV6.Concat(new[] { new Migracion(7, "rota", "CREATE TABL rota (id TEXT;"), }).ToList();
            var (dir2, ruta2) = CrearBaseTemporal();
            try
            {
                await new MigradorBaseDatos(ruta2).MigrarAsync(catalogoV6, null);
                await Assert.ThrowsAsync<SqliteException>(() => new MigradorBaseDatos(ruta2).MigrarAsync(catalogoRoto, Path.Combine(dir2, "respaldos2")));
                Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, await VersionesAsync(ruta2));
                Assert.False(await TablaExisteAsync(ruta2, "conteo"));
            }
            finally { Limpiar(dir2); }
        }
        finally { Limpiar(directorio); }
    }

    private static readonly DateTimeOffset MomentoFijo = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static (string Directorio, string Ruta) CrearBaseTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        return (directorio, Path.Combine(directorio, "stocktake-f03.db"));
    }
    private static void Limpiar(string directorio) { if (Directory.Exists(directorio)) Directory.Delete(directorio, recursive: true); }
    private static async Task SembrarConteoYRecepcionAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false }.ToString());
        await conexion.OpenAsync();
        foreach (var sql in new[] {
            "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-f', 'Ficticia', 'duena', '2026-09-21T12:00:00Z');",
            "INSERT INTO producto (id, nombre, creado_en) VALUES ('p-f', 'Ficticio', '2026-09-21T12:00:00Z');",
            "INSERT INTO variante (id, producto_id, sku, creado_en) VALUES ('v-f', 'p-f', 'FIC-001', '2026-09-21T12:00:00Z');",
            "INSERT INTO proveedor (id, nombre, creado_en) VALUES ('pr-f', 'Prov F', '2026-09-21T12:00:00Z');",
            "INSERT INTO recepcion (id, proveedor_id, operacion_id, creado_en) VALUES ('r-f', 'pr-f', 'ficticia-rec', '2026-09-21T12:00:00Z');",
            "INSERT INTO recepcion_linea (id, recepcion_id, variante_id, cantidad) VALUES ('rl-f', 'r-f', 'v-f', 3);",
            "INSERT INTO conteo (id, operacion_id, estado, creado_en) VALUES ('c-f', 'ficticia-conteo', 'borrador', '2026-09-21T12:00:00Z');"})
        { using var c = conexion.CreateCommand(); c.CommandText = sql; await c.ExecuteNonQueryAsync(); }
    }

    private static async Task SembrarRecepcionSoloAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false }.ToString());
        await conexion.OpenAsync();
        foreach (var sql in new[] {
            "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-f', 'Ficticia', 'duena', '2026-09-21T12:00:00Z');",
            "INSERT INTO producto (id, nombre, creado_en) VALUES ('p-f', 'Ficticio', '2026-09-21T12:00:00Z');",
            "INSERT INTO variante (id, producto_id, sku, creado_en) VALUES ('v-f', 'p-f', 'FIC-001', '2026-09-21T12:00:00Z');",
            "INSERT INTO proveedor (id, nombre, creado_en) VALUES ('pr-f', 'Prov F', '2026-09-21T12:00:00Z');",
            "INSERT INTO recepcion (id, proveedor_id, operacion_id, creado_en) VALUES ('r-f', 'pr-f', 'ficticia-rec', '2026-09-21T12:00:00Z');",
            "INSERT INTO recepcion_linea (id, recepcion_id, variante_id, cantidad) VALUES ('rl-f', 'r-f', 'v-f', 3);"})
        { using var c = conexion.CreateCommand(); c.CommandText = sql; await c.ExecuteNonQueryAsync(); }
    }
    private static async Task<string?> LeerTextoAsync(string ruta, string sql)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await conexion.OpenAsync(); using var comando = conexion.CreateCommand(); comando.CommandText = sql; return await comando.ExecuteScalarAsync() as string;
    }
    private static async Task<bool> TablaExisteAsync(string ruta, string tabla)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await conexion.OpenAsync(); using var comando = conexion.CreateCommand(); comando.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $t;"; comando.Parameters.AddWithValue("$t", tabla); return Convert.ToInt64(await comando.ExecuteScalarAsync()) > 0;
    }
    private static async Task<string?> ClaveValorAsync(string ruta, string pragma)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await conexion.OpenAsync(); using var comando = conexion.CreateCommand(); comando.CommandText = pragma; return await comando.ExecuteScalarAsync() as string;
    }
    private static async Task<List<int>> VersionesAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await conexion.OpenAsync(); using var comando = conexion.CreateCommand(); comando.CommandText = "SELECT version FROM schema_migrations ORDER BY version;"; await using var lector = await comando.ExecuteReaderAsync(); var v = new List<int>(); while (await lector.ReadAsync()) v.Add(lector.GetInt32(0)); return v;
    }

    public sealed class EntornoStocktake : IDisposable
    {
        private readonly string _directorio;
        private readonly FabricaConexionesSqlite _fabrica;
        private readonly GeneradorIds _ids;
        private readonly RelojFijo _reloj;
        private readonly GestorSesiones _sesiones;
        public readonly CrearProducto CrearProducto;
        public readonly CrearVariante CrearVariante;
        public readonly CargarExistenciaInicial Carga;
        public readonly AjustarInventario Ajuste;
        public readonly CrearConteo CrearConteo;
        public readonly CorregirConteo CorregirConteo;
        public readonly AprobarConteo AprobarConteo;
        public readonly RevertirRecepcion Revertir;
        public readonly ConsultasInventario ConsultasInv;
        public readonly ConfirmarRecepcion Recepcion;
        public readonly JuegoProveedores Proveedores;
        public readonly ConfigurarPermiso Permisos;
        public readonly Autorizador Autorizador;
        public readonly CrearUsuario Alta;
        public readonly IniciarSesion Login;
        private string? _sesionId;
        public string SesionDuena => _sesionId ?? throw new InvalidOperationException("Sin sesión registrada.");
        private EntornoStocktake(string directorio, FabricaConexionesSqlite fabrica, MatrizPermisos matriz)
        {
            _directorio = directorio; _fabrica = fabrica; _ids = new GeneradorIds(); var reloj = new RelojFijo(MomentoFijo); _reloj = reloj; _sesiones = new GestorSesiones();
            var productos = new RepositorioProductos(_ids); var variantes = new RepositorioVariantes(_ids); var movimientos = new RepositorioMovimientos(_ids);
            var proveedores = new RepositorioProveedores(_ids); var recepciones = new RepositorioRecepciones(_ids); var conteos = new RepositorioConteos(_ids); var apartados = new RepositorioApartados(_ids);
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj); var usuarios = new RepositorioUsuarios(_ids); var permisos = new RepositorioPermisos();
            var autorizador = new Autorizador(_sesiones, usuarios, permisos, _fabrica, matriz); Autorizador = autorizador;
            CrearProducto = new CrearProducto(productos, new RepositorioCategorias(_ids), autorizador, _fabrica, auditoria, _ids, reloj);
            CrearVariante = new CrearVariante(variantes, productos, autorizador, _fabrica, auditoria, _ids, reloj);
            Carga = new CargarExistenciaInicial(movimientos, variantes, autorizador, _fabrica, auditoria, reloj);
            Ajuste = new AjustarInventario(movimientos, variantes, apartados, autorizador, _fabrica, auditoria, reloj);
            CrearConteo = new CrearConteo(conteos, variantes, movimientos, apartados, autorizador, _fabrica, auditoria, _ids, reloj);
            CorregirConteo = new CorregirConteo(conteos, autorizador, _fabrica, auditoria, _ids);
            AprobarConteo = new AprobarConteo(conteos, movimientos, apartados, autorizador, _fabrica, auditoria, _ids, reloj);
            Revertir = new RevertirRecepcion(recepciones, movimientos, apartados, autorizador, _fabrica, auditoria, reloj);
            ConsultasInv = new ConsultasInventario(movimientos, variantes, new RepositorioConteos(_ids), apartados, _sesiones, _fabrica);
            Proveedores = new JuegoProveedores(proveedores, autorizador, _fabrica, auditoria, _ids, reloj);
            Permisos = new ConfigurarPermiso(permisos, usuarios, autorizador, _fabrica, auditoria, _ids, reloj);
            Recepcion = new ConfirmarRecepcion(recepciones, proveedores, variantes, movimientos, autorizador, _fabrica, auditoria, reloj);
            Alta = new CrearUsuario(usuarios, new Pbkdf2HashContrasenas(), autorizador, _fabrica, auditoria, _ids, reloj);
            Login = new IniciarSesion(usuarios, new Pbkdf2HashContrasenas(), _sesiones, _fabrica, auditoria, _ids, reloj, new OpcionesAutenticacion(0));
        }
        public static async Task<EntornoStocktake> CrearAsync()
        {
            var matriz = MatrizPermisos.ConValoresConfirmados();
            matriz.Permitir(RolesLocal.Duena, PermisosCatalogo.ProductosGestionar);
            matriz.Permitir(RolesLocal.Duena, PermisosCatalogo.VariantesGestionar);
            matriz.Permitir(RolesLocal.Duena, PermisosInventario.CargaInicialEstablecer);
            matriz.Permitir(RolesLocal.Duena, PermisosInventario.AjusteStock);
            matriz.Permitir(RolesLocal.Duena, PermisosInventario.ConteoRegistrar);
            matriz.Permitir(RolesLocal.Duena, PermisosInventario.ConteoAprobar);
            matriz.Permitir(RolesLocal.Duena, PermisosCompras.ProveedoresGestionar);
            matriz.Permitir(RolesLocal.Duena, PermisosCompras.RecepcionConfirmar);
            matriz.Permitir(RolesLocal.Duena, PermisosCompras.RecepcionRevertir);
            var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(directorio, "stocktake-f03.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var entorno = new EntornoStocktake(directorio, new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta)), matriz);
            var id = await entorno.AltaDuenaAsync(); var sesion = await entorno.LoginAsync(id); entorno._sesionId = sesion.IdSesion; return entorno;
        }
        private async Task<string> AltaDuenaAsync()
        {
            await using var sesion = await _fabrica.IniciarAsync();
            var usuarios = new RepositorioUsuarios(_ids); var hash = new Pbkdf2HashContrasenas();
            var id = await usuarios.CrearAsync(sesion, new NuevoUsuario("duena", RolesLocal.Duena), hash.CrearHuella("clave-duena-000000"), "2026-09-21T12:00:00Z");
            await sesion.ConfirmarAsync(); return id;
        }
        private async Task<SesionUsuario> LoginAsync(string usuarioId)
        {
            var usuarios = new RepositorioUsuarios(_ids); await using var lectura = await _fabrica.IniciarLecturaAsync();
            var usuario = await usuarios.ObtenerPorIdAsync(lectura, usuarioId);
            var valida = new SesionUsuario(Guid.NewGuid().ToString("N"), usuarioId, usuario!.Nombre, usuario.Rol, "2026-09-21T12:00:00Z");
            _sesiones.Registrar(valida); return valida;
        }
        public async Task<string> CrearConteoConSesionAsync(IReadOnlyList<FilaConteo> filas, string op, string sesionId) => await CrearConteo.EjecutarAsync(filas, op, sesionId);
        public async Task<long> ExistenciasDeAsync(string varianteId) { var m = new RepositorioMovimientos(_ids); await using var s = await _fabrica.IniciarLecturaAsync(); return await m.SumarPorVarianteAsync(s, varianteId); }
        public async Task<long> MovimientosDeAsync(string operacionId) { await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directorio, "stocktake-f03.db"), Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()); await c.OpenAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM movimiento_inventario WHERE operacion_id = $op;"; cmd.Parameters.AddWithValue("$op", operacionId); return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
        public async Task<long> MovimientosDeAsync(string operacionId, string varianteId) { await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directorio, "stocktake-f03.db"), Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()); await c.OpenAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM movimiento_inventario WHERE operacion_id = $op AND variante_id = $v;"; cmd.Parameters.AddWithValue("$op", operacionId); cmd.Parameters.AddWithValue("$v", varianteId); return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
        public async Task<long> AuditoriaDeAsync(string operacionId) { var a = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), _reloj); await using var s = await _fabrica.IniciarLecturaAsync(); return (await a.ObtenerPorOperacionAsync(s, operacionId)).Count; }
        public async Task<string> EstadoConteoAsync(string conteoId) { var c = new RepositorioConteos(_ids); await using var s = await _fabrica.IniciarLecturaAsync(); return (await c.ObtenerAsync(s, conteoId))!.Estado; }
        public async Task<IReadOnlyList<InformeConteoLinea>> InformeAsync(string conteoId) { var c = new RepositorioConteos(_ids); await using var s = await _fabrica.IniciarLecturaAsync(); return await c.InformeAsync(s, conteoId); }
        public async Task<int> ConteosPorOperacionAsync(string operacionId) { await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directorio, "stocktake-f03.db"), Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()); await c.OpenAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM conteo WHERE operacion_id = $op;"; cmd.Parameters.AddWithValue("$op", operacionId); return Convert.ToInt32(await cmd.ExecuteScalarAsync()); }
        public RevertirRecepcion RevertirReabierta() { var f = new FabricaConexionesSqlite(new OpcionesConexionSqlite(Path.Combine(_directorio, "stocktake-f03.db"))); var a = new Autorizador(_sesiones, new RepositorioUsuarios(_ids), new RepositorioPermisos(), f, MatrizDuena()); return new RevertirRecepcion(new RepositorioRecepciones(_ids), new RepositorioMovimientos(_ids), new RepositorioApartados(_ids), a, f, new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), _reloj), _reloj); }
        public AprobarConteo AprobarReabierto() { var f = new FabricaConexionesSqlite(new OpcionesConexionSqlite(Path.Combine(_directorio, "stocktake-f03.db"))); var a = new Autorizador(_sesiones, new RepositorioUsuarios(_ids), new RepositorioPermisos(), f, MatrizDuena()); return new AprobarConteo(new RepositorioConteos(_ids), new RepositorioMovimientos(_ids), new RepositorioApartados(_ids), a, f, new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), _reloj), _ids, _reloj); }
        private static MatrizPermisos MatrizDuena() { var m = MatrizPermisos.ConValoresConfirmados(); m.Permitir(RolesLocal.Duena, PermisosInventario.ConteoAprobar); m.Permitir(RolesLocal.Duena, PermisosCompras.RecepcionRevertir); return m; }
        public async Task<string?> MotivoReversionAsync(string recepcionId) { await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directorio, "stocktake-f03.db"), Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()); await c.OpenAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT motivo FROM reversion_recepcion WHERE recepcion_id = $id;"; cmd.Parameters.AddWithValue("$id", recepcionId); return await cmd.ExecuteScalarAsync() as string; }
        public async Task<long?> CostoDeAsync(string recepcionId, string varianteId) { await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directorio, "stocktake-f03.db"), Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()); await c.OpenAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT costo_unitario_minor FROM recepcion_linea WHERE recepcion_id = $r AND variante_id = $v;"; cmd.Parameters.AddWithValue("$r", recepcionId); cmd.Parameters.AddWithValue("$v", varianteId); var v = await cmd.ExecuteScalarAsync(); return v is null || v is DBNull ? null : Convert.ToInt64(v); }
        public async Task<bool> HayReversionAsync(string recepcionId, string operacionId) { await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directorio, "stocktake-f03.db"), Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()); await c.OpenAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM reversion_recepcion WHERE recepcion_id = $r AND operacion_id = $op;"; cmd.Parameters.AddWithValue("$r", recepcionId); cmd.Parameters.AddWithValue("$op", operacionId); return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0; }
        public async Task<bool> HayAuditoriaDeAsync(string operacionId) => await AuditoriaDeAsync(operacionId) > 0;
        public async Task AltaDuenaPublicAsync() => _ = await AltaDuenaAsync();
        public sealed class JuegoProveedores
        {
            private readonly IRepositorioProveedores _proveedores; private readonly IAutorizador _autorizador; private readonly IFabricaSesionesPersistencia _fabrica; private readonly IRepositorioAuditoria _auditoria; private readonly IGeneradorIds _ids; private readonly IReloj _reloj;
            public JuegoProveedores(IRepositorioProveedores proveedores, IAutorizador autorizador, IFabricaSesionesPersistencia fabrica, IRepositorioAuditoria auditoria, IGeneradorIds ids, IReloj reloj) { _proveedores = proveedores; _autorizador = autorizador; _fabrica = fabrica; _auditoria = auditoria; _ids = ids; _reloj = reloj; }
            public Task<string> CrearProveedorAsync(NuevoProveedor datos, string sesionId) => new CrearProveedor(_proveedores, _autorizador, _fabrica, _auditoria, _ids, _reloj).EjecutarAsync(datos, sesionId);
        }
        private const string Fecha = "2026-09-21T12:00:00Z";
        public void Dispose() { if (Directory.Exists(_directorio)) Directory.Delete(_directorio, recursive: true); }
    }
}
