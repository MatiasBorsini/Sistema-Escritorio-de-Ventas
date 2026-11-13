using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Tests.Ventas;
using LocalInfantil.Wpf.ViewModels;
using LocalInfantil.Wpf.Views;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Regresiones del cierre de la operación de cambio/devolución. El defecto
/// original: <c>Continuar_Click</c> abría el comprobante con la modal ya
/// cerrada como Owner, lo que lanzaba InvalidOperationException dentro de un
/// evento async void y terminaba el proceso.
/// </summary>
[Collection("WpfCambiosCierre")]
public sealed class WpfCambiosCierreTests
{
    private static readonly object Puerta = new();
    private static bool _appLista;

    [Fact]
    public async Task Confirmar_devolucion_cierra_la_modal_y_mantiene_el_shell_abierto()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new NuevaLineaVenta(v1, 1, precio)],
            [new NuevoPago(MediosPago.Efectivo, precio, precio)],
            $"op-cierre-{Guid.NewGuid():N}", e.Sesion);
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        var ticketAbierto = false;
        var historialAbierto = false;
        var shellVisible = false;
        Exception? fallo = null;

        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var vista = new CambiosView { DataContext = vm };
            var shell = new Window { Width = 1280, Height = 720, ShowInTaskbar = false, Content = vista };
            shell.Show();
            try
            {
                var estado = 0;
                var limite = Stopwatch.StartNew();
                Bombear(() =>
                {
                    if (limite.Elapsed > TimeSpan.FromSeconds(30))
                        throw new TimeoutException($"El flujo no terminó; estado={estado}.");
                    switch (estado)
                    {
                        case 0:
                            vm.Venta = new FilaVentaCambio(resumen);
                            estado = 1;
                            return false;
                        case 1:
                            if (vm.CargandoDetalle || vm.Entradas.Count == 0) return false;
                            vm.Entradas[0].Cantidad = 1;
                            vm.Entradas[0].Apta = true;
                            vm.MedioReintegro = MediosPago.Efectivo;
                            vm.Motivo = "Devolución de prueba";
                            estado = 2;
                            return false;
                        case 2:
                            var continuar = Botones(vista).FirstOrDefault(b => Equals(b.Content, "Continuar"));
                            if (continuar is null || !continuar.IsEnabled) return false;
                            estado = 3;
                            continuar.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                            return false;
                        case 3:
                            var modal = shell.OwnedWindows.OfType<ConfirmarCambioWindow>().FirstOrDefault();
                            if (modal is null) return false;
                            var confirmar = (Button)modal.FindName("ConfirmarButton")!;
                            if (!confirmar.IsEnabled) return false;
                            estado = 4;
                            confirmar.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                            return false;
                        case 4:
                            var ticket = shell.OwnedWindows.OfType<TicketCambioWindow>().FirstOrDefault();
                            if (ticket is null) return false;
                            ticketAbierto = true;
                            ticket.Close();
                            vm.SeccionActiva = "historial";
                            estado = 5;
                            return false;
                        case 5:
                            var historial = (DataGrid)vista.FindName("HistorialDataGrid");
                            historial.UpdateLayout();
                            if (historial.Items.Count == 0 || historial.ItemContainerGenerator.ContainerFromIndex(0) is not DataGridRow fila) return false;
                            historial.SelectedIndex = 0;
                            estado = 6;
                            historial.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                                Environment.TickCount, System.Windows.Input.MouseButton.Left)
                                { RoutedEvent = Control.MouseDoubleClickEvent, Source = fila });
                            return false;
                        case 6:
                            var ticketHistorial = shell.OwnedWindows.OfType<TicketCambioWindow>().FirstOrDefault();
                            if (ticketHistorial is null) return false;
                            historialAbierto = true;
                            ticketHistorial.Close();
                            return true;
                        default:
                            return false;
                    }
                });
                shellVisible = shell.IsVisible;
            }
            catch (Exception ex) { fallo = ex; }
            finally { shell.Close(); }
        });

        Assert.Null(fallo);
        Assert.True(ticketAbierto, "El comprobante del cambio no se abrió.");
        Assert.True(historialAbierto, "El doble clic en el historial no abrió el comprobante.");
        Assert.True(shellVisible, "La ventana principal se cerró al confirmar la devolución.");
        Assert.False(string.IsNullOrWhiteSpace(vm.CambioIdReciente));
        Assert.Null(vm.Error);

        var registrados = await e.Servicios.ConsultasCambios.ListarAsync(resumen.Id, e.Sesion);
        Assert.Single(registrados);

        var caja = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        var reintegro = Assert.Single(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.CambioReintegro);
        Assert.Equal(-precio, reintegro.ImporteMinor);
        Assert.Equal(MediosCaja.Efectivo, reintegro.Medio);

        var inventario = (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1), e.Sesion))!;
        Assert.Equal(10, inventario.Disponible);

        // Un segundo intento sobre la operación ya registrada no duplica el reintegro.
        var repetido = await vm.ConfirmarAsync();
        Assert.Null(repetido);
        Assert.Single(await e.Servicios.ConsultasCambios.ListarAsync(resumen.Id, e.Sesion));
        Assert.Single((await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!.Movimientos,
            m => m.Tipo == TiposMovimientoCaja.CambioReintegro);

        // Persistencia: la operación quedó en la base y sobrevive a un reinicio.
        await using (var conexion = new Microsoft.Data.Sqlite.SqliteConnection(
                         new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                         {
                             DataSource = e.RutaBaseDatos,
                             Pooling = false,
                         }.ToString()))
        {
            await conexion.OpenAsync();
            await using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT COUNT(*) FROM cambio WHERE venta_id = $venta;";
            comando.Parameters.AddWithValue("$venta", resumen.Id);
            Assert.Equal(1L, (long)(await comando.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Cerrar_la_modal_con_X_no_registra_ni_descarta_el_borrador()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new NuevaLineaVenta(v1, 1, precio)],
            [new NuevoPago(MediosPago.Efectivo, precio, precio)],
            $"op-cerrar-x-{Guid.NewGuid():N}", e.Sesion);
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        vm.Entradas[0].Cantidad = 1;
        vm.Entradas[0].Apta = true;
        vm.MedioReintegro = MediosPago.Efectivo;
        vm.Motivo = "Borrador conservado";

        var shellVisible = false;
        bool? resultado = null;
        Exception? fallo = null;
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var shell = new Window { Width = 900, Height = 600, ShowInTaskbar = false };
            shell.Show();
            try
            {
                var modal = new ConfirmarCambioWindow(vm) { Owner = shell };
                modal.ConfirmarOperacionAsync = _ => Task.FromResult(true);
                modal.Loaded += (_, _) => modal.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                    new Action(() => modal.Close()));
                resultado = modal.ShowDialog();
                shellVisible = shell.IsVisible;
            }
            catch (Exception ex) { fallo = ex; }
            finally { shell.Close(); }
        });

        Assert.Null(fallo);
        Assert.False(resultado);
        Assert.True(shellVisible);
        Assert.Null(vm.CambioIdReciente);
        Assert.Equal(1, vm.Entradas[0].Cantidad);
        Assert.Equal("Borrador conservado", vm.Motivo);
        Assert.Empty(await e.Servicios.ConsultasCambios.ListarAsync(resumen.Id, e.Sesion));
    }

    [Fact]
    public async Task Volver_a_la_preparacion_no_registra_y_conserva_el_borrador()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new NuevaLineaVenta(v1, 1, precio)],
            [new NuevoPago(MediosPago.Efectivo, precio, precio)],
            $"op-volver-{Guid.NewGuid():N}", e.Sesion);
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        vm.Entradas[0].Cantidad = 1;
        vm.Entradas[0].Apta = true;
        vm.MedioReintegro = MediosPago.Efectivo;
        vm.Motivo = "Volver sin confirmar";

        var shellVisible = false;
        bool? resultado = null;
        Exception? fallo = null;
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var shell = new Window { Width = 900, Height = 600, ShowInTaskbar = false };
            shell.Show();
            try
            {
                var modal = new ConfirmarCambioWindow(vm) { Owner = shell };
                modal.ConfirmarOperacionAsync = _ => Task.FromResult(true);
                modal.Loaded += (_, _) => modal.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                    new Action(() =>
                    {
                        var volver = Botones(modal).First(b => Equals(b.Content, "Volver a la preparación"));
                        ClickComoUsuario(volver);
                    }));
                resultado = modal.ShowDialog();
                shellVisible = shell.IsVisible;
            }
            catch (Exception ex) { fallo = ex; }
            finally { shell.Close(); }
        });

        Assert.Null(fallo);
        Assert.Equal(false, resultado);
        Assert.True(shellVisible);
        Assert.Null(vm.CambioIdReciente);
        Assert.Equal(1, vm.Entradas[0].Cantidad);
        Assert.Equal("Volver sin confirmar", vm.Motivo);
        Assert.Empty(await e.Servicios.ConsultasCambios.ListarAsync(resumen.Id, e.Sesion));
    }

    [Fact]
    public void El_comprobante_se_abre_contra_la_ventana_contenedora_y_no_contra_la_modal_cerrada()
    {
        var ruta = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "CambiosView.xaml.cs");
        var codigo = File.ReadAllText(ruta);
        Assert.Contains("MostrarComprobanteSiNuevoAsync(vm, owner)", codigo);
        Assert.DoesNotContain("MostrarComprobanteSiNuevoAsync(vm, confirmacion)", codigo);
    }

    [Fact]
    public void Selector_de_formato_actualiza_la_misma_preview_sin_modificar_el_comprobante()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var datos = ComprobantesCambioTests.Datos(-10000, false);
            var ventana = new TicketCambioWindow(datos);
            try
            {
                var selector = (ComboBox)ventana.FindName("FormatoSelector");
                var preview = (DocumentViewer)ventana.FindName("VistaPrevia");
                var a4 = Assert.IsType<System.Windows.Documents.FixedDocument>(preview.Document);
                Assert.Equal(LocalInfantil.Wpf.Printing.DocumentoCambioRenderer.AnchoA4, a4.Pages[0].Child.Width, 3);
                selector.SelectedIndex = 1;
                var ticket = Assert.IsType<System.Windows.Documents.FixedDocument>(preview.Document);
                Assert.NotSame(a4, ticket);
                Assert.Equal(LocalInfantil.Wpf.Printing.DocumentoCambioRenderer.AnchoTicket, ticket.Pages[0].Child.Width, 3);
                selector.SelectedIndex = 0;
                Assert.Equal(LocalInfantil.Wpf.Printing.DocumentoCambioRenderer.AnchoA4,
                    ((System.Windows.Documents.FixedDocument)preview.Document).Pages[0].Child.Width, 3);
                Assert.Same(datos, ventana.DataContext);
            }
            finally { ventana.Close(); }
        });
    }

    /// <summary>Bombea el dispatcher encadenando BeginInvoke hasta que el paso devuelva true.</summary>
    private static void Bombear(Func<bool> paso, int segundosLimite = 30)
    {
        var frame = new DispatcherFrame();
        var limite = Stopwatch.StartNew();
        var terminado = false;
        void Siguiente()
        {
            if (terminado) return;
            // Se reprograma antes de ejecutar: el paso puede bloquearse en un
            // ShowDialog y el bucle debe seguir avanzando dentro de su bombeo.
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Siguiente));
            try
            {
                if (paso()) { terminado = true; frame.Continue = false; return; }
                if (limite.Elapsed > TimeSpan.FromSeconds(segundosLimite))
                {
                    terminado = true;
                    frame.Continue = false;
                    throw new TimeoutException($"El flujo no terminó en {segundosLimite} s.");
                }
            }
            catch
            {
                terminado = true;
                frame.Continue = false;
                throw;
            }
        }
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Siguiente));
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<Button> Botones(DependencyObject raiz)
    {
        if (raiz is Button boton) yield return boton;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
            foreach (var hijo in Botones(VisualTreeHelper.GetChild(raiz, i)))
                yield return hijo;
    }

    private static void ClickComoUsuario(Button boton)
        => typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(boton, null);

    private static async Task EsperarCargaAsync(CambiosViewModel vm)
    {
        for (var i = 0; i < 300 && (vm.CargandoDetalle || vm.Entradas.Count == 0); i++)
            await Task.Delay(10);
        Assert.False(vm.CargandoDetalle);
        Assert.NotEmpty(vm.Entradas);
    }

    private static void EjecutarEnSta(Action cuerpo)
    {
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try { cuerpo(); }
            catch (Exception ex) { error = ex; }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        if (!hilo.Join(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("La interacción WPF no terminó en 2 minutos.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
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
            var ensamblado = typeof(LocalInfantil.Wpf.Infra.ServiciosComerciales).Assembly;
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
