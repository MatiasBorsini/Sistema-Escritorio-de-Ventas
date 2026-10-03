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
using LocalInfantil.Wpf.Infra;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Inventario;

/// <summary>
/// Stock apartado/no vendible (F03-T04, D-33) sobre bases temporales
/// aisladas. Físico = disponible + apartado; apartar/reincorporar no alteran
/// el físico; la baja lo reduce de forma atómica. Sin base provisional.
/// </summary>
public sealed class ApartadoStockTests
{
    [Fact]
    public async Task Migracion_V7_V8_con_datos_respaldo_y_fallo_atomico()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV7 = MigracionesF01.Todas.Where(m => m.Version <= 7).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV7, null));
            await SembrarFisicoAsync(ruta);
            Assert.Equal(new[] { 8, 9, 10 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
            var respaldo = Assert.Single(Directory.GetFiles(respaldos, "*.db"));
            Assert.Equal("ok", await ClaveValorAsync(respaldo, "PRAGMA integrity_check;"));
            Assert.True(await TablaExisteAsync(ruta, "apartado_movimiento"));
            Assert.True(await TablaExisteAsync(ruta, "conteo_clasificacion"));
            Assert.Equal("ficticia-fisico", await LeerTextoAsync(ruta, "SELECT operacion_id FROM movimiento_inventario LIMIT 1;"));
            Assert.Equal("ficticia-rec", await LeerTextoAsync(ruta, "SELECT operacion_id FROM recepcion LIMIT 1;"));
            Assert.Empty(await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
            var catalogoRoto = catalogoV7.Concat(new[] { new Migracion(8, "rota", "CREATE TABL rota (id TEXT;"), }).ToList();
            var (dir2, ruta2) = CrearBaseTemporal();
            try
            {
                await new MigradorBaseDatos(ruta2).MigrarAsync(catalogoV7, null);
                await SembrarFisicoAsync(ruta2);
                await Assert.ThrowsAsync<SqliteException>(() => new MigradorBaseDatos(ruta2).MigrarAsync(catalogoRoto, Path.Combine(dir2, "respaldos2")));
                Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, await VersionesAsync(ruta2));
                Assert.False(await TablaExisteAsync(ruta2, "apartado_movimiento"));
                Assert.Equal("ficticia-fisico", await LeerTextoAsync(ruta2, "SELECT operacion_id FROM movimiento_inventario LIMIT 1;"));
            }
            finally { Limpiar(dir2); }
        }
        finally { Limpiar(directorio); }
    }

    [Fact]
    public async Task Ejemplo_10_apartar_1_reincorporar_y_descartar()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, codAP) = await e.CargarVarianteConStockAsync("SKU-AP", 10);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.BajaDefinitivaAutorizar, true, e.SesionDuena);

        await e.Apartar.EjecutarAsync(v1, 1, "roto", "op-apartar-1", e.SesionDuena);
        var ex1 = (await e.Consultas.ObtenerClasificadaAsync(codAP, e.SesionDuena))!;
        Assert.Equal(10, ex1.Fisico);
        Assert.Equal(9, ex1.Disponible);
        Assert.Equal(1, ex1.Apartado);

        await e.Reincorporar.EjecutarAsync(v1, 1, "revisado apto", "op-reinc-1", e.SesionDuena);
        var ex2 = (await e.Consultas.ObtenerClasificadaAsync(codAP, e.SesionDuena))!;
        Assert.Equal(10, ex2.Fisico);
        Assert.Equal(10, ex2.Disponible);
        Assert.Equal(0, ex2.Apartado);

        await e.Apartar.EjecutarAsync(v1, 1, "roto otra vez", "op-apartar-2", e.SesionDuena);
        await e.Baja.EjecutarAsync(v1, 1, "descartado", "op-baja-1", e.SesionDuena);
        var ex3 = (await e.Consultas.ObtenerClasificadaAsync(codAP, e.SesionDuena))!;
        Assert.Equal(9, ex3.Fisico);
        Assert.Equal(9, ex3.Disponible);
        Assert.Equal(0, ex3.Apartado);

        var hist = await e.Consultas.HistorialApartadosPorSkuAsync(codAP, 10, 0, e.SesionDuena);
        Assert.Equal(4, hist.Count);
        Assert.Contains(hist, h => h.Tipo == "apartar");
        Assert.Contains(hist, h => h.Tipo == "reincorporar");
        Assert.Contains(hist, h => h.Tipo == "baja_definitiva");
        Assert.All(hist, h => Assert.False(string.IsNullOrWhiteSpace(h.Motivo)));
        Assert.True(await e.HayAuditoriaDeAsync("op-apartar-1"));
        Assert.True(await e.HayAuditoriaDeAsync("op-baja-1"));
    }

    [Fact]
    public async Task Apartar_mas_que_disponible_rechaza_sin_parciales()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP2) = await e.CargarVarianteConStockAsync("SKU-AP2", 10);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Apartar.EjecutarAsync(v1, 11, "mucho", "op-ap-malo", e.SesionDuena));
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP2, e.SesionDuena))!;
        Assert.Equal(10, ex.Fisico);
        Assert.Equal(0, ex.Apartado);
        Assert.False(await e.HayAuditoriaDeAsync("op-ap-malo"));
    }

    [Fact]
    public async Task Retirar_mas_que_apartado_rechaza()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP3) = await e.CargarVarianteConStockAsync("SKU-AP3", 10);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.BajaDefinitivaAutorizar, true, e.SesionDuena);
        await e.Apartar.EjecutarAsync(v1, 1, "roto", "op-ap-ok", e.SesionDuena);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Reincorporar.EjecutarAsync(v1, 2, "mucho", "op-re-malo", e.SesionDuena));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Baja.EjecutarAsync(v1, 2, "mucho", "op-ba-malo", e.SesionDuena));
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP3, e.SesionDuena))!;
        Assert.Equal(10, ex.Fisico);
        Assert.Equal(9, ex.Disponible);
        Assert.Equal(1, ex.Apartado);
    }

    [Fact]
    public async Task Cantidades_invalidas_y_motivo_rechazados()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP4) = await e.CargarVarianteConStockAsync("SKU-AP4", 10);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.BajaDefinitivaAutorizar, true, e.SesionDuena);

        await Assert.ThrowsAsync<ArgumentException>(() => e.Apartar.EjecutarAsync(v1, 0, "m", "op-c0", e.SesionDuena));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Apartar.EjecutarAsync(v1, -1, "m", "op-cn", e.SesionDuena));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Apartar.EjecutarAsync(v1, 1, "", "op-sm", e.SesionDuena));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Reincorporar.EjecutarAsync(v1, 1, "  ", "op-rm", e.SesionDuena));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Baja.EjecutarAsync(v1, 1, "m", "", e.SesionDuena));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Apartar.EjecutarAsync("inexistente", 1, "m", "op-vx", e.SesionDuena));
    }

    [Fact]
    public async Task Ajuste_negativo_no_consume_apartadas()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP5) = await e.CargarVarianteConStockAsync("SKU-AP5", 10);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.AjusteStock, true, e.SesionDuena);
        await e.Apartar.EjecutarAsync(v1, 4, "revisión", "op-ap-aj", e.SesionDuena);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Ajuste.EjecutarAsync(v1, -7, "merma", "op-aj-malo", e.SesionDuena));
        await e.Ajuste.EjecutarAsync(v1, -6, "merma", "op-aj-ok", e.SesionDuena);
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP5, e.SesionDuena))!;
        Assert.Equal(4, ex.Fisico);
        Assert.Equal(0, ex.Disponible);
        Assert.Equal(4, ex.Apartado);
    }

    [Fact]
    public async Task Reversion_no_consume_apartadas()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionRevertir, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (_, v1, codAP6) = await e.CrearVarianteSinStockAsync("SKU-AP6");
        var recId = await e.Recepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 10, null, null) }, "op-rec-ap6", null, e.SesionDuena);
        await e.Apartar.EjecutarAsync(v1, 3, "golpe", "op-ap-rec", e.SesionDuena);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Revertir.EjecutarAsync(recId, "error", "op-rev-malo", e.SesionDuena));
        Assert.False(await e.HayReversionAsync(recId));
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP6, e.SesionDuena))!;
        Assert.Equal(10, ex.Fisico);
        Assert.Equal(7, ex.Disponible);

        await e.Reincorporar.EjecutarAsync(v1, 3, "apto", "op-reinc-rec", e.SesionDuena);
        await e.Revertir.EjecutarAsync(recId, "error", "op-rev-ok", e.SesionDuena);
        var ex2 = (await e.Consultas.ObtenerClasificadaAsync(codAP6, e.SesionDuena))!;
        Assert.Equal(0, ex2.Fisico);
    }

    [Fact]
    public async Task Conteo_parcial_y_foto_vieja_tras_clasificacion_mismo_saldo()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        var (_, v1, v2, codAP7A, codAP7B) = await e.CargarDosVariantesAsync("SKU-AP7A", "SKU-AP7B", 10, 5);
        var cid = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 10) }, "op-conteo-ap7", e.SesionDuena);
        var informe = await e.ConsultasConteos.InformeAsync(cid, e.SesionDuena);
        Assert.Single(informe);

        await e.Apartar.EjecutarAsync(v1, 2, "revisión", "op-ap-foto", e.SesionDuena);
        await e.Reincorporar.EjecutarAsync(v1, 2, "apto", "op-re-foto", e.SesionDuena);
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP7A, e.SesionDuena))!;
        Assert.Equal(10, ex.Fisico);
        Assert.Equal(0, ex.Apartado);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Aprobar.EjecutarAsync(cid, "op-aprob-mala", e.SesionDuena));
        Assert.Equal("borrador", await e.EstadoConteoAsync(cid));
        var cid2 = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 10) }, "op-conteo-ap7b", e.SesionDuena);
        await e.Aprobar.EjecutarAsync(cid2, "op-aprob-ok", e.SesionDuena);
        Assert.Equal("aprobado", await e.EstadoConteoAsync(cid2));
        var ex2 = (await e.Consultas.ObtenerClasificadaAsync(codAP7B, e.SesionDuena))!;
        Assert.Equal(5, ex2.Fisico);
    }

    [Fact]
    public async Task Conteo_con_apartado_respeta_invariante()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        var (_, v1, _, codAP8A, codAP8B) = await e.CargarDosVariantesAsync("SKU-AP8A", "SKU-AP8B", 10, 0);
        await e.Apartar.EjecutarAsync(v1, 2, "revisión", "op-ap-inv", e.SesionDuena);

        var cidOk = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 10) }, "op-c-ok", e.SesionDuena);
        await e.Aprobar.EjecutarAsync(cidOk, "op-a-ok", e.SesionDuena);
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP8A, e.SesionDuena))!;
        Assert.Equal(10, ex.Fisico);
        Assert.Equal(2, ex.Apartado);

        var cidMalo = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 1) }, "op-c-malo", e.SesionDuena);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Aprobar.EjecutarAsync(cidMalo, "op-a-mala", e.SesionDuena));
        var ex2 = (await e.Consultas.ObtenerClasificadaAsync(codAP8A, e.SesionDuena))!;
        Assert.Equal(10, ex2.Fisico);
    }

    [Fact]
    public async Task Idempotencia_y_concurrencia_mismo_operacion_id()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP9) = await e.CargarVarianteConStockAsync("SKU-AP9", 10);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);

        await e.Apartar.EjecutarAsync(v1, 2, "m", "op-dup", e.SesionDuena);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Apartar.EjecutarAsync(v1, 2, "m", "op-dup", e.SesionDuena));

        var op2 = "op-conc-" + Guid.NewGuid().ToString("N");
        var t1 = Task.Run(() => e.Apartar.EjecutarAsync(v1, 1, "m", op2, e.SesionDuena));
        var t2 = Task.Run(() => e.Apartar.EjecutarAsync(v1, 1, "m", op2, e.SesionDuena));
        var exitos = 0;
        foreach (var t in new[] { t1, t2 })
        {
            try { await t; exitos++; }
            catch (Exception) { }
        }

        Assert.Equal(1, exitos);
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP9, e.SesionDuena))!;
        Assert.Equal(3, ex.Apartado);
    }

    [Fact]
    public async Task Autorizacion_revocacion_y_auditoria()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP10) = await e.CargarVarianteConStockAsync("SKU-AP10", 10);
        var anaId = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        var ana = await e.Login.EjecutarAsync("ana", "clave-ana-00000000");

        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => e.Apartar.EjecutarAsync(v1, 1, "m", "op-ana-1", ana.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => e.Baja.EjecutarAsync(v1, 1, "m", "op-ana-2", ana.IdSesion));

        await e.Permisos.EjecutarAsync(anaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        await e.Apartar.EjecutarAsync(v1, 1, "m", "op-ana-3", ana.IdSesion);
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => e.Baja.EjecutarAsync(v1, 1, "m", "op-ana-4", ana.IdSesion));

        await e.Permisos.EjecutarAsync(anaId, PermisosInventario.NoVendibleGestionar, false, e.SesionDuena);
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => e.Apartar.EjecutarAsync(v1, 1, "m", "op-ana-5", ana.IdSesion));

        var eventos = await e.AuditoriaDeAsync("op-ana-3");
        Assert.True(eventos.Count > 0);
        Assert.All(eventos, ev => Assert.Equal(ana.UsuarioId, ev.ActorId));
        Assert.Contains(eventos, ev => ev.TipoEvento == "inventario.apartado");
    }

    [Fact]
    public async Task Baja_fallida_sin_parciales()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP11) = await e.CargarVarianteConStockAsync("SKU-AP11", 10);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.BajaDefinitivaAutorizar, true, e.SesionDuena);
        await e.Apartar.EjecutarAsync(v1, 2, "roto", "op-ap-rb", e.SesionDuena);
        var movsAntes = await e.ContarMovimientosAsync(v1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Baja.EjecutarAsync(v1, 5, "mucho", "op-ba-mala", e.SesionDuena));
        Assert.Equal(movsAntes, await e.ContarMovimientosAsync(v1));
        Assert.Equal(0, await e.ContarApartadosAsync(v1, "op-ba-mala"));
        Assert.False(await e.HayAuditoriaDeAsync("op-ba-mala"));
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP11, e.SesionDuena))!;
        Assert.Equal(10, ex.Fisico);
        Assert.Equal(2, ex.Apartado);
    }

    [Fact]
    public async Task Consultas_fisico_disponible_apartado_historial_y_vendible()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP12) = await e.CargarVarianteConStockAsync("SKU-AP12", 10);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.BajaDefinitivaAutorizar, true, e.SesionDuena);
        await e.Apartar.EjecutarAsync(v1, 3, "golpe", "op-ap-h", e.SesionDuena);

        var vendible = (await e.Consultas.ObtenerStockVendibleAsync(v1, e.SesionDuena))!;
        Assert.Equal(7, vendible.Disponible);
        Assert.Null(await e.Consultas.ObtenerStockVendibleAsync("inexistente", e.SesionDuena));
        Assert.Null(await e.Consultas.ObtenerClasificadaAsync("NO-EXISTE", e.SesionDuena));
        Assert.Empty(await e.Consultas.HistorialApartadosPorSkuAsync("NO-EXISTE", 10, 0, e.SesionDuena));

        var hist = await e.Consultas.HistorialApartadosPorSkuAsync(codAP12, 10, 0, e.SesionDuena);
        var fila = Assert.Single(hist);
        Assert.Equal("apartar", fila.Tipo);
        Assert.Equal(3, fila.Cantidad);
        Assert.Equal("golpe", fila.Motivo);
        Assert.Equal("duena", fila.ResponsableNombre);
        Assert.Equal("op-ap-h", fila.OperacionId);
    }

    [Fact]
    public async Task Recepcion_y_carga_suman_disponible()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.CargaInicialEstablecer, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (_, v1, codAP13) = await e.CrearVarianteSinStockAsync("SKU-AP13");
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, 5) }, "op-carga-ap13", e.SesionDuena);
        var ex1 = (await e.Consultas.ObtenerClasificadaAsync(codAP13, e.SesionDuena))!;
        Assert.Equal(5, ex1.Fisico);
        Assert.Equal(5, ex1.Disponible);

        await e.Apartar.EjecutarAsync(v1, 2, "revisión", "op-ap-rec", e.SesionDuena);
        await e.Recepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 3, null, null) }, "op-rec-ap13", null, e.SesionDuena);
        var ex2 = (await e.Consultas.ObtenerClasificadaAsync(codAP13, e.SesionDuena))!;
        Assert.Equal(8, ex2.Fisico);
        Assert.Equal(6, ex2.Disponible);
        Assert.Equal(2, ex2.Apartado);
    }

    [Fact]
    public async Task ViewModel_apartado_buscar_operar_baja_logout()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP14) = await e.CargarVarianteConStockAsync("SKU-AP14", 10);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.BajaDefinitivaAutorizar, true, e.SesionDuena);
        var svc = new ServiciosComerciales(e.Ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.ApartadoViewModel(svc, () => ses.IdSesion);

        vm.SkuBusqueda = codAP14;
        await vm.BuscarAsyncPublic();
        Assert.Null(vm.Error);
        Assert.NotNull(vm.Existencia);
        Assert.Equal(10, vm.Existencia!.Fisico);
        Assert.Equal(10, vm.Existencia.Disponible);

        vm.CantidadTexto = "3";
        vm.Motivo = "golpe";
        await vm.ApartarAsyncPublic();
        Assert.Null(vm.Error);
        Assert.Equal(7, vm.Existencia!.Disponible);
        Assert.Equal(3, vm.Existencia.Apartado);
        Assert.Single(vm.Historial);

        vm.CantidadTexto = "1";
        vm.Motivo = "apto";
        await vm.ReincorporarAsyncPublic();
        Assert.Null(vm.Error);
        Assert.Equal(8, vm.Existencia!.Disponible);

        vm.CantidadTexto = "2";
        vm.Motivo = "descartar";
        await vm.SolicitarBajaAsyncPublic();
        Assert.True(vm.ConfirmacionBajaVisible);
        await vm.CancelarBajaAsyncPublic();
        Assert.False(vm.ConfirmacionBajaVisible);
        var exSinBaja = (await e.Consultas.ObtenerClasificadaAsync(codAP14, e.SesionDuena))!;
        Assert.Equal(2, exSinBaja.Apartado);

        await vm.SolicitarBajaAsyncPublic();
        await vm.ConfirmarBajaAsyncPublic();
        Assert.Null(vm.Error);
        Assert.False(vm.ConfirmacionBajaVisible);
        var exBaja = (await e.Consultas.ObtenerClasificadaAsync(codAP14, e.SesionDuena))!;
        Assert.Equal(8, exBaja.Fisico);
        Assert.Equal(0, exBaja.Apartado);

        await svc.CasoCerrarSesion.EjecutarAsync(ses.IdSesion);
        await vm.BuscarAsyncPublic();
        Assert.Contains("Sesión inválida", vm.Error ?? "");
    }

    [Fact]
    public async Task ViewModel_empleada_permiso_especifico_y_doble_confirmacion()
    {
        using var e = await Entorno.CrearAsync();
        var (_, v1, codAP15) = await e.CargarVarianteConStockAsync("SKU-AP15", 10);
        var anaId = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        await e.Permisos.EjecutarAsync(anaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        var svc = new ServiciosComerciales(e.Ruta);
        var sesAna = await svc.CasoIniciarSesion.EjecutarAsync("ana", "clave-ana-00000000");
        var vm = new LocalInfantil.Wpf.ViewModels.ApartadoViewModel(svc, () => sesAna.IdSesion);

        vm.SkuBusqueda = codAP15;
        await vm.BuscarAsyncPublic();
        Assert.Null(vm.Error);
        vm.CantidadTexto = "2";
        vm.Motivo = "revisión";
        await vm.ApartarAsyncPublic();
        Assert.Null(vm.Error);

        vm.CantidadTexto = "1";
        vm.Motivo = "baja";
        await vm.SolicitarBajaAsyncPublic();
        await vm.ConfirmarBajaAsyncPublic();
        Assert.Contains("No tenés permiso para autorizar bajas definitivas", vm.Error ?? "");

        await vm.ConfirmarBajaAsyncPublic();
        Assert.Contains("No tenés permiso para autorizar bajas definitivas", vm.Error ?? "");
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP15, e.SesionDuena))!;
        Assert.Equal(2, ex.Apartado);
        Assert.Equal(10, ex.Fisico);
    }

    [Fact]
    public async Task Conteo_preV8_sin_foto_rechazo_controlado()
    {
        var (directorio, ruta) = CrearBaseTemporal();
        var respaldos = Path.Combine(directorio, "respaldos");
        try
        {
            var catalogoV7 = MigracionesF01.Todas.Where(m => m.Version <= 7).ToList();
            await new MigradorBaseDatos(ruta).MigrarAsync(catalogoV7, null);
            await SembrarConteoV7Async(ruta);
            Assert.Equal(new[] { 8, 9, 10 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas, respaldos));
            using var e = await Entorno.AbrirAsync(ruta);
            await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.ConteoAprobar, true, e.SesionDuena);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => e.Aprobar.EjecutarAsync("c-prev8", "op-aprob-prev8", e.SesionDuena));
            Assert.Contains("foto incompleta", ex.Message);
            Assert.Equal("borrador", await e.EstadoConteoAsync("c-prev8"));
        }
        finally { Limpiar(directorio); }
    }

    [Fact]
    public async Task Baja_tras_conteo_rechaza_aprobacion()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.NoVendibleGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.BajaDefinitivaAutorizar, true, e.SesionDuena);
        var (_, v1, codAP16) = await e.CargarVarianteConStockAsync("SKU-AP16", 10);
        var cid = await e.CrearConteo.EjecutarAsync(new[] { new FilaConteo(v1, 10) }, "op-c-baja", e.SesionDuena);
        await e.Apartar.EjecutarAsync(v1, 3, "roto", "op-ap-baja", e.SesionDuena);
        await e.Baja.EjecutarAsync(v1, 3, "descartado", "op-ba-baja", e.SesionDuena);
        var ex = (await e.Consultas.ObtenerClasificadaAsync(codAP16, e.SesionDuena))!;
        Assert.Equal(7, ex.Fisico);
        Assert.Equal(0, ex.Apartado);

        var err = await Assert.ThrowsAsync<InvalidOperationException>(
            () => e.Aprobar.EjecutarAsync(cid, "op-a-baja", e.SesionDuena));
        Assert.Contains("foto vieja", err.Message);
        Assert.Equal("borrador", await e.EstadoConteoAsync(cid));
    }

    private static (string Directorio, string Ruta) CrearBaseTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        return (directorio, Path.Combine(directorio, "apartado-f03.db"));
    }

    private static void Limpiar(string directorio) { if (Directory.Exists(directorio)) Directory.Delete(directorio, recursive: true); }

    private static async Task SembrarFisicoAsync(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false }.ToString());
        await conexion.OpenAsync();
        foreach (var sql in new[] {
            "INSERT INTO usuario (id, nombre, rol, creado_en) VALUES ('u-f', 'Ficticia', 'duena', '2026-09-21T12:00:00Z');",
            "INSERT INTO producto (id, nombre, creado_en) VALUES ('p-f', 'Ficticio', '2026-09-21T12:00:00Z');",
            "INSERT INTO variante (id, producto_id, sku, creado_en) VALUES ('v-f', 'p-f', 'FIC-001', '2026-09-21T12:00:00Z');",
            "INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, operacion_id, creado_en) VALUES ('m-f', 'v-f', 'ingreso', 10, 'ficticia-fisico', '2026-09-21T12:00:00Z');",
            "INSERT INTO proveedor (id, nombre, creado_en) VALUES ('pr-f', 'Prov F', '2026-09-21T12:00:00Z');",
            "INSERT INTO recepcion (id, proveedor_id, operacion_id, creado_en) VALUES ('r-f', 'pr-f', 'ficticia-rec', '2026-09-21T12:00:00Z');",
            "INSERT INTO recepcion_linea (id, recepcion_id, variante_id, cantidad) VALUES ('rl-f', 'r-f', 'v-f', 3);"})
        { using var c = conexion.CreateCommand(); c.CommandText = sql; await c.ExecuteNonQueryAsync(); }
    }

    private static async Task SembrarConteoV7Async(string ruta)
    {
        await using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false }.ToString());
        await conexion.OpenAsync();
        foreach (var sql in new[] {
            "INSERT INTO producto (id, nombre, creado_en) VALUES ('p-v8', 'Previo V8', '2026-09-21T12:00:00Z');",
            "INSERT INTO variante (id, producto_id, sku, creado_en) VALUES ('v-v8', 'p-v8', 'PREV8-001', '2026-09-21T12:00:00Z');",
            "INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, operacion_id, creado_en) VALUES ('m-v8', 'v-v8', 'ingreso', 10, 'op-fisico-prev8', '2026-09-21T12:00:00Z');",
            "INSERT INTO conteo (id, operacion_id, estado, creado_en) VALUES ('c-prev8', 'op-conteo-prev8', 'borrador', '2026-09-21T12:00:00Z');",
            "INSERT INTO conteo_detalle (conteo_id, variante_id, existencia_esperada, movimientos_contados, cantidad_contada) VALUES ('c-prev8', 'v-v8', 10, 1, 10);"})
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
        await conexion.OpenAsync(); using var comando = conexion.CreateCommand(); comando.CommandText = "SELECT version FROM schema_migrations ORDER BY version;"; await using var lector = await comando.ExecuteReaderAsync(); var versiones = new List<int>(); while (await lector.ReadAsync()) { versiones.Add(lector.GetInt32(0)); }

        return versiones;
    }

    private sealed class Entorno : IDisposable
    {
        public string Ruta { get; }
        public FabricaConexionesSqlite Fabrica { get; }
        public CrearProducto CrearProducto { get; }
        public CrearVariante CrearVariante { get; }
        public CargarExistenciaInicial Cargar { get; }
        public ApartarStock Apartar { get; }
        public ReincorporarStock Reincorporar { get; }
        public BajaDefinitivaApartado Baja { get; }
        public AjustarInventario Ajuste { get; }
        public CrearConteo CrearConteo { get; }
        public CorregirConteo CorregirConteo { get; }
        public AprobarConteo Aprobar { get; }
        public ConsultasInventario Consultas { get; }
        public ConsultasCatalogo Catalogo { get; }
        public ConsultasConteos ConsultasConteos { get; }
        public CrearProveedor CrearProveedor { get; }
        public ConfirmarRecepcion Recepcion { get; }
        public RevertirRecepcion Revertir { get; }
        public ConfigurarPermiso Permisos { get; }
        public CrearUsuario Alta { get; }
        public IniciarSesion Login { get; }
        public RepositorioApartados ReposApartados { get; }
        public RepositorioMovimientos ReposMovimientos { get; }
        public RepositorioAuditoria ReposAuditoria { get; }
        public string SesionDuena { get; }
        public string DuenaId { get; }
        private readonly string _dir;

        public Entorno(string dir, string ruta, FabricaConexionesSqlite fabrica, CrearProducto cprod, CrearVariante cvar, CargarExistenciaInicial carga, ApartarStock apartar, ReincorporarStock reinc, BajaDefinitivaApartado baja, AjustarInventario ajuste, CrearConteo crearConteo, CorregirConteo corregir, AprobarConteo aprobar, ConsultasInventario consultas, ConsultasCatalogo catalogo, ConsultasConteos consultasConteos, CrearProveedor crearProv, ConfirmarRecepcion recep, RevertirRecepcion revertir, ConfigurarPermiso perm, CrearUsuario alta, IniciarSesion login, RepositorioApartados reposApartados, RepositorioMovimientos reposMovimientos, RepositorioAuditoria reposAuditoria, string sesion, string duenaId)
        { _dir = dir; Ruta = ruta; Fabrica = fabrica; CrearProducto = cprod; CrearVariante = cvar; Cargar = carga; Apartar = apartar; Reincorporar = reinc; Baja = baja; Ajuste = ajuste; CrearConteo = crearConteo; CorregirConteo = corregir; Aprobar = aprobar; Consultas = consultas; Catalogo = catalogo; ConsultasConteos = consultasConteos; CrearProveedor = crearProv; Recepcion = recep; Revertir = revertir; Permisos = perm; Alta = alta; Login = login; ReposApartados = reposApartados; ReposMovimientos = reposMovimientos; ReposAuditoria = reposAuditoria; SesionDuena = sesion; DuenaId = duenaId; }

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "apartado.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            return await ConstruirAsync(dir, ruta);
        }

        public static async Task<Entorno> AbrirAsync(string ruta)
        {
            return await ConstruirAsync(Path.GetDirectoryName(ruta)!, ruta);
        }

        private static async Task<Entorno> ConstruirAsync(string dir, string ruta)
        {
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var reloj = new RelojFijo(DateTimeOffset.Parse("2026-09-22T00:00:00Z"));
            var sesiones = new GestorSesiones();
            var usuarios = new RepositorioUsuarios(ids);
            var permisos = new RepositorioPermisos();
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            var matriz = MatrizPermisos.ConValoresConfirmados();
            var autorizador = new Autorizador(sesiones, usuarios, permisos, fabrica, matriz);
            var hasher = new Pbkdf2HashContrasenas();
            var alta = new CrearUsuario(usuarios, hasher, autorizador, fabrica, auditoria, ids, reloj);
            var login = new IniciarSesion(usuarios, hasher, sesiones, fabrica, auditoria, ids, reloj, new OpcionesAutenticacion(0));
            var perm = new ConfigurarPermiso(permisos, usuarios, autorizador, fabrica, auditoria, ids, reloj);
            var prodRepo = new RepositorioProductos(ids);
            var varRepo = new RepositorioVariantes(ids);
            var movRepo = new RepositorioMovimientos(ids);
            var apartRepo = new RepositorioApartados(ids);
            var conteoRepo = new RepositorioConteos(ids);
            var provRepo = new RepositorioProveedores(ids);
            var recepRepo = new RepositorioRecepciones(ids);
            var crearProd = new CrearProducto(prodRepo, new RepositorioCategorias(ids), autorizador, fabrica, auditoria, ids, reloj);
            var crearVar = new CrearVariante(varRepo, prodRepo, autorizador, fabrica, auditoria, ids, reloj);
            var cargar = new CargarExistenciaInicial(movRepo, varRepo, autorizador, fabrica, auditoria, reloj);
            var apartar = new ApartarStock(apartRepo, varRepo, movRepo, autorizador, fabrica, auditoria, reloj);
            var reinc = new ReincorporarStock(apartRepo, varRepo, autorizador, fabrica, auditoria, reloj);
            var baja = new BajaDefinitivaApartado(apartRepo, varRepo, movRepo, autorizador, fabrica, auditoria, reloj);
            var ajuste = new AjustarInventario(movRepo, varRepo, apartRepo, autorizador, fabrica, auditoria, reloj);
            var crearConteo = new CrearConteo(conteoRepo, varRepo, movRepo, apartRepo, autorizador, fabrica, auditoria, ids, reloj);
            var corregir = new CorregirConteo(conteoRepo, autorizador, fabrica, auditoria, ids);
            var aprobar = new AprobarConteo(conteoRepo, movRepo, apartRepo, autorizador, fabrica, auditoria, ids, reloj);
            var consultas = new ConsultasInventario(movRepo, varRepo, conteoRepo, apartRepo, sesiones, fabrica);
            var catalogo = new ConsultasCatalogo(prodRepo, varRepo, sesiones, fabrica);
            var consultasConteos = new ConsultasConteos(conteoRepo, sesiones, fabrica);
            var crearProv = new CrearProveedor(provRepo, autorizador, fabrica, auditoria, ids, reloj);
            var confirmarRec = new ConfirmarRecepcion(recepRepo, provRepo, varRepo, movRepo, autorizador, fabrica, auditoria, reloj);
            var revertirRec = new RevertirRecepcion(recepRepo, movRepo, apartRepo, autorizador, fabrica, auditoria, reloj);
            var duenaId = await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await login.EjecutarAsync("duena", "clave-duena-000000");
            return new Entorno(dir, ruta, fabrica, crearProd, crearVar, cargar, apartar, reinc, baja, ajuste, crearConteo, corregir, aprobar, consultas, catalogo, consultasConteos, crearProv, confirmarRec, revertirRec, perm, alta, login, apartRepo, movRepo, auditoria, sesion.IdSesion, duenaId);
        }

        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        public async Task<(string Prod, string V1, string Cod)> CargarVarianteConStockAsync(string sku, long cantidad)
        {
            await Permisos.EjecutarAsync(DuenaId, PermisosCatalogo.ProductosGestionar, true, SesionDuena);
            await Permisos.EjecutarAsync(DuenaId, PermisosCatalogo.VariantesGestionar, true, SesionDuena);
            await Permisos.EjecutarAsync(DuenaId, PermisosInventario.CargaInicialEstablecer, true, SesionDuena);
            await Permisos.EjecutarAsync(DuenaId, PermisosInventario.ConteoRegistrar, true, SesionDuena);
            await Permisos.EjecutarAsync(DuenaId, PermisosInventario.ConteoAprobar, true, SesionDuena);
            var prod = await CrearProducto.EjecutarAsync(new NuevoProducto("Prod-" + sku, null, "cat-otr"), SesionDuena);
            var v1 = await CrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", null), SesionDuena);
            var cod = (await Catalogo.ObtenerVarianteAsync(v1, SesionDuena))!.CodigoInterno;
            await Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, cantidad) }, "op-carga-" + sku, SesionDuena);
            return (prod, v1, cod);
        }

        public async Task<(string Prod, string V1, string Cod)> CrearVarianteSinStockAsync(string sku)
        {
            await Permisos.EjecutarAsync(DuenaId, PermisosCatalogo.ProductosGestionar, true, SesionDuena);
            await Permisos.EjecutarAsync(DuenaId, PermisosCatalogo.VariantesGestionar, true, SesionDuena);
            var prod = await CrearProducto.EjecutarAsync(new NuevoProducto("Prod-" + sku, null, "cat-otr"), SesionDuena);
            var v1 = await CrearVariante.EjecutarAsync(new NuevaVariante(prod, null, null), SesionDuena);
            var cod = (await Catalogo.ObtenerVarianteAsync(v1, SesionDuena))!.CodigoInterno;
            return (prod, v1, cod);
        }

        public async Task<(string Prod, string V1, string V2, string Cod1, string Cod2)> CargarDosVariantesAsync(string sku1, string sku2, long cant1, long cant2)
        {
            await Permisos.EjecutarAsync(DuenaId, PermisosCatalogo.ProductosGestionar, true, SesionDuena);
            await Permisos.EjecutarAsync(DuenaId, PermisosCatalogo.VariantesGestionar, true, SesionDuena);
            await Permisos.EjecutarAsync(DuenaId, PermisosInventario.CargaInicialEstablecer, true, SesionDuena);
            await Permisos.EjecutarAsync(DuenaId, PermisosInventario.ConteoRegistrar, true, SesionDuena);
            await Permisos.EjecutarAsync(DuenaId, PermisosInventario.ConteoAprobar, true, SesionDuena);
            var prod = await CrearProducto.EjecutarAsync(new NuevoProducto("Prod-" + sku1, null, "cat-otr"), SesionDuena);
            var v1 = await CrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", null), SesionDuena);
            var v2 = await CrearVariante.EjecutarAsync(new NuevaVariante(prod, null, null), SesionDuena);
            var cod1 = (await Catalogo.ObtenerVarianteAsync(v1, SesionDuena))!.CodigoInterno;
            var cod2 = (await Catalogo.ObtenerVarianteAsync(v2, SesionDuena))!.CodigoInterno;
            await Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, cant1) }, "op-carga-" + sku1, SesionDuena);
            if (cant2 > 0)
            {
                await Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v2, cant2) }, "op-carga-" + sku2, SesionDuena);
            }

            return (prod, v1, v2, cod1, cod2);
        }

        public async Task<long> ContarMovimientosAsync(string varianteId)
        {
            await using var s = await Fabrica.IniciarLecturaAsync();
            return (await ReposMovimientos.ListarPorVarianteAsync(s, varianteId, 100, 0)).Count;
        }

        public async Task<long> ContarApartadosAsync(string varianteId, string operacionId)
        {
            await using var s = await Fabrica.IniciarLecturaAsync();
            return (await ReposApartados.ListarPorVarianteAsync(s, varianteId, 100, 0))
                .Count(h => h.OperacionId == operacionId);
        }

        public async Task<IReadOnlyList<EventoAuditado>> AuditoriaDeAsync(string operacionId)
        {
            await using var s = await Fabrica.IniciarLecturaAsync();
            return await ReposAuditoria.ObtenerPorOperacionAsync(s, operacionId);
        }

        public async Task<bool> HayAuditoriaDeAsync(string operacionId) => (await AuditoriaDeAsync(operacionId)).Count > 0;

        public async Task<bool> HayReversionAsync(string recepcionId)
        {
            await using var s = await Fabrica.IniciarLecturaAsync();
            await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Ruta, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await c.OpenAsync();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM reversion_recepcion WHERE recepcion_id = $r;";
            cmd.Parameters.AddWithValue("$r", recepcionId);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
        }

        public async Task<string> EstadoConteoAsync(string conteoId)
        {
            await using var s = await Fabrica.IniciarLecturaAsync();
            var c = new RepositorioConteos(new GeneradorIds());
            return (await c.ObtenerAsync(s, conteoId))!.Estado;
        }
    }

}
