using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// E3 Recepciones: resumen batch en una lectura, filtros, peek, detalle con
/// diálogos fake y verificación de filas de recepción.
/// </summary>
public sealed class WpfRecepcionesResumenTests
{
    [Fact]
    public async Task Resumen_batch_con_agregados_y_total_solo_si_hay_costos()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov Resumen");
        var (v1, v2, _, _) = await e.CrearVariantesConStockAsync("RSM", 5, 7);
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 5, 1000, "ARS"), new LineaRecepcion(v2, 7, null, null) }, "COMP-R1");
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 2, 2000, "ARS") }, null);

        var lista = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 20, 0, e.Sesion);
        Assert.Equal(2, lista.Count);
        var conComp = lista.First(r => r.ComprobanteRef == "COMP-R1");
        Assert.Equal("Prov Resumen", conComp.ProveedorNombre);
        Assert.Equal(2, conComp.CantidadLineas);
        Assert.Equal(12, conComp.Unidades);
        Assert.Equal(1, conComp.LineasSinCosto);
        Assert.False(conComp.Revertida);
        Assert.Equal("duena", conComp.CreadoPorNombre);
        var sinComp = lista.First(r => r.ComprobanteRef is null);
        Assert.Equal(0, sinComp.LineasSinCosto);
        Assert.Equal(4000, sinComp.TotalMinor);
    }

    [Fact]
    public async Task Filtro_revertidas_y_peek_exacto()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov Filtro");
        var (v1, _, _, _) = await e.CrearVariantesConStockAsync("FLT", 10, 10);
        var id1 = await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 1, null, null) }, "C-1");
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 1, null, null) }, "C-2");
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 1, null, null) }, "C-3");
        await e.Servicios.CasoRevertirRecepcion.EjecutarAsync(id1, "motivo", Guid.NewGuid().ToString("N"), e.Sesion);

        var rev = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Revertidas, 20, 0, e.Sesion);
        Assert.Single(rev);
        Assert.True(rev[0].Revertida);
        var conf = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Confirmadas, 20, 0, e.Sesion);
        Assert.Equal(2, conf.Count);

        var pag1 = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 2, 0, e.Sesion);
        Assert.Equal(3, pag1.Count); // 2 + peek
        var pag2 = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 2, 2, e.Sesion);
        Assert.Single(pag2);
    }

    [Fact]
    public async Task Lineas_detalle_resuelven_sku_en_una_lectura()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov Detalle");
        var (v1, v2, cod1, cod2) = await e.CrearVariantesConStockAsync("DET", 3, 4);
        var id = await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 3, 500, "ARS"), new LineaRecepcion(v2, 4, null, null) }, "C-D");
        var lineas = await e.Servicios.ConsultasRecepciones.ListarLineasDetalleAsync(id, e.Sesion);
        Assert.Equal(2, lineas.Count);
        Assert.Equal(new[] { cod1, cod2 }.OrderBy(c => c), lineas.Select(l => l.Sku).OrderBy(c => c));
        Assert.Equal(1500, lineas.First(l => l.Sku == cod1).SubtotalMinor);
        Assert.Null(lineas.First(l => l.Sku == cod2).SubtotalMinor);
    }

    [Fact]
    public async Task Consulta_vm_historial_detalle_y_dialogo()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov VM");
        var (v1, _, _, _) = await e.CrearVariantesConStockAsync("CVM", 6, 6);
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 6, 700, "ARS") }, "C-VM");
        var vm = new RecepcionesConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.CargarAsync();
        Assert.Single(vm.Historial);
        var fila = vm.Historial[0];
        Assert.Equal("C-VM", fila.Referencia);
        Assert.Equal("Prov VM", fila.Resumen.ProveedorNombre);
        Assert.Equal("Confirmada", fila.EstadoTexto);

        vm.FilaSeleccionada = fila;
        await EsperarAsync(() => vm.LineasDetalle.Count == 1);
        Assert.Equal(6, vm.UnidadesDetalle);
        Assert.Equal(4200, vm.TotalDetalleExacto);
        Assert.Equal("duena", vm.ResponsableDetalle);

        await vm.AbrirDetalleAsyncPublic();
        Assert.Equal(fila.Resumen.Id, e.Dialogos.UltimoId);

        vm.FilaSeleccionada = null;
        await vm.AbrirDetalleAsyncPublic();
        Assert.Contains("Seleccioná", vm.Error ?? "");
    }

    [Fact]
    public async Task Entrada_verificar_expone_stock_quedara_y_resumen()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov Entrada");
        var (v1, _, cod1, _) = await e.CrearVariantesConStockAsync("ENT", 10, 10);
        var rec = new RecepcionViewModel(e.Servicios, () => e.Sesion);
        await rec.CargarProveedoresAsync();
        rec.ProveedorSeleccionado = rec.Proveedores.First(p => p.Id == prov);
        rec.Filas[0].Sku = cod1;
        rec.Filas[0].CantidadTexto = "4";
        rec.Filas[0].CostoTexto = "100,00";
        await rec.VerificarAsyncPublic();
        var fila = rec.Filas[0];
        Assert.True(fila.Resuelta);
        Assert.True(fila.Valida);
        Assert.Equal(10, fila.StockActual);
        Assert.Equal(14, fila.Quedara);
        Assert.Equal(40000, fila.SubtotalMinor);
        Assert.Equal(1, rec.CantidadLineas);
        Assert.Equal(4, rec.UnidadesSumar);
        Assert.Equal(40000, rec.TotalEstimado);
        Assert.Contains("sumar 4 unidades", rec.ConfirmarTexto);
    }

    [Fact]
    public void Referencia_usa_comprobante_y_nunca_id_tecnico()
    {
        var conComp = new ResumenRecepcion("id-largo-0123456789", "p", "Prov", "op", "COMP-9", null, null, "2026-09-24T00:00:00Z", 1, 1, 0, 100, false);
        Assert.Equal("COMP-9", FilaHistorialRecepcion.ReferenciaDe(conComp));
        var sinComp = conComp with { ComprobanteRef = null };
        Assert.Equal("Sin referencia", FilaHistorialRecepcion.ReferenciaDe(sinComp));
        Assert.DoesNotContain("id-largo", FilaHistorialRecepcion.ReferenciaDe(sinComp));
        Assert.Contains("24/09", FilaHistorialRecepcion.FechaCorta("2026-09-24T10:30:00Z"));
    }

    private static async Task EsperarAsync(Func<bool> listo)
    {
        for (var t = 0; t < 100 && !listo(); t++) await Task.Delay(50);
    }

    private sealed class DialogosFake : IDialogosRecepciones
    {
        public string? UltimoId { get; private set; }
        public void MostrarDetalleRecepcion(ResumenRecepcion resumen) => UltimoId = resumen.Id;
    }

    private sealed class Entorno : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public string DuenaId { get; }
        public DialogosFake Dialogos { get; } = new();
        private readonly string _dir;

        private Entorno(string dir, ServiciosComerciales servicios, string sesion, string duenaId)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
            DuenaId = duenaId;
        }

        public Task<string> CrearProveedorAsync(string nombre)
            => Servicios.CasoCrearProveedor.EjecutarAsync(new NuevoProveedor(nombre), Sesion);

        public async Task<(string V1, string V2, string Cod1, string Cod2)> CrearVariantesConStockAsync(string prefijo, long s1, long s2)
        {
            var pid = await Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto($"Prod-{prefijo}", null, "cat-otr"), Sesion);
            var v1 = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, "M", null), Sesion);
            var v2 = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), Sesion);
            await Servicios.CasoCargarExistenciaInicial.EjecutarAsync(
                new[] { new FilaCargaInicial(v1, s1), new FilaCargaInicial(v2, s2) }, Guid.NewGuid().ToString("N"), Sesion);
            var cod1 = (await Servicios.ConsultasCatalogo.ObtenerVarianteAsync(v1, Sesion))!.CodigoInterno;
            var cod2 = (await Servicios.ConsultasCatalogo.ObtenerVarianteAsync(v2, Sesion))!.CodigoInterno;
            return (v1, v2, cod1, cod2);
        }

        public async Task<string> ConfirmarAsync(string prov, IReadOnlyList<LineaRecepcion> lineas, string? comprobante)
            => await Servicios.CasoConfirmarRecepcion.EjecutarAsync(prov, lineas, Guid.NewGuid().ToString("N"), comprobante, Sesion);

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "recep.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            var e = new Entorno(dir, servicios, sesion.IdSesion, duenaId);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCompras.ProveedoresGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCompras.RecepcionConfirmar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCompras.RecepcionRevertir, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.ProductosGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.VariantesGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosInventario.CargaInicialEstablecer, true, e.Sesion);
            return e;
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
