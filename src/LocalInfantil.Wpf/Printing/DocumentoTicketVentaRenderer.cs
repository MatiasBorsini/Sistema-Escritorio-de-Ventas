using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Printing;

/// <summary>
/// Convierte un comprobante interno de venta en documento visual, solo desde
/// snapshots persistidos (nunca del catálogo actual). Sin diálogos.
/// </summary>
public sealed class DocumentoTicketVentaRenderer
{
    private const double AnchoPagina = 816;
    private const double AltoPagina = 1056;
    private static readonly CultureInfo CulturaArgentina = CultureInfo.GetCultureInfo("es-AR");

    public FixedDocument Renderizar(DocumentoTicketVenta documento)
    {
        ArgumentNullException.ThrowIfNull(documento);

        var pagina = new FixedPage { Width = AnchoPagina, Height = AltoPagina };
        var contenido = new StackPanel { Width = AnchoPagina - 96, Margin = new Thickness(48) };

        contenido.Children.Add(new TextBlock { Text = "LocalInfantil", FontSize = 22, FontWeight = FontWeights.Bold });
        contenido.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 0),
            Text = "COMPROBANTE INTERNO — NO FISCAL",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
        });
        var fecha = DateTimeOffset.TryParse(documento.CreadaEn, out var f)
            ? f.ToLocalTime().ToString("g", CulturaArgentina)
            : documento.CreadaEn;
        contenido.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Text = $"Venta {documento.Numero} · {fecha}",
            Foreground = Brushes.DimGray,
        });
        if (!string.IsNullOrWhiteSpace(documento.VendidoPor))
        {
            contenido.Children.Add(new TextBlock
            {
                Text = $"Vendido por: {documento.VendidoPor}",
                Foreground = Brushes.DimGray,
            });
        }
        if (!string.IsNullOrWhiteSpace(documento.ClienteNombre))
        {
            contenido.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 4, 0, 0),
                Text = $"Cliente: {documento.ClienteNombre}",
                Foreground = Brushes.DimGray,
            });
        }

        if (documento.Estado == "anulada")
        {
            contenido.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                Text = $"ANULADA{(string.IsNullOrWhiteSpace(documento.MotivoAnulacion) ? string.Empty : $" — {documento.MotivoAnulacion}")}",
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Firebrick,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        contenido.Children.Add(new Border { Margin = new Thickness(0, 14, 0, 6), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1) });
        foreach (var linea in documento.Lineas)
        {
            contenido.Children.Add(CrearLinea(linea));
        }

        contenido.Children.Add(new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(0, 8, 0, 0),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new TextBlock
            {
                Text = $"Total: {Formatear(documento.TotalMinor)}",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right,
            },
        });

        if (documento.ImporteCuentaCorrienteMinor > 0)
        {
            contenido.Children.Add(new TextBlock { Margin = new Thickness(0, 4, 0, 0), Text = $"Pagado hoy: {Formatear(documento.ImportePagadoMinor)}" });
            contenido.Children.Add(new TextBlock { Text = $"A cuenta corriente: {Formatear(documento.ImporteCuentaCorrienteMinor)}", FontWeight = FontWeights.SemiBold });
            if (DateOnly.TryParse(documento.VencimientoCuentaCorriente, out var vence))
                contenido.Children.Add(new TextBlock { Text = $"Vencimiento: {vence:dd/MM/yyyy}" });
        }

        contenido.Children.Add(new TextBlock { Margin = new Thickness(0, 12, 0, 2), Text = "Pagos", FontWeight = FontWeights.Bold });
        foreach (var pago in documento.Pagos)
        {
            var detalle = $"{NombreMedio(pago.Medio)}: {Formatear(pago.ImporteMinor)}";
            if (pago.RecibidoMinor.HasValue)
            {
                detalle += $" (recibido {Formatear(pago.RecibidoMinor.Value)}, vuelto {Formatear(pago.VueltoMinor ?? 0)})";
            }

            contenido.Children.Add(new TextBlock { Text = detalle });
        }

        contenido.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 28, 0, 0),
            Text = "Comprobante interno — no fiscal. No válido como factura.",
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
        });

        pagina.Children.Add(contenido);
        var contenidoPagina = new PageContent { Child = pagina };
        var documentoVisual = new FixedDocument();
        documentoVisual.Pages.Add(contenidoPagina);
        return documentoVisual;
    }

    private static Grid CrearLinea(LineaTicketVenta linea)
    {
        var fila = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        var detalle = linea.NombreProducto;
        var variante = (linea.Talle, linea.Color) switch
        {
            (null, null) => null,
            (null, var c) => c,
            (var t, null) => t,
            (var t, var c) => $"{t} · {c}",
        };
        if (variante is not null)
            detalle += $" ({variante})";

        AgregarTexto(fila, linea.CodigoInterno, 0, TextAlignment.Left);
        AgregarTexto(fila, detalle, 1, TextAlignment.Left);
        AgregarTexto(fila, $"×{linea.Cantidad}", 2, TextAlignment.Right);
        AgregarTexto(fila, Formatear(linea.PrecioUnitarioMinor), 3, TextAlignment.Right);
        AgregarTexto(fila, Formatear(linea.SubtotalMinor), 4, TextAlignment.Right);
        return fila;
    }

    private static void AgregarTexto(Grid fila, string texto, int columna, TextAlignment alineacion)
    {
        var bloque = new TextBlock { Text = texto, TextAlignment = alineacion, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(bloque, columna);
        fila.Children.Add(bloque);
    }

    private static string Formatear(long minor)
        => ((decimal)minor / 100m).ToString("C", CulturaArgentina);

    private static string NombreMedio(string medio) => medio switch
    {
        "efectivo" => "Efectivo",
        "transferencia" => "Transferencia",
        "debito" => "Débito",
        "credito" => "Crédito",
        "billetera" => "Billetera",
        _ => medio,
    };
}
