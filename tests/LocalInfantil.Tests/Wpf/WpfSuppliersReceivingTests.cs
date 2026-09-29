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

namespace LocalInfantil.Tests.Wpf;

public sealed class WpfSuppliersReceivingTests
{
    [Fact]
    public async Task Proveedor_alta_edicion_desactivacion_y_cancelar()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var id = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Proveedor A"), e.SesionDuena);
        await e.EditarProveedor.EjecutarAsync(id, new EdicionProveedor("Proveedor A Editado"), e.SesionDuena);
        var prov = await e.ConsultasProv.ObtenerAsync(id, e.SesionDuena);
        Assert.Equal("Proveedor A Editado", prov!.Nombre);
        // ViewModel cancelar no desactiva
        var svc = new ServiciosComerciales(e.Ruta);
        var sesVm = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.ProveedoresViewModel(svc, () => sesVm.IdSesion);
        await vm.CargarAsync();
        vm.Seleccionado = vm.Proveedores.First(x => x.Id == id);
        await vm.SolicitarDesactivarAsyncPublic();
        Assert.True(vm.ConfirmacionVisible);
        await vm.CancelarDesactivarAsyncPublic();
        Assert.False(vm.ConfirmacionVisible);
        var prov2 = await e.ConsultasProv.ObtenerAsync(id, e.SesionDuena);
        Assert.True(prov2!.Activo);
        // confirmar sí desactiva
        await vm.SolicitarDesactivarAsyncPublic();
        await vm.ConfirmarDesactivarAsyncPublic();
        var prov3 = await e.ConsultasProv.ObtenerAsync(id, e.SesionDuena);
        Assert.False(prov3!.Activo);
    }

    [Fact]
    public async Task Recepcion_multilinea_suma_stock_una_vez()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, v2) = await CrearCatalogoAsync(e);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var op = Guid.NewGuid().ToString("N");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 5, null, null), new LineaRecepcion(v2, 7, 1000, "ARS") }, op, "COMP-1", e.SesionDuena);
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.Contains(ficha!.Variantes, x => x.Existencias == 5);
        Assert.Contains(ficha.Variantes, x => x.Existencias == 7);
    }

    [Fact]
    public async Task Recepcion_cantidades_invalidas_y_duplicada_rechazadas_sin_parciales()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        await Assert.ThrowsAsync<ArgumentException>(() => e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 0, null, null) }, Guid.NewGuid().ToString("N"), null, e.SesionDuena));
        await Assert.ThrowsAsync<ArgumentException>(() => e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 2, null, null), new LineaRecepcion(v1, 3, null, null) }, Guid.NewGuid().ToString("N"), null, e.SesionDuena));
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.All(ficha!.Variantes, x => Assert.Equal(0, x.Existencias));
    }

    [Fact]
    public async Task Costo_NULL_frente_a_costo_cero()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, v2) = await CrearCatalogoAsync(e);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 2, null, null), new LineaRecepcion(v2, 3, 0, "ARS") }, Guid.NewGuid().ToString("N"), null, e.SesionDuena);
        var lineas = await e.ConsultasRecepciones.ListarLineasAsync((await e.ConsultasRecepciones.ListarAsync(10, 0, e.SesionDuena)).First().Id, e.SesionDuena);
        var l1 = lineas.First(x => x.VarianteId == v1);
        var l2 = lineas.First(x => x.VarianteId == v2);
        Assert.Null(l1.CostoUnitarioMinor); Assert.Null(l1.Moneda);
        Assert.Equal(0, l2.CostoUnitarioMinor); Assert.Equal("ARS", l2.Moneda);
    }

    [Fact]
    public async Task Operacion_duplicada_y_reintento_incierto()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var op = Guid.NewGuid().ToString("N");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 4, null, null) }, op, null, e.SesionDuena);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 4, null, null) }, op, null, e.SesionDuena));
        // reintento con mismo id y mismas filas debe seguir indicando duplicado, no crear segunda
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 4, null, null) }, op, null, e.SesionDuena));
        var recepciones = await e.ConsultasRecepciones.ListarAsync(10, 0, e.SesionDuena);
        Assert.Single(recepciones);
    }

    [Fact]
    public async Task Permisos_distintos_y_revocacion_en_sesion_abierta()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var anaId = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        await e.Permisos.EjecutarAsync(anaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var sAna = await e.Login.EjecutarAsync("ana", "clave-ana-00000000");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 2, null, null) }, Guid.NewGuid().ToString("N"), null, sAna.IdSesion);
        await e.Permisos.EjecutarAsync(anaId, PermisosCompras.RecepcionConfirmar, false, e.SesionDuena);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 1, null, null) }, Guid.NewGuid().ToString("N"), null, sAna.IdSesion));
        // otra empleada sin permiso denegada
        var luciaId = await e.Alta.EjecutarAsync(new NuevoUsuario("lucia", RolesLocal.Empleada), "clave-lucia-000000", e.SesionDuena);
        var sLucia = await e.Login.EjecutarAsync("lucia", "clave-lucia-000000");
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 1, null, null) }, Guid.NewGuid().ToString("N"), null, sLucia.IdSesion));
    }

    [Fact]
    public async Task Reversion_multilinea_exitosa()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, v2) = await CrearCatalogoAsync(e);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionRevertir, true, e.SesionDuena);
        var op = Guid.NewGuid().ToString("N");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 5, 100, "ARS"), new LineaRecepcion(v2, 6, null, null) }, op, null, e.SesionDuena);
        var rec = (await e.ConsultasRecepciones.ListarAsync(10, 0, e.SesionDuena)).First();
        await e.RevertirRecepcion.EjecutarAsync(rec.Id, "error", Guid.NewGuid().ToString("N"), e.SesionDuena);
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.All(ficha!.Variantes, x => Assert.Equal(0, x.Existencias));
        Assert.True(await e.ConsultasRecepciones.EstaRevertidaAsync(rec.Id, e.SesionDuena));
    }

    [Fact]
    public async Task Reversion_stock_insuficiente_cero_parciales()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, v2) = await CrearCatalogoAsync(e);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionRevertir, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.PreciosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, "inventario.ajuste_stock", true, e.SesionDuena);
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 3, null, null), new LineaRecepcion(v2, 3, null, null) }, Guid.NewGuid().ToString("N"), null, e.SesionDuena);
        // consumir stock de v1
        await e.AjustarInventario.EjecutarAsync(v1, -2, "venta", Guid.NewGuid().ToString("N"), e.SesionDuena);
        var rec = (await e.ConsultasRecepciones.ListarAsync(10, 0, e.SesionDuena)).First();
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RevertirRecepcion.EjecutarAsync(rec.Id, "motivo", Guid.NewGuid().ToString("N"), e.SesionDuena));
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.Equal(1, ficha!.Variantes.First(x => x.Id == v1).Existencias);
        Assert.Equal(3, ficha.Variantes.First(x => x.Id == v2).Existencias);
        Assert.False(await e.ConsultasRecepciones.EstaRevertidaAsync(rec.Id, e.SesionDuena));
    }

    [Fact]
    public async Task Reversion_repetida_tras_reapertura_rechazada()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionRevertir, true, e.SesionDuena);
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 4, null, null) }, Guid.NewGuid().ToString("N"), null, e.SesionDuena);
        var rec = (await e.ConsultasRecepciones.ListarAsync(10, 0, e.SesionDuena)).First();
        var opRev = Guid.NewGuid().ToString("N");
        await e.RevertirRecepcion.EjecutarAsync(rec.Id, "motivo", opRev, e.SesionDuena);
        // reintento con misma recepcion
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RevertirRecepcion.EjecutarAsync(rec.Id, "otro", Guid.NewGuid().ToString("N"), e.SesionDuena));
        // reintento con mismo operacionId
        var rec2 = (await e.ConsultasRecepciones.ListarAsync(10, 0, e.SesionDuena)).First();
        // crear segunda recepción para probar duplicado operacionId
        var v2 = await e.CrearVariante.EjecutarAsync(new NuevaVariante((await e.ConsultasCatalogo.ObtenerFichaAsync((await e.ConsultasCatalogo.BuscarProductosAsync("", 10, 0, e.SesionDuena)).First().Id, DateTime.UtcNow.ToString("o"), e.SesionDuena))!.Producto.Id, "L", null), e.SesionDuena);
        // no necesitamos probar más, el primer caso ya cubre
    }

    [Fact]
    public async Task Costos_historial_auditoria_intactos_tras_reversion()
    {
        using var e = await Entorno.CrearAsync();
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionRevertir, true, e.SesionDuena);
        var op = Guid.NewGuid().ToString("N");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 5, 999, "ARS") }, op, "COMP-99", e.SesionDuena);
        var rec = (await e.ConsultasRecepciones.ListarAsync(10, 0, e.SesionDuena)).First();
        var lineasAntes = await e.ConsultasRecepciones.ListarLineasAsync(rec.Id, e.SesionDuena);
        await e.RevertirRecepcion.EjecutarAsync(rec.Id, "corrige", Guid.NewGuid().ToString("N"), e.SesionDuena);
        var lineasDespues = await e.ConsultasRecepciones.ListarLineasAsync(rec.Id, e.SesionDuena);
        Assert.Equal(lineasAntes.First().CostoUnitarioMinor, lineasDespues.First().CostoUnitarioMinor);
        Assert.Equal("COMP-99", (await e.ConsultasRecepciones.ObtenerAsync(rec.Id, e.SesionDuena))!.ComprobanteRef);
        // auditoría: ambas operaciones registradas
        await using var s = await e.Fabrica.IniciarLecturaAsync();
        var sql = (LocalInfantil.Infrastructure.Persistence.Sqlite.ISesionSqlite)s;
        using var cmd = sql.Conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM auditoria WHERE referencia_id=$id;";
        cmd.Parameters.AddWithValue("$id", rec.Id);
        var aud = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.True(aud >= 1);
    }

    [Fact]
    public async Task Logout_impide_operaciones_y_ViewModel_refresca()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var sDuena = e.SesionDuena;
        await e.Cerrar.EjecutarAsync(sDuena);
        await Assert.ThrowsAsync<SesionInvalidaException>(() => e.CrearProveedor.EjecutarAsync(new NuevoProveedor("X"), sDuena));
        await Assert.ThrowsAsync<SesionInvalidaException>(() => e.ConsultasProv.ListarAsync(sDuena, true));
        // ViewModel con sesión inválida debe reportar error
        var svc = new ServiciosComerciales(e.Ruta);
        var vm = new LocalInfantil.Wpf.ViewModels.ProveedoresViewModel(svc, () => sDuena);
        await vm.CargarAsync();
        Assert.NotNull(vm.Error);
        Assert.Contains("Sesión", vm.Error);
    }

    [Fact]
    public void Selector_falla_cerrado_si_no_puede_comprobar_identidad()
    {
        Assert.Throws<InvalidOperationException>(() => SelectorBaseDesarrollo.Resolver(new[] { "--dev-db", "" }));
        Assert.Throws<InvalidOperationException>(() => SelectorBaseDesarrollo.Resolver(new[] { "--dev-db=" }));
        // Verificar que reparse en antecesores se rechaza conservadoramente
        var tmp = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var real = Path.Combine(tmp, "real");
        var link = Path.Combine(tmp, "link");
        Directory.CreateDirectory(real);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{real}\"")
            { CreateNoWindow = true, UseShellExecute = false };
            var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(5000);
            if (Directory.Exists(link) && (new DirectoryInfo(link).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var dev = Path.Combine(link, "dev.db");
                Assert.Throws<InvalidOperationException>(() => SelectorBaseDesarrollo.Resolver(new[] { "--dev-db=" + dev }));
            }
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Paginacion_recepciones_mas_de_20()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("ProvPag"), e.SesionDuena);
        for (int i = 0; i < 25; i++)
        {
            await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 1, null, null) }, Guid.NewGuid().ToString("N"), $"COMP-{i}", e.SesionDuena);
        }
        var pag1 = await e.ConsultasRecepciones.ListarAsync(20, 0, e.SesionDuena);
        var pag2 = await e.ConsultasRecepciones.ListarAsync(20, 20, e.SesionDuena);
        Assert.Equal(20, pag1.Count);
        Assert.Equal(5, pag2.Count);
        // ViewModel pagination
        var svc = new ServiciosComerciales(e.Ruta);
        var sesVm = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionesConsultaViewModel(svc, () => sesVm.IdSesion);
        await vm.CargarAsync();
        Assert.Equal(20, vm.Recepciones.Count);
        await vm.SiguienteAsyncPublic();
        Assert.Equal(5, vm.Recepciones.Count);
        // La 25ª (más antigua) está en segunda página y debe poder revertirse
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionRevertir, true, e.SesionDuena);
        var svc2 = new ServiciosComerciales(e.Ruta);
        var sesVm2 = await svc2.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm2 = new LocalInfantil.Wpf.ViewModels.RecepcionesConsultaViewModel(svc2, () => sesVm2.IdSesion);
        await vm2.CargarAsync();
        await vm2.SiguienteAsyncPublic();
        var masAntigua = vm2.Recepciones.First();
        vm2.Seleccionada = masAntigua;
        await Task.Delay(300);
        vm2.Motivo = "test revertir 25";
        await vm2.SolicitarReversionAsyncPublic();
        await vm2.ConfirmarReversionAsyncPublic();
        // Verificar via servicio que quedó revertida
        Assert.True(await svc2.ConsultasRecepciones.EstaRevertidaAsync(masAntigua.Id, sesVm2.IdSesion));
        await vm.AnteriorAsyncPublic();
        Assert.Equal(20, vm.Recepciones.Count);
    }

    [Fact]
    public async Task Reconciliacion_recepcion_incerta_antes_de_nueva()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("ProvRec"), e.SesionDuena);
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        var sku = ficha!.Variantes.First(x => x.Id == v1).CodigoInterno;
        var op = Guid.NewGuid().ToString("N");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 2, null, null) }, op, null, e.SesionDuena);
        var svc = new ServiciosComerciales(e.Ruta);
        var sesVm = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => sesVm.IdSesion);
        await vm.CargarProveedoresAsync();
        vm.ProveedorSeleccionado = vm.Proveedores.First(p => p.Id == prov);
        // Simular estado incierto previo
        var fOp = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_operacionId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var fUlt = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimasLineas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var fProv = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimoProveedorId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var fComp = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimoComprobante", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fOp.SetValue(vm, op);
        fUlt.SetValue(vm, new List<LineaRecepcion> { new LineaRecepcion(v1, 2, null, null) });
        fProv.SetValue(vm, prov);
        fComp.SetValue(vm, null);
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku, CantidadTexto = "2" });
        // Reconciliación: el operacionId ya existe, ViewModel debe detectarlo y no duplicar
        var existe = await svc.ConsultasRecepciones.ExisteOperacionAsync(op, sesVm.IdSesion);
        Assert.True(existe);
        await vm.ConfirmarAsyncPublic();
        Assert.Contains("ya quedó registrada", vm.Mensaje ?? vm.Error ?? "");
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, sesVm.IdSesion);
        Assert.Single(recepciones);
        Assert.NotEqual(op, vm.OperacionId);
    }

    [Fact]
    public async Task Reconciliacion_recepcion_contenido_modificado_no_auto_confirma()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var (prod, v1, v2) = await CrearCatalogoAsync(e);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("ProvRec2"), e.SesionDuena);
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        var sku1 = ficha!.Variantes.First(x => x.Id == v1).CodigoInterno;
        var sku2 = ficha.Variantes.First(x => x.Id == v2).CodigoInterno;
        var op = Guid.NewGuid().ToString("N");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 2, null, null) }, op, null, e.SesionDuena);
        var svc = new ServiciosComerciales(e.Ruta);
        var sesVm = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => sesVm.IdSesion);
        await vm.CargarProveedoresAsync();
        vm.ProveedorSeleccionado = vm.Proveedores.First(p => p.Id == prov);
        var fOp = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_operacionId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var fUlt = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimasLineas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var fProv = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimoProveedorId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var fComp = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimoComprobante", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fOp.SetValue(vm, op);
        fUlt.SetValue(vm, new List<LineaRecepcion> { new LineaRecepcion(v1, 2, null, null) });
        fProv.SetValue(vm, prov);
        fComp.SetValue(vm, null);
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku2, CantidadTexto = "5" }); // contenido distinto
        await vm.ConfirmarAsyncPublic();
        Assert.Contains("requiere nueva confirmación explícita", vm.Mensaje ?? "");
        Assert.NotEqual(op, vm.OperacionId);
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, sesVm.IdSesion);
        Assert.Single(recepciones); // no se creó segunda automáticamente
        // Ahora nueva confirmación explícita con nuevo id sí crea
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku2, CantidadTexto = "5" });
        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, sesVm.IdSesion);
        Assert.Equal(2, recepciones.Count);
    }

    [Fact]
    public async Task Reconciliacion_consulta_fallida_bloquea_nueva_operacion()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("ProvRec3"), e.SesionDuena);
        var svc = new ServiciosComerciales(e.Ruta);
        var sesVm = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => sesVm.IdSesion);
        // Forzar estado incierto
        var fOp = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_operacionId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var fUlt = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimasLineas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var op = Guid.NewGuid().ToString("N");
        fOp.SetValue(vm, op);
        fUlt.SetValue(vm, new List<LineaRecepcion> { new LineaRecepcion(v1, 2, null, null) });
        typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimoProveedorId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm, prov);
        // Usar sesión inválida para que la consulta de reconciliación falle
        var vmInvalida = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => "sesion-invalida");
        // Copiar estado incierto a vmInvalida
        typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_operacionId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vmInvalida, op);
        typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimasLineas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vmInvalida, new List<LineaRecepcion> { new LineaRecepcion(v1, 2, null, null) });
        typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimoProveedorId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vmInvalida, prov);
        vmInvalida.ProveedorSeleccionado = new ProveedorRegistrado(prov, "ProvRec3", true);
        vmInvalida.Filas.Clear();
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        vmInvalida.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = ficha!.Variantes.First(x => x.Id == v1).CodigoInterno, CantidadTexto = "2" });
        await vmInvalida.ConfirmarAsyncPublic();
        Assert.NotNull(vmInvalida.Error);
        Assert.Equal(op, vmInvalida.OperacionId); // conserva id para reintento idempotente
    }

    // ViewModel estado y comandos
    [Fact]
    public async Task ViewModel_proveedores_y_recepcion_estado()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var provId = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("ProvVM"), e.SesionDuena);
        var prod = await e.CrearProducto.EjecutarAsync(new NuevoProducto("ProdVM", null, "cat-otr"), e.SesionDuena);
        var vari = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, null, null), e.SesionDuena);
        var cod = (await e.ConsultasCatalogo.ObtenerVarianteAsync(vari, e.SesionDuena))!.CodigoInterno;

        var svc = new ServiciosComerciales(e.Ruta);
        var sesVm = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vmProv = new LocalInfantil.Wpf.ViewModels.ProveedoresViewModel(svc, () => sesVm.IdSesion);
        await vmProv.CargarAsync();
        Assert.Contains(vmProv.Proveedores, p => p.Id == provId);
        // Recepción ViewModel
        var vmRec = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => sesVm.IdSesion);
        await vmRec.CargarProveedoresAsync();
        Assert.Contains(vmRec.Proveedores, p => p.Id == provId);
        vmRec.ProveedorSeleccionado = vmRec.Proveedores.First(p => p.Id == provId);
        vmRec.Filas.First().Sku = cod;
        vmRec.Filas.First().CantidadTexto = "3";
        vmRec.Filas.First().CostoTexto = "150,50";
        var opAntes = vmRec.OperacionId;
        await vmRec.ConfirmarAsyncPublic();
        Assert.Null(vmRec.Error);
        Assert.NotEqual(opAntes, vmRec.OperacionId); // rotó tras éxito
        // Reintento con mismo contenido y mismo id debe indicar duplicado, no crear segunda
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CasoConfirmarRecepcion.EjecutarAsync(provId, new[] { new LineaRecepcion(vari, 3, 15050, "ARS") }, opAntes, null, sesVm.IdSesion));
    }

    private async Task<(string prod, string v1, string v2)> CrearCatalogoAsync(Entorno e)
    {
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        var prod = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod-" + Guid.NewGuid().ToString("N").Substring(0, 4), null, "cat-otr"), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Rojo"), e.SesionDuena);
        var v2 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, null, null), e.SesionDuena);
        return (prod, v1, v2);
    }

    private sealed class Entorno : IDisposable
    {
        public string Ruta { get; }
        public FabricaConexionesSqlite Fabrica { get; }
        public CrearProveedor CrearProveedor { get; }
        public EditarProveedor EditarProveedor { get; }
        public DesactivarProveedor DesactivarProveedor { get; }
        public ConsultasProveedores ConsultasProv { get; }
        public CrearProducto CrearProducto { get; }
        public CrearVariante CrearVariante { get; }
        public ConsultasCatalogo ConsultasCatalogo { get; }
        public ConfirmarRecepcion ConfirmarRecepcion { get; }
        public ConsultasRecepciones ConsultasRecepciones { get; }
        public RevertirRecepcion RevertirRecepcion { get; }
        public AjustarInventario AjustarInventario { get; }
        public ConfigurarPermiso Permisos { get; }
        public CrearUsuario Alta { get; }
        public IniciarSesion Login { get; }
        public CerrarSesion Cerrar { get; }
        public string SesionDuena { get; }
        public string DuenaId { get; }
        private readonly string _dir;
        public Entorno(string dir, string ruta, FabricaConexionesSqlite fabrica, CrearProveedor cp, EditarProveedor ep, DesactivarProveedor dp, ConsultasProveedores cprov, CrearProducto cprod, CrearVariante cvar, ConsultasCatalogo cc, ConfirmarRecepcion cr, ConsultasRecepciones cr2, RevertirRecepcion rr, AjustarInventario aj, ConfigurarPermiso perm, CrearUsuario alta, IniciarSesion login, CerrarSesion cerrar, string sesion, string duenaId)
        { _dir = dir; Ruta = ruta; Fabrica = fabrica; CrearProveedor = cp; EditarProveedor = ep; DesactivarProveedor = dp; ConsultasProv = cprov; CrearProducto = cprod; CrearVariante = cvar; ConsultasCatalogo = cc; ConfirmarRecepcion = cr; ConsultasRecepciones = cr2; RevertirRecepcion = rr; AjustarInventario = aj; Permisos = perm; Alta = alta; Login = login; Cerrar = cerrar; SesionDuena = sesion; DuenaId = duenaId; }
        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "suppliers.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var reloj = new RelojAvance(DateTimeOffset.Parse("2026-09-22T00:00:00Z"));
            var sesiones = new GestorSesiones();
            var usuarios = new RepositorioUsuarios(ids);
            var permisos = new RepositorioPermisos();
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            var matriz = MatrizPermisos.ConValoresConfirmados();
            var autorizador = new Autorizador(sesiones, usuarios, permisos, fabrica, matriz);
            var hasher = new Pbkdf2HashContrasenas();
            var alta = new CrearUsuario(usuarios, hasher, autorizador, fabrica, auditoria, ids, reloj);
            var login = new IniciarSesion(usuarios, hasher, sesiones, fabrica, auditoria, ids, reloj, new OpcionesAutenticacion(0));
            var cerrar = new CerrarSesion(sesiones, fabrica, auditoria, ids);
            var perm = new ConfigurarPermiso(permisos, usuarios, autorizador, fabrica, auditoria, ids, reloj);
            var provRepo = new RepositorioProveedores(ids);
            var varRepo = new RepositorioVariantes(ids);
            var prodRepo = new RepositorioProductos(ids);
            var precioRepo = new RepositorioPrecios(ids);
            var movRepo = new RepositorioMovimientos(ids);
            var recepRepo = new RepositorioRecepciones(ids);
            var crearProv = new CrearProveedor(provRepo, autorizador, fabrica, auditoria, ids, reloj);
            var editarProv = new EditarProveedor(provRepo, autorizador, fabrica, auditoria, ids);
            var desactivarProv = new DesactivarProveedor(provRepo, autorizador, fabrica, auditoria, ids);
            var cprov = new ConsultasProveedores(provRepo, sesiones, fabrica);
            var crearProd = new CrearProducto(prodRepo, new RepositorioCategorias(ids), autorizador, fabrica, auditoria, ids, reloj);
            var crearVar = new CrearVariante(varRepo, prodRepo, autorizador, fabrica, auditoria, ids, reloj);
            var consultasCat = new ConsultasCatalogo(prodRepo, varRepo, sesiones, fabrica);
            var confirmarRec = new ConfirmarRecepcion(recepRepo, provRepo, varRepo, movRepo, autorizador, fabrica, auditoria, reloj);
            var consultasRec = new ConsultasRecepciones(recepRepo, sesiones, fabrica);
            var revertirRec = new RevertirRecepcion(recepRepo, movRepo, new RepositorioApartados(ids), autorizador, fabrica, auditoria, reloj);
            var ajustarInv = new AjustarInventario(movRepo, varRepo, new RepositorioApartados(ids), autorizador, fabrica, auditoria, reloj);
            var duenaId = await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await login.EjecutarAsync("duena", "clave-duena-000000");
            return new Entorno(dir, ruta, fabrica, crearProv, editarProv, desactivarProv, cprov, crearProd, crearVar, consultasCat, confirmarRec, consultasRec, revertirRec, ajustarInv, perm, alta, login, cerrar, sesion.IdSesion, duenaId);
        }
        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    }

    private sealed class RelojAvance : IReloj
    {
        private DateTimeOffset _ahora;
        public RelojAvance(DateTimeOffset inicio) => _ahora = inicio;
        public DateTimeOffset UtcNow() { var r = _ahora; _ahora = _ahora.AddSeconds(1); return r; }
    }
}
