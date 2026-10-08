using System.Xml.Linq;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Navegación superior: mismo orden/nombres/agrupación entre roles, grupos
/// ocultos sin destinos, sin duplicados, mnemónicos sin colisión y sección
/// activa por tipo de vista. Solo Shell/navigation (sin pantallas).
/// </summary>
public sealed class NavegacionShellTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LocalInfantil.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("No se encontró la raíz del repositorio (LocalInfantil.sln).");
    }

    private static XDocument Shell(string nombre)
    {
        var ruta = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", nombre);
        return XDocument.Parse(File.ReadAllText(ruta), LoadOptions.SetLineInfo);
    }

    private static List<XElement> ItemsPrincipales(XDocument doc)
    {
        var menu = doc.Descendants(Ns + "Menu")
            .First(m => (string?)m.Attribute("Margin") == "16,0,0,0");
        return menu.Elements(Ns + "MenuItem").ToList();
    }

    private static string TextoCabecera(XElement item)
    {
        var directo = (string?)item.Attribute("Header");
        if (directo is not null)
            return directo.Replace("_", string.Empty);
        var access = item.Descendants(Ns + "AccessText").FirstOrDefault();
        var texto = (string?)access?.Attribute("Text") ?? string.Empty;
        return texto.Replace("_", string.Empty);
    }

    private static string? Mnemotecnico(XElement item)
    {
        var directo = (string?)item.Attribute("Header");
        if (directo is not null)
        {
            var i = directo.IndexOf('_');
            return i >= 0 && i + 1 < directo.Length ? directo.Substring(i + 1, 1).ToLowerInvariant() : null;
        }
        var access = item.Descendants(Ns + "AccessText").FirstOrDefault();
        var texto = (string?)access?.Attribute("Text") ?? string.Empty;
        var j = texto.IndexOf('_');
        return j >= 0 && j + 1 < texto.Length ? texto.Substring(j + 1, 1).ToLowerInvariant() : null;
    }

    [Fact]
    public void Orden_duena_y_misma_taxonomia_base()
    {
        var items = ItemsPrincipales(Shell("ShellDuenaView.xaml"));
        var nombres = items.Select(TextoCabecera).ToList();
        Assert.Equal(new[] { "Inicio", "Ventas", "Caja", "Mercadería", "Clientes", "Administración" }, nombres);
    }

    [Fact]
    public void Orden_empleada_comparte_taxonomia()
    {
        var items = ItemsPrincipales(Shell("ShellEmpleadaView.xaml"));
        var nombres = items.Select(TextoCabecera).ToList();
        Assert.Equal(new[] { "Inicio", "Ventas", "Caja", "Mercadería", "Clientes", "Administración" }, nombres);

        var duena = ItemsPrincipales(Shell("ShellDuenaView.xaml")).Select(TextoCabecera).ToList();
        Assert.Equal(duena, nombres);
    }

    [Fact]
    public void Grupos_se_ocultan_sin_destinos_y_sin_duplicados()
    {
        foreach (var archivo in new[] { "ShellDuenaView.xaml", "ShellEmpleadaView.xaml" })
        {
            var texto = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", archivo));
            Assert.Contains("HayVentas", texto);
            Assert.Contains("HayClientes", texto);
            var doc = Shell(archivo);
            var parametros = doc.Descendants(Ns + "MenuItem")
                .Select(e => (string?)e.Attribute("CommandParameter"))
                .Where(p => p is not null && p.StartsWith("{Binding ", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(parametros.Count, parametros.Distinct().Count());
        }
    }

    [Fact]
    public void Visibilidad_por_permisos_con_mecanismos_vigentes()
    {
        foreach (var archivo in new[] { "ShellDuenaView.xaml", "ShellEmpleadaView.xaml" })
        {
            var texto = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", archivo));
            foreach (var prop in new[] { "Clientes", "CuentaCorriente", "Cambios", "Respaldos", "Reportes" })
                Assert.Contains($"{{Binding {prop}, Converter={{StaticResource NullToVisibility}}}}", texto);
            Assert.Contains("Caja.PuedeVer", texto);
        }
        var duena = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "ShellDuenaView.xaml"));
        Assert.Contains("{Binding ConfigMercadoPago, Converter={StaticResource NullToVisibility}}", duena);
    }

    [Theory]
    [InlineData("ShellDuenaView.xaml", 6)]
    [InlineData("ShellEmpleadaView.xaml", 6)]
    public void Mnemotecnicos_primer_nivel_sin_colision(string archivo, int esperados)
    {
        var teclas = ItemsPrincipales(Shell(archivo))
            .Select(Mnemotecnico)
            .Where(t => t is not null)
            .ToList();
        Assert.Equal(esperados, teclas.Count);
        Assert.Equal(teclas.Count, teclas.Distinct().Count());
    }

    [Fact]
    public void Menu_usuario_unico_con_identidad_y_cierre()
    {
        foreach (var (archivo, rol) in new[] { ("ShellDuenaView.xaml", "Dueña"), ("ShellEmpleadaView.xaml", "Empleada") })
        {
            var texto = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", archivo));
            Assert.DoesNotContain("_Perfil", texto);
            Assert.Contains($"Header=\"{rol}\"", texto);
            Assert.Contains("Cerrar sesión", texto);
        }
    }

    [Theory]
    [InlineData(typeof(PortadaViewModel), "Inicio")]
    [InlineData(typeof(PosViewModel), "Ventas")]
    [InlineData(typeof(VentasConsultaViewModel), "Ventas")]
    [InlineData(typeof(CambiosViewModel), "Ventas")]
    [InlineData(typeof(CajaViewModel), "Caja")]
    [InlineData(typeof(CatalogoViewModel), "Mercaderia")]
    [InlineData(typeof(ProveedoresViewModel), "Mercaderia")]
    [InlineData(typeof(RecepcionViewModel), "Mercaderia")]
    [InlineData(typeof(RecepcionesConsultaViewModel), "Mercaderia")]
    [InlineData(typeof(ConteoViewModel), "Mercaderia")]
    [InlineData(typeof(ApartadoViewModel), "Mercaderia")]
    [InlineData(typeof(ClientesViewModel), "Clientes")]
    [InlineData(typeof(CuentaCorrienteViewModel), "Clientes")]
    [InlineData(typeof(AdministracionUsuariosViewModel), "Administracion")]
    [InlineData(typeof(RespaldosViewModel), "Administracion")]
    [InlineData(typeof(ReportesViewModel), "Administracion")]
    [InlineData(typeof(ConfiguracionMercadoPagoViewModel), "Administracion")]
    [InlineData(typeof(ActualizacionesViewModel), "Administracion")]
    public void Seccion_activa_por_tipo(Type tipo, string seccion)
    {
        Assert.Equal(seccion, NavegacionSecciones.ResolverSeccionPorTipo(tipo));
    }

    [Fact]
    public void Seccion_activa_nula_y_desconocida_es_vacia()
    {
        Assert.Equal(string.Empty, NavegacionSecciones.ResolverSeccionPorTipo(null));
        Assert.Equal(string.Empty, NavegacionSecciones.ResolverSeccionPorTipo(typeof(string)));
        Assert.Equal(string.Empty, NavegacionSecciones.ResolverSeccion(null));
    }

    [Fact]
    public void Estilo_menu_principal_existe_con_tokens()
    {
        var texto = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Recursos", "DisenoRecursos.xaml"));
        var inicio = texto.IndexOf("EstiloItemMenuPrincipal", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "Falta EstiloItemMenuPrincipal en DisenoRecursos.xaml.");
        var fin = texto.IndexOf("</Style>", inicio, StringComparison.Ordinal);
        var bloque = texto.Substring(inicio, fin - inicio);
        foreach (var token in new[] { "Color.CanvasBrush", "Color.PrimaryDarkBrush", "EstiloFocoTeclado" })
            Assert.Contains(token, bloque);
        Assert.DoesNotContain("#", bloque);
    }
}
