using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Regresión del crash de Caja (XamlParseException por
/// {StaticResource EstiloComboBox} inexistente): valida estáticamente que
/// todo StaticResource usado en XAML resuelva a una clave definida (global
/// en App/Recursos o local del propio archivo) y que cada vista sea XML
/// bien formado. Sin esto, una referencia rota solo explota en runtime.
/// </summary>
public sealed class VistasRecursosTests
{
    private static readonly Regex UsoRecurso = new(@"\{StaticResource\s+([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);
    private static readonly Regex ClaveDefinida = new(@"x:Key=""([A-Za-z0-9_.]+)""", RegexOptions.Compiled);

    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LocalInfantil.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("No se encontró la raíz del repositorio (LocalInfantil.sln) desde el directorio de pruebas.");
    }

    private static HashSet<string> ClavesGlobales(string wpf)
    {
        var claves = new HashSet<string>(StringComparer.Ordinal);
        var archivos = new[] { Path.Combine(wpf, "App.xaml") }
            .Concat(Directory.GetFiles(Path.Combine(wpf, "Recursos"), "*.xaml"));
        foreach (var archivo in archivos)
        {
            var texto = File.ReadAllText(archivo);
            XDocument.Parse(texto, LoadOptions.SetLineInfo);
            foreach (Match m in ClaveDefinida.Matches(texto))
                claves.Add(m.Groups[1].Value);
        }
        return claves;
    }

    [Fact]
    public void CajaView_no_referencia_EstiloComboBox_obsoleto()
    {
        var wpf = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf");
        var texto = File.ReadAllText(Path.Combine(wpf, "Views", "CajaView.xaml"));
        Assert.DoesNotContain("{StaticResource EstiloComboBox}", texto);
        Assert.Contains("{StaticResource EstiloCombo}", texto);
    }

    private static readonly HashSet<string> AtributosTwoWay = new(StringComparer.Ordinal)
    {
        "TextBox.Text", "Run.Text",
        "ComboBox.SelectedItem", "ComboBox.SelectedValue", "ComboBox.SelectedIndex", "ComboBox.Text",
        "CheckBox.IsChecked", "RadioButton.IsChecked", "ToggleButton.IsChecked",
        "DatePicker.SelectedDate", "DatePicker.Text", "Slider.Value",
        "DataGrid.SelectedItem", "ListBox.SelectedItem", "ListView.SelectedItem",
    };

    private static string QuitarBloques(string texto, string apertura, string cierre)
    {
        var sb = new System.Text.StringBuilder();
        var lineas = texto.Split('\n');
        var profundidad = 0;
        foreach (var linea in lineas)
        {
            if (linea.Contains(apertura, StringComparison.Ordinal)) profundidad++;
            if (profundidad == 0) sb.AppendLine(linea);
            if (profundidad > 0 && linea.Contains(cierre, StringComparison.Ordinal)) profundidad--;
        }
        return sb.ToString();
    }

    private static Dictionary<string, string> MapaVistaVm(string wpf)
    {
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal);
        var app = File.ReadAllText(Path.Combine(wpf, "App.xaml"));
        foreach (Match m in Regex.Matches(app,
            @"<DataTemplate\s+DataType=""\{x:Type\s+viewModels:(\w+)\}""\s*>\s*\r?\n?\s*<views:(\w+)",
            RegexOptions.Compiled))
        {
            mapa[m.Groups[2].Value] = m.Groups[1].Value;
        }
        return mapa;
    }

    /// <summary>
    /// Chequeo por reflexión: todo binding con modo TwoWay por defecto (sin
    /// Mode explícito) sobre el DataContext raíz debe apuntar a una propiedad
    /// con setter público. Las plantillas/columnas de grilla se excluyen aquí
    /// (contexto de ítem) y se verifican aparte.
    /// </summary>
    [Fact]
    public void Bindings_TwoWay_raiz_apuntan_a_propiedades_con_setter()
    {
        var wpf = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf");
        var mapa = MapaVistaVm(wpf);
        var ensamblado = typeof(LocalInfantil.Wpf.ViewModels.CajaViewModel).Assembly;
        var vistas = new[]
        {
            "CajaView", "PosView", "CambiosView", "ClientesView", "CuentaCorrienteView",
            "ReportesView", "RespaldosView", "ActualizacionesView",
            "ConfiguracionMercadoPagoView", "CatalogoView", "RecepcionView",
        };
        var problemas = new List<string>();
        foreach (var vista in vistas)
        {
            if (!mapa.TryGetValue(vista, out var vmNombre))
            {
                var convencional = vista.EndsWith("View", StringComparison.Ordinal)
                    ? vista[..^4] + "ViewModel"
                    : vista + "ViewModel";
                vmNombre = convencional;
            }
            var tipoVm = ensamblado.GetType("LocalInfantil.Wpf.ViewModels." + vmNombre);
            if (tipoVm is null)
            {
                problemas.Add($"{vista}: sin ViewModel resoluble ({vmNombre})");
                continue;
            }
            var texto = File.ReadAllText(Path.Combine(wpf, "Views", vista + ".xaml"));
            var scan = QuitarBloques(texto, "<DataTemplate", "</DataTemplate>");
            scan = QuitarBloques(scan, "<DataGrid.Columns>", "</DataGrid.Columns>");
            scan = QuitarBloques(scan, "<ItemsControl.ItemTemplate>", "</ItemsControl.ItemTemplate>");
            scan = QuitarBloques(scan, "<ComboBox.ItemTemplate>", "</ComboBox.ItemTemplate>");
            scan = QuitarBloques(scan, "<ListBox.ItemTemplate>", "</ListBox.ItemTemplate>");
            scan = QuitarBloques(scan, "<ContentControl.ContentTemplate>", "</ContentControl.ContentTemplate>");
            foreach (Match em in Regex.Matches(scan, @"<(\w+)\b[^>]*>"))
            {
                var tag = em.Groups[1].Value;
                foreach (Match b in Regex.Matches(em.Value, @"(\w+)=""\{Binding\s+([^},]+)([^""]*)"""))
                {
                    var clave = tag + "." + b.Groups[1].Value;
                    if (!AtributosTwoWay.Contains(clave)) continue;
                    if (b.Groups[3].Value.Contains("Mode", StringComparison.Ordinal)) continue;
                    if (b.Groups[3].Value.Contains("RelativeSource", StringComparison.Ordinal)
                        || b.Groups[3].Value.Contains("ElementName", StringComparison.Ordinal)) continue;
                    var prop = b.Groups[2].Value.Trim();
                    if (prop.Contains('.')) { problemas.Add($"{vista}: TwoWay con ruta anidada sin revisar: {clave} -> {prop}"); continue; }
                    var pi = tipoVm.GetProperty(prop);
                    if (pi is null) problemas.Add($"{vista}: propiedad inexistente en {vmNombre}: {clave} -> {prop}");
                    else if (pi.GetSetMethod() is null) problemas.Add($"{vista}: propiedad read-only con binding TwoWay: {clave} -> {prop}");
                }
            }

            foreach (Match g in Regex.Matches(texto, @"<DataGrid\b[^>]*>"))
            {
                if (g.Value.Contains("IsReadOnly=\"True\"")) continue;
                var mItems = Regex.Match(g.Value, @"ItemsSource=""\{Binding\s+(\w+)");
                if (!mItems.Success) continue;
                var piCol = tipoVm.GetProperty(mItems.Groups[1].Value);
                var itemTipo = piCol?.PropertyType.IsGenericType == true
                    ? piCol.PropertyType.GetGenericArguments().FirstOrDefault()
                    : null;
                if (itemTipo is null) { problemas.Add($"{vista}: grilla editable sin tipo de ítem resoluble ({mItems.Groups[1].Value})"); continue; }
                var idx = texto.IndexOf(g.Value, StringComparison.Ordinal);
                var finCols = texto.IndexOf("</DataGrid.Columns>", idx, StringComparison.Ordinal);
                if (finCols < 0) continue;
                var cols = texto.Substring(idx, finCols - idx);
                foreach (Match c in Regex.Matches(cols, @"Binding=""\{Binding\s+([\w\.]+)([^""]*)"""))
                {
                    var previo = cols.Substring(0, c.Index);
                    var ultTag = previo.LastIndexOf('<');
                    var finTag = cols.IndexOf('>', c.Index);
                    var tagColumna = ultTag >= 0 && finTag > ultTag
                        ? cols.Substring(ultTag, finTag - ultTag)
                        : string.Empty;
                    if (tagColumna.Contains("IsReadOnly=\"True\"")) continue;
                    var propCol = c.Groups[1].Value;
                    if (propCol.Contains('.')) continue;
                    var pi = itemTipo.GetProperty(propCol);
                    if (pi is null) problemas.Add($"{vista}: columna a propiedad inexistente en {itemTipo.Name}: {propCol}");
                    else if (pi.GetSetMethod() is null) problemas.Add($"{vista}: columna editable a propiedad read-only en {itemTipo.Name}: {propCol}");
                }
            }
        }
        Assert.True(problemas.Count == 0, "Bindings inválidos:\n" + string.Join("\n", problemas));
    }

    [Fact]
    public void Todos_los_StaticResource_resuelven_a_claves_definidas()
    {
        var wpf = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf");
        var globales = ClavesGlobales(wpf);
        var vistas = Directory.GetFiles(Path.Combine(wpf, "Views"), "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(wpf, "*.xaml"))
            .Distinct()
            .OrderBy(f => f)
            .ToList();
        Assert.NotEmpty(vistas);

        var rotas = new List<string>();
        foreach (var vista in vistas)
        {
            var texto = File.ReadAllText(vista);
            XDocument.Parse(texto, LoadOptions.SetLineInfo);
            var locales = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in ClaveDefinida.Matches(texto))
                locales.Add(m.Groups[1].Value);
            foreach (Match m in UsoRecurso.Matches(texto))
            {
                var clave = m.Groups[1].Value;
                if (!globales.Contains(clave) && !locales.Contains(clave))
                    rotas.Add($"{Path.GetFileName(vista)}: {clave}");
            }
        }

        Assert.True(rotas.Count == 0,
            "StaticResource sin clave definida (rompería en runtime con XamlParseException):\n" + string.Join("\n", rotas));
    }
}
