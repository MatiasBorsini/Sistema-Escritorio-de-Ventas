using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.Printing;

namespace LocalInfantil.Tests.Wpf;

public sealed class ComprobantesCambioTests
{
    [Theory]
    [InlineData(10000, true, "Cambio", "A cobrar")]
    [InlineData(-10000, false, "Devolución", "A reintegrar")]
    [InlineData(-10000, true, "Cambio", "A reintegrar")]
    [InlineData(0, true, "Sin diferencia", "Sin diferencia")]
    public void Tipo_y_resultado_provienen_de_los_snapshots(long diferencia, bool tieneSalidas, string tipo, string resultado)
    {
        var modelo = new ComprobanteCambioPresentacion(Datos(diferencia, tieneSalidas));
        Assert.Equal(tipo, modelo.Tipo);
        Assert.Equal(resultado, modelo.ResultadoFinal);
        Assert.Contains(tipo.ToUpperInvariant(), modelo.Titulo);
    }

    [Theory]
    [InlineData(FormatoComprobanteCambio.A4)]
    [InlineData(FormatoComprobanteCambio.Ticket80mm)]
    public void Ambos_formatos_conservan_aptitud_importes_efectivo_y_banner(FormatoComprobanteCambio formato)
    {
        EnSta(() =>
        {
            var modelo = new ComprobanteCambioPresentacion(Datos(-10000, false));
            var documento = new DocumentoCambioRenderer().Renderizar(modelo, formato, LogoNegocio.ObtenerBannerPng());
            var pagina = Assert.Single(documento.Pages).Child;
            Assert.Equal(formato == FormatoComprobanteCambio.A4 ? DocumentoCambioRenderer.AnchoA4 : DocumentoCambioRenderer.AnchoTicket, pagina.Width, 3);
            var textos = Descendientes(pagina).OfType<TextBlock>().Select(x => x.Text).ToArray();
            Assert.Contains("COMPROBANTE INTERNO DE DEVOLUCIÓN", textos);
            Assert.Contains("Apta para volver al inventario disponible", textos);
            Assert.Contains("Efectivo entregado", textos);
            Assert.DoesNotContain("Efectivo recibido", textos);
            Assert.DoesNotContain("Vuelto", textos);
            Assert.Contains("ARS 100,00", textos);
            Assert.Contains("NO FISCAL", textos);
            var imagen = Assert.Single(Descendientes(pagina).OfType<Image>());
            Assert.Equal(Stretch.Uniform, imagen.Stretch);
            Assert.True(imagen.Width > imagen.Height * 4);
            foreach (var fila in Descendientes(pagina).OfType<Grid>().Where(x => x.ColumnDefinitions.Count == 2))
            {
                var valor = Assert.Single(fila.Children.OfType<TextBlock>(), x => Grid.GetColumn(x) == 1);
                Assert.Equal(HorizontalAlignment.Right, valor.HorizontalAlignment);
                Assert.True(valor.TranslatePoint(new Point(0, 0), fila).X >= fila.Children[0].RenderSize.Width);
            }
            if (formato == FormatoComprobanteCambio.Ticket80mm) Assert.True(pagina.Height < DocumentoCambioRenderer.AltoA4);
        });
    }

    [Fact]
    public void A4_pagina_todas_las_prendas_y_repite_identidad_sin_cortar_filas()
    {
        EnSta(() =>
        {
            var datos = Datos(-10000, false);
            datos = datos with { Lineas = Enumerable.Range(0, 60).Select(i => datos.Lineas[0] with { CodigoInterno = $"SKU-{i:000}" }).ToArray() };
            var documento = new DocumentoCambioRenderer().Renderizar(new(datos), FormatoComprobanteCambio.A4, LogoNegocio.ObtenerBannerPng());
            Assert.True(documento.Pages.Count > 1);
            var textos = documento.Pages.SelectMany(p => Descendientes(p.Child).OfType<TextBlock>()).ToArray();
            Assert.Equal(60, textos.Count(t => t.Text.StartsWith("SKU-")));
            foreach (var pagina in documento.Pages)
            {
                Assert.Equal(DocumentoCambioRenderer.AltoA4, pagina.Child.Height, 3);
                Assert.Single(Descendientes(pagina.Child).OfType<Image>());
                foreach (var bloque in pagina.Child.Children.OfType<StackPanel>().Single().Children.OfType<FrameworkElement>())
                    Assert.True(bloque.TranslatePoint(new Point(0, bloque.ActualHeight), pagina.Child).Y < pagina.Child.Height - 20);
            }
        });
    }

    [Fact]
    public void Credito_aplicado_a_deuda_no_se_presenta_como_efectivo_reintegrado()
    {
        var datos = Datos(-10000, false) with { DecisionCuenta = "aplicar_a_deuda", ImporteAplicadoDeudaMinor = 10000, Pagos = [] };
        var modelo = new ComprobanteCambioPresentacion(datos);
        Assert.Contains("deuda", modelo.ResultadoFinal);
        var resumen = new ResumenCambio("id", datos.Numero, datos.VentaNumero, -10000, datos.Fecha, "duena", false, 10000);
        Assert.Equal("Devolución", resumen.TipoTexto);
        Assert.Equal("Aplicado a deuda", resumen.ResultadoTexto);
        Assert.Equal(10000, resumen.ImporteResultadoMinor);
    }

    [Fact]
    public void Efectivo_cobrado_conserva_recibido_y_vuelto()
    {
        EnSta(() =>
        {
            var datos = Datos(10000, true) with { Pagos = [new("cobro", MediosPago.Efectivo, 10000, 15000)] };
            var documento = new DocumentoCambioRenderer().Renderizar(new(datos), FormatoComprobanteCambio.Ticket80mm, LogoNegocio.ObtenerBannerPng());
            var textos = Descendientes(documento.Pages[0].Child).OfType<TextBlock>().Select(x => x.Text).ToArray();
            Assert.Contains("Efectivo recibido", textos);
            Assert.Contains("Vuelto", textos);
            Assert.Contains("ARS 50,00", textos);
            Assert.DoesNotContain("Efectivo entregado", textos);
        });
    }

    internal static DocumentoTicketCambio Datos(long diferencia, bool salida)
    {
        var reconocido = salida ? 20000L : 10000L;
        var nuevos = salida ? reconocido + diferencia : 0;
        var entrada = new LineaTicketCambio("entrada", "PAN-01", "Pantalón jean", "4", "Jean", 1, reconocido, reconocido, true, null);
        return new("C-000001", "T-000009", "2026-10-21T12:15:00Z", "duena", null, "Cambio de talle",
            reconocido, nuevos, diferencia,
            salida ? [entrada, new("salida", "PAN-02", "Pantalón nuevo", "6", "Jean", 1, nuevos, nuevos, true, null)] : [entrada],
            diferencia < 0 ? [new("reintegro", MediosPago.Efectivo, -diferencia, -diferencia)] : []);
    }

    private static IEnumerable<DependencyObject> Descendientes(DependencyObject raiz)
    {
        yield return raiz;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
            foreach (var hijo in Descendientes(VisualTreeHelper.GetChild(raiz, i))) yield return hijo;
    }

    private static void EnSta(Action accion)
    {
        Exception? error = null;
        var hilo = new Thread(() => { try { accion(); } catch (Exception ex) { error = ex; } });
        hilo.SetApartmentState(ApartmentState.STA); hilo.Start();
        if (!hilo.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("La renderización no terminó en 30 segundos.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
