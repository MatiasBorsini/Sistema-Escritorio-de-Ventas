using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;
using LocalInfantil.Wpf.Views;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Smoke tests WPF reales (hilo STA): cargan los ResourceDictionary de la
/// aplicaciÃ³n, instancian vistas, asignan ViewModel y fuerzan layout para
/// que los BindingExpression se activen. Detectan XamlParseException,
/// StaticResource inexistentes y bindings TwoWay contra propiedades
/// read-only (crash real de Caja con IngresosTexto). Requiere sesiÃ³n
/// grÃ¡fica de Windows; no hace screenshots ni prueba estÃ©tica.
/// </summary>
public sealed class VistasSmokeTests
{
    private static readonly object Puerta = new();
    private static bool _appLista;

    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LocalInfantil.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("No se encontrÃ³ la raÃ­z del repositorio (LocalInfantil.sln).");
    }

    private static void EjecutarEnSta(Action cuerpo)
    {
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try
            {
                cuerpo();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        if (!hilo.Join(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("El smoke test WPF no terminÃ³ en 2 minutos.");
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void AsegurarAplicacion()
    {
        lock (Puerta)
        {
            if (System.Windows.Application.Current is not null)
            {
                _appLista = true;
            }
            if (_appLista)
                return;
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var wpf = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf");
            using (var lector = XmlReader.Create(Path.Combine(wpf, "Recursos", "DisenoRecursos.xaml")))
            {
                if (XamlReader.Load(lector) is ResourceDictionary diseno)
                    app.Resources.MergedDictionaries.Add(diseno);
            }
            var appXaml = File.ReadAllText(Path.Combine(wpf, "App.xaml"));
            var ensamblado = typeof(ServiciosComerciales).Assembly;
            foreach (Match m in Regex.Matches(appXaml, @"<infra:(\w+)[^>]*x:Key=""(\w+)"""))
            {
                var tipo = ensamblado.GetType("LocalInfantil.Wpf.Infra." + m.Groups[1].Value);
                Assert.True(tipo is not null, $"Converter {m.Groups[1].Value} de App.xaml no existe en el ensamblado.");
                app.Resources.Add(m.Groups[2].Value, Activator.CreateInstance(tipo!)!);
            }
            _appLista = true;
        }
    }

    private static void Medir(UIElement vista)
    {
        if (vista is FrameworkElement fe)
        {
            fe.Width = double.NaN;
            fe.Height = double.NaN;
        }
        vista.Measure(new Size(1200, 800));
        vista.Arrange(new Rect(new Point(0, 0), vista.DesiredSize));
        vista.UpdateLayout();
    }

    private sealed class EntornoCaja : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        private readonly string _dir;

        private EntornoCaja(string dir, ServiciosComerciales servicios, string sesion)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
        }

        public static async Task<EntornoCaja> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "caja-smoke.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            foreach (var permiso in new[]
            {
                PermisosCaja.Abrir, PermisosCaja.Consultar, PermisosCaja.Movimiento,
                PermisosCaja.Cerrar, PermisosCaja.RetiroPropietario, PermisosCaja.CorregirMedio,
            })
            {
                await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, permiso, true, sesion.IdSesion);
            }
            await servicios.CasoAbrirCaja.EjecutarAsync(0, $"op-apertura-smoke-{Guid.NewGuid():N}", sesion.IdSesion);
            return new EntornoCaja(dir, servicios, sesion.IdSesion);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task CajaView_carga_enlaza_y_mide_sin_excepciones()
    {
        using var e = await EntornoCaja.CrearAsync();
        var vm = new CajaViewModel(e.Servicios, () => e.Sesion);
        await vm.CargarAsyncPublic();
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new CajaView { DataContext = vm };
            Medir(vista);
            Assert.Same(vm, vista.DataContext);

            var textos = new List<string>();
            void Recorrer(DependencyObject d)
            {
                var hijos = LogicalTreeHelper.GetChildren(d);
                foreach (var h in hijos)
                {
                    if (h is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
                        textos.Add(tb.Text);
                    if (h is DependencyObject dd)
                        Recorrer(dd);
                }
            }
            Recorrer(vista);
            Assert.Contains(textos, t => t.Contains("ARS", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task EtiquetasWindow_carga_y_enlaza_sin_excepciones_ni_efectos()
    {
        using var e = await EntornoCaja.CrearAsync();
        var vm = new EtiquetasViewModel(e.Servicios, () => e.Sesion, "producto-inexistente");
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new EtiquetasWindow(vm);
            Medir(vista);
            Assert.Same(vm, vista.DataContext);
        });
    }

    [Fact]
    public async Task MatrizVariantesWindow_carga_y_enlaza_sin_excepciones_ni_efectos()
    {
        using var e = await EntornoCaja.CrearAsync();
        var vm = new MatrizVariantesViewModel(e.Servicios, () => e.Sesion, "producto-inexistente");
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new MatrizVariantesWindow(vm);
            Medir(vista);
            Assert.Same(vm, vista.DataContext);
        });
    }

    public static IEnumerable<object[]> VistasPrincipales()
    {
        var ensamblado = typeof(CajaView).Assembly;
        var tipos = new[]
        {
            "PosView", "CambiosView", "ClientesView", "CuentaCorrienteView",
            "ReportesView", "RespaldosView", "ActualizacionesView",
            "ConfiguracionMercadoPagoView", "CatalogoView", "RecepcionView",
            "VentasConsultaView", "ApartadoView", "ConteoView", "ProveedoresView",
            "RecepcionesConsultaView", "InicioView", "PortadaView",
            "AdministracionUsuariosView", "LoginView", "BootstrapDuenaView",
            "EstadoPrototipoView", "ArqueoCajaView", "BuscadorCliente",
            "ShellDuenaView", "ShellEmpleadaView", "AppRootView",
            // DiÃ¡logos con constructor con argumentos (FichaProductoWindow,
            // MatrizVariantesWindow, DetalleRecepcionWindow, EtiquetasWindow,
            // Ticket*Window): no instanciables sin datos; cubiertos por el
            // lint estÃ¡tico de VistasRecursosTests (parse XAML + StaticResource).
        };
        foreach (var nombre in tipos)
        {
            var tipo = ensamblado.GetType("LocalInfantil.Wpf.Views." + nombre);
            Assert.True(tipo is not null, $"Vista {nombre} no encontrada en el ensamblado WPF.");
            yield return new object[] { tipo! };
        }
    }

    [Theory]
    [MemberData(nameof(VistasPrincipales))]
    public void Vista_carga_recursos_y_mide_sin_excepciones(Type tipoVista)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = (FrameworkElement)Activator.CreateInstance(tipoVista)!;
            Medir(vista);
        });
    }

    [Theory]
    [InlineData(1200, 750, 1.0, "Apartar")]
    [InlineData(980, 620, 1.0, "Reincorporar")]
    [InlineData(980, 620, 1.5, "Baja")]
    public void Inventario_layout_etiquetas_y_render(int ancho, int alto, double escala, string accion)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            // Presentation fixture only: no command calls or database access.
            var servicios = new ServiciosComerciales(Path.Combine(Path.GetTempPath(), "inventario-render-unused.db"));
            var vm = new ApartadoViewModel(servicios, () => null)
            {
                Existencia = new ExistenciaClasificada("v", "BUZ-001-02", 5, 3, 2, "Buzo canguro", "L", "Azul")
            };
            vm.SeleccionarAccionCommand.Execute(accion);
            var vista = new ApartadoView { DataContext = vm };
            var shell = new ShellDuenaView
            {
                DataContext = new
                {
                    VistaActual = vista, SeccionActiva = "Mercaderia", HayVentas = true, HayClientes = true,
                    Caja = new { PuedeVer = true }, Identidad = "Vista de prueba", VersionApp = "Fixture UX"
                }
            };
            shell.Measure(new Size(ancho, alto));
            shell.Arrange(new Rect(0, 0, ancho, alto));
            shell.UpdateLayout();
            var cantidad = Assert.IsType<TextBox>(vista.FindName("Cantidad" + accion));
            var motivo = Assert.IsType<TextBox>(vista.FindName("Motivo" + accion));
            Assert.Equal(Visibility.Visible, cantidad.Visibility);
            Assert.Equal(Visibility.Visible, motivo.Visibility);
            Assert.True(cantidad.ActualWidth >= 100);
            Assert.True(motivo.ActualWidth >= 150);
            var controles = Descendientes(vista).ToList();
            Assert.Contains(controles.OfType<Label>(), l => ReferenceEquals(l.Target, cantidad));
            Assert.Contains(controles.OfType<Label>(), l => ReferenceEquals(l.Target, motivo));
            Assert.Equal("Buzo canguro · L · Azul", vm.DescripcionVariante);
            Assert.Contains(controles.OfType<Button>(), b => (string?)b.Content ==
                (accion == "Apartar" ? "Apartar prendas" : accion == "Reincorporar" ? "Reincorporar prendas" : "Confirmar baja definitiva"));
            var menus = Descendientes(shell).OfType<Menu>().ToList();
            var principal = menus.Single(m => m.Items.Count == 6);
            var usuario = menus.Single(m => m.Items.Count == 1);
            Assert.True(principal.TranslatePoint(new Point(principal.ActualWidth, 0), shell).X <= usuario.TranslatePoint(new Point(), shell).X);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)(ancho * escala), (int)(alto * escala), 96 * escala, 96 * escala, PixelFormats.Pbgra32);
            bitmap.Render(shell);
            Assert.Equal((int)(ancho * escala), bitmap.PixelWidth);
            var salida = Environment.GetEnvironmentVariable("LOCALINFANTIL_UX_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(salida))
            {
                Directory.CreateDirectory(salida);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var archivo = File.Create(Path.Combine(salida, $"inventario-{ancho}x{alto}-{accion}-{escala}.png"));
                encoder.Save(archivo);
            }
        });
    }

    private static IEnumerable<DependencyObject> Descendientes(DependencyObject raiz)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            yield return hijo;
            foreach (var descendiente in Descendientes(hijo)) yield return descendiente;
        }
    }
}
