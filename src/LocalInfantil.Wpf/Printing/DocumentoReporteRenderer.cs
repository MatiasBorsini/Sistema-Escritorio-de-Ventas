using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalInfantil.Wpf.Printing;

/// <summary>
/// Arma un documento A4 (vertical u horizontal) a partir de datos ya
/// consultados. Pagina por filas completas, repite los encabezados de tabla y
/// no calcula ni altera importes: solo presenta textos recibidos.
/// </summary>
public sealed class DocumentoReporteRenderer
{
    public const double AnchoA4 = 210 * 96 / 25.4;
    public const double AltoA4 = 297 * 96 / 25.4;

    private const double Margen = 34;
    private const double ReservaPie = 30;

    private static readonly Brush GrisTexto = Congelado(0x52, 0x63, 0x6A);
    private static readonly Brush GrisBorde = Congelado(0xD5, 0xD5, 0xD5);
    private static readonly Brush GrisEncabezado = Congelado(0xF1, 0xF1, 0xF1);
    private static readonly Brush GrisFondoResumen = Congelado(0xF7, 0xF7, 0xF6);

    private static SolidColorBrush Congelado(byte rojo, byte verde, byte azul)
    {
        var pincel = new SolidColorBrush(Color.FromRgb(rojo, verde, azul));
        pincel.Freeze();
        return pincel;
    }

    public Size TamanoPagina(OrientacionReporte orientacion) => orientacion == OrientacionReporte.Horizontal
        ? new Size(AltoA4, AnchoA4)
        : new Size(AnchoA4, AltoA4);

    public FixedDocument Renderizar(ReporteImpresion modelo, byte[] bannerPng)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        var tamano = TamanoPagina(modelo.Orientacion);
        var util = tamano.Width - 2 * Margen;
        var limite = tamano.Height - 2 * Margen - ReservaPie;

        TextBlock Texto(string valor, double tamanoFuente = 10, bool negrita = false, bool gris = false)
        {
            var bloque = new TextBlock
            {
                Text = valor,
                FontSize = tamanoFuente,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = negrita ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = gris ? GrisTexto : Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = util,
            };
            return bloque;
        }

        double Alto(FrameworkElement bloque)
        {
            bloque.Measure(new Size(util, double.PositiveInfinity));
            return bloque.DesiredSize.Height;
        }

        FrameworkElement Separador(double margenSuperior, double margenInferior) => new Border
        {
            Height = 1,
            Background = GrisBorde,
            Margin = new Thickness(0, margenSuperior, 0, margenInferior),
            Width = util,
        };

        Image? Banner(double alto)
        {
            if (bannerPng.Length == 0) return null;
            var imagen = new BitmapImage();
            using var flujo = new MemoryStream(bannerPng, writable: false);
            imagen.BeginInit();
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.StreamSource = flujo;
            imagen.EndInit();
            imagen.Freeze();
            return new Image
            {
                Source = imagen,
                Height = alto,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
        }

        FrameworkElement CabeceraCompleta()
        {
            var panel = new StackPanel { Width = util };
            var superior = new Grid { Width = util };
            superior.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            superior.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var banner = Banner(52);
            if (banner is not null) superior.Children.Add(banner);
            else superior.Children.Add(Texto("TiNA kids — Vistiendo sonrisas", 14, true));

            var derecha = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var titulo = Texto(modelo.Titulo, 18, true);
            titulo.TextAlignment = TextAlignment.Right;
            derecha.Children.Add(titulo);
            var emitido = Texto($"Emitido: {modelo.Emitido.ToLocalTime():dd/MM/yyyy HH:mm}", 9, gris: true);
            emitido.TextAlignment = TextAlignment.Right;
            derecha.Children.Add(emitido);
            Grid.SetColumn(derecha, 1);
            superior.Children.Add(derecha);
            panel.Children.Add(superior);
            panel.Children.Add(Separador(8, 6));

            var filtros = Texto(modelo.Filtros, 10);
            panel.Children.Add(filtros);
            if (!string.IsNullOrWhiteSpace(modelo.Nota))
            {
                var nota = Texto(modelo.Nota!, 9, gris: true);
                nota.FontStyle = FontStyles.Italic;
                nota.Margin = new Thickness(0, 2, 0, 0);
                panel.Children.Add(nota);
            }
            return panel;
        }

        FrameworkElement CabeceraCompacta()
        {
            var panel = new StackPanel { Width = util };
            var grid = new Grid { Width = util };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var banner = Banner(28);
            if (banner is not null) grid.Children.Add(banner);
            else grid.Children.Add(Texto("TiNA kids", 11, true));
            var derecha = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var titulo = Texto($"{modelo.Titulo} (continuación)", 11, true);
            titulo.TextAlignment = TextAlignment.Right;
            derecha.Children.Add(titulo);
            var filtros = Texto(modelo.Filtros, 8.5, gris: true);
            filtros.TextAlignment = TextAlignment.Right;
            filtros.MaxWidth = util * 0.62;
            derecha.Children.Add(filtros);
            Grid.SetColumn(derecha, 1);
            grid.Children.Add(derecha);
            panel.Children.Add(grid);
            panel.Children.Add(Separador(6, 4));
            return panel;
        }

        FrameworkElement BloqueResumen()
        {
            var columnas = Math.Min(4, Math.Max(1, modelo.Resumen.Count));
            var filas = (int)Math.Ceiling(modelo.Resumen.Count / (double)columnas);
            var grid = new Grid();
            for (var c = 0; c < columnas; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var r = 0; r < filas; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var i = 0; i < modelo.Resumen.Count; i++)
            {
                var (etiqueta, valor) = modelo.Resumen[i];
                var celda = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
                celda.Children.Add(Texto(etiqueta, 8.5, gris: true));
                celda.Children.Add(Texto(valor, 11.5, true));
                Grid.SetColumn(celda, i % columnas);
                Grid.SetRow(celda, i / columnas);
                grid.Children.Add(celda);
            }

            return new Border
            {
                Width = util,
                Background = GrisFondoResumen,
                BorderBrush = GrisBorde,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                Margin = new Thickness(0, 10, 0, 2),
                Child = grid,
            };
        }

        TextBlock TituloTabla(string texto) => new()
        {
            Text = texto,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Width = util,
            Margin = new Thickness(0, 14, 0, 5),
        };

        Border FilaTabla(IReadOnlyList<string> celdas, double[] anchos, TablaReporte tabla, bool encabezado)
        {
            var grid = new Grid { Width = util };
            foreach (var ancho in anchos)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ancho) });
            for (var i = 0; i < tabla.Columnas.Count; i++)
            {
                var columna = tabla.Columnas[i];
                var valor = i < celdas.Count ? celdas[i] : string.Empty;
                var celda = new TextBlock
                {
                    Text = valor,
                    FontSize = encabezado ? 9.5 : 10,
                    FontWeight = encabezado ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = Brushes.Black,
                    TextWrapping = columna.AlineadaDerecha ? TextWrapping.NoWrap : TextWrapping.Wrap,
                    TextTrimming = columna.AlineadaDerecha ? TextTrimming.CharacterEllipsis : TextTrimming.None,
                    TextAlignment = columna.AlineadaDerecha ? TextAlignment.Right : TextAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 3, 4, 3),
                };
                Grid.SetColumn(celda, i);
                grid.Children.Add(celda);
            }

            return new Border
            {
                Width = util,
                Background = encabezado ? GrisEncabezado : Brushes.Transparent,
                BorderBrush = GrisBorde,
                BorderThickness = new Thickness(0, 0, 0, encabezado ? 1.2 : 0.6),
                Child = grid,
            };
        }

        Border FilaSinRegistros(int columnas)
        {
            var texto = Texto("Sin registros para los filtros aplicados.", 9.5, gris: true);
            texto.Margin = new Thickness(4, 5, 4, 5);
            var grid = new Grid { Width = util };
            for (var i = 0; i < columnas; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumnSpan(texto, Math.Max(1, columnas));
            grid.Children.Add(texto);
            return new Border
            {
                Width = util,
                BorderBrush = GrisBorde,
                BorderThickness = new Thickness(0, 0, 0, 0.6),
                Child = grid,
            };
        }

        double[] Anchos(TablaReporte tabla)
        {
            var total = tabla.Columnas.Sum(c => c.Peso);
            return tabla.Columnas.Select(c => util * c.Peso / total).ToArray();
        }

        var paginas = new List<StackPanel>();
        var actual = new StackPanel { Width = util };
        paginas.Add(actual);
        var usado = 0d;

        void NuevaPagina()
        {
            actual = new StackPanel { Width = util };
            paginas.Add(actual);
            var cabecera = CabeceraCompacta();
            actual.Children.Add(cabecera);
            usado = Alto(cabecera);
        }

        var cabeceraInicial = CabeceraCompleta();
        actual.Children.Add(cabeceraInicial);
        usado += Alto(cabeceraInicial);

        if (modelo.Resumen.Count > 0)
        {
            var resumen = BloqueResumen();
            var alto = Alto(resumen);
            if (usado + alto > limite) NuevaPagina();
            actual.Children.Add(resumen);
            usado += alto;
        }

        foreach (var tabla in modelo.Tablas)
        {
            var anchos = Anchos(tabla);
            var encabezado = FilaTabla(tabla.Columnas.Select(c => c.Titulo).ToList(), anchos, tabla, true);
            var altoEncabezado = Alto(encabezado);
            var primeraFila = tabla.Filas.Count > 0 ? FilaTabla(tabla.Filas[0], anchos, tabla, false) : null;
            var altoPrimera = primeraFila is null ? 0 : Alto(primeraFila);
            var titulo = TituloTabla(tabla.Titulo);
            var altoTitulo = Alto(titulo);

            if (usado + altoTitulo + altoEncabezado + altoPrimera > limite && actual.Children.Count > 0) NuevaPagina();

            actual.Children.Add(titulo);
            usado += altoTitulo;
            actual.Children.Add(encabezado);
            usado += altoEncabezado;

            if (tabla.Filas.Count == 0)
            {
                var vacia = FilaSinRegistros(tabla.Columnas.Count);
                actual.Children.Add(vacia);
                usado += Alto(vacia);
                continue;
            }

            foreach (var fila in tabla.Filas)
            {
                var elemento = FilaTabla(fila, anchos, tabla, false);
                var altoFila = Alto(elemento);
                if (usado + altoFila > limite)
                {
                    NuevaPagina();
                    var continuacion = TituloTabla($"{tabla.Titulo} (continuación)");
                    var altoContinuacion = Alto(continuacion);
                    actual.Children.Add(continuacion);
                    usado += altoContinuacion;
                    var encabezadoNuevo = FilaTabla(tabla.Columnas.Select(c => c.Titulo).ToList(), anchos, tabla, true);
                    var altoEncabezadoNuevo = Alto(encabezadoNuevo);
                    actual.Children.Add(encabezadoNuevo);
                    usado += altoEncabezadoNuevo;
                }
                actual.Children.Add(elemento);
                usado += altoFila;
            }
        }

        var documento = new FixedDocument();
        documento.DocumentPaginator.PageSize = tamano;
        for (var i = 0; i < paginas.Count; i++)
        {
            var pagina = new FixedPage { Width = tamano.Width, Height = tamano.Height, Background = Brushes.White };
            FixedPage.SetLeft(paginas[i], Margen);
            FixedPage.SetTop(paginas[i], Margen);
            pagina.Children.Add(paginas[i]);

            var pie = new TextBlock
            {
                Text = modelo.Pie,
                FontSize = 8.5,
                Foreground = GrisTexto,
                FontFamily = new FontFamily("Segoe UI"),
            };
            FixedPage.SetLeft(pie, Margen);
            FixedPage.SetBottom(pie, 14);
            pagina.Children.Add(pie);

            var numero = new TextBlock
            {
                Text = $"Página {i + 1} de {paginas.Count}",
                FontSize = 8.5,
                Foreground = GrisTexto,
                FontFamily = new FontFamily("Segoe UI"),
            };
            FixedPage.SetRight(numero, Margen);
            FixedPage.SetBottom(numero, 14);
            pagina.Children.Add(numero);

            pagina.Measure(tamano);
            pagina.Arrange(new Rect(new Point(0, 0), tamano));
            pagina.UpdateLayout();
            documento.Pages.Add(new PageContent { Child = pagina });
        }

        return documento;
    }
}
