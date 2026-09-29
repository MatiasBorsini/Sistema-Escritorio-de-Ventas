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

namespace LocalInfantil.Tests.Wpf;

public sealed class WpfCatalogTests
{
    [Fact]
    public async Task Duena_sin_permisos_escrituras_denegadas()
    {
        using var e = await Entorno.CrearAsync();
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, null), e.SesionDuena));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.CrearVariante.EjecutarAsync(new NuevaVariante("prod", null, null), e.SesionDuena));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.FijarPrecio.EjecutarAsync("var", 1000, e.SesionDuena));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial("var", 5) }, Guid.NewGuid().ToString("N"), e.SesionDuena));
    }

    [Fact]
    public async Task Concesion_individual_autoriza_operaciones()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.PreciosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.CargaInicialEstablecer, true, e.SesionDuena);
        var prodId = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", "Mimo", "cat-rem"), e.SesionDuena);
        var varId = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prodId, "M", "Rojo"), e.SesionDuena);
        var precioId = await e.FijarPrecio.EjecutarAsync(varId, 2500, e.SesionDuena);
        Assert.NotEmpty(precioId);
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(varId, 10) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var ficha = await e.Consultas.ObtenerFichaAsync(prodId, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.NotNull(ficha);
        Assert.Single(ficha!.Variantes);
        Assert.Equal(10, ficha.Variantes[0].Existencias);
    }

    [Fact]
    public async Task Empleada_permisos_diferentes()
    {
        using var e = await Entorno.CrearAsync();
        var anaId = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        var luciaId = await e.Alta.EjecutarAsync(new NuevoUsuario("lucia", RolesLocal.Empleada), "clave-lucia-000000", e.SesionDuena);
        await e.Permisos.EjecutarAsync(anaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(anaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(luciaId, PermisosCatalogo.PreciosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(luciaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        var sAna = await e.Login.EjecutarAsync("ana", "clave-ana-00000000");
        var sLucia = await e.Login.EjecutarAsync("lucia", "clave-lucia-000000");
        var prodId = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Pantalon", null, "cat-pan"), sAna.IdSesion);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.CrearProducto.EjecutarAsync(new NuevoProducto("Otro", null, null), sLucia.IdSesion));
        var varId = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prodId, null, null), sAna.IdSesion);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.FijarPrecio.EjecutarAsync(varId, 1000, sAna.IdSesion));
        var pid = await e.FijarPrecio.EjecutarAsync(varId, 1200, sLucia.IdSesion);
        Assert.NotEmpty(pid);
    }

    [Fact]
    public async Task Revocacion_en_sesion_abierta_bloquea()
    {
        using var e = await Entorno.CrearAsync();
        var anaId = await e.Alta.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", e.SesionDuena);
        await e.Permisos.EjecutarAsync(anaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        var sAna = await e.Login.EjecutarAsync("ana", "clave-ana-00000000");
        var prodId = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), sAna.IdSesion);
        Assert.NotEmpty(prodId);
        await e.Permisos.EjecutarAsync(anaId, PermisosCatalogo.ProductosGestionar, false, e.SesionDuena);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.CrearProducto.EjecutarAsync(new NuevoProducto("Otra", null, null), sAna.IdSesion));
    }

    [Fact]
    public async Task Creacion_edicion_desactivacion_producto()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        var prodId = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Buzo", "Marca", "cat-buz"), e.SesionDuena);
        await e.EditarProducto.EjecutarAsync(prodId, new EdicionProducto("Buzo Editado", null, "cat-buz"), e.SesionDuena);
        var ficha = await e.Consultas.ObtenerFichaAsync(prodId, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.Equal("Buzo Editado", ficha!.Producto.Nombre);
        Assert.Null(ficha.Producto.Marca);
        await e.DesactivarProducto.EjecutarAsync(prodId, e.SesionDuena);
        var prod = await e.Consultas.ObtenerProductoAsync(prodId, e.SesionDuena);
        Assert.False(prod!.Activo);
    }

    [Fact]
    public async Task Variantes_con_y_sin_talle_color_y_codigos_unicos()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        var prodId = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.SesionDuena);
        var prod = await e.Consultas.ObtenerProductoAsync(prodId, e.SesionDuena);
        Assert.NotNull(prod);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prodId, "M", "Rojo"), e.SesionDuena);
        var v2 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prodId, null, null), e.SesionDuena);
        var v3 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prodId, "L", null), e.SesionDuena);
        Assert.NotEqual(v1, v2);
        // Código interno automático: la cuarta variante también se crea, con
        // código secuencial único (el duplicado manual ya no existe).
        var v4 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prodId, "XL", "Azul"), e.SesionDuena);
        await e.EditarVariante.EjecutarAsync(v2, new EdicionVariante("S", "Verde"), e.SesionDuena);
        var ficha = await e.Consultas.ObtenerFichaAsync(prodId, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.Equal(4, ficha!.Variantes.Count);
        var codigos = ficha.Variantes.Select(v => v.CodigoInterno).ToList();
        Assert.Equal(4, codigos.Distinct().Count());
        Assert.All(codigos, c => Assert.StartsWith(prod.CodigoInterno + "-", c));
        _ = v4;
    }

    [Fact]
    public async Task Busqueda_paginada_y_ficha_correcta()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.PreciosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.CargaInicialEstablecer, true, e.SesionDuena);
        for (int i = 0; i < 5; i++)
        {
            var pid = await e.CrearProducto.EjecutarAsync(new NuevoProducto($"Prod-{i:D2}", null, "cat-otr"), e.SesionDuena);
            var vid = await e.CrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), e.SesionDuena);
            await e.FijarPrecio.EjecutarAsync(vid, 1000 + i * 100, e.SesionDuena);
            await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(vid, i + 1) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        }
        var pag1 = await e.Consultas.BuscarProductosAsync("Prod", 2, 0, e.SesionDuena);
        var pag2 = await e.Consultas.BuscarProductosAsync("Prod", 2, 2, e.SesionDuena);
        Assert.Equal(2, pag1.Count);
        Assert.Equal(2, pag2.Count);
        Assert.NotEqual(pag1[0].Id, pag2[0].Id);
        var ficha = await e.Consultas.ObtenerFichaAsync(pag1[0].Id, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.NotNull(ficha);
        Assert.Single(ficha!.Variantes);
        Assert.True(ficha.Variantes[0].PrecioMinor.HasValue);
    }

    [Fact]
    public async Task Cambio_precio_historial_conservado()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.PreciosGestionar, true, e.SesionDuena);
        var pid = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.SesionDuena);
        var vid = await e.CrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), e.SesionDuena);
        await e.FijarPrecio.EjecutarAsync(vid, 1000, e.SesionDuena);
        await e.FijarPrecio.EjecutarAsync(vid, 1500, e.SesionDuena);
        var ficha = await e.Consultas.ObtenerFichaAsync(pid, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.Equal(1500, ficha!.Variantes[0].PrecioMinor);
        // historial: contar precios en DB
        await using var s = await e.Fabrica.IniciarLecturaAsync();
        var sql = (ISesionSqlite)s;
        await using var c = sql.Conexion.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM precio WHERE variante_id=$id;";
        c.Parameters.AddWithValue("$id", vid);
        var count = Convert.ToInt64(await c.ExecuteScalarAsync());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Cantidades_iniciales_varias_variantes()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.CargaInicialEstablecer, true, e.SesionDuena);
        var pid = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod", null, "cat-otr"), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), e.SesionDuena);
        var v2 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(pid, "6", null), e.SesionDuena);
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, 5), new FilaCargaInicial(v2, 7) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        var ficha = await e.Consultas.ObtenerFichaAsync(pid, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.Equal(2, ficha!.Variantes.Count);
        Assert.Contains(ficha.Variantes, x => x.Existencias == 5);
        Assert.Contains(ficha.Variantes, x => x.Existencias == 7);
    }

    [Fact]
    public async Task Cantidad_invalida_o_repetida_rollback_total()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.CargaInicialEstablecer, true, e.SesionDuena);
        var pid = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod", null, "cat-otr"), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), e.SesionDuena);
        var v2 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(pid, "6", null), e.SesionDuena);
        var op = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<ArgumentException>(() => e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, 0), new FilaCargaInicial(v2, 5) }, op, e.SesionDuena));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(v1, 3), new FilaCargaInicial(v1, 4) }, Guid.NewGuid().ToString("N"), e.SesionDuena));
        var ficha = await e.Consultas.ObtenerFichaAsync(pid, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.All(ficha!.Variantes, v => Assert.Equal(0, v.Existencias));
    }

    [Fact]
    public async Task Segundo_intento_carga_con_historial_rechazado()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.CargaInicialEstablecer, true, e.SesionDuena);
        var pid = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod", null, "cat-otr"), e.SesionDuena);
        var vid = await e.CrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), e.SesionDuena);
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(vid, 5) }, Guid.NewGuid().ToString("N"), e.SesionDuena);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(vid, 3) }, Guid.NewGuid().ToString("N"), e.SesionDuena));
    }

    [Fact]
    public async Task Doble_confirmacion_y_reintento_mismo_operacion_id()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosInventario.CargaInicialEstablecer, true, e.SesionDuena);
        var pid = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod", null, "cat-otr"), e.SesionDuena);
        var vid = await e.CrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), e.SesionDuena);
        var op = Guid.NewGuid().ToString("N");
        await e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(vid, 5) }, op, e.SesionDuena);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Cargar.EjecutarAsync(new[] { new FilaCargaInicial(vid, 5) }, op, e.SesionDuena));
        // reintento con mismo op debe seguir rechazado (no duplica)
        var ficha = await e.Consultas.ObtenerFichaAsync(pid, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        Assert.Equal(5, ficha!.Variantes[0].Existencias);
    }

    [Fact]
    public async Task Cierre_sesion_protege_acceso()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        var sDuena = e.SesionDuena;
        await e.Cerrar.EjecutarAsync(sDuena);
        await Assert.ThrowsAsync<SesionInvalidaException>(() => e.CrearProducto.EjecutarAsync(new NuevoProducto("X", null, null), sDuena));
        await Assert.ThrowsAsync<SesionInvalidaException>(() => e.Consultas.BuscarProductosAsync("", 10, 0, sDuena));
    }

    [Fact]
    public void Selector_arg_invalido_produce_error()
    {
        Assert.Throws<InvalidOperationException>(() => LocalInfantil.Wpf.Infra.SelectorBaseDesarrollo.Resolver(new[] { "--dev-db" }));
        Assert.Throws<InvalidOperationException>(() => LocalInfantil.Wpf.Infra.SelectorBaseDesarrollo.Resolver(new[] { "--dev-db=" }));
    }

    [Fact]
    public void Selector_rechaza_antecesor_con_reparse()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var real = Path.Combine(tmp, "real");
        var link = Path.Combine(tmp, "link");
        Directory.CreateDirectory(real);
        try
        {
            // Intentar crear junction (requiere privilegios; si falla, se omite)
            var psi = new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{real}\"")
            { CreateNoWindow = true, UseShellExecute = false };
            var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(5000);
            if (!Directory.Exists(link) || (new DirectoryInfo(link).Attributes & FileAttributes.ReparsePoint) == 0)
                return; // sin privilegios, omitir
            var dev = Path.Combine(link, "dev.db");
            Assert.Throws<InvalidOperationException>(() => LocalInfantil.Wpf.Infra.SelectorBaseDesarrollo.Resolver(new[] { "--dev-db=" + dev }));
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    // ViewModel: estado y comandos
    [Fact]
    public async Task Catalogo_carga_primera_pagina_al_mostrarse_por_primera_vez()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        for (int i = 0; i < 3; i++) await e.CrearProducto.EjecutarAsync(new NuevoProducto($"ProdAuto-{i}", null, "cat-otr"), e.SesionDuena);
        var svcVm = new LocalInfantil.Wpf.Infra.ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.CatalogoViewModel(svcVm, () => sesVm.IdSesion);

        // Al inicializar, el VM debe cargar productos automáticamente
        await vm.InicializarAsyncPublic();

        Assert.True(vm.Filas.Count >= 3);
        Assert.Null(vm.Error);
        // La primera fila debe estar seleccionada automáticamente
        Assert.NotNull(vm.FilaSeleccionada);
    }

    [Fact]
    public async Task Inicializacion_dos_llamadas_rapidas_solo_una_carga()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod-1", null, "cat-otr"), e.SesionDuena);
        var svcVm = new LocalInfantil.Wpf.Infra.ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.CatalogoViewModel(svcVm, () => sesVm.IdSesion);

        // Dos llamadas concurrentes rápidas
        var t1 = vm.InicializarAsyncPublic();
        var t2 = vm.InicializarAsyncPublic();
        await Task.WhenAll(t1, t2);

        // Solo debe haber una carga (la primera), sin error
        Assert.Null(vm.Error);
        Assert.True(vm.Filas.Count >= 1);
    }

    [Fact]
    public async Task Inicializacion_fallida_permite_reintento()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod-Reintento", null, "cat-otr"), e.SesionDuena);
        var svcVm = new LocalInfantil.Wpf.Infra.ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        string? sesionActual = "sesion-inexistente";
        var vm = new LocalInfantil.Wpf.ViewModels.CatalogoViewModel(svcVm, () => sesionActual);

        // Primera carga falla (sesión inválida): queda Fallida, no Inicializada
        await vm.InicializarAsyncPublic();
        Assert.NotNull(vm.Error);

        // Al volverse válida la sesión, una llamada posterior reintenta y carga
        sesionActual = sesVm.IdSesion;
        await vm.InicializarAsyncPublic();
        Assert.Null(vm.Error);
        Assert.True(vm.Filas.Count >= 1);
        Assert.NotNull(vm.FilaSeleccionada);
    }

    [Fact]
    public async Task Inicializacion_exitosa_no_recarga_al_volver()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod-1", null, "cat-otr"), e.SesionDuena);
        var svcVm = new LocalInfantil.Wpf.Infra.ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.CatalogoViewModel(svcVm, () => sesVm.IdSesion);

        await vm.InicializarAsyncPublic();
        var primeraCarga = vm.Filas.Count;

        // Simular volver a la pantalla: llamar InicializarAsync nuevamente
        await vm.InicializarAsyncPublic();

        // No debe recargar (misma cantidad de filas)
        Assert.Equal(primeraCarga, vm.Filas.Count);
    }

    [Fact]
    public async Task ViewModel_busqueda_y_paginacion()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        for (int i = 0; i < 3; i++) await e.CrearProducto.EjecutarAsync(new NuevoProducto($"ProdVM-{i}", null, "cat-otr"), e.SesionDuena);
        var svcVm = new LocalInfantil.Wpf.Infra.ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.CatalogoViewModel(svcVm, () => sesVm.IdSesion);
        vm.TextoBusqueda = "ProdVM";
        await vm.BuscarAsyncPublic();
        Assert.True(vm.Productos.Count >= 2);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task ViewModel_precio_ARS_exacto_y_formulario_conservado_ante_fallo()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        var pid = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod", null, "cat-otr"), e.SesionDuena);
        var vid = await e.CrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), e.SesionDuena);
        var svcVm = new LocalInfantil.Wpf.Infra.ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.CatalogoViewModel(svcVm, () => sesVm.IdSesion);
        var ficha = await svcVm.ConsultasCatalogo.ObtenerFichaAsync(pid, DateTime.UtcNow.ToString("o"), sesVm.IdSesion);
        vm.GetType().GetProperty("Ficha")!.SetValue(vm, ficha);
        vm.PrecioTexto = "12,34";
        await vm.FijarPrecioAsyncPublic();
        Assert.NotNull(vm.Error);
        Assert.Contains("no tenés permiso para fijar precios", vm.Error!.ToLowerInvariant());
        Assert.Equal("12,34", vm.PrecioTexto);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.PreciosGestionar, true, e.SesionDuena);
        // nueva sesión para ver permiso revocado
        var sesVm2 = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm2 = new LocalInfantil.Wpf.ViewModels.CatalogoViewModel(svcVm, () => sesVm2.IdSesion);
        var ficha2 = await svcVm.ConsultasCatalogo.ObtenerFichaAsync(pid, DateTime.UtcNow.ToString("o"), sesVm2.IdSesion);
        vm2.GetType().GetProperty("Ficha")!.SetValue(vm2, ficha2);
        vm2.PrecioTexto = "1234,50";
        await vm2.FijarPrecioAsyncPublic();
        Assert.Null(vm2.Error);
        var ficha3 = await svcVm.ConsultasCatalogo.ObtenerFichaAsync(pid, DateTime.UtcNow.ToString("o"), sesVm2.IdSesion);
        Assert.Equal(123450, ficha3!.Variantes[0].PrecioMinor);
    }

    [Fact]
    public async Task ViewModel_ficha_actualizada_tras_guardar()
    {
        using var e = await Entorno.CrearAsync();
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        var pid = await e.CrearProducto.EjecutarAsync(new NuevoProducto("ProdFicha", null, "cat-rem"), e.SesionDuena);
        var prod = await e.Consultas.ObtenerProductoAsync(pid, e.SesionDuena);
        Assert.NotNull(prod);
        var svcVm = new LocalInfantil.Wpf.Infra.ServiciosComerciales(e.Ruta);
        var sesVm = await svcVm.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.CatalogoViewModel(svcVm, () => sesVm.IdSesion);
        vm.ProductoSeleccionado = prod;
        await vm.VerFichaAsyncPublic();
        Assert.NotNull(vm.Ficha);
        // Código interno automático: la usuaria no lo escribe.
        await vm.CrearVarianteAsyncPublic();
        Assert.NotNull(vm.Ficha);
        var unica = Assert.Single(vm.Ficha!.Variantes);
        Assert.Equal($"{prod.CodigoInterno}-01", unica.CodigoInterno);
    }

    private sealed class RelojAvance : IReloj
    {
        private DateTimeOffset _ahora;
        public RelojAvance(DateTimeOffset inicio) => _ahora = inicio;
        public DateTimeOffset UtcNow() { var r = _ahora; _ahora = _ahora.AddSeconds(1); return r; }
    }

    private sealed class Entorno : IDisposable
    {
        public readonly FabricaConexionesSqlite Fabrica;
        public readonly CrearProducto CrearProducto;
        public readonly EditarProducto EditarProducto;
        public readonly DesactivarProducto DesactivarProducto;
        public readonly CrearVariante CrearVariante;
        public readonly EditarVariante EditarVariante;
        public readonly DesactivarVariante DesactivarVariante;
        public readonly FijarPrecio FijarPrecio;
        public readonly ConsultasCatalogo Consultas;
        public readonly CargarExistenciaInicial Cargar;
        public readonly IniciarSesion Login;
        public readonly CerrarSesion Cerrar;
        public readonly ConfigurarPermiso Permisos;
        public readonly CrearUsuario Alta;
        public readonly string SesionDuena;
        public readonly string DuenaId;
        public readonly string Ruta;
        private readonly string _dir;
        public Entorno(string dir, string ruta, FabricaConexionesSqlite fabrica, CrearProducto cp, EditarProducto ep, DesactivarProducto dp, CrearVariante cv, EditarVariante ev, DesactivarVariante dv, FijarPrecio fp, ConsultasCatalogo cc, CargarExistenciaInicial ca, IniciarSesion login, CerrarSesion cerrar, ConfigurarPermiso perm, CrearUsuario alta, string sesion, string duenaId)
        { _dir = dir; Ruta = ruta; Fabrica = fabrica; CrearProducto = cp; EditarProducto = ep; DesactivarProducto = dp; CrearVariante = cv; EditarVariante = ev; DesactivarVariante = dv; FijarPrecio = fp; Consultas = cc; Cargar = ca; Login = login; Cerrar = cerrar; Permisos = perm; Alta = alta; SesionDuena = sesion; DuenaId = duenaId; }
        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "catalog.db");
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
            var prodRepo = new RepositorioProductos(ids);
            var varRepo = new RepositorioVariantes(ids);
            var precioRepo = new RepositorioPrecios(ids);
            var movRepo = new RepositorioMovimientos(ids);
            var crearProd = new CrearProducto(prodRepo, new RepositorioCategorias(ids), autorizador, fabrica, auditoria, ids, reloj);
            var editarProd = new EditarProducto(prodRepo, new RepositorioCategorias(ids), autorizador, fabrica, auditoria, ids);
            var desactivarProd = new DesactivarProducto(prodRepo, varRepo, autorizador, fabrica, auditoria, ids);
            var crearVar = new CrearVariante(varRepo, prodRepo, autorizador, fabrica, auditoria, ids, reloj);
            var editarVar = new EditarVariante(varRepo, autorizador, fabrica, auditoria, ids);
            var desactivarVar = new DesactivarVariante(varRepo, autorizador, fabrica, auditoria, ids);
            var fijarPrecio = new FijarPrecio(precioRepo, varRepo, autorizador, fabrica, auditoria, ids, reloj);
            var consultas = new ConsultasCatalogo(prodRepo, varRepo, sesiones, fabrica);
            var cargar = new CargarExistenciaInicial(movRepo, varRepo, autorizador, fabrica, auditoria, reloj);
            var duenaId = await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await login.EjecutarAsync("duena", "clave-duena-000000");
            return new Entorno(dir, ruta, fabrica, crearProd, editarProd, desactivarProd, crearVar, editarVar, desactivarVar, fijarPrecio, consultas, cargar, login, cerrar, perm, alta, sesion.IdSesion, duenaId);
        }
        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    }
}
