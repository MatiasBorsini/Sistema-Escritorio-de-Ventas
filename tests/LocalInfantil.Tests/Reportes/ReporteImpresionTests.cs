using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.Printing;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Reportes;

/// <summary>
/// Impresión A4 de reportes: paginado con encabezados repetidos, orientación,
/// banner y preparación del reporte visible sin escribir datos comerciales.
/// </summary>
public sealed class ReporteImpresionTests
{
    [Fact]
    public void Renderiza_A4_vertical_con_banner_resumen_y_pie_de_pagina()
    {
        EjecutarEnSta(() =>
        {
            var modelo = new ReporteImpresion
            {
                Titulo = "Reporte de prueba",
                Filtros = "Período: 01/10/2026 al 05/10/2026 · Personalizado",
                Nota = "Nota de prueba",
                Resumen = [("Ventas brutas", "ARS 1.000,00"), ("Ventas netas", "ARS 900,00")],
                Tablas =
                [
                    new TablaReporte("Detalle",
                        [new ColumnaReporte("Referencia", 1), new ColumnaReporte("Importe", 1, true)],
                        [
                            ["T-000001", "ARS 1.000,00"],
                            ["T-000002", "ARS 900,00"],
                        ]),
                ],
            };

            var documento = new DocumentoReporteRenderer().Renderizar(modelo, LogoNegocio.ObtenerBannerPng());

            Assert.Equal(1, documento.Pages.Count);
            var pagina = documento.DocumentPaginator.PageSize;
            Assert.Equal(DocumentoReporteRenderer.AnchoA4, pagina.Width, 1);
            Assert.Equal(DocumentoReporteRenderer.AltoA4, pagina.Height, 1);
            Assert.True(pagina.Height > pagina.Width);

            var textos = Textos(documento.Pages[0].Child);
            Assert.Contains(textos, t => t.Contains("Reporte de prueba"));
            Assert.Contains(textos, t => t.Contains("Ventas brutas"));
            Assert.Contains(textos, t => t.Contains("T-000001"));
            Assert.Contains(textos, t => t.Contains("Página 1 de 1"));
            Assert.Contains(textos, t => t.Contains("Documento no fiscal"));
            Assert.NotEmpty(Imagenes(documento.Pages[0].Child));
        });
    }

    [Fact]
    public void Pagina_en_horizontal_repite_encabezados_y_no_pierde_filas()
    {
        EjecutarEnSta(() =>
        {
            var filas = Enumerable.Range(1, 120)
                .Select(i => (IReadOnlyList<string>)new[] { $"Fila {i:000}", $"ARS {i},00" })
                .ToList();
            var modelo = new ReporteImpresion
            {
                Titulo = "Reporte largo",
                Filtros = "Período: 01/01/2026 al 31/12/2026 · Personalizado",
                Orientacion = OrientacionReporte.Horizontal,
                Resumen = [("Ventas brutas", "ARS 7.260,00"), ("Ventas netas", "ARS 7.260,00")],
                Tablas = [new TablaReporte("Detalle", [new("Referencia", 1), new("Importe", 1, true)], filas)],
            };

            var documento = new DocumentoReporteRenderer().Renderizar(modelo, LogoNegocio.ObtenerBannerPng());

            Assert.True(documento.Pages.Count > 1);
            var pagina = documento.DocumentPaginator.PageSize;
            Assert.Equal(DocumentoReporteRenderer.AltoA4, pagina.Width, 1);
            Assert.Equal(DocumentoReporteRenderer.AnchoA4, pagina.Height, 1);
            Assert.True(pagina.Width > pagina.Height);

            var todas = new List<string>();
            for (var i = 0; i < documento.Pages.Count; i++)
            {
                var textos = Textos(documento.Pages[i].Child);
                todas.AddRange(textos);
                Assert.Contains(textos, t => t == "Referencia");
                Assert.Contains(textos, t => t == "Importe");
                Assert.Contains(textos, t => t.Contains($"Página {i + 1} de {documento.Pages.Count}"));
                if (i > 0) Assert.Contains(textos, t => t.Contains("(continuación)"));
            }

            Assert.Equal(120, todas.Count(t => t.StartsWith("Fila ", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public async Task Paginacion_de_impresion_falla_con_aviso_al_superar_el_tope()
    {
        var llamadas = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PaginacionReporte.TodasLasPaginasAsync((_, _) =>
            {
                llamadas++;
                return Task.FromResult<IReadOnlyList<string>>(
                    Enumerable.Repeat("fila", PaginacionReporte.TamanoPagina + 1).ToList());
            }));

        Assert.Equal(PaginacionReporte.MaximoPaginas, llamadas);
        Assert.Contains(PaginacionReporte.MaximoPaginas.ToString(), error.Message);
        Assert.Contains("No se envió nada a la impresora", error.Message);
    }

    [Fact]
    public async Task Paginacion_de_impresion_completa_justo_en_el_tope()
    {
        var llamadas = 0;
        var filas = await PaginacionReporte.TodasLasPaginasAsync((_, _) =>
        {
            llamadas++;
            var cantidad = llamadas < PaginacionReporte.MaximoPaginas
                ? PaginacionReporte.TamanoPagina + 1
                : PaginacionReporte.TamanoPagina;
            return Task.FromResult<IReadOnlyList<string>>(
                Enumerable.Repeat("fila", cantidad).ToList());
        });

        Assert.Equal(PaginacionReporte.MaximoPaginas, llamadas);
        Assert.Equal(PaginacionReporte.MaximoRegistros, filas.Count);
    }

    [Fact]
    public async Task Preparar_reporte_usa_lo_aplicado_y_no_escribe_datos()
    {
        using var entorno = await EntornoImpresion.CrearAsync();
        var vm = new ReportesViewModel(entorno.Servicios, () => entorno.Sesion, true, true, true);

        Assert.False(vm.PuedeImprimir);
        Assert.Null(await vm.PrepararReporteActualAsync());
        Assert.Contains("Consultar", vm.TextoVacioVentas);

        await vm.CargarVentasAsyncPublic();
        Assert.Null(vm.Error);
        Assert.True(vm.VentasConsultadas);
        Assert.True(vm.PuedeImprimir);

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var periodo = RangoFechas.PorDias(hoy, hoy);
        var detalleAntes = await entorno.Servicios.ConsultasReportes.DetalleVentasAsync(
            periodo.DesdeUtc, periodo.HastaUtc, 100, 0, entorno.Sesion);
        var stockAntes = await entorno.Servicios.ConsultasReportes.StockActualAsync(
            "", null, false, null, 100, 0, entorno.Sesion);

        var reporte = await vm.PrepararReporteActualAsync();

        Assert.NotNull(reporte);
        Assert.Equal("Reporte de ventas", reporte.Titulo);
        Assert.Equal(OrientacionReporte.Horizontal, reporte.Orientacion);
        Assert.Equal(8, reporte.Resumen.Count);
        Assert.Equal(3, reporte.Tablas.Count);
        var detalle = reporte.Tablas.Single(t => t.Titulo == "Detalle de ventas");
        Assert.Contains(detalle.Columnas, c => c.Titulo == "Tipo");
        Assert.Contains(detalle.Filas, f => f[1] == entorno.Referencia && f[2] == "Venta");

        var detalleDespues = await entorno.Servicios.ConsultasReportes.DetalleVentasAsync(
            periodo.DesdeUtc, periodo.HastaUtc, 100, 0, entorno.Sesion);
        var stockDespues = await entorno.Servicios.ConsultasReportes.StockActualAsync(
            "", null, false, null, 100, 0, entorno.Sesion);
        Assert.Equal(detalleAntes.Count, detalleDespues.Count);
        Assert.Equal(stockAntes.Select(f => f.Fisico), stockDespues.Select(f => f.Fisico));

        var ayer = DateTime.Today.AddDays(-1);
        vm.Desde = ayer;
        vm.Hasta = ayer;
        var reporteConFiltrosSinAplicar = await vm.PrepararReporteActualAsync();

        Assert.NotNull(reporteConFiltrosSinAplicar);
        Assert.Contains(DateTime.Today.ToString("dd/MM/yyyy"), reporteConFiltrosSinAplicar.Filtros);
        Assert.DoesNotContain(ayer.ToString("dd/MM/yyyy"), reporteConFiltrosSinAplicar.Filtros);
        Assert.Contains(reporteConFiltrosSinAplicar.Tablas.Single(t => t.Titulo == "Detalle de ventas").Filas,
            f => f[1] == entorno.Referencia);
    }

    [Fact]
    public async Task Ranking_impreso_A4_incluye_anuladas_y_la_formula_neta()
    {
        using var entorno = await EntornoImpresion.CrearAsync();
        await entorno.AnularVentaAsync();
        var vm = new ReportesViewModel(entorno.Servicios, () => entorno.Sesion, true, true, true)
        {
            TabActiva = "Inventario",
            SubAreaInventario = "Ranking",
        };

        await vm.CargarInventarioAsyncPublic();
        var reporte = await vm.PrepararReporteActualAsync();

        Assert.NotNull(reporte);
        Assert.Equal(
            ["Producto", "Código", "Talle/Color", "Brutas", "Anuladas", "Devueltas", "Salidas", "Netas"],
            reporte.Tablas.Single().Columnas.Select(c => c.Titulo));
        Assert.Equal(["Remera impresión", "REM-001-01", "M", "2", "2", "0", "0", "0"],
            reporte.Tablas.Single().Filas.Single());
        Assert.Contains("brutas − anuladas − devueltas + salidas por cambio", reporte.Nota);

        EjecutarEnSta(() =>
        {
            var documento = new DocumentoReporteRenderer().Renderizar(reporte, LogoNegocio.ObtenerBannerPng());
            Assert.Single(documento.Pages);
            var textos = Textos(documento.Pages[0].Child);
            Assert.Contains("Anuladas", textos);
            Assert.Contains("Netas", textos);
        });
    }

    private static List<string> Textos(DependencyObject raiz)
    {
        var textos = new List<string>();
        Recorrer(raiz);
        return textos;

        void Recorrer(DependencyObject actual)
        {
            if (actual is TextBlock bloque) textos.Add(bloque.Text);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(actual); i++)
                Recorrer(VisualTreeHelper.GetChild(actual, i));
        }
    }

    private static List<Image> Imagenes(DependencyObject raiz)
    {
        var imagenes = new List<Image>();
        Recorrer(raiz);
        return imagenes;

        void Recorrer(DependencyObject actual)
        {
            if (actual is Image imagen) imagenes.Add(imagen);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(actual); i++)
                Recorrer(VisualTreeHelper.GetChild(actual, i));
        }
    }

    private static void EjecutarEnSta(Action cuerpo)
    {
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try { cuerpo(); }
            catch (Exception ex) { error = ex; }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        if (!hilo.Join(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("La prueba WPF no terminó en 2 minutos.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Base temporal con permiso de reportes y una venta del día.</summary>
    private sealed class EntornoImpresion : IDisposable
    {
        private readonly string _directorio;

        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public string Referencia { get; } = "T-000001";
        private readonly string _ventaId;

        private EntornoImpresion(string directorio, ServiciosComerciales servicios, string sesion, string ventaId)
        {
            _directorio = directorio;
            Servicios = servicios;
            Sesion = sesion;
            _ventaId = ventaId;
        }

        public Task AnularVentaAsync() => Servicios.CasoAnularVenta.EjecutarAsync(
            _ventaId, "Prueba de impresión", $"op-impresion-anular-{Guid.NewGuid():N}", Sesion);

        public static async Task<EntornoImpresion> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "reportes-impresion.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(
                new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            foreach (var permiso in new[]
            {
                PermisosCatalogo.ProductosGestionar,
                PermisosCatalogo.VariantesGestionar,
                PermisosCatalogo.PreciosGestionar,
                PermisosInventario.CargaInicialEstablecer,
                PermisosVentas.VentasConfirmar,
                PermisosVentas.VentasAnular,
                PermisosCaja.Abrir,
                PermisosReportes.Ventas,
                PermisosReportes.Caja,
                PermisosReportes.Inventario,
            })
            {
                await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, permiso, true, sesion.IdSesion);
            }

            await servicios.CasoAbrirCaja.EjecutarAsync(0, $"op-impresion-{Guid.NewGuid():N}", sesion.IdSesion);
            var producto = await servicios.CasoCrearProducto.EjecutarAsync(
                new NuevoProducto("Remera impresión", null, "cat-rem"), sesion.IdSesion);
            var variante = await servicios.CasoCrearVariante.EjecutarAsync(
                new NuevaVariante(producto, "M", null), sesion.IdSesion);
            await servicios.CasoFijarPrecio.EjecutarAsync(variante, 10000, sesion.IdSesion);
            await servicios.CasoCargarExistenciaInicial.EjecutarAsync(
                [new FilaCargaInicial(variante, 10)], Guid.NewGuid().ToString("N"), sesion.IdSesion);
            var venta = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await servicios.CasoConfirmarVenta.EjecutarAsync(
                [new NuevaLineaVenta(variante, 2, 10000)],
                [new NuevoPago(MediosPago.Efectivo, 20000, 20000)],
                $"op-impresion-venta-{Guid.NewGuid():N}",
                sesion.IdSesion));

            return new EntornoImpresion(dir, servicios, sesion.IdSesion, venta.VentaId);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directorio)) Directory.Delete(_directorio, true);
        }
    }
}
