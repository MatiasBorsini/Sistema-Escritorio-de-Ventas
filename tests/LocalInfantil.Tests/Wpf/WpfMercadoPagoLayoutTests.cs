using System.Xml.Linq;

namespace LocalInfantil.Tests.Wpf;

public sealed class WpfMercadoPagoLayoutTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LocalInfantil.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("No se encontró la raíz de LocalInfantil.sln.");
    }

    private static (XDocument Xml, string Xaml, string CodeBehind) LeerVista()
    {
        var ruta = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "ConfiguracionMercadoPagoView.xaml");
        var xaml = File.ReadAllText(ruta);
        return (XDocument.Parse(xaml), xaml, File.ReadAllText(Path.ChangeExtension(ruta, ".xaml.cs")));
    }

    [Fact]
    public void Layout_tiene_dos_columnas_escritorio_y_scroll_solo_vertical()
    {
        var (xml, xaml, _) = LeerVista();
        var raiz = xml.Root!;
        var grid = raiz.Descendants().Single(e => e.Name.LocalName == "Grid"
            && e.Elements().Any(c => c.Name.LocalName == "Grid.ColumnDefinitions"
                && c.Elements().Any(d => d.Attribute("Width")?.Value == "3.8*")));
        var columnas = grid.Elements().Single(e => e.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Where(e => e.Name.LocalName == "ColumnDefinition").ToArray();

        Assert.Equal(2, columnas.Length);
        Assert.Equal("3.8*", columnas[0].Attribute("Width")?.Value);
        Assert.Equal("6.2*", columnas[1].Attribute("Width")?.Value);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml);
        Assert.Contains("EstiloTituloPagina", xaml);
        Assert.Contains("EstiloAyuda", xaml);
        Assert.DoesNotContain("#", xaml);
    }

    [Fact]
    public void Secciones_y_estado_conservan_datos_reales_del_view_model()
    {
        var (xml, xaml, _) = LeerVista();
        var nombres = xml.Descendants().Select(e => e.Attribute(Xaml + "Name")?.Value)
            .Where(n => n is not null).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("BloqueEstado", nombres);
        Assert.Contains("BloqueCredencial", nombres);
        Assert.Contains("BloqueTerminal", nombres);
        Assert.Contains("BloqueAcciones", nombres);
        Assert.Contains("{Binding EstadoTexto}", xaml);
        Assert.Contains("{Binding Conectado}", xaml);
        Assert.Contains("{Binding TerminalTexto}", xaml);
        Assert.Contains("{Binding TerminalDetalle}", xaml);
        Assert.Contains("{Binding ModoTexto}", xaml);
        Assert.Contains("{Binding SucursalTexto}", xaml);
        Assert.Contains("{Binding UltimaComprobacionTexto}", xaml);
    }

    [Fact]
    public void Acciones_credencial_y_terminal_mantienen_bindings_y_proteccion()
    {
        var (xml, xaml, codeBehind) = LeerVista();
        var botones = xml.Descendants().Where(e => e.Name.LocalName == "Button")
            .ToDictionary(e => e.Attribute("Content")?.Value ?? string.Empty, StringComparer.Ordinal);

        Assert.Equal("{Binding ConectarCommand}", botones["Conectar / cambiar"].Attribute("Command")?.Value);
        Assert.Equal("{Binding BuscarTerminalesCommand}", botones["Buscar terminales"].Attribute("Command")?.Value);
        Assert.Equal("{Binding SeleccionarCommand}", botones["Seleccionar"].Attribute("Command")?.Value);
        Assert.Equal("{Binding ActivarModoCommand}", botones["Activar modo integrado"].Attribute("Command")?.Value);
        Assert.Equal("{Binding PuedeActivarModo}", botones["Activar modo integrado"].Attribute("IsEnabled")?.Value);
        Assert.Equal("{Binding ProbarCommand}", botones["Probar conexión"].Attribute("Command")?.Value);
        Assert.Equal("{Binding DesconectarCommand}", botones["Desconectar"].Attribute("Command")?.Value);
        Assert.Contains("PasswordBox x:Name=\"CajaCredencial\"", xaml);
        Assert.Contains("vm.CredencialTexto = CajaCredencial.Password", codeBehind);
        Assert.Contains("no se muestra en claro", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("protege en este equipo", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Visibility=\"{Binding Mensaje, Converter={StaticResource NullToVisibility}}\"", xaml);
        Assert.Contains("Visibility=\"{Binding Error, Converter={StaticResource NullToVisibility}}\"", xaml);
    }
}
