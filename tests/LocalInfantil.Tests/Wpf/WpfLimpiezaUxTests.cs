using System.Text;
using System.Xml.Linq;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Regresiones de encoding, navegación redundante y botones de cierre
/// secundarios cuya única función era duplicar la X de Windows.
/// </summary>
public sealed class WpfLimpiezaUxTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LocalInfantil.sln"))) return dir.FullName;
        throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static string RutaWpf(params string[] partes)
        => Path.Combine(new[] { RaizRepo(), "src", "LocalInfantil.Wpf" }.Concat(partes).ToArray());

    private static XDocument LeerXaml(string archivo)
        => XDocument.Load(RutaWpf("Views", archivo));

    [Fact]
    public void Fuentes_WPF_son_UTF8_y_no_contienen_patrones_de_mojibake()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var marcadores = new[] { "\u00C3", "\u00C2", "\uFFFD", "\u00E2\u20AC" };
        var fuentes = Directory.EnumerateFiles(RutaWpf(), "*.*", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p) is ".xaml" or ".cs" or ".resx")
            .ToArray();
        Assert.NotEmpty(fuentes);

        var hallazgos = new List<string>();
        foreach (var archivo in fuentes)
        {
            var lineas = utf8.GetString(File.ReadAllBytes(archivo)).Split('\n');
            for (var i = 0; i < lineas.Length; i++)
            foreach (var marcador in marcadores)
                if (lineas[i].Contains(marcador, StringComparison.Ordinal))
                    hallazgos.Add($"{Path.GetRelativePath(RaizRepo(), archivo)}:{i + 1}: {lineas[i].Trim()}");
        }

        Assert.True(hallazgos.Count == 0, "Se encontraron literales o fuentes con encoding sospechoso:\n" + string.Join("\n", hallazgos));
    }

    [Fact]
    public void Inventario_muestra_terminologia_y_estados_vacios_correctos()
    {
        var texto = File.ReadAllText(RutaWpf("Views", "ApartadoView.xaml"), Encoding.UTF8);
        foreach (var literal in new[]
        {
            "Consultá una variante y gestioná su disponibilidad.", "Físico", "Disponible", "Apartado",
            "Físico = Disponible + Apartado.", "Elegí la acción. Solo aparece el formulario necesario.",
            "Últimos movimientos de la variante", "Todavía no hay movimientos para esta variante.",
            "FallbackValue=—"
        })
            Assert.Contains(literal, texto);
    }

    [Theory]
    [InlineData("ShellDuenaView.xaml")]
    [InlineData("ShellEmpleadaView.xaml")]
    public void Navegacion_no_duplica_No_vendible_y_conserva_Inventario_y_Conteo(string archivo)
    {
        var doc = XDocument.Load(RutaWpf("Views", archivo));
        var items = doc.Descendants(Ns + "MenuItem").ToArray();
        Assert.DoesNotContain(items, e => (string?)e.Attribute("Header") == "No vendible");
        Assert.Contains(items, e => (string?)e.Attribute("Header") == "Consulta de inventario"
            && ((string?)e.Attribute("Command"))?.Contains("IrAApartadoCommand", StringComparison.Ordinal) == true);
        Assert.Contains(items, e => (string?)e.Attribute("Header") == "Conteo físico");
    }

    [Fact]
    public void Solo_queda_Cerrar_en_Ficha_como_confirmacion_semantica()
    {
        var vistas = Directory.EnumerateFiles(RutaWpf(), "*.xaml", SearchOption.AllDirectories)
            .Select(path => (Path: path, Doc: XDocument.Load(path)))
            .Where(x => x.Doc.Root?.Name == Ns + "Window")
            .SelectMany(x => x.Doc.Descendants(Ns + "Button")
                .Where(b => (string?)b.Attribute("Content") == "Cerrar")
                .Select(b => (Archivo: Path.GetFileName(x.Path), Boton: b)))
            .ToArray();

        var cierre = Assert.Single(vistas);
        Assert.Equal("FichaProductoWindow.xaml", cierre.Archivo);
        Assert.Equal("{Binding ConfirmarCierreCommand}", (string?)cierre.Boton.Attribute("Command"));
        Assert.Contains("ConfirmacionCierreVisible", File.ReadAllText(RutaWpf("Views", cierre.Archivo), Encoding.UTF8));
        Assert.Contains("Seguir completando", File.ReadAllText(RutaWpf("Views", cierre.Archivo), Encoding.UTF8));
    }

    [Theory]
    [InlineData("MatrizVariantesWindow.xaml")]
    [InlineData("TicketAbonoWindow.xaml")]
    [InlineData("VistaPreviaEtiquetasWindow.xaml")]
    public void Ventanas_sin_boton_Cerrar_conservan_cierre_por_escape(string archivo)
    {
        var doc = LeerXaml(archivo);
        Assert.DoesNotContain(doc.Descendants(Ns + "Button"), b => (string?)b.Attribute("Content") == "Cerrar");
        Assert.Equal("Window_PreviewKeyDown", (string?)doc.Root?.Attribute("PreviewKeyDown"));
        var codeBehind = Path.ChangeExtension(RutaWpf("Views", archivo), ".xaml.cs");
        var codigo = File.ReadAllText(codeBehind, Encoding.UTF8);
        Assert.Contains("Key.Escape", codigo);
        Assert.Contains("Close()", codigo);
    }

    [Theory]
    [InlineData("DetalleRecepcionWindow.xaml")]
    [InlineData("FichaProductoWindow.xaml")]
    public void Ventanas_con_X_decorativa_usan_X_nativa_y_conservan_escape_o_confirmacion(string archivo)
    {
        var doc = LeerXaml(archivo);
        Assert.DoesNotContain(doc.Descendants(Ns + "Button"), b => (string?)b.Attribute("Content") == "✕");
        var codigo = File.ReadAllText(Path.ChangeExtension(RutaWpf("Views", archivo), ".xaml.cs"), Encoding.UTF8);
        Assert.Contains("Key.Escape", codigo);
        if (archivo == "FichaProductoWindow.xaml") Assert.Contains("AlIntentarCerrar", codigo);
    }
}
