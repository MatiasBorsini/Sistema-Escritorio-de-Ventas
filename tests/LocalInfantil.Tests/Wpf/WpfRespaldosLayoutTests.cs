using System.Xml.Linq;

namespace LocalInfantil.Tests.Wpf;

public sealed class WpfRespaldosLayoutTests
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
        var ruta = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "RespaldosView.xaml");
        var xaml = File.ReadAllText(ruta);
        return (XDocument.Parse(xaml), xaml, File.ReadAllText(Path.ChangeExtension(ruta, ".xaml.cs")));
    }

    [Fact]
    public void Layout_es_de_escritorio_con_cuatro_bloques_y_sin_scroll_horizontal()
    {
        var (xml, xaml, _) = LeerVista();
        var paneles = xml.Descendants().Single(e => e.Attribute(Xaml + "Name")?.Value == "Paneles");
        var columnas = paneles.Elements().Single(e => e.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Where(e => e.Name.LocalName == "ColumnDefinition").ToArray();

        Assert.Equal(2, columnas.Length);
        Assert.All(columnas, c => Assert.EndsWith("*", c.Attribute("Width")?.Value));
        Assert.Equal(4, paneles.Elements().Count(e => e.Name.LocalName == "Border"
            && e.Attribute("Style")?.Value == "{StaticResource EstiloTarjeta}"));
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml);
        Assert.Contains("MaxHeight=\"190\"", xaml);
        Assert.Contains("MaxHeight=\"132\"", xaml);
        Assert.Contains("EstiloTituloPagina", xaml);
        Assert.Contains("EstiloAyuda", xaml);
    }

    [Fact]
    public void Estados_vacios_sustituyen_las_dos_grillas_sin_filas_ficticias()
    {
        var (_, xaml, _) = LeerVista();

        Assert.Contains("No hay respaldos para mostrar.", xaml);
        Assert.Contains("No hay copias remotas para mostrar.", xaml);
        Assert.Contains("Binding=\"{Binding Copias.Count}\" Value=\"0\"", xaml);
        Assert.Contains("Binding=\"{Binding CopiasDrive.Count}\" Value=\"0\"", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding Copias}\" AutoGenerateColumns=\"True\"", xaml);
    }

    [Fact]
    public void Acciones_y_campos_sensibles_conservan_sus_comandos_y_contexto()
    {
        var (xml, xaml, codeBehind) = LeerVista();
        var botones = xml.Descendants().Where(e => e.Name.LocalName == "Button")
            .ToDictionary(e => e.Attribute("Content")?.Value ?? string.Empty, StringComparer.Ordinal);

        Assert.Equal("{Binding CrearCopiaCommand}", botones["Crear copia ahora"].Attribute("Command")?.Value);
        Assert.Equal("{Binding ActualizarCommand}", botones["Actualizar"].Attribute("Command")?.Value);
        Assert.Equal("{Binding GenerarCodigoCommand}", botones["Generar código de recuperación"].Attribute("Command")?.Value);
        Assert.Equal("{Binding ConfirmarCodigoCommand}", botones["Confirmar y proteger en este Windows"].Attribute("Command")?.Value);
        Assert.Equal("{Binding ProtegerCodigoExistenteCommand}", botones["Proteger código existente / recuperar esta PC"].Attribute("Command")?.Value);
        Assert.Equal("{Binding ConectarDriveCommand}", botones["Conectar"].Attribute("Command")?.Value);
        Assert.Equal("{Binding ActualizarDriveCommand}", botones["Consultar copias remotas"].Attribute("Command")?.Value);
        Assert.Equal("{Binding DescargarDriveCommand}", botones["Descargar seleccionada"].Attribute("Command")?.Value);
        Assert.Equal("{Binding SeleccionarArchivoCommand}", botones["Seleccionar archivo"].Attribute("Command")?.Value);
        Assert.Equal("ImprimirHoja_Click", botones["Imprimir hoja de recuperación"].Attribute("Click")?.Value);
        Assert.Equal("Restaurar_Click", botones["Verificar y restaurar"].Attribute("Click")?.Value);
        Assert.Equal("{Binding HayCodigo}", botones["Imprimir hoja de recuperación"].Attribute("IsEnabled")?.Value);

        Assert.Contains("Text=\"{Binding Codigo, Mode=OneWay}\" IsReadOnly=\"True\"", xaml);
        Assert.Contains("Text=\"{Binding Confirmacion, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("Text=\"{Binding CodigoRestauracion, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("x:Name=\"PasswordDuena\"", xaml);
        Assert.Contains("PasswordDuena.Password", codeBehind);
        Assert.Contains("File.Exists(vm.ArchivoRestauracion)", codeBehind);
        Assert.Contains("vm.RestaurarCommand.CanExecute(null)", codeBehind);
        Assert.Contains("CopiaDriveSeleccionada", xaml);
        Assert.Contains("DriveConectado", xaml);
        Assert.Contains("PuedeRestaurar", xaml);
        Assert.Contains("PuedeConfigurar, Converter={StaticResource BoolToVisibility}", xaml);
        Assert.Contains("DataTrigger Binding=\"{Binding Configurado}\" Value=\"False\"", xaml);
        Assert.Contains("DataTrigger Binding=\"{Binding Confirmacion}\" Value=\"\"", xaml);
        Assert.Contains("x:Name=\"RestaurarButton\"", xaml);
        Assert.Contains("RestaurarButton.IsEnabled = vm.PuedeRestaurar", codeBehind);
    }

    [Fact]
    public void Fechas_locales_se_presentan_en_hora_local_y_drive_conserva_utc()
    {
        var (_, xaml, _) = LeerVista();

        Assert.Contains("Header=\"Fecha (hora local)\"", xaml);
        Assert.Contains("CreadoUtc, Converter={StaticResource FechaCorta}", xaml);
        Assert.Contains("Header=\"Fecha UTC\" Binding=\"{Binding CreadoUtc}\"", xaml);
    }

    [Fact]
    public void Fuente_de_respaldos_no_contiene_mojibake()
    {
        var (_, xaml, codeBehind) = LeerVista();
        foreach (var marcador in new[] { "Ã", "Â", "â€", "�" })
        {
            Assert.DoesNotContain(marcador, xaml);
            Assert.DoesNotContain(marcador, codeBehind);
        }
    }
}
