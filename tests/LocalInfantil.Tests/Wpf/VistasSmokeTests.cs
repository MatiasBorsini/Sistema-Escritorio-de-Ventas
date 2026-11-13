using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Threading;
using System.Xml;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Inicio;
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

    [Theory]
    [InlineData(1680, 900)]
    [InlineData(1480, 900)]
    [InlineData(1280, 720)]
    [InlineData(1024, 576)]
    public async Task CajaView_carga_enlaza_y_mide_sin_excepciones(int ancho, int alto)
    {
        using var e = await EntornoCaja.CrearAsync();
        var vm = new CajaViewModel(e.Servicios, () => e.Sesion);
        await vm.CargarAsyncPublic();
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new CajaView { DataContext = vm };
            Assert.IsType<TextBox>(vista.FindName("BusquedaVentaCajaTextBox"));
            Assert.IsType<ListBox>(vista.FindName("SugerenciasVentasCajaListBox"));
            vista.Measure(new Size(ancho, alto));
            vista.Arrange(new Rect(0, 0, ancho, alto));
            vista.UpdateLayout();
            Assert.True(vista.ActualWidth <= ancho + 1);
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

    [Fact]
    public void Cuenta_corriente_consulta_los_bindings_del_listado_en_el_viewmodel_padre()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            // El listado y la búsqueda se enlazan al VM padre (sin DataContext interno).
            var vm = new CuentaCorrienteViewModel(new ServiciosComerciales(Path.Combine(Path.GetTempPath(), $"cuenta-busqueda-{Guid.NewGuid():N}.db")), () => null, false, false, false, false);
            var vista = new CuentaCorrienteView { DataContext = vm };
            Medir(vista);

            var grilla = Assert.IsType<DataGrid>(vista.FindName("ClientesCuentaDataGrid"));
            Assert.Same(vm, grilla.DataContext);
            Assert.Same(vm.Clientes, grilla.ItemsSource);
            var buscador = Assert.IsType<TextBox>(vista.FindName("BusquedaCuentaTextBox"));
            Assert.Same(vm, buscador.DataContext);
        });
    }

    [Fact]
    public async Task Cuenta_corriente_cambiar_filtro_en_ComboBox_real_no_cierra_WPF_con_clientes_sin_fiado()
    {
        using var e = await EntornoCaja.CrearAsync();
        await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cliente sin fiado A", "11-4100", null), e.Sesion);
        await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cliente sin fiado B", "11-4200", null), e.Sesion);
        var vm = new CuentaCorrienteViewModel(e.Servicios, () => e.Sesion, true, true, true, true);
        await vm.CargarClientesAsync();

        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new CuentaCorrienteView { DataContext = vm };
            Medir(vista);
            var filtro = Assert.IsType<ComboBox>(vista.FindName("FiltroClientesCuentaComboBox"));
            Assert.Equal((int)FiltroClientesCuentaCorriente.Todos, filtro.SelectedIndex);

            // Cambia el ComboBox WPF real rápidamente; el último filtro debe
            // ganar y la grilla debe materializar filas con propiedades de solo lectura.
            filtro.SelectedIndex = (int)FiltroClientesCuentaCorriente.ConDeuda;
            filtro.SelectedIndex = (int)FiltroClientesCuentaCorriente.FiadoHabilitado;
            filtro.SelectedIndex = (int)FiltroClientesCuentaCorriente.Todos;
            EsperarDispatcher(() => !vm.OcupadoListado && vm.Clientes.Count == 2);
            vista.UpdateLayout();

            Assert.Equal(FiltroClientesCuentaCorriente.Todos, vm.FiltroClientes);
            Assert.Equal(2, vm.Clientes.Count);
            Assert.All(vm.Clientes, cliente => Assert.Null(cliente.LimiteMinor));
            var busqueda = Assert.IsType<TextBox>(vista.FindName("BusquedaCuentaTextBox"));
            var grilla = Assert.IsType<DataGrid>(vista.FindName("ClientesCuentaDataGrid"));
            grilla.SelectedItem = vm.Clientes[0];
            vista.UpdateLayout();
            Assert.Equal(vm.Clientes[0].Id, vm.Cliente?.Id);
            Assert.Equal(string.Empty, vm.TextoCliente);
            Assert.Equal(2, vm.Clientes.Count);

            grilla.SelectedItem = vm.Clientes[1];
            vista.UpdateLayout();
            EsperarDispatcher(() => !vm.Ocupado && vm.Cliente?.Id == vm.Clientes[1].Id);
            Assert.Equal(string.Empty, vm.TextoCliente);
            Assert.Equal(2, vm.Clientes.Count);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(vista.FindName("AjustarDeudaButton")).Visibility);

            Assert.Same(vm, busqueda.DataContext);
            Assert.Equal(string.Empty, busqueda.Text);
            Assert.Equal(string.Empty, vm.TextoCliente);
        });
    }

    private static void EsperarDispatcher(Func<bool> condicion)
    {
        var frame = new DispatcherFrame();
        var limite = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        timer.Tick += (_, _) =>
        {
            if (condicion() || DateTime.UtcNow >= limite)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        Assert.True(condicion(), "El filtro real de Cuenta corriente no terminó a tiempo.");
    }

    private sealed class ContextoBuscadorCliente
    {
        public MotorSugerencias<SugerenciaCliente> MotorClientes { get; } = new(
            (_, _) => Task.FromResult<IReadOnlyList<SugerenciaCliente>>([]), TimeSpan.Zero);
        public RelayCommand SeleccionarClienteCommand { get; } = new(_ => { });
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
    [InlineData(1480, 900)]
    [InlineData(1280, 720)]
    [InlineData(1024, 576)]
    [InlineData(1366, 768)]
    public void Cambios_mide_en_resoluciones_objetivo_sin_scroll_horizontal(int ancho, int alto)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new CambiosView();
            vista.Measure(new Size(ancho, alto));
            vista.Arrange(new Rect(0, 0, ancho, alto));
            vista.UpdateLayout();

            Assert.True(vista.ActualWidth <= ancho + 1);
            // Sin scroll horizontal en contenedores de página; el interno de la
            // grilla de prendas queda excluido (red de seguridad en 1024).
            Assert.All(Descendientes(vista).OfType<ScrollViewer>().Where(s => s.TemplatedParent is not DataGrid), scroll =>
                Assert.NotEqual(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility));
        });
    }

    [Theory]
    [InlineData(1680, 900)]
    [InlineData(1280, 720)]
    [InlineData(1024, 576)]
    public void Portada_muestra_indicadores_reales_y_mide_sin_scroll_horizontal(int ancho, int alto)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var resumen = new ResumenInicio(
                new HashSet<string> { ConsultasInicio.RecibirMercaderia, ConsultasInicio.NuevoProducto, ConsultasInicio.ConteoFisico },
                2,
                new ResumenInventarioInicio(14, 10, 4),
                [new ActividadInicio("Recepción confirmada", "Ropa Sur", 7, "Ana", "2026-10-17T15:00:00Z")],
                new VentasDelDiaInicio(123450, 3),
                new RecepcionRecienteInicio("Ropa Sur", 7, "2026-10-17T15:00:00Z"));
            var vm = new PortadaViewModel("María", resumen,
                new RelayCommand(_ => { }), new RelayCommand(_ => { }), new RelayCommand(_ => { }), new RelayCommand(_ => { }),
                null, new RelayCommand(_ => { }), new RelayCommand(_ => { }));
            var vista = new PortadaView { DataContext = vm };
            vista.Measure(new Size(ancho, alto));
            vista.Arrange(new Rect(0, 0, ancho, alto));
            vista.UpdateLayout();

            Assert.True(vista.ActualWidth <= ancho + 1);
            Assert.All(Descendientes(vista).OfType<ScrollViewer>(), scroll =>
                Assert.NotEqual(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility));
            var textos = Descendientes(vista).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Inicio", textos);
            Assert.Contains("Ventas de hoy", textos);
            Assert.Contains("ARS 1.234,50", textos);
            Assert.Contains("Inventario disponible", textos);
            Assert.Contains("Recepción más reciente", textos);
            Assert.Contains("Ropa Sur", textos);
            Assert.Contains("Acciones frecuentes", textos);
            Assert.Contains("2 conteos esperan revisión", textos);
            Assert.Contains("4 unidades están apartadas", textos);
            Assert.Contains("Para revisar", textos);
            Assert.Contains("Actividad reciente", textos);
            Assert.Contains("Recepción confirmada", textos);
        });
    }

    [Theory]
    [InlineData(1480, 900)]
    [InlineData(1280, 720)]
    public void VentasConsulta_muestra_filtros_fecha_y_cabe_en_resoluciones_objetivo(int ancho, int alto)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new VentasConsultaView();
            vista.Measure(new Size(ancho, alto));
            vista.Arrange(new Rect(0, 0, ancho, alto));
            vista.UpdateLayout();

            Assert.Equal(2, Descendientes(vista).OfType<DatePicker>().Count());
            Assert.Single(Descendientes(vista).OfType<Button>(), b => (string?)b.Content == "Limpiar filtros");
            Assert.True(vista.ActualWidth <= ancho + 1);
        });
    }

    [Fact]
    public void VentasConsulta_enlaza_buscador_reactivo_y_etiqueta_filtros()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new VentasConsultaView();
            var buscador = Assert.IsType<TextBox>(vista.FindName("BuscarVentaTextBox"));
            Assert.Equal("Buscar venta por número de ticket, nombre de producto o código", AutomationProperties.GetName(buscador));
            var binding = BindingOperations.GetBinding(buscador, TextBox.TextProperty);

            Assert.NotNull(binding);
            Assert.Equal("TextoBusqueda", binding!.Path.Path);
            Assert.Equal(UpdateSourceTrigger.PropertyChanged, binding.UpdateSourceTrigger);

        });
    }

    [Theory]
    [InlineData(1680, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Recepciones_compacta_encabezado_seleccion_y_ficha_con_shell_y_aviso(int ancho, int alto)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            // Fixture de presentación: no abre ni escribe una base de datos.
            var servicios = new ServiciosComerciales(Path.Combine(Path.GetTempPath(), $"recepciones-layout-{Guid.NewGuid():N}.db"));
            var vm = new RecepcionesConsultaViewModel(servicios, () => null);
            var resumen = new ResumenRecepcion("id-tecnico", "prov", "Proveedor de prueba", "op", "REM-UX-001", null, "Dueña", "2026-10-17T15:00:00Z", 2, 3, 0, 750000, false);
            var fila = new FilaHistorialRecepcion(resumen);
            vm.Historial.Add(fila);
            vm.Historial.Add(new FilaHistorialRecepcion(resumen with { Id = "otro-id", ComprobanteRef = null }));
            vm.FilaSeleccionada = fila;
            vm.LineasDetalle = [new("BUZ-001-01", "M", "Azul", 2, 250000, "ARS"), new("BUZ-001-02", "L", "Azul", 1, 250000, "ARS")];
            var vista = new RecepcionesConsultaView { DataContext = vm };
            var shell = new ShellDuenaView { DataContext = new { VistaActual = vista, SeccionActiva = "Mercaderia", HayVentas = true, HayClientes = true, Caja = new { PuedeVer = true }, Identidad = "Dueña de prueba", VersionApp = "Fixture UX" } };
            var host = new DockPanel();
            var aviso = new TextBlock { Text = "Modo desarrollo — Base ficticia de prueba", Height = 32 };
            DockPanel.SetDock(aviso, Dock.Top);
            host.Children.Add(aviso);
            host.Children.Add(shell);
            host.Measure(new Size(ancho, alto));
            host.Arrange(new Rect(0, 0, ancho, alto));
            host.UpdateLayout();

            var visuales = Descendientes(vista).ToList();
            var buscador = Assert.Single(visuales.OfType<TextBox>(), t => (System.Windows.Automation.AutomationProperties.GetName(t) ?? "").StartsWith("Buscar por comprobante"));
            Assert.Equal(new[] { "Desde, fecha de registro", "Hasta, fecha de registro" },
                visuales.OfType<DatePicker>()
                    .Select(System.Windows.Automation.AutomationProperties.GetName)
                    .OrderBy(nombre => nombre)
                    .ToArray());
            var subtitulo = Assert.Single(visuales.OfType<TextBlock>(), t => t.Text == "Historial de ingresos y detalle de mercadería recibida.");
            var distancia = buscador.TranslatePoint(new Point(), vista).Y - subtitulo.TranslatePoint(new Point(0, subtitulo.ActualHeight), vista).Y;
            Assert.InRange(distancia, 0, 16);
            var ficha = Assert.IsType<Border>(vista.FindName("FichaRecepcion"));
            var cuerpo = Assert.IsType<Grid>(vista.FindName("CuerpoRecepciones"));
            var scrollDetalle = Assert.IsType<ScrollViewer>(vista.FindName("DetalleSeleccionado"));
            Assert.True(ficha.ActualHeight <= cuerpo.ActualHeight);
            // La ficha mide contenido + padding/borde, sin una fila expansiva
            // que agregue blanco hasta el fondo. No exige un hueco artificial.
            var marco = ficha.Padding.Top + ficha.Padding.Bottom + ficha.BorderThickness.Top + ficha.BorderThickness.Bottom;
            if (alto <= 720)
            {
                Assert.True(scrollDetalle.ExtentHeight >= scrollDetalle.ViewportHeight);
                Assert.Equal(ScrollBarVisibility.Disabled, scrollDetalle.HorizontalScrollBarVisibility);
            }
            else
            {
                Assert.InRange(ficha.ActualHeight - scrollDetalle.ExtentHeight, marco - 2, marco + 2);
            }
            var grilla = Assert.IsType<DataGrid>(vista.FindName("Grilla"));
            Assert.Same(fila, grilla.SelectedItem);
            var seleccion = Assert.IsType<DataGridRow>(grilla.ItemContainerGenerator.ContainerFromItem(fila));
            Assert.True(seleccion.IsSelected);
            Assert.Equal(vista.FindResource("Color.SuccessBgBrush"), seleccion.Background);
            Assert.Equal(vista.FindResource("Color.BorderSoftBrush"), grilla.HorizontalGridLinesBrush);
            Assert.Equal(40, grilla.RowHeight);
            Assert.True(grilla.EnableRowVirtualization);
            Assert.All(grilla.Columns, c => Assert.True(c.ActualWidth >= c.MinWidth));
            Assert.Contains(visuales.OfType<TextBlock>(), t => t.Text == "Comprobante del proveedor: REM-UX-001");
            Assert.Equal(Visibility.Visible, visuales.OfType<TextBox>().Single(t => t.ToolTip?.ToString() == "Indicá el motivo antes de solicitar la reversión").Visibility);
        });
    }

    [Fact]
    public void Recepcion_ajusta_la_grilla_y_encabezados_en_1200x750()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new RecepcionView();
            vista.Measure(new Size(1200, 750));
            vista.Arrange(new Rect(0, 0, 1200, 750));
            vista.UpdateLayout();

            var grilla = Assert.Single(Descendientes(vista).OfType<DataGrid>());
            Assert.True(vista.ActualWidth <= 1200 + 1);
            Assert.Equal(8, grilla.Columns.Count);
            Assert.True(grilla.ActualWidth > 0);
            Assert.Contains("Stock actual", grilla.Columns.Select(c => c.Header?.ToString()));
            Assert.Contains("Cantidad recibida", grilla.Columns.Select(c => c.Header?.ToString()));
            Assert.Contains("Stock después", grilla.Columns.Select(c => c.Header?.ToString()));
            Assert.Contains("Costo total", grilla.Columns.Select(c => c.Header?.ToString()));
            Assert.True(grilla.Columns.Single(c => c.Header?.ToString() == "Stock actual").IsReadOnly);
            Assert.False(grilla.Columns.Single(c => c.Header?.ToString() == "Cantidad recibida").IsReadOnly);
            Assert.False(grilla.Columns.Single(c => c.Header?.ToString() == "Costo unitario").IsReadOnly);
            Assert.True(grilla.Columns.Single(c => c.Header?.ToString() == "Stock después").IsReadOnly);
            Assert.True(grilla.Columns.Single(c => c.Header?.ToString() == "Costo total").IsReadOnly);
        });
    }

    [Theory]
    [InlineData(1366, 768)]
    [InlineData(1200, 750)]
    [InlineData(980, 620)]
    public void Pos_es_progresivo_responsivo_y_enfoca_busqueda_al_abrir(int ancho, int alto)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var servicios = new ServiciosComerciales(Path.Combine(Path.GetTempPath(), $"pos-layout-{Guid.NewGuid():N}.db"));
            var vm = new PosViewModel(servicios, () => null);
            var vista = new PosView { DataContext = vm };
            var ventana = new Window
            {
                Content = vista,
                Width = ancho,
                Height = alto,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 20,
                Top = 20,
                ShowInTaskbar = false,
            };
            try
            {
                ventana.Show();
                ventana.Activate();
                vista.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                // Rediseño POS: raíz en grilla de dos columnas, sin scroll de
                // página; se verifica que ningún panel interno desborde.
                Assert.IsType<Grid>(vista.Content);
                Assert.True(Descendientes(vista).OfType<ScrollViewer>().Any());
                foreach (var interno in Descendientes(vista).OfType<ScrollViewer>())
                    Assert.True(interno.ExtentWidth <= interno.ViewportWidth + 1,
                        $"El POS desborda horizontalmente a {ancho}x{alto}: contenido {interno.ExtentWidth}px, viewport {interno.ViewportWidth}px.");
                var buscador = Assert.IsType<BuscadorAsistido>(vista.FindName("Buscador"));
                var caja = Assert.IsType<TextBox>(buscador.FindName("Caja"));
                Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(buscador.FindName("TextoPlaceholder")).Visibility);
                Assert.True(caja.IsKeyboardFocused, $"El buscador no recibió foco al abrir a {ancho}x{alto}.");
                Assert.Equal(Visibility.Collapsed,
                    Assert.Single(Descendientes(vista).OfType<Button>(), b => (string?)b.Content == "Cancelar venta").Visibility);
            }
            finally
            {
                ventana.Close();
            }
        });
    }

    [Fact]
    public void CobrarVentaWindow_reutiliza_el_mismo_viewmodel_y_carga_sus_controles()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var servicios = new ServiciosComerciales(Path.Combine(Path.GetTempPath(), $"pos-cobro-modal-{Guid.NewGuid():N}.db"));
            var vm = new PosViewModel(servicios, () => null);
            vm.Filas.Add(new FilaVentaViewModel
            {
                VarianteId = "variante-fixture",
                CodigoInterno = "REM-001-01",
                NombreProducto = "Remera de ejemplo",
                Talle = "M",
                Color = "Azul",
                PrecioUnitarioMinor = 3500,
            });
            vm.AbrirCobroCommand.Execute(null);

            var ventana = new CobrarVentaWindow(vm);
            try
            {
                ventana.WindowStartupLocation = WindowStartupLocation.Manual;
                ventana.Left = 20;
                ventana.Top = 20;
                ventana.ShowInTaskbar = false;
                ventana.Show();
                ventana.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Same(vm, ventana.DataContext);
                Assert.Equal("Cobrar venta", ventana.Title);
                var botones = Descendientes(ventana).OfType<Button>().ToArray();
                Assert.Contains(botones, b => (string?)b.Content == "Volver al carrito");
                Assert.Contains(botones, b => (string?)b.Content == "Confirmar venta");
                var modalidades = Descendientes(ventana).OfType<RadioButton>().ToArray();
                Assert.Equal(3, modalidades.Length);
                Assert.All(modalidades, radio => Assert.Same(modalidades[0].Style, radio.Style));
                Assert.True(modalidades[0].IsChecked);
                Assert.All(modalidades.Skip(1), radio => Assert.False(radio.IsEnabled));
                var textosModalidad = Descendientes(ventana).OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains("Paga todo ahora", textosModalidad);
                Assert.Contains("Toda la venta a cuenta", textosModalidad);
                Assert.Contains("Pago parcial y resto a cuenta", textosModalidad);
                Assert.Contains("Elegí un cliente para habilitar la venta a cuenta.", textosModalidad);
                Assert.True(vm.CobroVisible);
                Assert.Single(vm.Filas);
            }
            finally
            {
                ventana.Close();
            }

            Assert.False(vm.CobroVisible);
            Assert.Single(vm.Filas);
        });
    }

    [Fact]
    public void ConfirmarCambioWindow_reutiliza_el_borrador_y_carga_los_controles_de_cobro()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var servicios = new ServiciosComerciales(Path.Combine(Path.GetTempPath(), $"cambio-confirmacion-modal-{Guid.NewGuid():N}.db"));
            var vm = new CambiosViewModel(servicios, () => null, puedeConsultar: true, puedeRegistrar: true);
            var ventana = new ConfirmarCambioWindow(vm);
            try
            {
                ventana.WindowStartupLocation = WindowStartupLocation.Manual;
                ventana.Left = 20;
                ventana.Top = 20;
                ventana.ShowInTaskbar = false;
                ventana.Show();
                ventana.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Same(vm, ventana.DataContext);
                var listaPagos = Assert.Single(Descendientes(ventana).OfType<ItemsControl>().Where(x => x is not ComboBox));
                Assert.Same(vm.Pagos, listaPagos.ItemsSource);
                Assert.Contains(Descendientes(ventana).OfType<Button>(), b => (string?)b.Content == "Volver a la preparación");
                Assert.Contains(Descendientes(ventana).OfType<Button>(), b => (string?)b.Content == "Confirmar operación");
            }
            finally
            {
                ventana.Close();
            }
        });
    }

    [Fact]
    public void Buscador_POS_Enter_agrega_la_activa_y_prioriza_codigo_exacto()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var primera = new SugerenciaVariante("v1", "p", "REM-001-01", "Remera", null, null, "S", null, true);
            var segunda = new SugerenciaVariante("v2", "p", "REM-001-02", "Remera", null, null, "M", null, true);
            var motor = new MotorSugerencias<SugerenciaVariante>((_, _) =>
                Task.FromResult((IReadOnlyList<SugerenciaVariante>)new[] { primera, segunda }),
                debounce: TimeSpan.Zero, seleccionarPrimeraAlBuscar: false);
            SugerenciaVariante? seleccionada = null;
            string? busquedaDirecta = null;
            var control = new BuscadorAsistido
            {
                Motor = motor,
                SeleccionCommand = new RelayCommand(p => seleccionada = p as SugerenciaVariante),
                BusquedaDirectaCommand = new RelayCommand(p => busquedaDirecta = p as string),
                PatronCodigoExactoEnEnter = "^[A-Za-z]{2,5}-[0-9]{3}-[A-Za-z0-9-]+$",
            };
            var ventana = new Window { Content = control, Width = 600, Height = 300, ShowInTaskbar = false };
            try
            {
                ventana.Show();
                ventana.Activate();
                var caja = Assert.IsType<TextBox>(control.FindName("Caja"));
                caja.Focus();
                caja.Text = "Remera";
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(control.FindName("TextoPlaceholder")).Visibility);
                Assert.Equal(-1, motor.IndiceActivo);

                void Presionar(Key tecla)
                {
                    var fuente = PresentationSource.FromVisual(caja);
                    Assert.NotNull(fuente);
                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, fuente!, Environment.TickCount, tecla)
                    {
                        RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    };
                    caja.RaiseEvent(args);
                }

                Presionar(Key.Down);
                Presionar(Key.Down);
                Presionar(Key.Enter);
                Assert.Same(segunda, seleccionada);
                Assert.Null(busquedaDirecta);

                seleccionada = null;
                caja.Text = "REM-001-02";
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Presionar(Key.Enter);
                Assert.Null(seleccionada);
                Assert.Equal("REM-001-02", busquedaDirecta);
            }
            finally
            {
                ventana.Close();
            }
        });
    }

    [Fact]
    public void Buscador_un_clic_sobre_sugerencia_confirma_sin_Enter()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var primera = new SugerenciaVariante("v1", "p", "BUZ-001-01", "Buzo", null, null, "S", "Azul", true);
            var segunda = new SugerenciaVariante("v2", "p", "BUZ-001-02", "Buzo", null, null, "M", "Azul", true);
            var motor = new MotorSugerencias<SugerenciaVariante>((_, _) =>
                    Task.FromResult((IReadOnlyList<SugerenciaVariante>)new[] { primera, segunda }),
                debounce: TimeSpan.Zero);
            SugerenciaVariante? seleccionada = null;
            var control = new BuscadorAsistido
            {
                Motor = motor,
                SeleccionCommand = new RelayCommand(p => seleccionada = p as SugerenciaVariante),
            };
            var ventana = new Window { Content = control, Width = 600, Height = 280, ShowInTaskbar = false };
            try
            {
                ventana.Show();
                ventana.Activate();
                var caja = Assert.IsType<TextBox>(control.FindName("Caja"));
                caja.Focus();
                caja.Text = "buz";
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var lista = Assert.IsType<ListBox>(control.FindName("Lista"));
                lista.UpdateLayout();
                var segundaFila = Assert.IsType<ListBoxItem>(lista.ItemContainerGenerator.ContainerFromIndex(1));
                var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Mouse.PreviewMouseDownEvent,
                };

                segundaFila.RaiseEvent(args);

                Assert.True(args.Handled);
                Assert.Same(segunda, seleccionada);
                Assert.False(motor.Abierto);
            }
            finally
            {
                ventana.Close();
            }
        });
    }

    [Fact]
    public void Buscador_no_reconsulta_al_reflejar_texto_programatico_del_motor()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var consultas = 0;
            var motor = new MotorSugerencias<SugerenciaVariante>((_, _) =>
            {
                consultas++;
                return Task.FromResult((IReadOnlyList<SugerenciaVariante>)Array.Empty<SugerenciaVariante>());
            }, debounce: TimeSpan.Zero);
            var control = new BuscadorAsistido { Motor = motor };
            var ventana = new Window { Content = control, Width = 500, Height = 160, ShowInTaskbar = false };
            try
            {
                ventana.Show();
                var caja = Assert.IsType<TextBox>(control.FindName("Caja"));
                motor.EstablecerTexto("BUZ-001-01");
                Assert.Equal("BUZ-001-01", caja.Text);
                Assert.Equal(0, consultas);

                caja.Text = "BUZ-001-02";
                Assert.Equal(1, consultas);
            }
            finally
            {
                ventana.Close();
            }
        });
    }

    [Theory]
    [InlineData(1366, 768)]
    [InlineData(1200, 750)]
    [InlineData(980, 620)]
    public void Pos_con_cobro_abierto_mantiene_carrito_y_labels_sin_scroll_horizontal(int ancho, int alto)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var servicios = new ServiciosComerciales(Path.Combine(Path.GetTempPath(), $"pos-cobro-layout-{Guid.NewGuid():N}.db"));
            // F12-B: sin modalidad guardada, una terminal configurada deja el
            // Point en integrado (comportamiento previo a los modos).
            servicios.ConfigMercadoPagoStore.Guardar(new ConfigMercadoPago("FUTURO_X1__SN000001", "PDV", null, null, null, null));
            var vm = new PosViewModel(servicios, () => null);
            vm.Filas.Add(new FilaVentaViewModel
            {
                VarianteId = "variante-fixture",
                CodigoInterno = "REM-001-01",
                NombreProducto = "Remera de ejemplo",
                Talle = "M",
                Color = "Azul",
                PrecioUnitarioMinor = 3500,
            });
            vm.Pagos[0].ImporteTexto = "35,00";
            vm.Pagos[0].RecibidoTexto = "35,00";
            vm.AbrirCobroCommand.Execute(null);

            var vista = new PosView { DataContext = vm };
            var ventana = new Window
            {
                Content = vista,
                Width = ancho,
                Height = alto,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 25,
                Top = 25,
                ShowInTaskbar = false,
            };
            try
            {
                ventana.Show();
                vista.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                // Rediseño POS: la raíz es una grilla de dos columnas sin scroll
                // de página; el desborde horizontal se verifica en cada
                // ScrollViewer interno (carrito y medios de pago).
                Assert.IsType<Grid>(vista.Content);
                foreach (var interno in Descendientes(vista).OfType<ScrollViewer>())
                    Assert.True(interno.ExtentWidth <= interno.ViewportWidth + 1,
                        $"Cobro desborda horizontalmente a {ancho}x{alto}.");
                var textos = Descendientes(vista).OfType<TextBlock>().Select(tb => tb.Text).ToArray();
                Assert.Contains("Medio de pago", textos);
                Assert.Contains("Importe", textos);
                Assert.Contains("Efectivo recibido", textos);
                Assert.Contains("Sin vuelto", textos);
                Assert.Contains("Carrito", textos);
                Assert.Contains("1 variante · 1 unidad", textos);
                var grilla = Assert.Single(Descendientes(vista).OfType<DataGrid>());
                Assert.Equal(Visibility.Visible, grilla.Visibility);
                var confirmar = Assert.Single(Descendientes(vista).OfType<Button>(), b => (string?)b.Content == "Confirmar venta");
                Assert.True(confirmar.IsEnabled);
                var recibidoLabel = Assert.Single(Descendientes(vista).OfType<TextBlock>(), tb => tb.Text == "Efectivo recibido");
                var recibidoInput = Assert.Single(Descendientes(vista).OfType<TextBox>(), tb => tb.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == "RecibidoTexto");
                Assert.Equal(Visibility.Visible, recibidoLabel.Visibility);
                Assert.Equal(Visibility.Visible, recibidoInput.Visibility);

                vm.Pagos[0].Medio = MediosPago.Transferencia;
                vista.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(Visibility.Collapsed, recibidoLabel.Visibility);
                Assert.Equal(Visibility.Collapsed, recibidoInput.Visibility);

                vm.Pagos[0].Medio = MediosPago.Debito;
                vista.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var ayudaExterna = Assert.Single(Descendientes(vista).OfType<TextBlock>(), tb => tb.Text == "Este cobro se realiza fuera de LocalInfantil. Aquí solo se registra el importe.");
                Assert.Equal(Visibility.Visible, ayudaExterna.Visibility);
                Assert.DoesNotContain(Descendientes(vista).OfType<TextBlock>(), tb => tb.Text.Contains("Cuota", StringComparison.OrdinalIgnoreCase));

                vm.Pagos[0].Medio = MediosPago.MercadoPago;
                vista.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var ayudaPoint = Assert.Single(Descendientes(vista).OfType<TextBlock>(), tb => tb.Text == vm.AyudaPoint);
                Assert.Equal(Visibility.Visible, ayudaPoint.Visibility);
                Assert.Equal(Visibility.Collapsed, ayudaExterna.Visibility);
                var point = Assert.Single(Descendientes(vista).OfType<Button>(), b => (string?)b.Content == "Cobrar ARS 35,00 en Point");
                Assert.True(point.IsEnabled);
                Assert.Equal(Visibility.Collapsed,
                    Assert.Single(Descendientes(vista).OfType<Button>(), b => (string?)b.Content == "Confirmar venta").Visibility);

                var cancelar = Assert.Single(Descendientes(vista).OfType<Button>(), b => (string?)b.Content == "Cancelar venta");
                Assert.True(cancelar.ActualWidth < 200, "Cancelar venta debe conservar ancho de acción secundaria.");
                var ajustes = Descendientes(vista).OfType<Button>().Where(b => (string?)b.Content is "−" or "+").ToArray();
                Assert.Equal(2, ajustes.Length);
                Assert.All(ajustes, b => Assert.True(b.ActualWidth >= 34));
            }
            finally
            {
                ventana.Close();
            }
        });
    }

    [Theory]
    [InlineData(1680, 900)]
    [InlineData(1280, 720)]
    public void Clientes_mantiene_listado_compacto_y_ficha_financiera_separada(int ancho, int alto)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var servicios = new ServiciosComerciales(Path.Combine(Path.GetTempPath(), $"clientes-layout-{Guid.NewGuid():N}.db"));
            var vm = new ClientesViewModel(servicios, () => null, puedeAdministrar: true, puedeConsultarCuenta: true);
            vm.Clientes.Add(new ClienteRegistrado("cliente-1", "María López", "11-5555-0101", "v", true, "", ""));
            vm.Clientes.Add(new ClienteRegistrado("cliente-2", "María Pérez", null, null, false, "", ""));
            vm.Seleccionado = vm.Clientes[0];

            var vista = new ClientesView { DataContext = vm };
            var ventana = new Window
            {
                Content = vista,
                Width = ancho,
                Height = alto,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 20,
                Top = 20,
                ShowInTaskbar = false,
            };
            try
            {
                ventana.Show();
                vista.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var grilla = Assert.Single(Descendientes(vista).OfType<DataGrid>());
                Assert.Equal(2, grilla.Items.Count);
                Assert.Same(vm.Seleccionado, grilla.SelectedItem);
                Assert.True(grilla.ActualHeight < vista.ActualHeight * 0.55,
                    $"El listado con pocos clientes ocupa demasiado alto a {ancho}x{alto}: {grilla.ActualHeight}px.");

                var textos = Descendientes(vista).OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains("Activo", textos);
                Assert.Contains("Saldo pendiente", textos);
                Assert.Contains("Fiado no habilitado", textos);
                Assert.Contains("Observación:", textos);
                Assert.DoesNotContain(textos, t => t.Contains("Disponible ARS", StringComparison.Ordinal) && t.Contains("Activo", StringComparison.Ordinal));
            }
            finally
            {
                ventana.Close();
            }
        });
    }

    public sealed class RespaldosLayoutFixture
    {
        public string Error { get; set; } = string.Empty;
        public string Mensaje { get; set; } = string.Empty;
        public string Estado { get; set; } = "No existe una copia local verificada";
        public string RutaLocal { get; set; } = @"C:\Temp\LocalInfantil\Backups";
        public bool RutaPuedeSincronizarse { get; set; }
        public bool DriveConfigurado { get; set; }
        public bool DriveConectado { get; set; }
        public bool PuedeCrear { get; set; }
        public bool PuedeConfigurar { get; set; } = true;
        public bool PuedeRestaurar { get; set; } = true;
        public bool Configurado { get; set; }
        public bool HayCodigo { get; set; }
        public bool Ocupado { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Confirmacion { get; set; } = string.Empty;
        public string CodigoRestauracion { get; set; } = string.Empty;
        public string ArchivoRestauracion { get; set; } = string.Empty;
        public object? CopiaDriveSeleccionada { get; set; }
        public ObservableCollection<object> Copias { get; } = [];
        public ObservableCollection<object> CopiasDrive { get; } = [];
    }

    [Theory]
    [InlineData(1200, 750)]
    [InlineData(1366, 768)]
    [InlineData(980, 620)]
    public void Respaldos_mantiene_dos_columnas_y_sin_desborde_horizontal(int ancho, int alto)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new RespaldosView();
            vista.Measure(new Size(ancho, alto));
            vista.Arrange(new Rect(0, 0, ancho, alto));
            vista.UpdateLayout();

            var scroll = Assert.IsType<ScrollViewer>(vista.Content);
            var paneles = Assert.IsType<Grid>(vista.FindName("Paneles"));
            Assert.Equal(2, paneles.ColumnDefinitions.Count);
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.True(scroll.ViewportWidth > 0);
            Assert.True(paneles.ActualWidth <= scroll.ViewportWidth + 1,
                $"El panel mide {paneles.ActualWidth} px con viewport {scroll.ViewportWidth} px a {ancho}x{alto}.");
        });
    }

    [Fact]
    public void Respaldos_muestra_empty_states_y_acciones_deshabilitadas_sin_datos()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new RespaldosView { DataContext = new RespaldosLayoutFixture() };
            vista.Measure(new Size(1200, 750));
            vista.Arrange(new Rect(0, 0, 1200, 750));
            vista.UpdateLayout();

            var vacioLocal = Assert.IsType<Border>(vista.FindName("EstadoVacioCopias"));
            var vacioDrive = Assert.IsType<Border>(vista.FindName("EstadoVacioDrive"));
            Assert.Equal(Visibility.Visible, vacioLocal.Visibility);
            Assert.Equal(Visibility.Visible, vacioDrive.Visibility);
            Assert.Equal("No hay respaldos para mostrar.", Assert.IsType<TextBlock>(vacioLocal.Child).Text);
            Assert.Equal("No hay copias remotas para mostrar.", Assert.IsType<TextBlock>(vacioDrive.Child).Text);

            var grids = Descendientes(vista).OfType<DataGrid>().ToArray();
            Assert.Equal(2, grids.Length);
            Assert.All(grids, grid => Assert.Equal(Visibility.Collapsed, grid.Visibility));

            var botones = Descendientes(vista).OfType<Button>().ToArray();
            Assert.False(Assert.Single(botones, b => (string?)b.Content == "Verificar y restaurar").IsEnabled);
            Assert.False(Assert.Single(botones, b => (string?)b.Content == "Imprimir hoja de recuperación").IsEnabled);
            Assert.False(Assert.Single(botones, b => (string?)b.Content == "Consultar copias remotas").IsEnabled);
            Assert.False(Assert.Single(botones, b => (string?)b.Content == "Descargar seleccionada").IsEnabled);
            Assert.Equal(Visibility.Collapsed,
                Assert.Single(botones, b => (string?)b.Content == "Crear copia ahora").Visibility);
        });
    }

    [Theory]
    [InlineData(1366, 768)]
    [InlineData(1200, 750)]
    [InlineData(980, 620)]
    public void MercadoPago_mantiene_dos_columnas_sin_desborde_horizontal(int ancho, int alto)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new ConfiguracionMercadoPagoView();
            vista.Measure(new Size(ancho, alto));
            vista.Arrange(new Rect(0, 0, ancho, alto));
            vista.UpdateLayout();

            var scroll = Assert.IsType<ScrollViewer>(vista.Content);
            var layout = Assert.IsType<Grid>(vista.FindName("LayoutConfiguracion"));
            Assert.Equal(2, layout.ColumnDefinitions.Count);
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.True(scroll.ViewportWidth > 0);
            Assert.True(layout.ActualWidth <= scroll.ViewportWidth + 1,
                $"El panel mide {layout.ActualWidth} px con viewport {scroll.ViewportWidth} px a {ancho}x{alto}.");
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
