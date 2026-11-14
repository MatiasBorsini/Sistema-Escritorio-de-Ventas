using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Markup;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Printing;

/// <summary>Compone una FixedDocument vectorial A4. No conoce impresoras ni envía trabajos.</summary>
public sealed class DocumentoEtiquetasRenderer
{
    private const double AnchoPaginaMm = 210, AltoPaginaMm = 297;
    private readonly IGeneradorCodigoBarras _barcode;
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");
    public DocumentoEtiquetasRenderer(IGeneradorCodigoBarras barcode) => _barcode = barcode;

    public FixedDocument Renderizar(IReadOnlyList<SeleccionEtiqueta> seleccionadas, EtiquetasOpciones opciones)
    {
        ArgumentNullException.ThrowIfNull(seleccionadas);
        DistribucionEtiquetas.Validar(opciones);
        var repetidas = new List<VarianteParaEtiqueta>();
        foreach (var fila in seleccionadas)
        {
            if (fila.Cantidad < 0) throw new InvalidOperationException("La cantidad de etiquetas no puede ser negativa.");
            if (fila.Cantidad == 0) continue;
            for (var i = 0; i < fila.Cantidad; i++) repetidas.Add(fila.Variante);
        }
        if (repetidas.Count == 0) throw new InvalidOperationException("Indicá al menos una etiqueta.");

        var layout = DistribucionEtiquetas.Calcular(repetidas.Count, AnchoPaginaMm, AltoPaginaMm, opciones);
        var matrices = repetidas.Select(e => e.CodigoInterno).Distinct(StringComparer.Ordinal)
            .ToDictionary(codigo => codigo, codigo => _barcode.GenerarCodigoBarras(codigo), StringComparer.Ordinal);
        var doc = new FixedDocument();
        var anchoPagina = Mm(AnchoPaginaMm); var altoPagina = Mm(AltoPaginaMm);
        for (var paginaIndex = 0; paginaIndex < layout.Paginas; paginaIndex++)
        {
            var pagina = new FixedPage { Width = anchoPagina, Height = altoPagina, Background = Brushes.White };
            var primera = paginaIndex * layout.EtiquetasPorPagina;
            var ultima = Math.Min(primera + layout.EtiquetasPorPagina, repetidas.Count);
            for (var indice = primera; indice < ultima; indice++)
            {
                var celda = indice - primera;
                var col = celda % layout.Columnas; var fila = celda / layout.Columnas;
                var left = Mm(opciones.MargenHorizontalMm + col * (opciones.AnchoMm + opciones.SeparacionHorizontalMm));
                var top = Mm(opciones.MargenVerticalMm + fila * (opciones.AltoMm + opciones.SeparacionVerticalMm));
                var dato = repetidas[indice];
                var etiqueta = CrearEtiqueta(dato, matrices[dato.CodigoInterno], opciones, Mm(opciones.AnchoMm), Mm(opciones.AltoMm));
                FixedPage.SetLeft(etiqueta, left); FixedPage.SetTop(etiqueta, top); pagina.Children.Add(etiqueta);
            }
            var contenido = new PageContent(); ((IAddChild)contenido).AddChild(pagina); doc.Pages.Add(contenido);
        }
        return doc;
    }

    private Border CrearEtiqueta(VarianteParaEtiqueta dato, MatrizCodigoBarras matrix, EtiquetasOpciones opciones, double ancho, double alto)
    {
        var panel = new StackPanel { Margin = new Thickness(3) };
        panel.Children.Add(new TextBlock { Text = dato.NombreProducto, FontSize = 8, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 11 });
        var detalle = (dato.Talle, dato.Color) switch { (null, null) => null, (null, var c) => c, (var t, null) => t, (var t, var c) => $"{t} · {c}" };
        if (!string.IsNullOrWhiteSpace(detalle)) panel.Children.Add(new TextBlock { Text = detalle, FontSize = 7, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 10 });

        var barcodeCanvas = new Canvas { Width = Math.Max(1, ancho - 8), Height = Math.Max(8, alto * 0.42), ClipToBounds = true, Background = Brushes.White };
        var scale = barcodeCanvas.Width / matrix.Ancho;
        var yTop = Mm(2);
        for (var y = 0; y < matrix.Alto; y++)
        {
            var x = 0;
            while (x < matrix.Ancho)
            {
                if (!matrix.EstaNegro(x, y)) { x++; continue; }
                var inicio = x++;
                while (x < matrix.Ancho && matrix.EstaNegro(x, y)) x++;
                var rect = new System.Windows.Shapes.Rectangle { Width = (x - inicio) * scale, Height = Math.Max(1, barcodeCanvas.Height - Mm(3)), Fill = Brushes.Black, SnapsToDevicePixels = true };
                Canvas.SetLeft(rect, inicio * scale); Canvas.SetTop(rect, yTop); barcodeCanvas.Children.Add(rect);
            }
        }
        panel.Children.Add(barcodeCanvas);
        panel.Children.Add(new TextBlock { Text = dato.CodigoInterno, FontSize = 7, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Center });
        if (opciones.MostrarPrecio && dato.PrecioMinor.HasValue)
            panel.Children.Add(new TextBlock { Text = ((decimal)dato.PrecioMinor.Value / 100m).ToString("C", EsAr), FontSize = 8, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });

        return new Border { Width = ancho, Height = alto, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0.35), Child = panel, ClipToBounds = true };
    }

    private static double Mm(double mm) => mm * 96d / 25.4d;
}
