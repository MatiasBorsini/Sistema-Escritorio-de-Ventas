using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Printing;

/// <summary>
/// Convierte contenido de prueba en un documento visual de WPF sin abrir diálogos ni imprimir.
/// </summary>
public sealed class DocumentoImpresionDePruebaRenderer
{
    private const double AnchoPagina = 816;
    private const double AltoPagina = 1056;
    private static readonly CultureInfo CulturaArgentina = CultureInfo.GetCultureInfo("es-AR");

    public FixedDocument Renderizar(DocumentoImpresionDePrueba documento)
    {
        ArgumentNullException.ThrowIfNull(documento);

        var pagina = new FixedPage { Width = AnchoPagina, Height = AltoPagina };
        var contenido = new StackPanel { Width = AnchoPagina - 96, Margin = new Thickness(48) };

        contenido.Children.Add(new TextBlock { Text = documento.NombreLocal, FontSize = 22, FontWeight = FontWeights.Bold });
        contenido.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 20),
            Text = $"Fecha: {documento.Fecha.ToString("g", CulturaArgentina)}",
            Foreground = System.Windows.Media.Brushes.DimGray
        });
        contenido.Children.Add(CrearEncabezado());

        foreach (var linea in documento.Lineas)
        {
            contenido.Children.Add(CrearLinea(linea));
        }

        contenido.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            BorderBrush = System.Windows.Media.Brushes.Gray,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new TextBlock
            {
                Text = $"Total de ejemplo: {documento.Total.ToString("C", CulturaArgentina)}",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right
            }
        });
        contenido.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 28, 0, 0),
            Text = "Documento de prueba. No es una factura ni registra una venta.",
            FontStyle = FontStyles.Italic,
            Foreground = System.Windows.Media.Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap
        });

        pagina.Children.Add(contenido);
        var contenidoPagina = new PageContent { Child = pagina };
        var documentoVisual = new FixedDocument();
        documentoVisual.Pages.Add(contenidoPagina);
        return documentoVisual;
    }

    private static Grid CrearEncabezado()
    {
        var encabezado = CrearFila("Descripción", "Cant.", "Unitario", "Subtotal");
        encabezado.Margin = new Thickness(0, 0, 0, 6);
        foreach (var hijo in encabezado.Children.OfType<TextBlock>())
        {
            hijo.FontWeight = FontWeights.Bold;
        }

        return encabezado;
    }

    private static Grid CrearLinea(LineaDocumentoImpresionDePrueba linea)
    {
        return CrearFila(
            linea.Descripcion,
            linea.Cantidad.ToString(CulturaArgentina),
            linea.ImporteUnitario.ToString("C", CulturaArgentina),
            linea.ImporteSubtotal.ToString("C", CulturaArgentina));
    }

    private static Grid CrearFila(string descripcion, string cantidad, string unitario, string subtotal)
    {
        var fila = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        AgregarTexto(fila, descripcion, 0, TextAlignment.Left);
        AgregarTexto(fila, cantidad, 1, TextAlignment.Right);
        AgregarTexto(fila, unitario, 2, TextAlignment.Right);
        AgregarTexto(fila, subtotal, 3, TextAlignment.Right);
        return fila;
    }

    private static void AgregarTexto(Grid fila, string texto, int columna, TextAlignment alineacion)
    {
        var bloque = new TextBlock { Text = texto, TextAlignment = alineacion, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(bloque, columna);
        fila.Children.Add(bloque);
    }
}
