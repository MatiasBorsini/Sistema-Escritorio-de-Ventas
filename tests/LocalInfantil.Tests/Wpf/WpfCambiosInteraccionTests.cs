using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Data;
using System.Windows.Threading;
using System.Xml;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Tests.Ventas;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;
using LocalInfantil.Wpf.Views;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Interacciones WPF reales sobre <see cref="BuscadorAsistido"/> conectado a un
/// <see cref="CambiosViewModel"/>: clic, flechas+Enter y Escape. Verifican que
/// seleccionar no agrega y que el clic queda seleccionado sin depender del
/// estado del Popup ni del resaltado.
/// </summary>
public sealed class WpfCambiosInteraccionTests
{
    private static readonly object Puerta = new();
    private static bool _appLista;

    [Fact]
    public async Task Un_clic_selecciona_sin_agregar_y_el_agregado_reinicia_el_buscador()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "clic");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        var codigo = await e.CodigoDeAsync(v1);
        await vm.MotorArticulos.TextoCambiadoAsync(codigo);

        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var control = new BuscadorAsistido { Motor = vm.MotorArticulos, SeleccionCommand = vm.SeleccionarArticuloCommand, EnfocarAlLimpiarMotor = true, DataContext = vm };
            BindingOperations.SetBinding(control, BuscadorAsistido.TextoSeleccionadoProperty, new Binding(nameof(CambiosViewModel.TextoArticuloPresentacion)));
            var ventana = new Window { Content = control, Width = 600, Height = 280, ShowInTaskbar = false };
            try
            {
                ventana.Show();
                ventana.Activate();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var lista = Assert.IsType<ListBox>(control.FindName("Lista"));
                lista.UpdateLayout();
                var fila = Assert.IsType<ListBoxItem>(lista.ItemContainerGenerator.ContainerFromIndex(0));
                var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Mouse.PreviewMouseDownEvent,
                };

                fila.RaiseEvent(args);

                Assert.True(args.Handled);
                Assert.False(vm.MotorArticulos.Abierto);
                Assert.Equal(vm.TextoArticuloPresentacion, Assert.IsType<TextBox>(control.FindName("Caja")).Text);
                Assert.Equal(codigo, vm.MotorArticulos.Texto);
                Assert.Equal(1, vm.CantidadSalida);
                Assert.Empty(vm.Salidas);

                vm.CantidadSalida = 2;
                vm.AgregarSalidaCommand.Execute(null);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Input);

                Assert.Single(vm.Salidas);
                Assert.Equal(2, vm.Salidas[0].Cantidad);
                Assert.Null(vm.ArticuloSeleccionado);
                Assert.Equal(1, vm.CantidadSalida);
                Assert.Equal(string.Empty, Assert.IsType<TextBox>(control.FindName("Caja")).Text);
                Assert.True(Assert.IsType<TextBox>(control.FindName("Caja")).IsKeyboardFocused);
            }
            finally
            {
                control.Motor = null;
                ventana.Close();
            }
        });

        Assert.Null(vm.ArticuloSeleccionado);
        Assert.Single(vm.Salidas);

        await vm.MotorArticulos.TextoCambiadoAsync(codigo);
        vm.SeleccionarArticuloCommand.Execute(Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == v1));
        EjecutarEnSta(() =>
        {
            var control = new BuscadorAsistido { Motor = vm.MotorArticulos, SeleccionCommand = vm.SeleccionarArticuloCommand, DataContext = vm };
            BindingOperations.SetBinding(control, BuscadorAsistido.TextoSeleccionadoProperty, new Binding(nameof(CambiosViewModel.TextoArticuloPresentacion)));
            var ventana = new Window { Content = control, Width = 600, Height = 280, ShowInTaskbar = false };
            try
            {
                ventana.Show();
                var caja = Assert.IsType<TextBox>(control.FindName("Caja"));
                Assert.Equal(vm.TextoArticuloPresentacion, caja.Text);
                caja.Text = "texto manual";
                Assert.Null(vm.ArticuloSeleccionado);
                Assert.Equal("texto manual", caja.Text);
            }
            finally { ventana.Close(); }
        });
    }

    [Fact]
    public async Task Flechas_y_enter_seleccionan_la_sugerencia_resaltada()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "enter");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        await vm.MotorArticulos.TextoCambiadoAsync(await e.CodigoDeAsync(v1));
        Assert.Equal(-1, vm.MotorArticulos.IndiceActivo);

        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var control = new BuscadorAsistido { Motor = vm.MotorArticulos, SeleccionCommand = vm.SeleccionarArticuloCommand, DataContext = vm };
            BindingOperations.SetBinding(control, BuscadorAsistido.TextoSeleccionadoProperty, new Binding(nameof(CambiosViewModel.TextoArticuloPresentacion)));
            var ventana = new Window { Content = control, Width = 600, Height = 280, ShowInTaskbar = false };
            try
            {
                ventana.Show();
                ventana.Activate();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var caja = Assert.IsType<TextBox>(control.FindName("Caja"));

                Presionar(caja, Key.Down);
                Assert.Equal(0, vm.MotorArticulos.IndiceActivo);
                Presionar(caja, Key.Enter);

                Assert.False(vm.MotorArticulos.Abierto);
                Assert.Equal(vm.TextoArticuloPresentacion, caja.Text);
            }
            finally
            {
                ventana.Close();
            }
        });

        Assert.Equal(v1, vm.ArticuloSeleccionado!.VarianteId);
        Assert.Empty(vm.Salidas);
    }

    [Fact]
    public async Task Escape_cierra_las_sugerencias_sin_seleccionar()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "escape");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        await vm.MotorArticulos.TextoCambiadoAsync(await e.CodigoDeAsync(v1));

        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var control = new BuscadorAsistido { Motor = vm.MotorArticulos, SeleccionCommand = vm.SeleccionarArticuloCommand };
            var ventana = new Window { Content = control, Width = 600, Height = 280, ShowInTaskbar = false };
            try
            {
                ventana.Show();
                ventana.Activate();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var caja = Assert.IsType<TextBox>(control.FindName("Caja"));

                Presionar(caja, Key.Escape);

                Assert.False(vm.MotorArticulos.Abierto);
            }
            finally
            {
                ventana.Close();
            }
        });

        Assert.Null(vm.ArticuloSeleccionado);
        Assert.False(vm.TieneArticuloSeleccionado);
        Assert.Empty(vm.Salidas);
    }

    private static void Presionar(TextBox caja, Key tecla)
    {
        var fuente = PresentationSource.FromVisual(caja);
        Assert.NotNull(fuente);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, fuente!, Environment.TickCount, tecla)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        caja.RaiseEvent(args);
    }

    private static async Task VenderAsync(VentasTests.EntornoVentas e, string variante, long precio, string sufijo)
    {
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new NuevaLineaVenta(variante, 1, precio)],
            [new NuevoPago(MediosPago.Efectivo, precio, precio)],
            $"op-cambios-int-{sufijo}-{Guid.NewGuid():N}", e.Sesion);
    }

    private static async Task EsperarCargaAsync(CambiosViewModel vm)
    {
        for (var i = 0; i < 300 && (vm.CargandoDetalle || vm.Entradas.Count == 0); i++)
            await Task.Delay(10);
        Assert.False(vm.CargandoDetalle);
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
            throw new TimeoutException("La interacción WPF no terminó en 2 minutos.");
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void AsegurarAplicacion()
    {
        lock (Puerta)
        {
            if (System.Windows.Application.Current is not null) _appLista = true;
            if (_appLista) return;
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

    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LocalInfantil.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
