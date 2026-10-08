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
/// aplicación, instancian vistas, asignan ViewModel y fuerzan layout para
/// que los BindingExpression se activen. Detectan XamlParseException,
/// StaticResource inexistentes y bindings TwoWay contra propiedades
/// read-only (crash real de Caja con IngresosTexto). Requiere sesión
/// gráfica de Windows; no hace screenshots ni prueba estética.
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
        throw new InvalidOperationException("No se encontró la raíz del repositorio (LocalInfantil.sln).");
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
            throw new TimeoutException("El smoke test WPF no terminó en 2 minutos.");
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
            // Diálogos con constructor con argumentos (FichaProductoWindow,
            // MatrizVariantesWindow, DetalleRecepcionWindow, EtiquetasWindow,
            // Ticket*Window): no instanciables sin datos; cubiertos por el
            // lint estático de VistasRecursosTests (parse XAML + StaticResource).
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
}
