using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Printing;

/// <summary>La vista previa y la impresora reciben exactamente el mismo documento paginado.</summary>
public sealed class DocumentoCambioRenderer
{
    public const double AnchoA4 = 210 * 96 / 25.4;
    public const double AltoA4 = 297 * 96 / 25.4;
    public const double AnchoTicket = 80 * 96 / 25.4;

    public FixedDocument Renderizar(ComprobanteCambioPresentacion modelo, FormatoComprobanteCambio formato, byte[] bannerPng)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        var ticket = formato == FormatoComprobanteCambio.Ticket80mm;
        var ancho = ticket ? AnchoTicket : AnchoA4;
        var margen = ticket ? 12d : 36d;
        var util = ancho - 2 * margen;
        var tamano = ticket ? 11d : 12d;
        var bloques = new List<FrameworkElement>();
        var datos = modelo.Datos;

        TextBlock Texto(string valor, bool negrita = false) => new()
        {
            Text = valor, FontSize = tamano, FontFamily = new FontFamily("Segoe UI"),
            FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black
        };
        Grid Fila(string etiqueta, string importe, bool destacada = false)
        {
            var fila = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fila.Children.Add(Texto(etiqueta, destacada));
            var valor = Texto(importe, destacada);
            valor.Margin = new Thickness(ticket ? 10 : 20, 0, 0, 0);
            valor.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(valor, 1);
            fila.Children.Add(valor);
            return fila;
        }
        void Seccion(string titulo) => bloques.Add(new Border
        {
            Tag = "seccion", BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 4), Margin = new Thickness(0, 12, 0, 6), Child = Texto(titulo, true)
        });
        void AgregarLineas(string titulo, IReadOnlyList<LineaTicketCambio> lineas)
        {
            if (lineas.Count == 0) return;
            Seccion(titulo);
            foreach (var linea in lineas)
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                panel.Children.Add(Fila(linea.NombreProducto, ComprobanteCambioPresentacion.Importe(linea.SubtotalMinor), true));
                panel.Children.Add(Texto($"{linea.CodigoInterno} · {linea.Talle ?? "—"} / {linea.Color ?? "—"}"));
                panel.Children.Add(Texto($"{linea.Cantidad} × {ComprobanteCambioPresentacion.Importe(linea.PrecioUnitarioMinor)}"));
                if (!string.IsNullOrEmpty(linea.AptitudTexto)) panel.Children.Add(Texto(linea.AptitudTexto));
                if (!string.IsNullOrWhiteSpace(linea.MotivoNoApta)) panel.Children.Add(Texto($"Motivo: {linea.MotivoNoApta}"));
                bloques.Add(panel);
            }
        }

        bloques.Add(Fila("Comprobante", datos.Numero, true));
        bloques.Add(Fila("Venta original", datos.VentaNumero));
        bloques.Add(Texto($"Fecha: {modelo.FechaTexto}"));
        bloques.Add(Texto($"Responsable: {datos.Responsable}"));
        if (!string.IsNullOrWhiteSpace(datos.Autorizante)) bloques.Add(Texto($"Autorización presencial: {datos.Autorizante}"));
        AgregarLineas("Prendas que vuelven", modelo.Entradas);
        AgregarLineas("Artículos nuevos", modelo.Salidas);
        Seccion("Resumen económico");
        bloques.Add(Fila("Valor reconocido", ComprobanteCambioPresentacion.Importe(datos.ImporteEntradasMinor)));
        bloques.Add(Fila("Artículos nuevos", ComprobanteCambioPresentacion.Importe(datos.ImporteSalidasMinor)));
        bloques.Add(new Border
        {
            BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 5, 0, 5), Margin = new Thickness(0, 6, 0, 6),
            Child = Fila(modelo.ResultadoFinal, ComprobanteCambioPresentacion.Importe(Math.Abs(datos.DiferenciaMinor)), true)
        });
        if (datos.DecisionCuenta is not null)
        {
            bloques.Add(Texto(datos.DecisionCuentaTexto, true));
            bloques.Add(Fila("Aplicado a deuda", ComprobanteCambioPresentacion.Importe(datos.ImporteAplicadoDeudaMinor)));
            if (datos.DeudaAnteriorMinor is { } anterior) bloques.Add(Fila("Deuda anterior", ComprobanteCambioPresentacion.Importe(anterior)));
            if (datos.DeudaPosteriorMinor is { } posterior) bloques.Add(Fila("Deuda posterior", ComprobanteCambioPresentacion.Importe(posterior)));
        }
        if (datos.Pagos.Count > 0)
        {
            Seccion("Cobros / reintegros registrados");
            foreach (var pago in datos.Pagos)
            {
                bloques.Add(Texto(pago.MedioTexto, true));
                bloques.Add(Fila(pago.DireccionTexto, ComprobanteCambioPresentacion.Importe(pago.ImporteMinor)));
                if (pago.RecibidoMinor is { } recibido) bloques.Add(Fila(pago.EtiquetaRecibidoTexto!, ComprobanteCambioPresentacion.Importe(recibido)));
                if (pago.VueltoMinor is { } vuelto) bloques.Add(Fila("Vuelto", ComprobanteCambioPresentacion.Importe(vuelto)));
            }
        }
        if (!string.IsNullOrWhiteSpace(datos.Motivo))
        {
            Seccion("Motivo");
            bloques.Add(Texto(datos.Motivo));
        }
        bloques.Add(new TextBlock { Text = ComprobanteCambioPresentacion.Pie, FontSize = 10, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0), TextAlignment = TextAlignment.Center });

        var documento = new FixedDocument();
        StackPanel Encabezado()
        {
            var panel = new StackPanel { Width = util, Margin = new Thickness(0, 0, 0, 10) };
            if (bannerPng.Length > 0)
            {
                var imagen = new BitmapImage();
                using var flujo = new MemoryStream(bannerPng, writable: false);
                imagen.BeginInit(); imagen.CacheOption = BitmapCacheOption.OnLoad; imagen.StreamSource = flujo; imagen.EndInit(); imagen.Freeze();
                panel.Children.Add(new Image { Source = imagen, Width = ticket ? 230 : 320, Height = ticket ? 46 : 64,
                    Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
            }
            else panel.Children.Add(Texto("TiNA kids — Vistiendo sonrisas", true));
            var titulo = Texto(modelo.Titulo, true);
            titulo.TextAlignment = TextAlignment.Center;
            titulo.FontSize = ticket ? 12 : 16;
            panel.Children.Add(titulo);
            var noFiscal = Texto("NO FISCAL"); noFiscal.TextAlignment = TextAlignment.Center;
            panel.Children.Add(noFiscal);
            return panel;
        }
        double Alto(FrameworkElement bloque)
        {
            bloque.Measure(new Size(util, double.PositiveInfinity));
            return bloque.DesiredSize.Height;
        }
        var contenido = new StackPanel { Width = util };
        var encabezado = Encabezado(); contenido.Children.Add(encabezado);
        var alto = Alto(encabezado);
        void AgregarPagina()
        {
            var altura = ticket ? Math.Ceiling(alto + 2 * margen) : AltoA4;
            var pagina = new FixedPage { Width = ancho, Height = altura, Background = Brushes.White };
            FixedPage.SetLeft(contenido, margen); FixedPage.SetTop(contenido, margen);
            pagina.Children.Add(contenido);
            if (!ticket)
            {
                var numero = new TextBlock { Text = $"{datos.Numero} · {documento.Pages.Count + 1}", FontSize = 9 };
                FixedPage.SetRight(numero, margen); FixedPage.SetBottom(numero, 12);
                pagina.Children.Add(numero);
            }
            pagina.Measure(new Size(ancho, altura)); pagina.Arrange(new Rect(0, 0, ancho, altura)); pagina.UpdateLayout();
            var paginaContenido = new PageContent(); paginaContenido.Child = pagina; documento.Pages.Add(paginaContenido);
        }
        for (var i = 0; i < bloques.Count; i++)
        {
            var bloque = bloques[i];
            var altoBloque = Alto(bloque);
            var siguiente = Equals(bloque.Tag, "seccion") && i + 1 < bloques.Count ? Alto(bloques[i + 1]) : 0;
            if (!ticket && alto + altoBloque + siguiente > AltoA4 - 2 * margen - 14 && contenido.Children.Count > 1)
            {
                AgregarPagina();
                contenido = new StackPanel { Width = util };
                encabezado = Encabezado(); contenido.Children.Add(encabezado); alto = Alto(encabezado);
            }
            contenido.Children.Add(bloque); alto += altoBloque;
        }
        AgregarPagina();
        documento.DocumentPaginator.PageSize = new Size(ancho, ticket ? documento.Pages[0].Child.Height : AltoA4);
        return documento;
    }
}
